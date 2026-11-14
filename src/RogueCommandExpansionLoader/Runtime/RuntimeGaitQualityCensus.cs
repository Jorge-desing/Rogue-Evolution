using System;
using System.Collections.Generic;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Bounded live telemetry for layered unit animation, foot-ground contact,
    /// and main-camera viewport screen coverage without pixel/screenshot capture.
    /// </summary>
    public sealed class GaitQualityRow
    {
        public string EntityKeyId { get; }
        public string EntityId { get; }
        public bool IsLoaderOwned { get; }
        public bool GaitQualityPass { get; }
        public bool HasAnimator { get; }
        public bool AnimatorActive { get; }
        public int LayerCount { get; }
        public float AdditiveLayerWeight { get; }
        public bool IsMoving { get; }
        public float SpeedMultiplier { get; }
        public string ActiveClipName { get; }
        public int FootContactCount { get; }
        public float FootMinDeltaY { get; }
        public float FootMaxDeltaY { get; }
        public int GroundPenetrationCount { get; }
        public bool VisibleToMainCamera { get; }
        public float ScreenCoverageFraction { get; }
        public bool WorldBoundsValid { get; }
        public IReadOnlyList<string> Issues { get; }

        public GaitQualityRow(
            string entityKeyId,
            string entityId,
            bool isLoaderOwned,
            bool gaitQualityPass,
            bool hasAnimator,
            bool animatorActive,
            int layerCount,
            float additiveLayerWeight,
            bool isMoving,
            float speedMultiplier,
            string activeClipName,
            int footContactCount,
            float footMinDeltaY,
            float footMaxDeltaY,
            int groundPenetrationCount,
            bool visibleToMainCamera,
            float screenCoverageFraction,
            bool worldBoundsValid,
            IReadOnlyList<string> issues)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            EntityId = entityId ?? string.Empty;
            IsLoaderOwned = isLoaderOwned;
            GaitQualityPass = gaitQualityPass;
            HasAnimator = hasAnimator;
            AnimatorActive = animatorActive;
            LayerCount = Math.Max(0, layerCount);
            AdditiveLayerWeight = additiveLayerWeight;
            IsMoving = isMoving;
            SpeedMultiplier = speedMultiplier;
            ActiveClipName = activeClipName ?? string.Empty;
            FootContactCount = Math.Max(0, footContactCount);
            FootMinDeltaY = footMinDeltaY;
            FootMaxDeltaY = footMaxDeltaY;
            GroundPenetrationCount = Math.Max(0, groundPenetrationCount);
            VisibleToMainCamera = visibleToMainCamera;
            ScreenCoverageFraction = screenCoverageFraction;
            WorldBoundsValid = worldBoundsValid;
            Issues = issues ?? new List<string>().AsReadOnly();
        }
    }

    public sealed class GaitQualityCensus
    {
        public int OmittedRowCount { get; }
        public int LoaderOwnedUnitCount { get; }
        public int GaitQualityPassCount { get; }
        public int GaitQualityIssueCount { get; }
        public int GroundPenetrationUnitCount { get; }
        public IReadOnlyList<GaitQualityRow> Rows { get; }

        public GaitQualityCensus(
            IReadOnlyList<GaitQualityRow> rows,
            int omittedRowCount,
            int loaderOwnedUnitCount,
            int gaitQualityPassCount,
            int gaitQualityIssueCount,
            int groundPenetrationUnitCount)
        {
            Rows = rows ?? new List<GaitQualityRow>().AsReadOnly();
            OmittedRowCount = Math.Max(0, omittedRowCount);
            LoaderOwnedUnitCount = Math.Max(0, loaderOwnedUnitCount);
            GaitQualityPassCount = Math.Max(0, gaitQualityPassCount);
            GaitQualityIssueCount = Math.Max(0, gaitQualityIssueCount);
            GroundPenetrationUnitCount = Math.Max(0, groundPenetrationUnitCount);
        }
    }

    public static class RuntimeGaitQualityCensus
    {
        private const float GroundPenetrationThreshold = -0.05f;
        private const int MaxHierarchyNodes = 256;

        public static GaitQualityCensus Current(int maxRows = 64)
        {
            var rows = new List<GaitQualityRow>();
            int omitted = 0;
            int loaderOwned = 0;
            int passCount = 0;
            int issueCount = 0;
            int penetrationUnits = 0;

            try
            {
                Camera mainCam = Camera.main;
                List<EntityController> units = RuntimeVisualCensusSupport.CollectLivingUnits(maxRows, out omitted);
                for (int i = 0; i < units.Count; i++)
                {
                    EntityController unit = units[i];
                    try
                    {
                        GaitQualityRow row = CollectRow(unit, mainCam);
                        rows.Add(row);
                        if (row.IsLoaderOwned)
                        {
                            loaderOwned++;
                            if (row.GaitQualityPass) passCount++;
                            else issueCount++;
                        }
                        if (row.GroundPenetrationCount > 0)
                        {
                            penetrationUnits++;
                        }
                    }
                    catch
                    {
                        omitted++;
                    }
                }
            }
            catch
            {
                omitted++;
            }

            return new GaitQualityCensus(
                rows.AsReadOnly(),
                omitted,
                loaderOwned,
                passCount,
                issueCount,
                penetrationUnits);
        }

        private static GaitQualityRow CollectRow(EntityController unit, Camera mainCam)
        {
            string entityKeyId = unit.UniqueEntityId > 0 ? unit.UniqueEntityId.ToString() : (unit.EntityId ?? string.Empty);
            string entityId = unit.EntityId ?? string.Empty;
            bool isLoaderOwned = entityId.IndexOf(':') >= 0;
            var issues = new List<string>();

            Animator animator = unit.GetComponentInChildren<Animator>(true);
            bool hasAnimator = animator != null;
            bool animatorActive = hasAnimator && animator.isActiveAndEnabled;
            int layerCount = hasAnimator ? animator.layerCount : 0;
            float additiveWeight = (hasAnimator && layerCount > 1) ? animator.GetLayerWeight(1) : 0f;

            bool isMoving = false;
            float speedMult = 1.0f;
            string activeClip = string.Empty;
            if (hasAnimator)
            {
                AnimatorControllerParameter[] parameters = animator.parameters;
                for (int p = 0; p < parameters.Length; p++)
                {
                    if (parameters[p].name == "RCL_IsMoving" && parameters[p].type == AnimatorControllerParameterType.Bool)
                    {
                        isMoving = animator.GetBool("RCL_IsMoving");
                    }
                    else if (parameters[p].name == "RCL_SpeedMultiplier" && parameters[p].type == AnimatorControllerParameterType.Float)
                    {
                        speedMult = animator.GetFloat("RCL_SpeedMultiplier");
                    }
                }

                if (animatorActive)
                {
                    AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
                    if (clips != null && clips.Length > 0 && clips[0].clip != null)
                    {
                        activeClip = clips[0].clip.name;
                    }
                }
            }

            // Foot contact / ground penetration audit
            float baseY = unit.transform.position.y;
            int footCount = 0;
            int penetrationCount = 0;
            float minDeltaY = 0f;
            float maxDeltaY = 0f;
            bool hasFootSample = false;

            Transform[] transforms = unit.GetComponentsInChildren<Transform>(true);
            int limit = Math.Min(transforms.Length, MaxHierarchyNodes);
            for (int t = 0; t < limit; t++)
            {
                Transform tr = transforms[t];
                if (tr == null) continue;
                string name = tr.name;
                if (name.StartsWith("Socket_FootContact_", StringComparison.Ordinal) ||
                    name.StartsWith("Bone_Foot_", StringComparison.Ordinal))
                {
                    float deltaY = tr.position.y - baseY;
                    footCount++;
                    if (!hasFootSample)
                    {
                        minDeltaY = deltaY;
                        maxDeltaY = deltaY;
                        hasFootSample = true;
                    }
                    else
                    {
                        if (deltaY < minDeltaY) minDeltaY = deltaY;
                        if (deltaY > maxDeltaY) maxDeltaY = deltaY;
                    }
                    if (deltaY < GroundPenetrationThreshold)
                    {
                        penetrationCount++;
                    }
                }
            }

            // Renderer bounds and screen-space coverage
            Renderer[] renderers = unit.GetComponentsInChildren<Renderer>(false);
            bool hasBounds = false;
            Bounds combined = new Bounds(unit.transform.position, Vector3.zero);
            bool anyVisible = false;
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer rend = renderers[r];
                if (rend == null || !rend.enabled) continue;
                if (rend.isVisible) anyVisible = true;
                if (!hasBounds)
                {
                    combined = rend.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(rend.bounds);
                }
            }

            bool boundsValid = hasBounds &&
                !float.IsNaN(combined.size.x) && !float.IsInfinity(combined.size.x) &&
                combined.size.sqrMagnitude > 1e-6f;

            float screenCoverage = 0f;
            bool visibleToCam = anyVisible;
            if (boundsValid && mainCam != null && mainCam.isActiveAndEnabled)
            {
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(mainCam);
                bool inFrustum = GeometryUtility.TestPlanesAABB(planes, combined);
                if (inFrustum)
                {
                    visibleToCam = true;
                    Vector3 c = combined.center;
                    Vector3 e = combined.extents;
                    float minVx = 1f, maxVx = 0f, minVy = 1f, maxVy = 0f;
                    bool anyInFront = false;
                    for (int ix = -1; ix <= 1; ix += 2)
                    {
                        for (int iy = -1; iy <= 1; iy += 2)
                        {
                            for (int iz = -1; iz <= 1; iz += 2)
                            {
                                Vector3 pt = new Vector3(c.x + e.x * ix, c.y + e.y * iy, c.z + e.z * iz);
                                Vector3 vp = mainCam.WorldToViewportPoint(pt);
                                if (vp.z > 0f)
                                {
                                    anyInFront = true;
                                    if (vp.x < minVx) minVx = vp.x;
                                    if (vp.x > maxVx) maxVx = vp.x;
                                    if (vp.y < minVy) minVy = vp.y;
                                    if (vp.y > maxVy) maxVy = vp.y;
                                }
                            }
                        }
                    }
                    if (anyInFront)
                    {
                        float w = Mathf.Clamp01(maxVx) - Mathf.Clamp01(minVx);
                        float h = Mathf.Clamp01(maxVy) - Mathf.Clamp01(minVy);
                        screenCoverage = Mathf.Clamp01(w * h);
                    }
                }
            }

            if (isLoaderOwned)
            {
                if (!hasAnimator) issues.Add("MISSING_ANIMATOR");
                if (hasAnimator && layerCount < 2) issues.Add("MISSING_ADDITIVE_LAYER");
                if (speedMult < 0.5f || speedMult > 2.0f || float.IsNaN(speedMult)) issues.Add("INVALID_SPEED_MULTIPLIER");
                if (penetrationCount > 0) issues.Add("GROUND_PENETRATION");
                if (!boundsValid) issues.Add("INVALID_BOUNDS");
            }

            return new GaitQualityRow(
                entityKeyId,
                entityId,
                isLoaderOwned,
                issues.Count == 0,
                hasAnimator,
                animatorActive,
                layerCount,
                additiveWeight,
                isMoving,
                speedMult,
                activeClip,
                footCount,
                minDeltaY,
                maxDeltaY,
                penetrationCount,
                visibleToCam,
                screenCoverage,
                boundsValid,
                issues.AsReadOnly());
        }
    }
}
