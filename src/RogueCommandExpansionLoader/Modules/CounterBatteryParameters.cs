using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct CounterBatterySettings
    {
        public int BaseIndex;
        public float ThreatTriggerDistance;
        public float MinInterceptorSpeed;
        public int MaxInterceptors;
        public float CooldownSeconds;
    }

    /// <summary>
    /// Parses bounded, data-only settings for the counter-battery AI coordination module.
    /// Detects long-range/artillery hostiles and dispatches fast interceptors to neutralize them.
    /// This parser is Unity-independent so package limits can be validated offline.
    /// </summary>
    internal static class CounterBatteryParameterParser
    {
        internal const string ModuleId = "sample:counter-battery";

        private const int DefaultBaseIndex = -1;
        private const float DefaultThreatTriggerDistance = 25.0f;
        private const float DefaultMinInterceptorSpeed = 10.0f;
        private const int DefaultMaxInterceptors = 4;
        private const float DefaultCooldownSeconds = 8.0f;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out CounterBatterySettings settings,
            out string diagnostic)
        {
            settings = default(CounterBatterySettings);
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

                if (parameter.name != "baseIndex" && parameter.name != "threatTriggerDistance" &&
                    parameter.name != "minInterceptorSpeed" && parameter.name != "maxInterceptors" &&
                    parameter.name != "cooldownSeconds")
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

            float threatTriggerDistance;
            if (!TryGetFloat(values, "threatTriggerDistance", DefaultThreatTriggerDistance, 15.0f, 80.0f,
                out threatTriggerDistance))
            {
                diagnostic = "threatTriggerDistance must be a finite number between 15 and 80.";
                return false;
            }

            float minInterceptorSpeed;
            if (!TryGetFloat(values, "minInterceptorSpeed", DefaultMinInterceptorSpeed, 5.0f, 30.0f,
                out minInterceptorSpeed))
            {
                diagnostic = "minInterceptorSpeed must be a finite number between 5 and 30.";
                return false;
            }

            int maxInterceptors;
            if (!TryGetInteger(values, "maxInterceptors", DefaultMaxInterceptors, 1, 12,
                out maxInterceptors))
            {
                diagnostic = "maxInterceptors must be an integer between 1 and 12.";
                return false;
            }

            float cooldownSeconds;
            if (!TryGetFloat(values, "cooldownSeconds", DefaultCooldownSeconds, 2.0f, 60.0f,
                out cooldownSeconds))
            {
                diagnostic = "cooldownSeconds must be a finite number between 2 and 60.";
                return false;
            }

            settings.BaseIndex = baseIndex;
            settings.ThreatTriggerDistance = threatTriggerDistance;
            settings.MinInterceptorSpeed = minInterceptorSpeed;
            settings.MaxInterceptors = maxInterceptors;
            settings.CooldownSeconds = cooldownSeconds;
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
