using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Adds a bounded build order through the game's observed public AI helper.
    /// The selected entity must already be in the active AI deck and be a building.
    /// </summary>
    public sealed class ScheduledBaseBuildAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = ScheduledBaseBuildParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            ScheduledBaseBuildSettings settings;
            return ScheduledBaseBuildParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            ScheduledBaseBuildSettings settings;
            string diagnostic;
            if (!ScheduledBaseBuildParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new ScheduledBaseBuildRuntimeBehaviour(gameBehaviour, settings.EntityId,
                settings.BaseIndex, settings.IntervalSeconds, settings.MaximumSuccessfulBuilds,
                settings.MaximumAttempts);
        }

        private sealed class ScheduledBaseBuildRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly string _requestedEntityId;
            private readonly int _baseIndex;
            private readonly float _intervalSeconds;
            private readonly int _maximumSuccessfulBuilds;
            private readonly int _maximumAttempts;
            private readonly HashSet<EntityController> _createdBuildings = new HashSet<EntityController>();

            private string _entityId;
            private int _deckMaxLiving;
            private float _nextAttemptAt;
            private int _successfulBuilds;
            private int _attempts;
            private bool _resolved;
            private bool _hasScheduledFirstAttempt;
            private bool _stopped;

            public ScheduledBaseBuildRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                string requestedEntityId,
                int baseIndex,
                float intervalSeconds,
                int maximumSuccessfulBuilds,
                int maximumAttempts)
            {
                _gameBehaviour = gameBehaviour;
                _requestedEntityId = requestedEntityId;
                _baseIndex = baseIndex;
                _intervalSeconds = intervalSeconds;
                _maximumSuccessfulBuilds = maximumSuccessfulBuilds;
                _maximumAttempts = maximumAttempts;
            }

            public void OnInitialized()
            {
                Debug.Log("[RogueCommandExpansionLoader] Scheduled base-build AI attached to '" +
                    _gameBehaviour.gameObject.name + "'.");
            }

            public void Tick()
            {
                if (_stopped || _gameBehaviour == null || _gameBehaviour.gameObject == null ||
                    !DefaultAiBehaviour.HasTickStarted_Static())
                {
                    return;
                }

                if (!_resolved && !TryResolveBuildOrder())
                {
                    return;
                }

                int baseCount = DefaultAiBehaviour.ActualAiStartBaseCount_Static();
                if (_baseIndex >= baseCount)
                {
                    Stop("baseIndex " + _baseIndex + " is outside the active AI base count " + baseCount + ".");
                    return;
                }

                float now = Time.time;
                if (!_hasScheduledFirstAttempt)
                {
                    _nextAttemptAt = now + _intervalSeconds;
                    _hasScheduledFirstAttempt = true;
                    return;
                }

                if (now < _nextAttemptAt)
                {
                    return;
                }

                _nextAttemptAt = now + _intervalSeconds;
                _attempts++;

                if (!TryRefreshDeckMaxLiving())
                {
                    return;
                }

                int livingFactories;
                string countDiagnostic;
                if (!TryCountLivingFactories(out livingFactories, out countDiagnostic))
                {
                    Stop("could not verify the active AI factory count: " + countDiagnostic);
                    return;
                }

                if (!ScheduledBaseBuildLimitPolicy.HasRemainingCapacity(livingFactories, _deckMaxLiving))
                {
                    Debug.LogWarning("[RogueCommandExpansionLoader] AI build attempt " + _attempts + "/" +
                        _maximumAttempts + " for '" + _entityId + "' skipped because base " + _baseIndex +
                        " already has " + livingFactories + " living factories (deck maxLiving " +
                        _deckMaxLiving + ").");
                    if (_attempts >= _maximumAttempts)
                    {
                        Stop("the configured attempt limit was reached.");
                    }
                    return;
                }

                EntityController created = null;
                try
                {
                    created = DefaultAiBehaviour.CreateBuilding_Static(_entityId, _baseIndex);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[RogueCommandExpansionLoader] AI build attempt " + _attempts + "/" +
                        _maximumAttempts + " for '" + _entityId + "' threw " +
                        exception.GetType().Name + ": " + exception.Message);
                }

                if (created != null)
                {
                    _successfulBuilds++;
                    _createdBuildings.Add(created);
                    Debug.Log("[RogueCommandExpansionLoader] AI built '" + _entityId + "' at base " + _baseIndex +
                        " (" + _successfulBuilds + "/" + _maximumSuccessfulBuilds + ").");
                }
                else
                {
                    Debug.LogWarning("[RogueCommandExpansionLoader] AI build attempt " + _attempts + "/" +
                        _maximumAttempts + " for '" + _entityId + "' returned no entity.");
                }

                if (_successfulBuilds >= _maximumSuccessfulBuilds)
                {
                    Stop("the configured successful-build limit was reached.");
                }
                else if (_attempts >= _maximumAttempts)
                {
                    Stop("the configured attempt limit was reached.");
                }
            }

            public void OnDestroyed()
            {
                _createdBuildings.Clear();
            }

            private bool TryResolveBuildOrder()
            {
                try
                {
                    List<EntityInAiDeck> constructableEntities = DefaultAiBehaviour.ConstructableEntities_Static();
                    if (constructableEntities == null || constructableEntities.Count == 0)
                    {
                        Stop("the active AI has no constructable entities in its deck.");
                        return false;
                    }

                    for (int i = 0; i < constructableEntities.Count; i++)
                    {
                        EntityInAiDeck candidate = constructableEntities[i];
                        string candidateId = candidate.entityId;
                        if (String.IsNullOrWhiteSpace(candidateId) ||
                            (_requestedEntityId != null && !String.Equals(candidateId, _requestedEntityId, StringComparison.Ordinal)) ||
                            !EntityBalancingStore.IsBuilding(candidateId))
                        {
                            continue;
                        }

                        _entityId = candidateId;
                        _deckMaxLiving = candidate.MaxLiving;
                        if (_deckMaxLiving < 1)
                        {
                            Stop("the requested building '" + _entityId + "' has no living capacity in the active AI deck.");
                            return false;
                        }

                        _resolved = true;
                        Debug.Log("[RogueCommandExpansionLoader] Scheduled base-build AI selected '" + _entityId + "'.");
                        return true;
                    }

                    Stop(_requestedEntityId == null
                        ? "the active AI deck contains no building that can be placed by the native helper."
                        : "the requested entity '" + _requestedEntityId + "' is not a building in the active AI deck.");
                    return false;
                }
                catch (Exception exception)
                {
                    Stop("could not resolve a building from the active AI deck: " +
                        exception.GetBaseException().Message);
                    return false;
                }
            }

            private bool TryRefreshDeckMaxLiving()
            {
                try
                {
                    List<EntityInAiDeck> constructableEntities = DefaultAiBehaviour.ConstructableEntities_Static();
                    if (constructableEntities != null)
                    {
                        for (int i = 0; i < constructableEntities.Count; i++)
                        {
                            EntityInAiDeck candidate = constructableEntities[i];
                            if (!String.Equals(candidate.entityId, _entityId, StringComparison.Ordinal) ||
                                !EntityBalancingStore.IsBuilding(candidate.entityId))
                            {
                                continue;
                            }

                            _deckMaxLiving = candidate.MaxLiving;
                            if (_deckMaxLiving > 0)
                            {
                                return true;
                            }
                        }
                    }

                    Stop("the requested building '" + _entityId + "' is no longer constructable in the active AI deck.");
                    return false;
                }
                catch (Exception exception)
                {
                    Stop("could not refresh the active AI deck capacity: " + exception.GetBaseException().Message);
                    return false;
                }
            }

            private bool TryCountLivingFactories(out int count, out string diagnostic)
            {
                count = 0;
                diagnostic = null;
                try
                {
                    ExistingControllers existing = ExistingControllers.Instance;
                    if (existing == null)
                    {
                        diagnostic = "ExistingControllers is unavailable.";
                        return false;
                    }

                    HashSet<EntityController> aiBuildings = existing.AiBuildings();
                    if (aiBuildings == null)
                    {
                        diagnostic = "the AI building registry is unavailable.";
                        return false;
                    }

                    var counted = new HashSet<EntityController>();
                    foreach (EntityController building in aiBuildings)
                    {
                        if (IsLivingFactoryForBase(building) && counted.Add(building))
                        {
                            count++;
                        }
                    }

                    foreach (EntityController building in _createdBuildings)
                    {
                        if (IsLivingFactoryForBase(building) && counted.Add(building))
                        {
                            count++;
                        }
                    }

                    return true;
                }
                catch (Exception exception)
                {
                    diagnostic = exception.GetBaseException().Message;
                    return false;
                }
            }

            private bool IsLivingFactoryForBase(EntityController building)
            {
                return building != null && !building.IsDestroyed && !building.DestroyHasBeenStarted &&
                    building.IsControlledByAi && building.aiBaseIndex == _baseIndex &&
                    String.Equals(building.EntityId, _entityId, StringComparison.Ordinal);
            }

            private void Stop(string reason)
            {
                if (_stopped)
                {
                    return;
                }

                _stopped = true;
                Debug.LogWarning("[RogueCommandExpansionLoader] Scheduled base-build AI stopped: " + reason);
            }
        }
    }
}
