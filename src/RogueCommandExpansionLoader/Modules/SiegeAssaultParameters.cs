using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct SiegeAssaultSettings
    {
        public int BaseIndex;
        public float CooldownSeconds;
        public int MinSquadSize;
        public string TargetPriority;
        public float MinGameTimeSeconds;
    }

    /// <summary>
    /// Parses bounded, data-only settings for the siege assault AI module.
    /// This parser is Unity-independent so package limits can be validated offline.
    /// </summary>
    internal static class SiegeAssaultParameterParser
    {
        internal const string ModuleId = "sample:siege-assault";

        public const string TargetPriorityClosestBuilding = "ClosestBuilding";
        public const string TargetPriorityRefinery = "Refinery";
        public const string TargetPriorityHq = "Hq";

        private const int DefaultBaseIndex = 0;
        private const float DefaultCooldownSeconds = 40f;
        private const int DefaultMinSquadSize = 4;
        private const string DefaultTargetPriority = TargetPriorityClosestBuilding;
        private const float DefaultMinGameTimeSeconds = 30f;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out SiegeAssaultSettings settings,
            out string diagnostic)
        {
            settings = default(SiegeAssaultSettings);
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
                    parameter.name != "minSquadSize" && parameter.name != "targetPriority" &&
                    parameter.name != "minGameTimeSeconds")
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
            if (!TryGetFloat(values, "cooldownSeconds", DefaultCooldownSeconds, 10f, 300f,
                out cooldownSeconds))
            {
                diagnostic = "cooldownSeconds must be a finite number between 10 and 300.";
                return false;
            }

            int minSquadSize;
            if (!TryGetInteger(values, "minSquadSize", DefaultMinSquadSize, 1, 12,
                out minSquadSize))
            {
                diagnostic = "minSquadSize must be an integer between 1 and 12.";
                return false;
            }

            string targetPriority;
            if (!values.TryGetValue("targetPriority", out targetPriority))
            {
                targetPriority = DefaultTargetPriority;
            }
            else if (!String.Equals(targetPriority, TargetPriorityClosestBuilding, StringComparison.Ordinal) &&
                     !String.Equals(targetPriority, TargetPriorityRefinery, StringComparison.Ordinal) &&
                     !String.Equals(targetPriority, TargetPriorityHq, StringComparison.Ordinal))
            {
                diagnostic = "targetPriority must be 'ClosestBuilding', 'Refinery', or 'Hq'.";
                return false;
            }

            float minGameTimeSeconds;
            if (!TryGetFloat(values, "minGameTimeSeconds", DefaultMinGameTimeSeconds, 0f, 600f,
                out minGameTimeSeconds))
            {
                diagnostic = "minGameTimeSeconds must be a finite number between 0 and 600.";
                return false;
            }

            settings = new SiegeAssaultSettings
            {
                BaseIndex = baseIndex,
                CooldownSeconds = cooldownSeconds,
                MinSquadSize = minSquadSize,
                TargetPriority = targetPriority,
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
                !Single.IsNaN(result) && !Single.IsInfinity(result) && result >= minimum && result <= maximum;
        }
    }
}
