using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using UnityJigs.Extensions;

namespace UnityJigs.Editor.CustomDrawers
{
    [CustomEditor(typeof(KenneyInputPrompts))]
    public class InputPromptParserEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            var didPress = GUILayout.Button("Parse Files");
            if (didPress)
            {
                var folder = EditorUtility.OpenFolderPanel("Select Directory", "", "");
                folder = Path.GetRelativePath(Path.GetDirectoryName(Application.dataPath), folder);
                ParseFiles((KenneyInputPrompts)serializedObject.targetObject, folder);
            }

            base.OnInspectorGUI();
        }

        [Button, PropertyOrder(-1)]
        public static void ParseFiles(KenneyInputPrompts self, string folderPath)
        {
            self.Platforms.Clear();

            var directories = Directory.GetDirectories(folderPath);

            foreach (var directory in directories)
            {
                var platformName = Path.GetFileName(directory);
                var xmlFiles = Directory.GetFiles(directory)
                    .Where(it => it.EndsWith("xml", StringComparison.InvariantCultureIgnoreCase))
                    .ToList();

                var defaultPath = xmlFiles.FirstOrDefault(it => it.EndsWith("default.xml"));
                var doublePath = xmlFiles.FirstOrDefault(it => it.EndsWith("double.xml"));

                var defaultSpriteXml = AssetDatabase.LoadAssetAtPath<TextAsset>(defaultPath);
                var doubleSpriteXml = AssetDatabase.LoadAssetAtPath<TextAsset>(doublePath);
                var platform = new InputPromptPlatform { PlatformId = platformName };
                GetPlatformButtons(platform,defaultSpriteXml, doubleSpriteXml);
                self.Platforms.Add(platform);
            }

            EditorUtility.SetDirty(self);
        }

        private static void GetPlatformButtons(InputPromptPlatform platform, TextAsset defaultSpriteXml, TextAsset doubleSpriteXml)
        {
            var spritesDefault = ParseFile(defaultSpriteXml, SpriteAlignment.Center);
            var spritesDouble = ParseFile(doubleSpriteXml, SpriteAlignment.Center);

            var ct = Mathf.Max(spritesDefault.Count, spritesDouble.Count);
            for (var i = 0; i < ct; i++)
            {
                var spriteDefault = spritesDefault[i];
                var spriteDouble = spritesDouble[i];

                if (spriteDefault.name != spriteDouble.name)
                    Debug.Log($"Name Mismatch: {spriteDefault.name} - {spriteDouble.name}");

                var button = new ButtonImage()
                {
                    ImageDefault = spriteDefault,
                    ImageDouble = spriteDouble,
                    ButtonId = spriteDefault.name,
                    PlatformId = platform.PlatformId
                };

                platform.Buttons.Add(button);
            }
        }

        public static List<Sprite> ParseFile(TextAsset asset, SpriteAlignment spriteAlignment,
            Vector2? customOffset = null)
        {
            var doc = new XmlDocument();
            doc.LoadXml(asset.text);
            var root = doc.DocumentElement;
            if (root?.Name != "TextureAtlas") throw new Exception("Bad file");
            var relativeImagePath = root.GetAttribute("imagePath");
            var textFilePath = AssetDatabase.GetAssetPath(asset);
            var parentPath = Path.GetDirectoryName(textFilePath);
            var imageFilePath = Path.Join(parentPath, relativeImagePath);
            var importer = AssetImporter.GetAtPath(imageFilePath) as TextureImporter;
            if (!importer) throw new Exception("Bad image path: " + imageFilePath);
            importer.isReadable = true;

            var metadata = new List<SpriteMetaData>();

            foreach (var child in root.ChildNodes)
            {
                if (child is not XmlNode childNode) continue;
                if (childNode.Name != "SubTexture") continue;
                var widthAttr = Convert.ToInt32(childNode.Attributes?["width"].Value);
                var heightAttr = Convert.ToInt32(childNode.Attributes?["height"].Value);
                var xAttr = Convert.ToInt32(childNode.Attributes?["x"].Value);
                var yAttr = Convert.ToInt32(childNode.Attributes?["y"].Value);
                var nameAttr = childNode.Attributes?["name"].Value;

                SpriteMetaData spriteMetaData = new()
                {
                    alignment = (int)spriteAlignment,
                    border = new Vector4(),
                    name = nameAttr,
                    pivot = GetPivotValue(spriteAlignment, customOffset),
                    rect = new Rect(xAttr, yAttr, widthAttr, heightAttr)
                };
                metadata.Add(spriteMetaData);
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
#pragma warning disable CS0618 // Type or member is obsolete
            importer.spritesheet = metadata.ToArray();
#pragma warning restore CS0618 // Type or member is obsolete


            EditorUtility.SetDirty(importer);

            try
            {
                AssetDatabase.StartAssetEditing();
                AssetDatabase.ImportAsset(importer.assetPath);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            var objects = AssetDatabase.LoadAllAssetsAtPath(importer.assetPath);

            return objects.OfType<Sprite>().ToList();
        }
        //<TextureAtlas imagePath="nintendo-gamecube_sheet_default.png">
        //<SubTexture name="gamecube_button_a" x="0" y="0" width="64" height="64"/>


        private static Vector2 GetPivotValue(SpriteAlignment alignment, Vector2? customOffset)
        {
            return alignment switch
            {
                SpriteAlignment.Center       => new Vector2(0.5f, 0.5f),
                SpriteAlignment.TopLeft      => new Vector2(0.0f, 1f),
                SpriteAlignment.TopCenter    => new Vector2(0.5f, 1f),
                SpriteAlignment.TopRight     => new Vector2(1f, 1f),
                SpriteAlignment.LeftCenter   => new Vector2(0.0f, 0.5f),
                SpriteAlignment.RightCenter  => new Vector2(1f, 0.5f),
                SpriteAlignment.BottomLeft   => new Vector2(0.0f, 0.0f),
                SpriteAlignment.BottomCenter => new Vector2(0.5f, 0.0f),
                SpriteAlignment.BottomRight  => new Vector2(1f, 0.0f),
                SpriteAlignment.Custom       => customOffset!.Value,
                _                            => Vector2.zero
            };
        }
    }
}
