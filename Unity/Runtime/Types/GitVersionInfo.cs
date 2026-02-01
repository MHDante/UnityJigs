using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Serialization;
using Debug = UnityEngine.Debug;

namespace UnityJigs.Types
{
    /// <summary>
    /// Stores git version information that gets baked into builds.
    /// In editor, queries git directly. In builds, uses stored data.
    /// </summary>
    [CreateAssetMenu(fileName = "GitVersionInfo", menuName = "Jigs/Git Version Info")]
    public class GitVersionInfo : RuntimeScriptableSingleton<GitVersionInfo>
    {
        [FormerlySerializedAs("gitDescribe")] [SerializeField]
        private string GitDescribe = "";

        [FormerlySerializedAs("buildDateTimeUtc")] [SerializeField]
        private string BuildDateTimeUtc = "";

        private static string? _CachedBuildId;

        /// <summary>
        /// Gets the build ID string. Behavior depends on context:
        /// - Editor + git works: Runs git describe and caches result until domain reload
        /// - Editor + git fails: Returns "GIT ERROR - " + last known data
        /// - Build: Returns stored data from this asset
        /// </summary>
        public string GetBuildId()
        {
            if (Application.isEditor)
            {
                // Only run git describe once per domain reload
                if (_CachedBuildId != null) return _CachedBuildId;
                _CachedBuildId = TryGetGitBuildId();
                return _CachedBuildId;
            }

            // In build, use the baked data
            return GetStoredBuildId();
        }


        private string TryGetGitBuildId()
        {
            try
            {
                var gitResult = RunGitDescribe();
                if (!string.IsNullOrEmpty(gitResult))
                {
                    var now = DateTime.UtcNow;
                    return FormatBuildId(gitResult, now);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Git command failed: {e.Message}");
            }

            // Git failed, return error message with fallback
            var fallback = GetStoredBuildId();
            return $"GIT ERROR - {fallback}";
        }

        private string? RunGitDescribe()
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

            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output)) return output.Trim();
            if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"Git error: {error}");

            return null;
        }

        private string GetStoredBuildId()
        {
            if (string.IsNullOrEmpty(GitDescribe)) return "UNKNOWN BUILD";

            if (string.IsNullOrEmpty(BuildDateTimeUtc)) return GitDescribe;

            // Parse UTC time and convert to local
            if (DateTime.TryParse(BuildDateTimeUtc, out var utcTime))
            {
                var localTime = utcTime.ToLocalTime();
                return $"{GitDescribe} ({localTime:yyyy-MM-dd HH:mm:ss})";
            }

            return $"{GitDescribe} ({BuildDateTimeUtc})";
        }

        private static string FormatBuildId(string gitDescribe, DateTime utcTime)
        {
            var localTime = utcTime.ToLocalTime();
            return $"{gitDescribe} ({localTime:yyyy-MM-dd HH:mm:ss})";
        }

        public void SetBuildInfo(string gitDescribeOutput, DateTime buildTimeUtc)
        {
            GitDescribe = gitDescribeOutput;
            BuildDateTimeUtc = buildTimeUtc.ToString("o"); // ISO 8601 format

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}