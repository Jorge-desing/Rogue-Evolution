using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Content
{
    internal sealed class GuaranteedModRewardSelectionCandidate
    {
        public GuaranteedModRewardSelectionCandidate(
            string productId,
            string productionFactoryId,
            int neededExperienceLevel,
            bool isAllowedAsStartingBlueprint,
            bool isAllowedForDemo,
            bool isProductionBuilding,
            bool isStandaloneBuilding,
            string[] roles)
        {
            ProductId = productId;
            ProductionFactoryId = productionFactoryId;
            NeededExperienceLevel = neededExperienceLevel;
            IsAllowedAsStartingBlueprint = isAllowedAsStartingBlueprint;
            IsAllowedForDemo = isAllowedForDemo;
            IsProductionBuilding = isProductionBuilding;
            IsStandaloneBuilding = isStandaloneBuilding;
            Roles = roles;
        }

        public string ProductId { get; private set; }
        public string ProductionFactoryId { get; private set; }
        public int NeededExperienceLevel { get; private set; }
        public bool IsAllowedAsStartingBlueprint { get; private set; }
        public bool IsAllowedForDemo { get; private set; }
        public bool IsProductionBuilding { get; private set; }
        public bool IsStandaloneBuilding { get; private set; }
        public string[] Roles { get; private set; }
    }

    internal static class GuaranteedModRewardSelectionPolicy
    {
        public static List<GuaranteedModRewardSelectionCandidate> GetEligibleCandidates(
            IEnumerable<GuaranteedModRewardSelectionCandidate> candidates,
            string poolId,
            bool isStartingSelection,
            int currentXpLevel,
            bool isDemo,
            ICollection<string> excludedProductIds)
        {
            var generalCandidates = new List<GuaranteedModRewardSelectionCandidate>();
            var matchingPoolCandidates = new List<GuaranteedModRewardSelectionCandidate>();

            if (candidates == null)
            {
                return generalCandidates;
            }

            foreach (GuaranteedModRewardSelectionCandidate candidate in candidates)
            {
                if (candidate == null || String.IsNullOrEmpty(candidate.ProductId) ||
                    (candidate.IsProductionBuilding && !candidate.IsStandaloneBuilding) ||
                    (isDemo && !candidate.IsAllowedForDemo))
                {
                    continue;
                }

                if (isStartingSelection)
                {
                    if (!candidate.IsAllowedAsStartingBlueprint && candidate.NeededExperienceLevel > 0)
                    {
                        continue;
                    }
                }
                else if (candidate.NeededExperienceLevel > currentXpLevel)
                {
                    continue;
                }

                if (excludedProductIds != null &&
                    (excludedProductIds.Contains(candidate.ProductId) ||
                     (!String.IsNullOrEmpty(candidate.ProductionFactoryId) &&
                      excludedProductIds.Contains(candidate.ProductionFactoryId))))
                {
                    continue;
                }

                generalCandidates.Add(candidate);
                if (MatchesPoolTheme(candidate.Roles, poolId))
                {
                    matchingPoolCandidates.Add(candidate);
                }
            }

            return matchingPoolCandidates.Count > 0 ? matchingPoolCandidates : generalCandidates;
        }

        private static bool MatchesPoolTheme(string[] roles, string poolId)
        {
            if (roles == null || roles.Length == 0 || String.IsNullOrEmpty(poolId))
            {
                return false;
            }

            for (int i = 0; i < roles.Length; i++)
            {
                string role = roles[i];
                if (!String.IsNullOrEmpty(role) &&
                    poolId.IndexOf(role, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
