using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// One raw numeric or enum-like balance override declared by a unit variant.
    /// The loader preserves the original text so unsupported instance-only fields fail closed
    /// instead of being silently converted to a different native value.
    /// </summary>
    public sealed class RuntimeUnitVariantOverride
    {
        public RuntimeUnitVariantOverride(string name, string value)
        {
            Name = name == null ? string.Empty : name.Trim();
            Value = value == null ? string.Empty : value.Trim();
        }

        public string Name { get; }
        public string Value { get; }
    }

    /// <summary>
    /// Immutable runtime representation of a declarative ContentUnitDefinition.variants entry.
    /// </summary>
    public sealed class RuntimeUnitVariant
    {
        public RuntimeUnitVariant(
            string variantId,
            string loadout,
            IEnumerable<RuntimeUnitVariantOverride> balanceOverrides)
        {
            VariantId = variantId == null ? string.Empty : variantId.Trim();
            Loadout = loadout == null ? string.Empty : loadout.Trim();

            var copiedOverrides = new List<RuntimeUnitVariantOverride>();
            if (balanceOverrides != null)
            {
                foreach (RuntimeUnitVariantOverride balanceOverride in balanceOverrides)
                {
                    if (balanceOverride != null)
                    {
                        copiedOverrides.Add(new RuntimeUnitVariantOverride(
                            balanceOverride.Name,
                            balanceOverride.Value));
                    }
                }
            }

            BalanceOverrides = copiedOverrides.AsReadOnly();
        }

        public string VariantId { get; }
        public string Loadout { get; }
        public IReadOnlyList<RuntimeUnitVariantOverride> BalanceOverrides { get; }
    }

    /// <summary>
    /// Process-local registry populated by ContentPackRuntimeLoader for unit gameplay variants.
    /// The registry is intentionally independent from Unity objects so it is testable offline.
    /// </summary>
    public static class RuntimeUnitVariantRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Dictionary<string, RuntimeUnitVariant>> VariantsByUnit =
            new Dictionary<string, Dictionary<string, RuntimeUnitVariant>>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string unitId, IEnumerable<RuntimeUnitVariant> variants)
        {
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return;
            }

            var normalizedVariants = new Dictionary<string, RuntimeUnitVariant>(StringComparer.OrdinalIgnoreCase);
            if (variants != null)
            {
                foreach (RuntimeUnitVariant variant in variants)
                {
                    if (variant == null || string.IsNullOrWhiteSpace(variant.VariantId))
                    {
                        continue;
                    }

                    normalizedVariants[variant.VariantId.Trim()] = new RuntimeUnitVariant(
                        variant.VariantId,
                        variant.Loadout,
                        variant.BalanceOverrides);
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

        public static bool TryGet(string unitId, string variantId, out RuntimeUnitVariant variant)
        {
            variant = null;
            if (string.IsNullOrWhiteSpace(unitId) || string.IsNullOrWhiteSpace(variantId))
            {
                return false;
            }

            lock (Gate)
            {
                Dictionary<string, RuntimeUnitVariant> unitVariants;
                if (!VariantsByUnit.TryGetValue(unitId.Trim(), out unitVariants) || unitVariants == null)
                {
                    return false;
                }

                return unitVariants.TryGetValue(variantId.Trim(), out variant) && variant != null;
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
                Dictionary<string, RuntimeUnitVariant> unitVariants;
                return VariantsByUnit.TryGetValue(unitId.Trim(), out unitVariants) &&
                    unitVariants != null && unitVariants.Count > 0;
            }
        }

        public static IReadOnlyList<string> GetVariantIds(string unitId)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return result.AsReadOnly();
            }

            lock (Gate)
            {
                Dictionary<string, RuntimeUnitVariant> unitVariants;
                if (VariantsByUnit.TryGetValue(unitId.Trim(), out unitVariants) && unitVariants != null)
                {
                    result.AddRange(unitVariants.Keys);
                }
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result.AsReadOnly();
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
