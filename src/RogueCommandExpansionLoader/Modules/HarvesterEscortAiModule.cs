using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    public sealed class HarvesterEscortAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = HarvesterEscortParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            HarvesterEscortSettings settings;
            return HarvesterEscortParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            HarvesterEscortSettings settings;
            string diagnostic;
            if (!HarvesterEscortParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new HarvesterEscortRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class HarvesterEscortRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private const string CommandTagPrefix = "roguecommand.expansionloader.harvester-escort.";
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly HarvesterEscortSettings _settings;
            private readonly Dictionary<EntityController, Vector3> _escortDestinations = new Dictionary<EntityController, Vector3>();
            private string _commandTag;
            private float _nextScanAt;
            private bool _stopped;

            public HarvesterEscortRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                HarvesterEscortSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
            }

            public void OnInitialized()
            {
                _commandTag = CommandTagPrefix + _gameBehaviour.GetInstanceID();
                _nextScanAt = Time.time;
                Debug.Log("[RogueCommandExpansionLoader] Harvester-escort AI attached to '" +
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

                _nextScanAt = now + _settings.ScanIntervalSeconds;

                EntityController threatenedHarvester;
                EntityController attacker;
                if (!TryFindThreat(out threatenedHarvester, out attacker))
                {
                    ReleaseEscorts(null);
                    return;
                }

                List<EntityController> escorts = SelectEscorts(threatenedHarvester.Position, attacker.Position);
                ReleaseEscorts(escorts);
                if (escorts.Count == 0)
                {
                    return;
                }

                Vector3 destination = _settings.EngageAttackers && attacker != null
                    ? attacker.Position
                    : threatenedHarvester.Position;

                int dispatched = 0;
                for (int i = 0; i < escorts.Count; i++)
                {
                    EntityController escort = escorts[i];
                    bool ownsCommand = escort.CurrentCommandIsTaggedWith(_commandTag);
                    Vector3 priorDestination;
                    if (ownsCommand && _escortDestinations.TryGetValue(escort, out priorDestination) &&
                        (priorDestination - destination).sqrMagnitude < 1f)
                    {
                        continue;
                    }
                    EntityCommand command = EntityCommand.AttackMoveOnPosition(
                        destination,
                        EntityCommand.Role.None,
                        "Harvester escort");

                    command.tag = _commandTag;
                    try
                    {
                        escort.ExecuteCommand(command,
                            ownsCommand ? EntityController.CommandProcessingType.ExecuteAndCancelCurrentCommand
                                : EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                        _escortDestinations[escort] = destination;
                        dispatched++;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("[RogueCommandExpansionLoader] Harvester escort could not dispatch '" +
                            escort.gameObject.name + "': " + exception.GetType().Name + ": " +
                            exception.Message);
                    }
                }

                if (dispatched > 0)
                {
                    Debug.Log("[RogueCommandExpansionLoader] AI base " + _settings.BaseIndex +
                        " dispatched " + dispatched + " harvester escorts toward " + destination + ".");
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
                ReleaseEscorts(null);
                _commandTag = null;
            }

            private void ReleaseEscorts(List<EntityController> retained)
            {
                foreach (EntityController escort in new List<EntityController>(_escortDestinations.Keys))
                {
                    if (retained != null && retained.Contains(escort)) continue;
                    if (escort != null && !escort.IsDestroyed && !escort.DestroyHasBeenStarted &&
                        _commandTag != null && escort.CurrentCommandIsTaggedWith(_commandTag))
                    {
                        try
                        {
                            escort.ExecuteCommand(EntityCommand.Stop(), EntityController.CommandProcessingType.ExecuteAndCancelCurrentCommand);
                        }
                        catch (Exception exception)
                        {
                            Debug.LogWarning("[RogueCommandExpansionLoader] Could not release harvester escort: " + exception.Message);
                        }
                    }
                    _escortDestinations.Remove(escort);
                }
            }

            private bool TryFindThreat(out EntityController harvester, out EntityController attacker)
            {
                harvester = null;
                attacker = null;
                float bestDistance = float.MaxValue;

                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null || existing.PlayerUnits() == null) return false;
                HashSet<EntityController> harvesters = existing.Harvesters;
                if (harvesters == null || harvesters.Count == 0)
                {
                    return false;
                }

                foreach (EntityController candidateHarvester in harvesters)
                {
                    if (candidateHarvester == null ||
                        candidateHarvester.IsDestroyed ||
                        candidateHarvester.DestroyHasBeenStarted ||
                        !candidateHarvester.IsControlledByAi ||
                        !candidateHarvester.IsUnit ||
                        candidateHarvester.aiBaseIndex != _settings.BaseIndex ||
                        candidateHarvester.HasRole(UnitRole.Harvester) == false)
                    {
                        continue;
                    }

                    foreach (EntityController enemy in existing.PlayerUnits())
                    {
                        if (enemy == null ||
                            enemy.IsDestroyed ||
                            enemy.DestroyHasBeenStarted ||
                            !enemy.IsControlledByPlayer ||
                            !enemy.IsUnit ||
                            !enemy.IsCombatUnit ||
                            enemy.canNotBeAttacked ||
                            enemy.canNotBeTargetedByAi ||
                            enemy.doNotRegisterGlobally || !candidateHarvester.IsEnemy(enemy))
                        {
                            continue;
                        }

                        float distance = Vector3.Distance(candidateHarvester.Position, enemy.Position);
                        if (distance <= _settings.EscortRadiusCells && distance < bestDistance)
                        {
                            bestDistance = distance;
                            harvester = candidateHarvester;
                            attacker = enemy;
                        }
                    }
                }

                return harvester != null && attacker != null;
            }

            private List<EntityController> SelectEscorts(Vector3 harvesterPosition, Vector3 attackerPosition)
            {
                var escorts = new List<EntityController>();
                HashSet<EntityController> aiUnits = ExistingControllers.Instance.AiUnits();
                if (aiUnits == null || aiUnits.Count == 0)
                {
                    return escorts;
                }

                var candidates = new List<EntityController>();
                foreach (EntityController unit in aiUnits)
                {
                    if (unit == null ||
                        unit.IsDestroyed ||
                        unit.DestroyHasBeenStarted ||
                        !unit.IsControlledByAi ||
                        !unit.IsUnit ||
                        !unit.IsCombatUnit ||
                        unit.aiBaseIndex != _settings.BaseIndex ||
                        !unit.CanAttackAndIsAttackEnabled ||
                        !unit.CanMoveAndMovementIsEnabled ||
                        unit.HasRole(UnitRole.Harvester) ||
                        unit.HasRole(UnitRole.Refinery) ||
                        unit.HasRole(UnitRole.Engineer) ||
                        unit.HasRole(UnitRole.Builder))
                    {
                        continue;
                    }

                    candidates.Add(unit);
                }

                Vector3 reference = _settings.EngageAttackers ? attackerPosition : harvesterPosition;
                candidates.Sort((a, b) =>
                {
                    // Keep the assigned squad instead of recruiting another MaxEscorts each scan.
                    bool aAssigned = a.CurrentCommandIsTaggedWith(_commandTag);
                    bool bAssigned = b.CurrentCommandIsTaggedWith(_commandTag);
                    if (aAssigned != bAssigned) return aAssigned ? -1 : 1;
                    float distanceA = Vector3.Distance(a.Position, reference);
                    float distanceB = Vector3.Distance(b.Position, reference);
                    return distanceA.CompareTo(distanceB);
                });

                int take = Math.Min(_settings.MaxEscorts, candidates.Count);
                for (int i = 0; i < take; i++)
                {
                    escorts.Add(candidates[i]);
                }

                return escorts;
            }
        }
    }
}
