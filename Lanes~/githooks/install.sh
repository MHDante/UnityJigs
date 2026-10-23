#!/bin/sh
#
# Installs the git hooks beside this script into ONE clone (README "Setting up a repository", step 6):
# into its shared git folder's hooks/ (`git rev-parse --git-common-dir`), so the clone's lane worktrees run
# them too. Once per clone, like the merge driver; re-run after pulling a newer UnityJigs to refresh them.
# Existing files that differ are kept as <name>.bak.<date>.
#
#   sh install.sh <repo>            install / refresh
#   sh install.sh --check <repo>    report whether the clone matches, change nothing (exit 1 if not)
#
# <repo> defaults to the current directory. A core.hooksPath (global or in the clone) makes git skip
# .git/hooks entirely; the installer won't change that config for you, it prints the command to unset it.

check=0
[ "$1" = "--check" ] && { check=1; shift; }
repo=${1:-.}

src="$(cd "$(dirname "$0")" && pwd)"
hooks="pre-commit pre-push post-checkout post-commit post-merge"

common=$(git -C "$repo" rev-parse --path-format=absolute --git-common-dir 2>/dev/null) || {
    echo "not a git repository: $repo" >&2
    exit 1
}
dest="$common/hooks"

ok=1
for h in $hooks; do
    if ! cmp -s "$src/$h" "$dest/$h" 2>/dev/null; then
        echo "differs or missing: $dest/$h"
        ok=0
    fi
done

# Any core.hooksPath (system / global / local / worktree) bypasses $dest.
bypass=$(git -C "$repo" config --show-scope --get-all core.hooksPath 2>/dev/null)
if [ -n "$bypass" ]; then
    ok=0
    echo "core.hooksPath is set, so git ignores $dest:" >&2
    printf '%s\n' "$bypass" | while read -r scope value; do
        case "$scope" in
            global) fix="git config --global --unset core.hooksPath" ;;
            system) fix="git config --system --unset core.hooksPath   (admin shell)" ;;
            worktree) fix="git -C \"$repo\" config --worktree --unset core.hooksPath" ;;
            *) fix="git -C \"$repo\" config --local --unset core.hooksPath" ;;
        esac
        echo "  $scope: $value    unset with: $fix" >&2
    done
fi

if [ "$check" -eq 1 ]; then
    [ "$ok" -eq 1 ] && echo "git hooks: installed and current ($dest)"
    [ "$ok" -eq 1 ]
    exit $?
fi

mkdir -p "$dest"
stamp=$(date +%Y%m%d-%H%M%S)
for h in $hooks; do
    if [ -f "$dest/$h" ] && ! cmp -s "$src/$h" "$dest/$h"; then
        mv "$dest/$h" "$dest/$h.bak.$stamp"
        echo "kept the old $h as $h.bak.$stamp"
    fi
    cp "$src/$h" "$dest/$h"
    chmod +x "$dest/$h"
done
echo "git hooks installed: $dest ($hooks)"
if [ -n "$bypass" ]; then
    echo "WARNING: they won't run until core.hooksPath is unset (see above)." >&2
    exit 1
fi
exit 0
