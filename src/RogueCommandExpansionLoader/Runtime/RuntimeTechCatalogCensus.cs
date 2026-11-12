using System;
using System.Collections.Generic;
using System.Reflection;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry of all registered upgrades, research branches, and relics
    /// in the active game balancing stores. Decoupled from Unity types.
    /// </summary>
    public sealed class TechCatalogCensus
    {
        public int OmittedUpgradeCount { get; }
        public int OmittedResearchCount { get; }
        public int OmittedRelicCount { get; }
        public int TotalUpgrades { get; }
        public int TotalResearch { get; }
        public int TotalRelics { get; }
        public IReadOnlyList<string> UpgradeIds { get; }
        public IReadOnlyList<string> ResearchIds { get; }
        public IReadOnlyList<string> RelicIds { get; }
        public bool IsTechCatalogReady { get; }

        public TechCatalogCensus(
            int omittedUpgradeCount,
            int omittedResearchCount,
            int omittedRelicCount,
            int totalUpgrades,
            int totalResearch,
            int totalRelics,
            IReadOnlyList<string> upgradeIds,
            IReadOnlyList<string> researchIds,
            IReadOnlyList<string> relicIds,
            bool isTechCatalogReady)
        {
            OmittedUpgradeCount = omittedUpgradeCount;
            OmittedResearchCount = omittedResearchCount;
            OmittedRelicCount = omittedRelicCount;
            TotalUpgrades = totalUpgrades;
            TotalResearch = totalResearch;
            TotalRelics = totalRelics;
            UpgradeIds = upgradeIds ?? Array.Empty<string>();
            ResearchIds = researchIds ?? Array.Empty<string>();
            RelicIds = relicIds ?? Array.Empty<string>();
            IsTechCatalogReady = isTechCatalogReady;
        }

        public static TechCatalogCensus Empty { get; } =
            new TechCatalogCensus(0, 0, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), false);
    }

    /// <summary>
    /// Diagnostic collector that inspects registered tech upgrades, research cards, and relics
    /// across the live balancing stores.
    /// </summary>
    public static class RuntimeTechCatalogCensus
    {
        private const int MaxEntriesPerCollection = 64;
        private static readonly BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        public static TechCatalogCensus Current()
        {
            try
            {
                var upgrades = ReadIdList(typeof(UpgradeBalancingStore), "AllUpgradeIds");
                var research = ReadIdList(typeof(ResearchBalancingStore), "AllResearchIds");
                var relics = ReadIdList(typeof(RelicBalancingStore), "AllRelicIds");

                int totalUpgrades = upgrades.Count;
                int totalResearch = research.Count;
                int totalRelics = relics.Count;

                int omittedUpgrades = Math.Max(0, totalUpgrades - MaxEntriesPerCollection);
                int omittedResearch = Math.Max(0, totalResearch - MaxEntriesPerCollection);
                int omittedRelics = Math.Max(0, totalRelics - MaxEntriesPerCollection);

                var cappedUpgrades = CapList(upgrades, MaxEntriesPerCollection);
                var cappedResearch = CapList(research, MaxEntriesPerCollection);
                var cappedRelics = CapList(relics, MaxEntriesPerCollection);

                bool isReady = totalUpgrades > 0 || totalResearch > 0 || totalRelics > 0;

                return new TechCatalogCensus(
                    omittedUpgrades,
                    omittedResearch,
                    omittedRelics,
                    totalUpgrades,
                    totalResearch,
                    totalRelics,
                    cappedUpgrades,
                    cappedResearch,
                    cappedRelics,
                    isReady);
            }
            catch
            {
                return TechCatalogCensus.Empty;
            }
        }

        private static List<string> ReadIdList(Type storeType, string methodName)
        {
            var result = new List<string>();
            try
            {
                MethodInfo m = storeType.GetMethod(methodName, Flags, null, Type.EmptyTypes, null);
                if (m != null)
                {
                    var raw = m.Invoke(null, null) as IEnumerable<string>;
                    if (raw != null)
                    {
                        result.AddRange(raw);
                    }
                }
            }
            catch { }
            return result;
        }

        private static List<string> CapList(List<string> source, int limit)
        {
            if (source.Count <= limit)
            {
                return new List<string>(source);
            }

            var capped = new List<string>(limit);
            for (int i = 0; i < limit; i++)
            {
                capped.Add(source[i]);
            }
            return capped;
        }
    }
}
