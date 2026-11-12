namespace RogueCommandExpansionLoader.Runtime.Visual
{
    /// <summary>
    /// Pure policy that limits runtime turret aiming to hostile targets
    /// inside the native weapon range.
    /// </summary>
    public static class TurretAimPolicy
    {
        private const float WorldUnitsPerCell = 10f;

        public static bool IsInWeaponRange(float worldDistance, float weaponRangeCells, float? minRangeCells)
        {
            if (float.IsNaN(worldDistance) || float.IsInfinity(worldDistance) || worldDistance < 0f)
            {
                return false;
            }
            if (float.IsNaN(weaponRangeCells) || float.IsInfinity(weaponRangeCells) || weaponRangeCells <= 0f)
            {
                return false;
            }

            float worldRange = weaponRangeCells * WorldUnitsPerCell;
            if (worldDistance > worldRange)
            {
                return false;
            }

            if (minRangeCells.HasValue)
            {
                float minRange = minRangeCells.Value;
                if (!float.IsNaN(minRange) && !float.IsInfinity(minRange) && minRange > 0f)
                {
                    float worldMinRange = minRange * WorldUnitsPerCell;
                    if (worldDistance < worldMinRange)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
