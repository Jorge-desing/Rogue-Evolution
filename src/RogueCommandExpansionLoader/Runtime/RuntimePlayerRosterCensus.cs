using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// One live player-controlled entity captured for agent-readable diagnostics,
    /// enriched with its base balance parameters read from the game's own
    /// EntityBalancingStore. Complements RuntimeBattlefieldCensus (which only
    /// carries live state) with the authored base stats.
    /// </summary>
    public readonly struct PlayerRosterRow
    {
        public PlayerRosterRow(
            string entityKeyId,
            bool isBuilding,
            float currentHealth,
            float maxHealth,
            float healthRatio,
            int cost,
            float moveSpeed,
            float dps,
            float damage1,
            float weaponRange)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            IsBuilding = isBuilding;
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
            HealthRatio = maxHealth > 0.001f ? (currentHealth / maxHealth) : 0f;
            Cost = cost;
            MoveSpeed = moveSpeed;
            Dps = dps;
            Damage1 = damage1;
            WeaponRange = weaponRange;
        }

        public string EntityKeyId { get; }
        public bool IsBuilding { get; }
        public float CurrentHealth { get; }
        public float MaxHealth { get; }
        public float HealthRatio { get; }
        public int Cost { get; }
        public float MoveSpeed { get; }
        public float Dps { get; }
        public float Damage1 { get; }
        public float WeaponRange { get; }
    }

    public sealed class PlayerRosterCensus
    {
        public int PlayerUnitCount { get; }
        public IReadOnlyList<PlayerRosterRow> PlayerUnitRows { get; }

        public PlayerRosterCensus(int playerUnitCount, IReadOnlyList<PlayerRosterRow> playerUnitRows)
        {
            PlayerUnitCount = playerUnitCount;
            PlayerUnitRows = playerUnitRows ?? new List<PlayerRosterRow>().AsReadOnly();
        }

        public static PlayerRosterCensus Empty { get; } =
            new PlayerRosterCensus(0, new List<PlayerRosterRow>().AsReadOnly());
    }

    public static class RuntimePlayerRosterCensus
    {
        public static PlayerRosterCensus Current()
        {
            try
            {
                BattlefieldCensus census = RuntimeBattlefieldCensus.Current();
                if (census == null || census.PlayerUnitRows == null)
                {
                    return PlayerRosterCensus.Empty;
                }

                var rows = new List<PlayerRosterRow>();
                foreach (BattlefieldUnitRow battlefieldRow in census.PlayerUnitRows)
                {
                    EntityBalanceRow balance = RuntimeEntityBalanceLookup.Current(battlefieldRow.EntityKeyId);
                    rows.Add(new PlayerRosterRow(
                        battlefieldRow.EntityKeyId,
                        battlefieldRow.IsBuilding,
                        battlefieldRow.CurrentHealth,
                        battlefieldRow.MaxHealth,
                        battlefieldRow.HealthRatio,
                        balance.Cost,
                        balance.MoveSpeed,
                        balance.Dps,
                        balance.Damage1,
                        balance.WeaponRange));
                }

                return new PlayerRosterCensus(census.PlayerUnitCount, rows.AsReadOnly());
            }
            catch
            {
                return PlayerRosterCensus.Empty;
            }
        }
    }
}