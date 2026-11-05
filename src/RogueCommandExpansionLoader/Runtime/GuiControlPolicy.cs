#nullable enable
using System;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure validation for GUI and playback bridge commands.
    /// Keeps NaN/infinity and out-of-range values out of Unity mutation paths.
    /// </summary>
    public static class GuiControlPolicy
    {
        public const int MinimumTab = 0;
        public const int MaximumTab = 7;
        public const float MinimumTimeScale = 0f;
        public const float MaximumTimeScale = 10f;

        public static bool TryClampTab(int requested, out int tab)
        {
            if (requested < MinimumTab)
            {
                tab = MinimumTab;
            }
            else if (requested > MaximumTab)
            {
                tab = MaximumTab;
            }
            else
            {
                tab = requested;
            }

            return true;
        }

        public static bool TryValidateTimeScale(float requested, out float timeScale)
        {
            if (float.IsNaN(requested) || float.IsInfinity(requested))
            {
                timeScale = 0f;
                return false;
            }

            timeScale = requested < MinimumTimeScale
                ? MinimumTimeScale
                : (requested > MaximumTimeScale ? MaximumTimeScale : requested);
            return true;
        }

        public static bool TryValidateCameraFloat(float value, out float result)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                result = 0f;
                return false;
            }

            result = value;
            return true;
        }
    }

    /// <summary>Primitive playback view for GUI state, decoupled from Unity types.</summary>
    public sealed class GuiPlaybackView
    {
        public GuiPlaybackView(
            float timeScale,
            bool hooksInstalled,
            float cameraX,
            float cameraZ,
            float fov)
        {
            TimeScale = timeScale;
            HooksInstalled = hooksInstalled;
            CameraX = cameraX;
            CameraZ = cameraZ;
            Fov = fov;
        }

        public float TimeScale { get; }
        public bool HooksInstalled { get; }
        public float CameraX { get; }
        public float CameraZ { get; }
        public float Fov { get; }
    }
}
