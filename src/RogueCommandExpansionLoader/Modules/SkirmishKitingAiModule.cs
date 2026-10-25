using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Coordinates skirmish hit-and-run kiting micro for fast ranged AI units.
    /// Units maintain optimal firing distance, stepping back when hostiles encroach inside their retreat buffer.
    /// </summary>
    public sealed class SkirmishKitingAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = SkirmishKitingParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            SkirmishKitingSettings settings;
            return SkirmishKitingParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            SkirmishKitingSettings settings;
            string diagnostic;
            if (!SkirmishKitingParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new SkirmishKitingRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class SkirmishKitingRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private const string CommandTagPrefix = "rcl-skirmishkiting-";
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly SkirmishKitingSettings _settings;
            private string _commandTag;
            private float _nextKiteTickAt;
            private bool _stopped;

            public SkirmishKitingRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                SkirmishKitingSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
            }

            public void OnInitialized()
            {
                _commandTag = CommandTagPrefix + _gameBehaviour.GetInstanceID();
                _nextKiteTickAt = 0f;
            }

            public void Tick()
            {
                if (_stopped || _gameBehaviour == null || _gameBehaviour.gameObject == null)
                {
                    return;
                }

                float now = Time.time;
                if (now < _nextKiteTickAt)
                {
                    return;
                }

                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    _nextKiteTickAt = now + 1.0f;
                    return;
                }

                HashSet<EntityController> aiUnits = existing.AiUnits();
                if (aiUnits == null || aiUnits.Count == 0)
                {
                    _nextKiteTickAt = now + 1.0f;
                    return;
                }

                HashSet<EntityController> playerEntities = existing.PlayerEntities();
                if (playerEntities == null || playerEntities.Count == 0)
                {
                    _nextKiteTickAt = now + 1.0f;
                    return;
                }

                int kitedCount = 0;
                foreach (EntityController unit in aiUnits)
                {
                    if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted ||
                        unit.IsBuilding || !unit.IsControlledByAi || unit.canNotBeAttacked)
                    {
                        continue;
                    }

                    if (unit.MoveSpeed < _settings.MinKiteSpeed || unit.WeaponRange < 5.0f)
                    {
                        continue;
                    }

                    // If unit is currently executing an evasive kite retreat, allow it to complete
                    if (unit.CurrentCommandIsTaggedWith(_commandTag))
                    {
                        continue;
                    }

                    float retreatRange = unit.WeaponRange * _settings.RetreatDistanceRatio;
                    float retreatRangeSq = retreatRange * retreatRange;
                    float weaponRangeSq = unit.WeaponRange * unit.WeaponRange;

                    Vector3 unitPos = unit.Position;
                    EntityController closestThreat = null;
                    float closestDistSq = Single.MaxValue;
                    int evaluatedTargets = 0;

                    foreach (EntityController threat in playerEntities)
                    {
                        if (threat == null || threat.IsDestroyed || threat.DestroyHasBeenStarted ||
                            threat.canNotBeAttacked || threat.canNotBeTargetedByAi)
                        {
                            continue;
                        }

                        Vector3 threatPos = threat.Position;
                        float dx = threatPos.x - unitPos.x;
                        float dz = threatPos.z - unitPos.z;
                        float distSq = dx * dx + dz * dz;
                        if (distSq > weaponRangeSq)
                        {
                            continue;
                        }

                        if (distSq < closestDistSq)
                        {
                            closestDistSq = distSq;
                            closestThreat = threat;
                        }

                        evaluatedTargets++;
                        if (_settings.MaxKiteTargets > 0 && evaluatedTargets >= _settings.MaxKiteTargets)
                        {
                            break;
                        }
                    }

                    if (closestThreat != null && closestDistSq < retreatRangeSq)
                    {
                        Vector3 threatPos = closestThreat.Position;
                        float fdx = unitPos.x - threatPos.x;
                        float fdz = unitPos.z - threatPos.z;
                        float distSqThreat = fdx * fdx + fdz * fdz;
                        if (float.IsNaN(distSqThreat) || float.IsInfinity(distSqThreat) || distSqThreat < 0.001f)
                        {
                            fdx = 1f;
                            fdz = 0f;
                        }
                        else
                        {
                            float invDist = 1.0f / (float)Math.Sqrt(distSqThreat);
                            fdx *= invDist;
                            fdz *= invDist;
                        }

                        Vector3 retreatPos = new Vector3(
                            unitPos.x + fdx * _settings.FleeDistance,
                            unitPos.y,
                            unitPos.z + fdz * _settings.FleeDistance);

                        if (WorldGrid.Instance != null)
                        {
                            retreatPos = WorldGrid.Instance.ClampToWorld(retreatPos);
                        }

                        try
                        {
                            EntityCommand retreatCmd = EntityCommand.Move(retreatPos);
                            retreatCmd.tag = _commandTag;
                            unit.ExecuteCommand(retreatCmd, EntityController.CommandProcessingType.ExecuteAndDeleteCommandChain);

                            EntityCommand reengageCmd = EntityCommand.AttackMoveOnTarget(closestThreat, EntityCommand.Role.ThisCommandWantsPathToBeCleared);
                            unit.ExecuteCommand(reengageCmd, EntityController.CommandProcessingType.AppendToCommandChain);
                            kitedCount++;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning("[RogueCommandExpansionLoader] Skirmish kiting could not order retreat for '" +
                                unit.gameObject.name + "': " + ex.Message);
                        }
                    }
                }

                _nextKiteTickAt = now + _settings.CooldownSeconds;
            }

            public void OnDestroyed()
            {
                _stopped = true;
                _commandTag = null;
            }
        }
    }
}
