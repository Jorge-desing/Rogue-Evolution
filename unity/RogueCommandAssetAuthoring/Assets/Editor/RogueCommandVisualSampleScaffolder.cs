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
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string outputPath = Path.GetFullPath(Path.Combine(projectRoot, BundleOutputPath));
                Directory.CreateDirectory(outputPath);

                var build = new AssetBundleBuild
                {
                    assetBundleName = BundleName,
                    assetNames = new[] { prefabPath }
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
                for (int i = 0; i < assetNames.Length; i++)
                {
                    if (assetNames[i].EndsWith("/scouttankvisual.prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        prefabAssetPath = assetNames[i];
                        break;
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
