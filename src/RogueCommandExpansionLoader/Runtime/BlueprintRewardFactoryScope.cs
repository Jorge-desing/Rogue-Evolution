using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Restricts only loader-owned factory IDs when the game builds a reward from its global
    /// blueprint fallback. Native IDs and unrelated mod IDs remain untouched.
    /// </summary>
    public sealed class BlueprintRewardFactoryScope
    {
        private readonly HashSet<string> _loaderFactoryIds;
        private readonly HashSet<string> _stageFactoryIds;

        public BlueprintRewardFactoryScope(
            IEnumerable<string> loaderFactoryIds,
            IEnumerable<string> stageFactoryIds)
        {
            _loaderFactoryIds = CopyIds(loaderFactoryIds);
            _stageFactoryIds = CopyIds(stageFactoryIds);
        }

        public void RemoveFactoriesOutsideStage(List<string> candidates)
        {
            if (candidates == null || _loaderFactoryIds.Count == 0)
            {
                return;
            }

            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                string id = candidates[i];
                if (_loaderFactoryIds.Contains(id) && !_stageFactoryIds.Contains(id))
                {
                    candidates.RemoveAt(i);
                }
            }
        }

        public void RemoveFactoriesOutsideStage(HashSet<string> candidates)
        {
            if (candidates == null || _loaderFactoryIds.Count == 0)
            {
                return;
            }

            var removals = new List<string>();
            foreach (string id in candidates)
            {
                if (_loaderFactoryIds.Contains(id) && !_stageFactoryIds.Contains(id))
                {
                    removals.Add(id);
                }
            }

            for (int i = 0; i < removals.Count; i++)
            {
                candidates.Remove(removals[i]);
            }
        }

        /// <summary>
        /// Returns a scope that excludes every loader-owned factory. Use it when the active stage
        /// cannot be resolved so an unexpected provider failure cannot expose global custom offers.
        /// </summary>
        public BlueprintRewardFactoryScope CreateFailClosedScope()
        {
            return new BlueprintRewardFactoryScope(_loaderFactoryIds, null);
        }

        private static HashSet<string> CopyIds(IEnumerable<string> ids)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (ids == null)
            {
                return result;
            }

            foreach (string id in ids)
            {
                if (!String.IsNullOrWhiteSpace(id))
                {
                    result.Add(id);
                }
            }

            return result;
        }
    }

    /// <summary>Provides a snapshot of loader-owned factories valid for the active reward stage.</summary>
    public interface IBlueprintRewardFactoryScopeProvider
    {
        bool TryGetBlueprintRewardFactoryScope(out BlueprintRewardFactoryScope scope);

        /// <summary>Returns a scope that removes every factory owned by this provider.</summary>
        BlueprintRewardFactoryScope CreateFailClosedBlueprintRewardFactoryScope();

        /// <summary>Captures the effective visible-stage order for a newly created game map.</summary>
        void OnStageMapCreated(object stageMap);
    }
}
