using System.IO;
using UnityEngine;

namespace UnityJigs.Lanes
{
    /// <summary>The lane this editor runs in (Lanes~/README.md): one checkout and one Unity editor each,
    /// so several agents can work in parallel. The main checkout is no lane; a git worktree in a folder
    /// named <c>&lt;repo&gt;-&lt;letter&gt;</c> (<c>my-game-b</c>) is lane B, and lanes B to J are numbered
    /// 1 to 9. Edit mode and editor play mode only: a build is never a lane.</summary>
    public static class Lane
    {
        public const int MaxIndex = 9;

        private static bool _Resolved;
        private static string? _Id;
        private static int _Index;
        private static string? _RepoRoot;

        /// <summary>"B" in lane B; the worktree folder's name in a worktree whose name has no lane
        /// letter; null in the main checkout and in a build.</summary>
        public static string? Id
        {
            get
            {
                Resolve();
                return _Id;
            }
        }

        /// <summary>1 to <see cref="MaxIndex"/> for lanes B to J, else 0.</summary>
        public static int Index
        {
            get
            {
                Resolve();
                return _Index;
            }
        }

        /// <summary>The checkout's root folder (the one holding <c>.git</c>); null in a build, or when
        /// the project isn't in a git checkout.</summary>
        public static string? RepoRoot
        {
            get
            {
                Resolve();
                return _RepoRoot;
            }
        }

        /// <summary><c>persistentDataPath</c>, and its <c>lane-b</c> folder in lane B: a game's save
        /// folder, so lanes can play at the same time without sharing saves (persistentDataPath is per
        /// user and product, not per checkout). Created on first use.</summary>
        public static string SaveFolder
        {
            get
            {
                if (Id == null) return Application.persistentDataPath;
                var folder = Path.Combine(Application.persistentDataPath, "lane-" + Id.ToLowerInvariant());
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        /// <summary>A per-user key (EditorPrefs, PlayerPrefs) made per lane: unchanged in the main
        /// checkout, "<paramref name="key"/>_lane-b" in lane B.</summary>
        public static string Key(string key) => Id == null ? key : key + "_lane-" + Id.ToLowerInvariant();

        private static void Resolve()
        {
            if (_Resolved) return;
            _Resolved = true;
#if UNITY_EDITOR
            // The repository root is the first folder at or above the project holding .git: a folder in
            // the main checkout, a file (pointing at the main checkout's .git) in a worktree.
            var dir = Path.GetDirectoryName(Application.dataPath);
            while (dir != null && !Directory.Exists(Path.Combine(dir, ".git")) && !File.Exists(Path.Combine(dir, ".git")))
                dir = Path.GetDirectoryName(dir);
            _RepoRoot = dir;
            if (dir == null || !File.Exists(Path.Combine(dir, ".git"))) return;

            var folder = Path.GetFileName(dir);
            var dash = folder.LastIndexOf('-');
            var suffix = dash < 0 ? "" : folder[(dash + 1)..].ToLowerInvariant();
            if (suffix.Length == 1 && suffix[0] >= 'b' && suffix[0] < 'b' + MaxIndex)
            {
                _Id = suffix.ToUpperInvariant();
                _Index = suffix[0] - 'a';
            }
            else
            {
                _Id = folder;
            }
#endif
        }
    }
}
