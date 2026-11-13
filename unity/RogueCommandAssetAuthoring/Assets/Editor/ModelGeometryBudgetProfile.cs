using System;
using System.IO;
using UnityEngine;

namespace RogueCommandExpansion.Authoring
{
    /// <summary>
    /// Reads the Python-authored performance calibration and provides the same
    /// screen-space LOD transitions to Unity prefab and bundle builders.
    /// </summary>
    public static class ModelGeometryBudgetProfile
    {
        private const string ProfileRelativePath = "content/source-art/model-budget-profile.json";
        private const string CanonicalBuildPolicyRelativePath = "content/source-art/canonical-visual-module-build-policy.json";
        private const string RequiredVerdict = "CALIBRATED_FOR_RECORDED_PC_AND_SCENE";

        [Serializable]
        private sealed class Profile
        {
            public int schemaVersion;
            public string calibrationStatus;
            public float targetFps;
            public BudgetReport budgetReport;
        }

        [Serializable]
        private sealed class CanonicalBuildPolicy
        {
            public int schemaVersion;
            public bool allowUncalibratedCanonicalLevel2;
            public string authorizationStatus;
            public float[] lodScreenRelativeTransitionHeights;
        }

        [Serializable]
        private sealed class BudgetReport
        {
            public string budgetVerdict;
            public SceneReport[] scenes;
        }

        [Serializable]
        private sealed class SceneReport
        {
            public string sceneId;
            public bool sceneBudgetPassed;
            public AssetReport[] assets;
        }

        [Serializable]
        private sealed class AssetReport
        {
            public string assetId;
            public string assetVerdict;
            public LodReport[] lods;
        }

        [Serializable]
        private sealed class LodReport
        {
            public string lod;
            public float screenRelativeTransitionHeight;
        }

        public static string RequireCalibrated(string repositoryRoot)
        {
            if (String.IsNullOrWhiteSpace(repositoryRoot))
            {
                throw new ArgumentException("A repository root is required for model-budget validation.", nameof(repositoryRoot));
            }

            string fullRoot = Path.GetFullPath(repositoryRoot);
            string profilePath = Path.GetFullPath(Path.Combine(fullRoot,
                ProfileRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(profilePath))
            {
                throw new FileNotFoundException("Package and prefab builds require a calibrated model-budget profile.", profilePath);
            }

            Profile profile = JsonUtility.FromJson<Profile>(File.ReadAllText(profilePath));
            if (profile == null || profile.schemaVersion != 1 ||
                !String.Equals(profile.calibrationStatus, "calibrated", StringComparison.Ordinal) ||
                profile.targetFps < 59.9f || profile.targetFps > 60.1f ||
                profile.budgetReport == null ||
                !String.Equals(profile.budgetReport.budgetVerdict, RequiredVerdict, StringComparison.Ordinal) ||
                profile.budgetReport.scenes == null || profile.budgetReport.scenes.Length == 0)
            {
                throw new InvalidDataException("Model-budget profile must be calibrated for the 60 FPS target before packaging.");
            }

            for (int sceneIndex = 0; sceneIndex < profile.budgetReport.scenes.Length; sceneIndex++)
            {
                SceneReport scene = profile.budgetReport.scenes[sceneIndex];
                if (scene == null || !scene.sceneBudgetPassed)
                {
                    string sceneId = scene == null ? "<null>" : scene.sceneId;
                    throw new InvalidDataException("Model-budget allocation exceeds the measured scene capacity: " + sceneId);
                }
                if (scene.assets == null || scene.assets.Length == 0)
                {
                    throw new InvalidDataException("Calibrated scene has no asset demands: " + scene.sceneId);
                }
                for (int assetIndex = 0; assetIndex < scene.assets.Length; assetIndex++)
                {
                    AssetReport asset = scene.assets[assetIndex];
                    if (asset == null || !String.Equals(asset.assetVerdict, "PASS", StringComparison.Ordinal))
                    {
                        string assetId = asset == null ? "<null>" : asset.assetId;
                        throw new InvalidDataException("Asset requires geometry reduction before packaging: " +
                            scene.sceneId + "/" + assetId);
                    }
                }
            }
            return profilePath;
        }

        public static float[] GetLodTransitions(string repositoryRoot, string assetId)
        {
            if (String.IsNullOrWhiteSpace(assetId))
            {
                throw new ArgumentException("A model-budget asset ID is required.", nameof(assetId));
            }

            string profilePath = RequireCalibrated(repositoryRoot);
            Profile profile = JsonUtility.FromJson<Profile>(File.ReadAllText(profilePath));
            float[] transitions = null;
            for (int sceneIndex = 0; sceneIndex < profile.budgetReport.scenes.Length; sceneIndex++)
            {
                AssetReport[] assets = profile.budgetReport.scenes[sceneIndex].assets;
                if (assets == null) continue;
                for (int assetIndex = 0; assetIndex < assets.Length; assetIndex++)
                {
                    AssetReport asset = assets[assetIndex];
                    if (asset == null || !String.Equals(asset.assetId, assetId, StringComparison.Ordinal)) continue;
                    if (asset.lods == null || asset.lods.Length != 3)
                    {
                        throw new InvalidDataException(assetId + " has no three-level LOD screen profile.");
                    }
                    float[] candidate = new float[3];
                    for (int lodIndex = 0; lodIndex < 3; lodIndex++)
                    {
                        string expected = "LOD" + lodIndex;
                        if (!String.Equals(asset.lods[lodIndex].lod, expected, StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(assetId + " has LOD entries in an invalid order.");
                        }
                        candidate[lodIndex] = asset.lods[lodIndex].screenRelativeTransitionHeight;
                        if (candidate[lodIndex] <= 0.0f || candidate[lodIndex] > 1.0f ||
                            (lodIndex > 0 && candidate[lodIndex - 1] <= candidate[lodIndex]))
                        {
                            throw new InvalidDataException(assetId + " has invalid screen-relative LOD transitions.");
                        }
                    }
                    if (transitions == null)
                    {
                        transitions = candidate;
                    }
                    else if (!ApproximatelyEqual(transitions, candidate))
                    {
                        throw new InvalidDataException(assetId + " declares conflicting LOD transitions across scenes.");
                    }
                }
            }

            if (transitions == null)
            {
                throw new InvalidDataException(assetId + " is missing from the calibrated geometry profile.");
            }
            return transitions;
        }

        public static bool IsUncalibratedCanonicalLevel2BuildAuthorized(string repositoryRoot)
        {
            string fullRoot = Path.GetFullPath(repositoryRoot);
            string profilePath = Path.GetFullPath(Path.Combine(fullRoot,
                ProfileRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            string policyPath = Path.GetFullPath(Path.Combine(fullRoot,
                CanonicalBuildPolicyRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(policyPath)) return false;

            CanonicalBuildPolicy policy;
            try
            {
                policy = JsonUtility.FromJson<CanonicalBuildPolicy>(File.ReadAllText(policyPath));
            }
            catch
            {
                return false;
            }
            if (policy == null || policy.schemaVersion != 1 || !policy.allowUncalibratedCanonicalLevel2 ||
                !String.Equals(policy.authorizationStatus, "user-authorized-until-revoked", StringComparison.Ordinal) ||
                !IsValidUncalibratedLodPolicy(policy.lodScreenRelativeTransitionHeights))
            {
                return false;
            }

            if (!File.Exists(profilePath)) return true;
            Profile profile;
            try
            {
                profile = JsonUtility.FromJson<Profile>(File.ReadAllText(profilePath));
            }
            catch
            {
                return true;
            }
            return profile == null || !String.Equals(profile.calibrationStatus, "calibrated", StringComparison.Ordinal);
        }

        public static float[] GetLodTransitions(
            string repositoryRoot,
            string assetId,
            bool allowUncalibratedCanonicalLevel2)
        {
            if (allowUncalibratedCanonicalLevel2 &&
                IsUncalibratedCanonicalLevel2BuildAuthorized(repositoryRoot))
            {
                string policyPath = Path.Combine(Path.GetFullPath(repositoryRoot),
                    CanonicalBuildPolicyRelativePath.Replace('/', Path.DirectorySeparatorChar));
                CanonicalBuildPolicy policy = JsonUtility.FromJson<CanonicalBuildPolicy>(File.ReadAllText(policyPath));
                return (float[])policy.lodScreenRelativeTransitionHeights.Clone();
            }
            return GetLodTransitions(repositoryRoot, assetId);
        }

        private static bool IsValidUncalibratedLodPolicy(float[] transitions)
        {
            return transitions != null && transitions.Length == 3 &&
                transitions[0] > transitions[1] && transitions[1] > transitions[2] &&
                transitions[0] <= 1.0f && transitions[2] > 0.0f;
        }

        private static bool ApproximatelyEqual(float[] left, float[] right)
        {
            for (int i = 0; i < left.Length; i++)
            {
                if (Math.Abs(left[i] - right[i]) > 0.0001f) return false;
            }
            return true;
        }
    }
}
