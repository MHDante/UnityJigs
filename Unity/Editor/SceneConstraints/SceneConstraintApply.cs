using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityJigs.Editor.SceneConstraints
{
    /// <summary>
    /// Writes a [SceneManaged] list so each entry is recorded at the DEEPEST prefab that contains both the
    /// owner and that entry — a relationship authored in a base prefab lives in the base (inherited by every
    /// instance), and only later-added members become higher-level/scene overrides. Entries are ordered
    /// deepest-prefab → scene so the inherited entries form a stable array prefix (minimal, deterministic
    /// prefab overrides — see SceneManagedAttribute for why the field is read-only).
    /// </summary>
    internal static class SceneConstraintApply
    {
        // Owner's prefab-asset chain, shallowest (outermost instance source) → deepest (base). Empty if the
        // owner isn't part of any prefab (a pure scene object).
        private static List<string> OwnerChainPaths(Component owner)
        {
            var paths = new List<string>();
            Object cur = owner;
            while (true)
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(cur);
                if (!src || src == cur) break;
                var p = AssetDatabase.GetAssetPath(src);
                if (string.IsNullOrEmpty(p)) break;
                paths.Add(p);
                cur = src;
            }
            return paths;
        }

        // Index into ownerChain of the deepest prefab that ALSO contains member (larger = deeper). -1 = the
        // two only co-exist in the scene.
        private static int DeepestCommonIndex(List<string> ownerChain, Object member)
        {
            var deepest = -1;
            for (var i = 0; i < ownerChain.Count; i++)
                if (PrefabUtility.GetCorrespondingObjectFromSourceAtPath(member, ownerChain[i]))
                    deepest = i; // keep the last (deepest) match
            return deepest;
        }

        // Stable sort: deepest-common-prefab first, scene-level last; ties keep the gathered (hierarchy) order.
        public static List<Object> Order<T>(Component owner, IReadOnlyList<T> members) where T : Object
        {
            var chain = OwnerChainPaths(owner);
            var indexed = new List<(Object member, int depth, int[] path)>(members.Count);
            for (var i = 0; i < members.Count; i++)
                if (members[i])
                    indexed.Add((members[i], DeepestCommonIndex(chain, members[i]), HierarchyKey(members[i])));
            indexed.Sort((a, b) =>
                a.depth != b.depth ? b.depth.CompareTo(a.depth) /* deeper first */ : CompareKey(a.path, b.path));
            var result = new List<Object>(indexed.Count);
            foreach (var e in indexed) result.Add(e.member);
            return result;
        }

        // Sibling-index path from the scene root — a stable, deterministic order independent of how members
        // were gathered (GetComponentsInChildren is hierarchy-ordered; FindObjectsByType is not), so re-runs
        // don't churn the serialized order.
        private static int[] HierarchyKey(Object o)
        {
            var t = o as Transform ?? (o as Component)?.transform ?? (o as GameObject)?.transform;
            if (!t) return System.Array.Empty<int>();
            var idx = new List<int>();
            for (var c = t; c; c = c.parent) idx.Add(c.GetSiblingIndex());
            idx.Reverse();
            return idx.ToArray();
        }

        private static int CompareKey(int[] a, int[] b)
        {
            var n = Mathf.Min(a.Length, b.Length);
            for (var i = 0; i < n; i++)
                if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Length.CompareTo(b.Length);
        }

        public static void Apply(Component owner, string fieldName, List<Object> ordered, string when)
        {
            var chain = OwnerChainPaths(owner); // shallow → deep

            // Deepest chain index each member lives at (-1 = co-exists with owner only in the scene).
            var depth = new int[ordered.Count];
            var anyScene = false;
            for (var k = 0; k < ordered.Count; k++)
            {
                depth[k] = DeepestCommonIndex(chain, ordered[k]);
                if (depth[k] < 0) anyScene = true;
            }

            // Walk DEEPEST (base) → shallowest, writing each prefab level's CUMULATIVE membership (everything
            // that exists at-or-below it). Editing base before variant means Unity records each level as just
            // its own delta vs its base — minimal, inheritance-correct overrides. (The members are already
            // ordered deepest→scene, so each level's slice is a stable prefix.)
            for (var j = chain.Count - 1; j >= 0; j--)
            {
                var levelMembers = new List<Object>();
                for (var k = 0; k < ordered.Count; k++)
                    if (depth[k] >= j) levelMembers.Add(ordered[k]);
                ApplyToPrefabAsset(owner, fieldName, levelMembers, chain[j], when);
            }

            // The scene instance carries ONLY the scene-only members as an override; if there are none it
            // should inherit cleanly, so revert any (stale) override instead of re-asserting one.
            if (chain.Count == 0 || anyScene)
                WriteList(owner, fieldName, ordered);
            else
                RevertInstanceOverride(owner, fieldName);
        }

        private static void RevertInstanceOverride(Component owner, string fieldName)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(owner)) return;
            var so = new SerializedObject(owner);
            var prop = so.FindProperty(fieldName);
            if (prop == null || !prop.prefabOverride) return;
            PrefabUtility.RevertPropertyOverride(prop, InteractionMode.AutomatedAction);
        }

        private static void ApplyToPrefabAsset(Component owner, string fieldName, List<Object> instanceItems,
            string path, string when)
        {
            var ownerInAsset = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(owner, path) as Component;
            if (!ownerInAsset) { WriteList(owner, fieldName, instanceItems); return; }

            var inAsset = new List<Object>(instanceItems.Count);
            foreach (var m in instanceItems)
            {
                var c = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(m, path);
                if (c) inAsset.Add(c);
            }

            if (!WriteList(ownerInAsset, fieldName, inAsset)) return;
            EditorUtility.SetDirty(ownerInAsset);
            AssetDatabase.SaveAssetIfDirty(ownerInAsset);
            Debug.Log($"[SceneConstraint/{when}] {owner.name}.{fieldName}: {inAsset.Count} entries recorded in " +
                      $"{System.IO.Path.GetFileName(path)} (inherited by all instances).", owner);
        }

        // Returns true if anything changed.
        private static bool WriteList(Object owner, string fieldName, IReadOnlyList<Object> items)
        {
            var so = new SerializedObject(owner);
            var prop = so.FindProperty(fieldName);
            if (prop == null || !prop.isArray)
            {
                Debug.LogError($"[SceneConstraint] {owner}: '{fieldName}' is not a serialized list.", owner);
                return false;
            }

            var changed = prop.arraySize != items.Count;
            prop.arraySize = items.Count;
            for (var i = 0; i < items.Count; i++)
            {
                var el = prop.GetArrayElementAtIndex(i);
                if (el.objectReferenceValue != items[i]) { el.objectReferenceValue = items[i]; changed = true; }
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }
    }
}
