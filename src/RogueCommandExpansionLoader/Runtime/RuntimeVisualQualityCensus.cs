using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Agent-readable quality metrics for URP materials and procedural vertex wear.
    /// The row intentionally contains only primitive values and bounded strings.
    /// </summary>
    public sealed class VisualQualityRow
    {
        public string EntityKeyId { get; }
        public bool IsLoaderOwned { get; }
        public bool ContractPass { get; }
        public int RendererCount { get; }
        public int VisibleRendererCount { get; }
        public int MaterialCount { get; }
        public int PbrMaterialCount { get; }
        public int StandardMaterialCount { get; }
        public int UnsupportedShaderCount { get; }
        public int PbrKeywordCompliantMaterialCount { get; }
        public int GpuInstancedMaterialCount { get; }
        public int MetallicGlossMapCount { get; }
        public int NormalMapCount { get; }
        public int EmissionMapCount { get; }
        public int VertexColorSampleCount { get; }
        public float CavityAoLowFraction { get; }
        public float EdgeWearFraction { get; }
        public float ThermalSootFraction { get; }
        public float TeamMaskCoverage { get; }
        public int LodGroupCount { get; }
        public IReadOnlyList<string> QualityIssues { get; }

        public VisualQualityRow(
            string entityKeyId,
            bool isLoaderOwned,
            bool contractPass,
            int rendererCount,
            int visibleRendererCount,
            int materialCount,
            int pbrMaterialCount,
            int standardMaterialCount,
            int unsupportedShaderCount,
            int pbrKeywordCompliantMaterialCount,
            int gpuInstancedMaterialCount,
            int metallicGlossMapCount,
            int normalMapCount,
            int emissionMapCount,
            int vertexColorSampleCount,
            float cavityAoLowFraction,
            float edgeWearFraction,
            float thermalSootFraction,
            float teamMaskCoverage,
            int lodGroupCount,
            IReadOnlyList<string> qualityIssues)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            IsLoaderOwned = isLoaderOwned;
            ContractPass = contractPass;
            RendererCount = Math.Max(0, rendererCount);
            VisibleRendererCount = Math.Max(0, visibleRendererCount);
            MaterialCount = Math.Max(0, materialCount);
            PbrMaterialCount = Math.Max(0, pbrMaterialCount);
            StandardMaterialCount = Math.Max(0, standardMaterialCount);
            UnsupportedShaderCount = Math.Max(0, unsupportedShaderCount);
            PbrKeywordCompliantMaterialCount = Math.Max(0, pbrKeywordCompliantMaterialCount);
            GpuInstancedMaterialCount = Math.Max(0, gpuInstancedMaterialCount);
            MetallicGlossMapCount = Math.Max(0, metallicGlossMapCount);
            NormalMapCount = Math.Max(0, normalMapCount);
            EmissionMapCount = Math.Max(0, emissionMapCount);
            VertexColorSampleCount = Math.Max(0, vertexColorSampleCount);
            CavityAoLowFraction = cavityAoLowFraction;
            EdgeWearFraction = edgeWearFraction;
            ThermalSootFraction = thermalSootFraction;
            TeamMaskCoverage = teamMaskCoverage;
            LodGroupCount = Math.Max(0, lodGroupCount);
            QualityIssues = qualityIssues ?? new List<string>().AsReadOnly();
        }
    }

    public sealed class VisualQualityCensus
    {
        public int OmittedRowCount { get; }
        public int LoaderOwnedUnitCount { get; }
        public int PbrContractPassCount { get; }
        public int PbrContractIssueCount { get; }
        public int PbrMaterialCount { get; }
        public int VertexColorSampleCount { get; }
        public IReadOnlyList<VisualQualityRow> Rows { get; }

        public VisualQualityCensus(
            IReadOnlyList<VisualQualityRow> rows,
            int omittedRowCount,
            int loaderOwnedUnitCount,
            int pbrContractPassCount,
            int pbrContractIssueCount,
            int pbrMaterialCount,
            int vertexColorSampleCount)
        {
            Rows = rows ?? new List<VisualQualityRow>().AsReadOnly();
            OmittedRowCount = Math.Max(0, omittedRowCount);
            LoaderOwnedUnitCount = Math.Max(0, loaderOwnedUnitCount);
            PbrContractPassCount = Math.Max(0, pbrContractPassCount);
            PbrContractIssueCount = Math.Max(0, pbrContractIssueCount);
            PbrMaterialCount = Math.Max(0, pbrMaterialCount);
            VertexColorSampleCount = Math.Max(0, vertexColorSampleCount);
        }
    }

    /// <summary>
    /// Collects a bounded URP/PBR and vertex-wear quality snapshot from live units.
    /// Native units are reported but only namespaced loader-owned units receive the
    /// modded shader/map contract verdict.
    /// </summary>
    public static class RuntimeVisualQualityCensus
    {
        private const string PbrShaderName = "RogueCommand/VehiclePBR_VertexWear";
        private const string StandardShaderName = "Standard";
        private const string NativeShaderName = "Shader Graphs/EntityColorShader";

        public static VisualQualityCensus Current(int maxRows = 64)
        {
            var rows = new List<VisualQualityRow>();
            int omitted = 0;
            int loaderOwned = 0;
            int contractPass = 0;
            int contractIssues = 0;
            int pbrMaterials = 0;
            int vertexSamples = 0;
            int limit = Math.Max(1, Math.Min(maxRows, 256));

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new VisualQualityCensus(rows.AsReadOnly(), 0, 0, 0, 0, 0, 0);
                }

                var units = new List<EntityController>();
                try { if (existing.PlayerUnits() != null) units.AddRange(existing.PlayerUnits()); } catch { }
                try { if (existing.AiUnits() != null) units.AddRange(existing.AiUnits()); } catch { }

                foreach (EntityController unit in units)
                {
                    if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted)
                    {
                        continue;
                    }

                    if (rows.Count >= limit)
                    {
                        omitted++;
                        continue;
                    }

                    try
                    {
                        VisualQualityRow row = CollectRow(unit);
                        rows.Add(row);
                        if (row.IsLoaderOwned) loaderOwned++;
                        if (row.IsLoaderOwned && row.PbrMaterialCount > 0)
                        {
                            if (row.ContractPass) contractPass++;
                            else contractIssues++;
                        }
                        pbrMaterials += row.PbrMaterialCount;
                        vertexSamples += row.VertexColorSampleCount;
                    }
                    catch
                    {
                        omitted++;
                    }
                }
            }
            catch
            {
                // Fail closed: return the bounded partial snapshot.
            }

            return new VisualQualityCensus(
                rows.AsReadOnly(), omitted, loaderOwned, contractPass, contractIssues,
                pbrMaterials, vertexSamples);
        }

        private static VisualQualityRow CollectRow(EntityController unit)
        {
            string key = ResolveEntityKey(unit);
            bool isLoaderOwned = key.IndexOf(':') >= 0;
            var issues = new List<string>();
            int rendererCount = 0;
            int visibleRendererCount = 0;
            int materialCount = 0;
            int pbrCount = 0;
            int standardCount = 0;
            int unsupportedCount = 0;
            int keywordCompliantCount = 0;
            int instancedCount = 0;
            int metallicGlossCount = 0;
            int normalCount = 0;
            int emissionCount = 0;
            int samples = 0;
            int cavityLow = 0;
            int edgeWear = 0;
            int soot = 0;
            int teamMask = 0;
            int lodCount = 0;

            GameObject go = null;
            try { go = unit.gameObject; } catch { }
            if (go == null)
            {
                if (isLoaderOwned) issues.Add("missing-game-object");
                return BuildRow(key, isLoaderOwned, issues, rendererCount, visibleRendererCount,
                    materialCount, pbrCount, standardCount, unsupportedCount, keywordCompliantCount,
                    instancedCount, metallicGlossCount, normalCount, emissionCount, samples,
                    cavityLow, edgeWear, soot, teamMask, lodCount);
            }

            try
            {
                LODGroup[] lods = go.GetComponentsInChildren<LODGroup>(true);
                lodCount = lods != null ? lods.Length : 0;
            }
            catch { }

            Renderer[] renderers = null;
            try { renderers = go.GetComponentsInChildren<Renderer>(true); } catch { }
            rendererCount = renderers != null ? renderers.Length : 0;
            if (rendererCount == 0 && isLoaderOwned) issues.Add("no-renderer");

            if (renderers != null)
            {
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null) continue;
                    bool rendererEnabled = false;
                    try
                    {
                        rendererEnabled = renderer.enabled;
                        if (rendererEnabled && renderer.isVisible) visibleRendererCount++;
                    }
                    catch { }

                    // Native template renderers remain in the cloned gameplay hierarchy
                    // but are disabled when a bundle overlay replaces that subtree. They
                    // must not count as active shader/material failures for the live unit.
                    if (!rendererEnabled)
                    {
                        continue;
                    }

                    Material[] materials = null;
                    try { materials = renderer.sharedMaterials; } catch { }
                    if (materials == null) continue;
                    foreach (Material material in materials)
                    {
                        if (material == null) continue;
                        materialCount++;
                        string shaderName = string.Empty;
                        try { shaderName = material.shader != null ? material.shader.name ?? string.Empty : string.Empty; } catch { }
                        bool isPbr = string.Equals(shaderName, PbrShaderName, StringComparison.Ordinal);
                        bool isStandard = string.Equals(shaderName, StandardShaderName, StringComparison.Ordinal);
                        bool isNative = string.Equals(shaderName, NativeShaderName, StringComparison.Ordinal);
                        if (isPbr) pbrCount++;
                        if (isStandard) standardCount++;
                        if (isLoaderOwned && !IsAcceptedRuntimeShaderName(shaderName)) unsupportedCount++;

                        string[] keywords = null;
                        try { keywords = material.shaderKeywords; } catch { }
                        bool hasMetallicKeyword = Contains(keywords, "_METALLICSPECGLOSSMAP");
                        bool hasNormalKeyword = Contains(keywords, "_NORMALMAP");
                        if (isPbr && hasMetallicKeyword && hasNormalKeyword) keywordCompliantCount++;
                        if (isPbr && material.enableInstancing) instancedCount++;

                        if (GetTexture(material, "_MetallicGlossMap") != null) metallicGlossCount++;
                        if (GetTexture(material, "_BumpMap") != null || GetTexture(material, "_NormalMap") != null) normalCount++;
                        if (GetTexture(material, "_EmissionMap") != null) emissionCount++;
                    }

                    Mesh mesh = null;
                    try
                    {
                        MeshFilter filter = renderer.GetComponent<MeshFilter>();
                        if (filter != null) mesh = filter.sharedMesh;
                        if (mesh == null)
                        {
                            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                            if (skinned != null) mesh = skinned.sharedMesh;
                        }
                    }
                    catch { }
                    if (mesh == null) continue;
                    Color[] colors = null;
                    try { colors = mesh.colors; } catch { }
                    if (colors == null || colors.Length == 0) continue;
                    foreach (Color color in colors)
                    {
                        samples++;
                        if (color.r <= 0.4f) cavityLow++;
                        if (color.g >= 0.7f) edgeWear++;
                        if (color.b >= 0.5f) soot++;
                        if (color.a >= 0.5f) teamMask++;
                    }
                }
            }

            if (isLoaderOwned && unsupportedCount > 0)
            {
                issues.Add("unsupported-shader");
            }

            if (isLoaderOwned && pbrCount > 0)
            {
                if (keywordCompliantCount < pbrCount) issues.Add("pbr-keywords-incomplete");
                if (metallicGlossCount < pbrCount) issues.Add("missing-metallic-gloss-map");
                if (normalCount < pbrCount) issues.Add("missing-normal-map");
                if (samples == 0) issues.Add("missing-vertex-wear-colors");
            }

            bool pass = !isLoaderOwned || pbrCount == 0 || issues.Count == 0;
            return BuildRow(key, isLoaderOwned, issues, rendererCount, visibleRendererCount,
                materialCount, pbrCount, standardCount, unsupportedCount, keywordCompliantCount,
                instancedCount, metallicGlossCount, normalCount, emissionCount, samples,
                cavityLow, edgeWear, soot, teamMask, lodCount, pass);
        }

        private static VisualQualityRow BuildRow(
            string key,
            bool loaderOwned,
            List<string> issues,
            int rendererCount,
            int visibleRendererCount,
            int materialCount,
            int pbrCount,
            int standardCount,
            int unsupportedCount,
            int keywordCompliantCount,
            int instancedCount,
            int metallicGlossCount,
            int normalCount,
            int emissionCount,
            int samples,
            int cavityLow,
            int edgeWear,
            int soot,
            int teamMask,
            int lodCount,
            bool? passOverride = null)
        {
            float denominator = samples > 0 ? samples : 1f;
            bool pass = passOverride ?? (!loaderOwned || pbrCount == 0 || issues.Count == 0);
            var boundedIssues = issues.Count <= 8 ? issues : issues.GetRange(0, 8);
            return new VisualQualityRow(
                key,
                loaderOwned,
                pass,
                rendererCount,
                visibleRendererCount,
                materialCount,
                pbrCount,
                standardCount,
                unsupportedCount,
                keywordCompliantCount,
                instancedCount,
                metallicGlossCount,
                normalCount,
                emissionCount,
                samples,
                cavityLow / denominator,
                edgeWear / denominator,
                soot / denominator,
                teamMask / denominator,
                lodCount,
                boundedIssues.AsReadOnly());
        }

        private static string ResolveEntityKey(EntityController unit)
        {
            try
            {
                PropertyInfo property = unit.GetType().GetProperty("EntityId") ?? unit.GetType().GetProperty("entityId");
                if (property != null)
                {
                    string value = property.GetValue(unit, null) as string;
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
                FieldInfo field = unit.GetType().GetField("entityId") ?? unit.GetType().GetField("EntityId");
                if (field != null)
                {
                    string value = field.GetValue(unit) as string;
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
            catch { }
            try { return unit.name ?? string.Empty; } catch { return string.Empty; }
        }

        private static bool IsAcceptedRuntimeShaderName(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName) ||
                shaderName.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.IndexOf("Hidden", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return string.Equals(shaderName, PbrShaderName, StringComparison.Ordinal) ||
                   string.Equals(shaderName, StandardShaderName, StringComparison.Ordinal) ||
                   string.Equals(shaderName, NativeShaderName, StringComparison.Ordinal) ||
                   shaderName.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal) ||
                   shaderName.StartsWith("Shader Graphs/", StringComparison.Ordinal) ||
                   shaderName.StartsWith("Shapes/", StringComparison.Ordinal) ||
                   shaderName.StartsWith("Sprites/", StringComparison.Ordinal);
        }

        private static Texture GetTexture(Material material, string property)
        {
            try
            {
                if (material == null || string.IsNullOrEmpty(property) || !HasMaterialProperty(material, property))
                {
                    return null;
                }

                return material.GetTexture(property);
            }
            catch { return null; }
        }

        private static bool HasMaterialProperty(Material material, string property)
        {
            try
            {
                MethodInfo hasProperty = material.GetType().GetMethod(
                    "HasProperty",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(string) },
                    null);
                if (hasProperty == null)
                {
                    // The dependency-free test stub does not model Unity's property API.
                    return true;
                }

                object result = hasProperty.Invoke(material, new object[] { property });
                return result is bool && (bool)result;
            }
            catch
            {
                return true;
            }
        }

        private static bool Contains(string[] keywords, string value)
        {
            if (keywords == null) return false;
            for (int i = 0; i < keywords.Length; i++)
            {
                if (string.Equals(keywords[i], value, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
