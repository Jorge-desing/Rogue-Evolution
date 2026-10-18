using System;
using System.Collections.Generic;
using System.Text;

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
        private readonly Dictionary<string, string> _factoryByProductId;
        private readonly Dictionary<string, SortedSet<string>> _productsByFactoryId;

        public BlueprintRewardFactoryScope(
            IEnumerable<string> loaderFactoryIds,
            IEnumerable<string> stageFactoryIds,
            IDictionary<string, string> factoryByProductId = null,
            int activeStageIndex = -1,
            string activeStageId = null)
        {
            _loaderFactoryIds = CopyIds(loaderFactoryIds);
            _stageFactoryIds = CopyIds(stageFactoryIds);
            _factoryByProductId = new Dictionary<string, string>(StringComparer.Ordinal);
            _productsByFactoryId = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            if (factoryByProductId != null)
            {
                foreach (KeyValuePair<string, string> pair in factoryByProductId)
                {
                    if (!String.IsNullOrWhiteSpace(pair.Key) && IsLoaderFactory(pair.Value))
                    {
                        _factoryByProductId[pair.Key] = pair.Value;
                        SortedSet<string> products;
                        if (!_productsByFactoryId.TryGetValue(pair.Value, out products))
                        {
                            products = new SortedSet<string>(StringComparer.Ordinal);
                            _productsByFactoryId.Add(pair.Value, products);
                        }

                        products.Add(pair.Key);
                    }
                }
            }

            ActiveStageIndex = activeStageIndex;
            ActiveStageId = activeStageId;
        }

        public int ActiveStageIndex { get; private set; }

        public string ActiveStageId { get; private set; }

        public bool IsLoaderFactory(string factoryId)
        {
            return !String.IsNullOrEmpty(factoryId) && _loaderFactoryIds.Contains(factoryId);
        }

        public bool IsStageFactory(string factoryId)
        {
            return IsLoaderFactory(factoryId) && _stageFactoryIds.Contains(factoryId);
        }

        public bool IsLoaderProduct(string productId)
        {
            return !String.IsNullOrEmpty(productId) && _factoryByProductId.ContainsKey(productId);
        }

        public bool TryGetProductIds(string factoryId, out string productIds)
        {
            SortedSet<string> products;
            if (_productsByFactoryId.TryGetValue(factoryId ?? String.Empty, out products))
            {
                productIds = String.Join("|", products);
                return true;
            }

            productIds = null;
            return false;
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
            return new BlueprintRewardFactoryScope(
                _loaderFactoryIds, null, _factoryByProductId, ActiveStageIndex, ActiveStageId);
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

    /// <summary>
    /// Captures only loader-owned candidates and offered products during one synchronous reward
    /// generation call. It never changes the game's candidate list or seeded random selection.
    /// </summary>
    public sealed class BlueprintRewardObservation
    {
        private const int MaxRecordedIdsPerSet = 256;
        private const int MaxDisplayedIdsPerSet = 24;
        private const int MaxRecordedPoolQueries = 8;
        private const int MaxTraceCharacters = 4096;
        private readonly SortedSet<string> _globalCandidates = new SortedSet<string>(StringComparer.Ordinal);
        private readonly SortedSet<string> _stageCandidates = new SortedSet<string>(StringComparer.Ordinal);
        private readonly SortedSet<string> _offeredProducts = new SortedSet<string>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, SortedSet<string>> _poolCandidates =
            new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        private bool _globalFallbackObserved;
        private bool _poolCandidatesObserved;
        private int _omittedIdObservations;
        private int _omittedPoolQueries;

        public void RecordGlobalCandidates(
            IList<string> candidates,
            BlueprintRewardFactoryScope scope,
            bool afterStageScope)
        {
            if (candidates == null || scope == null)
            {
                return;
            }

            _globalFallbackObserved = true;
            SortedSet<string> destination = afterStageScope ? _stageCandidates : _globalCandidates;
            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                if (scope.IsLoaderFactory(candidate))
                {
                    AddBounded(destination, candidate);
                }
            }
        }

        public void RecordPoolCandidates(
            string poolId,
            IList<string> candidates,
            BlueprintRewardFactoryScope scope,
            int? currentExperienceLevel,
            bool excludeExclusivesFromOtherPools)
        {
            if (candidates == null || scope == null)
            {
                return;
            }

            _poolCandidatesObserved = true;
            string key = (String.IsNullOrWhiteSpace(poolId) ? "<unresolved>" : poolId) +
                "(xp=" + (currentExperienceLevel.HasValue
                    ? currentExperienceLevel.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "<null>") +
                ",excludeOther=" + excludeExclusivesFromOtherPools.ToString().ToLowerInvariant() + ")";
            SortedSet<string> destination;
            if (!_poolCandidates.TryGetValue(key, out destination))
            {
                if (_poolCandidates.Count >= MaxRecordedPoolQueries)
                {
                    _omittedPoolQueries++;
                    return;
                }

                destination = new SortedSet<string>(StringComparer.Ordinal);
                _poolCandidates.Add(key, destination);
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                if (scope.IsLoaderFactory(candidate))
                {
                    AddBounded(destination, candidate);
                }
            }
        }

        public void RecordOfferedProduct(string productId, BlueprintRewardFactoryScope scope)
        {
            if (scope != null && scope.IsLoaderProduct(productId))
            {
                AddBounded(_offeredProducts, productId);
            }
        }

        public string Format(BlueprintRewardFactoryScope scope)
        {
            if (scope == null)
            {
                return String.Empty;
            }

            var report = new StringBuilder();
            report.Append("stageIndex=").Append(scope.ActiveStageIndex);
            report.Append(" stageId=").Append(String.IsNullOrWhiteSpace(scope.ActiveStageId)
                ? "<native-or-unresolved>"
                : scope.ActiveStageId);
            report.Append(" offeredProducts=").Append(FormatSet(_offeredProducts, null));

            report.Append(" observedSources=");
            if (!_globalFallbackObserved && !_poolCandidatesObserved)
            {
                report.Append("unobserved");
            }
            else
            {
                if (_globalFallbackObserved)
                {
                    report.Append("global");
                }

                if (_globalFallbackObserved && _poolCandidatesObserved)
                {
                    report.Append(',');
                }

                if (_poolCandidatesObserved)
                {
                    report.Append("pool");
                }
            }

            if (_globalFallbackObserved)
            {
                report.Append(" globalFactories=").Append(FormatSet(_globalCandidates, scope));
                report.Append(" stageFactories=").Append(FormatSet(_stageCandidates, scope));
            }

            if (_poolCandidates.Count > 0)
            {
                report.Append(" pools={");
                bool firstPool = true;
                foreach (KeyValuePair<string, SortedSet<string>> pair in _poolCandidates)
                {
                    if (!firstPool)
                    {
                        report.Append(';');
                    }

                    firstPool = false;
                    report.Append(pair.Key).Append(':').Append(FormatSet(pair.Value, scope));
                }

                report.Append('}');
            }

            if (_omittedIdObservations > 0)
            {
                report.Append(" omittedIdObservations=").Append(_omittedIdObservations);
            }

            if (_omittedPoolQueries > 0)
            {
                report.Append(" omittedPoolQueries=").Append(_omittedPoolQueries);
            }

            if (report.Length > MaxTraceCharacters)
            {
                report.Length = MaxTraceCharacters - 3;
                report.Append("...");
            }

            return report.ToString();
        }

        private void AddBounded(SortedSet<string> destination, string value)
        {
            if (destination.Contains(value))
            {
                return;
            }

            if (destination.Count >= MaxRecordedIdsPerSet)
            {
                _omittedIdObservations++;
                return;
            }

            destination.Add(value);
        }

        private static string FormatSet(SortedSet<string> values, BlueprintRewardFactoryScope scope)
        {
            var result = new StringBuilder("[");
            int shown = 0;
            foreach (string value in values)
            {
                if (shown >= MaxDisplayedIdsPerSet)
                {
                    if (shown > 0)
                    {
                        result.Append(',');
                    }

                    result.Append("...+").Append(values.Count - shown);
                    break;
                }

                if (shown > 0)
                {
                    result.Append(',');
                }

                result.Append(value);
                string productId;
                if (scope != null && scope.TryGetProductIds(value, out productId))
                {
                    result.Append("->").Append(productId.IndexOf('|') >= 0 ? "{" + productId + "}" : productId);
                }

                shown++;
            }

            result.Append(']');
            return result.ToString();
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

        /// <summary>Restores stage identities from a serialized map when they can be resolved safely.</summary>
        void OnStageMapDeserialized(object stageMap, string json);

        /// <summary>Retries a pending saved-map restore after progression contributions are applied.</summary>
        void OnProgressionContentApplied();

        /// <summary>Adds the current map's stage identities to the game's serialized JSON.</summary>
        string AppendStageMapOrder(object stageMap, string json);
    }
}
