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
    internal static IEnumerable<(string Name, Action Run)> GetRuntimeAndDiagnosticsValidationTests() => new (string Name, Action Run)[]
    {
    ("visual socket discovery accepts canonical IDs and exact legacy suffixes only", () =>
    {
        if (!RogueCommandSocketCatalog.TryResolveCanonicalSocketName("Socket_TurretPivot", out string canonical) ||
            canonical != "Socket_TurretPivot" ||
            !RogueCommandSocketCatalog.TryResolveCanonicalSocketName(
                "Sandlance_Socket_Hardpoint_ERA", out string legacy) ||
            legacy != "Socket_Hardpoint_ERA")
        {
            throw new InvalidOperationException("A canonical or exact legacy socket name was not resolved.");
        }

        string[] unsupportedNames =
        {
            "Unit_Socket_TurretPivotExtra",
            "Socket_TurretPivot_Mesh",
            "Sensor_Eye",
            "VehicleSocket_RadarMast",
            "_Socket_TurretPivot"
        };
        foreach (string unsupportedName in unsupportedNames)
        {
            if (RogueCommandSocketCatalog.TryResolveCanonicalSocketName(unsupportedName, out _))
            {
                throw new InvalidOperationException("A noncanonical socket-like name was accepted: " + unsupportedName);
            }
        }
    }),
    ("visual socket roles disable ambiguous duplicate aliases", () =>
    {
        object canonical = new object();
        var oneRole = new UniqueSocketRoleBinding<object>();
        oneRole.Add(canonical);
        oneRole.Add(canonical);
        if (oneRole.IsAmbiguous || !ReferenceEquals(oneRole.Value, canonical))
        {
            throw new InvalidOperationException("Repeated registration of one transform should preserve its unique role.");
        }

        oneRole.Add(new object());
        if (!oneRole.IsAmbiguous || oneRole.Value != null)
        {
            throw new InvalidOperationException("Distinct aliases for one behavior should disable the ambiguous role.");
        }

        oneRole.Add(new object());
        if (!oneRole.IsAmbiguous || oneRole.Value != null)
        {
            throw new InvalidOperationException("An ambiguous role should remain disabled after later candidates arrive.");
        }
    }),
    ("ambiguous attachment group does not fall back to its physical socket", () =>
    {
        object socket = new object();
        object groupVisual = new object();
        var ambiguousGroup = new UniqueSocketRoleBinding<object>();
        ambiguousGroup.Add(new object());
        ambiguousGroup.Add(new object());

        if (UniqueSocketRoleBinding<object>.TryResolveVisual(ambiguousGroup, null!, socket, out _))
        {
            throw new InvalidOperationException("An ambiguous visual group fell back to its physical socket.");
        }

        var uniqueGroup = new UniqueSocketRoleBinding<object>();
        uniqueGroup.Add(groupVisual);
        if (!UniqueSocketRoleBinding<object>.TryResolveVisual(uniqueGroup, groupVisual, socket, out object? resolved) ||
            !ReferenceEquals(resolved, groupVisual))
        {
            throw new InvalidOperationException("A unique detached group was not preferred over its physical socket.");
        }

        if (!UniqueSocketRoleBinding<object>.TryResolveVisual(null!, null!, socket, out resolved) ||
            !ReferenceEquals(resolved, socket))
        {
            throw new InvalidOperationException("A missing detached group did not preserve the legacy socket fallback.");
        }
    }),
    ("runtime Harmony patch installer hook names resolve to declared methods", () =>
    {
        string[] hookNames =
        [
            "EntityBalancingStoreInitPostfix",
            "SetScriptableObjectPostfix",
            "CreateStageMapPrefix",
            "CreateStageMapPostfix",
            "StageMapJsonConstructorPostfix",
            "StageMapToJsonPostfix",
            "CreateLevelMapPrefix",
            "CreateLevelMapFinalizer",
            "InstantiateEntityPrefix",
            "InstantiateEntityPostfix",
            "CreateEntityMeshPrefix",
            "AiBehaviourUpdatePostfix",
            "DefaultAiBehaviourInitPostfix",
            "AiBehaviourDestroyPostfix",
            "BlueprintNamePrefix",
            "BlueprintDescriptionPrefix",
            "SkillDescriptionPrefix",
            "LandscapeLocalizationPrefix",
            "LandscapeLocalizationFinalizer",
            "FallbackLandscapeGeneratorsPostfix",
            "OutlyingStartAiBuildingDistancePrefix",
            "BlueprintPoolIsInPoolPrefix",
            "BlueprintPoolFindPrefix",
            "BlueprintPoolExclusiveBlueprintsPostfix",
            "PresentCardsToChoosePrefix",
            "PresentCardsToChooseFinalizer",
            "InstantiateCardsWithDelayPrefix",
            "BlueprintPoolBlueprintsInPoolPostfix",
            "PresentCardAsRewardRecordPostfix",
            "AllEntityIdsAllowedAsBlueprintsPostfix",
            "EntityFactorySetupPrespawningPostfix",
            "EntityFactorySetupAiPrespawningPostfix",
            "EntityAttackIsTargetInRangePostfix"
        ];

        foreach (string hookName in hookNames)
        {
            if (typeof(RuntimeHarmonyPatches).GetMethod(hookName,
                    BindingFlags.Public | BindingFlags.Static) == null)
            {
                throw new InvalidOperationException($"RuntimePatchInstaller expects missing hook '{hookName}'.");
            }
        }

        MethodInfo? instantiateCardsHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "InstantiateCardsWithDelayPrefix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] instantiateCardsParams = instantiateCardsHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (instantiateCardsHook == null || instantiateCardsHook.ReturnType != typeof(void) ||
            instantiateCardsParams.Length != 1 || instantiateCardsParams[0].ParameterType != typeof(List<string>))
        {
            throw new InvalidOperationException("InstantiateCardsWithDelayPrefix has an invalid Harmony signature.");
        }

        MethodInfo? poolCandidatesHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "BlueprintPoolBlueprintsInPoolPostfix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] poolCandidateParameters = poolCandidatesHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (poolCandidatesHook == null || poolCandidatesHook.ReturnType != typeof(void) ||
            poolCandidateParameters.Length != 4 || poolCandidateParameters[0].ParameterType != typeof(object) ||
            poolCandidateParameters[1].ParameterType != typeof(List<string>) ||
            poolCandidateParameters[2].ParameterType != typeof(int?) ||
            poolCandidateParameters[3].ParameterType != typeof(bool))
        {
            throw new InvalidOperationException("Blueprint pool candidate hook has an invalid Harmony signature.");
        }

        MethodInfo? rewardRecordHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "PresentCardAsRewardRecordPostfix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] rewardRecordParameters = rewardRecordHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (rewardRecordHook == null || rewardRecordHook.ReturnType != typeof(void) ||
            rewardRecordParameters.Length != 2 || rewardRecordParameters.Any(parameter =>
                parameter.ParameterType != typeof(string)))
        {
            throw new InvalidOperationException("Blueprint reward record hook has an invalid Harmony signature.");
        }

        MethodInfo? nameHook = typeof(RuntimeHarmonyPatches).GetMethod("BlueprintNamePrefix",
            BindingFlags.Public | BindingFlags.Static);
        MethodInfo? descriptionHook = typeof(RuntimeHarmonyPatches).GetMethod("BlueprintDescriptionPrefix",
            BindingFlags.Public | BindingFlags.Static);
        MethodInfo? skillDescriptionHook = typeof(RuntimeHarmonyPatches).GetMethod("SkillDescriptionPrefix",
            BindingFlags.Public | BindingFlags.Static);
        foreach (MethodInfo? hook in new[] { nameHook, descriptionHook, skillDescriptionHook })
        {
            ParameterInfo[] parameters = hook?.GetParameters() ?? Array.Empty<ParameterInfo>();
            if (hook == null || hook.ReturnType != typeof(bool) || parameters.Length != 2 ||
                parameters[0].ParameterType != typeof(string) ||
                parameters[1].ParameterType != typeof(string).MakeByRefType())
            {
                throw new InvalidOperationException("Blueprint localization hooks have an invalid Harmony signature.");
            }
        }

        MethodInfo? poolEligibilityHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "BlueprintPoolIsInPoolPrefix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] eligibilityParameters = poolEligibilityHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (poolEligibilityHook == null || poolEligibilityHook.ReturnType != typeof(bool) ||
            eligibilityParameters.Length != 3 || eligibilityParameters[0].ParameterType != typeof(object) ||
            eligibilityParameters[1].ParameterType != typeof(string) ||
            eligibilityParameters[2].ParameterType != typeof(bool).MakeByRefType())
        {
            throw new InvalidOperationException("BlueprintPool.IsInPool hook has an invalid Harmony signature.");
        }

        MethodInfo? savedMapConstructorHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "StageMapJsonConstructorPostfix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] savedMapConstructorParameters = savedMapConstructorHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (savedMapConstructorHook == null || savedMapConstructorHook.ReturnType != typeof(void) ||
            savedMapConstructorParameters.Length != 2 || savedMapConstructorParameters[0].ParameterType != typeof(object) ||
            savedMapConstructorParameters[1].ParameterType != typeof(string))
        {
            throw new InvalidOperationException("The StageMap JSON constructor hook has an invalid Harmony signature.");
        }

        MethodInfo? savedMapSerializeHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "StageMapToJsonPostfix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] savedMapSerializeParameters = savedMapSerializeHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (savedMapSerializeHook == null || savedMapSerializeHook.ReturnType != typeof(void) ||
            savedMapSerializeParameters.Length != 2 || savedMapSerializeParameters[0].ParameterType != typeof(object) ||
            savedMapSerializeParameters[1].ParameterType != typeof(string).MakeByRefType())
        {
            throw new InvalidOperationException("The StageMap JSON serialization hook has an invalid Harmony signature.");
        }

        MethodInfo? outlyingStartDistanceHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "OutlyingStartAiBuildingDistancePrefix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] distanceParameters = outlyingStartDistanceHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (outlyingStartDistanceHook == null || outlyingStartDistanceHook.ReturnType != typeof(bool) ||
            distanceParameters.Length != 1 || distanceParameters[0].ParameterType != typeof(int).MakeByRefType())
        {
            throw new InvalidOperationException("The appended-stage AI spacing hook has an invalid Harmony signature.");
        }

        MethodInfo? poolFindHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "BlueprintPoolFindPrefix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] findParameters = poolFindHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (poolFindHook == null || poolFindHook.ReturnType != typeof(bool) || findParameters.Length != 2 ||
            findParameters[0].ParameterType != typeof(string) ||
            findParameters[1].ParameterType != typeof(global::BlueprintPool).MakeByRefType())
        {
            throw new InvalidOperationException("BlueprintPool.FindBlueprintPool hook has an invalid Harmony signature.");
        }

        MethodInfo? exclusiveHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "BlueprintPoolExclusiveBlueprintsPostfix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] exclusiveParameters = exclusiveHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (exclusiveHook == null || exclusiveHook.ReturnType != typeof(void) || exclusiveParameters.Length != 2 ||
            exclusiveParameters[0].ParameterType != typeof(string) ||
            exclusiveParameters[1].ParameterType != typeof(HashSet<string>))
        {
            throw new InvalidOperationException("BlueprintPool.ExclusiveBlueprints hook has an invalid Harmony signature.");
        }

        if (typeof(UnityResourcesLoadTypedPatch).GetMethod("Prefix",
                BindingFlags.Public | BindingFlags.Static) == null)
        {
            throw new InvalidOperationException("The typed Resources.Load patch prefix is missing.");
        }

        if (typeof(UnityResourcesLoadUntypedPatch).GetMethod("Prefix",
                BindingFlags.Public | BindingFlags.Static) == null)
        {
            throw new InvalidOperationException("The untyped Resources.Load patch prefix is missing.");
        }
    }),
    ("unit diagnostic report variant count defaults to zero and accepts two", () =>
    {
        var defaultReport = new UnitDiagnosticReport("test:unit", "test:pack", "prefab", "card", false);
        PropertyInfo? count = typeof(UnitDiagnosticReport).GetProperty("VariantCount");
        ConstructorInfo? constructor = typeof(UnitDiagnosticReport).GetConstructors()
            .SingleOrDefault(candidate => candidate.GetParameters().LastOrDefault()?.Name == "variantCount");
        if (count == null || count.PropertyType != typeof(int) || constructor == null ||
            !constructor.GetParameters().Last().IsOptional ||
            count.GetValue(defaultReport) is not int defaultCount || defaultCount != 0)
        {
            throw new InvalidOperationException("VariantCount must be an optional final constructor parameter defaulting to zero.");
        }
        var twoReport = new UnitDiagnosticReport("test:unit", "test:pack", "prefab", "card", false,
            variantCount: 2);
        if (count.GetValue(twoReport) is not int declaredCount || declaredCount != 2)
        {
            throw new InvalidOperationException("The diagnostic report lost its explicitly declared variant count.");
        }
        if (UnitVariantDiagnosticsFormatter.FormatVariantCount(twoReport) !=
            "Variantes declaradas (solo metadatos): 2")
        {
            throw new InvalidOperationException("The unit diagnostics label must expose the metadata-only variant count.");
        }
    }),
    ("runtime entity registration stores MoveSpeed on the row across cache invalidation", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            var unit = new ContentUnitDefinition
            {
                id = "sample:move-cache",
                movement = new ContentUnitMovementDefinition { moveSpeed = 12.5f }
            };
            var provider = new RuntimeMutatorTestProvider();
            EntityBalancingParameters row = RuntimeMutatorTestData.Row(unit.id);
            row.moveSpeed = 1f;
            provider.EntityRows.Add(row);
            provider.BalanceOverrides.Add(new BalanceOverride
            {
                EntityId = unit.id,
                Parameters = UnitBalanceOverrideMapper.CreateCustomBalance(unit)
            });
            var mutator = new RuntimeContentMutator(
                typeof(EntityBalancingStore).Assembly, provider, null);

            if (!mutator.RegisterEntityRows())
            {
                throw new InvalidOperationException("Entity registration failed.");
            }

            EntityBalancingStore.InvalidateCache(unit.id);
            EntityBalancingParameters registered = EntityBalancingStore.EntityBalancingParametersList[0];
            EntityBalancingParameters source = (EntityBalancingParameters)provider.EntityRows[0];
            if (registered.moveSpeed != 12.5f || source.moveSpeed != 12.5f ||
                EntityBalancingStore.ChangeableFloatValueCache[unit.id].Count != 0)
            {
                throw new InvalidOperationException(
                    "Structured MoveSpeed must remain in both provider and registered rows after cache invalidation.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity registration stores MaxHealth on the row across cache invalidation", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            const string entityId = "sample:max-health-cache";
            var provider = new RuntimeMutatorTestProvider();
            EntityBalancingParameters row = RuntimeMutatorTestData.Row(entityId);
            row.maxHealth = 100;
            provider.EntityRows.Add(row);
            provider.BalanceOverrides.Add(new BalanceOverride
            {
                EntityId = entityId,
                Parameters = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["MaxHealth"] = 450d
                }
            });
            var mutator = new RuntimeContentMutator(
                typeof(EntityBalancingStore).Assembly, provider, null);

            if (!mutator.RegisterEntityRows())
            {
                throw new InvalidOperationException("Entity registration failed.");
            }

            EntityBalancingStore.InvalidateCache(entityId);
            EntityBalancingParameters registered = EntityBalancingStore.EntityBalancingParametersList[0];
            EntityBalancingParameters source = (EntityBalancingParameters)provider.EntityRows[0];
            if (registered.maxHealth != 450 || source.maxHealth != 450 ||
                EntityBalancingStore.ChangeableIntValueCache[entityId].Count != 0)
            {
                throw new InvalidOperationException(
                    "MaxHealth must remain in both provider and registered rows after cache invalidation.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity registration rejects integers rounded by the native getter", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            const string entityId = "sample:rounded-max-health";
            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row(entityId));
            provider.BalanceOverrides.Add(new BalanceOverride
            {
                EntityId = entityId,
                Parameters = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["MaxHealth"] = 16777217d
                }
            });
            var mutator = new RuntimeContentMutator(
                typeof(EntityBalancingStore).Assembly, provider, null);

            if (mutator.RegisterEntityRows() || EntityBalancingStore.EntityBalancingParametersList.Count != 0)
            {
                throw new InvalidOperationException(
                    "An integer balance override rounded by the native getter must not be registered.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime mutation handles invalid targets and fields", () =>
    {
        var mutationManager = new RuntimeMutationManager(msg => { });

        // Null target
        if (mutationManager.MutateField(null!, "someField", 1.0f)) 
            throw new InvalidOperationException("Mutation should fail for null target.");

        // Non-existent field
        var target = new object();
        if (mutationManager.MutateField(target, "NonExistentField", 1.0f)) 
            throw new InvalidOperationException("Mutation should fail for non-existent field.");

        // Wrong type
        var buff = new EntityValueModifier { value = 1.0f };
        if (mutationManager.MutateField(buff, "value", "not a float")) 
            throw new InvalidOperationException("Mutation should fail for type mismatch.");

        // AI Behaviour: null manager
        if (mutationManager.MutateActiveAiBehaviour(null!, "tickRate", 0.5f)) 
            throw new InvalidOperationException("AI mutation should fail for null manager.");

        // AI Behaviour: null activeBehaviour
        var manager = new AiBehaviourManager { activeBehaviour = null! };
        if (mutationManager.MutateActiveAiBehaviour(manager, "tickRate", 0.5f)) 
            throw new InvalidOperationException("AI mutation should fail for null activeBehaviour.");
    }),
    ("production building visual-source compatibility is explicit and independent", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentUnitDefinition unit = pack.units[0];
            ContentProductionBuildingDefinition building = unit.productionBuilding;
            string originalUnitBundleId = unit.assetBundleId;
            string originalUnitCardImageAssetName = unit.cardImageAssetName;
            building.assetBundleId = pack.assetBundles[0].id;
            building.visualAssetName = "FactoryVisual";
            building.visualRootPath = "FactoryModel";
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (unit.assetBundleId != originalUnitBundleId ||
                unit.cardImageAssetName != originalUnitCardImageAssetName ||
                building.assetBundleId != pack.assetBundles[0].id ||
                building.visualAssetName != "FactoryVisual" ||
                building.visualRootPath != "FactoryModel")
                throw new InvalidOperationException("Factory visual content should not mutate product unit asset fields.");
        });
    }),
    ("game compatibility rejects an unsupported Unity version", () =>
    {
        string previousVersion = UnityEngine.Application.unityVersion;
        try
        {
            UnityEngine.Application.unityVersion = "2022.3.0f1";
            if (GameCompatibility.TryValidate(Path.GetTempPath(), out string diagnostic) ||
                diagnostic == null || !diagnostic.Contains("Unsupported Unity version", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("An unsupported Unity version was not rejected with a local diagnostic.");
            }
        }
        finally
        {
            UnityEngine.Application.unityVersion = previousVersion;
        }
    }),
    ("game compatibility rejects an unsupported game version marker", () =>
    {
        WithOwnedTemporaryDirectory("rogue-command-version-", root =>
        {
            UnityEngine.Application.unityVersion = GameCompatibility.SupportedUnityVersion;
            File.WriteAllText(Path.Combine(root, "version.txt"), "9= Version 1.0.8\n");
            if (GameCompatibility.TryValidate(root, out string diagnostic) ||
                diagnostic == null || !diagnostic.Contains("Unsupported Rogue Command version marker", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("An unsupported game version marker was not rejected.");
            }
        });
    }),
    ("game compatibility rejects an Assembly-CSharp hash mismatch", () =>
    {
        WithOwnedTemporaryDirectory("rogue-command-hash-", root =>
        {
            UnityEngine.Application.unityVersion = GameCompatibility.SupportedUnityVersion;
            File.WriteAllText(Path.Combine(root, "version.txt"), "9= Version 1.0.9\n");
            string managedDirectory = Path.Combine(root, "Rogue Command_Data", "Managed");
            Directory.CreateDirectory(managedDirectory);
            File.WriteAllBytes(Path.Combine(managedDirectory, "Assembly-CSharp.dll"), [0x4D, 0x5A]);
            if (GameCompatibility.TryValidate(root, out string diagnostic) ||
                diagnostic == null || !diagnostic.Contains("SHA-256 mismatch", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A changed Assembly-CSharp hash was not rejected.");
            }
        });
    }),
    ("runtime build guard rejects an unsupported Rogue Command version", () =>
    {
        Assembly fakeGameAssembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Assembly-CSharp"), AssemblyBuilderAccess.Run);
        if (RuntimeBuildGuard.TryValidate(Path.GetTempPath(), "1.0.8", fakeGameAssembly,
            out string diagnostic) || diagnostic == null ||
            !diagnostic.Contains("Expected Rogue Command 1.0.9", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("RuntimeBuildGuard did not fail closed on a mismatched game version.");
        }
    }),
    ("runtime build guard rejects a mismatched Assembly-CSharp MVID", () =>
    {
        Assembly fakeGameAssembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Assembly-CSharp"), AssemblyBuilderAccess.Run);
        if (RuntimeBuildGuard.TryValidate(Path.GetTempPath(), RuntimeBuildGuard.ExpectedGameVersion,
            fakeGameAssembly, out string diagnostic) || diagnostic == null ||
            !diagnostic.Contains("MVID does not match", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("RuntimeBuildGuard did not reject an unverified Assembly-CSharp MVID.");
        }
    }),
    ("runtime entity mutator appends balancing rows indices factory mappings and stat caches idempotently", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:product"));
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:factory", "sample:product"));
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);

            if (!mutator.RegisterEntityRows() || !mutator.RegisterEntityRows())
            {
                throw new InvalidOperationException("The runtime mutator did not accept a valid entity registration batch.");
            }

            if (EntityBalancingStore.EntityBalancingParametersList.Count != 2 ||
                EntityBalancingStore.ParameterListIndexOf.Count != 2 ||
                EntityBalancingStore.ParameterListIndexOf["sample:product"] != 0 ||
                EntityBalancingStore.ParameterListIndexOf["sample:factory"] != 1 ||
                EntityBalancingStore.FactoryEntityIdOf.Count != 1 ||
                EntityBalancingStore.FactoryEntityIdOf["sample:product"] != "sample:factory" ||
                EntityBalancingStore.ChangeableIntValueCache.Count != 2 ||
                EntityBalancingStore.ChangeableFloatValueCache.Count != 2 ||
                EntityBalancingStore.ChangeableIntValueCache["sample:product"].Count != 0 ||
                EntityBalancingStore.ChangeableFloatValueCache["sample:product"].Count != 0 ||
                EntityBalancingStore.ChangeableIntValueCache["sample:factory"].Count != 0 ||
                EntityBalancingStore.ChangeableFloatValueCache["sample:factory"].Count != 0 ||
                provider.EntityRowsRegisteredCount != 1)
            {
                throw new InvalidOperationException("The runtime mutator left inconsistent registry rows, indices, factory mappings, or stat caches.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity registration invalidates cached image misses for new unit and factory IDs", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            EntityBalancingStore.ImageOf.Add("sample:product", null!);
            var cachedFactorySprite = new UnityEngine.Sprite();
            EntityBalancingStore.ImageOf.Add("sample:factory", cachedFactorySprite);
            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:product"));
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:factory", "sample:product"));
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);

            bool registered = mutator.RegisterEntityRows();
            if (!registered || EntityBalancingStore.ImageOf.ContainsKey("sample:product") ||
                !EntityBalancingStore.ImageOf.ContainsKey("sample:factory") ||
                !ReferenceEquals(EntityBalancingStore.ImageOf["sample:factory"], cachedFactorySprite))
            {
                throw new InvalidOperationException("Entity registration cache state mismatch. registered=" + registered +
                    ", imageCount=" + EntityBalancingStore.ImageOf.Count +
                    ", product=" + EntityBalancingStore.ImageOf.ContainsKey("sample:product") +
                    ", factory=" + EntityBalancingStore.ImageOf.ContainsKey("sample:factory") +
                    ", sameSprite=" + (EntityBalancingStore.ImageOf.ContainsKey("sample:factory") &&
                        ReferenceEquals(EntityBalancingStore.ImageOf["sample:factory"], cachedFactorySprite)));
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity mutator rejects an ID collision without partially changing the registry", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            EntityBalancingStore.EntityBalancingParametersList.Add(RuntimeMutatorTestData.Row("sample:existing"));
            EntityBalancingStore.ParameterListIndexOf.Add("sample:existing", 0);
            EntityBalancingStore.ChangeableIntValueCache.Add("sample:existing",
                new Dictionary<EntityBalancingStore.ChangeableValue, int>());
            EntityBalancingStore.ChangeableFloatValueCache.Add("sample:existing",
                new Dictionary<EntityBalancingStore.ChangeableValue, float>());

            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:new"));
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:existing"));
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);

            if (mutator.RegisterEntityRows() ||
                EntityBalancingStore.EntityBalancingParametersList.Count != 1 ||
                EntityBalancingStore.EntityBalancingParametersList[0].entityId != "sample:existing" ||
                EntityBalancingStore.ParameterListIndexOf.Count != 1 ||
                EntityBalancingStore.ParameterListIndexOf["sample:existing"] != 0 ||
                EntityBalancingStore.FactoryEntityIdOf.Count != 0 ||
                EntityBalancingStore.ChangeableIntValueCache.Count != 1 ||
                EntityBalancingStore.ChangeableFloatValueCache.Count != 1 ||
                !EntityBalancingStore.ChangeableIntValueCache.ContainsKey("sample:existing") ||
                !EntityBalancingStore.ChangeableFloatValueCache.ContainsKey("sample:existing") ||
                provider.EntityRowsRegisteredCount != 0)
            {
                throw new InvalidOperationException("A duplicate entity ID changed existing registry state or partially appended its batch.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity mutator rolls back rows and value caches after index insertion fails", () =>
    {
        var comparer = new ThrowOnFirstTargetHashComparer("sample:product");
        EntityBalancingStore.Reset(comparer);
        try
        {
            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:product"));
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:factory", "sample:product"));
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);

            bool registered = mutator.RegisterEntityRows();
            if (registered || !comparer.FailureTriggered ||
                EntityBalancingStore.EntityBalancingParametersList.Count != 0 ||
                EntityBalancingStore.ParameterListIndexOf.Count != 0 ||
                EntityBalancingStore.FactoryEntityIdOf.Count != 0 ||
                EntityBalancingStore.ChangeableIntValueCache.Count != 0 ||
                EntityBalancingStore.ChangeableFloatValueCache.Count != 0 ||
                provider.EntityRowsRegisteredCount != 0)
            {
                throw new InvalidOperationException("A failed index commit did not roll back all appended registry state: " +
                    "registered=" + registered +
                    ", injectedFailure=" + comparer.FailureTriggered +
                    ", targetHashCalls=" + comparer.TargetHashCalls +
                    ", comparerAttached=" + ReferenceEquals(EntityBalancingStore.ParameterListIndexOf.Comparer, comparer) +
                    ", rows=" + EntityBalancingStore.EntityBalancingParametersList.Count +
                    ", indices=" + EntityBalancingStore.ParameterListIndexOf.Count +
                    ", factoryMappings=" + EntityBalancingStore.FactoryEntityIdOf.Count +
                    ", intCache=" + EntityBalancingStore.ChangeableIntValueCache.Count +
                    ", floatCache=" + EntityBalancingStore.ChangeableFloatValueCache.Count +
                    ", notifications=" + provider.EntityRowsRegisteredCount + ".");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity mutator preserves rows index and both caches when cache rollback fails", () =>
    {
        var indexComparer = new ThrowOnFirstTargetHashComparer("sample:product");
        var cacheComparer = new ThrowOnTargetHashCallComparer("sample:product", 3);
        EntityBalancingStore.Reset(indexComparer);
        EntityBalancingStore.ChangeableFloatValueCache =
            new Dictionary<string, Dictionary<EntityBalancingStore.ChangeableValue, float>>(cacheComparer);
        try
        {
            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:product"));
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);

            bool registered = mutator.RegisterEntityRows();
            if (registered || !cacheComparer.FailureTriggered ||
                EntityBalancingStore.EntityBalancingParametersList.Count != 1 ||
                EntityBalancingStore.ParameterListIndexOf.Count != 1 ||
                EntityBalancingStore.ParameterListIndexOf["sample:product"] != 0 ||
                EntityBalancingStore.ChangeableIntValueCache.Count != 1 ||
                EntityBalancingStore.ChangeableFloatValueCache.Count != 1 ||
                EntityBalancingStore.ChangeableIntValueCache["sample:product"].Count != 0 ||
                EntityBalancingStore.ChangeableFloatValueCache["sample:product"].Count != 0 ||
                provider.EntityRowsRegisteredCount != 0)
            {
                throw new InvalidOperationException("The mutator did not preserve a consistent unit row, index, and cache pair after cleanup failed: " +
                    "registered=" + registered +
                    ", injectedFailure=" + cacheComparer.FailureTriggered +
                    ", rows=" + EntityBalancingStore.EntityBalancingParametersList.Count +
                    ", indices=" + EntityBalancingStore.ParameterListIndexOf.Count +
                    ", intCache=" + EntityBalancingStore.ChangeableIntValueCache.Count +
                    ", floatCache=" + EntityBalancingStore.ChangeableFloatValueCache.Count + ".");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity mutator rejects a missing factory product before registry changes", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row("sample:factory", "sample:missing-product"));
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);

            if (mutator.RegisterEntityRows() || EntityBalancingStore.EntityBalancingParametersList.Count != 0 ||
                EntityBalancingStore.ParameterListIndexOf.Count != 0 ||
                EntityBalancingStore.FactoryEntityIdOf.Count != 0 || provider.EntityRowsRegisteredCount != 0)
            {
                throw new InvalidOperationException("A factory with an unresolved product changed the game registry.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("loader diagnostics snapshot preserves its pre-extension constructor", () =>
    {
        var snapshot = new LoaderDiagnosticsSnapshot(
            "1.0.9", true, true, 0, 0, 0,
            Array.Empty<PackDiagnosticReport>(),
            Array.Empty<UnitDiagnosticReport>(),
            Array.Empty<StageDiagnosticReport>(),
            Array.Empty<AiProfileDiagnosticReport>(),
            Array.Empty<UnitFactoryDiagnosticReport>(),
            Array.Empty<StagePoolScopeDiagnosticReport>(),
            Array.Empty<AiProfileStageRangeDiagnosticReport>(),
            Array.Empty<string>());

        if (snapshot.UnitEligibilities.Count != 0 || snapshot.UnitAssetResolutions.Count != 0)
        {
            throw new InvalidOperationException("The legacy snapshot constructor must initialize new diagnostic lists as empty.");
        }
    }),
    ("loader unit diagnostic builder reports eligibility and asset resolution", () =>
    {
        UnitDiagnosticReport[] units =
        [
            new UnitDiagnosticReport("test:unit", "test:pack", "unit-prefab", "unit-card",
                false, true, false, true, false)
        ];
        var resolvedResources = new HashSet<string>(new[] { "unit-prefab", "unit-card" }, StringComparer.Ordinal);
        var visualRequests = new HashSet<string>(new[] { "test:unit" }, StringComparer.Ordinal);
        var resolvedVisuals = new HashSet<string>(new[] { "test:unit" }, StringComparer.Ordinal);

        IReadOnlyList<UnitEligibilityDiagnosticReport> eligibility =
            LoaderUnitDiagnosticsBuilder.CreateEligibilityReports(units);
        IReadOnlyList<UnitAssetResolutionDiagnosticReport> assets =
            LoaderUnitDiagnosticsBuilder.CreateAssetResolutionReports(
                units, resolvedResources, visualRequests, resolvedVisuals);

        string eligibilityLine = LoaderDiagnosticsFormatter.FormatUnitEligibility(eligibility[0]);
        if (eligibility.Count != 1 || eligibility[0].IsAllowedAsBlueprint ||
            !eligibility[0].IsAllowedAsStartingBlueprint || eligibility[0].IsAllowedForAi ||
            !eligibility[0].IsAllowedForDemo ||
            eligibilityLine != "test:unit [Blueprint=False, StartingBlueprint=True, AI=False, Demo=True, Specialist=False]" ||
            assets.Count != 1 || !assets[0].PrefabResolved || !assets[0].CardImageResolved ||
            !assets[0].VisualResolved)
        {
            throw new InvalidOperationException("Loader diagnostics did not preserve unit eligibility and resolved asset state.");
        }

        IReadOnlyList<UnitAssetResolutionDiagnosticReport> missing =
            LoaderUnitDiagnosticsBuilder.CreateAssetResolutionReports(
                units, new HashSet<string>(StringComparer.Ordinal), visualRequests,
                new HashSet<string>(StringComparer.Ordinal));
        if (missing[0].PrefabResolved || missing[0].CardImageResolved || missing[0].VisualResolved)
        {
            throw new InvalidOperationException("Unresolved unit resources must be reported as missing.");
        }
    }),
    ("unit asset diagnostics distinguish missing, unresolved, and resolved requests", () =>
    {
        UnitDiagnosticReport[] units =
        [
            new UnitDiagnosticReport("test:unit-visual", "test:pack", "unit-prefab", "unit-card",
                false, true, false, true, false)
        ];

        IReadOnlyList<UnitAssetResolutionDiagnosticReport> missingVisual =
            LoaderUnitDiagnosticsBuilder.CreateAssetResolutionReports(
                units,
                new HashSet<string>(new[] { "unit-prefab", "unit-card" }, StringComparer.Ordinal),
                new HashSet<string>(new[] { "test:unit-visual" }, StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));
        IReadOnlyList<UnitAssetResolutionDiagnosticReport> unverifiedNativeCard =
            LoaderUnitDiagnosticsBuilder.CreateAssetResolutionReports(
                units,
                new HashSet<string>(new[] { "unit-prefab" }, StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));
        IReadOnlyList<UnitAssetResolutionDiagnosticReport> resolvedVisual =
            LoaderUnitDiagnosticsBuilder.CreateAssetResolutionReports(
                units,
                new HashSet<string>(new[] { "unit-prefab", "unit-card" }, StringComparer.Ordinal),
                new HashSet<string>(new[] { "test:unit-visual" }, StringComparer.Ordinal),
                new HashSet<string>(new[] { "test:unit-visual" }, StringComparer.Ordinal));

        if (LoaderUnitDiagnosticsBuilder.GetAssetResolutionSeverity(missingVisual[0]) !=
                UnitAssetResolutionSeverity.Error ||
            LoaderUnitDiagnosticsBuilder.GetAssetResolutionSeverity(unverifiedNativeCard[0]) !=
                UnitAssetResolutionSeverity.Warning ||
            LoaderUnitDiagnosticsBuilder.GetAssetResolutionSeverity(resolvedVisual[0]) !=
                UnitAssetResolutionSeverity.Success)
        {
            throw new InvalidOperationException("Asset diagnostics must preserve error, warning, and success severity.");
        }
    }),
    ("cross-pack graph cycle diagnostics exclude external predecessors", () =>
    {
        var packs = new[]
        {
            new ContentPackDefinition
            {
                packId = "test:alpha-pack",
                units = new[]
                {
                    new ContentUnitDefinition
                    {
                        id = "test:alpha-unit",
                        upgrades = new[] { new ContentUnitUpgradeDefinition { targetUnitId = "test:beta-unit" } }
                    }
                }
            },
            new ContentPackDefinition
            {
                packId = "test:beta-pack",
                units = new[]
                {
                    new ContentUnitDefinition
                    {
                        id = "test:beta-unit",
                        upgrades = new[] { new ContentUnitUpgradeDefinition { targetUnitId = "test:alpha-unit" } }
                    }
                }
            },
            new ContentPackDefinition
            {
                packId = "test:gamma-pack",
                units = new[]
                {
                    new ContentUnitDefinition
                    {
                        id = "test:gamma-unit",
                        upgrades = new[] { new ContentUnitUpgradeDefinition { targetUnitId = "test:alpha-unit" } }
                    }
                }
            }
        };

        var issues = ContentPackValidator.ValidateCrossPackUnitReferenceGraphs(packs);
        if (issues.Any(issue => issue.PackId == "test:gamma-pack") ||
            !issues.Any(issue => issue.PackId == "test:alpha-pack") ||
            !issues.Any(issue => issue.PackId == "test:beta-pack"))
        {
            throw new InvalidOperationException("Cycle diagnostics must include cycle owners, not external predecessors.");
        }
    }),
    ("prespawn quota binds production factory ID and preserves anti-hitch quotas", () =>
    {
        PrespawnQuota quota = new PrespawnQuota("sample:unit-scout", 20, false, "sample:building-scout-factory");
        if (quota.EntityId != "sample:unit-scout" ||
            quota.Quota != 20 ||
            quota.IsBuilding != false ||
            quota.FactoryId != "sample:building-scout-factory")
        {
            throw new InvalidOperationException("PrespawnQuota does not preserve entity ID, quota, building flag, or factory ID.");
        }

        PrespawnQuota standaloneQuota = new PrespawnQuota("sample:building-turret", 5, true);
        if (standaloneQuota.EntityId != "sample:building-turret" ||
            standaloneQuota.Quota != 5 ||
            standaloneQuota.IsBuilding != true ||
            standaloneQuota.FactoryId != null)
        {
            throw new InvalidOperationException("Standalone PrespawnQuota does not default factory ID to null.");
        }
    }),
    ("runtime unit mechanics director executes baseline status effects, combat boosts, veterancy rank-ups, healing, and auras", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        UnityEngine.Time.time = 10f;

        try
        {
            var profile = new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:mechanics-unit",
                MinAttackRange = 4.5f,
                CriticalHitChance = 0.25f,
                OnHitStatusEffects = new[]
                {
                    new ContentOnHitStatusEffectDefinition { statusEffect = "Stun", duration = 2.0f, chance = 0.35f }
                },
                BaselineStatusEffects = new[] { "Armored", "Robust" },
                Healing = new ContentUnitHealingDefinition
                {
                    healAmount = 15f,
                    repairAmount = 20f,
                    healRange = 4.0f,
                    healRate = 1.0f
                },
                Aura = new ContentUnitAuraDefinition
                {
                    auraRadius = 4.0f,
                    auraType = "Shield",
                    buffAmount = 12f,
                    affectsAllies = true,
                    affectsEnemies = false
                },
                CombatBoosts = new[]
                {
                    new ContentCombatBoostDefinition
                    {
                        id = "test:boost-speed",
                        statName = "MoveSpeed",
                        multiplier = 1.3f,
                        duration = 3.0f
                    }
                },
                Experience = new ContentUnitExperienceDefinition
                {
                    experiencePerLevel = 1,
                    maxLevel = 3,
                    experienceGainMultiplier = 1.0f,
                    levelUpBonus = 0.20f
                }
            };

            RuntimeUnitMechanicsDirector.RegisterProfile("test:mechanics-unit", profile);
            string summary = RuntimeUnitMechanicsDirector.FormatMechanicsSummary("test:mechanics-unit");
            if (string.IsNullOrWhiteSpace(summary) ||
                !summary.Contains("MinRange: 4.5m", StringComparison.Ordinal) ||
                !summary.Contains("Crit: 25%", StringComparison.Ordinal) ||
                !summary.Contains("OnHit: Stun", StringComparison.Ordinal) ||
                !summary.Contains("Pasivos: Armored, Robust", StringComparison.Ordinal) ||
                !summary.Contains("Curación: 15 HP / 20 Arm", StringComparison.Ordinal) ||
                !summary.Contains("Aura: Shield", StringComparison.Ordinal) ||
                !summary.Contains("Veteranía: Rango 3", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unexpected mechanics summary: " + summary);
            }

            var self = new EntityController
            {
                entityId = "test:mechanics-unit",
                UniqueEntityId = 101,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 60f,
                MaxHealth = 100f,
                HasShield = true,
                CurrentShield = 10f,
                MaxShield = 50f,
                CurrentRank = 0,
                MaxRank = 3,
                Position = new UnityEngine.Vector3(0f, 0f, 0f)
            };
            var ally = new EntityController
            {
                entityId = "test:ally",
                UniqueEntityId = 102,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 50f,
                MaxHealth = 100f,
                CurrentArmor = 30f,
                MaxArmor = 100f,
                HasShield = true,
                CurrentShield = 20f,
                MaxShield = 60f,
                Position = new UnityEngine.Vector3(15f, 0f, 0f)
            };
            var enemy = new EntityController
            {
                entityId = "test:enemy",
                UniqueEntityId = 201,
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 0f,
                MaxHealth = 100f,
                DestroyHasBeenStarted = true,
                Position = new UnityEngine.Vector3(10f, 0f, 0f)
            };

            ExistingControllers.Instance.PlayerUnits().Add(self);
            ExistingControllers.Instance.PlayerUnits().Add(ally);
            ExistingControllers.Instance.AiUnits().Add(enemy);

            RuntimeUnitMechanicsDirector.AttachIfApplicable(self);
            if (!self.AppliedStatusEffects.Any(e => e.Effect == StatusEffect.Armored && e.DurationType == SetStatusEffect.DurationType.Unlimited) ||
                !self.AppliedStatusEffects.Any(e => e.Effect == StatusEffect.Robust && e.DurationType == SetStatusEffect.DurationType.Unlimited))
            {
                throw new InvalidOperationException("AttachIfApplicable did not apply baseline unlimited status effects.");
            }

            var director = self.gameObject.GetComponent<RuntimeUnitMechanicsDirector>();
            if (director == null)
            {
                throw new InvalidOperationException("AttachIfApplicable did not attach RuntimeUnitMechanicsDirector component.");
            }

            // Advance time past initial 0.5s check window and tick healing + aura
            UnityEngine.Time.time = 11f;
            director.TickMechanics();

            if (ally.CurrentHealth != 65f || ally.CurrentArmor != 50f || ally.CurrentShield != 32f)
            {
                throw new InvalidOperationException(
                    $"Expected ally HP=65, Armor=50, Shield=32 after healing+shield aura pulse, got HP={ally.CurrentHealth}, Armor={ally.CurrentArmor}, Shield={ally.CurrentShield}");
            }

            // Trigger OnEntityDealtDamage on a killed target (3 XP >= threshold 3 -> immediate RankUp + CombatBoost + levelUpBonus heal)
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(enemy, 45f, self);

            if (self.CurrentRank != 1)
            {
                throw new InvalidOperationException($"Expected self rank to promote to 1 on kill, got {self.CurrentRank}");
            }
            if (!self.AppliedStatusEffects.Any(e => e.Effect == StatusEffect.Turbo && e.DurationType == SetStatusEffect.DurationType.Seconds && e.Duration == 3.0f))
            {
                throw new InvalidOperationException("Expected MoveSpeed combatBoost to apply timed Turbo status effect.");
            }
            if (!self.AppliedStatusEffects.Any(e => e.Effect == StatusEffect.CombatBoost && e.DurationType == SetStatusEffect.DurationType.Unlimited))
            {
                throw new InvalidOperationException("Expected veterancy promotion to apply unlimited CombatBoost status effect.");
            }
            if (self.CurrentHealth <= 60f)
            {
                throw new InvalidOperationException($"Expected veterancy levelUpBonus to heal self above 60 HP, got {self.CurrentHealth}");
            }
            if (!self.AppliedSpecificValueChanges.Any(c =>
                    c.Value == EntityController.ChangeableValue.MoveSpeed &&
                    Math.Abs(c.Change.ValueToAdd - 0.3f) < 0.001f &&
                    c.Change.Add == SpecificValueChange.AddType.Relative &&
                    !c.IsStackable))
            {
                throw new InvalidOperationException("Expected MoveSpeed combatBoost to apply relative +30% SpecificValueChange.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            Bank.Reset();
            UnityEngine.Time.time = 0f;
        }
    }),
    ("runtime unit mechanics director executes synergies, autonomous stealth, command aura, and kill experience rewards", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        Bank.Reset();
        UnityEngine.Time.time = 20f;

        try
        {
            var profile = new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:commander-unit",
                Synergies = new[]
                {
                    new ContentUnitSynergyDefinition
                    {
                        id = "test:synergy-armor",
                        statName = "ArmorProtection",
                        multiplier = 1.25f,
                        radius = 4.0f,
                        targetUnitIds = new[] { "*" }
                    }
                },
                Stealth = new ContentUnitStealthDefinition
                {
                    stealthLevel = 2.0f,
                    cloakDuration = 4.5f,
                    detectionReduction = 0.5f
                },
                Command = new ContentUnitCommandDefinition
                {
                    commandRange = 4.0f,
                    commandType = "Attack",
                    maxCommandChain = 3
                },
                ExperienceReward = new ContentUnitExperienceRewardDefinition
                {
                    experienceAmount = 20,
                    experienceType = "Crystals",
                    bonusMultiplier = 1.5f
                }
            };

            RuntimeUnitMechanicsDirector.RegisterProfile("test:commander-unit", profile);
            string summary = RuntimeUnitMechanicsDirector.FormatMechanicsSummary("test:commander-unit");
            if (string.IsNullOrWhiteSpace(summary) ||
                !summary.Contains("Sinergias: 1", StringComparison.Ordinal) ||
                !summary.Contains("Sigilo: 4.5s", StringComparison.Ordinal) ||
                !summary.Contains("Mando: Attack (4m)", StringComparison.Ordinal) ||
                !summary.Contains("Recompensa: +20", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unexpected commander mechanics summary: " + summary);
            }

            var commander = new EntityController
            {
                entityId = "test:commander-unit",
                UniqueEntityId = 301,
                IsControlledByPlayer = true,
                IsUnit = true,
                TimeOfLastDamage = 5f,
                Position = new UnityEngine.Vector3(0f, 0f, 0f)
            };
            var escort = new EntityController
            {
                entityId = "test:escort-ally",
                UniqueEntityId = 302,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new UnityEngine.Vector3(12f, 0f, 0f)
            };
            var enemy = new EntityController
            {
                entityId = "test:target-enemy",
                UniqueEntityId = 401,
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 0f,
                DestroyHasBeenStarted = true,
                Position = new UnityEngine.Vector3(8f, 0f, 0f)
            };

            ExistingControllers.Instance.PlayerUnits().Add(commander);
            ExistingControllers.Instance.PlayerUnits().Add(escort);
            ExistingControllers.Instance.AiUnits().Add(enemy);

            RuntimeUnitMechanicsDirector.AttachIfApplicable(commander);
            var director = commander.gameObject.GetComponent<RuntimeUnitMechanicsDirector>();
            if (director == null)
            {
                throw new InvalidOperationException("AttachIfApplicable did not attach RuntimeUnitMechanicsDirector to commander.");
            }

            UnityEngine.Time.time = 21f;
            director.TickMechanics();

            if (!commander.AppliedStatusEffects.Any(e => e.Effect == StatusEffect.Stealth && e.DurationType == SetStatusEffect.DurationType.Seconds && Math.Abs(e.Duration - 4.5f) < 0.01f))
            {
                throw new InvalidOperationException("Expected out-of-combat commander to activate Stealth status effect.");
            }

            if (!ReferenceEquals(escort.CaptainOfThisEntity, commander))
            {
                throw new InvalidOperationException("Expected Command pulse to bind escort's CaptainOfThisEntity to commander.");
            }
            if (!escort.AppliedSpecificValueChanges.Any(c =>
                    c.Value == EntityController.ChangeableValue.Damage &&
                    Math.Abs(c.Change.ValueToAdd - 0.12f) < 0.001f))
            {
                throw new InvalidOperationException("Expected Attack Command pulse to apply +12% Damage SpecificValueChange to escort.");
            }

            if (!escort.AppliedSpecificValueChanges.Any(c =>
                    c.Value == EntityController.ChangeableValue.ArmorProtection &&
                    Math.Abs(c.Change.ValueToAdd - 0.25f) < 0.001f))
            {
                throw new InvalidOperationException("Expected Synergy pulse to apply +25% ArmorProtection SpecificValueChange to escort.");
            }

            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(enemy, 50f, commander);
            if (!Bank.Deposits.Any(d => d.Account == "Player" && Math.Abs(d.Amount - 30f) < 0.01f))
            {
                throw new InvalidOperationException("Expected ExperienceReward on kill to deposit 30 crystals into Player account.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            Bank.Reset();
            UnityEngine.Time.time = 0f;
        }
    }),
    ("experience reward does not pay for killing a friendly unit", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        Bank.Reset();

        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:player-killer", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:player-killer",
                ExperienceReward = new ContentUnitExperienceRewardDefinition
                {
                    experienceAmount = 12,
                    experienceType = "Crystals",
                    bonusMultiplier = 1.0f
                }
            });

            var attacker = new EntityController
            {
                entityId = "test:player-killer",
                IsControlledByPlayer = true,
                IsUnit = true
            };
            var friendlyTarget = new EntityController
            {
                entityId = "test:friendly-target",
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 0f,
                DestroyHasBeenStarted = true
            };

            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(friendlyTarget, 10f, attacker);

            if (Bank.Deposits.Count != 0)
            {
                throw new InvalidOperationException(
                    "Expected no Player reward for killing a friendly unit, got " + Bank.Deposits.Count + " deposit(s).");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            Bank.Reset();
            UnityEngine.Time.time = 0f;
        }
    }),
    };
}
