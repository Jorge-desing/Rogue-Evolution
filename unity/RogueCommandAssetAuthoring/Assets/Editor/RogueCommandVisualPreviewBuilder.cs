using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

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
        private const string VisualOnlyRoot = "Assets/RogueCommandExpansionSample/VisualOnly";

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

        [MenuItem("Rogue Command/Visual Preview/Build Minefield Sower + Longshot Tankette")]
        public static void BuildMinefieldSowerAndLongshotTankettePreview()
        {
            BuildSampleVisualPreview("MinefieldSower", "LongshotTankette");
        }

        [MenuItem("Rogue Command/Visual Preview/Build Tidebreaker Hovercraft + Deep Space Relay")]
        public static void BuildTidebreakerAndDeepSpaceRelayPreview()
        {
            BuildSampleVisualPreview("TidebreakerHovercraft", "DeepSpaceRelay");
        }

        [MenuItem("Rogue Command/Visual Preview/Bind Tidebreaker + Deep Space PBR Prefabs")]
        public static void BuildTidebreakerAndDeepSpaceRelayPbrPrefabs()
        {
            try
            {
                ConfigureSampleSourceTextureImports("TidebreakerHovercraft", "DeepSpaceRelay");
                BuildVisualOnlyPbrPrefab("TidebreakerHovercraft");
                BuildVisualOnlyPbrPrefab("DeepSpaceRelay");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("Created persistent, script-free PBR visual prefabs for Tidebreaker Hovercraft and " +
                    "Deep Space Relay under " + VisualOnlyRoot + ". No gameplay or pack registration was changed.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        [MenuItem("Rogue Command/Visual Preview/Render Tidebreaker + Deep Space at RTS Scale")]
        public static void RenderTidebreakerAndDeepSpaceAtRtsScale()
        {
            RenderVisualPrefabsAtRtsScale(
                new[] { "TidebreakerHovercraft", "DeepSpaceRelay" },
                new[] { 30, 50, 120, 300 });
        }

        [MenuItem("Rogue Command/Visual Preview/Render Arachnid + Overlord Models at RTS Scale")]
        public static void RenderArachnidAndOverlordAtRtsScale()
        {
            RenderVisualPrefabsAtRtsScale(
                new[]
                {
                    "ArachnidHexapod",
                    "OverlordCommandHQ",
                    "ArachnidHexapodFactory",
                    "OverlordCommandHQFactory"
                },
                new[] { 50, 120, 300 });
        }

        private static void RenderVisualPrefabsAtRtsScale(string[] modelNames, int[] targetScreenHeights)
        {
            RenderPipelineAsset originalDefaultPipeline = GraphicsSettings.defaultRenderPipeline;
            RenderPipelineAsset originalQualityPipeline = QualitySettings.renderPipeline;
            bool originalColorTemperature = GraphicsSettings.lightsUseColorTemperature;
            int exitCode = 0;
            try
            {
                if (!Application.isBatchMode)
                {
                    throw new InvalidOperationException(
                        "RTS scale renders run in a dedicated Unity batch process so the open authoring scene is untouched.");
                }

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string pairName = String.Join("-", modelNames.Select(name => name.ToLowerInvariant()));
                string outputRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", "..", "artifacts",
                    "visual-previews", "rts-scale-" + pairName + "-" + runId));
                Directory.CreateDirectory(outputRoot);

                // URP's editor preview path currently throws while creating its volume stack because the
                // project has duplicate Bloom registrations. Use a temporary Standard-shader adapter here;
                // persistent prefabs and materials remain on the project's URP shader.
                GraphicsSettings.defaultRenderPipeline = null;
                QualitySettings.renderPipeline = null;

                for (int i = 0; i < targetScreenHeights.Length; i++)
                {
                    for (int modelIndex = 0; modelIndex < modelNames.Length; modelIndex++)
                    {
                        RenderVisualPrefabAtScreenHeight(modelNames[modelIndex], 512,
                            targetScreenHeights[i], outputRoot);
                    }
                }

                Debug.Log("Rendered " + String.Join(", ", modelNames) + " at " +
                    String.Join(", ", targetScreenHeights.Select(height => height + "px")) +
                    " RTS scales under " + outputRoot + ".");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                exitCode = 1;
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = originalDefaultPipeline;
                QualitySettings.renderPipeline = originalQualityPipeline;
                GraphicsSettings.lightsUseColorTemperature = originalColorTemperature;
            }
            if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
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
                fbxPath = ToAbsolutePath(projectRoot, ResolveSampleModelAssetPath(modelName)),
                albedoPath = ToAbsolutePath(projectRoot, textureStem + "_Albedo.png"),
                metallicGlossPath = ToAbsolutePath(projectRoot, textureStem + "_MetallicGloss.png"),
                normalPath = ToAbsolutePath(projectRoot, textureStem + "_Normal.png"),
                emissionPath = ToAbsolutePath(projectRoot, textureStem + "_Emission.png")
            };
        }

        private static string ResolveSampleModelAssetPath(string modelName)
        {
            string[] candidates =
            {
                SampleModelRoot + "/" + modelName + "_LODs.fbx",
                SampleModelRoot + "/" + modelName + "_Atlas.fbx",
                SampleModelRoot + "/" + modelName + ".fbx"
            };
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(ToAbsolutePath(projectRoot, candidates[i]))) return candidates[i];
            }
            throw new FileNotFoundException("No sample model FBX was found for " + modelName + ".");
        }

        private static void BuildVisualOnlyPbrPrefab(string modelName)
        {
            string modelAssetPath = ResolveSampleModelAssetPath(modelName);
            if (!modelAssetPath.EndsWith("_Atlas.fbx", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(modelName + " must use its unified _Atlas.fbx for atlas texturing.");
            }

            string albedoPath = SampleTextureRoot + "/" + modelName + "_Albedo.png";
            string metallicGlossPath = SampleTextureRoot + "/" + modelName + "_MetallicGloss.png";
            string normalPath = SampleTextureRoot + "/" + modelName + "_Normal.png";
            string emissionPath = SampleTextureRoot + "/" + modelName + "_Emission.png";
            Texture2D albedo = LoadTexture(albedoPath, modelName + " albedo");
            Texture2D metallicGloss = LoadTexture(metallicGlossPath, modelName + " MetallicGloss");
            Texture2D normal = LoadTexture(normalPath, modelName + " normal");
            Texture2D emission = LoadTexture(emissionPath, modelName + " emission");
            ValidateTextureSet(modelName, albedo, metallicGloss, normal, emission);
            LogTextureImporter(albedoPath, albedo);
            LogTextureImporter(metallicGlossPath, metallicGloss);
            LogTextureImporter(normalPath, normal);
            LogTextureImporter(emissionPath, emission);

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelAssetPath);
            if (modelAsset == null)
            {
                throw new InvalidDataException("Unity did not import " + modelAssetPath + ".");
            }
            ValidateImportedMeshVertexColors(modelAsset, modelName);

            EnsureAssetFolder(VisualOnlyRoot + "/Materials");
            EnsureAssetFolder(VisualOnlyRoot + "/Prefabs");
            string materialPath = VisualOnlyRoot + "/Materials/" + modelName + "_Atlas.mat";
            Material atlasMaterial = CreateOrUpdateVisualOnlyMaterial(materialPath, modelName + "_Atlas",
                albedo, metallicGloss, normal, emission);

            string prefabPath = VisualOnlyRoot + "/Prefabs/" + modelName + "Visual.prefab";
            GameObject root = new GameObject(modelName + "Visual");
            try
            {
                GameObject model = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null) model = Instantiate(modelAsset);
                model.name = Path.GetFileNameWithoutExtension(modelAssetPath);
                model.transform.SetParent(root.transform, false);

                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    throw new InvalidDataException(modelName + " atlas FBX has no renderers.");
                }
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    Material[] previousMaterials = renderers[rendererIndex].sharedMaterials;
                    int slotCount = previousMaterials == null || previousMaterials.Length == 0
                        ? 1
                        : previousMaterials.Length;
                    var assignedMaterials = new Material[slotCount];
                    for (int slot = 0; slot < slotCount; slot++) assignedMaterials[slot] = atlasMaterial;
                    renderers[rendererIndex].sharedMaterials = assignedMaterials;
                }

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out success);
                if (!success) throw new IOException("Unity could not save " + prefabPath + ".");
            }
            finally
            {
                DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            ValidateScriptFreePrefab(savedPrefab, modelName, ShaderName);
            ValidateVisualOnlyPrefabTextureBindings(savedPrefab, albedo, metallicGloss, normal, emission,
                modelName);
            Debug.Log("Bound all four canonical PBR maps to the unified atlas prefab " + prefabPath +
                " using " + modelAssetPath + ".");
        }

        private static Material CreateOrUpdateVisualOnlyMaterial(
            string materialPath,
            string materialName,
            Texture2D albedo,
            Texture2D metallicGloss,
            Texture2D normal,
            Texture2D emission)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException("Shader.Find failed for " + ShaderName + ".");
            Material updated = CreatePreviewMaterial(shader, materialName, albedo, metallicGloss, normal, emission);
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(updated, materialPath);
                return updated;
            }

            EditorUtility.CopySerialized(updated, existing);
            DestroyImmediate(updated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void ValidateVisualOnlyPrefabTextureBindings(
            GameObject prefab,
            Texture2D albedo,
            Texture2D metallicGloss,
            Texture2D normal,
            Texture2D emission,
            string label)
        {
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            int materialCount = 0;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Material[] materials = renderers[rendererIndex].sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    if (material == null || material.GetTexture("_BaseMap") != albedo ||
                        material.GetTexture("_MetallicGlossMap") != metallicGloss ||
                        material.GetTexture("_BumpMap") != normal ||
                        material.GetTexture("_EmissionMap") != emission)
                    {
                        throw new InvalidDataException(label + " prefab has a renderer/material slot that does not " +
                            "reference the four canonical PBR maps.");
                    }
                    materialCount++;
                }
            }
            if (materialCount == 0)
            {
                throw new InvalidDataException(label + " prefab contains no material bindings.");
            }
            Debug.Log("Verified " + label + " prefab material bindings: " + renderers.Length +
                " renderers, " + materialCount + " slots; canonical Albedo, MetallicGloss, Normal and Emission.");
        }

        private static void RenderVisualPrefabAtScreenHeight(
            string modelName,
            int renderSize,
            int targetScreenHeight,
            string outputRoot)
        {
            string prefabPath = VisualOnlyRoot + "/Prefabs/" + modelName + "Visual.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new FileNotFoundException("Missing visual-only prefab " + prefabPath + ".");

            Scene previousScene = SceneManager.GetActiveScene();
            Scene previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderTexture renderTexture = null;
            Texture2D screenshot = null;
            var temporaryMaterials = new List<Material>();
            try
            {
                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, previewScene) as GameObject;
                if (instance == null)
                {
                    instance = Instantiate(prefab);
                    SceneManager.MoveGameObjectToScene(instance, previewScene);
                }

                SceneManager.SetActiveScene(previewScene);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.65f, 0.68f, 0.72f, 1.0f);

                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidDataException(modelName + " prefab has no renderers.");
                ConfigureBuiltInPreviewMaterials(renderers, temporaryMaterials);
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                float modelHeight = Mathf.Max(bounds.size.y, 0.01f);

                GameObject lightObject = new GameObject(modelName + "_PreviewKeyLight");
                SceneManager.MoveGameObjectToScene(lightObject, previewScene);
                Light keyLight = lightObject.AddComponent<Light>();
                keyLight.type = LightType.Directional;
                keyLight.intensity = 1.35f;
                keyLight.color = Color.white;
                keyLight.shadows = LightShadows.Soft;
                lightObject.transform.rotation = Quaternion.Euler(42.0f, -32.0f, 0.0f);

                GameObject cameraObject = new GameObject(modelName + "_PreviewCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.22f, 0.26f, 0.30f, 1.0f);
                camera.orthographic = true;
                camera.aspect = 1.0f;
                camera.allowHDR = true;
                camera.useOcclusionCulling = false;
                camera.nearClipPlane = 0.01f;
                Vector3 viewDirection = new Vector3(1.0f, 0.78f, 1.0f).normalized;
                float cameraDistance = Mathf.Max(modelHeight * 3.0f, 2.0f);
                camera.farClipPlane = cameraDistance + modelHeight * 4.0f;
                camera.transform.position = bounds.center + viewDirection * cameraDistance;
                camera.transform.LookAt(bounds.center, Vector3.up);
                Vector3 extents = bounds.extents;
                Vector3 cameraUp = camera.transform.up;
                float projectedHeight =
                    Mathf.Abs(Vector3.Dot(cameraUp, Vector3.right)) * extents.x +
                    Mathf.Abs(Vector3.Dot(cameraUp, Vector3.up)) * extents.y +
                    Mathf.Abs(Vector3.Dot(cameraUp, Vector3.forward)) * extents.z;
                camera.orthographicSize = Mathf.Max(projectedHeight, 0.01f) * renderSize /
                    targetScreenHeight;
                float initialPixelHeight = MeasureProjectedMeshHeight(camera, renderers, renderSize);
                if (initialPixelHeight <= 0.0f)
                {
                    throw new InvalidDataException(modelName + " has no measurable projected mesh geometry.");
                }
                camera.orthographicSize *= initialPixelHeight / targetScreenHeight;
                float fittedPixelHeight = MeasureProjectedMeshHeight(camera, renderers, renderSize);
                if (Mathf.Abs(fittedPixelHeight - targetScreenHeight) > 0.75f)
                {
                    throw new InvalidDataException(modelName + " RTS scale fit missed target " +
                        targetScreenHeight + "px (measured " + fittedPixelHeight + "px).");
                }

                renderTexture = new RenderTexture(renderSize, renderSize, 24,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                renderTexture.name = modelName + "_RTSPreview_" + targetScreenHeight + "px";
                renderTexture.antiAliasing = 1;
                renderTexture.Create();
                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture.active = renderTexture;
                screenshot = new Texture2D(renderSize, renderSize, TextureFormat.RGBA32, false, false);
                screenshot.ReadPixels(new Rect(0, 0, renderSize, renderSize), 0, 0, false);
                screenshot.Apply(false, false);

                string outputPath = Path.Combine(outputRoot,
                    modelName + "_" + targetScreenHeight + "px.png");
                File.WriteAllBytes(outputPath, screenshot.EncodeToPNG());
                Debug.Log("Rendered " + modelName + " at target " + targetScreenHeight + "px model height: " +
                    outputPath + "; worldBounds=" + bounds.size + ", projectedHeight=" + projectedHeight +
                    ", measuredPixelHeight=" + fittedPixelHeight + "px, orthoSize=" +
                    camera.orthographicSize + ".");
            }
            finally
            {
                RenderTexture.active = null;
                if (screenshot != null) DestroyImmediate(screenshot);
                for (int i = temporaryMaterials.Count - 1; i >= 0; i--)
                {
                    if (temporaryMaterials[i] != null) DestroyImmediate(temporaryMaterials[i]);
                }
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    DestroyImmediate(renderTexture);
                }
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            }
        }

        private static float MeasureProjectedMeshHeight(Camera camera, Renderer[] renderers, int renderSize)
        {
            float minimumY = float.PositiveInfinity;
            float maximumY = float.NegativeInfinity;
            bool foundVertex = false;

            foreach (Renderer renderer in renderers)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh != null)
                {
                    Vector3[] vertices = mesh.vertices;
                    for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                    {
                        float screenY = camera.WorldToViewportPoint(
                            renderer.transform.TransformPoint(vertices[vertexIndex])).y;
                        minimumY = Mathf.Min(minimumY, screenY);
                        maximumY = Mathf.Max(maximumY, screenY);
                        foundVertex = true;
                    }
                    continue;
                }

                Bounds rendererBounds = renderer.bounds;
                Vector3 extents = rendererBounds.extents;
                Vector3 center = rendererBounds.center;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = center + new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);
                    float screenY = camera.WorldToViewportPoint(point).y;
                    minimumY = Mathf.Min(minimumY, screenY);
                    maximumY = Mathf.Max(maximumY, screenY);
                    foundVertex = true;
                }
            }

            if (!foundVertex) return 0.0f;
            return (maximumY - minimumY) * renderSize;
        }

        private static void ConfigureBuiltInPreviewMaterials(Renderer[] renderers, List<Material> ownedMaterials)
        {
            Shader standard = Shader.Find("Standard");
            if (standard == null) throw new InvalidOperationException("Unity Standard shader is unavailable.");

            var replacements = new Dictionary<int, Material>();
            foreach (Renderer renderer in renderers)
            {
                Material[] sourceMaterials = renderer.sharedMaterials;
                var previewMaterials = new Material[sourceMaterials.Length];
                for (int slot = 0; slot < sourceMaterials.Length; slot++)
                {
                    Material source = sourceMaterials[slot];
                    if (source == null) continue;

                    int sourceId = source.GetInstanceID();
                    Material preview;
                    if (!replacements.TryGetValue(sourceId, out preview))
                    {
                        preview = new Material(standard);
                        preview.name = source.name + "_RTSPreview";
                        if (source.HasProperty("_BaseMap"))
                        {
                            preview.SetTexture("_MainTex", source.GetTexture("_BaseMap"));
                            preview.SetTextureScale("_MainTex", source.GetTextureScale("_BaseMap"));
                            preview.SetTextureOffset("_MainTex", source.GetTextureOffset("_BaseMap"));
                        }
                        if (source.HasProperty("_BaseColor")) preview.SetColor("_Color", source.GetColor("_BaseColor"));
                        if (source.HasProperty("_MetallicGlossMap"))
                        {
                            preview.SetTexture("_MetallicGlossMap", source.GetTexture("_MetallicGlossMap"));
                            // A low-metal inspection response keeps the baked albedo readable without an
                            // environment cubemap; the source map still drives per-pixel metalness.
                            preview.SetFloat("_Metallic", 0.18f);
                            preview.SetFloat("_GlossMapScale", source.GetFloat("_GlossMapScale") * 0.35f);
                            preview.EnableKeyword("_METALLICGLOSSMAP");
                        }
                        if (source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null)
                        {
                            preview.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
                            preview.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
                            preview.EnableKeyword("_NORMALMAP");
                        }
                        if (source.HasProperty("_EmissionMap") && source.GetTexture("_EmissionMap") != null)
                        {
                            preview.SetTexture("_EmissionMap", source.GetTexture("_EmissionMap"));
                            preview.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
                            preview.EnableKeyword("_EMISSION");
                        }

                        replacements.Add(sourceId, preview);
                        ownedMaterials.Add(preview);
                    }
                    previewMaterials[slot] = preview;
                }
                renderer.sharedMaterials = previewMaterials;
            }
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
            if (albedo == null || albedo.width <= 0 || albedo.height <= 0 || albedo.width != albedo.height)
            {
                throw new InvalidDataException(label + " albedo must import as a non-empty square atlas.");
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
