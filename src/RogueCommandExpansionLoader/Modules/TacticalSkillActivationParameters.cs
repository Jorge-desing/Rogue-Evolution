using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct TacticalSkillActivationSettings
    {
        public int BaseIndex;
        public float CooldownSeconds;
        public float EnemyTriggerRadiusCells;
        public float MinManaRatio;
    }

    /// <summary>
    /// Parses bounded settings for tactical AI active skill triggering in combat.
    /// Uses native EntityCommand.ActiveSkill* commands when enemies are detected within range.
    /// </summary>
    internal static class TacticalSkillActivationParameterParser
    {
        internal const string ModuleId = "sample:tactical-skill-activation";

        private const int DefaultBaseIndex = -1;
        private const float DefaultCooldownSeconds = 4.0f;
        private const float DefaultEnemyTriggerRadiusCells = 10.0f;
        private const float DefaultMinManaRatio = 0.5f;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out TacticalSkillActivationSettings settings,
            out string diagnostic)
        {
            settings = default(TacticalSkillActivationSettings);
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
                    parameter.name != "enemyTriggerRadiusCells" && parameter.name != "minManaRatio")
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
                    cooldownSeconds < 1.0f || cooldownSeconds > 30.0f)
                {
                    diagnostic = "cooldownSeconds must be a finite float between 1 and 30.";
                    return false;
                }
            }

            float enemyTriggerRadiusCells = DefaultEnemyTriggerRadiusCells;
            if (values.TryGetValue("enemyTriggerRadiusCells", out string radiusText))
            {
                if (!Single.TryParse(radiusText, NumberStyles.Float, CultureInfo.InvariantCulture, out enemyTriggerRadiusCells) ||
                    Single.IsNaN(enemyTriggerRadiusCells) || Single.IsInfinity(enemyTriggerRadiusCells) ||
                    enemyTriggerRadiusCells < 3.0f || enemyTriggerRadiusCells > 32.0f)
                {
                    diagnostic = "enemyTriggerRadiusCells must be a finite float between 3 and 32.";
                    return false;
                }
            }

            float minManaRatio = DefaultMinManaRatio;
            if (values.TryGetValue("minManaRatio", out string manaText))
            {
                if (!Single.TryParse(manaText, NumberStyles.Float, CultureInfo.InvariantCulture, out minManaRatio) ||
                    Single.IsNaN(minManaRatio) || Single.IsInfinity(minManaRatio) ||
                    minManaRatio < 0.1f || minManaRatio > 1.0f)
                {
                    diagnostic = "minManaRatio must be a finite float between 0.1 and 1.0.";
                    return false;
                }
            }

            settings = new TacticalSkillActivationSettings
            {
                BaseIndex = baseIndex,
                CooldownSeconds = cooldownSeconds,
                EnemyTriggerRadiusCells = enemyTriggerRadiusCells,
                MinManaRatio = minManaRatio
            };

            diagnostic = null;
            return true;
        }
    }
}
