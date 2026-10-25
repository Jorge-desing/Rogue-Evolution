using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Tactical AI module that monitors combat progression, increments the attack wave counter
    /// via AiBehaviourManager.IncrementAttackWaveNumber_Static(), and coordinates escalation waves.
    /// </summary>
    public sealed class WaveEscalationAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = WaveEscalationParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            WaveEscalationSettings settings;
            return WaveEscalationParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            WaveEscalationSettings settings;
            string diagnostic;
            if (!WaveEscalationParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new WaveEscalationRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class WaveEscalationRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly WaveEscalationSettings _settings;
            private float _nextWaveAt;
            private int _dispatchedWaves;
            private bool _stopped;

            public WaveEscalationRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                WaveEscalationSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
                _nextWaveAt = 0f;
                _dispatchedWaves = 0;
                _stopped = false;
            }

            public void OnInitialized()
            {
                _nextWaveAt = Time.time + _settings.WaveIntervalSeconds;
            }

            public void Tick()
            {
                if (_stopped || Time.time < _nextWaveAt)
                {
                    return;
                }

                _nextWaveAt = Time.time + _settings.WaveIntervalSeconds;

                if (_dispatchedWaves < _settings.MaxWaves)
                {
                    _dispatchedWaves++;
                    AiBehaviourManager.IncrementAttackWaveNumber_Static();
                    DispatchEscalationWave();
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
            }

            private void DispatchEscalationWave()
            {
                if (ExistingControllers.Instance == null)
                {
                    return;
                }

                HashSet<EntityController> aiUnits = ExistingControllers.Instance.AiUnits();
                if (aiUnits == null || aiUnits.Count == 0)
                {
                    return;
                }

                HashSet<EntityController> playerBuildings = ExistingControllers.Instance.PlayerBuildingsKnownToAi();
                EntityController targetBuilding = null;
                if (playerBuildings != null && playerBuildings.Count > 0)
                {
                    foreach (EntityController building in playerBuildings)
                    {
                        if (building != null && !building.DestroyHasBeenStarted)
                        {
                            targetBuilding = building;
                            if (building.EntityId == "EngineeringHq" || building.EntityId.Contains("Hq"))
                            {
                                break;
                            }
                        }
                    }
                }

                if (targetBuilding == null)
                {
                    EntityController hq = ExistingControllers.Instance.EngineeringHq();
                    if (hq != null && !hq.DestroyHasBeenStarted)
                    {
                        targetBuilding = hq;
                    }
                }

                if (targetBuilding == null)
                {
                    return;
                }

                // Dispatch idle or moving combat units
                int dispatchCount = 0;
                int maxToDispatch = 2 + (_dispatchedWaves * 2);

                foreach (EntityController unit in aiUnits)
                {
                    if (unit == null || unit.DestroyHasBeenStarted || unit.IsBuilding)
                    {
                        continue;
                    }

                    if (_settings.BaseIndex >= 0 && unit.aiBaseIndex != _settings.BaseIndex)
                    {
                        continue;
                    }

                    // Only order units not already actively engaging a target or executing wave command
                    if (unit.CurrentAttackTarget != null && !unit.CurrentAttackTarget.DestroyHasBeenStarted)
                    {
                        continue;
                    }

                    string waveTag = "RCL_Wave_" + unit.UniqueEntityId;
                    if (unit.CurrentCommandIsTaggedWith(waveTag))
                    {
                        continue;
                    }

                    EntityCommand command = EntityCommand.AttackMoveOnTarget(targetBuilding, EntityCommand.Role.None);
                    command.tag = waveTag;
                    unit.ExecuteCommand(command, EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                    dispatchCount++;

                    if (dispatchCount >= maxToDispatch)
                    {
                        break;
                    }
                }
            }
        }
    }
}
