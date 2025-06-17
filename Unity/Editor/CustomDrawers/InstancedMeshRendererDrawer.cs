using System.IO;
using System.Linq;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using UnityJigs.Components;
using UnityJigs.Editor.Utilities;

namespace UnityJigs.Editor.CustomDrawers
{
    [CustomEditor(typeof(InstancedMeshRenderer)), CanEditMultipleObjects]
    public class InstancedMeshRendererDrawer : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var upgradableTargets = targets.OfType<InstancedMeshRenderer>().Where(HasUpgrade).ToList();
            var ct = upgradableTargets.Count;
            if (ct <= 0) return;
            var label = ct == targets.Length ? "Upgrade" : $"Upgrade ({ct})";
            if (!GUILayout.Button(label)) return;
            foreach (var upgradableTarget in upgradableTargets) Upgrade(upgradableTarget);
        }

        private static bool HasUpgrade(InstancedMeshRenderer it) =>
            it.Mesh == null && it.GetComponent<MeshRenderer>();

        private void Upgrade(InstancedMeshRenderer renderer)
        {
            var mr = renderer.gameObject.GetComponent<MeshRenderer>();
            var mf = renderer.gameObject.GetComponent<MeshFilter>();

            var mesh = mf.sharedMesh;
            var material = mr.sharedMaterial;

            var existingMeshes = EditorUtils.FindAllAssetsOfType<InstancedMesh>();
            var match = existingMeshes.FirstOrDefault(it => it.Material == material && it.Mesh == mesh);

            if (match == null)
            {
                match = CreateInstance<InstancedMesh>();
                match.Mesh = mesh;
                match.Material = material;
                Directory.CreateDirectory("Assets/InstancedMeshes");
                var mName = match.Mesh?.name ?? renderer.name;
                var path = AssetDatabase.GenerateUniqueAssetPath($"Assets/InstancedMeshes/{mName}.asset");
                AssetDatabase.CreateAsset(match, path);
            }
            if (mr) DestroyImmediate(mr);
            if (mf) DestroyImmediate(mf);

            renderer.Mesh = match;
            EditorUtility.SetDirty(renderer);
        }
    }
}
