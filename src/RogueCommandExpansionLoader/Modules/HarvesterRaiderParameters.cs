using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct HarvesterRaiderSettings
    {
        public int BaseIndex;
        public float ScanIntervalSeconds;
        public float CooldownSeconds;
        public int RaidSquadSize;
        public float MinGameTimeSeconds;
    }

    /// <summary>
    /// Parses bounded, data-only settings for the harvester raider AI module.
    /// This parser is Unity-independent so package limits can be validated offline.
    /// </summary>
    internal static class HarvesterRaiderParameterParser
    {
        internal const string ModuleId = "sample:harvester-raider";

        private const int DefaultBaseIndex = 0;
        private const float DefaultScanIntervalSeconds = 10f;
        private const float DefaultCooldownSeconds = 25f;
        private const int DefaultRaidSquadSize = 3;
        private const float DefaultMinGameTimeSeconds = 15f;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out HarvesterRaiderSettings settings,
            out string diagnostic)
        {
            settings = default(HarvesterRaiderSettings);
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

                if (parameter.name != "baseIndex" && parameter.name != "scanIntervalSeconds" &&
                    parameter.name != "cooldownSeconds" && parameter.name != "raidSquadSize" &&
                    parameter.name != "minGameTimeSeconds")
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

            float scanIntervalSeconds;
            if (!TryGetFloat(values, "scanIntervalSeconds", DefaultScanIntervalSeconds, 2f, 120f,
                out scanIntervalSeconds))
            {
                diagnostic = "scanIntervalSeconds must be a finite number between 2 and 120.";
                return false;
            }

            float cooldownSeconds;
            if (!TryGetFloat(values, "cooldownSeconds", DefaultCooldownSeconds, 5f, 300f,
                out cooldownSeconds))
            {
                diagnostic = "cooldownSeconds must be a finite number between 5 and 300.";
                return false;
            }

            int raidSquadSize;
            if (!TryGetInteger(values, "raidSquadSize", DefaultRaidSquadSize, 1, 8,
                out raidSquadSize))
            {
                diagnostic = "raidSquadSize must be an integer between 1 and 8.";
                return false;
            }

            float minGameTimeSeconds;
            if (!TryGetFloat(values, "minGameTimeSeconds", DefaultMinGameTimeSeconds, 0f, 600f,
                out minGameTimeSeconds))
            {
                diagnostic = "minGameTimeSeconds must be a finite number between 0 and 600.";
                return false;
            }

            settings = new HarvesterRaiderSettings
            {
                BaseIndex = baseIndex,
                ScanIntervalSeconds = scanIntervalSeconds,
                CooldownSeconds = cooldownSeconds,
                RaidSquadSize = raidSquadSize,
                MinGameTimeSeconds = minGameTimeSeconds
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
                !Single.IsNaN(result) && !Single.IsInfinity(result) && result >= minimum && result <= maximum;
        }
    }
}
