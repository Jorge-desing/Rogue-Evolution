using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct AdaptiveCommanderSettings
    {
        public int BaseIndex;
        public float ScanIntervalSeconds;
        public float CommandRadius;
        public float HealthyStanceThreshold;
        public float DamagedStanceThreshold;
        public string AuraType;
        public string PreferredLoadout;
        public string CommanderAffix;
        public int XpGrantPerPulse;
        public bool AdaptiveDoctrine;
    }

    /// <summary>
    /// Parses bounded, data-only settings for the adaptive commander AI module.
    /// Dynamically promotes an AI squad commander, dispatches command auras, modulates squad stances
    /// and loadouts, grants combat XP, and adapts AI doctrine based on battlefield force balance.
    /// </summary>
    internal static class AdaptiveCommanderParameterParser
    {
        internal const string ModuleId = "sample:adaptive-commander";

        private const int DefaultBaseIndex = -1;
        private const float DefaultScanIntervalSeconds = 3.0f;
        private const float DefaultCommandRadius = 25.0f;
        private const float DefaultHealthyStanceThreshold = 0.70f;
        private const float DefaultDamagedStanceThreshold = 0.35f;
        private const string DefaultAuraType = "Inspire";
        private const string DefaultPreferredLoadout = "AP";
        private const string DefaultCommanderAffix = "Overcharged";
        private const int DefaultXpGrantPerPulse = 1;
        private const bool DefaultAdaptiveDoctrine = true;

        private static readonly HashSet<string> SupportedAuraTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Inspire", "Aegis", "Swiftness"
        };

        private static readonly HashSet<string> SupportedLoadouts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Standard", "AP", "HE", "Speed", "Siege", "Reinforced"
        };

        private static readonly HashSet<string> SupportedAffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Swift", "Bulwark", "Juggernaut", "Overcharged", "Vampiric", "Volatile"
        };

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out AdaptiveCommanderSettings settings,
            out string diagnostic)
        {
            settings = default(AdaptiveCommanderSettings);
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
                    parameter.name != "scanIntervalSeconds" &&
                    parameter.name != "commandRadius" &&
                    parameter.name != "healthyStanceThreshold" &&
                    parameter.name != "damagedStanceThreshold" &&
                    parameter.name != "auraType" &&
                    parameter.name != "preferredLoadout" &&
                    parameter.name != "commanderAffix" &&
                    parameter.name != "xpGrantPerPulse" &&
                    parameter.name != "adaptiveDoctrine")
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

            float scanInterval;
            if (!TryGetFloat(values, "scanIntervalSeconds", DefaultScanIntervalSeconds, 0.5f, 60.0f, out scanInterval))
            {
                diagnostic = "scanIntervalSeconds must be a finite number between 0.5 and 60.";
                return false;
            }

            float commandRadius;
            if (!TryGetFloat(values, "commandRadius", DefaultCommandRadius, 5.0f, 100.0f, out commandRadius))
            {
                diagnostic = "commandRadius must be a finite number between 5 and 100.";
                return false;
            }

            float healthyThreshold;
            if (!TryGetFloat(values, "healthyStanceThreshold", DefaultHealthyStanceThreshold, 0.50f, 1.0f, out healthyThreshold))
            {
                diagnostic = "healthyStanceThreshold must be a finite number between 0.5 and 1.0.";
                return false;
            }

            float damagedThreshold;
            if (!TryGetFloat(values, "damagedStanceThreshold", DefaultDamagedStanceThreshold, 0.05f, 0.49f, out damagedThreshold))
            {
                diagnostic = "damagedStanceThreshold must be a finite number between 0.05 and 0.49.";
                return false;
            }

            if (damagedThreshold >= healthyThreshold)
            {
                diagnostic = "damagedStanceThreshold must be strictly less than healthyStanceThreshold.";
                return false;
            }

            string auraType = DefaultAuraType;
            string rawAura;
            if (values.TryGetValue("auraType", out rawAura))
            {
                if (string.IsNullOrWhiteSpace(rawAura) || !SupportedAuraTypes.Contains(rawAura.Trim()))
                {
                    diagnostic = "auraType must be one of: Inspire, Aegis, Swiftness.";
                    return false;
                }
                auraType = NormalizeCanonical(rawAura.Trim(), new[] { "Inspire", "Aegis", "Swiftness" });
            }

            string preferredLoadout = DefaultPreferredLoadout;
            string rawLoadout;
            if (values.TryGetValue("preferredLoadout", out rawLoadout))
            {
                if (string.IsNullOrWhiteSpace(rawLoadout) || !SupportedLoadouts.Contains(rawLoadout.Trim()))
                {
                    diagnostic = "preferredLoadout must be one of: Standard, AP, HE, Speed, Siege, Reinforced.";
                    return false;
                }
                preferredLoadout = NormalizeCanonical(rawLoadout.Trim(), new[] { "Standard", "AP", "HE", "Speed", "Siege", "Reinforced" });
            }

            string commanderAffix = DefaultCommanderAffix;
            string rawAffix;
            if (values.TryGetValue("commanderAffix", out rawAffix))
            {
                if (string.IsNullOrWhiteSpace(rawAffix) || !SupportedAffixes.Contains(rawAffix.Trim()))
                {
                    diagnostic = "commanderAffix must be one of: Swift, Bulwark, Juggernaut, Overcharged, Vampiric, Volatile.";
                    return false;
                }
                commanderAffix = NormalizeCanonical(rawAffix.Trim(), new[] { "Swift", "Bulwark", "Juggernaut", "Overcharged", "Vampiric", "Volatile" });
            }

            int xpGrant;
            if (!TryGetInteger(values, "xpGrantPerPulse", DefaultXpGrantPerPulse, 0, 10, out xpGrant))
            {
                diagnostic = "xpGrantPerPulse must be an integer between 0 and 10.";
                return false;
            }

            bool adaptiveDoctrine = DefaultAdaptiveDoctrine;
            string rawAdaptive;
            if (values.TryGetValue("adaptiveDoctrine", out rawAdaptive))
            {
                bool parsedBool;
                if (!bool.TryParse(rawAdaptive, out parsedBool))
                {
                    diagnostic = "adaptiveDoctrine must be 'true' or 'false'.";
                    return false;
                }
                adaptiveDoctrine = parsedBool;
            }

            settings = new AdaptiveCommanderSettings
            {
                BaseIndex = baseIndex,
                ScanIntervalSeconds = scanInterval,
                CommandRadius = commandRadius,
                HealthyStanceThreshold = healthyThreshold,
                DamagedStanceThreshold = damagedThreshold,
                AuraType = auraType,
                PreferredLoadout = preferredLoadout,
                CommanderAffix = commanderAffix,
                XpGrantPerPulse = xpGrant,
                AdaptiveDoctrine = adaptiveDoctrine
            };

            diagnostic = null;
            return true;
        }

        private static string NormalizeCanonical(string input, string[] canonicalValues)
        {
            for (int i = 0; i < canonicalValues.Length; i++)
            {
                if (string.Equals(input, canonicalValues[i], StringComparison.OrdinalIgnoreCase))
                {
                    return canonicalValues[i];
                }
            }
            return input;
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
