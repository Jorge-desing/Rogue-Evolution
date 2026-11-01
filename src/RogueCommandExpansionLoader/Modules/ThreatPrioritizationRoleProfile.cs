using System;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Pure targeting profile that specializes threat-prioritization behavior for a given allied
    /// (AI-controlled) unit class. This type is intentionally free of Unity / game assembly types
    /// so it stays unit-testable in isolation. It never mutates control or hostility flags.
    /// </summary>
    public readonly struct ThreatPrioritizationRoleProfile : IEquatable<ThreatPrioritizationRoleProfile>
    {
        /// <summary>
        /// Multiplier applied to the module's <c>engagementRadius</c> for this unit. Values above 1
        /// let ranged classes engage from further away; values below 1 make melee classes close in.
        /// </summary>
        public float EngagementRangeMultiplier { get; }

        /// <summary>
        /// Multiplier applied to the squared-distance threat penalty. Higher values make the unit
        /// strongly prefer the closest target; lower values let it keep firing on distant threats.
        /// </summary>
        public float DistanceWeight { get; }

        /// <summary>
        /// When false the unit is never retargeted onto enemy combat targets (e.g. a pure
        /// support / healer that should not be pulled into the fight by this module).
        /// </summary>
        public bool EngagesCombat { get; }

        private ThreatPrioritizationRoleProfile(float engagementRangeMultiplier, float distanceWeight, bool engagesCombat)
        {
            EngagementRangeMultiplier = engagementRangeMultiplier;
            DistanceWeight = distanceWeight;
            EngagesCombat = engagesCombat;
        }

        /// <summary>Generic / unclassified allied units keep the historical composite behavior.</summary>
        public static readonly ThreatPrioritizationRoleProfile Default =
            new ThreatPrioritizationRoleProfile(1.0f, 1.0f, true);

        /// <summary>Ranged damage dealers tolerate distance and extend their engagement envelope.</summary>
        public static readonly ThreatPrioritizationRoleProfile Sniper =
            new ThreatPrioritizationRoleProfile(1.4f, 0.6f, true);

        /// <summary>Mobile scouts keep moderate distance while staying in the fight.</summary>
        public static readonly ThreatPrioritizationRoleProfile Scout =
            new ThreatPrioritizationRoleProfile(1.2f, 0.8f, true);

        /// <summary>Mass-destruction units trade a little range for a heavy single-target focus.</summary>
        public static readonly ThreatPrioritizationRoleProfile MassDestruction =
            new ThreatPrioritizationRoleProfile(1.05f, 1.1f, true);

        /// <summary>Frontal attackers close to the fight and punish distant targets.</summary>
        public static readonly ThreatPrioritizationRoleProfile FrontalAttacker =
            new ThreatPrioritizationRoleProfile(0.85f, 1.6f, true);

        /// <summary>Tanks hold the line and draw fire from nearby threats.</summary>
        public static readonly ThreatPrioritizationRoleProfile Tank =
            new ThreatPrioritizationRoleProfile(0.9f, 1.3f, true);

        /// <summary>Melee units aggressively close the shortest distance to their target.</summary>
        public static readonly ThreatPrioritizationRoleProfile Melee =
            new ThreatPrioritizationRoleProfile(0.7f, 2.0f, true);

        /// <summary>Pure support / healer units are not pulled onto enemy combat targets.</summary>
        public static readonly ThreatPrioritizationRoleProfile Supporter =
            new ThreatPrioritizationRoleProfile(1.0f, 1.0f, false);

        public bool Equals(ThreatPrioritizationRoleProfile other)
        {
            return EngagementRangeMultiplier.Equals(other.EngagementRangeMultiplier) &&
                   DistanceWeight.Equals(other.DistanceWeight) &&
                   EngagesCombat == other.EngagesCombat;
        }

        public override bool Equals(object obj)
        {
            return obj is ThreatPrioritizationRoleProfile profile && Equals(profile);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(EngagementRangeMultiplier, DistanceWeight, EngagesCombat);
        }

        /// <summary>
        /// Resolves the targeting profile for an allied unit from its serialized role flags, checking
        /// the most behaviorally specific classes first. Unknown / mixed units fall back to
        /// <see cref="Default"/> so existing packs behave exactly as before.
        /// </summary>
        public static ThreatPrioritizationRoleProfile Resolve(UnitRole role)
        {
            if ((role & UnitRole.Supporter) != 0 || (role & UnitRole.Heal) != 0)
            {
                return Supporter;
            }
            if ((role & UnitRole.Sniper) != 0)
            {
                return Sniper;
            }
            if ((role & UnitRole.Scout) != 0)
            {
                return Scout;
            }
            if ((role & UnitRole.MassDestruction) != 0)
            {
                return MassDestruction;
            }
            if ((role & UnitRole.FrontalAttacker) != 0)
            {
                return FrontalAttacker;
            }
            if ((role & UnitRole.Tank) != 0)
            {
                return Tank;
            }
            if ((role & UnitRole.Melee) != 0)
            {
                return Melee;
            }
            return Default;
        }

        /// <summary>
        /// Resolves the targeting profile for an allied unit using the same <c>HasRole</c> query the
        /// rest of the module uses, so multi-role units are classified consistently with runtime checks.
        /// A null unit degrades to <see cref="Default"/>.
        /// </summary>
        public static ThreatPrioritizationRoleProfile ResolveFor(EntityController unit)
        {
            if (unit == null)
            {
                return Default;
            }
            if (unit.HasRole(UnitRole.Supporter) || unit.HasRole(UnitRole.Heal))
            {
                return Supporter;
            }
            if (unit.HasRole(UnitRole.Sniper))
            {
                return Sniper;
            }
            if (unit.HasRole(UnitRole.Scout))
            {
                return Scout;
            }
            if (unit.HasRole(UnitRole.MassDestruction))
            {
                return MassDestruction;
            }
            if (unit.HasRole(UnitRole.FrontalAttacker))
            {
                return FrontalAttacker;
            }
            if (unit.HasRole(UnitRole.Tank))
            {
                return Tank;
            }
            if (unit.HasRole(UnitRole.Melee))
            {
                return Melee;
            }
            return Default;
        }
    }
}
