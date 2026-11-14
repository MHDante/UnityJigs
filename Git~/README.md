# Git and worktrees for a Unity project

This folder (`Git~`, ignored by Unity, but copied with the package) holds what a Unity repository needs
from git: a merge driver for Unity YAML and a set of hooks. It also records what it takes to run several
Unity editors on one repository, one per git worktree. The editor side of that is compiled into UnityJigs
(`UnityJigs.Lanes.Lane`, `UnityJigs.Editor.Lanes`).

Who works in which worktree, and when, is not this package's business.

**Where this folder is in a project:** read `<UnityProject>/Packages/packages-lock.json` →
`com.mhdante.unity-jigs`. A git package lives in
`<UnityProject>/Library/PackageCache/com.mhdante.unity-jigs@<hash>/` (Unity must have opened the project
once); a local one (`"version": "file:…"`) at that path.

## The merge driver

`sh <this folder>/unity-yaml-merge.sh --install`, once per clone (worktrees share it; re-run after pulling
a newer UnityJigs), and `.gitattributes` lines sending Unity YAML to it (`*.prefab merge=unityyamlmerge
eol=lf`, likewise `.unity`, `.asset`, `.meta`, `.mat`, `.anim`, `.controller` …). Merges then go field by
field, and a real conflict gets ordinary git markers on just the conflicting lines.

**What a tool regenerates** needs no hand merge, and often can't have one (a mesh's vertex data is one line
of hex). Two attributes tell the driver: `unity-derived` on files a tool writes whole (`**/Baked/*.asset
unity-derived`), and `unity-derived-fields=A,B` on files where only those fields are regenerated (`*.unity
unity-derived-fields=BakeHash`). A conflict in a `unity-derived` file, or one whose conflicting lines are
all named fields, keeps ours there (every other change still merged) and exits clean, with a line saying
what it kept. That is right only when the tool notices: the project's own check must find the result out of
date (a hash over the inputs, say) and regenerate it. A conflict on any other field still gets markers.

A merge with no conflict can still be wrong: lists of entries present on both sides can come out
duplicated. Diff the result against each parent before trusting it.

## The git hooks

`sh <this folder>/githooks/install.sh <clone>` copies the hooks in `githooks/` into the clone's shared git
folder (`git rev-parse --git-common-dir`/hooks), so its worktrees run them too (differing old files kept as
`.bak.<date>`); `install.sh --check <clone>` reports whether a clone matches without changing anything.
Once per clone; re-run after pulling a newer UnityJigs. A `core.hooksPath` (global or local) makes git skip
that folder: the installer warns and prints the `--unset` command rather than changing the config itself.

- `pre-commit` — **never committing a machine-specific package path:** refuses a staged
  `Packages/manifest.json` / `packages-lock.json` with a `file:` ref that points OUTSIDE the repository —
  absolute (`file:D:/Clones/SomePackage`) or climbing out (`file:../../../SomePackage`, what
  PackageSwitcher's Switch To Local writes): a teammate without that clone breaks on pull. Packages inside
  the repo pass (`file:Reflections`, or `file:../../Pkg` from a project in a subfolder).
  It also holds an optional **report gate**, a no-op unless the repo commits a `.commit-gate` file in its
  root with `enforce=on`: the file names a report a tool keeps up to date (`report=`, one line per checked
  file: git blob id, failures, path, tab separated), which staged files it judges (`files=*.prefab
  *.unity`) and what to tell the user about failures (`fix=`). A staged file with failures, or one the
  report hasn't seen in that exact version, blocks the commit; the hook parses nothing but the report.
  Merge commits are not checked.
- `pre-push`, `post-checkout`, `post-commit`, `post-merge` — Git LFS's own stubs, so the set is complete
  for an LFS repo (the same files `git lfs install --local` writes).

Bypass on purpose with `--no-verify`. These are guards against accidents: whether anyone commits or pushes
is the user's call, not a hook's.

## Several editors on one repository

Unity won't open one folder twice, so parallel work needs one checkout per editor: the main checkout, and
git worktrees beside it, each with its own `Library`. UnityJigs calls such a checkout a **lane**. A worktree
in a folder named `<repo>-<letter>` (`my-game-b`) is lane B; the main checkout is no lane.

### Making one

- **Worktree:** `git worktree add` (seconds: the LFS store is shared, nothing is downloaded). A branch can
  be checked out in only one worktree.
- **Seeding the Library:** copy the main checkout's `Library` instead of paying a cold import, leaving out
  the files that name its editor (`*-lock`, `Library/Pipeline/.unity-pipeline-port`, `EditorInstance.json`,
  `ProtocolInstance.json`). With the port file copied, `unity status` lists the new editor as an
  unreachable second copy of the main one until it has started.
- **Opening:** `unity open` (it picks the project's editor version). Each editor takes the next free port;
  `unity status` lists them all with their project paths.
- **Dialogs on the first start wait for a click.** A fresh worktree's editor can open dialogs that block
  its main thread: the editor is listed as ready but answers nothing. Seen: Spine's skeleton import
  warning, FMOD's offer to repair its macOS `Info.plist`s, FMOD's first-time setup window.
- **First-start repair.** A copied Library comes with stale script records for some assemblies (seen:
  FMODUnity, ParadoxNotion, AssetUsageDetector.Editor, Febucci.Attributes): every script of theirs resolves
  to no class, so their assets load broken, and a plugin that recreates a "missing" settings asset saves
  defaults over it (FMOD's `FMODStudioSettings.asset`: no banks, no project path). The editor repairs
  itself (`UnityJigs.Editor.Lanes.LaneFirstStart`: once per Library in a lane, it force-reimports every
  assembly whose scripts all lost their class). Afterwards restore every tracked file that first start
  changed in the brand-new worktree (`git status`, then `git restore`) and reimport them.

### Working in one

- **Every Unity CLI call** takes `--project-path <worktree>\<UnityProject>`. An unpinned call drives
  whichever editor owns the folder the shell is in, without complaint. (From a folder that isn't a Unity
  project, with several editors running, an unpinned call fails with `AMBIGUOUS_EDITOR`.) The port file can
  be stale: before a destructive call, check which process answers.
- **Shared between editors:** EditorPrefs (unless the code keys them with `Lane.Key`),
  `persistentDataPath` (unless the save code uses `Lane.SaveFolder`), and the Unity editor log
  (`%LOCALAPPDATA%\Unity\Editor\Editor.log`, written by every editor). A fresh editor's first eval can hit
  the 5 s main-thread limit while it warms up: pass a longer timeout or retry.
- **IDE checks:** a post-edit hook that asks an IDE to analyse the edited file only covers the solution
  the IDE has open. Files in another worktree go unchecked: verify with that worktree's editor (`recompile`,
  `recompile_status`, `console --level error`).
- **Diff every asset you save.** An editor can hold stale data (seen: blank fields in memory while the
  disk had text) and write it back on save.
- **After the branch moves under an open editor** (a fast-forward, a merge, a stash pop), recompile and
  check the result. `recompile` can answer `up_to_date` while the loaded assemblies are a day old (seen: a
  player build then failed on a stale serialized field).
  `CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache)` through an
  eval forces it.
- **Builds:** one fixed output path per worktree and purpose (e.g. `<UnityProject>/Builds/Perf/B/`),
  overwritten each time. Every new exe path makes Windows Firewall prompt (a development player listens),
  and the launch waits on a click. A one-time inbound-block rule per fixed path removes the prompt.
- **Check `git status` after every build.** Plugin build steps switch native-plugin import flags in memory
  for the build and back afterwards; a FAILED build skips the switch-back, and the next editor quit writes
  the switched flags into the plugin's `.meta` (seen: FMOD's Linux library disabled for Linux). Revert
  those; never commit them.
- **Closing an editor:** gracefully (`unity close`, else a normal window close); kill only that PID, and
  only if it won't close. Never kill by image name. `unity close` can report `Closed false` while the
  editor does exit: check `unity status`. Unity Hub's background `unity serve` process is not an editor and
  can hold files open (logs from batch builds): leave it alone.

### Merging, and other machines

- **Fast-forwards are always safe.** After moving a branch under an open editor, recompile (above).
- **A checkout on a local `file:` ref of a shared package** flips back to the git ref (or the lock is
  bumped to the package's new commit) before its work merges; the `pre-commit` hook refuses an outside
  `file:` path.
- **Work travels between machines through git**, not patch files: patch files handed between machines
  collided on shared files every round.
- **Remote editors** open with `Unity.exe -projectPath …` and the CLI attaches as usual. Install build
  modules (Linux, IL2CPP) BEFORE starting an editor: one installed while it runs makes its builds fail
  oddly until it restarts.
- **Line endings:** files written on another Windows machine may arrive CRLF; edit them through scripts,
  and mark binary assets (e.g. `*.graphicsstate binary`) in `.gitattributes`.
- **A machine move:** worktrees, Libraries, local settings and unpushed commits don't travel. Re-run the
  merge driver's `--install` and `githooks/install.sh <clone>` (`--check <clone>` first) in each clone.

## In the editor (compiled into UnityJigs)

- **`UnityJigs.Lanes.Lane`** (runtime, editor-only logic; a build is never a lane): `Id` ("B", or null in
  the main checkout), `Index` (1 to 9), `RepoRoot`, **`SaveFolder`** (`persistentDataPath`, and its
  `lane-b` folder in lane B) and **`Key(key)`** (an EditorPrefs / PlayerPrefs key made per lane). A game's
  save code should read and write under `Lane.SaveFolder`, and keep editor-side save state under
  `Lane.Key(...)`, so two editors can play at the same time.
- **The lane badge** (Unity 6000.3+): a button in the lane's colour on the main toolbar (B red, C green,
  D blue, E cyan, F magenta, G yellow, H brown, I gray, J black) reading "Lane B · <branch>"; the same at
  the front of the window title (any Unity version). Click opens the lane's folder; right-click copies its
  `--project-path`. It is shown and coloured through Unity internals; if an update breaks them, tick it in
  the toolbar's ⋮ menu and its icon still carries the colour.
- **`LaneFirstStart`** (above).
