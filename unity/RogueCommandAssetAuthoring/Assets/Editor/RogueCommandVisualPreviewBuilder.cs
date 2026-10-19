using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace RogueCommandExpansion.Authoring
{
    /// <summary>
    /// Non-distributable preview flow for two FBX models and PBR maps. It applies the required Unity texture
    /// import settings to the source maps, copies inputs into a new preview folder, and builds a temporary bundle.
    /// </summary>
    internal sealed class RogueCommandVisualPreviewBuilder : EditorWindow
    {
        private const string PreviewAssetRoot = "Assets/__RogueCommandVisualPreview";
        private const string ShaderAssetPath =
            "Assets/RogueCommandExpansionSample/Shaders/VehiclePBR_VertexWear.shader";
        private const string ShaderName = "RogueCommand/VehiclePBR_VertexWear";
        private const string BundleFileName = "rogue-command-visual-preview.bundle";
        private const float FactoryNewWearStrength = 0.25f;
        private const string SampleModelRoot = "Assets/RogueCommandExpansionSample/Models";
        private const string SampleTextureRoot = SampleModelRoot + "/Textures";

        [Serializable]
        private sealed class ModelInput
        {
            public string fbxPath = String.Empty;
            public string albedoPath = String.Empty;
            public string metallicGlossPath = String.Empty;
            public string normalPath = String.Empty;
            public string emissionPath = String.Empty;
        }

        [SerializeField] private ModelInput _modelA = new ModelInput();
        [SerializeField] private ModelInput _modelB = new ModelInput();
        [SerializeField] private string _displayNameA = "PreviewA";
        [SerializeField] private string _displayNameB = "PreviewB";
        [SerializeField] private Vector2 _scroll;

        [MenuItem("Rogue Command/Visual Preview/Build Two-Model Preview")]
        private static void OpenWindow()
        {
            RogueCommandVisualPreviewBuilder window = GetWindow<RogueCommandVisualPreviewBuilder>(
                "Visual Preview Builder");
            window.minSize = new Vector2(560.0f, 460.0f);
            window.Show();
        }

        private void OnGUI()
        {
            if (_modelA == null) _modelA = new ModelInput();
            if (_modelB == null) _modelB = new ModelInput();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox(
                "Select two FBX files and their texture maps. Build configures Unity import settings for the source maps, " +
                "copies each input into an isolated Assets/__RogueCommandVisualPreview run folder, creates script-free " +
                "PBR prefabs, then writes and reopens a temporary AssetBundle under artifacts/visual-previews. It does " +
                "not edit gameplay, manifests, source FBX/image data, project settings, or distributed bundles.",
                MessageType.Info);

            DrawModelInput("Model A", _modelA, ref _displayNameA);
            EditorGUILayout.Space(8.0f);
            DrawModelInput("Model B", _modelB, ref _displayNameB);
            EditorGUILayout.Space(12.0f);

            using (new EditorGUI.DisabledScope(!InputsLookReady(_modelA) || !InputsLookReady(_modelB)))
            {
                if (GUILayout.Button("Create Two-Model Preview Bundle", GUILayout.Height(34.0f)))
                {
                    BuildPreviewPair(true);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawModelInput(string heading, ModelInput input, ref string displayName)
        {
            EditorGUILayout.LabelField(heading, EditorStyles.boldLabel);
            displayName = EditorGUILayout.TextField("Prefab Name", displayName);
            DrawFileField("FBX", ref input.fbxPath, "Select FBX model");
            DrawFileField("Albedo (sRGB)", ref input.albedoPath, "Select albedo map");
            DrawFileField("Metallic Gloss (linear)", ref input.metallicGlossPath,
                "Select metallic/smoothness map");
            DrawFileField("Normal (optional)", ref input.normalPath, "Select normal map");
            DrawFileField("Emission (optional)", ref input.emissionPath, "Select emission map");
        }

        private static void DrawFileField(string label, ref string value, string dialogTitle)
        {
            EditorGUILayout.BeginHorizontal();
            value = EditorGUILayout.TextField(label, value);
            if (GUILayout.Button("…", GUILayout.Width(28.0f)))
            {
                string directory = String.IsNullOrEmpty(value)
                    ? Application.dataPath
                    : Path.GetDirectoryName(value);
                if (String.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    directory = Application.dataPath;
                }

                string selected = EditorUtility.OpenFilePanel(dialogTitle, directory, String.Empty);
                if (!String.IsNullOrEmpty(selected))
                {
                    value = selected;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static bool InputsLookReady(ModelInput input)
        {
            return input != null && File.Exists(input.fbxPath) &&
                File.Exists(input.albedoPath) && File.Exists(input.metallicGlossPath) &&
                IsOptionalFileValid(input.normalPath) && IsOptionalFileValid(input.emissionPath);
        }

        private static bool IsOptionalFileValid(string path)
        {
            return String.IsNullOrEmpty(path) || File.Exists(path);
        }

        [MenuItem("Rogue Command/Visual Preview/Build Salvage Hauler + Skyguard SAM")]
        public static void BuildSalvageHaulerAndSkyguardPreview()
        {
            BuildSampleVisualPreview("SalvageHauler", "SkyguardSAM");
        }


        [MenuItem("Rogue Command/Visual Preview/Build Wraith Stealth Wing + Subterranean Mole")]
        public static void BuildWraithStealthWingAndSubterraneanMolePreview()
        {
            BuildSampleVisualPreview("WraithStealthWing", "SubterraneanMole");
        }

        private static void BuildSampleVisualPreview(string modelAName, string modelBName)
        {
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                var builder = CreateInstance<RogueCommandVisualPreviewBuilder>();
                builder._displayNameA = modelAName;
                builder._displayNameB = modelBName;
                builder._modelA = CreateSampleInput(projectRoot, modelAName);
                builder._modelB = CreateSampleInput(projectRoot, modelBName);
                try
                {
                    builder.BuildPreviewPair(!Application.isBatchMode,
                        new[] { modelAName, modelBName });
                }
                finally
                {
                    DestroyImmediate(builder);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        [MenuItem("Rogue Command/Visual Preview/Configure Sample Source Texture Imports")]
        public static void ConfigureSampleSourceTextureImportsOnly()
        {
            try
            {
                ConfigureSampleSourceTextureImports("SalvageHauler", "SkyguardSAM");
                Debug.Log("Configured and verified source PBR texture import settings for Salvage Hauler and Skyguard SAM.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static void ConfigureSampleSourceTextureImports(string modelAName, string modelBName)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string[] modelNames = { modelAName, modelBName };
            string[] mapSuffixes = { "_Albedo.png", "_MetallicGloss.png", "_Normal.png", "_Emission.png" };
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            for (int modelIndex = 0; modelIndex < modelNames.Length; modelIndex++)
            {
                for (int mapIndex = 0; mapIndex < mapSuffixes.Length; mapIndex++)
                {
                    string assetPath = SampleTextureRoot + "/" + modelNames[modelIndex] + mapSuffixes[mapIndex];
                    string absolutePath = ToAbsolutePath(projectRoot, assetPath);
                    if (!File.Exists(absolutePath))
                    {
                        throw new FileNotFoundException("Required source PBR texture is missing.", absolutePath);
                    }
                    if (AssetImporter.GetAtPath(assetPath) as TextureImporter == null)
                    {
                        throw new InvalidDataException("Unity did not create a TextureImporter for " + assetPath + ".");
                    }
                }
            }

            for (int modelIndex = 0; modelIndex < modelNames.Length; modelIndex++)
            {
                string textureStem = SampleTextureRoot + "/" + modelNames[modelIndex];
                ConfigureAndLogSourceTexture(textureStem + "_Albedo.png",
                    TextureImporterType.Default, true, true, "albedo");
                ConfigureAndLogSourceTexture(textureStem + "_MetallicGloss.png",
                    TextureImporterType.Default, false, true, "MetallicGloss");
                ConfigureAndLogSourceTexture(textureStem + "_Normal.png",
                    TextureImporterType.NormalMap, false, false, "normal");
                ConfigureAndLogSourceTexture(textureStem + "_Emission.png",
                    TextureImporterType.Default, true, false, "emission");
            }
        }

        private static void ConfigureAndLogSourceTexture(
            string assetPath,
            TextureImporterType textureType,
            bool sRgbTexture,
            bool preserveInputAlpha,
            string label)
        {
            ConfigureTextureImporter(assetPath, textureType, sRgbTexture, preserveInputAlpha);
            Texture2D texture = LoadTexture(assetPath, label);
            LogTextureImporter(assetPath, texture);
        }

        private static ModelInput CreateSampleInput(string projectRoot, string modelName)
        {
            string textureStem = SampleTextureRoot + "/" + modelName;
            return new ModelInput
            {
                fbxPath = ToAbsolutePath(projectRoot, SampleModelRoot + "/" + modelName + ".fbx"),
                albedoPath = ToAbsolutePath(projectRoot, textureStem + "_Albedo.png"),
                metallicGlossPath = ToAbsolutePath(projectRoot, textureStem + "_MetallicGloss.png"),
                normalPath = ToAbsolutePath(projectRoot, textureStem + "_Normal.png"),
                emissionPath = ToAbsolutePath(projectRoot, textureStem + "_Emission.png")
            };
        }

        private static string ToAbsolutePath(string projectRoot, string assetPath)
        {
            return Path.GetFullPath(Path.Combine(projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private void BuildPreviewPair(bool showDialog, string[] sourceTextureModelNames = null)
        {
            try
            {
                ValidateInputs();
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,
                    BuildTarget.StandaloneWindows64))
                {
                    throw new InvalidOperationException(
                        "Install Unity Windows Build Support to create the temporary preview bundle.");
                }

                Shader shader = Shader.Find(ShaderName);
                if (shader == null || AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath) == null)
                {
                    throw new InvalidOperationException(
                        "The authoring project is missing its RogueCommand/VehiclePBR_VertexWear shader asset.");
                }

                if (sourceTextureModelNames != null)
                {
                    if (sourceTextureModelNames.Length != 2 ||
                        String.IsNullOrWhiteSpace(sourceTextureModelNames[0]) ||
                        String.IsNullOrWhiteSpace(sourceTextureModelNames[1]))
                    {
                        throw new InvalidDataException(
                            "Source texture configuration requires exactly two model names.");
                    }
                    ConfigureSampleSourceTextureImports(sourceTextureModelNames[0], sourceTextureModelNames[1]);
                }

                string runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" +
                    Guid.NewGuid().ToString("N").Substring(0, 8);
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string runAssetRoot = PreviewAssetRoot + "/" + runId;
                string artifactRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", "..", "artifacts",
                    "visual-previews", runId));

                EnsureAssetFolder(runAssetRoot);
                string prefabA = BuildModelPreview(_modelA, _displayNameA, "ModelA", runAssetRoot);
                string prefabB = BuildModelPreview(_modelB, _displayNameB, "ModelB", runAssetRoot);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                Directory.CreateDirectory(artifactRoot);
                BuildTemporaryBundle(artifactRoot, prefabA, prefabB, runAssetRoot);

                string bundlePath = Path.Combine(artifactRoot, BundleFileName);
                Debug.Log("Visual-only two-model preview built and reopened. Preview assets: " + runAssetRoot +
                    "; temporary bundle: " + bundlePath + ". No sample pack or distributed bundle was changed.");
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("Visual preview ready",
                        "Script-free prefabs were created under:\n" + runAssetRoot +
                        "\n\nTemporary bundle:\n" + bundlePath,
                        "OK");
                }
                else if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("Visual preview build failed", exception.Message, "OK");
                }
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private void ValidateInputs()
        {
            if (!InputsLookReady(_modelA) || !InputsLookReady(_modelB))
            {
                throw new FileNotFoundException(
                    "Each model needs an existing FBX, albedo map, and metallic/gloss map. Optional maps must also exist.");
            }

            if (String.Equals(Path.GetFullPath(_modelA.fbxPath), Path.GetFullPath(_modelB.fbxPath),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Choose two different FBX files for the preview pair.");
            }

            ValidateModelExtension(_modelA.fbxPath, "Model A FBX");
            ValidateModelExtension(_modelB.fbxPath, "Model B FBX");
            ValidateImageExtension(_modelA.albedoPath, "Model A albedo");
            ValidateImageExtension(_modelA.metallicGlossPath, "Model A metallic/gloss");
            ValidateImageExtension(_modelB.albedoPath, "Model B albedo");
            ValidateImageExtension(_modelB.metallicGlossPath, "Model B metallic/gloss");
            ValidateOptionalImageExtension(_modelA.normalPath, "Model A normal");
            ValidateOptionalImageExtension(_modelA.emissionPath, "Model A emission");
            ValidateOptionalImageExtension(_modelB.normalPath, "Model B normal");
            ValidateOptionalImageExtension(_modelB.emissionPath, "Model B emission");
            ValidatePrefabName(_displayNameA, "Model A");
            ValidatePrefabName(_displayNameB, "Model B");
            if (String.Equals(_displayNameA, _displayNameB, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Use distinct prefab names for Model A and Model B.");
            }
        }

        private static void ValidateOptionalImageExtension(string path, string label)
        {
            if (!String.IsNullOrEmpty(path)) ValidateImageExtension(path, label);
        }

        private static void ValidateModelExtension(string path, string label)
        {
            if (!String.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(label + " must be an FBX file.");
            }
        }

        private static void ValidateImageExtension(string path, string label)
        {
            string extension = Path.GetExtension(path);
            if (!String.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(extension, ".tga", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(label + " must be PNG, TGA, JPG, or JPEG.");
            }
        }

        private static string ValidatePrefabName(string value, string label)
        {
            string name = SanitizeName(value);
            if (String.IsNullOrEmpty(name))
            {
                throw new InvalidDataException(label + " needs a prefab name containing letters or numbers.");
            }
            return name;
        }

        private static string BuildModelPreview(
            ModelInput input,
            string requestedName,
            string slotName,
            string runAssetRoot)
        {
            string prefabName = ValidatePrefabName(requestedName, slotName);
            string slotRoot = runAssetRoot + "/" + slotName;
            string sourceFolder = slotRoot + "/Source";
            string textureFolder = slotRoot + "/Textures";
            string materialFolder = slotRoot + "/Materials";
            string prefabFolder = slotRoot + "/Prefabs";
            EnsureAssetFolder(sourceFolder);
            EnsureAssetFolder(textureFolder);
            EnsureAssetFolder(materialFolder);
            EnsureAssetFolder(prefabFolder);

            string fbxAssetPath = CopyInput(input.fbxPath, sourceFolder, String.Empty);
            string albedoAssetPath = CopyInput(input.albedoPath, textureFolder, "_Albedo");
            string metallicAssetPath = CopyInput(input.metallicGlossPath, textureFolder, "_MetallicGloss");
            string normalAssetPath = String.IsNullOrEmpty(input.normalPath)
                ? String.Empty
                : CopyInput(input.normalPath, textureFolder, "_Normal");
            string emissionAssetPath = String.IsNullOrEmpty(input.emissionPath)
                ? String.Empty
                : CopyInput(input.emissionPath, textureFolder, "_Emission");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureTextureImporter(albedoAssetPath, TextureImporterType.Default, true, true);
            ConfigureTextureImporter(metallicAssetPath, TextureImporterType.Default, false, true);
            if (!String.IsNullOrEmpty(normalAssetPath))
            {
                ConfigureTextureImporter(normalAssetPath, TextureImporterType.NormalMap, false, false);
            }
            if (!String.IsNullOrEmpty(emissionAssetPath))
            {
                ConfigureTextureImporter(emissionAssetPath, TextureImporterType.Default, true, false);
            }

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
            if (modelAsset == null)
            {
                throw new InvalidDataException("Unity did not import the selected FBX: " + fbxAssetPath);
            }
            ValidateImportedMeshVertexColors(modelAsset, prefabName);

            Texture2D albedo = LoadTexture(albedoAssetPath, "albedo");
            Texture2D metallicGloss = LoadTexture(metallicAssetPath, "metallic/gloss");
            Texture2D normal = String.IsNullOrEmpty(normalAssetPath) ? null : LoadTexture(normalAssetPath, "normal");
            Texture2D emission = String.IsNullOrEmpty(emissionAssetPath)
                ? null
                : LoadTexture(emissionAssetPath, "emission");
            ValidateTextureSet(prefabName, albedo, metallicGloss, normal, emission);
            LogTextureImporter(albedoAssetPath, albedo);
            LogTextureImporter(metallicAssetPath, metallicGloss);
            if (normal != null) LogTextureImporter(normalAssetPath, normal);
            if (emission != null) LogTextureImporter(emissionAssetPath, emission);

            GameObject root = new GameObject(prefabName + "VisualPreview");
            GameObject model = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
            if (model == null) model = Instantiate(modelAsset);
            model.name = Path.GetFileNameWithoutExtension(fbxAssetPath);
            model.transform.SetParent(root.transform, false);
            try
            {
                CreateAndAssignPreviewMaterials(model, materialFolder, prefabName, albedo,
                    metallicGloss, normal, emission);

                string prefabPath = prefabFolder + "/" + prefabName + "VisualPreview.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                ValidateScriptFreePrefab(savedPrefab, prefabName, ShaderName);
                return prefabPath;
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        private static void CreateAndAssignPreviewMaterials(
            GameObject model,
            string materialFolder,
            string prefabName,
            Texture2D albedo,
            Texture2D metallicGloss,
            Texture2D normal,
            Texture2D emission)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException("Shader.Find failed for " + ShaderName + ".");

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidDataException("The imported FBX contains no MeshRenderer or SkinnedMeshRenderer.");
            }

            var materialsByKey = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Material[] sourceMaterials = renderer.sharedMaterials;
                if (sourceMaterials == null || sourceMaterials.Length == 0)
                {
                    sourceMaterials = new Material[] { null };
                }

                var assignedMaterials = new Material[sourceMaterials.Length];
                for (int slot = 0; slot < sourceMaterials.Length; slot++)
                {
                    Material source = sourceMaterials[slot];
                    string sourceName = source == null ? "MaterialSlot" + slot : source.name;
                    string key = SanitizeName(sourceName);
                    if (String.IsNullOrEmpty(key)) key = "MaterialSlot" + slot;

                    Material previewMaterial;
                    if (!materialsByKey.TryGetValue(key, out previewMaterial))
                    {
                        string prefix = prefabName + "_";
                        string uniqueName = key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            ? key
                            : prefix + key;
                        string materialPath = materialFolder + "/" + uniqueName + ".mat";
                        previewMaterial = CreatePreviewMaterial(shader, uniqueName,
                            albedo, metallicGloss, normal, emission);
                        AssetDatabase.CreateAsset(previewMaterial, materialPath);
                        materialsByKey.Add(key, previewMaterial);
                    }
                    assignedMaterials[slot] = previewMaterial;
                }
                renderer.sharedMaterials = assignedMaterials;
            }
        }

        private static Material CreatePreviewMaterial(
            Shader shader,
            string materialName,
            Texture2D albedo,
            Texture2D metallicGloss,
            Texture2D normal,
            Texture2D emission)
        {
            var material = new Material(shader);
            material.name = materialName;
            material.enableInstancing = true;
            material.SetTexture("_BaseMap", albedo);
            // The atlas already contains the Blender diffuse bake. A white multiplier avoids tinting it twice.
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_WearStrength", FactoryNewWearStrength);
            material.SetFloat("_AoStrength", 1.0f);
            material.SetFloat("_SootStrength", 1.0f);
            material.SetColor("_TeamColor", Color.white);

            if (metallicGloss != null)
            {
                material.SetTexture("_MetallicGlossMap", metallicGloss);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                // This project shader declares the SPEC variant keyword in its pragma.
                material.SetFloat("_GlossMapScale", 1.0f);
                material.SetFloat("_Metallic", 1.0f);
                material.SetFloat("_Smoothness", 1.0f);
                if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 1.0f);
            }

            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 1.0f);
                material.EnableKeyword("_NORMALMAP");
            }

            if (emission != null)
            {
                material.SetTexture("_EmissionMap", emission);
                // The emission map also contains the baked source material color.
                material.SetColor("_EmissionColor", Color.white);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }

            return material;
        }

        private static void ConfigureTextureImporter(
            string assetPath,
            TextureImporterType textureType,
            bool sRgbTexture,
            bool preserveInputAlpha)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidDataException("Unity did not create a TextureImporter for " + assetPath + ".");
            }

            bool changed = importer.textureType != textureType || importer.sRGBTexture != sRgbTexture;
            importer.textureType = textureType;
            importer.sRGBTexture = sRgbTexture;
            if (preserveInputAlpha)
            {
                changed |= importer.alphaSource != TextureImporterAlphaSource.FromInput || importer.alphaIsTransparency;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
            }

            if (changed) importer.SaveAndReimport();

            TextureImporter verifiedImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (verifiedImporter == null || verifiedImporter.textureType != textureType ||
                verifiedImporter.sRGBTexture != sRgbTexture ||
                (preserveInputAlpha && (verifiedImporter.alphaSource != TextureImporterAlphaSource.FromInput ||
                    verifiedImporter.alphaIsTransparency)))
            {
                throw new InvalidDataException("Unity did not retain the requested linear/alpha import settings for " +
                    assetPath + ".");
            }
        }

        private static Texture2D LoadTexture(string assetPath, string label)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                throw new InvalidDataException("Unity could not import the " + label + " map at " + assetPath + ".");
            }
            return texture;
        }

        private static string CopyInput(string sourcePath, string destinationFolder, string mapSuffix)
        {
            string extension = Path.GetExtension(sourcePath);
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            if (!String.IsNullOrEmpty(mapSuffix))
            {
                if (baseName.EndsWith(mapSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    baseName = baseName.Substring(0, baseName.Length - mapSuffix.Length);
                }
                baseName += mapSuffix;
            }
            string destinationAssetPath = destinationFolder + "/" + SanitizeName(baseName) + extension.ToLowerInvariant();
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string destinationAbsolutePath = Path.GetFullPath(Path.Combine(projectRoot,
                destinationAssetPath.Replace('/', Path.DirectorySeparatorChar)));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationAbsolutePath));
            File.Copy(Path.GetFullPath(sourcePath), destinationAbsolutePath, false);
            return destinationAssetPath;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            int slash = folderPath.LastIndexOf('/');
            if (slash <= 0) throw new InvalidDataException("Invalid preview asset folder: " + folderPath);
            string parent = folderPath.Substring(0, slash);
            string leaf = folderPath.Substring(slash + 1);
            EnsureAssetFolder(parent);
            if (!AssetDatabase.IsValidFolder(folderPath)) AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void BuildTemporaryBundle(string outputRoot, string prefabA, string prefabB, string runAssetRoot)
        {
            var assetPaths = new List<string> { prefabA, prefabB, ShaderAssetPath };
            string absoluteRunFolder = Path.GetFullPath(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                runAssetRoot.Replace('/', Path.DirectorySeparatorChar)));
            foreach (string absolutePath in Directory.GetFiles(absoluteRunFolder, "*", SearchOption.AllDirectories))
            {
                if (absolutePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string assetPath = absolutePath.Substring(projectRoot.Length + 1).Replace('\\', '/');
                string extension = Path.GetExtension(assetPath);
                if (String.Equals(extension, ".mat", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(extension, ".tga", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    assetPaths.Add(assetPath);
                }
            }

            var build = new AssetBundleBuild
            {
                assetBundleName = BundleFileName,
                assetNames = assetPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(outputRoot,
                new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression |
                BuildAssetBundleOptions.ForceRebuildAssetBundle,
                BuildTarget.StandaloneWindows64);
            if (manifest == null || manifest.GetAllAssetBundles().Length != 1 ||
                !String.Equals(manifest.GetAllAssetBundles()[0], BundleFileName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Unity did not write exactly one temporary preview bundle.");
            }

            string bundlePath = Path.Combine(outputRoot, BundleFileName);
            ValidateReopenedBundle(bundlePath, prefabA, prefabB);
        }

        private static void ValidateReopenedBundle(string bundlePath, string prefabAPath, string prefabBPath)
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null) throw new InvalidDataException("Unity could not reopen the preview bundle.");
            try
            {
                ValidateBundledPrefab(bundle, prefabAPath);
                ValidateBundledPrefab(bundle, prefabBPath);
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        private static void ValidateBundledPrefab(AssetBundle bundle, string prefabAssetPath)
        {
            string assetName = prefabAssetPath.ToLowerInvariant();
            GameObject prefab = bundle.LoadAsset<GameObject>(assetName);
            if (prefab == null)
            {
                string[] names = bundle.GetAllAssetNames();
                string path = names.FirstOrDefault(name =>
                    name.EndsWith("/" + Path.GetFileName(prefabAssetPath).ToLowerInvariant(),
                        StringComparison.OrdinalIgnoreCase));
                if (!String.IsNullOrEmpty(path)) prefab = bundle.LoadAsset<GameObject>(path);
            }
            if (prefab == null) throw new InvalidDataException("Bundle is missing preview prefab " + prefabAssetPath + ".");
            ValidateScriptFreePrefab(prefab, prefab.name, ShaderName);
        }

        private static void ValidateScriptFreePrefab(GameObject prefab, string label, string shaderName)
        {
            if (prefab == null) throw new InvalidDataException(label + " prefab was not loaded.");
            MonoBehaviour[] scripts = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            if (scripts.Length != 0)
            {
                throw new InvalidDataException(label + " prefab contains " + scripts.Length + " MonoBehaviour script(s).");
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidDataException(label + " prefab has no renderers.");
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    throw new InvalidDataException(label + " renderer " + renderers[i].name + " has no materials.");
                }
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null || material.shader == null ||
                        !String.Equals(material.shader.name, shaderName, StringComparison.Ordinal) ||
                        !material.HasProperty("_WearStrength") ||
                        Mathf.Abs(material.GetFloat("_WearStrength") - FactoryNewWearStrength) > 0.001f)
                    {
                        throw new InvalidDataException(label + " has a material without " + shaderName +
                            " at factory-new wear strength.");
                    }
                    if (!material.HasProperty("_BaseMap") || material.GetTexture("_BaseMap") == null ||
                        !material.HasProperty("_MetallicGlossMap") ||
                        material.GetTexture("_MetallicGlossMap") == null ||
                        !material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"))
                    {
                        throw new InvalidDataException(label + " has a material missing its albedo or MetallicGloss map binding.");
                    }
                    if (material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") != null &&
                        !material.IsKeywordEnabled("_NORMALMAP"))
                    {
                        throw new InvalidDataException(label + " has a normal map without the _NORMALMAP shader keyword.");
                    }
                    if (material.HasProperty("_EmissionMap") && material.GetTexture("_EmissionMap") != null &&
                        !material.IsKeywordEnabled("_EMISSION"))
                    {
                        throw new InvalidDataException(label + " has an emission map without the _EMISSION shader keyword.");
                    }
                }
            }
        }

        private static void ValidateTextureSet(
            string label,
            Texture2D albedo,
            Texture2D metallicGloss,
            Texture2D normal,
            Texture2D emission)
        {
            if (albedo == null || albedo.width != 1024 || albedo.height != 1024)
            {
                throw new InvalidDataException(label + " albedo must import at 1024x1024.");
            }

            Texture2D[] maps = { metallicGloss, normal, emission };
            string[] names = { "MetallicGloss", "Normal", "Emission" };
            for (int i = 0; i < maps.Length; i++)
            {
                if (maps[i] == null) continue;
                if (maps[i].width != albedo.width || maps[i].height != albedo.height)
                {
                    throw new InvalidDataException(label + " " + names[i] + " dimensions do not match the albedo atlas.");
                }
            }
        }

        private static void ValidateImportedMeshVertexColors(GameObject modelAsset, string label)
        {
            MeshFilter[] meshFilters = modelAsset.GetComponentsInChildren<MeshFilter>(true);
            SkinnedMeshRenderer[] skinnedRenderers = modelAsset.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var checkedMeshes = new HashSet<int>();
            int meshCount = 0;
            int vertexCount = 0;
            int[] minimum = { 255, 255, 255, 255 };
            int[] maximum = { 0, 0, 0, 0 };

            Action<Mesh> inspectMesh = mesh =>
            {
                if (mesh == null || !checkedMeshes.Add(mesh.GetInstanceID())) return;
                Color32[] colors = mesh.colors32;
                if (colors == null || colors.Length != mesh.vertexCount)
                {
                    throw new InvalidDataException(label + " imported mesh " + mesh.name +
                        " lost its RGBA vertex-color stream (" + (colors == null ? 0 : colors.Length) +
                        " colors for " + mesh.vertexCount + " vertices).");
                }

                meshCount++;
                vertexCount += mesh.vertexCount;
                for (int colorIndex = 0; colorIndex < colors.Length; colorIndex++)
                {
                    Color32 color = colors[colorIndex];
                    minimum[0] = Math.Min(minimum[0], color.r);
                    minimum[1] = Math.Min(minimum[1], color.g);
                    minimum[2] = Math.Min(minimum[2], color.b);
                    minimum[3] = Math.Min(minimum[3], color.a);
                    maximum[0] = Math.Max(maximum[0], color.r);
                    maximum[1] = Math.Max(maximum[1], color.g);
                    maximum[2] = Math.Max(maximum[2], color.b);
                    maximum[3] = Math.Max(maximum[3], color.a);
                }
            };

            for (int i = 0; i < meshFilters.Length; i++) inspectMesh(meshFilters[i].sharedMesh);
            for (int i = 0; i < skinnedRenderers.Length; i++) inspectMesh(skinnedRenderers[i].sharedMesh);
            if (meshCount == 0 || vertexCount == 0)
            {
                throw new InvalidDataException(label + " imported FBX has no readable mesh vertex-color data.");
            }

            Debug.Log("Verified imported RGBA vertex colors for " + label + ": " + meshCount +
                " meshes, " + vertexCount + " vertices; R=" + minimum[0] + ".." + maximum[0] +
                ", G=" + minimum[1] + ".." + maximum[1] + ", B=" + minimum[2] + ".." + maximum[2] +
                ", A=" + minimum[3] + ".." + maximum[3] + ".");
        }

        private static void LogTextureImporter(string assetPath, Texture2D texture)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null || texture == null)
            {
                throw new InvalidDataException("Unity could not inspect the imported texture at " + assetPath + ".");
            }

            Debug.Log("Verified imported texture " + assetPath + ": " + texture.width + "x" + texture.height +
                ", type=" + importer.textureType + ", sRGB=" + importer.sRGBTexture +
                ", alphaSource=" + importer.alphaSource + ", alphaIsTransparency=" + importer.alphaIsTransparency + ".");
        }

        private static string SanitizeName(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;
            return Regex.Replace(value.Trim(), "[^A-Za-z0-9_-]+", "_").Trim('_');
        }
    }
}
