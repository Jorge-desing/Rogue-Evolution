using System;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Immutable snapshot of a unit's base balance parameters, read from the game's own
    /// <c>EntityBalancingStore</c> (static getters statically verified against
    /// Assembly-CSharp.dll, SHA-256 d2da384df3318e7ac52db58eb13699ec483790b469effa46205de716825e543b):
    /// <c>IsBuilding(string)</c>, <c>Cost(string, bool)</c>, <c>MaxHealth/MaxShield/MaxMana/MaxRank/
    /// MaxArmor/MoveSpeed/WeaponRange/Damage1/Damage2(string, bool)</c>, <c>Dps(string)</c>.
    /// This is the unit's authored/base config, distinct from the live per-entity state captured by
    /// <see cref="RuntimeBattlefieldCensus"/>.
    /// </summary>
    public readonly struct EntityBalanceRow
    {
        public EntityBalanceRow(
            string entityId,
            bool isBuilding,
            int cost,
            int maxHealth,
            float maxArmor,
            int maxShield,
            float maxMana,
            int maxRank,
            float moveSpeed,
            float weaponRange,
            float dps,
            float damage1,
            float damage2)
        {
            EntityId = entityId ?? string.Empty;
            IsBuilding = isBuilding;
            Cost = cost;
            MaxHealth = maxHealth;
            MaxArmor = maxArmor;
            MaxShield = maxShield;
            MaxMana = maxMana;
            MaxRank = maxRank;
            MoveSpeed = moveSpeed;
            WeaponRange = weaponRange;
            Dps = dps;
            Damage1 = damage1;
            Damage2 = damage2;
        }

        public string EntityId { get; }
        public bool IsBuilding { get; }
        public int Cost { get; }
        public int MaxHealth { get; }
        public float MaxArmor { get; }
        public int MaxShield { get; }
        public float MaxMana { get; }
        public int MaxRank { get; }
        public float MoveSpeed { get; }
        public float WeaponRange { get; }
        public float Dps { get; }
        public float Damage1 { get; }
        public float Damage2 { get; }
    }

    /// <summary>
    /// Reads a single unit's base balance parameters from the game's <c>EntityBalancingStore</c>.
    /// Every getter is guarded independently: if the store is not initialized yet or an individual
    /// getter throws, that field falls back to a neutral value instead of aborting the whole row, so
    /// callers never crash on an uninitialized balance store. Stat modifications themselves stay on
    /// the native data-driven relic pipeline (RelicBalancingStore / CardChangeScriptableObject); this
    /// type only inspects the base numbers.
    /// </summary>
    public static class RuntimeEntityBalanceLookup
    {
        public static EntityBalanceRow Current(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return default;
            }

            try
            {
                return new EntityBalanceRow(
                    entityId,
                    Guard(() => EntityBalancingStore.IsBuilding(entityId), false),
                    Guard(() => EntityBalancingStore.Cost(entityId, false), 0),
                    Guard(() => EntityBalancingStore.MaxHealth(entityId, false), 0),
                    Guard(() => EntityBalancingStore.MaxArmor(entityId, false), 0f),
                    Guard(() => EntityBalancingStore.MaxShield(entityId, false), 0),
                    Guard(() => EntityBalancingStore.MaxMana(entityId, false), 0f),
                    Guard(() => EntityBalancingStore.MaxRank(entityId, false), 0),
                    Guard(() => EntityBalancingStore.MoveSpeed(entityId, false), 0f),
                    Guard(() => EntityBalancingStore.WeaponRange(entityId, false), 0f),
                    Guard(() => EntityBalancingStore.Dps(entityId), 0f),
                    Guard(() => EntityBalancingStore.Damage1(entityId, false), 0f),
                    Guard(() => EntityBalancingStore.Damage2(entityId, false), 0f));
            }
            catch
            {
                return new EntityBalanceRow(entityId, false, 0, 0, 0f, 0, 0f, 0, 0f, 0f, 0f, 0f, 0f);
            }
        }

        private static bool Guard(Func<bool> getter, bool fallback)
        {
            try
            {
                return getter();
            }
            catch
            {
                return fallback;
            }
        }

        private static int Guard(Func<int> getter, int fallback)
        {
            try
            {
                return getter();
            }
            catch
            {
                return fallback;
            }
        }

        private static float Guard(Func<float> getter, float fallback)
        {
            try
            {
                return getter();
            }
            catch
            {
                return fallback;
            }
        }
    }
}
