using System;
using System.Collections.Generic;
using System.Globalization;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>Windowed frame-timing health verdict.</summary>
    public enum FrameTimingVerdict
    {
        Smooth,
        MinorStutter,
        MajorStutter
    }

    /// <summary>
    /// Pure policy for classifying a window of frame timings. Non-finite or
    /// empty samples fail closed as <see cref="FrameTimingVerdict.MajorStutter"/>
    /// so a broken/missing time source is never reported as smooth.
    /// </summary>
    public static class FrameTimingPolicy
    {
        public const float DefaultStutterThresholdMs = 33.33f;
        public const float DefaultHitchThresholdMs = 100f;

        public static FrameTimingVerdict Classify(
            float averageFrameTimeMs, float p95FrameTimeMs, int stutterCount, int sampleCount)
        {
            (FrameTimingVerdict verdict, string reason) =
                Evaluate(averageFrameTimeMs, p95FrameTimeMs, stutterCount, sampleCount);
            return verdict;
        }

        public static (FrameTimingVerdict Verdict, string Reason) Evaluate(
            float averageFrameTimeMs, float p95FrameTimeMs, int stutterCount, int sampleCount)
        {
            if (sampleCount <= 0 ||
                !IsFiniteNonNegative(averageFrameTimeMs) ||
                !IsFiniteNonNegative(p95FrameTimeMs) ||
                stutterCount < 0)
            {
                return (FrameTimingVerdict.MajorStutter, "no or invalid frame samples");
            }

            float stutterRatio = sampleCount > 0 ? (float)stutterCount / sampleCount : 1f;

            if (p95FrameTimeMs > DefaultHitchThresholdMs || stutterRatio > 0.25f)
            {
                return (FrameTimingVerdict.MajorStutter, "frequent or severe frame hitches");
            }

            if (p95FrameTimeMs > DefaultStutterThresholdMs || stutterRatio > 0.05f ||
                averageFrameTimeMs > 20f)
            {
                return (FrameTimingVerdict.MinorStutter, "frame time above smooth budget");
            }

            return (FrameTimingVerdict.Smooth, "frame timing within smooth budget");
        }

        public static float ComputePercentile(IReadOnlyList<float> samples, float percentile)
        {
            if (samples == null || samples.Count == 0)
            {
                return 0f;
            }

            float clamped = percentile;
            if (clamped < 0f) clamped = 0f;
            if (clamped > 1f) clamped = 1f;

            var sorted = new List<float>(samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                if (IsFiniteNonNegative(samples[i]))
                {
                    sorted.Add(samples[i]);
                }
            }
            if (sorted.Count == 0)
            {
                return 0f;
            }
            sorted.Sort();

            int index = (int)Math.Ceiling(clamped * sorted.Count) - 1;
            if (index < 0) index = 0;
            if (index >= sorted.Count) index = sorted.Count - 1;
            return sorted[index];
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }
    }

    /// <summary>
    /// Agent-readable windowed frame-timing census. Decoupled from Unity types so
    /// it can be serialized and unit-tested without a game runtime.
    /// </summary>
    public sealed class FrameTimingCensus
    {
        public FrameTimingCensus(
            int sampleCount,
            float averageFrameTimeMs,
            float minFrameTimeMs,
            float maxFrameTimeMs,
            float p95FrameTimeMs,
            int stutterCount,
            int hitchCount,
            FrameTimingVerdict verdict,
            string verdictReason,
            int omittedSampleCount,
            IReadOnlyList<string> recommendations)
        {
            SampleCount = sampleCount < 0 ? 0 : sampleCount;
            AverageFrameTimeMs = averageFrameTimeMs;
            MinFrameTimeMs = minFrameTimeMs;
            MaxFrameTimeMs = maxFrameTimeMs;
            P95FrameTimeMs = p95FrameTimeMs;
            StutterCount = stutterCount < 0 ? 0 : stutterCount;
            HitchCount = hitchCount < 0 ? 0 : hitchCount;
            Verdict = verdict;
            VerdictReason = verdictReason ?? string.Empty;
            OmittedSampleCount = omittedSampleCount < 0 ? 0 : omittedSampleCount;
            Recommendations = recommendations ?? new List<string>().AsReadOnly();
        }

        public int SampleCount { get; }
        public float AverageFrameTimeMs { get; }
        public float MinFrameTimeMs { get; }
        public float MaxFrameTimeMs { get; }
        public float P95FrameTimeMs { get; }
        public int StutterCount { get; }
        public int HitchCount { get; }
        public FrameTimingVerdict Verdict { get; }
        public string VerdictReason { get; }
        /// <summary>Loss/omission counter placed first in serialization.</summary>
        public int OmittedSampleCount { get; }
        public IReadOnlyList<string> Recommendations { get; }

        public static FrameTimingCensus Empty { get; } = new FrameTimingCensus(
            0, 0f, 0f, 0f, 0f, 0, 0,
            FrameTimingVerdict.MajorStutter, "frame timing unavailable", 0,
            new List<string>().AsReadOnly());
    }

    /// <summary>
    /// Pure aggregation of a window of frame timings plus a fail-safe live sampler.
    /// </summary>
    public static class RuntimeFrameTimingCensus
    {
        public const int MaximumSamples = 256;

        public static FrameTimingCensus Aggregate(
            IReadOnlyList<float> frameTimes,
            float stutterThresholdMs = FrameTimingPolicy.DefaultStutterThresholdMs,
            float hitchThresholdMs = FrameTimingPolicy.DefaultHitchThresholdMs)
        {
            if (frameTimes == null || frameTimes.Count == 0)
            {
                return FrameTimingCensus.Empty;
            }

            int omitted = 0;
            var valid = new List<float>();
            for (int i = 0; i < frameTimes.Count; i++)
            {
                float value = frameTimes[i];
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                {
                    omitted++;
                    continue;
                }
                if (valid.Count < MaximumSamples)
                {
                    valid.Add(value);
                }
                else
                {
                    omitted++;
                }
            }

            if (valid.Count == 0)
            {
                return new FrameTimingCensus(
                    0, 0f, 0f, 0f, 0f, 0, 0,
                    FrameTimingVerdict.MajorStutter, "no valid frame samples", omitted,
                    new List<string>().AsReadOnly());
            }

            float sum = 0f;
            float min = valid[0];
            float max = valid[0];
            int stutterCount = 0;
            int hitchCount = 0;
            for (int i = 0; i < valid.Count; i++)
            {
                float value = valid[i];
                sum += value;
                if (value < min) min = value;
                if (value > max) max = value;
                if (value > stutterThresholdMs) stutterCount++;
                if (value > hitchThresholdMs) hitchCount++;
            }
            float average = sum / valid.Count;
            float p95 = FrameTimingPolicy.ComputePercentile(valid, 0.95f);

            (FrameTimingVerdict verdict, string reason) =
                FrameTimingPolicy.Evaluate(average, p95, stutterCount, valid.Count);

            var recommendations = new List<string>();
            if (verdict == FrameTimingVerdict.MinorStutter)
            {
                recommendations.Add("reduce non-essential visual effects");
            }
            else if (verdict == FrameTimingVerdict.MajorStutter)
            {
                recommendations.Add("reduce unit counts and disable heavy VFX");
            }

            return new FrameTimingCensus(
                valid.Count, average, min, max, p95, stutterCount, hitchCount,
                verdict, reason, omitted, recommendations.AsReadOnly());
        }

        public static FrameTimingCensus Current()
        {
            try
            {
                float deltaTime = 0f;
                try { deltaTime = UnityEngine.Time.deltaTime; } catch { }
                var samples = new List<float> { deltaTime };
                return Aggregate(samples);
            }
            catch
            {
                return FrameTimingCensus.Empty;
            }
        }
    }
}
