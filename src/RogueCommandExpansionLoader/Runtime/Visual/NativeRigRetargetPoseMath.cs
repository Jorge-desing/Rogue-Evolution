using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime.Visual
{
    /// <summary>
    /// Copies a source transform's local pose delta onto a target transform's own bind pose.
    /// Scale is deliberately excluded so target mesh proportions remain under the authored visual.
    /// </summary>
    public static class NativeRigRetargetPoseMath
    {
        public static Vector3 RetargetLocalPosition(
            Vector3 sourceRestPosition,
            Vector3 sourceCurrentPosition,
            Vector3 targetRestPosition,
            float sourceToTargetPositionScale)
        {
            return targetRestPosition +
                (sourceCurrentPosition - sourceRestPosition) * sourceToTargetPositionScale;
        }

        public static Quaternion RetargetLocalRotation(
            Quaternion sourceRestRotation,
            Quaternion sourceCurrentRotation,
            Quaternion targetRestRotation)
        {
            Quaternion sourceDelta = Multiply(Inverse(sourceRestRotation), sourceCurrentRotation);
            return Multiply(targetRestRotation, sourceDelta);
        }

        private static Quaternion Inverse(Quaternion value)
        {
            float normSquared = value.x * value.x + value.y * value.y +
                value.z * value.z + value.w * value.w;
            if (normSquared <= 1e-12f)
            {
                return new Quaternion(0f, 0f, 0f, 1f);
            }

            float inverseNorm = 1f / normSquared;
            return new Quaternion(
                -value.x * inverseNorm,
                -value.y * inverseNorm,
                -value.z * inverseNorm,
                value.w * inverseNorm);
        }

        private static Quaternion Multiply(Quaternion left, Quaternion right)
        {
            return new Quaternion(
                left.w * right.x + left.x * right.w + left.y * right.z - left.z * right.y,
                left.w * right.y - left.x * right.z + left.y * right.w + left.z * right.x,
                left.w * right.z + left.x * right.y - left.y * right.x + left.z * right.w,
                left.w * right.w - left.x * right.x - left.y * right.y - left.z * right.z);
        }
    }
}
