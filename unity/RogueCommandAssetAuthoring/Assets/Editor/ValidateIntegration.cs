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
                "Assets/RogueCommandExpansionSample/Models/FieldMedicRover_Atlas.fbx"
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
                "Assets/TacticalOperations/Prefabs/FieldMedicRoverVisual.prefab"
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
        }
    }
}
