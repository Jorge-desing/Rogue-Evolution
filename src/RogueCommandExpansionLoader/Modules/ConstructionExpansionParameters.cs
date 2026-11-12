using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct ConstructionExpansionSettings
    {
        public int BaseIndex;
        public float CheckIntervalSeconds;
        public string BuildingId;
        public int DesiredCount;
        public int QueuePriority;
        public int MinSurplusCrystals;
    }

    /// <summary>
    /// Parses bounded, data-only settings for the dynamic AI base construction expansion module.
    /// This parser is Unity-independent so package limits can be validated offline.
    /// </summary>
    internal static class ConstructionExpansionParameterParser
    {
        internal const string ModuleId = "sample:construction-expansion";

        private const int DefaultBaseIndex = 0;
        private const float DefaultCheckIntervalSeconds = 12.0f;
        private const string DefaultBuildingId = "PCXTurret";
        private const int DefaultDesiredCount = 2;
        private const int DefaultQueuePriority = 10;
        private const int DefaultMinSurplusCrystals = 150;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out ConstructionExpansionSettings settings,
            out string diagnostic)
        {
            settings = default(ConstructionExpansionSettings);
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

                if (parameter.name != "baseIndex" && parameter.name != "checkIntervalSeconds" &&
                    parameter.name != "buildingId" && parameter.name != "desiredCount" &&
                    parameter.name != "queuePriority" && parameter.name != "minSurplusCrystals")
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

            float checkIntervalSeconds;
            if (!TryGetFloat(values, "checkIntervalSeconds", DefaultCheckIntervalSeconds, 2f, 120f,
                out checkIntervalSeconds))
            {
                diagnostic = "checkIntervalSeconds must be a finite number between 2 and 120.";
                return false;
            }

            string buildingId;
            if (values.TryGetValue("buildingId", out buildingId))
            {
                if (String.IsNullOrWhiteSpace(buildingId))
                {
                    diagnostic = "buildingId must be a non-empty string when supplied.";
                    return false;
                }
            }
            else
            {
                buildingId = DefaultBuildingId;
            }

            int desiredCount;
            if (!TryGetInteger(values, "desiredCount", DefaultDesiredCount, 1, 10, out desiredCount))
            {
                diagnostic = "desiredCount must be an integer between 1 and 10.";
                return false;
            }

            int queuePriority;
            if (!TryGetInteger(values, "queuePriority", DefaultQueuePriority, 1, 100, out queuePriority))
            {
                diagnostic = "queuePriority must be an integer between 1 and 100.";
                return false;
            }

            int minSurplusCrystals;
            if (!TryGetInteger(values, "minSurplusCrystals", DefaultMinSurplusCrystals, 0, 5000, out minSurplusCrystals))
            {
                diagnostic = "minSurplusCrystals must be an integer between 0 and 5000.";
                return false;
            }

            settings = new ConstructionExpansionSettings
            {
                BaseIndex = baseIndex,
                CheckIntervalSeconds = checkIntervalSeconds,
                BuildingId = buildingId,
                DesiredCount = desiredCount,
                QueuePriority = queuePriority,
                MinSurplusCrystals = minSurplusCrystals
            };

            diagnostic = null;
            return true;
        }

        private static bool TryGetInteger(
            Dictionary<string, string> values,
            string key,
            int defaultValue,
            int minimum,
            int maximum,
            out int result)
        {
            string raw;
            if (!values.TryGetValue(key, out raw))
            {
                result = defaultValue;
                return true;
            }

            int parsed;
            if (!Int32.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ||
                parsed < minimum || parsed > maximum)
            {
                result = defaultValue;
                return false;
            }

            result = parsed;
            return true;
        }

        private static bool TryGetFloat(
            Dictionary<string, string> values,
            string key,
            float defaultValue,
            float minimum,
            float maximum,
            out float result)
        {
            string raw;
            if (!values.TryGetValue(key, out raw))
            {
                result = defaultValue;
                return true;
            }

            float parsed;
            if (!Single.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                Single.IsNaN(parsed) || Single.IsInfinity(parsed) || parsed < minimum || parsed > maximum)
            {
                result = defaultValue;
                return false;
            }

            result = parsed;
            return true;
        }
    }
}
