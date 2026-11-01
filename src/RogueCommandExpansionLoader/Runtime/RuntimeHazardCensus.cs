using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure diagnostic telemetry row for an active battlefield hazard zone.
    /// Decoupled from Unity types to support offline testing and MCP inspection.
    /// </summary>
    public readonly struct HazardZoneDiagnosticRow
    {
        public HazardZoneDiagnosticRow(
            string zoneId,
            string hazardType,
            float centerX,
            float centerZ,
            float radius,
            float tickDamage,
            float tickIntervalSeconds,
            float remainingDurationSeconds,
            int affectedEntitiesLastTick)
        {
            ZoneId = zoneId ?? string.Empty;
            HazardType = hazardType ?? string.Empty;
            CenterX = centerX;
            CenterZ = centerZ;
            Radius = radius;
            TickDamage = tickDamage;
            TickIntervalSeconds = tickIntervalSeconds;
            RemainingDurationSeconds = remainingDurationSeconds;
            AffectedEntitiesLastTick = affectedEntitiesLastTick;
        }

        public string ZoneId { get; }
        public string HazardType { get; }
        public float CenterX { get; }
        public float CenterZ { get; }
        public float Radius { get; }
        public float TickDamage { get; }
        public float TickIntervalSeconds { get; }
        public float RemainingDurationSeconds { get; }
        public int AffectedEntitiesLastTick { get; }
    }

    /// <summary>
    /// Pure diagnostic snapshot of active battlefield hazard zones.
    /// Omission counter placed strictly first to respect bounded trace caps.
    /// </summary>
    public sealed class HazardZoneCensus
    {
        public int OmittedZoneCount { get; }
        public int TotalActiveZonesCount { get; }
        public IReadOnlyList<HazardZoneDiagnosticRow> ActiveZones { get; }

        public HazardZoneCensus(
            int omittedZoneCount,
            int totalActiveZonesCount,
            IReadOnlyList<HazardZoneDiagnosticRow> activeZones)
        {
            OmittedZoneCount = omittedZoneCount;
            TotalActiveZonesCount = totalActiveZonesCount;
            ActiveZones = activeZones ?? new List<HazardZoneDiagnosticRow>().AsReadOnly();
        }

        public static HazardZoneCensus Empty { get; } =
            new HazardZoneCensus(0, 0, new List<HazardZoneDiagnosticRow>().AsReadOnly());
    }

    /// <summary>
    /// Inspects active battlefield environmental hazard zones managed by <see cref="RuntimeHazardManager"/>.
    /// </summary>
    public static class RuntimeHazardCensus
    {
        public const int MaxTrackedZones = 16;

        public static HazardZoneCensus Current()
        {
            try
            {
                int totalZones = RuntimeHazardManager.ActiveZoneCount;
                int omittedCount;
                IReadOnlyList<HazardZoneDiagnosticRow> rows =
                    RuntimeHazardManager.GetActiveHazardRows(MaxTrackedZones, out omittedCount);

                return new HazardZoneCensus(omittedCount, totalZones, rows);
            }
            catch
            {
                return HazardZoneCensus.Empty;
            }
        }
    }
}
