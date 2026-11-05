using System;
using System.Collections.Generic;
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
        public bool HierarchyTruncated { get; }
        public bool MaterialSamplingBounded { get; }
        public bool NameSamplingBounded { get; }

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
            IReadOnlyList<string> shaderKeywords,
            bool hierarchyTruncated = false,
            bool materialSamplingBounded = false,
            bool nameSamplingBounded = false)
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
            HierarchyTruncated = hierarchyTruncated;
            MaterialSamplingBounded = materialSamplingBounded;
            NameSamplingBounded = nameSamplingBounded;
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

            try
            {
                List<EntityController> units = RuntimeVisualCensusSupport.CollectLivingUnits(maxRows, out omitted);
                var materialCache = new Dictionary<Material, RuntimeVisualCensusSupport.MaterialBindingSnapshot>(
                    ReferenceEqualityComparer<Material>.Instance);
                for (int i = 0; i < units.Count; i++)
                {
                    try
                    {
                        rows.Add(CollectRow(units[i], materialCache));
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

            return new VisualMaterialCensus(rows.AsReadOnly(), omitted);
        }

        private static VisualMaterialRow CollectRow(
            EntityController unit,
            Dictionary<Material, RuntimeVisualCensusSupport.MaterialBindingSnapshot> materialCache)
        {
            string key = RuntimeVisualCensusSupport.ResolveEntityKey(unit);
            bool isLoaderOwned = RuntimeVisualCensusSupport.IsLoaderOwned(key);

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
            var seenMaterialNames = new HashSet<string>(StringComparer.Ordinal);
            var seenShaderNames = new HashSet<string>(StringComparer.Ordinal);
            var seenTextureNames = new HashSet<string>(StringComparer.Ordinal);
            var seenKeywords = new HashSet<string>(StringComparer.Ordinal);
            bool hierarchyTruncated = false;
            bool materialSamplingBounded = false;
            bool nameSamplingBounded = false;

            if (go != null)
            {
                VisualHierarchySnapshot snapshot = RuntimeVisualCensusSupport.CaptureHierarchy(go);
                hierarchyTruncated = snapshot.HierarchyTruncated;
                rendererCount = snapshot.Renderers.Count;
                int inspectedMaterialSlots = 0;

                for (int rendererIndex = 0; rendererIndex < snapshot.Renderers.Count; rendererIndex++)
                {
                    Renderer renderer = snapshot.Renderers[rendererIndex];
                    if (renderer == null) continue;
                    bool rendererEnabled = false;
                    try { rendererEnabled = renderer.enabled; } catch { }
                    if (!rendererEnabled) continue;
                    if (rendererEnabled) rendererEnabledCount++;

                    Material[] materials = null;
                    try { materials = renderer.sharedMaterials; } catch { }
                    if (materials == null) continue;
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

                        if (!isLoaderOwned)
                        {
                            materialSamplingBounded = true;
                            nameSamplingBounded = true;
                            continue;
                        }

                        RuntimeVisualCensusSupport.MaterialBindingSnapshot binding;
                        if (!materialCache.TryGetValue(material, out binding))
                        {
                            binding = RuntimeVisualCensusSupport.InspectMaterial(material);
                            materialCache[material] = binding;
                        }

                        RuntimeVisualCensusSupport.AddUniqueBounded(
                            materialNames,
                            seenMaterialNames,
                            binding.MaterialName,
                            RuntimeVisualCensusSupport.MaxDiagnosticNamesPerUnit,
                            ref nameSamplingBounded);
                        RuntimeVisualCensusSupport.AddUniqueBounded(
                            shaderNames,
                            seenShaderNames,
                            binding.ShaderName,
                            RuntimeVisualCensusSupport.MaxDiagnosticNamesPerUnit,
                            ref nameSamplingBounded);

                        string[] keywords = binding.ShaderKeywords;
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

                        Texture main = binding.MainTexture;
                        if (main != null)
                        {
                            mainTexCount++;
                            string textureName = string.Empty;
                            try { textureName = main.name; } catch { }
                            RuntimeVisualCensusSupport.AddUniqueBounded(
                                textureNames,
                                seenTextureNames,
                                textureName,
                                RuntimeVisualCensusSupport.MaxDiagnosticNamesPerUnit,
                                ref nameSamplingBounded);
                        }

                        Texture metallicGloss = binding.MetallicGlossMap;
                        if (metallicGloss != null)
                        {
                            metallicGlossCount++;
                            string textureName = string.Empty;
                            try { textureName = metallicGloss.name; } catch { }
                            RuntimeVisualCensusSupport.AddUniqueBounded(
                                textureNames,
                                seenTextureNames,
                                textureName,
                                RuntimeVisualCensusSupport.MaxDiagnosticNamesPerUnit,
                                ref nameSamplingBounded);
                        }

                        Texture normal = binding.NormalMap;
                        if (normal != null)
                        {
                            normalCount++;
                            string textureName = string.Empty;
                            try { textureName = normal.name; } catch { }
                            RuntimeVisualCensusSupport.AddUniqueBounded(
                                textureNames,
                                seenTextureNames,
                                textureName,
                                RuntimeVisualCensusSupport.MaxDiagnosticNamesPerUnit,
                                ref nameSamplingBounded);
                        }

                        Texture emission = binding.EmissionMap;
                        if (emission != null)
                        {
                            emissionCount++;
                            string textureName = string.Empty;
                            try { textureName = emission.name; } catch { }
                            RuntimeVisualCensusSupport.AddUniqueBounded(
                                textureNames,
                                seenTextureNames,
                                textureName,
                                RuntimeVisualCensusSupport.MaxDiagnosticNamesPerUnit,
                                ref nameSamplingBounded);
                        }
                    }
                }

                if (snapshot.RendererListTruncated || snapshot.LodListTruncated)
                {
                    nameSamplingBounded = true;
                }
            }

            return new VisualMaterialRow(
                key,
                rendererCount,
                materialCount,
                materialNames.AsReadOnly(),
                shaderNames.AsReadOnly(),
                textureNames.AsReadOnly(),
                mainTexCount,
                metallicGlossCount,
                normalCount,
                emissionCount,
                shaderKeywords.AsReadOnly(),
                hierarchyTruncated,
                materialSamplingBounded,
                nameSamplingBounded);
        }
    }
}