using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RogueCommandExpansion.Authoring
{
    /// <summary>
    /// Imports the generated Level 2 mechanical modules as script-free visual
    /// prefabs, assigns the canonical PBR material sets, and builds their packs.
    /// </summary>
    public static class CanonicalVisualModuleBuilder
    {
        private const string ShaderName = "RogueCommand/VehiclePBR_VertexWear";
        private const string ModuleAssetRoot = "Assets/RogueCommandExpansionSample/VisualModules";
        private const string TextureFolder = ModuleAssetRoot + "/Textures";
        private const string MaterialFolder = ModuleAssetRoot + "/Materials";
        private const string ManifestRelativePath = "content/source-art/canonical-visual-modules.json";
        private const float FactoryNewWearStrength = 0.55f;
        private const int ExpectedTextureResolution = 2048;

        [Serializable]
        private sealed class ModuleManifest
        {
            public int schemaVersion;
            public ModuleEntry[] modules;
        }

        [Serializable]
        private sealed class ModuleEntry
        {
            public string moduleName;
            public string packName;
            public string unitId;
            public string variantId;
            public string assetBundleName;
            public string fbxAssetPath;
            public string prefabAssetPath;
            public string assetName;
            public string texturePrefix;
            public string preset;
        }

        [Serializable]
        private sealed class ContentPackReferences
        {
            public ContentAssetBundleReference[] assetBundles;
            public ContentUnitReference[] units;
            public ContentStageReference[] stages;
            public ContentAiProfileReference[] aiProfiles;
        }

        [Serializable]
        private sealed class ContentAssetBundleReference
        {
            public string id;
            public string path;
        }

        [Serializable]
        private sealed class ContentUnitReference
        {
            public string id;
            public string assetBundleId;
            public string prefabAssetName;
            public string balanceAssetName;
            public string cardImageAssetName;
            public string visualAssetName;
            public ContentVisualVariantReference[] visualVariants;
            public ContentProductionBuildingReference productionBuilding;
        }

        [Serializable]
        private sealed class ContentVisualVariantReference
        {
            public ContentVisualModuleReference[] modules;
        }

        [Serializable]
        private sealed class ContentVisualModuleReference
        {
            public string assetName;
        }

        [Serializable]
        private sealed class ContentProductionBuildingReference
        {
            public string id;
            public string assetBundleId;
            public string cardImageAssetName;
            public string visualAssetName;
        }

        [Serializable]
        private sealed class ContentStageReference
        {
            public string id;
            public string nativeTemplateId;
            public string assetBundleId;
            public string assetName;
        }

        [Serializable]
        private sealed class ContentAiProfileReference
        {
            public string id;
            public string nativeTemplateId;
            public string assetBundleId;
            public string prefabAssetName;
        }

        [MenuItem("Rogue Command/Asset Bundles/Build Canonical Two-Level Modules")]
        public static void BuildCanonicalModules()
        {
            try
            {
                BuildCanonicalModulesCore();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                    return;
                }
                throw;
            }
        }

        private static void BuildCanonicalModulesCore()
        {
            if (!String.Equals(Application.unityVersion, "2021.3.45f2", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Canonical module bundles require Unity 2021.3.45f2; current editor is " +
                    Application.unityVersion + ".");
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string repositoryRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", ".."));
            string manifestPath = Path.Combine(repositoryRoot, ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("Canonical visual module manifest is missing.", manifestPath);
            }

            ModuleManifest manifest = JsonUtility.FromJson<ModuleManifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.modules == null || manifest.modules.Length != 35)
            {
                throw new InvalidDataException("Canonical visual module manifest must contain exactly 35 module entries.");
            }

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException("Required self-contained module shader was not found: " + ShaderName);
            }

            EnsureAssetFolder(ModuleAssetRoot);
            EnsureAssetFolder(TextureFolder);
            EnsureAssetFolder(MaterialFolder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var seenPrefabPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var materialSets = new Dictionary<string, ModuleMaterials>(StringComparer.Ordinal);
            for (int i = 0; i < manifest.modules.Length; i++)
            {
                ModuleEntry entry = manifest.modules[i];
                ValidateEntry(entry, seenPrefabPaths);
                ModuleMaterials materials;
                if (!materialSets.TryGetValue(entry.preset, out materials))
                {
                    materials = BuildMaterials(entry, shader);
                    materialSets.Add(entry.preset, materials);
                }
                BuildPrefab(entry, materials);
                Debug.Log("[CanonicalVisualModuleBuilder] Built visual-only module " + (i + 1) + "/" +
                    manifest.modules.Length + ": " + entry.variantId + " at " + entry.prefabAssetPath + ".");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            int referencedAssets = 0;
            referencedAssets += AssignContentPackReferences("sample-native-expansion", repositoryRoot);
            referencedAssets += AssignContentPackReferences("tactical-operations", repositoryRoot);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateBundleAssignments(manifest.modules);
            RogueCommandAssetBundleBuilder.BuildContentPacksByName(
                "sample-native-expansion", "tactical-operations");
            ValidateBuiltBundleContents(manifest.modules, repositoryRoot);
            Debug.Log("[CanonicalVisualModuleBuilder] Built 35 visual-only modules, 28 shared PBR sets, labeled " +
                referencedAssets + " referenced pack assets, and built both content packs.");
        }

        private static int AssignContentPackReferences(string packName, string repositoryRoot)
        {
            string packPath = Path.Combine(repositoryRoot, "content", "packs", packName, "content.json");
            if (!File.Exists(packPath))
            {
                throw new FileNotFoundException("Content pack manifest is missing.", packPath);
            }

            ContentPackReferences pack = JsonUtility.FromJson<ContentPackReferences>(File.ReadAllText(packPath));
            if (pack == null || pack.assetBundles == null || pack.assetBundles.Length == 0)
            {
                throw new InvalidDataException("Content pack has no AssetBundle declarations: " + packName);
            }

            var bundleNamesById = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < pack.assetBundles.Length; i++)
            {
                ContentAssetBundleReference bundle = pack.assetBundles[i];
                if (bundle == null || String.IsNullOrWhiteSpace(bundle.id) || String.IsNullOrWhiteSpace(bundle.path))
                {
                    throw new InvalidDataException("Content pack contains an incomplete bundle declaration: " + packName);
                }
                bundleNamesById.Add(bundle.id, Path.GetFileName(bundle.path));
            }

            var assetPathsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
            for (int i = 0; i < allAssetPaths.Length; i++)
            {
                string assetPath = allAssetPaths[i];
                if (assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    assetPathsByName[assetPath] = assetPath;
                }
            }

            var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (pack.units != null)
            {
                for (int i = 0; i < pack.units.Length; i++)
                {
                    ContentUnitReference unit = pack.units[i];
                    if (unit == null) continue;
                    AddContentAssetReference(assignments, bundleNamesById, unit.assetBundleId,
                        unit.prefabAssetName, "unit:" + unit.id + ".prefabAssetName");
                    AddContentAssetReference(assignments, bundleNamesById, unit.assetBundleId,
                        unit.balanceAssetName, "unit:" + unit.id + ".balanceAssetName");
                    AddContentAssetReference(assignments, bundleNamesById, unit.assetBundleId,
                        unit.cardImageAssetName, "unit:" + unit.id + ".cardImageAssetName");
                    AddContentAssetReference(assignments, bundleNamesById, unit.assetBundleId,
                        unit.visualAssetName, "unit:" + unit.id + ".visualAssetName");

                    if (unit.visualVariants != null)
                    {
                        for (int variantIndex = 0; variantIndex < unit.visualVariants.Length; variantIndex++)
                        {
                            ContentVisualVariantReference variant = unit.visualVariants[variantIndex];
                            if (variant == null || variant.modules == null) continue;
                            for (int moduleIndex = 0; moduleIndex < variant.modules.Length; moduleIndex++)
                            {
                                ContentVisualModuleReference module = variant.modules[moduleIndex];
                                if (module != null)
                                {
                                    AddContentAssetReference(assignments, bundleNamesById, unit.assetBundleId,
                                        module.assetName, "unit:" + unit.id + ".visualVariants[" + variantIndex +
                                        "].modules[" + moduleIndex + "].assetName");
                                }
                            }
                        }
                    }

                    ContentProductionBuildingReference building = unit.productionBuilding;
                    if (building != null)
                    {
                        string buildingBundleId = String.IsNullOrWhiteSpace(building.assetBundleId)
                            ? unit.assetBundleId
                            : building.assetBundleId;
                        AddContentAssetReference(assignments, bundleNamesById, buildingBundleId,
                            building.cardImageAssetName, "productionBuilding:" + building.id + ".cardImageAssetName");
                        AddContentAssetReference(assignments, bundleNamesById, buildingBundleId,
                            building.visualAssetName, "productionBuilding:" + building.id + ".visualAssetName");
                    }
                }
            }

            if (pack.stages != null)
            {
                for (int i = 0; i < pack.stages.Length; i++)
                {
                    ContentStageReference stage = pack.stages[i];
                    if (stage != null && String.IsNullOrWhiteSpace(stage.nativeTemplateId))
                    {
                        AddContentAssetReference(assignments, bundleNamesById, stage.assetBundleId,
                            stage.assetName, "stage:" + stage.id + ".assetName");
                    }
                }
            }

            if (pack.aiProfiles != null)
            {
                for (int i = 0; i < pack.aiProfiles.Length; i++)
                {
                    ContentAiProfileReference profile = pack.aiProfiles[i];
                    if (profile != null && String.IsNullOrWhiteSpace(profile.nativeTemplateId))
                    {
                        AddContentAssetReference(assignments, bundleNamesById, profile.assetBundleId,
                            profile.prefabAssetName, "aiProfile:" + profile.id + ".prefabAssetName");
                    }
                }
            }

            foreach (KeyValuePair<string, string> assignment in assignments)
            {
                string assetPath;
                if (!assetPathsByName.TryGetValue(assignment.Key, out assetPath))
                {
                    throw new FileNotFoundException("Content pack reference has no imported Unity asset: " +
                        assignment.Key);
                }
                AssetImporter importer = AssetImporter.GetAtPath(assetPath);
                if (importer == null)
                {
                    throw new InvalidDataException("Content pack reference has no Unity AssetImporter: " + assetPath);
                }
                importer.SetAssetBundleNameAndVariant(assignment.Value, String.Empty);
            }

            Debug.Log("[CanonicalVisualModuleBuilder] Labeled " + assignments.Count +
                " referenced Unity assets for pack '" + packName + "'.");
            return assignments.Count;
        }

        private static void AddContentAssetReference(
            Dictionary<string, string> assignments,
            Dictionary<string, string> bundleNamesById,
            string bundleId,
            string assetName,
            string label)
        {
            if (String.IsNullOrWhiteSpace(assetName)) return;
            string bundleName;
            if (String.IsNullOrWhiteSpace(bundleId) || !bundleNamesById.TryGetValue(bundleId, out bundleName))
            {
                throw new InvalidDataException("Content asset " + label + " references an undeclared bundle ID '" +
                    bundleId + "'.");
            }

            if (!assetName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) ||
                assetName.IndexOf('\\') >= 0)
            {
                throw new InvalidDataException("Content asset " + label + " has an invalid Unity asset path: " + assetName);
            }
            string unityAssetPath = "Assets/" + assetName.Substring("assets/".Length);
            string existingBundle;
            if (assignments.TryGetValue(unityAssetPath, out existingBundle) &&
                !String.Equals(existingBundle, bundleName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Content asset " + label + " is assigned to multiple bundles.");
            }
            assignments[unityAssetPath] = bundleName;
        }

        private static void ValidateEntry(ModuleEntry entry, HashSet<string> seenPrefabPaths)
        {
            if (entry == null || String.IsNullOrWhiteSpace(entry.moduleName) ||
                String.IsNullOrWhiteSpace(entry.packName) || String.IsNullOrWhiteSpace(entry.variantId) ||
                String.IsNullOrWhiteSpace(entry.assetBundleName) || String.IsNullOrWhiteSpace(entry.fbxAssetPath) ||
                String.IsNullOrWhiteSpace(entry.prefabAssetPath) || String.IsNullOrWhiteSpace(entry.assetName) ||
                String.IsNullOrWhiteSpace(entry.texturePrefix) ||
                String.IsNullOrWhiteSpace(entry.preset))
            {
                throw new InvalidDataException("Canonical module manifest contains an incomplete entry.");
            }

            if (!seenPrefabPaths.Add(entry.prefabAssetPath) ||
                !entry.prefabAssetPath.StartsWith(ModuleAssetRoot + "/Prefabs/", StringComparison.Ordinal) ||
                !entry.fbxAssetPath.StartsWith(ModuleAssetRoot + "/Source/", StringComparison.Ordinal) ||
                !String.Equals(entry.assetName, entry.prefabAssetPath.ToLowerInvariant(), StringComparison.Ordinal))
            {
                throw new InvalidDataException("Canonical module asset path is duplicated or outside VisualModules: " +
                    entry.moduleName);
            }

            if (!File.Exists(Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                entry.fbxAssetPath.Replace('/', Path.DirectorySeparatorChar)))) ||
                !File.Exists(Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                TextureFolder.Replace('/', Path.DirectorySeparatorChar), entry.texturePrefix + "_Albedo.png"))) ||
                !File.Exists(Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                TextureFolder.Replace('/', Path.DirectorySeparatorChar), entry.texturePrefix + "_MetallicGloss.png"))) ||
                !File.Exists(Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                TextureFolder.Replace('/', Path.DirectorySeparatorChar), entry.texturePrefix + "_Normal.png"))) ||
                !File.Exists(Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                TextureFolder.Replace('/', Path.DirectorySeparatorChar), entry.texturePrefix + "_Emission.png"))))
            {
                throw new FileNotFoundException("Canonical module FBX or one of its four PBR maps is missing: " +
                    entry.moduleName);
            }
        }

        private static ModuleMaterials BuildMaterials(ModuleEntry entry, Shader shader)
        {
            string albedoPath = TextureFolder + "/" + entry.texturePrefix + "_Albedo.png";
            string metallicGlossPath = TextureFolder + "/" + entry.texturePrefix + "_MetallicGloss.png";
            string normalPath = TextureFolder + "/" + entry.texturePrefix + "_Normal.png";
            string emissionPath = TextureFolder + "/" + entry.texturePrefix + "_Emission.png";

            ConfigureTextureImporter(albedoPath, TextureImporterType.Default, true, false);
            ConfigureTextureImporter(metallicGlossPath, TextureImporterType.Default, false, true);
            ConfigureTextureImporter(normalPath, TextureImporterType.NormalMap, false, false);
            ConfigureTextureImporter(emissionPath, TextureImporterType.Default, true, false);

            Texture2D albedo = LoadTexture(albedoPath);
            Texture2D metallicGloss = LoadTexture(metallicGlossPath);
            Texture2D normal = LoadTexture(normalPath);
            Texture2D emission = LoadTexture(emissionPath);
            if (albedo.width != metallicGloss.width || albedo.height != metallicGloss.height ||
                albedo.width != normal.width || albedo.height != normal.height ||
                albedo.width != emission.width || albedo.height != emission.height ||
                albedo.width != ExpectedTextureResolution || albedo.height != ExpectedTextureResolution)
            {
                throw new InvalidDataException("Four-map dimensions must all be 2048x2048 for preset " + entry.preset + ".");
            }

            return new ModuleMaterials
            {
                primaryArmor = CreateMaterial(entry.preset, "PrimaryArmor", shader, albedo,
                    metallicGloss, normal, emission, Color.white),
                gunMetal = CreateMaterial(entry.preset, "GunMetal", shader, albedo,
                    metallicGloss, normal, emission, new Color(0.38f, 0.43f, 0.50f, 1.0f)),
                accent = CreateMaterial(entry.preset, "Accent", shader, albedo,
                    metallicGloss, normal, emission, new Color(0.82f, 0.78f, 0.62f, 1.0f))
            };
        }

        private static Texture2D LoadTexture(string assetPath)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                throw new InvalidDataException("Unity could not import canonical PBR map " + assetPath + ".");
            }
            return texture;
        }

        private static void ConfigureTextureImporter(
            string assetPath,
            TextureImporterType type,
            bool sRgb,
            bool metallicGloss)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidDataException("Texture importer is missing for " + assetPath + ".");
            }

            bool changed = importer.textureType != type || importer.sRGBTexture != sRgb ||
                importer.wrapMode != TextureWrapMode.Repeat || importer.filterMode != FilterMode.Trilinear ||
                importer.anisoLevel != 8;
            importer.textureType = type;
            importer.sRGBTexture = sRgb;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            if (metallicGloss)
            {
                changed |= importer.alphaSource != TextureImporterAlphaSource.FromInput || importer.alphaIsTransparency;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }

            importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null || importer.textureType != type || importer.sRGBTexture != sRgb ||
                importer.wrapMode != TextureWrapMode.Repeat || importer.filterMode != FilterMode.Trilinear ||
                importer.anisoLevel != 8 || (metallicGloss &&
                    (importer.alphaSource != TextureImporterAlphaSource.FromInput || importer.alphaIsTransparency)))
            {
                throw new InvalidDataException("Unity texture importer settings do not match the four-map PBR contract: " + assetPath);
            }
        }

        private static Material CreateMaterial(
            string preset,
            string role,
            Shader shader,
            Texture2D albedo,
            Texture2D metallicGloss,
            Texture2D normal,
            Texture2D emission,
            Color baseColor)
        {
            string materialName = "RCLModule_" + preset + "__" + role;
            string materialPath = MaterialFolder + "/" + materialName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader);
                material.name = materialName;
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.enableInstancing = true;
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", baseColor);
            material.SetTexture("_MetallicGlossMap", metallicGloss);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_GlossMapScale", 1.0f);
            material.SetFloat("_Metallic", 1.0f);
            material.SetFloat("_Smoothness", 1.0f);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 0.8f);
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_EmissionMap", emission);
            material.SetColor("_EmissionColor", Color.white);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            material.SetFloat("_WearStrength", FactoryNewWearStrength);
            material.SetFloat("_AoStrength", 1.0f);
            material.SetFloat("_SootStrength", 1.0f);
            material.SetColor("_TeamColor", Color.white);
            material.SetFloat("_MudAmount", 0.0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildPrefab(ModuleEntry entry, ModuleMaterials materials)
        {
            ModelImporter modelImporter = AssetImporter.GetAtPath(entry.fbxAssetPath) as ModelImporter;
            if (modelImporter == null)
            {
                throw new InvalidDataException("Unity did not create a ModelImporter for module FBX " + entry.fbxAssetPath);
            }

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(entry.fbxAssetPath);
            if (modelAsset == null)
            {
                throw new InvalidDataException("Unity did not import module FBX as a GameObject: " + entry.fbxAssetPath);
            }

            EnsureAssetFolder(Path.GetDirectoryName(entry.prefabAssetPath).Replace('\\', '/'));
            GameObject wrapper = new GameObject(entry.moduleName);
            GameObject modelInstance = null;
            try
            {
                modelInstance = UnityEngine.Object.Instantiate(modelAsset);
                modelInstance.name = entry.moduleName + "_Geometry";
                modelInstance.transform.SetParent(wrapper.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;
                StripNonVisualComponents(wrapper);
                Renderer[] renderers = wrapper.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    throw new InvalidDataException(entry.moduleName + " contains no imported renderers.");
                }
                AssignMaterials(renderers, materials, entry.moduleName);
                ValidateVisualOnlyPrefab(wrapper, entry.moduleName);

                bool saved;
                PrefabUtility.SaveAsPrefabAsset(wrapper, entry.prefabAssetPath, out saved);
                if (!saved)
                {
                    throw new IOException("Unity did not save visual-only module prefab " + entry.prefabAssetPath + ".");
                }

                AssetImporter prefabImporter = AssetImporter.GetAtPath(entry.prefabAssetPath);
                if (prefabImporter == null)
                {
                    throw new InvalidDataException("Saved module prefab has no asset importer: " + entry.prefabAssetPath);
                }
                prefabImporter.SetAssetBundleNameAndVariant(entry.assetBundleName, String.Empty);
            }
            finally
            {
                if (wrapper != null)
                {
                    UnityEngine.Object.DestroyImmediate(wrapper);
                }
            }
        }

        private static void StripNonVisualComponents(GameObject root)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component is Transform || component is MeshFilter ||
                    component is MeshRenderer || component is SkinnedMeshRenderer)
                {
                    continue;
                }
                UnityEngine.Object.DestroyImmediate(component, true);
            }
        }

        private static void AssignMaterials(Renderer[] renderers, ModuleMaterials materials, string label)
        {
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Material[] imported = renderer.sharedMaterials;
                if (imported == null || imported.Length == 0)
                {
                    imported = new Material[1];
                }
                var assigned = new Material[imported.Length];
                for (int materialIndex = 0; materialIndex < imported.Length; materialIndex++)
                {
                    string sourceName = imported[materialIndex] == null ? String.Empty : imported[materialIndex].name;
                    assigned[materialIndex] = sourceName.IndexOf("GunMetal", StringComparison.OrdinalIgnoreCase) >= 0
                        ? materials.gunMetal
                        : sourceName.IndexOf("Accent", StringComparison.OrdinalIgnoreCase) >= 0
                            ? materials.accent
                            : materials.primaryArmor;
                }
                renderer.sharedMaterials = assigned;
            }
        }

        private static void ValidateVisualOnlyPrefab(GameObject root, string label)
        {
            if (root.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 ||
                root.GetComponentsInChildren<Collider>(true).Length != 0 ||
                root.GetComponentsInChildren<Joint>(true).Length != 0)
            {
                throw new InvalidDataException(label + " module prefab contains non-visual or gameplay components.");
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    throw new InvalidDataException(label + " renderer has no shared materials.");
                }
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null || material.shader == null || material.shader.name != ShaderName ||
                        material.GetTexture("_BaseMap") == null || material.GetTexture("_MetallicGlossMap") == null ||
                        !material.IsKeywordEnabled("_METALLICSPECGLOSSMAP") ||
                        material.GetTexture("_BumpMap") == null || !material.IsKeywordEnabled("_NORMALMAP") ||
                        material.GetTexture("_EmissionMap") == null || !material.IsKeywordEnabled("_EMISSION") ||
                        !material.enableInstancing)
                    {
                        throw new InvalidDataException(label + " has a material that does not satisfy the four-map VehiclePBR contract.");
                    }
                }

                MeshFilter filter = renderers[i].GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh :
                    (renderers[i] as SkinnedMeshRenderer != null ? ((SkinnedMeshRenderer)renderers[i]).sharedMesh : null);
                if (mesh == null || mesh.vertexCount == 0 || mesh.colors32 == null ||
                    mesh.colors32.Length != mesh.vertexCount)
                {
                    throw new InvalidDataException(label + " mesh lost its RGBA vertex wear attribute during FBX import.");
                }
            }
        }

        private static void ValidateBuiltBundleContents(ModuleEntry[] entries, string repositoryRoot)
        {
            var bundleModules = new Dictionary<string, List<ModuleEntry>>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                List<ModuleEntry> modules;
                if (!bundleModules.TryGetValue(entries[i].packName, out modules))
                {
                    modules = new List<ModuleEntry>();
                    bundleModules.Add(entries[i].packName, modules);
                }
                modules.Add(entries[i]);
            }

            foreach (KeyValuePair<string, List<ModuleEntry>> pair in bundleModules)
            {
                string bundleName = pair.Value[0].assetBundleName;
                string bundlePath = Path.Combine(repositoryRoot, "artifacts", "canonical-two-level",
                    "content-packs", pair.Key, "bundles", bundleName);
                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    throw new InvalidDataException("Could not reopen canonical module AssetBundle " + bundlePath);
                }
                try
                {
                    var assetNames = new HashSet<string>(bundle.GetAllAssetNames(), StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        if (!assetNames.Contains(pair.Value[i].assetName))
                        {
                            throw new InvalidDataException("Built AssetBundle is missing module prefab " +
                                pair.Value[i].assetName + ".");
                        }
                    }
                }
                finally
                {
                    bundle.Unload(false);
                }
            }
        }

        private static void ValidateBundleAssignments(ModuleEntry[] entries)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                AssetImporter importer = AssetImporter.GetAtPath(entries[i].prefabAssetPath);
                if (importer == null || importer.assetBundleName != entries[i].assetBundleName ||
                    !String.IsNullOrEmpty(importer.assetBundleVariant))
                {
                    throw new InvalidDataException("Prefab bundle assignment does not match the canonical manifest: " +
                        entries[i].prefabAssetPath);
                }
            }
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string folderName = Path.GetFileName(assetPath);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureAssetFolder(parent);
            }
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private sealed class ModuleMaterials
        {
            public Material primaryArmor;
            public Material gunMetal;
            public Material accent;
        }
    }
}
