using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace RogueCommandExpansion.Authoring
{
    /// <summary>
    /// Builds a script-free sample model bundle that can be overlaid onto a native unit prefab.
    /// </summary>
    internal static class RogueCommandVisualSampleScaffolder
    {
        private const string RequiredUnityVersion = "2021.3.45f2";
        private const string AssetRoot = "Assets/RogueCommandExpansionSample";
        private const string PrefabPath = AssetRoot + "/ScoutTankVisual.prefab";
        private const string CardSpritePath = AssetRoot + "/SandlanceScoutCard.png";
        private const string ModelPath = AssetRoot + "/Models/SandlanceScout.fbx";
        private const string MaterialRoot = AssetRoot + "/Materials";
        private const string BundleOutputPath = "../../artifacts/asset-bundles";
        private const string BundleName = "sample-native-expansion.bundle";

        [MenuItem("Rogue Command/Asset Bundles/Build Sample Native Visual Pack")]
        public static void BuildSampleNativeExpansion()
        {
            try
            {
                if (!String.Equals(Application.unityVersion, RequiredUnityVersion, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("This sample must be built with Unity " +
                        RequiredUnityVersion + "; current Editor is " + Application.unityVersion + ".");
                }

                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,
                    BuildTarget.StandaloneWindows64))
                {
                    throw new InvalidOperationException(
                        "Install the Unity Windows Build Support module before building this sample.");
                }

                string prefabPath = CreateSampleVisual();
                CreateScoutCardSprite();
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string outputPath = Path.GetFullPath(Path.Combine(projectRoot, BundleOutputPath));
                Directory.CreateDirectory(outputPath);

                var build = new AssetBundleBuild
                {
                    assetBundleName = BundleName,
                    assetNames = new[] { prefabPath, CardSpritePath }
                };
                AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(outputPath,
                    new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression |
                    BuildAssetBundleOptions.ForceRebuildAssetBundle,
                    BuildTarget.StandaloneWindows64);
                if (manifest == null)
                {
                    throw new InvalidOperationException("Unity did not produce an AssetBundle manifest.");
                }

                string[] bundleNames = manifest.GetAllAssetBundles();
                if (bundleNames == null || bundleNames.Length != 1 ||
                    !String.Equals(bundleNames[0], BundleName, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Unity's AssetBundle manifest does not contain exactly " +
                        BundleName + ".");
                }

                ValidateBuiltVisualBundle(Path.Combine(outputPath, BundleName));

                string packRoot = ResolvePackRoot(projectRoot);
                string packBundles = Path.Combine(packRoot, "bundles");
                Directory.CreateDirectory(packBundles);
                CopyRequiredFile(Path.Combine(outputPath, BundleName),
                    Path.Combine(packBundles, BundleName));
                string generatedManifest = Path.Combine(outputPath, new DirectoryInfo(outputPath).Name);
                CopyRequiredFile(generatedManifest,
                    Path.Combine(packBundles, "assetbundle-manifest.bundle"));

                AssetDatabase.Refresh();
                Debug.Log("Built the script-free " + BundleName + " visual overlay for sample-native:expansion at " +
                    packBundles + ". The game controller remains on the native unit prefab.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
                else
                {
                    EditorUtility.DisplayDialog("Sample visual bundle build failed", exception.Message, "OK");
                }
            }
        }

        private static string CreateSampleVisual()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, AssetRoot)));
            AssetDatabase.Refresh();

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException("Import the authored Sandlance FBX before building the " +
                    "sample bundle.", ModelPath);
            }

            Dictionary<string, Material> materials = CreateScoutMaterials();

            var root = new GameObject("ScoutTankVisual");
            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null)
                {
                    throw new InvalidOperationException("Unity could not instantiate the authored Sandlance FBX.");
                }

                modelInstance.name = "SandlanceScoutModel";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;
                AssignScoutMaterials(modelInstance, materials);

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Unity could not save " + PrefabPath + ".");
                }

                NormalizeEmptyUnityYamlStrings(Path.GetFullPath(Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName, PrefabPath)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetImporter importer = AssetImporter.GetAtPath(PrefabPath);
            if (importer == null)
            {
                throw new FileNotFoundException("Unity did not import " + PrefabPath + ".");
            }

            importer.assetBundleName = BundleName;
            importer.assetBundleVariant = String.Empty;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            NormalizeGeneratedUnityMetaFiles(projectRoot);
            return PrefabPath;
        }

        private static void ValidateBuiltVisualBundle(string bundlePath)
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                throw new InvalidDataException("Unity could not reopen the sample visual AssetBundle.");
            }

            try
            {
                string[] assetNames = bundle.GetAllAssetNames();
                string prefabAssetPath = null;
                string cardSpriteAssetPath = null;
                for (int i = 0; i < assetNames.Length; i++)
                {
                    if (assetNames[i].EndsWith("/scouttankvisual.prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        prefabAssetPath = assetNames[i];
                    }
                    else if (assetNames[i].EndsWith("/sandlancescoutcard.png", StringComparison.OrdinalIgnoreCase))
                    {
                        cardSpriteAssetPath = assetNames[i];
                    }
                }

                if (prefabAssetPath == null)
                {
                    throw new InvalidDataException("The sample visual prefab is not listed in its AssetBundle.");
                }

                GameObject prefab = bundle.LoadAsset<GameObject>(prefabAssetPath);
                if (prefab == null || !String.Equals(prefab.name, "ScoutTankVisual", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The sample visual prefab could not be loaded by its AssetBundle path.");
                }

                Sprite cardSprite = cardSpriteAssetPath == null
                    ? null
                    : bundle.LoadAsset<Sprite>(cardSpriteAssetPath);
                if (cardSprite == null ||
                    !String.Equals(cardSprite.name, "SandlanceScoutCard", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The Sandlance Scout card Sprite is missing or has the wrong asset name.");
                }

                Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    throw new InvalidDataException("The sample visual prefab has no renderers.");
                }

                MeshFilter[] meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
                if (meshFilters.Length < 20)
                {
                    throw new InvalidDataException("The authored Sandlance model is missing expected independent " +
                        "mesh parts. Expected at least 20 MeshFilters, found " + meshFilters.Length + ".");
                }

                for (int i = 0; i < meshFilters.Length; i++)
                {
                    if (meshFilters[i] == null || meshFilters[i].sharedMesh == null ||
                        meshFilters[i].sharedMesh.vertexCount < 3)
                    {
                        throw new InvalidDataException("The Sandlance visual contains an empty mesh.");
                    }
                }

                for (int i = 0; i < renderers.Length; i++)
                {
                    Material[] assignedMaterials = renderers[i].sharedMaterials;
                    if (assignedMaterials == null || assignedMaterials.Length == 0)
                    {
                        throw new InvalidDataException("The Sandlance visual renderer '" + renderers[i].name +
                            "' has no material.");
                    }

                    for (int materialIndex = 0; materialIndex < assignedMaterials.Length; materialIndex++)
                    {
                        if (assignedMaterials[materialIndex] == null)
                        {
                            throw new InvalidDataException("The Sandlance visual renderer '" + renderers[i].name +
                                "' has a missing material.");
                        }
                    }
                }

                Component[] components = prefab.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] == null || components[i] is MonoBehaviour)
                    {
                        throw new InvalidDataException("The sample visual bundle contains a missing or scripted component.");
                    }
                }
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        private static Dictionary<string, Material> CreateScoutMaterials()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, MaterialRoot)));
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal)
            {
                { "armor", CreateOrUpdateMaterial("ScoutArmor", new Color32(36, 69, 82, 255), 0.42f, 0.63f) },
                { "lightArmor", CreateOrUpdateMaterial("ScoutLightArmor", new Color32(67, 95, 98, 255), 0.32f, 0.60f) },
                { "tread", CreateOrUpdateMaterial("ScoutTread", new Color32(22, 27, 30, 255), 0.18f, 0.34f) },
                { "wheel", CreateOrUpdateMaterial("ScoutWheel", new Color32(48, 65, 69, 255), 0.60f, 0.58f) },
                { "gunmetal", CreateOrUpdateMaterial("ScoutGunmetal", new Color32(27, 38, 43, 255), 0.74f, 0.70f) },
                { "amber", CreateOrUpdateMaterial("ScoutSignalAmber", new Color32(232, 108, 32, 255), 0.18f, 0.64f) },
                { "sensor", CreateOrUpdateMaterial("ScoutSensorGlass", new Color32(31, 205, 191, 255), 0.20f, 0.76f) }
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return materials;
        }

        private static void NormalizeGeneratedUnityMetaFiles(string projectRoot)
        {
            string[] assetDirectories =
            {
                MaterialRoot,
                AssetRoot + "/Models"
            };
            for (int i = 0; i < assetDirectories.Length; i++)
            {
                string absoluteDirectory = Path.GetFullPath(Path.Combine(projectRoot, assetDirectories[i]));
                string folderMetadataPath = absoluteDirectory + ".meta";
                if (File.Exists(folderMetadataPath))
                {
                    NormalizeEmptyUnityYamlStrings(folderMetadataPath);
                }

                if (!Directory.Exists(absoluteDirectory))
                {
                    continue;
                }

                string[] metadataFiles = Directory.GetFiles(
                    absoluteDirectory, "*.meta", SearchOption.TopDirectoryOnly);
                for (int metadataIndex = 0; metadataIndex < metadataFiles.Length; metadataIndex++)
                {
                    NormalizeEmptyUnityYamlStrings(metadataFiles[metadataIndex]);
                }
            }
        }

        private static void CreateScoutCardSprite()
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(CardSpritePath);
            if (existing != null)
            {
                TextureImporter existingImporter = AssetImporter.GetAtPath(CardSpritePath) as TextureImporter;
                if (existingImporter == null)
                {
                    throw new InvalidDataException("Unity did not create a TextureImporter for " + CardSpritePath + ".");
                }

                existingImporter.assetBundleName = BundleName;
                existingImporter.assetBundleVariant = String.Empty;
                existingImporter.SaveAndReimport();
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot,
                CardSpritePath.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(absolutePath))
            {
                throw new InvalidDataException("SandlanceScoutCard.png exists but Unity did not import it as a Sprite; preserving the file.");
            }

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                byte shade = (byte)(22 + y / 8);
                for (int x = 0; x < size; x++)
                {
                    texture.SetPixel(x, y, new Color32((byte)(10 + shade / 3), shade, (byte)(shade + 10), 255));
                }
            }

            for (int x = 10; x < 119; x++)
            {
                texture.SetPixel(x, 16, new Color32(26, 49, 61, 255));
                texture.SetPixel(x, 112, new Color32(26, 49, 61, 255));
            }

            DrawPolygon(texture, new[] { new Vector2Int(18, 31), new Vector2Int(111, 31),
                new Vector2Int(104, 47), new Vector2Int(24, 47) }, new Color32(19, 28, 34, 255));
            for (int wheel = 0; wheel < 5; wheel++)
            {
                DrawCircle(texture, 31 + wheel * 17, 39, 7, new Color32(47, 65, 69, 255));
                DrawCircle(texture, 31 + wheel * 17, 39, 3, new Color32(25, 38, 43, 255));
            }

            DrawPolygon(texture, new[] { new Vector2Int(21, 47), new Vector2Int(39, 68),
                new Vector2Int(84, 68), new Vector2Int(107, 55), new Vector2Int(108, 47) },
                new Color32(75, 131, 139, 255));
            DrawPolygon(texture, new[] { new Vector2Int(34, 53), new Vector2Int(42, 63),
                new Vector2Int(79, 63), new Vector2Int(91, 54) }, new Color32(104, 166, 169, 255));
            DrawPolygon(texture, new[] { new Vector2Int(47, 67), new Vector2Int(54, 82),
                new Vector2Int(83, 82), new Vector2Int(91, 69), new Vector2Int(81, 65) },
                new Color32(95, 150, 153, 255));
            DrawPolygon(texture, new[] { new Vector2Int(76, 73), new Vector2Int(119, 77),
                new Vector2Int(119, 82), new Vector2Int(74, 79) }, new Color32(178, 194, 182, 255));
            DrawPolygon(texture, new[] { new Vector2Int(58, 82), new Vector2Int(61, 95),
                new Vector2Int(65, 95), new Vector2Int(66, 82) }, new Color32(205, 116, 48, 255));
            DrawPolygon(texture, new[] { new Vector2Int(53, 95), new Vector2Int(53, 100),
                new Vector2Int(70, 100), new Vector2Int(70, 95) }, new Color32(242, 170, 72, 255));
            DrawPolygon(texture, new[] { new Vector2Int(28, 49), new Vector2Int(40, 54),
                new Vector2Int(31, 57) }, new Color32(37, 206, 191, 255));
            texture.SetPixel(61, 86, new Color32(37, 206, 191, 255));
            texture.SetPixel(62, 86, new Color32(37, 206, 191, 255));
            texture.Apply();

            byte[] png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllBytes(absolutePath, png);
            AssetDatabase.ImportAsset(CardSpritePath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(CardSpritePath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("Unity did not create a TextureImporter for " + CardSpritePath + ".");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.assetBundleName = BundleName;
            importer.assetBundleVariant = String.Empty;
            importer.SaveAndReimport();
            if (AssetDatabase.LoadAssetAtPath<Sprite>(CardSpritePath) == null)
            {
                throw new InvalidDataException("Unity failed to import the Sandlance Scout card as a Sprite.");
            }
        }

        private static void DrawPolygon(Texture2D texture, Vector2Int[] vertices, Color32 color)
        {
            int minimumY = texture.height;
            int maximumY = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                minimumY = Math.Min(minimumY, vertices[i].y);
                maximumY = Math.Max(maximumY, vertices[i].y);
            }

            for (int y = minimumY; y <= maximumY; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    bool inside = false;
                    for (int i = 0, previous = vertices.Length - 1; i < vertices.Length; previous = i++)
                    {
                        Vector2Int a = vertices[i];
                        Vector2Int b = vertices[previous];
                        bool crosses = (a.y > y) != (b.y > y) &&
                            x < (b.x - a.x) * (y - a.y) / (float)(b.y - a.y) + a.x;
                        if (crosses)
                        {
                            inside = !inside;
                        }
                    }

                    if (inside)
                    {
                        texture.SetPixel(x, y, color);
                    }
                }
            }
        }

        private static void DrawCircle(Texture2D texture, int centerX, int centerY, int radius, Color32 color)
        {
            int radiusSquared = radius * radius;
            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    int offsetX = x - centerX;
                    int offsetY = y - centerY;
                    if (offsetX * offsetX + offsetY * offsetY <= radiusSquared &&
                        x >= 0 && x < texture.width && y >= 0 && y < texture.height)
                    {
                        texture.SetPixel(x, y, color);
                    }
                }
            }
        }

        private static void NormalizeEmptyUnityYamlStrings(string filePath)
        {
            string contents = File.ReadAllText(filePath);
            string normalized = Regex.Replace(contents,
                @"(?m)^([ \t]*(?:value|userData|assetBundleName|assetBundleVariant|motionNodeName|" +
                @"rigImportErrors|rigImportWarnings|animationImportErrors|animationImportWarnings|" +
                @"animationRetargetingWarnings|rootMotionBoneName):)[ \t]*(\r?)$",
                "$1 \"\"$2");
            if (!String.Equals(contents, normalized, StringComparison.Ordinal))
            {
                File.WriteAllText(filePath, normalized, new UTF8Encoding(false));
            }
        }

        private static Material CreateOrUpdateMaterial(
            string name,
            Color color,
            float metallic,
            float smoothness)
        {
            string path = MaterialRoot + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException("Unity's Standard shader is unavailable for " + name + ".");
            }

            if (material == null)
            {
                material = new Material(shader);
                material.name = name;
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void AssignScoutMaterials(GameObject model, IDictionary<string, Material> materials)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidDataException("The authored Sandlance FBX contains no renderers.");
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                string name = renderers[i].gameObject.name;
                string materialKey;
                if (name.StartsWith("ScoutArmor_", StringComparison.Ordinal))
                {
                    materialKey = name.Contains("Deck") || name.Contains("LowTurret")
                        ? "lightArmor" : "armor";
                }
                else if (name.StartsWith("ScoutTreads_", StringComparison.Ordinal))
                {
                    materialKey = "tread";
                }
                else if (name.StartsWith("ScoutWheel_", StringComparison.Ordinal))
                {
                    materialKey = name.Contains("Road") ? "wheel" : "gunmetal";
                }
                else if (name.StartsWith("ScoutGun_", StringComparison.Ordinal) ||
                    name.StartsWith("ScoutSensor_Mast", StringComparison.Ordinal))
                {
                    materialKey = "gunmetal";
                }
                else if (name.StartsWith("ScoutSensor_Lens", StringComparison.Ordinal) ||
                    name.StartsWith("ScoutAccent_FrontLight", StringComparison.Ordinal))
                {
                    materialKey = "sensor";
                }
                else if (name.StartsWith("ScoutSensor_Array", StringComparison.Ordinal))
                {
                    materialKey = "lightArmor";
                }
                else if (name.StartsWith("ScoutAccent_", StringComparison.Ordinal))
                {
                    materialKey = "amber";
                }
                else
                {
                    throw new InvalidDataException("No authored material mapping exists for model part '" +
                        name + "'.");
                }

                Material material = materials[materialKey];
                Material[] assigned = renderers[i].sharedMaterials;
                if (assigned == null || assigned.Length == 0)
                {
                    assigned = new Material[1];
                }
                for (int materialIndex = 0; materialIndex < assigned.Length; materialIndex++)
                {
                    assigned[materialIndex] = material;
                }
                renderers[i].sharedMaterials = assigned;
            }
        }

        private static string ResolvePackRoot(string projectRoot)
        {
            string configuredRoot = Environment.GetEnvironmentVariable("RCL_PACK_ROOT");
            string packRoot = String.IsNullOrWhiteSpace(configuredRoot)
                ? Path.GetFullPath(Path.Combine(projectRoot, "..", "..", "content", "packs",
                    "sample-native-expansion"))
                : Path.GetFullPath(configuredRoot);
            if (!Directory.Exists(packRoot) || !File.Exists(Path.Combine(packRoot, "content.json")))
            {
                throw new DirectoryNotFoundException("The sample-native-expansion pack was not found at " +
                    packRoot + ". Set RCL_PACK_ROOT to its folder when running this command from a clone.");
            }

            return packRoot;
        }

        private static void CopyRequiredFile(string source, string destination)
        {
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("Unity did not write the expected AssetBundle output.", source);
            }

            File.Copy(source, destination, true);
        }
    }
}
