using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    public sealed class SquadOperationsDirectorSettings
    {
        public int BaseIndex { get; }
        public float EvalIntervalSeconds { get; }
        public float SupportRadius { get; }
        public float CounterBatteryMinRangeThreshold { get; }
        public bool EnableSupportLogistics { get; }
        public bool EnableCounterBattery { get; }
        public bool EnableTacticalSkills { get; }

        public SquadOperationsDirectorSettings(
            int baseIndex,
            float evalIntervalSeconds,
            float supportRadius,
            float counterBatteryMinRangeThreshold,
            bool enableSupportLogistics,
            bool enableCounterBattery,
            bool enableTacticalSkills)
        {
            BaseIndex = baseIndex;
            EvalIntervalSeconds = evalIntervalSeconds;
            SupportRadius = supportRadius;
            CounterBatteryMinRangeThreshold = counterBatteryMinRangeThreshold;
            EnableSupportLogistics = enableSupportLogistics;
            EnableCounterBattery = enableCounterBattery;
            EnableTacticalSkills = enableTacticalSkills;
        }
    }

    public static class SquadOperationsDirectorParameterParser
    {
        public const string ModuleId = "sample:squad-operations-director";

        public const int DefaultBaseIndex = -1;
        public const int MinBaseIndex = -1;
        public const int MaxBaseIndex = 31;

        public const float DefaultEvalIntervalSeconds = 0.75f;
        public const float MinEvalIntervalSeconds = 0.20f;
        public const float MaxEvalIntervalSeconds = 10.0f;

        public const float DefaultSupportRadius = 30.0f;
        public const float MinSupportRadius = 5.0f;
        public const float MaxSupportRadius = 80.0f;

        public const float DefaultCounterBatteryMinRangeThreshold = 14.0f;
        public const float MinCounterBatteryMinRangeThreshold = 8.0f;
        public const float MaxCounterBatteryMinRangeThreshold = 50.0f;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out SquadOperationsDirectorSettings settings,
            out string diagnostic)
        {
            settings = null;
            diagnostic = null;

            int baseIndex = DefaultBaseIndex;
            float evalInterval = DefaultEvalIntervalSeconds;
            float supportRadius = DefaultSupportRadius;
            float counterBatteryThreshold = DefaultCounterBatteryMinRangeThreshold;
            bool enableSupport = true;
            bool enableCounterBattery = true;
            bool enableSkills = true;

            if (parameters != null && parameters.Length > 0)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < parameters.Length; i++)
                {
                    ContentModuleParameter param = parameters[i];
                    if (param == null || string.IsNullOrWhiteSpace(param.name))
                    {
                        diagnostic = ModuleId + " parameter name cannot be empty.";
                        return false;
                    }

                    string name = param.name.Trim();
                    if (!seen.Add(name))
                    {
                        diagnostic = ModuleId + " duplicate parameter: " + name;
                        return false;
                    }

                    string rawValue = param.value != null ? param.value.Trim() : string.Empty;
                    switch (name)
                    {
                        case "baseIndex":
                            if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out baseIndex) ||
                                baseIndex < MinBaseIndex || baseIndex > MaxBaseIndex)
                            {
                                diagnostic = ModuleId + " baseIndex must be an integer between " +
                                    MinBaseIndex + " and " + MaxBaseIndex + ".";
                                return false;
                            }
                            break;

                        case "evalIntervalSeconds":
                            if (!TryParseBoundedFloat(rawValue, MinEvalIntervalSeconds, MaxEvalIntervalSeconds, out evalInterval))
                            {
                                diagnostic = ModuleId + " evalIntervalSeconds must be between " +
                                    MinEvalIntervalSeconds.ToString(CultureInfo.InvariantCulture) + " and " +
                                    MaxEvalIntervalSeconds.ToString(CultureInfo.InvariantCulture) + ".";
                                return false;
                            }
                            break;

                        case "supportRadius":
                            if (!TryParseBoundedFloat(rawValue, MinSupportRadius, MaxSupportRadius, out supportRadius))
                            {
                                diagnostic = ModuleId + " supportRadius must be between " +
                                    MinSupportRadius.ToString(CultureInfo.InvariantCulture) + " and " +
                                    MaxSupportRadius.ToString(CultureInfo.InvariantCulture) + ".";
                                return false;
                            }
                            break;

                        case "counterBatteryMinRangeThreshold":
                            if (!TryParseBoundedFloat(rawValue, MinCounterBatteryMinRangeThreshold, MaxCounterBatteryMinRangeThreshold, out counterBatteryThreshold))
                            {
                                diagnostic = ModuleId + " counterBatteryMinRangeThreshold must be between " +
                                    MinCounterBatteryMinRangeThreshold.ToString(CultureInfo.InvariantCulture) + " and " +
                                    MaxCounterBatteryMinRangeThreshold.ToString(CultureInfo.InvariantCulture) + ".";
                                return false;
                            }
                            break;

                        case "enableSupportLogistics":
                            if (!bool.TryParse(rawValue, out enableSupport))
                            {
                                diagnostic = ModuleId + " enableSupportLogistics must be a boolean.";
                                return false;
                            }
                            break;

                        case "enableCounterBattery":
                            if (!bool.TryParse(rawValue, out enableCounterBattery))
                            {
                                diagnostic = ModuleId + " enableCounterBattery must be a boolean.";
                                return false;
                            }
                            break;

                        case "enableTacticalSkills":
                            if (!bool.TryParse(rawValue, out enableSkills))
                            {
                                diagnostic = ModuleId + " enableTacticalSkills must be a boolean.";
                                return false;
                            }
                            break;

                        default:
                            diagnostic = ModuleId + " unknown parameter: " + name;
                            return false;
                    }
                }
            }

            settings = new SquadOperationsDirectorSettings(
                baseIndex: baseIndex,
                evalIntervalSeconds: evalInterval,
                supportRadius: supportRadius,
                counterBatteryMinRangeThreshold: counterBatteryThreshold,
                enableSupportLogistics: enableSupport,
                enableCounterBattery: enableCounterBattery,
                enableTacticalSkills: enableSkills);
            return true;
        }

        private static bool TryParseBoundedFloat(string raw, float min, float max, out float parsed)
        {
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
                float.IsNaN(parsed) || float.IsInfinity(parsed) ||
                parsed < min || parsed > max)
            {
                parsed = 0f;
                return false;
            }

            return true;
        }
    }
}
