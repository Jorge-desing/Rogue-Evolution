using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct FlankingStrikeSettings
    {
        public int BaseIndex;
        public float CooldownSeconds;
        public int SquadSize;
        public float FlankAngleDegrees;
        public float FlankDistance;
        public float MinGameTimeSeconds;
        public string TargetPreference;
    }

    /// <summary>
    /// Parses bounded, data-only settings for the tactical flanking strike AI module.
    /// This parser is Unity-independent so package limits can be validated offline.
    /// </summary>
    internal static class FlankingStrikeParameterParser
    {
        internal const string ModuleId = "sample:flanking-strike";

        public const string TargetPreferenceRefinery = "Refinery";
        public const string TargetPreferenceBuilding = "Building";
        public const string TargetPreferenceHarvester = "Harvester";

        private const int DefaultBaseIndex = 0;
        private const float DefaultCooldownSeconds = 35f;
        private const int DefaultSquadSize = 3;
        private const float DefaultFlankAngleDegrees = 60f;
        private const float DefaultFlankDistance = 35f;
        private const float DefaultMinGameTimeSeconds = 30f;
        private const string DefaultTargetPreference = TargetPreferenceRefinery;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out FlankingStrikeSettings settings,
            out string diagnostic)
        {
            settings = default(FlankingStrikeSettings);
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

                if (parameter.name != "baseIndex" && parameter.name != "cooldownSeconds" &&
                    parameter.name != "squadSize" && parameter.name != "flankAngleDegrees" &&
                    parameter.name != "flankDistance" && parameter.name != "minGameTimeSeconds" &&
                    parameter.name != "targetPreference")
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

            float cooldownSeconds;
            if (!TryGetFloat(values, "cooldownSeconds", DefaultCooldownSeconds, 5f, 300f,
                out cooldownSeconds))
            {
                diagnostic = "cooldownSeconds must be a finite number between 5 and 300.";
                return false;
            }

            int squadSize;
            if (!TryGetInteger(values, "squadSize", DefaultSquadSize, 1, 12, out squadSize))
            {
                diagnostic = "squadSize must be an integer between 1 and 12.";
                return false;
            }

            float flankAngleDegrees;
            if (!TryGetFloat(values, "flankAngleDegrees", DefaultFlankAngleDegrees, 15f, 90f,
                out flankAngleDegrees))
            {
                diagnostic = "flankAngleDegrees must be a finite number between 15 and 90.";
                return false;
            }

            float flankDistance;
            if (!TryGetFloat(values, "flankDistance", DefaultFlankDistance, 10f, 120f,
                out flankDistance))
            {
                diagnostic = "flankDistance must be a finite number between 10 and 120.";
                return false;
            }

            float minGameTimeSeconds;
            if (!TryGetFloat(values, "minGameTimeSeconds", DefaultMinGameTimeSeconds, 0f, 600f,
                out minGameTimeSeconds))
            {
                diagnostic = "minGameTimeSeconds must be a finite number between 0 and 600.";
                return false;
            }

            string targetPreference;
            if (!values.TryGetValue("targetPreference", out targetPreference))
            {
                targetPreference = DefaultTargetPreference;
            }
            else if (!String.Equals(targetPreference, TargetPreferenceRefinery, StringComparison.Ordinal) &&
                     !String.Equals(targetPreference, TargetPreferenceBuilding, StringComparison.Ordinal) &&
                     !String.Equals(targetPreference, TargetPreferenceHarvester, StringComparison.Ordinal))
            {
                diagnostic = "targetPreference must be 'Refinery', 'Building', or 'Harvester'.";
                return false;
            }

            settings = new FlankingStrikeSettings
            {
                BaseIndex = baseIndex,
                CooldownSeconds = cooldownSeconds,
                SquadSize = squadSize,
                FlankAngleDegrees = flankAngleDegrees,
                FlankDistance = flankDistance,
                MinGameTimeSeconds = minGameTimeSeconds,
                TargetPreference = targetPreference
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
    }
}
