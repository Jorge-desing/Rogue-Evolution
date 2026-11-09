using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Static entry points used by Harmony. Hook bodies catch provider/reflection failures so a
    /// content problem does not replace the game's normal execution path.
    /// </summary>
    internal static class RuntimeHooks
    {
        private sealed class BlueprintRewardScopeFrame
        {
            public BlueprintRewardScopeFrame(BlueprintRewardFactoryScope scope)
            {
                Scope = scope;
                Observation = new BlueprintRewardObservation();
            }

            public BlueprintRewardFactoryScope Scope { get; private set; }

            public BlueprintRewardObservation Observation { get; private set; }
        }

        private static readonly object Gate = new object();
        private static RuntimeContentMutator _mutator;
        private static IRuntimeContentProvider _provider;
        private static Action<string> _log;
        [ThreadStatic]
        private static BlueprintRewardScopeFrame _blueprintRewardScope;
        public static Action<object> OnEntityInstantiated;

        private static int _unitCreationHookCount;
        private static int _unitReuseHookCount;
        private static int _placementGhostHookCount;
        private static int _factoryProduceHookCount;
        private static int _entityRegistryResetCount;
        private static int _entityRegisteredCount;
        private static int _entityUnregisteredCount;
        private static int _commandExecutedCount;
        private static int _commandNextStepCount;
        private static int _commandResetCount;
        private static int _commandChainDeletedCount;
        private static int _unitCreationTranspilerCount;
        private static int _entityRegisteredTranspilerCount;
        private static int _entityUnregisteredTranspilerCount;
        private static int _unitReuseTranspilerCount;
        private static string _lastExecutedCommandKind = string.Empty;
        private static readonly HashSet<int> _activeRegisteredEntities = new HashSet<int>();
        private static FieldInfo _cachedCommandKindField;
        private static FieldInfo _cachedCommandTagField;

        public static int UnitCreationHookCount => System.Threading.Interlocked.CompareExchange(ref _unitCreationHookCount, 0, 0);
        public static int UnitReuseHookCount => System.Threading.Interlocked.CompareExchange(ref _unitReuseHookCount, 0, 0);
        public static int PlacementGhostHookCount => System.Threading.Interlocked.CompareExchange(ref _placementGhostHookCount, 0, 0);
        public static int FactoryProduceHookCount => System.Threading.Interlocked.CompareExchange(ref _factoryProduceHookCount, 0, 0);
        public static int EntityRegistryResetCount => System.Threading.Interlocked.CompareExchange(ref _entityRegistryResetCount, 0, 0);
        public static int EntityRegisteredCount => System.Threading.Interlocked.CompareExchange(ref _entityRegisteredCount, 0, 0);
        public static int EntityUnregisteredCount => System.Threading.Interlocked.CompareExchange(ref _entityUnregisteredCount, 0, 0);
        public static int CommandExecutedCount => System.Threading.Interlocked.CompareExchange(ref _commandExecutedCount, 0, 0);
        public static int CommandNextStepCount => System.Threading.Interlocked.CompareExchange(ref _commandNextStepCount, 0, 0);
        public static int CommandResetCount => System.Threading.Interlocked.CompareExchange(ref _commandResetCount, 0, 0);
        public static int CommandChainDeletedCount => System.Threading.Interlocked.CompareExchange(ref _commandChainDeletedCount, 0, 0);
        public static int UnitCreationTranspilerCount => System.Threading.Interlocked.CompareExchange(ref _unitCreationTranspilerCount, 0, 0);
        public static int EntityRegisteredTranspilerCount => System.Threading.Interlocked.CompareExchange(ref _entityRegisteredTranspilerCount, 0, 0);
        public static int EntityUnregisteredTranspilerCount => System.Threading.Interlocked.CompareExchange(ref _entityUnregisteredTranspilerCount, 0, 0);
        public static int UnitReuseTranspilerCount => System.Threading.Interlocked.CompareExchange(ref _unitReuseTranspilerCount, 0, 0);

        public static string LastExecutedCommandKind
        {
            get
            {
                lock (Gate)
                {
                    return _lastExecutedCommandKind ?? string.Empty;
                }
            }
        }

        public static int ActiveRegisteredEntityCount
        {
            get
            {
                lock (_activeRegisteredEntities)
                {
                    return _activeRegisteredEntities.Count;
                }
            }
        }

        public static bool IsEntityRegisteredInBattlefield(object entityObj)
        {
            if (entityObj == null)
            {
                return false;
            }

            int key = RuntimeUnitMechanicsDirector.GetEntityTrackingKey(entityObj);
            if (key == 0)
            {
                return false;
            }

            lock (_activeRegisteredEntities)
            {
                return _activeRegisteredEntities.Contains(key);
            }
        }

        public static void ResetCoreHookTelemetry()
        {
            System.Threading.Interlocked.Exchange(ref _unitCreationHookCount, 0);
            System.Threading.Interlocked.Exchange(ref _unitReuseHookCount, 0);
            System.Threading.Interlocked.Exchange(ref _placementGhostHookCount, 0);
            System.Threading.Interlocked.Exchange(ref _factoryProduceHookCount, 0);
            System.Threading.Interlocked.Exchange(ref _entityRegistryResetCount, 0);
            System.Threading.Interlocked.Exchange(ref _entityRegisteredCount, 0);
            System.Threading.Interlocked.Exchange(ref _entityUnregisteredCount, 0);
            System.Threading.Interlocked.Exchange(ref _commandExecutedCount, 0);
            System.Threading.Interlocked.Exchange(ref _commandNextStepCount, 0);
            System.Threading.Interlocked.Exchange(ref _commandResetCount, 0);
            System.Threading.Interlocked.Exchange(ref _commandChainDeletedCount, 0);
            System.Threading.Interlocked.Exchange(ref _unitCreationTranspilerCount, 0);
            System.Threading.Interlocked.Exchange(ref _entityRegisteredTranspilerCount, 0);
            System.Threading.Interlocked.Exchange(ref _entityUnregisteredTranspilerCount, 0);
            System.Threading.Interlocked.Exchange(ref _unitReuseTranspilerCount, 0);
            lock (Gate)
            {
                _lastExecutedCommandKind = string.Empty;
            }
            lock (_activeRegisteredEntities)
            {
                _activeRegisteredEntities.Clear();
            }
        }

        public static void Bind(
            RuntimeContentMutator mutator,
            IRuntimeContentProvider provider,
            Action<string> logger)
        {
            lock (Gate)
            {
                _mutator = mutator;
                _provider = provider;
                _log = logger;
            }
        }

        public static void Clear(RuntimeContentMutator expectedMutator)
        {
            lock (Gate)
            {
                if (!object.ReferenceEquals(_mutator, expectedMutator))
                {
                    return;
                }

                _mutator = null;
                _provider = null;
                _log = null;
                OnEntityInstantiated = null;
                _blueprintRewardScope = null;
            }
        }

        public static object BeginBlueprintRewardScope()
        {
            BlueprintRewardScopeFrame previous = _blueprintRewardScope;
            _blueprintRewardScope = null;
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            IBlueprintRewardFactoryScopeProvider scopeProvider =
                provider as IBlueprintRewardFactoryScopeProvider;
            if (scopeProvider != null)
            {
                try
                {
                    BlueprintRewardFactoryScope scope;
                    if (scopeProvider.TryGetBlueprintRewardFactoryScope(out scope) && scope != null)
                    {
                        _blueprintRewardScope = new BlueprintRewardScopeFrame(scope);
                    }
                }
                catch (Exception exception)
                {
                    Log("Could not resolve stage-scoped factory rewards: " +
                        exception.GetBaseException().Message);
                    try
                    {
                        BlueprintRewardFactoryScope failClosedScope =
                            scopeProvider.CreateFailClosedBlueprintRewardFactoryScope();
                        if (failClosedScope != null)
                        {
                            _blueprintRewardScope = new BlueprintRewardScopeFrame(failClosedScope);
                        }
                    }
                    catch (Exception failClosedException)
                    {
                        Log("Could not create a fail-closed factory reward scope: " +
                            failClosedException.GetBaseException().Message);
                    }
                }
            }

            return previous;
        }

        public static Exception EndBlueprintRewardScope(Exception exception, object previousScope)
        {
            BlueprintRewardScopeFrame completedScope = _blueprintRewardScope;
            try
            {
                if (completedScope != null)
                {
                    Log("Blueprint reward trace completion=" + (exception == null ? "completed" : "aborted") +
                        " " + completedScope.Observation.Format(completedScope.Scope));
                }
            }
            catch (Exception loggingException)
            {
                Log("Could not format blueprint reward trace: " +
                    loggingException.GetBaseException().Message);
            }
            finally
            {
                _blueprintRewardScope = previousScope as BlueprintRewardScopeFrame;
            }

            return exception;
        }

        public static void FilterGlobalBlueprintRewardCandidates(List<string> candidates)
        {
            BlueprintRewardScopeFrame activeScope = _blueprintRewardScope;
            if (activeScope != null && candidates != null)
            {
                activeScope.Observation.RecordGlobalCandidates(candidates, activeScope.Scope, false);
                activeScope.Scope.RemoveFactoriesOutsideStage(candidates);
                activeScope.Observation.RecordGlobalCandidates(candidates, activeScope.Scope, true);
            }
        }

        public static void AddGlobalBlueprintRewardCandidates(object[] args, ref List<string> result)
        {
            if (result == null || args == null || args.Length < 2)
            {
                return;
            }

            try
            {
                int rarity = args[0] != null ? Convert.ToInt32(args[0]) : -1;
                bool? startingBlueprints = args[1] as bool?;
                bool demoOnly = args.Length > 2 && args[2] is bool ? (bool)args[2] : false;
                int? maxExp = args.Length > 4 && args[4] is int ? (int?)args[4] : null;

                IGlobalBlueprintCandidateProvider candidateProvider;
                lock (Gate)
                {
                    candidateProvider = _provider as IGlobalBlueprintCandidateProvider;
                }

                if (candidateProvider != null)
                {
                    IEnumerable<string> modFactories = candidateProvider.GetEligibleBlueprintFactories(
                        rarity, startingBlueprints, demoOnly, maxExp);
                    if (modFactories != null)
                    {
                        foreach (string factoryId in modFactories)
                        {
                            if (!String.IsNullOrEmpty(factoryId) && !result.Contains(factoryId))
                            {
                                result.Add(factoryId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Error in AddGlobalBlueprintRewardCandidates: " + ex.GetBaseException().Message);
            }
        }

        public static bool EnsureGuaranteedModCardOption(List<string> ids, int? forcedSlot = null)
        {
            if (ids == null || ids.Count == 0)
            {
                return false;
            }

            RuntimeContentMutator mutator;
            if (!TryPrepareRuntimeContent(out mutator) || mutator == null || mutator.GameAssembly == null)
            {
                return false;
            }

            // The card coroutine can start before any EntityFactory call. Register packs before
            // adding a mod ID, otherwise CardNew.Init cannot resolve its balance row.
            bool rowsRegistered = false;
            SafeCall(delegate { rowsRegistered = mutator.RegisterEntityRows(); });
            if (!rowsRegistered)
            {
                Log("Mod card reward injection was skipped because entity rows are not registered.");
                return false;
            }

            IGuaranteedModCardRewardProvider guaranteedProvider = GetGuaranteedRewardProvider();
            if (guaranteedProvider == null)
            {
                return false;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                if (guaranteedProvider.IsModUnitProduct(ids[i]))
                {
                    return false;
                }
            }

            string poolId = null;
            bool isStartingSelection = false;
            int currentXpLevel = 0;
            HashSet<string> excluded = new HashSet<string>(ids, StringComparer.Ordinal);

            try
            {
                // The game assembly also defines Shapes2D.Game; resolve only the global Game type.
                Type gameType = mutator.GameAssembly.GetType("Game", false, false);
                if (gameType != null)
                {
                    FieldInfo startingField = gameType.GetField("AtLeastOneBlueprintHasBeenAddedByPlayer",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (startingField != null)
                    {
                        object value = startingField.GetValue(null);
                        if (value is bool)
                        {
                            isStartingSelection = !(bool)value;
                        }
                    }

                    PropertyInfo cardsInDeckProperty = gameType.GetProperty("CardsInDeck",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    IEnumerable<string> cardsInDeck = cardsInDeckProperty == null
                        ? null
                        : cardsInDeckProperty.GetValue(null, null) as IEnumerable<string>;
                    if (cardsInDeck != null)
                    {
                        foreach (string card in cardsInDeck)
                        {
                            if (!String.IsNullOrEmpty(card))
                            {
                                excluded.Add(card);
                            }
                        }
                    }

                    FieldInfo openCardRewardsField = gameType.GetField("OpenCardRewards",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    System.Collections.IList openRewards = openCardRewardsField == null
                        ? null
                        : openCardRewardsField.GetValue(null) as System.Collections.IList;
                    if (openRewards != null && openRewards.Count > 0 && openRewards[0] != null)
                    {
                        FieldInfo poolIdField = openRewards[0].GetType().GetField("blueprintPoolId",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (poolIdField != null)
                        {
                            poolId = poolIdField.GetValue(openRewards[0]) as string;
                        }
                    }
                }

                Type metaGameType = mutator.GameAssembly.GetType("MetaGame", false, false);
                PropertyInfo instanceProperty = metaGameType == null ? null : metaGameType.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object metaGame = instanceProperty == null ? null : instanceProperty.GetValue(null, null);
                PropertyInfo xpProperty = metaGame == null ? null : metaGameType.GetProperty("CurrentExperienceLevel",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object xpValue = xpProperty == null ? null : xpProperty.GetValue(metaGame, null);
                if (xpValue is int)
                {
                    currentXpLevel = (int)xpValue;
                }
            }
            catch (Exception exception)
            {
                Log("Warning: Could not inspect game state for guaranteed mod card reward: " +
                    exception.GetBaseException().Message);
            }

            string modProductId;
            if (guaranteedProvider.TryGetGuaranteedModRewardProduct(
                poolId, isStartingSelection, currentXpLevel, excluded, out modProductId))
            {
                int slot = forcedSlot.HasValue
                    ? Math.Abs(forcedSlot.Value) % ids.Count
                    : _guaranteedRewardRandom.Next(0, ids.Count);
                string replaced = ids[slot];
                ids[slot] = modProductId;
                Log(string.Format("Guaranteed mod unit card injected into slot {0} (replaced '{1}'): '{2}'",
                    slot, replaced, modProductId));
                return true;
            }

            return false;
        }

        public static void ObserveBlueprintPoolCandidates(
            object pool,
            List<string> candidates,
            int? currentExperienceLevel,
            bool excludeExclusivesFromOtherPools)
        {
            BlueprintRewardScopeFrame activeScope = _blueprintRewardScope;
            if (activeScope == null || candidates == null)
            {
                return;
            }

            IBlueprintPoolRuntimeProvider poolProvider = GetBlueprintPoolProvider();
            if (poolProvider == null)
            {
                activeScope.Observation.RecordUnmappedPoolCandidates(candidates, activeScope.Scope);
                return;
            }

            try
            {
                string poolId;
                if (poolProvider.TryGetBlueprintPoolId(pool, out poolId))
                {
                    activeScope.Observation.RecordPoolCandidates(
                        poolId, candidates, activeScope.Scope,
                        currentExperienceLevel, excludeExclusivesFromOtherPools);
                }
                else
                {
                    activeScope.Observation.RecordUnmappedPoolCandidates(candidates, activeScope.Scope);
                }
            }
            catch (Exception hookException)
            {
                activeScope.Observation.RecordUnmappedPoolCandidates(candidates, activeScope.Scope);
                Log("Could not observe custom BlueprintPool candidates: " +
                    hookException.GetBaseException().Message);
            }
        }

        public static void ObserveBlueprintRewardRecord(string cardType, string cardName)
        {
            BlueprintRewardScopeFrame activeScope = _blueprintRewardScope;
            if (activeScope != null && String.Equals(cardType, "blueprint", StringComparison.Ordinal))
            {
                activeScope.Observation.RecordOfferedProduct(cardName, activeScope.Scope);
            }
        }

        public static void AfterEntityBalancingInit()
        {
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            if (provider != null)
            {
                SafeCall(delegate { provider.OnEntityRegistryInitialized(); });
            }
        }

        public static void AfterSetScriptableObject(object[] arguments)
        {
            RuntimeContentMutator mutator = GetMutator();
            if (mutator == null || arguments == null || arguments.Length == 0)
            {
                return;
            }

            SafeCall(delegate { mutator.ApplyProgressionFrom(arguments[0]); });
            NotifyProgressionContentApplied();
        }

        public static void BeforeProgressionMap()
        {
            RuntimeContentMutator mutator;
            if (TryPrepareRuntimeContent(out mutator))
            {
                bool rowsRegistered = false;
                SafeCall(delegate { rowsRegistered = mutator.RegisterEntityRows(); });
                if (rowsRegistered)
                {
                    SafeCall(delegate { mutator.ApplyCachedProgression(); });
                    NotifyProgressionContentApplied();
                }
            }
        }

        public static void AfterStageMapCreated(object stageMap)
        {
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            IBlueprintRewardFactoryScopeProvider scopeProvider =
                provider as IBlueprintRewardFactoryScopeProvider;
            if (scopeProvider != null)
            {
                SafeCall(delegate { scopeProvider.OnStageMapCreated(stageMap); });
            }
        }

        public static void AfterStageMapDeserialized(object stageMap, string json)
        {
            // Save loading can construct StageMap before the normal map-generation hook. Prepare
            // the same additive content registrations now, then let the provider restore its
            // serialized stage identity sequence.
            BeforeProgressionMap();

            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            IBlueprintRewardFactoryScopeProvider scopeProvider =
                provider as IBlueprintRewardFactoryScopeProvider;
            if (scopeProvider == null)
            {
                return;
            }

            try
            {
                scopeProvider.OnStageMapDeserialized(stageMap, json);
            }
            catch (Exception exception)
            {
                Log("Could not restore the saved stage identity sequence: " +
                    exception.GetBaseException().Message);
            }
        }

        private static void NotifyProgressionContentApplied()
        {
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            IBlueprintRewardFactoryScopeProvider scopeProvider =
                provider as IBlueprintRewardFactoryScopeProvider;
            if (scopeProvider != null)
            {
                SafeCall(delegate { scopeProvider.OnProgressionContentApplied(); });
            }
        }

        public static void AfterStageMapSerialized(object stageMap, ref string json)
        {
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            IBlueprintRewardFactoryScopeProvider scopeProvider =
                provider as IBlueprintRewardFactoryScopeProvider;
            if (scopeProvider == null || json == null)
            {
                return;
            }

            try
            {
                string updatedJson = scopeProvider.AppendStageMapOrder(stageMap, json);
                if (updatedJson != null)
                {
                    json = updatedJson;
                }
            }
            catch (Exception exception)
            {
                Log("Could not persist the stage identity sequence: " +
                    exception.GetBaseException().Message);
            }
        }

        public static void BeforeEntityFactory()
        {
            RuntimeContentMutator mutator;
            if (TryPrepareRuntimeContent(out mutator))
            {
                SafeCall(delegate { mutator.RegisterEntityRows(); });
            }
        }

        public static void OnSetupPrespawning()
        {
            SafeCall(delegate { ApplyModPrespawnQuotas(false); });
        }

        public static void OnSetupAiPrespawning()
        {
            SafeCall(delegate { ApplyModPrespawnQuotas(true); });
        }

        private static void ApplyModPrespawnQuotas(bool isAi)
        {
            RuntimeContentMutator mutator;
            TryPrepareRuntimeContent(out mutator);

            IPrespawnRuntimeProvider prespawnProvider;
            lock (Gate) { prespawnProvider = _provider as IPrespawnRuntimeProvider; }
            if (prespawnProvider == null || mutator == null || mutator.GameAssembly == null) return;

            Type entityFactoryType = RuntimeReflection.FindUniqueType(mutator.GameAssembly, "EntityFactory");
            if (entityFactoryType == null) return;

            FieldInfo toPrespawnField = entityFactoryType.GetField("EntitiesToPrespawn",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            FieldInfo prespawnedField = entityFactoryType.GetField("PrespawnedEntities",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (toPrespawnField == null || prespawnedField == null) return;

            System.Collections.IDictionary toPrespawn = toPrespawnField.GetValue(null) as System.Collections.IDictionary;
            System.Collections.IDictionary prespawned = prespawnedField.GetValue(null) as System.Collections.IDictionary;
            if (toPrespawn == null || prespawned == null) return;

            IEnumerable<PrespawnQuota> quotas = prespawnProvider.GetPrespawnQuotas();
            if (quotas == null) return;

            Type entityControllerType = RuntimeReflection.FindUniqueType(mutator.GameAssembly, "EntityController");
            if (entityControllerType == null) return;
            Type stackType = typeof(Stack<>).MakeGenericType(entityControllerType);

            MethodInfo prespawnEntityMethod = entityFactoryType.GetMethod("PrespawnEntity",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            HashSet<string> activeEntityIds = null;
            try
            {
                Type gameType = mutator.GameAssembly.GetType("Game", false, false);
                if (gameType != null)
                {
                    activeEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!isAi)
                    {
                        PropertyInfo cardsInDeckProp = gameType.GetProperty("CardsInDeck",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (cardsInDeckProp != null)
                        {
                            IEnumerable<string> cards = cardsInDeckProp.GetValue(null) as IEnumerable<string>;
                            if (cards != null)
                            {
                                foreach (string card in cards)
                                {
                                    if (!string.IsNullOrEmpty(card))
                                    {
                                        activeEntityIds.Add(card);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        Type defaultAiType = RuntimeReflection.FindUniqueType(mutator.GameAssembly, "DefaultAiBehaviour");
                        MethodInfo constructablesMethod = defaultAiType?.GetMethod("ConstructableEntities_Static",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (constructablesMethod != null)
                        {
                            System.Collections.IEnumerable constructables = constructablesMethod.Invoke(null, null) as System.Collections.IEnumerable;
                            if (constructables != null)
                            {
                                foreach (object item in constructables)
                                {
                                    if (item != null)
                                    {
                                        FieldInfo idField = item.GetType().GetField("entityId",
                                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        string id = idField?.GetValue(item) as string;
                                        if (!string.IsNullOrEmpty(id))
                                        {
                                            activeEntityIds.Add(id);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Deck / AI constructable inspection failed: " + ex.GetBaseException().Message);
            }

            foreach (PrespawnQuota quota in quotas)
            {
                if (String.IsNullOrEmpty(quota.EntityId) || quota.Quota <= 0) continue;

                if (toPrespawn.Contains(quota.EntityId))
                {
                    int current = Convert.ToInt32(toPrespawn[quota.EntityId]);
                    if (current < quota.Quota)
                    {
                        toPrespawn[quota.EntityId] = quota.Quota;
                    }
                }
                else
                {
                    toPrespawn[quota.EntityId] = quota.Quota;
                }

                if (!prespawned.Contains(quota.EntityId))
                {
                    object stack = Activator.CreateInstance(stackType);
                    prespawned[quota.EntityId] = stack;
                }

                bool isActive = activeEntityIds == null || activeEntityIds.Count == 0 ||
                    activeEntityIds.Contains(quota.EntityId) ||
                    (!string.IsNullOrEmpty(quota.FactoryId) && activeEntityIds.Contains(quota.FactoryId));

                if (isActive && prespawnEntityMethod != null)
                {
                    try
                    {
                        int warmCount = Math.Min(2, quota.Quota);
                        for (int w = 0; w < warmCount; w++)
                        {
                            object result = prespawnEntityMethod.Invoke(null, new object[] { quota.EntityId, warmCount });
                            if (result is bool spawned && !spawned) break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("Warm pre-spawn omitted for " + quota.EntityId + ": " + ex.GetBaseException().Message);
                    }
                }
            }
        }

        public static bool BlueprintNamePrefix(string __0, ref string __result)
        {
            return TryResolveUnitLocalization(__0, false, ref __result);
        }

        public static object ResolveFallbackLandscapes(object nativeFallbacks)
        {
            IStageFallbackRuntimeProvider fallbackProvider;
            lock (Gate) { fallbackProvider = _provider as IStageFallbackRuntimeProvider; }
            if (fallbackProvider == null || nativeFallbacks == null) return nativeFallbacks;
            try
            {
                object snapshot = fallbackProvider.GetFallbackLandscapeSnapshot(nativeFallbacks);
                if (snapshot != null && snapshot.GetType() == nativeFallbacks.GetType()) return snapshot;
                Log("The stage fallback provider returned an incompatible list.");
            }
            catch (Exception exception)
            {
                Log("Could not resolve appended stage fallbacks: " + exception.GetBaseException().Message);
            }

            return nativeFallbacks;
        }

        public static bool OutlyingStartAiBuildingDistancePrefix(ref int __result)
        {
            IStageOwnedAiPlacementDistanceRuntimeProvider distanceProvider;
            lock (Gate)
            {
                distanceProvider = _provider as IStageOwnedAiPlacementDistanceRuntimeProvider;
            }

            if (distanceProvider == null)
            {
                return true;
            }

            try
            {
                int distance;
                if (distanceProvider.TryGetOutlyingStartAiBuildingsDistance(out distance))
                {
                    __result = distance;
                    return false;
                }
            }
            catch (Exception exception)
            {
                Log("Could not resolve spacing for a loader-owned appended stage: " +
                    exception.GetBaseException().Message);
            }

            return true;
        }

        public static bool BlueprintDescriptionPrefix(string __0, ref string __result)
        {
            return TryResolveUnitLocalization(__0, true, ref __result);
        }

        public static bool RelicNamePrefix(string __0, ref string __result)
        {
            return TryResolveRelicLocalization(__0, false, ref __result);
        }

        public static bool RelicDescriptionPrefix(string __0, ref string __result)
        {
            return TryResolveRelicLocalization(__0, true, ref __result);
        }

        public static bool UpgradeNamePrefix(string __0, ref string __result)
        {
            return TryResolveUpgradeLocalization(__0, false, ref __result);
        }

        public static bool UpgradeDescriptionPrefix(string __0, ref string __result)
        {
            return TryResolveUpgradeLocalization(__0, true, ref __result);
        }

        public static bool SkillDescriptionPrefix(string __0, ref string __result)
        {
            IUnitSkillLocalizationRuntimeProvider provider;
            Action<string> logger;
            lock (Gate)
            {
                provider = _provider as IUnitSkillLocalizationRuntimeProvider;
                logger = _log;
            }

            if (provider == null)
            {
                if (__0 != null && __0.Contains(":"))
                {
                    __result = String.Empty;
                    return false;
                }
                return true;
            }

            try
            {
                string value;
                if (!provider.TryGetUnitSkillDescription(__0, out value))
                {
                    if (__0 != null && __0.Contains(":"))
                    {
                        __result = String.Empty;
                        return false;
                    }
                    return true;
                }

                __result = value ?? String.Empty;
                return false;
            }
            catch (Exception exception)
            {
                if (logger != null)
                {
                    logger("Could not resolve localized skill text for unit '" + __0 + "': " +
                        exception.GetType().Name + ": " + exception.Message);
                }

                if (__0 != null && __0.Contains(":"))
                {
                    __result = String.Empty;
                    return false;
                }
                return true;
            }
        }

        public static bool LandscapeLocalizationPrefix(ref string __0, ref string __result)
        {
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            IBlueprintPoolRuntimeProvider poolProvider = provider as IBlueprintPoolRuntimeProvider;
            string sourcePoolId = null;
            bool isRclPool = !String.IsNullOrEmpty(__0) &&
                (__0.StartsWith("RCL_POOL_", StringComparison.OrdinalIgnoreCase) ||
                 __0.StartsWith("rcl_pool_", StringComparison.OrdinalIgnoreCase));

            if (isRclPool || (poolProvider != null && poolProvider.TryGetBlueprintPoolLocalizationId(__0, out sourcePoolId)))
            {
                if (String.IsNullOrEmpty(sourcePoolId) && poolProvider != null)
                {
                    poolProvider.TryGetBlueprintPoolLocalizationId(__0, out sourcePoolId);
                }

                while (!String.IsNullOrEmpty(sourcePoolId) &&
                       (sourcePoolId.StartsWith("RCL_POOL_", StringComparison.OrdinalIgnoreCase) ||
                        sourcePoolId.StartsWith("rcl_pool_", StringComparison.OrdinalIgnoreCase)))
                {
                    string next;
                    if (poolProvider != null && poolProvider.TryGetBlueprintPoolLocalizationId(sourcePoolId, out next) && next != sourcePoolId)
                    {
                        sourcePoolId = next;
                    }
                    else
                    {
                        break;
                    }
                }

                if (!String.IsNullOrEmpty(sourcePoolId) &&
                    !sourcePoolId.StartsWith("RCL_POOL_", StringComparison.OrdinalIgnoreCase) &&
                    !sourcePoolId.StartsWith("rcl_pool_", StringComparison.OrdinalIgnoreCase))
                {
                    __0 = sourcePoolId;
                    return true;
                }

                // If sourcePoolId could not be resolved or is still an RCL_POOL_,
                // rewrite to native blueprint tooltip so native Loca.Global never throws 'No translation found'.
                __0 = "overworldMapBlueprintTooltip";
                return true;
            }

            IStageLandscapeRuntimeProvider landscapeProvider = provider as IStageLandscapeRuntimeProvider;
            if (landscapeProvider == null)
            {
                return true;
            }

            try
            {
                string value;
                if (!landscapeProvider.TryGetLandscapeLocalization(__0, out value))
                {
                    return true;
                }

                __result = value ?? String.Empty;
                return false;
            }
            catch (Exception exception)
            {
                Log("Could not resolve landscape localization '" + __0 + "': " + exception.Message);
                return true;
            }
        }

        public static Exception LandscapeLocalizationFinalizer(Exception __exception, ref string __result)

        {

            if (__exception != null)

            {

                Log("Handled localization exception: " + __exception.Message);

                __result = "Blueprint";

                return null;

            }



            return null;

        }



        public static bool BlueprintPoolIsInPoolPrefix(object __instance, string __0, ref bool __result)
        {
            IBlueprintPoolRuntimeProvider poolProvider = GetBlueprintPoolProvider();
            if (poolProvider == null)
            {
                return true;
            }

            try
            {
                bool runOriginal;
                if (!poolProvider.TryGetBlueprintPoolDecision(__instance, __0, out runOriginal) || runOriginal)
                {
                    return true;
                }

                __result = false;
                return false;
            }
            catch (Exception exception)
            {
                Log("Could not scope blueprint pool eligibility for unit '" + __0 + "': " +
                    exception.GetBaseException().Message);
                return true;
            }
        }

        public static bool BlueprintPoolFindPrefix(string __0, ref global::BlueprintPool __result)
        {
            IBlueprintPoolRuntimeProvider poolProvider = GetBlueprintPoolProvider();
            if (poolProvider == null)
            {
                return true;
            }

            try
            {
                object pool;
                if (!poolProvider.TryFindBlueprintPool(__0, out pool))
                {
                    return true;
                }

                global::BlueprintPool typedPool = pool as global::BlueprintPool;
                if (typedPool == null)
                {
                    return true;
                }

                __result = typedPool;
                return false;
            }
            catch (Exception exception)
            {
                Log("Could not resolve scoped BlueprintPool '" + __0 + "': " +
                    exception.GetBaseException().Message);
                return true;
            }
        }

        public static void BlueprintPoolExclusiveBlueprintsPostfix(string __0, HashSet<string> __result)
        {
            IBlueprintPoolRuntimeProvider poolProvider = GetBlueprintPoolProvider();
            if (poolProvider == null || __result == null)
            {
                return;
            }

            try
            {
                poolProvider.AddExclusiveBlueprints(__0, __result);
                BlueprintRewardScopeFrame activeScope = _blueprintRewardScope;
                if (activeScope != null && String.IsNullOrWhiteSpace(__0))
                {
                    activeScope.Scope.RemoveFactoriesOutsideStage(__result);
                }
            }
            catch (Exception exception)
            {
                Log("Could not add scoped exclusive blueprints: " + exception.GetBaseException().Message);
            }
        }

        public static void BeginLevelMapScope(object stage, out object __state)
        {
            __state = null;
            BeforeProgressionMap();

            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            IStageLandscapeRuntimeProvider landscapeProvider = provider as IStageLandscapeRuntimeProvider;
            if (landscapeProvider == null)
            {
                return;
            }

            try
            {
                __state = landscapeProvider.BeginStageLandscapeScope(stage);
            }
            catch (Exception exception)
            {
                Log("Could not apply stage-scoped landscape additions: " + exception.Message);
            }
        }

        public static Exception EndLevelMapScope(Exception __exception, object __state)
        {
            IDisposable scope = __state as IDisposable;
            if (scope == null)
            {
                return __exception;
            }

            try
            {
                scope.Dispose();
            }
            catch (Exception exception)
            {
                Log("Could not restore stage-scoped landscape additions: " + exception.Message);
            }

            return __exception;
        }

        private static bool TryResolveUnitLocalization(string unitId, bool description, ref string result)
        {
            IRuntimeContentProvider provider;
            Action<string> logger;
            lock (Gate)
            {
                provider = _provider;
                logger = _log;
            }

            if (provider == null)
            {
                if (unitId != null && unitId.Contains(":"))
                {
                    result = description ? String.Empty : unitId;
                    return false;
                }
                return true;
            }

            try
            {
                string value;
                if (!provider.TryGetUnitLocalization(unitId, description, out value))
                {
                    if (unitId != null && unitId.Contains(":"))
                    {
                        result = description ? String.Empty : unitId;
                        return false;
                    }
                    return true;
                }

                result = value ?? String.Empty;
                return false;
            }
            catch (Exception exception)
            {
                if (logger != null)
                {
                    logger("Could not resolve localized text for unit '" + unitId + "': " +
                        exception.GetType().Name + ": " + exception.Message);
                }

                if (unitId != null && unitId.Contains(":"))
                {
                    result = description ? String.Empty : unitId;
                    return false;
                }
                return true;
            }
        }

        private static bool TryResolveRelicLocalization(string relicId, bool description, ref string result)
        {
            IRelicLocalizationRuntimeProvider provider;
            Action<string> logger;
            lock (Gate)
            {
                provider = _provider as IRelicLocalizationRuntimeProvider;
                logger = _log;
            }

            if (provider == null)
            {
                return true;
            }

            try
            {
                string value;
                if (!provider.TryGetRelicLocalization(relicId, description, out value))
                {
                    return true;
                }

                result = value ?? String.Empty;
                return false;
            }
            catch (Exception exception)
            {
                if (logger != null)
                {
                    logger("Could not resolve localized text for relic '" + relicId + "': " +
                        exception.GetType().Name + ": " + exception.Message);
                }

                return true;
            }
        }

        private static bool TryResolveUpgradeLocalization(string upgradeId, bool description, ref string result)
        {
            IUpgradeLocalizationRuntimeProvider provider;
            Action<string> logger;
            lock (Gate)
            {
                provider = _provider as IUpgradeLocalizationRuntimeProvider;
                logger = _log;
            }

            if (provider == null)
            {
                return true;
            }

            try
            {
                string value;
                if (!provider.TryGetUpgradeLocalization(upgradeId, description, out value))
                {
                    return true;
                }

                result = value ?? String.Empty;
                return false;
            }
            catch (Exception exception)
            {
                if (logger != null)
                {
                    logger("Could not resolve localized text for upgrade '" + upgradeId + "': " +
                        exception.GetType().Name + ": " + exception.Message);
                }

                return true;
            }
        }

        private static bool TryPrepareRuntimeContent(out RuntimeContentMutator mutator)
        {
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                mutator = _mutator;
                provider = _provider;
            }

            if (mutator == null || provider == null)
            {
                return false;
            }

            bool ready = false;
            SafeCall(delegate { ready = provider.PrepareContentForRuntime(); });
            return ready;
        }

        public static void DefaultAiBehaviourInitPostfix(object __instance)
        {
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            if (provider != null && __instance != null)
            {
                SafeCall(delegate { provider.OnAiBehaviourInitialized(__instance); });
            }
        }

        public static void AiBehaviourUpdatePostfix(object __instance)
        {
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            if (provider != null && __instance != null)
            {
                SafeCall(delegate { provider.OnAiBehaviourUpdated(__instance); });
            }
        }

        public static void AiBehaviourDestroyPostfix(object __instance)
        {
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            if (provider != null && __instance != null)
            {
                SafeCall(delegate { provider.OnAiBehaviourDestroyed(__instance); });
            }
        }

        private static FieldInfo _entityAttackControllerField;
        private static PropertyInfo _entityControllerIdProperty;
        private static FieldInfo _entityControllerIdBackingField;
        private static PropertyInfo _entityControllerPositionProperty;
        private static MethodInfo _setActiveStatusEffectMethod;
        private static Type _statusEffectEnumType;
        private static Type _durationTypeEnumType;
        [ThreadStatic]
        private static System.Random _threadRng;
        private static System.Random Rng => _threadRng ?? (_threadRng = new System.Random());

#pragma warning disable CS0649
        public static Func<string, float?> MinAttackRangeResolver;
        public static Func<string, List<RogueCommandExpansionLoader.Content.ContentOnHitStatusEffectDefinition>> OnHitStatusEffectsResolver;
#pragma warning restore CS0649

        public static void EntityAttackIsTargetInRangePostfix(object __instance, object __0, ref bool __result)
        {
            if (!__result || __instance == null || __0 == null)
            {
                return;
            }

            try
            {
                if (_entityAttackControllerField == null)
                {
                    _entityAttackControllerField = __instance.GetType().GetField("_entityController",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (_entityAttackControllerField == null)
                    {
                        return;
                    }
                }

                object attackerObj = _entityAttackControllerField.GetValue(__instance);
                if (attackerObj == null)
                {
                    return;
                }

                if (_entityControllerIdProperty == null && _entityControllerIdBackingField == null)
                {
                    Type attackerType = attackerObj.GetType();
                    _entityControllerIdProperty = attackerType.GetProperty("EntityId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (_entityControllerIdProperty == null)
                    {
                        _entityControllerIdBackingField = attackerType.GetField("entityId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                }

                string entityId = null;
                if (_entityControllerIdProperty != null)
                {
                    entityId = _entityControllerIdProperty.GetValue(attackerObj, null) as string;
                }
                else if (_entityControllerIdBackingField != null)
                {
                    entityId = _entityControllerIdBackingField.GetValue(attackerObj) as string;
                }

                if (string.IsNullOrEmpty(entityId))
                {
                    return;
                }

                float? minRange = MinAttackRangeResolver != null ? MinAttackRangeResolver(entityId) : null;
                if (minRange.HasValue && minRange.Value > 0f)
                {
                    if (_entityControllerPositionProperty == null)
                    {
                        _entityControllerPositionProperty = attackerObj.GetType().GetProperty("Position",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    }

                    if (_entityControllerPositionProperty != null)
                    {
                        Vector3 p1 = (Vector3)_entityControllerPositionProperty.GetValue(attackerObj, null);
                        Vector3 p2 = (Vector3)_entityControllerPositionProperty.GetValue(__0, null);
                        float dx = p1.x - p2.x;
                        float dz = p1.z - p2.z;
                        float worldMinRange = minRange.Value <= 25f ? minRange.Value * 10f : minRange.Value;
                        if ((dx * dx + dz * dz) < (worldMinRange * worldMinRange))
                        {
                            __result = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke("[RuntimeHooks] EntityAttackIsTargetInRangePostfix error: " + ex.Message);
            }
        }

        public static void EntityTakeDamagePostfix(object __instance, float __0, object __1)
        {
            if (__instance == null || __1 == null || __0 <= 0f)
            {
                return;
            }

            try
            {
                if (_entityControllerIdProperty == null && _entityControllerIdBackingField == null)
                {
                    Type attackerType = __1.GetType();
                    _entityControllerIdProperty = attackerType.GetProperty("EntityId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (_entityControllerIdProperty == null)
                    {
                        _entityControllerIdBackingField = attackerType.GetField("entityId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                }

                string originatorId = null;
                if (_entityControllerIdProperty != null)
                {
                    originatorId = _entityControllerIdProperty.GetValue(__1, null) as string;
                }
                else if (_entityControllerIdBackingField != null)
                {
                    originatorId = _entityControllerIdBackingField.GetValue(__1) as string;
                }

                if (string.IsNullOrEmpty(originatorId) ||
                    !RuntimeUnitMechanicsDirector.IsHostileTarget(__1, __instance))
                {
                    return;
                }

                RuntimeUnitMechanicsDirector.OnEntityDealtDamage(__instance, __0, __1);

                List<RogueCommandExpansionLoader.Content.ContentOnHitStatusEffectDefinition> effects =
                    OnHitStatusEffectsResolver != null ? OnHitStatusEffectsResolver(originatorId) : null;
                if (effects == null || effects.Count == 0)
                {
                    return;
                }

                if (_setActiveStatusEffectMethod == null)
                {
                    Type ecType = __instance.GetType();
                    Assembly gameAsm = ecType.Assembly;
                    _statusEffectEnumType = gameAsm.GetType("StatusEffect");
                    _durationTypeEnumType = gameAsm.GetType("SetStatusEffect+DurationType") ?? gameAsm.GetType("SetStatusEffect/DurationType");
                    if (_statusEffectEnumType != null && _durationTypeEnumType != null)
                    {
                        _setActiveStatusEffectMethod = ecType.GetMethod("SetActiveStatusEffect",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new Type[] { _statusEffectEnumType, _durationTypeEnumType, typeof(float) },
                            null);
                    }
                }

                if (_setActiveStatusEffectMethod == null || _statusEffectEnumType == null || _durationTypeEnumType == null)
                {
                    return;
                }

                for (int i = 0; i < effects.Count; i++)
                {
                    RogueCommandExpansionLoader.Content.ContentOnHitStatusEffectDefinition effect = effects[i];
                    if (effect == null || string.IsNullOrEmpty(effect.statusEffect) || effect.duration <= 0f)
                    {
                        continue;
                    }

                    if (effect.chance < 1.0f && (float)Rng.NextDouble() > effect.chance)
                    {
                        continue;
                    }

                    try
                    {
                        object parsedEffect = Enum.Parse(_statusEffectEnumType, effect.statusEffect, true);
                        object durationTypeSeconds = Enum.ToObject(_durationTypeEnumType, 1);
                        _setActiveStatusEffectMethod.Invoke(__instance, new object[] { parsedEffect, durationTypeSeconds, effect.duration });
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke("[RuntimeHooks] EntityTakeDamagePostfix error: " + ex.Message);
            }
        }

        public static void EntityControllerDiePostfix(object __instance)
        {
            if (__instance == null)
            {
                return;
            }

            try
            {
                RuntimeUnitMechanicsDirector.OnEntityDied(__instance);
            }
            catch (Exception exception)
            {
                Log("EntityControllerDiePostfix failed safely: " + exception.Message);
            }
        }

        public static void EntityControllerHealPostfix(object __instance, float __0)
        {
            if (__instance == null)
            {
                return;
            }

            try
            {
                RuntimeUnitMechanicsDirector.OnEntityHealed(__instance, __0);
            }
            catch (Exception exception)
            {
                Log("EntityControllerHealPostfix failed safely: " + exception.Message);
            }
        }

        public static void EntityControllerRepairArmorPostfix(object __instance, float __0)
        {
            if (__instance == null)
            {
                return;
            }

            try
            {
                RuntimeUnitMechanicsDirector.OnEntityRepairedArmor(__instance, __0);
            }
            catch (Exception exception)
            {
                Log("EntityControllerRepairArmorPostfix failed safely: " + exception.Message);
            }
        }

        public static bool TryLoadResource(string resourcePath, out UnityEngine.Object asset)
        {
            asset = null;
            IRuntimeContentProvider provider;
            lock (Gate)
            {
                provider = _provider;
            }

            if (provider == null || String.IsNullOrEmpty(resourcePath))
            {
                return false;
            }

            try
            {
                if (provider.TryLoadResource(resourcePath, out asset) && asset != null)
                {
                    return true;
                }
            }
            catch (Exception exception)
            {
                Log("Resource provider failed for '" + resourcePath + "': " + exception.Message);
            }

            asset = null;
            return false;
        }

        private static RuntimeContentMutator GetMutator()
        {
            lock (Gate)
            {
                return _mutator;
            }
        }

        private static readonly System.Random _guaranteedRewardRandom = new System.Random();

        private static IGuaranteedModCardRewardProvider GetGuaranteedRewardProvider()
        {
            lock (Gate)
            {
                return _provider as IGuaranteedModCardRewardProvider;
            }
        }

        private static IBlueprintPoolRuntimeProvider GetBlueprintPoolProvider()
        {
            lock (Gate)
            {
                return _provider as IBlueprintPoolRuntimeProvider;
            }
        }

        private static void SafeCall(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Log("Runtime hook failed safely: " + exception.Message);
            }
        }

        private static void Log(string message)
        {
            Action<string> logger;
            lock (Gate)
            {
                logger = _log;
            }

            if (logger == null)
            {
                return;
            }

            try
            {
                logger("[ExpansionLoader] " + message);
            }
            catch
            {
                // Logging failures are isolated from the game thread.
            }
        }
        public static void AfterInstantiateEntity(object result)
        {
            if (result == null)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref _unitCreationHookCount);

            Action<object> callback;
            lock (Gate)
            {
                callback = OnEntityInstantiated;
            }

            if (callback != null)
            {
                SafeCall(delegate { callback(result); });
            }
            else
            {
                SafeCall(delegate
                {
                    Visual.RuntimeVisualDirector.AttachIfApplicable(result);
                    RuntimeUnitMechanicsDirector.AttachIfApplicable(result);
                    RuntimeGameplayCommandDirector.ApplyAiProfileUnitDefaultsToEntity(result);
                });
            }
        }

        public static void AfterCreateBuildingPlacementGhost(string entityId, GameObject ghost)
        {
            System.Threading.Interlocked.Increment(ref _placementGhostHookCount);
            if (ghost == null)
            {
                return;
            }

            SafeCall(delegate
            {
                Visual.RuntimeVisualDirector.AttachIfApplicable(ghost);
                RuntimeGameplayCommandDirector.OnPlacementGhostCreated(entityId, ghost);
            });
        }

        public static void BeforeEntityResetForReuse(object entityObj)
        {
            if (entityObj == null)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref _unitReuseHookCount);
            SafeCall(delegate
            {
                int key = RuntimeUnitMechanicsDirector.GetEntityTrackingKey(entityObj);
                if (key != 0)
                {
                    lock (_activeRegisteredEntities)
                    {
                        _activeRegisteredEntities.Remove(key);
                    }
                }

                RuntimeUnitMechanicsDirector.OnEntityDied(entityObj);
            });
        }

        public static void AfterEntityReused(object entityObj)
        {
            if (entityObj == null)
            {
                return;
            }

            AfterInstantiateEntity(entityObj);
        }

        public static void AfterEntityProduce(object factoryObj, bool instantProduction, bool forFree, bool doNotTriggerEvent)
        {
            if (factoryObj == null)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref _factoryProduceHookCount);
            SafeCall(delegate
            {
                RuntimeGameplayCommandDirector.OnFactoryProduced(factoryObj, instantProduction, forFree, doNotTriggerEvent);
            });
        }

        public static void OnEntityRegistryReset(object existingControllersObj)
        {
            System.Threading.Interlocked.Increment(ref _entityRegistryResetCount);
            lock (_activeRegisteredEntities)
            {
                _activeRegisteredEntities.Clear();
            }

            SafeCall(delegate
            {
                RuntimeUnitMechanicsDirector.OnBattlefieldRegistryReset();
                RuntimeGameplayCommandDirector.OnBattlefieldRegistryReset();
            });
        }

        public static void OnEntityRegistered(object entityObj, bool hasBeenCalledFromAbove)
        {
            if (entityObj == null)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref _entityRegisteredCount);
            SafeCall(delegate
            {
                int key = RuntimeUnitMechanicsDirector.GetEntityTrackingKey(entityObj);
                if (key != 0)
                {
                    lock (_activeRegisteredEntities)
                    {
                        _activeRegisteredEntities.Add(key);
                    }
                }

                Visual.RuntimeVisualDirector.AttachIfApplicable(entityObj);
                RuntimeUnitMechanicsDirector.AttachIfApplicable(entityObj);
                RuntimeGameplayCommandDirector.ApplyAiProfileUnitDefaultsToEntity(entityObj);
            });
        }

        public static void OnEntityUnregistered(object entityObj, object originatorObj)
        {
            if (entityObj == null)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref _entityUnregisteredCount);
            SafeCall(delegate
            {
                int key = RuntimeUnitMechanicsDirector.GetEntityTrackingKey(entityObj);
                if (key != 0)
                {
                    lock (_activeRegisteredEntities)
                    {
                        _activeRegisteredEntities.Remove(key);
                    }
                }

                RuntimeUnitMechanicsDirector.OnEntityDestroyed(entityObj);
            });
        }

        public static void OnEntityInstantiatedTranspiler(object entityObj, bool hasBeenCalledFromAbove)
        {
            if (entityObj == null)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref _unitCreationTranspilerCount);
            SafeCall(delegate
            {
                Visual.RuntimeVisualDirector.AttachIfApplicable(entityObj);
                RuntimeUnitMechanicsDirector.AttachIfApplicable(entityObj);
                RuntimeGameplayCommandDirector.ApplyAiProfileUnitDefaultsToEntity(entityObj);
            });
        }

        public static void OnEntityRegisteredTranspiler(object existingControllersObj, object entityObj, bool hasBeenCalledFromAbove)
        {
            if (entityObj == null)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref _entityRegisteredTranspilerCount);
            SafeCall(delegate
            {
                int key = RuntimeUnitMechanicsDirector.GetEntityTrackingKey(entityObj);
                if (key != 0)
                {
                    lock (_activeRegisteredEntities)
                    {
                        _activeRegisteredEntities.Add(key);
                    }
                }

                Visual.RuntimeVisualDirector.AttachIfApplicable(entityObj);
                RuntimeUnitMechanicsDirector.AttachIfApplicable(entityObj);
                RuntimeGameplayCommandDirector.ApplyAiProfileUnitDefaultsToEntity(entityObj);
            });
        }

        public static void OnEntityWillBeDestroyedTranspiler(object existingControllersObj, object entityObj, object originatorObj)
        {
            if (entityObj == null)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref _entityUnregisteredTranspilerCount);
            SafeCall(delegate
            {
                int key = RuntimeUnitMechanicsDirector.GetEntityTrackingKey(entityObj);
                if (key != 0)
                {
                    lock (_activeRegisteredEntities)
                    {
                        _activeRegisteredEntities.Remove(key);
                    }
                }

                RuntimeUnitMechanicsDirector.OnEntityDestroyed(entityObj);
            });
        }

        public static void OnEntityResetForReuseTranspiler(object entityObj)
        {
            if (entityObj == null)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref _unitReuseTranspilerCount);
            SafeCall(delegate
            {
                int key = RuntimeUnitMechanicsDirector.GetEntityTrackingKey(entityObj);
                if (key != 0)
                {
                    lock (_activeRegisteredEntities)
                    {
                        _activeRegisteredEntities.Remove(key);
                    }
                }

                RuntimeUnitMechanicsDirector.OnEntityDied(entityObj);
            });
        }

        public static void OnEntityExecuteCommand(object entityObj, object[] args)
        {
            if (entityObj == null)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref _commandExecutedCount);
            SafeCall(delegate
            {
                string commandKind = "Unknown";
                string processingType = "ExecuteAndCancelCurrentCommand";
                string commandTag = string.Empty;

                if (args != null && args.Length > 0 && args[0] != null)
                {
                    object cmdObj = args[0];
                    Type cmdType = cmdObj.GetType();
                    FieldInfo cmdField = _cachedCommandKindField;
                    if (cmdField == null || cmdField.DeclaringType != cmdType)
                    {
                        cmdField = cmdType.GetField("command", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        _cachedCommandKindField = cmdField;
                    }

                    if (cmdField != null)
                    {
                        object val = cmdField.GetValue(cmdObj);
                        if (val != null)
                        {
                            commandKind = val.ToString();
                        }
                    }
                    else
                    {
                        commandKind = cmdObj.ToString();
                    }

                    FieldInfo tagField = _cachedCommandTagField;
                    if (tagField == null || tagField.DeclaringType != cmdType)
                    {
                        tagField = cmdType.GetField("tag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        _cachedCommandTagField = tagField;
                    }

                    if (tagField != null)
                    {
                        object tagVal = tagField.GetValue(cmdObj);
                        if (tagVal != null)
                        {
                            commandTag = tagVal.ToString();
                        }
                    }
                }

                if (args != null && args.Length > 1 && args[1] != null)
                {
                    processingType = args[1].ToString();
                }

                lock (Gate)
                {
                    _lastExecutedCommandKind = commandKind ?? "Unknown";
                }

                RuntimeGameplayCommandDirector.OnEntityCommandExecuted(entityObj, commandKind, processingType, commandTag);
            });
        }

        public static void OnEntityExecuteNextCommand(object entityObj)
        {
            if (entityObj == null)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref _commandNextStepCount);
            SafeCall(delegate
            {
                RuntimeGameplayCommandDirector.OnEntityCommandChainStep(entityObj);
            });
        }

        public static void OnEntityResetCurrentCommand(object entityObj)
        {
            if (entityObj == null)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref _commandResetCount);
            SafeCall(delegate
            {
                RuntimeGameplayCommandDirector.OnEntityCommandReset(entityObj);
            });
        }

        public static void OnEntityDeleteCommandChain(object entityObj)
        {
            if (entityObj == null)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref _commandChainDeletedCount);
            SafeCall(delegate
            {
                RuntimeGameplayCommandDirector.OnEntityCommandChainDeleted(entityObj);
            });
        }
    }

    public static class RuntimeHarmonyPatches
    {
        public static void PresentCardsToChoosePrefix(out object __state)
        {
            __state = RuntimeHooks.BeginBlueprintRewardScope();
        }

        public static void InstantiateCardsWithDelayPrefix(List<string> __0)
        {
            RuntimeHooks.EnsureGuaranteedModCardOption(__0);
        }

        public static Exception PresentCardsToChooseFinalizer(Exception __exception, object __state)
        {
            return RuntimeHooks.EndBlueprintRewardScope(__exception, __state);
        }

        public static void BlueprintPoolBlueprintsInPoolPostfix(
            object __instance,
            List<string> __result,
            int? __0,
            bool __1)
        {
            RuntimeHooks.ObserveBlueprintPoolCandidates(__instance, __result, __0, __1);
        }

        public static void PresentCardAsRewardRecordPostfix(string __3, string __4)
        {
            RuntimeHooks.ObserveBlueprintRewardRecord(__3, __4);
        }

        public static void AllEntityIdsAllowedAsBlueprintsPostfix(object[] __args, ref List<string> __result)
        {
            RuntimeHooks.AddGlobalBlueprintRewardCandidates(__args, ref __result);
            RuntimeHooks.FilterGlobalBlueprintRewardCandidates(__result);
        }

        public static void FallbackLandscapeGeneratorsPostfix(ref List<global::LandscapeGenerator> __result)
        {
            __result = (List<global::LandscapeGenerator>)RuntimeHooks.ResolveFallbackLandscapes(__result);
        }

        public static void EntityFactorySetupPrespawningPostfix()
        {
            RuntimeHooks.OnSetupPrespawning();
        }

        public static void EntityFactorySetupAiPrespawningPostfix()
        {
            RuntimeHooks.OnSetupAiPrespawning();
        }

        public static void EntityBalancingStoreInitPostfix()
        {
            RuntimeHooks.AfterEntityBalancingInit();
        }

        public static void SetScriptableObjectPostfix(object[] __args)
        {
            RuntimeHooks.AfterSetScriptableObject(__args);
        }

        public static bool OutlyingStartAiBuildingDistancePrefix(ref int __result)
        {
            return RuntimeHooks.OutlyingStartAiBuildingDistancePrefix(ref __result);
        }

        public static void CreateStageMapPrefix()
        {
            RuntimeHooks.BeforeProgressionMap();
        }

        public static void CreateStageMapPostfix(object __result)
        {
            RuntimeHooks.AfterStageMapCreated(__result);
        }

        public static void StageMapJsonConstructorPostfix(object __instance, string __0)
        {
            RuntimeHooks.AfterStageMapDeserialized(__instance, __0);
        }

        public static void StageMapToJsonPostfix(object __instance, ref string __result)
        {
            RuntimeHooks.AfterStageMapSerialized(__instance, ref __result);
        }

        public static void CreateLevelMapPrefix(object __0, out object __state)
        {
            RuntimeHooks.BeginLevelMapScope(__0, out __state);
        }

        public static Exception CreateLevelMapFinalizer(Exception __exception, object __state)
        {
            return RuntimeHooks.EndLevelMapScope(__exception, __state);
        }

        public static void InstantiateEntityPrefix()
        {
            RuntimeHooks.BeforeEntityFactory();
        }

        public static void InstantiateEntityPostfix(object __result)
        {
            RuntimeHooks.AfterInstantiateEntity(__result);
        }

        public static bool EntityControllerIsProductionPossiblePrefix(object __instance, ref bool __result)
        {
            if (RuntimeUnitMechanicsDirector.ShouldBlockAiProduction(__instance))
            {
                __result = false;
                return false;
            }

            return true;
        }

        public static void EntityControllerFixedUpdatePrefix(object __instance)
        {
            RuntimeUnitMechanicsDirector.ApplyNativeAiSkillActivationInterval(__instance, false);
        }

        public static void EntityControllerFixedUpdatePostfix(object __instance)
        {
            RuntimeUnitMechanicsDirector.ApplyNativeAiSkillActivationInterval(__instance, true);
        }

        public static void CreateEntityMeshPrefix()
        {
            RuntimeHooks.BeforeEntityFactory();
        }

        public static void DefaultAiBehaviourInitPostfix(object __instance)
        {
            RuntimeHooks.DefaultAiBehaviourInitPostfix(__instance);
        }

        public static void AiBehaviourUpdatePostfix(object __instance)
        {
            RuntimeHooks.AiBehaviourUpdatePostfix(__instance);
        }

        public static void AiBehaviourDestroyPostfix(object __instance)
        {
            RuntimeHooks.AiBehaviourDestroyPostfix(__instance);
        }

        public static bool BlueprintNamePrefix(string __0, ref string __result)
        {
            return RuntimeHooks.BlueprintNamePrefix(__0, ref __result);
        }

        public static bool BlueprintDescriptionPrefix(string __0, ref string __result)
        {
            return RuntimeHooks.BlueprintDescriptionPrefix(__0, ref __result);
        }

        public static bool SkillDescriptionPrefix(string __0, ref string __result)
        {
            return RuntimeHooks.SkillDescriptionPrefix(__0, ref __result);
        }

        public static bool RelicNamePrefix(string __0, ref string __result)
        {
            return RuntimeHooks.RelicNamePrefix(__0, ref __result);
        }

        public static bool RelicDescriptionPrefix(string __0, ref string __result)
        {
            return RuntimeHooks.RelicDescriptionPrefix(__0, ref __result);
        }

        public static bool UpgradeNamePrefix(string __0, ref string __result)
        {
            return RuntimeHooks.UpgradeNamePrefix(__0, ref __result);
        }

        public static bool UpgradeDescriptionPrefix(string __0, ref string __result)
        {
            return RuntimeHooks.UpgradeDescriptionPrefix(__0, ref __result);
        }

        public static bool LandscapeLocalizationPrefix(ref string __0, ref string __result)
        {
            return RuntimeHooks.LandscapeLocalizationPrefix(ref __0, ref __result);
        }

        public static Exception LandscapeLocalizationFinalizer(Exception __exception, ref string __result)
        {
            return RuntimeHooks.LandscapeLocalizationFinalizer(__exception, ref __result);
        }

        public static bool BlueprintPoolIsInPoolPrefix(object __instance, string __0, ref bool __result)
        {
            return RuntimeHooks.BlueprintPoolIsInPoolPrefix(__instance, __0, ref __result);
        }

        public static bool BlueprintPoolFindPrefix(string __0, ref global::BlueprintPool __result)
        {
            return RuntimeHooks.BlueprintPoolFindPrefix(__0, ref __result);
        }

        public static void BlueprintPoolExclusiveBlueprintsPostfix(string __0, HashSet<string> __result)
        {
            RuntimeHooks.BlueprintPoolExclusiveBlueprintsPostfix(__0, __result);
        }

        public static void EntityAttackIsTargetInRangePostfix(object __instance, object __0, ref bool __result)
        {
            RuntimeHooks.EntityAttackIsTargetInRangePostfix(__instance, __0, ref __result);
        }

        public static void EntityControllerDiePostfix(object __instance)
        {
            RuntimeHooks.EntityControllerDiePostfix(__instance);
        }

        public static void EntityControllerHealPostfix(object __instance, float __0)
        {
            RuntimeHooks.EntityControllerHealPostfix(__instance, __0);
        }

        public static void EntityControllerDestroyPostfix(object __instance)
        {
            RuntimeUnitMechanicsDirector.OnEntityDestroyed(__instance);
        }

        public static void EntityControllerRepairArmorPostfix(object __instance, float __0)
        {
            RuntimeHooks.EntityControllerRepairArmorPostfix(__instance, __0);
        }

        public static void EntityTakeDamagePostfix(object __instance, float __0, object __1)
        {
            RuntimeHooks.EntityTakeDamagePostfix(__instance, __0, __1);
        }

        public static void CreateBuildingPlacementGhostPrefix()
        {
            RuntimeHooks.BeforeEntityFactory();
        }

        public static void CreateBuildingPlacementGhostPostfix(string __0, GameObject __result)
        {
            RuntimeHooks.AfterCreateBuildingPlacementGhost(__0, __result);
        }

        public static void InitEntityControllerPrefix()
        {
            RuntimeHooks.BeforeEntityFactory();
        }

        public static void InitEntityControllerPostfix(object __result)
        {
            RuntimeHooks.AfterInstantiateEntity(__result);
        }

        public static void EntityControllerResetAndInitForReusePrefix(object __instance)
        {
            RuntimeHooks.BeforeEntityResetForReuse(__instance);
        }

        public static void EntityControllerOnHasBeenInstantiatedForReusePostfix(object __instance)
        {
            RuntimeHooks.AfterEntityReused(__instance);
        }

        public static void EntityControllerProducePrefix()
        {
            RuntimeHooks.BeforeEntityFactory();
        }

        public static void EntityControllerProducePostfix(object __instance, bool __0, bool __1, bool __2)
        {
            RuntimeHooks.AfterEntityProduce(__instance, __0, __1, __2);
        }

        public static void ExistingControllersInitPostfix(object __instance)
        {
            RuntimeHooks.OnEntityRegistryReset(__instance);
        }

        public static void ExistingControllersOnEntityCreatedPostfix(object __0, bool __1)
        {
            RuntimeHooks.OnEntityRegistered(__0, __1);
        }

        public static void ExistingControllersOnEntityWillBeDestroyedPostfix(object __0, object __1)
        {
            RuntimeHooks.OnEntityUnregistered(__0, __1);
        }

        public static void EntityControllerExecuteCommandPostfix(object __instance, object[] __args)
        {
            RuntimeHooks.OnEntityExecuteCommand(__instance, __args);
        }

        public static void EntityControllerExecuteNextCommandPostfix(object __instance)
        {
            RuntimeHooks.OnEntityExecuteNextCommand(__instance);
        }

        public static void EntityControllerResetCurrentCommandPostfix(object __instance)
        {
            RuntimeHooks.OnEntityResetCurrentCommand(__instance);
        }

        public static void EntityControllerDeleteCommandChainPostfix(object __instance)
        {
            RuntimeHooks.OnEntityDeleteCommandChain(__instance);
        }

        public static void OnEntityInstantiatedTranspilerHook(object entity, bool hasBeenCalledFromAbove)
        {
            RuntimeHooks.OnEntityInstantiatedTranspiler(entity, hasBeenCalledFromAbove);
        }

        public static void OnEntityRegisteredTranspilerHook(object existingControllers, object entity, bool hasBeenCalledFromAbove)
        {
            RuntimeHooks.OnEntityRegisteredTranspiler(existingControllers, entity, hasBeenCalledFromAbove);
        }

        public static void OnEntityWillBeDestroyedTranspilerHook(object existingControllers, object entity, object originator)
        {
            RuntimeHooks.OnEntityWillBeDestroyedTranspiler(existingControllers, entity, originator);
        }

        public static void OnEntityResetForReuseTranspilerHook(object entity)
        {
            RuntimeHooks.OnEntityResetForReuseTranspiler(entity);
        }
    }

    internal static class UnityResourcesLoadUntypedPatch
    {
        public static bool Prefix(string path, ref UnityEngine.Object __result)
        {
            if (String.IsNullOrEmpty(path) || !ResourceLoadRecursionGuard.TryEnter(path))
            {
                return true;
            }

            try
            {
                UnityEngine.Object asset;
                if (RuntimeHooks.TryLoadResource(path, out asset) && asset != null)
                {
                    __result = asset;
                    return false;
                }

                return true;
            }
            finally
            {
                ResourceLoadRecursionGuard.Exit(path);
            }
        }
    }

    internal static class UnityResourcesLoadTypedPatch
    {
        private static UnityEngine.Object _fallbackTmpSettings;

        /// <summary>
        /// Patch the shared Resources.Load(string, Type) implementation used by generic and
        /// non-generic wrappers. Every requested asset type follows one type-checked path.
        /// </summary>
        public static bool Prefix(string path, Type systemTypeInstance, ref UnityEngine.Object __result)
        {
            if (String.IsNullOrEmpty(path) || systemTypeInstance == null)
            {
                return true;
            }

            // Protect against a provider accidentally re-entering Resources.Load for its own path.
            if (!ResourceLoadRecursionGuard.TryEnter(path))
            {
                return true;
            }

            try
            {
                UnityEngine.Object asset;
                if (RuntimeHooks.TryLoadResource(path, out asset) &&
                    systemTypeInstance.IsInstanceOfType(asset))
                {
                    __result = asset;
                    return false;
                }

                if (String.Equals(path, "TMP Settings", StringComparison.Ordinal) ||
                    String.Equals(path, "TMP Settings.asset", StringComparison.Ordinal))
                {
                    if (_fallbackTmpSettings == null && typeof(ScriptableObject).IsAssignableFrom(systemTypeInstance))
                    {
                        try
                        {
                            ScriptableObject settings = ScriptableObject.CreateInstance(systemTypeInstance);
                            settings.name = "Fallback TMP Settings";
                            settings.hideFlags = HideFlags.HideAndDontSave;

                            // 1. m_defaultStyleSheet with "Normal" style initialized
                            FieldInfo styleSheetField = systemTypeInstance.GetField("m_defaultStyleSheet",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (styleSheetField != null && typeof(ScriptableObject).IsAssignableFrom(styleSheetField.FieldType))
                            {
                                ScriptableObject sheet = ScriptableObject.CreateInstance(styleSheetField.FieldType);
                                sheet.name = "Fallback TMP Style Sheet";
                                sheet.hideFlags = HideFlags.HideAndDontSave;
                                MethodInfo refreshStyles = styleSheetField.FieldType.GetMethod("RefreshStyles",
                                    BindingFlags.Public | BindingFlags.Instance);
                                if (refreshStyles != null)
                                {
                                    refreshStyles.Invoke(sheet, null);
                                }
                                styleSheetField.SetValue(settings, sheet);
                            }

                            // 2. m_linebreakingRules initialized with empty dictionaries
                            FieldInfo linebreakingField = systemTypeInstance.GetField("m_linebreakingRules",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (linebreakingField != null)
                            {
                                Type tableType = linebreakingField.FieldType;
                                object tableInstance = Activator.CreateInstance(tableType);
                                FieldInfo leadingField = tableType.GetField("leadingCharacters", BindingFlags.Public | BindingFlags.Instance);
                                FieldInfo followingField = tableType.GetField("followingCharacters", BindingFlags.Public | BindingFlags.Instance);
                                if (leadingField != null)
                                {
                                    leadingField.SetValue(tableInstance, Activator.CreateInstance(leadingField.FieldType));
                                }
                                if (followingField != null)
                                {
                                    followingField.SetValue(tableInstance, Activator.CreateInstance(followingField.FieldType));
                                }
                                linebreakingField.SetValue(settings, tableInstance);
                            }

                            // 3. m_fallbackFontAssets non-null list
                            FieldInfo fallbackFontsField = systemTypeInstance.GetField("m_fallbackFontAssets",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (fallbackFontsField != null)
                            {
                                fallbackFontsField.SetValue(settings, Activator.CreateInstance(fallbackFontsField.FieldType));
                            }

                            // 4. Safe non-null path strings
                            FieldInfo fontPathField = systemTypeInstance.GetField("m_defaultFontAssetPath",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (fontPathField != null) fontPathField.SetValue(settings, string.Empty);
                            FieldInfo spritePathField = systemTypeInstance.GetField("m_defaultSpriteAssetPath",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (spritePathField != null) spritePathField.SetValue(settings, string.Empty);
                            FieldInfo stylePathField = systemTypeInstance.GetField("m_StyleSheetsResourcePath",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (stylePathField != null) stylePathField.SetValue(settings, string.Empty);
                            FieldInfo gradPathField = systemTypeInstance.GetField("m_defaultColorGradientPresetsPath",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (gradPathField != null) gradPathField.SetValue(settings, string.Empty);

                            // 5. Assign static s_Instance so TMP_Settings.instance returns immediately
                            FieldInfo instanceField = systemTypeInstance.GetField("s_Instance",
                                BindingFlags.NonPublic | BindingFlags.Static);
                            if (instanceField != null)
                            {
                                instanceField.SetValue(null, settings);
                            }

                            _fallbackTmpSettings = settings;
                        }
                        catch
                        {
                            // Fallback creation failure passes through to engine.
                        }
                    }

                    if (_fallbackTmpSettings != null && systemTypeInstance.IsInstanceOfType(_fallbackTmpSettings))
                    {
                        __result = _fallbackTmpSettings;
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                ResourceLoadRecursionGuard.Exit(path);
            }
        }
    }

    internal static class ResourceLoadRecursionGuard
    {
        [ThreadStatic]
        private static HashSet<string> _activePaths;

        public static bool TryEnter(string path)
        {
            if (_activePaths == null)
            {
                _activePaths = new HashSet<string>(StringComparer.Ordinal);
            }

            return _activePaths.Add(path);
        }

        public static void Exit(string path)
        {
            if (_activePaths != null)
            {
                _activePaths.Remove(path);
            }
        }
    }
}
