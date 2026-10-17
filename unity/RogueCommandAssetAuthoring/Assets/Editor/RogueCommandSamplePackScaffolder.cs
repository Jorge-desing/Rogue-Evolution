using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace RogueCommandExpansion.Authoring
{
    /// <summary>
    /// Editor scaffolding and automation tool for Rogue Command expansion content packs.
    /// Creates the sample card art and validates authored game templates before bundling them for Windows x64.
    /// </summary>
    public static class RogueCommandSamplePackScaffolder
    {
        private const string SampleBundleName = "sample-expansion.bundle";
        private const string SamplePackFolderName = "sample-expansion";
        private const string DefaultGameManagedPath = @"C:\Program Files (x86)\Steam\steamapps\common\Rogue Command\Rogue Command_Data\Managed";
        private const string AssetsFolder = "Assets/SampleExpansion";
        private const string GameAssembliesFolder = "Assets/Plugins/GameAssemblies";
        private const string ExpectedGameAssemblySha256 = "d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b";
        private const string ExpectedGameAssemblyMvid = "db0e9462-128d-4296-9900-de18b4bd0c00";

        [MenuItem("Rogue Command/Sample Pack/1. Import Game Assemblies for Editor...")]
        public static void ImportGameAssembliesMenu()
        {
            ImportGameAssemblies(null);
        }

        public static void ImportGameAssemblies(string sourceDir = null, bool force = false)
        {
            string targetDir = Path.Combine(Application.dataPath, "Plugins", "GameAssemblies");

            if (string.IsNullOrEmpty(sourceDir))
            {
                if (Application.isBatchMode)
                {
                    sourceDir = DefaultGameManagedPath;
                }
                else
                {
                    sourceDir = EditorUtility.OpenFolderPanel(
                        "Select Rogue Command Managed folder",
                        Directory.Exists(DefaultGameManagedPath) ? DefaultGameManagedPath : Application.dataPath,
                        "");
                }
            }

            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
            {
                string message = "Rogue Command Managed folder not found: " + sourceDir;
                if (Application.isBatchMode)
                {
                    throw new DirectoryNotFoundException(message);
                }

                Debug.LogWarning("[SamplePackScaffolder] " + message);
                return;
            }

            string gameAssemblySource = Path.Combine(sourceDir, "Assembly-CSharp.dll");
            if (!File.Exists(gameAssemblySource))
            {
                throw new FileNotFoundException("The selected folder does not contain Assembly-CSharp.dll.", gameAssemblySource);
            }

            string sourceHash = ComputeSha256(gameAssemblySource);
            if (!String.Equals(sourceHash, ExpectedGameAssemblySha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Assembly-CSharp.dll does not match the verified Rogue Command 1.0.9 build. " +
                    "Expected SHA-256 " + ExpectedGameAssemblySha256 + ", found " + sourceHash + ". " +
                    "Re-inspect the game version, assembly hash, and MVID before authoring against it.");
            }

            Directory.CreateDirectory(targetDir);

            string[] dllsToCopy = new[]
            {
                "Assembly-CSharp.dll",
                "Sirenix.Serialization.dll",
                "Sirenix.Serialization.Config.dll",
                "Sirenix.Utilities.dll",
                "Sirenix.OdinInspector.Attributes.dll",
                "ShapesRuntime.dll",
                "com.rlabrecque.steamworks.net.dll",
                "Unity.RenderPipelines.Core.Runtime.dll",
                "Unity.RenderPipelines.Universal.Runtime.dll",
                "Unity.Services.Analytics.dll",
                "Unity.TextMeshPro.dll",
                "Unity.Mathematics.dll",
                "Unity.Burst.dll",
                "Unity.InputSystem.dll",
                "Unity.Services.Core.dll",
                "Unity.Services.Core.Environments.dll",
                "UnityEngine.UI.dll",
                "Unity.RenderPipeline.Universal.ShaderLibrary.dll",
                "Unity.RenderPipelines.Core.ShaderLibrary.dll",
                "Unity.RenderPipelines.ShaderGraph.ShaderGraphLibrary.dll",
                "Unity.RenderPipelines.Universal.Shaders.dll",
                "Unity.InputSystem.ForUI.dll",
                "Unity.Timeline.dll",
                "Unity.Services.Core.Internal.dll",
                "Unity.Services.Core.Networking.dll",
                "Unity.Services.Core.Registration.dll",
                "Unity.Services.Core.Scheduler.dll",
                "Unity.Services.Core.Telemetry.dll",
                "Unity.Services.Core.Threading.dll",
                "Unity.Services.Core.Configuration.dll",
                "Unity.Services.Core.Device.dll",
                "Unity.Services.Core.Environments.Internal.dll"
            };

            int copied = 0;
            foreach (string dllName in dllsToCopy)
            {
                string src = Path.Combine(sourceDir, dllName);
                string dst = Path.Combine(targetDir, dllName);
                if (!File.Exists(src))
                {
                    if (String.Equals(dllName, "Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new FileNotFoundException("A required game assembly is missing.", src);
                    }

                    Debug.LogWarning("[SamplePackScaffolder] Optional authoring reference is missing: " + src);
                    continue;
                }

                if (!File.Exists(dst) ||
                    !String.Equals(ComputeSha256(src), ComputeSha256(dst), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(src, dst, true);
                    copied++;
                }
            }

            if (copied > 0)
            {
                AssetDatabase.Refresh();
            }

            // Keep local game DLLs available to the Editor without making them player-compatible.
            string projectRelativeAssemblyFolder = GameAssembliesFolder.Replace('/', Path.DirectorySeparatorChar);
            string absoluteAssemblyFolder = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                projectRelativeAssemblyFolder);
            string[] relativeDlls = Directory.GetFiles(absoluteAssemblyFolder, "*.dll", SearchOption.AllDirectories);
            int configured = 0;
            for (int i = 0; i < relativeDlls.Length; i++)
            {
                string rel = relativeDlls[i].Substring(Directory.GetParent(Application.dataPath).FullName.Length + 1)
                    .Replace('\\', '/');
                PluginImporter importer = AssetImporter.GetAtPath(rel) as PluginImporter;
                if (importer == null)
                {
                    throw new InvalidOperationException("Unity did not create a PluginImporter for " + rel + ".");
                }

                if (importer.GetCompatibleWithAnyPlatform() || !importer.GetCompatibleWithEditor())
                {
                    importer.SetCompatibleWithAnyPlatform(false);
                    importer.SetCompatibleWithEditor(true);
                    importer.SaveAndReimport();
                    configured++;
                }

                if (!importer.GetCompatibleWithEditor() || importer.GetCompatibleWithAnyPlatform())
                {
                    throw new InvalidOperationException("Game assembly was not restricted to Editor-only use: " + rel);
                }
            }

            string copiedAssembly = Path.Combine(absoluteAssemblyFolder, "Assembly-CSharp.dll");
            string copiedHash = ComputeSha256(copiedAssembly);
            if (!String.Equals(copiedHash, ExpectedGameAssemblySha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The imported Assembly-CSharp.dll failed its SHA-256 check.");
            }

            if (copied > 0 || configured > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            string msg = $"Imported {copied} and configured {configured} assembly file(s) as Editor-only authoring references. " +
                "Assembly-CSharp SHA-256: " + copiedHash + ".";
            Debug.Log("[SamplePackScaffolder] " + msg);

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Game Assemblies Imported", msg, "OK");
            }
        }

        [MenuItem("Rogue Command/Sample Pack/2. Scaffold Sample Pack Assets")]
        public static void ScaffoldSampleAssets()
        {
            try
            {
                ValidateGameAuthoringTypes();
                ValidateAuthoredTemplatePrefabs();

                if (!AssetDatabase.IsValidFolder(AssetsFolder))
                {
                    AssetDatabase.CreateFolder("Assets", "SampleExpansion");
                }

                // Game-specific balance and stage assets must come from complete native templates.
                ValidateAuthoredDataAssets();

                // 1. ScoutTankCard Sprite
                CreateCardSprite();

                // Game-specific unit, AI, and map prefabs must be authored from valid game templates.
                // The editor cannot safely fabricate those hierarchies from empty GameObjects.

                // 2. Validate and label the authored balance ScriptableObject.
                CreateBalanceAsset();

                // 3. Validate and label the authored stage ScriptableObject.
                CreateStageAsset();

                AssignBundleLabel(AssetsFolder + "/ScoutTank.prefab");
                AssignBundleLabel(AssetsFolder + "/RCL_SampleBaseBuilderAI.prefab");
                AssignBundleLabel(AssetsFolder + "/SampleMap.prefab");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                ValidateSampleAssets();

                string msg = $"Validated authored game assets and scaffolded the card Sprite in {AssetsFolder} for {SampleBundleName}.";
                Debug.Log("[SamplePackScaffolder] " + msg);

                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog(
                        "Sample Pack Assets Scaffolded",
                        msg + "\n\nNext step: Select 'Rogue Command/Sample Pack/3. Build and Export Sample Bundles'.",
                        "OK");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    throw;
                }
                else
                {
                    EditorUtility.DisplayDialog("Scaffolding Error", exception.Message, "OK");
                }
            }
        }

        [MenuItem("Rogue Command/Sample Pack/3. Build and Export Sample Bundles")]
        public static void BuildAndExportBundles()
        {
            try
            {
                ValidateSampleAssets();

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string packBundlesDir = Path.GetFullPath(Path.Combine(
                    projectRoot, "..", "..", "content", "packs", SamplePackFolderName, "bundles"));

                Directory.CreateDirectory(packBundlesDir);

                // Build using ChunkBasedCompression (LZ4)
                AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                    packBundlesDir,
                    BuildAssetBundleOptions.ChunkBasedCompression,
                    BuildTarget.StandaloneWindows64);

                if (manifest == null)
                {
                    throw new InvalidOperationException("BuildPipeline.BuildAssetBundles failed. Check the Unity console for details.");
                }

                // Unity writes the manifest bundle named after the destination folder ("bundles")
                string sourceManifestBundle = Path.Combine(packBundlesDir, "bundles");
                string targetManifestBundle = Path.Combine(packBundlesDir, "assetbundle-manifest.bundle");

                if (!File.Exists(sourceManifestBundle) || new FileInfo(sourceManifestBundle).Length <= 0)
                {
                    throw new InvalidDataException("Unity returned a bundle manifest but did not write its manifest bundle.");
                }

                File.Copy(sourceManifestBundle, targetManifestBundle, true);
                File.Delete(sourceManifestBundle);

                string sourceManifestMeta = Path.Combine(packBundlesDir, "bundles.manifest");
                string targetManifestMeta = Path.Combine(packBundlesDir, "assetbundle-manifest.bundle.manifest");
                if (File.Exists(sourceManifestMeta))
                {
                    File.Copy(sourceManifestMeta, targetManifestMeta, true);
                    File.Delete(sourceManifestMeta);
                }

                AssetDatabase.Refresh();

                string msg = "Sample expansion bundles and manifest generated at: " + packBundlesDir;
                Debug.Log("[SamplePackScaffolder] " + msg);

                if (!Application.isBatchMode)
                {
                    EditorUtility.RevealInFinder(packBundlesDir);
                    EditorUtility.DisplayDialog("AssetBundles Built Successfully", msg, "OK");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    throw;
                }
                else
                {
                    EditorUtility.DisplayDialog("Bundle Build Error", exception.Message, "OK");
                }
            }
        }

        public static void BatchScaffoldAndBuild()
        {
            try
            {
                Debug.Log("[SamplePackScaffolder] Starting batchmode scaffold and build...");
                ImportGameAssemblies(DefaultGameManagedPath, false);
                ScaffoldSampleAssets();
                ValidateSampleAssets();
                BuildAndExportBundles();
                Debug.Log("[SamplePackScaffolder] Batchmode operation finished.");
            }
            catch (Exception exception)
            {
                Debug.LogError("[SamplePackScaffolder] Batch operation failed: " + exception);
                EditorApplication.Exit(1);
                throw;
            }
        }

        public static void ProbeMonoScripts()
        {
            Debug.Log("=== PROBE MONOSCRIPTS START ===");
            try
            {
                Type entityControllerType = FindGameType("EntityController");
                Debug.Log("EntityController Type: " + (entityControllerType != null ? entityControllerType.AssemblyQualifiedName : "null"));
                if (entityControllerType != null)
                {
                    Debug.Log("Assembly.Location: " + entityControllerType.Assembly.Location);
                    Debug.Log("IsSubclassOf MonoBehaviour: " + typeof(MonoBehaviour).IsAssignableFrom(entityControllerType));
                }

                Type balanceType = FindGameType("EntityBalancingScriptableObject");
                Debug.Log("EntityBalancingScriptableObject Type: " + (balanceType != null ? balanceType.AssemblyQualifiedName : "null"));
                if (balanceType != null)
                {
                    try
                    {
                        ScriptableObject so = ScriptableObject.CreateInstance(balanceType);
                        Debug.Log("ScriptableObject.CreateInstance: " + (so != null ? so.GetType().FullName : "null"));
                        if (so != null)
                        {
                            MonoScript msFromSO = MonoScript.FromScriptableObject(so);
                            Debug.Log("MonoScript.FromScriptableObject: " + (msFromSO != null ? msFromSO.name : "null"));
                            UnityEngine.Object.DestroyImmediate(so);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Log("ScriptableObject.CreateInstance failed: " + ex.Message);
                    }
                }

                MonoScript[] scripts = Resources.FindObjectsOfTypeAll<MonoScript>();
                Debug.Log("Total MonoScripts in project: " + scripts.Length);
                int foundInPlugins = 0;
                string[] targets = new[] { "EntityController", "EntityBalancingScriptableObject", "StageParametersScriptableObject", "DefaultAiBehaviour", "InitMap", "BlueprintPool", "WorldList", "BlueprintPoolList" };
                foreach (var s in scripts)
                {
                    if (s != null)
                    {
                        foreach (var target in targets)
                        {
                            if (s.name == target)
                            {
                                string path = AssetDatabase.GetAssetPath(s);
                                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(s, out string guid, out long localId);
                                Debug.Log($"TARGET MONOSCRIPT: {s.name} => guid: {guid}, localId: {localId}, path: {path}, class: {(s.GetClass() != null ? s.GetClass().FullName : "null")}");
                            }
                        }
                    }
                }

                if (entityControllerType != null)
                {
                    GameObject go = new GameObject("TestProbe");
                    try
                    {
                        Component c = go.AddComponent(entityControllerType);
                        Debug.Log("go.AddComponent(EntityController): " + (c != null ? c.GetType().FullName : "null"));
                    }
                    catch (Exception ex)
                    {
                        Debug.Log("go.AddComponent(EntityController) threw: " + ex.ToString());
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(go);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            Debug.Log("=== PROBE MONOSCRIPTS END ===");
        }

        [MenuItem("Rogue Command/Sample Pack/0. Validate Authoring Prerequisites")]
        public static void ValidateAuthoringPrerequisitesMenu()
        {
            try
            {
                ValidateGameAuthoringTypes();
                ValidateAuthoredTemplatePrefabs();
                Debug.Log("[SamplePackScaffolder] Game identity and authored unit, AI, and map templates passed preflight.");
                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog("Authoring Preflight Passed",
                        "The game type references and authored sample templates are ready for scaffold/build.", "OK");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    throw;
                }

                EditorUtility.DisplayDialog("Authoring Preflight Failed", exception.Message, "OK");
            }
        }

        private static void CreateCardSprite()
        {
            string pngPath = AssetsFolder + "/ScoutTankCard.png";
            UnityEngine.Object existing = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
            if (existing != null)
            {
                AssignBundleLabel(pngPath);
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string existingFile = Path.Combine(projectRoot, pngPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(existingFile))
            {
                throw new InvalidDataException("ScoutTankCard.png exists but Unity did not import it as a Sprite; preserving the file.");
            }

            int width = 128;
            int height = 128;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

            Color bg = new Color(0.12f, 0.16f, 0.22f, 1f);
            Color tankBody = new Color(0.25f, 0.70f, 0.90f, 1f);
            Color turret = new Color(0.85f, 0.92f, 0.98f, 1f);
            Color track = new Color(0.08f, 0.10f, 0.14f, 1f);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    tex.SetPixel(x, y, bg);
                }
            }

            // Draw tank silhouette
            for (int y = 28; y <= 42; y++)
            {
                for (int x = 20; x <= 108; x++)
                {
                    tex.SetPixel(x, y, track);
                }
            }
            for (int y = 43; y <= 72; y++)
            {
                for (int x = 28; x <= 100; x++)
                {
                    tex.SetPixel(x, y, tankBody);
                }
            }
            for (int y = 73; y <= 94; y++)
            {
                for (int x = 46; x <= 82; x++)
                {
                    tex.SetPixel(x, y, turret);
                }
            }
            for (int y = 80; y <= 86; y++)
            {
                for (int x = 83; x <= 118; x++)
                {
                    tex.SetPixel(x, y, turret);
                }
            }

            tex.Apply();
            byte[] bytes = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            string fullSystemPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, pngPath);
            File.WriteAllBytes(fullSystemPath, bytes);

            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("Unity did not create a TextureImporter for " + pngPath + ".");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.assetBundleName = SampleBundleName;
            importer.SaveAndReimport();
            if (AssetDatabase.LoadAssetAtPath<Sprite>(pngPath) == null)
            {
                throw new InvalidDataException("Unity failed to import ScoutTankCard.png as a Sprite.");
            }
        }

        private static void CreateBalanceAsset()
        {
            string assetPath = AssetsFolder + "/ScoutTankBalance.asset";
            Type balanceType = RequireGameType("EntityBalancingScriptableObject");
            string absolutePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
            {
                throw new InvalidDataException("Author ScoutTankBalance.asset from a complete Rogue Command balance template before scaffolding. The tool will not create a sparse default row.");
            }

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(assetPath, balanceType);
            if (asset == null)
            {
                throw new InvalidDataException("ScoutTankBalance.asset exists but is not a loadable game balance asset; preserving it.");
            }

            ValidateBalanceAsset(asset, balanceType);
            AssignBundleLabel(assetPath);
        }

        private static void CreateStageAsset()
        {
            string assetPath = AssetsFolder + "/TrainingStage.asset";
            Type stageParamType = RequireGameType("StageParametersScriptableObject");
            Type initMapType = RequireGameType("InitMap");
            string absoluteStagePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absoluteStagePath))
            {
                throw new InvalidDataException("Author TrainingStage.asset from a complete Rogue Command stage template before scaffolding. The tool will not create stage data with defaulted gameplay fields.");
            }

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(assetPath, stageParamType);
            if (asset == null)
            {
                throw new InvalidDataException("TrainingStage.asset exists but is not a loadable game stage asset; preserving it.");
            }

            GameObject mapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetsFolder + "/SampleMap.prefab");
            ValidateStageAsset(asset, stageParamType, initMapType, mapPrefab);
            AssignBundleLabel(assetPath);
        }

        private static void ValidateGameAuthoringTypes()
        {
            string assembliesDirectory = Path.Combine(Application.dataPath, "Plugins", "GameAssemblies");
            string assemblyPath = Path.Combine(assembliesDirectory, "Assembly-CSharp.dll");
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException(
                    "Import the verified Rogue Command game assemblies before scaffolding sample content.", assemblyPath);
            }

            string hash = ComputeSha256(assemblyPath);
            if (!String.Equals(hash, ExpectedGameAssemblySha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The local Assembly-CSharp.dll is not the verified 1.0.9 assembly. SHA-256: " + hash);
            }

            Type entityController = RequireGameType("EntityController");
            Type defaultAiBehaviour = RequireGameType("DefaultAiBehaviour");
            Type aiBehaviour = RequireGameType("AiBehaviour");
            Type deckEntry = RequireGameType("EntityInAiDeck");
            Type balanceAsset = RequireGameType("EntityBalancingScriptableObject");
            Type balanceRow = RequireGameType("EntityBalancingParameters");
            Type stageParameters = RequireGameType("StageParametersScriptableObject");
            Type blueprintPoolList = RequireGameType("BlueprintPoolList");
            Type blueprintPool = RequireGameType("BlueprintPool");
            Type worldList = RequireGameType("WorldList");
            Type initMap = RequireGameType("InitMap");
            Type entityIdFilter = RequireGameType("Entity.EntityIdBlueprintFilter");

            ValidateGameAssemblyMvid(entityController);
            RequireField(balanceAsset, "parameters", typeof(List<>).MakeGenericType(balanceRow));
            RequireField(balanceRow, "entityId", typeof(string));
            RequireField(balanceRow, "prefabLocation", typeof(string));
            RequireField(balanceRow, "imageLocation", typeof(string));
            RequireField(balanceRow, "cost", typeof(int));
            RequireField(balanceRow, "combatValue", typeof(int));
            RequireField(balanceRow, "maxHealth", typeof(int));
            RequireField(balanceRow, "moveSpeed", typeof(float));
            RequireField(balanceRow, "isAllowedAsBlueprint", typeof(bool));
            RequireField(balanceRow, "inactive", typeof(bool));
            RequireField(stageParameters, "levelCount", typeof(int));
            RequireField(stageParameters, "maxWidth", typeof(int));
            RequireField(stageParameters, "coinReward", typeof(int));
            RequireField(stageParameters, "twoWayBranchingProbability", typeof(float));
            RequireField(stageParameters, "blueprintPoolList", blueprintPoolList);
            RequireField(stageParameters, "worldList", worldList);
            RequireField(blueprintPoolList, "blueprintPools", typeof(List<>).MakeGenericType(blueprintPool));
            RequireField(blueprintPool, "poolId", typeof(string));
            RequireField(blueprintPool, "blueprintsAreExclusive", typeof(bool));
            FieldInfo filterField = RequireField(blueprintPool, "blueprintFilter", null);
            if (!filterField.FieldType.IsAssignableFrom(entityIdFilter))
            {
                throw new InvalidOperationException("BlueprintPool.blueprintFilter cannot accept Entity.EntityIdBlueprintFilter.");
            }
            RequireField(entityIdFilter, "hasEntityId", typeof(string));
            RequireField(worldList, "worlds", typeof(List<>).MakeGenericType(initMap));
            RequireField(initMap, "nameLocaId", typeof(string));
            RequireField(initMap, "worldGenerator", null);
            RequireField(aiBehaviour, "isDeactivated", typeof(bool));
            RequireField(defaultAiBehaviour, "_constructableEntities", typeof(List<>).MakeGenericType(deckEntry));
            RequireField(deckEntry, "entityId", typeof(string));
            RequireField(aiBehaviour, "aiBases", null);
        }

        private static void ValidateGameAssemblyMvid(Type gameType)
        {
            Assembly assembly = gameType.Assembly;
            if (!String.Equals(assembly.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Game type " + gameType.FullName +
                    " did not resolve from Assembly-CSharp.dll.");
            }

            string actualMvid = assembly.ManifestModule.ModuleVersionId.ToString("D");
            if (!String.Equals(actualMvid, ExpectedGameAssemblyMvid, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Assembly-CSharp.dll MVID mismatch. Expected " +
                    ExpectedGameAssemblyMvid + ", found " + actualMvid + ".");
            }
        }

        private static void ValidateAuthoredTemplatePrefabs()
        {
            Type entityControllerType = RequireGameType("EntityController");
            Type defaultAiType = RequireGameType("DefaultAiBehaviour");
            Type aiBehaviourType = RequireGameType("AiBehaviour");
            Type deckEntryType = RequireGameType("EntityInAiDeck");
            Type initMapType = RequireGameType("InitMap");

            GameObject unitPrefab = RequirePrefab("ScoutTank.prefab", entityControllerType);
            if (unitPrefab.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                throw new InvalidDataException("ScoutTank.prefab must be based on a game-compatible unit prefab and contain a visual Renderer hierarchy.");
            }

            GameObject aiPrefab = RequirePrefab("RCL_SampleBaseBuilderAI.prefab", defaultAiType);
            Component[] aiComponents = aiPrefab.GetComponents(defaultAiType);
            if (!aiPrefab.activeSelf || aiComponents == null || aiComponents.Length != 1 ||
                !(aiComponents[0] is Behaviour) || !((Behaviour)aiComponents[0]).enabled)
            {
                throw new InvalidDataException("The authored AI prefab must be active and contain exactly one enabled DefaultAiBehaviour.");
            }

            if ((bool)RequireField(aiBehaviourType, "isDeactivated", typeof(bool)).GetValue(aiComponents[0]))
            {
                throw new InvalidDataException("The authored AI prefab has AiBehaviour.isDeactivated set to true.");
            }

            IList deck = RequireField(defaultAiType, "_constructableEntities", typeof(List<>).MakeGenericType(deckEntryType))
                .GetValue(aiComponents[0]) as IList;
            IList bases = RequireField(aiBehaviourType, "aiBases", null).GetValue(aiComponents[0]) as IList;
            if (deck == null || deck.Count == 0 || bases == null || bases.Count == 0)
            {
                throw new InvalidDataException("The authored AI prefab needs at least one constructable entity and one configured aiBases entry.");
            }

            FieldInfo deckEntityIdField = RequireField(deckEntryType, "entityId", typeof(string));
            bool containsSampleUnit = false;
            for (int i = 0; i < deck.Count; i++)
            {
                object entry = deck[i];
                if (entry != null && String.Equals((string)deckEntityIdField.GetValue(entry),
                    "sample:scout-tank", StringComparison.Ordinal))
                {
                    containsSampleUnit = true;
                    break;
                }
            }
            if (!containsSampleUnit)
            {
                throw new InvalidDataException("The authored AI deck must include sample:scout-tank.");
            }

            GameObject mapPrefab = RequirePrefab("SampleMap.prefab", initMapType);
            Component initMap = mapPrefab.GetComponent(initMapType);
            if (initMap == null || RequireField(initMapType, "worldGenerator", null).GetValue(initMap) == null)
            {
                throw new InvalidDataException("SampleMap.prefab needs an InitMap component with a configured WorldGenerator.");
            }
        }

        private static void ValidateAuthoredDataAssets()
        {
            Type balanceType = RequireGameType("EntityBalancingScriptableObject");
            Type stageType = RequireGameType("StageParametersScriptableObject");
            Type initMapType = RequireGameType("InitMap");
            string balancePath = AssetsFolder + "/ScoutTankBalance.asset";
            string stagePath = AssetsFolder + "/TrainingStage.asset";
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;

            if (!File.Exists(Path.Combine(projectRoot, balancePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                throw new InvalidDataException("Author ScoutTankBalance.asset from a complete native balance template before scaffolding.");
            }
            if (!File.Exists(Path.Combine(projectRoot, stagePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                throw new InvalidDataException("Author TrainingStage.asset from a complete native stage template before scaffolding.");
            }

            UnityEngine.Object balance = AssetDatabase.LoadAssetAtPath(balancePath, balanceType);
            UnityEngine.Object stage = AssetDatabase.LoadAssetAtPath(stagePath, stageType);
            GameObject map = AssetDatabase.LoadAssetAtPath<GameObject>(AssetsFolder + "/SampleMap.prefab");
            if (balance == null || stage == null || map == null)
            {
                throw new InvalidDataException("The authored balance, stage, or SampleMap asset is missing or has an unexpected Unity type.");
            }

            ValidateBalanceAsset(balance, balanceType);
            ValidateStageAsset(stage, stageType, initMapType, map);
        }

        private static void ValidateBalanceAsset(UnityEngine.Object asset, Type balanceType)
        {
            Type rowType = RequireGameType("EntityBalancingParameters");
            IList rows = RequireField(balanceType, "parameters", typeof(List<>).MakeGenericType(rowType))
                .GetValue(asset) as IList;
            if (rows == null || rows.Count != 1 || rows[0] == null || rows[0].GetType() != rowType)
            {
                throw new InvalidDataException("ScoutTankBalance.asset must contain exactly one native EntityBalancingParameters row.");
            }

            object row = rows[0];
            string entityId = (string)RequireField(rowType, "entityId", typeof(string)).GetValue(row);
            int maxHealth = (int)RequireField(rowType, "maxHealth", typeof(int)).GetValue(row);
            int cost = (int)RequireField(rowType, "cost", typeof(int)).GetValue(row);
            int combatValue = (int)RequireField(rowType, "combatValue", typeof(int)).GetValue(row);
            float moveSpeed = (float)RequireField(rowType, "moveSpeed", typeof(float)).GetValue(row);
            if (!String.Equals(entityId, "sample:scout-tank", StringComparison.Ordinal))
            {
                throw new InvalidDataException("ScoutTankBalance.asset row must declare entityId sample:scout-tank.");
            }
            if (maxHealth <= 0 || cost < 0 || combatValue < 0 || Single.IsNaN(moveSpeed) ||
                Single.IsInfinity(moveSpeed) || moveSpeed < 0f)
            {
                throw new InvalidDataException("ScoutTankBalance.asset must retain usable native balance values: positive maxHealth and finite non-negative cost, combatValue, and moveSpeed.");
            }
        }

        private static void ValidateStageAsset(
            UnityEngine.Object asset,
            Type stageType,
            Type initMapType,
            GameObject expectedMapPrefab)
        {
            int levelCount = (int)RequireField(stageType, "levelCount", typeof(int)).GetValue(asset);
            int maxWidth = (int)RequireField(stageType, "maxWidth", typeof(int)).GetValue(asset);
            int coinReward = (int)RequireField(stageType, "coinReward", typeof(int)).GetValue(asset);
            float branchingProbability = (float)RequireField(stageType,
                "twoWayBranchingProbability", typeof(float)).GetValue(asset);
            if (levelCount < 1 || maxWidth < 1 || coinReward < 0 || Single.IsNaN(branchingProbability) ||
                Single.IsInfinity(branchingProbability) || branchingProbability < 0f || branchingProbability > 1f)
            {
                throw new InvalidDataException("TrainingStage.asset has invalid levelCount, maxWidth, coinReward, or twoWayBranchingProbability values.");
            }

            Type poolListType = RequireGameType("BlueprintPoolList");
            Type poolType = RequireGameType("BlueprintPool");
            Type worldListType = RequireGameType("WorldList");
            object poolList = RequireField(stageType, "blueprintPoolList", poolListType).GetValue(asset);
            object worldList = RequireField(stageType, "worldList", worldListType).GetValue(asset);
            if (poolList == null || poolList.GetType() != poolListType ||
                worldList == null || worldList.GetType() != worldListType)
            {
                throw new InvalidDataException("TrainingStage.asset must preserve native BlueprintPoolList and WorldList references.");
            }

            IList pools = RequireField(poolListType, "blueprintPools", typeof(List<>).MakeGenericType(poolType))
                .GetValue(poolList) as IList;
            if (pools == null || pools.Count == 0)
            {
                throw new InvalidDataException("TrainingStage.asset needs at least one native BlueprintPool.");
            }

            var poolIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < pools.Count; i++)
            {
                object pool = pools[i];
                if (pool == null || pool.GetType() != poolType || (pool as UnityEngine.Object) == null)
                {
                    throw new InvalidDataException("TrainingStage.asset contains a null or unexpected blueprint pool.");
                }
                string poolId = (string)RequireField(poolType, "poolId", typeof(string)).GetValue(pool);
                object filter = RequireField(poolType, "blueprintFilter", null).GetValue(pool);
                if (String.IsNullOrWhiteSpace(poolId) || !poolIds.Add(poolId) || IsNullOrDestroyedUnityObject(filter))
                {
                    throw new InvalidDataException("TrainingStage.asset contains an empty/duplicate pool ID or a pool without a filter.");
                }
                MethodInfo refresh = poolType.GetMethod("OnBlueprintFilterChanged",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (refresh == null || refresh.GetParameters().Length != 0 || refresh.ReturnType != typeof(void))
                {
                    throw new InvalidDataException("BlueprintPool is missing the expected native OnBlueprintFilterChanged() method.");
                }
            }

            IList worlds = RequireField(worldListType, "worlds", typeof(List<>).MakeGenericType(initMapType))
                .GetValue(worldList) as IList;
            Component expectedMap = expectedMapPrefab == null ? null : expectedMapPrefab.GetComponent(initMapType);
            if (worlds == null || worlds.Count == 0 || expectedMap == null)
            {
                throw new InvalidDataException("TrainingStage.asset needs native InitMap worlds and must reference the bundled SampleMap prefab.");
            }

            FieldInfo generatorField = RequireField(initMapType, "worldGenerator", null);
            bool referencesSampleMap = false;
            for (int i = 0; i < worlds.Count; i++)
            {
                Component world = worlds[i] as Component;
                if (world == null || world.GetType() != initMapType)
                {
                    throw new InvalidDataException("TrainingStage.asset contains a null, unexpected, or incomplete InitMap.");
                }
                ValidateNoMissingPrefabComponents(world.gameObject, "TrainingStage world");
                object generator = generatorField.GetValue(world);
                if (IsNullOrDestroyedUnityObject(generator))
                {
                    throw new InvalidDataException("TrainingStage.asset contains an InitMap without a WorldGenerator.");
                }
                if (world == expectedMap) referencesSampleMap = true;
            }
            if (!referencesSampleMap)
            {
                throw new InvalidDataException("TrainingStage.asset worldList must include the InitMap from the bundled SampleMap prefab.");
            }
        }

        private static void ValidateNoMissingPrefabComponents(GameObject gameObject, string owner)
        {
            Component[] components = gameObject.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    throw new InvalidDataException(owner + " contains a missing script component.");
                }
            }
        }

        private static bool IsNullOrDestroyedUnityObject(object value)
        {
            if (System.Object.ReferenceEquals(value, null))
            {
                return true;
            }

            UnityEngine.Object unityObject = value as UnityEngine.Object;
            return !System.Object.ReferenceEquals(unityObject, null) && unityObject == null;
        }

        private static void ProbeGameComponent(Type componentType)
        {
            if (!typeof(MonoBehaviour).IsAssignableFrom(componentType) || componentType.IsAbstract ||
                componentType.ContainsGenericParameters)
            {
                throw new InvalidOperationException(componentType.FullName + " is not an attachable MonoBehaviour.");
            }

            var probe = new GameObject("RCL_AuthoringComponentProbe");
            probe.SetActive(false);
            try
            {
                Component component = AddRequiredComponent(probe, componentType);
                if (MonoScript.FromMonoBehaviour(component as MonoBehaviour) == null)
                {
                    throw new InvalidOperationException("Unity cannot serialize the game script " +
                        componentType.FullName + " into an AssetBundle.");
                }

                Component[] components = probe.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] == null)
                    {
                        throw new InvalidOperationException("Unity created a missing script for " + componentType.FullName + ".");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        private static void ValidateSampleAssets()
        {
            Type entityControllerType = RequireGameType("EntityController");
            Type aiType = RequireGameType("DefaultAiBehaviour");
            Type aiBaseType = RequireGameType("AiBehaviour");
            Type deckEntryType = RequireGameType("EntityInAiDeck");
            Type balanceType = RequireGameType("EntityBalancingScriptableObject");
            Type stageType = RequireGameType("StageParametersScriptableObject");
            Type initMapType = RequireGameType("InitMap");

            RequireBundleAsset("ScoutTankCard.png", typeof(Sprite));
            GameObject unitPrefab = RequirePrefab("ScoutTank.prefab", entityControllerType);
            if (unitPrefab.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                throw new InvalidDataException("ScoutTank.prefab must contain a renderer in its visual hierarchy.");
            }

            GameObject aiPrefab = RequirePrefab("RCL_SampleBaseBuilderAI.prefab", aiType);
            if (!aiPrefab.activeSelf)
            {
                throw new InvalidDataException("The sample AI prefab must be active.");
            }

            Component[] aiComponents = aiPrefab.GetComponents(aiType);
            if (aiComponents == null || aiComponents.Length != 1 || !(aiComponents[0] is Behaviour) ||
                !((Behaviour)aiComponents[0]).enabled)
            {
                throw new InvalidDataException("The sample AI prefab must contain exactly one enabled DefaultAiBehaviour.");
            }

            if ((bool)RequireField(aiBaseType, "isDeactivated", typeof(bool)).GetValue(aiComponents[0]))
            {
                throw new InvalidDataException("The sample AI prefab has AiBehaviour.isDeactivated set to true.");
            }

            IList deck = RequireField(aiType, "_constructableEntities", typeof(List<>).MakeGenericType(deckEntryType))
                .GetValue(aiComponents[0]) as IList;
            if (deck == null || deck.Count == 0)
            {
                throw new InvalidDataException("The sample AI prefab needs at least one constructable entity in its deck.");
            }

            bool hasSampleUnit = false;
            for (int i = 0; i < deck.Count; i++)
            {
                object entry = deck[i];
                if (entry != null && String.Equals((string)RequireField(deckEntryType, "entityId", typeof(string))
                        .GetValue(entry), "sample:scout-tank", StringComparison.Ordinal))
                {
                    hasSampleUnit = true;
                    break;
                }
            }
            if (!hasSampleUnit)
            {
                throw new InvalidDataException("The sample AI deck must include sample:scout-tank.");
            }

            IList bases = RequireField(aiBaseType, "aiBases", null).GetValue(aiComponents[0]) as IList;
            if (bases == null || bases.Count == 0)
            {
                throw new InvalidDataException("The sample AI needs at least one configured aiBases entry before it can run.");
            }

            UnityEngine.Object balanceAsset = RequireBundleAsset("ScoutTankBalance.asset", balanceType);
            ValidateBalanceAsset(balanceAsset, balanceType);
            UnityEngine.Object stageAsset = RequireBundleAsset("TrainingStage.asset", stageType);
            GameObject mapPrefab = RequirePrefab("SampleMap.prefab", initMapType);
            ValidateStageAsset(stageAsset, stageType, initMapType, mapPrefab);

            string[] expectedAssets =
            {
                AssetsFolder + "/ScoutTankCard.png",
                AssetsFolder + "/ScoutTank.prefab",
                AssetsFolder + "/RCL_SampleBaseBuilderAI.prefab",
                AssetsFolder + "/ScoutTankBalance.asset",
                AssetsFolder + "/TrainingStage.asset",
                AssetsFolder + "/SampleMap.prefab"
            };
            string[] assignedAssets = AssetDatabase.GetAssetPathsFromAssetBundle(SampleBundleName);
            if (assignedAssets == null || assignedAssets.Length != expectedAssets.Length)
            {
                throw new InvalidDataException("AssetBundle label '" + SampleBundleName +
                    "' must contain exactly the six validated sample assets.");
            }

            var expectedAssetSet = new HashSet<string>(expectedAssets, StringComparer.Ordinal);
            var assignedAssetSet = new HashSet<string>(assignedAssets, StringComparer.Ordinal);
            if (assignedAssetSet.Count != assignedAssets.Length)
            {
                throw new InvalidDataException("AssetBundle label '" + SampleBundleName +
                    "' contains duplicate asset paths.");
            }

            for (int i = 0; i < assignedAssets.Length; i++)
            {
                if (!expectedAssetSet.Contains(assignedAssets[i]))
                {
                    throw new InvalidDataException("Unexpected asset assigned to sample bundle: " + assignedAssets[i]);
                }
            }

            for (int i = 0; i < expectedAssets.Length; i++)
            {
                if (!assignedAssetSet.Contains(expectedAssets[i]))
                {
                    throw new InvalidDataException("Required sample asset is not assigned to the bundle: " + expectedAssets[i]);
                }
            }
        }

        private static GameObject RequirePrefab(string fileName, Type requiredComponent)
        {
            string path = AssetsFolder + "/" + fileName;
            RequireBundleAsset(fileName, typeof(GameObject));
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new InvalidDataException("Could not load sample prefab " + path + ".");
            }

            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    throw new InvalidDataException(path + " contains a missing script component.");
                }
            }

            if (prefab.GetComponent(requiredComponent) == null)
            {
                throw new InvalidDataException(path + " is missing required component " + requiredComponent.FullName + ".");
            }

            return prefab;
        }

        private static UnityEngine.Object RequireBundleAsset(string fileName, Type expectedType)
        {
            string path = AssetsFolder + "/" + fileName;
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(path, expectedType);
            if (asset == null)
            {
                throw new InvalidDataException("Missing or invalid sample asset " + path + " (expected " +
                    expectedType.FullName + ").");
            }

            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer == null || !String.Equals(importer.assetBundleName, SampleBundleName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(path + " must have the AssetBundle label " + SampleBundleName + ".");
            }

            return asset;
        }

        private static void AssignBundleLabel(string assetPath)
        {
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
            {
                throw new InvalidOperationException("Unity did not create an AssetImporter for " + assetPath + ".");
            }

            importer.assetBundleName = SampleBundleName;
            importer.SaveAndReimport();
            importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null || !String.Equals(importer.assetBundleName, SampleBundleName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unity did not retain the AssetBundle label on " + assetPath + ".");
            }
        }

        private static Type RequireGameType(string typeName)
        {
            Type type = FindGameType(typeName);
            if (type == null)
            {
                throw new InvalidOperationException("Required Rogue Command type '" + typeName +
                    "' was not resolved. Check the local Editor-only game assembly references.");
            }

            if (!String.Equals(type.Assembly.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Resolved type '" + typeName + "' came from unexpected assembly " +
                    type.Assembly.GetName().Name + ".");
            }

            return type;
        }

        private static FieldInfo RequireField(Type type, string fieldName, Type expectedType)
        {
            FieldInfo field = FindInstanceField(type, fieldName);
            if (expectedType != null && field.FieldType != expectedType)
            {
                throw new InvalidOperationException(type.FullName + "." + fieldName + " has type " +
                    field.FieldType.FullName + ", expected " + expectedType.FullName + ".");
            }

            return field;
        }

        private static FieldInfo FindInstanceField(Type type, string fieldName)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }
            }

            throw new MissingFieldException(type.FullName, fieldName);
        }

        private static Component AddRequiredComponent(GameObject owner, Type componentType)
        {
            if (owner == null || componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                throw new ArgumentException("A live GameObject and a Unity Component type are required.");
            }

            Component component = owner.AddComponent(componentType);
            if (component == null || owner.GetComponent(componentType) == null)
            {
                throw new InvalidOperationException("Unity could not attach game component " + componentType.FullName +
                    "; it may be unresolved or incompatible with the Editor.");
            }

            return component;
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", String.Empty).ToLowerInvariant();
            }
        }

        private static bool _assemblyResolverRegistered = false;

        private static void EnsureAssemblyResolver()
        {
            if (_assemblyResolverRegistered) return;
            _assemblyResolverRegistered = true;

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string simpleName = args.Name.Split(',')[0].Trim();
                string candidate = Path.Combine(Application.dataPath, "Plugins", "GameAssemblies", simpleName + ".dll");
                if (File.Exists(candidate))
                {
                    try { return Assembly.LoadFrom(candidate); } catch {}
                }
                string managedCandidate = Path.Combine(DefaultGameManagedPath, simpleName + ".dll");
                if (File.Exists(managedCandidate))
                {
                    try { return Assembly.LoadFrom(managedCandidate); } catch {}
                }
                return null;
            };
        }

        private static Type FindGameType(string typeName)
        {
            EnsureAssemblyResolver();

            // 1. Check loaded assemblies by direct GetType
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type t = assemblies[i].GetType(typeName, false);
                    if (t != null) return t;
                }
                catch {}
            }

            // 2. Scan loaded assemblies by Name or FullName
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type[] types = assemblies[i].GetTypes();
                    for (int j = 0; j < types.Length; j++)
                    {
                        if (types[j] != null && (types[j].Name == typeName || types[j].FullName == typeName))
                        {
                            return types[j];
                        }
                    }
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    for (int j = 0; j < rtle.Types.Length; j++)
                    {
                        if (rtle.Types[j] != null && (rtle.Types[j].Name == typeName || rtle.Types[j].FullName == typeName))
                        {
                            return rtle.Types[j];
                        }
                    }
                }
                catch {}
            }

            // 3. Fallback: explicitly load Assembly-CSharp.dll
            string pluginDll = Path.Combine(Application.dataPath, "Plugins", "GameAssemblies", "Assembly-CSharp.dll");
            if (!File.Exists(pluginDll))
            {
                pluginDll = Path.Combine(DefaultGameManagedPath, "Assembly-CSharp.dll");
            }

            if (File.Exists(pluginDll))
            {
                try
                {
                    Assembly asm = Assembly.LoadFrom(pluginDll);
                    Type t = asm.GetType(typeName, false);
                    if (t != null) return t;

                    Type[] types = asm.GetTypes();
                    for (int j = 0; j < types.Length; j++)
                    {
                        if (types[j] != null && (types[j].Name == typeName || types[j].FullName == typeName))
                        {
                            return types[j];
                        }
                    }
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    for (int j = 0; j < rtle.Types.Length; j++)
                    {
                        if (rtle.Types[j] != null && (rtle.Types[j].Name == typeName || rtle.Types[j].FullName == typeName))
                        {
                            return rtle.Types[j];
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[SamplePackScaffolder] Could not load Assembly-CSharp.dll: " + ex.Message);
                }
            }

            return null;
        }

        private static void SetField(Type type, object target, string fieldName, object value)
        {
            if (type == null || target == null)
            {
                throw new ArgumentNullException(type == null ? "type" : "target");
            }

            FieldInfo field = FindInstanceField(type, fieldName);
            if (value == null)
            {
                if (field.FieldType.IsValueType && Nullable.GetUnderlyingType(field.FieldType) == null)
                {
                    throw new InvalidOperationException(type.FullName + "." + fieldName + " cannot receive null.");
                }
            }
            else if (!field.FieldType.IsInstanceOfType(value))
            {
                throw new InvalidOperationException(type.FullName + "." + fieldName + " expects " +
                    field.FieldType.FullName + " but received " + value.GetType().FullName + ".");
            }

            if (field.IsInitOnly)
            {
                throw new InvalidOperationException(type.FullName + "." + fieldName + " is readonly.");
            }

            field.SetValue(target, value);
        }
    }
}
