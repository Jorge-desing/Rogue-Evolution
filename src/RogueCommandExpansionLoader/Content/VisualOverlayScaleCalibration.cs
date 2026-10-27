using System;

namespace RogueCommandExpansionLoader.Content
{
    /// <summary>
    /// Calculates a uniform visual scale from measured renderer-space bounds.
    /// </summary>
    public static class VisualOverlayScaleCalibration
    {
        public static float CalculateUniformScale(float modelHalfExtent, float targetHalfExtent)
        {
            if (float.IsNaN(modelHalfExtent) || float.IsInfinity(modelHalfExtent) || modelHalfExtent <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(modelHalfExtent),
                    "Model half-extent must be finite and greater than zero.");
            }

            if (float.IsNaN(targetHalfExtent) || float.IsInfinity(targetHalfExtent) || targetHalfExtent <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(targetHalfExtent),
                    "Target half-extent must be finite and greater than zero.");
            }

            return Math.Max(0.01f, Math.Min(100f, targetHalfExtent / modelHalfExtent));
        }
    }
}
