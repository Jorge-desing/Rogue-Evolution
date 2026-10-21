using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Dispatches bounded attack-move orders to native AI-building waypoints and intercepts player
    /// units attacking the configured AI base. Repeated waypoint orders are inferred patrol behavior.
    /// </summary>
    public sealed class PatrolAndInterceptAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = PatrolAndInterceptParameterParser.ModuleId;
        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            PatrolAndInterceptSettings settings;
            return PatrolAndInterceptParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null) throw new ArgumentNullException("gameBehaviour");
            PatrolAndInterceptSettings settings;
            string diagnostic;
            if (!PatrolAndInterceptParameterParser.TryParse(parameters, out settings, out diagnostic))
                throw new ArgumentException(diagnostic, "parameters");
            return new RuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class RuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private const string TagPrefix = "roguecommand.expansionloader.patrol-intercept.";
            private readonly DefaultAiBehaviour _owner;
            private readonly PatrolAndInterceptSettings _settings;
            private string _tag;
            private float _nextScanAt;
            private bool _stopped;

            public RuntimeBehaviour(DefaultAiBehaviour owner, PatrolAndInterceptSettings settings)
            {
                _owner = owner;
                _settings = settings;
            }

            public void OnInitialized()
            {
                _tag = TagPrefix + _owner.GetInstanceID();
                _nextScanAt = Time.time;
                Debug.Log("[RogueCommandExpansionLoader] Patrol/intercept module attached for AI base " +
                    _settings.BaseIndex + ".");
            }

            public void Tick()
            {
                if (_stopped || _owner == null || _owner.gameObject == null || Time.time < _nextScanAt) return;
                _nextScanAt = Time.time + _settings.ScanIntervalSeconds;

                HashSet<EntityController> players = ExistingControllers.Instance.PlayerUnits();
                HashSet<EntityController> defenders = ExistingControllers.Instance.AiUnits();
                HashSet<EntityController> buildings = ExistingControllers.Instance.AiBuildings();
                if (players == null || defenders == null || buildings == null) return;

                var baseBuildings = new List<EntityController>();
                var waypointCells = new List<Vector2Int>();
                foreach (EntityController building in buildings)
                {
                    if (building == null || building.IsDestroyed || building.DestroyHasBeenStarted ||
                        !building.IsControlledByAi || !building.IsBuilding ||
                        building.aiBaseIndex != _settings.BaseIndex)
                    {
                        continue;
                    }

                    baseBuildings.Add(building);
                    waypointCells.Add(building.CurrentCell);
                }
                if (baseBuildings.Count == 0) return;

                var threats = new List<EntityController>();
                foreach (EntityController player in players)
                {
                    if (player == null || player.IsDestroyed || player.DestroyHasBeenStarted ||
                        !player.IsControlledByPlayer || !player.IsUnit || !player.CanAttackAndIsAttackEnabled ||
                        player.canNotBeAttacked || player.canNotBeTargetedByAi || player.doNotRegisterGlobally)
                        continue;
                    EntityController target = player.CurrentAttackTarget;
                    if (target == null || target.IsDestroyed || target.DestroyHasBeenStarted ||
                        !target.IsControlledByAi || !target.IsBuilding || target.aiBaseIndex != _settings.BaseIndex)
                        continue;
                    if (ReactiveBaseDefenseThreatRange.IsWithinRadius(player.CurrentCell, target.CurrentCell,
                        _settings.InterceptRadiusCells)) threats.Add(player);
                }

                int dispatched = 0;
                for (int i = 0; i < threats.Count && dispatched < _settings.MaxPatrolUnits; i++)
                {
                    EntityController threat = threats[i];
                    EntityController building = threat.CurrentAttackTarget;
                    if (building == null || building.IsDestroyed || building.DestroyHasBeenStarted ||
                        !building.IsControlledByAi || building.aiBaseIndex != _settings.BaseIndex)
                    {
                        continue;
                    }

                    EntityController responder = null;
                    float nearest = Single.MaxValue;
                    foreach (EntityController candidate in defenders)
                    {
                        if (candidate == null || candidate.IsDestroyed || candidate.DestroyHasBeenStarted ||
                            !candidate.IsControlledByAi || !candidate.IsUnit || candidate.aiBaseIndex != _settings.BaseIndex ||
                            !candidate.IsCombatUnit || !candidate.CanAttackAndIsAttackEnabled ||
                            !candidate.CanMoveAndMovementIsEnabled || candidate.CurrentAttackTarget != null ||
                            candidate.CommandChainExecutionIsPaused || candidate.HasSkillActivationInCommandChain ||
                            candidate.CurrentCommandIsTaggedWith(_tag) ||
                            (candidate.CurrentPlayerCommandName != EntityCommand.Command.DoNothing &&
                             candidate.CurrentPlayerCommandName != EntityCommand.Command.Move) ||
                            !ReactiveBaseDefenseThreatRange.IsWithinRadius(candidate.CurrentCell, building.CurrentCell,
                                _settings.PatrolRadiusCells)) continue;
                        float distance = ReactiveBaseDefenseThreatRange.DistanceSquared(candidate.CurrentCell, building.CurrentCell);
                        if (distance < nearest) { nearest = distance; responder = candidate; }
                    }
                    if (responder == null) continue;
                    EntityCommand command = EntityCommand.AttackMoveOnPosition(threat.Position,
                        EntityCommand.Role.None, "Patrol intercept");
                    command.tag = _tag;
                    try
                    {
                        responder.ExecuteCommand(command,
                            EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                        dispatched++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[RogueCommandExpansionLoader] Patrol/intercept dispatch failed: " +
                            ex.GetType().Name + ": " + ex.Message);
                    }
                }

                foreach (EntityController candidate in defenders)
                {
                    if (dispatched >= _settings.MaxPatrolUnits) return;
                    if (candidate == null || candidate.IsDestroyed || candidate.DestroyHasBeenStarted ||
                        !candidate.IsControlledByAi || !candidate.IsUnit || candidate.aiBaseIndex != _settings.BaseIndex ||
                        !candidate.IsCombatUnit || !candidate.CanAttackAndIsAttackEnabled ||
                        !candidate.CanMoveAndMovementIsEnabled || candidate.CurrentAttackTarget != null ||
                        candidate.CommandChainExecutionIsPaused || candidate.HasSkillActivationInCommandChain ||
                        candidate.CurrentCommandIsTaggedWith(_tag) ||
                        (candidate.CurrentPlayerCommandName != EntityCommand.Command.DoNothing &&
                         candidate.CurrentPlayerCommandName != EntityCommand.Command.Move))
                    {
                        continue;
                    }

                    int waypointIndex;
                    if (!PatrolAndInterceptWaypointResolver.TrySelectNearest(candidate.CurrentCell,
                            waypointCells, _settings.PatrolRadiusCells, out waypointIndex))
                    {
                        continue;
                    }

                    EntityController waypoint = baseBuildings[waypointIndex];
                    EntityCommand patrol = EntityCommand.AttackMoveOnPosition(waypoint.Position,
                        EntityCommand.Role.None, "Base patrol waypoint");
                    patrol.tag = _tag;
                    try
                    {
                        candidate.ExecuteCommand(patrol,
                            EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                        dispatched++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[RogueCommandExpansionLoader] Patrol waypoint dispatch failed: " +
                            ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
                _tag = null;
            }
        }
    }
}
