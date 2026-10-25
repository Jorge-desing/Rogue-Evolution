using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Directs a squad of AI units from the configured base to attack-move toward exposed player
    /// harvesters gathering resources. This prevents the player from free-farming the map and forces
    /// defensive escort deployment.
    /// </summary>
    public sealed class HarvesterRaiderAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = HarvesterRaiderParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            HarvesterRaiderSettings settings;
            return HarvesterRaiderParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            HarvesterRaiderSettings settings;
            string diagnostic;
            if (!HarvesterRaiderParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new HarvesterRaiderRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class HarvesterRaiderRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private const string CommandTagPrefix = "roguecommand.expansionloader.harvester-raid.";

            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly HarvesterRaiderSettings _settings;
            private string _commandTag;
            private float _nextScanAt;
            private bool _stopped;

            public HarvesterRaiderRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                HarvesterRaiderSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
            }

            public void OnInitialized()
            {
                _commandTag = CommandTagPrefix + _gameBehaviour.GetInstanceID();
                _nextScanAt = _settings.MinGameTimeSeconds;
                Debug.Log("[RogueCommandExpansionLoader] Harvester-raider AI attached to '" +
                    _gameBehaviour.gameObject.name + "' for base " + _settings.BaseIndex + ".");
            }

            public void Tick()
            {
                if (_stopped || _gameBehaviour == null || _gameBehaviour.gameObject == null)
                {
                    return;
                }

                float now = Time.time;
                if (now < _settings.MinGameTimeSeconds || now < _nextScanAt)
                {
                    return;
                }

                _nextScanAt = now + _settings.ScanIntervalSeconds;

                EntityController targetHarvester;
                if (!TryFindTargetHarvester(out targetHarvester))
                {
                    return;
                }

                List<EntityController> raiders = SelectRaiderSquad(targetHarvester.Position, _settings.RaidSquadSize);
                if (raiders.Count == 0)
                {
                    return;
                }

                int dispatched = 0;
                Vector3 targetPos = targetHarvester.Position;
                for (int i = 0; i < raiders.Count; i++)
                {
                    EntityController raider = raiders[i];
                    EntityCommand raidCommand = EntityCommand.AttackMoveOnPosition(
                        targetPos,
                        EntityCommand.Role.ThisCommandWantsPathToBeCleared,
                        "Harvester raid strike");
                    raidCommand.tag = _commandTag;

                    try
                    {
                        raider.ExecuteCommand(raidCommand,
                            EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                        dispatched++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[RogueCommandExpansionLoader] Harvester raider could not dispatch '" +
                            raider.gameObject.name + "': " + ex.GetType().Name + ": " + ex.Message);
                    }
                }

                if (dispatched > 0)
                {
                    _nextScanAt = now + _settings.CooldownSeconds;
                    Debug.Log("[RogueCommandExpansionLoader] AI base " + _settings.BaseIndex +
                        " launched harvester raid squad (" + dispatched + " units) toward player harvester at " +
                        targetHarvester.Position + ".");
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
                _commandTag = null;
            }

            private bool TryFindTargetHarvester(out EntityController target)
            {
                target = null;
                HashSet<EntityController> harvesters = ExistingControllers.Instance.Harvesters;
                if (harvesters == null || harvesters.Count == 0)
                {
                    return false;
                }

                float closestDistance = float.MaxValue;
                Vector3 basePos = _gameBehaviour.transform.position;

                foreach (EntityController harvester in harvesters)
                {
                    if (harvester == null || harvester.IsDestroyed || harvester.DestroyHasBeenStarted ||
                        !harvester.IsControlledByPlayer || !harvester.IsUnit ||
                        harvester.canNotBeAttacked || harvester.canNotBeTargetedByAi ||
                        harvester.doNotRegisterGlobally)
                    {
                        continue;
                    }

                    float dist = Vector3.Distance(basePos, harvester.Position);
                    if (dist < closestDistance)
                    {
                        closestDistance = dist;
                        target = harvester;
                    }
                }

                return target != null;
            }

            private List<EntityController> SelectRaiderSquad(Vector3 targetPosition, int count)
            {
                var squad = new List<EntityController>();
                HashSet<EntityController> aiUnits = ExistingControllers.Instance.AiUnits();
                if (aiUnits == null || aiUnits.Count == 0)
                {
                    return squad;
                }

                var candidates = new List<EntityController>();
                foreach (EntityController unit in aiUnits)
                {
                    if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted ||
                        !unit.IsControlledByAi || !unit.IsUnit ||
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

                    if (unit.CurrentAttackTarget != null && unit.CurrentAttackTarget.HasRole(UnitRole.Harvester))
                    {
                        continue;
                    }

                    candidates.Add(unit);
                }

                // Sort by distance to the target harvester so closest combat troops respond quickly
                candidates.Sort((a, b) =>
                {
                    float distA = Vector3.Distance(a.Position, targetPosition);
                    float distB = Vector3.Distance(b.Position, targetPosition);
                    return distA.CompareTo(distB);
                });

                int take = Math.Min(count, candidates.Count);
                for (int i = 0; i < take; i++)
                {
                    squad.Add(candidates[i]);
                }

                return squad;
            }
        }
    }
}
