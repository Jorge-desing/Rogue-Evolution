using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Content
{
    public readonly struct VisualRendererBounds
    {
        public readonly float CenterX;
        public readonly float CenterY;
        public readonly float CenterZ;
        public readonly float SizeX;
        public readonly float SizeY;
        public readonly float SizeZ;

        public VisualRendererBounds(float centerX, float centerY, float centerZ, float sizeX, float sizeY, float sizeZ)
        {
            CenterX = centerX;
            CenterY = centerY;
            CenterZ = centerZ;
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
        }
    }

    /// <summary>
    /// Calculates a uniform visual scale from measured renderer-space bounds.
    /// </summary>
    public static class VisualOverlayScaleCalibration
    {
        private const float MinimumUsableHalfExtent = 0.001f;
        private const float NativeVisualScaleFactor = 0.80f;
        private const float UnitFallbackHalfExtent = 0.85f;
        private const float BuildingFallbackHalfExtent = 1.60f;

        public static float CalculateCombinedHalfExtent(IEnumerable<VisualRendererBounds> rendererBounds)
        {
            if (rendererBounds == null)
            {
                return 0f;
            }

            bool hasBounds = false;
            float minX = 0f;
            float minY = 0f;
            float minZ = 0f;
            float maxX = 0f;
            float maxY = 0f;
            float maxZ = 0f;
            foreach (VisualRendererBounds bounds in rendererBounds)
            {
                if (!IsFinite(bounds.CenterX) || !IsFinite(bounds.CenterY) || !IsFinite(bounds.CenterZ) ||
                    !IsFinite(bounds.SizeX) || !IsFinite(bounds.SizeY) || !IsFinite(bounds.SizeZ) ||
                    bounds.SizeX < 0f || bounds.SizeY < 0f || bounds.SizeZ < 0f)
                {
                    continue;
                }

                float halfX = bounds.SizeX * 0.5f;
                float halfY = bounds.SizeY * 0.5f;
                float halfZ = bounds.SizeZ * 0.5f;
                float boundMinX = bounds.CenterX - halfX;
                float boundMinY = bounds.CenterY - halfY;
                float boundMinZ = bounds.CenterZ - halfZ;
                float boundMaxX = bounds.CenterX + halfX;
                float boundMaxY = bounds.CenterY + halfY;
                float boundMaxZ = bounds.CenterZ + halfZ;
                if (!IsFinite(boundMinX) || !IsFinite(boundMinY) || !IsFinite(boundMinZ) ||
                    !IsFinite(boundMaxX) || !IsFinite(boundMaxY) || !IsFinite(boundMaxZ))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    minX = boundMinX;
                    minY = boundMinY;
                    minZ = boundMinZ;
                    maxX = boundMaxX;
                    maxY = boundMaxY;
                    maxZ = boundMaxZ;
                    hasBounds = true;
                    continue;
                }

                minX = Math.Min(minX, boundMinX);
                minY = Math.Min(minY, boundMinY);
                minZ = Math.Min(minZ, boundMinZ);
                maxX = Math.Max(maxX, boundMaxX);
                maxY = Math.Max(maxY, boundMaxY);
                maxZ = Math.Max(maxZ, boundMaxZ);
            }

            if (!hasBounds)
            {
                return 0f;
            }

            float extent = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ)) * 0.5f;
            return IsFinite(extent) ? extent : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool IsUsableRendererExtent(float halfExtent)
        {
            return IsFinite(halfExtent) && halfExtent > MinimumUsableHalfExtent;
        }

        public static float CalculateTargetHalfExtent(float nativeHalfExtent, bool isBuilding)
        {
            return !float.IsNaN(nativeHalfExtent) && !float.IsInfinity(nativeHalfExtent) && nativeHalfExtent > 0.05f
                ? nativeHalfExtent * NativeVisualScaleFactor
                : (isBuilding ? BuildingFallbackHalfExtent : UnitFallbackHalfExtent);
        }

        public static float CalculateUniformScale(float modelHalfExtent, float targetHalfExtent)
        {
            if (float.IsNaN(modelHalfExtent) || float.IsInfinity(modelHalfExtent) ||
                modelHalfExtent <= MinimumUsableHalfExtent)
            {
                throw new ArgumentOutOfRangeException(nameof(modelHalfExtent),
                    "Model half-extent must be finite and greater than 0.001.");
            }

            if (float.IsNaN(targetHalfExtent) || float.IsInfinity(targetHalfExtent) || targetHalfExtent <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(targetHalfExtent),
                    "Target half-extent must be finite and greater than zero.");
            }

            return Math.Max(0.01f, Math.Min(100f, targetHalfExtent / modelHalfExtent));
        }

        public static bool IsExcludedRendererObjectName(string objectName)
        {
            return !String.IsNullOrEmpty(objectName) &&
                (objectName.IndexOf("_COLLIDER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 objectName.IndexOf("Antenna", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 objectName.StartsWith("Socket_", StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsExcludedRendererHierarchy(IEnumerable<string> objectNames)
        {
            if (objectNames == null)
            {
                return false;
            }

            foreach (string objectName in objectNames)
            {
                if (IsExcludedRendererObjectName(objectName))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
