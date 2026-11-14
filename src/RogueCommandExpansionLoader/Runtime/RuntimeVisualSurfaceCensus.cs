using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry of visual surface material and vertex-wear
    /// channels for living units. Decoupled from Unity types to support clean-room
    /// MCP telemetry without visual captures.
    /// </summary>
    public sealed class VisualSurfaceRow
    {
        public string EntityKeyId { get; }
        public int RendererCount { get; }
        public int MaterialCount { get; }
        public int VertexCount { get; }
        public bool HasVertexColors { get; }
        public float AverageCavityAo { get; }
        public float AverageEdgeWear { get; }
        public float AverageThermalSoot { get; }
        public float AverageTeamMask { get; }
        public IReadOnlyList<string> ShaderNames { get; }

        public int RendererEnabledCount { get; }
        public int VertexColorSampleCount { get; }
        public float MinCavityAo { get; }
        public float MaxCavityAo { get; }
        public int MicroReliefVertexCount { get; }
        public float MinEdgeWear { get; }
        public float MaxEdgeWear { get; }
        public int WeldVertexCount { get; }
        public float MinThermalSoot { get; }
        public float MaxThermalSoot { get; }
        public int MudVertexCount { get; }
        public int TemperFringeVertexCount { get; }
        public float MinTeamMask { get; }
        public float MaxTeamMask { get; }
        public int LodCount { get; }
        public IReadOnlyList<string> ShaderKeywords { get; }
        public bool HierarchyTruncated { get; }
        public bool VertexColorSamplingBounded { get; }
        public bool NameSamplingBounded { get; }

        public VisualSurfaceRow(
            string entityKeyId,
            int rendererCount,
            int rendererEnabledCount,
            int materialCount,
            int vertexCount,
            bool hasVertexColors,
            int vertexColorSampleCount,
            float averageCavityAo,
            float minCavityAo,
            float maxCavityAo,
            int microReliefVertexCount,
            float averageEdgeWear,
            float minEdgeWear,
            float maxEdgeWear,
            int weldVertexCount,
            float averageThermalSoot,
            float minThermalSoot,
            float maxThermalSoot,
            int mudVertexCount,
            float averageTeamMask,
            float minTeamMask,
            float maxTeamMask,
            int lodCount,
            IReadOnlyList<string> shaderNames,
            IReadOnlyList<string> shaderKeywords,
            int temperFringeVertexCount = 0,
            bool hierarchyTruncated = false,
            bool vertexColorSamplingBounded = false,
            bool nameSamplingBounded = false)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            RendererCount = rendererCount < 0 ? 0 : rendererCount;
            RendererEnabledCount = rendererEnabledCount < 0 ? 0 : rendererEnabledCount;
            MaterialCount = materialCount < 0 ? 0 : materialCount;
            VertexCount = vertexCount < 0 ? 0 : vertexCount;
            HasVertexColors = hasVertexColors;
            VertexColorSampleCount = vertexColorSampleCount < 0 ? 0 : vertexColorSampleCount;

            AverageCavityAo = averageCavityAo;
            MinCavityAo = minCavityAo;
            MaxCavityAo = maxCavityAo;
            MicroReliefVertexCount = microReliefVertexCount < 0 ? 0 : microReliefVertexCount;

            AverageEdgeWear = averageEdgeWear;
            MinEdgeWear = minEdgeWear;
            MaxEdgeWear = maxEdgeWear;
            WeldVertexCount = weldVertexCount < 0 ? 0 : weldVertexCount;

            AverageThermalSoot = averageThermalSoot;
            MinThermalSoot = minThermalSoot;
            MaxThermalSoot = maxThermalSoot;
            MudVertexCount = mudVertexCount < 0 ? 0 : mudVertexCount;
            TemperFringeVertexCount = temperFringeVertexCount < 0 ? 0 : temperFringeVertexCount;

            AverageTeamMask = averageTeamMask;
            MinTeamMask = minTeamMask;
            MaxTeamMask = maxTeamMask;

            LodCount = lodCount < 0 ? 0 : lodCount;
            ShaderNames = shaderNames ?? new List<string>().AsReadOnly();
            ShaderKeywords = shaderKeywords ?? new List<string>().AsReadOnly();
            HierarchyTruncated = hierarchyTruncated;
            VertexColorSamplingBounded = vertexColorSamplingBounded;
            NameSamplingBounded = nameSamplingBounded;
        }
    }

    public sealed class VisualSurfaceCensus
    {
        public IReadOnlyList<VisualSurfaceRow> Rows { get; }
        public int OmittedRowCount { get; }

        public VisualSurfaceCensus(IReadOnlyList<VisualSurfaceRow> rows, int omittedRowCount)
        {
            Rows = rows ?? new List<VisualSurfaceRow>().AsReadOnly();
            OmittedRowCount = omittedRowCount < 0 ? 0 : omittedRowCount;
        }
    }

    /// <summary>
    /// Diagnostic collector that inspects living units' renderers, materials and
    /// vertex-wear color channels via the verified ExistingControllers registry.
    /// </summary>
    public static class RuntimeVisualSurfaceCensus
    {
        public static VisualSurfaceCensus Current(int maxRows = 64)
        {
            var rows = new List<VisualSurfaceRow>();
            int omitted = 0;

            try
            {
                List<EntityController> units = RuntimeVisualCensusSupport.CollectLivingUnits(maxRows, out omitted);
                for (int i = 0; i < units.Count; i++)
                {
                    try
                    {
                        rows.Add(CollectRow(units[i]));
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

            return new VisualSurfaceCensus(rows.AsReadOnly(), omitted);
        }

        private static VisualSurfaceRow CollectRow(EntityController unit)
        {
            string key = RuntimeVisualCensusSupport.ResolveEntityKey(unit);
            bool loaderOwned = RuntimeVisualCensusSupport.IsLoaderOwned(unit);

            GameObject go = null;
            try { go = unit.gameObject; } catch { go = null; }

            int rendererCount = 0;
            int rendererEnabledCount = 0;
            int materialCount = 0;
            int vertexCount = 0;
            bool hasColors = false;
            float sumR = 0f, sumG = 0f, sumB = 0f, sumA = 0f;
            int colorSamples = 0;
            float minR = 1f, maxR = 0f;
            float minG = 1f, maxG = 0f;
            float minB = 1f, maxB = 0f;
            float minA = 1f, maxA = 0f;
            bool hasSample = false;
            int weldCount = 0;
            int mudCount = 0;
            int temperFringeCount = 0;
            int microReliefCount = 0;
            var shaderNames = new List<string>();
            var shaderKeywords = new List<string>();
            var seenShaders = new HashSet<string>(StringComparer.Ordinal);
            var seenKeywords = new HashSet<string>(StringComparer.Ordinal);
            int lodCount = 0;
            bool hierarchyTruncated = false;
            bool vertexColorSamplingBounded = false;
            bool nameSamplingBounded = false;
            bool materialSamplingBounded = false;

            if (go != null)
            {
                VisualHierarchySnapshot snapshot = RuntimeVisualCensusSupport.CaptureHierarchy(go);
                hierarchyTruncated = snapshot.HierarchyTruncated;
                rendererCount = snapshot.Renderers.Count;
                lodCount = snapshot.LodGroups.Count;
                int inspectedMaterialSlots = 0;
                int inspectedMeshes = 0;

                for (int rendererIndex = 0; rendererIndex < snapshot.Renderers.Count; rendererIndex++)
                {
                    Renderer renderer = snapshot.Renderers[rendererIndex];
                    if (renderer == null) continue;
                    bool rendererEnabled = false;
                    try { rendererEnabled = renderer.enabled; } catch { }
                    if (rendererEnabled) rendererEnabledCount++;
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
                            RuntimeVisualCensusSupport.AddUniqueBounded(
                                shaderNames,
                                seenShaders,
                                RuntimeVisualCensusSupport.ResolveShaderName(material),
                                RuntimeVisualCensusSupport.MaxDiagnosticNamesPerUnit,
                                ref nameSamplingBounded);

                            string[] keywords = null;
                            try { keywords = material.shaderKeywords; } catch { }
                            if (keywords != null)
                            {
                                for (int keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
                                {
                                    RuntimeVisualCensusSupport.AddUniqueBounded(
                                        shaderKeywords,
                                        seenKeywords,
                                        keywords[keywordIndex],
                                        RuntimeVisualCensusSupport.MaxDiagnosticNamesPerUnit,
                                        ref nameSamplingBounded);
                                }
                            }
                        }
                    }

                    Mesh mesh = RuntimeVisualCensusSupport.ResolveMesh(renderer);
                    if (mesh == null) continue;
                    try { vertexCount += Math.Max(0, mesh.vertexCount); } catch { }
                    if (!loaderOwned) continue;
                    if (inspectedMeshes >= RuntimeVisualCensusSupport.MaxMeshInspectionsPerUnit)
                    {
                        vertexColorSamplingBounded = true;
                        continue;
                    }
                    inspectedMeshes++;
                    if (colorSamples >= RuntimeVisualCensusSupport.MaxColorSamplesPerUnit)
                    {
                        vertexColorSamplingBounded = true;
                        continue;
                    }

                    Color[] colors = null;
                    try { colors = mesh.colors; } catch { }
                    if (colors == null || colors.Length == 0) continue;
                    hasColors = true;
                    int colorLimit = Math.Min(colors.Length,
                        RuntimeVisualCensusSupport.MaxColorSamplesPerUnit - colorSamples);
                    for (int colorIndex = 0; colorIndex < colorLimit; colorIndex++)
                    {
                        Color color = colors[colorIndex];
                        sumR += color.r;
                        sumG += color.g;
                        sumB += color.b;
                        sumA += color.a;
                        colorSamples++;

                        if (!hasSample)
                        {
                            minR = maxR = color.r;
                            minG = maxG = color.g;
                            minB = maxB = color.b;
                            minA = maxA = color.a;
                            hasSample = true;
                        }
                        else
                        {
                            if (color.r < minR) minR = color.r;
                            if (color.r > maxR) maxR = color.r;
                            if (color.g < minG) minG = color.g;
                            if (color.g > maxG) maxG = color.g;
                            if (color.b < minB) minB = color.b;
                            if (color.b > maxB) maxB = color.b;
                            if (color.a < minA) minA = color.a;
                            if (color.a > maxA) maxA = color.a;
                        }

                        if (color.g >= 0.7f) weldCount++;
                        if (color.b >= 0.5f) mudCount++;
                        if (color.b >= 0.15f && color.b <= 0.60f) temperFringeCount++;
                        if (color.r <= 0.4f) microReliefCount++;
                    }
                    if (colorLimit < colors.Length) vertexColorSamplingBounded = true;
                }

                if (snapshot.RendererListTruncated || snapshot.LodListTruncated)
                {
                    nameSamplingBounded = true;
                }
            }

            float avgR = colorSamples > 0 ? sumR / colorSamples : 0f;
            float avgG = colorSamples > 0 ? sumG / colorSamples : 0f;
            float avgB = colorSamples > 0 ? sumB / colorSamples : 0f;
            float avgA = colorSamples > 0 ? sumA / colorSamples : 0f;

            if (!hasSample)
            {
                minR = maxR = 0f;
                minG = maxG = 0f;
                minB = maxB = 0f;
                minA = maxA = 0f;
            }

            return new VisualSurfaceRow(
                key,
                rendererCount,
                rendererEnabledCount,
                materialCount,
                vertexCount,
                hasColors,
                colorSamples,
                avgR, minR, maxR, microReliefCount,
                avgG, minG, maxG, weldCount,
                avgB, minB, maxB, mudCount,
                avgA, minA, maxA,
                lodCount,
                shaderNames.AsReadOnly(),
                shaderKeywords.AsReadOnly(),
                temperFringeCount,
                hierarchyTruncated,
                vertexColorSamplingBounded,
                nameSamplingBounded || materialSamplingBounded);
        }
    }
}
