using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct FactoryReinforcementSettings
    {
        public int BaseIndex;
        public float CheckIntervalSeconds;
        public int MaxQueuePerFactory;
        public int MaxStandingArmy;
        public bool RequireEconomy;
        public float MinGameTimeSeconds;
    }

    /// <summary>
    /// Parses bounded, data-only settings for the dynamic factory reinforcement AI module.
    /// This parser is Unity-independent so package limits can be validated offline.
    /// </summary>
    internal static class FactoryReinforcementParameterParser
    {
        internal const string ModuleId = "sample:factory-reinforcement";

        private const int DefaultBaseIndex = -1;
        private const float DefaultCheckIntervalSeconds = 10f;
        private const int DefaultMaxQueuePerFactory = 1;
        private const int DefaultMaxStandingArmy = 25;
        private const bool DefaultRequireEconomy = true;
        private const float DefaultMinGameTimeSeconds = 15f;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out FactoryReinforcementSettings settings,
            out string diagnostic)
        {
            settings = default(FactoryReinforcementSettings);
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
                    parameter.name != "maxQueuePerFactory" && parameter.name != "maxStandingArmy" &&
                    parameter.name != "requireEconomy" && parameter.name != "minGameTimeSeconds")
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
            if (!TryGetFloat(values, "checkIntervalSeconds", DefaultCheckIntervalSeconds, 1f, 120f,
                out checkIntervalSeconds))
            {
                diagnostic = "checkIntervalSeconds must be a finite number between 1 and 120.";
                return false;
            }

            int maxQueuePerFactory;
            if (!TryGetInteger(values, "maxQueuePerFactory", DefaultMaxQueuePerFactory, 1, 5,
                out maxQueuePerFactory))
            {
                diagnostic = "maxQueuePerFactory must be an integer between 1 and 5.";
                return false;
            }

            int maxStandingArmy;
            if (!TryGetInteger(values, "maxStandingArmy", DefaultMaxStandingArmy, 1, 100,
                out maxStandingArmy))
            {
                diagnostic = "maxStandingArmy must be an integer between 1 and 100.";
                return false;
            }

            bool requireEconomy;
            if (!TryGetBoolean(values, "requireEconomy", DefaultRequireEconomy, out requireEconomy))
            {
                diagnostic = "requireEconomy must be true or false.";
                return false;
            }

            float minGameTimeSeconds;
            if (!TryGetFloat(values, "minGameTimeSeconds", DefaultMinGameTimeSeconds, 0f, 600f,
                out minGameTimeSeconds))
            {
                diagnostic = "minGameTimeSeconds must be a finite number between 0 and 600.";
                return false;
            }

            settings = new FactoryReinforcementSettings
            {
                BaseIndex = baseIndex,
                CheckIntervalSeconds = checkIntervalSeconds,
                MaxQueuePerFactory = maxQueuePerFactory,
                MaxStandingArmy = maxStandingArmy,
                RequireEconomy = requireEconomy,
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

            return Boolean.TryParse(value, out result);
        }
    }
}
