using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Coordinates periodic siege strikes against known player installations, targeting either
    /// discovered buildings, refineries, or the engineering HQ once the enemy base accumulates
    /// a critical combat squad.
    /// </summary>
    public sealed class SiegeAssaultAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = SiegeAssaultParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            SiegeAssaultSettings settings;
            return SiegeAssaultParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            SiegeAssaultSettings settings;
            string diagnostic;
            if (!SiegeAssaultParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new SiegeAssaultRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class SiegeAssaultRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private const string CommandTagPrefix = "roguecommand.expansionloader.siege-assault.";

            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly SiegeAssaultSettings _settings;
            private readonly HashSet<int> _assignedSquadIds = new HashSet<int>();
            private string _commandTag;
            private float _nextAssaultAt;
            private bool _stopped;

            public SiegeAssaultRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                SiegeAssaultSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
            }

            public void OnInitialized()
            {
                _commandTag = CommandTagPrefix + _gameBehaviour.GetInstanceID();
                _nextAssaultAt = _settings.MinGameTimeSeconds;
                Debug.Log("[RogueCommandExpansionLoader] Siege assault AI attached to '" +
                    _gameBehaviour.gameObject.name + "' for base " + _settings.BaseIndex +
                    " (priority: " + _settings.TargetPriority + ").");
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

                EntityController targetBuilding;
                if (!TryFindTargetBuilding(out targetBuilding))
                {
                    return;
                }

                List<EntityController> squad = SelectSiegeSquad(_settings.MinSquadSize);
                if (squad.Count < _settings.MinSquadSize)
                {
                    // Force not yet assembled, retry in 5 seconds
                    _nextAssaultAt = now + 5f;
                    return;
                }

                int dispatched = 0;
                Vector3 targetPos = targetBuilding.Position;
                for (int i = 0; i < squad.Count; i++)
                {
                    EntityController unit = squad[i];
                    EntityCommand assaultCommand = EntityCommand.AttackMoveOnPosition(
                        targetPos,
                        EntityCommand.Role.None,
                        "Siege assault operation");
                    assaultCommand.tag = _commandTag;

                    try
                    {
                        unit.ExecuteCommand(assaultCommand,
                            EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                        _assignedSquadIds.Add(unit.UniqueEntityId);
                        dispatched++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[RogueCommandExpansionLoader] Siege assault could not dispatch '" +
                            unit.gameObject.name + "': " + ex.GetType().Name + ": " + ex.Message);
                    }
                }

                if (dispatched > 0)
                {
                    _nextAssaultAt = now + _settings.CooldownSeconds;
                    Debug.Log("[RogueCommandExpansionLoader] AI base " + _settings.BaseIndex +
                        " launched siege assault (" + dispatched + " units) toward player structure '" +
                        targetBuilding.name + "' at " + targetPos + ".");
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
                _assignedSquadIds.Clear();
                _commandTag = null;
            }

            private bool TryFindTargetBuilding(out EntityController target)
            {
                target = null;
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return false;
                }

                Vector3 basePos = _gameBehaviour.transform.position;

                // Priority 1: Engineering HQ if specifically requested
                if (_settings.TargetPriority == SiegeAssaultParameterParser.TargetPriorityHq)
                {
                    EntityController hq = existing.EngineeringHq();
                    if (IsValidTarget(hq))
                    {
                        target = hq;
                        return true;
                    }
                }

                // Priority 2: Player refineries if requested or as economic target
                if (_settings.TargetPriority == SiegeAssaultParameterParser.TargetPriorityRefinery)
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

                // Default/Fallback: Closest known player building
                HashSet<EntityController> knownBuildings = existing.PlayerBuildingsKnownToAi();
                if (knownBuildings != null && knownBuildings.Count > 0)
                {
                    target = FindClosestValidTarget(knownBuildings, basePos);
                    if (target != null)
                    {
                        return true;
                    }
                }

                // Ultimate fallback: check HQ if known buildings list is empty
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
                float closestDistSq = float.MaxValue;

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

            private List<EntityController> SelectSiegeSquad(int requiredCount)
            {
                var squad = new List<EntityController>();
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return squad;
                }

                HashSet<EntityController> aiUnits = existing.AiUnits();
                if (aiUnits == null || aiUnits.Count == 0)
                {
                    return squad;
                }

                foreach (EntityController unit in aiUnits)
                {
                    if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted ||
                        !unit.IsControlledByAi || !unit.IsUnit ||
                        unit.aiBaseIndex != _settings.BaseIndex ||
                        !unit.CanAttackAndIsAttackEnabled ||
                        unit.HasRole(UnitRole.Harvester) ||
                        unit.HasRole(UnitRole.Refinery) ||
                        unit.HasRole(UnitRole.Engineer) ||
                        unit.HasRole(UnitRole.Builder))
                    {
                        continue;
                    }

                    // Avoid pulling already dispatched raiders or workers
                    if (_assignedSquadIds.Contains(unit.UniqueEntityId))
                    {
                        continue;
                    }

                    squad.Add(unit);
                }

                return squad;
            }
        }
    }
}
