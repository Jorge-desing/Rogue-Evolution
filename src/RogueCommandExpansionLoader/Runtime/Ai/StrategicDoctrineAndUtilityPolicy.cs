using System;
using System.Collections.Generic;

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
    /// Pure utility functions for scoring tactical unit actions (survival/kiting, hazard evacuation,
    /// anti-overkill combat targeting, tactical skills, and support/repair logistics)
    /// without touching Unity or Assembly-CSharp types.
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
        /// Scores emergency evacuation when a unit is standing inside a harmful environmental hazard zone.
        /// </summary>
        public static float ScoreHazardEvacuation(
            AiUnitSnapshot unit,
            IReadOnlyList<AiHazardZoneSnapshot> hazards,
            AiDoctrineWeights doctrine)
        {
            if (hazards == null || hazards.Count == 0 || unit.IsBuilding)
            {
                return 0f;
            }

            float maxUrgency = 0f;
            for (int i = 0; i < hazards.Count; i++)
            {
                AiHazardZoneSnapshot hz = hazards[i];
                if (hz.IsBeneficialToAi)
                {
                    continue;
                }

                if (hz.ContainsPoint(unit.PosX, unit.PosZ, margin: 0.5f))
                {
                    float urgency = 135.0f + (hz.TickDamage * 2.5f);
                    if (urgency > maxUrgency)
                    {
                        maxUrgency = urgency;
                    }
                }
            }

            return maxUrgency * Math.Max(0.5f, doctrine.SurvivalWeight);
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

        /// <summary>
        /// Scores engaging a hostile target while applying anti-overkill damping when
        /// <paramref name="alreadyCommittedDamage"/> exceeds 115% of the candidate's remaining vitality.
        /// </summary>
        public static float ScoreCombatTargetWithOverkill(
            AiUnitSnapshot unit,
            AiUnitSnapshot candidate,
            AiDoctrineWeights doctrine,
            float alreadyCommittedDamage)
        {
            float baseScore = ScoreCombatTarget(unit, candidate, doctrine);
            if (baseScore <= 0f)
            {
                return 0f;
            }

            float safeCommitted = float.IsNaN(alreadyCommittedDamage) || float.IsInfinity(alreadyCommittedDamage)
                ? 0f
                : Math.Max(0f, alreadyCommittedDamage);
            float remainingVitality = Math.Max(1f, candidate.CurrentHealth + candidate.CurrentShield);

            if (safeCommitted >= remainingVitality * 1.15f)
            {
                return baseScore * 0.25f;
            }

            return baseScore;
        }

        /// <summary>
        /// Scores firing a ready active tactical skill.
        /// </summary>
        public static float ScoreTacticalSkill(
            AiUnitSnapshot unit,
            bool hasHostileInRange,
            AiDoctrineWeights doctrine)
        {
            if (!unit.HasReadyActiveSkill)
            {
                return 0f;
            }

            if (!unit.ActiveSkillTargetsSelf && !hasHostileInRange)
            {
                return 0f;
            }

            float baseScore = unit.HealthRatio <= 0.50f ? 115f : 95f;
            return baseScore * Math.Max(0.5f, doctrine.SkirmishWeight);
        }

        /// <summary>
        /// Scores dispatching a support/engineer unit to repair or assist a damaged friendly entity.
        /// Prioritizes harvesters and high-value frontline units.
        /// </summary>
        public static float ScoreSupportOrRepair(
            AiUnitSnapshot supportUnit,
            AiUnitSnapshot damagedAlly,
            AiDoctrineWeights doctrine)
        {
            if (!supportUnit.IsSupportUnit || supportUnit.UniqueEntityId == damagedAlly.UniqueEntityId)
            {
                return 0f;
            }

            if (damagedAlly.HealthRatio >= 0.90f)
            {
                return 0f;
            }

            float distSq = supportUnit.DistanceSquaredTo(damagedAlly);
            float dist = (float)Math.Sqrt(Math.Max(0f, distSq));

            float missingHealthUrgency = (1.0f - damagedAlly.HealthRatio) * 80.0f;
            float priorityBonus = damagedAlly.IsHarvester ? 45.0f : (damagedAlly.IsBuilding ? 30.0f : 20.0f);
            float rawScore = Math.Max(5.0f, missingHealthUrgency + priorityBonus - (dist * 0.8f));

            return rawScore * Math.Max(0.5f, doctrine.DefenseWeight);
        }

        /// <summary>
        /// Scores flanking maneuver utility for mobile combat units during offensive or skirmish postures.
        /// Rewards bypassing the primary battle line to strike from hostile blind-spots while penalizing slow or support units.
        /// </summary>
        public static float ScoreFlankingManeuver(
            AiUnitSnapshot unit,
            float flankDestX,
            float flankDestZ,
            AiBattlefieldSnapshot snapshot,
            AiDoctrineWeights doctrine)
        {
            if (!unit.IsCombatUnit || ((unit.RoleFlags & (RoleSupporter | RoleHeal)) != 0) || unit.IsBuilding || unit.IsHarvester)
            {
                return 0f;
            }

            // Minimum mobility threshold: slow units (heavy tanks, siege) hold the line or push center; fast units flank
            if (unit.MoveSpeed < 7.0f)
            {
                return 0f;
            }

            // Needs an established battle line: at least 3 AI combat units and 1 hostile unit
            if (snapshot == null || snapshot.TotalAiCombatUnits < 3 || snapshot.TotalHostileCombatUnits < 1)
            {
                return 0f;
            }

            // In Fortify doctrine or under heavy base pressure, all units must prioritize defense over flanking
            if (snapshot.UnderHarvesterOrBasePressure || doctrine.SurvivalWeight >= 1.40f)
            {
                return 0f;
            }

            float dx = unit.PosX - flankDestX;
            float dz = unit.PosZ - flankDestZ;
            float distToFlank = (float)Math.Sqrt(Math.Max(0f, dx * dx + dz * dz));

            // If unit is already at the flank destination (within 4 cells), no need to re-issue flank move
            if (distToFlank < 4.0f)
            {
                return 10f * doctrine.AssaultWeight;
            }

            // Scout / fast role receives an additional mobility bonus
            float roleBonus = (((unit.RoleFlags & (RoleScout | RoleSniper)) != 0) || unit.MoveSpeed >= 10.0f) ? 1.30f : 1.0f;

            // Flanking utility scales with AssaultWeight and distance feasibility
            float baseFlankUtility = Math.Max(15.0f, 75.0f - (distToFlank * 0.75f));
            return baseFlankUtility * roleBonus * Math.Max(0.5f, doctrine.AssaultWeight);
        }

        /// <summary>
        /// Resolves the recommended tactical stance for a unit based on the active strategic doctrine and unit role.
        /// </summary>
        public static string EvaluateRecommendedStance(AiDoctrineWeights doctrine, AiUnitSnapshot unit)
        {
            if (unit.IsBuilding || unit.IsHarvester)
            {
                return "HoldGround";
            }

            if (unit.IsSupportUnit)
            {
                return "Defensive";
            }

            if (string.Equals(doctrine.DoctrineName, "Fortify", StringComparison.OrdinalIgnoreCase))
            {
                return (unit.MoveSpeed >= 8.0f && unit.WeaponRange >= 10.0f) ? "Kite" : "Defensive";
            }

            if (string.Equals(doctrine.DoctrineName, "Assault", StringComparison.OrdinalIgnoreCase))
            {
                return "Aggressive";
            }

            // Skirmish default
            return (unit.MoveSpeed >= 7.5f && unit.WeaponRange >= 8.0f) ? "Kite" : "Aggressive";
        }
    }
}
