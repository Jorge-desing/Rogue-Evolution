using System;
using System.Globalization;

namespace RogueCommandExpansionLoader.Content
{
    internal static class StageParameterOverrideParser
    {
        internal const int MaximumStartCountRangeValue = 64;

        internal static bool TryParseStartCountRange(string value, out int minimum, out int maximum)
        {
            minimum = 0;
            maximum = 0;
            if (String.IsNullOrEmpty(value) ||
                !String.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            int separator = value.IndexOf(',');
            if (separator <= 0 || separator != value.LastIndexOf(',') || separator == value.Length - 1)
            {
                return false;
            }

            if (!Int32.TryParse(value.Substring(0, separator), NumberStyles.None,
                    CultureInfo.InvariantCulture, out minimum) ||
                !Int32.TryParse(value.Substring(separator + 1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out maximum))
            {
                return false;
            }

            return minimum >= 1 && maximum >= minimum && maximum <= MaximumStartCountRangeValue;
        }

        internal static bool IsStartCountRangeWithinWidth(int minimum, int maximum, int width)
        {
            return width >= 1 && width <= MaximumStartCountRangeValue &&
                minimum >= 1 && maximum >= minimum && maximum <= width;
        }
    }
}
