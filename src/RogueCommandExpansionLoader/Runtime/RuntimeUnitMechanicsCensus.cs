using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Telemetry row describing active runtime mechanics, combat stances, veterancy,
    /// and visual director synchronization for a living entity on the battlefield.
    /// </summary>
    public sealed class UnitMechanicsCensusRow
    {
        public string EntityKeyId { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public bool IsControlledByPlayer { get; set; }
        public float HealthRatio { get; set; }
        public float ShieldRatio { get; set; }
        public string TacticalStance { get; set; } = "Standard";
        public string LoadoutVariant { get; set; } = "Standard";
        public int VeterancyRank { get; set; }
        public int CombatXp { get; set; }
        public IReadOnlyList<string> Affixes { get; set; } = Array.Empty<string>();
        public bool HasHealing { get; set; }
        public bool HasAura { get; set; }
        public bool HasShieldRegen { get; set; }
        public float CriticalHitChance { get; set; }
        public float MinAttackRange { get; set; }
        public int ActiveStatusEffectsCount { get; set; }
        public string ActiveStatusEffectsSummary { get; set; } = string.Empty;
        public bool HasVisualDirector { get; set; }
        public bool IsVisualTurretJammed { get; set; }
        public float VisualRecoilOffset { get; set; }
        public float VisualDoorProgress { get; set; }
    }

    /// <summary>
    /// Clean-room diagnostic census collecting runtime mechanics across living entities.
    /// Invariant: OmittedUnitCount strictly precedes unbounded Collections.
    /// </summary>
    public sealed class UnitMechanicsCensus
    {
        public int OmittedUnitCount { get; set; }
        public int TotalTrackedUnits { get; set; }
        public int TotalMechanicsInstancesCount { get; set; }
        public int ActiveHealerCount { get; set; }
        public int ActiveAuraCount { get; set; }
        public int ActiveShieldRegenCount { get; set; }
        public IReadOnlyList<UnitMechanicsCensusRow> Rows { get; set; } = Array.Empty<UnitMechanicsCensusRow>();
    }

    /// <summary>
    /// Result DTO for commanding visual feedback or hardpoint attachment toggles.
    /// </summary>
    public sealed class VisualFeedbackResult
    {
        public bool Success { get; set; }
        public string EntityKeyId { get; set; } = string.Empty;
        public string FeedbackType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
