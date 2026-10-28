namespace RogueCommandExpansionLoader.Content
{
    internal static class VisualOverlayOrientation
    {
        private const float MinimumForwardSeparation = 0.01f;

        internal static float CalculateYawCorrectionDegrees(float muzzleForward, float rearForward)
        {
            if (System.Single.IsNaN(muzzleForward) || System.Single.IsInfinity(muzzleForward) ||
                System.Single.IsNaN(rearForward) || System.Single.IsInfinity(rearForward))
            {
                return 0f;
            }

            return muzzleForward - rearForward < -MinimumForwardSeparation ? 180f : 0f;
        }
    }
}
