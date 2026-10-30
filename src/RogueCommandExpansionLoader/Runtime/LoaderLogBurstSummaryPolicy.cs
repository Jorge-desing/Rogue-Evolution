using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    public sealed class LoaderLogBurstSummary
    {
        public LoaderLogBurstSummary(string message, int occurrenceCount)
        {
            Message = message ?? string.Empty;
            RepeatCount = occurrenceCount > 1 ? occurrenceCount - 1 : 0;
        }

        public string Message { get; private set; }
        public int RepeatCount { get; private set; }
        public string DisplayText => RepeatCount > 0
            ? Message + " (" + RepeatCount + ")"
            : Message;
    }

    public static class LoaderLogBurstSummaryPolicy
    {
        private const int TimestampPrefixLength = 9;

        public static LoaderLogBurstSummary Create(IReadOnlyList<string> recentLogs)
        {
            if (recentLogs == null || recentLogs.Count == 0)
            {
                return null;
            }

            int lastIndex = recentLogs.Count - 1;
            string message = StripTimestamp(recentLogs[lastIndex]);
            int count = 1;
            for (int i = lastIndex - 1; i >= 0; i--)
            {
                if (!String.Equals(message, StripTimestamp(recentLogs[i]), StringComparison.Ordinal))
                {
                    break;
                }

                count++;
            }

            return new LoaderLogBurstSummary(message, count);
        }

        private static string StripTimestamp(string entry)
        {
            if (entry == null || entry.Length < TimestampPrefixLength || entry[8] != ' ' ||
                !IsDigit(entry[0]) || !IsDigit(entry[1]) || entry[2] != ':' ||
                !IsDigit(entry[3]) || !IsDigit(entry[4]) || entry[5] != ':' ||
                !IsDigit(entry[6]) || !IsDigit(entry[7]))
            {
                return entry ?? string.Empty;
            }

            return entry.Substring(TimestampPrefixLength);
        }

        private static bool IsDigit(char value)
        {
            return value >= '0' && value <= '9';
        }
    }
}
