using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry entry for a relic in the balancing store.
    /// </summary>
    public sealed class RelicDetailEntry
    {
        public string Id { get; }
        public string Rarity { get; }
        public string Tech { get; }
        public int CoinsAmount { get; }
        public int NeededExperienceLevel { get; }
        public bool IsAllowedForDemo { get; }
        public bool IsForSpecialists { get; }
        public bool Inactive { get; }
        public string OfferedSystemTags { get; }
        public string NeededSystemTags { get; }
        public int CardChangeCount { get; }
        public bool IsModded { get; }

        public RelicDetailEntry(
            string id,
            string rarity,
            string tech,
            int coinsAmount,
            int neededExperienceLevel,
            bool isAllowedForDemo,
            bool isForSpecialists,
            bool inactive,
            string offeredSystemTags,
            string neededSystemTags,
            int cardChangeCount,
            bool isModded)
        {
            Id = id ?? string.Empty;
            Rarity = rarity ?? string.Empty;
            Tech = tech ?? string.Empty;
            CoinsAmount = coinsAmount;
            NeededExperienceLevel = neededExperienceLevel;
            IsAllowedForDemo = isAllowedForDemo;
            IsForSpecialists = isForSpecialists;
            Inactive = inactive;
            OfferedSystemTags = offeredSystemTags ?? string.Empty;
            NeededSystemTags = neededSystemTags ?? string.Empty;
            CardChangeCount = cardChangeCount;
            IsModded = isModded;
        }
    }

    /// <summary>
    /// Telemetry census of all registered player relics across the game balancing store.
    /// </summary>
    public sealed class RelicCensus
    {
        public int OmittedRelicCount { get; }
        public int TotalRelics { get; }
        public int ActiveRelics { get; }
        public int ModdedRelics { get; }
        public IReadOnlyList<RelicDetailEntry> Relics { get; }
        public bool IsReady { get; }

        public RelicCensus(
            int omittedRelicCount,
            int totalRelics,
            int activeRelics,
            int moddedRelics,
            IReadOnlyList<RelicDetailEntry> relics,
            bool isReady)
        {
            OmittedRelicCount = omittedRelicCount;
            TotalRelics = totalRelics;
            ActiveRelics = activeRelics;
            ModdedRelics = moddedRelics;
            Relics = relics ?? Array.Empty<RelicDetailEntry>();
            IsReady = isReady;
        }

        public static RelicCensus Empty { get; } =
            new RelicCensus(0, 0, 0, 0, Array.Empty<RelicDetailEntry>(), false);
    }

    /// <summary>
    /// Diagnostic collector that inspects registered relics in the RelicBalancingStore.
    /// </summary>
    public static class RuntimeRelicCensus
    {
        private const int DefaultMaxEntries = 64;
        private static readonly BindingFlags StaticFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static RelicCensus Current(int maxEntries = DefaultMaxEntries)
        {
            try
            {
                Type storeType = Type.GetType("RelicBalancingStore, Assembly-CSharp") ??
                                 Type.GetType("RelicBalancingStore");
                if (storeType == null)
                {
                    return RelicCensus.Empty;
                }

                FieldInfo assetField = storeType.GetField("_relicBalancingScriptableObject", StaticFlags);
                if (assetField == null)
                {
                    return RelicCensus.Empty;
                }

                object nativeAsset = assetField.GetValue(null);
                if (nativeAsset == null)
                {
                    return RelicCensus.Empty;
                }

                FieldInfo parametersField = nativeAsset.GetType().GetField("parameters", InstanceFlags);
                if (parametersField == null)
                {
                    return RelicCensus.Empty;
                }

                IList rawList = parametersField.GetValue(nativeAsset) as IList;
                if (rawList == null)
                {
                    return RelicCensus.Empty;
                }

                int totalCount = rawList.Count;
                int activeCount = 0;
                int moddedCount = 0;
                var details = new List<RelicDetailEntry>(Math.Min(totalCount, maxEntries));

                for (int i = 0; i < totalCount; i++)
                {
                    object param = rawList[i];
                    if (param == null)
                    {
                        continue;
                    }

                    Type pType = param.GetType();
                    string id = pType.GetField("relicId", InstanceFlags)?.GetValue(param) as string ?? string.Empty;
                    string rarity = pType.GetField("rarity", InstanceFlags)?.GetValue(param)?.ToString() ?? string.Empty;
                    string tech = pType.GetField("tech", InstanceFlags)?.GetValue(param)?.ToString() ?? string.Empty;
                    int coins = (int)(pType.GetField("coinsAmount", InstanceFlags)?.GetValue(param) ?? 0);
                    int exp = (int)(pType.GetField("neededExperienceLevel", InstanceFlags)?.GetValue(param) ?? 0);
                    bool demo = (bool)(pType.GetField("isAllowedForDemo", InstanceFlags)?.GetValue(param) ?? false);
                    bool spec = (bool)(pType.GetField("isForSpecialists", InstanceFlags)?.GetValue(param) ?? false);
                    bool inactive = (bool)(pType.GetField("inactive", InstanceFlags)?.GetValue(param) ?? false);
                    string offered = pType.GetField("offeredSystemTags", InstanceFlags)?.GetValue(param)?.ToString() ?? string.Empty;
                    string needed = pType.GetField("neededSystemTags", InstanceFlags)?.GetValue(param)?.ToString() ?? string.Empty;

                    int changeCount = 0;
                    object scriptableObj = pType.GetField("scriptableObject", InstanceFlags)?.GetValue(param);
                    if (scriptableObj != null)
                    {
                        Type soType = scriptableObj.GetType();
                        IList changes = soType.GetField("cardChanges", InstanceFlags)?.GetValue(scriptableObj) as IList;
                        if (changes != null)
                        {
                            changeCount = changes.Count;
                        }
                    }

                    if (!inactive)
                    {
                        activeCount++;
                    }

                    bool isModded = id.Contains(":") || id.StartsWith("rcl:", StringComparison.OrdinalIgnoreCase);
                    if (isModded)
                    {
                        moddedCount++;
                    }

                    if (details.Count < maxEntries)
                    {
                        details.Add(new RelicDetailEntry(
                            id, rarity, tech, coins, exp, demo, spec, inactive,
                            offered, needed, changeCount, isModded));
                    }
                }

                int omitted = Math.Max(0, totalCount - maxEntries);
                return new RelicCensus(omitted, totalCount, activeCount, moddedCount, details, totalCount > 0);
            }
            catch
            {
                return RelicCensus.Empty;
            }
        }
    }
}
