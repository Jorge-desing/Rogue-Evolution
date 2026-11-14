using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime.Ai
{
    /// <summary>
    /// Canonical priority tier for HUB-D intent proposals. Higher values represent higher urgency
    /// that can preempt lower-tier active lockouts.
    /// </summary>
    public enum AiIntentCategory
    {
        SiegeAndFormation = 10,
        FlankingManeuver = 15,
        CombatTargeting = 20,
        BaseAndHarvesterDefense = 30,
        SupportLogistics = 35,
        TacticalSkill = 40,
        SurvivalKite = 50
    }

    /// <summary>
    /// Structured order proposal emitted by a module or tactical scorer for a single AI unit.
    /// </summary>
    public readonly struct AiIntentProposal
    {
        public string ModuleId { get; }
        public string CommandTag { get; }
        public int UnitId { get; }
        public int TargetEntityId { get; }
        public float DestX { get; }
        public float DestZ { get; }
        public AiIntentCategory Category { get; }
        public float UtilityScore { get; }
        public float LockoutDurationSeconds { get; }
        public bool RequiresEmptyQueue { get; }

        public AiIntentProposal(
            string moduleId,
            string commandTag,
            int unitId,
            int targetEntityId,
            float destX,
            float destZ,
            AiIntentCategory category,
            float utilityScore,
            float lockoutDurationSeconds,
            bool requiresEmptyQueue)
        {
            ModuleId = moduleId ?? string.Empty;
            CommandTag = commandTag ?? string.Empty;
            UnitId = unitId;
            TargetEntityId = targetEntityId;
            DestX = float.IsNaN(destX) || float.IsInfinity(destX) ? 0f : destX;
            DestZ = float.IsNaN(destZ) || float.IsInfinity(destZ) ? 0f : destZ;
            Category = category;
            UtilityScore = float.IsNaN(utilityScore) || float.IsInfinity(utilityScore) ? 0f : utilityScore;
            LockoutDurationSeconds = float.IsNaN(lockoutDurationSeconds) || float.IsInfinity(lockoutDurationSeconds)
                ? 0f
                : Math.Max(0f, lockoutDurationSeconds);
            RequiresEmptyQueue = requiresEmptyQueue;
        }
    }

    /// <summary>
    /// Tracks active per-unit order leases to prevent command thrashing across modules and frames.
    /// </summary>
    public sealed class AiCommandArbiterState
    {
        internal struct UnitLease
        {
            public string ModuleId;
            public string CommandTag;
            public int TargetEntityId;
            public AiIntentCategory Category;
            public float LastApprovedScore;
            public float LockoutExpiresAt;
        }

        private readonly Dictionary<int, UnitLease> _leases = new Dictionary<int, UnitLease>();

        public int ActiveLeaseCount
        {
            get { return _leases.Count; }
        }

        public void Clear()
        {
            _leases.Clear();
        }

        internal bool TryGetLease(int unitId, out UnitLease lease)
        {
            return _leases.TryGetValue(unitId, out lease);
        }

        internal void SetLease(int unitId, UnitLease lease)
        {
            _leases[unitId] = lease;
        }

        public bool ReleaseLease(int unitId)
        {
            return _leases.Remove(unitId);
        }

        public bool ReleaseLeaseIfTagDiffers(int unitId, string executedCommandTag)
        {
            if (!_leases.TryGetValue(unitId, out UnitLease activeLease))
            {
                return false;
            }

            if (string.Equals(activeLease.CommandTag, executedCommandTag ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            return _leases.Remove(unitId);
        }
    }

    /// <summary>
    /// Immutable result of a single arbitration pass. Diagnostic counters are placed first.
    /// </summary>
    public sealed class AiArbitrationResult
    {
        public int BudgetOmittedCount { get; }
        public int ConflictsResolvedCount { get; }
        public int LockoutBlockedCount { get; }
        public int HysteresisBlockedCount { get; }
        public IReadOnlyList<AiIntentProposal> ApprovedOrders { get; }

        public AiArbitrationResult(
            int budgetOmittedCount,
            int conflictsResolvedCount,
            int lockoutBlockedCount,
            int hysteresisBlockedCount,
            IReadOnlyList<AiIntentProposal> approvedOrders)
        {
            BudgetOmittedCount = Math.Max(0, budgetOmittedCount);
            ConflictsResolvedCount = Math.Max(0, conflictsResolvedCount);
            LockoutBlockedCount = Math.Max(0, lockoutBlockedCount);
            HysteresisBlockedCount = Math.Max(0, hysteresisBlockedCount);
            ApprovedOrders = approvedOrders ?? Array.Empty<AiIntentProposal>();
        }
    }

    /// <summary>
    /// Pure command arbitration policy that resolves competing module intents per unit,
    /// enforces lockout windows and retarget hysteresis, and caps global dispatches per tick.
    /// </summary>
    public static class AiCommandArbiterPolicy
    {
        public static AiArbitrationResult Arbitrate(
            AiCommandArbiterState state,
            float now,
            float retargetHysteresisMultiplier,
            int maxDispatchesPerTick,
            IReadOnlyList<AiIntentProposal> proposals,
            Func<AiIntentProposal, bool> tryDispatch = null,
            Func<AiIntentProposal, bool> keepsNativeOrder = null)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            float safeNow = float.IsNaN(now) || float.IsInfinity(now) ? 0f : now;
            float safeHysteresis = float.IsNaN(retargetHysteresisMultiplier) || float.IsInfinity(retargetHysteresisMultiplier)
                ? 1.15f
                : Math.Max(1.0f, retargetHysteresisMultiplier);
            int budget = Math.Max(0, maxDispatchesPerTick);

            if (proposals == null || proposals.Count == 0 || budget == 0)
            {
                return new AiArbitrationResult(0, 0, 0, 0, Array.Empty<AiIntentProposal>());
            }

            var ranked = new List<int>(proposals.Count);
            for (int i = 0; i < proposals.Count; i++)
            {
                AiIntentProposal candidate = proposals[i];
                if (candidate.UnitId >= 0 && candidate.UtilityScore > 0f)
                {
                    ranked.Add(i);
                }
            }

            ranked.Sort((left, right) =>
            {
                AiIntentProposal a = proposals[left];
                AiIntentProposal b = proposals[right];
                int comparison = b.UtilityScore.CompareTo(a.UtilityScore);
                if (comparison != 0) return comparison;
                comparison = b.Category.CompareTo(a.Category);
                if (comparison != 0) return comparison;
                comparison = a.UnitId.CompareTo(b.UnitId);
                return comparison != 0 ? comparison : left.CompareTo(right);
            });

            int conflictsResolved = 0;
            int lockoutBlocked = 0;
            int hysteresisBlocked = 0;
            var seenUnits = new HashSet<int>();
            var dispatchedUnits = new HashSet<int>();
            var omittedUnits = new HashSet<int>();
            var approved = new List<AiIntentProposal>(Math.Min(budget, ranked.Count));

            foreach (int index in ranked)
            {
                AiIntentProposal candidate = proposals[index];
                int unitId = candidate.UnitId;
                if (!seenUnits.Add(unitId)) conflictsResolved++;
                if (dispatchedUnits.Contains(unitId) || omittedUnits.Contains(unitId)) continue;

                if (state.TryGetLease(unitId, out AiCommandArbiterState.UnitLease activeLease))
                {
                    if (safeNow < activeLease.LockoutExpiresAt && candidate.Category < activeLease.Category)
                    {
                        lockoutBlocked++;
                        continue;
                    }

                    if (candidate.Category <= activeLease.Category &&
                        candidate.TargetEntityId != activeLease.TargetEntityId &&
                        candidate.UtilityScore <= activeLease.LastApprovedScore * safeHysteresis)
                    {
                        hysteresisBlocked++;
                        continue;
                    }
                }

                if (approved.Count >= budget)
                {
                    omittedUnits.Add(unitId);
                    continue;
                }

                if (tryDispatch != null && !tryDispatch(candidate)) continue;

                approved.Add(candidate);
                dispatchedUnits.Add(unitId);
                if (keepsNativeOrder != null && !keepsNativeOrder(candidate)) continue;

                state.SetLease(unitId, new AiCommandArbiterState.UnitLease
                {
                    ModuleId = candidate.ModuleId,
                    CommandTag = candidate.CommandTag,
                    TargetEntityId = candidate.TargetEntityId,
                    Category = candidate.Category,
                    LastApprovedScore = candidate.UtilityScore,
                    LockoutExpiresAt = safeNow + candidate.LockoutDurationSeconds
                });
            }

            return new AiArbitrationResult(
                budgetOmittedCount: omittedUnits.Count,
                conflictsResolvedCount: conflictsResolved,
                lockoutBlockedCount: lockoutBlocked,
                hysteresisBlockedCount: hysteresisBlocked,
                approvedOrders: approved.AsReadOnly());
        }

    }
}
