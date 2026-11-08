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
    /// intents for survival/dead-zone kiting, base/harvester defense, and role-aware focus targeting.
    /// Consolidates multiple tactical behaviors into a single profile module slot.
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
                if (aiUnits == null || aiUnits.Count == 0 || hostiles == null || hostiles.Count == 0)
                {
                    return;
                }

                float engagementRadiusSq = _settings.EngagementRadius * _settings.EngagementRadius;
                float defenseRadiusSq = _settings.DefenseRadius * _settings.DefenseRadius;

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

                    // Find nearest hostile and best combat target
                    AiUnitSnapshot nearestHostile = default(AiUnitSnapshot);
                    float nearestDistSq = float.MaxValue;
                    bool hasNearest = false;

                    AiUnitSnapshot bestTarget = default(AiUnitSnapshot);
                    float bestTargetScore = 0f;
                    bool hasBestTarget = false;

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
                            float targetScore = TacticalUtilityScorer.ScoreCombatTarget(unit, hostile, doctrineWeights);
                            if (targetScore > bestTargetScore)
                            {
                                bestTargetScore = targetScore;
                                bestTarget = hostile;
                                hasBestTarget = true;
                            }
                        }
                    }

                    // 1. Survival / Dead-zone kiting proposal
                    if (_settings.EnableSurvivalKiting && hasNearest && emittedForUnit < _settings.MaxProposalsPerUnit)
                    {
                        float kiteScore = TacticalUtilityScorer.ScoreSurvivalOrKite(unit, nearestHostile, doctrineWeights);
                        if (unit.HealthRatio <= _settings.RetreatHealthRatio && kiteScore < 120f)
                        {
                            kiteScore = 120f * doctrineWeights.SurvivalWeight;
                        }

                        if (kiteScore > 0f)
                        {
                            float dx = unit.PosX - nearestHostile.PosX;
                            float dz = unit.PosZ - nearestHostile.PosZ;
                            float len = (float)Math.Sqrt(dx * dx + dz * dz);
                            if (len < 0.01f)
                            {
                                dx = -1f;
                                dz = 0f;
                            }
                            else
                            {
                                dx /= len;
                                dz /= len;
                            }

                            float retreatDistance = Math.Max(8.0f, unit.MinAttackRange > 0f ? unit.MinAttackRange + 4.0f : 10.0f);
                            proposals.Add(new AiIntentProposal(
                                moduleId: ModuleId,
                                commandTag: _commandTag + ".kite",
                                unitId: unit.UniqueEntityId,
                                targetEntityId: -1,
                                destX: unit.PosX + dx * retreatDistance,
                                destZ: unit.PosZ + dz * retreatDistance,
                                category: AiIntentCategory.SurvivalKite,
                                utilityScore: kiteScore,
                                lockoutDurationSeconds: 1.25f,
                                requiresEmptyQueue: false));
                            emittedForUnit++;
                        }
                    }

                    // 2. Base & Harvester defense proposal when under pressure
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

                    // 3. Focus combat targeting proposal
                    if (_settings.EnableFocusTargeting && hasBestTarget &&
                        emittedForUnit < _settings.MaxProposalsPerUnit)
                    {
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
            }
        }
    }
}
