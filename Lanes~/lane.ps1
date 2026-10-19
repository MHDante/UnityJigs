<#
.SYNOPSIS
    Lanes (README.md beside this script): one checkout and one Unity editor each, so several agents can
    work in parallel. Lane A is the main checkout; lanes B to J are git worktrees at
    <WorktreeRoot>\<repo>-<id> (default C:\Worktrees). A told agent claims a lane before changing files or
    driving Unity; the claim lives in the lane's memory file (lane_<id>.md in the repo's Claude memory
    folder), so it travels with the transcripts.

.EXAMPLE
    pwsh lane.ps1 status -Session <id>          # the lane table
    pwsh lane.ps1 claim -Session <id> -Task "…"  # the first free lane (A first, unless the repo says otherwise)
    pwsh lane.ps1 claim b -Session <id>         # lane B, if it's free
    pwsh lane.ps1 release -Session <id>         # give back the lane this session holds
    pwsh lane.ps1 new b                         # branch lane-b from the main checkout's HEAD
    pwsh lane.ps1 new b -Branch fix-ui          # a new branch fix-ui
    pwsh lane.ps1 revive b -Branch fix-ui       # an existing branch (local or origin/), e.g. on another machine
    pwsh lane.ps1 open b                        # (re)open its editor
    pwsh lane.ps1 first-start b                 # a fresh lane's repair and restore, when `new` timed out
    pwsh lane.ps1 retire b                      # close its editor, remove the worktree (refuses if it has changes)
    pwsh lane.ps1 session-start                 # a SessionStart hook's output (reads the hook JSON on stdin)

.NOTES
    The repository is the one holding the current folder (or -Repo); its main checkout is found through
    git, so the script works from any worktree. The Unity project is the folder holding
    ProjectSettings/ProjectVersion.txt at the root or one level down (or -UnityProject). An optional
    .lanes.json at the repository root overrides: { "unityProject": "MyGameUnity", "laneAFirst": true,
    "worktreeRoot": "C:/Worktrees" }. A repository with no Unity project gets lanes without editors.
#>
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('status', 'claim', 'release', 'session-start', 'new', 'revive', 'open', 'first-start', 'list', 'retire')]
    [string]$Command,
    [Parameter(Position = 1)][ValidatePattern('^[a-jA-J]$')][string]$Id,
    [string]$Session,
    [string]$Name,
    [string]$Task,
    [string]$Branch,
    [string]$Base = 'HEAD',
    [string]$Repo,
    [string]$UnityProject,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8 # the hook's output goes into a session's context
# PowerShell names ignore case: functions take a lane as $lane, so nothing shadows the -Id argument.
$Requested = if ($Id) { $Id.ToLower() } else { $null }

# ── The repository ────────────────────────────────────────────────────────────────────────────────

$from = if ($Repo) { $Repo } else { (Get-Location).Path }
$commonDir = git -C $from rev-parse --path-format=absolute --git-common-dir 2>$null
if (-not $commonDir) { throw "Not in a git repository: $from" }
$repoRoot = (Split-Path ($commonDir -replace '/', '\') -Parent)
$repoName = Split-Path $repoRoot -Leaf

$config = @{}
$configPath = Join-Path $repoRoot '.lanes.json'
if (Test-Path $configPath) { $config = Get-Content $configPath -Raw | ConvertFrom-Json -AsHashtable }

$worktreeRoot = if ($config.worktreeRoot) { $config.worktreeRoot -replace '/', '\' } else { 'C:\Worktrees' }
$laneAFirst = if ($config.ContainsKey('laneAFirst')) { [bool]$config.laneAFirst } else { $true }

# The Unity project, relative to a checkout's root ('' when the root is the project, $null when none).
$unityProject = if ($UnityProject) { $UnityProject }
elseif ($config.unityProject) { $config.unityProject }
elseif (Test-Path (Join-Path $repoRoot 'ProjectSettings\ProjectVersion.txt')) { '' }
else {
    $found = @(Get-ChildItem $repoRoot -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'ProjectSettings\ProjectVersion.txt') })
    if ($found.Count -gt 1) { throw "Several Unity projects in $repoRoot ($($found.Name -join ', ')): pass -UnityProject or set unityProject in .lanes.json" }
    if ($found.Count -eq 1) { $found[0].Name } else { $null }
}
$hasUnity = $null -ne $unityProject

$unity = Join-Path $env:LOCALAPPDATA 'Unity\bin\unity.exe'
$laneIds = if ($laneAFirst) { 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j' } else { 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j' }
$allIds = 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j'

# Claude Code keys a repository's memory folder by its path, non-alphanumerics as '-'.
$memoryDir = Join-Path $env:USERPROFILE ".claude\projects\$($repoRoot -replace '[^A-Za-z0-9]', '-')\memory"

function Lane-Root([string]$id) { if ($id -eq 'a') { $repoRoot } else { Join-Path $worktreeRoot "$repoName-$id" } }
function Lane-Project([string]$id) { if ($unityProject) { Join-Path (Lane-Root $id) $unityProject } else { Lane-Root $id } }
function Lane-Exists([string]$id) { Test-Path (Lane-Root $id) }
function Need-Worktree([string]$id) { if (-not $id -or $id -eq 'a') { throw "Which lane? (b to j; lane A is the main checkout)" } }

function Unity-Json([string[]]$cliArgs) {
    if (-not (Test-Path $unity)) { return $null }
    $out = & $unity @cliArgs --json --no-banner 2>$null
    try { return ($out | Out-String | ConvertFrom-Json) } catch { return $null }
}

function Lane-Eval([string]$id, [string]$body, [int]$timeoutMs = 120000) {
    $file = Join-Path ([IO.Path]::GetTempPath()) "lane-eval-$([guid]::NewGuid().ToString('N')).cs" # eval_file wants .cs
    Set-Content -Path $file -Value $body -Encoding utf8
    try {
        $r = Unity-Json @('command', 'eval_file', $file, "$timeoutMs", '--timeout', "$([int]($timeoutMs / 1000) + 10)", '--project-path', (Lane-Project $id))
        if ($r -and $r.data) { return $r.data.result.result }
        return $null
    } finally { Remove-Item $file -ErrorAction SilentlyContinue }
}

# ── Claims ────────────────────────────────────────────────────────────────────────────────────────
# One line of the lane's memory file: "- **Held by:** nobody", or
# "- **Held by:** session `<id>` (<name>) since <time> · <task>".

$heldByPattern = '^- \*\*Held by:\*\* (.*)$'

function Lane-File([string]$id) { Join-Path $memoryDir "lane_$id.md" }

function Ensure-LaneFile([string]$id) {
    $file = Lane-File $id
    if (Test-Path $file) { return }
    New-Item -ItemType Directory -Force $memoryDir | Out-Null
    $upper = $id.ToUpper()
    $path = "``$(Lane-Project $id)``" + $(if ($id -eq 'a') { ' (the main checkout)' } else { '' })
    $pin = if ($hasUnity) { " (pin: ``--project-path`` this path)" } else { '' }
    $text = @"
---
name: lane-$id
description: Lane $upper ($(Lane-Root $id)): who holds it, branch, task, uncommitted work, push state; written only by its holder
metadata:
  type: project
---

- **Held by:** nobody
- **Path:** $path$pin
- **Branch:** see git.
- **Task:** none.
- **Uncommitted work:** none.
- **Last pushed:** nothing.

The lane workflow: UnityJigs Lanes~/README.md. Update this file whenever the holder, branch, task or push state changes.
"@
    [IO.File]::WriteAllText($file, ($text -replace "`r`n", "`n"))
    $index = Join-Path $memoryDir 'MEMORY.md'
    $line = "- [Lane $upper](lane_$id.md) — who holds lane $upper, its branch, task and uncommitted work (only its holder writes it)"
    if (-not (Test-Path $index) -or -not (Select-String -Path $index -SimpleMatch "(lane_$id.md)" -Quiet)) {
        Add-Content -Path $index -Value $line -Encoding utf8
    }
}

function Get-Holder([string]$id) {
    $file = Lane-File $id
    if (-not (Test-Path $file)) { return $null }
    foreach ($line in [IO.File]::ReadAllLines($file)) {
        if ($line -match $heldByPattern) {
            if ($Matches[1] -match '^session `([^`]+)`') { return @{ Session = $Matches[1]; Text = ($line -replace $heldByPattern, '$1') } }
            return $null
        }
    }
    return $null
}

function Set-Holder([string]$id, [string]$value) {
    Ensure-LaneFile $id
    $file = Lane-File $id
    $lines = [Collections.Generic.List[string]][IO.File]::ReadAllLines($file)
    $at = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match $heldByPattern) { $at = $i; break } }
    if ($at -ge 0) { $lines[$at] = "- **Held by:** $value" }
    else {
        # First bullet of the body, else the end.
        $insert = $lines.Count
        for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].StartsWith('- **')) { $insert = $i; break } }
        $lines.Insert($insert, "- **Held by:** $value")
    }
    [IO.File]::WriteAllText($file, ($lines -join "`n") + "`n")
}

# Two agents claiming at once must not get the same lane: claims run one at a time on this machine.
function With-ClaimLock([scriptblock]$body) {
    $lockPath = Join-Path ([IO.Path]::GetTempPath()) "lanes-$repoName.lock"
    $lock = $null
    for ($i = 0; $i -lt 100 -and -not $lock; $i++) {
        try { $lock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') } catch { Start-Sleep -Milliseconds 100 }
    }
    if (-not $lock) { throw "Couldn't take the lane lock ($lockPath)" }
    try { & $body } finally { $lock.Dispose() }
}

function Held-By([string]$session) {
    foreach ($lane in $allIds) { $h = Get-Holder $lane; if ($h -and $h.Session -eq $session) { return $lane } }
    return $null
}

function Editor-States {
    $states = @{}
    if (-not $hasUnity) { return $states }
    foreach ($i in (Unity-Json @('status')).data.instances) { $states[$i.project] = $i.state }
    return $states
}

function Show-Status([string]$session) {
    $editors = Editor-States
    $rows = foreach ($lane in $allIds) {
        if (-not (Lane-Exists $lane)) { continue }
        $root = Lane-Root $lane
        $h = Get-Holder $lane
        $holder = if (-not $h) { 'free' } elseif ($session -and $h.Session -eq $session) { "YOU ($($h.Text))" } else { $h.Text }
        if ($lane -eq 'a' -and -not $laneAFirst -and -not $h) { $holder = 'not a lane here' }
        $row = [ordered]@{
            Lane = $lane.ToUpper(); Branch = (git -C $root branch --show-current 2>$null)
            Changes = @(git -C $root status --porcelain 2>$null).Count
        }
        if ($hasUnity) { $e = $editors[(Lane-Project $lane)]; $row.Editor = if ($e) { $e } else { 'closed' } }
        $row.'Held by' = $holder
        [pscustomobject]$row
    }
    $rows | Format-Table -AutoSize -Wrap | Out-String -Width 200
}

function Claim-Lane {
    if (-not $Session) { throw "claim needs -Session <this session's id>" }
    With-ClaimLock {
        $mine = Held-By $Session
        if ($mine) { Write-Host "You already hold lane $($mine.ToUpper()): $(Lane-Root $mine)"; return }
        $candidates = if ($Requested) { @($Requested) } else { $laneIds | Where-Object { Lane-Exists $_ } }
        foreach ($lane in $candidates) {
            if (-not (Lane-Exists $lane)) { throw "Lane $($lane.ToUpper()) doesn't exist (lane.ps1 new $lane)" }
            $h = Get-Holder $lane
            if ($h) {
                if ($Requested) { Write-Host "Lane $($lane.ToUpper()) is held by $($h.Text)"; exit 2 }
                continue
            }
            $who = if ($Name) { "session ``$Session`` ($Name)" } else { "session ``$Session``" }
            $since = Get-Date -Format 'yyyy-MM-dd HH:mm'
            Set-Holder $lane ("$who since $since" + $(if ($Task) { " · $Task" } else { '' }))
            Write-Host "Claimed lane $($lane.ToUpper()): $(Lane-Root $lane)"
            if ($hasUnity) { Write-Host "Unity project: $(Lane-Project $lane) (pin --project-path to it)" }
            Write-Host "Tell the user which lane you're in, so they can /rename this session's tab to it."
            $changes = @(git -C (Lane-Root $lane) status --porcelain).Count
            if ($changes) { Write-Host "It holds $changes uncommitted change(s) left by its last holder: read lane_$lane.md before touching them." }
            return
        }
        Write-Host "No free lane. Ask the user which holder to relinquish, or whether to create a lane:"
        Write-Host (Show-Status $Session)
        exit 3
    }
}

function Release-Lane {
    With-ClaimLock {
        $lane = if ($Requested) { $Requested } elseif ($Session) { Held-By $Session } else { $null }
        if (-not $lane) { throw "release: which lane? (this session holds none, or pass the lane)" }
        $h = Get-Holder $lane
        if (-not $h) { Write-Host "Lane $($lane.ToUpper()) is already free"; return }
        if ($h.Session -ne $Session -and -not $Force) {
            throw "Lane $($lane.ToUpper()) is held by $($h.Text). Only its holder releases it (or -Force, once the user says the holder can't)."
        }
        Set-Holder $lane 'nobody'
        Write-Host "Released lane $($lane.ToUpper())"
    }
}

function Session-Start {
    # A SessionStart hook: its input is JSON on stdin; plain stdout goes into the session's context.
    # Silent in a repository with no lane worktrees.
    $worktrees = @($allIds | Where-Object { $_ -ne 'a' -and (Lane-Exists $_) })
    if ($worktrees.Count -eq 0) { return }
    $hookInput = $null
    if ([Console]::IsInputRedirected) { try { $hookInput = [Console]::In.ReadToEnd() | ConvertFrom-Json } catch { } }
    $sid = $hookInput.session_id
    $mine = if ($sid) { Held-By $sid } else { $null }
    Write-Output "## Lanes (UnityJigs Lanes~/README.md)"
    Write-Output "This session's id: $sid"
    if ($mine) {
        Write-Output "You hold lane $($mine.ToUpper()) ($(Lane-Root $mine)): follow the lane rules (the README, or this repo's /lane skill)."
    } else {
        Write-Output "Use a lane only when the user tells you to; otherwise work in the main checkout as usual."
        Write-Output "Told to: claim one before changing files or driving Unity (the README, or this repo's /lane skill)."
    }
    Write-Output (Show-Status $sid)
}

# ── Worktrees and editors ─────────────────────────────────────────────────────────────────────────

function Wait-Ready([string]$id, [int]$timeoutSec = 2700) {
    $project = Lane-Project $id
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    $unanswered = 0
    while ((Get-Date) -lt $deadline) {
        $s = Unity-Json @('status')
        $inst = $s.data.instances | Where-Object { $_.project -eq $project -and $_.state -eq 'ready' }
        if ($inst) {
            if ((Lane-Eval $id 'return "ok";' 30000) -eq 'ok') { return }
            # Listed as ready, but its main thread doesn't answer: a fresh lane's first start opens
            # dialogs (an import warning, a plugin offering a repair, a setup window) that wait for a click.
            if (++$unanswered -eq 2) {
                Write-Host "Lane $($id.ToUpper())'s editor isn't answering: it is probably waiting on dialogs. Click through them in its window (the README lists the usual ones); waiting."
            }
        }
        Start-Sleep -Seconds 5
    }
    throw "Lane $($id.ToUpper()): the editor wasn't ready after $timeoutSec s. Once it answers, run: lane.ps1 first-start $id"
}

function Seed-Library([string]$id) {
    $from = Join-Path (Lane-Project 'a') 'Library'
    $to = Join-Path (Lane-Project $id) 'Library'
    if (Test-Path $to) { Write-Host "Library already there: kept"; return }
    if (-not (Test-Path $from)) { Write-Host "Lane A has no Library: the editor will import from scratch"; return }
    Write-Host "Seeding the Library from lane A..."
    # The skipped files name lane A's editor (its port, eval token and process).
    robocopy $from $to /E /MT:16 /XF *-lock .unity-pipeline-port EditorInstance.json ProtocolInstance.json UnityJigs.LaneFirstStart /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }
}

function Copy-LocalSettings([string]$id) {
    # Sessions started inside the lane would read these; they pin the Unity CLI to the lane's editor.
    foreach ($rel in @('.claude\settings.local.json') + $(if ($unityProject) { @("$unityProject\.claude\settings.local.json") } else { @() })) {
        $src = Join-Path $repoRoot $rel
        if (-not (Test-Path $src)) { continue }
        $settings = Get-Content $src -Raw | ConvertFrom-Json -AsHashtable
        if ($hasUnity) {
            if (-not $settings.ContainsKey('env')) { $settings.env = @{} }
            $settings.env.UNITY_PROJECT_PATH = Lane-Project $id
        }
        $dst = Join-Path (Lane-Root $id) $rel
        New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
        $settings | ConvertTo-Json -Depth 20 | Set-Content $dst -Encoding utf8
    }
}

function Open-Lane([string]$id) {
    if (-not $hasUnity) { return }
    & $unity open (Lane-Project $id) --no-banner 2>&1 | Out-Null
    Write-Host "Opening lane $($id.ToUpper())'s editor..."
}

function First-Start([string]$id) {
    if (-not $hasUnity) { return }
    Wait-Ready $id
    # UnityJigs' LaneFirstStart repairs the copied Library's stale script records (it also runs by
    # itself; calling it here waits for it).
    $fixed = Lane-Eval $id 'return UnityJigs.Editor.Lanes.LaneFirstStart.Repair();'
    if ($null -eq $fixed) { Write-Warning "Couldn't run UnityJigs' LaneFirstStart.Repair: is the project on a UnityJigs with Lanes?" }
    elseif ($fixed -ne 'none') {
        Write-Host "Reimported stale scripts: $fixed"
        Start-Sleep -Seconds 5
        Wait-Ready $id
    }
    # The worktree is brand new, so every change to a tracked file is the editor's first start (e.g. a
    # plugin saving default settings over its own while its class was missing): put them back, then
    # reimport what was under the project's Assets so the editor reads the restored files.
    $root = Lane-Root $id
    # git status, not git diff: a file can read as modified with no content diff (line endings, stat).
    $changed = @(git -C $root status --porcelain --untracked-files=no | ForEach-Object { $_.Substring(3).Trim('"') })
    if ($changed.Count) {
        git -C $root checkout -- .
        $prefix = if ($unityProject) { "$($unityProject -replace '\\', '/')/" } else { '' }
        $assets = @($changed | Where-Object { $_.StartsWith("${prefix}Assets/") -and -not $_.EndsWith('.meta') } | ForEach-Object { $_.Substring($prefix.Length) })
        $reimport = ($assets | ForEach-Object { "UnityEditor.AssetDatabase.ImportAsset(`"$_`", UnityEditor.ImportAssetOptions.ForceUpdate);" }) -join "`n"
        if ($reimport) { Lane-Eval $id "$reimport`nreturn `"ok`";" | Out-Null }
        Write-Host "Restored $($changed.Count) file(s) the editor changed on its first start:`n  $($changed -join "`n  ")"
    }
    $dirty = git -C $root status --porcelain
    if ($dirty) { Write-Warning "Lane $($id.ToUpper()) has changes after its first start:`n$($dirty -join "`n")" }
}

function Add-Lane([string]$id, [bool]$existing) {
    $root = Lane-Root $id
    if (Test-Path $root) { throw "$root already exists" }
    if (-not $Branch) { $script:Branch = "lane-$id" }
    New-Item -ItemType Directory -Force (Split-Path $root) | Out-Null
    if ($existing) {
        git -C $repoRoot fetch --quiet origin 2>$null
        $local = git -C $repoRoot branch --list $Branch
        if ($local) { git -C $repoRoot worktree add $root $Branch }
        else { git -C $repoRoot worktree add --track -b $Branch $root "origin/$Branch" }
    } else {
        git -C $repoRoot worktree add -b $Branch $root $Base
    }
    if ($LASTEXITCODE) { throw "git worktree add failed" }
    if ($hasUnity) { Seed-Library $id }
    Copy-LocalSettings $id
    Ensure-LaneFile $id
    Open-Lane $id
    First-Start $id
    $pin = if ($hasUnity) { " Pin the Unity CLI with --project-path $(Lane-Project $id)" } else { '' }
    Write-Host "Lane $($id.ToUpper()) ready: $root on $Branch.$pin"
}

function Retire-Lane([string]$id) {
    $root = Lane-Root $id
    if (-not (Test-Path $root)) { throw "$root doesn't exist" }
    $h = Get-Holder $id
    if ($h -and -not $Force) { throw "Lane $($id.ToUpper()) is held by $($h.Text): its holder releases it first (or -Force)" }
    $dirty = git -C $root status --porcelain
    if ($dirty -and -not $Force) { throw "Lane $($id.ToUpper()) has changes; commit them, or pass -Force to discard them:`n$($dirty -join "`n")" }
    if ($hasUnity) {
        $project = Lane-Project $id
        $editor = (Unity-Json @('status')).data.instances | Where-Object { $_.project -eq $project -and $_.state -eq 'ready' }
        if ($editor) {
            $closing = Lane-Eval $id 'UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0); return "closing";' 30000
            Write-Host "Closing lane $($id.ToUpper())'s editor ($closing)..."
            Wait-Process -Id $editor.pid -Timeout 120 -ErrorAction SilentlyContinue
            # git can't remove files the editor still holds, and would leave a half-deleted lane.
            if (Get-Process -Id $editor.pid -ErrorAction SilentlyContinue) { throw "Lane $($id.ToUpper())'s editor (pid $($editor.pid)) is still running: close it, then retire again" }
        }
    }
    $branch = git -C $root branch --show-current
    if ($Force) { git -C $repoRoot worktree remove --force $root } else { git -C $repoRoot worktree remove $root }
    if ($LASTEXITCODE) { throw "git worktree remove failed" }
    if (Test-Path (Lane-File $id)) { Set-Holder $id 'nobody' }
    Write-Host "Lane $($id.ToUpper()) retired. Its branch $branch is kept; so is lane_$id.md."
}

switch ($Command) {
    'status' { Show-Status $Session }
    'list' { git -C $repoRoot worktree list; Show-Status $Session }
    'claim' { Claim-Lane }
    'release' { Release-Lane }
    'session-start' { Session-Start }
    'new' { Need-Worktree $Requested; Add-Lane $Requested $false }
    'revive' {
        Need-Worktree $Requested
        if (-not $Branch) { throw "revive needs -Branch (the lane's memory file names it)" }
        Add-Lane $Requested $true
    }
    'open' { if (-not $Requested) { throw "Which lane?" }; Open-Lane $Requested }
    # Only for a lane that has never been worked in: it puts back every tracked file the editor changed.
    'first-start' { Need-Worktree $Requested; First-Start $Requested }
    'retire' { Need-Worktree $Requested; Retire-Lane $Requested }
}
