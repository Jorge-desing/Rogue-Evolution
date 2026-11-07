using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Immutable runtime representation of a declarative ContentUnitDefinition.visualVariants entry,
    /// establishing the two-level visual system: Level 1 Primary Chassis and Level 2 Tactical Variants.
    /// </summary>
    public sealed class RuntimeUnitVisualVariant
    {
        public RuntimeUnitVisualVariant(
            string variantId,
            string unitId,
            string presetName,
            string colorTintHex,
            float scale,
            string visualAssetName,
            string visualRootPath,
            bool isPrimary)
        {
            VariantId = variantId == null ? string.Empty : variantId.Trim();
            UnitId = unitId == null ? string.Empty : unitId.Trim();
            PresetName = presetName == null ? string.Empty : presetName.Trim();
            ColorTintHex = colorTintHex == null ? string.Empty : colorTintHex.Trim();
            Scale = scale <= 0f ? 1.0f : scale;
            VisualAssetName = visualAssetName == null ? string.Empty : visualAssetName.Trim();
            VisualRootPath = visualRootPath == null ? string.Empty : visualRootPath.Trim();
            IsPrimary = isPrimary;

            Color parsed;
            if (TryParseHexColor(ColorTintHex, out parsed))
            {
                ColorTint = parsed;
            }
            else
            {
                ColorTint = new Color(1f, 1f, 1f, 1f);
            }
        }

        public string VariantId { get; }
        public string UnitId { get; }
        public string PresetName { get; }
        public string ColorTintHex { get; }
        public Color ColorTint { get; }
        public float Scale { get; }
        public string VisualAssetName { get; }
        public string VisualRootPath { get; }
        public bool IsPrimary { get; }

        public static bool TryParseHexColor(string hex, out Color color)
        {
            color = new Color(1f, 1f, 1f, 1f);
            if (string.IsNullOrWhiteSpace(hex))
            {
                return false;
            }

            string clean = hex.Trim().TrimStart('#');
            if (clean.Length == 6)
            {
                int r, g, b;
                if (int.TryParse(clean.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) &&
                    int.TryParse(clean.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) &&
                    int.TryParse(clean.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                {
                    color = new Color(r / 255f, g / 255f, b / 255f, 1f);
                    return true;
                }
            }
            else if (clean.Length == 8)
            {
                int r, g, b, a;
                if (int.TryParse(clean.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) &&
                    int.TryParse(clean.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) &&
                    int.TryParse(clean.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b) &&
                    int.TryParse(clean.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a))
                {
                    color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Thread-safe process-local registry populated by ContentPackRuntimeLoader for visual variants.
    /// Operates independently from Unity objects for safe offline testing.
    /// </summary>
    public static class RuntimeUnitVisualVariantRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Dictionary<string, RuntimeUnitVisualVariant>> VariantsByUnit =
            new Dictionary<string, Dictionary<string, RuntimeUnitVisualVariant>>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string unitId, IEnumerable<RuntimeUnitVisualVariant> variants)
        {
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return;
            }

            var normalizedVariants = new Dictionary<string, RuntimeUnitVisualVariant>(StringComparer.OrdinalIgnoreCase);
            if (variants != null)
            {
                foreach (RuntimeUnitVisualVariant variant in variants)
                {
                    if (variant == null || string.IsNullOrWhiteSpace(variant.VariantId))
                    {
                        continue;
                    }

                    normalizedVariants[variant.VariantId.Trim()] = variant;

                    // Also index by local slug / suffix for flexible lookups (e.g. "desert" or "singularity")
                    string id = variant.VariantId.Trim();
                    int colonIdx = id.IndexOf(':');
                    string namePart = colonIdx >= 0 ? id.Substring(colonIdx + 1) : id;
                    int lastDash = namePart.LastIndexOf('-');
                    if (lastDash >= 0 && lastDash + 1 < namePart.Length)
                    {
                        string localSlug = namePart.Substring(lastDash + 1);
                        if (!normalizedVariants.ContainsKey(localSlug))
                        {
                            normalizedVariants[localSlug] = variant;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(variant.PresetName) &&
                        !normalizedVariants.ContainsKey(variant.PresetName))
                    {
                        normalizedVariants[variant.PresetName] = variant;
                    }
                }
            }

            lock (Gate)
            {
                string normalizedUnitId = unitId.Trim();
                if (normalizedVariants.Count == 0)
                {
                    VariantsByUnit.Remove(normalizedUnitId);
                }
                else
                {
                    VariantsByUnit[normalizedUnitId] = normalizedVariants;
                }
            }
        }

        public static bool TryGet(string unitId, string variantId, out RuntimeUnitVisualVariant variant)
        {
            variant = null;
            if (string.IsNullOrWhiteSpace(unitId) || string.IsNullOrWhiteSpace(variantId))
            {
                return false;
            }

            lock (Gate)
            {
                Dictionary<string, RuntimeUnitVisualVariant> unitVariants;
                if (!VariantsByUnit.TryGetValue(unitId.Trim(), out unitVariants) || unitVariants == null)
                {
                    return false;
                }

                return unitVariants.TryGetValue(variantId.Trim(), out variant) && variant != null;
            }
        }

        public static bool TryGetPrimary(string unitId, out RuntimeUnitVisualVariant primaryVariant)
        {
            primaryVariant = null;
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return false;
            }

            lock (Gate)
            {
                Dictionary<string, RuntimeUnitVisualVariant> unitVariants;
                if (!VariantsByUnit.TryGetValue(unitId.Trim(), out unitVariants) || unitVariants == null)
                {
                    return false;
                }

                foreach (RuntimeUnitVisualVariant v in unitVariants.Values)
                {
                    if (v != null && v.IsPrimary)
                    {
                        primaryVariant = v;
                        return true;
                    }
                }

                // Fallback to first registered variant
                foreach (RuntimeUnitVisualVariant v in unitVariants.Values)
                {
                    if (v != null)
                    {
                        primaryVariant = v;
                        return true;
                    }
                }

                return false;
            }
        }

        public static bool HasVariants(string unitId)
        {
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return false;
            }

            lock (Gate)
            {
                Dictionary<string, RuntimeUnitVisualVariant> unitVariants;
                return VariantsByUnit.TryGetValue(unitId.Trim(), out unitVariants) &&
                    unitVariants != null && unitVariants.Count > 0;
            }
        }

        public static IReadOnlyList<string> GetVisualVariantIds(string unitId)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return result.AsReadOnly();
            }

            lock (Gate)
            {
                Dictionary<string, RuntimeUnitVisualVariant> unitVariants;
                if (VariantsByUnit.TryGetValue(unitId.Trim(), out unitVariants) && unitVariants != null)
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (RuntimeUnitVisualVariant v in unitVariants.Values)
                    {
                        if (v != null && !string.IsNullOrWhiteSpace(v.VariantId) && seen.Add(v.VariantId))
                        {
                            result.Add(v.VariantId);
                        }
                    }
                }
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result.AsReadOnly();
        }

        public static IReadOnlyList<RuntimeUnitVisualVariant> GetAllVariants(string unitId)
        {
            var result = new List<RuntimeUnitVisualVariant>();
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return result.AsReadOnly();
            }

            lock (Gate)
            {
                Dictionary<string, RuntimeUnitVisualVariant> unitVariants;
                if (VariantsByUnit.TryGetValue(unitId.Trim(), out unitVariants) && unitVariants != null)
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (RuntimeUnitVisualVariant v in unitVariants.Values)
                    {
                        if (v != null && !string.IsNullOrWhiteSpace(v.VariantId) && seen.Add(v.VariantId))
                        {
                            result.Add(v);
                        }
                    }
                }
            }

            return result.AsReadOnly();
        }

        public static int GetRegisteredUnitCount()
        {
            lock (Gate)
            {
                return VariantsByUnit.Count;
            }
        }

        public static int GetTotalVariantCount()
        {
            lock (Gate)
            {
                int count = 0;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Dictionary<string, RuntimeUnitVisualVariant> dict in VariantsByUnit.Values)
                {
                    if (dict != null)
                    {
                        foreach (RuntimeUnitVisualVariant v in dict.Values)
                        {
                            if (v != null && seen.Add(v.VariantId))
                            {
                                count++;
                            }
                        }
                    }
                }
                return count;
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                VariantsByUnit.Clear();
            }
        }
    }
}
