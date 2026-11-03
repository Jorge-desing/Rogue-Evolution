using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry of material and texture bindings for living
    /// units. Decoupled from Unity types to support clean-room MCP telemetry
    /// without visual captures or asset inspection.
    /// </summary>
    public sealed class VisualMaterialRow
    {
        public string EntityKeyId { get; }
        public int RendererCount { get; }
        public int MaterialCount { get; }
        public IReadOnlyList<string> MaterialNames { get; }
        public IReadOnlyList<string> ShaderNames { get; }
        public IReadOnlyList<string> TextureNames { get; }
        public int HasMainTextureCount { get; }
        public int HasMetallicGlossMapCount { get; }
        public int HasNormalMapCount { get; }
        public int HasEmissionMapCount { get; }
        public IReadOnlyList<string> ShaderKeywords { get; }

        public VisualMaterialRow(
            string entityKeyId,
            int rendererCount,
            int materialCount,
            IReadOnlyList<string> materialNames,
            IReadOnlyList<string> shaderNames,
            IReadOnlyList<string> textureNames,
            int hasMainTextureCount,
            int hasMetallicGlossMapCount,
            int hasNormalMapCount,
            int hasEmissionMapCount,
            IReadOnlyList<string> shaderKeywords)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            RendererCount = rendererCount < 0 ? 0 : rendererCount;
            MaterialCount = materialCount < 0 ? 0 : materialCount;
            MaterialNames = materialNames ?? new List<string>().AsReadOnly();
            ShaderNames = shaderNames ?? new List<string>().AsReadOnly();
            TextureNames = textureNames ?? new List<string>().AsReadOnly();
            HasMainTextureCount = hasMainTextureCount < 0 ? 0 : hasMainTextureCount;
            HasMetallicGlossMapCount = hasMetallicGlossMapCount < 0 ? 0 : hasMetallicGlossMapCount;
            HasNormalMapCount = hasNormalMapCount < 0 ? 0 : hasNormalMapCount;
            HasEmissionMapCount = hasEmissionMapCount < 0 ? 0 : hasEmissionMapCount;
            ShaderKeywords = shaderKeywords ?? new List<string>().AsReadOnly();
        }
    }

    public sealed class VisualMaterialCensus
    {
        public IReadOnlyList<VisualMaterialRow> Rows { get; }
        public int OmittedRowCount { get; }

        public VisualMaterialCensus(IReadOnlyList<VisualMaterialRow> rows, int omittedRowCount)
        {
            Rows = rows ?? new List<VisualMaterialRow>().AsReadOnly();
            OmittedRowCount = omittedRowCount < 0 ? 0 : omittedRowCount;
        }
    }

    public static class RuntimeVisualMaterialCensus
    {
        public static VisualMaterialCensus Current(int maxRows = 64)
        {
            var rows = new List<VisualMaterialRow>();
            int omitted = 0;
            int clampedMax = Math.Max(1, Math.Min(maxRows, 256));

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new VisualMaterialCensus(rows.AsReadOnly(), 0);
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
            }

            return new VisualMaterialCensus(rows.AsReadOnly(), omitted);
        }

        private static VisualMaterialRow CollectRow(EntityController unit)
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
            var materialNames = new List<string>();
            var shaderNames = new List<string>();
            var textureNames = new List<string>();
            int mainTexCount = 0;
            int metallicGlossCount = 0;
            int normalCount = 0;
            int emissionCount = 0;
            var shaderKeywords = new List<string>();

            if (go != null)
            {
                Renderer[] renderers = null;
                try { renderers = go.GetComponentsInChildren<Renderer>(true); } catch { renderers = null; }
                if (renderers != null)
                {
                    foreach (Renderer renderer in renderers)
                    {
                        if (renderer == null) continue;
                        rendererCount++;
                        try { if (renderer.enabled) rendererEnabledCount++; } catch { }

                        Material[] mats = null;
                        try { mats = renderer.sharedMaterials; } catch { mats = null; }
                        if (mats == null)
                        {
                            try { mats = renderer.materials; } catch { mats = null; }
                        }
                        if (mats == null) continue;

                        foreach (Material mat in mats)
                        {
                            if (mat == null) continue;
                            materialCount++;
                            try { if (!string.IsNullOrWhiteSpace(mat.name)) materialNames.Add(mat.name); } catch { }
                            try
                            {
                                if (mat.shader != null && !string.IsNullOrWhiteSpace(mat.shader.name))
                                {
                                    shaderNames.Add(mat.shader.name);
                                }
                            }
                            catch { }
                            try
                            {
                                if (mat.shaderKeywords != null)
                                {
                                    foreach (string kw in mat.shaderKeywords)
                                    {
                                        if (!string.IsNullOrWhiteSpace(kw) && !shaderKeywords.Contains(kw))
                                        {
                                            shaderKeywords.Add(kw);
                                        }
                                    }
                                }
                            }
                            catch { }

                            try
                            {
                                Texture main = null;
                                try { main = mat.mainTexture; } catch { main = null; }
                                if (main != null)
                                {
                                    mainTexCount++;
                                    try { if (!string.IsNullOrWhiteSpace(main.name)) textureNames.Add(main.name); } catch { }
                                }
                            }
                            catch { }

                            try
                            {
                                Texture mg = null;
                                try { mg = mat.GetTexture("_MetallicGlossMap"); } catch { mg = null; }
                                if (mg != null)
                                {
                                    metallicGlossCount++;
                                    try { if (!string.IsNullOrWhiteSpace(mg.name) && !textureNames.Contains(mg.name)) textureNames.Add(mg.name); } catch { }
                                }
                            }
                            catch { }

                            try
                            {
                                Texture nm = null;
                                try { nm = mat.GetTexture("_BumpMap"); } catch { nm = null; }
                                if (nm == null)
                                {
                                    try { nm = mat.GetTexture("_NormalMap"); } catch { nm = null; }
                                }
                                if (nm != null)
                                {
                                    normalCount++;
                                    try { if (!string.IsNullOrWhiteSpace(nm.name) && !textureNames.Contains(nm.name)) textureNames.Add(nm.name); } catch { }
                                }
                            }
                            catch { }

                            try
                            {
                                Texture em = null;
                                try { em = mat.GetTexture("_EmissionMap"); } catch { em = null; }
                                if (em != null)
                                {
                                    emissionCount++;
                                    try { if (!string.IsNullOrWhiteSpace(em.name) && !textureNames.Contains(em.name)) textureNames.Add(em.name); } catch { }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }

            return new VisualMaterialRow(
                key,
                rendererCount,
                materialCount,
                materialNames.Distinct().ToList().AsReadOnly(),
                shaderNames.Distinct().ToList().AsReadOnly(),
                textureNames.Distinct().ToList().AsReadOnly(),
                mainTexCount,
                metallicGlossCount,
                normalCount,
                emissionCount,
                shaderKeywords.Distinct().ToList().AsReadOnly());
        }
    }
}