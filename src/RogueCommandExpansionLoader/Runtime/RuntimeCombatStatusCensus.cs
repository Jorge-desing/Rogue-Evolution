using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry row for an entity currently in active combat or bearing status effects.
    /// Decoupled from Unity types to support offline testing and MCP inspection.
    /// </summary>
    public readonly struct CombatEntityStatusRow
    {
        public CombatEntityStatusRow(
            string entityKeyId,
            bool isControlledByPlayer,
            float timeOfLastDamage,
            bool isUnderRecentAttack,
            int activeStatusEffectsCount)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            IsControlledByPlayer = isControlledByPlayer;
            TimeOfLastDamage = timeOfLastDamage;
            IsUnderRecentAttack = isUnderRecentAttack;
            ActiveStatusEffectsCount = activeStatusEffectsCount;
        }

        public string EntityKeyId { get; }
        public bool IsControlledByPlayer { get; }
        public float TimeOfLastDamage { get; }
        public bool IsUnderRecentAttack { get; }
        public int ActiveStatusEffectsCount { get; }
    }

    /// <summary>
    /// Snapshot of combat engagements and status effects across living battlefield units.
    /// </summary>
    public sealed class CombatStatusCensus
    {
        /// <summary>
        /// Loss/omission counter placed first to ensure bounded diagnostic trace bounds are respected.
        /// </summary>
        public int OmittedUnitCount { get; }
        public int TotalCombatActiveUnitCount { get; }
        public int UnitsWithActiveStatusEffectsCount { get; }
        public IReadOnlyList<CombatEntityStatusRow> CombatActiveUnits { get; }

        public CombatStatusCensus(
            int omittedUnitCount,
            int totalCombatActiveUnitCount,
            int unitsWithActiveStatusEffectsCount,
            IReadOnlyList<CombatEntityStatusRow> combatActiveUnits)
        {
            OmittedUnitCount = omittedUnitCount;
            TotalCombatActiveUnitCount = totalCombatActiveUnitCount;
            UnitsWithActiveStatusEffectsCount = unitsWithActiveStatusEffectsCount;
            CombatActiveUnits = combatActiveUnits ?? new List<CombatEntityStatusRow>().AsReadOnly();
        }

        public static CombatStatusCensus Empty { get; } =
            new CombatStatusCensus(0, 0, 0, new List<CombatEntityStatusRow>().AsReadOnly());
    }

    /// <summary>
    /// Reads living combatants from <see cref="ExistingControllers"/> and evaluates recent damage,
    /// target focus, and status effect presence.
    /// </summary>
    public static class RuntimeCombatStatusCensus
    {
        private const int MaxTrackedUnits = 32;
        private static readonly StatusEffect[] TrackedEffects = (StatusEffect[])Enum.GetValues(typeof(StatusEffect));

        public static CombatStatusCensus Current()
        {
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return CombatStatusCensus.Empty;
                }

                float now = 0f;
                try
                {
                    now = Time.time;
                }
                catch
                {
                    // Fallback
                }

                int combatActiveCount = 0;
                int withEffectsCount = 0;
                int omittedCount = 0;
                var activeRows = new List<CombatEntityStatusRow>();

                var candidateEntities = new List<EntityController>();
                HashSet<EntityController> playerUnits = existing.PlayerUnits();
                if (playerUnits != null)
                {
                    candidateEntities.AddRange(playerUnits);
                }
                HashSet<EntityController> aiUnits = existing.AiUnits();
                if (aiUnits != null)
                {
                    candidateEntities.AddRange(aiUnits);
                }

                foreach (EntityController entity in candidateEntities)
                {
                    if (entity == null || entity.IsDestroyed || entity.DestroyHasBeenStarted)
                    {
                        continue;
                    }

                    bool recentDamage = entity.TimeOfLastDamage > -0.5f && (now - entity.TimeOfLastDamage) <= 5.0f;
                    bool isTargeting = entity.CurrentAttackTarget != null;

                    int effectCount = 0;
                    try
                    {
                        for (int i = 0; i < TrackedEffects.Length; i++)
                        {
                            if (entity.HasActiveStatusEffect(TrackedEffects[i]))
                            {
                                effectCount++;
                            }
                        }
                    }
                    catch
                    {
                        // Fallback
                    }

                    if (effectCount > 0)
                    {
                        withEffectsCount++;
                    }

                    if (recentDamage || isTargeting || effectCount > 0)
                    {
                        combatActiveCount++;
                        if (activeRows.Count < MaxTrackedUnits)
                        {
                            activeRows.Add(new CombatEntityStatusRow(
                                entity.EntityId,
                                entity.IsControlledByPlayer,
                                entity.TimeOfLastDamage,
                                recentDamage,
                                effectCount));
                        }
                        else
                        {
                            omittedCount++;
                        }
                    }
                }

                return new CombatStatusCensus(omittedCount, combatActiveCount, withEffectsCount, activeRows.AsReadOnly());
            }
            catch
            {
                return CombatStatusCensus.Empty;
            }
        }
    }
}
