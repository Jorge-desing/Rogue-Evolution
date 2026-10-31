using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry of audio mixer volume slider levels.
    /// Decoupled from Unity types to support clean-room MCP telemetry.
    /// </summary>
    public sealed class AudioCensus
    {
        public bool IsAudioAvailable { get; }
        public float MasterVolumeSlider { get; }
        public float MusicVolumeSlider { get; }
        public float GameplayVolumeSlider { get; }
        public float UiVolumeSlider { get; }

        public AudioCensus(
            bool isAudioAvailable,
            float masterVolumeSlider,
            float musicVolumeSlider,
            float gameplayVolumeSlider,
            float uiVolumeSlider)
        {
            IsAudioAvailable = isAudioAvailable;
            MasterVolumeSlider = masterVolumeSlider;
            MusicVolumeSlider = musicVolumeSlider;
            GameplayVolumeSlider = gameplayVolumeSlider;
            UiVolumeSlider = uiVolumeSlider;
        }

        public static AudioCensus Unavailable { get; } =
            new AudioCensus(false, 0f, 0f, 0f, 0f);
    }

    /// <summary>
    /// Diagnostic collector that inspects audio mixer volume slider levels via <see cref="AudioMixerHelper"/>.
    /// </summary>
    public static class RuntimeAudioCensus
    {
        private static readonly BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static AudioCensus Current()
        {
            try
            {
                AudioMixer mixer = ResolveAudioMixer();
                if (mixer == null)
                {
                    return AudioCensus.Unavailable;
                }

                float master = ReadSlider("GetMasterSliderValue", mixer);
                float music = ReadSlider("GetMusicSliderValue", mixer);
                float gameplay = ReadSlider("GetGameplaySliderValue", mixer);
                float ui = ReadSlider("GetUiSelectionSoundsSliderValue", mixer);

                return new AudioCensus(true, master, music, gameplay, ui);
            }
            catch
            {
                return AudioCensus.Unavailable;
            }
        }

        public static AudioMixer ResolveAudioMixer()
        {
            try
            {
                // Check FinishLevel
                Type flType = typeof(FinishLevel);
                PropertyInfo instProp = flType.GetProperty("Instance", Flags);
                object flInstance = instProp?.GetValue(null, null);
                if (flInstance == null)
                {
                    FieldInfo instanceF = flType.GetField("_instance", Flags);
                    flInstance = instanceF?.GetValue(null) ?? UnityEngine.Object.FindObjectOfType<FinishLevel>();
                }

                if (flInstance != null)
                {
                    FieldInfo mixerF = flType.GetField("audioMixer", Flags);
                    var mixer = mixerF?.GetValue(flInstance) as AudioMixer;
                    if (mixer != null) return mixer;

                    PropertyInfo mixerP = flType.GetProperty("audioMixer", Flags);
                    var mixerFromProp = mixerP?.GetValue(flInstance, null) as AudioMixer;
                    if (mixerFromProp != null) return mixerFromProp;
                }

                // Check other known components
                AudioMixer found = UnityEngine.Object.FindObjectOfType<AudioMixer>();
                if (found != null) return found;

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static float ReadSlider(string methodName, AudioMixer mixer)
        {
            try
            {
                Type type = typeof(AudioMixerHelper);
                MethodInfo m = type.GetMethod(methodName, Flags, null, new[] { typeof(AudioMixer) }, null);
                if (m != null)
                {
                    object res = m.Invoke(null, new object[] { mixer });
                    if (res != null) return Convert.ToSingle(res);
                }
            }
            catch { }
            return 1.0f;
        }
    }
}
