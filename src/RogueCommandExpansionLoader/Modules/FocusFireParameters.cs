using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct FocusFireSettings
    {
        public int BaseIndex;
        public float CheckIntervalSeconds;
        public float SquadRadius;
        public int MinUnitsInSquad;
        public string PriorityMode;
        public string TargetRole;
    }

    /// <summary>
    /// Parses bounded, data-only settings for the tactical focus-fire AI coordination module.
    /// This parser is Unity-independent so package limits can be validated offline.
    /// </summary>
    internal static class FocusFireParameterParser
    {
        internal const string ModuleId = "sample:focus-fire";

        private const int DefaultBaseIndex = -1;
        private const float DefaultCheckIntervalSeconds = 3.0f;
        private const float DefaultSquadRadius = 18.0f;
        private const int DefaultMinUnitsInSquad = 2;
        private const string DefaultPriorityMode = "LowestHealth";
        private const string DefaultTargetRole = "None";
        private static readonly HashSet<string> SupportedTargetRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "None", "Spawn", "Harvester", "FrontalAttacker", "MassDestruction", "Fire", "Sniper", "Supporter",
            "Rock", "Robo", "Spawner", "PCXCard", "Building", "Factory", "Turret", "Refinery", "Unit",
            "Heal", "Specialist", "Skill", "Engineer", "Bomb", "Scout", "Chest", "Tank", "Vehicle",
            "Mech", "Tree", "Melee", "Research", "Crystal", "Builder", "Drop"
        };

        private static bool IsSupportedTargetRole(string roleName)
        {
            return SupportedTargetRoles.Contains(roleName);
        }

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out FocusFireSettings settings,
            out string diagnostic)
        {
            settings = default(FocusFireSettings);
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
                    parameter.name != "squadRadius" && parameter.name != "minUnitsInSquad" &&
                    parameter.name != "priorityMode" && parameter.name != "targetRole")
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
            if (!TryGetFloat(values, "checkIntervalSeconds", DefaultCheckIntervalSeconds, 0.5f, 30f,
                out checkIntervalSeconds))
            {
                diagnostic = "checkIntervalSeconds must be a finite number between 0.5 and 30.";
                return false;
            }

            float squadRadius;
            if (!TryGetFloat(values, "squadRadius", DefaultSquadRadius, 5f, 50f, out squadRadius))
            {
                diagnostic = "squadRadius must be a finite number between 5 and 50.";
                return false;
            }

            int minUnitsInSquad;
            if (!TryGetInteger(values, "minUnitsInSquad", DefaultMinUnitsInSquad, 2, 12, out minUnitsInSquad))
            {
                diagnostic = "minUnitsInSquad must be an integer between 2 and 12.";
                return false;
            }

            string priorityMode;
            if (values.TryGetValue("priorityMode", out priorityMode))
            {
                if (priorityMode != "LowestHealth" && priorityMode != "HighestThreat" && priorityMode != "TargetRole")
                {
                    diagnostic = "priorityMode must be 'LowestHealth', 'HighestThreat', or 'TargetRole'.";
                    return false;
                }
            }
            else
            {
                priorityMode = DefaultPriorityMode;
            }

            string targetRole;
            if (values.TryGetValue("targetRole", out targetRole))
            {
                if (String.IsNullOrWhiteSpace(targetRole) || !IsSupportedTargetRole(targetRole))
                {
                    diagnostic = "targetRole must match a supported UnitRole name when supplied.";
                    return false;
                }
            }
            else
            {
                targetRole = DefaultTargetRole;
            }

            settings = new FocusFireSettings
            {
                BaseIndex = baseIndex,
                CheckIntervalSeconds = checkIntervalSeconds,
                SquadRadius = squadRadius,
                MinUnitsInSquad = minUnitsInSquad,
                PriorityMode = priorityMode,
                TargetRole = targetRole
            };

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
