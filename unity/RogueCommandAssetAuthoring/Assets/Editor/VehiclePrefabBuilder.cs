using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace RogueCommandExpansion.Authoring
{
    /// <summary>
    /// Builds game-ready Unity prefabs from Rogue Command 3D models and .gameplay.json metadata.
    /// Configures MeshFilter, BoxCollider, LODGroup, Sockets, and PBR Atlas materials with GPU instancing.
    /// Supports execution in Unity batchmode (-executeMethod RogueCommandExpansion.Authoring.VehiclePrefabBuilder.BuildPrefabFromCommandLine)
    /// and interactive Editor menus.
    /// </summary>
    public static class VehiclePrefabBuilder
    {
        private const string AssetRoot = "Assets/RogueCommandExpansionSample";
        private const string PrefabFolder = AssetRoot + "/Prefabs";
        private const string ModelFolder = AssetRoot + "/Models";
        private const string TextureFolder = ModelFolder + "/Textures";
        private const string MaterialFolder = AssetRoot + "/Materials";

        public class SocketData
        {
            public string Name;
            public Vector3 Position;
            public Vector3 Forward;
        }

        public class GameplayMetadata
        {
            public string UnitName;
            public string Archetype;
            public string VisualRootPath;
            public Vector3 ColliderCenter = new Vector3(0f, 0.8f, 0f);
            public Vector3 ColliderSize = new Vector3(2f, 1.5f, 3f);
            public bool HasCollider = false;
            public List<SocketData> Sockets = new List<SocketData>();
        }

        [MenuItem("Rogue Command/Prefabs/Build All Discovered Gameplay Prefabs")]
        public static void BuildAllDiscoveredPrefabs()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string repoRoot = Directory.GetParent(projectRoot).FullName;

            // Search in artifacts/ and unity/ for *.gameplay.json files
            var foundFiles = new List<string>();
            string artifactsDir = Path.Combine(repoRoot, "artifacts");
            if (Directory.Exists(artifactsDir))
            {
                foundFiles.AddRange(Directory.GetFiles(artifactsDir, "*.gameplay.json", SearchOption.AllDirectories));
            }

            string assetsDir = Application.dataPath;
            foundFiles.AddRange(Directory.GetFiles(assetsDir, "*.gameplay.json", SearchOption.AllDirectories));

            if (foundFiles.Count == 0)
            {
                Debug.LogWarning("[VehiclePrefabBuilder] No *.gameplay.json files found to build prefabs from.");
                return;
            }

            int builtCount = 0;
            foreach (string file in foundFiles)
            {
                try
                {
                    BuildPrefabFromGameplayJson(file);
                    builtCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogError("[VehiclePrefabBuilder] Failed building prefab for " + file + ": " + ex.Message);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[VehiclePrefabBuilder] Successfully built " + builtCount + " prefab(s).");
        }

        /// <summary>
        /// Command line entrypoint for headless batchmode building.
        /// Example:
        ///   Unity.exe -batchmode -quit -projectPath &lt;path&gt; -executeMethod RogueCommandExpansion.Authoring.VehiclePrefabBuilder.BuildPrefabFromCommandLine -gameplayJson &lt;jsonPath&gt;
        /// </summary>
        public static void BuildPrefabFromCommandLine()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string jsonPath = null;

                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "-gameplayJson", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        jsonPath = args[i + 1];
                        break;
                    }
                }

                if (string.IsNullOrEmpty(jsonPath))
                {
                    throw new ArgumentException("Missing -gameplayJson <path> command line argument.");
                }

                if (!File.Exists(jsonPath))
                {
                    throw new FileNotFoundException("Specified gameplay JSON file does not exist: " + jsonPath);
                }

                BuildPrefabFromGameplayJson(jsonPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[VehiclePrefabBuilder] Command line build completed successfully for: " + jsonPath);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        public static string BuildPrefabFromGameplayJson(string jsonFilePath)
        {
            GameplayMetadata meta = ParseGameplayJson(jsonFilePath);
            string unitName = meta.UnitName;
            if (string.IsNullOrEmpty(unitName))
            {
                unitName = Path.GetFileNameWithoutExtension(jsonFilePath).Replace(".gameplay", "");
                meta.UnitName = unitName;
            }

            Debug.Log("[VehiclePrefabBuilder] Building prefab for unit: " + unitName + " (Archetype: " + meta.Archetype + ")");

            // Ensure directory structures exist
            EnsureDirectories();

            // Setup or import Atlas textures if present
            Material atlasMaterial = SetupAtlasTexturesAndMaterial(unitName);

            // Locate 3D Model Asset (Prioritize Atlas FBX, then LODs FBX, then base FBX)
            GameObject modelAsset = FindModelAsset(unitName);
            if (modelAsset == null)
            {
                throw new FileNotFoundException("Could not locate authored FBX model for unit: " + unitName);
            }

            // Create temporary hierarchy in scene
            string rootName = !string.IsNullOrEmpty(meta.VisualRootPath) ? meta.VisualRootPath : (unitName + "Visual");
            var root = new GameObject(rootName);

            try
            {
                GameObject modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (modelInstance == null)
                {
                    throw new InvalidOperationException("Failed to instantiate model asset for: " + unitName);
                }

                modelInstance.name = unitName + "Model";
                modelInstance.transform.SetParent(root.transform, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;

                // Assign Atlas material if available, or configure instancing on existing materials
                if (atlasMaterial != null)
                {
                    Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in renderers)
                    {
                        // Check if renderer is a shield bubble or vfx
                        if (r.name.EndsWith("_ShieldBubble", StringComparison.OrdinalIgnoreCase))
                        {
                            // Keep or create translucent shield material
                            continue;
                        }
                        r.sharedMaterial = atlasMaterial;
                    }
                }
                else
                {
                    // Ensure all materials have GPU instancing enabled
                    Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in renderers)
                    {
                        foreach (Material m in r.sharedMaterials)
                        {
                            if (m != null && !m.enableInstancing)
                            {
                                m.enableInstancing = true;
                                EditorUtility.SetDirty(m);
                            }
                        }
                    }
                }

                // Setup BoxCollider if specified
                if (meta.HasCollider)
                {
                    BoxCollider collider = root.GetComponent<BoxCollider>();
                    if (collider == null)
                    {
                        collider = root.AddComponent<BoxCollider>();
                    }
                    collider.center = meta.ColliderCenter;
                    collider.size = meta.ColliderSize;
                }

                // Setup Sockets
                if (meta.Sockets != null && meta.Sockets.Count > 0)
                {
                    foreach (SocketData socket in meta.Sockets)
                    {
                        Transform existing = root.transform.Find(socket.Name);
                        GameObject socketGo = existing != null ? existing.gameObject : new GameObject(socket.Name);
                        socketGo.transform.SetParent(root.transform, false);
                        socketGo.transform.localPosition = socket.Position;
                        if (socket.Forward.sqrMagnitude > 0.001f)
                        {
                            socketGo.transform.localRotation = Quaternion.LookRotation(socket.Forward);
                        }
                    }
                }

                // Setup LODGroup if LOD submeshes are detected
                SetupLodGroupIfAvailable(root, modelInstance);

                // Save Prefab
                string targetPrefabPath = PrefabFolder + "/" + unitName + "Visual.prefab";
                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, targetPrefabPath, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Failed to save prefab asset at: " + targetPrefabPath);
                }

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string absolutePrefabPath = Path.GetFullPath(Path.Combine(projectRoot, targetPrefabPath));
                NormalizeEmptyUnityYamlStrings(absolutePrefabPath);
                string metaPath = absolutePrefabPath + ".meta";
                if (File.Exists(metaPath))
                {
                    NormalizeEmptyUnityYamlStrings(metaPath);
                }

                Debug.Log("[VehiclePrefabBuilder] Successfully created prefab: " + targetPrefabPath);
                return targetPrefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void SetupLodGroupIfAvailable(GameObject root, GameObject modelInstance)
        {
            Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
            var lod0Renderers = new List<Renderer>();
            var lod1Renderers = new List<Renderer>();
            var lod2Renderers = new List<Renderer>();

            foreach (Renderer r in renderers)
            {
                string rName = r.name.ToLowerInvariant();
                if (rName.Contains("lod0") || rName.Contains("_l0"))
                {
                    lod0Renderers.Add(r);
                }
                else if (rName.Contains("lod1") || rName.Contains("_l1"))
                {
                    lod1Renderers.Add(r);
                }
                else if (rName.Contains("lod2") || rName.Contains("_l2"))
                {
                    lod2Renderers.Add(r);
                }
            }

            // Only construct LODGroup if we found distinct LOD meshes
            if (lod0Renderers.Count > 0 && lod1Renderers.Count > 0)
            {
                LODGroup lodGroup = root.GetComponent<LODGroup>();
                if (lodGroup == null)
                {
                    lodGroup = root.AddComponent<LODGroup>();
                }

                var lodList = new List<LOD>();
                lodList.Add(new LOD(0.50f, lod0Renderers.ToArray()));
                lodList.Add(new LOD(0.20f, lod1Renderers.ToArray()));

                if (lod2Renderers.Count > 0)
                {
                    lodList.Add(new LOD(0.05f, lod2Renderers.ToArray()));
                }

                lodGroup.SetLODs(lodList.ToArray());
                lodGroup.RecalculateBounds();
            }
        }

        private static GameObject FindModelAsset(string unitName)
        {
            string[] candidatePaths = new[]
            {
                ModelFolder + "/" + unitName + "_Atlas.fbx",
                ModelFolder + "/" + unitName + "_LODs.fbx",
                ModelFolder + "/" + unitName + ".fbx",
                AssetRoot + "/" + unitName + ".fbx"
            };

            foreach (string path in candidatePaths)
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null)
                {
                    return asset;
                }
            }

            // Fallback: search by AssetDatabase filter
            string[] guids = AssetDatabase.FindAssets(unitName + " t:Model");
            if (guids != null && guids.Length > 0)
            {
                string foundPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<GameObject>(foundPath);
            }

            return null;
        }

        private static Material SetupAtlasTexturesAndMaterial(string unitName)
        {
            string albedoPath = TextureFolder + "/" + unitName + "_Albedo.png";
            string metallicPath = TextureFolder + "/" + unitName + "_MetallicGloss.png";
            string normalPath = TextureFolder + "/" + unitName + "_Normal.png";
            string emissionPath = TextureFolder + "/" + unitName + "_Emission.png";

            Texture2D albedoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            if (albedoTex == null)
            {
                // No atlas texture found
                return null;
            }

            // Configure Normal Map importer if needed
            Texture2D normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (normalTex != null)
            {
                TextureImporter normImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
                if (normImporter != null && normImporter.textureType != TextureImporterType.NormalMap)
                {
                    normImporter.textureType = TextureImporterType.NormalMap;
                    normImporter.SaveAndReimport();
                    normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                }
            }

            Texture2D metallicTex = AssetDatabase.LoadAssetAtPath<Texture2D>(metallicPath);
            Texture2D emissionTex = AssetDatabase.LoadAssetAtPath<Texture2D>(emissionPath);

            // Create or load Atlas Material
            string matPath = MaterialFolder + "/" + unitName + "_Atlas.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            Shader standardShader = Shader.Find("Standard");

            if (mat == null)
            {
                mat = new Material(standardShader);
                mat.name = unitName + "_Atlas";
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.shader = standardShader;
            }

            mat.mainTexture = albedoTex;
            mat.enableInstancing = true;

            if (metallicTex != null)
            {
                mat.SetTexture("_MetallicGlossMap", metallicTex);
                mat.SetFloat("_Metallic", 1.0f);
                mat.SetFloat("_Glossiness", 1.0f);
            }

            if (normalTex != null)
            {
                mat.EnableKeyword("_NORMALMAP");
                mat.SetTexture("_BumpMap", normalTex);
                mat.SetFloat("_BumpScale", 1.0f);
            }
            else
            {
                mat.DisableKeyword("_NORMALMAP");
            }

            if (emissionTex != null)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetTexture("_EmissionMap", emissionTex);
                mat.SetColor("_EmissionColor", Color.white);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GameplayMetadata ParseGameplayJson(string jsonPath)
        {
            var meta = new GameplayMetadata();
            string text = File.ReadAllText(jsonPath);

            Match unitMatch = Regex.Match(text, @"\""unitName\""\s*:\s*\""([^\""]+)\""");
            if (unitMatch.Success) meta.UnitName = unitMatch.Groups[1].Value;

            Match archMatch = Regex.Match(text, @"\""archetype\""\s*:\s*\""([^\""]+)\""");
            if (archMatch.Success) meta.Archetype = archMatch.Groups[1].Value;

            Match rootMatch = Regex.Match(text, @"\""visualRootPath\""\s*:\s*\""([^\""]+)\""");
            if (rootMatch.Success) meta.VisualRootPath = rootMatch.Groups[1].Value;

            // BoxCollider Bounds
            Match centerMatch = Regex.Match(text, @"\""center\""\s*:\s*\[\s*([-0-9.]+)\s*,\s*([-0-9.]+)\s*,\s*([-0-9.]+)\s*\]");
            Match sizeMatch = Regex.Match(text, @"\""size\""\s*:\s*\[\s*([-0-9.]+)\s*,\s*([-0-9.]+)\s*,\s*([-0-9.]+)\s*\]");

            if (centerMatch.Success && sizeMatch.Success)
            {
                float cx = float.Parse(centerMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                float cy = float.Parse(centerMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                float cz = float.Parse(centerMatch.Groups[3].Value, CultureInfo.InvariantCulture);

                float sx = float.Parse(sizeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                float sy = float.Parse(sizeMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                float sz = float.Parse(sizeMatch.Groups[3].Value, CultureInfo.InvariantCulture);

                // Blender coordinate translation: X=X, Y=Z (up), Z=Y (forward)
                meta.ColliderCenter = new Vector3(cx, cz, cy);
                meta.ColliderSize = new Vector3(sx, sz, sy);
                meta.HasCollider = true;
            }

            // Sockets
            // Match pattern: "Socket_...": { "position": [...], "forward": [...] }
            MatchCollection socketMatches = Regex.Matches(text,
                @"\""([A-Za-z0-9_]+)\""\s*:\s*\{\s*\""position\""\s*:\s*\[\s*([-0-9.]+)\s*,\s*([-0-9.]+)\s*,\s*([-0-9.]+)\s*\]\s*,\s*\""forward\""\s*:\s*\[\s*([-0-9.]+)\s*,\s*([-0-9.]+)\s*,\s*([-0-9.]+)\s*\]",
                RegexOptions.Multiline);

            foreach (Match sm in socketMatches)
            {
                string sName = sm.Groups[1].Value;
                float px = float.Parse(sm.Groups[2].Value, CultureInfo.InvariantCulture);
                float py = float.Parse(sm.Groups[3].Value, CultureInfo.InvariantCulture);
                float pz = float.Parse(sm.Groups[4].Value, CultureInfo.InvariantCulture);

                float fx = float.Parse(sm.Groups[5].Value, CultureInfo.InvariantCulture);
                float fy = float.Parse(sm.Groups[6].Value, CultureInfo.InvariantCulture);
                float fz = float.Parse(sm.Groups[7].Value, CultureInfo.InvariantCulture);

                meta.Sockets.Add(new SocketData
                {
                    Name = sName,
                    Position = new Vector3(px, pz, py),
                    Forward = new Vector3(fx, fz, fy)
                });
            }

            return meta;
        }

        private static void EnsureDirectories()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string[] dirs = new[] { PrefabFolder, ModelFolder, TextureFolder, MaterialFolder };
            foreach (string d in dirs)
            {
                string abs = Path.GetFullPath(Path.Combine(projectRoot, d));
                if (!Directory.Exists(abs))
                {
                    Directory.CreateDirectory(abs);
                }
            }
        }

        private static void NormalizeEmptyUnityYamlStrings(string filePath)
        {
            if (!File.Exists(filePath)) return;
            string contents = File.ReadAllText(filePath);
            string normalized = contents;
            if (string.Equals(Path.GetExtension(filePath), ".meta", StringComparison.OrdinalIgnoreCase))
            {
                normalized = Regex.Replace(normalized, @"(?m)[ \t]+(?=\r?$)", string.Empty);
            }

            normalized = Regex.Replace(normalized,
                @"(?m)^([ \t]*(?:value|userData|assetBundleName|assetBundleVariant|motionNodeName|" +
                @"rigImportErrors|rigImportWarnings|animationImportErrors|animationImportWarnings|" +
                @"animationRetargetingWarnings|rootMotionBoneName):)[ \t]*(\r?)$",
                "$1 \"\"$2");

            if (!string.Equals(contents, normalized, StringComparison.Ordinal))
            {
                File.WriteAllText(filePath, normalized, new UTF8Encoding(false));
            }
        }
    }
}
