using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Modules;

namespace RogueCommandExpansionLoader.Runtime.Ai
{
    /// <summary>
    /// Pure diagnostic telemetry row for a single active <see cref="CompositeAiRuntimeBehaviour"/>
    /// instance participating in the HUB-D architecture.
    /// </summary>
    public readonly struct AiArchitectureInstanceDiagnosticRow
    {
        public string ProfileId { get; }
        public int CoordinatedModuleCount { get; }
        public string ActiveDoctrine { get; }
        public int TrackedAiCombatUnits { get; }
        public int TrackedHostileCombatUnits { get; }
        public int TrackedHazardZones { get; }
        public bool UnderHarvesterOrBasePressure { get; }
        public int ApprovedOrdersLastTick { get; }
        public int ConflictsResolvedLastTick { get; }
        public int LockoutBlockedLastTick { get; }
        public int HysteresisBlockedLastTick { get; }
        public int BudgetOmittedLastTick { get; }

        public AiArchitectureInstanceDiagnosticRow(
            string profileId,
            int coordinatedModuleCount,
            string activeDoctrine,
            int trackedAiCombatUnits,
            int trackedHostileCombatUnits,
            bool underHarvesterOrBasePressure,
            int approvedOrdersLastTick,
            int conflictsResolvedLastTick,
            int lockoutBlockedLastTick,
            int hysteresisBlockedLastTick,
            int budgetOmittedLastTick,
            int trackedHazardZones = 0)
        {
            ProfileId = profileId ?? string.Empty;
            CoordinatedModuleCount = Math.Max(0, coordinatedModuleCount);
            ActiveDoctrine = activeDoctrine ?? "Standard";
            TrackedAiCombatUnits = Math.Max(0, trackedAiCombatUnits);
            TrackedHostileCombatUnits = Math.Max(0, trackedHostileCombatUnits);
            TrackedHazardZones = Math.Max(0, trackedHazardZones);
            UnderHarvesterOrBasePressure = underHarvesterOrBasePressure;
            ApprovedOrdersLastTick = Math.Max(0, approvedOrdersLastTick);
            ConflictsResolvedLastTick = Math.Max(0, conflictsResolvedLastTick);
            LockoutBlockedLastTick = Math.Max(0, lockoutBlockedLastTick);
            HysteresisBlockedLastTick = Math.Max(0, hysteresisBlockedLastTick);
            BudgetOmittedLastTick = Math.Max(0, budgetOmittedLastTick);
        }
    }

    /// <summary>
    /// Pure diagnostic snapshot of the HUB-D AI architecture across all active AI instances.
    /// Omission counter (<see cref="OmittedInstanceCount"/>) is placed strictly first to respect
    /// bounded telemetry trace rules.
    /// </summary>
    public sealed class AiArchitectureCensus
    {
        public int OmittedInstanceCount { get; }
        public int ActiveCompositeCount { get; }
        public int TotalCoordinatedModuleCount { get; }
        public int TotalTrackedHazardZones { get; }
        public int TotalApprovedOrdersLastTick { get; }
        public int TotalConflictsResolvedLastTick { get; }
        public int TotalLockoutBlockedLastTick { get; }
        public int TotalHysteresisBlockedLastTick { get; }
        public int TotalBudgetOmittedLastTick { get; }
        public IReadOnlyList<AiArchitectureInstanceDiagnosticRow> Instances { get; }

        public AiArchitectureCensus(
            int omittedInstanceCount,
            int activeCompositeCount,
            int totalCoordinatedModuleCount,
            int totalApprovedOrdersLastTick,
            int totalConflictsResolvedLastTick,
            int totalLockoutBlockedLastTick,
            int totalHysteresisBlockedLastTick,
            int totalBudgetOmittedLastTick,
            IReadOnlyList<AiArchitectureInstanceDiagnosticRow> instances,
            int totalTrackedHazardZones = 0)
        {
            OmittedInstanceCount = Math.Max(0, omittedInstanceCount);
            ActiveCompositeCount = Math.Max(0, activeCompositeCount);
            TotalCoordinatedModuleCount = Math.Max(0, totalCoordinatedModuleCount);
            TotalTrackedHazardZones = Math.Max(0, totalTrackedHazardZones);
            TotalApprovedOrdersLastTick = Math.Max(0, totalApprovedOrdersLastTick);
            TotalConflictsResolvedLastTick = Math.Max(0, totalConflictsResolvedLastTick);
            TotalLockoutBlockedLastTick = Math.Max(0, totalLockoutBlockedLastTick);
            TotalHysteresisBlockedLastTick = Math.Max(0, totalHysteresisBlockedLastTick);
            TotalBudgetOmittedLastTick = Math.Max(0, totalBudgetOmittedLastTick);
            Instances = instances ?? Array.Empty<AiArchitectureInstanceDiagnosticRow>();
        }

        public static AiArchitectureCensus Empty { get; } = new AiArchitectureCensus(
            omittedInstanceCount: 0,
            activeCompositeCount: 0,
            totalCoordinatedModuleCount: 0,
            totalApprovedOrdersLastTick: 0,
            totalConflictsResolvedLastTick: 0,
            totalLockoutBlockedLastTick: 0,
            totalHysteresisBlockedLastTick: 0,
            totalBudgetOmittedLastTick: 0,
            instances: Array.Empty<AiArchitectureInstanceDiagnosticRow>(),
            totalTrackedHazardZones: 0);
    }

    /// <summary>
    /// Collects live HUB-D AI architecture telemetry across active <see cref="CompositeAiRuntimeBehaviour"/> instances.
    /// </summary>
    public static class RuntimeAiArchitectureCensus
    {
        public const int DefaultMaxTrackedInstances = 16;

        public static AiArchitectureCensus Current(int maxTrackedInstances = DefaultMaxTrackedInstances)
        {
            return CompositeAiRuntimeBehaviour.BuildArchitectureCensus(maxTrackedInstances);
        }
    }
}
