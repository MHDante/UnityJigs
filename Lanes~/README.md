# Lanes

Several Claude Code agents working on one Unity project at the same time, each with its own checkout and
its own Unity editor. Unity won't open one folder twice, and most Unity work ends with compiling, running
validators and checking things in the editor, so parallel work needs one editor per folder: a **lane**.

- **Lane A** is the main checkout (`C:\Repos\<repo>`).
- **Lanes B to J** are git worktrees at `C:\Worktrees\<repo>-<id>` (`my-game-b`), each with its own
  `Library` and editor. They are long-lived: a lane keeps its Library between tasks; switch branches inside
  it instead of making a new one.
- **Letters are per machine.** Each machine has its own lane A, its own lane B …; what is shared between
  machines (the memory files, so the claims, and anything an agent writes) names a lane with its machine:
  **lane B-desk**, `lane_b-desk.md`. The machine name is `-Machine`, else `$env:LANES_MACHINE`, else the
  computer's entry in `.lanes.json`'s `machines`, else the computer name in lower case. The table lists this
  machine's lanes only.
- **Agents use lanes only when the user tells them to.** Otherwise an agent works in the main checkout as
  usual, with no claim.

This folder (`Lanes~`, ignored by Unity, but copied with the package) holds the workflow: this README,
`lane.ps1` and `unity-yaml-merge.sh`. The editor side is compiled into UnityJigs (`UnityJigs.Lanes.Lane`,
`UnityJigs.Editor.Lanes`). Everything is inert in a project that has no lane worktrees.

**Where this folder is in a project:** read `<UnityProject>/Packages/packages-lock.json` →
`com.mhdante.unity-jigs`. A git package lives in `<UnityProject>/Library/PackageCache/com.mhdante.unity-jigs@<hash>/`
(Unity must have opened the project once); a local one (`"version": "file:…"`) at that path. Below,
`lane.ps1` means `pwsh -NoProfile -File <that folder>/Lanes~/lane.ps1`, run from anywhere inside the
repository (it finds the main checkout through git; or pass `-Repo <path>`).

## Working in a lane (when told to)

1. **Your session id:** a skill gets it as `${CLAUDE_SESSION_ID}`; a SessionStart hook gets it in its JSON
   (`session_id`). A repo set up for lanes prints it at session start or in its `/lane` skill.
2. **Claim:** `lane.ps1 claim -Session <id> -Name <your name, the first line of ListAgents> -Task "<one line>"`
   takes the first free lane, **lane A first** (unless the repo says otherwise). `lane.ps1 claim b -Session …`
   asks for one lane. Hold at most one.
   - **No free lane** (exit 3): don't take one. Show the user the table and ask which lane to free, or
     whether to create one (`lane.ps1 new <next letter>`: a few minutes and the Library's size on disk).
   - A held lane you asked for by name exits 2 and names its holder.
3. **Read the lane's note**: with a registry (below) `lane.ps1 note <id>` (a claim prints it too); without one, its
   memory file (`lane_<id>-<machine>.md` in the repo's Claude memory folder, indexed in
   MEMORY.md: the folder Claude Code reads — `autoMemoryDirectory` when local or user settings set it, else
   `<CLAUDE_CONFIG_DIR or ~/.claude>/projects/<repo key>/memory`; `lane.ps1` resolves it the same way). It may hold uncommitted work a previous holder left on purpose: a handoff. Then
   `git -C <lane root> status -sb`, and check the editor (`lane.ps1 status`): closed → `lane.ps1 open <id>`
   and wait for `unity status` to say `ready`.
4. **Tell the user which lane you're in**, in one line ("Working in **lane B**"), so they can `/rename`
   your session's tab after it. Never rename anything yourself.

### While you hold it

- **Every Unity CLI call** takes `--project-path <lane root>\<UnityProject>`. Sessions start in lane A's
  folder and the shell returns there after every command, so an unpinned call drives **lane A's** editor
  without complaint. (From a folder that isn't a Unity project, with several editors running, an unpinned
  call fails with `AMBIGUOUS_EDITOR`.)
- **Files:** absolute paths under the lane root. A relative path, or a path under the main checkout, edits
  lane A.
- **Git:** `git -C <lane root> …`. Don't commit or push unless the user says so. **Lane branches
  (`lane-b` …) are never pushed**: their work reaches the remote by being merged into a real branch.
- **Docs:** the lane's branch may have changed CLAUDE.md or the docs: read the lane's copies when it matters.
- **IDE checks:** a post-edit hook that asks an IDE (Rider) to analyse the edited file only covers the
  solution the IDE has open (usually lane A's). Lane files go unchecked: verify with the lane's editor
  (`recompile`, `recompile_status`, `console_status`, `console --level error`).
- **Shared with other lanes:** memory, EditorPrefs (unless the code keys them per lane with
  `Lane.Key`), `persistentDataPath` (unless the save code uses `Lane.SaveFolder`), the Unity editor log
  (`%LOCALAPPDATA%\Unity\Editor\Editor.log`, written by every editor). A fresh editor's first eval can hit
  the 5 s main-thread limit while it warms up: pass a longer positional timeout or retry. `eval_file`
  only takes `.cs` files.
- **The note:** keep the lane's note current (`lane.ps1 note <id> -File x.md`, or the memory file) (task, branch, uncommitted work, last push) and update it
  whenever one changes. Never edit another lane's file.
- **Diff every asset you save from a lane.** An editor can hold stale data (seen: blank fields in memory
  while the disk had text) and write it back on save.
- **After the lane's branch moves under an open editor** (a fast-forward, a merge, a stash pop), recompile and
  check the result. `recompile` can answer `up_to_date` while the loaded assemblies are a day old (seen: a
  player build then failed on a stale serialized field). `CompilationPipeline.RequestScriptCompilation(
  RequestScriptCompilationOptions.CleanBuildCache)` through an eval forces it.
- **Builds:** one fixed output path per lane and purpose (e.g. `<UnityProject>/Builds/Perf/B/`), overwritten
  each time. Every new exe path makes Windows Firewall prompt (a development player listens), and the launch
  waits on a click. A one-time inbound-block rule per fixed path removes the prompt.
- **Check `git status` after every build.** Plugin build steps switch native-plugin import flags in memory
  for the build and back afterwards; a FAILED build skips the switch-back, and the next editor quit writes
  the switched flags into the plugin's `.meta` (seen: FMOD's Linux library disabled for Linux). Revert those;
  never commit them.

### Releasing

When your work in the lane is done, or the user asks: bring the lane's note up to date (what you did, where
it stands, what the uncommitted work is and whether the next holder should keep it), then
`lane.ps1 release -Session <id>`. Uncommitted work may stay: it passes to the next holder (sometimes on
purpose, to hand a working directory to an agent with different context).

### Pausing

When the user pauses work (they may need the machine, or its editors, for something else): reach a clean point
you can resume from (don't start a new step; let a run already in flight finish), stage what is done, write
exactly where you stopped and what comes next into the lane's note, stop play mode, then **close your own
editor**: its PID is in `unity status` for your project path. Close it gracefully (`unity close`, else a
normal window close); kill only that PID, and only if it won't close. Never kill by image name. Keep the claim
unless told to release it. (`unity close` can report `Closed false` while the editor does exit: check
`unity status`.) Unity Hub's background `unity serve` process is not an editor and can hold files open
(logs from batch builds): leave it alone; the file frees after a Hub restart.

### Relinquishing someone else's lane

Only the holder releases a lane, so its context can wrap up and its memory gets written. When the user
asks you to free lane X:

1. Find the holder in `lane.ps1 status` (session id and name). `ListAgents`: if a session with that name is
   running, `SendMessage` it: "The user asks you to give up lane X: update lane_x-<machine>.md, release it with
   lane.ps1, and reply with a summary." Its answer arrives as a notification when it's done; wait for it.
   A lane agent you started yourself (below, "A driver with lane agents") is messaged by its agent id.
2. Not running: ask the user first, then resume its transcript headless, from the folder its session
   started in (so the session is found):
   `claude -p --resume <session id> "The user asks you to give up lane X: update lane_x-<machine>.md, release it with lane.ps1, and summarise."`
3. The lane is free once its "Held by" line says nobody. Claim it.
4. The holder can't be resumed (transcript gone): only on the user's word,
   `lane.ps1 release x -Session <your id> -Force`, and write what you can into its memory file.

Claims never expire: a closed session still holds its lane until it is asked to give it up.

## A driver with lane agents

The pattern that worked best (three perf sessions, two Windows machines and a Steam Deck): one session, the
**driver**, holds no lane and starts one **lane agent** per lane with the agent tool (a background copy of
itself; `SendMessage <agent id>` sends it follow-ups, its answers arrive as notifications). Each agent claims
its lane with its OWN session id.

- **The driver orchestrates; it never implements.** It writes briefs, reviews reports, keeps every lane and
  every test device busy, and takes decisions to the user one at a time. A driver that starts editing files in
  the main checkout leaves staged work there that blocks the next round (lanes branch from committed HEAD).
- **Fixed roles by file ownership** (e.g. A = rendering / shaders, B = CPU / the shared package, C = a target
  device) instead of re-assigning every round. Two lanes in one Shader Graph or one tooling file means a hard
  merge: when it can't be avoided, say so in both briefs.
- **One owner per shared device.** One lane queues every run on a test device (a handheld at 15 W shares
  power between CPU and GPU: a second job ruins the measurement); the others hand it a build and the exact
  launch line through the driver. Likewise one lane at a time edits a shared package repo.
- **Reuse warm agents.** An agent that just reported knows the lane, its editor's quirks and the code: send it
  the next task instead of starting a cold one. The driver keeps a queue in its notes and sends it the moment
  a lane reports.
- **Every brief carries the standing rules**, pasted in full (a missing one cost a whole round): never commit
  (stage and report; the user commits), fix the shared package in the package (local ref, leave it
  uncommitted), fixed build paths, a GPU timing lock on a shared machine, only kill PIDs you started, and
  "a rendering change isn't done until it is captured on the target graphics API" (a D3D11 editor proof missed
  a Vulkan-only bug).
- **Lanes branch from committed HEAD.** Before launching a round, have the user commit whatever the lanes must
  start from.

## Merging lane work, and lanes on other machines

- **Fast-forwards are always safe** (lane ← main, main ← lane). The user decides who may make merge commits;
  commits of *work* stay the user's. After moving a branch under an open editor, recompile (above).
- **When two lanes touched one file**, merge after the user has committed the lane that sits on the main
  checkout; merging into a branch that has staged work means untangling it around that work.
- **A lane on a local `file:` ref of a shared package** flips back to the git ref (or the lock is bumped to the
  package's new commit) before its work merges. Check the manifest before every lane merge; the `pre-commit`
  hook (below) refuses an outside `file:` path.
- **Abandoned work is stashed, not deleted**, with a message saying what it is
  (`git stash push -u -m "<task>-wip: …"`), and noted in the lane's memory file: the next agent resumes it.
- **Lanes on another machine** (driven over a remote shell): their work travels **through git**, not patch
  files — stage in that machine's lane, the user commits there, merge into that machine's main branch, push,
  pull here. Patch files handed between machines collided on shared files every round.
- **Remote editors** open with `Unity.exe -projectPath …` and the CLI attaches as usual. Install build
  modules (Linux, IL2CPP) BEFORE starting an editor: one installed while it runs makes its builds fail oddly
  until it restarts. A module install may need the user's UAC click on that machine.
- **Line endings:** files written on another Windows machine may arrive CRLF; edit them through scripts, and
  mark binary assets (e.g. `*.graphicsstate binary`) in `.gitattributes`.

## Lane lifecycle (`lane.ps1`)

| Command | Does |
|---|---|
| `status [-Session s]` / `list` | the lane table (branch, uncommitted changes, editor, holder); `list` adds the worktrees |
| `claim [id] -Session s [-Name n] [-Task t]` / `release [id] -Session s [-Force]` | take / give back a lane |
| `new <id> [-Branch b] [-Base ref]` | worktree on a new branch (default `lane-<id>` from `HEAD`), seeded Library, editor opened and repaired |
| `revive <id> -Branch b` | the same on an existing branch (local, else `origin/b`): another machine, or a lane retired earlier |
| `open <id>` | (re)open its editor |
| `first-start <id>` | a fresh lane's repair and restore (below), when `new` / `revive` timed out or the lane was opened by hand; only for a lane nobody has worked in |
| `retire <id> [-Force]` | close its editor, remove the worktree (refused while it has changes or a holder, unless `-Force`); the branch and the memory file stay |
| `session-start` | a SessionStart hook's output: this session's id, the rule and the table (silent without lane worktrees) |

What `new` / `revive` do, and why:

- **Worktree:** `git worktree add` (seconds: the LFS store is shared, nothing is downloaded). A branch can
  be checked out in only one worktree.
- **Seeding the Library:** a copy of lane A's `Library` instead of a cold import, leaving out the files
  that name lane A's editor (`*-lock`, `Library/Pipeline/.unity-pipeline-port`, `EditorInstance.json`,
  `ProtocolInstance.json`). With the port file copied, `unity status` lists the new editor as an unreachable
  second lane A (lane A's port and eval token) until it has started.
- **Opening:** `unity open` (it picks the project's editor version). Each editor takes the next free port;
  `unity status` lists them all with their project paths.
- **Dialogs on the first start wait for a click.** A fresh lane's editor can open dialogs that block its
  main thread: the editor is listed as ready but answers nothing. Seen: Spine's skeleton import warning,
  FMOD's offer to repair its macOS `Info.plist`s, FMOD's first-time setup window. `new` says so once and
  keeps waiting (45 min) for someone to click through them. (Unity's `-automated` launch flag would answer
  them by itself, but for the editor's whole session, prompts included: not used.) After a timeout, run
  `lane.ps1 first-start <id>` once the editor answers.
- **First-start repair.** A copied Library comes with stale script records for some assemblies (seen:
  FMODUnity, ParadoxNotion, AssetUsageDetector.Editor, Febucci.Attributes): every script of theirs resolves
  to no class, so their assets load broken, and a plugin that recreates a "missing" settings asset saves
  defaults over it (FMOD's `FMODStudioSettings.asset`: no banks, no project path). The editor repairs itself
  (`UnityJigs.Editor.Lanes.LaneFirstStart`: once per Library in a lane, it force-reimports every assembly
  whose scripts all lost their class); then the script restores every tracked file the editor changed in
  the brand-new worktree and reimports them. A fresh lane opened by hand: run `lane.ps1 first-start <id>`
  once its editor answers.
- **Local settings:** `.claude/settings.local.json` (repo root and Unity project) is copied in with
  `UNITY_PROJECT_PATH` pinned, for a session started inside the lane.
- **The memory file** `lane_<id>-<machine>.md` is created if missing (not with a registry).

`lane.ps1` finds the Unity project (the folder with `ProjectSettings/ProjectVersion.txt`, at the root or
one level down). An optional `.lanes.json` at the repository root overrides it:

```json
{ "unityProject": "MyGameUnity", "laneAFirst": true, "worktreeRoot": "C:/Worktrees",
  "machines": { "MY-DESKTOP": "desk", "MY-LAPTOP": "laptop" } }
```

`laneAFirst: false` keeps agents out of the main checkout (lanes B to J only). A repository with no Unity
project gets lanes without editors. `machines` maps computer names (`$env:COMPUTERNAME`) to the short
names lanes carry.

## A shared registry (optional)

The memory folder only reaches another machine when it is synced, so it can't hold claims for lanes on two machines
(or two repos' agents sharing machines). A **registry** can: a small web service that keeps, per repository,
machine and lane, the holder (session, name, since, task) and a short **note** (branch, task, uncommitted work, a
handoff; 1500 characters). Name it in `.lanes.json`:

```json
{ "registry": "https://art.example.com/lanes/api", "registryRepo": "my-game" }
```

`registryRepo` defaults to the repo folder's name in lower case. With a registry, `claim` / `release` / the table /
`session-start` go through it (a claim is atomic there, across machines), `lane.ps1 note <id>` prints a lane's note
and `-File x.md` replaces it, and no memory file is written. The token comes from `$env:LANES_TOKEN`, else
`~/.lanes-token` (per user, outside every repo), sent as `Authorization: Bearer` (on a 401, `lane.ps1`
retries with an `x-lanes-token` header, for a registry that checks the token itself). The API (one JSON document per repo):
`GET lanes?repo=R`; `POST claim {repo, machine, lanes, exact, session, name, task}`, `release {repo, machine,
lane?, session, force}`, `note {repo, machine, lane, note}`.

**Devices.** A registry can also list **devices**: shared resources with no checkout and no editor (a test
handheld, a console devkit), one holder each, shared by every repo on the registry. `lane.ps1 claim -Device <name>`
/ `release -Device <name>` / `note -Device <name> [-File x.md]`; the table lists them under the lanes. A session may
hold a device and a lane at once. Claim the device before using it (a build, a perf run), release it when done,
and keep its note to what the next user needs (its state, what is installed, what is running). This is how "one
owner per shared device" (above) is held. The API: `GET devices`; `POST device-claim {device, session, name,
task}`, `device-release {device, session, force}`, `device-note {device, note}`.

**A machine that runs only N editors.** The registry can cap a machine (`machineLimits` in its config, e.g. a
small machine at 2): a claim there is refused while N lanes on that machine are held, counted across every repo on
the registry. `claim` then exits 3 and names the holders. Give such a machine at most N lanes per repo too.

## In the editor (compiled into UnityJigs)

- **`UnityJigs.Lanes.Lane`** (runtime, editor-only logic; a build is never a lane): `Id` ("B", or null in the
  main checkout), `Index` (1 to 9), `RepoRoot`, **`SaveFolder`** (`persistentDataPath`, and its `lane-b`
  folder in lane B) and **`Key(key)`** (an EditorPrefs / PlayerPrefs key made per lane). A game's save code
  should read and write under `Lane.SaveFolder`, and keep editor-side save state under `Lane.Key(...)`, so
  lanes can play at the same time.
- **The lane badge** (Unity 6000.3+): a button in the lane's colour on the main toolbar (B red, C green,
  D blue, E cyan, F magenta, G yellow, H brown, I gray, J black) reading "Lane B · <branch>"; the same at
  the front of the window title (any Unity version). Click opens the lane's folder; right-click copies its
  `--project-path`. It is shown and coloured through Unity internals; if an update breaks them, tick it in
  the toolbar's ⋮ menu and its icon still carries the colour. Drag it where you like: Unity keeps toolbar
  layouts in one file for every project.
- **`LaneFirstStart`** (above).

## Setting up a repository (an agent, by hand)

Nothing here is imposed on a repo: read it, then adapt. A typical setup:

1. **Pull the latest UnityJigs** in the project (its `packages-lock.json` hash), and open the project once
   so the package is in `Library/PackageCache`.
2. **Point the repo's agents at this README.** A line in the repo's CLAUDE.md, and/or a project skill
   (`.claude/skills/lane/SKILL.md`, see `skill-template.md` beside this file) that hands an agent its
   session id and the live table. A small forwarder script in the repo (say `tools/lane.ps1`) that finds the
   current UnityJigs copy and runs its `lane.ps1` gives skills, hooks and docs one stable path; see
   `forwarder-template.ps1`.
3. **Optionally, a SessionStart hook** in the user's local settings (`.claude/settings.local.json` of the
   folder sessions start in) that runs `lane.ps1 session-start`: every new session then sees its id and the
   table. It prints nothing while the repo has no lane worktrees.
4. **The merge driver:** `sh <this folder>/unity-yaml-merge.sh --install` (per clone; worktrees share it),
   and `.gitattributes` lines sending Unity YAML to it (`*.prefab merge=unityyamlmerge eol=lf`, likewise
   `.unity`, `.asset`, `.meta`, `.mat`, `.anim`, `.controller` …). Merges then go field by field, and a real
   conflict gets ordinary git markers on just the conflicting lines.
5. **Saves:** route the game's save folder through `Lane.SaveFolder` and editor save state through
   `Lane.Key`.
6. **The git hooks** (`githooks/` beside this file; per clone, like the merge driver in step 4):
   `sh <this folder>/githooks/install.sh <clone>` copies them into the clone's shared git folder
   (`git rev-parse --git-common-dir`/hooks), so its lane worktrees run them too (differing old files kept as
   `.bak.<date>`); `install.sh --check <clone>` reports whether a clone matches without changing anything.
   A `core.hooksPath` (global or local) makes git skip that folder: the installer warns and prints the
   `--unset` command rather than changing the config itself. They are:
   - `pre-push` — **never pushing lane branches:** refuses `refs/heads/lane-[a-j]` (local or remote ref),
     then runs `git lfs pre-push` with the same input.
   - `pre-commit` — **never committing a machine-specific package path:** refuses a staged
     `Packages/manifest.json` / `packages-lock.json` with a `file:` ref that points OUTSIDE the repository —
     absolute (`file:D:/Clones/SomePackage`) or climbing out (`file:../../../SomePackage`, what PackageSwitcher's
     Switch To Local writes): a teammate without that clone breaks on pull. Packages inside the repo pass
     (`file:Reflections`, or `file:../../Pkg` from a project in a subfolder).
   - `post-checkout` / `post-commit` / `post-merge` — Git LFS's own stubs, so the set is complete for an LFS
     repo (the same files `git lfs install --local` writes).
   Bypass on purpose with `--no-verify`. These are guards against accidents, not against agents: whether an
   agent commits or pushes is the user's call (trust), not a hook's.
7. **Machine moves:** memory (claims included) and transcripts travel; a lane's memory file names its
   machine, so lanes with the same letter on two machines never share a claim. Worktrees, Libraries, local settings
   and unpushed commits don't. Revive lanes from their memory files (`lane.ps1 revive <id> -Branch <b>`),
   re-run the merge driver's `--install` and `githooks/install.sh <clone>` (step 6; `--check <clone>` first
   to see whether it's needed) in each clone, and recreate any other local hook.
