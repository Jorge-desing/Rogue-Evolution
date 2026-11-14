using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Detailed runtime texture slot audit and mip-bias diagnostic control
    /// for VehiclePBR_VertexWear and native materials.
    /// </summary>
    public sealed class TextureSlotAuditInfo
    {
        public string SlotName { get; }
        public string TextureName { get; }
        public int Width { get; }
        public int Height { get; }
        public string Format { get; }
        public int MipmapCount { get; }
        public int AnisoLevel { get; }
        public float MipMapBias { get; }
        public float EstimatedMemoryKb { get; }

        public TextureSlotAuditInfo(
            string slotName,
            string textureName,
            int width,
            int height,
            string format,
            int mipmapCount,
            int anisoLevel,
            float mipMapBias,
            float estimatedMemoryKb)
        {
            SlotName = slotName ?? string.Empty;
            TextureName = textureName ?? string.Empty;
            Width = Math.Max(0, width);
            Height = Math.Max(0, height);
            Format = format ?? string.Empty;
            MipmapCount = Math.Max(0, mipmapCount);
            AnisoLevel = Math.Max(0, anisoLevel);
            MipMapBias = mipMapBias;
            EstimatedMemoryKb = estimatedMemoryKb < 0f ? 0f : estimatedMemoryKb;
        }
    }

    public sealed class TextureAuditRow
    {
        public string EntityKeyId { get; }
        public string EntityId { get; }
        public bool IsLoaderOwned { get; }
        public int UniqueTextureCount { get; }
        public float EstimatedMemoryKb { get; }
        public int MaxWidth { get; }
        public int MaxHeight { get; }
        public bool HasDetailBumpMap { get; }
        public int NonPowerOfTwoCount { get; }
        public IReadOnlyList<TextureSlotAuditInfo> Textures { get; }

        public TextureAuditRow(
            string entityKeyId,
            string entityId,
            bool isLoaderOwned,
            int uniqueTextureCount,
            float estimatedMemoryKb,
            int maxWidth,
            int maxHeight,
            bool hasDetailBumpMap,
            int nonPowerOfTwoCount,
            IReadOnlyList<TextureSlotAuditInfo> textures)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            EntityId = entityId ?? string.Empty;
            IsLoaderOwned = isLoaderOwned;
            UniqueTextureCount = Math.Max(0, uniqueTextureCount);
            EstimatedMemoryKb = estimatedMemoryKb < 0f ? 0f : estimatedMemoryKb;
            MaxWidth = Math.Max(0, maxWidth);
            MaxHeight = Math.Max(0, maxHeight);
            HasDetailBumpMap = hasDetailBumpMap;
            NonPowerOfTwoCount = Math.Max(0, nonPowerOfTwoCount);
            Textures = textures ?? new List<TextureSlotAuditInfo>().AsReadOnly();
        }
    }

    public sealed class TextureAuditCensus
    {
        public int OmittedRowCount { get; }
        public int TotalUniqueTextures { get; }
        public float TotalEstimatedMemoryKb { get; }
        public int NonPowerOfTwoCount { get; }
        public IReadOnlyList<TextureAuditRow> Rows { get; }

        public TextureAuditCensus(
            int omittedRowCount,
            int totalUniqueTextures,
            float totalEstimatedMemoryKb,
            int nonPowerOfTwoCount,
            IReadOnlyList<TextureAuditRow> rows)
        {
            OmittedRowCount = Math.Max(0, omittedRowCount);
            TotalUniqueTextures = Math.Max(0, totalUniqueTextures);
            TotalEstimatedMemoryKb = totalEstimatedMemoryKb < 0f ? 0f : totalEstimatedMemoryKb;
            NonPowerOfTwoCount = Math.Max(0, nonPowerOfTwoCount);
            Rows = rows ?? new List<TextureAuditRow>().AsReadOnly();
        }
    }

    public sealed class TextureControlResult
    {
        public bool Success { get; }
        public string EntityKeyId { get; }
        public float AppliedMipBias { get; }
        public int UpdatedTextureCount { get; }
        public string Message { get; }

        public TextureControlResult(
            bool success,
            string entityKeyId,
            float appliedMipBias,
            int updatedTextureCount,
            string message)
        {
            Success = success;
            EntityKeyId = entityKeyId ?? string.Empty;
            AppliedMipBias = appliedMipBias;
            UpdatedTextureCount = Math.Max(0, updatedTextureCount);
            Message = message ?? string.Empty;
        }
    }

    public static class RuntimeTextureAuditCensus
    {
        private static readonly string[] InspectedSlots = new[]
        {
            "_MainTex",
            "_BaseMap",
            "_MetallicGlossMap",
            "_BumpMap",
            "_DetailBumpMap",
            "_EmissionMap"
        };

        public static TextureAuditCensus Current(int maxRows = 64)
        {
            int omitted;
            List<EntityController> units = RuntimeVisualCensusSupport.CollectLivingUnits(maxRows, out omitted);
            var rows = new List<TextureAuditRow>(units.Count);
            var globalUnique = new HashSet<Texture>();
            float totalMemKb = 0f;
            int totalNpot = 0;

            for (int i = 0; i < units.Count; i++)
            {
                EntityController unit = units[i];
                if (unit == null) continue;

                TextureAuditRow row = InspectUnit(unit, globalUnique);
                rows.Add(row);
                totalMemKb += row.EstimatedMemoryKb;
                totalNpot += row.NonPowerOfTwoCount;
            }

            return new TextureAuditCensus(
                omitted,
                globalUnique.Count,
                totalMemKb,
                totalNpot,
                rows.AsReadOnly());
        }

        public static TextureAuditRow InspectUnit(EntityController unit, HashSet<Texture> globalSeen = null)
        {
            string key = RuntimeVisualCensusSupport.ResolveEntityKey(unit);
            string entityId = string.Empty;
            try { entityId = unit.EntityId ?? string.Empty; } catch { }
            bool loaderOwned = RuntimeVisualCensusSupport.IsLoaderOwned(unit);

            GameObject root = null;
            try { root = unit.gameObject; } catch { }
            if (root == null)
            {
                return new TextureAuditRow(key, entityId, loaderOwned, 0, 0f, 0, 0, false, 0, new List<TextureSlotAuditInfo>().AsReadOnly());
            }

            VisualHierarchySnapshot hierarchy = RuntimeVisualCensusSupport.CaptureHierarchy(root);
            var localSeen = new HashSet<Texture>();
            var slots = new List<TextureSlotAuditInfo>();
            float totalKb = 0f;
            int maxW = 0;
            int maxH = 0;
            bool hasDetailBump = false;
            int npotCount = 0;

            for (int rIdx = 0; rIdx < hierarchy.Renderers.Count; rIdx++)
            {
                Renderer r = hierarchy.Renderers[rIdx];
                if (r == null) continue;

                Material[] mats = null;
                try { mats = r.sharedMaterials; } catch { }
                if (mats == null) continue;

                for (int mIdx = 0; mIdx < mats.Length; mIdx++)
                {
                    Material mat = mats[mIdx];
                    if (mat == null) continue;

                    for (int sIdx = 0; sIdx < InspectedSlots.Length; sIdx++)
                    {
                        string slot = InspectedSlots[sIdx];
                        Texture tex = RuntimeVisualCensusSupport.GetTexture(mat, slot);
                        if (tex == null) continue;

                        if (slot == "_DetailBumpMap") hasDetailBump = true;
                        if (globalSeen != null) globalSeen.Add(tex);
                        if (!localSeen.Add(tex)) continue;

                        int w = 0;
                        int h = 0;
                        int mips = 1;
                        int aniso = 1;
                        float bias = 0f;
                        string fmt = "Unknown";
                        string texName = string.Empty;

                        try { texName = tex.name ?? string.Empty; } catch { }
                        try { w = tex.width; } catch { }
                        try { h = tex.height; } catch { }
                        try { mips = tex.mipmapCount; } catch { }
                        try { aniso = tex.anisoLevel; } catch { }
                        try { bias = tex.mipMapBias; } catch { }

                        Texture2D tex2d = tex as Texture2D;
                        if (tex2d != null)
                        {
                            try { fmt = tex2d.format.ToString(); } catch { }
                        }

                        if (w > maxW) maxW = w;
                        if (h > maxH) maxH = h;
                        if (!IsPowerOfTwo(w) || !IsPowerOfTwo(h)) npotCount++;

                        float estKb = EstimateTextureMemoryKb(w, h, fmt, mips);
                        totalKb += estKb;

                        if (slots.Count < 32)
                        {
                            slots.Add(new TextureSlotAuditInfo(
                                slot, texName, w, h, fmt, mips, aniso, bias, estKb));
                        }
                    }
                }
            }

            return new TextureAuditRow(
                key,
                entityId,
                loaderOwned,
                localSeen.Count,
                totalKb,
                maxW,
                maxH,
                hasDetailBump,
                npotCount,
                slots.AsReadOnly());
        }

        public static TextureControlResult ApplyTextureMipBias(string entityKeyId, float mipBias)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new TextureControlResult(false, string.Empty, 0f, 0, "entityKeyId is required.");
            }
            if (float.IsNaN(mipBias) || float.IsInfinity(mipBias))
            {
                return new TextureControlResult(false, entityKeyId, 0f, 0, "mipBias must be finite.");
            }

            float clamped = Math.Max(-2f, Math.Min(4f, mipBias));
            ExistingControllers existing = null;
            try { existing = ExistingControllers.Instance; } catch { }
            EntityController target = existing != null
                ? RuntimeEntityKeyResolver.FindUniqueEntityByKey(existing, entityKeyId)
                : null;

            if (target == null)
            {
                return new TextureControlResult(false, entityKeyId, clamped, 0, "Target entity not found.");
            }

            VisualHierarchySnapshot hierarchy = RuntimeVisualCensusSupport.CaptureHierarchy(target.gameObject);
            var seen = new HashSet<Texture>();
            int updated = 0;

            for (int rIdx = 0; rIdx < hierarchy.Renderers.Count; rIdx++)
            {
                Renderer r = hierarchy.Renderers[rIdx];
                if (r == null) continue;
                Material[] mats = null;
                try { mats = r.sharedMaterials; } catch { }
                if (mats == null) continue;

                for (int mIdx = 0; mIdx < mats.Length; mIdx++)
                {
                    Material mat = mats[mIdx];
                    if (mat == null) continue;

                    for (int sIdx = 0; sIdx < InspectedSlots.Length; sIdx++)
                    {
                        Texture tex = RuntimeVisualCensusSupport.GetTexture(mat, InspectedSlots[sIdx]);
                        if (tex == null || !seen.Add(tex)) continue;
                        try
                        {
                            tex.mipMapBias = clamped;
                            updated++;
                        }
                        catch { }
                    }
                }
            }

            return new TextureControlResult(
                updated > 0,
                entityKeyId,
                clamped,
                updated,
                updated > 0 ? $"Updated mipMapBias={clamped:F2} on {updated} texture(s)." : "No inspectable textures found on entity.");
        }

        public static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }

        public static float EstimateTextureMemoryKb(int width, int height, string format, int mipmapCount)
        {
            if (width <= 0 || height <= 0) return 0f;
            float bpp = 4f; // RGBA32 default
            string f = (format ?? string.Empty).ToUpperInvariant();
            if (f.Contains("DXT1") || f.Contains("BC1") || f.Contains("BC4"))
            {
                bpp = 0.5f;
            }
            else if (f.Contains("DXT5") || f.Contains("BC3") || f.Contains("BC5") || f.Contains("BC7"))
            {
                bpp = 1.0f;
            }
            else if (f.Contains("RGB24"))
            {
                bpp = 3.0f;
            }

            float baseBytes = width * (float)height * bpp;
            float mipFactor = mipmapCount > 1 ? 1.333f : 1.0f;
            return (baseBytes * mipFactor) / 1024f;
        }
    }
}
