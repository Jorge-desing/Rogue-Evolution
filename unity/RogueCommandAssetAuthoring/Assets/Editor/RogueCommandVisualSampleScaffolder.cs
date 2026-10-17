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
        private const string BikePrefabPath = AssetRoot + "/SkirmishBikeVisual.prefab";
        private const string BikeCardSpritePath = AssetRoot + "/SkirmishBikeCard.png";
        private const string BikeModelPath = AssetRoot + "/Models/SkirmishStrikeBike.fbx";
        private const string ColossusPrefabPath = AssetRoot + "/ColossusSiegeVisual.prefab";
        private const string ColossusCardSpritePath = AssetRoot + "/ColossusSiegeCard.png";
        private const string ColossusModelPath = AssetRoot + "/Models/ColossusSiegePlatform.fbx";
        private const string IroncladPrefabPath = AssetRoot + "/IroncladWalkerVisual.prefab";
        private const string IroncladModelPath = AssetRoot + "/Models/IroncladWalker.fbx";
        private const string SurgeArcTankPrefabPath = AssetRoot + "/SurgeArcTankVisual.prefab";
        private const string SurgeArcTankModelPath = AssetRoot + "/Models/SurgeArcTank.fbx";
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
                ImportCardSprite(CardSpritePath);
                string bikePrefabPath = CreateSkirmishBikeVisual();
                ImportCardSprite(BikeCardSpritePath);
                string colossusPrefabPath = CreateColossusVisual();
                ImportCardSprite(ColossusCardSpritePath);
                string ironcladPrefabPath = CreateIroncladVisual();
                string surgeArcTankPrefabPath = CreateSurgeArcTankVisual();
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string outputPath = Path.GetFullPath(Path.Combine(projectRoot, BundleOutputPath));
                Directory.CreateDirectory(outputPath);

                var build = new AssetBundleBuild
                {
                    assetBundleName = BundleName,
                    assetNames = new[]
                    {
                        prefabPath, CardSpritePath,
                        bikePrefabPath, BikeCardSpritePath,
                        colossusPrefabPath, ColossusCardSpritePath,
                        ironcladPrefabPath, surgeArcTankPrefabPath
                    }
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
                Debug.Log("Built script-free Sandlance, Skirmish Bike, Colossus, Ironclad, and Surge Arc Tank " +
                    "visual overlays in " + BundleName + " at " + packBundles + ". Ironclad and Surge Arc Tank " +
                    "retain their native cards, and all units retain their native gameplay prefabs.");
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

        private static string CreateSkirmishBikeVisual()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, AssetRoot)));
            AssetDatabase.Refresh();

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BikeModelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException("Import the authored Skirmish Strike Bike FBX before building the " +
                    "sample bundle.", BikeModelPath);
            }

            Dictionary<string, Material> materials = CreateSkirmishBikeMaterials();
            var root = new GameObject("SkirmishBikeVisual");
            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null)
                {
                    throw new InvalidOperationException("Unity could not instantiate the authored Skirmish Strike Bike.");
                }

                modelInstance.name = "SkirmishStrikeBikeModel";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;
                AssignSkirmishBikeMaterials(modelInstance, materials);

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, BikePrefabPath, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Unity could not save " + BikePrefabPath + ".");
                }

                NormalizeEmptyUnityYamlStrings(Path.GetFullPath(Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName, BikePrefabPath)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetImporter importer = AssetImporter.GetAtPath(BikePrefabPath);
            if (importer == null)
            {
                throw new FileNotFoundException("Unity did not import " + BikePrefabPath + ".");
            }

            importer.assetBundleName = BundleName;
            importer.assetBundleVariant = String.Empty;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            NormalizeGeneratedUnityMetaFiles(projectRoot);
            return BikePrefabPath;
        }

        private static string CreateColossusVisual()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, AssetRoot)));
            AssetDatabase.Refresh();

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ColossusModelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException("Import the authored Colossus Siege Platform FBX before building the " +
                    "sample bundle.", ColossusModelPath);
            }

            Dictionary<string, Material> materials = CreateColossusMaterials();
            var root = new GameObject("ColossusSiegeVisual");
            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null)
                {
                    throw new InvalidOperationException("Unity could not instantiate the authored Colossus Siege Platform.");
                }

                modelInstance.name = "ColossusSiegePlatformModel";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;
                AssignColossusMaterials(modelInstance, materials);

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, ColossusPrefabPath, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Unity could not save " + ColossusPrefabPath + ".");
                }

                string prefabPath = Path.GetFullPath(Path.Combine(projectRoot, ColossusPrefabPath));
                NormalizeEmptyUnityYamlStrings(prefabPath);
                string prefabMetadataPath = prefabPath + ".meta";
                if (File.Exists(prefabMetadataPath))
                {
                    NormalizeEmptyUnityYamlStrings(prefabMetadataPath);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetImporter importer = AssetImporter.GetAtPath(ColossusPrefabPath);
            if (importer == null)
            {
                throw new FileNotFoundException("Unity did not import " + ColossusPrefabPath + ".");
            }

            importer.assetBundleName = BundleName;
            importer.assetBundleVariant = String.Empty;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            NormalizeGeneratedUnityMetaFiles(projectRoot);
            return ColossusPrefabPath;
        }

        private static string CreateIroncladVisual()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, AssetRoot)));
            AssetDatabase.Refresh();

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(IroncladModelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException("Import the authored Ironclad Walker FBX before building the " +
                    "sample bundle.", IroncladModelPath);
            }

            Dictionary<string, Material> materials = CreateIroncladMaterials();
            var root = new GameObject("IroncladWalkerVisual");
            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null)
                {
                    throw new InvalidOperationException("Unity could not instantiate the authored Ironclad Walker.");
                }

                modelInstance.name = "IroncladWalkerModel";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;
                AssignIroncladMaterials(modelInstance, materials);

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, IroncladPrefabPath, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Unity could not save " + IroncladPrefabPath + ".");
                }

                string prefabPath = Path.GetFullPath(Path.Combine(projectRoot, IroncladPrefabPath));
                NormalizeEmptyUnityYamlStrings(prefabPath);
                string prefabMetadataPath = prefabPath + ".meta";
                if (File.Exists(prefabMetadataPath))
                {
                    NormalizeEmptyUnityYamlStrings(prefabMetadataPath);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetImporter importer = AssetImporter.GetAtPath(IroncladPrefabPath);
            if (importer == null)
            {
                throw new FileNotFoundException("Unity did not import " + IroncladPrefabPath + ".");
            }

            importer.assetBundleName = BundleName;
            importer.assetBundleVariant = String.Empty;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            NormalizeGeneratedUnityMetaFiles(projectRoot);
            NormalizeEmptyUnityYamlStrings(Path.GetFullPath(Path.Combine(projectRoot, IroncladPrefabPath)) + ".meta");
            return IroncladPrefabPath;
        }

        private static string CreateSurgeArcTankVisual()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, AssetRoot)));
            AssetDatabase.Refresh();

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SurgeArcTankModelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException("Import the authored Surge Arc Tank FBX before building the " +
                    "sample bundle.", SurgeArcTankModelPath);
            }

            Dictionary<string, Material> materials = CreateSurgeArcTankMaterials();
            var root = new GameObject("SurgeArcTankVisual");
            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null)
                {
                    throw new InvalidOperationException("Unity could not instantiate the authored Surge Arc Tank.");
                }

                modelInstance.name = "SurgeArcTankModel";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;
                AssignSurgeArcTankMaterials(modelInstance, materials);

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, SurgeArcTankPrefabPath, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Unity could not save " + SurgeArcTankPrefabPath + ".");
                }

                string prefabPath = Path.GetFullPath(Path.Combine(projectRoot, SurgeArcTankPrefabPath));
                NormalizeEmptyUnityYamlStrings(prefabPath);
                string prefabMetadataPath = prefabPath + ".meta";
                if (File.Exists(prefabMetadataPath))
                {
                    NormalizeEmptyUnityYamlStrings(prefabMetadataPath);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetImporter importer = AssetImporter.GetAtPath(SurgeArcTankPrefabPath);
            if (importer == null)
            {
                throw new FileNotFoundException("Unity did not import " + SurgeArcTankPrefabPath + ".");
            }

            importer.assetBundleName = BundleName;
            importer.assetBundleVariant = String.Empty;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            NormalizeGeneratedUnityMetaFiles(projectRoot);
            NormalizeEmptyUnityYamlStrings(Path.GetFullPath(Path.Combine(projectRoot, SurgeArcTankPrefabPath)) + ".meta");
            return SurgeArcTankPrefabPath;
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
                ValidateVisualBundleAssets(bundle, assetNames, "scouttankvisual.prefab", "ScoutTankVisual",
                    "sandlancescoutcard.png", "SandlanceScoutCard", 7, "Sandlance Scout");
                ValidateVisualBundleAssets(bundle, assetNames, "skirmishbikevisual.prefab", "SkirmishBikeVisual",
                    "skirmishbikecard.png", "SkirmishBikeCard", 7, "Skirmish Strike Bike");
                ValidateVisualBundleAssets(bundle, assetNames, "colossussiegevisual.prefab", "ColossusSiegeVisual",
                    "colossussiegecard.png", "ColossusSiegeCard", 8, "Colossus Siege Platform");
                ValidateVisualBundleAssets(bundle, assetNames, "ironcladwalkervisual.prefab", "IroncladWalkerVisual",
                    null, null, 5, "Ironclad Walker");
                ValidateVisualBundleAssets(bundle, assetNames, "surgearctankvisual.prefab", "SurgeArcTankVisual",
                    null, null, 6, "Surge Arc Tank");
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        private static void ValidateVisualBundleAssets(
            AssetBundle bundle,
            string[] assetNames,
            string prefabSuffix,
            string prefabName,
            string cardSuffix,
            string cardName,
            int minimumMeshFilters,
            string visualLabel)
        {
            string prefabAssetPath = FindUniqueAssetPath(assetNames, prefabSuffix, visualLabel + " prefab");
            GameObject prefab = bundle.LoadAsset<GameObject>(prefabAssetPath);
            if (prefab == null || !String.Equals(prefab.name, prefabName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(visualLabel + " prefab could not be loaded by its AssetBundle path.");
            }

            bool requiresCard = !String.IsNullOrEmpty(cardSuffix) || !String.IsNullOrEmpty(cardName);
            if (requiresCard && (String.IsNullOrEmpty(cardSuffix) || String.IsNullOrEmpty(cardName)))
            {
                throw new InvalidDataException(visualLabel + " card validation requires both an asset suffix and a name.");
            }

            if (requiresCard)
            {
                string cardSpriteAssetPath = FindUniqueAssetPath(
                    assetNames, cardSuffix, visualLabel + " card Sprite");
                Sprite cardSprite = bundle.LoadAsset<Sprite>(cardSpriteAssetPath);
                if (cardSprite == null || !String.Equals(cardSprite.name, cardName, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(visualLabel + " card Sprite is missing or has the wrong asset name.");
                }
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidDataException(visualLabel + " prefab has no renderers.");
            }

            MeshFilter[] meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (meshFilters.Length < minimumMeshFilters)
            {
                throw new InvalidDataException(visualLabel + " model is missing expected independent mesh parts. " +
                    "Expected at least " + minimumMeshFilters + " MeshFilters, found " + meshFilters.Length + ".");
            }

            for (int i = 0; i < meshFilters.Length; i++)
            {
                if (meshFilters[i] == null || meshFilters[i].sharedMesh == null ||
                    meshFilters[i].sharedMesh.vertexCount < 3)
                {
                    throw new InvalidDataException(visualLabel + " visual contains an empty mesh.");
                }
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] assignedMaterials = renderers[i].sharedMaterials;
                if (assignedMaterials == null || assignedMaterials.Length == 0)
                {
                    throw new InvalidDataException(visualLabel + " renderer '" + renderers[i].name +
                        "' has no material.");
                }

                for (int materialIndex = 0; materialIndex < assignedMaterials.Length; materialIndex++)
                {
                    if (assignedMaterials[materialIndex] == null)
                    {
                        throw new InvalidDataException(visualLabel + " renderer '" + renderers[i].name +
                            "' has a missing material.");
                    }
                }
            }

            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null || components[i] is MonoBehaviour)
                {
                    throw new InvalidDataException(visualLabel +
                        " visual bundle contains a missing or scripted component.");
                }
            }
        }

        private static string FindUniqueAssetPath(string[] assetNames, string suffix, string description)
        {
            string result = null;
            for (int i = 0; i < assetNames.Length; i++)
            {
                if (!assetNames[i].EndsWith("/" + suffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (result != null)
                {
                    throw new InvalidDataException("The AssetBundle contains multiple entries for " + description + ".");
                }

                result = assetNames[i];
            }

            if (result == null)
            {
                throw new InvalidDataException("The AssetBundle does not contain " + description + ".");
            }

            return result;
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
                { "amber", CreateOrUpdateMaterial("ScoutSignalAmber", new Color32(232, 108, 32, 255), 0.18f, 0.64f, new Color(0.95f, 0.35f, 0.08f) * 1.5f) },
                { "sensor", CreateOrUpdateMaterial("ScoutSensorGlass", new Color32(31, 205, 191, 255), 0.20f, 0.76f, new Color(0.12f, 0.85f, 0.78f) * 1.8f) }
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return materials;
        }

        private static Dictionary<string, Material> CreateSkirmishBikeMaterials()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, MaterialRoot)));
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal)
            {
                { "armor", CreateOrUpdateMaterial("StrikeBikeArmor", new Color32(20, 48, 70, 255), 0.46f, 0.66f) },
                { "lightArmor", CreateOrUpdateMaterial("StrikeBikeLightArmor", new Color32(57, 91, 111, 255), 0.34f, 0.61f) },
                { "rubber", CreateOrUpdateMaterial("StrikeBikeTire", new Color32(18, 23, 28, 255), 0.08f, 0.28f) },
                { "wheel", CreateOrUpdateMaterial("StrikeBikeWheel", new Color32(49, 70, 84, 255), 0.62f, 0.62f) },
                { "gunmetal", CreateOrUpdateMaterial("StrikeBikeGunmetal", new Color32(27, 38, 49, 255), 0.76f, 0.70f) },
                { "warning", CreateOrUpdateMaterial("StrikeBikeWarningOrange", new Color32(245, 69, 12, 255), 0.16f, 0.64f, new Color(0.98f, 0.28f, 0.05f) * 1.5f) },
                { "arc", CreateOrUpdateMaterial("StrikeBikeArcCyan", new Color32(16, 173, 235, 255), 0.22f, 0.80f, new Color(0.06f, 0.68f, 0.95f) * 1.8f) }
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return materials;
        }

        private static Dictionary<string, Material> CreateColossusMaterials()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, MaterialRoot)));
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal)
            {
                { "armor", CreateOrUpdateMaterial("ColossusArmor", new Color32(31, 48, 61, 255), 0.42f, 0.58f) },
                { "lightArmor", CreateOrUpdateMaterial("ColossusLightArmor", new Color32(56, 82, 97, 255), 0.34f, 0.60f) },
                { "gunmetal", CreateOrUpdateMaterial("ColossusGunmetal", new Color32(18, 26, 31, 255), 0.78f, 0.70f) },
                { "tread", CreateOrUpdateMaterial("ColossusTread", new Color32(11, 14, 15, 255), 0.18f, 0.30f) },
                { "wheelRim", CreateOrUpdateMaterial("ColossusWheelRim", new Color32(41, 56, 66, 255), 0.62f, 0.60f) },
                { "wheelHub", CreateOrUpdateMaterial("ColossusWheelHub", new Color32(26, 38, 46, 255), 0.55f, 0.50f) },
                { "amber", CreateOrUpdateMaterial("ColossusSignalAmber", new Color32(242, 82, 13, 255), 0.18f, 0.64f, new Color(0.95f, 0.32f, 0.05f) * 1.5f) },
                { "sensor", CreateOrUpdateMaterial("ColossusSensorCyan", new Color32(10, 184, 224, 255), 0.20f, 0.78f, new Color(0.04f, 0.72f, 0.88f) * 1.8f) }
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return materials;
        }

        private static Dictionary<string, Material> CreateIroncladMaterials()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, MaterialRoot)));
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal)
            {
                { "armor", CreateOrUpdateMaterial("IroncladArmor", new Color32(34, 58, 72, 255), 0.44f, 0.62f) },
                { "lightArmor", CreateOrUpdateMaterial("IroncladLightArmor", new Color32(71, 102, 113, 255), 0.34f, 0.60f) },
                { "gunmetal", CreateOrUpdateMaterial("IroncladGunmetal", new Color32(18, 27, 34, 255), 0.78f, 0.70f) },
                { "warning", CreateOrUpdateMaterial("IroncladWarningAmber", new Color32(239, 104, 24, 255), 0.18f, 0.64f, new Color(0.94f, 0.30f, 0.06f) * 1.5f) },
                { "sensor", CreateOrUpdateMaterial("IroncladSensorCyan", new Color32(10, 184, 224, 255), 0.20f, 0.78f, new Color(0.04f, 0.72f, 0.88f) * 1.8f) }
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return materials;
        }

        private static Dictionary<string, Material> CreateSurgeArcTankMaterials()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, MaterialRoot)));
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal)
            {
                { "armor", CreateOrUpdateMaterial("SurgeArcTankArmor", new Color32(20, 36, 59, 255), 0.55f, 0.68f) },
                { "lightArmor", CreateOrUpdateMaterial("SurgeArcTankLightArmor", new Color32(82, 96, 112, 255), 0.60f, 0.72f) },
                { "gunmetal", CreateOrUpdateMaterial("SurgeArcTankGunmetal", new Color32(18, 22, 28, 255), 0.78f, 0.75f) },
                { "tread", CreateOrUpdateMaterial("SurgeArcTankTread", new Color32(11, 15, 20, 255), 0.12f, 0.32f) },
                { "magenta", CreateOrUpdateMaterial("SurgeArcTankWarningMagenta", new Color32(212, 24, 118, 255), 0.20f, 0.70f) },
                { "arc", CreateOrUpdateMaterial("SurgeArcTankArcCyan", new Color32(0, 240, 255, 255), 0.15f, 0.88f, new Color(0.0f, 0.94f, 1.0f) * 2.2f) }
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return materials;
        }

        private static void AssignSkirmishBikeMaterials(
            GameObject model,
            IDictionary<string, Material> materials)
        {
            AssignBatchedMaterials(model, "StrikeBike", materials);
        }

        private static void AssignColossusMaterials(
            GameObject model,
            IDictionary<string, Material> materials)
        {
            AssignBatchedMaterials(model, "Colossus", materials);
        }

        private static void AssignIroncladMaterials(
            GameObject model,
            IDictionary<string, Material> materials)
        {
            AssignBatchedMaterials(model, "IroncladWalker", materials);
        }

        private static void AssignSurgeArcTankMaterials(
            GameObject model,
            IDictionary<string, Material> materials)
        {
            AssignBatchedMaterials(model, "SurgeArcTank", materials);
        }

        private static void AssignBatchedMaterials(
            GameObject model,
            string modelPrefix,
            IDictionary<string, Material> materials)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidDataException("The authored " + modelPrefix + " FBX contains no renderers.");
            }

            string batchPrefix = modelPrefix + "_Batch_";
            for (int i = 0; i < renderers.Length; i++)
            {
                string rendererName = renderers[i].gameObject.name;
                if (!rendererName.StartsWith(batchPrefix, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Unexpected mesh batch name '" + rendererName + "' for " + modelPrefix + ".");
                }

                string materialKey = rendererName.Substring(batchPrefix.Length);
                Material material;
                if (!materials.TryGetValue(materialKey, out material) || material == null)
                {
                    throw new InvalidDataException("No authored material mapping exists for model batch '" +
                        rendererName + "'.");
                }

                renderers[i].sharedMaterials = new[] { material };
            }
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

        private static void ImportCardSprite(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException(
                    "Add the ImageGen-authored card PNG before building the visual bundle.", absolutePath);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidDataException("Unity did not create a TextureImporter for " + assetPath + ".");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 128;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.assetBundleName = BundleName;
            importer.assetBundleVariant = String.Empty;
            importer.SaveAndReimport();

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null || sprite.texture.width != 128 || sprite.texture.height != 128)
            {
                throw new InvalidDataException("Unity failed to import a 128x128 card Sprite from " + assetPath + ".");
            }

            string metadataPath = absolutePath + ".meta";
            if (File.Exists(metadataPath))
            {
                NormalizeEmptyUnityYamlStrings(metadataPath);
            }
        }

        private static void NormalizeEmptyUnityYamlStrings(string filePath)
        {
            string contents = File.ReadAllText(filePath);
            string normalized = contents;
            if (String.Equals(Path.GetExtension(filePath), ".meta", StringComparison.OrdinalIgnoreCase))
            {
                normalized = Regex.Replace(normalized, @"(?m)[ \t]+(?=\r?$)", String.Empty);
            }

            normalized = Regex.Replace(normalized,
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
            float smoothness,
            Color? emissionColor = null)
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
            material.enableInstancing = true;

            if (emissionColor.HasValue && emissionColor.Value != Color.black)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emissionColor.Value);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void AssignScoutMaterials(GameObject model, IDictionary<string, Material> materials)
        {
            AssignBatchedMaterials(model, "Scout", materials);
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
