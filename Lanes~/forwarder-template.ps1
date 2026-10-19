# Runs UnityJigs' lane script (Lanes~/lane.ps1; see Lanes~/README.md) in the version this project has
# resolved, so skills, hooks and docs in this repository have one stable path. A template: copy it into the
# repository (e.g. tools/lane.ps1) and set $unityProjectFolder.
$unityProjectFolder = 'MyGameUnity' # the Unity project, relative to the repository root ('.' when it is the root)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path # adjust if this file isn't one level down
$unityProject = Join-Path $repoRoot $unityProjectFolder
$packages = Join-Path $unityProject 'Packages'
$lock = Get-Content (Join-Path $packages 'packages-lock.json') -Raw | ConvertFrom-Json
$jigs = $lock.dependencies.'com.mhdante.unity-jigs'
if (-not $jigs) { throw "$unityProject doesn't use UnityJigs" }
$jigsRoot = if ($jigs.version -like 'file:*') {
    [IO.Path]::GetFullPath([IO.Path]::Combine($packages, $jigs.version.Substring(5))) # a local package
} else {
    (Get-Item (Join-Path $unityProject 'Library\PackageCache\com.mhdante.unity-jigs@*') -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
}
if (-not $jigsRoot) { throw "UnityJigs isn't in $unityProject\Library\PackageCache yet: open the project in Unity once" }

& (Join-Path $jigsRoot 'Lanes~\lane.ps1') @args -Repo $repoRoot -UnityProject $unityProjectFolder
exit $LASTEXITCODE
