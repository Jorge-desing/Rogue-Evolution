using System;

namespace RogueCommandExpansionLoader.Runtime.Ai
{
    /// <summary>
    /// Pure, immutable snapshot of an entity on the battlefield for HUB-D AI perception.
    /// Free of Unity and Assembly-CSharp types so it can be evaluated in offline validation tests.
    /// Never mutates or replaces native control/hostility flags.
    /// </summary>
    public readonly struct AiUnitSnapshot
    {
        public int UniqueEntityId { get; }
        public string EntityId { get; }
        public int BaseIndex { get; }
        public float PosX { get; }
        public float PosZ { get; }
        public float CurrentHealth { get; }
        public float MaxHealth { get; }
        public float CurrentShield { get; }
        public float MaxShield { get; }
        public float DpsEstimate { get; }
        public float WeaponRange { get; }
        public float MinAttackRange { get; }
        public float MoveSpeed { get; }
        public int RoleFlags { get; }
        public bool IsControlledByAi { get; }
        public bool IsControlledByPlayer { get; }
        public bool IsCombatUnit { get; }
        public bool IsHarvester { get; }
        public bool IsBuilding { get; }
        public bool IsSupportUnit { get; }
        public bool HasQueuedCommands { get; }
        public string CurrentCommandTag { get; }
        public int CurrentTargetId { get; }

        public AiUnitSnapshot(
            int uniqueEntityId,
            string entityId,
            int baseIndex,
            float posX,
            float posZ,
            float currentHealth,
            float maxHealth,
            float currentShield,
            float maxShield,
            float dpsEstimate,
            float weaponRange,
            float minAttackRange,
            float moveSpeed,
            int roleFlags,
            bool isControlledByAi,
            bool isControlledByPlayer,
            bool isCombatUnit,
            bool isHarvester,
            bool isBuilding,
            bool isSupportUnit,
            bool hasQueuedCommands,
            string currentCommandTag,
            int currentTargetId)
        {
            UniqueEntityId = uniqueEntityId;
            EntityId = entityId ?? string.Empty;
            BaseIndex = baseIndex;
            PosX = SanitizeFloat(posX, 0f);
            PosZ = SanitizeFloat(posZ, 0f);
            CurrentHealth = Math.Max(0f, SanitizeFloat(currentHealth, 0f));
            MaxHealth = Math.Max(0f, SanitizeFloat(maxHealth, 0f));
            CurrentShield = Math.Max(0f, SanitizeFloat(currentShield, 0f));
            MaxShield = Math.Max(0f, SanitizeFloat(maxShield, 0f));
            DpsEstimate = Math.Max(0f, SanitizeFloat(dpsEstimate, 0f));
            WeaponRange = Math.Max(0f, SanitizeFloat(weaponRange, 0f));
            MinAttackRange = Math.Max(0f, SanitizeFloat(minAttackRange, 0f));
            MoveSpeed = Math.Max(0f, SanitizeFloat(moveSpeed, 0f));
            RoleFlags = roleFlags;
            IsControlledByAi = isControlledByAi;
            IsControlledByPlayer = isControlledByPlayer;
            IsCombatUnit = isCombatUnit;
            IsHarvester = isHarvester;
            IsBuilding = isBuilding;
            IsSupportUnit = isSupportUnit;
            HasQueuedCommands = hasQueuedCommands;
            CurrentCommandTag = currentCommandTag ?? string.Empty;
            CurrentTargetId = currentTargetId;
        }

        public float HealthRatio
        {
            get
            {
                if (MaxHealth <= 0.0001f)
                {
                    return 1.0f;
                }

                float ratio = CurrentHealth / MaxHealth;
                if (float.IsNaN(ratio) || float.IsInfinity(ratio))
                {
                    return 1.0f;
                }

                return Math.Max(0f, Math.Min(1f, ratio));
            }
        }

        public float ShieldRatio
        {
            get
            {
                if (MaxShield <= 0.0001f)
                {
                    return 0.0f;
                }

                float ratio = CurrentShield / MaxShield;
                if (float.IsNaN(ratio) || float.IsInfinity(ratio))
                {
                    return 0.0f;
                }

                return Math.Max(0f, Math.Min(1f, ratio));
            }
        }

        public float CombinedCombatPower
        {
            get
            {
                if (!IsCombatUnit)
                {
                    return 0f;
                }

                float effectiveVitality = CurrentHealth + CurrentShield + (MaxHealth * 0.25f);
                return Math.Max(1f, DpsEstimate * 2.0f + effectiveVitality * 0.15f);
            }
        }

        public float DistanceSquaredTo(AiUnitSnapshot other)
        {
            float dx = other.PosX - PosX;
            float dz = other.PosZ - PosZ;
            return dx * dx + dz * dz;
        }

        /// <summary>
        /// Verifies whether a persistent route order (such as native Patrol) is allowed without
        /// clobbering an active command or a private queued _commandChain entry.
        /// </summary>
        public bool IsIdleAndQueueEmpty(bool hasActiveNonIdleCommand)
        {
            return !hasActiveNonIdleCommand && !HasQueuedCommands;
        }

        private static float SanitizeFloat(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }
    }
}
