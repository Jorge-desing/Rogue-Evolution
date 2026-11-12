using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable diagnostic row for a base structure or command center.
    /// </summary>
    public readonly struct BaseStructureDiagnosticRow
    {
        public BaseStructureDiagnosticRow(
            string entityKeyId,
            bool isControlledByPlayer,
            float currentHealth,
            float maxHealth,
            float healthRatio,
            bool isEngineeringHq)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            IsControlledByPlayer = isControlledByPlayer;
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
            HealthRatio = healthRatio;
            IsEngineeringHq = isEngineeringHq;
        }

        public string EntityKeyId { get; }
        public bool IsControlledByPlayer { get; }
        public float CurrentHealth { get; }
        public float MaxHealth { get; }
        public float HealthRatio { get; }
        public bool IsEngineeringHq { get; }
    }

    /// <summary>
    /// Telemetry snapshot of player and enemy base structure integrity and win/loss objectives.
    /// </summary>
    public sealed class BaseIntegrityCensus
    {
        /// <summary>
        /// Loss/omission counter placed first to ensure bounded diagnostic trace bounds are respected.
        /// </summary>
        public int OmittedStructureCount { get; }
        public bool HasPlayerHq { get; }
        public float PlayerHqHealth { get; }
        public float PlayerHqMaxHealth { get; }
        public float PlayerHqHealthRatio { get; }
        public int LivingAiBuildingCount { get; }
        public float AverageAiStructureHealthRatio { get; }
        public IReadOnlyList<BaseStructureDiagnosticRow> StructureRows { get; }

        public BaseIntegrityCensus(
            int omittedStructureCount,
            bool hasPlayerHq,
            float playerHqHealth,
            float playerHqMaxHealth,
            float playerHqHealthRatio,
            int livingAiBuildingCount,
            float averageAiStructureHealthRatio,
            IReadOnlyList<BaseStructureDiagnosticRow> structureRows)
        {
            OmittedStructureCount = omittedStructureCount;
            HasPlayerHq = hasPlayerHq;
            PlayerHqHealth = playerHqHealth;
            PlayerHqMaxHealth = playerHqMaxHealth;
            PlayerHqHealthRatio = playerHqHealthRatio;
            LivingAiBuildingCount = livingAiBuildingCount;
            AverageAiStructureHealthRatio = averageAiStructureHealthRatio;
            StructureRows = structureRows ?? new List<BaseStructureDiagnosticRow>().AsReadOnly();
        }

        public static BaseIntegrityCensus Empty { get; } =
            new BaseIntegrityCensus(0, false, 0f, 0f, 0f, 0, 0f, new List<BaseStructureDiagnosticRow>().AsReadOnly());
    }

    /// <summary>
    /// Reads player command HQ and enemy base buildings to calculate base integrity telemetry.
    /// </summary>
    public static class RuntimeBaseIntegrityCensus
    {
        private const int MaxTrackedStructures = 32;

        public static BaseIntegrityCensus Current()
        {
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return BaseIntegrityCensus.Empty;
                }

                bool hasHq = false;
                float hqCurHp = 0f;
                float hqMaxHp = 0f;
                float hqRatio = 0f;
                var rows = new List<BaseStructureDiagnosticRow>();

                try
                {
                    EntityController hq = existing.EngineeringHq();
                    if (hq != null && !hq.IsDestroyed && !hq.DestroyHasBeenStarted)
                    {
                        hasHq = true;
                        hqMaxHp = hq.MaxHealth > 0.001f ? hq.MaxHealth : 1000f;
                        hqCurHp = hq.CurrentHealth >= 0f ? hq.CurrentHealth : 0f;
                        hqRatio = Mathf.Clamp(hqCurHp / hqMaxHp, 0f, 1f);

                        string hqKey = hq.EntityId ?? "HQ";
                        rows.Add(new BaseStructureDiagnosticRow(hqKey, true, hqCurHp, hqMaxHp, hqRatio, true));
                    }
                }
                catch
                {
                    // Degrade gracefully
                }

                int aiBuildingCount = 0;
                float totalAiRatio = 0f;
                int omittedCount = 0;

                try
                {
                    HashSet<EntityController> aiBuildings = existing.AiBuildings();
                    if (aiBuildings != null)
                    {
                        foreach (EntityController building in aiBuildings)
                        {
                            if (building == null || building.IsDestroyed || building.DestroyHasBeenStarted)
                            {
                                continue;
                            }

                            aiBuildingCount++;
                            float bMaxHp = building.MaxHealth > 0.001f ? building.MaxHealth : 100f;
                            float bCurHp = building.CurrentHealth >= 0f ? building.CurrentHealth : 0f;
                            float bRatio = Mathf.Clamp(bCurHp / bMaxHp, 0f, 1f);
                            totalAiRatio += bRatio;

                            if (rows.Count < MaxTrackedStructures)
                            {
                                string bKey = building.EntityId ?? string.Empty;
                                rows.Add(new BaseStructureDiagnosticRow(bKey, false, bCurHp, bMaxHp, bRatio, false));
                            }
                            else
                            {
                                omittedCount++;
                            }
                        }
                    }
                }
                catch
                {
                    // Degrade gracefully
                }

                float avgAiRatio = aiBuildingCount > 0 ? (totalAiRatio / aiBuildingCount) : 0f;

                return new BaseIntegrityCensus(
                    omittedCount,
                    hasHq,
                    hqCurHp,
                    hqMaxHp,
                    hqRatio,
                    aiBuildingCount,
                    avgAiRatio,
                    rows.AsReadOnly());
            }
            catch
            {
                return BaseIntegrityCensus.Empty;
            }
        }
    }
}
