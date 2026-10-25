using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RogueCommandExpansion.Authoring
{
    public static class TacticalOperationsPackScaffolder
    {
        private const string RootFolder = "Assets/TacticalOperations";
        private const string PrefabsFolder = RootFolder + "/Prefabs";
        private const string MaterialsFolder = RootFolder + "/Materials";

        public const string TidebreakerModelPath = "Assets/RogueCommandExpansionSample/Models/TidebreakerHovercraft_Atlas.fbx";
        public const string SalvageHaulerModelPath = "Assets/RogueCommandExpansionSample/Models/SalvageHauler.fbx";
        public const string SkyguardSAMModelPath = "Assets/RogueCommandExpansionSample/Models/SkyguardSAM.fbx";

        public const string TidebreakerCardPath = RootFolder + "/TidebreakerHovercraftCard.png";
        public const string SalvageHaulerCardPath = RootFolder + "/SalvageHaulerCard.png";
        public const string SkyguardSAMCardPath = RootFolder + "/SkyguardSAMCard.png";

        public const string TidebreakerPrefabPath = PrefabsFolder + "/TidebreakerHovercraftVisual.prefab";
        public const string SalvageHaulerPrefabPath = PrefabsFolder + "/SalvageHaulerVisual.prefab";
        public const string SkyguardSAMPrefabPath = PrefabsFolder + "/SkyguardSAMVisual.prefab";

        public const string BundleName = "tactical-operations.bundle";

        [MenuItem("Rogue Command/Tactical Operations/Build Tactical Operations Pack Assets and Bundle")]
        public static void BuildTacticalOperationsPack()
        {
            try
            {
                Debug.Log("[TacticalOperationsScaffolder] Starting build for Tactical Operations pack...");
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

                Directory.CreateDirectory(Path.Combine(Application.dataPath, "TacticalOperations"));
                Directory.CreateDirectory(Path.Combine(Application.dataPath, "TacticalOperations/Prefabs"));
                Directory.CreateDirectory(Path.Combine(Application.dataPath, "TacticalOperations/Materials"));
                AssetDatabase.Refresh();

                // 1. Configure and import card sprites
                ConfigureCardSprite(TidebreakerCardPath);
                ConfigureCardSprite(SalvageHaulerCardPath);
                ConfigureCardSprite(SkyguardSAMCardPath);

                // 2. Build Prefabs
                string p1 = CreateTidebreakerHovercraftVisual();
                string p2 = CreateSalvageHaulerVisual();
                string p3 = CreateSkyguardSAMVisual();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // 3. Build AssetBundles
                string packBundlesDir = Path.GetFullPath(Path.Combine(
                    repoRoot, "content", "packs", "tactical-operations", "bundles"));
                Directory.CreateDirectory(packBundlesDir);

                var build = new AssetBundleBuild
                {
                    assetBundleName = BundleName,
                    assetNames = new[]
                    {
                        p1, TidebreakerCardPath,
                        p2, SalvageHaulerCardPath,
                        p3, SkyguardSAMCardPath
                    }
                };

                Debug.Log("[TacticalOperationsScaffolder] Compiling StandaloneWindows64 bundle to " + packBundlesDir);
                AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                    packBundlesDir,
                    new[] { build },
                    BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                    BuildTarget.StandaloneWindows64);

                if (manifest == null)
                {
                    throw new InvalidOperationException("Unity BuildPipeline.BuildAssetBundles returned null manifest.");
                }

                // Unity names the manifest file after the directory ("bundles")
                string sourceManifestBundle = Path.Combine(packBundlesDir, "bundles");
                string targetManifestBundle = Path.Combine(packBundlesDir, "assetbundle-manifest.bundle");
                if (File.Exists(sourceManifestBundle))
                {
                    File.Copy(sourceManifestBundle, targetManifestBundle, true);
                    File.Delete(sourceManifestBundle);
                }

                string bundleFilePath = Path.Combine(packBundlesDir, BundleName);
                if (!File.Exists(bundleFilePath) || new FileInfo(bundleFilePath).Length == 0)
                {
                    throw new FileNotFoundException("Built bundle file is missing or empty: " + bundleFilePath);
                }

                if (!File.Exists(targetManifestBundle) || new FileInfo(targetManifestBundle).Length == 0)
                {
                    throw new FileNotFoundException("Built manifest bundle is missing or empty: " + targetManifestBundle);
                }

                Debug.Log($"[TacticalOperationsScaffolder] SUCCESS! Built {BundleName} ({new FileInfo(bundleFilePath).Length} bytes) and assetbundle-manifest.bundle ({new FileInfo(targetManifestBundle).Length} bytes).");
            }
            catch (Exception ex)
            {
                Debug.LogError("[TacticalOperationsScaffolder] Build failed: " + ex);
                throw;
            }
        }

        private static void ConfigureCardSprite(string assetPath)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("[TacticalOperationsScaffolder] Could not get TextureImporter for " + assetPath);
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 128;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static string CreateTidebreakerHovercraftVisual()
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TidebreakerModelPath);
            if (modelAsset == null) throw new FileNotFoundException("Model not found: " + TidebreakerModelPath);

            Material atlasMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/TidebreakerHovercraft_Atlas.mat");
            if (atlasMat == null)
            {
                atlasMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/RogueCommandExpansionSample/VisualOnly/Materials/TidebreakerHovercraft_Atlas.mat");
            }

            var root = new GameObject("TidebreakerHovercraftVisual");
            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null) modelInstance = UnityEngine.Object.Instantiate(modelAsset);
                modelInstance.name = "TidebreakerHovercraftModel";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;

                if (atlasMat != null)
                {
                    Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in renderers)
                    {
                        int count = r.sharedMaterials != null && r.sharedMaterials.Length > 0 ? r.sharedMaterials.Length : 1;
                        var mats = new Material[count];
                        for (int i = 0; i < count; i++) mats[i] = atlasMat;
                        r.sharedMaterials = mats;
                    }
                }

                FitBoxCollider(root, modelInstance);
                VehiclePrefabBuilder.EnsureCanonicalVfxSockets(root, modelInstance);
                VehiclePrefabBuilder.EnsureCanonicalVfxAndAudioComponents(root);

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, TidebreakerPrefabPath, out success);
                if (!success) throw new InvalidOperationException("Could not save prefab to " + TidebreakerPrefabPath);
                return TidebreakerPrefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static string CreateSalvageHaulerVisual()
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SalvageHaulerModelPath);
            if (modelAsset == null) throw new FileNotFoundException("Model not found: " + SalvageHaulerModelPath);

            var root = new GameObject("SalvageHaulerVisual");
            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null) modelInstance = UnityEngine.Object.Instantiate(modelAsset);
                modelInstance.name = "SalvageHaulerModel";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;

                Dictionary<string, Material> mats = LoadMaterialsByPrefix("SalvageHauler_");
                AssignMaterials(modelInstance, mats);

                FitBoxCollider(root, modelInstance);
                VehiclePrefabBuilder.EnsureCanonicalVfxSockets(root, modelInstance);
                VehiclePrefabBuilder.EnsureCanonicalVfxAndAudioComponents(root);

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, SalvageHaulerPrefabPath, out success);
                if (!success) throw new InvalidOperationException("Could not save prefab to " + SalvageHaulerPrefabPath);
                return SalvageHaulerPrefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static string CreateSkyguardSAMVisual()
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SkyguardSAMModelPath);
            if (modelAsset == null) throw new FileNotFoundException("Model not found: " + SkyguardSAMModelPath);

            var root = new GameObject("SkyguardSAMVisual");
            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null) modelInstance = UnityEngine.Object.Instantiate(modelAsset);
                modelInstance.name = "SkyguardSAMModel";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;

                Dictionary<string, Material> mats = LoadMaterialsByPrefix("SkyguardSAM_");
                AssignMaterials(modelInstance, mats);

                FitBoxCollider(root, modelInstance);
                VehiclePrefabBuilder.EnsureCanonicalVfxSockets(root, modelInstance);
                VehiclePrefabBuilder.EnsureCanonicalVfxAndAudioComponents(root);

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, SkyguardSAMPrefabPath, out success);
                if (!success) throw new InvalidOperationException("Could not save prefab to " + SkyguardSAMPrefabPath);
                return SkyguardSAMPrefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Dictionary<string, Material> LoadMaterialsByPrefix(string prefix)
        {
            var dict = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { MaterialsFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat != null) dict[name] = mat;
                }
            }
            return dict;
        }

        private static void AssignMaterials(GameObject model, Dictionary<string, Material> mats)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                Material[] shared = r.sharedMaterials;
                if (shared == null || shared.Length == 0) continue;
                for (int i = 0; i < shared.Length; i++)
                {
                    if (shared[i] != null && mats.TryGetValue(shared[i].name, out Material match))
                    {
                        shared[i] = match;
                    }
                }
                r.sharedMaterials = shared;
            }
        }

        private static void FitBoxCollider(GameObject root, GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null) bounds.Encapsulate(renderers[i].bounds);
            }

            BoxCollider collider = root.GetComponent<BoxCollider>();
            if (collider == null) collider = root.AddComponent<BoxCollider>();
            collider.center = root.transform.InverseTransformPoint(bounds.center);
            collider.size = bounds.size;
        }
    }
}
