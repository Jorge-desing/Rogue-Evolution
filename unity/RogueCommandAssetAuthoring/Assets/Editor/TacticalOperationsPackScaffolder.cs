using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
        public const string SentinelRelayModelPath = "Assets/RogueCommandExpansionSample/Models/SentinelRelay.fbx";
        public const string GravitasBreakerModelPath = "Assets/RogueCommandExpansionSample/Models/GravitasBreaker.fbx";
        public const string CommandRelayModelPath = "Assets/RogueCommandExpansionSample/Models/CommandRelay.fbx";

        public const string TidebreakerCardPath = RootFolder + "/TidebreakerHovercraftCard.png";
        public const string SalvageHaulerCardPath = RootFolder + "/SalvageHaulerCard.png";
        public const string SkyguardSAMCardPath = RootFolder + "/SkyguardSAMCard.png";
        public const string SentinelRelayCardPath = RootFolder + "/SentinelRelayCard.png";
        public const string GravitasBreakerCardPath = RootFolder + "/GravitasBreakerCard.png";
        public const string CommandRelayCardPath = RootFolder + "/CommandRelayCard.png";

        public const string TidebreakerPrefabPath = PrefabsFolder + "/TidebreakerHovercraftVisual.prefab";
        public const string SalvageHaulerPrefabPath = PrefabsFolder + "/SalvageHaulerVisual.prefab";
        public const string SkyguardSAMPrefabPath = PrefabsFolder + "/SkyguardSAMVisual.prefab";

        // Existing manually authored Tactical Operations overlays.
        public const string BulwarkSiegeTankCardPath = RootFolder + "/bulwarksiegetankcard.png";
        public const string ArcLanceWalkerCardPath = RootFolder + "/arclancewalkercard.png";
        public const string FieldMedicRoverCardPath = RootFolder + "/fieldmedicrovercard.png";

        public const string BulwarkSiegeTankPrefabPath = PrefabsFolder + "/BulwarkSiegeTankVisual.prefab";
        public const string ArcLanceWalkerPrefabPath = PrefabsFolder + "/ArcLanceWalkerVisual.prefab";
        public const string FieldMedicRoverPrefabPath = PrefabsFolder + "/FieldMedicRoverVisual.prefab";
        public const string SentinelRelayPrefabPath = PrefabsFolder + "/SentinelRelayVisual.prefab";
        public const string GravitasBreakerPrefabPath = PrefabsFolder + "/GravitasBreakerVisual.prefab";
        public const string CommandRelayPrefabPath = PrefabsFolder + "/CommandRelayVisual.prefab";

        public const string BundleName = "tactical-operations.bundle";

        [MenuItem("Rogue Command/Tactical Operations/Build Bulwark Foundry Visual and Bundle")]
        public static void BuildBulwarkFoundry()
        {
            BulwarkFoundryBuilder.Build();
        }

        [MenuItem("Rogue Command/Tactical Operations/Build Bulwark Tank Visual and Bundle")]
        public static void BuildBulwarkTank()
        {
            TacticalUnitSpec spec = Array.Find(UnitSpecs, item => item.UnitName == "BulwarkSiegeTank");
            if (spec == null) throw new InvalidDataException("Bulwark tank spec missing.");
            string prefabPath = spec.PrefabPath;
            string[] dependencies = AssetDatabase.GetDependencies(prefabPath, true);
            if (Array.IndexOf(dependencies, spec.ModelPath) < 0)
                throw new InvalidDataException("Bulwark prefab does not reference the rebuilt primary FBX.");
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Animator[] animators = root.GetComponentsInChildren<Animator>(true);
                if (animators.Length != 1 || animators[0].avatar == null ||
                    !animators[0].avatar.isValid || animators[0].runtimeAnimatorController == null)
                    throw new InvalidDataException("Existing Bulwark Animator must remain valid and unique.");
                Transform[] sockets = Array.FindAll(root.GetComponentsInChildren<Transform>(true),
                    item => item.name == "Socket_Hardpoint_ERA");
                if (sockets.Length != 1) throw new InvalidDataException("Bulwark requires one ERA socket.");
                sockets[0].localPosition = Vector3.zero;
                sockets[0].localRotation = Quaternion.identity;
                sockets[0].localScale = Vector3.one;
                bool saved;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out saved);
                if (!saved) throw new IOException("Could not save Bulwark ERA socket override.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            BulwarkFoundryBuilder.BuildExistingBundle("artifacts/bulwark-global-unlimited-build/bundles");
        }

        /// <summary>
        /// Declarative specification for one Tactical Operations visual.
        /// Unifies the previous per-unit hardcoded creation methods (Atlas for
        /// Tidebreaker, multi-material prefix lookup for Salvage/Skyguard, PBR
        /// for the rest) into a single code path with per-unit flags.
        /// All nine current units share the VehiclePBR_VertexWear PBR source
        /// texture set (Albedo/MetallicGloss/Normal/Emission) under
        /// Assets/RogueCommandExpansionSample/Models/Textures/.
        /// </summary>
        private sealed class TacticalUnitSpec
        {
            public string UnitName;
            public string ModelPath;
            public string PrefabPath;
            public string CardPath;
            /// <summary>True for bipedal walkers that need canonical walker sockets + foot contact.</summary>
            public bool IsWalker;
            /// <summary>True when the unit has a turret/radar mast/antenna that must be guaranteed.</summary>
            public bool HasTurretOrRadar;
            /// <summary>Prefer Action_Walk over Action_Drive for locomotion (walkers/hover).</summary>
            public bool PreferWalkForMovement;
            /// <summary>Throw when the final Animator/Avatar/Controller is invalid.</summary>
            public bool RequireValidAnimator;
            public string GameplayJsonPath;
        }

        private static readonly TacticalUnitSpec[] UnitSpecs = new[]
        {
            new TacticalUnitSpec
            {
                UnitName = "TidebreakerHovercraft",
                ModelPath = TidebreakerModelPath,
                PrefabPath = TidebreakerPrefabPath,
                CardPath = TidebreakerCardPath,
                IsWalker = false,
                HasTurretOrRadar = true,
                PreferWalkForMovement = true,
                RequireValidAnimator = false,
            },
            new TacticalUnitSpec
            {
                UnitName = "SalvageHauler",
                ModelPath = SalvageHaulerModelPath,
                PrefabPath = SalvageHaulerPrefabPath,
                CardPath = SalvageHaulerCardPath,
                IsWalker = false,
                HasTurretOrRadar = true,
                PreferWalkForMovement = true,
                RequireValidAnimator = false,
            },
            new TacticalUnitSpec
            {
                UnitName = "SkyguardSAM",
                ModelPath = SkyguardSAMModelPath,
                PrefabPath = SkyguardSAMPrefabPath,
                CardPath = SkyguardSAMCardPath,
                IsWalker = false,
                HasTurretOrRadar = true,
                PreferWalkForMovement = true,
                RequireValidAnimator = false,
            },
            new TacticalUnitSpec
            {
                UnitName = "BulwarkSiegeTank",
                ModelPath = BulwarkSiegeTankModelPath,
                PrefabPath = BulwarkSiegeTankPrefabPath,
                CardPath = BulwarkSiegeTankCardPath,
                IsWalker = false,
                HasTurretOrRadar = true,
                PreferWalkForMovement = true,
                RequireValidAnimator = false,
            },
            new TacticalUnitSpec
            {
                UnitName = "ArcLanceWalker",
                ModelPath = ArcLanceWalkerModelPath,
                PrefabPath = ArcLanceWalkerPrefabPath,
                CardPath = ArcLanceWalkerCardPath,
                IsWalker = true,
                HasTurretOrRadar = false,
                PreferWalkForMovement = true,
                RequireValidAnimator = true,
            },
            new TacticalUnitSpec
            {
                UnitName = "FieldMedicRover",
                ModelPath = FieldMedicRoverModelPath,
                PrefabPath = FieldMedicRoverPrefabPath,
                CardPath = FieldMedicRoverCardPath,
                IsWalker = false,
                HasTurretOrRadar = false,
                PreferWalkForMovement = true,
                RequireValidAnimator = false,
            },
            new TacticalUnitSpec
            {
                UnitName = "SentinelRelay",
                ModelPath = SentinelRelayModelPath,
                PrefabPath = SentinelRelayPrefabPath,
                CardPath = SentinelRelayCardPath,
                IsWalker = false,
                HasTurretOrRadar = true,
                PreferWalkForMovement = true,
                RequireValidAnimator = false,
            },
            new TacticalUnitSpec
            {
                UnitName = "GravitasBreaker",
                ModelPath = GravitasBreakerModelPath,
                PrefabPath = GravitasBreakerPrefabPath,
                CardPath = GravitasBreakerCardPath,
                IsWalker = false,
                HasTurretOrRadar = true,
                PreferWalkForMovement = true,
                RequireValidAnimator = false,
            },
            new TacticalUnitSpec
            {
                UnitName = "CommandRelay",
                ModelPath = CommandRelayModelPath,
                PrefabPath = CommandRelayPrefabPath,
                CardPath = CommandRelayCardPath,
                IsWalker = false,
                HasTurretOrRadar = true,
                PreferWalkForMovement = true,
                RequireValidAnimator = false,
            },
        };

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

                // 1. Configure importers for all models and cards via declarative specs.
                foreach (TacticalUnitSpec spec in UnitSpecs)
                {
                    string effectiveModelPath = ResolveEffectiveModelPath(spec);
                    ConfigureRiggedWalkerModel(effectiveModelPath, spec.IsWalker);
                    ConfigureCardSprite(spec.CardPath);
                }

                // 2. Build prefabs from specs (single unified PBR path).
                var builtPrefabs = new List<string>();
                var builtAssets = new List<string>();
                foreach (TacticalUnitSpec spec in UnitSpecs)
                {
                    string effectiveModelPath = ResolveEffectiveModelPath(spec);
                    string prefabPath = CreateVisualFromSpec(spec, effectiveModelPath);
                    builtPrefabs.Add(prefabPath);
                    builtAssets.Add(prefabPath);
                    builtAssets.Add(spec.CardPath);
                    builtAssets.Add(MaterialsFolder + "/" + spec.UnitName + "_PBR.mat");
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                string foundryPrefab = BulwarkFoundryBuilder.BuildPrefab(repoRoot);
                if (!string.IsNullOrEmpty(foundryPrefab))
                {
                    builtPrefabs.Add(foundryPrefab);
                    builtAssets.Add(foundryPrefab);
                }

                // 3. Build AssetBundles
                string packBundlesDir = Path.GetFullPath(Path.Combine(
                    repoRoot, "content", "packs", "tactical-operations", "bundles"));
                Directory.CreateDirectory(packBundlesDir);

                var build = new AssetBundleBuild
                {
                    assetBundleName = BundleName,
                    assetNames = builtAssets.ToArray()
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

                Debug.Log($"[TacticalOperationsScaffolder] SUCCESS! Built {BundleName} ({new FileInfo(bundleFilePath).Length} bytes) and assetbundle-manifest.bundle ({new FileInfo(targetManifestBundle).Length} bytes). Built {builtPrefabs.Count} prefabs.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[TacticalOperationsScaffolder] Build failed: " + ex);
                throw;
            }
        }

        /// <summary>
        /// Resolve the effective FBX to use for a spec, prioritizing _LODs over _Atlas
        /// per AGENTS.md to preserve LODGroup generation, while keeping the animated
        /// primary FBX when it carries the required Action_* clips.
        /// Falls back to AssetDatabase.FindAssets when the primary path is missing.
        /// </summary>
        private static string ResolveEffectiveModelPath(TacticalUnitSpec spec)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath) != null)
                return spec.ModelPath;

            string[] guids = AssetDatabase.FindAssets(spec.UnitName + " t:Model", new[] { "Assets/RogueCommandExpansionSample/Models" });
            if (guids != null)
            {
                string lodPath = null;
                string basePath = null;
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith("_LODs.fbx", StringComparison.OrdinalIgnoreCase))
                        lodPath = path;
                    else if (string.Equals(Path.GetFileNameWithoutExtension(path), spec.UnitName, StringComparison.OrdinalIgnoreCase))
                        basePath = path;
                }
                if (lodPath != null && basePath == null)
                    return lodPath;
                if (basePath != null)
                    return basePath;
                if (lodPath != null)
                    return lodPath;
            }
            return spec.ModelPath;
        }

        private static void ConfigureRiggedWalkerModel(string modelPath, bool isWalker = true, bool enforceIdleFireDeath = false)
        {
            ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
                importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            }
            if (importer == null)
            {
                throw new InvalidOperationException("Tactical source must be a ModelImporter: " + modelPath);
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
            bool hasIdle = false;
            bool hasMove = false;
            bool hasFire = false;
            bool hasDeath = false;
            if (clips != null)
            {
                foreach (ModelImporterClipAnimation clip in clips)
                {
                    if (clip == null) continue;
                    // Match the action token, not the unit name prefix: a walker named
                    // "ArcLanceWalker" must not make its FireRecoil/Death clips loop.
                    string clipName = clip.name;
                    int actionSeparator = clipName.LastIndexOf("Action_", StringComparison.OrdinalIgnoreCase);
                    string actionToken = actionSeparator >= 0
                        ? clipName.Substring(actionSeparator + "Action_".Length)
                        : clipName;
                    int pipeIndex = actionToken.LastIndexOf('|');
                    if (pipeIndex >= 0)
                    {
                        actionToken = actionToken.Substring(pipeIndex + 1);
                    }
                    actionToken = actionToken.Trim();
                    bool shouldLoop = actionToken.Equals("Idle", StringComparison.OrdinalIgnoreCase) ||
                        actionToken.Equals("Walk", StringComparison.OrdinalIgnoreCase) ||
                        actionToken.Equals("Drive", StringComparison.OrdinalIgnoreCase);
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
                    if (actionToken.Equals("Idle", StringComparison.OrdinalIgnoreCase)) hasIdle = true;
                    if (actionToken.Equals("Walk", StringComparison.OrdinalIgnoreCase) ||
                        actionToken.Equals("Drive", StringComparison.OrdinalIgnoreCase)) hasMove = true;
                    if (actionToken.Equals("FireRecoil", StringComparison.OrdinalIgnoreCase) ||
                        actionToken.Equals("Fire", StringComparison.OrdinalIgnoreCase)) hasFire = true;
                    if (actionToken.Equals("Death", StringComparison.OrdinalIgnoreCase)) hasDeath = true;
                }
            }
            if (hasClipSettings)
            {
                importer.clipAnimations = clips;
            }
            if (enforceIdleFireDeath && isWalker)
            {
                var missing = new List<string>();
                if (!hasIdle) missing.Add("Action_Idle");
                if (!hasMove) missing.Add("Action_Walk/Action_Drive");
                if (!hasFire) missing.Add("Action_FireRecoil");
                if (!hasDeath) missing.Add("Action_Death");
                if (missing.Count > 0)
                {
                    Debug.LogWarning("[TacticalOperationsScaffolder] Model " + modelPath +
                        " missing required clips for walker contract: " + string.Join(", ", missing.ToArray()) +
                        ". Set enforceIdleFireDeath=false to allow stripped rigs.");
                }
            }
            else if (isWalker && hasClipSettings && (!hasIdle || !hasMove))
            {
                Debug.LogWarning("[TacticalOperationsScaffolder] Walker model " + modelPath +
                    " has incomplete locomotion clips (Idle=" + hasIdle + ", Move=" + hasMove + ").");
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

        private static string CreateVisualFromSpec(TacticalUnitSpec spec, string effectiveModelPath)
        {
            Material pbrMat = CreatePbrMaterial(spec.UnitName);

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(effectiveModelPath);
            if (modelAsset == null) throw new FileNotFoundException("Model not found: " + effectiveModelPath + " (spec " + spec.UnitName + ")");

            var root = new GameObject(spec.UnitName + "Visual");
            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null) modelInstance = UnityEngine.Object.Instantiate(modelAsset);
                if (modelInstance == null) throw new InvalidOperationException(spec.UnitName + " model instance is null after instantiation");
                modelInstance.name = spec.UnitName + "Model";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;

                if (pbrMat == null) throw new InvalidOperationException(spec.UnitName + " PBR material is null");

                Renderer[] initialRenderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                bool hasMesh = false;
                foreach (Renderer renderer in initialRenderers)
                {
                    if (renderer is MeshRenderer meshRenderer)
                    {
                        MeshFilter meshFilter = meshRenderer.GetComponent<MeshFilter>();
                        if (meshFilter != null && meshFilter.sharedMesh != null) { hasMesh = true; break; }
                    }
                    else if (renderer is SkinnedMeshRenderer skinnedRenderer && skinnedRenderer.sharedMesh != null)
                    {
                        hasMesh = true; break;
                    }
                }
                if (!hasMesh) throw new InvalidOperationException(spec.UnitName + " model has no mesh-bearing renderers");

                // Unified collider handling: convert _COLLIDER hull meshes into BoxColliders and hide renderers.
                int converted = 0;
                foreach (Renderer renderer in initialRenderers)
                {
                    if (renderer.name.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        renderer.enabled = false;
                        MeshFilter mf = renderer.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null)
                        {
                            BoxCollider bc = renderer.GetComponent<BoxCollider>();
                            if (bc == null) bc = renderer.gameObject.AddComponent<BoxCollider>();
                            bc.center = mf.sharedMesh.bounds.center;
                            bc.size = mf.sharedMesh.bounds.size;
                            EditorUtility.SetDirty(renderer.gameObject);
                            converted++;
                        }
                    }
                }

                Renderer[] allRenderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in allRenderers)
                {
                    if (r is MeshRenderer || r is SkinnedMeshRenderer)
                    {
                        int count = r.sharedMaterials != null && r.sharedMaterials.Length > 0 ? r.sharedMaterials.Length : 1;
                        var mats = new Material[count];
                        for (int i = 0; i < count; i++) mats[i] = pbrMat;
                        r.sharedMaterials = mats;
                    }
                }

                if (converted == 0)
                {
                    FitBoxCollider(root, modelInstance);
                }

                VehiclePrefabBuilder.EnsureCanonicalVfxSockets(root, modelInstance);
                EnsureAdditionalTacticalSockets(root, modelInstance, spec);
                VehiclePrefabBuilder.EnsureCanonicalHardpointSockets(root, modelInstance);
                if (string.Equals(spec.UnitName, "BulwarkSiegeTank", StringComparison.Ordinal))
                {
                    Transform eraSocket = FindTransformRecursive(root.transform, "Socket_Hardpoint_ERA");
                    if (eraSocket != null)
                    {
                        eraSocket.localPosition = Vector3.zero;
                        eraSocket.localRotation = Quaternion.identity;
                        eraSocket.localScale = Vector3.one;
                    }
                }
                if (spec.IsWalker)
                {
                    RogueCommandVisualSampleScaffolder.EnsureCanonicalWalkerSockets(root, modelInstance, false);
                }
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
                    root, modelInstance, modelAsset, spec.UnitName, preferWalkForMovement: spec.PreferWalkForMovement);
                Animator animator = root.GetComponent<Animator>();
                bool animatorValid = animator != null && animator.avatar != null && animator.avatar.isValid &&
                    animator.runtimeAnimatorController != null;
                if (spec.RequireValidAnimator && !animatorValid)
                {
                    throw new InvalidOperationException(spec.UnitName + " rig must produce a valid Generic Avatar and AnimatorController");
                }
                if (animatorValid)
                {
                    animator.applyRootMotion = false;
                }
                else if (animator != null)
                {
                    Debug.LogWarning("[TacticalOperationsScaffolder] " + spec.UnitName +
                        " Animator present but invalid (avatar=" + (animator.avatar != null ? animator.avatar.isValid.ToString() : "null") +
                        ", controller=" + (animator.runtimeAnimatorController != null ? "present" : "missing") + ").");
                }

                VehiclePrefabBuilder.SetupCompanionLodGroup(root, modelInstance, spec.UnitName, pbrMat);

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath, out success);
                if (!success) throw new InvalidOperationException("Could not save prefab to " + spec.PrefabPath);

                NormalizeEmptyUnityYamlStrings(spec.PrefabPath);
                ValidateSavedPrefab(spec.PrefabPath, spec.UnitName);
                return spec.PrefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Additional canonical tactical sockets (turret pivot, radar, antenna, foot contact)
        /// that VehiclePrefabBuilder.EnsureCanonicalVfxSockets does not guarantee.
        /// </summary>
        private static void EnsureAdditionalTacticalSockets(GameObject root, GameObject modelInstance, TacticalUnitSpec spec)
        {
            if (root == null || modelInstance == null) return;
            Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;
            Bounds bounds = GetLocalBounds(root, modelInstance);
            if (bounds.size.sqrMagnitude < 1e-6f) return;
            Vector3 center = bounds.center;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3 size = bounds.size;
            if (spec.HasTurretOrRadar)
            {
                Vector3 turretPivot = new Vector3(center.x, Mathf.Lerp(min.y, max.y, 0.72f), center.z + size.z * 0.10f);
                EnsureSocketIfMissing(root.transform, "Socket_TurretPivot", turretPivot);
                Transform pivot = FindTransformRecursive(root.transform, "Socket_TurretPivot");
                if (pivot != null)
                {
                    EnsureSocketIfMissing(pivot, "Socket_TurretPitch", Vector3.zero);
                }
            }
            if (spec.HasTurretOrRadar)
            {
                Vector3 radar = new Vector3(center.x - size.x * 0.22f, Mathf.Lerp(min.y, max.y, 0.85f), min.z + size.z * 0.30f);
                Vector3 antenna = new Vector3(center.x + size.x * 0.22f, Mathf.Lerp(min.y, max.y, 0.80f), min.z + size.z * 0.30f);
                EnsureSocketIfMissing(root.transform, "Socket_RadarMast", radar);
                EnsureSocketIfMissing(root.transform, "Socket_Antenna", antenna);
            }
            if (spec.IsWalker)
            {
                float half = Mathf.Max(size.x * 0.22f, 0.12f);
                EnsureSocketIfMissing(root.transform, "Socket_FootContact_L", new Vector3(center.x - half, min.y, center.z));
                EnsureSocketIfMissing(root.transform, "Socket_FootContact_R", new Vector3(center.x + half, min.y, center.z));
            }
            EnsureSocketIfMissing(root.transform, "Socket_DamageSmoke", new Vector3(center.x, Mathf.Lerp(min.y, max.y, 0.82f), min.z + size.z * 0.20f));
            EnsureSocketIfMissing(root.transform, "Socket_DamageFire", new Vector3(center.x + size.x * 0.12f, Mathf.Lerp(min.y, max.y, 0.55f), center.z));
        }

        private static Transform FindTransformRecursive(Transform parent, string name)
        {
            if (parent == null) return null;
            if (string.Equals(parent.name, name, StringComparison.OrdinalIgnoreCase)) return parent;
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }
            return null;
        }

        private static void EnsureSocketIfMissing(Transform parent, string socketName, Vector3 localPosition)
        {
            if (FindTransformRecursive(parent, socketName) != null) return;
            var socket = new GameObject(socketName);
            socket.transform.SetParent(parent, false);
            socket.transform.localPosition = localPosition;
            socket.transform.localRotation = Quaternion.identity;
            socket.transform.localScale = Vector3.one;
        }

        private static Bounds GetLocalBounds(GameObject root, GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            Bounds world = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null) world.Encapsulate(renderers[i].bounds);
            }
            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 worldCorner = new Vector3(
                    (corner & 1) == 0 ? world.min.x : world.max.x,
                    (corner & 2) == 0 ? world.min.y : world.max.y,
                    (corner & 4) == 0 ? world.min.z : world.max.z);
                Vector3 localCorner = root.transform.InverseTransformPoint(worldCorner);
                min = Vector3.Min(min, localCorner);
                max = Vector3.Max(max, localCorner);
            }
            return new Bounds((min + max) * 0.5f, max - min);
        }

        private static void ValidateSavedPrefab(string prefabPath, string unitName)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[TacticalOperationsScaffolder] Could not reload prefab for validation: " + prefabPath);
                return;
            }
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            int enabledRenderers = 0;
            foreach (Renderer r in renderers) if (r != null && r.enabled) enabledRenderers++;
            Animator animator = prefab.GetComponent<Animator>();
            string animatorState = animator == null ? "none" :
                ((animator.avatar != null && animator.avatar.isValid) ? "valid" : "invalid") +
                "/controller " + (animator.runtimeAnimatorController != null ? "present" : "missing");
            string[] canonicalSockets = new[]
            {
                "Socket_MuzzlePrimary","Socket_EngineAudio","Socket_WeaponAudio",
                "Socket_DamageSmoke","Socket_DamageFire",
                "Socket_Hardpoint_ERA","Socket_Hardpoint_APS","Socket_Hardpoint_Sponsons","Socket_Hardpoint_FuelDrums"
            };
            var duplicateWarnings = new List<string>();
            var missingHardpoints = new List<string>();
            foreach (string socketName in canonicalSockets)
            {
                int count = 0;
                foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null && string.Equals(t.name, socketName, StringComparison.OrdinalIgnoreCase)) count++;
                }
                if (count > 1) duplicateWarnings.Add(socketName + " x" + count);
                if (count == 0 && socketName.StartsWith("Socket_Hardpoint_", StringComparison.Ordinal))
                {
                    missingHardpoints.Add(socketName);
                }
            }
            if (missingHardpoints.Count > 0)
            {
                throw new InvalidDataException(unitName + " prefab is missing canonical hardpoints: " + string.Join(", ", missingHardpoints.ToArray()));
            }
            if (duplicateWarnings.Count > 0)
            {
                throw new InvalidDataException(unitName + " prefab has duplicate canonical sockets: " + string.Join(", ", duplicateWarnings.ToArray()));
            }
            if (prefab.GetComponent<LODGroup>() == null)
            {
                throw new InvalidDataException(unitName + " prefab is missing required LODGroup.");
            }
            Debug.Log("[TacticalOperationsScaffolder] " + unitName + " prefab OK: renderers=" + renderers.Length +
                " (enabled " + enabledRenderers + "), animator=" + animatorState + ", LODGroup=present");
        }

        private static void NormalizeEmptyUnityYamlStrings(string assetRelativePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetRelativePath));
            if (!File.Exists(absolutePath)) return;
            string contents = File.ReadAllText(absolutePath);
            string normalized = contents;
            if (string.Equals(Path.GetExtension(absolutePath), ".meta", StringComparison.OrdinalIgnoreCase))
            {
                normalized = Regex.Replace(normalized, @"(?m)[ \t]+(?=
?$)", string.Empty);
            }
            normalized = Regex.Replace(normalized,
                @"(?m)^([ \t]*(?:value|userData|assetBundleName|assetBundleVariant|motionNodeName|" +
                @"rigImportErrors|rigImportWarnings|animationImportErrors|animationImportWarnings|" +
                @"animationRetargetingWarnings|rootMotionBoneName):)[ \t]*(
?)$",
                "$1 \"\"$2");
            if (!string.Equals(contents, normalized, StringComparison.Ordinal))
            {
                File.WriteAllText(absolutePath, normalized, new UTF8Encoding(false));
            }
        }

        // ---------------------------------------------------------------------------
        // Legacy per-unit entry points kept for compatibility. Each delegates to spec.
        // ---------------------------------------------------------------------------

        private static string CreateTidebreakerHovercraftVisual()
        {
            return CreateVisualFromSpec(UnitSpecs[0], ResolveEffectiveModelPath(UnitSpecs[0]));
        }

        private static string CreateSalvageHaulerVisual()
        {
            return CreateVisualFromSpec(UnitSpecs[1], ResolveEffectiveModelPath(UnitSpecs[1]));
        }

        private static string CreateSkyguardSAMVisual()
        {
            return CreateVisualFromSpec(UnitSpecs[2], ResolveEffectiveModelPath(UnitSpecs[2]));
        }

        private static string CreateBulwarkSiegeTankVisual()
        {
            return CreateVisualFromSpec(UnitSpecs[3], ResolveEffectiveModelPath(UnitSpecs[3]));
        }

        private static string CreateArcLanceWalkerVisual()
        {
            return CreateVisualFromSpec(UnitSpecs[4], ResolveEffectiveModelPath(UnitSpecs[4]));
        }

        private static string CreateFieldMedicRoverVisual()
        {
            return CreateVisualFromSpec(UnitSpecs[5], ResolveEffectiveModelPath(UnitSpecs[5]));
        }

        private static string CreatePbrVisual(string unitName, string modelPath, string prefabPath)
        {
            foreach (TacticalUnitSpec spec in UnitSpecs)
            {
                if (string.Equals(spec.UnitName, unitName, StringComparison.OrdinalIgnoreCase))
                {
                    return CreateVisualFromSpec(spec, ResolveEffectiveModelPath(spec));
                }
            }
            throw new InvalidOperationException("Unknown tactical unit spec: " + unitName + " (" + modelPath + " -> " + prefabPath + ")");
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

            const string detailTexturePath =
                "Assets/RogueCommandExpansionSample/Textures/Detail/RCL_SteelMicroDetail_Normal.png";
            AssetDatabase.ImportAsset(detailTexturePath, ImportAssetOptions.ForceUpdate);
            Texture2D detail = AssetDatabase.LoadAssetAtPath<Texture2D>(detailTexturePath);
            if (detail == null)
            {
                throw new FileNotFoundException("Shared steel micro-detail normal is missing: " + detailTexturePath);
            }
            mat.SetTexture("_DetailBumpMap", detail);
            mat.SetFloat("_DetailBumpScale", 0.5f);
            mat.SetFloat("_DetailTiling", 16.0f);

            mat.SetFloat("_GlossMapScale", 1.0f);
            mat.SetFloat("_Metallic", 1.0f);
            mat.SetFloat("_Smoothness", 1.0f);
            mat.SetFloat("_BumpScale", 1.0f);
            mat.SetFloat("_AoStrength", 1.0f);
            mat.SetFloat("_WearStrength", 0.25f);
            mat.SetFloat("_SootStrength", 1.0f);
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
            // MetallicGloss packs metallic in RGB and smoothness in A; preserve alpha as input, never as transparency.
            if (linear)
            {
                if (importer.alphaSource != TextureImporterAlphaSource.FromInput) { importer.alphaSource = TextureImporterAlphaSource.FromInput; changed = true; }
                if (importer.alphaIsTransparency) { importer.alphaIsTransparency = false; changed = true; }
            }
            if (changed) importer.SaveAndReimport();
        }

        private static void FitBoxCollider(GameObject root, GameObject model)
        {
            Renderer[] allRenderers = model.GetComponentsInChildren<Renderer>(true);
            if (allRenderers == null) return;

            var renderers = new List<Renderer>();
            foreach (Renderer r in allRenderers)
            {
                if (r == null || !r.enabled) continue;
                if (r.name.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                renderers.Add(r);
            }
            if (renderers.Count == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Count; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            BoxCollider collider = root.GetComponent<BoxCollider>();
            if (collider == null) collider = root.AddComponent<BoxCollider>();
            collider.center = root.transform.InverseTransformPoint(bounds.center);
            collider.size = bounds.size;
        }
    }
}
