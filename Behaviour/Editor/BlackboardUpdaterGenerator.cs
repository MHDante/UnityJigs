using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sirenix.OdinInspector;
using Unity.Behavior;
using UnityEditor;
using UnityJigs.Editor.Settings;
using UnityJigs.Extensions;
using FilePathAttribute = UnityEditor.FilePathAttribute;

namespace UnityJigs.Behaviour.Editor
{
    // ReSharper disable once EmptyNamespace

    [FilePath("ProjectSettings/BlackboardUpdater.asset", FilePathAttribute.Location.ProjectFolder)]
    public class BlackboardUpdaterGenerator : SettingsSingleton<BlackboardUpdaterGenerator>
    {
        protected override string Title => "Project/Blackboard Updater";
        protected override SettingsScope Scope => SettingsScope.Project;

        [FolderPath] public string GeneratedFolder = "Assets/Scripts/Generated";
        public string Namespace = "Generated";

        [TableList, ListDrawerSettings(IsReadOnly = false)]
        public List<BlackboardScriptTuple> AssetTuples = new();


        [Serializable]
        public class BlackboardScriptTuple
        {
            public RuntimeBlackboardAsset BlackboardAsset = null!;
            public MonoScript Script = null!;
            public Type? Type = null;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!this) return;

            foreach (var tuple in AssetTuples)
            {
                if (!tuple.Script)
                    tuple.Script = AssetDatabase.LoadAssetAtPath<MonoScript>(GetScriptPath(tuple.BlackboardAsset));
                if (!tuple.Script) continue;
                tuple.Type ??= tuple.Script.GetClass();
            }
        }

        public Type? GetType(RuntimeBlackboardAsset asset) => GetTuple(asset)?.Type;

        private BlackboardScriptTuple? GetTuple(RuntimeBlackboardAsset asset) =>
            AssetTuples.FirstOrDefault(it => it.BlackboardAsset == asset);

        public bool UpdateAsset(RuntimeBlackboardAsset asset)
        {
            var current = GetTuple(asset);
            if (current != null) return UpdateScript(current);
            var tuple = CreateScript(asset);
            AssetTuples.Add(tuple);
            return true;
        }

        [Button]
        private void UpdateAll()
        {
            AssetTuples.RemoveAll(it => !it.BlackboardAsset);
            foreach (var pair in AssetTuples)
            {
                if (pair.Script) UpdateScript(pair);
                else CreateScript(pair.BlackboardAsset);
            }
        }

        private BlackboardScriptTuple CreateScript(RuntimeBlackboardAsset asset)
        {
            var scriptPath = GetScriptPath(asset);
            var scriptText = MakeScriptText(asset.name, asset.Blackboard);
            File.WriteAllText(scriptPath, scriptText);
            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            return new() { BlackboardAsset = asset, Script = monoScript };
        }

        private string GetScriptPath(RuntimeBlackboardAsset asset)
        {
            var assetPath = AssetDatabase.GetAssetPath(asset);
            var assetName = Path.GetFileNameWithoutExtension(assetPath);
            var scriptName = GetComponentName(assetName) + ".g.cs";
            var scriptPath = Path.Combine(GeneratedFolder, scriptName);
            return scriptPath;
        }

        private string GetComponentName(string assetName) => "BlackBoardUpdater_" + Sanitize(assetName, false);

        private bool UpdateScript(BlackboardScriptTuple tuple)
        {
            var newScriptText = MakeScriptText(tuple.BlackboardAsset.name, tuple.BlackboardAsset.Blackboard);
            if (tuple.Script.text == newScriptText) return false;
            var path = AssetDatabase.GetAssetPath(tuple.Script);
            File.WriteAllText(path, newScriptText);
            return true;
        }

        public static string Sanitize(string input, bool verbatim = true)
        {
            if (string.IsNullOrEmpty(input))
                throw new ArgumentException("Input cannot be null or empty.");

            var sanitized = new StringBuilder();
            // Prefix with '@' to handle reserved keywords
            if (verbatim) sanitized.Append('@');

            // Ensure the first character is a letter or underscore
            if (char.IsLetter(input[0]) || input[0] == '_') sanitized.Append(input[0]);
            else sanitized.Append('_');

            // Process the rest of the characters, allowing letters, digits, and underscores
            for (var i = 1; i < input.Length; i++)
            {
                if (char.IsLetterOrDigit(input[i]) || input[i] == '_') sanitized.Append(input[i]);
                else sanitized.Append('_');
            }

            return sanitized.ToString();
        }

        public string MakeScriptText(string assetName, Blackboard blackboard)
        {
            var sb = new IndentedStringBuilder();
            sb.StartLine.AppendLine("// ReSharper disable All");
            sb.StartLine.AppendLine($"using {typeof(BlackboardUpdater).Namespace};");
            sb.StartLine.AppendLine($"namespace {Namespace}");
            using (sb.BraceScope())
            {
                var typeName = GetComponentName(assetName);
                sb.StartLine.AppendLine($"public class {typeName} : {nameof(BlackboardUpdater)}");
                using (sb.BraceScope())
                {
                    var variables = blackboard.Variables.Select(it => (it, Sanitize(it.Name))).ToList();

                    foreach (var (variable, varName) in variables)
                    {
                        var varTypeName =
                            $"{nameof(BlackboardValueOverride<int>)}<{variable.Type.GetFullCompilerSafeTypeName()}>";
                        sb.StartLine.AppendLine($"public {varTypeName} {varName};");
                    }

                    sb.Continue.AppendLine();

                    sb.StartLine.AppendLine($"public {typeName}()");
                    using (sb.BraceScope())
                    {
                        foreach (var (variable, varName) in variables)
                        {
                            sb.StartLine.AppendLine($"{varName} = new(this, \"{variable.GUID}\");");
                        }
                    }

                    sb.Continue.AppendLine();

                    sb.StartLine.AppendLine($"public override void {nameof(BlackboardUpdater.WriteOverrides)}()");
                    using (sb.BraceScope())
                    {
                        foreach (var (_, varName) in variables)
                        {
                            sb.StartLine.AppendLine(
                                $"{varName}.{nameof(BlackboardValueOverride<int>.WriteOverride)}();");
                        }
                    }
                }
            }

            return sb.ToString();
        }
    }
}
