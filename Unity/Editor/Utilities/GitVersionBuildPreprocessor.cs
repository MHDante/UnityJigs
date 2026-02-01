using System;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
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

            // Find the GitVersionInfo asset
            var versionInfo = FindGitVersionInfoAsset();

            if (versionInfo == null)
            {
                Debug.LogError("GitVersionBuildPreprocessor: Could not find GitVersionInfo asset. Build will proceed but version info will be missing.");
                return;
            }

            // Try to run git describe
            var gitDescribe = RunGitDescribe();
            var buildTime = DateTime.UtcNow;

            if (string.IsNullOrEmpty(gitDescribe))
            {
                Debug.LogWarning("GitVersionBuildPreprocessor: Git command failed. Using fallback 'UNKNOWN BUILD'.");
                gitDescribe = "UNKNOWN BUILD";
            }
            else
            {
                Debug.Log($"GitVersionBuildPreprocessor: Captured version: {gitDescribe}");
            }

            // Store the information
            versionInfo.SetBuildInfo(gitDescribe!, buildTime);
            
            // Save the asset
            AssetDatabase.SaveAssets();
            
            Debug.Log($"GitVersionBuildPreprocessor: Version info saved to asset at {AssetDatabase.GetAssetPath(versionInfo)}");
        }

        private GitVersionInfo? FindGitVersionInfoAsset()
        {
            // Search for GitVersionInfo assets in the project
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(GitVersionInfo)}");

            if (guids.Length == 0)
            {
                Debug.LogError("GitVersionBuildPreprocessor: No GitVersionInfo asset found in project. Please create one via Assets > Create > Build Info > Git Version Info");
                return null;
            }

            if (guids.Length > 1)
            {
                Debug.LogWarning($"GitVersionBuildPreprocessor: Multiple GitVersionInfo assets found. Using first one.");
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<GitVersionInfo>(path);
        }

        private string? RunGitDescribe()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "describe --tags --always --dirty",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Application.dataPath
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    Debug.LogWarning("GitVersionBuildPreprocessor: Failed to start git process.");
                    return null;
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output)) return output.Trim();

                if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"GitVersionBuildPreprocessor: Git error: {error}");

                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"GitVersionBuildPreprocessor: Exception running git: {e.Message}");
                return null;
            }
        }
    }
}