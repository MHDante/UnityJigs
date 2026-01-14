#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// CompositeColliderGenerator namespace

namespace UnityJigs.Types
{
    public static class CompositeColliderCleanup
    {
        private const string MenuPath = "Utils/CompositeCollider/Deep Delete Collider Children";

        [MenuItem(MenuPath)]
        private static void DeepDeleteColliderChildren()
        {
            Debug.Log("=== CompositeColliderCleanupV4: START ===");

            // Pass 1: gather generators in scene hierarchy order
            var sceneGenerators = CollectSceneGeneratorsInHierarchyOrder();

            Debug.Log($"[Pass] Found {sceneGenerators.Count} CompositeColliderGenerator instances in hierarchy order.");

            // Pass 2: build all nodes (scene + prefab/variant levels)
            var allNodes = BuildAllNodes(sceneGenerators);

            Debug.Log($"[Pass] Built {allNodes.Count} nodes (scene + prefab chain levels).");

            // Sort: deepest first, then by original discovery order
            allNodes.Sort((a, b) =>
            {
                var depthCompare = b.Depth.CompareTo(a.Depth);
                if (depthCompare != 0) return depthCompare;
                return a.OrderIndex.CompareTo(b.OrderIndex);
            });

            // Pass 3: process nodes, delete children at each level
            var totalDeleted = 0;

            foreach (var node in allNodes)
            {
                if (node.AssetPath == null)
                {
                    // Scene instance
                    if (!node.SceneTransform) continue;

                    Debug.Log($"[Depth {node.Depth}] Scene  '{node.SourceSceneName}' at '{node.SourceScenePath}'");

                    var deleted = DeleteAllChildren(node.SceneTransform);
                    totalDeleted += deleted;

                    if (deleted > 0)
                        Debug.Log($"    → Deleted {deleted} children on scene object '{node.SourceScenePath}'.");
                    else
                        Debug.Log($"    → No children to delete on scene object '{node.SourceScenePath}'.");
                }
                else
                {
                    // Prefab/variant asset
                    Debug.Log($"[Depth {node.Depth}] Prefab '{node.AssetPath}' at '{node.PrefabPathInAsset}' (from original scene generator '{node.SourceScenePath}')");

                    var root = PrefabUtility.LoadPrefabContents(node.AssetPath);
                    try
                    {
                        var target = GetTransformAtPath(root.transform, node.SiblingPathInAsset);
                        if (!target)
                        {
                            Debug.Log($"    → Target path not found in prefab '{node.AssetPath}' (hierarchy changed?).");
                        }
                        else
                        {
                            var deleted = DeleteAllChildren(target);
                            totalDeleted += deleted;

                            if (deleted > 0)
                            {
                                PrefabUtility.SaveAsPrefabAsset(root, node.AssetPath);
                                Debug.Log($"    → Deleted {deleted} children and saved prefab '{node.AssetPath}'.");
                            }
                            else
                            {
                                Debug.Log($"    → No children to delete for '{node.PrefabPathInAsset}' in '{node.AssetPath}'.");
                            }
                        }
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"=== CompositeColliderCleanupV4: DONE. Total children deleted: {totalDeleted} ===");
        }

        // --------------------------------------------------------------------
        // Data structures
        // --------------------------------------------------------------------

        private sealed class SceneGeneratorInfo
        {
            public CompositeColliderGenerator Generator = null!;
            public Transform SceneTransform = null!;
            public string SceneName = string.Empty;
            public string ScenePath = string.Empty;
            public int OrderIndex;
        }

        private sealed class GeneratorNode
        {
            public int Depth;
            public int OrderIndex;

            public string SourceSceneName = string.Empty;
            public string SourceScenePath = string.Empty;

            public Transform? SceneTransform; // for scene nodes

            public string? AssetPath;         // null for scene
            public string? PrefabPathInAsset; // null for scene
            public int[] SiblingPathInAsset = Array.Empty<int>(); // for prefab nodes
        }

        // --------------------------------------------------------------------
        // Pass 1: collect generators in hierarchy order
        // --------------------------------------------------------------------

        private static List<SceneGeneratorInfo> CollectSceneGeneratorsInHierarchyOrder()
        {
            var result = new List<SceneGeneratorInfo>();
            var orderIndex = 0;

            // 1) Regular scenes
            var sceneCount = SceneManager.sceneCount;
            for (var s = 0; s < sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;

                var roots = scene.GetRootGameObjects();
                Debug.Log($"[Collect] Scene '{scene.path}' has {roots.Length} root objects.");

                for (var r = 0; r < roots.Length; r++)
                {
                    TraverseHierarchyForGenerators(roots[r].transform, scene, ref orderIndex, result);
                }
            }

            // 2) Current prefab stage (if any)
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.prefabContentsRoot != null)
            {
                var root = prefabStage.prefabContentsRoot;
                var pseudoScene = root.scene; // prefab stage uses a temp scene

                Debug.Log($"[Collect] Prefab stage '{prefabStage.assetPath}' root '{root.name}'.");
                TraverseHierarchyForGenerators(root.transform, pseudoScene, ref orderIndex, result);
            }

            return result;
        }

        private static void TraverseHierarchyForGenerators(
            Transform root,
            Scene scene,
            ref int orderIndex,
            List<SceneGeneratorInfo> result)
        {
            var stack = new Stack<Transform>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (!t) continue;

                if (t.TryGetComponent<CompositeColliderGenerator>(out var gen) && gen && gen.gameObject)
                {
                    var info = new SceneGeneratorInfo
                    {
                        Generator = gen,
                        SceneTransform = t,
                        SceneName = scene.name,
                        ScenePath = GetScenePath(t),
                        OrderIndex = orderIndex++
                    };
                    result.Add(info);
                }

                for (var i = t.childCount - 1; i >= 0; i--)
                    stack.Push(t.GetChild(i));
            }
        }

        // --------------------------------------------------------------------
        // Pass 2: build node list (scene + prefab chain levels)
        // --------------------------------------------------------------------

        private static List<GeneratorNode> BuildAllNodes(List<SceneGeneratorInfo> sceneGenerators)
        {
            var nodes = new List<GeneratorNode>();

            foreach (var info in sceneGenerators)
            {
                var gen = info.Generator;
                if (!gen || !gen.gameObject) continue;

                var current = gen.gameObject;
                var depth = 0;

                // Level 0: scene instance
                nodes.Add(new GeneratorNode
                {
                    Depth = depth,
                    OrderIndex = info.OrderIndex,
                    SourceSceneName = info.SceneName,
                    SourceScenePath = info.ScenePath,
                    SceneTransform = info.SceneTransform,
                    AssetPath = null,
                    PrefabPathInAsset = null,
                    SiblingPathInAsset = Array.Empty<int>()
                });

                // Higher levels: prefab / variant chain
                while (true)
                {
                    var srcObj = PrefabUtility.GetCorrespondingObjectFromSource(current);
                    if (srcObj is not GameObject srcGo)
                        break;

                    var assetPath = AssetDatabase.GetAssetPath(srcGo);
                    if (string.IsNullOrEmpty(assetPath))
                        break;

                    var prefabType = PrefabUtility.GetPrefabAssetType(srcGo);
                    if (prefabType != PrefabAssetType.Regular &&
                        prefabType != PrefabAssetType.Variant)
                    {
                        break;
                    }

                    var srcTf = srcGo.transform;
                    var assetRoot = srcTf;
                    while (assetRoot.parent != null)
                        assetRoot = assetRoot.parent;

                    var siblingPath = GetSiblingIndexPath(srcTf, assetRoot);
                    var localPath = GetHierarchyPath(srcTf, assetRoot);

                    depth++;

                    nodes.Add(new GeneratorNode
                    {
                        Depth = depth,
                        OrderIndex = info.OrderIndex,
                        SourceSceneName = info.SceneName,
                        SourceScenePath = info.ScenePath,
                        SceneTransform = info.SceneTransform,
                        AssetPath = assetPath,
                        PrefabPathInAsset = localPath,
                        SiblingPathInAsset = siblingPath
                    });

                    current = srcGo;
                }
            }

            return nodes;
        }

        // --------------------------------------------------------------------
        // Deletion helpers
        // --------------------------------------------------------------------

        private static int DeleteAllChildren(Transform parent)
        {
            if (!parent) return 0;

            var deleted = 0;
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                Object.DestroyImmediate(child.gameObject);
                deleted++;
            }

            return deleted;
        }

        private static Transform? GetTransformAtPath(Transform root, int[] path)
        {
            var t = root;
            for (var i = 0; i < path.Length; i++)
            {
                var idx = path[i];
                if (idx < 0 || idx >= t.childCount)
                    return null;

                t = t.GetChild(idx);
            }

            return t;
        }

        // --------------------------------------------------------------------
        // Path helpers
        // --------------------------------------------------------------------

        private static int[] GetSiblingIndexPath(Transform t, Transform root)
        {
            var indices = new List<int>();
            var current = t;

            while (current && current != root)
            {
                indices.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            indices.Reverse();
            return indices.ToArray();
        }

        private static string GetHierarchyPath(Transform t, Transform? root = null)
        {
            var names = new List<string>();
            var current = t;
            while (current && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static string GetScenePath(Transform t)
        {
            var root = t.root;
            var rel = GetHierarchyPath(t, root);
            return string.IsNullOrEmpty(rel) ? root.name : $"{root.name}/{rel}";
        }
    }
}
#endif
