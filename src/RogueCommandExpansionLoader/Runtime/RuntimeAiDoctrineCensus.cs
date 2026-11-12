using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure diagnostic telemetry row for an AI base deck entry.
    /// Decoupled from Unity types to support offline testing and MCP inspection.
    /// </summary>
    public readonly struct AiDeckEntryDiagnosticRow
    {
        public AiDeckEntryDiagnosticRow(
            int baseIndex,
            string unitId,
            int countAtStart,
            int maxLiving,
            int currentLivingCount)
        {
            BaseIndex = baseIndex;
            UnitId = unitId ?? string.Empty;
            CountAtStart = countAtStart;
            MaxLiving = maxLiving;
            CurrentLivingCount = currentLivingCount;
        }

        public int BaseIndex { get; }
        public string UnitId { get; }
        public int CountAtStart { get; }
        public int MaxLiving { get; }
        public int CurrentLivingCount { get; }
    }

    /// <summary>
    /// Pure diagnostic snapshot of active AI tactical doctrine and live deck quotas.
    /// Omission counter placed strictly first to respect bounded trace caps.
    /// </summary>
    public sealed class AiDoctrineAndDeckCensus
    {
        public int OmittedDeckEntryCount { get; }
        public int TotalDeckEntries { get; }
        public string ActiveDoctrine { get; }
        public float AggressionMultiplier { get; }
        public int ProfileCount { get; }
        public IReadOnlyList<AiDeckEntryDiagnosticRow> DeckEntries { get; }

        public AiDoctrineAndDeckCensus(
            int omittedDeckEntryCount,
            int totalDeckEntries,
            string activeDoctrine,
            float aggressionMultiplier,
            int profileCount,
            IReadOnlyList<AiDeckEntryDiagnosticRow> deckEntries)
        {
            OmittedDeckEntryCount = omittedDeckEntryCount;
            TotalDeckEntries = totalDeckEntries;
            ActiveDoctrine = activeDoctrine ?? "Standard";
            AggressionMultiplier = aggressionMultiplier;
            ProfileCount = profileCount;
            DeckEntries = deckEntries ?? new List<AiDeckEntryDiagnosticRow>().AsReadOnly();
        }

        public static AiDoctrineAndDeckCensus Empty { get; } =
            new AiDoctrineAndDeckCensus(
                0, 0, "Standard", 1.0f, 0,
                new List<AiDeckEntryDiagnosticRow>().AsReadOnly());
    }

    /// <summary>
    /// Collector that inspects AI tactical doctrine and deck entries fail-closed.
    /// </summary>
    public static class RuntimeAiDoctrineCensus
    {
        public const int MaxTrackedDeckEntries = 32;

        public static AiDoctrineAndDeckCensus Current()
        {
            try
            {
                string doctrine = RuntimeGameplayCommandDirector.GetActiveAiDoctrine();
                float multiplier = RuntimeGameplayCommandDirector.GetAiAggressionMultiplier();

                int omittedCount;
                int totalEntries;
                int profileCount;
                IReadOnlyList<AiDeckEntryDiagnosticRow> entries =
                    RuntimeGameplayCommandDirector.GetAiDeckEntries(MaxTrackedDeckEntries, out omittedCount, out totalEntries, out profileCount);

                return new AiDoctrineAndDeckCensus(
                    omittedCount, totalEntries, doctrine, multiplier, profileCount, entries);
            }
            catch
            {
                return AiDoctrineAndDeckCensus.Empty;
            }
        }
    }
}
