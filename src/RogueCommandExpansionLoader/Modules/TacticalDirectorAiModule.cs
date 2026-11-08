using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Runtime.Ai;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// HUB-D unified meta-module (<c>sample:tactical-director</c>) that consumes the shared
    /// <see cref="AiBattlefieldSnapshot"/> and emits utility-scored <see cref="AiIntentProposal"/>
    /// intents for hazard evacuation, safe 8-ray dead-zone kiting, tactical skills, base/harvester defense,
    /// and anti-overkill role-aware focus targeting.
    /// </summary>
    public sealed class TacticalDirectorAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = TacticalDirectorParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            TacticalDirectorSettings settings;
            return TacticalDirectorParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException(nameof(gameBehaviour));
            }

            TacticalDirectorSettings settings;
            string diagnostic;
            if (!TacticalDirectorParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, nameof(parameters));
            }

            return new TacticalDirectorRuntimeBehaviour(gameBehaviour, settings);
        }

        internal sealed class TacticalDirectorRuntimeBehaviour : ICoordinatedAiRuntimeBehaviour
        {
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly TacticalDirectorSettings _settings;
            private readonly string _commandTag;
            private readonly Dictionary<int, float> _committedDamageByTarget = new Dictionary<int, float>();
            private float _nextEvalAt;
            private bool _stopped;

            public TacticalDirectorRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                TacticalDirectorSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
                _commandTag = "roguecommand.expansionloader.tactical-director." + Guid.NewGuid().ToString("N");
                _nextEvalAt = 0f;
            }

            public void OnInitialized()
            {
                _nextEvalAt = 0f;
            }

            public void Tick()
            {
                // Intentionally no-op: proposals are collected via CollectProposals in the HUB-D pass.
            }

            public void CollectProposals(
                AiBattlefieldSnapshot snapshot,
                AiDoctrineWeights doctrineWeights,
                IList<AiIntentProposal> proposals)
            {
                if (_stopped || _gameBehaviour == null || _gameBehaviour.gameObject == null ||
                    snapshot == null || proposals == null)
                {
                    return;
                }

                float now = Time.time;
                if (now < _nextEvalAt)
                {
                    return;
                }

                _nextEvalAt = now + _settings.EvalIntervalSeconds;

                IReadOnlyList<AiUnitSnapshot> aiUnits = snapshot.AiUnits;
                IReadOnlyList<AiUnitSnapshot> hostiles = snapshot.HostileUnits;
                IReadOnlyList<AiHazardZoneSnapshot> hazards = snapshot.HazardZones;
                if (aiUnits == null || aiUnits.Count == 0)
                {
                    return;
                }

                bool hasHostiles = hostiles != null && hostiles.Count > 0;
                float engagementRadiusSq = _settings.EngagementRadius * _settings.EngagementRadius;
                float defenseRadiusSq = _settings.DefenseRadius * _settings.DefenseRadius;
                _committedDamageByTarget.Clear();

                for (int i = 0; i < aiUnits.Count; i++)
                {
                    AiUnitSnapshot unit = aiUnits[i];
                    if (!unit.IsCombatUnit || unit.IsBuilding || unit.IsHarvester)
                    {
                        continue;
                    }

                    if (_settings.BaseIndex >= 0 && unit.BaseIndex != _settings.BaseIndex)
                    {
                        continue;
                    }

                    int emittedForUnit = 0;

                    AiUnitSnapshot nearestHostile = default(AiUnitSnapshot);
                    float nearestDistSq = float.MaxValue;
                    bool hasNearest = false;

                    AiUnitSnapshot bestTarget = default(AiUnitSnapshot);
                    float bestTargetScore = 0f;
                    bool hasBestTarget = false;

                    if (hasHostiles)
                    {
                        for (int h = 0; h < hostiles.Count; h++)
                        {
                            AiUnitSnapshot hostile = hostiles[h];
                            float distSq = unit.DistanceSquaredTo(hostile);
                            if (distSq < nearestDistSq)
                            {
                                nearestDistSq = distSq;
                                nearestHostile = hostile;
                                hasNearest = true;
                            }

                            if (_settings.EnableFocusTargeting && distSq <= engagementRadiusSq)
                            {
                                _committedDamageByTarget.TryGetValue(hostile.UniqueEntityId, out float committedDmg);
                                float targetScore = TacticalUtilityScorer.ScoreCombatTargetWithOverkill(
                                    unit,
                                    hostile,
                                    doctrineWeights,
                                    committedDmg);
                                if (targetScore > bestTargetScore)
                                {
                                    bestTargetScore = targetScore;
                                    bestTarget = hostile;
                                    hasBestTarget = true;
                                }
                            }
                        }
                    }

                    // 1. Survival / Hazard Evacuation / Safe 8-ray Dead-zone Kiting proposal
                    if (_settings.EnableSurvivalKiting && emittedForUnit < _settings.MaxProposalsPerUnit)
                    {
                        float hazardScore = TacticalUtilityScorer.ScoreHazardEvacuation(unit, hazards, doctrineWeights);
                        float kiteScore = hasNearest
                            ? TacticalUtilityScorer.ScoreSurvivalOrKite(unit, nearestHostile, doctrineWeights)
                            : 0f;

                        if (hasNearest && unit.HealthRatio <= _settings.RetreatHealthRatio && kiteScore < 120f)
                        {
                            kiteScore = 120f * doctrineWeights.SurvivalWeight;
                        }

                        float combinedSurvivalScore = Math.Max(hazardScore, kiteScore);
                        if (combinedSurvivalScore > 0f)
                        {
                            float retreatDistance = Math.Max(9.0f, unit.MinAttackRange > 0f ? unit.MinAttackRange + 4.0f : 10.0f);
                            (float safeX, float safeZ) = AiSafeVectorPolicy.ComputeSafeRetreatDestination(
                                unit,
                                nearestHostile,
                                snapshot,
                                retreatDistance);

                            proposals.Add(new AiIntentProposal(
                                moduleId: ModuleId,
                                commandTag: _commandTag + ".kite",
                                unitId: unit.UniqueEntityId,
                                targetEntityId: -1,
                                destX: safeX,
                                destZ: safeZ,
                                category: AiIntentCategory.SurvivalKite,
                                utilityScore: combinedSurvivalScore,
                                lockoutDurationSeconds: 1.25f,
                                requiresEmptyQueue: false));
                            emittedForUnit++;
                        }
                    }

                    // 2. Tactical Skill proposal when ready
                    if (unit.HasReadyActiveSkill && emittedForUnit < _settings.MaxProposalsPerUnit)
                    {
                        float skillRangeSq = unit.ActiveSkillRange * unit.ActiveSkillRange;
                        bool hostileInSkillRange = hasNearest && nearestDistSq <= skillRangeSq;
                        float skillScore = TacticalUtilityScorer.ScoreTacticalSkill(unit, hostileInSkillRange, doctrineWeights);
                        if (skillScore > 0f)
                        {
                            int skillTargetId = (!unit.ActiveSkillTargetsSelf && hasNearest) ? nearestHostile.UniqueEntityId : -1;
                            proposals.Add(new AiIntentProposal(
                                moduleId: ModuleId,
                                commandTag: _commandTag + ".skill",
                                unitId: unit.UniqueEntityId,
                                targetEntityId: skillTargetId,
                                destX: hasNearest ? nearestHostile.PosX : unit.PosX,
                                destZ: hasNearest ? nearestHostile.PosZ : unit.PosZ,
                                category: AiIntentCategory.TacticalSkill,
                                utilityScore: skillScore,
                                lockoutDurationSeconds: 1.0f,
                                requiresEmptyQueue: false));
                            emittedForUnit++;
                        }
                    }

                    // 3. Base & Harvester defense proposal when under pressure
                    if (_settings.EnableBaseDefense && snapshot.UnderHarvesterOrBasePressure &&
                        hasNearest && nearestDistSq <= defenseRadiusSq &&
                        emittedForUnit < _settings.MaxProposalsPerUnit)
                    {
                        float defenseScore = (90f + nearestHostile.DpsEstimate * 1.5f) * doctrineWeights.DefenseWeight;
                        proposals.Add(new AiIntentProposal(
                            moduleId: ModuleId,
                            commandTag: _commandTag + ".defense",
                            unitId: unit.UniqueEntityId,
                            targetEntityId: nearestHostile.UniqueEntityId,
                            destX: nearestHostile.PosX,
                            destZ: nearestHostile.PosZ,
                            category: AiIntentCategory.BaseAndHarvesterDefense,
                            utilityScore: defenseScore,
                            lockoutDurationSeconds: 0.75f,
                            requiresEmptyQueue: false));
                        emittedForUnit++;
                    }

                    // 4. Focus combat targeting proposal with anti-overkill tracking
                    if (_settings.EnableFocusTargeting && hasBestTarget &&
                        emittedForUnit < _settings.MaxProposalsPerUnit)
                    {
                        _committedDamageByTarget.TryGetValue(bestTarget.UniqueEntityId, out float currentCommitted);
                        _committedDamageByTarget[bestTarget.UniqueEntityId] = currentCommitted + (unit.DpsEstimate * 2.0f);

                        proposals.Add(new AiIntentProposal(
                            moduleId: ModuleId,
                            commandTag: _commandTag + ".focus",
                            unitId: unit.UniqueEntityId,
                            targetEntityId: bestTarget.UniqueEntityId,
                            destX: bestTarget.PosX,
                            destZ: bestTarget.PosZ,
                            category: AiIntentCategory.CombatTargeting,
                            utilityScore: bestTargetScore,
                            lockoutDurationSeconds: 0.50f,
                            requiresEmptyQueue: false));
                        emittedForUnit++;
                    }
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
                _committedDamageByTarget.Clear();
            }
        }
    }
}
