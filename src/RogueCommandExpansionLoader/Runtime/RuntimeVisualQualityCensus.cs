using System;
using System.Collections.Generic;
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
        public bool HierarchyTruncated { get; }
        public bool VertexColorSamplingBounded { get; }
        public bool MaterialSamplingBounded { get; }
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
            IReadOnlyList<string> qualityIssues,
            bool hierarchyTruncated = false,
            bool vertexColorSamplingBounded = false,
            bool materialSamplingBounded = false)
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
            HierarchyTruncated = hierarchyTruncated;
            VertexColorSamplingBounded = vertexColorSamplingBounded;
            MaterialSamplingBounded = materialSamplingBounded;
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

            try
            {
                List<EntityController> units = RuntimeVisualCensusSupport.CollectLivingUnits(maxRows, out omitted);
                for (int i = 0; i < units.Count; i++)
                {
                    EntityController unit = units[i];
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
                omitted++;
            }

            return new VisualQualityCensus(
                rows.AsReadOnly(), omitted, loaderOwned, contractPass, contractIssues,
                pbrMaterials, vertexSamples);
        }

        private static VisualQualityRow CollectRow(EntityController unit)
        {
            string key = RuntimeVisualCensusSupport.ResolveEntityKey(unit);
            bool isLoaderOwned = RuntimeVisualCensusSupport.IsLoaderOwned(unit);
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
            bool materialSamplingBounded = false;
            bool vertexColorSamplingBounded = false;
            bool hierarchyTruncated = false;

            GameObject go = null;
            try { go = unit.gameObject; } catch { }
            if (go == null)
            {
                if (isLoaderOwned) issues.Add("missing-game-object");
                return BuildRow(key, isLoaderOwned, issues, rendererCount, visibleRendererCount,
                    materialCount, pbrCount, standardCount, unsupportedCount, keywordCompliantCount,
                    instancedCount, metallicGlossCount, normalCount, emissionCount, samples,
                    cavityLow, edgeWear, soot, teamMask, lodCount, false,
                    hierarchyTruncated, vertexColorSamplingBounded, materialSamplingBounded);
            }

            VisualHierarchySnapshot snapshot = RuntimeVisualCensusSupport.CaptureHierarchy(go);
            hierarchyTruncated = snapshot.HierarchyTruncated;
            rendererCount = snapshot.Renderers.Count;
            lodCount = snapshot.LodGroups.Count;
            if (rendererCount == 0 && isLoaderOwned) issues.Add("no-renderer");
            if (snapshot.HierarchyTruncated) issues.Add("hierarchy-truncated");
            if (snapshot.RendererListTruncated) issues.Add("renderer-sampling-bounded");
            if (snapshot.LodListTruncated) issues.Add("lod-sampling-bounded");

            int inspectedMaterialSlots = 0;
            int inspectedMeshes = 0;
            for (int rendererIndex = 0; rendererIndex < snapshot.Renderers.Count; rendererIndex++)
            {
                Renderer renderer = snapshot.Renderers[rendererIndex];
                if (renderer == null) continue;

                bool rendererEnabled = false;
                try
                {
                    rendererEnabled = renderer.enabled;
                    if (rendererEnabled && renderer.isVisible) visibleRendererCount++;
                }
                catch { }
                if (!rendererEnabled) continue;

                Material[] materials = null;
                try { materials = renderer.sharedMaterials; } catch { }
                if (materials != null)
                {
                    for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    {
                        if (inspectedMaterialSlots >= RuntimeVisualCensusSupport.MaxMaterialSlotsPerUnit)
                        {
                            materialSamplingBounded = true;
                            break;
                        }

                        Material material = materials[materialIndex];
                        if (material == null) continue;
                        inspectedMaterialSlots++;
                        materialCount++;
                        string shaderName = RuntimeVisualCensusSupport.ResolveShaderName(material);
                        bool isPbr = string.Equals(shaderName, PbrShaderName, StringComparison.Ordinal);
                        bool isStandard = string.Equals(shaderName, StandardShaderName, StringComparison.Ordinal);
                        if (isPbr) pbrCount++;
                        if (isStandard) standardCount++;
                        if (isLoaderOwned && !IsAcceptedRuntimeShaderName(shaderName)) unsupportedCount++;

                        string[] keywords = null;
                        try { keywords = material.shaderKeywords; } catch { }
                        bool hasMetallicKeyword = Contains(keywords, "_METALLICSPECGLOSSMAP");
                        bool hasNormalKeyword = Contains(keywords, "_NORMALMAP");
                        if (isPbr && hasMetallicKeyword && hasNormalKeyword) keywordCompliantCount++;
                        if (isPbr && material.enableInstancing) instancedCount++;

                        if (RuntimeVisualCensusSupport.GetTexture(material, "_MetallicGlossMap") != null) metallicGlossCount++;
                        if (RuntimeVisualCensusSupport.GetTexture(material, "_BumpMap") != null ||
                            RuntimeVisualCensusSupport.GetTexture(material, "_NormalMap") != null) normalCount++;
                        if (RuntimeVisualCensusSupport.GetTexture(material, "_EmissionMap") != null) emissionCount++;
                    }
                }

                // Vertex-wear is a loader-owned contract. Native geometry keeps its
                // renderer/material telemetry but is not forced through a full mesh
                // color walk on every standalone diagnostic request.
                Mesh mesh = RuntimeVisualCensusSupport.ResolveMesh(renderer);
                if (mesh == null) continue;
                if (!isLoaderOwned) continue;
                if (inspectedMeshes >= RuntimeVisualCensusSupport.MaxMeshInspectionsPerUnit)
                {
                    vertexColorSamplingBounded = true;
                    continue;
                }
                inspectedMeshes++;
                if (samples >= RuntimeVisualCensusSupport.MaxColorSamplesPerUnit)
                {
                    vertexColorSamplingBounded = true;
                    continue;
                }
                Color[] colors = null;
                try { colors = mesh.colors; } catch { }
                if (colors == null || colors.Length == 0) continue;

                int colorLimit = Math.Min(colors.Length,
                    RuntimeVisualCensusSupport.MaxColorSamplesPerUnit - samples);
                for (int colorIndex = 0; colorIndex < colorLimit; colorIndex++)
                {
                    Color color = colors[colorIndex];
                    samples++;
                    if (color.r <= 0.4f) cavityLow++;
                    if (color.g >= 0.7f) edgeWear++;
                    if (color.b >= 0.5f) soot++;
                    if (color.a >= 0.5f) teamMask++;
                }
                if (colorLimit < colors.Length) vertexColorSamplingBounded = true;
            }

            if (materialSamplingBounded) issues.Add("material-sampling-bounded");
            if (vertexColorSamplingBounded) issues.Add("vertex-color-sampling-bounded");
            if (isLoaderOwned && unsupportedCount > 0) issues.Add("unsupported-shader");

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
                cavityLow, edgeWear, soot, teamMask, lodCount, pass,
                hierarchyTruncated, vertexColorSamplingBounded, materialSamplingBounded);
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
            bool? passOverride = null,
            bool hierarchyTruncated = false,
            bool vertexColorSamplingBounded = false,
            bool materialSamplingBounded = false)
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
                boundedIssues.AsReadOnly(),
                hierarchyTruncated,
                vertexColorSamplingBounded,
                materialSamplingBounded);
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
