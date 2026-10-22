using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct HarvesterEscortSettings
    {
        public int BaseIndex;
        public int EscortRadiusCells;
        public float ScanIntervalSeconds;
        public int MaxEscorts;
        public bool EngageAttackers;
    }

    internal static class HarvesterEscortParameterParser
    {
        internal const string ModuleId = "sample:harvester-escort";

        private const int DefaultBaseIndex = 0;
        private const int DefaultEscortRadiusCells = 8;
        private const float DefaultScanIntervalSeconds = 5f;
        private const int DefaultMaxEscorts = 3;
        private const bool DefaultEngageAttackers = true;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out HarvesterEscortSettings settings,
            out string diagnostic)
        {
            settings = default(HarvesterEscortSettings);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            parameters = parameters ?? new ContentModuleParameter[0];

            for (int i = 0; i < parameters.Length; i++)
            {
                ContentModuleParameter parameter = parameters[i];
                if (parameter == null || parameter.name == null || parameter.value == null)
                {
                    diagnostic = "Module parameters cannot be null.";
                    return false;
                }

                if (parameter.name != "baseIndex" &&
                    parameter.name != "escortRadiusCells" &&
                    parameter.name != "scanIntervalSeconds" &&
                    parameter.name != "maxEscorts" &&
                    parameter.name != "engageAttackers")
                {
                    diagnostic = "Unknown parameter '" + parameter.name + "' for module " + ModuleId + ".";
                    return false;
                }

                if (values.ContainsKey(parameter.name))
                {
                    diagnostic = "Parameter '" + parameter.name + "' may only be supplied once.";
                    return false;
                }

                values.Add(parameter.name, parameter.value);
            }

            int baseIndex;
            if (!TryGetInteger(values, "baseIndex", DefaultBaseIndex, 0, 31, out baseIndex))
            {
                diagnostic = "baseIndex must be an integer between 0 and 31.";
                return false;
            }

            int escortRadiusCells;
            if (!TryGetInteger(values, "escortRadiusCells", DefaultEscortRadiusCells, 1, 64, out escortRadiusCells))
            {
                diagnostic = "escortRadiusCells must be an integer between 1 and 64.";
                return false;
            }

            float scanIntervalSeconds;
            if (!TryGetFloat(values, "scanIntervalSeconds", DefaultScanIntervalSeconds, 1f, 60f, out scanIntervalSeconds))
            {
                diagnostic = "scanIntervalSeconds must be a finite number between 1 and 60.";
                return false;
            }

            int maxEscorts;
            if (!TryGetInteger(values, "maxEscorts", DefaultMaxEscorts, 1, 16, out maxEscorts))
            {
                diagnostic = "maxEscorts must be an integer between 1 and 16.";
                return false;
            }

            bool engageAttackers;
            if (!TryGetBoolean(values, "engageAttackers", DefaultEngageAttackers, out engageAttackers))
            {
                diagnostic = "engageAttackers must be true or false.";
                return false;
            }

            settings = new HarvesterEscortSettings
            {
                BaseIndex = baseIndex,
                EscortRadiusCells = escortRadiusCells,
                ScanIntervalSeconds = scanIntervalSeconds,
                MaxEscorts = maxEscorts,
                EngageAttackers = engageAttackers
            };
            diagnostic = null;
            return true;
        }

        private static bool TryGetInteger(
            Dictionary<string, string> values,
            string name,
            int defaultValue,
            int minimum,
            int maximum,
            out int result)
        {
            string value;
            if (!values.TryGetValue(name, out value))
            {
                result = defaultValue;
                return true;
            }

            return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) &&
                result >= minimum && result <= maximum;
        }

        private static bool TryGetFloat(
            Dictionary<string, string> values,
            string name,
            float defaultValue,
            float minimum,
            float maximum,
            out float result)
        {
            string value;
            if (!values.TryGetValue(name, out value))
            {
                result = defaultValue;
                return true;
            }

            return Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
                !Single.IsNaN(result) && !Single.IsInfinity(result) &&
                result >= minimum && result <= maximum;
        }

        private static bool TryGetBoolean(
            Dictionary<string, string> values,
            string name,
            bool defaultValue,
            out bool result)
        {
            string value;
            if (!values.TryGetValue(name, out value))
            {
                result = defaultValue;
                return true;
            }

            if (String.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (String.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            result = default(bool);
            return false;
        }
    }
}