using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using UnityEngine;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Monitors AI base infrastructure and enqueues replacement or expansion structures
    /// (such as defensive turrets or refineries) into native AiBehaviourManager.ConstructionQueue
    /// when destroyed or when surplus crystal reserves allow.
    /// In native Rogue Command, ConstructionQueue is only populated at level start.
    /// </summary>
    public sealed class ConstructionExpansionAiModule : IRogueCommandAiModule
    {
        public const string ModuleId = ConstructionExpansionParameterParser.ModuleId;

        public string Id { get { return ModuleId; } }
        public int ApiVersion { get { return AiModuleRegistry.CurrentApiVersion; } }

        public bool TryValidateParameters(ContentModuleParameter[] parameters, out string diagnostic)
        {
            ConstructionExpansionSettings settings;
            return ConstructionExpansionParameterParser.TryParse(parameters, out settings, out diagnostic);
        }

        public IRogueCommandAiRuntimeBehaviour CreateBehaviour(
            DefaultAiBehaviour gameBehaviour,
            ContentModuleParameter[] parameters)
        {
            if (gameBehaviour == null)
            {
                throw new ArgumentNullException("gameBehaviour");
            }

            ConstructionExpansionSettings settings;
            string diagnostic;
            if (!ConstructionExpansionParameterParser.TryParse(parameters, out settings, out diagnostic))
            {
                throw new ArgumentException(diagnostic, "parameters");
            }

            return new ConstructionExpansionRuntimeBehaviour(gameBehaviour, settings);
        }

        private sealed class ConstructionExpansionRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
        {
            private readonly DefaultAiBehaviour _gameBehaviour;
            private readonly ConstructionExpansionSettings _settings;
            private float _nextCheckAt;
            private bool _stopped;

            public ConstructionExpansionRuntimeBehaviour(
                DefaultAiBehaviour gameBehaviour,
                ConstructionExpansionSettings settings)
            {
                _gameBehaviour = gameBehaviour;
                _settings = settings;
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

                HashSet<EntityController> aiBuildings = existing.AiBuildings();
                int currentBuildingCount = 0;

                if (aiBuildings != null)
                {
                    foreach (EntityController building in aiBuildings)
                    {
                        if (building == null || building.IsDestroyed || building.DestroyHasBeenStarted)
                        {
                            continue;
                        }

                        if (building.aiBaseIndex == _settings.BaseIndex &&
                            String.Equals(building.EntityId, _settings.BuildingId, StringComparison.Ordinal))
                        {
                            currentBuildingCount++;
                        }
                    }
                }

                if (currentBuildingCount >= _settings.DesiredCount)
                {
                    return;
                }

                // Check if the building is already pending construction in the queue
                try
                {
                    if (AiBehaviourManager.ConstructionQueue.Contains(_settings.BuildingId))
                    {
                        return;
                    }
                }
                catch (Exception)
                {
                    // Fall back if queue inspection fails
                }

                // Check AI crystal reserves
                int balance = Bank.ActualBalance("AI");
                int cost = EntityBalancingStore.Cost(_settings.BuildingId);

                if (balance < _settings.MinSurplusCrystals + cost)
                {
                    return;
                }

                try
                {
                    AiBehaviourManager.ConstructionQueue.Enqueue(_settings.BuildingId, _settings.QueuePriority);
                    Debug.Log("[RogueCommandExpansionLoader] Enqueued construction of '" +
                        _settings.BuildingId + "' for base " + _settings.BaseIndex +
                        " (current: " + currentBuildingCount + "/" + _settings.DesiredCount +
                        ", cost: " + cost + ", balance: " + balance + ").");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[RogueCommandExpansionLoader] Failed to enqueue building '" +
                        _settings.BuildingId + "': " + exception.Message);
                }
            }

            public void OnDestroyed()
            {
                _stopped = true;
            }
        }
    }
}
