using System.Collections.Generic;
using UnityEngine;

namespace UnityJigs.Assistant.Editor
{
    /// Loud-failure helper for the reflection-based Unity-AI-Assistant hacks (McpAssemblyInjector,
    /// RunCommandNamespaceUnblocker, RunCommandTimeoutOverride). Those hacks no-op SILENTLY when the
    /// Assistant package is absent — which is correct — but that same silence would hide a package update
    /// that moves/renames an internal member they reflect on, leaving the hack quietly inactive.
    ///
    /// So each hack first checks whether the package is present (its anchor type resolves); if it is but a
    /// specific member can't be resolved, it calls <see cref="ReportMissing"/> to log a loud error instead
    /// of returning silently. De-dupes to one error per domain reload (the hacks fire on both
    /// afterAssemblyReload and delayCall).
    static class AssistantHackGuard
    {
        // Static => reset on every domain reload, so we warn once per reload while the breakage persists.
        static readonly HashSet<string> s_Warned = new();

        public static void ReportMissing(string hack, string member)
        {
            if (!s_Warned.Add(hack)) return; // already reported this domain (the other hook beat us to it)
            Debug.LogError(
                $"[{hack}] OUTDATED REFLECTION: the Unity AI Assistant package is installed, but '{member}' " +
                "could not be resolved — a package update has most likely moved, renamed, or re-scoped it, so " +
                "this hack is now INACTIVE. Fix the reflection target in UnityJigs/Assistant/Editor/.");
        }
    }
}
