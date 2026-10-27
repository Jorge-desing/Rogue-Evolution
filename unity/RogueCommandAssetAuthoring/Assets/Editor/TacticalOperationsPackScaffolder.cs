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

        public const string TidebreakerModelPath = "Assets/RogueCommandExpansionSample/Models/TidebreakerHovercraft.fbx";
        public const string SalvageHaulerModelPath = "Assets/RogueCommandExpansionSample/Models/SalvageHauler.fbx";
        public const string SkyguardSAMModelPath = "Assets/RogueCommandExpansionSample/Models/SkyguardSAM.fbx";
        public const string BulwarkSiegeTankModelPath = "Assets/RogueCommandExpansionSample/Models/BulwarkSiegeTank.fbx";
        public const string ArcLanceWalkerModelPath = "Assets/RogueCommandExpansionSample/Models/ArcLanceWalker.fbx";
        public const string FieldMedicRoverModelPath = "Assets/RogueCommandExpansionSample/Models/FieldMedicRover.fbx";

        public const string TidebreakerCardPath = RootFolder + "/TidebreakerHovercraftCard.png";
        public const string SalvageHaulerCardPath = RootFolder + "/SalvageHaulerCard.png";
        public const string SkyguardSAMCardPath = RootFolder + "/SkyguardSAMCard.png";

        public const string TidebreakerPrefabPath = PrefabsFolder + "/TidebreakerHovercraftVisual.prefab";
        public const string SalvageHaulerPrefabPath = PrefabsFolder + "/SalvageHaulerVisual.prefab";
        public const string SkyguardSAMPrefabPath = PrefabsFolder + "/SkyguardSAMVisual.prefab";

        // New units (reuse existing models as visual overlays)
        public const string BulwarkSiegeTankCardPath = RootFolder + "/bulwarksiegetankcard.png";
        public const string ArcLanceWalkerCardPath = RootFolder + "/arclancewalkercard.png";
        public const string FieldMedicRoverCardPath = RootFolder + "/fieldmedicrovercard.png";

        public const string BulwarkSiegeTankPrefabPath = PrefabsFolder + "/BulwarkSiegeTankVisual.prefab";
        public const string ArcLanceWalkerPrefabPath = PrefabsFolder + "/ArcLanceWalkerVisual.prefab";
        public const string FieldMedicRoverPrefabPath = PrefabsFolder + "/FieldMedicRoverVisual.prefab";

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
                ConfigureRiggedWalkerModel(ArcLanceWalkerModelPath);
                                ConfigureRiggedWalkerModel(BulwarkSiegeTankModelPath);
                                ConfigureRiggedWalkerModel(FieldMedicRoverModelPath);
                                ConfigureRiggedWalkerModel(TidebreakerModelPath);
                ConfigureRiggedWalkerModel(SalvageHaulerModelPath);
                ConfigureRiggedWalkerModel(SkyguardSAMModelPath);

                // 1. Configure and import card sprites
                ConfigureCardSprite(TidebreakerCardPath);
                ConfigureCardSprite(SalvageHaulerCardPath);
                ConfigureCardSprite(SkyguardSAMCardPath);
                ConfigureCardSprite(BulwarkSiegeTankCardPath);
                ConfigureCardSprite(ArcLanceWalkerCardPath);
                ConfigureCardSprite(FieldMedicRoverCardPath);

                // 2. Build Prefabs
                string p1 = CreateTidebreakerHovercraftVisual();
                string p2 = CreateSalvageHaulerVisual();
                string p3 = CreateSkyguardSAMVisual();
                string p4 = CreateBulwarkSiegeTankVisual();
                string p5 = CreateArcLanceWalkerVisual();
                string p6 = CreateFieldMedicRoverVisual();

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
                        p3, SkyguardSAMCardPath,
                        p4, BulwarkSiegeTankCardPath,
                        p5, ArcLanceWalkerCardPath,
                        p6, FieldMedicRoverCardPath,
                        MaterialsFolder + "/BulwarkSiegeTank_PBR.mat",
                        MaterialsFolder + "/ArcLanceWalker_PBR.mat",
                        MaterialsFolder + "/FieldMedicRover_PBR.mat"
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

        private static void ConfigureRiggedWalkerModel(string modelPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
                importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            }
            if (importer == null)
            {
                throw new InvalidOperationException("Arc Lance Walker source must be a ModelImporter: " + modelPath);
            }

            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                changed = true;
            }
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                changed = true;
            }
            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                changed = true;
            }
            if (importer.optimizeGameObjects)
            {
                importer.optimizeGameObjects = false;
                changed = true;
            }

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }
            bool hasClipSettings = false;
            if (clips != null)
            {
                foreach (ModelImporterClipAnimation clip in clips)
                {
                    if (clip == null) continue;
                    bool shouldLoop = clip.name.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        clip.name.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        clip.name.IndexOf("Drive", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (clip.loopTime != shouldLoop)
                    {
                        clip.loopTime = shouldLoop;
                        changed = true;
                    }
                    if (clip.loopPose != shouldLoop)
                    {
                        clip.loopPose = shouldLoop;
                        changed = true;
                    }
                    hasClipSettings = true;
                }
            }
            if (hasClipSettings)
            {
                importer.clipAnimations = clips;
            }
            if (changed)
            {
                importer.SaveAndReimport();
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
                Animator[] importedModelAnimators = modelInstance.GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < importedModelAnimators.Length; i++)
                {
                    if (importedModelAnimators[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(importedModelAnimators[i]);
                    }
                }
                VehiclePrefabBuilder.SetupAnimatorIfAvailable(
                    root, modelInstance, modelAsset, "TidebreakerHovercraft", preferWalkForMovement: true);
                Animator tidebreakerAnimator = root.GetComponent<Animator>();
                if (tidebreakerAnimator != null && tidebreakerAnimator.avatar != null && tidebreakerAnimator.avatar.isValid &&
                    tidebreakerAnimator.runtimeAnimatorController != null)
                {
                    tidebreakerAnimator.applyRootMotion = false;
                }

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
                Animator[] importedModelAnimators = modelInstance.GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < importedModelAnimators.Length; i++)
                {
                    if (importedModelAnimators[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(importedModelAnimators[i]);
                    }
                }
                VehiclePrefabBuilder.SetupAnimatorIfAvailable(
                    root, modelInstance, modelAsset, "SalvageHauler", preferWalkForMovement: true);
                Animator salvageAnimator = root.GetComponent<Animator>();
                if (salvageAnimator != null && salvageAnimator.avatar != null && salvageAnimator.avatar.isValid &&
                    salvageAnimator.runtimeAnimatorController != null)
                {
                    salvageAnimator.applyRootMotion = false;
                }

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
                Animator[] importedModelAnimators = modelInstance.GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < importedModelAnimators.Length; i++)
                {
                    if (importedModelAnimators[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(importedModelAnimators[i]);
                    }
                }
                VehiclePrefabBuilder.SetupAnimatorIfAvailable(
                    root, modelInstance, modelAsset, "SkyguardSAM", preferWalkForMovement: true);
                Animator skyguardAnimator = root.GetComponent<Animator>();
                if (skyguardAnimator != null && skyguardAnimator.avatar != null && skyguardAnimator.avatar.isValid &&
                    skyguardAnimator.runtimeAnimatorController != null)
                {
                    skyguardAnimator.applyRootMotion = false;
                }

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

        private static string CreateBulwarkSiegeTankVisual()
        {
            Material pbrMat = CreatePbrMaterial("BulwarkSiegeTank");

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BulwarkSiegeTankModelPath);
            if (modelAsset == null) throw new FileNotFoundException("Model not found: " + BulwarkSiegeTankModelPath);

            var root = new GameObject("BulwarkSiegeTankVisual");
            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null) modelInstance = UnityEngine.Object.Instantiate(modelAsset);
                if (modelInstance == null) throw new InvalidOperationException("BulwarkSiegeTank model instance is null after instantiation");
                modelInstance.name = "BulwarkSiegeTankModel";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;

                if (pbrMat == null) throw new InvalidOperationException("BulwarkSiegeTank PBR material is null");

                Renderer[] __bulwarkRenderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                bool __bulwarkHasMesh = false;
                foreach (Renderer __r in __bulwarkRenderers)
                {
                    if (__r is MeshRenderer __mr)
                    {
                        MeshFilter __mf = __mr.GetComponent<MeshFilter>();
                        if (__mf != null && __mf.sharedMesh != null) { __bulwarkHasMesh = true; break; }
                    }
                    else if (__r is SkinnedMeshRenderer __smr && __smr.sharedMesh != null)
                    {
                        __bulwarkHasMesh = true; break;
                    }
                }
                if (!__bulwarkHasMesh) throw new InvalidOperationException("BulwarkSiegeTank model has no mesh-bearing renderers");

                if (pbrMat != null)
                {
                    Renderer[] allRenderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in allRenderers)
                    {
                        // Only assign PBR material to mesh renderers, preserve VFX/particle materials
                        if (r is MeshRenderer || r is SkinnedMeshRenderer)
                        {
                            int count = r.sharedMaterials != null && r.sharedMaterials.Length > 0 ? r.sharedMaterials.Length : 1;
                            var mats = new Material[count];
                            for (int i = 0; i < count; i++) mats[i] = pbrMat;
                            r.sharedMaterials = mats;
                        }
                    }
                }

                FitBoxCollider(root, modelInstance);
                VehiclePrefabBuilder.EnsureCanonicalVfxSockets(root, modelInstance);
                VehiclePrefabBuilder.EnsureCanonicalVfxAndAudioComponents(root);
                Animator[] importedModelAnimators = modelInstance.GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < importedModelAnimators.Length; i++)
                {
                    if (importedModelAnimators[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(importedModelAnimators[i]);
                    }
                }
                VehiclePrefabBuilder.SetupAnimatorIfAvailable(
                    root, modelInstance, modelAsset, "BulwarkSiegeTank", preferWalkForMovement: true);
                Animator bulwarkAnimator = root.GetComponent<Animator>();
                if (bulwarkAnimator != null && bulwarkAnimator.avatar != null && bulwarkAnimator.avatar.isValid &&
                    bulwarkAnimator.runtimeAnimatorController != null)
                {
                    bulwarkAnimator.applyRootMotion = false;
                }

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, BulwarkSiegeTankPrefabPath, out success);
                if (!success) throw new InvalidOperationException("Could not save prefab to " + BulwarkSiegeTankPrefabPath);
                return BulwarkSiegeTankPrefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static string CreateArcLanceWalkerVisual()
        {
            Material pbrMat = CreatePbrMaterial("ArcLanceWalker");

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ArcLanceWalkerModelPath);
            if (modelAsset == null) throw new FileNotFoundException("Model not found: " + ArcLanceWalkerModelPath);

            var root = new GameObject("ArcLanceWalkerVisual");
            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null) modelInstance = UnityEngine.Object.Instantiate(modelAsset);
                if (modelInstance == null) throw new InvalidOperationException("ArcLanceWalker model instance is null after instantiation");
                modelInstance.name = "ArcLanceWalkerModel";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;

                if (pbrMat == null) throw new InvalidOperationException("ArcLanceWalker PBR material is null");

                Renderer[] __arcRenderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                bool __arcHasMesh = false;
                foreach (Renderer __r in __arcRenderers)
                {
                    if (__r is MeshRenderer __mr)
                    {
                        MeshFilter __mf = __mr.GetComponent<MeshFilter>();
                        if (__mf != null && __mf.sharedMesh != null) { __arcHasMesh = true; break; }
                    }
                    else if (__r is SkinnedMeshRenderer __smr && __smr.sharedMesh != null)
                    {
                        __arcHasMesh = true; break;
                    }
                }
                if (!__arcHasMesh) throw new InvalidOperationException("ArcLanceWalker model has no mesh-bearing renderers");

                if (pbrMat != null)
                {
                    Renderer[] allRenderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in allRenderers)
                    {
                        // Only assign PBR material to mesh renderers, preserve VFX/particle materials
                        if (r is MeshRenderer || r is SkinnedMeshRenderer)
                        {
                            int count = r.sharedMaterials != null && r.sharedMaterials.Length > 0 ? r.sharedMaterials.Length : 1;
                            var mats = new Material[count];
                            for (int i = 0; i < count; i++) mats[i] = pbrMat;
                            r.sharedMaterials = mats;
                        }
                    }
                }

                FitBoxCollider(root, modelInstance);
                VehiclePrefabBuilder.EnsureCanonicalVfxSockets(root, modelInstance);
                RogueCommandVisualSampleScaffolder.EnsureCanonicalWalkerSockets(root, modelInstance, false);
                VehiclePrefabBuilder.EnsureCanonicalVfxAndAudioComponents(root);
                Animator[] importedModelAnimators = modelInstance.GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < importedModelAnimators.Length; i++)
                {
                    if (importedModelAnimators[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(importedModelAnimators[i]);
                    }
                }
                VehiclePrefabBuilder.SetupAnimatorIfAvailable(
                    root, modelInstance, modelAsset, "ArcLanceWalker", preferWalkForMovement: true);
                Animator walkerAnimator = root.GetComponent<Animator>();
                if (walkerAnimator == null || walkerAnimator.avatar == null || !walkerAnimator.avatar.isValid ||
                    walkerAnimator.runtimeAnimatorController == null)
                {
                    throw new InvalidOperationException("ArcLanceWalker rig must produce a valid Generic Avatar and AnimatorController");
                }
                walkerAnimator.applyRootMotion = false;

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, ArcLanceWalkerPrefabPath, out success);
                if (!success) throw new InvalidOperationException("Could not save prefab to " + ArcLanceWalkerPrefabPath);
                return ArcLanceWalkerPrefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static string CreateFieldMedicRoverVisual()
        {
            Material pbrMat = CreatePbrMaterial("FieldMedicRover");

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FieldMedicRoverModelPath);
            if (modelAsset == null) throw new FileNotFoundException("Model not found: " + FieldMedicRoverModelPath);

            var root = new GameObject("FieldMedicRoverVisual");
            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null) modelInstance = UnityEngine.Object.Instantiate(modelAsset);
                if (modelInstance == null) throw new InvalidOperationException("FieldMedicRover model instance is null after instantiation");
                modelInstance.name = "FieldMedicRoverModel";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;

                if (pbrMat == null) throw new InvalidOperationException("FieldMedicRover PBR material is null");

                Renderer[] __medicRenderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                bool __medicHasMesh = false;
                foreach (Renderer __r in __medicRenderers)
                {
                    if (__r is MeshRenderer __mr)
                    {
                        MeshFilter __mf = __mr.GetComponent<MeshFilter>();
                        if (__mf != null && __mf.sharedMesh != null) { __medicHasMesh = true; break; }
                    }
                    else if (__r is SkinnedMeshRenderer __smr && __smr.sharedMesh != null)
                    {
                        __medicHasMesh = true; break;
                    }
                }
                if (!__medicHasMesh) throw new InvalidOperationException("FieldMedicRover model has no mesh-bearing renderers");

                if (pbrMat != null)
                {
                    Renderer[] allRenderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in allRenderers)
                    {
                        // Only assign PBR material to mesh renderers, preserve VFX/particle materials
                        if (r is MeshRenderer || r is SkinnedMeshRenderer)
                        {
                            int count = r.sharedMaterials != null && r.sharedMaterials.Length > 0 ? r.sharedMaterials.Length : 1;
                            var mats = new Material[count];
                            for (int i = 0; i < count; i++) mats[i] = pbrMat;
                            r.sharedMaterials = mats;
                        }
                    }
                }

                FitBoxCollider(root, modelInstance);
                VehiclePrefabBuilder.EnsureCanonicalVfxSockets(root, modelInstance);
                VehiclePrefabBuilder.EnsureCanonicalVfxAndAudioComponents(root);
                Animator[] importedModelAnimators = modelInstance.GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < importedModelAnimators.Length; i++)
                {
                    if (importedModelAnimators[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(importedModelAnimators[i]);
                    }
                }
                VehiclePrefabBuilder.SetupAnimatorIfAvailable(
                    root, modelInstance, modelAsset, "FieldMedicRover", preferWalkForMovement: true);
                Animator medicAnimator = root.GetComponent<Animator>();
                if (medicAnimator != null && medicAnimator.avatar != null && medicAnimator.avatar.isValid &&
                    medicAnimator.runtimeAnimatorController != null)
                {
                    medicAnimator.applyRootMotion = false;
                }

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, FieldMedicRoverPrefabPath, out success);
                if (!success) throw new InvalidOperationException("Could not save prefab to " + FieldMedicRoverPrefabPath);
                return FieldMedicRoverPrefabPath;
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

        private static Material CreatePbrMaterial(string unitName)
        {
            Shader shader = Shader.Find("RogueCommand/VehiclePBR_VertexWear");
            if (shader == null)
                throw new InvalidOperationException("Shader 'RogueCommand/VehiclePBR_VertexWear' not found. Ensure the shader is in the project.");

            string texBase = "Assets/RogueCommandExpansionSample/Models/Textures/" + unitName;
            ConfigureTextureImporter(texBase + "_Albedo.png", false);
            ConfigureTextureImporter(texBase + "_Emission.png", false);
            ConfigureTextureImporter(texBase + "_MetallicGloss.png", true);
            ConfigureTextureImporter(texBase + "_Normal.png", false, true);

            AssetDatabase.ImportAsset(texBase + "_Albedo.png", ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(texBase + "_MetallicGloss.png", ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(texBase + "_Normal.png", ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(texBase + "_Emission.png", ImportAssetOptions.ForceUpdate);

            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(texBase + "_Albedo.png");
            Texture2D metallicGloss = AssetDatabase.LoadAssetAtPath<Texture2D>(texBase + "_MetallicGloss.png");
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(texBase + "_Normal.png");
            Texture2D emission = AssetDatabase.LoadAssetAtPath<Texture2D>(texBase + "_Emission.png");

            if (albedo == null || metallicGloss == null || normal == null || emission == null)
                throw new FileNotFoundException("One or more PBR textures missing for " + unitName);

            string matPath = MaterialsFolder + "/" + unitName + "_PBR.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                mat.name = unitName + "_PBR";
                AssetDatabase.CreateAsset(mat, matPath);
            }

            mat.shader = shader;
            mat.SetTexture("_BaseMap", albedo);
            mat.SetTexture("_MetallicGlossMap", metallicGloss);
            mat.SetTexture("_BumpMap", normal);
            mat.SetTexture("_EmissionMap", emission);

            mat.SetFloat("_GlossMapScale", 1.0f);
            mat.SetFloat("_Metallic", 1.0f);
            mat.SetFloat("_Smoothness", 1.0f);
            mat.SetFloat("_BumpScale", 1.0f);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetColor("_EmissionColor", Color.white);
            mat.SetColor("_TeamColor", Color.white);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.EnableKeyword("_NORMALMAP");
            mat.EnableKeyword("_EMISSION");
            mat.enableInstancing = true;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void ConfigureTextureImporter(string assetPath, bool linear, bool normalMap = false)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) return;
            }

            bool changed = false;
            if (normalMap)
            {
                if (importer.textureType != TextureImporterType.NormalMap) { importer.textureType = TextureImporterType.NormalMap; changed = true; }
            }
            else
            {
                if (importer.textureType != TextureImporterType.Default) { importer.textureType = TextureImporterType.Default; changed = true; }
            }
            // Normal maps and explicitly linear textures must never be sRGB
            bool shouldBeSRGB = !linear && !normalMap;
            if (importer.sRGBTexture != shouldBeSRGB) { importer.sRGBTexture = shouldBeSRGB; changed = true; }
            if (importer.maxTextureSize != 2048) { importer.maxTextureSize = 2048; changed = true; }
            if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; changed = true; }
            if (importer.wrapMode != TextureWrapMode.Clamp) { importer.wrapMode = TextureWrapMode.Clamp; changed = true; }
            if (changed) importer.SaveAndReimport();
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
