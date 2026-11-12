using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// One live unit's consolidated visual health verdict, merging quality,
    /// surface, material, and socket telemetry. Primitive DTO only.
    /// </summary>
    public sealed class VisualHealthRow
    {
        public string EntityKeyId { get; }
        public bool IsLoaderOwned { get; }
        public bool ContractPass { get; }
        public int QualityIssueCount { get; }
        public int RendererCount { get; }
        public int MaterialCount { get; }
        public int PbrMaterialCount { get; }
        public int VertexColorSampleCount { get; }
        public int SurfaceVertexCount { get; }
        public int MaterialTextureCount { get; }
        public int SocketCount { get; }
        public int AmbiguousSocketCount { get; }
        public int TotalIssueCount { get; }

        public VisualHealthRow(
            string entityKeyId,
            bool isLoaderOwned,
            bool contractPass,
            int qualityIssueCount,
            int rendererCount,
            int materialCount,
            int pbrMaterialCount,
            int vertexColorSampleCount,
            int surfaceVertexCount,
            int materialTextureCount,
            int socketCount,
            int ambiguousSocketCount)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            IsLoaderOwned = isLoaderOwned;
            ContractPass = contractPass;
            QualityIssueCount = Math.Max(0, qualityIssueCount);
            RendererCount = Math.Max(0, rendererCount);
            MaterialCount = Math.Max(0, materialCount);
            PbrMaterialCount = Math.Max(0, pbrMaterialCount);
            VertexColorSampleCount = Math.Max(0, vertexColorSampleCount);
            SurfaceVertexCount = Math.Max(0, surfaceVertexCount);
            MaterialTextureCount = Math.Max(0, materialTextureCount);
            SocketCount = Math.Max(0, socketCount);
            AmbiguousSocketCount = Math.Max(0, ambiguousSocketCount);
            TotalIssueCount = Math.Max(0, QualityIssueCount + AmbiguousSocketCount);
        }
    }

    /// <summary>
    /// Consolidated visual health snapshot. Omission counters are emitted first
    /// by the formatter so tail truncation never hides data loss.
    /// </summary>
    public sealed class VisualHealthSummary
    {
        public int OmittedUnitCount { get; }
        public int TrackedUnitCount { get; }
        public int LoaderOwnedUnitCount { get; }
        public int QualityPassCount { get; }
        public int QualityIssueCount { get; }
        public int TotalVertexColorSampleCount { get; }
        public int TotalMaterialCount { get; }
        public int TotalSocketCount { get; }
        public int AmbiguousSocketUnitCount { get; }
        public IReadOnlyList<VisualHealthRow> Rows { get; }

        public VisualHealthSummary(
            int omittedUnitCount,
            int trackedUnitCount,
            int loaderOwnedUnitCount,
            int qualityPassCount,
            int qualityIssueCount,
            int totalVertexColorSampleCount,
            int totalMaterialCount,
            int totalSocketCount,
            int ambiguousSocketUnitCount,
            IReadOnlyList<VisualHealthRow> rows)
        {
            OmittedUnitCount = Math.Max(0, omittedUnitCount);
            TrackedUnitCount = Math.Max(0, trackedUnitCount);
            LoaderOwnedUnitCount = Math.Max(0, loaderOwnedUnitCount);
            QualityPassCount = Math.Max(0, qualityPassCount);
            QualityIssueCount = Math.Max(0, qualityIssueCount);
            TotalVertexColorSampleCount = Math.Max(0, totalVertexColorSampleCount);
            TotalMaterialCount = Math.Max(0, totalMaterialCount);
            TotalSocketCount = Math.Max(0, totalSocketCount);
            AmbiguousSocketUnitCount = Math.Max(0, ambiguousSocketUnitCount);
            Rows = rows ?? new List<VisualHealthRow>().AsReadOnly();
        }

        public static VisualHealthSummary Empty { get; } =
            new VisualHealthSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, new List<VisualHealthRow>().AsReadOnly());
    }

    /// <summary>
    /// Collects a bounded consolidated visual health snapshot from living units in
    /// one renderer/material/mesh/socket hierarchy pass. It avoids invoking the
    /// four standalone visual censuses, which would repeat Unity main-thread
    /// traversal and allocations, and degrades to a partial snapshot on failure.
    /// </summary>
    public static class RuntimeVisualHealthCensus
    {
        private const string PbrShaderName = "RogueCommand/VehiclePBR_VertexWear";
        private const string StandardShaderName = "Standard";
        private const string NativeShaderName = "Shader Graphs/EntityColorShader";
        private const int MaxHierarchyNodesPerUnit = 4096;
        private const int MaxMeshInspectionsPerLoaderUnit = 32;

        /// <summary>
        /// Collects the consolidated health report in one bounded live-unit sweep.
        /// The previous implementation invoked four independent censuses, causing
        /// each hierarchy, renderer, material, and vertex-color tree to be walked
        /// repeatedly on Unity's main thread.
        /// </summary>
        public static VisualHealthSummary Current(int maxRows = 64)
        {
            int limit = Math.Max(1, Math.Min(maxRows, 256));
            var rows = new List<VisualHealthRow>(limit);
            var candidates = new List<EntityController>();
            var seenUnits = new HashSet<EntityController>();
            int omitted = 0;

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return VisualHealthSummary.Empty;
                }

                try { AppendLivingUnits(existing.PlayerUnits(), candidates, seenUnits); } catch { }
                try { AppendLivingUnits(existing.AiUnits(), candidates, seenUnits); } catch { }

                int loaderOwned = 0;
                int passCount = 0;
                int issueCount = 0;
                int totalVertexSamples = 0;
                int totalMaterials = 0;
                int totalSockets = 0;
                int ambiguousUnits = 0;

                foreach (EntityController unit in candidates)
                {
                    if (rows.Count >= limit)
                    {
                        omitted++;
                        continue;
                    }

                    try
                    {
                        VisualHealthRow row = CollectRow(unit);
                        rows.Add(row);
                        if (row.IsLoaderOwned) loaderOwned++;
                        if (row.IsLoaderOwned && row.ContractPass && row.TotalIssueCount == 0)
                        {
                            passCount++;
                        }
                        else if (row.TotalIssueCount > 0)
                        {
                            issueCount++;
                        }

                        totalVertexSamples += row.VertexColorSampleCount;
                        totalMaterials += row.MaterialCount;
                        totalSockets += row.SocketCount;
                        if (row.AmbiguousSocketCount > 0) ambiguousUnits++;
                    }
                    catch
                    {
                        omitted++;
                    }
                }

                return new VisualHealthSummary(
                    omitted,
                    rows.Count,
                    loaderOwned,
                    passCount,
                    issueCount,
                    totalVertexSamples,
                    totalMaterials,
                    totalSockets,
                    ambiguousUnits,
                    rows.AsReadOnly());
            }
            catch
            {
                return new VisualHealthSummary(
                    omitted,
                    rows.Count,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    rows.AsReadOnly());
            }
        }

        private static void AppendLivingUnits(
            IEnumerable<EntityController> source,
            List<EntityController> destination,
            HashSet<EntityController> seen)
        {
            if (source == null) return;

            foreach (EntityController unit in source)
            {
                if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted) continue;
                if (seen.Add(unit)) destination.Add(unit);
            }
        }

        private static VisualHealthRow CollectRow(EntityController unit)
        {
            string key = ResolveEntityKey(unit);
            bool isLoaderOwned = key.IndexOf(':') >= 0;
            var qualityIssues = new List<string>();
            var socketCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            int rendererCount = 0;
            int materialCount = 0;
            int pbrCount = 0;
            int vertexSamples = 0;
            int surfaceVertices = 0;
            int materialTextures = 0;
            int socketCount = 0;
            int ambiguousSocketCount = 0;
            int keywordCompliantCount = 0;
            int metallicGlossCount = 0;
            int normalMapCount = 0;
            int unsupportedShaderCount = 0;
            int inspectedMeshes = 0;
            bool meshSamplingTruncated = false;
            bool hierarchyTruncated = false;

            GameObject go = null;
            try { go = unit.gameObject; } catch { }
            if (go == null)
            {
                if (isLoaderOwned) qualityIssues.Add("missing-game-object");
                return BuildRow(key, isLoaderOwned, qualityIssues, rendererCount, materialCount,
                    pbrCount, vertexSamples, surfaceVertices, materialTextures, socketCount,
                    ambiguousSocketCount, false);
            }

            Transform root = null;
            try { root = go.transform; } catch { }
            if (root == null)
            {
                if (isLoaderOwned) qualityIssues.Add("missing-transform");
                return BuildRow(key, isLoaderOwned, qualityIssues, rendererCount, materialCount,
                    pbrCount, vertexSamples, surfaceVertices, materialTextures, socketCount,
                    ambiguousSocketCount, false);
            }

            Renderer[] renderers = null;
            try { renderers = go.GetComponentsInChildren<Renderer>(true); } catch { }
            if (renderers != null)
            {
                if (renderers.Length > MaxHierarchyNodesPerUnit) hierarchyTruncated = true;
                int rendererLimit = Math.Min(renderers.Length, MaxHierarchyNodesPerUnit);
                for (int i = 0; i < rendererLimit; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null) continue;
                    rendererCount++;

                    bool enabled = false;
                    try { enabled = renderer.enabled; } catch { }
                    if (enabled)
                    {
                        Material[] materials = null;
                        try { materials = renderer.sharedMaterials; } catch { }
                        if (materials != null)
                        {
                            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                            {
                                Material material = materials[materialIndex];
                                if (material == null) continue;
                                materialCount++;
                                InspectMaterial(
                                    material,
                                    isLoaderOwned,
                                    ref pbrCount,
                                    ref keywordCompliantCount,
                                    ref metallicGlossCount,
                                    ref normalMapCount,
                                    ref materialTextures,
                                    ref unsupportedShaderCount);
                            }
                        }
                    }

                    if (!isLoaderOwned)
                    {
                        continue;
                    }
                    if (inspectedMeshes >= MaxMeshInspectionsPerLoaderUnit)
                    {
                        meshSamplingTruncated = true;
                        continue;
                    }
                    inspectedMeshes++;

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

                    if (mesh != null)
                    {
                        surfaceVertices += Math.Max(0, mesh.vertexCount);
                        if (enabled)
                        {
                            Color[] colors = null;
                            try { colors = mesh.colors; } catch { }
                            if (colors != null) vertexSamples += colors.Length;
                        }
                    }
                }
            }

            Transform[] transforms = null;
            try { transforms = go.GetComponentsInChildren<Transform>(true); } catch { }
            if (transforms != null)
            {
                if (transforms.Length > MaxHierarchyNodesPerUnit) hierarchyTruncated = true;
                int transformLimit = Math.Min(transforms.Length, MaxHierarchyNodesPerUnit);
                for (int i = 0; i < transformLimit; i++)
                {
                    Transform transform = transforms[i];
                    if (transform == null) continue;
                    string transformName = string.Empty;
                    try { transformName = transform.name ?? string.Empty; } catch { }
                    if (!transformName.StartsWith("Socket_", StringComparison.Ordinal)) continue;
                    socketCount++;
                    socketCounts[transformName] = socketCounts.TryGetValue(transformName, out int count)
                        ? count + 1
                        : 1;
                }
            }

            foreach (KeyValuePair<string, int> pair in socketCounts)
            {
                if (pair.Value > 1) ambiguousSocketCount += pair.Value - 1;
            }

            if (isLoaderOwned && rendererCount == 0) qualityIssues.Add("no-renderer");
            if (isLoaderOwned && unsupportedShaderCount > 0) qualityIssues.Add("unsupported-shader");
            if (isLoaderOwned && pbrCount > 0)
            {
                if (keywordCompliantCount < pbrCount) qualityIssues.Add("pbr-keywords-incomplete");
                if (metallicGlossCount < pbrCount) qualityIssues.Add("missing-metallic-gloss-map");
                if (normalMapCount < pbrCount) qualityIssues.Add("missing-normal-map");
                if (vertexSamples == 0) qualityIssues.Add("missing-vertex-wear-colors");
            }
            if (isLoaderOwned && hierarchyTruncated) qualityIssues.Add("hierarchy-truncated");
            if (isLoaderOwned && pbrCount > 0 && meshSamplingTruncated)
            {
                qualityIssues.Add("geometry-sampling-bounded");
            }

            return BuildRow(
                key,
                isLoaderOwned,
                qualityIssues,
                rendererCount,
                materialCount,
                pbrCount,
                vertexSamples,
                surfaceVertices,
                materialTextures,
                socketCount,
                ambiguousSocketCount,
                isLoaderOwned && pbrCount > 0 && qualityIssues.Count == 0);
        }

        private static void InspectMaterial(
            Material material,
            bool isLoaderOwned,
            ref int pbrCount,
            ref int keywordCompliantCount,
            ref int metallicGlossCount,
            ref int normalMapCount,
            ref int materialTextures,
            ref int unsupportedShaderCount)
        {
            string shaderName = string.Empty;
            try { shaderName = material.shader != null ? material.shader.name ?? string.Empty : string.Empty; } catch { }
            bool isPbr = string.Equals(shaderName, PbrShaderName, StringComparison.Ordinal);
            if (isPbr) pbrCount++;
            if (isLoaderOwned && !IsAcceptedRuntimeShaderName(shaderName)) unsupportedShaderCount++;

            string[] keywords = null;
            try { keywords = material.shaderKeywords; } catch { }
            if (isPbr && Contains(keywords, "_METALLICSPECGLOSSMAP") && Contains(keywords, "_NORMALMAP"))
            {
                keywordCompliantCount++;
            }

            if (TryGetTexture(material, null) != null) materialTextures++;
            if (TryGetTexture(material, "_MetallicGlossMap") != null)
            {
                metallicGlossCount++;
                materialTextures++;
            }
            if (TryGetTexture(material, "_BumpMap") != null || TryGetTexture(material, "_NormalMap") != null)
            {
                normalMapCount++;
                materialTextures++;
            }
            if (TryGetTexture(material, "_EmissionMap") != null)
            {
                materialTextures++;
            }
        }

        private static Texture TryGetTexture(Material material, string property)
        {
            try
            {
                if (property == null) return material.mainTexture;
                if (!material.HasProperty(property)) return null;
                return material.GetTexture(property);
            }
            catch
            {
                return null;
            }
        }

        private static VisualHealthRow BuildRow(
            string key,
            bool isLoaderOwned,
            List<string> qualityIssues,
            int rendererCount,
            int materialCount,
            int pbrCount,
            int vertexSamples,
            int surfaceVertices,
            int materialTextures,
            int socketCount,
            int ambiguousSocketCount,
            bool contractPass)
        {
            var boundedIssues = qualityIssues.Count <= 8
                ? qualityIssues
                : qualityIssues.GetRange(0, 8);
            bool pass = contractPass || !isLoaderOwned || pbrCount == 0;
            return new VisualHealthRow(
                key,
                isLoaderOwned,
                pass,
                boundedIssues.Count,
                rendererCount,
                materialCount,
                pbrCount,
                vertexSamples,
                surfaceVertices,
                materialTextures,
                socketCount,
                ambiguousSocketCount);
        }

        private static string ResolveEntityKey(EntityController unit)
        {
            try
            {
                string value = unit.EntityId;
                if (!string.IsNullOrWhiteSpace(value)) return value;
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
