using System;
using System.Collections.Generic;
using System.Reflection;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry of all registered unit, building, and factory entity IDs
    /// in the active game balancing store. Decoupled from Unity types to support clean-room MCP telemetry.
    /// </summary>
    public sealed class EntityCatalogCensus
    {
        public int OmittedUnitCount { get; }
        public int OmittedBuildingCount { get; }
        public int OmittedFactoryCount { get; }
        public int TotalUnits { get; }
        public int TotalBuildings { get; }
        public int TotalFactories { get; }
        public IReadOnlyList<string> UnitIds { get; }
        public IReadOnlyList<string> BuildingIds { get; }
        public IReadOnlyList<string> FactoryIds { get; }
        public bool IsCatalogReady { get; }

        public EntityCatalogCensus(
            int omittedUnitCount,
            int omittedBuildingCount,
            int omittedFactoryCount,
            int totalUnits,
            int totalBuildings,
            int totalFactories,
            IReadOnlyList<string> unitIds,
            IReadOnlyList<string> buildingIds,
            IReadOnlyList<string> factoryIds,
            bool isCatalogReady)
        {
            OmittedUnitCount = omittedUnitCount;
            OmittedBuildingCount = omittedBuildingCount;
            OmittedFactoryCount = omittedFactoryCount;
            TotalUnits = totalUnits;
            TotalBuildings = totalBuildings;
            TotalFactories = totalFactories;
            UnitIds = unitIds ?? Array.Empty<string>();
            BuildingIds = buildingIds ?? Array.Empty<string>();
            FactoryIds = factoryIds ?? Array.Empty<string>();
            IsCatalogReady = isCatalogReady;
        }

        public static EntityCatalogCensus Empty { get; } =
            new EntityCatalogCensus(0, 0, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), false);
    }

    /// <summary>
    /// Diagnostic collector that inspects registered entity IDs across the live balancing store.
    /// </summary>
    public static class RuntimeEntityCatalogCensus
    {
        private const int MaxEntriesPerCollection = 64;

        public static EntityCatalogCensus Current()
        {
            try
            {
                Type storeType = typeof(EntityBalancingStore);
                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

                var units = ReadEntityList(storeType, flags, "AllEntityIdsOfUnits");
                var buildings = ReadEntityList(storeType, flags, "AllEntityIdsOfBuildings");
                var factories = ReadEntityList(storeType, flags, "AllEntityIdsOfFactories");

                int totalUnits = units.Count;
                int totalBuildings = buildings.Count;
                int totalFactories = factories.Count;

                int omittedUnits = Math.Max(0, totalUnits - MaxEntriesPerCollection);
                int omittedBuildings = Math.Max(0, totalBuildings - MaxEntriesPerCollection);
                int omittedFactories = Math.Max(0, totalFactories - MaxEntriesPerCollection);

                var cappedUnits = CapList(units, MaxEntriesPerCollection);
                var cappedBuildings = CapList(buildings, MaxEntriesPerCollection);
                var cappedFactories = CapList(factories, MaxEntriesPerCollection);

                bool isReady = totalUnits > 0 || totalBuildings > 0 || totalFactories > 0;

                return new EntityCatalogCensus(
                    omittedUnits,
                    omittedBuildings,
                    omittedFactories,
                    totalUnits,
                    totalBuildings,
                    totalFactories,
                    cappedUnits,
                    cappedBuildings,
                    cappedFactories,
                    isReady);
            }
            catch
            {
                return EntityCatalogCensus.Empty;
            }
        }

        private static List<string> ReadEntityList(Type type, BindingFlags flags, string methodName)
        {
            var result = new List<string>();
            try
            {
                MethodInfo method = type.GetMethod(methodName, flags, null, Type.EmptyTypes, null);
                if (method != null)
                {
                    var raw = method.Invoke(null, null) as IEnumerable<string>;
                    if (raw != null)
                    {
                        result.AddRange(raw);
                    }
                }
            }
            catch
            {
                // Degrade gracefully
            }
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
