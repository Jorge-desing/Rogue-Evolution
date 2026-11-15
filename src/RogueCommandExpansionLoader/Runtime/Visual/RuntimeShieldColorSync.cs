using System;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime.Visual
{
    /// <summary>
    /// Ensures native entity shield visuals (<c>EntityController.shieldGameObject</c>) match the owning faction
    /// (<c>Player</c> blue vs <c>AI</c> red), even when a unit clones an opposite-faction native template
    /// such as <c>LightningTank</c>.
    /// </summary>
    public static class RuntimeShieldColorSync
    {
        public const string PlayerShieldShaderName = "Shader Graphs/EntityShieldShader2";
        public const string AiShieldShaderName = "Shader Graphs/EntityShieldShader";

        private static FieldInfo _shieldGameObjectField;
        private static bool _shieldGameObjectFieldLookedUp;
        private static MethodInfo _prefabLocationMethod;
        private static bool _prefabLocationMethodLookedUp;

        private static Material _cachedPlayerShieldMaterial;
        private static Material _cachedAiShieldMaterial;

        public static void ResetCacheForTests()
        {
            _cachedPlayerShieldMaterial = null;
            _cachedAiShieldMaterial = null;
        }

        public static bool ShouldReplaceShieldShader(
            bool isPlayer,
            bool isAi,
            string currentShaderName,
            out string targetShaderName)
        {
            targetShaderName = null;
            if ((!isPlayer && !isAi) || string.IsNullOrEmpty(currentShaderName))
            {
                return false;
            }

            if (!string.Equals(currentShaderName, PlayerShieldShaderName, StringComparison.Ordinal) &&
                !string.Equals(currentShaderName, AiShieldShaderName, StringComparison.Ordinal))
            {
                return false;
            }

            targetShaderName = isPlayer ? PlayerShieldShaderName : AiShieldShaderName;
            return !string.Equals(currentShaderName, targetShaderName, StringComparison.Ordinal);
        }

        public static void ApplyIfApplicable(object entityObj)
        {
            EntityController entity = ResolveEntityController(entityObj);
            if (entity != null)
            {
                SyncShieldVisuals(entity);
            }
        }

        public static bool SyncShieldVisuals(EntityController entity)
        {
            if (entity == null)
            {
                return false;
            }

            bool isPlayer = entity.IsControlledByPlayer;
            bool isAi = entity.IsControlledByAi;
            if (!isPlayer && !isAi)
            {
                return false;
            }

            GameObject shieldGo = GetShieldGameObject(entity);
            if (shieldGo == null)
            {
                return false;
            }

            Renderer[] renderers = shieldGo.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }

            bool updatedAny = false;
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    continue;
                }

                bool rendererChanged = false;
                Material[] updatedMaterials = null;
                for (int m = 0; m < materials.Length; m++)
                {
                    Material mat = materials[m];
                    string shaderName = mat != null && mat.shader != null ? mat.shader.name : null;
                    string targetShaderName;
                    if (!ShouldReplaceShieldShader(isPlayer, isAi, shaderName, out targetShaderName))
                    {
                        continue;
                    }

                    Material factionMaterial = ResolveFactionShieldMaterial(isPlayer);
                    if (factionMaterial == null)
                    {
                        continue;
                    }

                    if (updatedMaterials == null)
                    {
                        updatedMaterials = new Material[materials.Length];
                        Array.Copy(materials, updatedMaterials, materials.Length);
                    }

                    updatedMaterials[m] = factionMaterial;
                    rendererChanged = true;
                }

                if (rendererChanged)
                {
                    renderer.sharedMaterials = updatedMaterials;
                    updatedAny = true;
                }
            }

            return updatedAny;
        }

        public static Material ResolveFactionShieldMaterial(bool isPlayer)
        {
            Material cached = isPlayer ? _cachedPlayerShieldMaterial : _cachedAiShieldMaterial;
            if (cached != null)
            {
                return cached;
            }

            string targetShaderName = isPlayer ? PlayerShieldShaderName : AiShieldShaderName;
            Material found = FindLoadedMaterialByShaderName(targetShaderName);
            if (found == null)
            {
                found = LoadMaterialFromReferenceTemplate(
                    isPlayer ? "LightningTank" : "PCXBaseTank",
                    targetShaderName);
            }

            if (found != null)
            {
                if (isPlayer)
                {
                    _cachedPlayerShieldMaterial = found;
                }
                else
                {
                    _cachedAiShieldMaterial = found;
                }
            }

            return found;
        }

        private static Material FindLoadedMaterialByShaderName(string shaderName)
        {
            UnityEngine.Object[] allMaterials = Resources.FindObjectsOfTypeAll(typeof(Material));
            if (allMaterials == null)
            {
                return null;
            }

            Material fallbackInstance = null;
            for (int i = 0; i < allMaterials.Length; i++)
            {
                Material mat = allMaterials[i] as Material;
                if (mat == null || mat.shader == null ||
                    !string.Equals(mat.shader.name, shaderName, StringComparison.Ordinal))
                {
                    continue;
                }

                string name = mat.name ?? string.Empty;
                if (name.IndexOf("(Instance)", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return mat;
                }

                if (fallbackInstance == null)
                {
                    fallbackInstance = mat;
                }
            }

            return fallbackInstance;
        }

        private static Material LoadMaterialFromReferenceTemplate(string nativeTemplateId, string shaderName)
        {
            try
            {
                string prefabLocation = TryGetPrefabLocation(nativeTemplateId);
                if (string.IsNullOrEmpty(prefabLocation))
                {
                    return null;
                }

                GameObject prefab = Resources.Load(prefabLocation, typeof(GameObject)) as GameObject;
                if (prefab == null)
                {
                    return null;
                }

                Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
                if (renderers == null)
                {
                    return null;
                }

                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer renderer = renderers[r];
                    if (renderer == null)
                    {
                        continue;
                    }

                    Material[] mats = renderer.sharedMaterials;
                    if (mats == null)
                    {
                        continue;
                    }

                    for (int m = 0; m < mats.Length; m++)
                    {
                        Material mat = mats[m];
                        if (mat != null && mat.shader != null &&
                            string.Equals(mat.shader.name, shaderName, StringComparison.Ordinal))
                        {
                            return mat;
                        }
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static string TryGetPrefabLocation(string nativeTemplateId)
        {
            if (!_prefabLocationMethodLookedUp)
            {
                _prefabLocationMethodLookedUp = true;
                _prefabLocationMethod = typeof(EntityBalancingStore).GetMethod(
                    "PrefabLocation",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);
            }

            if (_prefabLocationMethod == null)
            {
                return null;
            }

            return _prefabLocationMethod.Invoke(null, new object[] { nativeTemplateId }) as string;
        }

        private static EntityController ResolveEntityController(object entityObj)
        {
            EntityController direct = entityObj as EntityController;
            if (direct != null)
            {
                return direct;
            }

            GameObject go = entityObj as GameObject;
            if (go != null)
            {
                return go.GetComponent<EntityController>();
            }

            Component comp = entityObj as Component;
            return comp != null ? comp.GetComponent<EntityController>() : null;
        }

        private static GameObject GetShieldGameObject(EntityController entity)
        {
            if (!_shieldGameObjectFieldLookedUp)
            {
                _shieldGameObjectFieldLookedUp = true;
                _shieldGameObjectField = typeof(EntityController).GetField(
                    "shieldGameObject",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }

            if (_shieldGameObjectField == null)
            {
                return null;
            }

            return _shieldGameObjectField.GetValue(entity) as GameObject;
        }
    }
}
