using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure diagnostic telemetry row for an active captain command aura.
    /// Decoupled from Unity types to support offline testing and MCP inspection.
    /// </summary>
    public readonly struct CommandAuraDiagnosticRow
    {
        public CommandAuraDiagnosticRow(
            string captainEntityKeyId,
            string captainEntityId,
            bool isControlledByPlayer,
            int linkedFollowerCount,
            float commandRadius,
            string activeAuraType,
            float remainingDurationSeconds)
        {
            CaptainEntityKeyId = captainEntityKeyId ?? string.Empty;
            CaptainEntityId = captainEntityId ?? string.Empty;
            IsControlledByPlayer = isControlledByPlayer;
            LinkedFollowerCount = linkedFollowerCount;
            CommandRadius = commandRadius;
            ActiveAuraType = activeAuraType ?? string.Empty;
            RemainingDurationSeconds = remainingDurationSeconds;
        }

        public string CaptainEntityKeyId { get; }
        public string CaptainEntityId { get; }
        public bool IsControlledByPlayer { get; }
        public int LinkedFollowerCount { get; }
        public float CommandRadius { get; }
        public string ActiveAuraType { get; }
        public float RemainingDurationSeconds { get; }
    }

    /// <summary>
    /// Pure diagnostic snapshot of active captain command auras and squad link chains.
    /// Omission counter placed strictly first to respect bounded trace caps.
    /// </summary>
    public sealed class CommandAuraCensus
    {
        public int OmittedAuraCount { get; }
        public int TotalActiveAurasCount { get; }
        public int TotalLinkedFollowersCount { get; }
        public IReadOnlyList<CommandAuraDiagnosticRow> Auras { get; }

        public CommandAuraCensus(
            int omittedAuraCount,
            int totalActiveAurasCount,
            int totalLinkedFollowersCount,
            IReadOnlyList<CommandAuraDiagnosticRow> auras)
        {
            OmittedAuraCount = omittedAuraCount;
            TotalActiveAurasCount = totalActiveAurasCount;
            TotalLinkedFollowersCount = totalLinkedFollowersCount;
            Auras = auras ?? new List<CommandAuraDiagnosticRow>().AsReadOnly();
        }

        public static CommandAuraCensus Empty { get; } =
            new CommandAuraCensus(0, 0, 0, new List<CommandAuraDiagnosticRow>().AsReadOnly());
    }

    /// <summary>
    /// Queries active command auras and linked follower chains across living units.
    /// </summary>
    public static class RuntimeCommandAuraCensus
    {
        public const int MaxTrackedAuras = 16;

        public static CommandAuraCensus Current()
        {
            try
            {
                int omittedCount;
                IReadOnlyList<CommandAuraDiagnosticRow> auras =
                    RuntimeUnitMechanicsDirector.GetActiveCommandAuras(MaxTrackedAuras, out omittedCount);

                int totalFollowers = 0;
                for (int i = 0; i < auras.Count; i++)
                {
                    totalFollowers += auras[i].LinkedFollowerCount;
                }

                int totalAuras = auras.Count + omittedCount;
                return new CommandAuraCensus(omittedCount, totalAuras, totalFollowers, auras);
            }
            catch
            {
                return CommandAuraCensus.Empty;
            }
        }
    }
}
