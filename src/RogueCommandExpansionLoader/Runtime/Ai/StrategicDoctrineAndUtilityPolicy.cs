using System;

namespace RogueCommandExpansionLoader.Runtime.Ai
{
    /// <summary>
    /// Strategic weight multipliers derived from Lanchester force ratios and base pressure.
    /// </summary>
    public readonly struct AiDoctrineWeights
    {
        public string DoctrineName { get; }
        public float SurvivalWeight { get; }
        public float DefenseWeight { get; }
        public float SkirmishWeight { get; }
        public float AssaultWeight { get; }

        public AiDoctrineWeights(
            string doctrineName,
            float survivalWeight,
            float defenseWeight,
            float skirmishWeight,
            float assaultWeight)
        {
            DoctrineName = doctrineName ?? "Standard";
            SurvivalWeight = Math.Max(0.1f, survivalWeight);
            DefenseWeight = Math.Max(0.1f, defenseWeight);
            SkirmishWeight = Math.Max(0.1f, skirmishWeight);
            AssaultWeight = Math.Max(0.1f, assaultWeight);
        }

        public static readonly AiDoctrineWeights Fortify =
            new AiDoctrineWeights("Fortify", survivalWeight: 1.45f, defenseWeight: 1.60f, skirmishWeight: 1.05f, assaultWeight: 0.70f);

        public static readonly AiDoctrineWeights Skirmish =
            new AiDoctrineWeights("Skirmish", survivalWeight: 1.20f, defenseWeight: 1.10f, skirmishWeight: 1.35f, assaultWeight: 1.00f);

        public static readonly AiDoctrineWeights Assault =
            new AiDoctrineWeights("Assault", survivalWeight: 0.90f, defenseWeight: 0.85f, skirmishWeight: 1.15f, assaultWeight: 1.45f);
    }

    /// <summary>
    /// Evaluates strategic doctrine weights from the current <see cref="AiBattlefieldSnapshot"/>.
    /// </summary>
    public static class StrategicDoctrinePolicy
    {
        public static AiDoctrineWeights Evaluate(AiBattlefieldSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return AiDoctrineWeights.Skirmish;
            }

            float aiPower = Math.Max(1f, snapshot.TotalAiCombatPower);
            float hostilePower = Math.Max(1f, snapshot.TotalHostileCombatPower);
            float powerRatio = aiPower / hostilePower;

            if (snapshot.UnderHarvesterOrBasePressure && powerRatio < 1.35f)
            {
                return AiDoctrineWeights.Fortify;
            }

            if (powerRatio < 0.75f)
            {
                return AiDoctrineWeights.Fortify;
            }

            if (powerRatio >= 1.35f && snapshot.TotalAiCombatUnits >= 3)
            {
                return AiDoctrineWeights.Assault;
            }

            return AiDoctrineWeights.Skirmish;
        }
    }

    /// <summary>
    /// Pure utility functions for scoring tactical unit actions (survival/kiting, combat targeting,
    /// base defense) without touching Unity or Assembly-CSharp types.
    /// </summary>
    public static class TacticalUtilityScorer
    {
        private const int RoleSniper = 1 << 2;
        private const int RoleScout = 1 << 7;
        private const int RoleSupporter = 1 << 6;
        private const int RoleHeal = 1 << 8;

        /// <summary>
        /// Scores the urgency of retreating or kiting away from an encroaching hostile unit.
        /// Strongly boosts utility when an artillery/minimum-range unit has an enemy inside its dead-zone.
        /// </summary>
        public static float ScoreSurvivalOrKite(
            AiUnitSnapshot unit,
            AiUnitSnapshot nearestHostile,
            AiDoctrineWeights doctrine)
        {
            float distSq = unit.DistanceSquaredTo(nearestHostile);
            float dist = (float)Math.Sqrt(Math.Max(0f, distSq));

            float baseUrgency = 0f;

            // 1. Artillery / Minimum-range dead-zone violation
            if (unit.MinAttackRange > 0.5f && dist < unit.MinAttackRange)
            {
                float penetrationRatio = 1.0f - (dist / unit.MinAttackRange);
                baseUrgency += 110f + (penetrationRatio * 60f);
            }

            // 2. Fast ranged skirmish kiting envelope
            if (unit.WeaponRange >= 8.0f && unit.MoveSpeed >= 7.0f)
            {
                float kiteThreshold = unit.WeaponRange * 0.55f;
                if (dist < kiteThreshold && kiteThreshold > 0.1f)
                {
                    float encroachment = 1.0f - (dist / kiteThreshold);
                    baseUrgency += 65f + (encroachment * 45f);
                }
            }

            // 3. Low health emergency survival
            if (unit.HealthRatio <= 0.35f)
            {
                baseUrgency += (0.40f - unit.HealthRatio) * 220f;
            }

            if (baseUrgency <= 0f)
            {
                return 0f;
            }

            float roleBonus = ((unit.RoleFlags & (RoleSniper | RoleScout | RoleSupporter | RoleHeal)) != 0) ? 1.15f : 1.0f;
            return baseUrgency * roleBonus * Math.Max(0.5f, doctrine.SurvivalWeight);
        }

        /// <summary>
        /// Scores engaging a hostile target, penalizing distance and dead-zone violations.
        /// </summary>
        public static float ScoreCombatTarget(
            AiUnitSnapshot unit,
            AiUnitSnapshot candidate,
            AiDoctrineWeights doctrine)
        {
            if (!unit.IsCombatUnit || ((unit.RoleFlags & (RoleSupporter | RoleHeal)) != 0))
            {
                return 0f;
            }

            float distSq = unit.DistanceSquaredTo(candidate);
            float dist = (float)Math.Sqrt(Math.Max(0f, distSq));

            // Cannot fire inside minimum attack range dead-zone
            if (unit.MinAttackRange > 0.5f && dist < unit.MinAttackRange)
            {
                return 5f;
            }

            float missingHealthBonus = (1.0f - candidate.HealthRatio) * 35f;
            float threatValue = (candidate.DpsEstimate * 1.6f) + missingHealthBonus + 25f;
            float distPenalty = dist * 1.25f;
            float rawScore = Math.Max(1f, threatValue - distPenalty);

            return rawScore * Math.Max(0.5f, doctrine.AssaultWeight);
        }
    }
}
