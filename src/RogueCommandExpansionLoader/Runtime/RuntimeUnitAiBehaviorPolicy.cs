using System;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure bounds and cadence policy for the per-unit AI behavior fields.
    /// Native Rogue Command exposes only a global AI production cap and a
    /// hard-coded 0.25 second skill check. The loader therefore scopes these
    /// values to a declared product without mutating global native state.
    /// </summary>
    public static class RuntimeUnitAiBehaviorPolicy
    {
        public const int MaxPerUnitCap = 32;
        public const float NativeSkillActivationCheckInterval = 0.25f;
        public const float MaxSkillActivationCheckInterval = 60.0f;

        public static bool AllowsProduction(
            int livingCount,
            int queuedCount,
            bool isCombatUnit,
            int unitCap,
            int combatUnitCap)
        {
            int totalCount = Math.Max(0, livingCount) + Math.Max(0, queuedCount);
            int effectiveCap = EffectiveCap(isCombatUnit, unitCap, combatUnitCap);
            return effectiveCap <= 0 || totalCount < effectiveCap;
        }

        public static int EffectiveCap(bool isCombatUnit, int unitCap, int combatUnitCap)
        {
            int effectiveCap = unitCap > 0 ? unitCap : 0;
            if (isCombatUnit && combatUnitCap > 0 &&
                (effectiveCap == 0 || combatUnitCap < effectiveCap))
            {
                effectiveCap = combatUnitCap;
            }

            return effectiveCap;
        }

        public static float ResolveSkillActivationInterval(float configuredInterval, float fallbackInterval)
        {
            if (IsValidInterval(configuredInterval))
            {
                return configuredInterval;
            }

            if (IsValidInterval(fallbackInterval))
            {
                return fallbackInterval;
            }

            return NativeSkillActivationCheckInterval;
        }

        public static bool ShouldRunAiSkillCheck(float now, float lastCheck, float configuredInterval)
        {
            if (float.IsNaN(now) || float.IsInfinity(now) ||
                float.IsNaN(lastCheck) || float.IsInfinity(lastCheck))
            {
                return false;
            }

            float interval = ResolveSkillActivationInterval(
                configuredInterval,
                NativeSkillActivationCheckInterval);
            return now - lastCheck >= interval;
        }

        public static bool ShouldForceNativeAiSkillCheck(float now, float lastCheck, float configuredInterval)
        {
            if (!IsConfiguredInterval(configuredInterval) ||
                configuredInterval >= NativeSkillActivationCheckInterval ||
                float.IsNaN(now) || float.IsInfinity(now) ||
                float.IsNaN(lastCheck) || float.IsInfinity(lastCheck))
            {
                return false;
            }

            float elapsed = now - lastCheck;
            return elapsed >= configuredInterval && elapsed < NativeSkillActivationCheckInterval;
        }

        public static bool ShouldDelayNativeAiSkillCheck(float now, float lastCheck, float configuredInterval)
        {
            return IsConfiguredInterval(configuredInterval) &&
                configuredInterval > NativeSkillActivationCheckInterval &&
                !float.IsNaN(now) && !float.IsInfinity(now) &&
                !float.IsNaN(lastCheck) && !float.IsInfinity(lastCheck) &&
                Math.Abs(now - lastCheck) <= 0.01f;
        }

        public static float DelayedNativeAiSkillCheckTimestamp(float lastCheck, float configuredInterval)
        {
            if (!IsConfiguredInterval(configuredInterval) ||
                configuredInterval <= NativeSkillActivationCheckInterval)
            {
                return lastCheck;
            }

            return lastCheck + configuredInterval - NativeSkillActivationCheckInterval;
        }

        private static bool IsConfiguredInterval(float interval)
        {
            return !float.IsNaN(interval) && !float.IsInfinity(interval) &&
                interval > 0.0f && interval <= MaxSkillActivationCheckInterval;
        }

        private static bool IsValidInterval(float interval)
        {
            return IsConfiguredInterval(interval);
        }
    }
}
