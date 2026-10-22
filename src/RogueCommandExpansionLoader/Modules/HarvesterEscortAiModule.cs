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
                    return;
                }

                List<EntityController> escorts = SelectEscorts(threatenedHarvester.Position, attacker.Position);
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
                    EntityCommand command = EntityCommand.AttackMoveOnPosition(
                        destination,
                        EntityCommand.Role.None,
                        "Harvester escort");

                    command.tag = _commandTag;
                    try
                    {
                        escort.ExecuteCommand(command,
                            EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
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
                _commandTag = null;
            }

            private bool TryFindThreat(out EntityController harvester, out EntityController attacker)
            {
                harvester = null;
                attacker = null;
                float bestDistance = float.MaxValue;

                HashSet<EntityController> harvesters = ExistingControllers.Instance.Harvesters;
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

                    foreach (EntityController enemy in ExistingControllers.Instance.PlayerUnits())
                    {
                        if (enemy == null ||
                            enemy.IsDestroyed ||
                            enemy.DestroyHasBeenStarted ||
                            !enemy.IsControlledByPlayer ||
                            !enemy.IsUnit ||
                            enemy.canNotBeAttacked ||
                            enemy.canNotBeTargetedByAi ||
                            enemy.doNotRegisterGlobally)
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

                    if (unit.CurrentCommandIsTaggedWith(_commandTag))
                    {
                        continue;
                    }

                    candidates.Add(unit);
                }

                Vector3 reference = _settings.EngageAttackers ? attackerPosition : harvesterPosition;
                candidates.Sort((a, b) =>
                {
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