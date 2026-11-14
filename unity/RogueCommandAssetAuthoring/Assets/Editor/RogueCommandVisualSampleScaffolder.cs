using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        private const string IroncladCardSpritePath = AssetRoot + "/IroncladWalkerCard.png";
        private const string IroncladModelPath = AssetRoot + "/Models/IroncladWalker.fbx";
        private const string AtlasCraneMechPrefabPath = AssetRoot + "/AtlasCraneMechVisual.prefab";
        private const string AtlasCraneMechCardSpritePath = AssetRoot + "/AtlasCraneMechCard.png";
        private const string AtlasCraneMechModelPath = AssetRoot + "/Models/AtlasCraneMech.fbx";
        private const string BastionSiegeMechPrefabPath = AssetRoot + "/BastionSiegeMechVisual.prefab";
        private const string BastionSiegeMechCardSpritePath = AssetRoot + "/BastionSiegeMechCard.png";
        private const string BastionSiegeMechModelPath = AssetRoot + "/Models/BastionSiegeMech.fbx";
        private const string BastionEngineLoopAudioPath = AssetRoot + "/Audio/BastionSiegeMech_EngineLoop.wav";
        private const string BastionWeaponShotAudioPath = AssetRoot + "/Audio/BastionSiegeMech_WeaponShot.wav";
        private const string SurgeArcTankPrefabPath = AssetRoot + "/SurgeArcTankVisual.prefab";
        private const string SurgeArcTankCardSpritePath = AssetRoot + "/SurgeArcTankCard.png";
        private const string SurgeArcTankModelPath = AssetRoot + "/Models/SurgeArcTank.fbx";
        private const string ArachnidHexapodVisualPrefabPath =
            AssetRoot + "/VisualOnly/Prefabs/ArachnidHexapodVisual.prefab";
        private const string ArachnidHexapodFactoryVisualPrefabPath =
            AssetRoot + "/VisualOnly/Prefabs/ArachnidHexapodFactoryVisual.prefab";
        private const string OverlordCommandHQVisualPrefabPath =
            AssetRoot + "/VisualOnly/Prefabs/OverlordCommandHQVisual.prefab";
        private const string OverlordCommandHQFactoryVisualPrefabPath =
            AssetRoot + "/VisualOnly/Prefabs/OverlordCommandHQFactoryVisual.prefab";
        private const string ArachnidHexapodModelPath = AssetRoot + "/Models/ArachnidHexapod.fbx";
        private const string ArachnidHexapodFactoryModelPath = AssetRoot + "/Models/ArachnidHexapodFactory.fbx";
        private const string OverlordCommandHQModelPath = AssetRoot + "/Models/OverlordCommandHQ.fbx";
        private const string OverlordCommandHQFactoryModelPath = AssetRoot + "/Models/OverlordCommandHQFactory.fbx";
        private const string ArachnidHexapodCardSpritePath = AssetRoot + "/ArachnidHexapodCard.png";
        private const string OverlordCommandHQCardSpritePath = AssetRoot + "/OverlordCommandHQCard.png";
        private const string MaterialRoot = AssetRoot + "/Materials";
        private const string DetailBumpMapPath = AssetRoot + "/Textures/Detail/RCL_SteelMicroDetail_Normal.png";
        private const string VertexWearShaderName = "RogueCommand/VehiclePBR_VertexWear";
        private const float FactoryNewWearStrength = 0.25f;
        private const string RenderingRoot = AssetRoot + "/Rendering";
        private const string AuthoringPipelineAssetPath = RenderingRoot + "/RogueCommandURP.asset";
        private const string DefaultRendererDataAssetPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRendererData.asset";
        private const string BundleOutputPath = "../../artifacts/asset-bundles";
        private const string BundleName = "sample-native-expansion.bundle";

        [MenuItem("Rogue Command/Asset Bundles/Configure URP Authoring Pipeline")]
        public static void ConfigureUrpAuthoringPipeline()
        {
            try
            {
                EnsureUrpAuthoringPipelineAsset();
                ConfigureUrpMaterialUpgradeVersion();
                AssetDatabase.SaveAssets();
                Debug.Log("Configured the Unity authoring project to use its URP asset for Rogue Command bundle builds.");
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
                    EditorUtility.DisplayDialog("URP authoring setup failed", exception.Message, "OK");
                }
            }
        }

        internal static void EnsureUrpAuthoringPipeline()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
            {
                throw new InvalidOperationException(
                    "The Unity authoring project is not using URP. Run 'Rogue Command/Asset Bundles/Configure URP Authoring Pipeline', " +
                    "then reopen the project before building game materials or bundles.");
            }

            ConfigureUrpMaterialUpgradeVersion();
        }

        private static void ConfigureUrpMaterialUpgradeVersion()
        {
            Type projectSettingsType = null;
            Type materialPostprocessorType = null;
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                if (projectSettingsType == null)
                {
                    projectSettingsType = loadedAssemblies[i].GetType(
                        "UnityEditor.Rendering.Universal.UniversalProjectSettings", false);
                }

                if (materialPostprocessorType == null)
                {
                    materialPostprocessorType = loadedAssemblies[i].GetType(
                        "UnityEditor.Rendering.Universal.MaterialPostprocessor", false);
                }

                if (projectSettingsType != null && materialPostprocessorType != null)
                {
                    break;
                }
            }

            const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo upgradersField = materialPostprocessorType == null
                ? null
                : materialPostprocessorType.GetField("k_Upgraders", StaticMembers);
            Array upgraders = upgradersField == null ? null : upgradersField.GetValue(null) as Array;
            PropertyInfo versionProperty = projectSettingsType == null
                ? null
                : projectSettingsType.GetProperty("materialVersionForUpgrade", StaticMembers);
            MethodInfo saveMethod = projectSettingsType == null
                ? null
                : projectSettingsType.GetMethod("Save", StaticMembers);
            if (upgraders == null || upgraders.Length == 0 || versionProperty == null || saveMethod == null)
            {
                throw new InvalidOperationException(
                    "Could not resolve URP's material migration settings; refusing to create materials with an " +
                    "uninitialized migration version.");
            }

            versionProperty.SetValue(null, upgraders.Length, null);
            saveMethod.Invoke(null, null);
            object savedVersion = versionProperty.GetValue(null, null);
            if (!(savedVersion is int) || (int)savedVersion != upgraders.Length)
            {
                throw new InvalidOperationException("URP did not save its current material migration version.");
            }

            EnsureSampleMaterialsHaveCurrentVersion(materialPostprocessorType, upgraders.Length);
        }

        private static void EnsureSampleMaterialsHaveCurrentVersion(Type materialPostprocessorType, int version)
        {
            Type assetVersionType = materialPostprocessorType.Assembly.GetType(
                "UnityEditor.Rendering.Universal.AssetVersion", false);
            FieldInfo assetVersionField = assetVersionType == null
                ? null
                : assetVersionType.GetField("version", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (assetVersionField == null || assetVersionType == null)
            {
                throw new InvalidOperationException("Could not resolve URP's material asset version metadata.");
            }

            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { MaterialRoot });
            int changedCount = 0;
            for (int i = 0; i < materialGuids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (material == null)
                {
                    continue;
                }

                UnityEngine.Object assetVersion = null;
                UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                for (int subAssetIndex = 0; subAssetIndex < subAssets.Length; subAssetIndex++)
                {
                    if (assetVersionType.IsInstanceOfType(subAssets[subAssetIndex]))
                    {
                        assetVersion = subAssets[subAssetIndex];
                        break;
                    }
                }

                if (assetVersion == null)
                {
                    assetVersion = ScriptableObject.CreateInstance(assetVersionType);
                    assetVersion.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.NotEditable;
                    AssetDatabase.AddObjectToAsset(assetVersion, material);
                }

                object existingVersion = assetVersionField.GetValue(assetVersion);
                if (!(existingVersion is int) || (int)existingVersion != version)
                {
                    assetVersionField.SetValue(assetVersion, version);
                    EditorUtility.SetDirty(assetVersion);
                    changedCount++;
                }
            }

            if (changedCount > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("Initialized the URP material migration metadata for " + changedCount +
                    " sample materials at version " + version + ".");
            }
        }

        private static void EnsureUrpAuthoringPipelineAsset()
        {
            RenderPipelineAsset configuredPipeline = GraphicsSettings.defaultRenderPipeline;
            if (configuredPipeline != null && !(configuredPipeline is UniversalRenderPipelineAsset))
            {
                throw new InvalidOperationException(
                    "Graphics Settings already uses a non-URP pipeline asset; it was left unchanged.");
            }

            if (configuredPipeline is UniversalRenderPipelineAsset)
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(RenderingRoot))
            {
                AssetDatabase.CreateFolder(AssetRoot, "Rendering");
            }

            UniversalRenderPipelineAsset pipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AuthoringPipelineAssetPath);
            if (pipeline == null)
            {
                ScriptableRendererData rendererData =
                    AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(DefaultRendererDataAssetPath);
                if (rendererData == null)
                {
                    throw new FileNotFoundException(
                        "Unity URP package default renderer data is unavailable at " + DefaultRendererDataAssetPath + ".");
                }

                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                pipeline.name = "RogueCommandURP";
                AssetDatabase.CreateAsset(pipeline, AuthoringPipelineAssetPath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            EditorUtility.SetDirty(GraphicsSettings.GetGraphicsSettings());
            AssetDatabase.SaveAssets();
        }

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

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                ValidateRiggedWalkerSources(projectRoot);
                EnsureUrpAuthoringPipeline();

                ConfigureSamplePbrTextureImports();
                string prefabPath = CreateSampleVisual();
                ImportCardSprite(CardSpritePath);
                string bikePrefabPath = CreateSkirmishBikeVisual();
                ImportCardSprite(BikeCardSpritePath);
                string colossusPrefabPath = CreateColossusVisual();
                ImportCardSprite(ColossusCardSpritePath);
                string ironcladPrefabPath = CreateIroncladVisual();
                ImportCardSprite(IroncladCardSpritePath);
                string atlasCraneMechPrefabPath = CreateAtlasCraneMechVisual();
                ImportCardSprite(AtlasCraneMechCardSpritePath);
                string bastionSiegeMechPrefabPath = CreateBastionSiegeMechVisual();
                ImportCardSprite(BastionSiegeMechCardSpritePath, true);
                string surgeArcTankPrefabPath = CreateSurgeArcTankVisual();
                ImportCardSprite(SurgeArcTankCardSpritePath);
                ImportCardSprite(ArachnidHexapodCardSpritePath);
                ImportCardSprite(OverlordCommandHQCardSpritePath);
                RefreshRiggedPbrPrefabAnimator("ArachnidHexapod", ArachnidHexapodModelPath, ArachnidHexapodVisualPrefabPath);
                RefreshRiggedPbrPrefabAnimator("OverlordCommandHQ", OverlordCommandHQModelPath, OverlordCommandHQVisualPrefabPath);
                RefreshFactoryPbrPrefab("ArachnidHexapodFactory", ArachnidHexapodFactoryModelPath, ArachnidHexapodFactoryVisualPrefabPath);
                RefreshFactoryPbrPrefab("OverlordCommandHQFactory", OverlordCommandHQFactoryModelPath, OverlordCommandHQFactoryVisualPrefabPath);
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
                        ironcladPrefabPath, IroncladCardSpritePath,
                        atlasCraneMechPrefabPath, AtlasCraneMechCardSpritePath,
                        bastionSiegeMechPrefabPath, BastionSiegeMechCardSpritePath,
                        surgeArcTankPrefabPath, SurgeArcTankCardSpritePath,
                        ArachnidHexapodCardSpritePath, OverlordCommandHQCardSpritePath,
                        ArachnidHexapodVisualPrefabPath, ArachnidHexapodFactoryVisualPrefabPath,
                        OverlordCommandHQVisualPrefabPath, OverlordCommandHQFactoryVisualPrefabPath
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
                Debug.Log("Built nine script-free Sandlance, Skirmish Bike, Colossus, Ironclad Walker, " +
                    "Atlas Crane Mech, Bastion Siege Mech, Surge Arc Tank, Arachnid Hexapod, and Overlord Command HQ visual overlays plus nine card Sprites in " +
                    BundleName + " at " + packBundles + ".");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
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

        /// <summary>
        /// Regenerates the Level 1 visuals that currently back canonical Level 2
        /// modules, without rebuilding or copying the sample AssetBundle snapshot.
        /// </summary>
        public static void RefreshCanonicalLevelOneVisuals()
        {
            try
            {
                if (!String.Equals(Application.unityVersion, RequiredUnityVersion, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Canonical Level 1 visuals require Unity " +
                        RequiredUnityVersion + "; current Editor is " + Application.unityVersion + ".");
                }

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                ValidateRiggedWalkerSources(projectRoot);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                string[] unitPrefabPaths =
                {
                    PrefabPath, BikePrefabPath, ColossusPrefabPath, IroncladPrefabPath,
                    AtlasCraneMechPrefabPath, BastionSiegeMechPrefabPath, SurgeArcTankPrefabPath,
                    ArachnidHexapodVisualPrefabPath, OverlordCommandHQVisualPrefabPath,
                    TacticalOperationsPackScaffolder.TidebreakerPrefabPath,
                    TacticalOperationsPackScaffolder.SalvageHaulerPrefabPath,
                    TacticalOperationsPackScaffolder.SkyguardSAMPrefabPath,
                    TacticalOperationsPackScaffolder.BulwarkSiegeTankPrefabPath,
                    TacticalOperationsPackScaffolder.ArcLanceWalkerPrefabPath,
                    TacticalOperationsPackScaffolder.FieldMedicRoverPrefabPath,
                    TacticalOperationsPackScaffolder.SentinelRelayPrefabPath,
                    TacticalOperationsPackScaffolder.GravitasBreakerPrefabPath,
                    TacticalOperationsPackScaffolder.CommandRelayPrefabPath
                };
                string[] unitNames =
                {
                    "SandlanceScout", "SkirmishStrikeBike", "ColossusSiegePlatform", "IroncladWalker",
                    "AtlasCraneMech", "BastionSiegeMech", "SurgeArcTank",
                    "ArachnidHexapod", "OverlordCommandHQ",
                    "TidebreakerHovercraft", "SalvageHauler", "SkyguardSAM",
                    "BulwarkSiegeTank", "ArcLanceWalker", "FieldMedicRover",
                    "SentinelRelay", "GravitasBreaker", "CommandRelay"
                };
                for (int i = 0; i < unitPrefabPaths.Length; i++)
                {
                    GameObject prefabContents = PrefabUtility.LoadPrefabContents(unitPrefabPaths[i]);
                    if (prefabContents == null)
                    {
                        throw new FileNotFoundException("Level 1 visual prefab could not be loaded: " + unitPrefabPaths[i]);
                    }

                    try
                    {
                        Transform modelTransform = GetUniqueNamedTransform(prefabContents, unitNames[i] + "Model", false);
                        GameObject modelObject = modelTransform != null ? modelTransform.gameObject : prefabContents;
                        string modelAssetPath = AssetRoot + "/Models/" + unitNames[i] + ".fbx";
                        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelAssetPath);
                        if (modelAsset == null)
                        {
                            throw new FileNotFoundException("Level 1 unit FBX could not be loaded: " + modelAssetPath, modelAssetPath);
                        }
                        int transformCountBefore = prefabContents.GetComponentsInChildren<Transform>(true).Length;
                        bool materialUpdated = EnsureAllRenderersUsePrefabVertexWearMaterial(prefabContents);
                        bool lodUpdated = VehiclePrefabBuilder.RemoveConflictingNestedLodGroups(prefabContents);
                        VehiclePrefabBuilder.EnsureCanonicalHardpointSockets(prefabContents, modelObject);
                        VehiclePrefabBuilder.SetupAnimatorIfAvailable(
                            prefabContents,
                            modelObject,
                            modelAsset,
                            unitNames[i],
                            preferWalkForMovement: IsWalkerUnitName(unitNames[i]));
                        Animator unitAnimator = prefabContents.GetComponent<Animator>();
                        if (unitAnimator != null)
                        {
                            unitAnimator.applyRootMotion = false;
                        }
                        if (String.Equals(unitNames[i], "BulwarkSiegeTank", StringComparison.Ordinal))
                        {
                            Transform eraSocket = GetUniqueNamedTransform(prefabContents, "Socket_Hardpoint_ERA", true);
                            eraSocket.localPosition = Vector3.zero;
                            eraSocket.localRotation = Quaternion.identity;
                            eraSocket.localScale = Vector3.one;
                        }
                        int transformCountAfter = prefabContents.GetComponentsInChildren<Transform>(true).Length;
                        bool saved;
                        PrefabUtility.SaveAsPrefabAsset(prefabContents, unitPrefabPaths[i], out saved);
                        if (!saved)
                        {
                            throw new InvalidOperationException("Unity could not save canonical hardpoint/animator updates to " + unitPrefabPaths[i] + ".");
                        }
                        NormalizeEmptyUnityYamlStrings(Path.GetFullPath(Path.Combine(projectRoot, unitPrefabPaths[i])));

                        ValidateCanonicalModuleHardpoints(prefabContents, unitPrefabPaths[i]);
                        ValidateLevelOnePbrAndLodContract(prefabContents, unitPrefabPaths[i], unitNames[i], requireLayeredAnimator: true);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(prefabContents);
                    }
                }

                string[] factoryPrefabPaths =
                {
                    ArachnidHexapodFactoryVisualPrefabPath,
                    OverlordCommandHQFactoryVisualPrefabPath,
                    "Assets/TacticalOperations/Prefabs/BulwarkFoundryVisual.prefab"
                };
                string[] factoryNames =
                {
                    "ArachnidHexapodFactory",
                    "OverlordCommandHQFactory",
                    "BulwarkFoundry"
                };
                for (int i = 0; i < factoryPrefabPaths.Length; i++)
                {
                    GameObject prefabContents = PrefabUtility.LoadPrefabContents(factoryPrefabPaths[i]);
                    if (prefabContents == null)
                    {
                        throw new FileNotFoundException("Level 1 factory prefab could not be loaded: " + factoryPrefabPaths[i]);
                    }
                    try
                    {
                        if (VehiclePrefabBuilder.RemoveConflictingNestedLodGroups(prefabContents))
                        {
                            bool saved;
                            PrefabUtility.SaveAsPrefabAsset(prefabContents, factoryPrefabPaths[i], out saved);
                            if (!saved) throw new InvalidOperationException("Could not save LOD ownership: " + factoryPrefabPaths[i]);
                        }
                        ValidateLevelOnePbrAndLodContract(prefabContents, factoryPrefabPaths[i], factoryNames[i]);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(prefabContents);
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                Debug.Log("[CanonicalLevelOneVisuals] Validated " + unitPrefabPaths.Length + " unit prefabs and " +
                    factoryPrefabPaths.Length + " factory prefabs with LODGroup, Material Maker VehiclePBR_VertexWear + steel micro-detail, and four unique canonical hardpoints per unit.");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
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
                    EditorUtility.DisplayDialog("Canonical Level 1 refresh failed", exception.Message, "OK");
                }
            }
        }

        private static void ValidateCanonicalModuleHardpoints(GameObject prefab, string prefabPath)
        {
            string[] slots = { "ERA", "APS", "Sponsons", "FuelDrums" };
            var seenPositions = new List<Vector3>();
            for (int i = 0; i < slots.Length; i++)
            {
                Transform socket = GetUniqueNamedTransform(prefab, "Socket_Hardpoint_" + slots[i], true);
                for (int j = 0; j < seenPositions.Count; j++)
                {
                    if (Vector3.Distance(seenPositions[j], socket.localPosition) < 0.05f)
                    {
                        throw new InvalidDataException("Duplicate/overlapping hardpoint socket position for Socket_Hardpoint_" +
                            slots[i] + " in " + prefabPath + ": " + socket.localPosition);
                    }
                }
                seenPositions.Add(socket.localPosition);
            }
            Debug.Log("[CanonicalLevelOneVisuals] Validated " + prefabPath + ".");
        }

        private static bool EnsureAllRenderersUsePrefabVertexWearMaterial(GameObject prefab)
        {
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            Material canonicalMat = null;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || r is ParticleSystemRenderer ||
                    r.name.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    r.name.EndsWith("_ShieldBubble", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                Material[] mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null && mats[m].shader != null &&
                        String.Equals(mats[m].shader.name, VertexWearShaderName, StringComparison.Ordinal))
                    {
                        canonicalMat = mats[m];
                        break;
                    }
                }
                if (canonicalMat != null) break;
            }
            if (canonicalMat == null) return false;

            bool changed = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || r is ParticleSystemRenderer ||
                    r.name.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    r.name.EndsWith("_ShieldBubble", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                Material[] mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0)
                {
                    r.sharedMaterials = new[] { canonicalMat };
                    changed = true;
                    continue;
                }
                bool slotChanged = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null || mats[m].shader == null ||
                        !String.Equals(mats[m].shader.name, VertexWearShaderName, StringComparison.Ordinal))
                    {
                        mats[m] = canonicalMat;
                        slotChanged = true;
                    }
                }
                if (slotChanged)
                {
                    r.sharedMaterials = mats;
                    changed = true;
                }
            }
            return changed;
        }

        private static bool IsWalkerUnitName(string unitName)
        {
            return String.Equals(unitName, "IroncladWalker", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(unitName, "AtlasCraneMech", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(unitName, "BastionSiegeMech", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(unitName, "ArachnidHexapod", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(unitName, "ArcLanceWalker", StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateLevelOnePbrAndLodContract(
            GameObject prefab,
            string prefabPath,
            string assetName,
            bool requireLayeredAnimator = false)
        {
            if (requireLayeredAnimator)
            {
                Animator anim = prefab.GetComponent<Animator>();
                if (anim == null || anim.avatar == null || !anim.avatar.isValid || anim.applyRootMotion)
                {
                    throw new InvalidDataException(assetName + " prefab must have a valid Generic Avatar with root motion disabled.");
                }
                var controller = anim.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                if (controller == null)
                {
                    throw new InvalidDataException(assetName + " prefab is missing an AnimatorController.");
                }
                bool hasMoving = false, hasSpeed = false, hasFire = false, hasDeath = false;
                foreach (AnimatorControllerParameter p in controller.parameters)
                {
                    if (p.name == "RCL_IsMoving" && p.type == AnimatorControllerParameterType.Bool) hasMoving = true;
                    else if (p.name == "RCL_SpeedMultiplier" && p.type == AnimatorControllerParameterType.Float &&
                        Mathf.Approximately(p.defaultFloat, 1.0f)) hasSpeed = true;
                    else if (p.name == "RCL_Fire" && p.type == AnimatorControllerParameterType.Trigger) hasFire = true;
                    else if (p.name == "RCL_Death" && p.type == AnimatorControllerParameterType.Trigger) hasDeath = true;
                }
                if (!hasMoving || !hasSpeed || !hasFire || !hasDeath)
                {
                    throw new InvalidDataException(assetName + " AnimatorController is missing RCL layered parameters (RCL_IsMoving/RCL_SpeedMultiplier=1.0/RCL_Fire/RCL_Death).");
                }
                bool hasAdditiveFireLayer = false;
                foreach (UnityEditor.Animations.AnimatorControllerLayer layer in controller.layers)
                {
                    if (String.Equals(layer.name, "UpperBodyFireRecoil", StringComparison.Ordinal) &&
                        layer.blendingMode == UnityEditor.Animations.AnimatorLayerBlendingMode.Additive)
                    {
                        hasAdditiveFireLayer = true;
                        break;
                    }
                }
                if (!hasAdditiveFireLayer)
                {
                    throw new InvalidDataException(assetName + " AnimatorController is missing Additive layer 'UpperBodyFireRecoil'.");
                }
            }
            LODGroup lodGroup = prefab.GetComponent<LODGroup>();
            if (lodGroup == null || lodGroup.lodCount < 3)
            {
                throw new InvalidDataException(assetName + " prefab is missing a 3-tier LODGroup: " + prefabPath);
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            int checkedMaterials = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || r is ParticleSystemRenderer ||
                    r.name.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    r.name.EndsWith("_ShieldBubble", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;

                Material[] mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0)
                {
                    throw new InvalidDataException(assetName + " renderer '" + r.name + "' has no material in " + prefabPath);
                }
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null || mat.shader == null ||
                        !String.Equals(mat.shader.name, VertexWearShaderName, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(assetName + " material on '" + r.name + "' must use " + VertexWearShaderName + ".");
                    }
                    ValidatePbrMaterialTexture(mat, "_BaseMap", true, assetName, "Albedo");
                    ValidatePbrMaterialTexture(mat, "_MetallicGlossMap", true, assetName, "MetallicGloss");
                    ValidatePbrMaterialTexture(mat, "_BumpMap", true, assetName, "Normal");
                    ValidatePbrMaterialTexture(mat, "_EmissionMap", true, assetName, "Emission");
                    Texture detail = mat.HasProperty("_DetailBumpMap") ? mat.GetTexture("_DetailBumpMap") : null;
                    if (detail == null)
                    {
                        throw new InvalidDataException(assetName + " material on '" + r.name + "' is missing _DetailBumpMap (steel micro-detail normal).");
                    }
                    checkedMaterials++;
                }
            }
            if (checkedMaterials == 0)
            {
                throw new InvalidDataException(assetName + " prefab has no validated mesh materials: " + prefabPath);
            }
        }

        // Targeted recovery/build entry point. This consumes already-authored prefabs and
        // cards without invoking any model, material, animation, or prefab scaffolding.
        public static void BuildBastionBundleFromExistingAssets()
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

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                EnsureExistingBundleAsset(PrefabPath, typeof(GameObject));
                EnsureExistingBundleAsset(CardSpritePath, typeof(Sprite));
                EnsureExistingBundleAsset(BikePrefabPath, typeof(GameObject));
                EnsureExistingBundleAsset(BikeCardSpritePath, typeof(Sprite));
                EnsureExistingBundleAsset(ColossusPrefabPath, typeof(GameObject));
                EnsureExistingBundleAsset(ColossusCardSpritePath, typeof(Sprite));
                EnsureExistingBundleAsset(IroncladPrefabPath, typeof(GameObject));
                EnsureExistingBundleAsset(IroncladCardSpritePath, typeof(Sprite));
                EnsureExistingBundleAsset(AtlasCraneMechPrefabPath, typeof(GameObject));
                EnsureExistingBundleAsset(AtlasCraneMechCardSpritePath, typeof(Sprite));
                EnsureExistingBundleAsset(BastionSiegeMechPrefabPath, typeof(GameObject));
                EnsureExistingBundleAsset(BastionSiegeMechCardSpritePath, typeof(Sprite));
                EnsureExistingBundleAsset(SurgeArcTankPrefabPath, typeof(GameObject));
                EnsureExistingBundleAsset(SurgeArcTankCardSpritePath, typeof(Sprite));
                EnsureExistingBundleAsset(ArachnidHexapodCardSpritePath, typeof(Sprite));
                EnsureExistingBundleAsset(OverlordCommandHQCardSpritePath, typeof(Sprite));
                EnsureExistingBundleAsset(BastionEngineLoopAudioPath, typeof(AudioClip));
                EnsureExistingBundleAsset(BastionWeaponShotAudioPath, typeof(AudioClip));
                EnsureExistingBundleAsset(ArachnidHexapodVisualPrefabPath, typeof(GameObject));
                EnsureExistingBundleAsset(ArachnidHexapodFactoryVisualPrefabPath, typeof(GameObject));
                EnsureExistingBundleAsset(OverlordCommandHQVisualPrefabPath, typeof(GameObject));
                EnsureExistingBundleAsset(OverlordCommandHQFactoryVisualPrefabPath, typeof(GameObject));

                GameObject bastionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BastionSiegeMechPrefabPath);
                AudioSource[] bastionAudioSources = bastionPrefab.GetComponentsInChildren<AudioSource>(true);
                if (bastionAudioSources.Length != 2)
                {
                    throw new InvalidDataException("Bastion prefab must already contain its two authored 3D AudioSources; " +
                        "found " + bastionAudioSources.Length + ". Refusing to regenerate the prefab in this targeted build.");
                }

                string packageManifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
                string packageManifest = File.ReadAllText(packageManifestPath);
                if (packageManifest.IndexOf("\"com.unity.modules.audio\": \"1.0.0\"", StringComparison.Ordinal) < 0)
                {
                    throw new InvalidDataException("Packages/manifest.json must enable built-in com.unity.modules.audio 1.0.0.");
                }

                EnsureUrpAuthoringPipeline();
                ConfigureArachnidOverlordPbrTextureImports();
                string outputPath = Path.GetFullPath(Path.Combine(projectRoot, BundleOutputPath));
                Directory.CreateDirectory(outputPath);
                var build = new AssetBundleBuild
                {
                    assetBundleName = BundleName,
                    assetNames = new[]
                    {
                        PrefabPath, CardSpritePath,
                        BikePrefabPath, BikeCardSpritePath,
                        ColossusPrefabPath, ColossusCardSpritePath,
                        IroncladPrefabPath, IroncladCardSpritePath,
                        AtlasCraneMechPrefabPath, AtlasCraneMechCardSpritePath,
                        BastionSiegeMechPrefabPath, BastionSiegeMechCardSpritePath,
                        SurgeArcTankPrefabPath, SurgeArcTankCardSpritePath,
                        ArachnidHexapodCardSpritePath, OverlordCommandHQCardSpritePath,
                        ArachnidHexapodVisualPrefabPath, ArachnidHexapodFactoryVisualPrefabPath,
                        OverlordCommandHQVisualPrefabPath, OverlordCommandHQFactoryVisualPrefabPath
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

                Debug.Log("Rebuilt and reopened " + BundleName + " from existing sample assets; no prefabs or " +
                    "model/material scaffolding was run. It includes the Arachnid Hexapod and Overlord HQ unit " +
                    "and factory visual overlays. Bastion includes its two authored AudioSources.");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
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
                    EditorUtility.DisplayDialog("Bastion bundle build failed", exception.Message, "OK");
                }
            }
        }

        private static void EnsureExistingBundleAsset(string assetPath, Type requiredType)
        {
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(assetPath, requiredType);
            if (asset == null)
            {
                throw new FileNotFoundException("Targeted bundle build requires an existing " +
                    requiredType.Name + " at " + assetPath + ". It will not create or repair it.");
            }
        }

        private static void ConfigureArachnidOverlordPbrTextureImports()
        {
            ConfigureSamplePbrTextureImports();
        }

        private static void ConfigureSamplePbrTextureImports()
        {
            string[] modelNames =
            {
                "SandlanceScout",
                "SkirmishStrikeBike",
                "ColossusSiegePlatform",
                "IroncladWalker",
                "AtlasCraneMech",
                "BastionSiegeMech",
                "SurgeArcTank",
                "ArachnidHexapod",
                "OverlordCommandHQ",
                "ArachnidHexapodFactory",
                "OverlordCommandHQFactory"
            };

            AssetDatabase.ImportAsset(DetailBumpMapPath, ImportAssetOptions.ForceUpdate);
            TextureImporter detailImporter = AssetImporter.GetAtPath(DetailBumpMapPath) as TextureImporter;
            if (detailImporter != null && (detailImporter.textureType != TextureImporterType.NormalMap || detailImporter.sRGBTexture))
            {
                detailImporter.textureType = TextureImporterType.NormalMap;
                detailImporter.sRGBTexture = false;
                detailImporter.mipmapEnabled = true;
                detailImporter.SaveAndReimport();
            }

            for (int i = 0; i < modelNames.Length; i++)
            {
                string textureRoot = AssetRoot + "/Models/Textures/" + modelNames[i];
                ConfigurePbrTextureImporter(textureRoot + "_Albedo.png", TextureImporterType.Default,
                    true, true, modelNames[i] + " albedo");
                ConfigurePbrTextureImporter(textureRoot + "_MetallicGloss.png", TextureImporterType.Default,
                    false, true, modelNames[i] + " MetallicGloss");
                ConfigurePbrTextureImporter(textureRoot + "_Normal.png", TextureImporterType.NormalMap,
                    false, false, modelNames[i] + " normal");
                ConfigurePbrTextureImporter(textureRoot + "_Emission.png", TextureImporterType.Default,
                    true, false, modelNames[i] + " emission");
            }
        }

        private static Material CreateUnifiedSamplePbrMaterial(string unitName)
        {
            Shader shader = Shader.Find(VertexWearShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException("Shader '" + VertexWearShaderName + "' not found.");
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, MaterialRoot)));

            string textureRoot = AssetRoot + "/Models/Textures/" + unitName;
            ConfigurePbrTextureImporter(textureRoot + "_Albedo.png", TextureImporterType.Default, true, true, unitName + " albedo");
            ConfigurePbrTextureImporter(textureRoot + "_MetallicGloss.png", TextureImporterType.Default, false, true, unitName + " MetallicGloss");
            ConfigurePbrTextureImporter(textureRoot + "_Normal.png", TextureImporterType.NormalMap, false, false, unitName + " normal");
            ConfigurePbrTextureImporter(textureRoot + "_Emission.png", TextureImporterType.Default, true, false, unitName + " emission");

            AssetDatabase.ImportAsset(DetailBumpMapPath, ImportAssetOptions.ForceUpdate);
            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(textureRoot + "_Albedo.png");
            Texture2D metallicGloss = AssetDatabase.LoadAssetAtPath<Texture2D>(textureRoot + "_MetallicGloss.png");
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(textureRoot + "_Normal.png");
            Texture2D emission = AssetDatabase.LoadAssetAtPath<Texture2D>(textureRoot + "_Emission.png");
            Texture2D detail = AssetDatabase.LoadAssetAtPath<Texture2D>(DetailBumpMapPath);
            if (albedo == null || metallicGloss == null || normal == null || emission == null)
            {
                throw new FileNotFoundException("One or more Material Maker PBR textures missing for " + unitName + " under " + textureRoot);
            }
            if (detail == null)
            {
                throw new FileNotFoundException("Shared steel micro-detail normal is missing: " + DetailBumpMapPath);
            }

            string matPath = MaterialRoot + "/" + unitName + "_PBR.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                mat.name = unitName + "_PBR";
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.shader = shader;
            }

            mat.SetTexture("_BaseMap", albedo);
            mat.SetTexture("_MetallicGlossMap", metallicGloss);
            mat.SetTexture("_BumpMap", normal);
            mat.SetTexture("_EmissionMap", emission);
            mat.SetTexture("_DetailBumpMap", detail);
            mat.SetFloat("_DetailBumpScale", 0.5f);
            mat.SetFloat("_DetailTiling", 16.0f);
            mat.SetFloat("_GlossMapScale", 1.0f);
            mat.SetFloat("_Metallic", 1.0f);
            mat.SetFloat("_Smoothness", 1.0f);
            mat.SetFloat("_BumpScale", 1.0f);
            mat.SetFloat("_AoStrength", 1.0f);
            mat.SetFloat("_WearStrength", FactoryNewWearStrength);
            mat.SetFloat("_SootStrength", 1.0f);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetColor("_EmissionColor", Color.white);
            mat.SetColor("_TeamColor", Color.white);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.EnableKeyword("_NORMALMAP");
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void AssignUnifiedPbrMaterial(GameObject target, Material pbrMaterial)
        {
            if (target == null || pbrMaterial == null) return;
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || r is ParticleSystemRenderer ||
                    r.name.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    r.name.EndsWith("_ShieldBubble", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                int slotCount = r.sharedMaterials != null && r.sharedMaterials.Length > 0 ? r.sharedMaterials.Length : 1;
                var slots = new Material[slotCount];
                for (int s = 0; s < slotCount; s++) slots[s] = pbrMaterial;
                r.sharedMaterials = slots;
            }
        }

        private static void ConfigurePbrTextureImporter(
            string assetPath,
            TextureImporterType textureType,
            bool sRgbTexture,
            bool preserveInputAlpha,
            string label)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new FileNotFoundException("Unity did not import the " + label + " map at " + assetPath + ".");
            }

            importer.textureType = textureType;
            importer.sRGBTexture = sRgbTexture;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.compressionQuality = 100;
            if (preserveInputAlpha)
            {
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
            }

            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            standalone.name = "Standalone";
            standalone.overridden = true;
            standalone.maxTextureSize = 2048;
            standalone.textureCompression = TextureImporterCompression.CompressedHQ;
            standalone.compressionQuality = 100;
            standalone.crunchedCompression = false;
            importer.SetPlatformTextureSettings(standalone);
            importer.SaveAndReimport();

            TextureImporter verifiedImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            TextureImporterPlatformSettings verifiedStandalone = verifiedImporter == null
                ? null
                : verifiedImporter.GetPlatformTextureSettings("Standalone");
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (verifiedImporter == null || texture == null || texture.width != 2048 || texture.height != 2048 ||
                verifiedImporter.textureType != textureType || verifiedImporter.sRGBTexture != sRgbTexture ||
                !verifiedImporter.mipmapEnabled || verifiedImporter.maxTextureSize != 2048 ||
                verifiedImporter.filterMode != FilterMode.Trilinear || verifiedImporter.anisoLevel != 8 ||
                verifiedImporter.textureCompression != TextureImporterCompression.CompressedHQ ||
                verifiedStandalone == null || !verifiedStandalone.overridden ||
                verifiedStandalone.maxTextureSize != 2048 ||
                verifiedStandalone.textureCompression != TextureImporterCompression.CompressedHQ ||
                verifiedStandalone.compressionQuality != 100 || verifiedStandalone.crunchedCompression ||
                (preserveInputAlpha && (verifiedImporter.alphaSource != TextureImporterAlphaSource.FromInput ||
                    verifiedImporter.alphaIsTransparency)))
            {
                throw new InvalidDataException("Unity did not preserve the required 2048px high-quality PBR import " +
                    "settings for " + label + " at " + assetPath + ".");
            }

            Debug.Log("Verified PBR texture import " + assetPath + ": " + texture.width + "x" + texture.height +
                ", type=" + verifiedImporter.textureType + ", sRGB=" + verifiedImporter.sRGBTexture +
                ", compression=" + verifiedStandalone.textureCompression + ", quality=" +
                verifiedStandalone.compressionQuality + ", alpha=" + verifiedImporter.alphaSource + ".");
        }

        private static void ValidateRiggedWalkerSources(string projectRoot)
        {
            var issues = new List<string>();
            ValidateSourceFile(projectRoot, IroncladCardSpritePath, "Ironclad Walker card", issues);
            ValidateSourceFile(projectRoot, AtlasCraneMechCardSpritePath, "Atlas Crane Mech card", issues);
            ValidateSourceFile(projectRoot, BastionSiegeMechCardSpritePath, "Bastion Siege Mech card", issues);
            ValidateSourceFile(projectRoot, BastionEngineLoopAudioPath, "Bastion Siege Mech engine loop", issues);
            ValidateSourceFile(projectRoot, BastionWeaponShotAudioPath, "Bastion Siege Mech weapon shot", issues);

            ValidateWalkerModelSource(projectRoot, IroncladModelPath, "Ironclad Walker", issues);
            ValidateWalkerModelSource(projectRoot, AtlasCraneMechModelPath, "Atlas Crane Mech", issues);
            ValidateWalkerModelSource(projectRoot, BastionSiegeMechModelPath, "Bastion Siege Mech", issues, true);

            if (issues.Count > 0)
            {
                throw new InvalidOperationException(
                    "The sample-native-expansion bundle needs these rigged walker source assets and settings:" +
                    Environment.NewLine + " - " + String.Join(Environment.NewLine + " - ", issues));
            }
        }

        private static void ValidateSourceFile(
            string projectRoot,
            string assetPath,
            string description,
            ICollection<string> issues)
        {
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(absolutePath))
            {
                issues.Add(description + " source is missing: " + assetPath);
            }
        }

        private static void ValidateWalkerModelSource(
            string projectRoot,
            string modelPath,
            string unitName,
            ICollection<string> issues,
            bool validateBastionRigContract = false)
        {
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot,
                modelPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(absolutePath))
            {
                issues.Add(unitName + " source FBX is missing: " + modelPath);
                return;
            }

            try
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) == null)
                {
                    AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
                }

                ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (importer == null)
                {
                    issues.Add(unitName + " must be imported by Unity as a ModelImporter: " + modelPath);
                    return;
                }

                bool settingsChanged = false;
                if (importer.animationType != ModelImporterAnimationType.Generic)
                {
                    importer.animationType = ModelImporterAnimationType.Generic;
                    settingsChanged = true;
                }
                if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    settingsChanged = true;
                }
                if (!importer.importAnimation)
                {
                    importer.importAnimation = true;
                    settingsChanged = true;
                }
                if (importer.optimizeGameObjects)
                {
                    importer.optimizeGameObjects = false;
                    settingsChanged = true;
                }
                if (settingsChanged)
                {
                    importer.SaveAndReimport();
                }

                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (model == null)
                {
                    issues.Add(unitName + " FBX could not be imported: " + modelPath);
                    return;
                }

                SkinnedMeshRenderer[] skinnedRenderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                bool hasSkinnedMeshWithBones = false;
                for (int i = 0; i < skinnedRenderers.Length; i++)
                {
                    if (skinnedRenderers[i] != null && skinnedRenderers[i].sharedMesh != null &&
                        skinnedRenderers[i].bones != null && skinnedRenderers[i].bones.Length > 0)
                    {
                        hasSkinnedMeshWithBones = true;
                        break;
                    }
                }

                bool hasRootBone = FindNamedTransforms(model, "Bone_Root").Count == 1;
                Avatar validAvatar = null;
                AnimationClip[] clips = new AnimationClip[0];
                UnityEngine.Object[] importedAssets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
                if (importedAssets != null)
                {
                    var foundClips = new List<AnimationClip>();
                    for (int i = 0; i < importedAssets.Length; i++)
                    {
                        AnimationClip clip = importedAssets[i] as AnimationClip;
                        if (clip != null && !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                        {
                            foundClips.Add(clip);
                        }

                        Avatar avatar = importedAssets[i] as Avatar;
                        if (avatar != null && avatar.isValid && validAvatar == null)
                        {
                            validAvatar = avatar;
                        }
                    }
                    clips = foundClips.ToArray();
                }

                var missingActions = new List<string>();
                if (!HasAnimationClip(clips, "Idle")) missingActions.Add("Action_Idle");
                if (!HasAnimationClip(clips, "Walk") && !HasAnimationClip(clips, "Drive"))
                {
                    missingActions.Add("Action_Walk or Action_Drive");
                }
                if (!HasAnimationClip(clips, "FireRecoil") && !HasAnimationClip(clips, "Fire"))
                {
                    missingActions.Add("Action_FireRecoil");
                }
                if (!HasAnimationClip(clips, "Death")) missingActions.Add("Action_Death");

                var modelIssues = new List<string>();
                if (!hasSkinnedMeshWithBones) modelIssues.Add("a SkinnedMeshRenderer with bound bones");
                if (!hasRootBone) modelIssues.Add("the Bone_Root transform");
                if (validAvatar == null) modelIssues.Add("a valid Generic Avatar");
                if (missingActions.Count > 0)
                {
                    modelIssues.Add("animation clips " + String.Join(", ", missingActions));
                }
                if (validateBastionRigContract)
                {
                    if (!HasClipFrameCount(clips, "FireRecoil", 15))
                    {
                        modelIssues.Add("Action_FireRecoil at 15 frames");
                    }
                    if (!HasClipFrameCount(clips, "Death", 45))
                    {
                        modelIssues.Add("Action_Death at 45 frames");
                    }
                    if (!HasInPlaceRootBoneAnimation(model, clips))
                    {
                        modelIssues.Add("Action_Walk with Bone_Root position invariant");
                    }
                    if (!HasRigidMechanicalBoneWeights(skinnedRenderers))
                    {
                        modelIssues.Add("single-bone 1.0 weights on all mechanical mesh vertices");
                    }
                }

                if (modelIssues.Count > 0)
                {
                    issues.Add(unitName + " is not ready at " + modelPath + "; add " +
                        String.Join(", ", modelIssues) +
                        ". The current static Ironclad Walker prototype does not satisfy the rigged overlay contract.");
                }
            }
            catch (Exception exception)
            {
                issues.Add(unitName + " FBX at " + modelPath + " could not be validated: " + exception.Message);
            }
        }

        private static bool HasAnimationClip(AnimationClip[] clips, string nameFragment)
        {
            if (clips == null)
            {
                return false;
            }

            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null &&
                    clips[i].name.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasClipFrameCount(AnimationClip[] clips, string nameFragment, int expectedFrames)
        {
            if (clips == null) return false;
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i];
                if (clip == null || clip.name.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                float frameCount = clip.length * Mathf.Max(1.0f, clip.frameRate);
                return Mathf.Abs(frameCount - expectedFrames) <= 1.01f;
            }

            return false;
        }

        private static bool HasInPlaceRootBoneAnimation(GameObject model, AnimationClip[] clips)
        {
            AnimationClip walkClip = null;
            if (clips != null)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] != null &&
                        (clips[i].name.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         clips[i].name.IndexOf("Drive", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        walkClip = clips[i];
                        break;
                    }
                }
            }
            if (model == null || walkClip == null) return false;

            GameObject sample = UnityEngine.Object.Instantiate(model);
            try
            {
                List<Transform> rootBones = FindNamedTransforms(sample, "Bone_Root");
                if (rootBones.Count != 1) return false;

                Transform rootBone = rootBones[0];
                float frameRate = Mathf.Max(1.0f, walkClip.frameRate);
                int frameCount = Mathf.Max(1, Mathf.CeilToInt(walkClip.length * frameRate));
                walkClip.SampleAnimation(sample, 0.0f);
                Vector3 inPlacePosition = rootBone.localPosition;
                for (int frame = 1; frame <= frameCount; frame++)
                {
                    float sampleTime = Mathf.Min(frame / frameRate, walkClip.length);
                    walkClip.SampleAnimation(sample, sampleTime);
                    if ((rootBone.localPosition - inPlacePosition).sqrMagnitude > 0.000001f)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sample);
            }
        }

        private static bool HasRigidMechanicalBoneWeights(SkinnedMeshRenderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0) return false;
            bool foundMesh = false;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = renderers[rendererIndex];
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                if (mesh == null) continue;
                foundMesh = true;
                BoneWeight[] weights = mesh.boneWeights;
                if (weights == null || weights.Length != mesh.vertexCount) return false;
                for (int vertex = 0; vertex < weights.Length; vertex++)
                {
                    BoneWeight weight = weights[vertex];
                    int activeWeights = 0;
                    float strongestWeight = 0.0f;
                    if (weight.weight0 > 0.001f) { activeWeights++; strongestWeight = Mathf.Max(strongestWeight, weight.weight0); }
                    if (weight.weight1 > 0.001f) { activeWeights++; strongestWeight = Mathf.Max(strongestWeight, weight.weight1); }
                    if (weight.weight2 > 0.001f) { activeWeights++; strongestWeight = Mathf.Max(strongestWeight, weight.weight2); }
                    if (weight.weight3 > 0.001f) { activeWeights++; strongestWeight = Mathf.Max(strongestWeight, weight.weight3); }
                    if (activeWeights != 1 || strongestWeight < 0.999f)
                    {
                        return false;
                    }
                }
            }

            return foundMesh;
        }

        internal static void EnsureCanonicalWalkerSockets(
            GameObject root,
            GameObject model,
            bool rapidFireMuzzle,
            bool quadrupedCrawler = false,
            bool requireFootContactSockets = true)
        {
            string[] footContactNames = !requireFootContactSockets
                ? new string[0]
                : quadrupedCrawler
                    ? new[]
                    {
                        "Socket_FootContact_FL",
                        "Socket_FootContact_FR",
                        "Socket_FootContact_RL",
                        "Socket_FootContact_RR"
                    }
                    : new[]
                    {
                        "Socket_FootContact_L",
                        "Socket_FootContact_R"
                    };
            string[] uniqueSocketNames =
            {
                "Socket_MuzzlePrimary",
                "Socket_EngineAudio",
                "Socket_WeaponAudio",
                "Socket_DamageSmoke",
                "Socket_DamageFire",
                "Socket_GroundCenter"
            };
            for (int i = 0; i < uniqueSocketNames.Length; i++)
            {
                GetUniqueNamedTransform(root, uniqueSocketNames[i], false);
            }

            for (int i = 0; i < footContactNames.Length; i++)
            {
                GetUniqueNamedTransform(root, footContactNames[i], false);
            }

            Bounds modelBounds = GetModelBoundsInRootSpace(root, model);
            float footHalfWidth = Mathf.Max(modelBounds.size.x * 0.22f, 0.12f);
            Transform[] footContacts = new Transform[footContactNames.Length];
            if (requireFootContactSockets && quadrupedCrawler)
            {
                // Crawler contact points are part of the authored model contract. Do not synthesize
                // generic L/R contacts, which lose the independent front/rear track anchors.
                for (int i = 0; i < footContactNames.Length; i++)
                {
                    footContacts[i] = GetUniqueNamedTransform(root, footContactNames[i], true);
                }
            }
            else if (requireFootContactSockets)
            {
                Vector3 leftFootFallback = new Vector3(
                    modelBounds.center.x - footHalfWidth, 0.0f, modelBounds.center.z);
                Vector3 rightFootFallback = new Vector3(
                    modelBounds.center.x + footHalfWidth, 0.0f, modelBounds.center.z);
                footContacts[0] = EnsureUniqueSocket(root, footContactNames[0], leftFootFallback);
                footContacts[1] = EnsureUniqueSocket(root, footContactNames[1], rightFootFallback);
            }

            Transform muzzle = GetUniqueNamedTransform(root, "Socket_MuzzlePrimary", true);
            Transform weaponAudio = GetUniqueNamedTransform(root, "Socket_WeaponAudio", true);
            Transform engineAudio = GetUniqueNamedTransform(root, "Socket_EngineAudio", true);
            for (int i = 0; i < footContacts.Length; i++)
            {
                EnsureRootLocalFootContact(root, footContacts[i], footContactNames[i]);
            }
            weaponAudio.SetPositionAndRotation(muzzle.position, muzzle.rotation);
            if (Vector3.Distance(engineAudio.position, root.transform.position) < 0.05f)
            {
                Vector3 fallbackEnginePosition = new Vector3(
                    modelBounds.center.x,
                    Mathf.Lerp(modelBounds.min.y, modelBounds.max.y, 0.30f),
                    modelBounds.min.z + modelBounds.size.z * 0.18f);
                engineAudio.position = root.transform.TransformPoint(fallbackEnginePosition);
            }

            float barrelSpacing = Mathf.Max(modelBounds.size.x * 0.10f, 0.06f);
            Transform firePoint0 = EnsureFirePoint(
                root, muzzle, "FirePoint_0", new Vector3(-barrelSpacing, 0.0f, 0.0f), rapidFireMuzzle);
            Transform firePoint1 = EnsureFirePoint(
                root, muzzle, "FirePoint_1", new Vector3(barrelSpacing, 0.0f, 0.0f), rapidFireMuzzle);
            if (firePoint0 == firePoint1)
            {
                throw new InvalidDataException("FirePoint_0 and FirePoint_1 must be distinct on " + root.name + ".");
            }

            ValidateCanonicalWalkerSockets(
                root, root.name, quadrupedCrawler, requireFootContactSockets);
        }

        private static void ValidateCanonicalWalkerSockets(
            GameObject root,
            string unitName,
            bool quadrupedCrawler = false,
            bool requireFootContactSockets = true)
        {
            string[] footContactNames = !requireFootContactSockets
                ? new string[0]
                : quadrupedCrawler
                    ? new[]
                    {
                        "Socket_FootContact_FL",
                        "Socket_FootContact_FR",
                        "Socket_FootContact_RL",
                        "Socket_FootContact_RR"
                    }
                    : new[]
                    {
                        "Socket_FootContact_L",
                        "Socket_FootContact_R"
                    };
            string[] requiredSocketNames =
            {
                "Socket_MuzzlePrimary",
                "Socket_EngineAudio",
                "Socket_WeaponAudio",
                "Socket_DamageSmoke",
                "Socket_DamageFire"
            };
            for (int i = 0; i < requiredSocketNames.Length; i++)
            {
                GetUniqueNamedTransform(root, requiredSocketNames[i], true);
            }

            for (int i = 0; i < footContactNames.Length; i++)
            {
                GetUniqueNamedTransform(root, footContactNames[i], true);
            }

            Transform muzzle = GetUniqueNamedTransform(root, "Socket_MuzzlePrimary", true);
            Transform weaponAudio = GetUniqueNamedTransform(root, "Socket_WeaponAudio", true);
            if (Vector3.Distance(muzzle.position, weaponAudio.position) > 0.75f)
            {
                throw new InvalidDataException(unitName + " has Socket_WeaponAudio too far from Socket_MuzzlePrimary.");
            }

            for (int i = 0; i < footContactNames.Length; i++)
            {
                EnsureRootLocalFootContact(root,
                    GetUniqueNamedTransform(root, footContactNames[i], true), footContactNames[i]);
            }

            ValidateFirePoint(root, muzzle, "FirePoint_0");
            ValidateFirePoint(root, muzzle, "FirePoint_1");
        }

        private static Transform EnsureUniqueSocket(GameObject root, string name, Vector3 fallbackPosition)
        {
            Transform existing = GetUniqueNamedTransform(root, name, false);
            if (existing != null)
            {
                return existing;
            }

            var socketObject = new GameObject(name);
            socketObject.transform.SetParent(root.transform, false);
            socketObject.transform.localPosition = fallbackPosition;
            socketObject.transform.localRotation = Quaternion.identity;
            socketObject.transform.localScale = Vector3.one;
            return socketObject.transform;
        }

        private static Transform EnsureFirePoint(
            GameObject root,
            Transform muzzle,
            string name,
            Vector3 fallbackPosition,
            bool rapidFireMuzzle)
        {
            Transform firePoint = GetUniqueNamedTransform(root, name, false);
            if (firePoint == null)
            {
                var firePointObject = new GameObject(name);
                firePointObject.transform.SetParent(muzzle, false);
                firePointObject.transform.localPosition = fallbackPosition;
                firePointObject.transform.localRotation = Quaternion.identity;
                firePointObject.transform.localScale = Vector3.one;
                firePoint = firePointObject.transform;
            }
            else if (firePoint.parent != muzzle)
            {
                firePoint.SetParent(muzzle, true);
            }

            EnsureMuzzleFlashParticleSystem(firePoint, rapidFireMuzzle);
            return firePoint;
        }

        private static void EnsureMuzzleFlashParticleSystem(Transform firePoint, bool rapidFireMuzzle)
        {
            ParticleSystem particleSystem = null;
            Transform particleObject = null;
            for (int i = 0; i < firePoint.childCount; i++)
            {
                Transform child = firePoint.GetChild(i);
                ParticleSystem candidate = child.GetComponent<ParticleSystem>();
                if (candidate != null)
                {
                    particleSystem = candidate;
                    particleObject = child;
                    break;
                }
            }

            if (particleSystem == null)
            {
                var particleGameObject = new GameObject("MuzzleFlash");
                particleGameObject.transform.SetParent(firePoint, false);
                particleGameObject.transform.localPosition = Vector3.zero;
                particleGameObject.transform.localRotation = Quaternion.identity;
                particleGameObject.transform.localScale = Vector3.one;
                particleSystem = particleGameObject.AddComponent<ParticleSystem>();
                particleObject = particleGameObject.transform;
            }

            particleObject.SetSiblingIndex(0);
            ParticleSystem.MainModule main = particleSystem.main;
            main.duration = rapidFireMuzzle ? 0.05f : 0.10f;
            main.loop = false;
            main.prewarm = false;
            main.playOnAwake = false;
            main.startLifetime = rapidFireMuzzle ? 0.04f : 0.06f;
            main.startSpeed = rapidFireMuzzle ? 3.0f : 0.0f;
            main.startSize = rapidFireMuzzle ? 0.9f : 1.8f;
            main.startColor = rapidFireMuzzle
                ? new Color(1.0f, 0.70f, 0.31f, 1.0f)
                : new Color(1.0f, 0.86f, 0.55f, 1.0f);
            main.maxParticles = rapidFireMuzzle ? 6 : 2;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = 0.0f;
            short burstCount = (short)(rapidFireMuzzle ? 2 : 1);
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, burstCount) });

            ParticleSystem.ShapeModule shape = particleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = rapidFireMuzzle ? 8.0f : 12.0f;
            shape.radius = 0.05f;

            ParticleSystemRenderer particleRenderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (particleRenderer == null)
            {
                throw new InvalidDataException("Unity did not create a ParticleSystemRenderer for " + firePoint.name + ".");
            }
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.enableGPUInstancing = true;
            particleRenderer.sharedMaterial = GetOrCreateMuzzleFlashMaterial();
        }

        private static Material GetOrCreateMuzzleFlashMaterial()
        {
            const string materialPath = MaterialRoot + "/RogueCommandMuzzleFlash.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The URP Particles/Unlit shader is required for walker muzzle flashes.");
            }

            if (material == null)
            {
                material = new Material(shader);
                material.name = "RogueCommandMuzzleFlash";
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.color = new Color(1.0f, 0.62f, 0.20f, 0.92f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ValidateFirePoint(GameObject root, Transform muzzle, string name)
        {
            Transform firePoint = GetUniqueNamedTransform(root, name, true);
            if (firePoint.parent != muzzle)
            {
                throw new InvalidDataException(name + " must be nested directly under Socket_MuzzlePrimary.");
            }
            if (firePoint.childCount == 0 || firePoint.GetChild(0).GetComponent<ParticleSystem>() == null)
            {
                throw new InvalidDataException(
                    name + " must have its ParticleSystem on child index 0 for native muzzle flash discovery.");
            }

            ParticleSystem.MainModule main = firePoint.GetChild(0).GetComponent<ParticleSystem>().main;
            if (main.playOnAwake || main.loop)
            {
                throw new InvalidDataException(name + " muzzle flash ParticleSystem must be non-looping and not play on awake.");
            }
        }

        private static void EnsureBastionAudioSources(GameObject root, AudioClip engineLoop, AudioClip weaponShot)
        {
            Transform engineSocket = GetUniqueNamedTransform(root, "Socket_EngineAudio", true);
            Transform weaponSocket = GetUniqueNamedTransform(root, "Socket_WeaponAudio", true);
            Transform muzzle = GetUniqueNamedTransform(root, "Socket_MuzzlePrimary", true);
            if (Vector3.Distance(weaponSocket.position, muzzle.position) > 0.03f)
            {
                throw new InvalidDataException(
                    "Bastion Siege Mech Socket_WeaponAudio must be colocated with Socket_MuzzlePrimary.");
            }
            if (Vector3.Distance(engineSocket.position, root.transform.position) < 0.10f)
            {
                throw new InvalidDataException(
                    "Bastion Siege Mech Socket_EngineAudio must be authored at its engine deck or exhaust midpoint.");
            }

            ConfigureBastionAudioSource(engineSocket, "EngineLoopAudioSource", engineLoop, true, 0.55f);
            ConfigureBastionAudioSource(weaponSocket, "WeaponShotAudioSource", weaponShot, false, 0.85f);
        }

        private static void ConfigureBastionAudioSource(
            Transform socket,
            string sourceName,
            AudioClip clip,
            bool loop,
            float volume)
        {
            if (socket == null || clip == null || clip.channels != 1)
            {
                throw new InvalidDataException(sourceName + " requires a non-null mono AudioClip and canonical socket.");
            }

            Transform sourceTransform = GetUniqueNamedTransform(socket.root.gameObject, sourceName, false);
            if (sourceTransform == null)
            {
                var sourceObject = new GameObject(sourceName);
                sourceTransform = sourceObject.transform;
                sourceTransform.SetParent(socket, false);
            }
            else if (sourceTransform.parent != socket)
            {
                sourceTransform.SetParent(socket, false);
            }

            AudioSource[] existingSources = socket.GetComponentsInChildren<AudioSource>(true);
            if (existingSources.Length > 1)
            {
                throw new InvalidDataException(socket.name + " must contain exactly one authored AudioSource.");
            }

            AudioSource source = sourceTransform.GetComponent<AudioSource>();
            if (source == null)
            {
                source = sourceTransform.gameObject.AddComponent<AudioSource>();
            }

            source.clip = clip;
            source.enabled = true;
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 1.0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 3.0f;
            source.maxDistance = 60.0f;
            source.dopplerLevel = 0.0f;
            source.volume = volume;
            source.priority = loop ? 192 : 128;
            EditorUtility.SetDirty(source);
        }

        private static void ImportBastionAudioClip(string assetPath, bool engineLoop)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException("Add the authored mono Bastion audio asset before building.", absolutePath);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            AudioImporter importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            if (importer == null)
            {
                throw new InvalidDataException("Unity did not create an AudioImporter for " + assetPath + ".");
            }

            importer.forceToMono = true;
            importer.ambisonic = false;
            importer.loadInBackground = false;
            importer.preloadAudioData = true;
            importer.defaultSampleSettings = new AudioImporterSampleSettings
            {
                loadType = engineLoop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad,
                compressionFormat = engineLoop ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM,
                quality = engineLoop ? 0.45f : 1.0f,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate
            };
            importer.assetBundleName = BundleName;
            importer.assetBundleVariant = String.Empty;
            importer.SaveAndReimport();

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            if (clip == null || clip.channels != 1)
            {
                throw new InvalidDataException(assetPath + " must import as a mono AudioClip.");
            }
        }

        private static void EnsureBastionDamageVfx(GameObject root)
        {
            Transform smokeSocket = GetUniqueNamedTransform(root, "Socket_DamageSmoke", true);
            Transform fireSocket = GetUniqueNamedTransform(root, "Socket_DamageFire", true);
            ValidateBastionDamageAnchors(root);
            ConfigureDamageSmokeParticleSystem(root, smokeSocket);
            ConfigureDamageFireParticleSystem(root, fireSocket);
        }

        private static void ValidateBastionDamageAnchors(GameObject root)
        {
            Transform engineSocket = GetUniqueNamedTransform(root, "Socket_EngineAudio", true);
            Transform damageSmokeSocket = GetUniqueNamedTransform(root, "Socket_DamageSmoke", true);
            Transform reactorCore = GetUniqueNamedTransform(root, "Socket_ReactorCore", true);
            Transform damageFireSocket = GetUniqueNamedTransform(root, "Socket_DamageFire", true);
            GameObject model = GetUniqueNamedTransform(root, "BastionSiegeMechModel", true).gameObject;
            Bounds modelBounds = GetModelBoundsInRootSpace(root, model);
            float largestDimension = Mathf.Max(modelBounds.size.x, modelBounds.size.y, modelBounds.size.z);
            float smokeTolerance = largestDimension * 0.30f;
            float fireTolerance = largestDimension * 0.10f;
            if (largestDimension <= 0.0f ||
                Vector3.Distance(engineSocket.position, damageSmokeSocket.position) > smokeTolerance)
            {
                throw new InvalidDataException(
                    "Bastion Siege Mech damage smoke must be anchored within 30% of its engine/exhaust midpoint.");
            }
            if (Vector3.Distance(reactorCore.position, damageFireSocket.position) > fireTolerance)
            {
                throw new InvalidDataException(
                    "Bastion Siege Mech damage fire must be anchored within 10% of its reactor core.");
            }
        }

        private static void EnsureBastionFootContactVfx(GameObject root)
        {
            string[] footSuffixes = { "FL", "FR", "RL", "RR" };
            for (int i = 0; i < footSuffixes.Length; i++)
            {
                string suffix = footSuffixes[i];
                ConfigureFootContactParticleSystem(root,
                    GetUniqueNamedTransform(root, "Socket_FootContact_" + suffix, true),
                    "FootstepDustVFX_" + suffix);
            }
        }

        private static ParticleSystem EnsureParticleSystemChild(GameObject root, Transform socket, string name)
        {
            Transform particleTransform = GetUniqueNamedTransform(root, name, false);
            if (particleTransform == null)
            {
                var particleObject = new GameObject(name);
                particleTransform = particleObject.transform;
                particleTransform.SetParent(socket, false);
            }
            else if (particleTransform.parent != socket)
            {
                particleTransform.SetParent(socket, false);
            }

            ParticleSystem[] systems = particleTransform.GetComponents<ParticleSystem>();
            if (systems.Length > 1)
            {
                throw new InvalidDataException(name + " must not contain duplicate ParticleSystem components.");
            }

            ParticleSystem particleSystem = systems.Length == 1
                ? systems[0]
                : particleTransform.gameObject.AddComponent<ParticleSystem>();
            ParticleSystemRenderer particleRenderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (particleRenderer == null)
            {
                throw new InvalidDataException("Unity did not create a ParticleSystemRenderer for " + name + ".");
            }
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.enableGPUInstancing = true;
            return particleSystem;
        }

        private static void ConfigureDamageSmokeParticleSystem(GameObject root, Transform socket)
        {
            ParticleSystem particleSystem = EnsureParticleSystemChild(root, socket, "DamageSmokeVFX");
            particleSystem.transform.localPosition = Vector3.zero;
            particleSystem.transform.localRotation = Quaternion.identity;
            particleSystem.transform.localScale = Vector3.one;

            ParticleSystem.MainModule main = particleSystem.main;
            main.duration = 1.0f;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
            main.startColor = new Color(0.18f, 0.18f, 0.20f, 0.84f);
            main.gravityModifier = -0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 35;

            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = 12.0f;
            emission.rateOverDistance = 0.0f;

            ParticleSystem.ShapeModule shape = particleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.15f;

            ParticleSystem.SizeOverLifetimeModule size = particleSystem.sizeOverLifetime;
            size.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0.0f, 0.45f);
            sizeCurve.AddKey(0.5f, 1.0f);
            sizeCurve.AddKey(1.0f, 2.8f);
            size.size = new ParticleSystem.MinMaxCurve(1.0f, sizeCurve);

            ParticleSystem.ColorOverLifetimeModule color = particleSystem.colorOverLifetime;
            color.enabled = true;
            color.color = CreateDamageSmokeGradient();

            ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GetOrCreateParticleMaterial("BastionDamageSmoke", false);
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static void ConfigureDamageFireParticleSystem(GameObject root, Transform socket)
        {
            ParticleSystem particleSystem = EnsureParticleSystemChild(root, socket, "DamageFireVFX");
            particleSystem.transform.localPosition = Vector3.zero;
            particleSystem.transform.localRotation = Quaternion.identity;
            particleSystem.transform.localScale = Vector3.one;

            ParticleSystem.MainModule main = particleSystem.main;
            main.duration = 0.8f;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
            main.startColor = new Color(1.0f, 0.55f, 0.12f, 0.96f);
            main.gravityModifier = -0.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 25;

            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = 18.0f;
            emission.rateOverDistance = 0.0f;

            ParticleSystem.ShapeModule shape = particleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10.0f;
            shape.radius = 0.05f;

            ParticleSystem.ColorOverLifetimeModule color = particleSystem.colorOverLifetime;
            color.enabled = true;
            color.color = CreateFireGradient();

            ParticleSystem.SizeOverLifetimeModule size = particleSystem.sizeOverLifetime;
            size.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0.0f, 0.35f);
            sizeCurve.AddKey(0.5f, 1.0f);
            sizeCurve.AddKey(1.0f, 0.1f);
            size.size = new ParticleSystem.MinMaxCurve(1.0f, sizeCurve);

            ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GetOrCreateParticleMaterial("BastionDamageFire", true);
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static void ConfigureFootContactParticleSystem(GameObject root, Transform socket, string name)
        {
            ParticleSystem particleSystem = EnsureParticleSystemChild(root, socket, name);
            particleSystem.transform.localPosition = Vector3.zero;
            particleSystem.transform.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
            particleSystem.transform.localScale = Vector3.one;

            ParticleSystem.MainModule main = particleSystem.main;
            main.duration = 1.0f;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startColor = new Color(0.60f, 0.52f, 0.40f, 0.42f);
            main.gravityModifier = 0.1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 20;

            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = 0.0f;
            emission.rateOverDistance = 2.5f;

            ParticleSystem.ShapeModule shape = particleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.08f;

            ParticleSystem.ColorOverLifetimeModule color = particleSystem.colorOverLifetime;
            color.enabled = true;
            color.color = CreateFadeGradient(new Color(0.70f, 0.62f, 0.49f, 0.52f));

            ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GetOrCreateParticleMaterial("BastionFootDust", false);
        }

        private static Gradient CreateFireGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.0f, 0.91f, 0.40f), 0.0f),
                    new GradientColorKey(new Color(1.0f, 0.38f, 0.05f), 0.48f),
                    new GradientColorKey(new Color(0.22f, 0.045f, 0.012f), 1.0f)
                },
                new[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(0.92f, 0.55f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            return gradient;
        }

        private static Gradient CreateDamageSmokeGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.18f, 0.18f, 0.20f), 0.0f),
                    new GradientColorKey(new Color(0.18f, 0.18f, 0.20f), 1.0f)
                },
                new[]
                {
                    new GradientAlphaKey(0.0f, 0.0f),
                    new GradientAlphaKey(0.86f, 0.10f),
                    new GradientAlphaKey(0.86f, 0.60f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            return gradient;
        }

        private static Gradient CreateFadeGradient(Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0.0f), new GradientColorKey(color, 1.0f) },
                new[] { new GradientAlphaKey(color.a * 0.65f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) });
            return gradient;
        }

        private static Material GetOrCreateParticleMaterial(string materialName, bool additive)
        {
            string materialPath = MaterialRoot + "/" + materialName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                throw new InvalidOperationException("The URP Particles/Unlit shader is required for Bastion VFX.");
            }

            if (material == null)
            {
                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.color = Color.white;
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1.0f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", additive ? 2.0f : 0.0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            }
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0.0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (material.HasProperty("_SoftParticlesEnabled"))
            {
                material.SetFloat("_SoftParticlesEnabled", 1.0f);
                material.EnableKeyword("_SOFTPARTICLES_ON");
            }
            material.renderQueue = (int)RenderQueue.Transparent;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureRootLocalFootContact(GameObject root, Transform footContact, string socketName)
        {
            float localHeight = root.transform.InverseTransformPoint(footContact.position).y;
            if (Mathf.Abs(localHeight) > 0.03f)
            {
                throw new InvalidDataException(socketName + " must rest on the Unity ground plane Y = 0.0.");
            }
        }

        private static Transform GetUniqueNamedTransform(GameObject root, string name, bool required)
        {
            List<Transform> matches = FindNamedTransforms(root, name);
            if (matches.Count > 1)
            {
                throw new InvalidDataException("The visual contains duplicate " + name + " transforms.");
            }
            if (matches.Count == 0)
            {
                if (required)
                {
                    throw new InvalidDataException("The visual is missing required transform " + name + ".");
                }
                return null;
            }
            if (!String.Equals(matches[0].name, name, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The visual transform must use the canonical name " + name + ".");
            }

            return matches[0];
        }

        private static List<Transform> FindNamedTransforms(GameObject root, string name)
        {
            var matches = new List<Transform>();
            if (root == null)
            {
                return matches;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null &&
                    String.Equals(transforms[i].name, name, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(transforms[i]);
                }
            }

            return matches;
        }

        private static Bounds GetModelBoundsInRootSpace(GameObject root, GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            bool hasPoint = false;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                Bounds bounds = renderers[i].bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 worldCorner = new Vector3(
                        (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                    Vector3 localCorner = root.transform.InverseTransformPoint(worldCorner);
                    if (!hasPoint)
                    {
                        min = localCorner;
                        max = localCorner;
                        hasPoint = true;
                    }
                    else
                    {
                        min = Vector3.Min(min, localCorner);
                        max = Vector3.Max(max, localCorner);
                    }
                }
            }

            if (!hasPoint)
            {
                throw new InvalidDataException("The walker model has no renderer bounds for socket fallback placement.");
            }

            return new Bounds((min + max) * 0.5f, max - min);
        }

        private static void ReplaceRiggedModelFromSource(
            GameObject prefabInstance,
            GameObject modelAsset,
            string unitName)
        {
            // The sample prefab is serialized as a flattened copy of the FBX. Refreshing only
            // its Animator leaves the old bone hierarchy in the bundle, so Arachnid would still
            // inherit Bone_Hull's idle motion. Replace that model subtree from the current FBX
            // while preserving the authored prefab materials and root-level sockets.
            // Overlord is included so its authored turret sockets reach the bundle.
            if (!String.Equals(unitName, "ArachnidHexapod", StringComparison.Ordinal) &&
                !String.Equals(unitName, "OverlordCommandHQ", StringComparison.Ordinal))
            {
                return;
            }

            Transform oldModel = GetUniqueNamedTransform(prefabInstance, unitName + "Model", true);
            Transform parent = oldModel.parent;
            Vector3 localPosition = oldModel.localPosition;
            Quaternion localRotation = oldModel.localRotation;
            Vector3 localScale = oldModel.localScale;
            int siblingIndex = oldModel.GetSiblingIndex();
            var materialsByPath = new Dictionary<string, Material[]>();
            Renderer[] oldRenderers = oldModel.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < oldRenderers.Length; i++)
            {
                Renderer renderer = oldRenderers[i];
                if (renderer != null)
                {
                    materialsByPath[GetRelativeTransformPath(oldModel, renderer.transform)] = renderer.sharedMaterials;
                }
            }

            GameObject replacement = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
            if (replacement == null)
            {
                throw new InvalidOperationException("Unity could not instantiate the refreshed " + unitName + " FBX.");
            }

            replacement.name = oldModel.name;
            replacement.transform.SetParent(parent, false);
            replacement.transform.localPosition = localPosition;
            replacement.transform.localRotation = localRotation;
            replacement.transform.localScale = localScale;
            replacement.transform.SetSiblingIndex(siblingIndex);

            Renderer[] replacementRenderers = replacement.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < replacementRenderers.Length; i++)
            {
                Renderer renderer = replacementRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Material[] materials;
                if (materialsByPath.TryGetValue(GetRelativeTransformPath(replacement.transform, renderer.transform), out materials))
                {
                    renderer.sharedMaterials = materials;
                }
            }

            UnityEngine.Object.DestroyImmediate(oldModel.gameObject);
        }

        private static string GetRelativeTransformPath(Transform root, Transform child)
        {
            var segments = new List<string>();
            Transform current = child;
            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }
            segments.Reverse();
            return String.Join("/", segments.ToArray());
        }

        private static void RefreshRiggedPbrPrefabAnimator(
            string unitName,
            string modelPath,
            string prefabPath)
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (modelAsset == null || prefabAsset == null)
            {
                throw new FileNotFoundException(
                    "The rigged PBR visual requires both model and prefab assets for " + unitName + ".");
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException("Unity could not instantiate the existing " + unitName + " visual prefab.");
            }
            PrefabUtility.UnpackPrefabInstance(
                instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            try
            {
                Material unifiedPbr = CreateUnifiedSamplePbrMaterial(unitName);
                ReplaceRiggedModelFromSource(instance, modelAsset, unitName);
                Transform modelTransform = GetUniqueNamedTransform(instance, unitName + "Model", false);
                GameObject modelObject = modelTransform != null ? modelTransform.gameObject : instance;
                if (modelObject != instance)
                {
                    var modelSocketNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Transform child in modelObject.GetComponentsInChildren<Transform>(true))
                    {
                        if (child != null &&
                            (child.name.StartsWith("Socket_", StringComparison.OrdinalIgnoreCase) ||
                             child.name.StartsWith("FirePoint_", StringComparison.OrdinalIgnoreCase)))
                        {
                            modelSocketNames.Add(child.name);
                        }
                    }

                    var staleSockets = new List<GameObject>();
                    foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                    {
                        if (t == null || t == instance.transform || t.IsChildOf(modelObject.transform))
                        {
                            continue;
                        }
                        if (modelSocketNames.Contains(t.name) &&
                            t.GetComponent<Renderer>() == null &&
                            t.GetComponent<AudioSource>() == null &&
                            t.GetComponent<ParticleSystem>() == null)
                        {
                            staleSockets.Add(t.gameObject);
                        }
                    }
                    for (int i = 0; i < staleSockets.Count; i++)
                    {
                        if (staleSockets[i] != null)
                        {
                            UnityEngine.Object.DestroyImmediate(staleSockets[i]);
                        }
                    }
                }
                AssignUnifiedPbrMaterial(modelObject, unifiedPbr);
                VehiclePrefabBuilder.SetupAnimatorIfAvailable(
                    instance, instance, modelAsset, unitName, preferWalkForMovement: true);
                Animator animator = instance.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isValid ||
                    animator.runtimeAnimatorController == null)
                {
                    throw new InvalidDataException(
                        unitName + " visual prefab must have one valid Generic Animator after refresh.");
                }

                animator.applyRootMotion = false;
                VehiclePrefabBuilder.EnsureCanonicalVfxSockets(instance, instance);
                EnsureTurretAndRadarSockets(instance, instance, requireTurret: true);
                VehiclePrefabBuilder.EnsureCanonicalHardpointSockets(instance, modelObject);
                VehiclePrefabBuilder.SetupCompanionLodGroup(instance, modelObject, unitName, unifiedPbr);
                bool success;
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Unity could not save refreshed " + prefabPath + ".");
                }
                ValidateSavedPrefab(prefabPath, unitName);
                Debug.Log("[RogueCommandVisualSampleScaffolder] Refreshed body-driven Animator graph, LODGroup, and PBR material for " + unitName + ".");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void RefreshFactoryPbrPrefab(
            string unitName,
            string modelPath,
            string prefabPath)
        {
            string effectiveModelPath = ResolveEffectiveModelPath(unitName, modelPath);
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(effectiveModelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException("Factory FBX is missing for " + unitName + ": " + effectiveModelPath, effectiveModelPath);
            }

            Material unifiedPbr = CreateUnifiedSamplePbrMaterial(unitName);
            var root = new GameObject(unitName + "Visual");
            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null) modelInstance = UnityEngine.Object.Instantiate(modelAsset);
                if (modelInstance == null)
                {
                    throw new InvalidOperationException("Unity could not instantiate factory model " + unitName + ".");
                }
                modelInstance.name = unitName + "Model";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;

                Renderer[] allRenderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < allRenderers.Length; i++)
                {
                    if (allRenderers[i].name.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        allRenderers[i].enabled = false;
                        MeshFilter mf = allRenderers[i].GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null)
                        {
                            BoxCollider bc = allRenderers[i].GetComponent<BoxCollider>();
                            if (bc == null) bc = allRenderers[i].gameObject.AddComponent<BoxCollider>();
                            bc.center = mf.sharedMesh.bounds.center;
                            bc.size = mf.sharedMesh.bounds.size;
                        }
                    }
                }

                AssignUnifiedPbrMaterial(modelInstance, unifiedPbr);
                Animator[] importedAnimators = modelInstance.GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < importedAnimators.Length; i++)
                {
                    if (importedAnimators[i] != null) UnityEngine.Object.DestroyImmediate(importedAnimators[i]);
                }

                VehiclePrefabBuilder.SetupCompanionLodGroup(root, modelInstance, unitName, unifiedPbr);

                string directory = Path.GetDirectoryName(prefabPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                    Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, directory)));
                }
                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Unity could not save refreshed factory prefab " + prefabPath + ".");
                }
                ValidateSavedPrefab(prefabPath, unitName);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static string CreateSampleVisual()
        {
            return CreateRiggedWalkerVisual(
                "SandlanceScout",
                ModelPath,
                PrefabPath,
                CreateScoutMaterials,
                AssignScoutMaterials,
                false,
                null,
                false,
                requireFootContactSockets: false,
                hasTurretOrRadar: true);
        }

        private static string CreateSkirmishBikeVisual()
        {
            return CreateRiggedWalkerVisual(
                "SkirmishStrikeBike",
                BikeModelPath,
                BikePrefabPath,
                CreateSkirmishBikeMaterials,
                AssignSkirmishBikeMaterials,
                false,
                null,
                false,
                requireFootContactSockets: false,
                hasTurretOrRadar: true);
        }

        private static string CreateColossusVisual()
        {
            return CreateRiggedWalkerVisual(
                "ColossusSiegePlatform",
                ColossusModelPath,
                ColossusPrefabPath,
                CreateColossusMaterials,
                AssignColossusMaterials,
                false,
                null,
                false,
                requireFootContactSockets: false,
                hasTurretOrRadar: true);
        }

        private static string CreateIroncladVisual()
        {
            return CreateRiggedWalkerVisual(
                "IroncladWalker",
                IroncladModelPath,
                IroncladPrefabPath,
                CreateIroncladMaterials,
                AssignIroncladMaterials,
                true,
                null,
                false,
                requireFootContactSockets: true,
                hasTurretOrRadar: true);
        }

        private static string CreateAtlasCraneMechVisual()
        {
            return CreateRiggedWalkerVisual(
                "AtlasCraneMech",
                AtlasCraneMechModelPath,
                AtlasCraneMechPrefabPath,
                CreateAtlasCraneMechMaterials,
                AssignAtlasCraneMechMaterials,
                false,
                null,
                false,
                requireFootContactSockets: true,
                hasTurretOrRadar: true);
        }

        private static string CreateBastionSiegeMechVisual()
        {
            ImportBastionAudioClip(BastionEngineLoopAudioPath, true);
            ImportBastionAudioClip(BastionWeaponShotAudioPath, false);

            AudioClip engineLoop = AssetDatabase.LoadAssetAtPath<AudioClip>(BastionEngineLoopAudioPath);
            AudioClip weaponShot = AssetDatabase.LoadAssetAtPath<AudioClip>(BastionWeaponShotAudioPath);
            if (engineLoop == null || weaponShot == null)
            {
                throw new InvalidDataException("Bastion Siege Mech audio clips could not be imported.");
            }

            return CreateRiggedWalkerVisual(
                "BastionSiegeMech",
                BastionSiegeMechModelPath,
                BastionSiegeMechPrefabPath,
                CreateBastionSiegeMechMaterials,
                AssignBastionSiegeMechMaterials,
                false,
                root =>
                {
                    EnsureBastionDamageVfx(root);
                    EnsureBastionFootContactVfx(root);
                    EnsureBastionAudioSources(root, engineLoop, weaponShot);
                },
                quadrupedCrawler: true,
                requireFootContactSockets: true,
                hasTurretOrRadar: true);
        }

        private static void SetupRiggedAnimatorOrThrow(
            GameObject root,
            GameObject modelInstance,
            GameObject modelAsset,
            string unitName,
            bool preferWalkForMovement)
        {
            Animator[] importedAnimators = modelInstance.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < importedAnimators.Length; i++)
            {
                if (importedAnimators[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(importedAnimators[i]);
                }
            }

            VehiclePrefabBuilder.SetupAnimatorIfAvailable(
                root, modelInstance, modelAsset, unitName, preferWalkForMovement);
            Animator animator = root.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid ||
                animator.runtimeAnimatorController == null)
            {
                throw new InvalidDataException(
                    unitName + " must import a valid Generic Avatar and the RCL animation controller.");
            }

            animator.applyRootMotion = false;
        }

        private static string ResolveEffectiveModelPath(string unitName, string baseModelPath)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(baseModelPath) != null)
                return baseModelPath;
            string[] guids = AssetDatabase.FindAssets(unitName + " t:Model", new[] { AssetRoot + "/Models" });
            if (guids != null)
            {
                string lodPath = null;
                string foundBase = null;
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith("_LODs.fbx", StringComparison.OrdinalIgnoreCase))
                        lodPath = path;
                    else if (string.Equals(Path.GetFileNameWithoutExtension(path), unitName, StringComparison.OrdinalIgnoreCase))
                        foundBase = path;
                }
                if (lodPath != null && foundBase == null)
                    return lodPath;
                if (foundBase != null)
                    return foundBase;
                if (lodPath != null)
                    return lodPath;
            }
            return baseModelPath;
        }

        /// <summary>
        /// Guarantees turret/radar sockets that VehiclePrefabBuilder alone does not
        /// add. Recursive search avoids duplicates (UniqueSocketRoleBinding).
        /// </summary>
        private static void EnsureTurretAndRadarSockets(GameObject root, GameObject modelInstance, bool requireTurret)
        {
            if (root == null || modelInstance == null || !requireTurret) return;
            Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;
            Bounds bounds = GetModelBoundsInRootSpace(root, modelInstance);
            if (bounds.size.sqrMagnitude < 1e-6f) return;
            Vector3 center = bounds.center;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3 size = bounds.size;
            Vector3 turretPivot = new Vector3(center.x, Mathf.Lerp(min.y, max.y, 0.72f), center.z + size.z * 0.10f);
            EnsureUniqueSocket(root, "Socket_TurretPivot", turretPivot);
            Transform pivot = GetUniqueNamedTransform(root, "Socket_TurretPivot", false);
            if (pivot != null && GetUniqueNamedTransform(root, "Socket_TurretPitch", false) == null)
            {
                GameObject pitch = new GameObject("Socket_TurretPitch");
                pitch.transform.SetParent(pivot, false);
                pitch.transform.localPosition = Vector3.zero;
                pitch.transform.localRotation = Quaternion.identity;
                pitch.transform.localScale = Vector3.one;
            }
            Vector3 radar = new Vector3(center.x - size.x * 0.22f, Mathf.Lerp(min.y, max.y, 0.85f), min.z + size.z * 0.30f);
            Vector3 antenna = new Vector3(center.x + size.x * 0.22f, Mathf.Lerp(min.y, max.y, 0.80f), min.z + size.z * 0.30f);
            EnsureUniqueSocket(root, "Socket_RadarMast", radar);
            EnsureUniqueSocket(root, "Socket_Antenna", antenna);
        }

        private static void ValidateSavedPrefab(string prefabPath, string unitName)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[VisualSampleScaffolder] Could not reload prefab for validation: " + prefabPath);
                return;
            }
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            int enabled = 0;
            foreach (Renderer r in renderers) if (r != null && r.enabled) enabled++;
            Animator animator = prefab.GetComponent<Animator>();
            string animatorState = animator == null ? "none" :
                ((animator.avatar != null && animator.avatar.isValid) ? "valid" : "invalid") +
                "/controller " + (animator.runtimeAnimatorController != null ? "present" : "missing");
            Debug.Log("[VisualSampleScaffolder] " + unitName + " prefab OK: renderers=" + renderers.Length +
                " (enabled " + enabled + "), animator=" + animatorState);
        }

        private static string CreateRiggedWalkerVisual(
            string unitName,
            string modelPath,
            string prefabPath,
            Func<Dictionary<string, Material>> createMaterials,
            Action<GameObject, IDictionary<string, Material>> assignMaterials,
            bool rapidFireMuzzle,
            Action<GameObject> configureAdditionalComponents = null,
            bool quadrupedCrawler = false,
            bool requireFootContactSockets = true,
            bool hasTurretOrRadar = false)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, AssetRoot)));
            AssetDatabase.Refresh();

            string effectiveModelPath = ResolveEffectiveModelPath(unitName, modelPath);
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(effectiveModelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException(
                    "Import the rigged " + unitName + " FBX before building the sample bundle.", effectiveModelPath);
            }

            Material unifiedPbr = CreateUnifiedSamplePbrMaterial(unitName);
            var root = new GameObject(unitName + "Visual");
            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null)
                {
                    throw new InvalidOperationException("Unity could not instantiate the authored " + unitName + ".");
                }

                modelInstance.name = unitName + "Model";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;
                AssignUnifiedPbrMaterial(modelInstance, unifiedPbr);

                VehiclePrefabBuilder.EnsureCanonicalVfxSockets(root, modelInstance);
                EnsureTurretAndRadarSockets(root, modelInstance, hasTurretOrRadar);
                VehiclePrefabBuilder.EnsureCanonicalHardpointSockets(root, modelInstance);
                EnsureCanonicalWalkerSockets(root, modelInstance, rapidFireMuzzle, quadrupedCrawler, requireFootContactSockets);
                if (configureAdditionalComponents != null)
                {
                    configureAdditionalComponents(root);
                }
                SetupRiggedAnimatorOrThrow(root, modelInstance, modelAsset, unitName, true);
                VehiclePrefabBuilder.SetupCompanionLodGroup(root, modelInstance, unitName, unifiedPbr);

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Unity could not save " + prefabPath + ".");
                }

                string absolutePrefabPath = Path.GetFullPath(Path.Combine(projectRoot, prefabPath));
                NormalizeEmptyUnityYamlStrings(absolutePrefabPath);
                string prefabMetadataPath = absolutePrefabPath + ".meta";
                if (File.Exists(prefabMetadataPath))
                {
                    NormalizeEmptyUnityYamlStrings(prefabMetadataPath);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetImporter importer = AssetImporter.GetAtPath(prefabPath);
            if (importer == null)
            {
                throw new FileNotFoundException("Unity did not import " + prefabPath + ".");
            }

            importer.assetBundleName = BundleName;
            importer.assetBundleVariant = String.Empty;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            NormalizeGeneratedUnityMetaFiles(projectRoot);
            NormalizeEmptyUnityYamlStrings(Path.GetFullPath(Path.Combine(projectRoot, prefabPath)) + ".meta");
            ValidateSavedPrefab(prefabPath, unitName);
            return prefabPath;
        }

        private static string CreateSurgeArcTankVisual()
        {
            return CreateRiggedWalkerVisual(
                "SurgeArcTank",
                SurgeArcTankModelPath,
                SurgeArcTankPrefabPath,
                CreateSurgeArcTankMaterials,
                AssignSurgeArcTankMaterials,
                false,
                null,
                false,
                requireFootContactSockets: false,
                hasTurretOrRadar: true);
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
                ValidateRiggedWalkerBundleAssets(bundle, assetNames,
                    "scouttankvisual.prefab", "ScoutTankVisual",
                    "sandlancescoutcard.png", "SandlanceScoutCard", "Sandlance Scout",
                    requireFootContactSockets: false);
                ValidateRiggedWalkerBundleAssets(bundle, assetNames,
                    "skirmishbikevisual.prefab", "SkirmishBikeVisual",
                    "skirmishbikecard.png", "SkirmishBikeCard", "Skirmish Strike Bike",
                    requireFootContactSockets: false);
                ValidateRiggedWalkerBundleAssets(bundle, assetNames,
                    "colossussiegevisual.prefab", "ColossusSiegeVisual",
                    "colossussiegecard.png", "ColossusSiegeCard", "Colossus Siege Platform",
                    requireFootContactSockets: false);
                ValidateRiggedWalkerBundleAssets(bundle, assetNames,
                    "ironcladwalkervisual.prefab", "IroncladWalkerVisual",
                    "ironcladwalkercard.png", "IroncladWalkerCard", "Ironclad Walker");
                ValidateRiggedWalkerBundleAssets(bundle, assetNames,
                    "atlascranemechvisual.prefab", "AtlasCraneMechVisual",
                    "atlascranemechcard.png", "AtlasCraneMechCard", "Atlas Crane Mech");
                ValidateRiggedWalkerBundleAssets(bundle, assetNames,
                    "bastionsiegemechvisual.prefab", "BastionSiegeMechVisual",
                    "bastionsiegemechcard.png", "BastionSiegeMechCard", "Bastion Siege Mech", true);
                ValidateRiggedWalkerBundleAssets(bundle, assetNames,
                    "surgearctankvisual.prefab", "SurgeArcTankVisual",
                    "surgearctankcard.png", "SurgeArcTankCard", "Surge Arc Tank",
                    requireFootContactSockets: false);
                ValidateVisualBundleAssets(bundle, assetNames, "arachnidhexapodvisual.prefab",
                    "ArachnidHexapodVisual", "arachnidhexapodcard.png", "ArachnidHexapodCard", 0, "Arachnid Hexapod");
                ValidateVisualBundleAssets(bundle, assetNames, "arachnidhexapodfactoryvisual.prefab",
                    "ArachnidHexapodFactoryVisual", null, null, 0, "Arachnid Hexapod Factory");
                ValidateVisualBundleAssets(bundle, assetNames, "overlordcommandhqvisual.prefab",
                    "OverlordCommandHQVisual", "overlordcommandhqcard.png", "OverlordCommandHQCard", 0, "Overlord Command HQ");
                ValidateVisualBundleAssets(bundle, assetNames, "overlordcommandhqfactoryvisual.prefab",
                    "OverlordCommandHQFactoryVisual", null, null, 0, "Overlord Command HQ Factory");
                ValidatePbrVisualBundlePrefab(bundle, assetNames, "scouttankvisual.prefab",
                    "ScoutTankVisual", "Sandlance Scout");
                ValidatePbrVisualBundlePrefab(bundle, assetNames, "skirmishbikevisual.prefab",
                    "SkirmishBikeVisual", "Skirmish Strike Bike");
                ValidatePbrVisualBundlePrefab(bundle, assetNames, "colossussiegevisual.prefab",
                    "ColossusSiegeVisual", "Colossus Siege Platform");
                ValidatePbrVisualBundlePrefab(bundle, assetNames, "ironcladwalkervisual.prefab",
                    "IroncladWalkerVisual", "Ironclad Walker");
                ValidatePbrVisualBundlePrefab(bundle, assetNames, "atlascranemechvisual.prefab",
                    "AtlasCraneMechVisual", "Atlas Crane Mech");
                ValidatePbrVisualBundlePrefab(bundle, assetNames, "bastionsiegemechvisual.prefab",
                    "BastionSiegeMechVisual", "Bastion Siege Mech");
                ValidatePbrVisualBundlePrefab(bundle, assetNames, "surgearctankvisual.prefab",
                    "SurgeArcTankVisual", "Surge Arc Tank");
                ValidatePbrVisualBundlePrefab(bundle, assetNames, "arachnidhexapodvisual.prefab",
                    "ArachnidHexapodVisual", "Arachnid Hexapod");
                ValidatePbrVisualBundlePrefab(bundle, assetNames, "arachnidhexapodfactoryvisual.prefab",
                    "ArachnidHexapodFactoryVisual", "Arachnid Hexapod Factory");
                ValidatePbrVisualBundlePrefab(bundle, assetNames, "overlordcommandhqvisual.prefab",
                    "OverlordCommandHQVisual", "Overlord Command HQ");
                ValidatePbrVisualBundlePrefab(bundle, assetNames, "overlordcommandhqfactoryvisual.prefab",
                    "OverlordCommandHQFactoryVisual", "Overlord Command HQ Factory");
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        private static void ValidateRiggedWalkerBundleAssets(
            AssetBundle bundle,
            string[] assetNames,
            string prefabSuffix,
            string prefabName,
            string cardSuffix,
            string cardName,
            string visualLabel,
            bool validateBastionIntegration = false,
            bool requireFootContactSockets = true)
        {
            string prefabAssetPath = FindUniqueAssetPath(assetNames, prefabSuffix, visualLabel + " prefab");
            GameObject prefab = bundle.LoadAsset<GameObject>(prefabAssetPath);
            if (prefab == null || !String.Equals(prefab.name, prefabName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(visualLabel + " prefab could not be loaded by its AssetBundle path.");
            }

            ValidateCardSpriteAsset(bundle, assetNames, cardSuffix, cardName, visualLabel);

            SkinnedMeshRenderer[] skinnedRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            bool hasBoundSkinnedMesh = false;
            for (int i = 0; i < skinnedRenderers.Length; i++)
            {
                if (skinnedRenderers[i] != null && skinnedRenderers[i].sharedMesh != null &&
                    skinnedRenderers[i].bones != null && skinnedRenderers[i].bones.Length > 0)
                {
                    hasBoundSkinnedMesh = true;
                    break;
                }
            }
            if (!hasBoundSkinnedMesh)
            {
                throw new InvalidDataException(visualLabel + " prefab has no skinned mesh with bound bones.");
            }

            Animator animator = prefab.GetComponent<Animator>();
            if (animator == null || animator.applyRootMotion || animator.avatar == null || !animator.avatar.isValid ||
                animator.runtimeAnimatorController == null)
            {
                throw new InvalidDataException(visualLabel +
                    " prefab must keep root motion disabled and include its valid Generic Avatar and controller.");
            }
            GameObject animatorProbe = null;
            try
            {
                animatorProbe = UnityEngine.Object.Instantiate(prefab);
                Animator probeAnimator = animatorProbe.GetComponent<Animator>();
                probeAnimator.Rebind();
                probeAnimator.Update(0f);

                if (!HasAnimatorParameter(probeAnimator, "RCL_IsMoving", AnimatorControllerParameterType.Bool) ||
                    !HasAnimatorParameter(probeAnimator, "RCL_Fire", AnimatorControllerParameterType.Trigger) ||
                    !HasAnimatorParameter(probeAnimator, "RCL_Death", AnimatorControllerParameterType.Trigger))
                {
                    AnimatorControllerParameter[] parameters = probeAnimator.parameters;
                    var parameterSummary = new StringBuilder();
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (i > 0) parameterSummary.Append(", ");
                        parameterSummary.Append(parameters[i].name).Append(":").Append(parameters[i].type);
                    }

                    throw new InvalidDataException(visualLabel +
                        " AnimatorController is missing the native RCL parameter contract after bundle instantiation " +
                        "(controller=" + probeAnimator.runtimeAnimatorController.name +
                        "; initialized=" + probeAnimator.isInitialized + "; parameters=" + parameterSummary + ").");
                }
            }
            finally
            {
                if (animatorProbe != null)
                {
                    UnityEngine.Object.DestroyImmediate(animatorProbe);
                }
            }

            ValidateCanonicalWalkerSockets(
                prefab, visualLabel, validateBastionIntegration, requireFootContactSockets);
            if (validateBastionIntegration)
            {
                ValidateBastionBundleIntegration(prefab);
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
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

            if (prefab.GetComponent<LODGroup>() == null)
            {
                throw new InvalidDataException(visualLabel + " PBR prefab is missing required LODGroup.");
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

        private static void ValidateBastionBundleIntegration(GameObject prefab)
        {
            ValidateBastionAudioSource(prefab, "Socket_EngineAudio", "EngineLoopAudioSource", true);
            ValidateBastionAudioSource(prefab, "Socket_WeaponAudio", "WeaponShotAudioSource", false);
            ValidateBastionParticleSystem(prefab, "Socket_DamageSmoke", "DamageSmokeVFX", true, false);
            ValidateBastionParticleSystem(prefab, "Socket_DamageFire", "DamageFireVFX", true, false);
            string[] footSuffixes = { "FL", "FR", "RL", "RR" };
            for (int i = 0; i < footSuffixes.Length; i++)
            {
                string suffix = footSuffixes[i];
                ValidateBastionParticleSystem(
                    prefab,
                    "Socket_FootContact_" + suffix,
                    "FootstepDustVFX_" + suffix,
                    true,
                    true);
            }

            Transform muzzle = GetUniqueNamedTransform(prefab, "Socket_MuzzlePrimary", true);
            ValidateFirePoint(prefab, muzzle, "FirePoint_0");
            ValidateFirePoint(prefab, muzzle, "FirePoint_1");

            ValidateBastionDamageAnchors(prefab);

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is ParticleSystemRenderer)
                {
                    continue;
                }

                Material[] materials = renderers[i].sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null || material.shader == null ||
                        !String.Equals(material.shader.name, VertexWearShaderName, StringComparison.Ordinal) ||
                        !material.HasProperty("_WearStrength") ||
                        Mathf.Abs(material.GetFloat("_WearStrength") - FactoryNewWearStrength) > 0.001f)
                    {
                        throw new InvalidDataException(
                            "Bastion Siege Mech mesh materials must use the bundled VertexWear shader at factory-new wear strength.");
                    }
                }
            }
        }

        private static void ValidateBastionAudioSource(
            GameObject root,
            string socketName,
            string sourceName,
            bool loop)
        {
            Transform socket = GetUniqueNamedTransform(root, socketName, true);
            Transform sourceTransform = GetUniqueNamedTransform(root, sourceName, true);
            if (sourceTransform.parent != socket)
            {
                throw new InvalidDataException(sourceName + " must be parented directly under " + socketName + ".");
            }

            AudioSource[] socketSources = socket.GetComponentsInChildren<AudioSource>(true);
            AudioSource source = sourceTransform.GetComponent<AudioSource>();
            if (socketSources.Length != 1 || source == null || source.clip == null || source.clip.channels != 1 ||
                source.loop != loop || source.playOnAwake || !source.enabled ||
                Mathf.Abs(source.spatialBlend - 1.0f) > 0.001f ||
                source.rolloffMode != AudioRolloffMode.Logarithmic ||
                Mathf.Abs(source.minDistance - 3.0f) > 0.001f ||
                Mathf.Abs(source.maxDistance - 60.0f) > 0.001f ||
                Mathf.Abs(source.dopplerLevel) > 0.001f)
            {
                throw new InvalidDataException(sourceName + " has a missing mono clip or invalid 3D source settings " +
                    "(count=" + socketSources.Length +
                    ", clip=" + (source != null && source.clip != null ? source.clip.name : "<null>") +
                    ", channels=" + (source != null && source.clip != null ? source.clip.channels.ToString() : "n/a") +
                    ", loop=" + (source != null && source.loop).ToString() +
                    ", playOnAwake=" + (source != null && source.playOnAwake).ToString() +
                    ", enabled=" + (source != null && source.enabled).ToString() +
                    ", spatialBlend=" + (source != null ? source.spatialBlend.ToString("F3") : "n/a") +
                    ", rolloff=" + (source != null ? source.rolloffMode.ToString() : "n/a") +
                    ", min=" + (source != null ? source.minDistance.ToString("F2") : "n/a") +
                    ", max=" + (source != null ? source.maxDistance.ToString("F2") : "n/a") +
                    ", doppler=" + (source != null ? source.dopplerLevel.ToString("F2") : "n/a") + ").");
            }
        }

        private static void ValidateBastionParticleSystem(
            GameObject root,
            string socketName,
            string particleName,
            bool expectLooping,
            bool expectDistanceEmission)
        {
            Transform socket = GetUniqueNamedTransform(root, socketName, true);
            Transform particleTransform = GetUniqueNamedTransform(root, particleName, true);
            if (particleTransform.parent != socket)
            {
                throw new InvalidDataException(particleName + " must be parented directly under " + socketName + ".");
            }

            ParticleSystem particleSystem = particleTransform.GetComponent<ParticleSystem>();
            ParticleSystemRenderer renderer = particleTransform.GetComponent<ParticleSystemRenderer>();
            if (particleSystem == null || renderer == null || renderer.sharedMaterial == null ||
                renderer.sharedMaterial.shader == null ||
                !renderer.sharedMaterial.shader.name.StartsWith("Universal Render Pipeline/Particles/", StringComparison.Ordinal) ||
                !renderer.enableGPUInstancing)
            {
                throw new InvalidDataException(particleName + " must use an instanced URP particle material.");
            }

            ParticleSystem.MainModule main = particleSystem.main;
            if (main.loop != expectLooping || main.playOnAwake != expectDistanceEmission)
            {
                throw new InvalidDataException(particleName + " has an invalid play/loop configuration.");
            }

            ParticleSystem.EmissionModule emission = particleSystem.emission;
            if (expectDistanceEmission)
            {
                if (emission.rateOverDistance.constant < 2.4f || emission.rateOverTime.constant > 0.01f)
                {
                    throw new InvalidDataException(particleName + " must emit dust by distance traveled only.");
                }
            }
            else if (emission.rateOverTime.constant <= 0.0f || main.maxParticles > 40)
            {
                throw new InvalidDataException(particleName + " must use a bounded damage emission rate.");
            }
        }

        private static bool HasAnimatorParameter(
            Animator animator,
            string parameterName,
            AnimatorControllerParameterType expectedType)
        {
            AnimatorControllerParameter[] parameters = animator != null
                ? animator.parameters
                : new AnimatorControllerParameter[0];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (String.Equals(parameters[i].name, parameterName, StringComparison.Ordinal) &&
                    parameters[i].type == expectedType)
                {
                    return true;
                }
            }

            return false;
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
                ValidateCardSpriteAsset(bundle, assetNames, cardSuffix, cardName, visualLabel);
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

            if (prefab.GetComponent<LODGroup>() == null)
            {
                throw new InvalidDataException(visualLabel + " PBR prefab is missing required LODGroup.");
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

        private static void ValidatePbrVisualBundlePrefab(
            AssetBundle bundle,
            string[] assetNames,
            string prefabSuffix,
            string prefabName,
            string visualLabel)
        {
            string prefabPath = FindUniqueAssetPath(assetNames, prefabSuffix, visualLabel + " prefab");
            GameObject prefab = bundle.LoadAsset<GameObject>(prefabPath);
            if (prefab == null || !String.Equals(prefab.name, prefabName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(visualLabel + " PBR prefab could not be loaded from the bundle.");
            }

            if (prefab.GetComponent<LODGroup>() == null)
            {
                throw new InvalidDataException(visualLabel + " PBR prefab is missing required LODGroup.");
            }

            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null || components[i] is MonoBehaviour)
                {
                    throw new InvalidDataException(visualLabel + " PBR prefab contains a missing or scripted component.");
                }
            }

            MeshFilter[] meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
            SkinnedMeshRenderer[] skinnedRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int checkedMeshCount = 0;
            var checkedMeshIds = new HashSet<int>();
            for (int i = 0; i < meshFilters.Length; i++)
            {
                ValidatePbrMeshVertexColors(meshFilters[i] == null ? null : meshFilters[i].sharedMesh,
                    visualLabel, checkedMeshIds, ref checkedMeshCount);
            }
            for (int i = 0; i < skinnedRenderers.Length; i++)
            {
                ValidatePbrMeshVertexColors(skinnedRenderers[i] == null ? null : skinnedRenderers[i].sharedMesh,
                    visualLabel, checkedMeshIds, ref checkedMeshCount);
            }
            if (checkedMeshCount == 0)
            {
                throw new InvalidDataException(visualLabel + " PBR prefab has no mesh to render.");
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            int materialCount = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!(renderers[i] is MeshRenderer) && !(renderers[i] is SkinnedMeshRenderer))
                {
                    continue;
                }

                Material[] materials = renderers[i].sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    throw new InvalidDataException(visualLabel + " mesh renderer has no material.");
                }
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null || material.shader == null ||
                        !String.Equals(material.shader.name, VertexWearShaderName, StringComparison.Ordinal) ||
                        !material.HasProperty("_WearStrength") ||
                        Mathf.Abs(material.GetFloat("_WearStrength") - FactoryNewWearStrength) > 0.001f)
                    {
                        throw new InvalidDataException(visualLabel + " material is missing " + VertexWearShaderName +
                            " or the expected vertex-wear setting.");
                    }

                    ValidatePbrMaterialTexture(material, "_BaseMap", true, visualLabel, "Albedo");
                    ValidatePbrMaterialTexture(material, "_MetallicGlossMap", true, visualLabel, "MetallicGloss");
                    ValidatePbrMaterialTexture(material, "_BumpMap", true, visualLabel, "Normal");
                    ValidatePbrMaterialTexture(material, "_EmissionMap", true, visualLabel, "Emission");
                    Texture detailMap = material.HasProperty("_DetailBumpMap") ? material.GetTexture("_DetailBumpMap") : null;
                    if (detailMap == null)
                    {
                        throw new InvalidDataException(visualLabel + " material is missing _DetailBumpMap (steel micro-detail normal).");
                    }
                    if (!material.IsKeywordEnabled("_METALLICSPECGLOSSMAP") ||
                        !material.IsKeywordEnabled("_NORMALMAP") || !material.IsKeywordEnabled("_EMISSION"))
                    {
                        throw new InvalidDataException(visualLabel + " material has a PBR texture without its shader keyword.");
                    }
                    materialCount++;
                }
            }

            if (materialCount == 0)
            {
                throw new InvalidDataException(visualLabel + " PBR prefab has no mesh material bindings.");
            }

            Debug.Log("Reopened PBR bundle asset " + visualLabel + ": " + checkedMeshCount +
                " mesh(es), " + materialCount + " material slot(s), RGBA vertex colors and four 2048px maps.");
        }

        private static void ValidatePbrMeshVertexColors(
            Mesh mesh,
            string visualLabel,
            HashSet<int> checkedMeshIds,
            ref int checkedMeshCount)
        {
            if (mesh == null || !checkedMeshIds.Add(mesh.GetInstanceID()))
            {
                if (mesh == null)
                {
                    throw new InvalidDataException(visualLabel + " PBR prefab contains a renderer with no mesh.");
                }
                return;
            }

            // Imported FBX meshes are intentionally non-readable in the shipped bundle.
            // Query the vertex attribute layout instead of mesh.colors32, which throws when
            // ModelImporter.isReadable is false even though Unity retained the GPU stream.
            if (!mesh.HasVertexAttribute(VertexAttribute.Color) ||
                mesh.GetVertexAttributeDimension(VertexAttribute.Color) != 4)
            {
                throw new InvalidDataException(visualLabel + " mesh " + mesh.name +
                    " lost its RGBA vertex-color stream.");
            }
            checkedMeshCount++;
        }

        private static void ValidatePbrMaterialTexture(
            Material material,
            string propertyName,
            bool required,
            string visualLabel,
            string mapLabel)
        {
            if (!material.HasProperty(propertyName))
            {
                if (required) throw new InvalidDataException(visualLabel + " material lacks " + mapLabel + " property " + propertyName + ".");
                return;
            }

            Texture texture = material.GetTexture(propertyName);
            if (texture == null)
            {
                if (required) throw new InvalidDataException(visualLabel + " material has no " + mapLabel + " texture.");
                return;
            }
            if (texture.width != 2048 || texture.height != 2048)
            {
                throw new InvalidDataException(visualLabel + " " + mapLabel + " imported at " + texture.width + "x" +
                    texture.height + "; expected 2048x2048.");
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

        private static void ValidateCardSpriteAsset(
            AssetBundle bundle,
            string[] assetNames,
            string cardSuffix,
            string cardName,
            string visualLabel)
        {
            string cardSpriteAssetPath = FindUniqueAssetPath(
                assetNames, cardSuffix, visualLabel + " card Sprite");
            Sprite cardSprite = bundle.LoadAsset<Sprite>(cardSpriteAssetPath);
            if (cardSprite == null || !String.Equals(cardSprite.name, cardName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(visualLabel + " card Sprite is missing or has the wrong asset name.");
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
                { "armor", CreateOrUpdateMaterial("IroncladArmor", new Color32(34, 58, 72, 255), 0.44f, 0.62f, useVertexWearShader: true) },
                { "lightArmor", CreateOrUpdateMaterial("IroncladLightArmor", new Color32(71, 102, 113, 255), 0.34f, 0.60f, useVertexWearShader: true) },
                { "gunmetal", CreateOrUpdateMaterial("IroncladGunmetal", new Color32(18, 27, 34, 255), 0.78f, 0.70f, useVertexWearShader: true) },
                { "warning", CreateOrUpdateMaterial("IroncladWarningAmber", new Color32(239, 104, 24, 255), 0.18f, 0.64f, new Color(0.94f, 0.30f, 0.06f) * 1.5f, true) },
                { "sensor", CreateOrUpdateMaterial("IroncladSensorCyan", new Color32(10, 184, 224, 255), 0.20f, 0.78f, new Color(0.04f, 0.72f, 0.88f) * 1.8f, true) }
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return materials;
        }

        private static Dictionary<string, Material> CreateAtlasCraneMechMaterials()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, MaterialRoot)));
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal)
            {
                { "armor", CreateOrUpdateMaterial("AtlasCranePrimaryArmor", new Color32(184, 198, 212, 255), 0.42f, 0.60f, useVertexWearShader: true) },
                { "lightArmor", CreateOrUpdateMaterial("AtlasCraneLightArmor", new Color32(128, 145, 164, 255), 0.34f, 0.60f, useVertexWearShader: true) },
                { "gunmetal", CreateOrUpdateMaterial("AtlasCraneGunmetal", new Color32(20, 26, 36, 255), 0.78f, 0.70f, useVertexWearShader: true) },
                { "tread", CreateOrUpdateMaterial("AtlasCraneTread", new Color32(13, 15, 20, 255), 0.18f, 0.32f, useVertexWearShader: true) },
                { "wheelRim", CreateOrUpdateMaterial("AtlasCraneWheelRim", new Color32(128, 145, 164, 255), 0.62f, 0.60f, useVertexWearShader: true) },
                { "wheelHub", CreateOrUpdateMaterial("AtlasCraneWheelHub", new Color32(20, 26, 36, 255), 0.55f, 0.50f, useVertexWearShader: true) },
                { "warning", CreateOrUpdateMaterial("AtlasCraneWarningRed", new Color32(230, 31, 38, 255), 0.18f, 0.64f, useVertexWearShader: true) },
                { "emissive", CreateOrUpdateMaterial("AtlasCraneSensorCyan", new Color32(38, 217, 255, 255), 0.20f, 0.80f, new Color(0.15f, 0.85f, 1.0f) * 2.0f, true) }
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return materials;
        }

        private static Dictionary<string, Material> CreateBastionSiegeMechMaterials()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, MaterialRoot)));
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal)
            {
                { "armor", CreateOrUpdateMaterial("BastionPrimaryArmor", new Color32(104, 65, 38, 255), 0.44f, 0.62f, useVertexWearShader: true) },
                { "lightArmor", CreateOrUpdateMaterial("BastionLightArmor", new Color32(140, 109, 72, 255), 0.38f, 0.60f, useVertexWearShader: true) },
                { "gunmetal", CreateOrUpdateMaterial("BastionGunmetal", new Color32(30, 31, 34, 255), 0.78f, 0.70f, useVertexWearShader: true) },
                { "tread", CreateOrUpdateMaterial("BastionTread", new Color32(22, 27, 30, 255), 0.18f, 0.32f, useVertexWearShader: true) },
                { "warning", CreateOrUpdateMaterial("BastionWarningYellow", new Color32(242, 169, 29, 255), 0.22f, 0.65f, useVertexWearShader: true) },
                { "emissive", CreateOrUpdateMaterial("BastionReactorAmber", new Color32(242, 169, 29, 255), 0.16f, 0.82f, new Color(1.0f, 0.48f, 0.05f) * 1.8f, true) },
                { "wheelRim", CreateOrUpdateMaterial("BastionWheelRim", new Color32(105, 107, 103, 255), 0.64f, 0.62f, useVertexWearShader: true) }
            };
            foreach (KeyValuePair<string, Material> material in materials)
            {
                if (material.Value == null || material.Value.shader == null ||
                    !String.Equals(material.Value.shader.name, VertexWearShaderName, StringComparison.Ordinal) ||
                    Mathf.Abs(material.Value.GetFloat("_WearStrength") - FactoryNewWearStrength) > 0.001f)
                {
                    throw new InvalidDataException("Bastion material '" + material.Key +
                        "' must use VehiclePBR_VertexWear with _WearStrength = " + FactoryNewWearStrength + ".");
                }
            }
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
            AssignBatchedMaterials(model, "SkirmishStrikeBike", materials);
        }

        private static void AssignColossusMaterials(
            GameObject model,
            IDictionary<string, Material> materials)
        {
            AssignBatchedMaterials(model, "ColossusSiegePlatform", materials);
        }

        private static void AssignIroncladMaterials(
            GameObject model,
            IDictionary<string, Material> materials)
        {
            AssignBatchedMaterials(model, "IroncladWalker", materials);
        }

        private static void AssignAtlasCraneMechMaterials(
            GameObject model,
            IDictionary<string, Material> materials)
        {
            AssignBatchedMaterials(model, "AtlasCraneMech", materials);
        }

        private static void AssignBastionSiegeMechMaterials(
            GameObject model,
            IDictionary<string, Material> materials)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidDataException("The authored BastionSiegeMech FBX contains no renderers.");
            }

            var usedMaterials = new HashSet<Material>();
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Material[] sourceMaterials = renderers[rendererIndex].sharedMaterials;
                if (sourceMaterials == null || sourceMaterials.Length == 0)
                {
                    throw new InvalidDataException(renderers[rendererIndex].name +
                        " has no imported material slot for the Bastion material map.");
                }

                Material[] assignedMaterials = new Material[sourceMaterials.Length];
                for (int materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++)
                {
                    Material sourceMaterial = sourceMaterials[materialIndex];
                    Material assignedMaterial;
                    if (sourceMaterial == null || !TryResolveBatchMaterial(
                        sourceMaterial.name, "BastionSiegeMech", materials, out assignedMaterial) ||
                        assignedMaterial == null)
                    {
                        throw new InvalidDataException("No authored material mapping exists for Bastion renderer '" +
                            renderers[rendererIndex].name + "' source material '" +
                            (sourceMaterial != null ? sourceMaterial.name : "<null>") + "'.");
                    }

                    assignedMaterials[materialIndex] = assignedMaterial;
                    usedMaterials.Add(assignedMaterial);
                }

                renderers[rendererIndex].sharedMaterials = assignedMaterials;
            }

            if (usedMaterials.Count != materials.Count)
            {
                throw new InvalidDataException("BastionSiegeMech must use every authored base material batch; used " +
                    usedMaterials.Count + " of " + materials.Count + ".");
            }
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
                if (!TryResolveBatchMaterial(materialKey, modelPrefix, materials, out material) || material == null)
                {
                    throw new InvalidDataException("No authored material mapping exists for model batch '" +
                        rendererName + "'.");
                }

                renderers[i].sharedMaterials = new[] { material };
            }
        }

        private static bool TryResolveBatchMaterial(
            string materialKey,
            string modelPrefix,
            IDictionary<string, Material> materials,
            out Material material)
        {
            if (materials.TryGetValue(materialKey, out material) && material != null)
            {
                return true;
            }

            string normalizedKey = materialKey ?? String.Empty;
            string modelMaterialPrefix = modelPrefix + "_";
            while (normalizedKey.StartsWith(modelMaterialPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalizedKey = normalizedKey.Substring(modelMaterialPrefix.Length);
            }

            string alias = null;
            switch (normalizedKey.Replace("_", String.Empty).ToLowerInvariant())
            {
                case "primaryarmor": alias = "armor"; break;
                case "sandlancearmor": alias = "armor"; break;
                case "sandlancelightarmor": alias = "lightArmor"; break;
                case "sandlancetread": alias = "tread"; break;
                case "sandlancewheelrim": alias = "wheelRim"; break;
                case "sandlancewheelhub": alias = "wheelHub"; break;
                case "sandlancewheelmetal": alias = "wheel"; break;
                case "sandlancegunmetal": alias = "gunmetal"; break;
                case "sandlancewarning": alias = "warning"; break;
                case "sandlanceamber": alias = "amber"; break;
                case "sandlancesignalamber": alias = "amber"; break;
                case "sandlanceemissive": alias = "emissive"; break;
                case "sandlancesensor": alias = "sensor"; break;
                case "sandlancesensorglass": alias = "sensor"; break;
                case "colossusarmor": alias = "armor"; break;
                case "colossuslightarmor": alias = "lightArmor"; break;
                case "colossusgunmetal": alias = "gunmetal"; break;
                case "colossustread": alias = "tread"; break;
                case "colossuswheelrim": alias = "wheelRim"; break;
                case "colossuswheelhub": alias = "wheelHub"; break;
                case "colossussensorcyan": alias = "sensor"; break;
                case "colossussignalamber": alias = "amber"; break;
                case "strikebikearmor": alias = "armor"; break;
                case "strikebikelightarmor": alias = "lightArmor"; break;
                case "strikebikegunmetal": alias = "gunmetal"; break;
                case "strikebiketread": alias = "tread"; break;
                case "strikebiketire": alias = "rubber"; break;
                case "strikebikewheelrim": alias = "wheelRim"; break;
                case "strikebikewheelhub": alias = "wheelHub"; break;
                case "strikebikearccyan": alias = "arc"; break;
                case "strikebikewheelmetal": alias = "wheel"; break;
                case "strikebikewarningorange": alias = "warning"; break;
                case "lightarmor": alias = "lightArmor"; break;
                case "gunmetal": alias = "gunmetal"; break;
                case "tread": alias = "tread"; break;
                case "wheelrim": alias = "wheelRim"; break;
                case "wheelhub": alias = "wheelHub"; break;
                case "warning": alias = "warning"; break;
                case "emissive": alias = "emissive"; break;
                case "surgearctankemissive": alias = "arc"; break;
                case "surgearctankwarning": alias = "magenta"; break;
            }

            if (alias != null && materials.TryGetValue(alias, out material) && material != null)
            {
                return true;
            }
            // Fallback for emissive/warning batches on units without a matching material key.
            if (String.Equals(alias, "emissive", StringComparison.Ordinal) ||
                String.Equals(alias, "warning", StringComparison.Ordinal))
            {
                foreach (string fallbackKey in new[] { "arc", "sensor", "magenta", "amber", "warning" })
                {
                    if (materials.TryGetValue(fallbackKey, out material) && material != null)
                    {
                        return true;
                    }
                }
            }
            // Fallback for wheel batches on units without wheel material keys.
            if (String.Equals(alias, "wheelHub", StringComparison.Ordinal) ||
                String.Equals(alias, "wheelRim", StringComparison.Ordinal))
            {
                foreach (string fallbackKey in new[] { "gunmetal", "tread", "armor" })
                {
                    if (materials.TryGetValue(fallbackKey, out material) && material != null)
                    {
                        return true;
                    }
                }
            }
            if (String.Equals(alias, "emissive", StringComparison.Ordinal) &&
                materials.TryGetValue("sensor", out material) && material != null)
            {
                return true;
            }

            material = null;
            return false;
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

        private static void ImportCardSprite(string assetPath, bool alphaIsTransparency = false)
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
            if (alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
            }
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

            string optionalCarriageReturn = new string((char)13, 1);
            normalized = Regex.Replace(normalized,
                @"(?m)^([ \t]*(?:value|userData|assetBundleName|assetBundleVariant|motionNodeName|" +
                @"rigImportErrors|rigImportWarnings|animationImportErrors|animationImportWarnings|" +
                @"animationRetargetingWarnings|rootMotionBoneName|m_WarningMessage):)[ \t]*(" +
                optionalCarriageReturn + @")?$",
                "$1 \"\"$2");
            if (!String.Equals(contents, normalized, StringComparison.Ordinal))
            {
                try
                {
                    File.WriteAllText(filePath, normalized, new UTF8Encoding(false));
                }
                catch (IOException exception)
                {
                    // Unity can keep a freshly saved prefab memory-mapped while its
                    // AssetDatabase imports it. Windows then reports ERROR_USER_MAPPED_FILE
                    // (1224) for this cosmetic YAML normalization write.
                    if ((exception.HResult & 0xffff) != 1224)
                    {
                        throw;
                    }

                    Debug.LogWarning("Skipped empty-string YAML normalization for a mapped " +
                        "Unity asset: " + filePath);
                }
            }
        }

        private static Material CreateOrUpdateMaterial(
            string name,
            Color color,
            float metallic,
            float smoothness,
            Color? emissionColor = null,
            bool useVertexWearShader = false)
        {
            string path = MaterialRoot + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = useVertexWearShader ? Shader.Find(VertexWearShaderName) : null;
            bool vertexWearEnabled = shader != null;
            if (shader == null)
            {
                shader = Shader.Find(useVertexWearShader
                    ? "Standard"
                    : "Universal Render Pipeline/Simple Lit");
                if (shader == null && !useVertexWearShader)
                {
                    shader = Shader.Find("Universal Render Pipeline/Lit");
                }
                if (useVertexWearShader)
                {
                    Debug.LogWarning("Shader " + VertexWearShaderName + " is unavailable for " + name +
                        "; falling back to Standard, which does not read vertex wear channels.");
                }
            }
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "The required Unity shader is unavailable for " + name + ". " +
                    (useVertexWearShader
                        ? "Resolve the bundled VertexWear or Standard shader before building sample visuals."
                        : "Resolve the Unity URP package dependency before building sample visuals."));
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
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }
            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }
            material.enableInstancing = true;
            if (vertexWearEnabled)
            {
                material.SetFloat("_GlossMapScale", 1.0f);
                material.SetFloat("_AoStrength", 1.0f);
                material.SetFloat("_WearStrength", FactoryNewWearStrength);
                material.SetFloat("_SootStrength", 1.0f);
                material.SetColor("_TeamColor", Color.white);
            }

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
            AssignBatchedMaterials(model, "SandlanceScout", materials);
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
