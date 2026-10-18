using System;

namespace RogueCommandExpansionLoader.Runtime.Visual
{
    /// <summary>
    /// Resolves only documented gameplay sockets. Legacy model-prefixed names remain supported
    /// when the complete suffix is an exact catalog entry.
    /// </summary>
    public static class RogueCommandSocketCatalog
    {
        private static readonly string[] CanonicalSocketNames =
        {
            "Socket_Antenna",
            "Socket_ThrusterVector_L",
            "Socket_ThrusterVector_R",
            "Socket_MuzzlePrimary",
            "Socket_MuzzleSecondary",
            "Socket_MuzzleFlash",
            "Socket_TurretYaw",
            "Socket_TurretPivot",
            "Socket_TurretPitch",
            "Socket_RecoilPrimary",
            "Socket_SensorOptics",
            "Socket_SensorEye",
            "Socket_Exhaust_L",
            "Socket_Exhaust_R",
            "Socket_DamageSmoke",
            "Socket_DamageFire",
            "Socket_CenterOfMass",
            "Socket_GroundContact",
            "Socket_GroundCenter",
            "Socket_RadarMast",
            "Socket_RadarDish",
            "Socket_Deploy_Unit",
            "Socket_DeployUnit",
            "Socket_DeployRamp",
            "Socket_Deploy_Ramp",
            "Socket_Hardpoint_ERA",
            "Socket_Hardpoint_APS",
            "Socket_Hardpoint_Sponsons",
            "Socket_Hardpoint_FuelDrums"
        };

        public static bool TryResolveCanonicalSocketName(string objectName, out string canonicalName)
        {
            canonicalName = null;
            if (String.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            if (TryMatchCanonicalName(objectName, out canonicalName))
            {
                return true;
            }

            // Older exports used <ModelName>_Socket_<CanonicalSuffix>. Accept that legacy
            // spelling only when the full suffix matches a documented socket exactly.
            int legacyMarker = objectName.LastIndexOf("_Socket_", StringComparison.OrdinalIgnoreCase);
            if (legacyMarker <= 0)
            {
                return false;
            }

            string legacySocketName = objectName.Substring(legacyMarker + 1);
            return TryMatchCanonicalName(legacySocketName, out canonicalName);
        }

        private static bool TryMatchCanonicalName(string candidate, out string canonicalName)
        {
            for (int i = 0; i < CanonicalSocketNames.Length; i++)
            {
                if (String.Equals(candidate, CanonicalSocketNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    canonicalName = CanonicalSocketNames[i];
                    return true;
                }
            }

            canonicalName = null;
            return false;
        }
    }
}
