using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// One live AI-controlled entity (unit or building) captured for agent-readable
    /// diagnostics, enriched with its base balance parameters read from the game's
    /// own EntityBalancingStore. Derived from the real ExistingControllers registry
    /// (statically verified against Assembly-CSharp.dll) plus RuntimeEntityBalanceLookup.
    /// </summary>
    public readonly struct AiRosterRow
    {
        public AiRosterRow(
            string entityKeyId,
            bool isBuilding,
            int cost,
            int maxHealth,
            float moveSpeed,
            float dps,
            float damage1,
            float weaponRange,
            int uniqueEntityId = 0)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            IsBuilding = isBuilding;
            Cost = cost;
            MaxHealth = maxHealth;
            MoveSpeed = moveSpeed;
            Dps = dps;
            Damage1 = damage1;
            WeaponRange = weaponRange;
            UniqueEntityId = uniqueEntityId;
        }

        public string EntityKeyId { get; }
        public int UniqueEntityId { get; }
        public bool IsBuilding { get; }
        public int Cost { get; }
        public int MaxHealth { get; }
        public float MoveSpeed { get; }
        public float Dps { get; }
        public float Damage1 { get; }
        public float WeaponRange { get; }
    }

    /// <summary>
    /// Pure, agent-readable snapshot of the live AI roster. Contains no game-type
    /// references so it can be serialized and tested in isolation.
    /// </summary>
    public sealed class AiRosterCensus
    {
        public int AiUnitCount { get; }
        public int AiBuildingCount { get; }
        public IReadOnlyList<AiRosterRow> AiUnitRows { get; }
        public IReadOnlyList<AiRosterRow> AiBuildingRows { get; }

        public AiRosterCensus(
            int aiUnitCount,
            int aiBuildingCount,
            IReadOnlyList<AiRosterRow> aiUnitRows,
            IReadOnlyList<AiRosterRow> aiBuildingRows)
        {
            AiUnitCount = aiUnitCount;
            AiBuildingCount = aiBuildingCount;
            AiUnitRows = aiUnitRows ?? new List<AiRosterRow>().AsReadOnly();
            AiBuildingRows = aiBuildingRows ?? new List<AiRosterRow>().AsReadOnly();
        }

        public static AiRosterCensus Empty { get; } =
            new AiRosterCensus(0, 0, new List<AiRosterRow>().AsReadOnly(), new List<AiRosterRow>().AsReadOnly());
    }

    /// <summary>
    /// Reads the live AI roster through the real ExistingControllers registry and
    /// enriches each entry with base balance parameters via RuntimeEntityBalanceLookup.
    /// Degrades to AiRosterCensus.Empty on any failure.
    /// </summary>
    public static class RuntimeAiRosterCensus
    {
        public static AiRosterCensus Current()
        {
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return AiRosterCensus.Empty;
                }

                HashSet<EntityController> aiUnits = existing.AiUnits();
                if (aiUnits == null)
                {
                    aiUnits = new HashSet<EntityController>();
                }

                HashSet<EntityController> aiBuildings = null;
                try
                {
                    aiBuildings = existing.AiBuildings();
                }
                catch
                {
                    aiBuildings = new HashSet<EntityController>();
                }
                if (aiBuildings == null)
                {
                    aiBuildings = new HashSet<EntityController>();
                }

                var unitRows = new List<AiRosterRow>();
                foreach (EntityController entity in aiUnits)
                {
                    if (entity == null)
                    {
                        continue;
                    }
                    EntityBalanceRow balance = RuntimeEntityBalanceLookup.Current(entity.EntityId);
                    unitRows.Add(new AiRosterRow(
                        entity.EntityId,
                        false,
                        balance.Cost,
                        balance.MaxHealth,
                        balance.MoveSpeed,
                        balance.Dps,
                        balance.Damage1,
                        balance.WeaponRange,
                        entity.UniqueEntityId));
                }

                var buildingRows = new List<AiRosterRow>();
                foreach (EntityController entity in aiBuildings)
                {
                    if (entity == null)
                    {
                        continue;
                    }
                    EntityBalanceRow balance = RuntimeEntityBalanceLookup.Current(entity.EntityId);
                    buildingRows.Add(new AiRosterRow(
                        entity.EntityId,
                        true,
                        balance.Cost,
                        balance.MaxHealth,
                        balance.MoveSpeed,
                        balance.Dps,
                        balance.Damage1,
                        balance.WeaponRange,
                        entity.UniqueEntityId));
                }

                return new AiRosterCensus(
                    aiUnits.Count,
                    aiBuildings.Count,
                    unitRows.AsReadOnly(),
                    buildingRows.AsReadOnly());
            }
            catch
            {
                return AiRosterCensus.Empty;
            }
        }
    }
}
