using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public float MinTeamMask { get; }
        public float MaxTeamMask { get; }
        public int LodCount { get; }
        public IReadOnlyList<string> ShaderKeywords { get; }

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
            IReadOnlyList<string> shaderKeywords)
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

            AverageTeamMask = averageTeamMask;
            MinTeamMask = minTeamMask;
            MaxTeamMask = maxTeamMask;

            LodCount = lodCount < 0 ? 0 : lodCount;
            ShaderNames = shaderNames ?? new List<string>().AsReadOnly();
            ShaderKeywords = shaderKeywords ?? new List<string>().AsReadOnly();
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
            int clampedMax = Math.Max(1, Math.Min(maxRows, 256));

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new VisualSurfaceCensus(rows.AsReadOnly(), 0);
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

                    if (rows.Count >= clampedMax)
                    {
                        omitted++;
                        continue;
                    }

                    try
                    {
                        rows.Add(CollectRow(unit));
                    }
                    catch
                    {
                        omitted++;
                    }
                }
            }
            catch
            {
                // Degrade gracefully.
            }

            return new VisualSurfaceCensus(rows.AsReadOnly(), omitted);
        }

        private static VisualSurfaceRow CollectRow(EntityController unit)
        {
            string key = string.Empty;
            try
            {
                PropertyInfo prop = unit.GetType().GetProperty("EntityId") ?? unit.GetType().GetProperty("entityId");
                if (prop != null) key = prop.GetValue(unit, null) as string ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    FieldInfo field = unit.GetType().GetField("entityId") ?? unit.GetType().GetField("EntityId");
                    if (field != null) key = field.GetValue(unit) as string ?? string.Empty;
                }
            }
            catch { }
            if (string.IsNullOrWhiteSpace(key)) key = unit != null ? unit.name : string.Empty;

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
            int microReliefCount = 0;
            var shaderNames = new List<string>();
            var shaderKeywords = new List<string>();
            var seenShaders = new HashSet<string>(StringComparer.Ordinal);
            var seenKeywords = new HashSet<string>(StringComparer.Ordinal);
            int lodCount = 0;

            if (go != null)
            {
                try
                {
                    LODGroup[] lods = go.GetComponentsInChildren<LODGroup>(true);
                    if (lods != null) lodCount = lods.Length;
                }
                catch { lodCount = 0; }

                Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                rendererCount = renderers != null ? renderers.Length : 0;

                if (renderers != null)
                {
                    foreach (Renderer renderer in renderers)
                    {
                        if (renderer == null) continue;
                        if (renderer.enabled) rendererEnabledCount++;

                        Material[] mats = renderer.sharedMaterials;
                        if (mats != null)
                        {
                            foreach (Material mat in mats)
                            {
                                if (mat == null) continue;
                                materialCount++;
                                string shaderName = string.Empty;
                                try { if (mat.shader != null) shaderName = mat.shader.name ?? string.Empty; } catch { shaderName = string.Empty; }
                                if (!string.IsNullOrWhiteSpace(shaderName) && seenShaders.Add(shaderName))
                                {
                                    shaderNames.Add(shaderName);
                                }
                                string[] keywords = null;
                                try { keywords = mat.shaderKeywords; } catch { keywords = null; }
                                if (keywords != null)
                                {
                                    foreach (string kw in keywords)
                                    {
                                        if (!string.IsNullOrWhiteSpace(kw) && seenKeywords.Add(kw))
                                        {
                                            shaderKeywords.Add(kw);
                                        }
                                    }
                                }
                            }
                        }

                        Mesh mesh = null;
                        MeshFilter filter = renderer.GetComponent<MeshFilter>();
                        if (filter != null) mesh = filter.sharedMesh;
                        if (mesh == null)
                        {
                            var skinned = renderer as SkinnedMeshRenderer;
                            if (skinned != null) mesh = skinned.sharedMesh;
                        }

                        if (mesh != null)
                        {
                            vertexCount += mesh.vertexCount;
                            Color[] colors = mesh.colors;
                            if (colors != null && colors.Length > 0)
                            {
                                hasColors = true;
                                foreach (Color c in colors)
                                {
                                    sumR += c.r;
                                    sumG += c.g;
                                    sumB += c.b;
                                    sumA += c.a;
                                    colorSamples++;

                                    if (!hasSample)
                                    {
                                        minR = maxR = c.r;
                                        minG = maxG = c.g;
                                        minB = maxB = c.b;
                                        minA = maxA = c.a;
                                        hasSample = true;
                                    }
                                    else
                                    {
                                        if (c.r < minR) minR = c.r;
                                        if (c.r > maxR) maxR = c.r;
                                        if (c.g < minG) minG = c.g;
                                        if (c.g > maxG) maxG = c.g;
                                        if (c.b < minB) minB = c.b;
                                        if (c.b > maxB) maxB = c.b;
                                        if (c.a < minA) minA = c.a;
                                        if (c.a > maxA) maxA = c.a;
                                    }

                                    if (c.g >= 0.7f) weldCount++;
                                    if (c.b >= 0.5f) mudCount++;
                                    if (c.r <= 0.4f) microReliefCount++;
                                }
                            }
                        }
                    }
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
                shaderKeywords.AsReadOnly());
        }
    }
}
