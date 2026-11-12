using System;
using System.Collections.Generic;
using System.Globalization;
using RogueCommandExpansionLoader.Content;

namespace RogueCommandExpansionLoader.Modules
{
    internal struct WaveEscalationSettings
    {
        public int BaseIndex;
        public float WaveIntervalSeconds;
        public int MaxWaves;
    }

    /// <summary>
    /// Parses bounded settings for the dynamic wave escalation AI module.
    /// Manages match wave pacing and calls AiBehaviourManager.IncrementAttackWaveNumber_Static().
    /// </summary>
    internal static class WaveEscalationParameterParser
    {
        internal const string ModuleId = "sample:wave-escalation";

        private const int DefaultBaseIndex = -1;
        private const float DefaultWaveIntervalSeconds = 60.0f;
        private const int DefaultMaxWaves = 10;

        public static bool TryParse(
            ContentModuleParameter[] parameters,
            out WaveEscalationSettings settings,
            out string diagnostic)
        {
            settings = default(WaveEscalationSettings);
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

                if (parameter.name != "baseIndex" && parameter.name != "waveIntervalSeconds" &&
                    parameter.name != "maxWaves")
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

            float waveIntervalSeconds = DefaultWaveIntervalSeconds;
            if (values.TryGetValue("waveIntervalSeconds", out string intervalText))
            {
                if (!Single.TryParse(intervalText, NumberStyles.Float, CultureInfo.InvariantCulture, out waveIntervalSeconds) ||
                    Single.IsNaN(waveIntervalSeconds) || Single.IsInfinity(waveIntervalSeconds) ||
                    waveIntervalSeconds < 10.0f || waveIntervalSeconds > 300.0f)
                {
                    diagnostic = "waveIntervalSeconds must be a finite float between 10 and 300.";
                    return false;
                }
            }

            int maxWaves = DefaultMaxWaves;
            if (values.TryGetValue("maxWaves", out string maxWavesText))
            {
                if (!Int32.TryParse(maxWavesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxWaves) ||
                    maxWaves < 1 || maxWaves > 20)
                {
                    diagnostic = "maxWaves must be an integer between 1 and 20.";
                    return false;
                }
            }

            settings = new WaveEscalationSettings
            {
                BaseIndex = baseIndex,
                WaveIntervalSeconds = waveIntervalSeconds,
                MaxWaves = maxWaves
            };

            diagnostic = null;
            return true;
        }
    }
}
