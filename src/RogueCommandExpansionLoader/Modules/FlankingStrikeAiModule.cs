using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Coordinates tactical flanking strikes against player infrastructure or harvesters.
    /// Rather than advancing down central choke lanes, the squad navigates through lateral flank waypoints
    /// angled away from the direct assault vector before closing in on the objective.
    /// </summary>
    public sealed class FlankingStrikeAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = FlankingStrikeParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            FlankingStrikeSettings settings;
            return FlankingStrikeParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            FlankingStrikeSettings settings;
            string diagnostic;
            if (!FlankingStrikeParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new FlankingStrikeRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class FlankingStrikeRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private const string CommandTagPrefix = "roguecommand.expansionloader.flanking-strike.";

            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly FlankingStrikeSettings _settings;
            private string _commandTag;
            private float _nextAssaultAt;
            private int _strikeCount;
            private bool _stopped;

            public FlankingStrikeRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                FlankingStrikeSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
            }

            public void OnInitialized()
            {
                _commandTag = CommandTagPrefix + _gameBehaviour.GetInstanceID();
                _nextAssaultAt = _settings.MinGameTimeSeconds;
                Debug.Log("[RogueCommandExpansionLoader] Flanking strike AI attached to '" +
                    _gameBehaviour.gameObject.name + "' for base " + _settings.BaseIndex +
                    " (target: " + _settings.TargetPreference + ", angle: " + _settings.FlankAngleDegrees + "°).");
            }

            public void Tick()
            {
                if (_stopped || _gameBehaviour == null || _gameBehaviour.gameObject == null)
                {
                    return;
                }

                float now = Time.time;
                if (now < _settings.MinGameTimeSeconds || now < _nextAssaultAt)
                {
                    return;
                }

                EntityController targetEntity;
                if (!TryFindTarget(out targetEntity))
                {
                    return;
                }

                List<EntityController> squad = SelectFlankSquad(_settings.SquadSize);
                if (squad.Count < _settings.SquadSize)
                {
                    _nextAssaultAt = now + 5f;
                    return;
                }

                Vector3 basePos = _gameBehaviour.transform.position;
                Vector3 targetPos = targetEntity.Position;
                Vector3 directVector = targetPos - basePos;
                directVector.y = 0f;

                if (directVector.sqrMagnitude < 4f)
                {
                    directVector = Vector3.forward;
                }

                // Alternate left/right flank sides across successive strikes
                float sign = (_strikeCount % 2 == 0) ? 1f : -1f;
                _strikeCount++;

                Quaternion rotation = Quaternion.Euler(0f, sign * _settings.FlankAngleDegrees, 0f);
                Vector3 lateralDir = rotation * directVector.normalized;
                Vector3 flankWaypoint = basePos + lateralDir * _settings.FlankDistance;

                int dispatched = 0;
                for (int i = 0; i < squad.Count; i++)
                {
                    EntityController unit = squad[i];
                    EntityCommand flankCommand = EntityCommand.AttackMoveOnPosition(
                        flankWaypoint,
                        EntityCommand.Role.None,
                        "Flanking strike vector");
                    flankCommand.tag = _commandTag;

                    try
                    {
                        unit.ExecuteCommand(flankCommand,
                            EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                        dispatched++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[RogueCommandExpansionLoader] Flanking strike could not dispatch '" +
                            unit.gameObject.name + "': " + ex.GetType().Name + ": " + ex.Message);
                    }
                }

                if (dispatched > 0)
                {
                    _nextAssaultAt = now + _settings.CooldownSeconds;
                    Debug.Log("[RogueCommandExpansionLoader] AI Base " + _settings.BaseIndex +
                        " dispatched flank squad (" + dispatched + " units) toward waypoint " +
                        flankWaypoint + " targeting '" + targetEntity.gameObject.name + "'.");
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
                _commandTag = null;
            }

            private bool TryFindTarget(out EntityController target)
            {
                target = null;
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return false;
                }

                Vector3 basePos = _gameBehaviour.transform.position;

                if (String.Equals(_settings.TargetPreference, FlankingStrikeParameterParser.TargetPreferenceHarvester, StringComparison.Ordinal))
                {
                    HashSet<EntityController> harvesters = existing.Harvesters;
                    if (harvesters != null && harvesters.Count > 0)
                    {
                        target = FindClosestValidHarvester(harvesters, basePos);
                        if (target != null)
                        {
                            return true;
                        }
                    }
                }

                if (String.Equals(_settings.TargetPreference, FlankingStrikeParameterParser.TargetPreferenceRefinery, StringComparison.Ordinal))
                {
                    HashSet<EntityController> refineries = existing.PlayerRefineries();
                    if (refineries != null && refineries.Count > 0)
                    {
                        target = FindClosestValidTarget(refineries, basePos);
                        if (target != null)
                        {
                            return true;
                        }
                    }
                }

                HashSet<EntityController> knownBuildings = existing.PlayerBuildingsKnownToAi();
                if (knownBuildings != null && knownBuildings.Count > 0)
                {
                    target = FindClosestValidTarget(knownBuildings, basePos);
                    if (target != null)
                    {
                        return true;
                    }
                }

                EntityController fallbackHq = existing.EngineeringHq();
                if (IsValidTarget(fallbackHq))
                {
                    target = fallbackHq;
                    return true;
                }

                return false;
            }

            private static bool IsValidTarget(EntityController candidate)
            {
                return candidate != null &&
                    !candidate.IsDestroyed &&
                    !candidate.DestroyHasBeenStarted &&
                    candidate.IsControlledByPlayer &&
                    !candidate.canNotBeAttacked &&
                    !candidate.canNotBeTargetedByAi &&
                    candidate.CurrentHealth > 0;
            }

            private static EntityController FindClosestValidTarget(
                IEnumerable<EntityController> candidates,
                Vector3 referencePos)
            {
                EntityController closest = null;
                float closestDistSq = Single.MaxValue;

                foreach (EntityController candidate in candidates)
                {
                    if (!IsValidTarget(candidate))
                    {
                        continue;
                    }

                    float distSq = (candidate.Position - referencePos).sqrMagnitude;
                    if (distSq < closestDistSq)
                    {
                        closestDistSq = distSq;
                        closest = candidate;
                    }
                }

                return closest;
            }

            private static EntityController FindClosestValidHarvester(
                IEnumerable<EntityController> candidates,
                Vector3 referencePos)
            {
                EntityController closest = null;
                float closestDistSq = Single.MaxValue;

                foreach (EntityController candidate in candidates)
                {
                    if (candidate == null ||
                        candidate.IsDestroyed ||
                        candidate.DestroyHasBeenStarted ||
                        !candidate.IsControlledByPlayer ||
                        !candidate.IsUnit ||
                        candidate.canNotBeAttacked ||
                        candidate.canNotBeTargetedByAi ||
                        candidate.doNotRegisterGlobally)
                    {
                        continue;
                    }

                    float distSq = (candidate.Position - referencePos).sqrMagnitude;
                    if (distSq < closestDistSq)
                    {
                        closestDistSq = distSq;
                        closest = candidate;
                    }
                }

                return closest;
            }

            private List<EntityController> SelectFlankSquad(int requiredCount)
            {
                var squad = new List<EntityController>(requiredCount);
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return squad;
                }

                HashSet<EntityController> units = existing.AiUnits();
                if (units == null || units.Count == 0)
                {
                    return squad;
                }

                foreach (EntityController unit in units)
                {
                    if (unit == null || unit.DestroyHasBeenStarted || unit.IsBuilding)
                    {
                        continue;
                    }

                    if (unit.aiBaseIndex != _settings.BaseIndex)
                    {
                        continue;
                    }

                    // Avoid re-dispatching a unit already executing an active flank command
                    if (unit.CurrentCommandIsTaggedWith(_commandTag))
                    {
                        continue;
                    }

                    squad.Add(unit);
                    if (squad.Count >= requiredCount)
                    {
                        break;
                    }
                }

                return squad;
            }
        }
    }
}
