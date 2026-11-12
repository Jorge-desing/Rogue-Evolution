using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct TacticalDirectorSettings
    {
        public int BaseIndex;
        public float EvalIntervalSeconds;
        public float EngagementRadius;
        public float DefenseRadius;
        public float RetreatHealthRatio;
        public int MaxProposalsPerUnit;
        public bool EnableSurvivalKiting;
        public bool EnableBaseDefense;
        public bool EnableFocusTargeting;
        public bool EnableFlanking;
        public float MinFlankSpeed;
    }

    /// <summary>
    /// Parses bounded, data-only settings for the HUB-D unified <c>sample:tactical-director</c> module.
    /// Consolidates survival/dead-zone kiting, base/harvester defense, and role-weighted combat targeting
    /// into a single coordinated module slot.
    /// </summary>
    internal static class TacticalDirectorParameterParser
    {
        internal const string ModuleId = "sample:tactical-director";

        private const int DefaultBaseIndex = -1;
        private const float DefaultEvalIntervalSeconds = 0.5f;
        private const float DefaultEngagementRadius = 40.0f;
        private const float DefaultDefenseRadius = 20.0f;
        private const float DefaultRetreatHealthRatio = 0.30f;
        private const int DefaultMaxProposalsPerUnit = 2;
        private const bool DefaultEnableSurvivalKiting = true;
        private const bool DefaultEnableBaseDefense = true;
        private const bool DefaultEnableFocusTargeting = true;
        private const bool DefaultEnableFlanking = true;
        private const float DefaultMinFlankSpeed = 7.5f;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out TacticalDirectorSettings settings,
            out string diagnostic)
        {
            settings = default(TacticalDirectorSettings);
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
                    parameter.name != "evalIntervalSeconds" &&
                    parameter.name != "engagementRadius" &&
                    parameter.name != "defenseRadius" &&
                    parameter.name != "retreatHealthRatio" &&
                    parameter.name != "maxProposalsPerUnit" &&
                    parameter.name != "enableSurvivalKiting" &&
                    parameter.name != "enableBaseDefense" &&
                    parameter.name != "enableFocusTargeting" &&
                    parameter.name != "enableFlanking" &&
                    parameter.name != "minFlankSpeed")
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

            float evalInterval;
            if (!TryGetFloat(values, "evalIntervalSeconds", DefaultEvalIntervalSeconds, 0.1f, 10.0f, out evalInterval))
            {
                diagnostic = "evalIntervalSeconds must be a finite number between 0.1 and 10.";
                return false;
            }

            float engagementRadius;
            if (!TryGetFloat(values, "engagementRadius", DefaultEngagementRadius, 5.0f, 120.0f, out engagementRadius))
            {
                diagnostic = "engagementRadius must be a finite number between 5 and 120.";
                return false;
            }

            float defenseRadius;
            if (!TryGetFloat(values, "defenseRadius", DefaultDefenseRadius, 5.0f, 60.0f, out defenseRadius))
            {
                diagnostic = "defenseRadius must be a finite number between 5 and 60.";
                return false;
            }

            float retreatHealthRatio;
            if (!TryGetFloat(values, "retreatHealthRatio", DefaultRetreatHealthRatio, 0.05f, 0.80f, out retreatHealthRatio))
            {
                diagnostic = "retreatHealthRatio must be a finite number between 0.05 and 0.80.";
                return false;
            }

            int maxProposalsPerUnit;
            if (!TryGetInteger(values, "maxProposalsPerUnit", DefaultMaxProposalsPerUnit, 1, 4, out maxProposalsPerUnit))
            {
                diagnostic = "maxProposalsPerUnit must be an integer between 1 and 4.";
                return false;
            }

            bool enableSurvivalKiting;
            if (!TryGetBool(values, "enableSurvivalKiting", DefaultEnableSurvivalKiting, out enableSurvivalKiting))
            {
                diagnostic = "enableSurvivalKiting must be 'true' or 'false'.";
                return false;
            }

            bool enableBaseDefense;
            if (!TryGetBool(values, "enableBaseDefense", DefaultEnableBaseDefense, out enableBaseDefense))
            {
                diagnostic = "enableBaseDefense must be 'true' or 'false'.";
                return false;
            }

            bool enableFocusTargeting;
            if (!TryGetBool(values, "enableFocusTargeting", DefaultEnableFocusTargeting, out enableFocusTargeting))
            {
                diagnostic = "enableFocusTargeting must be 'true' or 'false'.";
                return false;
            }

            bool enableFlanking;
            if (!TryGetBool(values, "enableFlanking", DefaultEnableFlanking, out enableFlanking))
            {
                diagnostic = "enableFlanking must be 'true' or 'false'.";
                return false;
            }

            float minFlankSpeed;
            if (!TryGetFloat(values, "minFlankSpeed", DefaultMinFlankSpeed, 3.0f, 25.0f, out minFlankSpeed))
            {
                diagnostic = "minFlankSpeed must be a finite number between 3 and 25.";
                return false;
            }

            settings = new TacticalDirectorSettings
            {
                BaseIndex = baseIndex,
                EvalIntervalSeconds = evalInterval,
                EngagementRadius = engagementRadius,
                DefenseRadius = defenseRadius,
                RetreatHealthRatio = retreatHealthRatio,
                MaxProposalsPerUnit = maxProposalsPerUnit,
                EnableSurvivalKiting = enableSurvivalKiting,
                EnableBaseDefense = enableBaseDefense,
                EnableFocusTargeting = enableFocusTargeting,
                EnableFlanking = enableFlanking,
                MinFlankSpeed = minFlankSpeed
            };
            diagnostic = null;
            return true;
        }

        private static bool TryGetInteger(
            IDictionary<string, string> values,
            string name,
            int defaultValue,
            int min,
            int max,
            out int result)
        {
            string raw;
            if (!values.TryGetValue(name, out raw))
            {
                result = defaultValue;
                return true;
            }

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                return false;
            }

            return result >= min && result <= max;
        }

        private static bool TryGetFloat(
            IDictionary<string, string> values,
            string name,
            float defaultValue,
            float min,
            float max,
            out float result)
        {
            string raw;
            if (!values.TryGetValue(name, out raw))
            {
                result = defaultValue;
                return true;
            }

            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                float.IsNaN(result) ||
                float.IsInfinity(result))
            {
                return false;
            }

            return result >= min && result <= max;
        }

        private static bool TryGetBool(
            IDictionary<string, string> values,
            string name,
            bool defaultValue,
            out bool result)
        {
            string raw;
            if (!values.TryGetValue(name, out raw))
            {
                result = defaultValue;
                return true;
            }

            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }
    }
}
