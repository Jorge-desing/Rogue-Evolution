using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct ReactiveBaseDefenseSettings
    {
        public int BaseIndex;
        public float CooldownSeconds;
        public float DefenseRadiusCells;
        public int MaximumResponders;
    }

    /// <summary>
    /// Parses the bounded, data-only settings for the reactive base-defense module.
    /// This parser is Unity-independent so package limits can be checked offline.
    /// </summary>
    internal static class ReactiveBaseDefenseParameterParser
    {
        internal const string ModuleId = "sample:reactive-base-defense";

        private const int DefaultBaseIndex = 0;
        private const float DefaultCooldownSeconds = 1f;
        private const float DefaultDefenseRadiusCells = 12f;
        private const int DefaultMaximumResponders = 2;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out ReactiveBaseDefenseSettings settings,
            out string diagnostic)
        {
            settings = default(ReactiveBaseDefenseSettings);
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
                    parameter.name != "defenseRadiusCells" && parameter.name != "maxResponders")
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
            if (!TryGetFloat(values, "cooldownSeconds", DefaultCooldownSeconds, 1f, 60f,
                out cooldownSeconds))
            {
                diagnostic = "cooldownSeconds must be a finite number between 1 and 60.";
                return false;
            }

            float defenseRadiusCells;
            if (!TryGetFloat(values, "defenseRadiusCells", DefaultDefenseRadiusCells, 1f, 64f,
                out defenseRadiusCells))
            {
                diagnostic = "defenseRadiusCells must be a finite number between 1 and 64.";
                return false;
            }

            int maximumResponders;
            if (!TryGetInteger(values, "maxResponders", DefaultMaximumResponders, 1, 4,
                out maximumResponders))
            {
                diagnostic = "maxResponders must be an integer between 1 and 4.";
                return false;
            }

            settings = new ReactiveBaseDefenseSettings
            {
                BaseIndex = baseIndex,
                CooldownSeconds = cooldownSeconds,
                DefenseRadiusCells = defenseRadiusCells,
                MaximumResponders = maximumResponders
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
