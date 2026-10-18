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
    }
}
