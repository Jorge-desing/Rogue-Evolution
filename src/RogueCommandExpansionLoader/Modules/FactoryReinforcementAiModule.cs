using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Coordinates dynamic reinforcement production in AI factories during active combat.
    /// In native Rogue Command, DefaultAiBehaviour never triggers factory production after stage start.
    /// This module monitors factories, enforces standing army caps and queue bounds, and triggers Produce().
    /// </summary>
    public sealed class FactoryReinforcementAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = FactoryReinforcementParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            FactoryReinforcementSettings settings;
            return FactoryReinforcementParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            FactoryReinforcementSettings settings;
            string diagnostic;
            if (!FactoryReinforcementParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new FactoryReinforcementRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class FactoryReinforcementRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly FactoryReinforcementSettings _settings;
            private float _nextCheckAt;
            private bool _stopped;

            public FactoryReinforcementRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                FactoryReinforcementSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
            }

            public void OnInitialized()
            {
                _nextCheckAt = _settings.MinGameTimeSeconds;
                Debug.Log("[RogueCommandExpansionLoader] Factory reinforcement AI attached to '" +
                    _gameBehaviour.gameObject.name + "' (base: " + _settings.BaseIndex +
                    ", army cap: " + _settings.MaxStandingArmy + ").");
            }

            public void Tick()
            {
                if (_stopped || _gameBehaviour == null || _gameBehaviour.gameObject == null)
                {
                    return;
                }

                float now = Time.time;
                if (now < _settings.MinGameTimeSeconds || now < _nextCheckAt)
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
                int currentArmy = aiUnits != null ? aiUnits.Count : 0;
                if (currentArmy >= _settings.MaxStandingArmy)
                {
                    return;
                }

                HashSet<EntityController> buildings = existing.AiBuildings();
                if (buildings == null || buildings.Count == 0)
                {
                    return;
                }

                foreach (EntityController factory in buildings)
                {
                    if (factory == null || factory.DestroyHasBeenStarted || !factory.IsFactory || !factory.IsControlledByAi)
                    {
                        continue;
                    }

                    if (_settings.BaseIndex >= 0 && factory.aiBaseIndex != _settings.BaseIndex)
                    {
                        continue;
                    }

                    int? queuedCountNullable = factory.InProductionCount;
                    int queued = queuedCountNullable.HasValue ? queuedCountNullable.Value : 0;
                    if (queued >= _settings.MaxQueuePerFactory)
                    {
                        continue;
                    }

                    int balance = _settings.RequireEconomy ? Bank.ActualBalance(factory.gameObject.tag) : Int32.MaxValue;
                    if (!factory.IsProductionPossible(balance))
                    {
                        continue;
                    }

                    try
                    {
                        factory.Produce(false, !_settings.RequireEconomy, false);
                        Debug.Log("[RogueCommandExpansionLoader] AI Factory '" + factory.gameObject.name +
                            "' queued reinforcement (" + (queued + 1) + "/" + _settings.MaxQueuePerFactory +
                            ", standing army: " + (currentArmy + 1) + "/" + _settings.MaxStandingArmy + ").");
                        currentArmy++;
                        if (currentArmy >= _settings.MaxStandingArmy)
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[RogueCommandExpansionLoader] Factory reinforcement could not trigger Produce on '" +
                            factory.gameObject.name + "': " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
            }
        }
    }
}
