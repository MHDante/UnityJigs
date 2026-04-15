using System;
using System.Diagnostics;
using Sirenix.OdinInspector;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityJigs.Types
{
    [CreateAssetMenu(fileName = "GitVersionInfo", menuName = "Jigs/Git Version Info")]
    public class GitVersionInfo : RuntimeScriptableSingleton<GitVersionInfo>
    {
        public string GitDescribe = "";
        public string BuildDateTimeUtc = "";


        [Button]
        public string UpdateBuildId() => Application.isEditor ? TryGetGitBuildId() : GetStoredBuildId();


        private string TryGetGitBuildId()
        {
            try
            {
                var gitResult = RunGitDescribe();
                if (!string.IsNullOrEmpty(gitResult))
                {
                    var now = DateTime.UtcNow;
                    GitDescribe = gitResult;
                    BuildDateTimeUtc = now.ToString("o"); // ISO 8601 format
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

        private static string? RunGitDescribe()
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

    }
}