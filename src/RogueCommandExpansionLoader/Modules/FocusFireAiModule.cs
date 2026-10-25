using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Coordinates tactical focus fire across AI combat units.
    /// In native Rogue Command, DefaultAiBehaviour causes units to acquire closest targets
    /// independently, resulting in scattered DPS across chaff. This module clusters nearby
    /// friendly units in combat and coordinates simultaneous volleys on prioritized targets.
    /// </summary>
    public sealed class FocusFireAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = FocusFireParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            FocusFireSettings settings;
            return FocusFireParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            FocusFireSettings settings;
            string diagnostic;
            if (!FocusFireParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new FocusFireRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class FocusFireRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly FocusFireSettings _settings;
            private readonly string _commandTag;
            private float _nextCheckAt;
            private bool _stopped;

            public FocusFireRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                FocusFireSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
                _commandTag = "roguecommand.expansionloader.focus-fire." + Guid.NewGuid().ToString("N");
            }

            public void OnInitialized()
            {
                _nextCheckAt = Time.time + _settings.CheckIntervalSeconds;
            }

            public void Tick()
            {
                if (_stopped || _gameBehaviour == null || _gameBehaviour.gameObject == null)
                {
                    return;
                }

                float now = Time.time;
                if (now < _nextCheckAt)
                {
                    return;
                }

                _nextCheckAt = now + _settings.CheckIntervalSeconds;

                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return;
                }

                HashSet<EntityController> aiUnits = existing.AiUnits();
                if (aiUnits == null || aiUnits.Count < _settings.MinUnitsInSquad)
                {
                    return;
                }

                HashSet<EntityController> playerEntities = existing.PlayerEntities();
                if (playerEntities == null || playerEntities.Count == 0)
                {
                    return;
                }

                // Gather mobile AI combat units eligible for focus-fire coordination
                List<EntityController> eligibleCombatUnits = new List<EntityController>();
                foreach (EntityController unit in aiUnits)
                {
                    if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted)
                    {
                        continue;
                    }

                    if (_settings.BaseIndex >= 0 && unit.aiBaseIndex != _settings.BaseIndex)
                    {
                        continue;
                    }

                    if (!unit.CanAttackAndIsAttackEnabled || !unit.CanMoveAndMovementIsEnabled)
                    {
                        continue;
                    }

                    eligibleCombatUnits.Add(unit);
                }

                if (eligibleCombatUnits.Count < _settings.MinUnitsInSquad)
                {
                    return;
                }

                // Identify combat squads by clustering units within squadRadius
                float squadRadiusSq = _settings.SquadRadius * _settings.SquadRadius;
                List<List<EntityController>> squads = ClusterUnitsIntoSquads(eligibleCombatUnits, squadRadiusSq);

                for (int s = 0; s < squads.Count; s++)
                {
                    List<EntityController> squad = squads[s];
                    if (squad.Count < _settings.MinUnitsInSquad)
                    {
                        continue;
                    }

                    Vector3 squadCenter = ComputeSquadCenter(squad);
                    EntityController priorityTarget = SelectPriorityTarget(squadCenter, playerEntities, _settings);

                    if (priorityTarget == null || priorityTarget.IsDestroyed || priorityTarget.DestroyHasBeenStarted)
                    {
                        continue;
                    }

                    // Order squad units to focus fire if not already attacking this target
                    for (int u = 0; u < squad.Count; u++)
                    {
                        EntityController unit = squad[u];
                        if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted)
                        {
                            continue;
                        }

                        if (unit.CurrentCommandIsTaggedWith(_commandTag))
                        {
                            continue;
                        }

                        EntityCommand command = EntityCommand.AttackMoveOnTarget(
                            priorityTarget,
                            EntityCommand.Role.None);
                        command.tag = _commandTag;

                        unit.ExecuteCommand(command, EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                    }
                }
            }

            private static List<List<EntityController>> ClusterUnitsIntoSquads(
                List<EntityController> units,
                float radiusSquared)
            {
                List<List<EntityController>> clusters = new List<List<EntityController>>();
                HashSet<EntityController> visited = new HashSet<EntityController>();

                for (int i = 0; i < units.Count; i++)
                {
                    EntityController leader = units[i];
                    if (visited.Contains(leader))
                    {
                        continue;
                    }

                    List<EntityController> cluster = new List<EntityController> { leader };
                    visited.Add(leader);

                    for (int j = i + 1; j < units.Count; j++)
                    {
                        EntityController candidate = units[j];
                        if (visited.Contains(candidate))
                        {
                            continue;
                        }

                        if ((candidate.Position - leader.Position).sqrMagnitude <= radiusSquared)
                        {
                            cluster.Add(candidate);
                            visited.Add(candidate);
                        }
                    }

                    clusters.Add(cluster);
                }

                return clusters;
            }

            private static Vector3 ComputeSquadCenter(List<EntityController> squad)
            {
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < squad.Count; i++)
                {
                    sum += squad[i].Position;
                }
                return sum / squad.Count;
            }

            private static EntityController SelectPriorityTarget(
                Vector3 squadCenter,
                HashSet<EntityController> candidates,
                FocusFireSettings settings)
            {
                EntityController bestCandidate = null;
                float bestScore = Single.NegativeInfinity;
                float maxRangeSq = (settings.SquadRadius * 1.5f) * (settings.SquadRadius * 1.5f);

                foreach (EntityController candidate in candidates)
                {
                    if (candidate == null || candidate.IsDestroyed || candidate.DestroyHasBeenStarted ||
                        candidate.canNotBeAttacked || candidate.canNotBeTargetedByAi)
                    {
                        continue;
                    }

                    float distSq = (candidate.Position - squadCenter).sqrMagnitude;
                    if (distSq > maxRangeSq)
                    {
                        continue;
                    }

                    float score = 0f;
                    float healthFraction = candidate.MaxHealth > 0f
                        ? candidate.CurrentHealth / candidate.MaxHealth
                        : 1f;

                    if (settings.PriorityMode == "LowestHealth")
                    {
                        // Higher score for lower health percentage (finisher priority)
                        score = (1f - healthFraction) * 100f - (distSq * 0.05f);
                    }
                    else if (settings.PriorityMode == "HighestThreat")
                    {
                        // Higher score for units with higher max health and combat role
                        score = candidate.MaxHealth * 0.5f + (candidate.IsCombatUnit ? 50f : 0f) - (distSq * 0.05f);
                    }
                    else if (settings.PriorityMode == "TargetRole")
                    {
                        bool matchesRole = false;
                        if (!String.IsNullOrWhiteSpace(settings.TargetRole) && settings.TargetRole != "None")
                        {
                            try
                            {
                                UnitRole role = (UnitRole)Enum.Parse(typeof(UnitRole), settings.TargetRole, true);
                                matchesRole = candidate.HasRole(role);
                            }
                            catch (Exception) { }
                        }

                        score = (matchesRole ? 200f : 0f) + (1f - healthFraction) * 50f - (distSq * 0.05f);
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCandidate = candidate;
                    }
                }

                return bestCandidate;
            }

            public void OnDestroyed()
            {
                _stopped = true;
            }
        }
    }
}
