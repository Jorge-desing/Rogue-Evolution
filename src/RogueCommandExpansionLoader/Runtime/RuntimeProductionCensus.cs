using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable diagnostic row for a factory building and its current production state.
    /// Decoupled from Unity types to support offline testing and MCP inspection.
    /// </summary>
    public readonly struct ProductionFactoryDiagnosticRow
    {
        public ProductionFactoryDiagnosticRow(
            string entityKeyId,
            bool isControlledByPlayer,
            int inProductionCount,
            bool isProducing,
            float currentHealth,
            float maxHealth,
            float healthRatio)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            IsControlledByPlayer = isControlledByPlayer;
            InProductionCount = inProductionCount;
            IsProducing = isProducing;
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
            HealthRatio = healthRatio;
        }

        public string EntityKeyId { get; }
        public bool IsControlledByPlayer { get; }
        public int InProductionCount { get; }
        public bool IsProducing { get; }
        public float CurrentHealth { get; }
        public float MaxHealth { get; }
        public float HealthRatio { get; }
    }

    /// <summary>
    /// Snapshot of factory production queues across player and AI battlefield structures.
    /// </summary>
    public sealed class ProductionCensus
    {
        /// <summary>
        /// Loss/omission counter placed first to ensure bounded diagnostic trace bounds are respected.
        /// </summary>
        public int OmittedFactoryCount { get; }
        public int TotalFactoryCount { get; }
        public int ActiveProducingCount { get; }
        public int TotalQueuedUnitsCount { get; }
        public int PlayerFactoryCount { get; }
        public int AiFactoryCount { get; }
        public IReadOnlyList<ProductionFactoryDiagnosticRow> FactoryRows { get; }

        public ProductionCensus(
            int omittedFactoryCount,
            int totalFactoryCount,
            int activeProducingCount,
            int totalQueuedUnitsCount,
            int playerFactoryCount,
            int aiFactoryCount,
            IReadOnlyList<ProductionFactoryDiagnosticRow> factoryRows)
        {
            OmittedFactoryCount = omittedFactoryCount;
            TotalFactoryCount = totalFactoryCount;
            ActiveProducingCount = activeProducingCount;
            TotalQueuedUnitsCount = totalQueuedUnitsCount;
            PlayerFactoryCount = playerFactoryCount;
            AiFactoryCount = aiFactoryCount;
            FactoryRows = factoryRows ?? new List<ProductionFactoryDiagnosticRow>().AsReadOnly();
        }

        public static ProductionCensus Empty { get; } =
            new ProductionCensus(0, 0, 0, 0, 0, 0, new List<ProductionFactoryDiagnosticRow>().AsReadOnly());
    }

    /// <summary>
    /// Diagnostic collector that inspects living factories in <see cref="ExistingControllers"/>
    /// and collects production queue metrics.
    /// </summary>
    public static class RuntimeProductionCensus
    {
        private const int MaxTrackedFactories = 32;

        public static ProductionCensus Current()
        {
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return ProductionCensus.Empty;
                }

                var candidateEntities = new List<EntityController>();

                try
                {
                    HashSet<EntityController> playerBuildings = existing.PlayerBuildingsKnownToAi();
                    if (playerBuildings != null)
                    {
                        candidateEntities.AddRange(playerBuildings);
                    }
                }
                catch
                {
                    // Degrade gracefully
                }

                try
                {
                    HashSet<EntityController> playerUnits = existing.PlayerUnits();
                    if (playerUnits != null)
                    {
                        foreach (EntityController entity in playerUnits)
                        {
                            if (entity != null && (entity.InProductionCount != null || entity.IsBuilding))
                            {
                                candidateEntities.Add(entity);
                            }
                        }
                    }
                }
                catch
                {
                    // Degrade gracefully
                }

                try
                {
                    HashSet<EntityController> aiBuildings = existing.AiBuildings();
                    if (aiBuildings != null)
                    {
                        candidateEntities.AddRange(aiBuildings);
                    }
                }
                catch
                {
                    // Degrade gracefully
                }

                int totalFactories = 0;
                int activeProducing = 0;
                int totalQueued = 0;
                int playerFactories = 0;
                int aiFactories = 0;
                int omittedCount = 0;
                var rows = new List<ProductionFactoryDiagnosticRow>();

                var seenEntities = new HashSet<EntityController>();
                foreach (EntityController entity in candidateEntities)
                {
                    if (entity == null || entity.IsDestroyed || entity.DestroyHasBeenStarted)
                    {
                        continue;
                    }

                    if (!seenEntities.Add(entity))
                    {
                        continue;
                    }

                    bool isFactory = entity.InProductionCount != null;
                    if (!isFactory)
                    {
                        continue;
                    }

                    totalFactories++;
                    if (entity.IsControlledByPlayer)
                    {
                        playerFactories++;
                    }
                    else
                    {
                        aiFactories++;
                    }

                    int inProd = entity.InProductionCount ?? 0;
                    bool isProducing = inProd > 0;
                    if (isProducing)
                    {
                        activeProducing++;
                        totalQueued += inProd;
                    }

                    if (rows.Count < MaxTrackedFactories)
                    {
                        float maxHp = entity.MaxHealth > 0.001f ? entity.MaxHealth : 100f;
                        float curHp = entity.CurrentHealth >= 0f ? entity.CurrentHealth : 0f;
                        float ratio = Mathf.Clamp(curHp / maxHp, 0f, 1f);

                        string keyId = entity.EntityId ?? string.Empty;
                        rows.Add(new ProductionFactoryDiagnosticRow(
                            keyId,
                            entity.IsControlledByPlayer,
                            inProd,
                            isProducing,
                            curHp,
                            maxHp,
                            ratio));
                    }
                    else
                    {
                        omittedCount++;
                    }
                }

                return new ProductionCensus(
                    omittedCount,
                    totalFactories,
                    activeProducing,
                    totalQueued,
                    playerFactories,
                    aiFactories,
                    rows.AsReadOnly());
            }
            catch
            {
                return ProductionCensus.Empty;
            }
        }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when commanding in-game production via the runtime bridge.
    /// </summary>
    public readonly struct ProductionOrderResult
    {
        public ProductionOrderResult(bool success, string factoryKeyId, int inProductionCount, string message)
        {
            Success = success;
            FactoryKeyId = factoryKeyId ?? string.Empty;
            InProductionCount = inProductionCount;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string FactoryKeyId { get; }
        public int InProductionCount { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Safe in-game gameplay director allowing authorized automated agents to trigger
    /// unit production on player factories for validation and testing.
    /// </summary>
    public static class RuntimeGameplayProductionDirector
    {
        public static ProductionOrderResult TryOrderProduction(string factoryKeyId, bool instant = false, bool forFree = true)
        {
            if (string.IsNullOrWhiteSpace(factoryKeyId))
            {
                return new ProductionOrderResult(false, string.Empty, 0, "Factory key ID cannot be empty.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new ProductionOrderResult(false, factoryKeyId, 0, "ExistingControllers not ready.");
                }

                EntityController targetFactory = null;
                var candidates = new List<EntityController>();
                try
                {
                    HashSet<EntityController> playerBuildings = existing.PlayerBuildingsKnownToAi();
                    if (playerBuildings != null) candidates.AddRange(playerBuildings);
                    HashSet<EntityController> playerUnits = existing.PlayerUnits();
                    if (playerUnits != null) candidates.AddRange(playerUnits);
                }
                catch
                {
                    // Degrade gracefully
                }

                foreach (EntityController entity in candidates)
                {
                    if (entity == null || entity.IsDestroyed || entity.DestroyHasBeenStarted)
                    {
                        continue;
                    }

                    string key = entity.EntityId ?? string.Empty;
                    if (string.Equals(key, factoryKeyId, StringComparison.OrdinalIgnoreCase))
                    {
                        targetFactory = entity;
                        break;
                    }
                }

                if (targetFactory == null)
                {
                    return new ProductionOrderResult(false, factoryKeyId, 0, "Factory not found or destroyed.");
                }

                if (!targetFactory.IsControlledByPlayer)
                {
                    return new ProductionOrderResult(false, factoryKeyId, 0, "Cannot order production on non-player entities.");
                }

                targetFactory.Produce(instant, forFree, false);
                int inProd = targetFactory.InProductionCount ?? 0;
                return new ProductionOrderResult(true, factoryKeyId, inProd, "Production ordered successfully.");
            }
            catch (Exception ex)
            {
                return new ProductionOrderResult(false, factoryKeyId, 0, "Error ordering production: " + ex.Message);
            }
        }
    }
}
