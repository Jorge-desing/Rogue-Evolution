using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>Frame performance health verdict for a single live sample.</summary>
    public enum PerformanceVerdict
    {
        Healthy,
        Degraded,
        Critical
    }

    /// <summary>
    /// Pure budget classification for live frame samples. Non-finite or negative
    /// frame metrics fail closed as <see cref="PerformanceVerdict.Critical"/> so
    /// a broken/missing time source is never reported as healthy.
    /// </summary>
    public static class PerformanceBudgetPolicy
    {
        private const float HealthyMinimumFps = 50f;
        private const float DegradedMinimumFps = 20f;
        private const float DegradedMaximumFrameMs = 50f;
        private const float CriticalMaximumGcMb = 256f;

        public static PerformanceVerdict Classify(float fps, float frameTimeMs, float gcMemoryMb)
        {
            (PerformanceVerdict verdict, string reason) = Evaluate(fps, frameTimeMs, gcMemoryMb);
            return verdict;
        }

        public static (PerformanceVerdict Verdict, string Reason) Evaluate(
            float fps, float frameTimeMs, float gcMemoryMb)
        {
            if (!IsFiniteNonNegative(fps) || !IsFiniteNonNegative(frameTimeMs) ||
                !IsFiniteNonNegative(gcMemoryMb))
            {
                return (PerformanceVerdict.Critical, "non-finite or negative frame metrics");
            }

            if (fps < DegradedMinimumFps || frameTimeMs > DegradedMaximumFrameMs ||
                gcMemoryMb > CriticalMaximumGcMb)
            {
                return (PerformanceVerdict.Critical, "frame budget exceeded");
            }

            if (fps < HealthyMinimumFps || frameTimeMs > 20f || gcMemoryMb > 128f)
            {
                return (PerformanceVerdict.Degraded, "frame time above healthy budget");
            }

            return (PerformanceVerdict.Healthy, "frame budget met");
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }
    }

    /// <summary>
    /// Agent-readable live performance sample. Decoupled from Unity types so it
    /// can be serialized and unit-tested without a game runtime.
    /// </summary>
    public sealed class PerformanceCensus
    {
        public PerformanceCensus(
            int frameCount,
            float frameTimeMs,
            float fps,
            float timeScale,
            int targetFrameRate,
            float gcMemoryMb,
            float totalMemoryMb,
            string unityVersion,
            PerformanceVerdict verdict,
            string verdictReason,
            int omittedSampleCount,
            float averageFrameTimeMs,
            IReadOnlyList<string> recommendations)
        {
            FrameCount = frameCount;
            FrameTimeMs = frameTimeMs;
            Fps = fps;
            TimeScale = timeScale;
            TargetFrameRate = targetFrameRate;
            GcMemoryMb = gcMemoryMb;
            TotalMemoryMb = totalMemoryMb;
            UnityVersion = unityVersion ?? string.Empty;
            Verdict = verdict;
            VerdictReason = verdictReason ?? string.Empty;
            OmittedSampleCount = omittedSampleCount;
            AverageFrameTimeMs = averageFrameTimeMs;
            Recommendations = recommendations ?? new List<string>().AsReadOnly();
        }

        public int FrameCount { get; }
        public float FrameTimeMs { get; }
        public float Fps { get; }
        public float TimeScale { get; }
        public int TargetFrameRate { get; }
        public float GcMemoryMb { get; }
        public float TotalMemoryMb { get; }
        public string UnityVersion { get; }
        public PerformanceVerdict Verdict { get; }
        public string VerdictReason { get; }
        /// <summary>
        /// Loss/omission counter placed first in serialization so bounded
        /// diagnostic trace bounds never drop it behind unbounded detail lists.
        /// </summary>
        public int OmittedSampleCount { get; }
        public float AverageFrameTimeMs { get; }
        public IReadOnlyList<string> Recommendations { get; }

        public static PerformanceCensus Empty { get; } = new PerformanceCensus(
            0, 0f, 0f, 1f, -1, 0f, 0f, string.Empty,
            PerformanceVerdict.Critical, "performance unavailable", 0, 0f,
            new List<string>().AsReadOnly());
    }

    /// <summary>
    /// Samples the live Unity frame loop and managed memory into a
    /// <see cref="PerformanceCensus"/>. Fail-safe: any missing Unity time or
    /// memory source returns <see cref="PerformanceCensus.Empty"/> rather than
    /// throwing into the main thread.
    /// </summary>
    public static class RuntimePerformanceCensus
    {
        public static PerformanceCensus Current()
        {
            try
            {
                float deltaTime = 0f;
                int frameCount = 0;
                float timeScale = 1f;
                int targetFrameRate = -1;
                string unityVersion = string.Empty;
                try { deltaTime = Time.deltaTime; } catch { }
                try { frameCount = Time.frameCount; } catch { }
                try { timeScale = Time.timeScale; } catch { }
                try { targetFrameRate = Application.targetFrameRate; } catch { }
                try { unityVersion = Application.unityVersion ?? string.Empty; } catch { }

                float fps = deltaTime > 0f ? 1f / deltaTime : 0f;
                float frameTimeMs = deltaTime * 1000f;
                float totalMemoryMb = 0f;
                try { totalMemoryMb = GC.GetTotalMemory(false) / (1024f * 1024f); } catch { }

                (PerformanceVerdict verdict, string reason) =
                    PerformanceBudgetPolicy.Evaluate(fps, frameTimeMs, totalMemoryMb);

                var recommendations = new List<string>();
                if (verdict == PerformanceVerdict.Degraded)
                {
                    recommendations.Add("consider reducing particle effects");
                }
                else if (verdict == PerformanceVerdict.Critical)
                {
                    recommendations.Add("reduce unit counts and visual quality");
                }

                return new PerformanceCensus(
                    frameCount,
                    frameTimeMs,
                    fps,
                    timeScale,
                    targetFrameRate,
                    totalMemoryMb,
                    totalMemoryMb,
                    unityVersion,
                    verdict,
                    reason,
                    0,
                    frameTimeMs,
                    recommendations.AsReadOnly());
            }
            catch
            {
                return PerformanceCensus.Empty;
            }
        }
    }
}
