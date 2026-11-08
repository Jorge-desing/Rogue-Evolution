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
            IReadOnlyList<AiIntentProposal> proposals)
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

            int conflictsResolved = 0;
            var bestByUnit = new Dictionary<int, AiIntentProposal>();

            // Step 1: Resolve intra-tick collisions per unit (highest utility wins, category tie-breaker)
            for (int i = 0; i < proposals.Count; i++)
            {
                AiIntentProposal candidate = proposals[i];
                if (candidate.UnitId < 0 || candidate.UtilityScore <= 0f)
                {
                    continue;
                }

                if (bestByUnit.TryGetValue(candidate.UnitId, out AiIntentProposal existing))
                {
                    conflictsResolved++;
                    if (IsBetterProposal(candidate, existing))
                    {
                        bestByUnit[candidate.UnitId] = candidate;
                    }
                }
                else
                {
                    bestByUnit.Add(candidate.UnitId, candidate);
                }
            }

            // Step 2: Filter against active leases (lockout windows and hysteresis)
            int lockoutBlocked = 0;
            int hysteresisBlocked = 0;
            var eligibleWinners = new List<AiIntentProposal>(bestByUnit.Count);

            foreach (KeyValuePair<int, AiIntentProposal> kvp in bestByUnit)
            {
                int unitId = kvp.Key;
                AiIntentProposal winner = kvp.Value;

                if (state.TryGetLease(unitId, out AiCommandArbiterState.UnitLease activeLease))
                {
                    bool lockoutActive = safeNow < activeLease.LockoutExpiresAt;
                    if (lockoutActive && winner.Category < activeLease.Category)
                    {
                        lockoutBlocked++;
                        continue;
                    }

                    // Apply retarget hysteresis when in the same or lower priority tier
                    if (winner.Category <= activeLease.Category &&
                        winner.TargetEntityId != activeLease.TargetEntityId)
                    {
                        float requiredScore = activeLease.LastApprovedScore * safeHysteresis;
                        if (winner.UtilityScore <= requiredScore)
                        {
                            hysteresisBlocked++;
                            continue;
                        }
                    }
                }

                eligibleWinners.Add(winner);
            }

            // Step 3: Sort eligible winners by descending utility score (then category, then unitId for determinism)
            eligibleWinners.Sort((a, b) =>
            {
                int scoreCmp = b.UtilityScore.CompareTo(a.UtilityScore);
                if (scoreCmp != 0)
                {
                    return scoreCmp;
                }

                int catCmp = ((int)b.Category).CompareTo((int)a.Category);
                if (catCmp != 0)
                {
                    return catCmp;
                }

                return a.UnitId.CompareTo(b.UnitId);
            });

            // Step 4: Enforce per-tick dispatch budget and commit leases
            var approved = new List<AiIntentProposal>(Math.Min(budget, eligibleWinners.Count));
            int budgetOmitted = 0;

            for (int i = 0; i < eligibleWinners.Count; i++)
            {
                AiIntentProposal winner = eligibleWinners[i];
                if (approved.Count < budget)
                {
                    approved.Add(winner);
                    state.SetLease(winner.UnitId, new AiCommandArbiterState.UnitLease
                    {
                        ModuleId = winner.ModuleId,
                        CommandTag = winner.CommandTag,
                        TargetEntityId = winner.TargetEntityId,
                        Category = winner.Category,
                        LastApprovedScore = winner.UtilityScore,
                        LockoutExpiresAt = safeNow + winner.LockoutDurationSeconds
                    });
                }
                else
                {
                    budgetOmitted++;
                }
            }

            return new AiArbitrationResult(
                budgetOmittedCount: budgetOmitted,
                conflictsResolvedCount: conflictsResolved,
                lockoutBlockedCount: lockoutBlocked,
                hysteresisBlockedCount: hysteresisBlocked,
                approvedOrders: approved.AsReadOnly());
        }

        private static bool IsBetterProposal(AiIntentProposal candidate, AiIntentProposal current)
        {
            if (Math.Abs(candidate.UtilityScore - current.UtilityScore) > 0.0001f)
            {
                return candidate.UtilityScore > current.UtilityScore;
            }

            return (int)candidate.Category > (int)current.Category;
        }
    }
}
