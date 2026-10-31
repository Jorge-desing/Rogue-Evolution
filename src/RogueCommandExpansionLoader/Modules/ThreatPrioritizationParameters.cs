using RogueCommandExpansionLoader.Content;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RogueCommandExpansionLoader.Modules
{
    public sealed class ThreatPrioritizationSettings
    {
        public int BaseIndex { get; set; }
        public float CheckIntervalSeconds { get; set; }
        public string PriorityMode { get; set; }
        public float EngagementRadius { get; set; }
        public float RetargetThreshold { get; set; }
        public int MaxRetargetsPerTick { get; set; }

        public ThreatPrioritizationSettings()
        {
            BaseIndex = -1;
            CheckIntervalSeconds = 3f;
            PriorityMode = "HighestThreat";
            EngagementRadius = 35f;
            RetargetThreshold = 1.25f;
            MaxRetargetsPerTick = 8;
        }
    }

    public static class ThreatPrioritizationParameterParser
    {
        public const string ModuleId = "sample:threat-prioritization";

        public static bool TryParse(ContentModuleParameter[] parameters, out ThreatPrioritizationSettings settings, out string diagnostic)
        {
            settings = new ThreatPrioritizationSettings();
            diagnostic = null;

            if (parameters == null)
            {
                return true;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < parameters.Length; i++)
            {
                ContentModuleParameter parameter = parameters[i];
                if (parameter == null || parameter.name == null || parameter.value == null)
                {
                    diagnostic = "Module parameters cannot be null.";
                    return false;
                }

                string name = parameter.name;
                string value = parameter.value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!seen.Add(name))
                {
                    diagnostic = "Parameter '" + name + "' may only be supplied once.";
                    return false;
                }

                if (string.Equals(name, "baseIndex", StringComparison.OrdinalIgnoreCase))
                {
                    int parsed;
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) || parsed < -1 || parsed > 31)
                    {
                        diagnostic = "baseIndex must be an integer between -1 and 31.";
                        return false;
                    }
                    settings.BaseIndex = parsed;
                }
                else if (string.Equals(name, "checkIntervalSeconds", StringComparison.OrdinalIgnoreCase))
                {
                    float parsed;
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                        float.IsNaN(parsed) || float.IsInfinity(parsed) ||
                        parsed < 0.5f || parsed > 30f)
                    {
                        diagnostic = "checkIntervalSeconds must be between 0.5 and 30.";
                        return false;
                    }
                    settings.CheckIntervalSeconds = parsed;
                }
                else if (string.Equals(name, "priorityMode", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(value, "HighestThreat", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(value, "LowestHealth", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(value, "HighestDps", StringComparison.OrdinalIgnoreCase))
                    {
                        diagnostic = "priorityMode must be HighestThreat, LowestHealth, or HighestDps.";
                        return false;
                    }
                    settings.PriorityMode = value;
                }
                else if (string.Equals(name, "engagementRadius", StringComparison.OrdinalIgnoreCase))
                {
                    float parsed;
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                        float.IsNaN(parsed) || float.IsInfinity(parsed) ||
                        parsed < 5f || parsed > 120f)
                    {
                        diagnostic = "engagementRadius must be between 5 and 120.";
                        return false;
                    }
                    settings.EngagementRadius = parsed;
                }
                else if (string.Equals(name, "retargetThreshold", StringComparison.OrdinalIgnoreCase))
                {
                    float parsed;
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                        float.IsNaN(parsed) || float.IsInfinity(parsed) ||
                        parsed < 1.0f || parsed > 3.0f)
                    {
                        diagnostic = "retargetThreshold must be between 1.0 and 3.0.";
                        return false;
                    }
                    settings.RetargetThreshold = parsed;
                }
                else if (string.Equals(name, "maxRetargetsPerTick", StringComparison.OrdinalIgnoreCase))
                {
                    int parsed;
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) || parsed < 1 || parsed > 32)
                    {
                        diagnostic = "maxRetargetsPerTick must be an integer between 1 and 32.";
                        return false;
                    }
                    settings.MaxRetargetsPerTick = parsed;
                }
                else
                {
                    diagnostic = "Unknown parameter: " + name;
                    return false;
                }
            }

            return true;
        }
    }
}
