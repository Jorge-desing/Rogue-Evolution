using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct SkirmishKitingSettings
    {
        public float RetreatDistanceRatio;
        public float PreferredEngagementRatio;
        public float MinKiteSpeed;
        public float FleeDistance;
        public int MaxKiteTargets;
        public float CooldownSeconds;
    }

    /// <summary>
    /// Parses bounded, data-only settings for the skirmish kiting AI module.
    /// Fast ranged units maintain tactical distance, retreating when enemies encroach inside the retreat buffer.
    /// </summary>
    internal static class SkirmishKitingParameterParser
    {
        internal const string ModuleId = "sample:skirmish-kiting";

        private const float DefaultRetreatDistanceRatio = 0.70f;
        private const float DefaultPreferredEngagementRatio = 0.90f;
        private const float DefaultMinKiteSpeed = 8.0f;
        private const float DefaultFleeDistance = 8.0f;
        private const int DefaultMaxKiteTargets = 8;
        private const float DefaultCooldownSeconds = 1.0f;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out SkirmishKitingSettings settings,
            out string diagnostic)
        {
            settings = default(SkirmishKitingSettings);
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

                if (parameter.name != "retreatDistanceRatio" && parameter.name != "preferredEngagementRatio" &&
                    parameter.name != "minKiteSpeed" && parameter.name != "fleeDistance" &&
                    parameter.name != "maxKiteTargets" && parameter.name != "cooldownSeconds")
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

            float retreatRatio;
            if (!TryGetFloat(values, "retreatDistanceRatio", DefaultRetreatDistanceRatio, 0.20f, 0.95f, out retreatRatio))
            {
                diagnostic = "retreatDistanceRatio must be a finite number between 0.2 and 0.95.";
                return false;
            }

            float preferredRatio;
            if (!TryGetFloat(values, "preferredEngagementRatio", DefaultPreferredEngagementRatio, 0.50f, 1.0f, out preferredRatio))
            {
                diagnostic = "preferredEngagementRatio must be a finite number between 0.5 and 1.0.";
                return false;
            }

            if (retreatRatio >= preferredRatio)
            {
                diagnostic = "retreatDistanceRatio must be strictly less than preferredEngagementRatio.";
                return false;
            }

            float minSpeed;
            if (!TryGetFloat(values, "minKiteSpeed", DefaultMinKiteSpeed, 1.0f, 50.0f, out minSpeed))
            {
                diagnostic = "minKiteSpeed must be a finite number between 1 and 50.";
                return false;
            }

            float fleeDistance;
            if (!TryGetFloat(values, "fleeDistance", DefaultFleeDistance, 2.0f, 40.0f, out fleeDistance))
            {
                diagnostic = "fleeDistance must be a finite number between 2 and 40.";
                return false;
            }

            int maxTargets;
            if (!TryGetInteger(values, "maxKiteTargets", DefaultMaxKiteTargets, 1, 32, out maxTargets))
            {
                diagnostic = "maxKiteTargets must be an integer between 1 and 32.";
                return false;
            }

            float cooldown;
            if (!TryGetFloat(values, "cooldownSeconds", DefaultCooldownSeconds, 0.2f, 10.0f, out cooldown))
            {
                diagnostic = "cooldownSeconds must be a finite number between 0.2 and 10.";
                return false;
            }

            settings = new SkirmishKitingSettings
            {
                RetreatDistanceRatio = retreatRatio,
                PreferredEngagementRatio = preferredRatio,
                MinKiteSpeed = minSpeed,
                FleeDistance = fleeDistance,
                MaxKiteTargets = maxTargets,
                CooldownSeconds = cooldown
            };

            diagnostic = null;
            return true;
        }

        private static bool TryGetInteger(
            Dictionary<string, string> values,
            string key,
            int defaultValue,
            int min,
            int max,
            out int result)
        {
            string raw;
            if (!values.TryGetValue(key, out raw))
            {
                result = defaultValue;
                return true;
            }

            int parsed;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ||
                parsed < min || parsed > max)
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
            float min,
            float max,
            out float result)
        {
            string raw;
            if (!values.TryGetValue(key, out raw))
            {
                result = defaultValue;
                return true;
            }

            float parsed;
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                float.IsNaN(parsed) || float.IsInfinity(parsed) || parsed < min || parsed > max)
            {
                result = defaultValue;
                return false;
            }

            result = parsed;
            return true;
        }
    }
}
