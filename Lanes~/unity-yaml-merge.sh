#!/bin/sh
# Git merge driver for Unity YAML (.prefab, .unity, .asset, .meta …): the driver "unityyamlmerge" that
# .gitattributes names (e.g. "*.prefab merge=unityyamlmerge"). Install once per clone (the config is
# shared by its worktrees; re-run to update after pulling a newer UnityJigs):
#
#     sh <UnityJigs>/Lanes~/unity-yaml-merge.sh --install
#
# --install copies this script into the clone's git folder and points the driver at that copy, so the
# repository needs no file of its own. Without the copy (a fresh clone), merges fall back to git's text
# merge.
#
# UnityYAMLMerge merges field by field, so edits git's line merge would call a conflict (two fields on
# neighbouring lines) merge cleanly. Where both sides changed the same field it is run twice, keeping ours
# and keeping theirs, and git merge-file marks the lines where the two differ: the conflict reads like any
# other git conflict, with every non-conflicting change already merged. (UnityYAMLMerge's own conflict
# output would hide it: it writes one side's value, no markers.) A file it can't read, or no Unity install,
# falls back to a plain text merge. The tool comes from the project's editor version (its
# ProjectSettings/ProjectVersion.txt, at the repository root or one level down) under the default Hub
# path; set UNITY_YAML_MERGE to the exe to use another.

if [ "$1" = "--install" ]; then
    common=$(git rev-parse --path-format=absolute --git-common-dir) || exit 1
    cp "$0" "$common/unity-yaml-merge.sh"
    git config merge.unityyamlmerge.name "Unity SmartMerge (UnityJigs Lanes~/unity-yaml-merge.sh)"
    # Runs from the top of the working tree; a clone without the copy gets a plain text merge.
    git config merge.unityyamlmerge.driver \
        'f="$(git rev-parse --path-format=absolute --git-common-dir)/unity-yaml-merge.sh"; if [ -f "$f" ]; then sh "$f" %O %A %B %P; else git merge-file -L ours -L base -L theirs %A %O %B; fi'
    git config merge.unityyamlmerge.recursive binary
    echo "unityyamlmerge driver installed: $common/unity-yaml-merge.sh"
    if ! git check-attr merge -- x.prefab | grep -q unityyamlmerge; then
        echo "note: .gitattributes doesn't send Unity files to it yet; add lines like: *.prefab merge=unityyamlmerge eol=lf" >&2
    fi
    exit 0
fi

base=$1 ours=$2 theirs=$3 path=$4

text_merge() {
    git merge-file -L ours -L base -L theirs "$ours" "$base" "$theirs"
}

tool=$UNITY_YAML_MERGE
if [ -z "$tool" ]; then
    # Git runs merge drivers from the top of the working tree.
    version_file=$(ls ProjectSettings/ProjectVersion.txt */ProjectSettings/ProjectVersion.txt 2>/dev/null | head -n 1)
    version=$(sed -n 's/^m_EditorVersion: //p' "$version_file" 2>/dev/null | tr -d '\r ')
    case "$(uname -s)" in
        Darwin) tool="/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/Helpers/UnityYAMLMerge" ;;
        Linux) tool="$HOME/Unity/Hub/Editor/$version/Editor/Data/Tools/UnityYAMLMerge" ;;
        *) tool="C:/Program Files/Unity/Hub/Editor/$version/Editor/Data/Tools/UnityYAMLMerge.exe" ;;
    esac
fi
if [ ! -f "$tool" ]; then
    echo "unity-yaml-merge: $tool not found, text merge for $path" >&2
    text_merge
    exit $?
fi

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

# UnityYAMLMerge takes <base> <left = theirs> <right = ours> <dest>; -r keeps ours on a conflict.
"$tool" merge -h -r --force --fallback none -o "$tmp/report" "$base" "$theirs" "$ours" "$tmp/ours" > "$tmp/log" 2>&1
case $? in
    0)
        cp "$tmp/ours" "$ours"
        exit 0
        ;;
    2)
        "$tool" merge -h -l --force --fallback none "$base" "$theirs" "$ours" "$tmp/theirs" > /dev/null 2>&1
        cp "$tmp/ours" "$ours"
        git merge-file -L ours -L base -L theirs "$ours" "$base" "$tmp/theirs"
        conflicts=$?
        echo "unity-yaml-merge: conflicts in $path:" >&2
        sed -n '/^Left\|^Right/p' "$tmp/report" | sed 's/^/    /' >&2
        exit $conflicts
        ;;
    *)
        echo "unity-yaml-merge: UnityYAMLMerge could not merge $path, text merge" >&2
        sed 's/^/    /' "$tmp/log" >&2
        text_merge
        exit $?
        ;;
esac
