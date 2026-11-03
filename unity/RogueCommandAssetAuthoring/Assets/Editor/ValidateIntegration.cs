using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RogueCommandExpansion.Authoring
{
    public static class ValidateIntegration
    {
        [MenuItem("Rogue Command/Tactical Operations/Validate Integration")]
        public static void Run()
        {
            string[] modelPaths = new[]
            {
                "Assets/RogueCommandExpansionSample/Models/BulwarkSiegeTank_Atlas.fbx",
                "Assets/RogueCommandExpansionSample/Models/ArcLanceWalker_Atlas.fbx",
                "Assets/RogueCommandExpansionSample/Models/FieldMedicRover_Atlas.fbx",
                "Assets/RogueCommandExpansionSample/Models/SentinelRelay.fbx",
                "Assets/RogueCommandExpansionSample/Models/GravitasBreaker.fbx",
                "Assets/RogueCommandExpansionSample/Models/CommandRelay.fbx"
            };

            foreach (var modelPath in modelPaths)
            {
                var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                Debug.Log($"[Validate] Model: {modelPath} = {(modelAsset != null ? "OK" : "MISSING")}");
                if (modelAsset != null)
                {
                    var renderers = modelAsset.GetComponentsInChildren<Renderer>(true);
                    Debug.Log($"[Validate]   Renderers in FBX: {renderers.Length}");
                    foreach (var r in renderers)
                    {
                        Debug.Log($"[Validate]   Renderer '{r.name}' ({r.GetType().Name}): mats={r.sharedMaterials.Length}");
                        foreach (var m in r.sharedMaterials)
                        {
                            Debug.Log($"[Validate]     Mat: {(m != null ? m.name : "NULL")} shader={(m != null ? m.shader.name : "N/A")}");
                        }
                    }
                }
            }

            // Check prefabs
            string[] prefabPaths = new[]
            {
                "Assets/TacticalOperations/Prefabs/BulwarkSiegeTankVisual.prefab",
                "Assets/TacticalOperations/Prefabs/ArcLanceWalkerVisual.prefab",
                "Assets/TacticalOperations/Prefabs/FieldMedicRoverVisual.prefab",
                "Assets/TacticalOperations/Prefabs/SentinelRelayVisual.prefab",
                "Assets/TacticalOperations/Prefabs/GravitasBreakerVisual.prefab",
                "Assets/TacticalOperations/Prefabs/CommandRelayVisual.prefab"
            };

            foreach (var prefabPath in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Debug.Log($"[Validate] Prefab: {prefabPath} = {(prefab != null ? "OK" : "MISSING")}");
                if (prefab != null)
                {
                    var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                    Debug.Log($"[Validate]   Renderers in prefab: {renderers.Length}");
                    foreach (var r in renderers)
                    {
                        Debug.Log($"[Validate]   Renderer '{r.name}' ({r.GetType().Name}): mats={r.sharedMaterials.Length}");
                        foreach (var m in r.sharedMaterials)
                        {
                            Debug.Log($"[Validate]     Mat: {(m != null ? m.name : "NULL")}");
                        }
                    }
                }
            }

            ValidateNewPbrUnit("SentinelRelay", "Assets/TacticalOperations/Prefabs/SentinelRelayVisual.prefab");
            ValidateNewPbrUnit("GravitasBreaker", "Assets/TacticalOperations/Prefabs/GravitasBreakerVisual.prefab");
            ValidateNewPbrUnit("CommandRelay", "Assets/TacticalOperations/Prefabs/CommandRelayVisual.prefab");
        }

        private static void ValidateNewPbrUnit(string unitName, string prefabPath)
        {
            string textureRoot = "Assets/RogueCommandExpansionSample/Models/Textures/" + unitName;
            ValidateTexture(textureRoot + "_Albedo.png", TextureImporterType.Default, true, false, false);
            ValidateTexture(textureRoot + "_MetallicGloss.png", TextureImporterType.Default, false, true, false);
            ValidateTexture(textureRoot + "_Normal.png", TextureImporterType.NormalMap, false, false, true);
            ValidateTexture(textureRoot + "_Emission.png", TextureImporterType.Default, true, false, false);

            string materialPath = "Assets/TacticalOperations/Materials/" + unitName + "_PBR.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null || material.shader == null ||
                material.shader.name != "RogueCommand/VehiclePBR_VertexWear")
            {
                throw new InvalidOperationException(unitName + " material must use RogueCommand/VehiclePBR_VertexWear.");
            }
            if (!material.IsKeywordEnabled("_METALLICSPECGLOSSMAP") ||
                !material.IsKeywordEnabled("_NORMALMAP") || !material.IsKeywordEnabled("_EMISSION"))
            {
                throw new InvalidOperationException(unitName + " material is missing an expected PBR shader keyword.");
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new FileNotFoundException("Prefab not found: " + prefabPath);
            Animator[] animators = prefab.GetComponentsInChildren<Animator>(true);
            if (animators.Length != 1 || animators[0].avatar == null || !animators[0].avatar.isValid ||
                animators[0].runtimeAnimatorController == null || animators[0].applyRootMotion)
            {
                throw new InvalidOperationException(unitName + " prefab must have one valid non-root-motion Animator.");
            }

            Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);
            foreach (string socketName in new[]
                     {
                         "Socket_MuzzlePrimary", "Socket_DamageSmoke", "Socket_DamageFire",
                         "Socket_EngineAudio", "Socket_WeaponAudio", "Socket_GroundCenter",
                         "Socket_GroundContact", "Socket_TurretPivot", "Socket_RecoilPrimary"
                     })
            {
                int matches = 0;
                foreach (Transform transform in transforms)
                {
                    if (string.Equals(transform.name, socketName, StringComparison.OrdinalIgnoreCase)) matches++;
                }
                if (matches != 1) throw new InvalidOperationException(unitName + " socket " + socketName + " is not unique.");
            }

            bool hasVertexWear = false;
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) >= 0 && renderer.enabled)
                {
                    throw new InvalidOperationException(unitName + " collider mesh renderer must be disabled.");
                }
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh != null && mesh.colors32 != null && mesh.colors32.Length == mesh.vertexCount)
                {
                    hasVertexWear = true;
                    break;
                }
            }
            if (!hasVertexWear) throw new InvalidOperationException(unitName + " imported mesh has no RGBA vertex-wear attribute.");
            if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
            {
                throw new InvalidOperationException(unitName + " visual prefab contains a MonoBehaviour script.");
            }
            Debug.Log("[Validate] PBR unit OK: " + unitName + " (importers, material, Animator, sockets, vertex wear, script-free prefab)");
        }

        private static void ValidateTexture(
            string assetPath,
            TextureImporterType expectedType,
            bool expectedSrgb,
            bool requireInputAlpha,
            bool normalMap)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (importer == null || texture == null)
                throw new FileNotFoundException("Texture not found or not imported: " + assetPath);
            if (importer.textureType != expectedType || importer.sRGBTexture != expectedSrgb ||
                (requireInputAlpha && importer.alphaSource != TextureImporterAlphaSource.FromInput) ||
                (requireInputAlpha && importer.alphaIsTransparency) ||
                (normalMap && importer.textureType != TextureImporterType.NormalMap) ||
                texture.width != 2048 || texture.height != 2048)
            {
                throw new InvalidOperationException(
                    $"Texture contract failed for {assetPath}: type={importer.textureType}, sRGB={importer.sRGBTexture}, " +
                    $"alphaSource={importer.alphaSource}, alphaIsTransparency={importer.alphaIsTransparency}, " +
                    $"size={texture.width}x{texture.height}.");
            }
            Debug.Log($"[Validate] Texture OK: {assetPath} type={importer.textureType} sRGB={importer.sRGBTexture} " +
                      $"alphaSource={importer.alphaSource} alphaIsTransparency={importer.alphaIsTransparency} " +
                      $"size={texture.width}x{texture.height}");
        }
    }
}
