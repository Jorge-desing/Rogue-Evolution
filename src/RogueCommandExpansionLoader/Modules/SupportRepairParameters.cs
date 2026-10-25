using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct SupportRepairSettings
    {
        public float HealthThresholdRatio;
        public float RepairScanRadius;
        public bool PrioritizeHarvestersAndBuildings;
        public int MaxAssignedRepairersPerTarget;
        public float CooldownSeconds;
    }

    /// <summary>
    /// Parses bounded, data-only settings for the autonomous support and field repair AI module.
    /// Support and engineer units monitor damaged friendly units and structures, moving to repair them.
    /// </summary>
    internal static class SupportRepairParameterParser
    {
        internal const string ModuleId = "sample:support-repair";

        private const float DefaultHealthThresholdRatio = 0.85f;
        private const float DefaultRepairScanRadius = 25.0f;
        private const bool DefaultPrioritizeHarvestersAndBuildings = true;
        private const int DefaultMaxAssignedRepairersPerTarget = 2;
        private const float DefaultCooldownSeconds = 2.0f;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out SupportRepairSettings settings,
            out string diagnostic)
        {
            settings = default(SupportRepairSettings);
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

                if (parameter.name != "healthThresholdRatio" && parameter.name != "repairScanRadius" &&
                    parameter.name != "prioritizeHarvestersAndBuildings" && parameter.name != "maxAssignedRepairersPerTarget" &&
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

            float healthThreshold;
            if (!TryGetFloat(values, "healthThresholdRatio", DefaultHealthThresholdRatio, 0.10f, 0.99f, out healthThreshold))
            {
                diagnostic = "healthThresholdRatio must be a finite number between 0.1 and 0.99.";
                return false;
            }

            float scanRadius;
            if (!TryGetFloat(values, "repairScanRadius", DefaultRepairScanRadius, 5.0f, 100.0f, out scanRadius))
            {
                diagnostic = "repairScanRadius must be a finite number between 5 and 100.";
                return false;
            }

            bool prioritize;
            if (!TryGetBoolean(values, "prioritizeHarvestersAndBuildings", DefaultPrioritizeHarvestersAndBuildings, out prioritize))
            {
                diagnostic = "prioritizeHarvestersAndBuildings must be 'true' or 'false'.";
                return false;
            }

            int maxRepairers;
            if (!TryGetInteger(values, "maxAssignedRepairersPerTarget", DefaultMaxAssignedRepairersPerTarget, 1, 10, out maxRepairers))
            {
                diagnostic = "maxAssignedRepairersPerTarget must be an integer between 1 and 10.";
                return false;
            }

            float cooldown;
            if (!TryGetFloat(values, "cooldownSeconds", DefaultCooldownSeconds, 0.5f, 15.0f, out cooldown))
            {
                diagnostic = "cooldownSeconds must be a finite number between 0.5 and 15.";
                return false;
            }

            settings = new SupportRepairSettings
            {
                HealthThresholdRatio = healthThreshold,
                RepairScanRadius = scanRadius,
                PrioritizeHarvestersAndBuildings = prioritize,
                MaxAssignedRepairersPerTarget = maxRepairers,
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

        private static bool TryGetBoolean(
            Dictionary<string, string> values,
            string key,
            bool defaultValue,
            out bool result)
        {
            string raw;
            if (!values.TryGetValue(key, out raw))
            {
                result = defaultValue;
                return true;
            }

            bool parsed;
            if (!bool.TryParse(raw, out parsed))
            {
                result = defaultValue;
                return false;
            }

            result = parsed;
            return true;
        }
    }
}
