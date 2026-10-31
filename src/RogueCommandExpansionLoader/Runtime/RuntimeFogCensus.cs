using System;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry of Fog of War runtime status and update settings.
    /// Decoupled from Unity types to support clean-room MCP telemetry and offline validation.
    /// </summary>
    public sealed class FogStateCensus
    {
        public bool IsAvailable { get; }
        public bool IsActive { get; }
        public bool IsDisabled { get; }
        public float UpdateIntervalSeconds { get; }
        public bool HasBlendTexture { get; }

        public FogStateCensus(
            bool isAvailable,
            bool isActive,
            bool isDisabled,
            float updateIntervalSeconds,
            bool hasBlendTexture)
        {
            IsAvailable = isAvailable;
            IsActive = isActive;
            IsDisabled = isDisabled;
            UpdateIntervalSeconds = updateIntervalSeconds;
            HasBlendTexture = hasBlendTexture;
        }

        public static FogStateCensus Unavailable { get; } =
            new FogStateCensus(false, false, true, 0f, false);
    }

    /// <summary>
    /// Diagnostic collector that inspects the live <see cref="UpdateFogOfWar"/>
    /// runtime status across game runtime and test stubs without throwing.
    /// </summary>
    public static class RuntimeFogCensus
    {
        public static FogStateCensus Current()
        {
            try
            {
                object fogManager = ResolveFogManager();
                if (fogManager == null)
                {
                    return FogStateCensus.Unavailable;
                }

                Type type = fogManager.GetType();
                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

                bool isEnabled = ReadBool(fogManager, type, flags, "enabled", true);
                bool isDisabled = ReadBool(fogManager, type, flags, "disableFogOfWar", false);
                float interval = ReadFloat(fogManager, type, flags, "updateIntervalInSeconds", 0.5f);

                FieldInfo texField = type.GetField("_blendAmountTexture", flags);
                bool hasTex = texField?.GetValue(fogManager) != null;

                bool isActive = isEnabled && !isDisabled;

                return new FogStateCensus(true, isActive, isDisabled, interval, hasTex);
            }
            catch
            {
                return FogStateCensus.Unavailable;
            }
        }

        private static object ResolveFogManager()
        {
            try
            {
                Type type = typeof(UpdateFogOfWar);
                FieldInfo instanceField = type.GetField("_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object instance = instanceField?.GetValue(null);
                if (instance != null)
                {
                    return instance;
                }

                return UnityEngine.Object.FindObjectOfType<UpdateFogOfWar>();
            }
            catch
            {
                return null;
            }
        }

        private static bool ReadBool(object target, Type type, BindingFlags flags, string memberName, bool fallback)
        {
            try
            {
                PropertyInfo prop = type.GetProperty(memberName, flags);
                if (prop != null)
                {
                    return Convert.ToBoolean(prop.GetValue(target, null));
                }

                FieldInfo field = type.GetField(memberName, flags);
                if (field != null)
                {
                    return Convert.ToBoolean(field.GetValue(target));
                }
            }
            catch
            {
                // Degrade gracefully
            }

            return fallback;
        }

        private static float ReadFloat(object target, Type type, BindingFlags flags, string memberName, float fallback)
        {
            try
            {
                PropertyInfo prop = type.GetProperty(memberName, flags);
                if (prop != null)
                {
                    return Convert.ToSingle(prop.GetValue(target, null));
                }

                FieldInfo field = type.GetField(memberName, flags);
                if (field != null)
                {
                    return Convert.ToSingle(field.GetValue(target));
                }
            }
            catch
            {
                // Degrade gracefully
            }

            return fallback;
        }
    }
}
