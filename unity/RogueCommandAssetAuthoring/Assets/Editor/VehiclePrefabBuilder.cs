using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
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
        private static readonly char[] LodNameSeparators = { '_', '-', '.', ' ' };

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

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string repositoryRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", ".."));
            ModelGeometryBudgetProfile.RequireCalibrated(repositoryRoot);
            float[] lodTransitions = ModelGeometryBudgetProfile.GetLodTransitions(repositoryRoot, unitName);
            RogueCommandVisualSampleScaffolder.EnsureUrpAuthoringPipeline();

            Debug.Log("[VehiclePrefabBuilder] Building prefab for unit: " + unitName + " (Archetype: " + meta.Archetype + ")");

            // Ensure directory structures exist
            EnsureDirectories();

            // Setup or import Atlas textures if present
            Material atlasMaterial = SetupAtlasTexturesAndMaterial(unitName);

            // Prefer an animated primary FBX when present; otherwise use the LOD, atlas, or base model.
            GameObject modelAsset = FindModelAsset(unitName);
            if (modelAsset == null)
            {
                throw new FileNotFoundException("Could not locate authored FBX model for unit: " + unitName);
            }
            GameObject lodAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder + "/" + unitName + "_LODs.fbx");

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
                GameObject lodInstance = null;
                if (lodAsset != null && lodAsset != modelAsset)
                {
                    lodInstance = PrefabUtility.InstantiatePrefab(lodAsset) as GameObject;
                    if (lodInstance == null)
                    {
                        throw new InvalidOperationException("Failed to instantiate profile-driven LOD FBX for: " + unitName);
                    }
                    lodInstance.name = unitName + "LODGeometry";
                    lodInstance.transform.SetParent(root.transform, false);
                    lodInstance.transform.localPosition = Vector3.zero;
                    lodInstance.transform.localRotation = Quaternion.identity;
                    lodInstance.transform.localScale = Vector3.one;
                }

                // 1. Convert collision hull meshes (_COLLIDER) into real Unity BoxColliders and disable their visible renderers
                int convertedColliderCount = 0;
                Renderer[] allModelRenderers = root.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in allModelRenderers)
                {
                    if (r.name.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        r.enabled = false;
                        MeshFilter mf = r.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null)
                        {
                            BoxCollider bc = r.GetComponent<BoxCollider>();
                            if (bc == null)
                            {
                                bc = r.gameObject.AddComponent<BoxCollider>();
                            }
                            Bounds b = mf.sharedMesh.bounds;
                            bc.center = b.center;
                            bc.size = b.size;
                            convertedColliderCount++;
                            EditorUtility.SetDirty(r.gameObject);
                        }
                    }
                }

                // 2. Assign Atlas material if available, or configure instancing on existing materials
                if (atlasMaterial != null)
                {
                    Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in renderers)
                    {
                        if (r.name.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            r.enabled = false;
                            continue;
                        }
                        if (r.name.EndsWith("_ShieldBubble", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        r.sharedMaterial = atlasMaterial;
                    }
                }
                else
                {
                    // Ensure all materials have GPU instancing enabled
                    Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in renderers)
                    {
                        if (r.name.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            r.enabled = false;
                            continue;
                        }
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

                // 3. Setup compound root BoxCollider if specified and no dedicated child colliders were present
                if (meta.HasCollider && convertedColliderCount == 0)
                {
                    BoxCollider collider = root.GetComponent<BoxCollider>();
                    if (collider == null)
                    {
                        collider = root.AddComponent<BoxCollider>();
                    }
                    collider.center = meta.ColliderCenter;
                    collider.size = meta.ColliderSize;
                }

                // 4. Setup Sockets
                if (meta.Sockets != null && meta.Sockets.Count > 0)
                {
                    foreach (SocketData socket in meta.Sockets)
                    {
                        Transform existing = FindTransformRecursive(root.transform, socket.Name);
                        if (existing != null)
                        {
                            // Socket already exists in model hierarchy (e.g. from FBX or turret/armature).
                            // Only update transform if it is an immediate child of root to avoid breaking nested pivots.
                            if (existing.parent == root.transform)
                            {
                                existing.localPosition = socket.Position;
                                if (socket.Forward.sqrMagnitude > 0.001f)
                                {
                                    existing.localRotation = Quaternion.LookRotation(socket.Forward);
                                }
                            }
                        }
                        else
                        {
                            // Socket not present in model hierarchy; create fallback under root
                            GameObject socketGo = new GameObject(socket.Name);
                            socketGo.transform.SetParent(root.transform, false);
                            socketGo.transform.localPosition = socket.Position;
                            if (socket.Forward.sqrMagnitude > 0.001f)
                            {
                                socketGo.transform.localRotation = Quaternion.LookRotation(socket.Forward);
                            }
                        }
                    }
                }

                // Add correctly placed fallback anchors for the loader's procedural VFX.
                // Existing FBX sockets always win; this search must remain recursive to avoid
                // duplicate transforms and ambiguous runtime role bindings.
                EnsureCanonicalVfxSockets(root, modelInstance);
                EnsureTurretAndRadarSockets(root, modelInstance);
                EnsureCanonicalHardpointSockets(root, modelInstance);

                // Scaffold muzzle-flash particles (FirePoint_0 child 0) and calibrated 3D mono AudioSources.
                // Damage sockets stay clean: RuntimeVisualDirector spawns smoke/fire dynamically.
                EnsureCanonicalVfxAndAudioComponents(root);

                // 5. Mount Detached Modular Attachments (ERA, APS, Sponson, FuelDrums) if present
                EnsureCanonicalHardpointGroups(root);
                MountModularAttachmentsIfAvailable(root, unitName);

                // 6. Mount Damaged Variant Model (<Name>_Damaged.fbx) if present
                MountDamagedVariantIfAvailable(root, unitName, atlasMaterial);

                // 7. Setup Animator & AnimatorController if model has bones or animation clips
                SetupAnimatorIfAvailable(root, modelInstance, modelAsset, unitName);

                // 8. Setup LODGroup if LOD submeshes are detected
                SetupLodGroupIfAvailable(root, modelInstance, lodInstance, lodTransitions);

                // Save Prefab
                string targetPrefabPath = PrefabFolder + "/" + unitName + "Visual.prefab";
                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, targetPrefabPath, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Failed to save prefab asset at: " + targetPrefabPath);
                }

                string absolutePrefabPath = Path.GetFullPath(Path.Combine(projectRoot, targetPrefabPath));
                NormalizeEmptyUnityYamlStrings(absolutePrefabPath);
                string metaPath = absolutePrefabPath + ".meta";
                if (File.Exists(metaPath))
                {
                    NormalizeEmptyUnityYamlStrings(metaPath);
                }

                Debug.Log("[VehiclePrefabBuilder] Successfully created prefab: " + targetPrefabPath);
                ValidateSavedPrefab(targetPrefabPath, unitName);
                return targetPrefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void SetupLodGroupIfAvailable(
            GameObject root,
            GameObject modelInstance,
            GameObject lodInstance,
            float[] transitions)
        {
            Renderer[] lod0Renderers;
            Renderer[] lod1Renderers;
            Renderer[] lod2Renderers;
            string failureReason;
            bool hasRequiredLods = TryGetLodRendererGroups(
                modelInstance, out lod0Renderers, out lod1Renderers, out lod2Renderers, out failureReason);
            if (hasRequiredLods && lod2Renderers.Length == 0)
            {
                hasRequiredLods = false;
                failureReason = "the imported model has no LOD2 renderer group";
            }

            if (!hasRequiredLods && lodInstance != null)
            {
                Renderer[] primaryRenderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                var primaryLod0 = new List<Renderer>();
                for (int i = 0; i < primaryRenderers.Length; i++)
                {
                    if (HasRenderableLodMesh(primaryRenderers[i]) &&
                        primaryRenderers[i].name.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        primaryLod0.Add(primaryRenderers[i]);
                    }
                }

                Renderer[] discardedLod0;
                string lodFailure;
                if (TryGetLodRendererGroups(lodInstance, out discardedLod0, out lod1Renderers, out lod2Renderers, out lodFailure) &&
                    primaryLod0.Count > 0 && lod2Renderers.Length > 0)
                {
                    for (int i = 0; i < discardedLod0.Length; i++)
                    {
                        discardedLod0[i].enabled = false;
                    }
                    lod0Renderers = primaryLod0.ToArray();
                    RebindSkinnedLodRenderers(modelInstance, lod1Renderers, lod2Renderers);
                    failureReason = null;
                    hasRequiredLods = true;
                }
                else
                {
                    failureReason = "primary LOD0 and separate LOD1/LOD2 renderers are required; " + lodFailure;
                }
            }

            // Only construct LODGroup if LOD0 and LOD1 are both present and distinct.
            if (hasRequiredLods)
            {
                LODGroup lodGroup = root.GetComponent<LODGroup>();
                if (lodGroup == null)
                {
                    lodGroup = root.AddComponent<LODGroup>();
                }

                var lodList = new List<LOD>();
                lodList.Add(new LOD(transitions[0], lod0Renderers));
                lodList.Add(new LOD(transitions[1], lod1Renderers));

                if (lod2Renderers.Length > 0)
                {
                    lodList.Add(new LOD(transitions[2], lod2Renderers));
                }

                lodGroup.SetLODs(lodList.ToArray());
                lodGroup.RecalculateBounds();
            }
            else
            {
                throw new InvalidDataException("Profile-driven LODGroup is incomplete for " + modelInstance.name + ": " + failureReason);
            }
        }

        private static void RebindSkinnedLodRenderers(
            GameObject primaryModel,
            Renderer[] lod1Renderers,
            Renderer[] lod2Renderers)
        {
            var bonesByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            Transform[] primaryTransforms = primaryModel.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < primaryTransforms.Length; i++)
            {
                if (!bonesByName.ContainsKey(primaryTransforms[i].name))
                {
                    bonesByName.Add(primaryTransforms[i].name, primaryTransforms[i]);
                }
            }

            Renderer[][] groups = { lod1Renderers, lod2Renderers };
            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                for (int rendererIndex = 0; rendererIndex < groups[groupIndex].Length; rendererIndex++)
                {
                    SkinnedMeshRenderer skinned = groups[groupIndex][rendererIndex] as SkinnedMeshRenderer;
                    if (skinned == null) continue;
                    Transform[] sourceBones = skinned.bones;
                    var reboundBones = new Transform[sourceBones.Length];
                    for (int boneIndex = 0; boneIndex < sourceBones.Length; boneIndex++)
                    {
                        Transform rebound;
                        if (sourceBones[boneIndex] == null || !bonesByName.TryGetValue(sourceBones[boneIndex].name, out rebound))
                        {
                            throw new InvalidDataException("LOD renderer " + skinned.name +
                                " has a bone that is absent from the primary model.");
                        }
                        reboundBones[boneIndex] = rebound;
                    }
                    skinned.bones = reboundBones;
                    if (skinned.rootBone != null)
                    {
                        Transform reboundRoot;
                        if (!bonesByName.TryGetValue(skinned.rootBone.name, out reboundRoot))
                        {
                            throw new InvalidDataException("LOD renderer " + skinned.name + " has no matching root bone.");
                        }
                        skinned.rootBone = reboundRoot;
                    }
                }
            }
        }

        private static bool TryGetLodRendererGroups(
            GameObject modelAsset,
            out Renderer[] lod0Renderers,
            out Renderer[] lod1Renderers,
            out Renderer[] lod2Renderers,
            out string failureReason)
        {
            var lod0 = new List<Renderer>();
            var lod1 = new List<Renderer>();
            var lod2 = new List<Renderer>();

            Renderer[] renderers = modelAsset != null
                ? modelAsset.GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];

            foreach (Renderer renderer in renderers)
            {
                if (!HasRenderableLodMesh(renderer)) continue;
                int lodLevel = GetLodLevel(renderer.name);
                if (lodLevel == 0)
                {
                    lod0.Add(renderer);
                }
                else if (lodLevel == 1)
                {
                    lod1.Add(renderer);
                }
                else if (lodLevel == 2)
                {
                    lod2.Add(renderer);
                }
            }

            lod0Renderers = lod0.ToArray();
            lod1Renderers = lod1.ToArray();
            lod2Renderers = lod2.ToArray();

            if (lod0Renderers.Length > 0 && lod1Renderers.Length > 0)
            {
                failureReason = null;
                return true;
            }

            var missing = new List<string>();
            if (lod0Renderers.Length == 0) missing.Add("LOD0/_L0 renderer group is empty");
            if (lod1Renderers.Length == 0) missing.Add("LOD1/_L1 renderer group is empty");
            failureReason = string.Join(" and ", missing.ToArray());
            return false;
        }

        private static int GetLodLevel(string rendererName)
        {
            if (string.IsNullOrEmpty(rendererName)) return -1;

            string[] tokens = rendererName.ToUpperInvariant().Split(
                LodNameSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                switch (token)
                {
                    case "LOD0":
                    case "L0": return 0;
                    case "LOD1":
                    case "L1": return 1;
                    case "LOD2":
                    case "L2": return 2;
                }
            }

            return -1;
        }

        private static bool HasRenderableLodMesh(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled) return false;

            for (Transform current = renderer.transform; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
            }

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && HasUsableLodMesh(meshFilter.sharedMesh)) return true;

            SkinnedMeshRenderer skinnedRenderer = renderer as SkinnedMeshRenderer;
            return skinnedRenderer != null && HasUsableLodMesh(skinnedRenderer.sharedMesh);
        }

        private static bool HasUsableLodMesh(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount == 0 || mesh.subMeshCount == 0) return false;
            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                if (mesh.GetIndexCount(subMeshIndex) > 0) return true;
            }

            return false;
        }

        private static void MountModularAttachmentsIfAvailable(GameObject root, string unitName)
        {
            string[] guids = AssetDatabase.FindAssets(unitName + "_Attachment_ t:Model", new[] { ModelFolder, AssetRoot });
            if (guids == null || guids.Length == 0) return;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string attFileName = Path.GetFileNameWithoutExtension(path);
                string prefix = unitName + "_Attachment_";
                if (!attFileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                string attType = attFileName.Substring(prefix.Length);
                string socketType = NormalizeHardpointSocketType(attType);
                GameObject attAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (attAsset != null)
                {
                    Transform socket = FindTransformRecursive(root.transform, "Socket_Hardpoint_" + socketType);
                    if (socket == null)
                    {
                        Debug.LogWarning("[VehiclePrefabBuilder] No Socket_Hardpoint_" + socketType +
                            " found for " + attFileName + "; the attachment was skipped.");
                        continue;
                    }

                    Transform parentTransform = GetOrCreateAttachmentGroup(root, socket, socketType);
                    if (parentTransform == null)
                    {
                        Debug.LogWarning("[VehiclePrefabBuilder] Could not resolve a unique RCL_AttachmentGroup_" +
                            socketType + " under Socket_Hardpoint_" + socketType + "; the attachment was skipped.");
                        continue;
                    }

                    GameObject attInstance = PrefabUtility.InstantiatePrefab(attAsset) as GameObject;
                    if (attInstance != null)
                    {
                        attInstance.name = attFileName;
                        // Detached FBX exports preserve the authored object transform.
                        // Reparenting with worldPositionStays=false keeps that local TRS.
                        attInstance.transform.SetParent(parentTransform, false);
                    }
                }
            }
        }

        private static void EnsureCanonicalHardpointGroups(GameObject root)
        {
            if (root == null) return;
            string[] socketTypes = { "ERA", "APS", "Sponsons", "FuelDrums" };
            for (int i = 0; i < socketTypes.Length; i++)
            {
                string socketName = "Socket_Hardpoint_" + socketTypes[i];
                Transform socket = FindTransformRecursive(root.transform, socketName);
                if (socket == null) continue;

                int socketCount = 0;
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < all.Length; j++)
                {
                    if (string.Equals(all[j].name, socketName, StringComparison.OrdinalIgnoreCase)) socketCount++;
                }
                if (socketCount != 1)
                {
                    Debug.LogWarning("[VehiclePrefabBuilder] Expected one " + socketName +
                        " but found " + socketCount + "; no attachment group was created.");
                    continue;
                }

                GetOrCreateAttachmentGroup(root, socket, socketTypes[i]);
            }
        }

        internal static void EnsureCanonicalHardpointSockets(GameObject root, GameObject model)
        {
            if (root == null || model == null) return;
            string[] socketTypes = { "ERA", "APS", "Sponsons", "FuelDrums" };
            Bounds modelBounds = CalculateModelBounds(root, model);
            Vector3 center = modelBounds.center;
            Vector3 extents = modelBounds.extents;
            Transform turret = FindTransformRecursive(root.transform, "Socket_TurretPivot");

            for (int i = 0; i < socketTypes.Length; i++)
            {
                string socketName = "Socket_Hardpoint_" + socketTypes[i];
                Transform existing = FindCanonicalHardpointSocket(root.transform, socketName, out int count);
                if (count > 1)
                {
                    Debug.LogWarning("[VehiclePrefabBuilder] Found " + count + " transforms for " + socketName +
                        "; preserving the authored hierarchy and leaving this hardpoint unavailable.");
                    continue;
                }
                if (existing != null) continue;

                Vector3 localPosition;
                switch (socketTypes[i])
                {
                    case "ERA":
                        localPosition = turret != null
                            ? turret.localPosition + new Vector3(0f, 0.40f, 0.10f)
                            : center + new Vector3(0f, extents.y * 0.55f, extents.z * 0.28f);
                        break;
                    case "APS":
                        localPosition = turret != null
                            ? turret.localPosition + new Vector3(0.35f, -0.35f, 0.40f)
                            : center + new Vector3(extents.x * 0.38f, extents.y * 0.55f, 0f);
                        break;
                    case "Sponsons":
                        localPosition = center + new Vector3(extents.x * 0.48f, 0f, 0f);
                        break;
                    default:
                        localPosition = center + new Vector3(0f, -extents.y * 0.30f, -extents.z * 0.45f);
                        break;
                }

                GameObject socketObject = new GameObject(socketName);
                socketObject.transform.SetParent(root.transform, false);
                socketObject.transform.localPosition = localPosition;
                socketObject.transform.localRotation = Quaternion.identity;
                socketObject.transform.localScale = Vector3.one;
            }
        }

        private static Transform FindCanonicalHardpointSocket(Transform root, string socketName, out int count)
        {
            count = 0;
            Transform match = null;
            if (root == null) return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                string currentName = all[i].name ?? string.Empty;
                if (!string.Equals(currentName, socketName, StringComparison.OrdinalIgnoreCase) &&
                    !currentName.EndsWith("_" + socketName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                count++;
                match = all[i];
            }

            if (count == 1 && match != null && !string.Equals(match.name, socketName, StringComparison.Ordinal))
            {
                match.name = socketName;
            }
            return count == 1 ? match : null;
        }

        private static Bounds CalculateModelBounds(GameObject root, GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds bounds = new Bounds(root.transform.position, Vector3.one);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled ||
                    renderer.name.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                Bounds rendererBounds = renderer.bounds;
                Vector3 localCenter = root.transform.InverseTransformPoint(rendererBounds.center);
                Vector3 localSize = rendererBounds.size;
                if (!hasBounds)
                {
                    bounds = new Bounds(localCenter, localSize);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(new Bounds(localCenter, localSize));
                }
            }
            return bounds;
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

        internal static void EnsureCanonicalVfxSockets(GameObject root, GameObject model)
        {
            if (root == null || model == null)
            {
                throw new ArgumentNullException(root == null ? "root" : "model");
            }

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                Debug.LogWarning("[VehiclePrefabBuilder] Cannot place fallback VFX sockets without model renderers.");
                return;
            }

            Renderer firstRenderer = null;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    firstRenderer = renderers[i];
                    break;
                }
            }
            if (firstRenderer == null)
            {
                Debug.LogWarning("[VehiclePrefabBuilder] Cannot place fallback VFX sockets because all model renderers are null.");
                return;
            }

            Bounds worldBounds = firstRenderer.bounds;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i] != firstRenderer)
                {
                    worldBounds.Encapsulate(renderers[i].bounds);
                }
            }

            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 worldCorner = new Vector3(
                    (corner & 1) == 0 ? worldBounds.min.x : worldBounds.max.x,
                    (corner & 2) == 0 ? worldBounds.min.y : worldBounds.max.y,
                    (corner & 4) == 0 ? worldBounds.min.z : worldBounds.max.z);
                Vector3 localCorner = root.transform.InverseTransformPoint(worldCorner);
                min = Vector3.Min(min, localCorner);
                max = Vector3.Max(max, localCorner);
            }
            Vector3 size = max - min;
            Vector3 center = (min + max) * 0.5f;
            float depthOffset = Mathf.Max(0.05f, size.z * 0.04f);

            Vector3 muzzle = new Vector3(center.x, Mathf.Lerp(min.y, max.y, 0.72f), max.z + depthOffset);
            Vector3 smoke = new Vector3(center.x, Mathf.Lerp(min.y, max.y, 0.82f), min.z + size.z * 0.20f);
            Vector3 fire = new Vector3(center.x + size.x * 0.12f, Mathf.Lerp(min.y, max.y, 0.55f), center.z);
            Vector3 engine = new Vector3(center.x, Mathf.Lerp(min.y, max.y, 0.30f), min.z + size.z * 0.18f);
            Vector3 ground = new Vector3(center.x, min.y, center.z);

            EnsureSocketIfMissing(root.transform, "Socket_MuzzlePrimary", muzzle);
            EnsureSocketIfMissing(root.transform, "Socket_DamageSmoke", smoke);
            EnsureSocketIfMissing(root.transform, "Socket_DamageFire", fire);
            EnsureSocketIfMissing(root.transform, "Socket_EngineAudio", engine);
            EnsureSocketIfMissing(root.transform, "Socket_WeaponAudio", muzzle);
            EnsureSocketIfMissing(root.transform, "Socket_GroundCenter", ground);
        }

        /// <summary>
        /// Guarantees turret/radar sockets that EnsureCanonicalVfxSockets does not add.
        /// Recursive missing-check avoids duplicate transforms (UniqueSocketRoleBinding).
        /// </summary>
        private static void EnsureTurretAndRadarSockets(GameObject root, GameObject modelInstance)
        {
            if (root == null || modelInstance == null) return;
            Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;
            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null) worldBounds.Encapsulate(renderers[i].bounds);
            }
            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 worldCorner = new Vector3(
                    (corner & 1) == 0 ? worldBounds.min.x : worldBounds.max.x,
                    (corner & 2) == 0 ? worldBounds.min.y : worldBounds.max.y,
                    (corner & 4) == 0 ? worldBounds.min.z : worldBounds.max.z);
                Vector3 localCorner = root.transform.InverseTransformPoint(worldCorner);
                min = Vector3.Min(min, localCorner);
                max = Vector3.Max(max, localCorner);
            }
            Vector3 size = max - min;
            if (size.sqrMagnitude < 1e-6f) return;
            Vector3 center = (min + max) * 0.5f;
            Vector3 turretPivot = new Vector3(center.x, Mathf.Lerp(min.y, max.y, 0.72f), center.z + size.z * 0.10f);
            EnsureSocketIfMissing(root.transform, "Socket_TurretPivot", turretPivot);
            Transform pivot = FindTransformRecursive(root.transform, "Socket_TurretPivot");
            if (pivot != null && FindTransformRecursive(root.transform, "Socket_TurretPitch") == null)
            {
                GameObject pitch = new GameObject("Socket_TurretPitch");
                pitch.transform.SetParent(pivot, false);
                pitch.transform.localPosition = Vector3.zero;
                pitch.transform.localRotation = Quaternion.identity;
                pitch.transform.localScale = Vector3.one;
            }
            Vector3 radar = new Vector3(center.x - size.x * 0.22f, Mathf.Lerp(min.y, max.y, 0.85f), min.z + size.z * 0.30f);
            Vector3 antenna = new Vector3(center.x + size.x * 0.22f, Mathf.Lerp(min.y, max.y, 0.80f), min.z + size.z * 0.30f);
            EnsureSocketIfMissing(root.transform, "Socket_RadarMast", radar);
            EnsureSocketIfMissing(root.transform, "Socket_Antenna", antenna);
        }

        private static void ValidateSavedPrefab(string prefabPath, string unitName)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[VehiclePrefabBuilder] Could not reload prefab for validation: " + prefabPath);
                return;
            }
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            int enabled = 0;
            foreach (Renderer r in renderers) if (r != null && r.enabled) enabled++;
            Animator animator = prefab.GetComponent<Animator>();
            string animatorState = animator == null ? "none" :
                ((animator.avatar != null && animator.avatar.isValid) ? "valid" : "invalid") +
                "/controller " + (animator.runtimeAnimatorController != null ? "present" : "missing");
            Debug.Log("[VehiclePrefabBuilder] " + unitName + " prefab OK: renderers=" + renderers.Length +
                " (enabled " + enabled + "), animator=" + animatorState);
        }

        private const string VfxMaterialFolder = MaterialFolder + "/VFX";
        private const string MuzzleFlashMaterialPath = VfxMaterialFolder + "/VFX_MuzzleFlash_Additive.mat";

        /// <summary>
        /// Ensures FirePoint_0 (with a ParticleSystem as child index 0, as ShootProjectile.Init expects)
        /// under each muzzle socket, and calibrated 3D AudioSources on the weapon/engine audio sockets.
        /// Idempotent: existing FirePoint_0 children, particle systems and AudioSources are preserved.
        /// </summary>
        internal static void EnsureCanonicalVfxAndAudioComponents(GameObject root)
        {
            if (root == null) throw new ArgumentNullException("root");

            string[] muzzleSockets = { "Socket_MuzzlePrimary", "Socket_MuzzleSecondary" };
            string[] firePointNames = { "FirePoint_0", "FirePoint_1" };
            Material flashMaterial = null;
            for (int i = 0; i < muzzleSockets.Length; i++)
            {
                Transform muzzle = FindTransformRecursive(root.transform, muzzleSockets[i]);
                if (muzzle == null) continue;

                Transform firePoint = muzzle.Find(firePointNames[i]);
                if (firePoint == null)
                {
                    var fp = new GameObject(firePointNames[i]);
                    fp.transform.SetParent(muzzle, false);
                    fp.transform.localPosition = Vector3.zero;
                    fp.transform.localRotation = Quaternion.identity;
                    firePoint = fp.transform;
                }

                // ShootProjectile reads child.GetChild(0).GetComponent<ParticleSystem>().
                if (firePoint.childCount > 0 && firePoint.GetChild(0).GetComponent<ParticleSystem>() != null) continue;

                if (flashMaterial == null) flashMaterial = GetOrCreateMuzzleFlashMaterial();
                var flash = new GameObject("MuzzleFlash");
                flash.transform.SetParent(firePoint, false);
                flash.transform.SetSiblingIndex(0);
                ConfigureMuzzleFlash(flash.AddComponent<ParticleSystem>(), flashMaterial);
            }

            EnsureAudioSource(root.transform, "Socket_WeaponAudio", false);
            EnsureAudioSource(root.transform, "Socket_EngineAudio", true);
        }

        private static void ConfigureMuzzleFlash(ParticleSystem ps, Material material)
        {
            var main = ps.main;
            main.duration = 0.08f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.08f;
            main.startSpeed = 2.5f;
            main.startSize = 0.35f;
            main.maxParticles = 25;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12, 15) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.04f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.enableGPUInstancing = true;
        }

        private static Material GetOrCreateMuzzleFlashMaterial()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(projectRoot, VfxMaterialFolder)));

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MuzzleFlashMaterialPath);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "URP shader 'Universal Render Pipeline/Particles/Unlit' is unavailable for muzzle flash material.");
            }

            mat = new Material(shader) { name = "VFX_MuzzleFlash_Additive", enableInstancing = true };
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f); // Additive
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_SoftParticlesEnabled")) mat.SetFloat("_SoftParticlesEnabled", 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(1f, 0.72f, 0.25f, 1f));
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            AssetDatabase.CreateAsset(mat, MuzzleFlashMaterialPath);
            return mat;
        }

        private static void EnsureAudioSource(Transform root, string socketName, bool loop)
        {
            Transform socket = FindTransformRecursive(root, socketName);
            if (socket == null) return;

            AudioSource source = socket.GetComponent<AudioSource>();
            if (source == null) source = socket.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 3f;
            source.maxDistance = 60f;
            source.dopplerLevel = 0f;
        }

        private static void EnsureSocketIfMissing(Transform root, string socketName, Vector3 localPosition)
        {
            if (FindTransformRecursive(root, socketName) != null)
            {
                return;
            }

            var socket = new GameObject(socketName);
            socket.transform.SetParent(root, false);
            socket.transform.localPosition = localPosition;
            socket.transform.localRotation = Quaternion.identity;
            socket.transform.localScale = Vector3.one;
        }

        private static string NormalizeHardpointSocketType(string attachmentType)
        {
            string compact = (attachmentType ?? string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
            switch (compact.ToLowerInvariant())
            {
                case "era": return "ERA";
                case "aps": return "APS";
                case "sponson":
                case "sponsons": return "Sponsons";
                case "fueldrum":
                case "fueldrums": return "FuelDrums";
                default: return attachmentType ?? string.Empty;
            }
        }

        private static Transform GetOrCreateAttachmentGroup(GameObject root, Transform socket, string socketType)
        {
            if (root == null || socket == null) return null;
            string groupName = "RCL_AttachmentGroup_" + socketType;
            Transform group = null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (!string.Equals(all[i].name, groupName, StringComparison.OrdinalIgnoreCase)) continue;
                if (group != null && group != all[i]) return null;
                group = all[i];
            }

            if (group != null)
            {
                if (group.parent != socket) ReparentAttachmentGroupPreservingChildren(root.transform, group, socket);
                return group;
            }

            GameObject groupObject = new GameObject(groupName);
            groupObject.transform.SetParent(socket, false);
            groupObject.transform.localPosition = Vector3.zero;
            groupObject.transform.localRotation = Quaternion.identity;
            groupObject.transform.localScale = Vector3.one;
            return groupObject.transform;
        }

        private static void ReparentAttachmentGroupPreservingChildren(
            Transform visualRoot,
            Transform group,
            Transform socket)
        {
            var children = new List<Transform>();
            while (group.childCount > 0)
            {
                Transform child = group.GetChild(0);
                children.Add(child);
                child.SetParent(visualRoot, true);
            }

            group.SetParent(socket, false);
            group.localPosition = Vector3.zero;
            group.localRotation = Quaternion.identity;
            group.localScale = Vector3.one;
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] != null) children[i].SetParent(group, true);
            }
        }

        private static void MountDamagedVariantIfAvailable(GameObject root, string unitName, Material atlasMaterial)
        {
            string damagedModelPath = ModelFolder + "/" + unitName + "_Damaged.fbx";
            GameObject damagedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(damagedModelPath);
            if (damagedAsset == null)
            {
                string[] guids = AssetDatabase.FindAssets(unitName + "_Damaged t:Model", new[] { ModelFolder, AssetRoot });
                if (guids != null && guids.Length > 0)
                {
                    damagedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }

            if (damagedAsset != null)
            {
                GameObject damagedInstance = PrefabUtility.InstantiatePrefab(damagedAsset) as GameObject;
                if (damagedInstance != null)
                {
                    damagedInstance.name = unitName + "_Damaged";
                    damagedInstance.transform.SetParent(root.transform, false);
                    damagedInstance.transform.localPosition = Vector3.zero;
                    damagedInstance.transform.localRotation = Quaternion.identity;
                    damagedInstance.transform.localScale = Vector3.one;

                    if (atlasMaterial != null)
                    {
                        foreach (Renderer r in damagedInstance.GetComponentsInChildren<Renderer>(true))
                        {
                            if (r.name.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                r.enabled = false;
                                continue;
                            }
                            r.sharedMaterial = atlasMaterial;
                        }
                    }

                    // Initially disabled; activated by RuntimeVisualDirector at <= 50% HP
                    damagedInstance.SetActive(false);
                }
            }
        }

        internal static void SetupAnimatorIfAvailable(
            GameObject root,
            GameObject modelInstance,
            GameObject modelAsset,
            string unitName,
            bool preferWalkForMovement = false)
        {
            // Imported FBX files may carry an Animator of their own. The prefab must
            // have exactly one controller owner on the visual root; two Animators can
            // fight over the same rigid bones and make FireRecoil appear frozen.
            Animator[] importedAnimators = modelInstance.GetComponentsInChildren<Animator>(true);
            foreach (Animator importedAnimator in importedAnimators)
            {
                if (importedAnimator != null)
                {
                    UnityEngine.Object.DestroyImmediate(importedAnimator);
                }
            }

            SkinnedMeshRenderer smr = modelInstance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            bool hasBones = false;
            foreach (Transform t in modelInstance.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Bone_", StringComparison.OrdinalIgnoreCase) || t.name.Equals("Armature", StringComparison.OrdinalIgnoreCase))
                {
                    hasBones = true;
                    break;
                }
            }

            string modelPath = AssetDatabase.GetAssetPath(modelAsset);
            UnityEngine.Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
            var animationClips = new List<AnimationClip>();
            Avatar importedAvatar = null;
            if (allAssets != null)
            {
                foreach (var asset in allAssets)
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    {
                        animationClips.Add(clip);
                    }
                    else if (asset is Avatar avatar && avatar.isValid && importedAvatar == null)
                    {
                        importedAvatar = avatar;
                    }
                }
            }

            if (smr != null || hasBones || animationClips.Count > 0)
            {
                Animator animator = root.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = root.AddComponent<Animator>();
                }
                animator.applyRootMotion = false;

                Animator modelAnimator = modelAsset.GetComponent<Animator>();
                if (importedAvatar != null)
                {
                    animator.avatar = importedAvatar;
                }
                else if (modelAnimator != null && modelAnimator.avatar != null)
                {
                    animator.avatar = modelAnimator.avatar;
                }

                if (animationClips.Count > 0 && animator.avatar == null)
                {
                    Debug.LogWarning("[VehiclePrefabBuilder] Imported animation clips for " + unitName +
                        " have no valid Avatar; verify the FBX rig import settings.");
                }

                if (animationClips.Count > 0)
                {
                    string animFolder = AssetRoot + "/Animations";
                    if (!AssetDatabase.IsValidFolder(animFolder))
                    {
                        AssetDatabase.CreateFolder(AssetRoot, "Animations");
                    }

                    string controllerPath = animFolder + "/" + unitName + "Controller.controller";
                    AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                    if (controller == null)
                    {
                        controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                    }

                    ConfigureAnimationController(
                        controller,
                        animationClips,
                        preferWalkForMovement,
                        modelInstance,
                        unitName);

                    animator.runtimeAnimatorController = controller;
                }
            }
        }

        private static void ConfigureAnimationController(
            AnimatorController controller,
            List<AnimationClip> clips,
            bool preferWalkForMovement,
            GameObject modelInstance,
            string unitName)
        {
            if (controller == null || clips == null || clips.Count == 0 || controller.layers.Length == 0) return;

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            // Rebuild this generated graph instead of accumulating stale short-name
            // states from previous imports. The old graph could leave a Death or
            // legacy Walk state as the movement destination, making attack clips and
            // locomotion look static even though the FBX contained valid curves.
            foreach (ChildAnimatorState child in stateMachine.states)
            {
                if (child.state != null)
                {
                    stateMachine.RemoveState(child.state);
                }
            }
            foreach (AnimatorStateTransition transition in stateMachine.anyStateTransitions)
            {
                stateMachine.RemoveAnyStateTransition(transition);
            }

            AnimationClip idleClip = FindAnimationClipByPriority(clips, "Action_Idle", "Idle") ?? clips[0];
            AnimationClip driveClip = preferWalkForMovement
                ? FindAnimationClipByPriority(clips, "Action_Walk", "Action_Drive", "Walk", "Drive")
                : FindAnimationClipByPriority(clips, "Action_Drive", "Action_Walk", "Drive", "Walk");
            AnimationClip fireClip = FindAnimationClipByPriority(clips, "Action_FireRecoil", "FireRecoil", "Fire");
            AnimationClip deathClip = FindAnimationClipByPriority(clips, "Action_Death", "Death");
            if (fireClip != null && !HasBodyDrivenFireRecoilClip(fireClip))
            {
                AnimationClip generatedClip = CreateBodyDrivenFireRecoilClip(
                    controller,
                    fireClip,
                    modelInstance,
                    unitName);
                if (generatedClip != null)
                {
                    clips.Remove(fireClip);
                    clips.Add(generatedClip);
                    fireClip = generatedClip;
                }
            }
            if (fireClip != null && !HasBodyDrivenFireRecoilClip(fireClip))
            {
                throw new InvalidDataException(
                    controller.name + " FireRecoil must animate a body/pelvis position curve plus a weapon or turret curve.");
            }

            AnimatorState idleState = GetOrCreateAnimationState(stateMachine, idleClip);
            AnimatorState driveState = driveClip != null ? GetOrCreateAnimationState(stateMachine, driveClip) : null;
            AnimatorState fireState = fireClip != null ? GetOrCreateAnimationState(stateMachine, fireClip) : null;
            AnimatorState deathState = deathClip != null ? GetOrCreateAnimationState(stateMachine, deathClip) : null;

            stateMachine.defaultState = idleState;

            bool hasMovementParameter = driveState != null && EnsureAnimatorParameter(
                controller, "RCL_IsMoving", AnimatorControllerParameterType.Bool);
            bool hasFireParameter = fireState != null && EnsureAnimatorParameter(
                controller, "RCL_Fire", AnimatorControllerParameterType.Trigger);
            bool hasDeathParameter = deathState != null && EnsureAnimatorParameter(
                controller, "RCL_Death", AnimatorControllerParameterType.Trigger);

            if (hasMovementParameter && driveState != idleState)
            {
                EnsureStateTransition(idleState, driveState, "RCL_IsMoving", AnimatorConditionMode.If, 0.12f);
                EnsureStateTransition(driveState, idleState, "RCL_IsMoving", AnimatorConditionMode.IfNot, 0.12f);
            }

            if (fireState != null && idleState != null && hasFireParameter)
            {
                EnsureAnyStateTriggerTransition(stateMachine, fireState, "RCL_Fire");
                // Add the conditioned locomotion exit before the fallback idle exit.
                // Unity evaluates transitions in serialized order; this prevents a
                // shot from interrupting a moving unit and leaving it in a static pose.
                if (driveState != null && driveState != fireState && hasMovementParameter)
                {
                    EnsureExitTransitionWithCondition(
                        fireState, driveState, "RCL_IsMoving", AnimatorConditionMode.If);
                }
                EnsureExitTransition(fireState, idleState);
            }

            if (deathState != null && hasDeathParameter)
            {
                EnsureAnyStateTriggerTransition(stateMachine, deathState, "RCL_Death");
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        private static AnimationClip FindAnimationClipByPriority(
            List<AnimationClip> clips,
            params string[] nameFragments)
        {
            if (clips == null || nameFragments == null) return null;
            for (int fragmentIndex = 0; fragmentIndex < nameFragments.Length; fragmentIndex++)
            {
                string fragment = nameFragments[fragmentIndex];
                if (string.IsNullOrEmpty(fragment)) continue;
                foreach (AnimationClip clip in clips)
                {
                    if (clip == null) continue;
                    string clipName = clip.name ?? string.Empty;
                    if (string.Equals(clipName, fragment, StringComparison.OrdinalIgnoreCase) ||
                        clipName.EndsWith("|" + fragment, StringComparison.OrdinalIgnoreCase) ||
                        clipName.EndsWith(fragment, StringComparison.OrdinalIgnoreCase))
                    {
                        return clip;
                    }
                }
            }

            return null;
        }

        private static bool HasBodyDrivenFireRecoilClip(AnimationClip clip)
        {
            if (clip == null) return false;
            bool bodyPosition = false;
            bool weaponMotion = false;
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (EditorCurveBinding binding in bindings)
            {
                string path = binding.path ?? string.Empty;
                string property = binding.propertyName ?? string.Empty;
                bool positionCurve = property.IndexOf("LocalPosition", StringComparison.OrdinalIgnoreCase) >= 0;
                bool rotationCurve = property.IndexOf("LocalRotation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    property.IndexOf("Euler", StringComparison.OrdinalIgnoreCase) >= 0;
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                bool dynamicCurve = HasMeaningfulCurveVariation(curve);
                if ((path.IndexOf("Bone_Hull", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     path.IndexOf("Bone_Pelvis", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    positionCurve && dynamicCurve)
                {
                    bodyPosition = true;
                }
                if (((path.IndexOf("Bone_BarrelRecoil", StringComparison.OrdinalIgnoreCase) >= 0 && positionCurve) ||
                     (path.IndexOf("Bone_TurretPitch", StringComparison.OrdinalIgnoreCase) >= 0 && rotationCurve)) &&
                    dynamicCurve)
                {
                    weaponMotion = true;
                }
            }

            return bodyPosition && weaponMotion;
        }

        private static bool HasMeaningfulCurveVariation(AnimationCurve curve)
        {
            if (curve == null || curve.length < 2) return false;
            float first = curve.keys[0].value;
            for (int index = 1; index < curve.length; index++)
            {
                if (Mathf.Abs(curve.keys[index].value - first) > 0.0001f)
                {
                    return true;
                }
            }

            return false;
        }

        private static string FindAnimationCurvePath(
            AnimationClip clip,
            string boneName,
            string propertyToken)
        {
            if (clip == null) return string.Empty;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if ((binding.path ?? string.Empty).IndexOf(boneName, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if ((binding.propertyName ?? string.Empty).IndexOf(propertyToken, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return binding.path ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static Transform ResolveAnimationTransform(
            GameObject modelInstance,
            string animationPath,
            string boneName)
        {
            if (modelInstance == null) return null;
            if (!string.IsNullOrEmpty(animationPath))
            {
                Transform direct = modelInstance.transform.Find(animationPath);
                if (direct != null) return direct;
            }

            return FindTransformRecursive(modelInstance.transform, boneName);
        }

        private static AnimationCurve CreateRecoilCurve(
            float frameRate,
            float[] offsets,
            float baseValue)
        {
            var keys = new Keyframe[offsets.Length];
            for (int index = 0; index < offsets.Length; index++)
            {
                keys[index] = new Keyframe(
                    new[] { 0f, 1f, 2f, 4f, 8f, 11f, 14f }[index] / frameRate,
                    baseValue + offsets[index]);
            }

            return new AnimationCurve(keys);
        }

        private static void SetRecoilVectorCurves(
            AnimationClip clip,
            string path,
            Vector3 baseline,
            float[] xOffsets,
            float[] yOffsets,
            float[] zOffsets,
            float frameRate)
        {
            clip.SetCurve(path, typeof(Transform), "m_LocalPosition.x",
                CreateRecoilCurve(frameRate, xOffsets, baseline.x));
            clip.SetCurve(path, typeof(Transform), "m_LocalPosition.y",
                CreateRecoilCurve(frameRate, yOffsets, baseline.y));
            clip.SetCurve(path, typeof(Transform), "m_LocalPosition.z",
                CreateRecoilCurve(frameRate, zOffsets, baseline.z));
        }

        private static void SetRecoilEulerCurves(
            AnimationClip clip,
            string path,
            Vector3 baseline,
            float[] xOffsets,
            float frameRate)
        {
            float[] zeroOffsets = new float[xOffsets.Length];
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.x",
                CreateRecoilCurve(frameRate, xOffsets, baseline.x));
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.y",
                CreateRecoilCurve(frameRate, zeroOffsets, baseline.y));
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.z",
                CreateRecoilCurve(frameRate, zeroOffsets, baseline.z));
        }

        private static AnimationClip CreateBodyDrivenFireRecoilClip(
            AnimatorController controller,
            AnimationClip importedClip,
            GameObject modelInstance,
            string unitName)
        {
            if (controller == null || importedClip == null || modelInstance == null)
            {
                return null;
            }

            string bodyBone = FindAnimationCurvePath(importedClip, "Bone_Hull", "LocalPosition");
            string bodyBoneName = "Bone_Hull";
            if (string.IsNullOrEmpty(bodyBone))
            {
                bodyBone = FindAnimationCurvePath(importedClip, "Bone_Pelvis", "LocalPosition");
                bodyBoneName = "Bone_Pelvis";
            }

            string barrelPath = FindAnimationCurvePath(importedClip, "Bone_BarrelRecoil", "LocalPosition");
            string pitchPath = FindAnimationCurvePath(importedClip, "Bone_TurretPitch", "LocalRotation");
            if (string.IsNullOrEmpty(pitchPath))
            {
                pitchPath = FindAnimationCurvePath(importedClip, "Bone_TurretPitch", "Euler");
            }
            if (string.IsNullOrEmpty(bodyBone) || (string.IsNullOrEmpty(barrelPath) && string.IsNullOrEmpty(pitchPath)))
            {
                Debug.LogWarning("[VehiclePrefabBuilder] Cannot synthesize body-driven FireRecoil for " + unitName +
                    ": imported clip has no resolvable body and weapon paths.");
                return null;
            }

            string controllerPath = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrEmpty(controllerPath)) return null;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(controllerPath))
            {
                AnimationClip oldGenerated = asset as AnimationClip;
                if (oldGenerated != null && oldGenerated.name == importedClip.name && oldGenerated != importedClip)
                {
                    UnityEngine.Object.DestroyImmediate(oldGenerated, true);
                }
            }

            AnimationClip generatedClip = new AnimationClip();
            generatedClip.name = importedClip.name;
            generatedClip.frameRate = importedClip.frameRate > 0f ? importedClip.frameRate : 24f;
            generatedClip.wrapMode = WrapMode.ClampForever;
            generatedClip.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(generatedClip, controller);

            Transform bodyTransform = ResolveAnimationTransform(modelInstance, bodyBone, bodyBoneName);
            Transform barrelTransform = ResolveAnimationTransform(modelInstance, barrelPath, "Bone_BarrelRecoil");
            Transform pitchTransform = ResolveAnimationTransform(modelInstance, pitchPath, "Bone_TurretPitch");
            Vector3 bodyPosition = bodyTransform != null ? bodyTransform.localPosition : Vector3.zero;
            Vector3 bodyEuler = bodyTransform != null ? bodyTransform.localEulerAngles : Vector3.zero;
            Vector3 barrelPosition = barrelTransform != null ? barrelTransform.localPosition : Vector3.zero;
            Vector3 pitchEuler = pitchTransform != null ? pitchTransform.localEulerAngles : Vector3.zero;

            float[] zero = { 0f, 0f, 0f, 0f, 0f, 0f, 0f };
            float[] bodyY = { 0f, 0.008f, -0.024f, -0.010f, -0.002f, 0f, 0f };
            float[] bodyPitch = { 0f, 0.25f, -1.4f, 0.45f, 0.12f, 0f, 0f };
            float[] barrelY = { 0f, 0.035f, -0.22f, -0.080f, -0.018f, 0f, 0f };
            float[] pitch = { 0f, 0.35f, -0.90f, 0.25f, 0.08f, 0f, 0f };

            SetRecoilVectorCurves(generatedClip, bodyBone, bodyPosition, zero, bodyY, zero, generatedClip.frameRate);
            SetRecoilEulerCurves(generatedClip, bodyBone, bodyEuler, bodyPitch, generatedClip.frameRate);
            if (!string.IsNullOrEmpty(barrelPath))
            {
                SetRecoilVectorCurves(generatedClip, barrelPath, barrelPosition, zero, barrelY, zero, generatedClip.frameRate);
            }
            if (!string.IsNullOrEmpty(pitchPath))
            {
                SetRecoilEulerCurves(generatedClip, pitchPath, pitchEuler, pitch, generatedClip.frameRate);
            }

            EditorUtility.SetDirty(generatedClip);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[VehiclePrefabBuilder] Synthesized body-driven FireRecoil for " + unitName +
                " from imported paths (body=" + bodyBone + ", barrel=" + barrelPath + ", pitch=" + pitchPath + ").");
            return generatedClip;
        }

        private static AnimatorState GetOrCreateAnimationState(AnimatorStateMachine stateMachine, AnimationClip clip)
        {
            if (stateMachine == null || clip == null) return null;
            ChildAnimatorState[] states = stateMachine.states;
            foreach (ChildAnimatorState child in states)
            {
                if (child.state != null && string.Equals(child.state.name, clip.name, StringComparison.Ordinal))
                {
                    child.state.motion = clip;
                    return child.state;
                }
            }

            AnimatorState state = stateMachine.AddState(clip.name);
            state.motion = clip;
            return state;
        }

        private static bool EnsureAnimatorParameter(
            AnimatorController controller,
            string parameterName,
            AnimatorControllerParameterType parameterType)
        {
            foreach (AnimatorControllerParameter parameter in controller.parameters)
            {
                if (string.Equals(parameter.name, parameterName, StringComparison.Ordinal))
                {
                    if (parameter.type == parameterType) return true;
                    Debug.LogWarning("[VehiclePrefabBuilder] Animator parameter " + parameterName +
                        " on " + controller.name + " has type " + parameter.type + "; expected " + parameterType + ".");
                    return false;
                }
            }

            controller.AddParameter(parameterName, parameterType);
            return true;
        }

        private static void EnsureStateTransition(
            AnimatorState source,
            AnimatorState destination,
            string parameterName,
            AnimatorConditionMode condition,
            float duration)
        {
            if (source == null || destination == null) return;
            foreach (AnimatorStateTransition existing in source.transitions)
            {
                if (existing.destinationState == destination && HasTransitionCondition(existing, parameterName, condition)) return;
            }

            AnimatorStateTransition transition = source.AddTransition(destination);
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.AddCondition(condition, 0f, parameterName);
        }

        private static void RemoveStateTransitionsWithConditionExcept(
            AnimatorState source,
            string parameterName,
            AnimatorConditionMode condition,
            AnimatorState destination)
        {
            if (source == null) return;

            bool keptExpectedTransition = false;
            foreach (AnimatorStateTransition existing in source.transitions)
            {
                if (!HasTransitionCondition(existing, parameterName, condition)) continue;
                if (existing.destinationState == destination && !keptExpectedTransition)
                {
                    keptExpectedTransition = true;
                    continue;
                }
                source.RemoveTransition(existing);
            }
        }

        private static void EnsureAnyStateTriggerTransition(
            AnimatorStateMachine stateMachine,
            AnimatorState destination,
            string parameterName)
        {
            if (stateMachine == null || destination == null) return;
            foreach (AnimatorStateTransition existing in stateMachine.anyStateTransitions)
            {
                if (existing.destinationState == destination && HasTransitionCondition(existing, parameterName, AnimatorConditionMode.If)) return;
            }

            AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(destination);
            transition.hasExitTime = false;
            transition.duration = 0.05f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, parameterName);
        }

        private static void EnsureExitTransition(AnimatorState source, AnimatorState destination)
        {
            if (source == null || destination == null) return;
            foreach (AnimatorStateTransition existing in source.transitions)
            {
                if (existing.destinationState == destination && existing.hasExitTime) return;
            }

            AnimatorStateTransition transition = source.AddTransition(destination);
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.duration = 0.05f;
        }

        private static void EnsureExitTransitionWithCondition(
            AnimatorState source,
            AnimatorState destination,
            string parameterName,
            AnimatorConditionMode condition)
        {
            if (source == null || destination == null) return;
            foreach (AnimatorStateTransition existing in source.transitions)
            {
                if (existing.destinationState == destination && existing.hasExitTime &&
                    HasTransitionCondition(existing, parameterName, condition))
                {
                    return;
                }
            }

            AnimatorStateTransition transition = source.AddTransition(destination);
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.duration = 0.05f;
            transition.AddCondition(condition, 0f, parameterName);
        }

        private static bool HasTransitionCondition(
            AnimatorStateTransition transition,
            string parameterName,
            AnimatorConditionMode condition)
        {
            foreach (AnimatorCondition existing in transition.conditions)
            {
                if (string.Equals(existing.parameter, parameterName, StringComparison.Ordinal) && existing.mode == condition)
                {
                    return true;
                }
            }

            return false;
        }

        private static GameObject FindModelAsset(string unitName)
        {
            string riggedModelPath = ModelFolder + "/" + unitName + ".fbx";
            GameObject riggedModel = AssetDatabase.LoadAssetAtPath<GameObject>(riggedModelPath);

            string lodPath = ModelFolder + "/" + unitName + "_LODs.fbx";
            GameObject lodAsset = AssetDatabase.LoadAssetAtPath<GameObject>(lodPath);
            bool hasUsableLodAsset = false;
            if (lodAsset != null)
            {
                Renderer[] lod0Renderers;
                Renderer[] lod1Renderers;
                Renderer[] lod2Renderers;
                string failureReason;
                hasUsableLodAsset = TryGetLodRendererGroups(
                    lodAsset, out lod0Renderers, out lod1Renderers, out lod2Renderers, out failureReason);
                if (!hasUsableLodAsset)
                {
                    Debug.LogWarning("[VehiclePrefabBuilder] LOD asset " + lodPath + " is invalid: " +
                        failureReason + ". Falling back to a single-mesh source.");
                }
            }

            // Preserve primary-FBX animation when it exists. The standalone LOD export is
            // mesh-only, so it cannot replace that animated source without losing its rig.
            if (HasEmbeddedAnimationClips(riggedModel))
            {
                ConfigureAnimationClipLooping(riggedModelPath);
                riggedModel = AssetDatabase.LoadAssetAtPath<GameObject>(riggedModelPath);
                Debug.Log("[VehiclePrefabBuilder] Using animated primary FBX for " + unitName +
                    " so imported clips remain available to its Animator.");
                return riggedModel;
            }

            if (hasUsableLodAsset)
            {
                return lodAsset;
            }

            // Prefer the atlas FBX and then the single-mesh source when no usable LOD chain exists.
            string[] candidatePaths = new[]
            {
                ModelFolder + "/" + unitName + "_Atlas.fbx",
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
            if (guids != null)
            {
                foreach (string guid in guids)
                {
                    string foundPath = AssetDatabase.GUIDToAssetPath(guid);
                    string foundName = Path.GetFileNameWithoutExtension(foundPath);
                    bool isSingleMeshSource = string.Equals(foundName, unitName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(foundName, unitName + "_Atlas", StringComparison.OrdinalIgnoreCase);
                    if (!isSingleMeshSource) continue;

                    GameObject foundAsset = AssetDatabase.LoadAssetAtPath<GameObject>(foundPath);
                    if (foundAsset != null) return foundAsset;
                }
            }

            return null;
        }

        private static bool HasEmbeddedAnimationClips(GameObject modelAsset)
        {
            if (modelAsset == null) return false;
            string modelPath = AssetDatabase.GetAssetPath(modelAsset);
            if (string.IsNullOrEmpty(modelPath)) return false;

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
            if (assets == null) return false;
            foreach (UnityEngine.Object asset in assets)
            {
                AnimationClip clip = asset as AnimationClip;
                if (clip != null && !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        public static void ConfigureAnimationClipLooping(string modelPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) return;

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }
            if (clips == null || clips.Length == 0) return;

            bool changed = false;
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
            }

            if (!changed) return;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static Texture2D LoadConfiguredAtlasTexture(
            string assetPath,
            TextureImporterType textureType,
            bool sRgbTexture,
            bool preserveInputAlpha)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                return null;
            }

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidDataException(
                    "Unity did not create a TextureImporter for atlas texture " + assetPath + ".");
            }

            bool changed = false;
            if (importer.textureType != textureType)
            {
                importer.textureType = textureType;
                changed = true;
            }

            if (importer.sRGBTexture != sRgbTexture)
            {
                importer.sRGBTexture = sRgbTexture;
                changed = true;
            }

            if (preserveInputAlpha)
            {
                if (importer.alphaSource != TextureImporterAlphaSource.FromInput)
                {
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    changed = true;
                }

                if (importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = false;
                    changed = true;
                }
            }

            if (changed)
            {
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }

            if (texture == null)
            {
                throw new InvalidDataException(
                    "Unity could not reload atlas texture after applying import settings: " + assetPath + ".");
            }

            return texture;
        }

        private static Material SetupAtlasTexturesAndMaterial(string unitName)
        {
            string albedoPath = TextureFolder + "/" + unitName + "_Albedo.png";
            string metallicPath = TextureFolder + "/" + unitName + "_MetallicGloss.png";
            string normalPath = TextureFolder + "/" + unitName + "_Normal.png";
            string emissionPath = TextureFolder + "/" + unitName + "_Emission.png";

            Texture2D albedoTex = LoadConfiguredAtlasTexture(
                albedoPath, TextureImporterType.Default, true, false);
            if (albedoTex == null)
            {
                // No atlas texture found
                return null;
            }

            Texture2D metallicTex = LoadConfiguredAtlasTexture(
                metallicPath, TextureImporterType.Default, false, true);
            Texture2D normalTex = LoadConfiguredAtlasTexture(
                normalPath, TextureImporterType.NormalMap, false, false);
            Texture2D emissionTex = LoadConfiguredAtlasTexture(
                emissionPath, TextureImporterType.Default, true, false);

            // Create or load Atlas Material
            string matPath = MaterialFolder + "/" + unitName + "_Atlas.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            Shader targetShader = Shader.Find("RogueCommand/VehiclePBR_VertexWear");
            if (targetShader == null)
            {
                throw new InvalidOperationException(
                    "URP shader RogueCommand/VehiclePBR_VertexWear is unavailable for " + unitName + ". " +
                    "Resolve the Unity URP package dependency before building atlas materials.");
            }

            if (mat == null)
            {
                mat = new Material(targetShader);
                mat.name = unitName + "_Atlas";
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.shader = targetShader;
            }

            mat.mainTexture = albedoTex;
            mat.color = Color.white;
            mat.enableInstancing = true;

            if (metallicTex != null)
            {
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetTexture("_MetallicGlossMap", metallicTex);
                mat.SetFloat("_GlossMapScale", 1.0f);
                mat.SetFloat("_Metallic", 1.0f);
                mat.SetFloat("_Smoothness", 1.0f);
            }
            else
            {
                mat.DisableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetTexture("_MetallicGlossMap", null);
                mat.SetFloat("_GlossMapScale", 1.0f);
                mat.SetFloat("_Metallic", 0.0f);
                mat.SetFloat("_Smoothness", 0.5f);
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
                mat.SetTexture("_BumpMap", null);
                mat.SetFloat("_BumpScale", 1.0f);
            }

            // Shared tileable rolled-steel micro relief (RNM-blended in the shader).
            Texture2D detailTex = LoadConfiguredAtlasTexture(
                "Assets/RogueCommandExpansionSample/Textures/Detail/RCL_SteelMicroDetail_Normal.png",
                TextureImporterType.NormalMap, false, false);
            mat.SetTexture("_DetailBumpMap", detailTex);
            mat.SetFloat("_DetailBumpScale", detailTex != null ? 0.5f : 0.0f);

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
                mat.SetTexture("_EmissionMap", null);
                mat.SetColor("_EmissionColor", Color.black);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
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
