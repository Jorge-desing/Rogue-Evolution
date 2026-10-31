using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure diagnostic row representing a single harvester on the battlefield.
    /// Decoupled from Unity types.
    /// </summary>
    public sealed class HarvesterDiagnosticRow
    {
        public string EntityKeyId { get; }
        public bool IsPlayerControlled { get; }
        public float CurrentLoad { get; }
        public float MaxLoad { get; }
        public float LoadFraction { get; }
        public string CurrentState { get; }
        public float PositionX { get; }
        public float PositionZ { get; }

        public HarvesterDiagnosticRow(
            string entityKeyId,
            bool isPlayerControlled,
            float currentLoad,
            float maxLoad,
            float loadFraction,
            string currentState,
            float positionX,
            float positionZ)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            IsPlayerControlled = isPlayerControlled;
            CurrentLoad = currentLoad;
            MaxLoad = maxLoad;
            LoadFraction = loadFraction;
            CurrentState = currentState ?? string.Empty;
            PositionX = positionX;
            PositionZ = positionZ;
        }
    }

    /// <summary>
    /// Pure, agent-readable telemetry of active harvesters, crystal payloads, and mining state.
    /// Decoupled from Unity types.
    /// </summary>
    public sealed class HarvesterCensus
    {
        public int OmittedHarvesterCount { get; }
        public int TotalHarvesterCount { get; }
        public int PlayerHarvesterCount { get; }
        public int AiHarvesterCount { get; }
        public int IdleHarvesterCount { get; }
        public float TotalCrystalsCarried { get; }
        public IReadOnlyList<HarvesterDiagnosticRow> HarvesterRows { get; }

        public HarvesterCensus(
            int omittedHarvesterCount,
            int totalHarvesterCount,
            int playerHarvesterCount,
            int aiHarvesterCount,
            int idleHarvesterCount,
            float totalCrystalsCarried,
            IReadOnlyList<HarvesterDiagnosticRow> harvesterRows)
        {
            OmittedHarvesterCount = omittedHarvesterCount;
            TotalHarvesterCount = totalHarvesterCount;
            PlayerHarvesterCount = playerHarvesterCount;
            AiHarvesterCount = aiHarvesterCount;
            IdleHarvesterCount = idleHarvesterCount;
            TotalCrystalsCarried = totalCrystalsCarried;
            HarvesterRows = harvesterRows ?? Array.Empty<HarvesterDiagnosticRow>();
        }

        public static HarvesterCensus Empty { get; } =
            new HarvesterCensus(0, 0, 0, 0, 0, 0f, Array.Empty<HarvesterDiagnosticRow>());
    }

    /// <summary>
    /// Diagnostic collector that inspects active harvesters and crystal payloads across the battlefield.
    /// </summary>
    public static class RuntimeHarvesterCensus
    {
        private const int MaxHarvestersToReport = 32;
        private static readonly BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static HarvesterCensus Current()
        {
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return HarvesterCensus.Empty;
                }

                HashSet<EntityController> harvesters = null;
                try
                {
                    harvesters = existing.Harvesters;
                }
                catch { }

                if (harvesters == null || harvesters.Count == 0)
                {
                    return HarvesterCensus.Empty;
                }

                int totalCount = 0;
                int playerCount = 0;
                int aiCount = 0;
                int idleCount = 0;
                float totalCarried = 0f;
                var rows = new List<HarvesterDiagnosticRow>();

                foreach (EntityController ec in harvesters)
                {
                    if (ec == null || ec.IsDestroyed || ec.DestroyHasBeenStarted)
                    {
                        continue;
                    }

                    totalCount++;
                    bool isPlayer = ec.IsControlledByPlayer || string.Equals(ec.tag, "Player", StringComparison.OrdinalIgnoreCase);
                    if (isPlayer) playerCount++;
                    else aiCount++;

                    float currentLoad = 0f;
                    float maxLoad = 100f;
                    float fraction = 0f;
                    string stateStr = "Unknown";

                    try
                    {
                        Harvest harvest = ec.GetComponent<Harvest>();
                        if (harvest != null)
                        {
                            Type hType = harvest.GetType();

                            FieldInfo loadF = hType.GetField("_currentLoad", Flags);
                            if (loadF != null) currentLoad = Convert.ToSingle(loadF.GetValue(harvest));

                            FieldInfo maxF = hType.GetField("MaxValue", Flags);
                            if (maxF != null) maxLoad = Convert.ToSingle(maxF.GetValue(harvest));

                            FieldInfo fracF = hType.GetField("_loadFraction", Flags);
                            if (fracF != null) fraction = Convert.ToSingle(fracF.GetValue(harvest));
                            else if (maxLoad > 0.01f) fraction = currentLoad / maxLoad;

                            FieldInfo stateF = hType.GetField("_currentState", Flags);
                            if (stateF != null)
                            {
                                object sVal = stateF.GetValue(harvest);
                                stateStr = sVal != null ? sVal.ToString() : "Active";
                            }
                        }
                    }
                    catch { }

                    totalCarried += currentLoad;
                    if (currentLoad < 1f && (stateStr.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                             stateStr.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        idleCount++;
                    }

                    if (rows.Count < MaxHarvestersToReport)
                    {
                        string keyId = ec.EntityId ?? ec.name ?? "Harvester_" + totalCount;
                        Vector3 pos = ec.transform != null ? ec.transform.position : Vector3.zero;

                        rows.Add(new HarvesterDiagnosticRow(
                            keyId,
                            isPlayer,
                            currentLoad,
                            maxLoad,
                            fraction,
                            stateStr,
                            pos.x,
                            pos.z));
                    }
                }

                int omittedCount = Math.Max(0, totalCount - MaxHarvestersToReport);

                return new HarvesterCensus(
                    omittedCount,
                    totalCount,
                    playerCount,
                    aiCount,
                    idleCount,
                    totalCarried,
                    rows);
            }
            catch
            {
                return HarvesterCensus.Empty;
            }
        }
    }
}
