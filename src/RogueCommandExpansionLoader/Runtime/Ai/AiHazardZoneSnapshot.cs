using System;

namespace RogueCommandExpansionLoader.Runtime.Ai
{
    /// <summary>
    /// Pure, immutable snapshot of an active battlefield environmental hazard zone for HUB-D 2.0 perception.
    /// Free of Unity and Assembly-CSharp types so it can be evaluated in offline validation tests.
    /// </summary>
    public readonly struct AiHazardZoneSnapshot
    {
        public string ZoneId { get; }
        public string HazardType { get; }
        public float CenterX { get; }
        public float CenterZ { get; }
        public float Radius { get; }
        public float TickDamage { get; }
        public bool IsBeneficialToAi { get; }

        public AiHazardZoneSnapshot(
            string zoneId,
            string hazardType,
            float centerX,
            float centerZ,
            float radius,
            float tickDamage,
            bool isBeneficialToAi = false)
        {
            ZoneId = zoneId ?? string.Empty;
            HazardType = hazardType ?? "Firestorm";
            CenterX = SanitizeFloat(centerX, 0f);
            CenterZ = SanitizeFloat(centerZ, 0f);
            Radius = Math.Max(0.1f, SanitizeFloat(radius, 1f));
            TickDamage = Math.Max(0f, SanitizeFloat(tickDamage, 0f));
            IsBeneficialToAi = isBeneficialToAi;
        }

        public bool ContainsPoint(float x, float z, float margin = 0f)
        {
            float safeMargin = SanitizeFloat(margin, 0f);
            float effectiveRadius = Math.Max(0f, Radius + safeMargin);
            float dx = x - CenterX;
            float dz = z - CenterZ;
            return (dx * dx + dz * dz) <= (effectiveRadius * effectiveRadius);
        }

        /// <summary>
        /// Signed distance from (x, z) to the perimeter of the hazard zone.
        /// Negative values indicate the point is inside the hazard radius.
        /// </summary>
        public float DistanceToPerimeter(float x, float z)
        {
            float dx = x - CenterX;
            float dz = z - CenterZ;
            float distFromCenter = (float)Math.Sqrt(Math.Max(0f, dx * dx + dz * dz));
            return distFromCenter - Radius;
        }

        private static float SanitizeFloat(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }
    }
}
