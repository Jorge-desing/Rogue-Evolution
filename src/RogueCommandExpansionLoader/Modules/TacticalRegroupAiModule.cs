using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Coordinates tactical retreat and regrouping for severely wounded AI units.
    /// In native Rogue Command, units fight to the death on a single straight path, trickling into
    /// player defenses. This module orders wounded units to fall back to the base perimeter
    /// until repaired or joined by fresh reinforcements.
    /// </summary>
    public sealed class TacticalRegroupAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = TacticalRegroupParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            TacticalRegroupSettings settings;
            return TacticalRegroupParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            TacticalRegroupSettings settings;
            string diagnostic;
            if (!TacticalRegroupParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new TacticalRegroupRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class TacticalRegroupRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly TacticalRegroupSettings _settings;
            private readonly string _commandTag;
            private readonly HashSet<EntityController> _retreatingUnits = new HashSet<EntityController>();
            private float _nextCheckAt;
            private bool _stopped;

            public TacticalRegroupRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                TacticalRegroupSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
                _commandTag = "roguecommand.expansionloader.tactical-regroup." + Guid.NewGuid().ToString("N");
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
                if (aiUnits == null || aiUnits.Count == 0)
                {
                    return;
                }

                Vector3 basePosition = ResolveBasePosition(existing, _settings.BaseIndex, _gameBehaviour.transform.position);

                // Clean up destroyed units from retreating set
                _retreatingUnits.RemoveWhere(u => u == null || u.IsDestroyed || u.DestroyHasBeenStarted);

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

                    if (!unit.CanMoveAndMovementIsEnabled)
                    {
                        continue;
                    }

                    float healthFraction = unit.MaxHealth > 0f ? unit.CurrentHealth / unit.MaxHealth : 1f;

                    if (_retreatingUnits.Contains(unit))
                    {
                        // Check if unit is ready to resume combat (healed up or has nearby friendly reinforcements)
                        bool healed = healthFraction > (_settings.HealthRetreatThreshold + 0.15f);
                        int nearbyFriends = CountNearbyFriendlyUnits(unit, aiUnits, 15f);
                        bool reinforced = nearbyFriends >= _settings.RegroupMinSquadSize;

                        if (healed || reinforced)
                        {
                            _retreatingUnits.Remove(unit);
                            // Unit is free to acquire new targets naturally
                        }
                    }
                    else
                    {
                        // Check if unit should retreat
                        if (healthFraction <= _settings.HealthRetreatThreshold)
                        {
                            Vector3 dirToBase = (basePosition - unit.Position).normalized;
                            Vector3 retreatTarget = basePosition - dirToBase * _settings.RegroupDistanceToBase;

                            EntityCommand retreatCommand = EntityCommand.Move(
                                retreatTarget,
                                null,
                                EntityCommand.Role.ThisCommandWantsPathToBeCleared);
                            retreatCommand.tag = _commandTag;

                            unit.ExecuteCommand(retreatCommand, EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                            _retreatingUnits.Add(unit);
                        }
                    }
                }
            }

            private static int CountNearbyFriendlyUnits(EntityController unit, HashSet<EntityController> allUnits, float radius)
            {
                float radiusSq = radius * radius;
                int count = 0;
                foreach (EntityController other in allUnits)
                {
                    if (other == null || other == unit || other.IsDestroyed || other.DestroyHasBeenStarted)
                    {
                        continue;
                    }

                    if ((other.Position - unit.Position).sqrMagnitude <= radiusSq)
                    {
                        count++;
                    }
                }
                return count;
            }

            private static Vector3 ResolveBasePosition(ExistingControllers existing, int baseIndex, Vector3 fallback)
            {
                HashSet<EntityController> buildings = existing.AiBuildings();
                if (buildings != null)
                {
                    foreach (EntityController b in buildings)
                    {
                        if (b != null && !b.IsDestroyed && !b.DestroyHasBeenStarted)
                        {
                            if (baseIndex < 0 || b.aiBaseIndex == baseIndex)
                            {
                                return b.Position;
                            }
                        }
                    }
                }
                return fallback;
            }

            public void OnDestroyed()
            {
                _stopped = true;
                _retreatingUnits.Clear();
            }
        }
    }
}
