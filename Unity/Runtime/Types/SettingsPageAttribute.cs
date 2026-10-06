using System;

namespace UnityJigs.Types
{
    /// <summary>
    /// Puts a <see cref="RuntimeScriptableSingleton{T}"/> on a Project Settings page: the page draws the preloaded
    /// asset's inspector inline, or offers to create it at <see cref="AssetPath"/> and add it to Preloaded Assets
    /// (UnityJigs.Editor.Settings.RuntimeSingletonSettings). One asset, readable at runtime, edited like a setting.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SettingsPageAttribute : Attribute
    {
        /// <summary>The page's path in Project Settings, e.g. "Project/My Game/Worlds".</summary>
        public readonly string Path;

        /// <summary>Where Create puts the asset, e.g. "Assets/Settings/Worlds.asset".</summary>
        public readonly string AssetPath;

        public SettingsPageAttribute(string path, string assetPath)
        {
            Path = path;
            AssetPath = assetPath;
        }
    }
}
