#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityJigs.Types; // CompositeColliderGenerator

namespace UnityJigs.Editor
{
    public static class CompositeColliderCleanup
    {
        private const string MenuPath = "Utils/CompositeCollider/Delete Children Deep (Prefabs + Scenes)";

        [MenuItem(MenuPath)]
        private static void DeleteChildrenDeep()
        {
            var iteration = 0;

            while (true)
            {
                iteration++;

                var assetTargetsProcessed = new HashSet<string>();
                var anyAssetChanged = false;

                var generators = Object.FindObjectsByType<CompositeColliderGenerator>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                foreach (var gen in generators)
                {
                    if (gen == null) continue;
                    if (!gen.gameObject) continue;

                    if (ProcessGeneratorInstance(gen, assetTargetsProcessed, out var assetChanged))
                        anyAssetChanged |= assetChanged;
                }

                if (!anyAssetChanged)
                    break;

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log("CompositeColliderCleanup: finished.");
        }

        /// <summary>
        /// Process a single CompositeColliderGenerator instance:
        /// - Build its prefab/variant chain (base → variants).
        /// - For each asset level, delete children on the corresponding object.
        /// - Finally, delete children on the instance itself.
        /// </summary>
        private static bool ProcessGeneratorInstance(
            CompositeColliderGenerator gen,
            HashSet<string> assetTargetsProcessed,
            out bool anyAssetChanged)
        {
            anyAssetChanged = false;
            var instanceGo = gen.gameObject;

            // Build asset chain: base-first list of (assetPath, siblingIndex path to this object).
            var assetChain = BuildPrefabChain(instanceGo);

            foreach (var node in assetChain)
            {
                var key = node.AssetPath + "|" + string.Join(".", node.SiblingPath);
                if (!assetTargetsProcessed.Add(key))
                    continue; // already processed this asset+object

                if (DeleteChildrenInPrefab(node.AssetPath, node.SiblingPath))
                    anyAssetChanged = true;
            }

            // Finally, delete children on the instance itself (scene or prefab stage).
            var instanceChanged = DeleteAllChildren(instanceGo.transform);

            return anyAssetChanged || instanceChanged;
        }

        private sealed class PrefabNode
        {
            public string AssetPath = string.Empty;
            public int[] SiblingPath = System.Array.Empty<int>();
        }

        /// <summary>
        /// Build the chain of prefab/variant objects corresponding to this instance:
        /// scene instance → variant asset → base asset → ...,
        /// returned as base-first list of nodes.
        /// </summary>
        private static List<PrefabNode> BuildPrefabChain(GameObject instance)
        {
            var chain = new List<PrefabNode>();

            var current = instance;
            while (true)
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(current);
                if (src == null)
                    break;

                if (src is not GameObject srcGo)
                    break;

                var assetPath = AssetDatabase.GetAssetPath(srcGo);
                if (string.IsNullOrEmpty(assetPath))
                    break;

                var assetType = PrefabUtility.GetPrefabAssetType(srcGo);
                if (assetType != PrefabAssetType.Regular &&
                    assetType != PrefabAssetType.Variant)
                    break;

                var siblingPath = GetSiblingIndexPath(srcGo.transform);
                chain.Add(new PrefabNode
                {
                    AssetPath = assetPath,
                    SiblingPath = siblingPath
                });

                // For variants: next loop will step into the base prefab.
                current = srcGo;
            }

            chain.Reverse(); // deepest base first, then variants
            return chain;
        }

        /// <summary>
        /// Get path from prefab root to this transform as sibling indices.
        /// Root itself → empty array; child → [idx], grandchild → [idxParent, idxChild], etc.
        /// </summary>
        private static int[] GetSiblingIndexPath(Transform t)
        {
            var indices = new List<int>();
            var current = t;

            while (current.parent != null)
            {
                indices.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            indices.Reverse();
            return indices.ToArray();
        }

        /// <summary>
        /// Open prefab asset at assetPath, find the object by siblingIndex path, delete ALL its children.
        /// Returns true if anything was actually deleted.
        /// </summary>
        private static bool DeleteChildrenInPrefab(string assetPath, int[] siblingPath)
        {
            var root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                var t = root.transform;

                for (var i = 0; i < siblingPath.Length; i++)
                {
                    var idx = siblingPath[i];
                    if (idx < 0 || idx >= t.childCount)
                        return false; // path mismatch; nothing to do

                    t = t.GetChild(idx);
                }

                return DeleteAllChildren(t);
            }
            finally
            {
                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Destroys ALL children of the given transform. Returns true if anything was deleted.
        /// </summary>
        private static bool DeleteAllChildren(Transform parent)
        {
            if (parent == null) return false;

            var changed = false;
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                Object.DestroyImmediate(child.gameObject);
                changed = true;
            }

            return changed;
        }
    }
}
#endif
