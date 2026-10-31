using RogueCommandExpansionLoader.Content;
using System;

namespace RogueCommandExpansionLoader.Modules
{
    public sealed class ThreatPrioritizationSettings
    {
        public int BaseIndex { get; set; }
        public float CheckIntervalSeconds { get; set; }
        public string PriorityMode { get; set; }

        public ThreatPrioritizationSettings()
        {
            BaseIndex = -1;
            CheckIntervalSeconds = 3f;
            PriorityMode = "HighestThreat";
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

            for (int i = 0; i < parameters.Length; i++)
            {
                ContentModuleParameter parameter = parameters[i];
                if (parameter == null)
                {
                    continue;
                }

                string name = parameter.name;
                string value = parameter.value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (string.Equals(name, "baseIndex", StringComparison.OrdinalIgnoreCase))
                {
                    int parsed;
                    if (!int.TryParse(value, out parsed) || parsed < -1 || parsed > 31)
                    {
                        diagnostic = "baseIndex must be an integer between -1 and 31.";
                        return false;
                    }
                    settings.BaseIndex = parsed;
                }
                else if (string.Equals(name, "checkIntervalSeconds", StringComparison.OrdinalIgnoreCase))
                {
                    float parsed;
                    if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed) ||
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