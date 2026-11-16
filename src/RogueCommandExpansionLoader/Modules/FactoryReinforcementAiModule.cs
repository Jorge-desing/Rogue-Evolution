using System;
using System.Collections.Generic;
using System.Linq;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Runtime;
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
            private readonly Dictionary<string, long> _lastProducedSequenceByFactoryId =
                new Dictionary<string, long>(StringComparer.Ordinal);
            private long _productionSequence;
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
                int currentArmy = 0;
                if (aiUnits != null)
                {
                    foreach (EntityController unit in aiUnits)
                    {
                        if (IsInScope(unit) && unit.IsUnit && !unit.IsBuilding) currentArmy++;
                    }
                }
                if (currentArmy >= _settings.MaxStandingArmy)
                {
                    return;
                }

                HashSet<EntityController> buildings = existing.AiBuildings();
                if (buildings == null || buildings.Count == 0)
                {
                    return;
                }

                // Reserve slots for units already being produced, including factories whose
                // local queue is full. Otherwise every scan can oversubscribe the army cap.
                foreach (EntityController factory in buildings)
                {
                    if (IsInScope(factory) && factory.IsFactory)
                    {
                        currentArmy += Math.Max(0, factory.InProductionCount ?? 0);
                    }
                }
                if (currentArmy >= _settings.MaxStandingArmy) return;

                var factoryList = new List<EntityController>(buildings.Count);
                var candidates = new List<FactoryReinforcementCandidate>(buildings.Count);
                foreach (EntityController candidateFactory in buildings)
                {
                    if (!IsInScope(candidateFactory) || !candidateFactory.IsFactory)
                    {
                        continue;
                    }

                    string factoryKey = candidateFactory.EntityId ?? string.Empty;
                    long lastSeq;
                    _lastProducedSequenceByFactoryId.TryGetValue(factoryKey, out lastSeq);
                    candidates.Add(new FactoryReinforcementCandidate
                    {
                        OriginalIndex = factoryList.Count,
                        IsModFactory = ScheduledBaseBuildLimitPolicy.IsModNamespacedEntity(factoryKey),
                        MechanicsPriority = RuntimeUnitMechanicsDirector.GetProductionPriority(candidateFactory),
                        LastProducedSequence = lastSeq
                    });
                    factoryList.Add(candidateFactory);
                }

                List<int> orderedIndices = FactoryReinforcementRotationPolicy.OrderCandidates(
                    candidates, _settings.PrioritizeModFactories);
                for (int i = 0; i < orderedIndices.Count; i++)
                {
                    EntityController factory = factoryList[orderedIndices[i]];
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

                    // Native free production replaces/refunds an existing queued entry.
                    // Wait for it to finish rather than restarting paid or free production.
                    if (!_settings.RequireEconomy && queued > 0) continue;

                    int balance = _settings.RequireEconomy ? Bank.ActualBalance(factory.gameObject.tag) : Int32.MaxValue;
                    if (!factory.IsProductionPossible(balance))
                    {
                        continue;
                    }

                    try
                    {
                        factory.Produce(false, !_settings.RequireEconomy, false);
                        int added = Math.Max(0, (factory.InProductionCount ?? 0) - queued);
                        if (added == 0) continue;
                        string producedFactoryId = factory.EntityId ?? string.Empty;
                        _lastProducedSequenceByFactoryId[producedFactoryId] = ++_productionSequence;
                        Debug.Log("[RogueCommandExpansionLoader] AI Factory '" + factory.gameObject.name +
                            "' queued reinforcement (" + (queued + 1) + "/" + _settings.MaxQueuePerFactory +
                            ", standing army: " + (currentArmy + 1) + "/" + _settings.MaxStandingArmy + ").");
                        currentArmy += added;
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
                _lastProducedSequenceByFactoryId.Clear();
            }

            private bool IsInScope(EntityController entity)
            {
                return entity != null && !entity.IsDestroyed && !entity.DestroyHasBeenStarted &&
                    entity.IsControlledByAi &&
                    (_settings.BaseIndex < 0 || entity.aiBaseIndex == _settings.BaseIndex);
            }
        }
    }
}
