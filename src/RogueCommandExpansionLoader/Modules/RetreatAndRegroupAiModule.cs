using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// When an AI base's units fall below a configurable health threshold, issues a retreat/attack-move
    /// to a regroup point so damaged squads can recover and re-form.
    /// </summary>
    public sealed class RetreatAndRegroupAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = RetreatAndRegroupParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            RetreatAndRegroupSettings settings;
            return RetreatAndRegroupParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            RetreatAndRegroupSettings settings;
            string diagnostic;
            if (!RetreatAndRegroupParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new RetreatAndRegroupRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class RetreatAndRegroupRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private const string CommandTagPrefix = "roguecommand.expansionloader.retreat-regroup.";
            private const float ScanIntervalSeconds = 2f;

            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly RetreatAndRegroupSettings _settings;
            private string _commandTag;
            private float _nextScanAt;
            private bool _stopped;

            public RetreatAndRegroupRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                RetreatAndRegroupSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
            }

            public void OnInitialized()
            {
                _commandTag = CommandTagPrefix + _gameBehaviour.GetInstanceID();
                _nextScanAt = Time.time;
                Debug.Log("[RogueCommandExpansionLoader] Retreat-and-regroup AI attached to '" +
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

                _nextScanAt = now + ScanIntervalSeconds;

                HashSet<EntityController> aiUnits = ExistingControllers.Instance.AiUnits();
                if (aiUnits == null || aiUnits.Count == 0)
                {
                    return;
                }

                var candidates = new List<EntityController>();
                float totalHealthPercent = 0f;
                int counted = 0;
                foreach (EntityController unit in aiUnits)
                {
                    if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted ||
                        !unit.IsControlledByAi || !unit.IsUnit ||
                        unit.aiBaseIndex != _settings.BaseIndex ||
                        !unit.CanMoveAndMovementIsEnabled ||
                        unit.HasRole(UnitRole.Harvester) || unit.HasRole(UnitRole.Refinery) ||
                        unit.HasRole(UnitRole.Engineer) || unit.HasRole(UnitRole.Builder))
                    {
                        continue;
                    }

                    candidates.Add(unit);
                    if (unit.MaxHealth > 0f)
                    {
                        totalHealthPercent += unit.CurrentHealth / unit.MaxHealth * 100f;
                        counted++;
                    }
                }

                if (candidates.Count == 0 || counted == 0)
                {
                    return;
                }

                float averageHealthPercent = totalHealthPercent / counted;
                if (averageHealthPercent >= _settings.HealthThresholdPercent)
                {
                    return;
                }

                Vector3 regroupPoint = _gameBehaviour.transform.position;
                // Offset the regroup point slightly so units cluster around the base rather than stacking.
                // regroupRadiusCells is bounded and applied as a simple scalar offset magnitude.
                if (_settings.RegroupRadiusCells > 0)
                {
                    regroupPoint += new Vector3(_settings.RegroupRadiusCells * 0.5f, 0f, 0f);
                }

                int dispatched = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    EntityController unit = candidates[i];
                    if (unit.CurrentCommandIsTaggedWith(_commandTag))
                    {
                        continue;
                    }

                    EntityCommand retreatCommand = EntityCommand.AttackMoveOnPosition(
                        regroupPoint,
                        EntityCommand.Role.None,
                        "Retreat and regroup");

                    retreatCommand.tag = _commandTag;
                    try
                    {
                        unit.ExecuteCommand(retreatCommand,
                            EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
                        dispatched++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[RogueCommandExpansionLoader] Retreat-and-regroup could not dispatch '" +
                            unit.gameObject.name + "': " + ex.GetType().Name + ": " + ex.Message);
                    }
                }

                if (dispatched > 0)
                {
                    _nextScanAt = now + _settings.CooldownSeconds;
                    Debug.Log("[RogueCommandExpansionLoader] AI base " + _settings.BaseIndex +
                        " ordered retreat-and-regroup (" + dispatched + " units) at " + regroupPoint + ".");
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
                _commandTag = null;
            }
        }
    }
}
