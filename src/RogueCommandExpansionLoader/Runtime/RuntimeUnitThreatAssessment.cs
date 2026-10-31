using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// One live unit's threat assessment, combining its current health ratio with
    /// its base DPS. Derived from RuntimePlayerRosterCensus and RuntimeEntityBalanceLookup.
    /// </summary>
    public readonly struct UnitThreatRow
    {
        public UnitThreatRow(string entityKeyId, float healthRatio, float baseDps, float threatScore)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            HealthRatio = healthRatio;
            BaseDps = baseDps;
            ThreatScore = threatScore;
        }

        public string EntityKeyId { get; }
        public float HealthRatio { get; }
        public float BaseDps { get; }
        public float ThreatScore { get; }
    }

    public sealed class UnitThreatAssessment
    {
        public IReadOnlyList<UnitThreatRow> Rows { get; }

        public UnitThreatAssessment(IReadOnlyList<UnitThreatRow> rows)
        {
            Rows = rows ?? new List<UnitThreatRow>().AsReadOnly();
        }

        public static UnitThreatAssessment Empty { get; } =
            new UnitThreatAssessment(new List<UnitThreatRow>().AsReadOnly());
    }

    public static class RuntimeUnitThreatAssessment
    {
        public static UnitThreatAssessment Current()
        {
            try
            {
                PlayerRosterCensus census = RuntimePlayerRosterCensus.Current();
                if (census == null || census.PlayerUnitRows == null)
                {
                    return UnitThreatAssessment.Empty;
                }

                var rows = new List<UnitThreatRow>();
                foreach (PlayerRosterRow row in census.PlayerUnitRows)
                {
                    float threat = row.Dps * Math.Max(0f, Math.Min(1f, row.HealthRatio));
                    rows.Add(new UnitThreatRow(row.EntityKeyId, row.HealthRatio, row.Dps, threat));
                }
                return new UnitThreatAssessment(rows.AsReadOnly());
            }
            catch
            {
                return UnitThreatAssessment.Empty;
            }
        }
    }
}