using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct TacticalRegroupSettings
    {
        public int BaseIndex;
        public float CheckIntervalSeconds;
        public float HealthRetreatThreshold;
        public int RegroupMinSquadSize;
        public float RegroupDistanceToBase;
    }

    /// <summary>
    /// Parses bounded, data-only settings for the tactical retreat and regroup AI module.
    /// This parser is Unity-independent so package limits can be validated offline.
    /// </summary>
    internal static class TacticalRegroupParameterParser
    {
        internal const string ModuleId = "sample:tactical-regroup";

        private const int DefaultBaseIndex = -1;
        private const float DefaultCheckIntervalSeconds = 4.0f;
        private const float DefaultHealthRetreatThreshold = 0.30f;
        private const int DefaultRegroupMinSquadSize = 3;
        private const float DefaultRegroupDistanceToBase = 20.0f;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out TacticalRegroupSettings settings,
            out string diagnostic)
        {
            settings = default(TacticalRegroupSettings);
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
                    parameter.name != "healthRetreatThreshold" && parameter.name != "regroupMinSquadSize" &&
                    parameter.name != "regroupDistanceToBase")
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
            if (!TryGetInteger(values, "baseIndex", DefaultBaseIndex, -1, 31, out baseIndex))
            {
                diagnostic = "baseIndex must be an integer between -1 and 31.";
                return false;
            }

            float checkIntervalSeconds;
            if (!TryGetFloat(values, "checkIntervalSeconds", DefaultCheckIntervalSeconds, 1f, 30f,
                out checkIntervalSeconds))
            {
                diagnostic = "checkIntervalSeconds must be a finite number between 1 and 30.";
                return false;
            }

            float healthRetreatThreshold;
            if (!TryGetFloat(values, "healthRetreatThreshold", DefaultHealthRetreatThreshold, 0.10f, 0.60f,
                out healthRetreatThreshold))
            {
                diagnostic = "healthRetreatThreshold must be a finite number between 0.10 and 0.60.";
                return false;
            }

            int regroupMinSquadSize;
            if (!TryGetInteger(values, "regroupMinSquadSize", DefaultRegroupMinSquadSize, 2, 10,
                out regroupMinSquadSize))
            {
                diagnostic = "regroupMinSquadSize must be an integer between 2 and 10.";
                return false;
            }

            float regroupDistanceToBase;
            if (!TryGetFloat(values, "regroupDistanceToBase", DefaultRegroupDistanceToBase, 5f, 50f,
                out regroupDistanceToBase))
            {
                diagnostic = "regroupDistanceToBase must be a finite number between 5 and 50.";
                return false;
            }

            settings = new TacticalRegroupSettings
            {
                BaseIndex = baseIndex,
                CheckIntervalSeconds = checkIntervalSeconds,
                HealthRetreatThreshold = healthRetreatThreshold,
                RegroupMinSquadSize = regroupMinSquadSize,
                RegroupDistanceToBase = regroupDistanceToBase
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
