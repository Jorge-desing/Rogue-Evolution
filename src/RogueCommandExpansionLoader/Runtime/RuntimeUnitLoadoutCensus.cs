using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure diagnostic telemetry row for an active unit loadout / variant state.
    /// Decoupled from Unity types to support offline testing and MCP inspection.
    /// </summary>
    public readonly struct UnitLoadoutDiagnosticRow
    {
        public UnitLoadoutDiagnosticRow(
            string unitKeyId,
            string entityId,
            string activeVariantId,
            string loadoutName,
            bool isPlayerControlled,
            float moveSpeed,
            float weaponRange,
            float damage,
            float attackCooldown)
        {
            UnitKeyId = unitKeyId ?? string.Empty;
            EntityId = entityId ?? string.Empty;
            ActiveVariantId = activeVariantId ?? string.Empty;
            LoadoutName = loadoutName ?? string.Empty;
            IsPlayerControlled = isPlayerControlled;
            MoveSpeed = moveSpeed;
            WeaponRange = weaponRange;
            Damage = damage;
            AttackCooldown = attackCooldown;
        }

        public string UnitKeyId { get; }
        public string EntityId { get; }
        public string ActiveVariantId { get; }
        public string LoadoutName { get; }
        public bool IsPlayerControlled { get; }
        public float MoveSpeed { get; }
        public float WeaponRange { get; }
        public float Damage { get; }
        public float AttackCooldown { get; }
    }

    /// <summary>
    /// Pure diagnostic telemetry row for an EntityFactory pre-spawn pool.
    /// </summary>
    public readonly struct PrespawnPoolDiagnosticRow
    {
        public PrespawnPoolDiagnosticRow(
            string entityId,
            int targetQuota,
            int prespawnedCount,
            bool isBuilding)
        {
            EntityId = entityId ?? string.Empty;
            TargetQuota = targetQuota;
            PrespawnedCount = prespawnedCount;
            IsBuilding = isBuilding;
        }

        public string EntityId { get; }
        public int TargetQuota { get; }
        public int PrespawnedCount { get; }
        public bool IsBuilding { get; }
    }

    /// <summary>
    /// Pure diagnostic snapshot of active unit loadouts and EntityFactory pre-spawn pools.
    /// Omission counters placed strictly first to respect bounded trace caps.
    /// </summary>
    public sealed class UnitLoadoutAndPoolCensus
    {
        public int OmittedUnitCount { get; }
        public int TotalTrackedUnits { get; }
        public int OmittedPoolCount { get; }
        public int TotalPoolCount { get; }
        public IReadOnlyList<UnitLoadoutDiagnosticRow> UnitRows { get; }
        public IReadOnlyList<PrespawnPoolDiagnosticRow> PoolRows { get; }

        public UnitLoadoutAndPoolCensus(
            int omittedUnitCount,
            int totalTrackedUnits,
            int omittedPoolCount,
            int totalPoolCount,
            IReadOnlyList<UnitLoadoutDiagnosticRow> unitRows,
            IReadOnlyList<PrespawnPoolDiagnosticRow> poolRows)
        {
            OmittedUnitCount = omittedUnitCount;
            TotalTrackedUnits = totalTrackedUnits;
            OmittedPoolCount = omittedPoolCount;
            TotalPoolCount = totalPoolCount;
            UnitRows = unitRows ?? new List<UnitLoadoutDiagnosticRow>().AsReadOnly();
            PoolRows = poolRows ?? new List<PrespawnPoolDiagnosticRow>().AsReadOnly();
        }

        public static UnitLoadoutAndPoolCensus Empty { get; } =
            new UnitLoadoutAndPoolCensus(
                0, 0, 0, 0,
                new List<UnitLoadoutDiagnosticRow>().AsReadOnly(),
                new List<PrespawnPoolDiagnosticRow>().AsReadOnly());
    }

    /// <summary>
    /// Collector that inspects active unit loadouts and pre-spawn pools fail-closed.
    /// </summary>
    public static class RuntimeUnitLoadoutCensus
    {
        public const int MaxTrackedUnits = 32;
        public const int MaxTrackedPools = 32;

        public static UnitLoadoutAndPoolCensus Current()
        {
            try
            {
                int omittedUnits;
                int totalUnits;
                IReadOnlyList<UnitLoadoutDiagnosticRow> unitRows =
                    RuntimeGameplayCommandDirector.GetActiveUnitLoadoutRows(MaxTrackedUnits, out omittedUnits, out totalUnits);

                int omittedPools;
                int totalPools;
                IReadOnlyList<PrespawnPoolDiagnosticRow> poolRows =
                    RuntimeGameplayCommandDirector.GetPrespawnPoolRows(MaxTrackedPools, out omittedPools, out totalPools);

                return new UnitLoadoutAndPoolCensus(
                    omittedUnits, totalUnits, omittedPools, totalPools,
                    unitRows, poolRows);
            }
            catch
            {
                return UnitLoadoutAndPoolCensus.Empty;
            }
        }
    }
}
