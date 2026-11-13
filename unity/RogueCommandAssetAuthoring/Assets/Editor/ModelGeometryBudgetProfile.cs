using System;
using System.IO;
using UnityEngine;

namespace RogueCommandExpansion.Authoring
{
    /// <summary>LOD authoring settings; no measured geometry limit is enforced.</summary>
    public static class ModelGeometryBudgetProfile
    {
        private const string PolicyPath = "content/source-art/canonical-visual-module-build-policy.json";
        private static readonly float[] DefaultTransitions = { 0.6f, 0.3f, 0.1f };

        [Serializable] private sealed class LodPolicy
        {
            public float[] lodScreenRelativeTransitionHeights;
        }

        public static float[] GetLodTransitions(string repositoryRoot, string assetId)
        {
            if (String.IsNullOrWhiteSpace(assetId))
                throw new ArgumentException("An asset ID is required.", nameof(assetId));
            string path = Path.Combine(Path.GetFullPath(repositoryRoot), PolicyPath);
            if (File.Exists(path))
            {
                try
                {
                    LodPolicy policy = JsonUtility.FromJson<LodPolicy>(File.ReadAllText(path));
                    float[] values = policy == null ? null : policy.lodScreenRelativeTransitionHeights;
                    if (values != null && values.Length == 3 && values[0] <= 1.0f &&
                        values[0] > values[1] && values[1] > values[2] && values[2] > 0.0f)
                        return (float[])values.Clone();
                }
                catch (Exception exception) when (exception is IOException || exception is ArgumentException)
                {
                    Debug.LogWarning("Could not read optional LOD settings: " + exception.Message);
                }
            }
            return (float[])DefaultTransitions.Clone();
        }

        // Legacy name retained for the canonical module builder. It selects an
        // honest uncalibrated status, not an evidence or performance verdict.
        public static bool IsUncalibratedCanonicalLevel2BuildAuthorized(string repositoryRoot)
        {
            return true;
        }

        public static float[] GetLodTransitions(string repositoryRoot, string assetId, bool unused)
        {
            return GetLodTransitions(repositoryRoot, assetId);
        }
    }
}
