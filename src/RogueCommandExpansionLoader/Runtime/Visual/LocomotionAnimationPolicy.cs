using System;

namespace RogueCommandExpansionLoader.Runtime.Visual
{
    /// <summary>
    /// Pure locomotion-state policy used by the visual Animator bridge.
    /// A false result keeps the stationary clip (normally Action_Idle) active;
    /// a true result allows the locomotion clip (Action_Walk/Action_Drive).
    /// </summary>
    public static class LocomotionAnimationPolicy
    {
        private const float MinimumDeltaTime = 1e-6f;
        private const float PositionDeltaSquaredThreshold = 1e-8f;
        private const float VelocitySquaredThreshold = 1e-6f;
        private const float MovementReleaseGraceSeconds = 0.12f;

        public const string SpeedMultiplierParameter = "RCL_SpeedMultiplier";
        public const float DefaultReferenceSpeed = 3.5f;
        public const float MinimumSpeedMultiplier = 0.65f;
        public const float MaximumSpeedMultiplier = 1.45f;

        public static float EvaluateSpeedMultiplier(
            float velocitySquared,
            float referenceSpeed = DefaultReferenceSpeed,
            float boostFactor = 1.0f)
        {
            if (float.IsNaN(velocitySquared) || float.IsInfinity(velocitySquared) || velocitySquared <= VelocitySquaredThreshold)
            {
                return 1.0f;
            }

            if (float.IsNaN(referenceSpeed) || float.IsInfinity(referenceSpeed) || referenceSpeed <= 0.01f)
            {
                referenceSpeed = DefaultReferenceSpeed;
            }

            if (float.IsNaN(boostFactor) || float.IsInfinity(boostFactor) || boostFactor <= 0.1f)
            {
                boostFactor = 1.0f;
            }

            float speed = (float)Math.Sqrt(velocitySquared);
            float ratio = (speed / referenceSpeed) * boostFactor;
            if (ratio < MinimumSpeedMultiplier)
            {
                return MinimumSpeedMultiplier;
            }
            if (ratio > MaximumSpeedMultiplier)
            {
                return MaximumSpeedMultiplier;
            }
            return ratio;
        }

        public static bool IsMoving(
            float positionDeltaSquared,
            float deltaTime,
            float velocitySquared)
        {
            if (float.IsNaN(positionDeltaSquared) || float.IsInfinity(positionDeltaSquared) ||
                float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ||
                float.IsNaN(velocitySquared) || float.IsInfinity(velocitySquared) ||
                deltaTime <= MinimumDeltaTime)
            {
                return false;
            }

            return positionDeltaSquared > PositionDeltaSquaredThreshold ||
                   velocitySquared > VelocitySquaredThreshold;
        }

        public static bool EvaluateWithMovementGrace(
            float positionDeltaSquared,
            float deltaTime,
            float velocitySquared,
            ref float movementHoldRemainingSeconds)
        {
            if (float.IsNaN(movementHoldRemainingSeconds) ||
                float.IsInfinity(movementHoldRemainingSeconds) ||
                movementHoldRemainingSeconds < 0f)
            {
                movementHoldRemainingSeconds = 0f;
            }

            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= MinimumDeltaTime)
            {
                movementHoldRemainingSeconds = 0f;
                return false;
            }

            if (IsMoving(positionDeltaSquared, deltaTime, velocitySquared))
            {
                movementHoldRemainingSeconds = MovementReleaseGraceSeconds;
                return true;
            }

            movementHoldRemainingSeconds = Math.Max(0f, movementHoldRemainingSeconds - deltaTime);
            return movementHoldRemainingSeconds > 0f;
        }
    }
}
