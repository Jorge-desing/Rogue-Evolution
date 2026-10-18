using System;
using System.Collections;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Maps StageMap.CurrentStage to the same visible-stage sequence that LevelProgression uses,
    /// including its Demo filter and optional tutorial-stage substitution.
    /// </summary>
    public static class BlueprintRewardStageResolver
    {
        public static List<object> BuildVisibleStages(
            IList progressionStages,
            object tutorialStage,
            bool isDemo,
            bool useTutorialStage,
            Func<object, bool> isPartOfDemo)
        {
            var visibleStages = new List<object>();
            if (progressionStages == null)
            {
                return visibleStages;
            }

            for (int progressionIndex = 0; progressionIndex < progressionStages.Count; progressionIndex++)
            {
                object stage = progressionIndex == 0 && useTutorialStage
                    ? tutorialStage
                    : progressionStages[progressionIndex];
                if (stage == null || (isDemo && (isPartOfDemo == null || !isPartOfDemo(stage))))
                {
                    continue;
                }

                visibleStages.Add(stage);
            }

            return visibleStages;
        }

        public static object ResolveVisibleStage(IList visibleStages, int stageMapIndex)
        {
            if (visibleStages == null || stageMapIndex < 0 || stageMapIndex >= visibleStages.Count)
            {
                return null;
            }

            return visibleStages[stageMapIndex];
        }

        /// <summary>
        /// Resolves a persisted stage identity sequence against the current progression assets.
        /// Native duplicate asset names are interchangeable for loader eligibility because they
        /// never own loader factories; a custom identity must resolve to one runtime stage object.
        /// </summary>
        public static bool TryResolveSavedStageOrder(
            IList<string> savedStageTokens,
            IList candidateStages,
            Func<object, string> getIdentityToken,
            out List<object> resolvedStages)
        {
            resolvedStages = null;
            if (savedStageTokens == null || savedStageTokens.Count == 0 || candidateStages == null ||
                getIdentityToken == null)
            {
                return false;
            }

            var candidatesByToken = new Dictionary<string, List<object>>(StringComparer.Ordinal);
            for (int i = 0; i < candidateStages.Count; i++)
            {
                object candidate = candidateStages[i];
                if (candidate == null)
                {
                    continue;
                }

                string token = getIdentityToken(candidate);
                if (String.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                List<object> matches;
                if (!candidatesByToken.TryGetValue(token, out matches))
                {
                    matches = new List<object>();
                    candidatesByToken.Add(token, matches);
                }

                bool alreadyPresent = false;
                for (int matchIndex = 0; matchIndex < matches.Count; matchIndex++)
                {
                    if (System.Object.ReferenceEquals(matches[matchIndex], candidate))
                    {
                        alreadyPresent = true;
                        break;
                    }
                }

                if (!alreadyPresent)
                {
                    matches.Add(candidate);
                }
            }

            var resolved = new List<object>(savedStageTokens.Count);
            var seenCustomStageTokens = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < savedStageTokens.Count; i++)
            {
                string savedToken = savedStageTokens[i];
                List<object> matches;
                if (String.IsNullOrWhiteSpace(savedToken) || !candidatesByToken.TryGetValue(savedToken, out matches) ||
                    matches.Count == 0)
                {
                    return false;
                }

                if (savedToken.StartsWith("C_", StringComparison.Ordinal) && matches.Count != 1)
                {
                    return false;
                }

                if (savedToken.StartsWith("C_", StringComparison.Ordinal) &&
                    !seenCustomStageTokens.Add(savedToken))
                {
                    return false;
                }

                resolved.Add(matches[0]);
            }

            resolvedStages = resolved;
            return true;
        }
    }
}
