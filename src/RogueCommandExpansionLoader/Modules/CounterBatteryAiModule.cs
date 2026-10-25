using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Coordinates autonomous counter-battery responses for AI bases.
    /// In native Rogue Command, long-range siege or artillery units outside base defensive
    /// perimeters can shell AI structures without retaliation. This module monitors incoming
    /// threats and dispatches high-speed interceptor units to neutralize distant artillery.
    /// </summary>
    public sealed class CounterBatteryAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = CounterBatteryParameterParser.ModuleId;

#pragma warning disable CS0649
        internal static Func<string, float?> MinAttackRangeResolver;
#pragma warning restore CS0649

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            CounterBatterySettings settings;
            return CounterBatteryParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            CounterBatterySettings settings;
            string diagnostic;
            if (!CounterBatteryParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new CounterBatteryRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class CounterBatteryRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private const string CommandTagPrefix = "rcl-counterbattery-";
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly CounterBatterySettings _settings;
            private string _commandTag;
            private float _nextDispatchAt;
            private bool _stopped;

            public CounterBatteryRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                CounterBatterySettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
            }

            public void OnInitialized()
            {
                _commandTag = CommandTagPrefix + _gameBehaviour.GetInstanceID();
                _nextDispatchAt = 0f;
            }

            public void Tick()
            {
                if (_stopped || _gameBehaviour == null || _gameBehaviour.gameObject == null)
                {
                    return;
                }

                float now = Time.time;
                if (now < _nextDispatchAt)
                {
                    return;
                }

                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return;
                }

                EntityController threat = FindArtilleryThreat(existing);
                if (threat == null)
                {
                    _nextDispatchAt = now + 2.5f;
                    return;
                }

                List<EntityController> interceptors = SelectInterceptors(existing, threat.Position);
                if (interceptors.Count == 0)
                {
                    _nextDispatchAt = now + 2.0f;
                    return;
                }

                int dispatched = 0;
                for (int i = 0; i < interceptors.Count; i++)
                {
                    EntityController unit = interceptors[i];
                    EntityCommand attackCommand = EntityCommand.AttackMoveOnPosition(
                        threat.Position,
                        EntityCommand.Role.None,
                        "Counter-battery strike vector");
                    attackCommand.tag = _commandTag;

                    try
                    {
                        unit.ExecuteCommand(attackCommand,
                            EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                        dispatched++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[RogueCommandExpansionLoader] Counter-battery could not dispatch '" +
                            unit.gameObject.name + "': " + ex.GetType().Name + ": " + ex.Message);
                    }
                }

                if (dispatched > 0)
                {
                    _nextDispatchAt = now + _settings.CooldownSeconds;
                }
                else
                {
                    _nextDispatchAt = now + 2.0f;
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
                _commandTag = null;
            }

            private EntityController FindArtilleryThreat(ExistingControllers existing)
            {
                Vector3 basePos = _gameBehaviour.transform.position;
                HashSet<EntityController> playerEntities = existing.PlayerEntities();
                if (playerEntities == null || playerEntities.Count == 0)
                {
                    return null;
                }

                EntityController closestThreat = null;
                float closestDistSq = Single.MaxValue;
                float triggerDistSq = _settings.ThreatTriggerDistance * _settings.ThreatTriggerDistance;

                foreach (EntityController candidate in playerEntities)
                {
                    if (candidate == null ||
                        candidate.IsDestroyed ||
                        candidate.DestroyHasBeenStarted ||
                        !candidate.IsControlledByPlayer ||
                        candidate.canNotBeAttacked ||
                        candidate.canNotBeTargetedByAi ||
                        candidate.doNotRegisterGlobally)
                    {
                        continue;
                    }

                    bool isArtillery = false;
                    if (MinAttackRangeResolver != null)
                    {
                        float? minR = MinAttackRangeResolver(candidate.EntityId);
                        if (minR.HasValue && minR.Value > 0f)
                        {
                            isArtillery = true;
                        }
                    }

                    if (!isArtillery && candidate.WeaponRange >= _settings.ThreatTriggerDistance)
                    {
                        isArtillery = true;
                    }

                    if (!isArtillery && (candidate.HasRole(UnitRole.MassDestruction) || candidate.HasRole(UnitRole.Sniper)))
                    {
                        isArtillery = true;
                    }

                    if (!isArtillery)
                    {
                        continue;
                    }

                    float distSq = (candidate.Position - basePos).sqrMagnitude;
                    if (distSq < closestDistSq)
                    {
                        closestDistSq = distSq;
                        closestThreat = candidate;
                    }
                }

                return closestThreat;
            }

            private List<EntityController> SelectInterceptors(ExistingControllers existing, Vector3 threatPos)
            {
                var result = new List<EntityController>();
                HashSet<EntityController> units = existing.AiUnits();
                if (units == null || units.Count == 0)
                {
                    return result;
                }

                var candidates = new List<EntityController>();
                foreach (EntityController unit in units)
                {
                    if (unit == null ||
                        unit.IsDestroyed ||
                        unit.DestroyHasBeenStarted ||
                        unit.IsBuilding ||
                        !unit.IsControlledByAi ||
                        unit.canNotBeAttacked)
                    {
                        continue;
                    }

                    if (_settings.BaseIndex >= 0 && unit.aiBaseIndex != _settings.BaseIndex)
                    {
                        continue;
                    }

                    if (unit.MoveSpeed < _settings.MinInterceptorSpeed)
                    {
                        continue;
                    }

                    if (unit.CurrentCommandIsTaggedWith(_commandTag))
                    {
                        continue;
                    }

                    candidates.Add(unit);
                }

                candidates.Sort((a, b) =>
                {
                    float distA = (a.Position - threatPos).sqrMagnitude;
                    float distB = (b.Position - threatPos).sqrMagnitude;
                    return distA.CompareTo(distB);
                });

                int countToTake = Math.Min(candidates.Count, _settings.MaxInterceptors);
                for (int i = 0; i < countToTake; i++)
                {
                    result.Add(candidates[i]);
                }

                return result;
            }
        }
    }
}
