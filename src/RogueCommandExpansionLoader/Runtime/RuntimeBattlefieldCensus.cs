using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// One live battlefield entity captured for agent-readable diagnostics.
    /// Derived from the real <c>EntityController</c> state (statically verified
    /// against Assembly-CSharp.dll) without copying game source.
    /// </summary>
    public readonly struct BattlefieldUnitRow
    {
        public BattlefieldUnitRow(
            string entityKeyId,
            bool isBuilding,
            float currentHealth,
            float maxHealth,
            float currentArmor,
            float currentShield,
            int currentRank,
            bool isControlledByAi,
            bool isControlledByPlayer,
            bool isAlive,
            int uniqueEntityId = 0)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            IsBuilding = isBuilding;
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
            HealthRatio = maxHealth > 0.001f ? (currentHealth / maxHealth) : 0f;
            CurrentArmor = currentArmor;
            CurrentShield = currentShield;
            CurrentRank = currentRank;
            IsControlledByAi = isControlledByAi;
            IsControlledByPlayer = isControlledByPlayer;
            IsAlive = isAlive;
            UniqueEntityId = uniqueEntityId;
        }

        public string EntityKeyId { get; }
        public int UniqueEntityId { get; }
        public bool IsBuilding { get; }
        public float CurrentHealth { get; }
        public float MaxHealth { get; }
        public float HealthRatio { get; }
        public float CurrentArmor { get; }
        public float CurrentShield { get; }
        public int CurrentRank { get; }
        public bool IsControlledByAi { get; }
        public bool IsControlledByPlayer { get; }
        public bool IsAlive { get; }
    }

    /// <summary>
    /// Pure, agent-readable snapshot of the live battlefield. Contains no
    /// game-type references so it can be serialized and tested in isolation.
    /// </summary>
    public sealed class BattlefieldCensus
    {
        public int BattlefieldEntityCount { get; }
        public int PlayerUnitCount { get; }
        public int AiUnitCount { get; }
        public int AiBuildingCount { get; }
        public int HarvesterCount { get; }
        public IReadOnlyList<BattlefieldUnitRow> PlayerUnitRows { get; }

        public BattlefieldCensus(
            int battlefieldEntityCount,
            int playerUnitCount,
            int aiUnitCount,
            int aiBuildingCount,
            int harvesterCount,
            IReadOnlyList<BattlefieldUnitRow> playerUnitRows)
        {
            BattlefieldEntityCount = battlefieldEntityCount;
            PlayerUnitCount = playerUnitCount;
            AiUnitCount = aiUnitCount;
            AiBuildingCount = aiBuildingCount;
            HarvesterCount = harvesterCount;
            PlayerUnitRows = playerUnitRows ?? new List<BattlefieldUnitRow>().AsReadOnly();
        }

        public static BattlefieldCensus Empty { get; } =
            new BattlefieldCensus(0, 0, 0, 0, 0, new List<BattlefieldUnitRow>().AsReadOnly());
    }

    /// <summary>
    /// Reads the live battlefield through the real <c>ExistingControllers</c>
    /// registry (statically verified against Assembly-CSharp.dll) and projects
    /// it into a <see cref="BattlefieldCensus"/>. Degrades to
    /// <see cref="BattlefieldCensus.Empty"/> when the registry or any entity is
    /// unavailable, so diagnostics never throw during a run.
    /// </summary>
    public static class RuntimeBattlefieldCensus
    {
        public static BattlefieldCensus Current()
        {
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return BattlefieldCensus.Empty;
                }

                HashSet<EntityController> playerUnits = existing.PlayerUnits();
                if (playerUnits == null)
                {
                    playerUnits = new HashSet<EntityController>();
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

                HashSet<EntityController> harvesters = existing.Harvesters;
                if (harvesters == null)
                {
                    harvesters = new HashSet<EntityController>();
                }

                // Total is derived from the three verified collections rather than
                // from an unverified Entities() helper, keeping this safe across
                // game and stub builds.
                int total = playerUnits.Count + aiUnits.Count + aiBuildings.Count;

                var rows = new List<BattlefieldUnitRow>();
                foreach (EntityController entity in playerUnits)
                {
                    if (entity == null)
                    {
                        continue;
                    }

                    rows.Add(new BattlefieldUnitRow(
                        entity.EntityId,
                        entity.IsBuilding,
                        entity.CurrentHealth,
                        entity.MaxHealth,
                        entity.CurrentArmor,
                        entity.CurrentShield,
                        entity.CurrentRank,
                        entity.IsControlledByAi,
                        entity.IsControlledByPlayer,
                        !entity.DestroyHasBeenStarted && entity.CurrentHealth > 0f,
                        entity.UniqueEntityId));
                }

                return new BattlefieldCensus(
                    total,
                    playerUnits.Count,
                    aiUnits.Count,
                    aiBuildings.Count,
                    harvesters.Count,
                    rows.AsReadOnly());
            }
            catch
            {
                return BattlefieldCensus.Empty;
            }
        }
    }
}
