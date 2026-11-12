using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct SquadFormationSettings
    {
        public int BaseIndex;
        public float CooldownSeconds;
        public string AnchorRole;
        public int MaxFollowersPerCaptain;
        public float FormationRadiusCells;
    }

    /// <summary>
    /// Parses bounded settings for the captain-follower squad formation AI module.
    /// Uses native EntityController.SetCaptain to group units into flocking formations.
    /// </summary>
    internal static class SquadFormationParameterParser
    {
        internal const string ModuleId = "sample:squad-formation";

        private const int DefaultBaseIndex = -1;
        private const float DefaultCooldownSeconds = 5.0f;
        private const string DefaultAnchorRole = "Tank";
        private const int DefaultMaxFollowers = 4;
        private const float DefaultFormationRadiusCells = 10.0f;

        private static readonly HashSet<string> AllowedAnchorRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Tank", "Mech", "Defender", "FrontalAttacker", "Unit"
        };

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out SquadFormationSettings settings,
            out string diagnostic)
        {
            settings = default(SquadFormationSettings);
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
                    parameter.name != "anchorRole" && parameter.name != "maxFollowersPerCaptain" &&
                    parameter.name != "formationRadiusCells")
                {
                    diagnostic = "Unknown parameter '" + parameter.name + "' for module " + ModuleId + ".";
                    return false;
                }

                if (values.ContainsKey(parameter.name))
                {
                    diagnostic = "Duplicate parameter '" + parameter.name + "' for module " + ModuleId + ".";
                    return false;
                }

                values.Add(parameter.name, parameter.value);
            }

            int baseIndex = DefaultBaseIndex;
            if (values.TryGetValue("baseIndex", out string baseIndexText))
            {
                if (!Int32.TryParse(baseIndexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out baseIndex) ||
                    baseIndex < -1 || baseIndex > 31)
                {
                    diagnostic = "baseIndex must be an integer between -1 and 31.";
                    return false;
                }
            }

            float cooldownSeconds = DefaultCooldownSeconds;
            if (values.TryGetValue("cooldownSeconds", out string cooldownText))
            {
                if (!Single.TryParse(cooldownText, NumberStyles.Float, CultureInfo.InvariantCulture, out cooldownSeconds) ||
                    Single.IsNaN(cooldownSeconds) || Single.IsInfinity(cooldownSeconds) ||
                    cooldownSeconds < 1.0f || cooldownSeconds > 60.0f)
                {
                    diagnostic = "cooldownSeconds must be a finite float between 1 and 60.";
                    return false;
                }
            }

            string anchorRole = DefaultAnchorRole;
            if (values.TryGetValue("anchorRole", out string roleText))
            {
                if (String.IsNullOrWhiteSpace(roleText) || !AllowedAnchorRoles.Contains(roleText))
                {
                    diagnostic = "anchorRole must be one of: Tank, Mech, Defender, FrontalAttacker, Unit.";
                    return false;
                }
                anchorRole = roleText;
            }

            int maxFollowers = DefaultMaxFollowers;
            if (values.TryGetValue("maxFollowersPerCaptain", out string maxFollowersText))
            {
                if (!Int32.TryParse(maxFollowersText, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxFollowers) ||
                    maxFollowers < 1 || maxFollowers > 8)
                {
                    diagnostic = "maxFollowersPerCaptain must be an integer between 1 and 8.";
                    return false;
                }
            }

            float formationRadiusCells = DefaultFormationRadiusCells;
            if (values.TryGetValue("formationRadiusCells", out string radiusText))
            {
                if (!Single.TryParse(radiusText, NumberStyles.Float, CultureInfo.InvariantCulture, out formationRadiusCells) ||
                    Single.IsNaN(formationRadiusCells) || Single.IsInfinity(formationRadiusCells) ||
                    formationRadiusCells < 3.0f || formationRadiusCells > 32.0f)
                {
                    diagnostic = "formationRadiusCells must be a finite float between 3 and 32.";
                    return false;
                }
            }

            settings = new SquadFormationSettings
            {
                BaseIndex = baseIndex,
                CooldownSeconds = cooldownSeconds,
                AnchorRole = anchorRole,
                MaxFollowersPerCaptain = maxFollowers,
                FormationRadiusCells = formationRadiusCells
            };

            diagnostic = null;
            return true;
        }
    }
}
