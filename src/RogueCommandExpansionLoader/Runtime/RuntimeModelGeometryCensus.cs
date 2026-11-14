using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Bounded live mesh-topology, bindpose, and SkinnedMeshRenderer bounds audit
    /// for active units, detecting collapsed skinned bounds and missing meshes
    /// without screenshots or captures.
    /// </summary>
    public sealed class ModelGeometryRow
    {
        public string EntityKeyId { get; }
        public string EntityId { get; }
        public bool IsLoaderOwned { get; }
        public int MeshFilterCount { get; }
        public int SkinnedMeshRendererCount { get; }
        public int VisibleRendererCount { get; }
        public int VertexCount { get; }
        public int TriangleCount { get; }
        public int BoneCount { get; }
        public int BindposeCount { get; }
        public bool HasCollapsedSkinnedBounds { get; }
        public int MissingMeshCount { get; }
        public IReadOnlyList<string> Issues { get; }

        public ModelGeometryRow(
            string entityKeyId,
            string entityId,
            bool isLoaderOwned,
            int meshFilterCount,
            int skinnedMeshRendererCount,
            int visibleRendererCount,
            int vertexCount,
            int triangleCount,
            int boneCount,
            int bindposeCount,
            bool hasCollapsedSkinnedBounds,
            int missingMeshCount,
            IReadOnlyList<string> issues)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            EntityId = entityId ?? string.Empty;
            IsLoaderOwned = isLoaderOwned;
            MeshFilterCount = Math.Max(0, meshFilterCount);
            SkinnedMeshRendererCount = Math.Max(0, skinnedMeshRendererCount);
            VisibleRendererCount = Math.Max(0, visibleRendererCount);
            VertexCount = Math.Max(0, vertexCount);
            TriangleCount = Math.Max(0, triangleCount);
            BoneCount = Math.Max(0, boneCount);
            BindposeCount = Math.Max(0, bindposeCount);
            HasCollapsedSkinnedBounds = hasCollapsedSkinnedBounds;
            MissingMeshCount = Math.Max(0, missingMeshCount);
            Issues = issues ?? new List<string>().AsReadOnly();
        }
    }

    public sealed class ModelGeometryCensus
    {
        public int OmittedRowCount { get; }
        public int CollapsedSkinnedCount { get; }
        public int MissingMeshUnitCount { get; }
        public int TotalVertices { get; }
        public int TotalTriangles { get; }
        public IReadOnlyList<ModelGeometryRow> Rows { get; }

        public ModelGeometryCensus(
            int omittedRowCount,
            int collapsedSkinnedCount,
            int missingMeshUnitCount,
            int totalVertices,
            int totalTriangles,
            IReadOnlyList<ModelGeometryRow> rows)
        {
            OmittedRowCount = Math.Max(0, omittedRowCount);
            CollapsedSkinnedCount = Math.Max(0, collapsedSkinnedCount);
            MissingMeshUnitCount = Math.Max(0, missingMeshUnitCount);
            TotalVertices = Math.Max(0, totalVertices);
            TotalTriangles = Math.Max(0, totalTriangles);
            Rows = rows ?? new List<ModelGeometryRow>().AsReadOnly();
        }
    }

    public static class RuntimeModelGeometryCensus
    {
        public static ModelGeometryCensus Current(int maxRows = 64)
        {
            int omitted;
            List<EntityController> units = RuntimeVisualCensusSupport.CollectLivingUnits(maxRows, out omitted);
            var rows = new List<ModelGeometryRow>(units.Count);
            int collapsedSkinnedCount = 0;
            int missingMeshUnitCount = 0;
            int totalVertices = 0;
            int totalTriangles = 0;

            for (int i = 0; i < units.Count; i++)
            {
                EntityController unit = units[i];
                if (unit == null) continue;

                ModelGeometryRow row = InspectUnit(unit);
                rows.Add(row);
                totalVertices += row.VertexCount;
                totalTriangles += row.TriangleCount;
                if (row.HasCollapsedSkinnedBounds) collapsedSkinnedCount++;
                if (row.MissingMeshCount > 0) missingMeshUnitCount++;
            }

            return new ModelGeometryCensus(
                omitted,
                collapsedSkinnedCount,
                missingMeshUnitCount,
                totalVertices,
                totalTriangles,
                rows.AsReadOnly());
        }

        public static ModelGeometryRow InspectUnit(EntityController unit)
        {
            string key = RuntimeVisualCensusSupport.ResolveEntityKey(unit);
            string entityId = string.Empty;
            try { entityId = unit.EntityId ?? string.Empty; } catch { }
            bool loaderOwned = RuntimeVisualCensusSupport.IsLoaderOwned(unit);

            GameObject root = null;
            try { root = unit.gameObject; } catch { }
            if (root == null)
            {
                return EvaluateGeometryRow(key, entityId, loaderOwned, 0, 0, 0, 0, 0, 0, 0, false, 1);
            }

            VisualHierarchySnapshot hierarchy = RuntimeVisualCensusSupport.CaptureHierarchy(root);
            int meshFilterCount = 0;
            int skinnedCount = 0;
            int visibleCount = 0;
            int vertexCount = 0;
            int triangleCount = 0;
            int boneCount = 0;
            int bindposeCount = 0;
            bool hasCollapsedBounds = false;
            int missingMeshCount = 0;

            for (int i = 0; i < hierarchy.Renderers.Count; i++)
            {
                Renderer r = hierarchy.Renderers[i];
                if (r == null) continue;

                bool enabled = false;
                try { enabled = r.enabled; } catch { }
                try { if (enabled && r.isVisible) visibleCount++; } catch { }

                SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
                if (smr != null)
                {
                    skinnedCount++;
                    Mesh smrMesh = null;
                    try { smrMesh = smr.sharedMesh; } catch { }
                    if (smrMesh == null)
                    {
                        missingMeshCount++;
                    }
                    else
                    {
                        try { vertexCount += Math.Max(0, smrMesh.vertexCount); } catch { }
                        try
                        {
                            int[] tris = smrMesh.triangles;
                            if (tris != null) triangleCount += tris.Length / 3;
                        }
                        catch { }
                        try
                        {
                            Matrix4x4[] bp = smrMesh.bindposes;
                            if (bp != null) bindposeCount += bp.Length;
                        }
                        catch { }
                    }

                    try
                    {
                        Transform[] bones = smr.bones;
                        if (bones != null) boneCount += bones.Length;
                    }
                    catch { }

                    try
                    {
                        if (enabled && smrMesh != null && smr.localBounds.extents.sqrMagnitude <= 1e-6f)
                        {
                            hasCollapsedBounds = true;
                        }
                    }
                    catch { }
                }
                else
                {
                    MeshFilter mf = null;
                    try { mf = r.GetComponent<MeshFilter>(); } catch { }
                    if (mf != null)
                    {
                        meshFilterCount++;
                        Mesh mfMesh = null;
                        try { mfMesh = mf.sharedMesh; } catch { }
                        if (mfMesh == null)
                        {
                            missingMeshCount++;
                        }
                        else
                        {
                            try { vertexCount += Math.Max(0, mfMesh.vertexCount); } catch { }
                            try
                            {
                                int[] tris = mfMesh.triangles;
                                if (tris != null) triangleCount += tris.Length / 3;
                            }
                            catch { }
                        }
                    }
                }
            }

            return EvaluateGeometryRow(
                key,
                entityId,
                loaderOwned,
                meshFilterCount,
                skinnedCount,
                visibleCount,
                vertexCount,
                triangleCount,
                boneCount,
                bindposeCount,
                hasCollapsedBounds,
                missingMeshCount);
        }

        public static ModelGeometryRow EvaluateGeometryRow(
            string entityKeyId,
            string entityId,
            bool isLoaderOwned,
            int meshFilterCount,
            int skinnedMeshRendererCount,
            int visibleRendererCount,
            int vertexCount,
            int triangleCount,
            int boneCount,
            int bindposeCount,
            bool hasCollapsedSkinnedBounds,
            int missingMeshCount)
        {
            var issues = new List<string>();
            if (hasCollapsedSkinnedBounds)
            {
                issues.Add("COLLAPSED_SKINNED_BOUNDS");
            }
            if (missingMeshCount > 0)
            {
                issues.Add("MISSING_SHARED_MESH");
            }
            if (skinnedMeshRendererCount > 0 && bindposeCount == 0)
            {
                issues.Add("MISSING_BINDPOSES");
            }
            if (skinnedMeshRendererCount > 0 && boneCount == 0)
            {
                issues.Add("MISSING_SKINNED_BONES");
            }

            return new ModelGeometryRow(
                entityKeyId,
                entityId,
                isLoaderOwned,
                meshFilterCount,
                skinnedMeshRendererCount,
                visibleRendererCount,
                vertexCount,
                triangleCount,
                boneCount,
                bindposeCount,
                hasCollapsedSkinnedBounds,
                missingMeshCount,
                issues.AsReadOnly());
        }
    }
}
