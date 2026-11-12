using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry entry for an upgrade in the balancing store.
    /// </summary>
    public sealed class UpgradeDetailEntry
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
        public bool ApplyToFactoryWhereApplicable { get; }
        public int CardChangeCount { get; }
        public bool IsModded { get; }

        public UpgradeDetailEntry(
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
            bool applyToFactoryWhereApplicable,
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
            ApplyToFactoryWhereApplicable = applyToFactoryWhereApplicable;
            CardChangeCount = cardChangeCount;
            IsModded = isModded;
        }
    }

    /// <summary>
    /// Telemetry census of all registered card upgrades across the game balancing store.
    /// </summary>
    public sealed class UpgradeCensus
    {
        public int OmittedUpgradeCount { get; }
        public int TotalUpgrades { get; }
        public int ActiveUpgrades { get; }
        public int ModdedUpgrades { get; }
        public IReadOnlyList<UpgradeDetailEntry> Upgrades { get; }
        public bool IsReady { get; }

        public UpgradeCensus(
            int omittedUpgradeCount,
            int totalUpgrades,
            int activeUpgrades,
            int moddedUpgrades,
            IReadOnlyList<UpgradeDetailEntry> upgrades,
            bool isReady)
        {
            OmittedUpgradeCount = omittedUpgradeCount;
            TotalUpgrades = totalUpgrades;
            ActiveUpgrades = activeUpgrades;
            ModdedUpgrades = moddedUpgrades;
            Upgrades = upgrades ?? Array.Empty<UpgradeDetailEntry>();
            IsReady = isReady;
        }

        public static UpgradeCensus Empty { get; } =
            new UpgradeCensus(0, 0, 0, 0, Array.Empty<UpgradeDetailEntry>(), false);
    }

    /// <summary>
    /// Diagnostic collector that inspects registered upgrades in the UpgradeBalancingStore.
    /// </summary>
    public static class RuntimeUpgradeCensus
    {
        private const int DefaultMaxEntries = 64;
        private static readonly BindingFlags StaticFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static UpgradeCensus Current(int maxEntries = DefaultMaxEntries)
        {
            try
            {
                Type storeType = Type.GetType("UpgradeBalancingStore, Assembly-CSharp") ??
                                 Type.GetType("UpgradeBalancingStore");
                if (storeType == null)
                {
                    return UpgradeCensus.Empty;
                }

                FieldInfo assetField = storeType.GetField("_upgradeBalancingScriptableObject", StaticFlags);
                if (assetField == null)
                {
                    return UpgradeCensus.Empty;
                }

                object nativeAsset = assetField.GetValue(null);
                if (nativeAsset == null)
                {
                    return UpgradeCensus.Empty;
                }

                FieldInfo parametersField = nativeAsset.GetType().GetField("parameters", InstanceFlags);
                if (parametersField == null)
                {
                    return UpgradeCensus.Empty;
                }

                IList rawList = parametersField.GetValue(nativeAsset) as IList;
                if (rawList == null)
                {
                    return UpgradeCensus.Empty;
                }

                int totalCount = rawList.Count;
                int activeCount = 0;
                int moddedCount = 0;
                var details = new List<UpgradeDetailEntry>(Math.Min(totalCount, maxEntries));

                for (int i = 0; i < totalCount; i++)
                {
                    object param = rawList[i];
                    if (param == null)
                    {
                        continue;
                    }

                    Type pType = param.GetType();
                    string id = pType.GetField("upgradeId", InstanceFlags)?.GetValue(param) as string ?? string.Empty;
                    string rarity = pType.GetField("rarity", InstanceFlags)?.GetValue(param)?.ToString() ?? string.Empty;
                    string tech = pType.GetField("tech", InstanceFlags)?.GetValue(param)?.ToString() ?? string.Empty;
                    int coins = (int)(pType.GetField("coinsAmount", InstanceFlags)?.GetValue(param) ?? 0);
                    int exp = (int)(pType.GetField("neededExperienceLevel", InstanceFlags)?.GetValue(param) ?? 0);
                    bool demo = (bool)(pType.GetField("isAllowedForDemo", InstanceFlags)?.GetValue(param) ?? false);
                    bool spec = (bool)(pType.GetField("isForSpecialists", InstanceFlags)?.GetValue(param) ?? false);
                    bool inactive = (bool)(pType.GetField("inactive", InstanceFlags)?.GetValue(param) ?? false);
                    string offered = pType.GetField("offeredSystemTags", InstanceFlags)?.GetValue(param)?.ToString() ?? string.Empty;
                    string needed = pType.GetField("neededSystemTags", InstanceFlags)?.GetValue(param)?.ToString() ?? string.Empty;

                    bool applyToFactory = false;
                    int changeCount = 0;
                    object scriptableObj = pType.GetField("scriptableObject", InstanceFlags)?.GetValue(param);
                    if (scriptableObj != null)
                    {
                        Type soType = scriptableObj.GetType();
                        applyToFactory = (bool)(soType.GetField("applyToFactoryWhereApplicable", InstanceFlags)?.GetValue(scriptableObj) ?? false);
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
                        details.Add(new UpgradeDetailEntry(
                            id, rarity, tech, coins, exp, demo, spec, inactive,
                            offered, needed, applyToFactory, changeCount, isModded));
                    }
                }

                int omitted = Math.Max(0, totalCount - maxEntries);
                return new UpgradeCensus(omitted, totalCount, activeCount, moddedCount, details, totalCount > 0);
            }
            catch
            {
                return UpgradeCensus.Empty;
            }
        }
    }
}
