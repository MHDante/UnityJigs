using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityJigs.Types;
using Debug = UnityEngine.Debug;

namespace UnityJigs.Editor.Utilities
{
    /// <summary>
    /// Build preprocessor that captures git version information before building.
    /// Runs git describe and stores the result in the GitVersionInfo asset.
    /// </summary>
    public class GitVersionBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            Debug.Log("GitVersionBuildPreprocessor: Capturing git version info...");

            // Find the GitVersionInfo asset. Throws (InvalidOperationException /
            // FileNotFoundException) if no GitVersionInfo is in PlayerSettings'
            // Preloaded Assets — projects that don't use it shouldn't fail the build.
            GitVersionInfo versionInfo;
            try
            {
                versionInfo = GitVersionInfo.Instance;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("GitVersionBuildPreprocessor: GitVersionInfo not in Preloaded Assets — " +
                                 "skipping version capture. " + ex.Message);
                return;
            }

            if (versionInfo == null)
            {
                Debug.LogError("GitVersionBuildPreprocessor: Could not find GitVersionInfo asset. " +
                               "Build will proceed but version info will be missing.");
                return;
            }

            // Try to run git describe
            var gitDescribe = versionInfo.UpdateBuildId();
            PlayerSettings.bundleVersion = versionInfo.GitDescribe;

            Debug.Log($"GitVersionBuildPreprocessor: Captured version: {gitDescribe}");
            EditorUtility.SetDirty(versionInfo);
            // Save the asset
            AssetDatabase.SaveAssets();

            Debug.Log($"GitVersionBuildPreprocessor: " +
                      $"Version info saved to asset at {AssetDatabase.GetAssetPath(versionInfo)}");
        }
    }
}