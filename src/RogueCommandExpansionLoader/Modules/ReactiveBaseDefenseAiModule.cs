using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Sends a bounded number of nearby AI units to attack-move toward the last known position
    /// of player units attacking a building at the configured AI base. Responses use a finite
    /// position command and are inserted before a safe current order so the native order can resume.
    /// </summary>
    public sealed class ReactiveBaseDefenseAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = ReactiveBaseDefenseParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            ReactiveBaseDefenseSettings settings;
            return ReactiveBaseDefenseParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            ReactiveBaseDefenseSettings settings;
            string diagnostic;
            if (!ReactiveBaseDefenseParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new ReactiveBaseDefenseRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class ReactiveBaseDefenseRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private const string CommandTagPrefix = "roguecommand.expansionloader.reactive-defense.";

            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly ReactiveBaseDefenseSettings _settings;
            private readonly HashSet<int> _respondedDefenderIds = new HashSet<int>();
            private string _commandTag;
            private float _nextScanAt;
            private int _responsesIssued;
            private bool _stopped;

            public ReactiveBaseDefenseRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                ReactiveBaseDefenseSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
            }

            public void OnInitialized()
            {
                _commandTag = CommandTagPrefix + _gameBehaviour.GetInstanceID();
                Debug.Log("[RogueCommandExpansionLoader] Reactive base-defense AI attached to '" +
                    _gameBehaviour.gameObject.name + "' for base " + _settings.BaseIndex + ".");
            }

            public void Tick()
            {
                if (_stopped || _gameBehaviour == null || _gameBehaviour.gameObject == null)
                {
                    return;
                }

                float now = Time.time;
                if (now < _nextScanAt)
                {
                    return;
                }

                _nextScanAt = now + _settings.CooldownSeconds;
                if (!TryGetActiveThreats(out List<EntityController> attackers))
                {
                    _respondedDefenderIds.Clear();
                    _responsesIssued = 0;
                    return;
                }

                if (_responsesIssued >= _settings.MaximumResponders)
                {
                    return;
                }

                HashSet<EntityController> committedResponders = FindCommittedResponders(attackers);
                if (committedResponders.Count > _responsesIssued)
                {
                    _responsesIssued = committedResponders.Count;
                }

                if (_responsesIssued >= _settings.MaximumResponders)
                {
                    return;
                }

                while (_responsesIssued < _settings.MaximumResponders)
                {
                    EntityController defender;
                    EntityController attacker;
                    if (!TryChooseResponder(attackers, committedResponders, out defender, out attacker))
                    {
                        return;
                    }

                    EntityCommand response = EntityCommand.AttackMoveOnPosition(
                        attacker.Position,
                        EntityCommand.Role.None,
                        "Reactive base defense");
                    response.tag = _commandTag;
                    try
                    {
                        defender.ExecuteCommand(response,
                            EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                        _respondedDefenderIds.Add(defender.UniqueEntityId);
                        committedResponders.Add(defender);
                        _responsesIssued++;
                        Debug.Log("[RogueCommandExpansionLoader] AI base " + _settings.BaseIndex +
                            " dispatched '" + defender.gameObject.name + "' (" + defender.EntityId + ") to counterattack '" +
                            attacker.gameObject.name + "' (" + _responsesIssued + "/" +
                            _settings.MaximumResponders + ").");
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("[RogueCommandExpansionLoader] Reactive defense could not dispatch '" +
                            defender.gameObject.name + "': " + exception.GetType().Name + ": " + exception.Message);
                        return;
                    }
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
                _respondedDefenderIds.Clear();
                _responsesIssued = 0;
                _commandTag = null;
            }

            private bool TryGetActiveThreats(out List<EntityController> attackers)
            {
                attackers = new List<EntityController>();
                HashSet<EntityController> playerUnits = ExistingControllers.Instance.PlayerUnits();
                if (playerUnits == null || playerUnits.Count == 0)
                {
                    return false;
                }

                foreach (EntityController playerUnit in playerUnits)
                {
                    if (playerUnit == null || playerUnit.IsDestroyed || playerUnit.DestroyHasBeenStarted ||
                        !playerUnit.IsControlledByPlayer || !playerUnit.IsUnit ||
                        !playerUnit.CanAttackAndIsAttackEnabled ||
                        playerUnit.canNotBeAttacked || playerUnit.canNotBeTargetedByAi ||
                        playerUnit.doNotRegisterGlobally)
                    {
                        continue;
                    }

                    EntityController target = playerUnit.CurrentAttackTarget;
                    if (target == null || target.IsDestroyed || target.DestroyHasBeenStarted ||
                        !IsProtectedTarget(target))
                    {
                        continue;
                    }

                    if (!ReactiveBaseDefenseThreatRange.IsWithinRadius(
                        playerUnit.CurrentCell, target.CurrentCell, _settings.DefenseRadiusCells))
                    {
                        continue;
                    }

                    attackers.Add(playerUnit);
                }

                return attackers.Count > 0;
            }

            private bool IsProtectedTarget(EntityController target)
            {
                bool protectsBuildings = _settings.ProtectedTargetType == "Buildings" ||
                    _settings.ProtectedTargetType == "BuildingsAndHarvesters";
                bool protectsHarvesters = _settings.ProtectedTargetType == "Harvesters" ||
                    _settings.ProtectedTargetType == "BuildingsAndHarvesters";
                if (protectsBuildings && target.IsControlledByAi && target.IsBuilding &&
                    target.aiBaseIndex == _settings.BaseIndex)
                {
                    return true;
                }

                return protectsHarvesters && target.IsControlledByAi && target.IsUnit &&
                    target.HasRole(UnitRole.Harvester) && target.aiBaseIndex == _settings.BaseIndex;
            }

            private HashSet<EntityController> FindCommittedResponders(List<EntityController> attackers)
            {
                var committed = new HashSet<EntityController>();
                HashSet<EntityController> aiUnits = ExistingControllers.Instance.AiUnits();
                if (aiUnits == null)
                {
                    return committed;
                }

                foreach (EntityController unit in aiUnits)
                {
                    if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted || !unit.IsUnit ||
                        !unit.IsControlledByAi || unit.aiBaseIndex != _settings.BaseIndex)
                    {
                        continue;
                    }

                    EntityController currentTarget = unit.CurrentAttackTarget;
                    bool alreadyCounterattacking = currentTarget != null && currentTarget.IsControlledByPlayer &&
                        ContainsReference(attackers, currentTarget);
                    if (unit.CurrentCommandIsTaggedWith(_commandTag) || alreadyCounterattacking)
                    {
                        _respondedDefenderIds.Add(unit.UniqueEntityId);
                        committed.Add(unit);
                    }
                    else if (_respondedDefenderIds.Contains(unit.UniqueEntityId))
                    {
                        committed.Add(unit);
                    }
                }

                return committed;
            }

            private bool TryChooseResponder(
                List<EntityController> attackers,
                HashSet<EntityController> committedResponders,
                out EntityController defender,
                out EntityController attacker)
            {
                defender = null;
                attacker = null;
                HashSet<EntityController> aiUnits = ExistingControllers.Instance.AiUnits();
                if (aiUnits == null || aiUnits.Count == 0)
                {
                    return false;
                }

                float nearestDefenderDistance = Single.MaxValue;
                foreach (EntityController candidate in aiUnits)
                {
                    if (candidate == null || candidate.IsDestroyed || candidate.DestroyHasBeenStarted ||
                        !candidate.IsControlledByAi || !candidate.IsUnit ||
                        candidate.aiBaseIndex != _settings.BaseIndex ||
                        (_settings.UnitId != null && !String.Equals(candidate.EntityId, _settings.UnitId, StringComparison.Ordinal)) ||
                        committedResponders.Contains(candidate))
                    {
                        continue;
                    }

                    EntityController currentTarget = candidate.CurrentAttackTarget;
                    EntityCommand.Command currentCommand = candidate.CurrentPlayerCommandName;
                    if (currentTarget != null || !candidate.CanAttackAndIsAttackEnabled ||
                        !candidate.CanMoveAndMovementIsEnabled || !candidate.IsCombatUnit ||
                        candidate.IsEngineer ||
                        candidate.HasRole(UnitRole.Spawn) || candidate.HasRole(UnitRole.Skill) ||
                        candidate.CommandChainExecutionIsPaused || candidate.HasSkillActivationInCommandChain ||
                        (currentCommand != EntityCommand.Command.DoNothing &&
                            currentCommand != EntityCommand.Command.Move))
                    {
                        continue;
                    }

                    float distanceToProtectedTarget;
                    EntityController nearestAttacker;
                    if (!TryFindNearestThreat(candidate, attackers, out nearestAttacker, out distanceToProtectedTarget))
                    {
                        continue;
                    }

                    float maximumDistanceSquared = _settings.DefenseRadiusCells * _settings.DefenseRadiusCells;
                    if (distanceToProtectedTarget > maximumDistanceSquared ||
                        distanceToProtectedTarget >= nearestDefenderDistance)
                    {
                        continue;
                    }

                    nearestDefenderDistance = distanceToProtectedTarget;
                    defender = candidate;
                    attacker = nearestAttacker;
                }

                return defender != null && attacker != null;
            }

            private bool TryFindNearestThreat(
                EntityController defender,
                List<EntityController> attackers,
                out EntityController nearestAttacker,
                out float nearestTargetDistanceSquared)
            {
                nearestAttacker = null;
                nearestTargetDistanceSquared = Single.MaxValue;
                float nearestAttackerDistanceSquared = Single.MaxValue;
                for (int i = 0; i < attackers.Count; i++)
                {
                    EntityController attacker = attackers[i];
                    EntityController protectedTarget = attackers[i].CurrentAttackTarget;
                    if (attacker == null || protectedTarget == null)
                    {
                        continue;
                    }

                    float targetDistance = ReactiveBaseDefenseThreatRange.DistanceSquared(
                        defender.CurrentCell, protectedTarget.CurrentCell);
                    float attackerDistance = ReactiveBaseDefenseThreatRange.DistanceSquared(
                        defender.CurrentCell, attacker.CurrentCell);
                    if (!ReactiveBaseDefenseThreatRange.IsWithinRadius(
                        defender.CurrentCell, protectedTarget.CurrentCell, _settings.DefenseRadiusCells))
                    {
                        continue;
                    }

                    if (nearestAttacker == null || targetDistance < nearestTargetDistanceSquared ||
                        (targetDistance == nearestTargetDistanceSquared &&
                            attackerDistance < nearestAttackerDistanceSquared))
                    {
                        nearestAttacker = attacker;
                        nearestTargetDistanceSquared = targetDistance;
                        nearestAttackerDistanceSquared = attackerDistance;
                    }
                }

                return nearestAttacker != null;
            }

            private static bool ContainsReference(List<EntityController> entities, EntityController target)
            {
                for (int i = 0; i < entities.Count; i++)
                {
                    if (entities[i] == target)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
