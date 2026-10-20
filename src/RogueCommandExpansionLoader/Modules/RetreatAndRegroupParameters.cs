using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct RetreatAndRegroupSettings
    {
        public int BaseIndex;
        public int HealthThresholdPercent;
        public int RegroupRadiusCells;
        public float CooldownSeconds;
    }

    /// <summary>
    /// Parses bounded, data-only settings for the retreat-and-regroup AI module.
    /// This parser is Unity-independent so package limits can be validated offline.
    /// </summary>
    internal static class RetreatAndRegroupParameterParser
    {
        internal const string ModuleId = "sample:retreat-and-regroup";

        private const int DefaultBaseIndex = 0;
        private const int DefaultHealthThresholdPercent = 30;
        private const int DefaultRegroupRadiusCells = 8;
        private const float DefaultCooldownSeconds = 20f;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out RetreatAndRegroupSettings settings,
            out string diagnostic)
        {
            settings = default(RetreatAndRegroupSettings);
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

                if (parameter.name != "baseIndex" && parameter.name != "healthThresholdPercent" &&
                    parameter.name != "regroupRadiusCells" && parameter.name != "cooldownSeconds")
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

            int healthThresholdPercent;
            if (!TryGetInteger(values, "healthThresholdPercent", DefaultHealthThresholdPercent, 1, 99,
                out healthThresholdPercent))
            {
                diagnostic = "healthThresholdPercent must be an integer between 1 and 99.";
                return false;
            }

            int regroupRadiusCells;
            if (!TryGetInteger(values, "regroupRadiusCells", DefaultRegroupRadiusCells, 1, 32,
                out regroupRadiusCells))
            {
                diagnostic = "regroupRadiusCells must be an integer between 1 and 32.";
                return false;
            }

            float cooldownSeconds;
            if (!TryGetFloat(values, "cooldownSeconds", DefaultCooldownSeconds, 5f, 300f,
                out cooldownSeconds))
            {
                diagnostic = "cooldownSeconds must be a finite number between 5 and 300.";
                return false;
            }

            settings = new RetreatAndRegroupSettings
            {
                BaseIndex = baseIndex,
                HealthThresholdPercent = healthThresholdPercent,
                RegroupRadiusCells = regroupRadiusCells,
                CooldownSeconds = cooldownSeconds
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
