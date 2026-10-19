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
        private const string SurgeArcTankPrefabPath = AssetRoot + "/SurgeArcTankVisual.prefab";
        private const string SurgeArcTankCardSpritePath = AssetRoot + "/SurgeArcTankCard.png";
        private const string SurgeArcTankModelPath = AssetRoot + "/Models/SurgeArcTank.fbx";
        private const string MaterialRoot = AssetRoot + "/Materials";
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
                string surgeArcTankPrefabPath = CreateSurgeArcTankVisual();
                ImportCardSprite(SurgeArcTankCardSpritePath);
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
                        surgeArcTankPrefabPath, SurgeArcTankCardSpritePath
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
                Debug.Log("Built six script-free Sandlance, Skirmish Bike, Colossus, Ironclad Walker, " +
                    "Atlas Crane Mech, and Surge Arc Tank visual overlays plus six card Sprites in " +
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

        private static void ValidateRiggedWalkerSources(string projectRoot)
        {
            var issues = new List<string>();
            ValidateSourceFile(projectRoot, IroncladCardSpritePath, "Ironclad Walker card", issues);
            ValidateSourceFile(projectRoot, AtlasCraneMechCardSpritePath, "Atlas Crane Mech card", issues);

            ValidateWalkerModelSource(projectRoot, IroncladModelPath, "Ironclad Walker", issues);
            ValidateWalkerModelSource(projectRoot, AtlasCraneMechModelPath, "Atlas Crane Mech", issues);

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
            ICollection<string> issues)
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

        private static void EnsureCanonicalWalkerSockets(GameObject root, GameObject model, bool rapidFireMuzzle)
        {
            string[] uniqueSocketNames =
            {
                "Socket_MuzzlePrimary",
                "Socket_EngineAudio",
                "Socket_WeaponAudio",
                "Socket_DamageSmoke",
                "Socket_DamageFire",
                "Socket_GroundCenter",
                "Socket_FootContact_L",
                "Socket_FootContact_R"
            };
            for (int i = 0; i < uniqueSocketNames.Length; i++)
            {
                GetUniqueNamedTransform(root, uniqueSocketNames[i], false);
            }

            Bounds modelBounds = GetModelBoundsInRootSpace(root, model);
            float footHalfWidth = Mathf.Max(modelBounds.size.x * 0.22f, 0.12f);
            Vector3 leftFootFallback = new Vector3(modelBounds.center.x - footHalfWidth, 0.0f, modelBounds.center.z);
            Vector3 rightFootFallback = new Vector3(modelBounds.center.x + footHalfWidth, 0.0f, modelBounds.center.z);
            Transform leftFoot = EnsureUniqueSocket(root, "Socket_FootContact_L", leftFootFallback);
            Transform rightFoot = EnsureUniqueSocket(root, "Socket_FootContact_R", rightFootFallback);

            Transform muzzle = GetUniqueNamedTransform(root, "Socket_MuzzlePrimary", true);
            Transform weaponAudio = GetUniqueNamedTransform(root, "Socket_WeaponAudio", true);
            Transform engineAudio = GetUniqueNamedTransform(root, "Socket_EngineAudio", true);
            EnsureRootLocalFootContact(root, leftFoot, "Socket_FootContact_L");
            EnsureRootLocalFootContact(root, rightFoot, "Socket_FootContact_R");
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

            ValidateCanonicalWalkerSockets(root, root.name);
        }

        private static void ValidateCanonicalWalkerSockets(GameObject root, string unitName)
        {
            string[] requiredSocketNames =
            {
                "Socket_MuzzlePrimary",
                "Socket_EngineAudio",
                "Socket_WeaponAudio",
                "Socket_DamageSmoke",
                "Socket_DamageFire",
                "Socket_FootContact_L",
                "Socket_FootContact_R"
            };
            for (int i = 0; i < requiredSocketNames.Length; i++)
            {
                GetUniqueNamedTransform(root, requiredSocketNames[i], true);
            }

            Transform muzzle = GetUniqueNamedTransform(root, "Socket_MuzzlePrimary", true);
            Transform weaponAudio = GetUniqueNamedTransform(root, "Socket_WeaponAudio", true);
            if (Vector3.Distance(muzzle.position, weaponAudio.position) > 0.75f)
            {
                throw new InvalidDataException(unitName + " has Socket_WeaponAudio too far from Socket_MuzzlePrimary.");
            }

            EnsureRootLocalFootContact(root, GetUniqueNamedTransform(root, "Socket_FootContact_L", true),
                "Socket_FootContact_L");
            EnsureRootLocalFootContact(root, GetUniqueNamedTransform(root, "Socket_FootContact_R", true),
                "Socket_FootContact_R");

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
                VehiclePrefabBuilder.EnsureCanonicalVfxSockets(root, modelInstance);

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
                VehiclePrefabBuilder.EnsureCanonicalVfxSockets(root, modelInstance);

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
                VehiclePrefabBuilder.EnsureCanonicalVfxSockets(root, modelInstance);

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
            return CreateRiggedWalkerVisual(
                "IroncladWalker",
                IroncladModelPath,
                IroncladPrefabPath,
                CreateIroncladMaterials,
                AssignIroncladMaterials,
                true);
        }

        private static string CreateAtlasCraneMechVisual()
        {
            return CreateRiggedWalkerVisual(
                "AtlasCraneMech",
                AtlasCraneMechModelPath,
                AtlasCraneMechPrefabPath,
                CreateAtlasCraneMechMaterials,
                AssignAtlasCraneMechMaterials,
                false);
        }

        private static string CreateRiggedWalkerVisual(
            string unitName,
            string modelPath,
            string prefabPath,
            Func<Dictionary<string, Material>> createMaterials,
            Action<GameObject, IDictionary<string, Material>> assignMaterials,
            bool rapidFireMuzzle)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, AssetRoot)));
            AssetDatabase.Refresh();

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException(
                    "Import the rigged " + unitName + " FBX before building the sample bundle.", modelPath);
            }

            Dictionary<string, Material> materials = createMaterials();
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
                assignMaterials(modelInstance, materials);

                VehiclePrefabBuilder.EnsureCanonicalVfxSockets(root, modelInstance);
                EnsureCanonicalWalkerSockets(root, modelInstance, rapidFireMuzzle);
                VehiclePrefabBuilder.SetupAnimatorIfAvailable(root, modelInstance, modelAsset, unitName, true);

                Animator animator = root.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isValid ||
                    animator.runtimeAnimatorController == null)
                {
                    throw new InvalidDataException(
                        unitName + " must import a valid Generic Avatar and the RCL animation controller.");
                }

                animator.applyRootMotion = false;

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
            return prefabPath;
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
                VehiclePrefabBuilder.EnsureCanonicalVfxSockets(root, modelInstance);

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
                ValidateRiggedWalkerBundleAssets(bundle, assetNames,
                    "ironcladwalkervisual.prefab", "IroncladWalkerVisual",
                    "ironcladwalkercard.png", "IroncladWalkerCard", "Ironclad Walker");
                ValidateRiggedWalkerBundleAssets(bundle, assetNames,
                    "atlascranemechvisual.prefab", "AtlasCraneMechVisual",
                    "atlascranemechcard.png", "AtlasCraneMechCard", "Atlas Crane Mech");
                ValidateVisualBundleAssets(bundle, assetNames, "surgearctankvisual.prefab", "SurgeArcTankVisual",
                    "surgearctankcard.png", "SurgeArcTankCard", 6, "Surge Arc Tank");
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
            string visualLabel)
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

            ValidateCanonicalWalkerSockets(prefab, visualLabel);

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

        private static void AssignAtlasCraneMechMaterials(
            GameObject model,
            IDictionary<string, Material> materials)
        {
            AssignBatchedMaterials(model, "AtlasCraneMech", materials);
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
            if (normalizedKey.StartsWith(modelMaterialPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalizedKey = normalizedKey.Substring(modelMaterialPrefix.Length);
            }

            string alias = null;
            switch (normalizedKey.Replace("_", String.Empty).ToLowerInvariant())
            {
                case "primaryarmor": alias = "armor"; break;
                case "lightarmor": alias = "lightArmor"; break;
                case "gunmetal": alias = "gunmetal"; break;
                case "tread": alias = "tread"; break;
                case "wheelrim": alias = "wheelRim"; break;
                case "wheelhub": alias = "wheelHub"; break;
                case "warning": alias = "warning"; break;
                case "emissive": alias = "emissive"; break;
            }

            if (alias != null && materials.TryGetValue(alias, out material) && material != null)
            {
                return true;
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
                    : "Universal Render Pipeline/Lit");
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
