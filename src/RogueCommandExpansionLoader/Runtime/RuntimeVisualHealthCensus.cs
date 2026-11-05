using System;
using System.Collections.Generic;

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
    /// Collects a bounded consolidated visual health snapshot from live units by
    /// merging the already-verified visual quality, surface, material, and
    /// socket censuses. Uses no new reflection beyond those collectors and
    /// degrades to <see cref="VisualHealthSummary.Empty"/> on failure.
    /// </summary>
    public static class RuntimeVisualHealthCensus
    {
        public static VisualHealthSummary Current(int maxRows = 64)
        {
            int limit = Math.Max(1, Math.Min(maxRows, 256));
            try
            {
                VisualQualityCensus quality = null;
                VisualSurfaceCensus surface = null;
                VisualMaterialCensus material = null;
                SocketAuditCensus socket = null;

                try { quality = RuntimeVisualQualityCensus.Current(limit); } catch { quality = null; }
                try { surface = RuntimeVisualSurfaceCensus.Current(limit); } catch { surface = null; }
                try { material = RuntimeVisualMaterialCensus.Current(limit); } catch { material = null; }
                try { socket = RuntimeSocketAuditCensus.Current(limit); } catch { socket = null; }

                var surfaceByKey = new Dictionary<string, VisualSurfaceRow>(StringComparer.Ordinal);
                if (surface != null && surface.Rows != null)
                {
                    foreach (VisualSurfaceRow row in surface.Rows)
                    {
                        if (row == null) continue;
                        if (!surfaceByKey.ContainsKey(row.EntityKeyId))
                            surfaceByKey[row.EntityKeyId] = row;
                    }
                }
                var materialByKey = new Dictionary<string, VisualMaterialRow>(StringComparer.Ordinal);
                if (material != null && material.Rows != null)
                {
                    foreach (VisualMaterialRow row in material.Rows)
                    {
                        if (row == null) continue;
                        if (!materialByKey.ContainsKey(row.EntityKeyId))
                            materialByKey[row.EntityKeyId] = row;
                    }
                }
                var socketByKey = new Dictionary<string, SocketAuditRow>(StringComparer.Ordinal);
                if (socket != null && socket.Rows != null)
                {
                    foreach (SocketAuditRow row in socket.Rows)
                    {
                        if (row == null) continue;
                        if (!socketByKey.ContainsKey(row.EntityKeyId))
                            socketByKey[row.EntityKeyId] = row;
                    }
                }

                var keys = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                if (quality != null && quality.Rows != null)
                {
                    foreach (VisualQualityRow row in quality.Rows)
                    {
                        if (row == null) continue;
                        if (seen.Add(row.EntityKeyId)) keys.Add(row.EntityKeyId);
                    }
                }
                foreach (string key in surfaceByKey.Keys) if (seen.Add(key)) keys.Add(key);
                foreach (string key in materialByKey.Keys) if (seen.Add(key)) keys.Add(key);
                foreach (string key in socketByKey.Keys) if (seen.Add(key)) keys.Add(key);

                var qualityByKey = new Dictionary<string, VisualQualityRow>(StringComparer.Ordinal);
                if (quality != null && quality.Rows != null)
                {
                    foreach (VisualQualityRow row in quality.Rows)
                    {
                        if (row == null) continue;
                        if (!qualityByKey.ContainsKey(row.EntityKeyId))
                            qualityByKey[row.EntityKeyId] = row;
                    }
                }

                var rows = new List<VisualHealthRow>();
                int loaderOwned = 0;
                int passCount = 0;
                int issueCount = 0;
                int totalVertexSamples = 0;
                int totalMaterials = 0;
                int totalSockets = 0;
                int ambiguousUnits = 0;

                foreach (string key in keys)
                {
                    try
                    {
                        VisualQualityRow q = qualityByKey.TryGetValue(key, out VisualQualityRow v) ? v : null;
                        VisualSurfaceRow s = surfaceByKey.TryGetValue(key, out VisualSurfaceRow sv) ? sv : null;
                        VisualMaterialRow m = materialByKey.TryGetValue(key, out VisualMaterialRow mv) ? mv : null;
                        SocketAuditRow so = socketByKey.TryGetValue(key, out SocketAuditRow sov) ? sov : null;

                        bool isLoaderOwned = q != null ? q.IsLoaderOwned : key.IndexOf(':') >= 0;
                        bool contractPass = q != null ? q.ContractPass : true;
                        int qualityIssues = q != null && q.QualityIssues != null ? q.QualityIssues.Count : 0;
                        int rendererCount = q != null ? q.RendererCount : (s != null ? s.RendererCount : (m != null ? m.RendererCount : 0));
                        int materialCount = q != null ? q.MaterialCount : (s != null ? s.MaterialCount : (m != null ? m.MaterialCount : 0));
                        int pbrCount = q != null ? q.PbrMaterialCount : 0;
                        int vertexSamples = q != null ? q.VertexColorSampleCount : 0;
                        int surfaceVertices = s != null ? s.VertexCount : 0;
                        int materialTextures = m != null ? (m.HasMainTextureCount + m.HasMetallicGlossMapCount + m.HasNormalMapCount + m.HasEmissionMapCount) : 0;
                        int socketCount = so != null ? so.SocketCount : 0;
                        int ambiguous = so != null ? so.AmbiguousSocketCount : 0;

                        var row = new VisualHealthRow(
                            key,
                            isLoaderOwned,
                            contractPass,
                            qualityIssues,
                            rendererCount,
                            materialCount,
                            pbrCount,
                            vertexSamples,
                            surfaceVertices,
                            materialTextures,
                            socketCount,
                            ambiguous);
                        rows.Add(row);
                        if (row.IsLoaderOwned) loaderOwned++;
                        if (row.IsLoaderOwned && row.ContractPass && row.TotalIssueCount == 0) passCount++;
                        else if (row.TotalIssueCount > 0) issueCount++;
                        totalVertexSamples += row.VertexColorSampleCount;
                        totalMaterials += row.MaterialCount;
                        totalSockets += row.SocketCount;
                        if (row.AmbiguousSocketCount > 0) ambiguousUnits++;
                    }
                    catch
                    {
                        // Skip an unusable unit instead of aborting the sweep.
                    }
                }

                int omitted = 0;
                if (quality != null) omitted += quality.OmittedRowCount;
                if (surface != null) omitted += surface.OmittedRowCount;
                if (material != null) omitted += material.OmittedRowCount;
                if (socket != null) omitted += socket.OmittedRowCount;

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
                return VisualHealthSummary.Empty;
            }
        }
    }
}
