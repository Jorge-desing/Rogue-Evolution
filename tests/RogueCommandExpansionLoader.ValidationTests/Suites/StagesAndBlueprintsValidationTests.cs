#nullable enable
using UnityEngine;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.RegularExpressions;
using RogueCommandExpansionLoader.Compatibility;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Modules;
using RogueCommandExpansionLoader.Runtime;
using RogueCommandExpansionLoader.Runtime.Visual;

internal static partial class ValidationTestRunner
{
    internal static IEnumerable<(string Name, Action Run)> GetStagesAndBlueprintsValidationTests() => new (string Name, Action Run)[]
    {
    ("stage parameter binding contract accepts the native value-type reward fixture", () =>
    {
        Type? contractType = typeof(ContentPackDefinition).Assembly.GetType(
            "RogueCommandExpansionLoader.Content.StageParameterBindingContract");
        MethodInfo? resolveMethod = contractType?.GetMethod("Resolve",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (contractType == null || resolveMethod == null)
        {
            throw new InvalidOperationException("The production stage binding contract is not exposed to offline validation.");
        }

        object? bindings = resolveMethod.Invoke(null,
            [typeof(StageParametersScriptableObject), typeof(BlueprintPoolList),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic]);
        (string propertyName, string fieldName, Type fieldType)[] expectedFields =
        [
            ("BlueprintPoolListField", "blueprintPoolList", typeof(BlueprintPoolList)),
            ("FirstLevelCardsField", "fillFirstLevelWithCardRewards", typeof(bool)),
            ("DefaultCenterField", "putDefaultCardRewardInCenter", typeof(bool))
        ];
        foreach ((string propertyName, string fieldName, Type fieldType) expected in expectedFields)
        {
            PropertyInfo? property = contractType.GetProperty(expected.propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo? field = property?.GetValue(bindings) as FieldInfo;
            if (field == null || field.DeclaringType != typeof(StageParametersScriptableObject) ||
                field.Name != expected.fieldName || field.FieldType != expected.fieldType)
            {
                throw new InvalidOperationException("The stage binding contract resolved an unexpected field: " +
                    expected.fieldName + ".");
            }
        }

        FieldInfo? cardRewardField = typeof(StageParametersScriptableObject).GetField(
            "cardReward", BindingFlags.Instance | BindingFlags.Public);
        if (cardRewardField == null || !cardRewardField.FieldType.IsValueType)
        {
            throw new InvalidOperationException("StageParametersScriptableObject.cardReward must use its native value-type fixture.");
        }
    }),
    ("pool-specific entries do not authorize sibling pools in the same stage", () =>
    {
        var registry = new BlueprintPoolScopeRegistry();
        var firstPool = new object();
        var siblingPool = new object();
        registry.Register(new[]
        {
            new BlueprintPoolScopeRegistration("RCL_POOL_SCOPED_A", "sample:stage", firstPool,
                new[] { "sample:alpha" }, false, "native:a"),
            new BlueprintPoolScopeRegistration("RCL_POOL_SCOPED_B", "sample:stage", siblingPool,
                new[] { "sample:beta" }, false, "native:b")
        });
        if (!registry.AllowsModUnitInPool(firstPool, "sample:alpha") ||
            registry.AllowsModUnitInPool(firstPool, "sample:beta") ||
            !registry.AllowsModUnitInPool(siblingPool, "sample:beta"))
            throw new InvalidOperationException("A stage's pool-specific unit allowlist leaked to a sibling pool.");
        var unregisteredPool = new object();
        if (!registry.ContainsPool(firstPool) ||
            !registry.ContainsPool(siblingPool) ||
            registry.ContainsPool(unregisteredPool) ||
            registry.ContainsPool(null))
        {
            throw new InvalidOperationException("ContainsPool must identify registered mod pools by object identity.");
        }
    }),
    ("blueprint pool registrations expose immutable, deterministic unit-ID snapshots", () =>
    {
        var suppliedUnitIds = new List<string> { "sample:zeta", "sample:alpha" };
        var registration = new BlueprintPoolScopeRegistration(
            "RCL_POOL_SNAPSHOT", "sample:stage", new object(), suppliedUnitIds,
            blueprintsAreExclusive: false);
        suppliedUnitIds.Clear();

        IReadOnlyList<string> unitIds = registration.UnitIds;
        if (unitIds.Count != 2 || unitIds[0] != "sample:alpha" || unitIds[1] != "sample:zeta")
        {
            throw new InvalidOperationException(
                "Blueprint pool unit IDs must be an immutable snapshot in ordinal order.");
        }

        var reverseRegistration = new BlueprintPoolScopeRegistration(
            "RCL_POOL_REVERSED", "sample:stage", new object(),
            new[] { "sample:alpha", "sample:zeta" }, blueprintsAreExclusive: false);
        IReadOnlyList<string> reversedUnitIds = reverseRegistration.UnitIds;
        if (reversedUnitIds.Count != unitIds.Count ||
            reversedUnitIds[0] != unitIds[0] || reversedUnitIds[1] != unitIds[1])
        {
            throw new InvalidOperationException(
                "Blueprint pool unit-ID ordering must not depend on input enumeration order.");
        }
    }),
    ("stage-scoped blueprint pools use object identity and isolate mod eligibility", () =>
    {
        var registry = new BlueprintPoolScopeRegistry();
        var firstPool = new object();
        var secondPool = new object();
        var otherStagePool = new object();
        registry.Register(new[]
        {
            new BlueprintPoolScopeRegistration("RCL_POOL_FIRST", "sample:stage-one", firstPool,
                new[] { "sample:first-unit" }, blueprintsAreExclusive: true),
            new BlueprintPoolScopeRegistration("RCL_POOL_SECOND", "sample:stage-one", secondPool,
                new[] { "sample:second-unit" }, blueprintsAreExclusive: true),
            new BlueprintPoolScopeRegistration("RCL_POOL_OTHER_STAGE", "sample:stage-two", otherStagePool,
                new[] { "sample:other-stage-unit" }, blueprintsAreExclusive: true)
        });

        if (!registry.TryFind("RCL_POOL_FIRST", out object? resolved) ||
            !ReferenceEquals(resolved, firstPool) ||
            !registry.AllowsModUnitInPool(firstPool, "sample:first-unit") ||
            !registry.AllowsModUnitInPool(firstPool, "sample:first-unit", isDemo: false, isAllowedForDemo: false) ||
            !registry.AllowsModUnitInPool(firstPool, "sample:first-unit", isDemo: true, isAllowedForDemo: true) ||
            registry.AllowsModUnitInPool(firstPool, "sample:first-unit", isDemo: true, isAllowedForDemo: false) ||
            !registry.AllowsModUnitInPool(firstPool, "sample:first-unit", isDemo: false,
                isAllowedForDemo: true, isStartingBlueprintSelection: true,
                isAllowedAsStartingBlueprint: true) ||
            registry.AllowsModUnitInPool(firstPool, "sample:first-unit", isDemo: false,
                isAllowedForDemo: true, isStartingBlueprintSelection: true,
                isAllowedAsStartingBlueprint: false) ||
            !registry.AllowsModUnitInPool(firstPool, "sample:first-unit", isDemo: false,
                isAllowedForDemo: true, isStartingBlueprintSelection: false,
                isAllowedAsStartingBlueprint: false) ||
            registry.AllowsModUnitInPool(firstPool, "sample:second-unit") ||
            registry.AllowsModUnitInPool(firstPool, "sample:second-unit", isDemo: true, isAllowedForDemo: true) ||
            registry.AllowsModUnitInPool(new object(), "sample:first-unit"))
        {
            throw new InvalidOperationException(
                "Scoped eligibility escaped its exact cloned pool instance or ignored demo/starting-blueprint flags.");
        }

        registry.SetEligibleExclusiveUnitIds(firstPool, new[] { "sample:first-unit" });
        registry.SetEligibleExclusiveUnitIds(secondPool, new[] { "sample:second-unit", "native:unit" });
        registry.SetEligibleExclusiveUnitIds(otherStagePool, new[] { "sample:other-stage-unit" });
        var exclusive = new HashSet<string>(StringComparer.Ordinal);
        registry.AddExclusiveModUnits("RCL_POOL_FIRST", exclusive);
        if (!exclusive.SetEquals(new[] { "sample:second-unit" }))
        {
            throw new InvalidOperationException(
                "Exclusive scopes leaked a native unit, another stage's unit, or the current pool's unit.");
        }

        var nativePoolExclusives = new HashSet<string>(StringComparer.Ordinal);
        registry.AddExclusiveModUnits("native:pool", nativePoolExclusives);
        if (nativePoolExclusives.Count != 0)
        {
            throw new InvalidOperationException("A cloned stage's exclusive IDs leaked into a native pool.");
        }

        var globalFallbackExclusives = new HashSet<string>(StringComparer.Ordinal);
        registry.AddExclusiveModUnits(null, globalFallbackExclusives);
        if (!globalFallbackExclusives.SetEquals(new[]
            { "sample:first-unit", "sample:second-unit", "sample:other-stage-unit" }))
        {
            throw new InvalidOperationException("The no-pool reward fallback omitted eligible exclusive mod units.");
        }

        bool duplicateRejected = false;
        try
        {
            registry.Register(new[]
            {
                new BlueprintPoolScopeRegistration("RCL_POOL_UNCOMMITTED", "sample:stage-three", new object(),
                    new[] { "sample:third-unit" }, blueprintsAreExclusive: false),
                new BlueprintPoolScopeRegistration("RCL_POOL_FIRST", "sample:stage-three", new object(),
                    new[] { "sample:fourth-unit" }, blueprintsAreExclusive: false)
            });
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }

        if (!duplicateRejected || registry.ContainsPoolId("RCL_POOL_UNCOMMITTED"))
        {
            throw new InvalidOperationException("A duplicate scoped pool registration was not rejected atomically.");
        }
    }),
    ("random reward fallback only retains loader factories declared by the active stage", () =>
    {
        var loaderFactories = new HashSet<string>(new[]
        {
            "sample:factory-stage-one",
            "sample:factory-stage-two"
        }, StringComparer.Ordinal);
        var activeStageFactories = new HashSet<string>(new[]
        {
            "sample:factory-stage-one"
        }, StringComparer.Ordinal);
        var scope = new BlueprintRewardFactoryScope(loaderFactories, activeStageFactories);

        loaderFactories.Add("sample:factory-added-after-snapshot");
        activeStageFactories.Add("sample:factory-stage-two");

        var candidates = new List<string>
        {
            "native:factory",
            "sample:factory-stage-one",
            "sample:factory-stage-two",
            "other-mod:factory"
        };
        scope.RemoveFactoriesOutsideStage(candidates);
        if (!candidates.Contains("native:factory") ||
            !candidates.Contains("sample:factory-stage-one") ||
            candidates.Contains("sample:factory-stage-two") ||
            !candidates.Contains("other-mod:factory") ||
            candidates.Contains("sample:factory-added-after-snapshot"))
        {
            throw new InvalidOperationException(
                "The random reward scope altered native/other-mod IDs or admitted a factory from another stage.");
        }

        var exclusives = new HashSet<string>(new[]
        {
            "native:exclusive",
            "sample:factory-stage-one",
            "sample:factory-stage-two"
        }, StringComparer.Ordinal);
        scope.RemoveFactoriesOutsideStage(exclusives);
        if (!exclusives.SetEquals(new[] { "native:exclusive", "sample:factory-stage-one" }))
        {
            throw new InvalidOperationException(
                "Global no-pool exclusivity retained a loader factory from another stage.");
        }

        var nativeStageScope = new BlueprintRewardFactoryScope(
            new[] { "sample:factory-stage-one", "sample:factory-stage-two" }, null);
        var nativeStageCandidates = new List<string> { "native:factory", "sample:factory-stage-one" };
        nativeStageScope.RemoveFactoriesOutsideStage(nativeStageCandidates);
        if (!nativeStageCandidates.SequenceEqual(new[] { "native:factory" }))
        {
            throw new InvalidOperationException("A native stage did not exclude all loader-owned factory IDs.");
        }

        var provider = new RuntimeMutatorTestProvider
        {
            RewardFactoryScope = new BlueprintRewardFactoryScope(
                new[] { "sample:factory-stage-one", "sample:factory-stage-two" },
                new[] { "sample:factory-stage-one" })
        };
        RuntimeHooks.Bind(null!, provider, null!);
        object previousScope = RuntimeHooks.BeginBlueprintRewardScope();
        var hookedCandidates = new List<string>
        {
            "native:factory",
            "sample:factory-stage-one",
            "sample:factory-stage-two"
        };
        RuntimeHooks.FilterGlobalBlueprintRewardCandidates(hookedCandidates);
        RuntimeHooks.EndBlueprintRewardScope(null!, previousScope);
        var candidatesAfterScope = new List<string> { "sample:factory-stage-two" };
        RuntimeHooks.FilterGlobalBlueprintRewardCandidates(candidatesAfterScope);
        RuntimeHooks.Clear(null!);
        if (!hookedCandidates.SequenceEqual(new[] { "native:factory", "sample:factory-stage-one" }) ||
            !candidatesAfterScope.SequenceEqual(new[] { "sample:factory-stage-two" }))
        {
            throw new InvalidOperationException("The reward scope hook did not filter candidates or leaked past its finalizer.");
        }
    }),
    ("random reward trace distinguishes unmapped native pool queries", () =>
    {
        const string factoryId = "sample:factory-stage-one";
        const string productId = "sample:unit-stage-one";
        var scope = new BlueprintRewardFactoryScope(
            new[] { factoryId },
            new[] { factoryId },
            new Dictionary<string, string>(StringComparer.Ordinal) { [productId] = factoryId },
            0,
            "native:stage-zero");
        var provider = new RuntimeMutatorTestProvider { RewardFactoryScope = scope };
        var logs = new List<string>();
        RuntimeHooks.Bind(null!, provider, logs.Add);
        object previousScope = RuntimeHooks.BeginBlueprintRewardScope();
        var candidates = new List<string> { "native:factory", factoryId };
        try
        {
            RuntimeHooks.ObserveBlueprintPoolCandidates(new object(), candidates, 0, false);
        }
        finally
        {
            RuntimeHooks.EndBlueprintRewardScope(null!, previousScope);
            RuntimeHooks.Clear(null!);
        }

        if (!candidates.SequenceEqual(new[] { "native:factory", factoryId }) || logs.Count != 1)
        {
            throw new InvalidOperationException("Unmapped-pool observation changed candidates or failed to emit one trace.");
        }

        string trace = logs[0];
        if (!trace.Contains("observedSources=unmapped-pool", StringComparison.Ordinal) ||
            !trace.Contains("unmappedPoolQueries=1", StringComparison.Ordinal) ||
            !trace.Contains("unmappedPoolFactories=[" + factoryId + "->" + productId + "]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The trace did not identify an unregistered pool query: " + trace);
        }
    }),
    ("random reward trace records candidates and actual mod offers", () =>
    {
        const string stageFactory = "sample:factory-stage-one";
        const string otherStageFactory = "sample:factory-stage-two";
        const string stageUnit = "sample:unit-stage-one";
        const string otherStageUnit = "sample:unit-stage-two";
        var scope = new BlueprintRewardFactoryScope(
            new[] { stageFactory, otherStageFactory },
            new[] { stageFactory },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [stageUnit] = stageFactory,
                [otherStageUnit] = otherStageFactory
            },
            3,
            "sample-expansion:stage-one");
        var pool = new object();
        var provider = new RuntimeMutatorTestProvider { RewardFactoryScope = scope };
        provider.PoolIds.Add(pool, "RCL_POOL_STAGE_ONE");
        var logs = new List<string>();
        RuntimeHooks.Bind(null!, provider, logs.Add);
        object previousScope = RuntimeHooks.BeginBlueprintRewardScope();

        var globalCandidates = new List<string>
        {
            "native:factory",
            stageFactory,
            otherStageFactory
        };
        RuntimeHooks.FilterGlobalBlueprintRewardCandidates(globalCandidates);
        var poolCandidates = new List<string> { stageFactory };
        RuntimeHooks.ObserveBlueprintPoolCandidates(pool, poolCandidates, 2, false);
        RuntimeHooks.ObserveBlueprintRewardRecord("blueprint", stageUnit);
        RuntimeHooks.ObserveBlueprintRewardRecord("unit", otherStageUnit);
        RuntimeHooks.EndBlueprintRewardScope(null!, previousScope);
        RuntimeHooks.Clear(null!);

        if (!globalCandidates.SequenceEqual(new[] { "native:factory", stageFactory }) ||
            !poolCandidates.SequenceEqual(new[] { stageFactory }) || logs.Count != 1)
        {
            throw new InvalidOperationException("Reward tracing changed candidate lists or did not emit one trace.");
        }

        string trace = logs[0];
        if (!trace.Contains("completion=completed", StringComparison.Ordinal) ||
            !trace.Contains("stageIndex=3", StringComparison.Ordinal) ||
            !trace.Contains("stageId=sample-expansion:stage-one", StringComparison.Ordinal) ||
            !trace.Contains("observedSources=global,pool", StringComparison.Ordinal) ||
            !trace.Contains("stageFactories=[" + stageFactory + "->" + stageUnit + "]", StringComparison.Ordinal) ||
            !trace.Contains("globalFactories=[" + stageFactory + "->" + stageUnit + "," +
                otherStageFactory + "->" + otherStageUnit + "]", StringComparison.Ordinal) ||
            !trace.Contains("RCL_POOL_STAGE_ONE", StringComparison.Ordinal) ||
            !trace.Contains("xp=2,excludeOther=false", StringComparison.Ordinal) ||
            !trace.Contains("offeredProducts=[" + stageUnit + "]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The trace did not distinguish stage candidates from actual offers: " + trace);
        }
    }),
    ("guaranteed mod reward registers its balance row before injecting a card", () =>
    {
        EntityBalancingStore.Reset();
        Game.AtLeastOneBlueprintHasBeenAddedByPlayer = false;
        Game.CardsInDeck = new List<string> { "native:owned" };
        Game.OpenCardRewards = new List<OpenReward>
        {
            new() { blueprintPoolId = "BP_CheaperVehicles" }
        };
        MetaGame.Instance.CurrentExperienceLevel = 4;
        var mockProvider = new RuntimeMutatorTestProvider
        {
            GuaranteedModProduct = "sample:unit-b",
            RequireRegisteredModProduct = true
        };
        mockProvider.ModProducts.Add("sample:unit-a");
        mockProvider.ModProducts.Add("sample:unit-b");
        mockProvider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:unit-b"));
        var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, mockProvider, null);

        RuntimeHooks.Bind(mutator, mockProvider, null!);
        try
        {
            var nativeCards = new List<string> { "native:tank", "native:turret", "native:scout" };
            bool injected = RuntimeHooks.EnsureGuaranteedModCardOption(nativeCards, forcedSlot: 1);
            if (!injected || nativeCards[1] != "sample:unit-b" ||
                !EntityBalancingStore.ParameterListIndexOf.ContainsKey("sample:unit-b") ||
                mockProvider.LastPoolId != "BP_CheaperVehicles" || !mockProvider.LastStartingSelection ||
                mockProvider.LastCurrentXpLevel != 4 ||
                !mockProvider.LastExcludedProductIds.SetEquals(new[]
                    { "native:tank", "native:turret", "native:scout", "native:owned" }))
            {
                throw new InvalidOperationException(
                    "A guaranteed mod card must register its row first and pass the active reward eligibility state.");
            }

            var mixedCards = new List<string> { "native:tank", "sample:unit-a", "native:scout" };
            bool injectedMixed = RuntimeHooks.EnsureGuaranteedModCardOption(mixedCards, forcedSlot: 0);
            if (injectedMixed || mixedCards[0] != "native:tank" || mixedCards[1] != "sample:unit-a")
            {
                throw new InvalidOperationException("A reward already containing a mod product must remain unchanged.");
            }

            var emptyCards = new List<string>();
            if (RuntimeHooks.EnsureGuaranteedModCardOption(emptyCards))
            {
                throw new InvalidOperationException("An empty reward list must not trigger injection.");
            }
        }
        finally
        {
            RuntimeHooks.Clear(mutator);
            EntityBalancingStore.Reset();
            Game.AtLeastOneBlueprintHasBeenAddedByPlayer = true;
            Game.CardsInDeck = new List<string>();
            Game.OpenCardRewards = new List<OpenReward>();
            MetaGame.Instance.CurrentExperienceLevel = 0;
        }
    }),
    ("random reward trace output is bounded", () =>
    {
        var factories = Enumerable.Range(0, 300)
            .Select(index => "sample:factory-" + index.ToString("D3"))
            .ToArray();
        var products = factories.ToDictionary(
            factory => "sample:unit-" + factory.Substring("sample:factory-".Length),
            factory => factory,
            StringComparer.Ordinal);
        var scope = new BlueprintRewardFactoryScope(
            factories, factories, products, 0, "sample-expansion:large-stage");
        var observation = new BlueprintRewardObservation();
        observation.RecordGlobalCandidates(factories, scope, false);
        observation.RecordGlobalCandidates(factories, scope, true);
        string trace = observation.Format(scope);
        if (trace.Length > 4096 || !trace.Contains("omittedIdObservations=", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A large reward trace exceeded its output bound or hid truncation.");
        }

        var poolObservation = new BlueprintRewardObservation();
        for (int i = 0; i < 9; i++)
        {
            poolObservation.RecordPoolCandidates(
                "RCL_POOL_" + i.ToString("D2"), Array.Empty<string>(), scope, null, false);
        }

        string poolTrace = poolObservation.Format(scope);
        if (!poolTrace.Contains("omittedPoolQueries=1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Pool-query truncation was not reported independently from IDs.");
        }

        var longFactories = Enumerable.Range(0, 300)
            .Select(index => "sample:factory-" + index.ToString("D3") + new string('x', 96))
            .ToArray();
        var longScope = new BlueprintRewardFactoryScope(
            longFactories, longFactories, null, 0, "sample-expansion:large-stage");
        var detailedPoolObservation = new BlueprintRewardObservation();
        for (int i = 0; i < 9; i++)
        {
            detailedPoolObservation.RecordPoolCandidates(
                "RCL_POOL_" + i.ToString("D2"),
                i < 8 ? longFactories : Array.Empty<string>(),
                longScope,
                null,
                false);
        }

        string detailedPoolTrace = detailedPoolObservation.Format(longScope);
        if (detailedPoolTrace.Length > 4096 || !detailedPoolTrace.EndsWith("...", StringComparison.Ordinal) ||
            !detailedPoolTrace.Contains("omittedIdObservations=", StringComparison.Ordinal) ||
            !detailedPoolTrace.Contains("omittedPoolQueries=1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A truncated candidate report hid its independent ID or pool-query omission counters.");
        }

        var longProducts = Enumerable.Range(0, 300)
            .Select(index => "sample:unit-" + index.ToString("D3") + new string('y', 180))
            .ToArray();
        var longProductScope = new BlueprintRewardFactoryScope(
            longFactories, longFactories, longProducts.ToDictionary(p => p, p => longFactories[0]), 0, "sample-expansion:large-stage");
        var offeredObservation = new BlueprintRewardObservation();
        for (int i = 0; i < longProducts.Length; i++)
        {
            offeredObservation.RecordOfferedProduct(longProducts[i], longProductScope);
        }
        for (int i = 0; i < 9; i++)
        {
            offeredObservation.RecordPoolCandidates(
                "RCL_POOL_" + i.ToString("D2"), Array.Empty<string>(), longProductScope, null, false);
        }
        string offeredTrace = offeredObservation.Format(longProductScope);
        if (offeredTrace.Length > 4096 || !offeredTrace.EndsWith("...", StringComparison.Ordinal) ||
            !offeredTrace.Contains("omittedIdObservations=", StringComparison.Ordinal) ||
            !offeredTrace.Contains("omittedPoolQueries=1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "An offered-product truncated report hid its independent ID or pool-query omission counters.");
        }
    }),
    ("demo reward stage index follows visible stages and tutorial substitution", () =>
    {
        var nativeHiddenStage = new object();
        var modDemoStage = new object();
        var tutorialStage = new object();
        var progressionStages = new ArrayList { nativeHiddenStage, modDemoStage };
        bool IsDemoStage(object stage) => ReferenceEquals(stage, modDemoStage) || ReferenceEquals(stage, tutorialStage);

        IList visibleStages = BlueprintRewardStageResolver.BuildVisibleStages(
            progressionStages, null, true, false, IsDemoStage);
        object resolved = BlueprintRewardStageResolver.ResolveVisibleStage(visibleStages, 0);
        if (!ReferenceEquals(resolved, modDemoStage))
        {
            throw new InvalidOperationException("Demo map index zero did not skip the hidden preceding progression stage.");
        }

        visibleStages = BlueprintRewardStageResolver.BuildVisibleStages(
            progressionStages, tutorialStage, true, true, IsDemoStage);
        resolved = BlueprintRewardStageResolver.ResolveVisibleStage(visibleStages, 0);
        if (!ReferenceEquals(resolved, tutorialStage))
        {
            throw new InvalidOperationException("The active tutorial stage was not substituted at the first map index.");
        }

        resolved = BlueprintRewardStageResolver.ResolveVisibleStage(visibleStages, 1);
        if (!ReferenceEquals(resolved, modDemoStage))
        {
            throw new InvalidOperationException("The progression stage following the visible tutorial stage was not resolved.");
        }

        // Tutorial state may change after the map has been created. Resolution must use the saved
        // visible stage list for that map rather than rebuilding it from the changed state.
        resolved = BlueprintRewardStageResolver.ResolveVisibleStage(visibleStages, 0);
        if (!ReferenceEquals(resolved, tutorialStage))
        {
            throw new InvalidOperationException("The map's tutorial stage snapshot changed after map creation.");
        }

        resolved = BlueprintRewardStageResolver.ResolveVisibleStage(visibleStages, 2);
        if (resolved != null)
        {
            throw new InvalidOperationException("An out-of-range Demo map index unexpectedly resolved a stage.");
        }
    }),
    ("saved stage order roundtrips through StageMap JSON and restores the serialized sequence", () =>
    {
        if (!BlueprintRewardStageOrder.TryCreateNativeStageToken("27 Circles", out string nativeA) ||
            !BlueprintRewardStageOrder.TryCreateNativeStageToken("35 Flat Island", out string nativeB) ||
            !BlueprintRewardStageOrder.TryCreateCustomStageToken(
                "sample-native", "sample-native:training-variant", out string customA) ||
            !BlueprintRewardStageOrder.TryCreateCustomStageToken(
                "sample-reactive-ai", "sample-reactive-ai:base-defense", out string customB))
        {
            throw new InvalidOperationException("A supported native or custom stage identity could not be encoded.");
        }

        var savedOrder = new List<string> { nativeA, customA, nativeB, customB };
        string originalJson = "{\"levelMaps\":[{\"note\":\"brace } comma , and \\\"rclStageOrderV1\\\" decoy\"}],\"currentStage\":1}";
        if (!BlueprintRewardStageOrder.TryAppendSerializedOrder(originalJson, savedOrder, out string serializedJson) ||
            !BlueprintRewardStageOrder.TryReadSerializedOrder(serializedJson, out List<string>? restoredOrder) ||
            restoredOrder == null || !restoredOrder.SequenceEqual(savedOrder))
        {
            throw new InvalidOperationException("The persisted stage identities did not survive a JSON roundtrip.");
        }

        if (!BlueprintRewardStageOrder.TryAppendSerializedOrder(serializedJson,
                new List<string> { customB, nativeB, customA, nativeA }, out string reorderedJson) ||
            !BlueprintRewardStageOrder.TryReadSerializedOrder(reorderedJson, out restoredOrder) ||
            restoredOrder == null || !restoredOrder.SequenceEqual(new[] { customB, nativeB, customA, nativeA }) ||
            reorderedJson.IndexOf("\"rclStageOrderV1\"", StringComparison.Ordinal) !=
                reorderedJson.LastIndexOf("\"rclStageOrderV1\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Updating a saved stage order duplicated or failed to replace its marker.");
        }

        if (!BlueprintRewardStageOrder.TryReadCustomStageToken(customA, out string packId, out string stageId) ||
            packId != "sample-native" || stageId != "sample-native:training-variant" ||
            !BlueprintRewardStageOrder.TryReadNativeStageToken(nativeA, out string assetName) || assetName != "27 Circles")
        {
            throw new InvalidOperationException("A persisted stage token could not be mapped back to its stable identity.");
        }

        var nativeStage = new object();
        var trainingStage = new object();
        var defenseStage = new object();
        var candidates = new ArrayList { trainingStage, nativeStage, defenseStage };
        var tokenByStage = new Dictionary<object, string>
        {
            [nativeStage] = nativeA,
            [trainingStage] = customA,
            [defenseStage] = customB
        };
        var persistedOrder = new List<string> { customB, nativeA, customA };
        if (!BlueprintRewardStageResolver.TryResolveSavedStageOrder(
                persistedOrder, candidates, stage => tokenByStage[stage], out List<object>? resolved) ||
            resolved == null || !resolved.SequenceEqual(new[] { defenseStage, nativeStage, trainingStage }))
        {
            throw new InvalidOperationException("A reordered save did not resolve the exact persisted stage sequence.");
        }

        var missingOrder = new List<string> { customB, customA };
        var missingCandidates = new ArrayList { defenseStage };
        if (BlueprintRewardStageResolver.TryResolveSavedStageOrder(
                missingOrder, missingCandidates, stage => tokenByStage[stage], out _))
        {
            throw new InvalidOperationException("A saved custom stage missing from the loaded packs was not rejected.");
        }

        if (BlueprintRewardStageOrder.TryAppendSerializedOrder(
                originalJson, new List<string> { customA, customA }, out _) ||
            BlueprintRewardStageResolver.TryResolveSavedStageOrder(
                new List<string> { customA, customA }, candidates, stage => tokenByStage[stage], out _))
        {
            throw new InvalidOperationException("A repeated custom stage identity was accepted in a saved order.");
        }
    }),
    ("stage-order JSON parser ignores nested marker text and rejects malformed markers", () =>
    {
        if (!BlueprintRewardStageOrder.TryCreateNativeStageToken("27 Circles", out string nativeToken))
        {
            throw new InvalidOperationException("The native stage token could not be created.");
        }

        var order = new List<string> { nativeToken };
        string nestedJson = "{\"levelMaps\":[{\"nested\":{\"rclStageOrderV1\":\"not-the-root\"}}],\"currentStage\":0}";
        if (!BlueprintRewardStageOrder.TryAppendSerializedOrder(nestedJson, order, out string appended) ||
            !BlueprintRewardStageOrder.TryReadSerializedOrder(appended, out List<string>? restored) ||
            restored == null || !restored.SequenceEqual(order))
        {
            throw new InvalidOperationException("A nested marker string interfered with the root stage-order property.");
        }

        string duplicate = appended.Insert(appended.LastIndexOf('}'), ",\"rclStageOrderV1\":\"N_bad\"");
        if (BlueprintRewardStageOrder.TryReadSerializedOrder(duplicate, out _))
        {
            throw new InvalidOperationException("Duplicate root stage-order properties were accepted.");
        }

        if (BlueprintRewardStageOrder.TryAppendSerializedOrder("not json", order, out _))
        {
            throw new InvalidOperationException("Malformed StageMap JSON was rewritten.");
        }
    }),
    ("reward provider exceptions install a fail-closed factory scope", () =>
    {
        var provider = new RuntimeMutatorTestProvider
        {
            RewardFactoryScope = new BlueprintRewardFactoryScope(
                new[] { "sample:factory-stage-one", "sample:factory-stage-two" },
                new[] { "sample:factory-stage-one" }),
            ThrowOnRewardScope = true
        };
        RuntimeHooks.Bind(null!, provider, null!);
        object previousScope = RuntimeHooks.BeginBlueprintRewardScope();
        var candidates = new List<string>
        {
            "native:factory",
            "sample:factory-stage-one",
            "sample:factory-stage-two",
            "other-mod:factory"
        };
        RuntimeHooks.FilterGlobalBlueprintRewardCandidates(candidates);
        RuntimeHooks.EndBlueprintRewardScope(null!, previousScope);
        RuntimeHooks.Clear(null!);
        if (!candidates.SequenceEqual(new[] { "native:factory", "other-mod:factory" }))
        {
            throw new InvalidOperationException("A provider exception left loader-owned global factories eligible.");
        }
    }),
    ("scoped blueprint pool tooltips retain their source localization key", () =>
    {
        var registry = new BlueprintPoolScopeRegistry();
        registry.Register(new[]
        {
            new BlueprintPoolScopeRegistration("RCL_POOL_TEST", "sample:stage", new object(),
                new[] { "sample:unit" }, false, "NativePool")
        });
        if (!registry.TryGetSourcePoolLocalizationId("rcl_pool_test", out string sourceKey) ||
            sourceKey != "NativePool" ||
            registry.TryGetSourcePoolLocalizationId("NativePool", out _) ||
            registry.TryGetSourcePoolLocalizationId(null, out _))
        {
            throw new InvalidOperationException("A scoped pool lost its source tooltip key or rewrote an unrelated key.");
        }

        MethodInfo hook = typeof(RuntimeHarmonyPatches).GetMethod("LandscapeLocalizationPrefix")!;
        if (hook.GetParameters()[0].ParameterType != typeof(string).MakeByRefType())
        {
            throw new InvalidOperationException("The localization hook cannot pass its source key to the original game method.");
        }

        var provider = new RuntimeMutatorTestProvider();
        provider.PoolLocalizationAliases.Add("RCL_POOL_TEST", "NativePool");
        provider.PoolLocalizationAliases.Add("RCL_POOL_CHAINED", "RCL_POOL_TEST");
        RuntimeHooks.Bind(null!, provider, null!);
        try
        {
            string key = "RCL_POOL_TEST";
            string result = "unchanged";
            if (!RuntimeHarmonyPatches.LandscapeLocalizationPrefix(ref key, ref result) ||
                key != "NativePool" || result != "unchanged")
                throw new InvalidOperationException("The localization prefix did not forward the source key to the game.");
            key = "RCL_POOL_CHAINED";
            if (!RuntimeHarmonyPatches.LandscapeLocalizationPrefix(ref key, ref result) ||
                key != "NativePool" || result != "unchanged")
                throw new InvalidOperationException("The localization prefix did not resolve chained pool aliases.");
            key = "RCL_POOL_UNKNOWN";
            if (!RuntimeHarmonyPatches.LandscapeLocalizationPrefix(ref key, ref result) ||
                key != "overworldMapBlueprintTooltip" || result != "unchanged")
                throw new InvalidOperationException("The localization prefix did not fall back unknown RCL pools to overworldMapBlueprintTooltip.");
            key = "UnrelatedKey";
            if (!RuntimeHarmonyPatches.LandscapeLocalizationPrefix(ref key, ref result) ||
                key != "UnrelatedKey" || result != "unchanged")
                throw new InvalidOperationException("The localization prefix rewrote an unrelated key.");

            string finalResult = "original";
            Exception ex = RuntimeHarmonyPatches.LandscapeLocalizationFinalizer(new Exception("test translation failure"), ref finalResult);
            if (ex != null || finalResult != "Blueprint")
            {
                throw new InvalidOperationException("The localization finalizer did not suppress the exception or fall back to Blueprint.");
            }
        }
        finally { RuntimeHooks.Clear(null!); }
    }),
    ("appended stage fallbacks preserve the native prefix and follow each active progression", () =>
    {
        var nativeFirst = new object();
        var nativeSecond = new object();
        var addedFallback = new object();
        var firstStage = new object();
        var secondStage = new object();
        var native = new List<object> { nativeFirst, nativeSecond };
        var firstProfile = new List<object> { new object(), new object(), firstStage, secondStage };
        var secondProfile = new List<object> { new object(), new object(), secondStage };
        object Resolve(object stage) => stage == firstStage || stage == secondStage ? addedFallback : null!;
        var first = StageFallbackSnapshot.Build(native, firstProfile, Resolve);
        var second = StageFallbackSnapshot.Build(native, secondProfile, Resolve);
        var repeated = StageFallbackSnapshot.Build(native, firstProfile, Resolve);
        if (native.Count != 2 || first.Count != 4 || second.Count != 3 || repeated.Count != 4 ||
            !ReferenceEquals(first[0], nativeFirst) || !ReferenceEquals(first[1], nativeSecond) ||
            !ReferenceEquals(first[2], addedFallback) || !ReferenceEquals(first[3], addedFallback) ||
            !ReferenceEquals(second[2], addedFallback) || ReferenceEquals(first, native))
        {
            throw new InvalidOperationException("Fallback snapshots mutated native slots, deduplicated positional entries, or leaked progression state.");
        }

        bool missingRejected = false;
        try { StageFallbackSnapshot.Build(native, firstProfile, _ => null!); }
        catch (InvalidOperationException) { missingRejected = true; }
        if (!missingRejected || native.Count != 2)
            throw new InvalidOperationException("An unknown appended stage was accepted or changed native fallbacks.");

        MethodInfo hook = typeof(RuntimeHarmonyPatches).GetMethod("FallbackLandscapeGeneratorsPostfix")!;
        if (hook.ReturnType != typeof(void) || hook.GetParameters()[0].ParameterType !=
            typeof(List<global::LandscapeGenerator>).MakeByRefType())
            throw new InvalidOperationException("The fallback getter hook has an incompatible typed result.");
    }),
    ("production building reward card accepts a Sprite from its declared bundle", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentProductionBuildingDefinition building = pack.units[0].productionBuilding;
            building.assetBundleId = pack.assetBundles[0].id;
            building.cardImageAssetName = "assets/factory-card.png";
            building.visualAssetName = null;
            building.visualRootPath = null;
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("production building reward card asset path is validated", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentProductionBuildingDefinition building = pack.units[0].productionBuilding;
            building.assetBundleId = pack.assetBundles[0].id;
            building.cardImageAssetName = "../outside.png";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "asset.name-invalid");
        });
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentProductionBuildingDefinition building = pack.units[0].productionBuilding;
            building.assetBundleId = "sample-native:missing-assets";
            building.cardImageAssetName = "assets/factory-card.png";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "reference.bundle-unresolved");
        });
    }),
    ("Atlas Crane declares its own factory, two firepoints, and random-pool offers", () =>
    {
        ContentUnitDefinition atlas = nativeSample.units.Single(
            unit => unit.id == "sample-native:atlas-crane-mech");
        if (atlas.productionBuilding == null || atlas.productionBuilding.id != "sample-native:atlas-crane-mech-factory" ||
            atlas.balanceOverrides.Single(item => item.name == "firePointCount").value != "2" ||
            nativeSample.units.Any(unit => unit.id != atlas.id &&
                unit.productionBuilding.id == atlas.productionBuilding.id) ||
            !nativeSample.stages.Any(stage => stage.unitIds.Contains(atlas.id)))
        {
            throw new InvalidOperationException(
                "Atlas does not have an exclusive factory, two firepoints, or a stage reward-pool offer.");
        }
    }),
    ("training stage declares a deterministic, localized native landscape variant", () =>
    {
        ContentStageDefinition stage = nativeSample.stages.Single(
            candidate => candidate.id == "sample-native:training-variant");
        ContentStageLandscapeDefinition landscape = stage.landscape;
        if (landscape == null || landscape.nativeTemplateId != "27 Circles" ||
            landscape.mapSize != 31 || landscape.randomSeed != 41021 || !landscape.alwaysUseSameSeed ||
            landscape.localizations == null ||
            !landscape.localizations.Any(item => item.language == "en-US" && item.name == "Sandlance Training Ground") ||
            !landscape.localizations.Any(item => item.language == "es-XL" && item.name == "Campo de Entrenamiento Sandlance"))
        {
            throw new InvalidOperationException("The training stage's procedural landscape definition is incomplete.");
        }

        WithPackage(nativeSample, (pack, root) =>
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator()));
    }),
    ("stage pool entries validate native IDs and reject unknown IDs", () =>
    {
        ContentPackDefinition invalid = Clone(nativeSample);
        invalid.stages[0].blueprintPoolEntries = [new ContentStagePoolEntryDefinition
        {
            poolId = "NativePool",
            unitIds = ["native:NativeUnit"]
        }];
        WithPackage(invalid, (pack, root) => ExpectValid(pack, root,
            new SnapshotWithUnits(new[] { "native:NativeUnit" }), new KnownModuleValidator()));

        invalid = Clone(nativeSample);
        invalid.stages[0].blueprintPoolEntries = [new ContentStagePoolEntryDefinition
        {
            poolId = "NativePool",
            unitIds = ["missing:NativeUnit"]
        }];
        WithPackage(invalid, (pack, root) => ExpectDiagnostic(pack, root,
            new SnapshotWithUnits(Array.Empty<string>()), new KnownModuleValidator(), "reference.unit-unresolved"));
    }),
    ("stage pool entries include their products' factories without stage unitIds", () =>
    {
        var stage = new ContentStageDefinition
        {
            unitIds = Array.Empty<string>(),
            blueprintPoolEntries =
            [
                new ContentStagePoolEntryDefinition
                {
                    poolId = "BP_CheaperTanks",
                    unitIds = ["sample:scout-tank", "native:NativeTank"]
                }
            ]
        };
        HashSet<string> factoryIds = StageBlueprintFactoryIds.CollectLoaderFactoryIds(stage,
            unitId => unitId == "sample:scout-tank" ? "sample:scout-tank-factory" : null);

        if (!factoryIds.SetEquals(new[] { "sample:scout-tank-factory" }))
        {
            throw new InvalidOperationException(
                "A pool-only custom product must grant only its factory blueprint eligibility.");
        }
    }),
    ("named stage pools use only their listed products while unlisted pools stay stage-wide", () =>
    {
        ContentStagePoolEntryDefinition[] entries =
        [
            new ContentStagePoolEntryDefinition
            {
                poolId = "pool-a",
                unitIds = ["sample:pool-a-product", "native:pool-a-unit"]
            },
            new ContentStagePoolEntryDefinition
            {
                poolId = "pool-b",
                unitIds = ["sample:pool-b-product"]
            }
        ];
        Dictionary<string, HashSet<string>> unitsByPool =
            StageBlueprintPoolFactoryIds.CollectByPool(
                new[] { "sample:stage-factory" },
                new[] { "pool-a", "pool-b", "pool-c" },
                entries,
                unitId => unitId switch
                {
                    "sample:pool-a-product" => "sample:pool-a-factory",
                    "sample:pool-b-product" => "sample:pool-b-factory",
                    _ => null
                });

        if (!unitsByPool["pool-a"].SetEquals(
                new[] { "sample:pool-a-factory", "native:pool-a-unit" }) ||
            !unitsByPool["pool-b"].SetEquals(new[] { "sample:pool-b-factory" }) ||
            !unitsByPool["pool-c"].SetEquals(new[] { "sample:stage-factory" }))
        {
            throw new InvalidOperationException(
                "Named pools must contain only their explicit products, while unlisted pools retain stage-wide " +
                "products: pool-a=[" + String.Join(",", unitsByPool["pool-a"]) + "], " +
                "pool-b=[" + String.Join(",", unitsByPool["pool-b"]) + "], " +
                "pool-c=[" + String.Join(",", unitsByPool["pool-c"]) + "].");
        }
    }),
    ("stage pool entries deep clone their unit ID arrays", () =>
    {
        ContentPackDefinition source = Clone(nativeSample);
        source.stages[0].blueprintPoolEntries = [new ContentStagePoolEntryDefinition
        {
            poolId = "native-test-pool",
            unitIds = ["sample-native:atlas-crane-mech", "NativeTank"]
        }];
        ContentPackDefinition clone = Clone(source);
        clone.stages[0].blueprintPoolEntries[0].unitIds[0] = "changed:unit";
        if (source.stages[0].blueprintPoolEntries[0].unitIds[0] != "sample-native:atlas-crane-mech")
            throw new InvalidOperationException("Stage pool override unit IDs shared mutable state.");
    }),
    ("sample-native-expansion stage factory IDs are collected through the new loader helpers", () =>
    {
        ContentStageDefinition stage = nativeSample.stages.Single(
            candidate => candidate.id == "sample-native:training-variant");

        Dictionary<string, string> factoryMap = nativeSample.units
            .Where(unit => unit.productionBuilding != null)
            .ToDictionary(unit => unit.id, unit => unit.productionBuilding.id, StringComparer.Ordinal);

        HashSet<string> factoryIds = StageBlueprintFactoryIds.CollectLoaderFactoryIds(
            stage, productId => factoryMap.TryGetValue(productId, out string? factoryId) ? factoryId : null);

        if (factoryIds.Count != 4 ||
            !factoryIds.SetEquals(new[]
            {
                "sample-native:scout-tank-factory",
                "sample-native:skirmish-bike-factory",
                "sample-native:arachnid-hexapod-factory",
                "sample-native:overlord-command-hq-factory"
            }))
        {
            throw new InvalidOperationException(
                "Expected 4 loader factory IDs for sample-native:training-variant, got: " +
                String.Join(",", factoryIds));
        }

        string[] availablePoolIds =
        [
            "BP_CheaperVehicles",
            "BP_CheaperRobos",
            "BP_CheaperMechs",
            "BP_CheaperTanks"
        ];

        Dictionary<string, HashSet<string>> idsByPool = StageBlueprintPoolFactoryIds.CollectByPool(
            factoryIds,
            availablePoolIds,
            stage.blueprintPoolEntries,
            productId => factoryMap.TryGetValue(productId, out string? factoryId) ? factoryId : null);

        if (!idsByPool["BP_CheaperVehicles"].SetEquals(new[]
                { "sample-native:scout-tank-factory", "sample-native:skirmish-bike-factory" }) ||
            !idsByPool["BP_CheaperRobos"].SetEquals(new[]
                { "sample-native:arachnid-hexapod-factory", "sample-native:overlord-command-hq-factory" }) ||
            !idsByPool["BP_CheaperMechs"].SetEquals(new[]
                { "sample-native:arachnid-hexapod-factory", "sample-native:overlord-command-hq-factory" }) ||
            !idsByPool["BP_CheaperTanks"].SetEquals(factoryIds))
        {
            throw new InvalidOperationException(
                "Pool factory IDs did not match expected values: " +
                "BP_CheaperVehicles=[" + String.Join(",", idsByPool["BP_CheaperVehicles"]) + "], " +
                "BP_CheaperRobos=[" + String.Join(",", idsByPool["BP_CheaperRobos"]) + "], " +
                "BP_CheaperMechs=[" + String.Join(",", idsByPool["BP_CheaperMechs"]) + "], " +
                "BP_CheaperTanks=[" + String.Join(",", idsByPool["BP_CheaperTanks"]) + "].");
        }
    }),
    ("stage landscape rejects unsafe size, seed, and localization values", () =>
    {
        ContentPackDefinition invalid = Clone(nativeSample);
        ContentStageDefinition stage = invalid.stages.Single(
            candidate => candidate.id == "sample-native:training-variant");
        stage.landscape.mapSize = 32;
        WithPackage(invalid, (pack, root) => ExpectDiagnostic(pack, root, new EmptySnapshot(),
            new KnownModuleValidator(), "stage.landscape-map-size-invalid"));

        invalid = Clone(nativeSample);
        stage = invalid.stages.Single(candidate => candidate.id == "sample-native:training-variant");
        stage.landscape.randomSeed = 99999;
        WithPackage(invalid, (pack, root) => ExpectDiagnostic(pack, root, new EmptySnapshot(),
            new KnownModuleValidator(), "stage.landscape-seed-invalid"));

        invalid = Clone(nativeSample);
        stage = invalid.stages.Single(candidate => candidate.id == "sample-native:training-variant");
        stage.landscape.localizations = [new ContentLandscapeLocalizationDefinition
        {
            language = "es-XL",
            name = "Campo de Entrenamiento"
        }];
        WithPackage(invalid, (pack, root) => ExpectDiagnostic(pack, root, new EmptySnapshot(),
            new KnownModuleValidator(), "stage.landscape-localization-english-name-required"));
    }),
    ("native stage rejects a mixed bundle source", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.stages[0].nativeTemplateId = "StageParameters_Pools_Level1";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "stage.source-conflict");
        });
    }),
    ("stage generation validation rejects disjoint landscape map sizes", () =>
    {
        StageGenerationAiCandidate[] ais = [ValidStageAi("sample-ai")];
        StageGenerationWorldCandidate[] worlds = [ValidStageWorld("sample-world")];
        StageGenerationLandscapeCandidate[] landscapes = [ValidStageLandscape("native-map", 53)];

        if (StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                250, 256, false, false, 19, ais, worlds, landscapes, out string diagnostic) ||
            diagnostic == null || !diagnostic.Contains("LandscapeGenerator", StringComparison.Ordinal) ||
            !diagnostic.Contains("sample-ai", StringComparison.Ordinal) ||
            !diagnostic.Contains("sample-world", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A stage with no matching landscape size was not rejected precisely.");
        }
    }),
    ("stage-scoped landscape candidate participates in generation preflight", () =>
    {
        StageGenerationAiCandidate[] ais = [ValidStageAi("sample-ai")];
        StageGenerationWorldCandidate[] worlds = [ValidStageWorld("sample-world")];
        StageGenerationLandscapeCandidate[] nativeLandscapes = [ValidStageLandscape("native-map", 53)];
        StageGenerationLandscapeCandidate stageLandscape = ValidStageLandscape("RCL_Landscape_sample_stage", 31);

        if (!StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                31, 31, false, false, 19, ais, worlds, nativeLandscapes,
                stageLandscape, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("The stage-owned landscape was not included in preflight: " + diagnostic);
        }

        stageLandscape = ValidStageLandscape("RCL_Landscape_sample_stage", 29);
        if (StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                31, 31, false, false, 19, ais, worlds, nativeLandscapes,
                stageLandscape, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("An out-of-range stage-owned landscape passed preflight.");
        }
    }),
    ("progression profiles retain fallback and explicit aliases", () =>
    {
        object defaultProfile = new();
        object firstRunProfile = new();
        object[] profiles = StageGenerationCompatibility.ResolveProgressionProfiles(
            defaultProfile, null, firstRunProfile);

        if (profiles.Length != 3 || !ReferenceEquals(profiles[0], defaultProfile) ||
            !ReferenceEquals(profiles[1], defaultProfile) ||
            !ReferenceEquals(profiles[2], firstRunProfile))
        {
            throw new InvalidOperationException("Missing Heat fallback or explicit FirstRun profile identity was not preserved.");
        }

        profiles = StageGenerationCompatibility.ResolveProgressionProfiles(
            defaultProfile, defaultProfile, defaultProfile);
        if (!ReferenceEquals(profiles[0], profiles[1]) || !ReferenceEquals(profiles[1], profiles[2]))
        {
            throw new InvalidOperationException("Progression profile aliases were copied or replaced.");
        }
    }),
    ("stage indices include native and already registered content stages", () =>
    {
        object earlierPackStageA = new();
        object earlierPackStageB = new();
        object candidateStageA = new();
        object candidateStageB = new();
        object[] progressionStages =
        [
            new object(), new object(), new object(),
            earlierPackStageA, earlierPackStageB
        ];
        List<object> loadedStages = [earlierPackStageA, earlierPackStageB, candidateStageA, candidateStageB];

        if (!StageGenerationCompatibility.TryBuildStageIndices(
                progressionStages, loadedStages, "default", out int[] stageIndices, out string diagnostic) ||
            diagnostic != null || !stageIndices.SequenceEqual([3, 4, 5, 6]))
        {
            throw new InvalidOperationException("Stage indices did not account for native, registered, and candidate stages: " +
                diagnostic);
        }

        object[] nativeOnlyStages = [new object(), new object()];
        if (!StageGenerationCompatibility.TryBuildStageIndices(
                nativeOnlyStages, [candidateStageA, candidateStageB], "first-run",
                out stageIndices, out diagnostic) || !stageIndices.SequenceEqual([2, 3]))
        {
            throw new InvalidOperationException("Unregistered content stages did not start after native stages: " + diagnostic);
        }
    }),
    ("stage index builder rejects out of order registered content references", () =>
    {
        object nativeA = new();
        object nativeB = new();
        object packStageA = new();
        object packStageB = new();
        object[] progressionStages = [nativeA, nativeB, packStageB, packStageA];

        if (StageGenerationCompatibility.TryBuildStageIndices(
                progressionStages, [packStageA, packStageB], "heat", out _, out string diagnostic) ||
            diagnostic == null || !diagnostic.Contains("unexpected content stage order", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Out-of-order registered content stages were not rejected precisely.");
        }
    }),
    ("stage generation validation rejects inactive and demo-disallowed worlds", () =>
    {
        StageGenerationAiCandidate[] ais = [ValidStageAi("sample-ai")];
        StageGenerationLandscapeCandidate[] landscapes = [ValidStageLandscape("native-map", 53)];
        StageGenerationWorldCandidate inactiveWorld = ValidStageWorld("inactive-world");
        inactiveWorld.IsDeactivated = true;
        if (StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                20, 60, false, false, 19, ais, [inactiveWorld], landscapes, out string diagnostic) ||
            diagnostic == null || !diagnostic.Contains("No hay World", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A stage with only deactivated worlds was accepted.");
        }

        StageGenerationWorldCandidate demoWorld = ValidStageWorld("demo-world");
        demoWorld.IsAllowedForDemo = false;
        if (StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                20, 60, true, false, 2, ais, [demoWorld], landscapes, out diagnostic) ||
            diagnostic == null || !diagnostic.Contains("No hay World", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A stage with only demo-disallowed worlds was accepted.");
        }
    }),
    ("stage generation validation requires a usable LandscapeGenerator restricted world", () =>
    {
        StageGenerationAiCandidate[] ais = [ValidStageAi("sample-ai")];
        StageGenerationWorldCandidate[] worlds = [ValidStageWorld("stage-world")];
        StageGenerationLandscapeCandidate landscape = ValidStageLandscape("restricted-map", 53);
        landscape.RestrictedWorlds = null;
        if (StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                53, 53, false, false, 19, ais, worlds, [landscape], out string diagnostic))
        {
            throw new InvalidOperationException("A LandscapeGenerator with a null restricted World list was accepted.");
        }

        landscape.RestrictedWorlds = [null];

        if (StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                53, 53, false, false, 19, ais, worlds, [landscape], out diagnostic) ||
            diagnostic == null || !diagnostic.Contains("LandscapeGenerator", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A LandscapeGenerator with only a null restricted World was accepted.");
        }

        landscape.RestrictedWorlds = [new StageGenerationWorldCandidate
        {
            Id = "disabled-world",
            IsDeactivated = true,
            IsAllowedForDemo = true
        }];
        if (StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                53, 53, false, false, 19, ais, worlds, [landscape], out diagnostic))
        {
            throw new InvalidOperationException("A LandscapeGenerator with only a deactivated restricted World was accepted.");
        }

        landscape.RestrictedWorlds = [new StageGenerationWorldCandidate
        {
            Id = "demo-disallowed-world",
            IsDeactivated = false,
            IsAllowedForDemo = false
        }];
        if (StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                53, 53, true, false, 2, ais, worlds, [landscape], out diagnostic))
        {
            throw new InvalidOperationException("A demo stage accepted a restricted World excluded from demo.");
        }

        landscape.RestrictedWorlds = [ValidStageWorld("usable-world")];
        if (!StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                53, 53, false, false, 19, ais, worlds, [landscape], out diagnostic))
        {
            throw new InvalidOperationException("A LandscapeGenerator with an active restricted World was rejected: " + diagnostic);
        }

        landscape.RestrictedWorlds = [ValidStageWorld("usable-world"), null];
        if (StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                53, 53, false, false, 19, ais, worlds, [landscape], out diagnostic))
        {
            throw new InvalidOperationException("A random-selectable null restricted World was accepted beside a valid entry.");
        }

        landscape.RestrictedWorlds = [null];
        worlds[0].RestrictedMaps = [landscape];
        landscape.RestrictedWorlds = null;
        if (StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                53, 53, false, false, 19, ais, worlds, [ValidStageLandscape("fallback-map", 101)], out diagnostic))
        {
            throw new InvalidOperationException("A selected map with a null restricted World list was accepted.");
        }

        landscape.RestrictedWorlds = [null];
        if (!StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                53, 53, false, false, 19, ais, worlds, [ValidStageLandscape("fallback-map", 101)], out diagnostic))
        {
            throw new InvalidOperationException("An explicit World map restriction should supersede map World restrictions: " +
                diagnostic);
        }
    }),
    ("demo generation skips stages excluded from the demo without compacting indices", () =>
    {
        StageGenerationStageCandidate fullGameStage = new()
        {
            Id = "sample:full-game-stage",
            StageIndex = 7,
            MapSizeMin = 90,
            MapSizeMax = 90,
            IsPartOfDemo = false,
            Worlds = [ValidStageWorld("sample:world")]
        };
        StageGenerationAiCandidate[] ais = [ValidStageAi("sample:ai")];
        StageGenerationLandscapeCandidate[] landscapes = [ValidStageLandscape("sample:map", 53)];

        if (!StageGenerationCompatibility.TryValidateStages("default", true, false, 3,
                [fullGameStage], ais, landscapes, out string diagnostic))
        {
            throw new InvalidOperationException("A full-game-only stage should be skipped in demo validation: " + diagnostic);
        }

        if (fullGameStage.StageIndex != 7 ||
            StageGenerationCompatibility.TryValidateStages("default", false, false, 3,
                [fullGameStage], ais, landscapes, out diagnostic) ||
            diagnostic == null || !diagnostic.Contains("[90, 90]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Full-game validation failed to retain the stage index or map constraint.");
        }
    }),
    ("unresolved stage unit references are rejected", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.stages[0].unitIds = ["missing:unit"];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "reference.unresolved");
        });
    }),
    ("stage parameter overrides accept boolean flags and reject invalid values", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            pack.stages[0].parameterOverrides =
            [
                new ContentStageParameterOverride { name = "fillFirstLevelWithCardRewards", value = "true" },
                new ContentStageParameterOverride { name = "putDefaultCardRewardInCenter", value = "false" },
                new ContentStageParameterOverride { name = "fillLastLevelWithShops", value = "true" }
            ];
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());

            pack.stages[0].parameterOverrides =
            [
                new ContentStageParameterOverride { name = "fillFirstLevelWithCardRewards", value = "not-a-bool" }
            ];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "stage.parameter-override-invalid");
        });
    }),
    ("stage startCountRange override validates ordered native bounds", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            pack.stages[0].parameterOverrides =
            [
                new ContentStageParameterOverride { name = "startCountRange", value = "2,6" }
            ];
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());

            foreach (string invalidValue in new[] { "0,2", "2,1", "1,65", "1,2,3", "1.5,3", " 1,3" })
            {
                pack.stages[0].parameterOverrides =
                [
                    new ContentStageParameterOverride { name = "startCountRange", value = invalidValue }
                ];
                ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                    "stage.parameter-override-invalid");
            }
        });
    }),
    ("stage overrides reject fields absent from native StageParametersScriptableObject", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            foreach (string unsupportedName in new[]
                     { "maxAscensionLevel", "aiBuildDistance", "cardRewardCount", "shopCardCount" })
            {
                pack.stages[0].parameterOverrides =
                [
                    new ContentStageParameterOverride { name = unsupportedName, value = "1" }
                ];
                ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                    "stage.parameter-override-invalid");
            }
        });
    }),
    ("stage startCountRange fits the final maxWidth", () =>
    {
        if (!StageParameterOverrideParser.IsStartCountRangeWithinWidth(2, 6, 6) ||
            !StageParameterOverrideParser.IsStartCountRangeWithinWidth(1, 1, 1) ||
            StageParameterOverrideParser.IsStartCountRangeWithinWidth(2, 6, 5) ||
            StageParameterOverrideParser.IsStartCountRangeWithinWidth(0, 1, 4) ||
            StageParameterOverrideParser.IsStartCountRangeWithinWidth(3, 2, 4) ||
            StageParameterOverrideParser.IsStartCountRangeWithinWidth(1, 65, 64))
        {
            throw new InvalidOperationException("startCountRange must be ordered and fit the final maxWidth.");
        }
    }),
    ("balance overrides accept themed blueprint role and system-tag combinations", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            pack.units[0].balanceOverrides = pack.units[0].balanceOverrides
                .Where(entry => entry.name != "roles" && entry.name != "offeredSystemTags")
                .Concat(new[]
                {
                    new ContentBalanceOverride { name = "roles", value = "Unit, Mech, Melee" },
                    new ContentBalanceOverride { name = "offeredSystemTags", value = "Armor, Mech" }
                })
                .ToArray();
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("native blueprint role and tag flags combine and reject special duplicates", () =>
    {
        if (!BalanceOverrideFlags.TryParse("roles", "Unit, Mech, Melee", true, false,
                out HashSet<string> roles, out string diagnostic) || diagnostic != null ||
            !roles.SetEquals(new[] { "Unit", "Mech", "Melee" }) ||
            !BalanceOverrideFlags.TryParse("offeredSystemTags", "Armor, Mech", true, false,
                out HashSet<string> tags, out diagnostic) || diagnostic != null ||
            !tags.SetEquals(new[] { "Armor", "Mech" }))
        {
            throw new InvalidOperationException("Valid native role/tag combinations did not parse as independent flags.");
        }

        string[] invalidValues = { "Unit,,Mech", "Unit, Unit", "UnknownRole", "Unit, All", "None, Armor" };
        foreach (string invalid in invalidValues)
        {
            if (BalanceOverrideFlags.TryParse("roles", invalid, true, false, out _, out diagnostic) || diagnostic == null)
            {
                throw new InvalidOperationException("Invalid product roles were accepted: " + invalid);
            }
        }

        if (!BalanceOverrideFlags.TryParse("roles", "Building, Factory", false, true, out _, out diagnostic) ||
            diagnostic != null ||
            BalanceOverrideFlags.TryParse("roles", "Building", false, true, out _, out diagnostic) ||
            diagnostic == null)
        {
            throw new InvalidOperationException("Factory role constraints were not distinguished between products and buildings.");
        }
    }),
    ("native blueprint flags map to the pinned game enum values", () =>
    {
        if (!BalanceOverrideFlags.TryCreateEnumValue(typeof(UnitRole), "roles", "Unit, Mech, Melee",
                true, false, out object roleValue, out string diagnostic) || diagnostic != null ||
            (UnitRole)roleValue != (UnitRole.Unit | UnitRole.Mech | UnitRole.Melee) ||
            !BalanceOverrideFlags.TryCreateEnumValue(typeof(SystemTags), "offeredSystemTags", "Armor, Mech",
                true, false, out object tagValue, out diagnostic) || diagnostic != null ||
            (SystemTags)tagValue != (SystemTags.Armor | SystemTags.Mech) ||
            BalanceOverrideFlags.TryCreateEnumValue(typeof(UnitRole), "roles", "Unit, NotAFlag",
                true, false, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Native role/tag values were not converted or invalid flags did not fail closed.");
        }
    }),
    ("native blueprint flags apply to detached balance row instances", () =>
    {
        FieldInfo rolesField = typeof(EntityBalancingParameters).GetField("roles")!;
        FieldInfo tagsField = typeof(EntityBalancingParameters).GetField("offeredSystemTags")!;
        EntityBalancingParameters product = RuntimeMutatorTestData.Row("sample:product");
        EntityBalancingParameters factory = RuntimeMutatorTestData.Row("sample:factory", "sample:product");
        if (!BalanceOverrideFlags.TryApply(product, rolesField, "roles", "Unit, Mech, Melee",
                true, false, out object updatedProduct, out string diagnostic) || diagnostic != null ||
            !(updatedProduct is EntityBalancingParameters) ||
            ((EntityBalancingParameters)updatedProduct).roles != (UnitRole.Unit | UnitRole.Mech | UnitRole.Melee) ||
            !BalanceOverrideFlags.TryApply(product, tagsField, "offeredSystemTags", "Armor, Mech",
                true, false, out updatedProduct, out diagnostic) || diagnostic != null ||
            !(updatedProduct is EntityBalancingParameters) ||
            !BalanceOverrideFlags.TryApply(factory, rolesField, "roles", "Building, Factory",
                false, true, out object updatedFactory, out diagnostic) || diagnostic != null ||
            !(updatedFactory is EntityBalancingParameters) ||
            ((EntityBalancingParameters)updatedFactory).roles != (UnitRole.Building | UnitRole.Factory) ||
            BalanceOverrideFlags.TryApply(product, rolesField, "roles", "Unit, Factory",
                true, false, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Native balance row flags were not applied or invalid product roles were admitted.");
        }
    }),
    ("stage pool entries must not repeat unit IDs", () =>
    {
        ContentStageDefinition stage = new ContentStageDefinition
        {
            id = "sample:dup-pool-stage",
            blueprintPoolEntries =
            [
                new ContentStagePoolEntryDefinition { poolId = "pool-a", unitIds = new[] { "sample:a", "sample:a" } }
            ]
        };

        var diagnostics = new List<ContentDiagnostic>();
        ContentPackValidator.ValidateStageBlueprintPoolEntriesUnique(stage, "stages[0]", diagnostics);

        if (!diagnostics.Any(d => d.Code == "stage.blueprint-pool-unit-id-duplicate"))
        {
            throw new InvalidOperationException("Duplicate unit IDs in a pool entry must be rejected.");
        }
    }),
    };
}
