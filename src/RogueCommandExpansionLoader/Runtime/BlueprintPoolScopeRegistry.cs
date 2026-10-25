using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// A cloned BlueprintPool and the mod units that its owning stage may admit through the
    /// game's existing filter. Pool instances are compared by reference because cloned Unity
    /// ScriptableObjects can have identical serialized values.
    /// </summary>
    public sealed class BlueprintPoolScopeRegistration
    {
        private readonly HashSet<string> _unitIds;
        private readonly Dictionary<string, HashSet<string>> _unitIdsBySourcePool;
        private HashSet<string> _eligibleExclusiveUnitIds = new HashSet<string>(StringComparer.Ordinal);

        public BlueprintPoolScopeRegistration(
            string poolId,
            string stageId,
            object pool,
            IEnumerable<string> unitIds,
            bool blueprintsAreExclusive,
            string sourcePoolId = null)
        {
            if (String.IsNullOrWhiteSpace(poolId))
            {
                throw new ArgumentException("A scoped blueprint pool ID is required.", "poolId");
            }

            if (pool == null)
            {
                throw new ArgumentNullException("pool");
            }

            if (String.IsNullOrWhiteSpace(stageId))
            {
                throw new ArgumentException("A stage scope ID is required.", "stageId");
            }

            PoolId = poolId;
            SourcePoolId = sourcePoolId;
            ScopeId = stageId;
            Pool = pool;
            BlueprintsAreExclusive = blueprintsAreExclusive;
            _unitIds = new HashSet<string>(StringComparer.Ordinal);
            if (unitIds != null)
            {
                foreach (string unitId in unitIds)
                {
                    if (!String.IsNullOrWhiteSpace(unitId))
                    {
                        _unitIds.Add(unitId);
                    }
                }
            }

            _unitIdsBySourcePool = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (!String.IsNullOrEmpty(sourcePoolId))
            {
                _unitIdsBySourcePool[sourcePoolId] = new HashSet<string>(_unitIds, StringComparer.Ordinal);
            }
        }

        public string PoolId { get; private set; }

        public string SourcePoolId { get; private set; }

        public string ScopeId { get; private set; }

        public object Pool { get; private set; }

        public bool BlueprintsAreExclusive { get; private set; }

        public bool ContainsUnit(string unitId)
        {
            return !String.IsNullOrEmpty(unitId) && _unitIds.Contains(unitId);
        }

        public bool ContainsUnitInSourcePool(string sourcePoolId, string unitId)
        {
            HashSet<string> scopedIds;
            return !String.IsNullOrEmpty(sourcePoolId) && !String.IsNullOrEmpty(unitId) &&
                _unitIdsBySourcePool.TryGetValue(sourcePoolId, out scopedIds) && scopedIds.Contains(unitId);
        }

        internal void SetEligibleExclusiveUnitIds(IEnumerable<string> unitIds)
        {
            var eligible = new HashSet<string>(StringComparer.Ordinal);
            if (unitIds != null)
            {
                foreach (string unitId in unitIds)
                {
                    if (ContainsUnit(unitId))
                    {
                        eligible.Add(unitId);
                    }
                }
            }

            _eligibleExclusiveUnitIds = eligible;
        }

        public IReadOnlyList<string> UnitIds
        {
            get
            {
                var result = new string[_unitIds.Count];
                _unitIds.CopyTo(result);
                Array.Sort(result, StringComparer.Ordinal);
                return Array.AsReadOnly(result);
            }
        }

        internal void AddEligibleExclusiveUnitIds(HashSet<string> destination)
        {
            foreach (string unitId in _eligibleExclusiveUnitIds)
            {
                destination.Add(unitId);
            }
        }
    }

    /// <summary>
    /// Maps loader-owned pool IDs to cloned pool objects and enforces stage-scoped mod-unit
    /// eligibility. It never modifies the game's resource pools.
    /// </summary>
    public sealed class BlueprintPoolScopeRegistry
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, BlueprintPoolScopeRegistration> _byId =
            new Dictionary<string, BlueprintPoolScopeRegistration>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<object, BlueprintPoolScopeRegistration> _byReference =
            new Dictionary<object, BlueprintPoolScopeRegistration>(ReferenceComparer.Instance);
        private readonly Dictionary<string, string> _localizationAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<BlueprintPoolScopeRegistration> _ordered =
            new List<BlueprintPoolScopeRegistration>();

        public void ValidateCanRegister(IEnumerable<BlueprintPoolScopeRegistration> registrations)
        {
            lock (_gate)
            {
                ValidateCanRegisterLocked(registrations);
            }
        }

        public void Register(IEnumerable<BlueprintPoolScopeRegistration> registrations)
        {
            lock (_gate)
            {
                var batch = new List<BlueprintPoolScopeRegistration>();
                if (registrations != null)
                {
                    foreach (BlueprintPoolScopeRegistration registration in registrations)
                    {
                        batch.Add(registration);
                    }
                }

                ValidateCanRegisterLocked(batch);
                for (int i = 0; i < batch.Count; i++)
                {
                    BlueprintPoolScopeRegistration registration = batch[i];
                    _byId[registration.PoolId] = registration;
                    _byReference[registration.Pool] = registration;
                    _ordered.Add(registration);
                    if (!String.IsNullOrWhiteSpace(registration.SourcePoolId))
                    {
                        string effectiveSource = registration.SourcePoolId;
                        string rootSource;
                        if (_localizationAliases.TryGetValue(effectiveSource, out rootSource) &&
                            !String.IsNullOrWhiteSpace(rootSource))
                        {
                            effectiveSource = rootSource;
                        }

                        _localizationAliases[registration.PoolId] = effectiveSource;
                    }
                }
            }
        }

        public bool ContainsPoolId(string poolId)
        {
            lock (_gate)
            {
                return !String.IsNullOrEmpty(poolId) && _byId.ContainsKey(poolId);
            }
        }

        public bool TryGetSourcePoolLocalizationId(string poolId, out string sourcePoolId)
        {
            lock (_gate)
            {
                if (!String.IsNullOrEmpty(poolId) && _localizationAliases.TryGetValue(poolId, out sourcePoolId))
                {
                    return true;
                }

                BlueprintPoolScopeRegistration registration;
                if (!String.IsNullOrEmpty(poolId) && _byId.TryGetValue(poolId, out registration))
                {
                    if (!String.IsNullOrWhiteSpace(registration.SourcePoolId))
                    {
                        sourcePoolId = registration.SourcePoolId;
                        return true;
                    }
                }
            }

            sourcePoolId = null;
            return false;
        }

        public bool TryFind(string poolId, out object pool)
        {
            lock (_gate)
            {
                BlueprintPoolScopeRegistration registration;
                if (!String.IsNullOrEmpty(poolId) && _byId.TryGetValue(poolId, out registration))
                {
                    pool = registration.Pool;
                    return true;
                }
            }

            pool = null;
            return false;
        }

        public bool TryGetPoolId(object pool, out string poolId)
        {
            lock (_gate)
            {
                BlueprintPoolScopeRegistration registration;
                if (pool != null && _byReference.TryGetValue(pool, out registration))
                {
                    poolId = registration.PoolId;
                    return true;
                }
            }

            poolId = null;
            return false;
        }

        public bool AllowsModUnitInPool(object pool, string unitId)
        {
            return AllowsModUnitInPool(pool, unitId, false, true, false, true);
        }

        /// <summary>
        /// Allows a namespaced unit only in its stage-owned clone and rejects it in demo runs
        /// when its registered balance row is not demo eligible.
        /// </summary>
        public bool AllowsModUnitInPool(object pool, string unitId, bool isDemo, bool isAllowedForDemo)
        {
            return AllowsModUnitInPool(
                pool, unitId, isDemo, isAllowedForDemo, false, true);
        }

        /// <summary>
        /// Applies the game's initial-blueprint restriction to loader-owned rows when querying a
        /// stage-scoped pool before the player has added a blueprint to the run.
        /// </summary>
        public bool AllowsModUnitInPool(
            object pool,
            string unitId,
            bool isDemo,
            bool isAllowedForDemo,
            bool isStartingBlueprintSelection,
            bool isAllowedAsStartingBlueprint)
        {
            lock (_gate)
            {
                BlueprintPoolScopeRegistration registration;
                return pool != null && _byReference.TryGetValue(pool, out registration) &&
                    registration.ContainsUnit(unitId) &&
                    (!isDemo || isAllowedForDemo) &&
                    (!isStartingBlueprintSelection || isAllowedAsStartingBlueprint);
            }
        }

        public bool IsExclusivePool(object pool)
        {
            lock (_gate)
            {
                BlueprintPoolScopeRegistration registration;
                return pool != null && _byReference.TryGetValue(pool, out registration) &&
                    registration.BlueprintsAreExclusive;
            }
        }

        public void SetEligibleExclusiveUnitIds(object pool, IEnumerable<string> eligibleUnitIds)
        {
            lock (_gate)
            {
                BlueprintPoolScopeRegistration registration;
                if (pool != null && _byReference.TryGetValue(pool, out registration) &&
                    registration.BlueprintsAreExclusive)
                {
                    registration.SetEligibleExclusiveUnitIds(eligibleUnitIds);
                }
            }
        }

        /// <summary>
        /// Adds only mod units from exclusive pools belonging to the queried clone's stage.
        /// Native pools and cloned pools from other stages cannot leak exclusive entries into it.
        /// </summary>
        public void AddExclusiveModUnits(string otherThanPoolId, HashSet<string> result)
        {
            if (result == null)
            {
                return;
            }

            lock (_gate)
            {
                BlueprintPoolScopeRegistration currentPool = null;
                bool globalFallback = String.IsNullOrEmpty(otherThanPoolId);
                if (!globalFallback && !_byId.TryGetValue(otherThanPoolId, out currentPool))
                {
                    return;
                }

                for (int i = 0; i < _ordered.Count; i++)
                {
                    BlueprintPoolScopeRegistration registration = _ordered[i];
                    if (!registration.BlueprintsAreExclusive ||
                        (!globalFallback &&
                            (!String.Equals(registration.ScopeId, currentPool.ScopeId, StringComparison.Ordinal) ||
                            String.Equals(registration.PoolId, otherThanPoolId, StringComparison.Ordinal))))
                    {
                        continue;
                    }

                    registration.AddEligibleExclusiveUnitIds(result);
                }
            }
        }

        private void ValidateCanRegisterLocked(IEnumerable<BlueprintPoolScopeRegistration> registrations)
        {
            var ids = new HashSet<string>(_byId.Keys, StringComparer.Ordinal);
            var pools = new HashSet<object>(_byReference.Keys, ReferenceComparer.Instance);
            var localizationIds = new HashSet<string>(_localizationAliases.Keys, StringComparer.OrdinalIgnoreCase);
            if (registrations == null)
            {
                return;
            }

            foreach (BlueprintPoolScopeRegistration registration in registrations)
            {
                if (registration == null || !ids.Add(registration.PoolId) || !pools.Add(registration.Pool) ||
                    (!String.IsNullOrWhiteSpace(registration.SourcePoolId) && !localizationIds.Add(registration.PoolId)))
                {
                    throw new InvalidOperationException("A scoped blueprint pool ID or object is already registered.");
                }
            }
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object left, object right)
            {
                return Object.ReferenceEquals(left, right);
            }

            public int GetHashCode(object value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }
    }
}
