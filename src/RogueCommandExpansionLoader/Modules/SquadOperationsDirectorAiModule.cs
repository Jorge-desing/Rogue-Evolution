using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Runtime.Ai;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// HUB-D 2.0 specialist coordinated module (<c>sample:squad-operations-director</c>) that manages
    /// support/repair logistics for engineers/medics, fast counter-battery interception against enemy
    /// long-range artillery, and active tactical skill triggering through the central command arbiter.
    /// </summary>
    public sealed class SquadOperationsDirectorAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = SquadOperationsDirectorParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            SquadOperationsDirectorSettings settings;
            return SquadOperationsDirectorParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException(nameof(gameBehaviour));
            }

            SquadOperationsDirectorSettings settings;
            string diagnostic;
            if (!SquadOperationsDirectorParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, nameof(parameters));
            }

            return new SquadOperationsDirectorRuntimeBehaviour(gameBehaviour, settings);
        }

        internal sealed class SquadOperationsDirectorRuntimeBehaviour : ICoordinatedAiRuntimeBehaviour
        {
            private const float FastInterceptorMinSpeed = 9.0f;

            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly SquadOperationsDirectorSettings _settings;
            private readonly string _commandTag;
            private float _nextEvalAt;
            private bool _stopped;

            public SquadOperationsDirectorRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                SquadOperationsDirectorSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
                _commandTag = "roguecommand.expansionloader.squad-ops." + Guid.NewGuid().ToString("N");
                _nextEvalAt = 0f;
            }

            public void OnInitialized()
            {
                _nextEvalAt = 0f;
            }

            public void Tick()
            {
                // Intentionally no-op: executed in CompositeAiRuntimeBehaviour's coordinated pass.
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

                float supportRadiusSq = _settings.SupportRadius * _settings.SupportRadius;

                // Identify highest-priority hostile artillery for counter-battery interception
                AiUnitSnapshot hostileArtillery = default(AiUnitSnapshot);
                bool hasHostileArtillery = false;
                if (_settings.EnableCounterBattery && hostiles != null)
                {
                    float bestArtilleryDps = 0f;
                    for (int h = 0; h < hostiles.Count; h++)
                    {
                        AiUnitSnapshot cand = hostiles[h];
                        bool isLongRangeArtillery =
                            cand.MinAttackRange >= _settings.CounterBatteryMinRangeThreshold ||
                            cand.WeaponRange >= 20.0f;
                        if (isLongRangeArtillery && cand.DpsEstimate >= bestArtilleryDps)
                        {
                            bestArtilleryDps = cand.DpsEstimate;
                            hostileArtillery = cand;
                            hasHostileArtillery = true;
                        }
                    }
                }

                for (int i = 0; i < aiUnits.Count; i++)
                {
                    AiUnitSnapshot unit = aiUnits[i];
                    if (unit.IsBuilding || unit.IsHarvester)
                    {
                        continue;
                    }

                    if (_settings.BaseIndex >= 0 && unit.BaseIndex != _settings.BaseIndex)
                    {
                        continue;
                    }

                    // 1. Emergency hazard evacuation for support units (which TacticalDirector skips)
                    if (unit.IsSupportUnit)
                    {
                        float hazardUrgency = TacticalUtilityScorer.ScoreHazardEvacuation(unit, hazards, doctrineWeights);
                        if (hazardUrgency > 0f)
                        {
                            (float safeX, float safeZ) = AiSafeVectorPolicy.ComputeTacticalRetreatDestination(
                                unit,
                                default(AiUnitSnapshot),
                                snapshot,
                                10.0f);
                            proposals.Add(new AiIntentProposal(
                                moduleId: ModuleId,
                                commandTag: _commandTag + ".evac",
                                unitId: unit.UniqueEntityId,
                                targetEntityId: -1,
                                destX: safeX,
                                destZ: safeZ,
                                category: AiIntentCategory.SurvivalKite,
                                utilityScore: hazardUrgency,
                                lockoutDurationSeconds: 1.25f,
                                requiresEmptyQueue: false));
                        }
                    }

                    // 2. Support & Repair Logistics for engineer/medic units
                    if (_settings.EnableSupportLogistics && unit.IsSupportUnit)
                    {
                        AiUnitSnapshot bestPatient = default(AiUnitSnapshot);
                        float bestSupportScore = 0f;
                        bool hasPatient = false;

                        for (int a = 0; a < aiUnits.Count; a++)
                        {
                            AiUnitSnapshot ally = aiUnits[a];
                            if (ally.UniqueEntityId == unit.UniqueEntityId || ally.HealthRatio >= 0.90f)
                            {
                                continue;
                            }

                            if (unit.DistanceSquaredTo(ally) > supportRadiusSq)
                            {
                                continue;
                            }

                            float supportScore = TacticalUtilityScorer.ScoreSupportOrRepair(unit, ally, doctrineWeights);
                            if (supportScore > bestSupportScore)
                            {
                                bestSupportScore = supportScore;
                                bestPatient = ally;
                                hasPatient = true;
                            }
                        }

                        if (hasPatient)
                        {
                            // Route via SurvivalKite category or BaseAndHarvesterDefense move so CompositeAiRuntimeBehaviour dispatches Move
                            proposals.Add(new AiIntentProposal(
                                moduleId: ModuleId,
                                commandTag: _commandTag + ".support",
                                unitId: unit.UniqueEntityId,
                                targetEntityId: -1,
                                destX: bestPatient.PosX,
                                destZ: bestPatient.PosZ,
                                category: AiIntentCategory.SurvivalKite,
                                utilityScore: bestSupportScore,
                                lockoutDurationSeconds: 1.0f,
                                requiresEmptyQueue: false));
                        }
                    }

                    // 3. Tactical Skill activation for support & specialist units
                    if (_settings.EnableTacticalSkills && unit.HasReadyActiveSkill)
                    {
                        bool hasHostileInRange = unit.TrySelectSkillTarget(hostiles, out AiUnitSnapshot nearestHostile);
                        float skillScore = TacticalUtilityScorer.ScoreTacticalSkill(unit, hasHostileInRange, doctrineWeights);
                        if (skillScore > 0f)
                        {
                            proposals.Add(new AiIntentProposal(
                                moduleId: ModuleId,
                                commandTag: _commandTag + ".skill",
                                unitId: unit.UniqueEntityId,
                                targetEntityId: ((!unit.ActiveSkillTargetsSelf || unit.ActiveSkillRequiresNearbyHostile) && hasHostileInRange) ? nearestHostile.UniqueEntityId : -1,
                                destX: hasHostileInRange ? nearestHostile.PosX : unit.PosX,
                                destZ: hasHostileInRange ? nearestHostile.PosZ : unit.PosZ,
                                category: AiIntentCategory.TacticalSkill,
                                utilityScore: skillScore,
                                lockoutDurationSeconds: 1.0f,
                                requiresEmptyQueue: false));
                        }
                    }

                    // 4. Fast Counter-Battery Interception
                    if (_settings.EnableCounterBattery && hasHostileArtillery &&
                        unit.IsCombatUnit && !unit.IsSupportUnit &&
                        unit.MoveSpeed >= FastInterceptorMinSpeed)
                    {
                        float distSq = unit.DistanceSquaredTo(hostileArtillery);
                        if (distSq <= (_settings.SupportRadius * _settings.SupportRadius * 2.25f))
                        {
                            float interceptScore = (85f + hostileArtillery.DpsEstimate * 1.4f) * doctrineWeights.SkirmishWeight;
                            proposals.Add(new AiIntentProposal(
                                moduleId: ModuleId,
                                commandTag: _commandTag + ".counter-battery",
                                unitId: unit.UniqueEntityId,
                                targetEntityId: hostileArtillery.UniqueEntityId,
                                destX: hostileArtillery.PosX,
                                destZ: hostileArtillery.PosZ,
                                category: AiIntentCategory.CombatTargeting,
                                utilityScore: interceptScore,
                                lockoutDurationSeconds: 1.0f,
                                requiresEmptyQueue: false));
                        }
                    }
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
            }
        }
    }
}
