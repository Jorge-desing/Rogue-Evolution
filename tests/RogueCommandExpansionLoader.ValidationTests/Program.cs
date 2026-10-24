#nullable enable
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

bool isUnitGraphStackProbe = Environment.GetEnvironmentVariable("RCL_UNIT_GRAPH_STACK_PROBE") == "1";
if (args.Length != 1)
{
    Console.Error.WriteLine("Pass the repository root as the only argument.");
    return 2;
}

string repositoryRoot = Path.GetFullPath(args[0]);
string sampleManifestPath = Path.Combine(repositoryRoot, "content", "packs", "sample-expansion", "content.json");
string nativeSampleManifestPath = Path.Combine(repositoryRoot, "content", "packs", "sample-native-expansion", "content.json");
string reactiveAiSampleManifestPath = Path.Combine(repositoryRoot, "content", "packs", "sample-reactive-ai", "content.json");
if (!File.Exists(sampleManifestPath) || !File.Exists(nativeSampleManifestPath) || !File.Exists(reactiveAiSampleManifestPath))
{
    Console.Error.WriteLine("A required sample manifest was not found: " +
        (!File.Exists(sampleManifestPath) ? sampleManifestPath :
            (!File.Exists(nativeSampleManifestPath) ? nativeSampleManifestPath : reactiveAiSampleManifestPath)));
    return 2;
}

var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
ContentPackDefinition sample = JsonSerializer.Deserialize<ContentPackDefinition>(
    File.ReadAllText(sampleManifestPath), jsonOptions)
    ?? throw new InvalidDataException("The sample content manifest is empty.");
ContentPackDefinition nativeSample = JsonSerializer.Deserialize<ContentPackDefinition>(
    File.ReadAllText(nativeSampleManifestPath), jsonOptions)
    ?? throw new InvalidDataException("The native sample content manifest is empty.");
ContentPackDefinition reactiveAiSample = JsonSerializer.Deserialize<ContentPackDefinition>(
    File.ReadAllText(reactiveAiSampleManifestPath), jsonOptions)
    ?? throw new InvalidDataException("The reactive AI sample is empty.");

if (isUnitGraphStackProbe)
{
    const int deepUnitCount = 4096;
    Action validateDeepGraph = () => WithPackage(sample, (pack, root) =>
    {
        ContentUnitDefinition[] originalUnits = pack.units;
        var expandedUnits = new ContentUnitDefinition[originalUnits.Length + deepUnitCount];
        Array.Copy(originalUnits, expandedUnits, originalUnits.Length);
        string templateJson = JsonSerializer.Serialize(originalUnits[0], jsonOptions);
        for (int i = 0; i < deepUnitCount; i++)
        {
            ContentUnitDefinition deepUnit = JsonSerializer.Deserialize<ContentUnitDefinition>(templateJson, jsonOptions)
                ?? throw new InvalidOperationException("Could not clone the deep graph fixture unit.");
            deepUnit.id = "sample:stack-probe-" + i;
            deepUnit.productionBuilding.id = "sample:stack-probe-factory-" + i;
            deepUnit.upgrades = Array.Empty<ContentUnitUpgradeDefinition>();
            deepUnit.synergies = i < deepUnitCount - 1
                ? new[]
                {
                    new ContentUnitSynergyDefinition
                    {
                        id = "sample:stack-probe-link-" + i,
                        statName = "damage", multiplier = 1.0f, radius = 1.0f,
                        targetUnitIds = new[] { "sample:stack-probe-" + (i + 1) }
                    }
                }
                : Array.Empty<ContentUnitSynergyDefinition>();
            expandedUnits[originalUnits.Length + i] = deepUnit;
        }
        pack.units = expandedUnits;

        ContentPackValidationResult result = ContentPackValidator.Validate(
            pack, root, new EmptySnapshot(), new KnownModuleValidator());
        if (!result.Diagnostics.Any(d => d.Code == "unit-synergy.depth-exceeded"))
        {
            throw new InvalidOperationException("Deep graph did not preserve the maximum-depth diagnostic.");
        }
    });
    var probeThread = new Thread(new ThreadStart(validateDeepGraph), 128 * 1024);
    probeThread.Start();
    probeThread.Join();
    Console.WriteLine("UNIT-GRAPH-STACK-PROBE-PASS");
    return 0;
}

var tests = new (string Name, Action Run)[]
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
    ("unit type validation rejects unsupported types", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].unitType = "InvalidType";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.type-invalid");
        });
    }),
    ("unit schema exposes every supported sub-definition DTO field", () =>
    {
        using JsonDocument schemaDocument = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repositoryRoot, "content", "schema", "content.schema.json")));
        JsonElement definitions = schemaDocument.RootElement.GetProperty("$defs");
        JsonElement unitProperties = definitions.GetProperty("unit").GetProperty("properties");
        var subDefinitions = new (string PropertyName, string SchemaName, Type DtoType, bool IsArray)[]
        {
            ("upgrades", "unitUpgrade", typeof(ContentUnitUpgradeDefinition), true),
            ("experience", "unitExperience", typeof(ContentUnitExperienceDefinition), false),
            ("synergies", "unitSynergy", typeof(ContentUnitSynergyDefinition), true),
            ("visualVariants", "unitVisualVariant", typeof(ContentUnitVisualVariantDefinition), true),
            ("spawn", "unitSpawn", typeof(ContentUnitSpawnDefinition), false),
            ("statusEffects", "unitStatusEffect", typeof(ContentUnitStatusEffectDefinition), true),
            ("productionQueue", "unitProductionQueue", typeof(ContentUnitProductionQueueDefinition), false),
            ("resourceCosts", "unitResourceCost", typeof(ContentUnitResourceCostDefinition), true),
            ("movement", "unitMovement", typeof(ContentUnitMovementDefinition), false),
            ("detection", "unitDetection", typeof(ContentUnitDetectionDefinition), false),
            ("weapons", "unitWeapon", typeof(ContentUnitWeaponDefinition), true),
            ("armor", "unitArmor", typeof(ContentUnitArmorDefinition), false),
            ("healing", "unitHealing", typeof(ContentUnitHealingDefinition), false),
            ("stealth", "unitStealth", typeof(ContentUnitStealthDefinition), false),
            ("command", "unitCommand", typeof(ContentUnitCommandDefinition), false),
            ("experienceReward", "unitExperienceReward", typeof(ContentUnitExperienceRewardDefinition), false),
            ("aura", "unitAura", typeof(ContentUnitAuraDefinition), false),
            ("resourceGathering", "unitResourceGathering", typeof(ContentUnitResourceGatheringDefinition), false),
            ("aiBehavior", "unitAiBehavior", typeof(ContentUnitAiBehaviorDefinition), false)
        };

        foreach (var entry in subDefinitions)
        {
            if (!unitProperties.TryGetProperty(entry.PropertyName, out JsonElement property))
            {
                throw new InvalidOperationException("Unit schema is missing property " + entry.PropertyName + ".");
            }

            JsonElement referencedSchema = entry.IsArray ? property.GetProperty("items") : property;
            string expectedReference = "#/$defs/" + entry.SchemaName;
            if (referencedSchema.GetProperty("$ref").GetString() != expectedReference)
            {
                throw new InvalidOperationException("Unit schema property " + entry.PropertyName +
                    " must reference " + expectedReference + ".");
            }

            HashSet<string> dtoFields = entry.DtoType.GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
            HashSet<string> schemaFields = definitions.GetProperty(entry.SchemaName).GetProperty("properties")
                .EnumerateObject().Select(propertyDefinition => propertyDefinition.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (!dtoFields.SetEquals(schemaFields))
            {
                throw new InvalidOperationException("Unit schema definition " + entry.SchemaName +
                    " does not match its DTO fields.");
            }
        }
    }),
    ("customBalance schema matches per-unit row fields and storage types", () =>
    {
        using JsonDocument schemaDocument = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repositoryRoot, "content", "schema", "content.schema.json")));
        JsonElement customBalance = schemaDocument.RootElement.GetProperty("$defs").GetProperty("unit")
            .GetProperty("properties").GetProperty("customBalance");
        if (!customBalance.TryGetProperty("properties", out JsonElement schemaParameters))
        {
            throw new InvalidOperationException("customBalance schema must enumerate supported per-unit balance parameters.");
        }

        Dictionary<string, BalanceParameterDefinition> balanceParameters =
            UnitBalanceOverrideMapper.CreateBalanceParameterMap();
        HashSet<string> schemaNames = schemaParameters.EnumerateObject()
            .Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);
        if (!schemaNames.SetEquals(balanceParameters.Keys) ||
            !customBalance.TryGetProperty("additionalProperties", out JsonElement additionalProperties) ||
            additionalProperties.ValueKind != JsonValueKind.False)
        {
            throw new InvalidOperationException("customBalance schema permits keys outside the per-unit row contract.");
        }

        foreach (KeyValuePair<string, BalanceParameterDefinition> parameter in balanceParameters)
        {
            string expectedType = parameter.Value.StorageType == BalanceParameterType.Int ? "integer" : "number";
            JsonElement parameterSchema = schemaParameters.GetProperty(parameter.Key);
            if (parameterSchema.GetProperty("type").GetString() != expectedType ||
                parameterSchema.GetProperty("minimum").GetDouble() != 0d)
            {
                throw new InvalidOperationException("customBalance schema has an invalid type or minimum for " +
                    parameter.Key + ".");
            }

            if (parameter.Value.StorageType == BalanceParameterType.Int &&
                (!parameterSchema.TryGetProperty("maximum", out JsonElement maximum) ||
                    maximum.GetInt64() != UnitBalanceOverrideMapper.MaximumContiguousSingleExactInteger))
            {
                throw new InvalidOperationException("customBalance schema must use the exact native Single integer bound for " +
                    parameter.Key + ".");
            }

            if (parameter.Value.StorageType == BalanceParameterType.Float &&
                (!parameterSchema.TryGetProperty("maximum", out JsonElement floatMaximum) ||
                    floatMaximum.GetDouble() != (double)Single.MaxValue))
            {
                throw new InvalidOperationException("customBalance schema must use the native Single bound for " +
                    parameter.Key + ".");
            }
        }
    }),
    ("balanceOverrides schema caps integer values at the native Single boundary", () =>
    {
        using JsonDocument schemaDocument = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repositoryRoot, "content", "schema", "content.schema.json")));
        JsonElement balanceOverride = schemaDocument.RootElement.GetProperty("$defs")
            .GetProperty("balanceOverride");
        JsonElement integerRule = balanceOverride.GetProperty("allOf").EnumerateArray()
            .First(rule =>
            {
                JsonElement conditionName = rule.GetProperty("if").GetProperty("properties")
                    .GetProperty("name");
                return conditionName.TryGetProperty("enum", out JsonElement names) &&
                    names.EnumerateArray().Any(name => name.GetString() == "maxHealth");
            });
        string pattern = integerRule.GetProperty("then").GetProperty("properties")
            .GetProperty("value").GetProperty("pattern").GetString()!;

        if (!Regex.IsMatch("0", pattern) || !Regex.IsMatch("16777216", pattern) ||
            Regex.IsMatch("16777217", pattern))
        {
            throw new InvalidOperationException(
                "Integer balanceOverrides must accept the 2^24 boundary and reject larger values in the schema.");
        }
    }),
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
            "FallbackLandscapeGeneratorsPostfix",
            "OutlyingStartAiBuildingDistancePrefix",
            "BlueprintPoolIsInPoolPrefix",
            "BlueprintPoolFindPrefix",
            "BlueprintPoolExclusiveBlueprintsPostfix",
            "PresentCardsToChoosePrefix",
            "PresentCardsToChooseFinalizer",
            "BlueprintPoolBlueprintsInPoolPostfix",
            "PresentCardAsRewardRecordPostfix",
            "AllEntityIdsAllowedAsBlueprintsPostfix"
        ];

        foreach (string hookName in hookNames)
        {
            if (typeof(RuntimeHarmonyPatches).GetMethod(hookName,
                    BindingFlags.Public | BindingFlags.Static) == null)
            {
                throw new InvalidOperationException($"RuntimePatchInstaller expects missing hook '{hookName}'.");
            }
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
    ("runtime mutation modifies buff and ai behaviour", () =>
    {
        var mutationManager = new RuntimeMutationManager(msg => Console.WriteLine(msg));
        
        // Test Buff Mutation
        var buff = new EntityValueModifier { value = 1.0f };
        mutationManager.MutateFloat(buff, "value", 2.5f);
        if (buff.value != 2.5f) throw new InvalidOperationException("Buff value mutation failed.");

        // Test AI Behaviour Mutation
        var behaviour = new AiBehaviour { tickRate = 1.0f, detectionRange = 10.0f };
        var manager = new AiBehaviourManager { activeBehaviour = behaviour };
        mutationManager.MutateActiveAiBehaviour(manager, "tickRate", 0.5f);
        if (behaviour.tickRate != 0.5f) throw new InvalidOperationException("AI tickRate mutation failed.");
        
        mutationManager.MutateActiveAiBehaviour(manager, "detectionRange", 20.0f);
        if (behaviour.detectionRange != 20.0f) throw new InvalidOperationException("AI detectionRange mutation failed.");
    }),
    ("unit balance map targets only row-backed native parameters", () =>
    {
        FieldInfo? field = typeof(RuntimeContentMutator).GetField("ParamMap",
            BindingFlags.NonPublic | BindingFlags.Static);
        IDictionary? map = field?.GetValue(null) as IDictionary;
        if (map == null || map.Count != 25 || map.Contains("ManaRechargePerSecond"))
        {
            throw new InvalidOperationException("The runtime map must exclude global-only values and contain 25 per-unit parameters.");
        }
        var expectedRowFields = new Dictionary<string, (string RowField, BalanceParameterType StorageType)>(StringComparer.Ordinal)
        {
            ["Cost"] = ("cost", BalanceParameterType.Int),
            ["ProductionDuration"] = ("productionDuration", BalanceParameterType.Float),
            ["MaxCapacity"] = ("maxCapacity", BalanceParameterType.Int),
            ["MaxHealth"] = ("maxHealth", BalanceParameterType.Int),
            ["MaxShield"] = ("maxShield", BalanceParameterType.Int),
            ["MaxMana"] = ("maxMana", BalanceParameterType.Int),
            ["MaxRank"] = ("maxRank", BalanceParameterType.Int),
            ["MoveSpeed"] = ("moveSpeed", BalanceParameterType.Float),
            ["SightRadius"] = ("sightRadius", BalanceParameterType.Int),
            ["Damage1"] = ("damage1", BalanceParameterType.Float),
            ["Damage2"] = ("damage2", BalanceParameterType.Float),
            ["WeaponRange"] = ("weaponRange", BalanceParameterType.Float),
            ["Attack1Cooldown"] = ("attackCooldown", BalanceParameterType.Float),
            ["Attack2Cooldown"] = ("attack2Cooldown", BalanceParameterType.Float),
            ["EffectRadius1"] = ("effectRadius1", BalanceParameterType.Float),
            ["EffectRadius2"] = ("effectRadius2", BalanceParameterType.Float),
            ["SkillRange"] = ("skillRange", BalanceParameterType.Int),
            ["HealAmount1"] = ("healAmount1", BalanceParameterType.Float),
            ["HealAmount2"] = ("healAmount2", BalanceParameterType.Float),
            ["GainCreditsAmount"] = ("gainCreditsAmount", BalanceParameterType.Float),
            ["SkillManaCost"] = ("skillManaCost", BalanceParameterType.Float),
            ["Duration1"] = ("duration1", BalanceParameterType.Float),
            ["SpawnTtl"] = ("duration2", BalanceParameterType.Float),
            ["MaxArmor"] = ("maxArmor", BalanceParameterType.Float),
            ["ArmorProtection"] = ("armorProtection", BalanceParameterType.Float)
        };
        if (map.Count != expectedRowFields.Count)
        {
            throw new InvalidOperationException("Runtime balance map contains an unexpected number of row-backed members.");
        }
        foreach (KeyValuePair<string, (string RowField, BalanceParameterType StorageType)> expected in expectedRowFields)
        {
            if (!map.Contains(expected.Key))
            {
                throw new InvalidOperationException("The runtime map is missing native ChangeableValue " + expected.Key + ".");
            }

            if (!(map[expected.Key] is BalanceParameterDefinition definition))
            {
                throw new InvalidOperationException("Runtime balance map entry " + expected.Key + " has an unexpected definition type.");
            }

            if (definition.RowFieldName != expected.Value.RowField ||
                definition.StorageType != expected.Value.StorageType)
            {
                throw new InvalidOperationException("Native ChangeableValue " + expected.Key +
                    " must map to unit-row field " + expected.Value.RowField + " as " +
                    expected.Value.StorageType + ".");
            }
        }
    }),
    ("unit loadout variants deserialize AP and HE declarations", () =>
    {
        ContentUnitDefinition parsed = JsonSerializer.Deserialize<ContentUnitDefinition>(
            "{\"variants\":[{\"id\":\"ap\",\"loadout\":\"AP\",\"balanceOverrides\":[{\"name\":\"damage1\",\"value\":\"12\"}]},{\"id\":\"he\",\"loadout\":\"HE\"}]}",
            jsonOptions) ?? throw new InvalidOperationException("Variant fixture did not deserialize.");
        FieldInfo? variantsField = typeof(ContentUnitDefinition).GetField("variants");
        Array? variants = variantsField?.GetValue(parsed) as Array;
        if (variantsField == null || variants == null || variants.Length != 2)
        {
            throw new InvalidOperationException("The unit DTO did not deserialize two loadout variants.");
        }
        Type variantType = variants.GetType().GetElementType()!;
        FieldInfo? id = variantType.GetField("id");
        FieldInfo? loadout = variantType.GetField("loadout");
        FieldInfo? overrides = variantType.GetField("balanceOverrides");
        if (!variantType.IsDefined(typeof(SerializableAttribute), inherit: false) ||
            id?.GetValue(variants.GetValue(0)) as string != "ap" ||
            loadout?.GetValue(variants.GetValue(0)) as string != "AP" ||
            loadout.GetValue(variants.GetValue(1)) as string != "HE" ||
            overrides?.GetValue(variants.GetValue(0)) is not ContentBalanceOverride[] rows ||
            rows.Length != 1 || rows[0].name != "damage1" || rows[0].value != "12")
        {
            throw new InvalidOperationException("Variant JSON lost AP/HE labels, local IDs, or raw damage1 overrides.");
        }
    }),
    ("unit loadout variants accept AP HE and an omitted loadout", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            SetUnitLoadoutVariants(pack.units[0],
                """[{"id":"ap","loadout":"AP","balanceOverrides":[{"name":"damage1","value":"12"}]},{"id":"he","loadout":"HE"},{"id":"default"}]""");
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("unit loadout variants accept eight local IDs including the 64-character boundary", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            string longest = "a" + new string('z', 63);
            string variantsJson = "[" + string.Join(",", new[]
            {
                longest, "a_0", "b-1", "c.2", "d3", "e4", "f5", "g6"
            }.Select(id => "{\"id\":\"" + id + "\"}")) + "]";
            SetUnitLoadoutVariants(pack.units[0], variantsJson);
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("unit loadout variant IDs are local to each parent unit", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            SetUnitLoadoutVariants(pack.units[0], """[{"id":"ap"}]""");
            SetUnitLoadoutVariants(pack.units[1], """[{"id":"ap"}]""");
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("unit loadout variants deep clone nested mutable override values", () =>
    {
        ContentPackDefinition source = ContentPackCopy.DeepClone(nativeSample);
        SetUnitLoadoutVariants(source.units[0],
            """[{"id":"ap","loadout":"AP","balanceOverrides":[{"name":"damage1","value":"12"}]}]""");
        ContentPackDefinition copy = ContentPackCopy.DeepClone(source);
        FieldInfo field = typeof(ContentUnitDefinition).GetField("variants")
            ?? throw new InvalidOperationException("The unit variants field is missing.");
        Array originals = (Array)(field.GetValue(source.units[0])
            ?? throw new InvalidOperationException("Source variants were dropped."));
        Array clones = (Array)(field.GetValue(copy.units[0])
            ?? throw new InvalidOperationException("Cloned variants were dropped."));
        object original = originals.GetValue(0)!;
        object cloned = clones.GetValue(0)!;
        FieldInfo id = cloned.GetType().GetField("id")!;
        FieldInfo loadout = cloned.GetType().GetField("loadout")!;
        FieldInfo overrides = cloned.GetType().GetField("balanceOverrides")!;
        ContentBalanceOverride[] sourceOverrides = (ContentBalanceOverride[])overrides.GetValue(original)!;
        ContentBalanceOverride[] clonedOverrides = (ContentBalanceOverride[])overrides.GetValue(cloned)!;
        if (ReferenceEquals(originals, clones) || ReferenceEquals(original, cloned) ||
            ReferenceEquals(sourceOverrides, clonedOverrides) ||
            ReferenceEquals(sourceOverrides[0], clonedOverrides[0]))
        {
            throw new InvalidOperationException("Variant DTOs or their override entries share mutable state.");
        }
        id.SetValue(cloned, "changed");
        loadout.SetValue(cloned, "HE");
        clonedOverrides[0].name = "damage2";
        clonedOverrides[0].value = "99";
        clones.SetValue(null, 0);
        if (id.GetValue(original) as string != "ap" || loadout.GetValue(original) as string != "AP" ||
            sourceOverrides[0].name != "damage1" || sourceOverrides[0].value != "12" ||
            originals.GetValue(0) == null)
        {
            throw new InvalidOperationException("Mutating a variant clone modified its source.");
        }
    }),
    ("unit loadout variants reject null entries", () =>
    {
        ExpectUnitLoadoutVariantDiagnostic(nativeSample, "[null]", "entry.null", "units[0].variants[0]");
    }),
    ("unit loadout variants reject more than eight entries", () =>
    {
        string json = "[" + string.Join(",", Enumerable.Range(0, 9)
            .Select(i => "{\"id\":\"v" + i + "\"}")) + "]";
        ExpectUnitLoadoutVariantDiagnostic(nativeSample, json, "unit.variants-limit", "units[0].variants");
    }),
    ("unit loadout variants reject null IDs", () =>
    {
        ExpectUnitLoadoutVariantDiagnostic(nativeSample, """[{"id":null}]""",
            "unit.variant-id-invalid", "units[0].variants[0].id");
    }),
    ("unit loadout variants reject blank IDs", () =>
    {
        foreach (string json in new[] { """[{"id":""}]""", """[{"id":"  "}]""" })
        {
            ExpectUnitLoadoutVariantDiagnostic(nativeSample, json,
                "unit.variant-id-invalid", "units[0].variants[0].id");
        }
    }),
    ("unit loadout variants reject nonlocal or non-ASCII IDs", () =>
    {
        foreach (string id in new[] { "AP", "0ap", "unit:ap", "áp", "a b", "a/../b", "a" + new string('x', 64) })
        {
            ExpectUnitLoadoutVariantDiagnostic(nativeSample,
                "[{\"id\":" + JsonSerializer.Serialize(id) + "}]",
                "unit.variant-id-invalid", "units[0].variants[0].id");
        }
    }),
    ("unit loadout variants reject duplicate IDs in the same unit", () =>
    {
        ExpectUnitLoadoutVariantDiagnostic(nativeSample, """[{"id":"ap"},{"id":"ap"}]""",
            "unit.variant-id-invalid", "units[0].variants[1].id");
    }),
    ("unit loadout variants reject invalid present loadouts", () =>
    {
        foreach (string loadout in new[] { "", " ", "ap", "HE ", "INC" })
        {
            ExpectUnitLoadoutVariantDiagnostic(nativeSample,
                "[{\"id\":\"ap\",\"loadout\":" + JsonSerializer.Serialize(loadout) + "}]",
                "unit.variant-loadout-invalid", "units[0].variants[0].loadout");
        }
    }),
    ("unit loadout variants reject invalid raw damage overrides", () =>
    {
        foreach (string json in new[]
        {
            """[{"id":"ap","balanceOverrides":[{"name":"noSuchRow","value":"1"}]}]""",
            """[{"id":"ap","balanceOverrides":[{"name":"damage1","value":"-1"}]}]"""
        })
        {
            ExpectUnitLoadoutVariantDiagnostic(nativeSample, json,
                "unit.variant.balance-override-invalid", "units[0].variants[0].balanceOverrides[0]");
        }
    }),
    ("unit loadout variants reject duplicate raw damage1 overrides", () =>
    {
        ExpectUnitLoadoutVariantDiagnostic(nativeSample,
            """[{"id":"ap","balanceOverrides":[{"name":"damage1","value":"12"},{"name":"damage1","value":"15"}]}]""",
            "unit.variant.balance-override-name-invalid", "units[0].variants[0].balanceOverrides[1].name");
    }),
    ("unit loadout variants reject null raw override entries", () =>
    {
        ExpectUnitLoadoutVariantDiagnostic(nativeSample,
            """[{"id":"ap","balanceOverrides":[null]}]""",
            "entry.null", "units[0].variants[0].balanceOverrides[0]");
    }),
    ("unit loadout variants do not change base registration or mapping", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            ContentUnitDefinition unit = ContentPackCopy.DeepClone(nativeSample).units[0];
            unit.customBalance = new Dictionary<string, double>(StringComparer.Ordinal) { ["MoveSpeed"] = 14.5d };
            unit.balanceOverrides = [new ContentBalanceOverride { name = "maxHealth", value = "5" }];
            Dictionary<string, double> original = UnitBalanceOverrideMapper.CreateCustomBalance(unit);
            SetUnitLoadoutVariants(unit,
                """[{"id":"ap","loadout":"AP","balanceOverrides":[{"name":"maxHealth","value":"99"}]},{"id":"he","loadout":"HE","balanceOverrides":[{"name":"moveSpeed","value":"200"}]}]""");
            Dictionary<string, double> mapped = UnitBalanceOverrideMapper.CreateCustomBalance(unit);
            List<BalanceOverride> mappedRows = UnitBalanceOverrideMapper.CreateBalanceOverridesForLoadedUnits(
                [unit], new HashSet<string>([unit.id], StringComparer.Ordinal));
            if (unit.id != nativeSample.units[0].id || unit.productionBuilding.id != nativeSample.units[0].productionBuilding.id ||
                unit.balanceOverrides.Length != 1 || unit.balanceOverrides[0].name != "maxHealth" ||
                unit.balanceOverrides[0].value != "5" ||
                mapped.Count != original.Count || !mapped.TryGetValue("MoveSpeed", out double speed) ||
                speed != original["MoveSpeed"] || mappedRows.Count != 1 ||
                mappedRows[0].EntityId != unit.id || mappedRows[0].Parameters.Count != original.Count ||
                mappedRows[0].Parameters["MoveSpeed"] != original["MoveSpeed"])
            {
                throw new InvalidOperationException("Variant overrides leaked into the base unit mapping or identity.");
            }

            EntityBalancingParameters nativeProduct = RuntimeMutatorTestData.Row("NativeUnit");
            EntityBalancingParameters nativeFactory = RuntimeMutatorTestData.Row("NativeFactory", "NativeUnit");
            EntityBalancingStore.EntityBalancingParametersList.Add(nativeProduct);
            EntityBalancingStore.EntityBalancingParametersList.Add(nativeFactory);
            EntityBalancingStore.ParameterListIndexOf.Add("NativeUnit", 0);
            EntityBalancingStore.ParameterListIndexOf.Add("NativeFactory", 1);
            EntityBalancingStore.ChangeableIntValueCache.Add("NativeUnit", new());
            EntityBalancingStore.ChangeableFloatValueCache.Add("NativeUnit", new());
            EntityBalancingStore.ChangeableIntValueCache.Add("NativeFactory", new());
            EntityBalancingStore.ChangeableFloatValueCache.Add("NativeFactory", new());
            EntityBalancingStore.FactoryEntityIdOf.Add("NativeUnit", "NativeFactory");
            var provider = new RuntimeMutatorTestProvider();
            EntityBalancingParameters productRow = RuntimeMutatorTestData.Row(unit.id);
            productRow.maxHealth = 5;
            provider.EntityRows.Add(productRow);
            provider.EntityRows.Add(RuntimeMutatorTestData.Row(unit.productionBuilding.id, unit.id));
            provider.BalanceOverrides.Add(mappedRows[0]);
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);
            if (!mutator.RegisterEntityRows() ||
                EntityBalancingStore.EntityBalancingParametersList.Count != 4 ||
                EntityBalancingStore.FactoryEntityIdOf.Count != 2 ||
                EntityBalancingStore.FactoryEntityIdOf["NativeUnit"] != "NativeFactory" ||
                EntityBalancingStore.FactoryEntityIdOf[unit.id] != unit.productionBuilding.id ||
                EntityBalancingStore.EntityBalancingParametersList[2].maxHealth != 5 ||
                EntityBalancingStore.EntityBalancingParametersList[2].moveSpeed != 14.5f)
            {
                throw new InvalidOperationException("Declaring variants changed base/native row registration or factory mapping.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("unit loadout variants omission preserves base balance mapping", () =>
    {
        ContentUnitDefinition unit = ContentPackCopy.DeepClone(nativeSample).units[0];
        if (typeof(ContentUnitDefinition).GetField("variants")?.GetValue(unit) != null)
        {
            throw new InvalidOperationException("Omitted variants must stay null on the unit DTO.");
        }
        Dictionary<string, double> baseline = UnitBalanceOverrideMapper.CreateCustomBalance(unit);
        List<BalanceOverride> mappedRows = UnitBalanceOverrideMapper.CreateBalanceOverridesForLoadedUnits(
            [unit], new HashSet<string>([unit.id], StringComparer.Ordinal));
        if (baseline.Count != mappedRows.Count ||
            mappedRows.Any(row => row.EntityId != unit.id || row.Parameters.Count != baseline.Count))
        {
            throw new InvalidOperationException("Omitting variants changed base mapper output.");
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
    ("unit balance mapper preserves custom raw and structured movement precedence", () =>
    {
        var unit = new ContentUnitDefinition
        {
            id = "sample:movement",
            movement = new ContentUnitMovementDefinition { moveSpeed = 12.5f },
            balanceOverrides = [new ContentBalanceOverride { name = "moveSpeed", value = "4.5" }]
        };
        Dictionary<string, double> mapped = UnitBalanceOverrideMapper.CreateCustomBalance(unit);
        if (mapped.ContainsKey("MoveSpeed"))
        {
            throw new InvalidOperationException("A raw moveSpeed row override must suppress the derived movement value.");
        }

        unit.customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["MoveSpeed"] = 14.5d
        };
        mapped = UnitBalanceOverrideMapper.CreateCustomBalance(unit);
        if (!mapped.TryGetValue("MoveSpeed", out double customSpeed) || customSpeed != 14.5d)
        {
            throw new InvalidOperationException("Explicit customBalance.MoveSpeed must remain available to override the raw row value.");
        }
    }),
    ("balance overrides exclude units without registered entity rows", () =>
    {
        var registeredUnit = new ContentUnitDefinition
        {
            id = "sample:registered",
            customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["MoveSpeed"] = 14.5d
            }
        };
        var rejectedUnit = new ContentUnitDefinition
        {
            id = "sample:rejected",
            customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["MaxHealth"] = 16777217d
            }
        };
        List<BalanceOverride> overrides = UnitBalanceOverrideMapper.CreateBalanceOverridesForLoadedUnits(
            new[] { registeredUnit, rejectedUnit },
            new HashSet<string>(new[] { registeredUnit.id }, StringComparer.Ordinal));
        if (overrides.Count != 1 || overrides[0].EntityId != registeredUnit.id ||
            !overrides[0].Parameters.TryGetValue("MoveSpeed", out double moveSpeed) || moveSpeed != 14.5d ||
            overrides.Any(balanceOverride => balanceOverride.EntityId == rejectedUnit.id))
        {
            throw new InvalidOperationException("Only balance overrides for units with registered entity rows may be returned.");
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
    ("unit movement speed mapper ignores non-finite positive values", () =>
    {
        var unit = new ContentUnitDefinition
        {
            id = "sample:movement",
            movement = new ContentUnitMovementDefinition { moveSpeed = Single.PositiveInfinity }
        };
        Dictionary<string, double> mapped = UnitBalanceOverrideMapper.CreateCustomBalance(unit);
        if (mapped.ContainsKey("MoveSpeed"))
        {
            throw new InvalidOperationException("Non-finite movement speed must not enter the native balance row mapping.");
        }
    }),
    ("unit movement speed rejects non-finite values before row mapping", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].movement = new ContentUnitMovementDefinition
            {
                moveSpeed = Single.PositiveInfinity
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-movement.speed-invalid");
        });
    }),
    ("unit armor validation rejects non-finite armor values", () =>
    {
        foreach (float value in new[] { Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity })
        {
            WithPackage(sample, (pack, root) =>
            {
                pack.units[0].armor = new ContentUnitArmorDefinition { armorValue = value };
                ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                    "unit-armor.value-invalid");
            });
        }
    }),
    ("unit armor validation rejects non-finite shield values", () =>
    {
        foreach (float value in new[] { Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity })
        {
            WithPackage(sample, (pack, root) =>
            {
                pack.units[0].armor = new ContentUnitArmorDefinition { shieldValue = value };
                ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                    "unit-armor.shield-invalid");
            });
        }
    }),
    ("unit armor validation rejects non-finite damage reduction", () =>
    {
        foreach (float value in new[] { Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity })
        {
            WithPackage(sample, (pack, root) =>
            {
                pack.units[0].armor = new ContentUnitArmorDefinition { damageReduction = value };
                ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                    "unit-armor.reduction-invalid");
            });
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
    ("ability validation rejects null entries and non-finite values", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].abilities = [null!];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "entry.null");
        });
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].abilities = [new ContentAbilityDefinition { id = "test", value = Single.NaN }];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "ability.value-invalid");
        });
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].abilities = [new ContentAbilityDefinition { id = "test", value = Single.PositiveInfinity }];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "ability.value-invalid");
        });
    }),
    ("AI override validation rejects null entries empty parameters and non-finite values", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].aiOverrides = [null!];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "entry.null");
        });
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].aiOverrides = [new ContentAiOverrideDefinition { parameter = "", value = 1.0f }];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "ai-override.parameter-required");
        });
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].aiOverrides = [new ContentAiOverrideDefinition { parameter = "tickRate", value = Single.NaN }];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "ai-override.value-invalid");
        });
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].aiOverrides = [new ContentAiOverrideDefinition { parameter = "tickRate", value = Single.PositiveInfinity }];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "ai-override.value-invalid");
        });
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
        RuntimeHooks.Bind(null!, provider, null!);
        try
        {
            string key = "RCL_POOL_TEST";
            string result = "unchanged";
            if (!RuntimeHarmonyPatches.LandscapeLocalizationPrefix(ref key, ref result) ||
                key != "NativePool" || result != "unchanged")
                throw new InvalidOperationException("The localization prefix did not forward the source key to the game.");
            key = "UnrelatedKey";
            if (!RuntimeHarmonyPatches.LandscapeLocalizationPrefix(ref key, ref result) ||
                key != "UnrelatedKey" || result != "unchanged")
                throw new InvalidOperationException("The localization prefix rewrote an unrelated key.");
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
    ("AI start spacing override applies only to provider-owned appended stages", () =>
    {
        var loadedStage = new object();
        var unrelatedStage = new object();
        var loadedStages = new[] { loadedStage };
        int distance;

        if (StageOwnedAiPlacementDistanceResolver.TryResolve(8, loadedStage, loadedStages,
                stage => stage, 37, out distance))
        {
            throw new InvalidOperationException("A native-band stage bypassed the game's original distance getter.");
        }

        if (StageOwnedAiPlacementDistanceResolver.TryResolve(12, unrelatedStage, loadedStages,
                stage => stage, 37, out distance))
        {
            throw new InvalidOperationException("An unrelated appended stage received the loader's distance override.");
        }

        if (!StageOwnedAiPlacementDistanceResolver.TryResolve(12, loadedStage, loadedStages,
                stage => stage, 37, out distance) || distance != 37)
        {
            throw new InvalidOperationException("A provider-owned appended stage did not use the configured final native band.");
        }
    }),
    ("sample units declare unique localized production buildings", () =>
    {
        foreach (ContentPackDefinition pack in new[] { sample, nativeSample })
        {
            var buildingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContentUnitDefinition unit in pack.units)
            {
                ContentProductionBuildingDefinition building = unit.productionBuilding;
                if (building == null || building.id == unit.id || !buildingIds.Add(building.id) ||
                    String.IsNullOrWhiteSpace(building.nativeTemplateId) ||
                    !building.localizations.Any(text => text.language == "en-US" && !String.IsNullOrWhiteSpace(text.name)) ||
                    !building.localizations.Any(text => text.language == "es-XL" && !String.IsNullOrWhiteSpace(text.name)))
                    throw new InvalidOperationException("A unit is missing its own localized production building.");
            }
        }
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
    ("production building card-only bundle is not reported unused", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentProductionBuildingDefinition building = pack.units[0].productionBuilding;
            building.assetBundleId = pack.assetBundles[0].id;
            building.cardImageAssetName = "assets/factory-card.png";
            building.visualAssetName = null;
            building.visualRootPath = null;
            ContentPackValidationResult result = ContentPackValidator.Validate(
                pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (result.Diagnostics.Any(diagnostic => diagnostic.Code == "production-building.bundle-unused"))
            {
                throw new InvalidOperationException("A bundle referenced only by the factory reward-card Sprite was incorrectly marked unused.");
            }
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
    ("production-building fallback eligibility follows its produced unit", () =>
    {
        var factoryRow = new ProductionBuildingEligibilityTestRow();
        ProductionBuildingEligibility.ApplyToFactoryRow(
            new ContentUnitDefinition
            {
                isAllowedAsStartingBlueprint = true,
                isAllowedForAi = true,
                isAllowedForDemo = false
            },
            factoryRow,
            typeof(ProductionBuildingEligibilityTestRow).GetField(nameof(factoryRow.isAllowedAsStartingBlueprint))!,
            typeof(ProductionBuildingEligibilityTestRow).GetField(nameof(factoryRow.isAllowedForAi))!,
            typeof(ProductionBuildingEligibilityTestRow).GetField(nameof(factoryRow.isAllowedForDemo))!);

        if (!factoryRow.isAllowedAsStartingBlueprint || factoryRow.isAllowedForDemo ||
            factoryRow.isAllowedAsBlueprint || !factoryRow.isAllowedForAi)
        {
            throw new InvalidOperationException(
                "A production building must inherit the product's AI, start-deck, and demo eligibility without becoming a regular blueprint by itself.");
        }

        factoryRow = new ProductionBuildingEligibilityTestRow();
        ProductionBuildingEligibility.ApplyToFactoryRow(
            new ContentUnitDefinition
            {
                isAllowedAsStartingBlueprint = false,
                isAllowedForAi = false,
                isAllowedForDemo = true
            },
            factoryRow,
            typeof(ProductionBuildingEligibilityTestRow).GetField(nameof(factoryRow.isAllowedAsStartingBlueprint))!,
            typeof(ProductionBuildingEligibilityTestRow).GetField(nameof(factoryRow.isAllowedForAi))!,
            typeof(ProductionBuildingEligibilityTestRow).GetField(nameof(factoryRow.isAllowedForDemo))!);
        if (factoryRow.isAllowedAsStartingBlueprint || factoryRow.isAllowedForAi || !factoryRow.isAllowedForDemo)
            throw new InvalidOperationException("Factory AI, demo, and starting-deck permissions were not copied independently.");
    }),
    ("unit production buildings are mandatory and cannot share entity IDs", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].productionBuilding = null!;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "production-building.required");
        });
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].productionBuilding.id = pack.units[0].id;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "id.duplicate-in-package");
        });
        WithPackage(nativeSample, (pack, root) =>
        {
            pack.units[1].productionBuilding.id = pack.units[0].productionBuilding.id;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "production-building.id-shared");
        });
        WithPackage(sample, (pack, root) =>
            ExpectDiagnostic(pack, root, new EmptySnapshot(pack.units[0].productionBuilding.id),
                new KnownModuleValidator(), "id.collision"));
    }),
    ("production buildings validate localization and bounded balance overrides", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].productionBuilding.localizations = [];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "production-building.localization-required");
        });
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].productionBuilding.balanceOverrides =
                [new ContentBalanceOverride { name = "neededExperienceLevel", value = "10001" }];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "production-building.balance-override-invalid");
        });
    }),
    ("unit and production building require matching profile unlock level and rarity", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            pack.units[0].balanceOverrides.Single(entry => entry.name == "neededExperienceLevel").value = "1";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.profile-unlock-mismatch");
        });
        WithPackage(nativeSample, (pack, root) =>
        {
            pack.units[0].productionBuilding.balanceOverrides
                .Single(entry => entry.name == "rarity").value = "Rare";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.card-rarity-mismatch");
        });
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].balanceOverrides = [];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.profile-unlock-required");
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.card-rarity-required");
        });
    }),
    ("production building deep clones preserve independent balance and localization", () =>
    {
        ContentPackDefinition source = ContentPackCopy.DeepClone(nativeSample);
        ContentProductionBuildingDefinition sourceBuilding = source.units[0].productionBuilding;
        sourceBuilding.cardImageAssetName = "FactoryCard";
        sourceBuilding.assetBundleId = "sample-native:assets";
        sourceBuilding.visualAssetName = "FactoryVisual";
        sourceBuilding.visualRootPath = "FactoryModel";
        ContentPackDefinition copy = ContentPackCopy.DeepClone(source);
        if (copy.units[0].productionBuilding.assetBundleId != "sample-native:assets" ||
            copy.units[0].productionBuilding.visualAssetName != "FactoryVisual" ||
            copy.units[0].productionBuilding.visualRootPath != "FactoryModel" ||
            copy.units[0].productionBuilding.cardImageAssetName != "FactoryCard")
        {
            throw new InvalidOperationException("Deep cloning dropped production-building visual overlay fields.");
        }

        copy.units[0].productionBuilding.id = "test:changed-factory";
        copy.units[0].productionBuilding.assetBundleId = "test:changed-assets";
        copy.units[0].productionBuilding.visualAssetName = "ChangedVisual";
        copy.units[0].productionBuilding.visualRootPath = "ChangedRoot";
        copy.units[0].productionBuilding.cardImageAssetName = "ChangedCard";
        copy.units[0].productionBuilding.balanceOverrides[0].value = "9999";
        copy.units[0].productionBuilding.localizations[0].name = "Changed";
        copy.units.Single(unit => unit.id == "sample-native:skirmish-bike")
            .localizations[0].skillDescription = "Changed skill text";
        if (sourceBuilding.id == "test:changed-factory" ||
            sourceBuilding.assetBundleId != "sample-native:assets" ||
            sourceBuilding.visualAssetName != "FactoryVisual" ||
            sourceBuilding.visualRootPath != "FactoryModel" ||
            sourceBuilding.cardImageAssetName != "FactoryCard" ||
            sourceBuilding.balanceOverrides[0].value == "9999" ||
            sourceBuilding.localizations[0].name == "Changed" ||
            source.units.Single(unit => unit.id == "sample-native:skirmish-bike")
                .localizations[0].skillDescription == "Changed skill text")
            throw new InvalidOperationException("Production-building overlay data or nested arrays shared mutable state with the source pack.");
    }),

    ("production building visual overlay accepts an explicit package bundle", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentProductionBuildingDefinition building = pack.units[0].productionBuilding;
            building.assetBundleId = pack.assetBundles[0].id;
            building.visualAssetName = "FactoryVisual";
            building.visualRootPath = "FactoryModel";
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("production building visual overlay requires paired asset and root fields", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentProductionBuildingDefinition building = pack.units[0].productionBuilding;
            building.assetBundleId = pack.assetBundles[0].id;
            building.visualAssetName = "FactoryVisual";
            building.visualRootPath = null;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "production-building.visual-source-invalid");
        });
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentProductionBuildingDefinition building = pack.units[0].productionBuilding;
            building.assetBundleId = pack.assetBundles[0].id;
            building.visualAssetName = null;
            building.visualRootPath = "FactoryModel";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "production-building.visual-source-invalid");
        });
    }),
    ("production building visual bundle and root path are validated", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentProductionBuildingDefinition building = pack.units[0].productionBuilding;
            building.assetBundleId = "sample-native:missing-assets";
            building.visualAssetName = "FactoryVisual";
            building.visualRootPath = "FactoryModel";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "reference.bundle-unresolved");
        });
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentProductionBuildingDefinition building = pack.units[0].productionBuilding;
            building.assetBundleId = pack.assetBundles[0].id;
            building.visualAssetName = "FactoryVisual";
            building.visualRootPath = "FactoryModel/../FactoryRoot";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "production-building.visual-root-path-invalid");
        });
    }),
    ("relic clone and localization preserve independent player card data", () =>
    {
        ContentRelicDefinition sourceRelic = nativeSample.relics.Single(
            relic => relic.id == "sample-native:foundry-counterweight");
        if (!RelicLocalizationResolver.TryResolve(sourceRelic, "es-XL", false, out string spanishName) ||
            spanishName != "Contrapeso de fundición" ||
            !RelicLocalizationResolver.TryResolve(sourceRelic, "fr-FR", true, out string fallbackDescription) ||
            fallbackDescription != "Atlas Crane Mechs gain 10% Health for the rest of the run.")
        {
            throw new InvalidOperationException("The relic localization or en-US fallback did not resolve.");
        }

        ContentPackDefinition copy = ContentPackCopy.DeepClone(nativeSample);
        ContentRelicDefinition clonedRelic = copy.relics.Single(
            relic => relic.id == "sample-native:foundry-counterweight");
        clonedRelic.localizations[0].name = "Changed";
        clonedRelic.cardChanges[0].onlyForTheseEntityIds[0] = "test:other-unit";
        clonedRelic.cardChanges[0].value = 9.0f;
        if (sourceRelic.localizations[0].name == "Changed" ||
            sourceRelic.cardChanges[0].onlyForTheseEntityIds[0] != "sample-native:atlas-crane-mech" ||
            Math.Abs(sourceRelic.cardChanges[0].value - 1.1f) > 0.0001f)
        {
            throw new InvalidOperationException("Relic localization, targets, or card changes shared mutable data.");
        }
    }),
    ("sample manifest passes package-local validation", () =>
    {
        WithPackage(sample, (pack, root) =>
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator()));
    }),
    ("authored native sample pack passes package-local validation", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator()));
    }),
    ("Ironclad declares its rigged native visual overlay and custom card", () =>
    {
        ContentUnitDefinition ironclad = nativeSample.units.Single(
            unit => unit.id == "sample-native:assault-walker");
        if (ironclad.visualAssetName != "assets/roguecommandexpansionsample/ironcladwalkervisual.prefab" ||
            ironclad.visualRootPath != "RoboMarine_Mesh" ||
            ironclad.cardImageAssetName != "assets/roguecommandexpansionsample/ironcladwalkercard.png" ||
            ironclad.assetBundleId != "sample-native:assets")
        {
            throw new InvalidOperationException(
                "Ironclad must declare its rigged native visual overlay and its independently bundled card Sprite.");
        }
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
    ("Foundry Counterweight targets player Atlas Health without Heat or meta scaling", () =>
    {
        ContentRelicDefinition relic = nativeSample.relics.Single(
            candidate => candidate.id == "sample-native:foundry-counterweight");
        ContentCardChangeDefinition change = relic.cardChanges.Single();
        if (relic.rarity != "Rare" || change.valueToChange != "Health" || change.operation != "Multiply" ||
            Math.Abs(change.value - 1.10f) > 0.0001f || change.side != "False" ||
            change.multiplyValueWithCurrentHeatLevel ||
            !String.IsNullOrEmpty(change.multiplyValueWithMetaProgressionMultiplier) ||
            change.onlyForTheseEntityIds == null || change.onlyForTheseEntityIds.Length != 1 ||
            change.onlyForTheseEntityIds[0] != "sample-native:atlas-crane-mech")
        {
            throw new InvalidOperationException("The foundry relic no longer matches its player-only Atlas Health contract.");
        }
    }),
    ("relic validation rejects unsupported stat attributes", () =>
    {
        ContentPackDefinition invalid = Clone(nativeSample);
        invalid.relics[0].cardChanges[0].valueToChange = "Damage";
        WithPackage(invalid, (pack, root) => ExpectDiagnostic(pack, root, new EmptySnapshot(),
            new KnownModuleValidator(), "relic.change-value-unsupported"));
    }),
    ("relic validation rejects unbudgeted Heat scaling", () =>
    {
        ContentPackDefinition invalid = Clone(nativeSample);
        invalid.relics[0].cardChanges[0].multiplyValueWithCurrentHeatLevel = true;
        WithPackage(invalid, (pack, root) => ExpectDiagnostic(pack, root, new EmptySnapshot(),
            new KnownModuleValidator(), "relic.heat-scaling-unsupported"));
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
    ("Sandlance declares its own localized name, description, and card Sprite", () =>
    {
        ContentUnitDefinition sandlance = nativeSample.units.Single(unit => unit.id == "sample-native:scout-tank");
        if (sandlance.cardImageAssetName !=
                "assets/roguecommandexpansionsample/sandlancescoutcard.png" || sandlance.localizations == null ||
            !sandlance.localizations.Any(localization => localization.language == "en-US" &&
                localization.name == "Sandlance Scout" && !String.IsNullOrWhiteSpace(localization.description)))
        {
            throw new InvalidOperationException("Sandlance is missing its own card Sprite or visible English text.");
        }
    }),
    ("Skirmish Strike Bike declares its custom model, card Sprite, and native visual target", () =>
    {
        ContentUnitDefinition bike = nativeSample.units.Single(unit => unit.id == "sample-native:skirmish-bike");
        if (bike.nativeTemplateId != "BombBike" || bike.assetBundleId != "sample-native:assets" ||
            bike.visualAssetName != "assets/roguecommandexpansionsample/skirmishbikevisual.prefab" ||
            bike.cardImageAssetName != "assets/roguecommandexpansionsample/skirmishbikecard.png" ||
            bike.visualRootPath != "GameObject/BombBike")
        {
            throw new InvalidOperationException("Skirmish Strike Bike is missing its bundled visual declaration.");
        }
    }),
    ("Unity prefab asset paths do not have to match the serialized root object name", () =>
    {
        if (!ContentPackValidator.PrefabReferenceMatchesObjectName(
                "assets/roguecommandexpansionsample/scouttankvisual.prefab", "ScoutTankVisual") ||
            !ContentPackValidator.PrefabReferenceMatchesObjectName("ScoutTankVisual.prefab", "ScoutTankVisual") ||
            !ContentPackValidator.PrefabReferenceMatchesObjectName("ScoutTankVisual", "ScoutTankVisual") ||
            ContentPackValidator.PrefabReferenceMatchesObjectName("ScoutTankVisual", "DifferentPrefab"))
        {
            throw new InvalidOperationException("Prefab asset references and object names were conflated.");
        }
    }),
    ("expansion units declare distinct English and Spanish localizations", () =>
    {
        string[] expectedUnitIds =
        [
            "sample-native:scout-tank",
            "sample-native:heavy-artillery",
            "sample-native:assault-walker",
            "sample-native:skirmish-bike",
            "sample-native:surge-tank"
        ];

        foreach (string unitId in expectedUnitIds)
        {
            ContentUnitDefinition unit = nativeSample.units.Single(u => u.id == unitId);
            if (unit.localizations == null || unit.localizations.Length < 2)
            {
                throw new InvalidOperationException($"Unit {unitId} is missing localized text entries.");
            }

            var en = unit.localizations.FirstOrDefault(l => l.language == "en-US");
            var es = unit.localizations.FirstOrDefault(l => l.language == "es-XL");
            if (en == null || string.IsNullOrWhiteSpace(en.name) || string.IsNullOrWhiteSpace(en.description) ||
                es == null || string.IsNullOrWhiteSpace(es.name) || string.IsNullOrWhiteSpace(es.description))
            {
                throw new InvalidOperationException($"Unit {unitId} is missing valid en-US or es-XL name/description.");
            }
        }
    }),
    ("expansion cards unlock by profile level and declare the card display stats", () =>
    {
        var expectedUnlockLevels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sample-native:scout-tank"] = "0",
            ["sample-native:skirmish-bike"] = "0",
            ["sample-native:assault-walker"] = "0",
            ["sample-native:surge-tank"] = "0",
            ["sample-native:heavy-artillery"] = "0"
        };
        var expectedFactoryCardImages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sample-native:heavy-artillery"] = "assets/roguecommandexpansionsample/colossussiegecard.png",
            ["sample-native:assault-walker"] = "assets/roguecommandexpansionsample/ironcladwalkercard.png"
        };
        string[] requiredCardStats =
        [
            "rarity", "neededExperienceLevel", "cost", "productionDuration", "maxCapacity", "maxHealth",
            "maxShield", "maxMana", "armorProtection", "moveSpeed", "sightRadius", "attackType", "skillType",
            "maxArmor", "damage1", "damage2", "firePointCount", "attackCooldown", "attack2Cooldown",
            "weaponRange", "effectRadius1", "effectRadius2", "skillRange", "healAmount1", "healAmount2",
            "gainCreditsAmount", "skillManaCost", "duration1", "duration2"
        ];
        var cardImages = new HashSet<string>(StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> expected in expectedUnlockLevels)
        {
            ContentUnitDefinition unit = nativeSample.units.Single(candidate => candidate.id == expected.Key);
            if (string.IsNullOrWhiteSpace(unit.cardImageAssetName) || !cardImages.Add(unit.cardImageAssetName))
            {
                throw new InvalidOperationException($"Card {unit.id} is missing a unique custom card image.");
            }

            foreach (string stat in requiredCardStats)
            {
                bool hasRawBalance = unit.balanceOverrides != null &&
                    unit.balanceOverrides.Any(entry => entry.name == stat);
                bool hasCustomDamage = stat == "damage1" && unit.customBalance != null &&
                    unit.customBalance.ContainsKey("Damage1");
                if (!hasRawBalance && !hasCustomDamage)
                {
                    string customBalanceKeys = unit.customBalance == null
                        ? "<null>"
                        : string.Join(",", unit.customBalance.Keys);
                    throw new InvalidOperationException(
                        $"Card {unit.id} is missing its explicit {stat} value; customBalance keys: {customBalanceKeys}.");
                }
            }

            var productBalanceOverrides = unit.balanceOverrides ??
                throw new InvalidOperationException($"Unit {unit.id} has no balance overrides.");
            var productionBuilding = unit.productionBuilding ??
                throw new InvalidOperationException($"Unit {unit.id} has no production building.");
            var buildingBalanceOverrides = productionBuilding.balanceOverrides ??
                throw new InvalidOperationException($"Factory {productionBuilding.id} has no balance overrides.");
            string productLevel = productBalanceOverrides.Single(entry => entry.name == "neededExperienceLevel").value;
            string buildingLevel = buildingBalanceOverrides
                .Single(entry => entry.name == "neededExperienceLevel").value;
            string productRarity = productBalanceOverrides.Single(entry => entry.name == "rarity").value;
            string buildingRarity = buildingBalanceOverrides
                .Single(entry => entry.name == "rarity").value;
            if (productLevel != expected.Value || buildingLevel != expected.Value ||
                productRarity != "Common" || buildingRarity != productRarity)
            {
                throw new InvalidOperationException(
                    $"Card {unit.id} does not mirror its level {expected.Value} unlock and rarity on its factory.");
            }

            if (expectedFactoryCardImages.TryGetValue(unit.id, out string? expectedFactoryCard) &&
                (unit.productionBuilding.assetBundleId != nativeSample.assetBundles[0].id ||
                 unit.productionBuilding.cardImageAssetName != expectedFactoryCard))
            {
                throw new InvalidOperationException($"Factory {unit.productionBuilding.id} is missing its independent reward-card Sprite mapping.");
            }
        }

        var expectedRosterUnlockLevels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sample-native:scout-tank"] = "0",
            ["sample-native:heavy-artillery"] = "0",
            ["sample-native:assault-walker"] = "0",
            ["sample-native:atlas-crane-mech"] = "0",
            ["sample-native:bastion-siege-mech"] = "0",
            ["sample-native:arachnid-hexapod"] = "0",
            ["sample-native:overlord-command-hq"] = "0",
            ["sample-native:skirmish-bike"] = "0",
            ["sample-native:surge-tank"] = "0"
        };
        foreach (ContentUnitDefinition unit in nativeSample.units)
        {
            string productLevel = unit.balanceOverrides.Single(entry => entry.name == "neededExperienceLevel").value;
            string buildingLevel = unit.productionBuilding.balanceOverrides
                .Single(entry => entry.name == "neededExperienceLevel").value;
            if (!expectedRosterUnlockLevels.TryGetValue(unit.id, out string? expectedLevel) ||
                productLevel != expectedLevel || buildingLevel != expectedLevel)
            {
                throw new InvalidOperationException(
                    $"Card {unit.id} and its factory must retain their expected matching profile unlock level.");
            }
        }

        ContentUnitDefinition bike = nativeSample.units.Single(unit => unit.id == "sample-native:skirmish-bike");
        ContentUnitDefinition artillery = nativeSample.units.Single(unit => unit.id == "sample-native:heavy-artillery");
        if (bike.localizations.Single(item => item.language == "en-US").skillDescription !=
                "Dies and summons #LargeMine#." ||
            bike.localizations.Single(item => item.language == "es-XL").skillDescription !=
                "Muere e invoca una #LargeMine#." ||
            artillery.localizations.Single(item => item.language == "en-US").skillDescription !=
                "Toggle between movement and attack modes." ||
            artillery.localizations.Single(item => item.language == "es-XL").skillDescription !=
                "Alterna entre los modos de movimiento y ataque.")
        {
            throw new InvalidOperationException("Sample ability cards are missing their localized skill text.");
        }
    }),
    ("unit skill descriptions use pack text before native-template fallback", () =>
    {
        var unit = new ContentUnitDefinition
        {
            id = "sample-native:skirmish-bike",
            nativeTemplateId = "BombBike",
            localizations =
            [
                new ContentUnitLocalizationDefinition
                {
                    language = "en-US", skillDescription = "Dies and summons #LargeMine#."
                },
                new ContentUnitLocalizationDefinition
                {
                    language = "es-XL", skillDescription = "Muere e invoca una #LargeMine#."
                }
            ]
        };
        if (!UnitLocalizationResolver.TryResolveSkillDescription(unit, "es-XL", _ => "native skill", out string spanish) ||
            spanish != "Muere e invoca una #LargeMine#." ||
            !UnitLocalizationResolver.TryResolveSkillDescription(unit, "de-DE", _ => "native skill", out string english) ||
            english != "Dies and summons #LargeMine#." ||
            !UnitLocalizationResolver.TryResolveSkillDescription(
                new ContentUnitDefinition { id = "sample-native:variant", nativeTemplateId = "BombBike" },
                "es-XL", _ => "native localized skill", out string inherited) || inherited != "native localized skill")
        {
            throw new InvalidOperationException("Skill localization did not resolve active language, English, and native fallbacks.");
        }
    }),
    ("unit skill descriptions reject whitespace-only localization", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentUnitDefinition bike = pack.units.Single(unit => unit.id == "sample-native:skirmish-bike");
            bike.localizations.Single(item => item.language == "en-US").skillDescription = "   ";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.localization-skill-description-invalid");
        });
    }),
    ("unit localization resolves active language and English fallback", () =>
    {
        var unit = new ContentUnitDefinition
        {
            id = "sample-native:sandlance-scout",
            localizations =
            [
                new ContentUnitLocalizationDefinition { language = "en-US", name = "Sandlance Scout", description = "Fast scout." },
                new ContentUnitLocalizationDefinition { language = "es-XL", name = "Explorador Sandlance", description = "Explorador veloz." }
            ]
        };
        if (!UnitLocalizationResolver.TryResolve(unit, "es-XL", false, null, out string spanishName) ||
            spanishName != "Explorador Sandlance" ||
            !UnitLocalizationResolver.TryResolve(unit, "de-DE", true, null, out string englishFallback) ||
            englishFallback != "Fast scout.")
        {
            throw new InvalidOperationException("Unit localization did not use the active language and en-US fallback.");
        }
    }),
    ("native unit localization inherits name and description from its template", () =>
    {
        var unit = new ContentUnitDefinition { id = "sample-native:variant", nativeTemplateId = "PCXBaseTank" };
        if (!UnitLocalizationResolver.TryResolve(unit, "en-US", false,
                (templateId, description) => templateId + (description ? " description" : " name"), out string name) ||
            name != "PCXBaseTank name" ||
            !UnitLocalizationResolver.TryResolve(unit, "es-XL", true,
                (templateId, description) => templateId + (description ? " description" : " name"), out string text) ||
            text != "PCXBaseTank description")
        {
            throw new InvalidOperationException("A native-template unit did not retain its template localization fallback.");
        }
    }),
    ("reactive AI sample adds a cross-pack unit to its isolated AI roster", () =>
    {
        ContentStageDefinition stage = reactiveAiSample.stages.Single(
            candidate => candidate.id == "sample-reactive-ai:counterstrike-lane");
        ContentAiProfileDefinition profile = reactiveAiSample.aiProfiles.Single(
            candidate => candidate.id == "sample-reactive-ai:base-defense");
        if (stage.unitIds == null || !stage.unitIds.Contains("sample-native:scout-tank") ||
            !stage.unitIds.Contains("sample-native:skirmish-bike") ||
            profile.unitEntries == null || !profile.unitEntries.Any(entry =>
                entry != null && entry.unitId == "sample-native:skirmish-bike" && entry.baseIndex == 0 &&
                entry.countAtStart == 1 && entry.maxLiving == 1) ||
            profile.productionBuildingEntries == null || !profile.productionBuildingEntries.Any(entry =>
                entry != null && entry.unitId == "sample-native:skirmish-bike" && entry.baseIndex == 0 &&
                entry.startingInAscensionLevel == 0 && entry.maxLiving == 1) ||
            profile.modules == null || !profile.modules.Any(module =>
                module != null && module.moduleId == "sample:reactive-base-defense" && module.parameters != null &&
                module.parameters.Any(parameter => parameter != null && parameter.name == "unitId" &&
                    parameter.value == "sample-native:skirmish-bike")) ||
            !profile.modules.Any(module => module != null && module.moduleId == "sample:scheduled-base-build" &&
                module.parameters != null && module.parameters.Any(parameter => parameter != null &&
                    parameter.name == "entityId" && parameter.value == "sample-native:skirmish-bike-factory")) ||
            stage.aiProfileIds == null || !stage.aiProfileIds.Contains("sample-reactive-ai:base-defense"))
        {
            throw new InvalidOperationException("The counterstrike profile does not add the bike and its buildable factory to the AI deck.");
        }

        WithPackage(reactiveAiSample, (pack, root) =>
            ExpectValid(pack, root, new EmptySnapshot("sample-native:scout-tank", "sample-native:skirmish-bike"),
                new KnownModuleValidator()));
        WithPackage(reactiveAiSample, (pack, root) =>
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "reference.unresolved"));
    }),
    ("AI profiles accept declared cross-pack unit dependencies", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.aiProfiles[0].unitDependencies = ["foreign:unit"];
            ExpectValid(pack, root, new EmptySnapshot("foreign:unit"), new KnownModuleValidator());
        });
    }),
    ("AI deck checks accept unitEntries dependencies without redundant unitDependencies", () =>
    {
        ContentAiProfileDefinition profile = reactiveAiSample.aiProfiles[0];
        if (!ContentPackValidator.DeclaresAiUnitDependency(profile, "sample-native:skirmish-bike") ||
            ContentPackValidator.DeclaresAiUnitDependency(profile, "foreign:undeclared") ||
            ContentPackValidator.DeclaresAiUnitDependency(profile, "SAMPLE-NATIVE:skirmish-bike") ||
            ContentPackValidator.DeclaresAiUnitDependency(profile, "") ||
            ContentPackValidator.DeclaresAiUnitDependency(null!, "sample-native:skirmish-bike"))
        {
            throw new InvalidOperationException("AI deck validation lost the explicit unitEntries dependency or admitted an undeclared ID.");
        }

        if (!ContentPackValidator.DeclaresAiUnitDependency(profile, "sample-native:skirmish-bike") ||
            ContentPackValidator.DeclaresAiUnitDependency(profile, "sample-native:skirmish-bike-factory"))
        {
            throw new InvalidOperationException("Production-building deck entries must declare their product unit dependency, not a raw factory ID.");
        }

        var customProfile = new ContentAiProfileDefinition { unitDependencies = ["foreign:unit"] };
        if (!ContentPackValidator.DeclaresAiUnitDependency(customProfile, "foreign:unit") ||
            ContentPackValidator.DeclaresAiUnitDependency(customProfile, "foreign:undeclared"))
        {
            throw new InvalidOperationException("AI deck validation no longer enforces custom prefab unitDependencies.");
        }
    }),
    ("AI profiles reject unresolved cross-pack unit dependencies", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.aiProfiles[0].unitDependencies = ["foreign:unit"];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "reference.unresolved");
        });
    }),
    ("AI profiles reject duplicate cross-pack unit dependencies", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.aiProfiles[0].unitDependencies = ["foreign:unit", "foreign:unit"];
            ExpectDiagnostic(pack, root, new EmptySnapshot("foreign:unit"), new KnownModuleValidator(),
                "reference.duplicate");
        });
    }),
    ("content pack deep clone preserves AI unit dependency declarations", () =>
    {
        ContentPackDefinition source = Clone(sample);
        source.aiProfiles[0].unitDependencies = ["foreign:unit"];
        ContentPackDefinition clone = ContentPackCopy.DeepClone(source);
        source.aiProfiles[0].unitDependencies[0] = "foreign:changed";
        if (clone.aiProfiles[0].unitDependencies == null ||
            clone.aiProfiles[0].unitDependencies.Length != 1 ||
            clone.aiProfiles[0].unitDependencies[0] != "foreign:unit")
        {
            throw new InvalidOperationException("Deep cloning an AI profile did not preserve an independent dependency array.");
        }
    }),
    ("content pack deep clone preserves independent AI production-building entries", () =>
    {
        ContentPackDefinition source = Clone(reactiveAiSample);
        ContentPackDefinition clone = ContentPackCopy.DeepClone(source);
        source.aiProfiles[0].productionBuildingEntries[0].maxLiving = 7;
        if (clone.aiProfiles[0].productionBuildingEntries == null ||
            clone.aiProfiles[0].productionBuildingEntries.Length != 1 ||
            clone.aiProfiles[0].productionBuildingEntries[0].unitId != "sample-native:skirmish-bike" ||
            clone.aiProfiles[0].productionBuildingEntries[0].maxLiving != 1)
        {
            throw new InvalidOperationException("AI production-building configuration was not cloned independently.");
        }
    }),
    ("stage-scoped native AI samples use unique reserved resource aliases", () =>
    {
        var resourceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (ContentAiProfileDefinition profile in nativeSample.aiProfiles.Concat(reactiveAiSample.aiProfiles))
        {
            if (profile == null || !profile.scopeToReferencedStages ||
                !ContentPackValidator.TryBuildScopedAiResourceName(profile.id, out string resourceName) ||
                !resourceName.StartsWith("RCL_", StringComparison.Ordinal) ||
                !resourceNames.Add(resourceName))
            {
                throw new InvalidOperationException("A stage-scoped sample AI profile did not produce a unique RCL_ alias.");
            }
        }
    }),
    ("stage-scoped AI range requires unique contiguous stage indices", () =>
    {
        if (!StageGenerationCompatibility.TryBuildContiguousStageRange(new[] { 9, 10, 11 },
            out int minimum, out int maximum, out string diagnostic) || minimum != 9 || maximum != 11 || diagnostic != null)
        {
            throw new InvalidOperationException("A contiguous stage-index sequence did not produce its exact inclusive range.");
        }
        if (StageGenerationCompatibility.TryBuildContiguousStageRange(new[] { 9, 11 },
            out _, out _, out diagnostic) || diagnostic == null || !diagnostic.Contains("contiguous", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A stage-index gap was accepted for an AI range.");
        }
        if (StageGenerationCompatibility.TryBuildContiguousStageRange(new[] { 9, 9 },
            out _, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Duplicate stage indices were accepted for an AI range.");
        }
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
    ("native-template unit accepts bounded numeric balance overrides", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].balanceAssetName = null;
            pack.units[0].prefabAssetName = null;
            pack.units[0].nativeTemplateId = "PCXBaseTank";
            pack.units[0].balanceOverrides =
            [
                new ContentBalanceOverride { name = "cost", value = "70" },
                new ContentBalanceOverride { name = "moveSpeed", value = "1.1" },
                new ContentBalanceOverride { name = "neededExperienceLevel", value = "0" },
                new ContentBalanceOverride { name = "rarity", value = "Common" }
            ];
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("unit customBalance accepts a canonical float row value", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["MoveSpeed"] = 14.5d
            };
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("unit customBalance accepts a canonical integer row value", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["MaxHealth"] = 130d
            };
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("unit customBalance preserves the native exact integer boundary through row registration", () =>
    {
        const int expectedMaxHealth = 16777216;
        ContentUnitDefinition unit = JsonSerializer.Deserialize<ContentUnitDefinition>(
            "{\"id\":\"sample:exact-integer\",\"customBalance\":{\"MaxHealth\":16777216}}",
            jsonOptions)
            ?? throw new InvalidOperationException("The custom balance test unit did not deserialize.");
        if (unit.customBalance == null ||
            !unit.customBalance.TryGetValue("MaxHealth", out var parsedMaxHealth) ||
            Convert.ToDouble(parsedMaxHealth) != expectedMaxHealth)
        {
            throw new InvalidOperationException("JSON deserialization rounded an integer-backed customBalance value.");
        }

        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].customBalance = unit.customBalance;
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });

        EntityBalancingStore.Reset();
        try
        {
            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row(unit.id));
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

            EntityBalancingParameters registered = EntityBalancingStore.EntityBalancingParametersList[0];
            EntityBalancingParameters source = (EntityBalancingParameters)provider.EntityRows[0];
            if (registered.maxHealth != expectedMaxHealth || source.maxHealth != expectedMaxHealth)
            {
                throw new InvalidOperationException("Integer-backed customBalance precision was lost before row registration.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("unit customBalance accepts the largest contiguous native exact integer", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["MaxHealth"] = 16777216d
            };
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("unit customBalance rejects integers rounded by the native getter", () =>
    {
        ContentUnitDefinition unit = JsonSerializer.Deserialize<ContentUnitDefinition>(
            "{\"id\":\"sample:rounded-max-health\",\"customBalance\":{\"MaxHealth\":16777217}}",
            jsonOptions)
            ?? throw new InvalidOperationException("The over-range integer-precision unit JSON is empty.");
        if (unit.customBalance == null ||
            !unit.customBalance.TryGetValue("MaxHealth", out double parsedMaxHealth) ||
            parsedMaxHealth != 16777217d)
        {
            throw new InvalidOperationException("JSON deserialization rounded an integer before native range validation.");
        }

        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].customBalance = unit.customBalance;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-custom-balance.value-invalid");
        });
    }),
    ("unit balanceOverrides reject integers rounded by the native getter", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            pack.units[0].balanceOverrides = (pack.units[0].balanceOverrides ?? Array.Empty<ContentBalanceOverride>())
                .Where(entry => entry == null || entry.name != "maxHealth")
                .Append(new ContentBalanceOverride { name = "maxHealth", value = "16777217" })
                .ToArray();
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.balance-override-invalid");
        });
    }),
    ("unit balanceOverrides accept the largest contiguous native exact integer", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            pack.units[0].balanceOverrides = (pack.units[0].balanceOverrides ?? Array.Empty<ContentBalanceOverride>())
                .Where(entry => entry == null || entry.name != "maxHealth")
                .Append(new ContentBalanceOverride { name = "maxHealth", value = "16777216" })
                .ToArray();
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("unit customBalance rejects a global-only balance member", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["ManaRechargePerSecond"] = 1d
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-custom-balance.parameter-invalid");
        });
    }),
    ("unit customBalance rejects aliases outside the per-unit balance contract", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["Damage"] = 12d
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-custom-balance.parameter-invalid");
        });
    }),
    ("unit customBalance rejects negative balance values", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["MoveSpeed"] = -1d
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-custom-balance.value-invalid");
        });
    }),
    ("unit customBalance rejects non-finite balance values", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["MoveSpeed"] = Double.NaN
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-custom-balance.value-invalid");
        });
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["MoveSpeed"] = Double.PositiveInfinity
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-custom-balance.value-invalid");
        });
    }),
    ("unit customBalance rejects values outside native Single range", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["MoveSpeed"] = Double.MaxValue
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-custom-balance.value-invalid");
        });
    }),
    ("unit customBalance rejects fractional integer-backed values", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["MaxHealth"] = 130.5d
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-custom-balance.value-invalid");
        });
    }),
    ("unit customBalance rejects integer-backed values outside Int32 range", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["MaxHealth"] = 2147483648d
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-custom-balance.value-invalid");
        });
    }),
    ("custom unit requires an English display name", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].localizations = [];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.localization-required");
        });
    }),
    ("unit localization rejects duplicate and unsupported language IDs", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentUnitDefinition unit = pack.units[0];
            unit.localizations =
            [
                new ContentUnitLocalizationDefinition { language = "en-US", name = "Scout" },
                new ContentUnitLocalizationDefinition { language = "en-US", name = "Duplicate" },
                new ContentUnitLocalizationDefinition { language = "zz-ZZ", name = "Unknown" }
            ];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.localization-language-duplicate");
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.localization-language-unsupported");
        });
    }),
    ("native-template unit accepts a bundle visual overlay", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            ContentUnitDefinition unit = pack.units[0];
            unit.balanceAssetName = null;
            unit.prefabAssetName = null;
            unit.cardImageAssetName = null;
            unit.visualAssetName = null;
            unit.visualRootPath = null;
            unit.nativeTemplateId = "PCXBaseTank";
            unit.assetBundleId = pack.assetBundles[0].id;
            unit.visualAssetName = "ScoutTankVisual";
            unit.visualRootPath = "Vehicle";
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("native-template unit requires a visual prefab and native root path together", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            ContentUnitDefinition unit = pack.units[0];
            unit.balanceAssetName = null;
            unit.prefabAssetName = null;
            unit.nativeTemplateId = "PCXBaseTank";
            unit.visualAssetName = "ScoutTankVisual";
            unit.visualRootPath = null;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.visual-source-invalid");
        });
    }),
    ("native-template visual root path rejects traversal segments", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            ContentUnitDefinition unit = pack.units[0];
            unit.balanceAssetName = null;
            unit.prefabAssetName = null;
            unit.nativeTemplateId = "PCXBaseTank";
            unit.visualAssetName = "ScoutTankVisual";
            unit.visualRootPath = "Vehicle/../Transform";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.visual-root-path-invalid");
        });
    }),
    ("custom-prefab unit rejects native visual overlay fields", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            ContentUnitDefinition unit = pack.units[0];
            unit.visualAssetName = "ScoutTankVisual";
            unit.visualRootPath = "Vehicle";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.visual-source-conflict");
        });
    }),
    ("custom prefab controller receives the unit namespaced identity", () =>
    {
        var controller = new EntityControllerIdentityTestController { entityId = "native:template" };
        FieldInfo entityIdField = typeof(EntityControllerIdentityTestController).GetField("entityId")!;
        EntityControllerIdentity.Assign(entityIdField, controller, "sample:custom-unit", "Unidad sample:custom-unit");
        if (!String.Equals(controller.entityId, "sample:custom-unit", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The custom prefab clone kept its original EntityController identity.");
        }

        try
        {
            EntityControllerIdentity.Assign(entityIdField, controller, " ", "Unidad inválida");
            throw new InvalidOperationException("An empty custom unit identity was accepted.");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("ID namespaced", StringComparison.Ordinal))
        {
        }

        if (!String.Equals(controller.entityId, "sample:custom-unit", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A rejected empty identity changed the controller field.");
        }
    }),
    ("native-template unit rejects mixed bundle prefab and native sources", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].nativeTemplateId = "PCXBaseTank";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.source-conflict");
        });
    }),
    ("unit upgrade rejects a production factory target", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].upgrades =
            [
                new ContentUnitUpgradeDefinition
                {
                    targetUnitId = pack.units[0].productionBuilding.id,
                    requiredExperienceLevel = 1,
                    cost = 10
                }
            ];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-upgrade.target-unknown");
        });
    }),
    ("unit upgrade accepts a registered native target", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].upgrades =
            [
                new ContentUnitUpgradeDefinition
                {
                    targetUnitId = "NativeWalker",
                    requiredExperienceLevel = 1,
                    cost = 10
                }
            ];
            ExpectValid(pack, root, new SnapshotWithUnits(["NativeWalker"]), new KnownModuleValidator());
        });
    }),
    ("unit upgrade accepts a unit declared by another pack", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].upgrades =
            [
                new ContentUnitUpgradeDefinition
                {
                    targetUnitId = "other:registered-unit",
                    requiredExperienceLevel = 1,
                    cost = 10
                }
            ];
            ExpectValid(pack, root,
                new SnapshotWithUnits(["other:registered-unit"]), new KnownModuleValidator());
        });
    }),
    ("unit synergy rejects a production factory target", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].synergies =
            [
                new ContentUnitSynergyDefinition
                {
                    id = "sample:test-synergy-factory",
                    statName = "damage",
                    multiplier = 1,
                    radius = 1,
                    targetUnitIds = [pack.units[0].productionBuilding.id]
                }
            ];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-synergy.target-unknown");
        });
    }),
    ("unit synergy accepts registered native and cross-pack targets", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].synergies =
            [
                new ContentUnitSynergyDefinition
                {
                    id = "sample:test-synergy-native",
                    statName = "damage",
                    multiplier = 1,
                    radius = 1,
                    targetUnitIds = ["NativeWalker", "other:registered-unit"]
                }
            ];
            ExpectValid(pack, root,
                new SnapshotWithUnits(["NativeWalker", "other:registered-unit"]), new KnownModuleValidator());
        });
    }),
    ("unit upgrade requires a known target unit", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].upgrades =
            [
                new ContentUnitUpgradeDefinition { targetUnitId = "sample:unknown-unit", requiredExperienceLevel = 1, cost = 10 }
            ];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-upgrade.target-unknown");
        });
    }),
    ("unit upgrade rejects negative cost", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].upgrades =
            [
                new ContentUnitUpgradeDefinition { targetUnitId = pack.units[0].id, requiredExperienceLevel = 1, cost = -5 }
            ];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-upgrade.cost-invalid");
        });
    }),
    ("unit experience rejects invalid max level", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].experience = new ContentUnitExperienceDefinition { experiencePerLevel = 100, maxLevel = 200, experienceGainMultiplier = 1, levelUpBonus = 5 };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-experience.max-level-invalid");
        });
    }),
    ("native-template unit rejects non-allowlisted or negative stat overrides", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].balanceAssetName = null;
            pack.units[0].prefabAssetName = null;
            pack.units[0].nativeTemplateId = "PCXBaseTank";
            pack.units[0].balanceOverrides =
            [
                new ContentBalanceOverride { name = "entityId", value = "other:unit" },
                new ContentBalanceOverride { name = "cost", value = "-10" }
            ];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.balance-override-invalid");
        });
    }),
    ("data-only native unit, stage and AI profile pass without a bundle manifest", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            string bundlesDirectory = Path.Combine(root, "bundles");
            if (File.Exists(Path.Combine(bundlesDirectory, "test-manifest.bundle")))
            {
                File.Delete(Path.Combine(bundlesDirectory, "test-manifest.bundle"));
            }
            foreach (string bundlePath in Directory.GetFiles(bundlesDirectory, "*.bundle"))
            {
                File.Delete(bundlePath);
            }

            pack.assetBundles = [];
            pack.assetBundleManifestPath = null;

            ContentUnitDefinition unit = pack.units[0];
            unit.assetBundleId = null;
            unit.balanceAssetName = null;
            unit.prefabAssetName = null;
            unit.cardImageAssetName = null;
            unit.visualAssetName = null;
            unit.visualRootPath = null;
            unit.nativeTemplateId = "PCXBaseTank";
            unit.balanceOverrides =
            [
                new ContentBalanceOverride { name = "cost", value = "70" },
                new ContentBalanceOverride { name = "neededExperienceLevel", value = "0" },
                new ContentBalanceOverride { name = "rarity", value = "Common" }
            ];

            ContentStageDefinition stage = pack.stages[0];
            stage.assetBundleId = null;
            stage.assetName = null;
            stage.nativeTemplateId = "StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit";
            stage.parameterOverrides = [new ContentStageParameterOverride { name = "levelCount", value = "8" }];

            ContentAiProfileDefinition profile = pack.aiProfiles[0];
            profile.assetBundleId = null;
            profile.prefabAssetName = null;
            profile.nativeTemplateId = "_01_ Quick Hunter Enemy Var";
            profile.scopeToReferencedStages = true;

            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("native stage rejects a mixed bundle source", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.stages[0].nativeTemplateId = "StageParameters_Pools_Level1";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "stage.source-conflict");
        });
    }),
    ("native AI profile accepts a named template without a bundle", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.aiProfiles[0].assetBundleId = null;
            pack.aiProfiles[0].prefabAssetName = null;
            pack.aiProfiles[0].nativeTemplateId = "_01_ Quick Hunter Enemy Var";
            pack.aiProfiles[0].scopeToReferencedStages = true;
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("scoped native AI can add a unit declared by the same pack", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            ContentAiProfileDefinition profile = pack.aiProfiles[0];
            profile.assetBundleId = null;
            profile.prefabAssetName = null;
            profile.nativeTemplateId = "_01_ Quick Hunter Enemy Var";
            profile.scopeToReferencedStages = true;
            profile.unitEntries =
            [
                new ContentAiUnitEntryDefinition
                {
                    unitId = pack.units[0].id,
                    baseIndex = 0,
                    startingInAscensionLevel = 0,
                    countAtStart = 1,
                    maxLiving = 1
                }
            ];

            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("scoped native AI can add a product factory to a base in the same pack", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            ContentAiProfileDefinition profile = pack.aiProfiles[0];
            profile.assetBundleId = null;
            profile.prefabAssetName = null;
            profile.nativeTemplateId = "_01_ Quick Hunter Enemy Var";
            profile.scopeToReferencedStages = true;
            profile.productionBuildingEntries =
            [
                new ContentAiProductionBuildingEntryDefinition
                {
                    unitId = pack.units[0].id,
                    baseIndex = 0,
                    startingInAscensionLevel = 0,
                    maxLiving = 1
                }
            ];

            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("AI production-building entries require a scoped native clone and report their path", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            ContentAiProfileDefinition profile = pack.aiProfiles[0];
            profile.productionBuildingEntries =
            [
                new ContentAiProductionBuildingEntryDefinition
                {
                    unitId = pack.units[0].id,
                    baseIndex = 0,
                    startingInAscensionLevel = 0,
                    maxLiving = 1
                }
            ];

            ContentPackValidationResult result = ContentPackValidator.Validate(
                pack, root, new EmptySnapshot(), new KnownModuleValidator());
            ContentDiagnostic diagnostic = result.Diagnostics.Single(item =>
                item.Code == "ai-profile.unit-entries-require-scoped-native");
            if (!diagnostic.Path.EndsWith(".productionBuildingEntries", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A factory-only scope error did not point to productionBuildingEntries.");
            }
        });
    }),
    ("AI production-building entries validate max living and reject duplicates per base", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            ContentAiProfileDefinition profile = pack.aiProfiles[0];
            profile.assetBundleId = null;
            profile.prefabAssetName = null;
            profile.nativeTemplateId = "_01_ Quick Hunter Enemy Var";
            profile.scopeToReferencedStages = true;
            profile.productionBuildingEntries =
            [
                new ContentAiProductionBuildingEntryDefinition
                {
                    unitId = pack.units[0].id,
                    baseIndex = 0,
                    startingInAscensionLevel = 0,
                    maxLiving = 0
                },
                new ContentAiProductionBuildingEntryDefinition
                {
                    unitId = pack.units[0].id,
                    baseIndex = 0,
                    startingInAscensionLevel = 0,
                    maxLiving = 1
                }
            ];

            ContentPackValidationResult result = ContentPackValidator.Validate(
                pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (!result.Diagnostics.Any(item => item.Code == "ai-production-building-entry.max-living-invalid") ||
                !result.Diagnostics.Any(item => item.Code == "reference.duplicate"))
            {
                throw new InvalidOperationException(
                    "Invalid production-building maxLiving or a duplicate base entry was not rejected.");
            }
        });
    }),
    ("AI production-building product IDs establish declared unit dependencies", () =>
    {
        ContentAiProfileDefinition profile = new ContentAiProfileDefinition
        {
            productionBuildingEntries =
            [
                new ContentAiProductionBuildingEntryDefinition
                {
                    unitId = "foreign:bike",
                    baseIndex = 0,
                    startingInAscensionLevel = 0,
                    maxLiving = 1
                }
            ]
        };

        if (!ContentPackValidator.DeclaresAiUnitDependency(profile, "foreign:bike") ||
            ContentPackValidator.DeclaresAiUnitDependency(profile, "foreign:bike-factory"))
        {
            throw new InvalidOperationException(
                "The product unit ID must establish the dependency; its derived factory ID must not.");
        }
    }),
    ("AI unit entries reject custom prefabs that are not isolated clones", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.aiProfiles[0].unitEntries =
            [
                new ContentAiUnitEntryDefinition
                {
                    unitId = pack.units[0].id,
                    baseIndex = 0,
                    startingInAscensionLevel = 0,
                    countAtStart = 1,
                    maxLiving = 1
                }
            ];

            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "ai-profile.unit-entries-require-scoped-native");
        });
    }),
    ("AI unit entries enforce a valid maximum living count", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            ContentAiProfileDefinition profile = pack.aiProfiles[0];
            profile.assetBundleId = null;
            profile.prefabAssetName = null;
            profile.nativeTemplateId = "_01_ Quick Hunter Enemy Var";
            profile.scopeToReferencedStages = true;
            profile.unitEntries =
            [
                new ContentAiUnitEntryDefinition
                {
                    unitId = pack.units[0].id,
                    baseIndex = 0,
                    startingInAscensionLevel = 0,
                    countAtStart = 1,
                    maxLiving = 0
                }
            ];

            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "ai-unit-entry.max-living-invalid");
        });
    }),
    ("native AI modules require an isolated stage-scoped clone", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.aiProfiles[0].assetBundleId = null;
            pack.aiProfiles[0].prefabAssetName = null;
            pack.aiProfiles[0].nativeTemplateId = "_01_ Quick Hunter Enemy Var";
            pack.aiProfiles[0].scopeToReferencedStages = false;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "ai-profile.native-scope-required");
        });
    }),
    ("stage-scoped AI rejects custom prefabs", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.aiProfiles[0].scopeToReferencedStages = true;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "ai-profile.scoped-native-required");
        });
    }),
    ("stage-scoped native AI must be referenced by a stage in its pack", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.aiProfiles[0].assetBundleId = null;
            pack.aiProfiles[0].prefabAssetName = null;
            pack.aiProfiles[0].nativeTemplateId = "_01_ Quick Hunter Enemy Var";
            pack.aiProfiles[0].scopeToReferencedStages = true;
            pack.stages[0].aiProfileIds = Array.Empty<string>();
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "ai-profile.scoped-stage-required");
        });
    }),
    ("scheduled base-build AI parser supplies bounded defaults", () =>
    {
        if (!ScheduledBaseBuildParameterParser.TryParse(null, out ScheduledBaseBuildSettings defaults,
            out string diagnostic) || diagnostic != null || defaults.EntityId != null || defaults.BaseIndex != 0 ||
            defaults.IntervalSeconds != 8f || defaults.MaximumSuccessfulBuilds != 1 || defaults.MaximumAttempts != 3)
        {
            throw new InvalidOperationException("The scheduled AI parser did not return its documented defaults.");
        }

        ContentModuleParameter[] boundaryValues =
        [
            new ContentModuleParameter { name = "entityId", value = "sample:heavy-tank" },
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "intervalSeconds", value = "600" },
            new ContentModuleParameter { name = "maxSuccessfulBuilds", value = "32" },
            new ContentModuleParameter { name = "maxAttempts", value = "128" }
        ];
        if (!ScheduledBaseBuildParameterParser.TryParse(boundaryValues, out ScheduledBaseBuildSettings maximums,
            out diagnostic) || maximums.EntityId != "sample:heavy-tank" || maximums.BaseIndex != 31 ||
            maximums.IntervalSeconds != 600f || maximums.MaximumSuccessfulBuilds != 32 ||
            maximums.MaximumAttempts != 128)
        {
            throw new InvalidOperationException("The scheduled AI parser rejected valid upper-bound values.");
        }
    }),
    ("scheduled base-build AI respects active deck maxLiving", () =>
    {
        if (!ScheduledBaseBuildLimitPolicy.HasRemainingCapacity(0, 1) ||
            !ScheduledBaseBuildLimitPolicy.HasRemainingCapacity(1, 2) ||
            ScheduledBaseBuildLimitPolicy.HasRemainingCapacity(1, 1) ||
            ScheduledBaseBuildLimitPolicy.HasRemainingCapacity(2, 1) ||
            ScheduledBaseBuildLimitPolicy.HasRemainingCapacity(-1, 1) ||
            ScheduledBaseBuildLimitPolicy.HasRemainingCapacity(0, 0))
        {
            throw new InvalidOperationException("The scheduled AI build limit did not enforce the active deck capacity.");
        }
    }),
    ("scheduled base-build AI counts only living factories for its AI base", () =>
    {
        const string factoryId = "sample-native:skirmish-bike-factory";
        EntityBalancingStore.Reset();
        EntityBalancingStore.BuildingEntityIds.Add(factoryId);
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;

        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        int createCalls = 0;
        try
        {
            DefaultAiBehaviour.TickHasStarted = true;
            DefaultAiBehaviour.ActiveConstructableEntities.Add(new EntityInAiDeck
            {
                entityId = factoryId,
                MaxLiving = 2
            });
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                EntityId = factoryId,
                IsControlledByAi = true,
                aiBaseIndex = 0
            });
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                EntityId = factoryId,
                IsControlledByAi = true,
                aiBaseIndex = 1
            });
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                EntityId = factoryId,
                IsControlledByAi = true,
                aiBaseIndex = 0,
                IsDestroyed = true
            });
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                EntityId = factoryId,
                IsControlledByAi = true,
                aiBaseIndex = 0,
                DestroyHasBeenStarted = true
            });
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                EntityId = factoryId,
                IsControlledByAi = false,
                aiBaseIndex = 0
            });
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                EntityId = "native:other-factory",
                IsControlledByAi = true,
                aiBaseIndex = 0
            });
            DefaultAiBehaviour.CreateBuildingHandler = (_, _) =>
            {
                createCalls++;
                return new EntityController();
            };

            behaviour = new ScheduledBaseBuildAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "entityId", value = factoryId },
                    new ContentModuleParameter { name = "intervalSeconds", value = "1" },
                    new ContentModuleParameter { name = "maxSuccessfulBuilds", value = "1" },
                    new ContentModuleParameter { name = "maxAttempts", value = "2" }
                ]);
            behaviour.OnInitialized();
            behaviour.Tick();
            UnityEngine.Time.time = 1f;
            behaviour.Tick();

            if (createCalls != 1)
            {
                throw new InvalidOperationException(
                    "The AI factory capacity count included another base, a destroyed factory, a non-AI factory, or a different entity ID.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("scheduled base-build AI deduplicates returned registered factories at the live cap", () =>
    {
        const string factoryId = "sample-native:skirmish-bike-factory";
        EntityBalancingStore.Reset();
        EntityBalancingStore.BuildingEntityIds.Add(factoryId);
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;

        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        int createCalls = 0;
        try
        {
            DefaultAiBehaviour.TickHasStarted = true;
            DefaultAiBehaviour.ActiveConstructableEntities.Add(new EntityInAiDeck
            {
                entityId = factoryId,
                MaxLiving = 2
            });
            DefaultAiBehaviour.CreateBuildingHandler = (entityId, baseIndex) =>
            {
                createCalls++;
                var created = new EntityController
                {
                    EntityId = entityId,
                    IsControlledByAi = true,
                    aiBaseIndex = baseIndex
                };
                ExistingControllers.Instance.AiBuildings().Add(created);
                return created;
            };

            behaviour = new ScheduledBaseBuildAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "entityId", value = factoryId },
                    new ContentModuleParameter { name = "intervalSeconds", value = "1" },
                    new ContentModuleParameter { name = "maxSuccessfulBuilds", value = "3" },
                    new ContentModuleParameter { name = "maxAttempts", value = "4" }
                ]);
            behaviour.OnInitialized();
            behaviour.Tick();
            for (int second = 1; second <= 3; second++)
            {
                UnityEngine.Time.time = second;
                behaviour.Tick();
            }

            if (createCalls != 2)
            {
                throw new InvalidOperationException(
                    "The AI module either counted a returned factory twice or built beyond the active deck maxLiving.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("scheduled base-build AI refreshes maxLiving before each attempt", () =>
    {
        const string factoryId = "sample-native:skirmish-bike-factory";
        EntityBalancingStore.Reset();
        EntityBalancingStore.BuildingEntityIds.Add(factoryId);
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;

        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        int createCalls = 0;
        try
        {
            DefaultAiBehaviour.TickHasStarted = true;
            DefaultAiBehaviour.ActiveConstructableEntities.Add(new EntityInAiDeck
            {
                entityId = factoryId,
                MaxLiving = 3
            });
            DefaultAiBehaviour.CreateBuildingHandler = (entityId, baseIndex) =>
            {
                createCalls++;
                var created = new EntityController
                {
                    EntityId = entityId,
                    IsControlledByAi = true,
                    aiBaseIndex = baseIndex
                };
                ExistingControllers.Instance.AiBuildings().Add(created);
                return created;
            };

            behaviour = new ScheduledBaseBuildAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "entityId", value = factoryId },
                    new ContentModuleParameter { name = "intervalSeconds", value = "1" },
                    new ContentModuleParameter { name = "maxSuccessfulBuilds", value = "3" },
                    new ContentModuleParameter { name = "maxAttempts", value = "4" }
                ]);
            behaviour.OnInitialized();
            behaviour.Tick();
            UnityEngine.Time.time = 1f;
            behaviour.Tick();
            DefaultAiBehaviour.ActiveConstructableEntities[0] = new EntityInAiDeck
            {
                entityId = factoryId,
                MaxLiving = 1
            };
            UnityEngine.Time.time = 2f;
            behaviour.Tick();

            if (createCalls != 1)
            {
                throw new InvalidOperationException("The AI module used a stale maxLiving value after the active deck changed.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("scheduled base-build AI fails closed when the building registry is unavailable", () =>
    {
        const string factoryId = "sample-native:skirmish-bike-factory";
        EntityBalancingStore.Reset();
        EntityBalancingStore.BuildingEntityIds.Add(factoryId);
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;

        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        int createCalls = 0;
        try
        {
            DefaultAiBehaviour.TickHasStarted = true;
            DefaultAiBehaviour.ActiveConstructableEntities.Add(new EntityInAiDeck
            {
                entityId = factoryId,
                MaxLiving = 1
            });
            ExistingControllers.Instance.ThrowOnAiBuildings = true;
            DefaultAiBehaviour.CreateBuildingHandler = (_, _) =>
            {
                createCalls++;
                return new EntityController();
            };

            behaviour = new ScheduledBaseBuildAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "entityId", value = factoryId },
                    new ContentModuleParameter { name = "intervalSeconds", value = "1" },
                    new ContentModuleParameter { name = "maxSuccessfulBuilds", value = "1" },
                    new ContentModuleParameter { name = "maxAttempts", value = "2" }
                ]);
            behaviour.OnInitialized();
            behaviour.Tick();
            UnityEngine.Time.time = 1f;
            behaviour.Tick();

            if (createCalls != 0)
            {
                throw new InvalidOperationException("The scheduled AI placed a factory without verifying live capacity.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("harvester-raider AI avoids duplicate orders and reuses raiders after their command ends", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 15f;

        var target = new EntityController
        {
            IsControlledByPlayer = true,
            IsUnit = true,
            Position = new UnityEngine.Vector3(8f, 0f, 0f),
            gameObject = new UnityEngine.GameObject("Player Harvester")
        };
        var raider = new EntityController
        {
            IsControlledByAi = true,
            IsUnit = true,
            CanAttackAndIsAttackEnabled = true,
            CanMoveAndMovementIsEnabled = true,
            aiBaseIndex = 0,
            UniqueEntityId = 501,
            Position = new UnityEngine.Vector3(5f, 0f, 0f)
        };
        var immobileCombatant = new EntityController
        {
            IsControlledByAi = true,
            IsUnit = true,
            CanAttackAndIsAttackEnabled = true,
            CanMoveAndMovementIsEnabled = false,
            aiBaseIndex = 0,
            UniqueEntityId = 502,
            Position = new UnityEngine.Vector3(6f, 0f, 0f)
        };
        ExistingControllers.Instance.Harvesters.Add(target);
        ExistingControllers.Instance.AiUnits().Add(raider);
        ExistingControllers.Instance.AiUnits().Add(immobileCombatant);

        IRogueCommandAiRuntimeBehaviour behaviour = new HarvesterRaiderAiModule().CreateBehaviour(
            new DefaultAiBehaviour
            {
                gameObject = new UnityEngine.GameObject("Harvester AI"),
                transform = new UnityEngine.Transform { position = new UnityEngine.Vector3(0f, 0f, 0f) }
            },
            [
                new ContentModuleParameter { name = "baseIndex", value = "0" },
                new ContentModuleParameter { name = "scanIntervalSeconds", value = "2" },
                new ContentModuleParameter { name = "cooldownSeconds", value = "5" },
                new ContentModuleParameter { name = "raidSquadSize", value = "1" },
                new ContentModuleParameter { name = "minGameTimeSeconds", value = "15" }
            ]);
        behaviour.OnInitialized();
        try
        {
            behaviour.Tick();
            if (!raider.LastExecutedCommand.HasValue ||
                raider.LastExecutedCommand.Value.intent != "Harvester raid strike" ||
                raider.LastExecutedCommand.Value.destination.x != target.Position.x ||
                immobileCombatant.LastExecutedCommand.HasValue)
            {
                throw new InvalidOperationException("The harvester raid did not select a mobile raider for the target.");
            }

            int firstRaidOrderCallCount = raider.ExecuteCommandCallCount;
            UnityEngine.Time.time = 20f;
            behaviour.Tick();
            if (raider.ExecuteCommandCallCount != firstRaidOrderCallCount)
            {
                throw new InvalidOperationException("The active raid order was duplicated during its cooldown scan.");
            }

            raider.ExecuteCommand(new EntityCommand { tag = "native:other-order" },
                EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
            int reassignmentCallCount = raider.ExecuteCommandCallCount;
            UnityEngine.Time.time = 22f;
            behaviour.Tick();
            if (raider.ExecuteCommandCallCount == reassignmentCallCount ||
                raider.LastExecutedCommand?.intent != "Harvester raid strike")
            {
                throw new InvalidOperationException("The raider did not become eligible after its raid command ended.");
            }
        }
        finally
        {
            behaviour.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("siege-assault AI reuses a completed mobile squad and waits for its minimum size", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 30f;

        var target = new EntityController
        {
            IsControlledByPlayer = true,
            IsBuilding = true,
            CurrentHealth = 250f,
            Position = new UnityEngine.Vector3(25f, 0f, 0f),
            gameObject = new UnityEngine.GameObject("Known Player Building")
        };
        var first = new EntityController
        {
            IsControlledByAi = true,
            IsUnit = true,
            CanAttackAndIsAttackEnabled = true,
            CanMoveAndMovementIsEnabled = true,
            aiBaseIndex = 0,
            UniqueEntityId = 601
        };
        var second = new EntityController
        {
            IsControlledByAi = true,
            IsUnit = true,
            CanAttackAndIsAttackEnabled = true,
            CanMoveAndMovementIsEnabled = true,
            aiBaseIndex = 0,
            UniqueEntityId = 602
        };
        var immobileCombatant = new EntityController
        {
            IsControlledByAi = true,
            IsUnit = true,
            CanAttackAndIsAttackEnabled = true,
            CanMoveAndMovementIsEnabled = false,
            aiBaseIndex = 0,
            UniqueEntityId = 603
        };
        ExistingControllers.Instance.PlayerBuildingsKnownToAi().Add(target);
        ExistingControllers.Instance.AiUnits().Add(first);
        ExistingControllers.Instance.AiUnits().Add(second);
        ExistingControllers.Instance.AiUnits().Add(immobileCombatant);

        IRogueCommandAiRuntimeBehaviour behaviour = new SiegeAssaultAiModule().CreateBehaviour(
            new DefaultAiBehaviour
            {
                gameObject = new UnityEngine.GameObject("Siege AI"),
                transform = new UnityEngine.Transform { position = new UnityEngine.Vector3(0f, 0f, 0f) }
            },
            [
                new ContentModuleParameter { name = "baseIndex", value = "0" },
                new ContentModuleParameter { name = "cooldownSeconds", value = "10" },
                new ContentModuleParameter { name = "minSquadSize", value = "2" },
                new ContentModuleParameter { name = "targetPriority", value = "ClosestBuilding" },
                new ContentModuleParameter { name = "minGameTimeSeconds", value = "30" }
            ]);
        behaviour.OnInitialized();
        try
        {
            behaviour.Tick();
            if (!first.LastExecutedCommand.HasValue || !second.LastExecutedCommand.HasValue ||
                first.LastExecutedCommand.Value.intent != "Siege assault operation" ||
                second.LastExecutedCommand.Value.intent != "Siege assault operation" ||
                first.LastExecutedCommand.Value.destination.x != target.Position.x ||
                second.LastExecutedCommand.Value.destination.x != target.Position.x ||
                immobileCombatant.LastExecutedCommand.HasValue)
            {
                throw new InvalidOperationException("The siege assault did not dispatch the required mobile squad to its known target.");
            }

            int firstOrderCallCount = first.ExecuteCommandCallCount;
            int secondOrderCallCount = second.ExecuteCommandCallCount;
            UnityEngine.Time.time = 40f;
            behaviour.Tick();
            if (first.ExecuteCommandCallCount != firstOrderCallCount ||
                second.ExecuteCommandCallCount != secondOrderCallCount)
            {
                throw new InvalidOperationException("The siege module duplicated orders while its squad was still assigned.");
            }

            first.ExecuteCommand(new EntityCommand { tag = "native:other-order" },
                EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
            int firstOtherOrderCallCount = first.ExecuteCommandCallCount;
            UnityEngine.Time.time = 45f;
            behaviour.Tick();
            if (first.LastExecutedCommand?.tag != "native:other-order" ||
                second.ExecuteCommandCallCount != secondOrderCallCount)
            {
                throw new InvalidOperationException("The siege module dispatched before the minimum squad had reassembled.");
            }

            second.ExecuteCommand(new EntityCommand { tag = "native:other-order" },
                EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);
            int secondOtherOrderCallCount = second.ExecuteCommandCallCount;
            UnityEngine.Time.time = 50f;
            behaviour.Tick();
            if (first.ExecuteCommandCallCount != firstOtherOrderCallCount + 1 ||
                second.ExecuteCommandCallCount != secondOtherOrderCallCount + 1)
            {
                throw new InvalidOperationException("The completed siege squad could not be reused in a later assault.");
            }
        }
        finally
        {
            behaviour.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("reactive base-defense AI parser supplies bounded defaults", () =>
    {
        if (!ReactiveBaseDefenseParameterParser.TryParse(null, out ReactiveBaseDefenseSettings defaults,
            out string diagnostic) || diagnostic != null || defaults.BaseIndex != 0 ||
            defaults.UnitId != null || defaults.CooldownSeconds != 1f || defaults.DefenseRadiusCells != 12f ||
            defaults.MaximumResponders != 2 || defaults.ProtectedTargetType != "Buildings")
        {
            throw new InvalidOperationException("The reactive AI parser did not return its documented defaults.");
        }

        ContentModuleParameter[] boundaries =
        [
            new ContentModuleParameter { name = "unitId", value = "sample-native:skirmish-bike" },
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "cooldownSeconds", value = "60" },
            new ContentModuleParameter { name = "defenseRadiusCells", value = "64" },
            new ContentModuleParameter { name = "maxResponders", value = "4" }
        ];
        if (!ReactiveBaseDefenseParameterParser.TryParse(boundaries, out ReactiveBaseDefenseSettings maximums,
            out diagnostic) || diagnostic != null || maximums.UnitId != "sample-native:skirmish-bike" ||
            maximums.BaseIndex != 31 || maximums.CooldownSeconds != 60f ||
            maximums.DefenseRadiusCells != 64f || maximums.MaximumResponders != 4)
        {
            throw new InvalidOperationException("The reactive AI parser rejected valid upper-bound values.");
        }

        ContentModuleParameter[] nativeUnitId =
        [
            new ContentModuleParameter { name = "unitId", value = "BombBike" }
        ];
        if (!ReactiveBaseDefenseParameterParser.TryParse(nativeUnitId,
            out ReactiveBaseDefenseSettings nativeSettings, out diagnostic) ||
            diagnostic != null || nativeSettings.UnitId != "BombBike")
        {
            throw new InvalidOperationException("The reactive AI parser rejected a valid unnamespaced native unit ID.");
        }
    }),
    ("reactive base-defense AI parser rejects unknown, duplicate, null, and out-of-range values", () =>
    {
        ContentModuleParameter[][] invalidInputs =
        [
            [new ContentModuleParameter { name = "unknown", value = "1" }],
            [new ContentModuleParameter { name = "baseIndex", value = "0" }, new ContentModuleParameter { name = "baseIndex", value = "1" }],
            [null!],
            [new ContentModuleParameter { name = "baseIndex", value = "32" }],
            [new ContentModuleParameter { name = "maxResponders", value = "5" }],
            [new ContentModuleParameter { name = "unitId", value = "" }],
            [new ContentModuleParameter { name = "unitId", value = ":" }],
            [new ContentModuleParameter { name = "unitId", value = ":bike" }],
            [new ContentModuleParameter { name = "unitId", value = "foo:" }],
            [new ContentModuleParameter { name = "unitId", value = "a::b" }],
            [new ContentModuleParameter { name = "unitId", value = "sample::bike" }],
            [new ContentModuleParameter { name = "unitId", value = "sample:bad/id" }],
            [new ContentModuleParameter { name = "unitId", value = new string('a', 129) }],
            [new ContentModuleParameter { name = "cooldownSeconds", value = "NaN" }],
            [new ContentModuleParameter { name = "defenseRadiusCells", value = "Infinity" }]
        ];

        for (int i = 0; i < invalidInputs.Length; i++)
        {
            if (ReactiveBaseDefenseParameterParser.TryParse(invalidInputs[i], out _, out string diagnostic) ||
                diagnostic == null)
            {
                throw new InvalidOperationException("The reactive AI parser accepted invalid input " + i + ".");
            }
        }
    }),
    ("reactive base-defense parser accepts explicit protected target types", () =>
    {
        ContentModuleParameter[] parameters =
        [new ContentModuleParameter { name = "protectedTargetType", value = "BuildingsAndHarvesters" }];
        if (!ReactiveBaseDefenseParameterParser.TryParse(parameters, out _, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("The parser did not retain the requested protected target types.");
        }

        ContentModuleParameter[][] invalid =
        [
            [new ContentModuleParameter { name = "protectedTargetType", value = "" }],
            [new ContentModuleParameter { name = "protectedTargetType", value = "Other" }],
            [new ContentModuleParameter { name = "protectedTargetType", value = "Buildings" },
                new ContentModuleParameter { name = "protectedTargetType", value = "Harvesters" }],
            [new ContentModuleParameter { name = "protectedTargetType", value = "Buildings,Buildings" }]
        ];
        foreach (ContentModuleParameter[] value in invalid)
        {
            if (ReactiveBaseDefenseParameterParser.TryParse(value, out _, out diagnostic) || diagnostic == null)
            {
                throw new InvalidOperationException("The parser accepted an empty, unknown, or duplicate target type.");
            }
        }
    }),
    ("reactive base-defense threat radius uses bounded grid distance", () =>
    {
        if (!ReactiveBaseDefenseThreatRange.IsWithinRadius(
                new UnityEngine.Vector2Int(0, 0), new UnityEngine.Vector2Int(12, 0), 12f) ||
            !ReactiveBaseDefenseThreatRange.IsWithinRadius(
                new UnityEngine.Vector2Int(0, 0), new UnityEngine.Vector2Int(8, 8), 12f))
        {
            throw new InvalidOperationException("A threat on or inside the defense radius was rejected.");
        }

        if (ReactiveBaseDefenseThreatRange.IsWithinRadius(
                new UnityEngine.Vector2Int(0, 0), new UnityEngine.Vector2Int(13, 0), 12f) ||
            ReactiveBaseDefenseThreatRange.IsWithinRadius(
                new UnityEngine.Vector2Int(0, 0), new UnityEngine.Vector2Int(9, 9), 12f) ||
            ReactiveBaseDefenseThreatRange.IsWithinRadius(
                new UnityEngine.Vector2Int(0, 0), new UnityEngine.Vector2Int(0, 0), -1f) ||
            ReactiveBaseDefenseThreatRange.IsWithinRadius(
                new UnityEngine.Vector2Int(0, 0), new UnityEngine.Vector2Int(0, 0), Single.NaN) ||
            ReactiveBaseDefenseThreatRange.IsWithinRadius(
                new UnityEngine.Vector2Int(0, 0), new UnityEngine.Vector2Int(0, 0), Single.PositiveInfinity))
        {
            throw new InvalidOperationException("An out-of-radius or invalid-radius threat was accepted.");
        }
    }),
    ("reactive base-defense dispatches an in-range attack order before weapon range", () =>
    {
        ExistingControllers.Instance.Reset();
        UnityEngine.Time.time = 10f;

        var target = new EntityController
        {
            IsControlledByAi = true,
            IsBuilding = true,
            aiBaseIndex = 0,
            CurrentCell = new UnityEngine.Vector2Int(10, 10),
            UniqueEntityId = 100,
            gameObject = new UnityEngine.GameObject("AI Base Building")
        };
        var attacker = new EntityController
        {
            IsControlledByPlayer = true,
            IsUnit = true,
            CanAttackAndIsAttackEnabled = true,
            IsTargetInRange = false,
            CurrentAttackTarget = target,
            CurrentCell = new UnityEngine.Vector2Int(15, 10),
            Position = new UnityEngine.Vector3(15f, 0f, 10f),
            UniqueEntityId = 200,
            gameObject = new UnityEngine.GameObject("Approaching Player Unit")
        };
        var defender = new EntityController
        {
            IsControlledByAi = true,
            IsUnit = true,
            CanAttackAndIsAttackEnabled = true,
            CanMoveAndMovementIsEnabled = true,
            IsCombatUnit = true,
            entityId = "sample-native:skirmish-bike",
            CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
            aiBaseIndex = 0,
            CurrentCell = new UnityEngine.Vector2Int(12, 10),
            UniqueEntityId = 300,
            gameObject = new UnityEngine.GameObject("Idle AI Defender")
        };
        var closerNativeDefender = new EntityController
        {
            IsControlledByAi = true,
            IsUnit = true,
            CanAttackAndIsAttackEnabled = true,
            CanMoveAndMovementIsEnabled = true,
            IsCombatUnit = true,
            entityId = "native:combat-unit",
            CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
            aiBaseIndex = 0,
            CurrentCell = new UnityEngine.Vector2Int(10, 10),
            UniqueEntityId = 301,
            gameObject = new UnityEngine.GameObject("Closer Native Defender")
        };
        ExistingControllers.Instance.PlayerUnits().Add(attacker);
        ExistingControllers.Instance.AiUnits().Add(target);
        ExistingControllers.Instance.AiUnits().Add(defender);
        ExistingControllers.Instance.AiUnits().Add(closerNativeDefender);

        var module = new ReactiveBaseDefenseAiModule();
        var parameters = new ContentModuleParameter[]
        {
            new() { name = "unitId", value = "sample-native:skirmish-bike" },
            new() { name = "baseIndex", value = "0" },
            new() { name = "cooldownSeconds", value = "1" },
            new() { name = "defenseRadiusCells", value = "12" },
            new() { name = "maxResponders", value = "1" }
        };
        IRogueCommandAiRuntimeBehaviour behaviour = module.CreateBehaviour(
            new DefaultAiBehaviour { gameObject = new UnityEngine.GameObject("Reactive Test AI"), instanceId = 10 },
            parameters);
        behaviour.OnInitialized();
        behaviour.Tick();

        if (!defender.LastExecutedCommand.HasValue || closerNativeDefender.LastExecutedCommand.HasValue ||
            defender.LastExecutedCommand.Value.intent != "Reactive base defense" ||
            defender.LastExecutedCommand.Value.destination.x != attacker.Position.x ||
            defender.LastExecutedCommand.Value.destination.z != attacker.Position.z ||
            defender.LastCommandProcessingType != EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand ||
            !defender.CurrentCommandIsTaggedWith(defender.LastExecutedCommand.Value.tag))
        {
            throw new InvalidOperationException(
                "The in-range attack order was not dispatched before the defender's current command.");
        }

        ExistingControllers.Instance.Reset();
    }),
    ("reactive base-defense default Buildings mode ignores Harvester targets", () =>
    {
        ExistingControllers.Instance.Reset();
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            UnityEngine.Time.time = 10f;
            var harvester = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                Role = UnitRole.Harvester,
                aiBaseIndex = 0,
                CurrentCell = new UnityEngine.Vector2Int(10, 10),
                UniqueEntityId = 410
            };
            var attacker = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CurrentAttackTarget = harvester,
                CurrentCell = new UnityEngine.Vector2Int(11, 10),
                Position = new UnityEngine.Vector3(11f, 0f, 10f),
                UniqueEntityId = 411
            };
            var defender = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                IsCombatUnit = true,
                aiBaseIndex = 0,
                CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                CurrentCell = new UnityEngine.Vector2Int(12, 10),
                UniqueEntityId = 412
            };
            ExistingControllers.Instance.PlayerUnits().Add(attacker);
            ExistingControllers.Instance.AiUnits().Add(harvester);
            ExistingControllers.Instance.AiUnits().Add(defender);

            behaviour = new ReactiveBaseDefenseAiModule().CreateBehaviour(
                new DefaultAiBehaviour(), Array.Empty<ContentModuleParameter>());
            behaviour.OnInitialized();
            behaviour.Tick();
            if (defender.LastExecutedCommand.HasValue)
            {
                throw new InvalidOperationException("The default Buildings mode responded to an attack on an AI Harvester.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("reactive base-defense rejects out-of-radius attacks and other AI bases", () =>
    {
        ExistingControllers.Instance.Reset();
        UnityEngine.Time.time = 10f;

        var target = new EntityController
        {
            IsControlledByAi = true,
            IsBuilding = true,
            aiBaseIndex = 0,
            CurrentCell = new UnityEngine.Vector2Int(10, 10),
            UniqueEntityId = 101
        };
        var attacker = new EntityController
        {
            IsControlledByPlayer = true,
            IsUnit = true,
            CanAttackAndIsAttackEnabled = true,
            IsTargetInRange = false,
            CurrentAttackTarget = target,
            CurrentCell = new UnityEngine.Vector2Int(23, 10),
            UniqueEntityId = 201
        };
        var defender = new EntityController
        {
            IsControlledByAi = true,
            IsUnit = true,
            CanAttackAndIsAttackEnabled = true,
            CanMoveAndMovementIsEnabled = true,
            IsCombatUnit = true,
            aiBaseIndex = 0,
            CurrentCell = new UnityEngine.Vector2Int(10, 10),
            UniqueEntityId = 301
        };
        ExistingControllers.Instance.PlayerUnits().Add(attacker);
        ExistingControllers.Instance.AiUnits().Add(target);
        ExistingControllers.Instance.AiUnits().Add(defender);

        var module = new ReactiveBaseDefenseAiModule();
        IRogueCommandAiRuntimeBehaviour behaviour = module.CreateBehaviour(new DefaultAiBehaviour(), null);
        behaviour.OnInitialized();
        behaviour.Tick();
        if (defender.LastExecutedCommand.HasValue)
        {
            throw new InvalidOperationException("An attacker outside the defense radius triggered a response.");
        }

        target.aiBaseIndex = 1;
        attacker.CurrentCell = new UnityEngine.Vector2Int(12, 10);
        UnityEngine.Time.time = 20f;
        behaviour.Tick();
        if (defender.LastExecutedCommand.HasValue)
        {
            throw new InvalidOperationException("An attack on a different AI base triggered a response.");
        }

        ExistingControllers.Instance.Reset();
    }),
    ("reactive base-defense optionally protects live AI harvesters at the configured base", () =>
    {
        ExistingControllers.Instance.Reset();
        UnityEngine.Time.time = 10f;
        var harvester = new EntityController
        {
            IsControlledByAi = true,
            IsUnit = true,
            Role = UnitRole.Harvester,
            aiBaseIndex = 0,
            CurrentCell = new UnityEngine.Vector2Int(10, 10)
        };
        var wrongOwner = new EntityController
        {
            IsControlledByPlayer = true, IsUnit = true, Role = UnitRole.Harvester,
            aiBaseIndex = 0, CurrentCell = new UnityEngine.Vector2Int(10, 10)
        };
        var wrongBase = new EntityController
        {
            IsControlledByAi = true, IsUnit = true, Role = UnitRole.Harvester,
            aiBaseIndex = 1, CurrentCell = new UnityEngine.Vector2Int(10, 10)
        };
        var wrongRole = new EntityController
        {
            IsControlledByAi = true, IsUnit = true, Role = UnitRole.Robo,
            aiBaseIndex = 0, CurrentCell = new UnityEngine.Vector2Int(10, 10)
        };
        var dead = new EntityController
        {
            IsControlledByAi = true, IsUnit = true, Role = UnitRole.Harvester,
            aiBaseIndex = 0, IsDestroyed = true, CurrentCell = new UnityEngine.Vector2Int(10, 10)
        };
        var targets = new[] { wrongOwner, wrongBase, wrongRole, dead, harvester };
        foreach (EntityController target in targets)
        {
            var attacker = new EntityController
            {
                IsControlledByPlayer = true, IsUnit = true, CanAttackAndIsAttackEnabled = true,
                CurrentAttackTarget = target, CurrentCell = new UnityEngine.Vector2Int(11, 10),
                Position = target == harvester
                    ? new UnityEngine.Vector3(44f, 0f, 44f)
                    : new UnityEngine.Vector3(11f, 0f, 10f),
            };
            ExistingControllers.Instance.PlayerUnits().Add(attacker);
        }
        var defender = new EntityController
        {
            IsControlledByAi = true, IsUnit = true, CanAttackAndIsAttackEnabled = true,
            CanMoveAndMovementIsEnabled = true, IsCombatUnit = true, aiBaseIndex = 0,
            CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
            CurrentCell = new UnityEngine.Vector2Int(12, 10)
        };
        ExistingControllers.Instance.AiUnits().Add(defender);
        var module = new ReactiveBaseDefenseAiModule();
        IRogueCommandAiRuntimeBehaviour behaviour = module.CreateBehaviour(new DefaultAiBehaviour(),
        [new ContentModuleParameter { name = "protectedTargetType", value = "Harvesters" }]);
        behaviour.OnInitialized();
        behaviour.Tick();
        if (!defender.LastExecutedCommand.HasValue || defender.LastExecutedCommand.Value.destination.x != 44f ||
            defender.LastExecutedCommand.Value.destination.z != 44f)
        {
            throw new InvalidOperationException("The AI defender did not respond to its live base Harvester.");
        }
        ExistingControllers.Instance.Reset();
    }),
    ("reactive base-defense BuildingsAndHarvesters protects both target types", () =>
    {
        foreach (bool harvesterTarget in new[] { false, true })
        {
            ExistingControllers.Instance.Reset();
            IRogueCommandAiRuntimeBehaviour? behaviour = null;
            try
            {
                UnityEngine.Time.time = 10f;
                var protectedTarget = new EntityController
                {
                    IsControlledByAi = true,
                    IsBuilding = !harvesterTarget,
                    IsUnit = harvesterTarget,
                    Role = harvesterTarget ? UnitRole.Harvester : UnitRole.None,
                    aiBaseIndex = 0,
                    CurrentCell = new UnityEngine.Vector2Int(10, 10),
                    UniqueEntityId = harvesterTarget ? 422 : 420
                };
                var attacker = new EntityController
                {
                    IsControlledByPlayer = true,
                    IsUnit = true,
                    CanAttackAndIsAttackEnabled = true,
                    CurrentAttackTarget = protectedTarget,
                    CurrentCell = new UnityEngine.Vector2Int(11, 10),
                    Position = new UnityEngine.Vector3(harvesterTarget ? 44f : 40f, 0f, 10f),
                    UniqueEntityId = harvesterTarget ? 423 : 421
                };
                var defender = new EntityController
                {
                    IsControlledByAi = true,
                    IsUnit = true,
                    CanAttackAndIsAttackEnabled = true,
                    CanMoveAndMovementIsEnabled = true,
                    IsCombatUnit = true,
                    aiBaseIndex = 0,
                    CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                    CurrentCell = new UnityEngine.Vector2Int(12, 10),
                    UniqueEntityId = harvesterTarget ? 425 : 424
                };
                ExistingControllers.Instance.PlayerUnits().Add(attacker);
                ExistingControllers.Instance.AiUnits().Add(protectedTarget);
                ExistingControllers.Instance.AiUnits().Add(defender);

                behaviour = new ReactiveBaseDefenseAiModule().CreateBehaviour(
                    new DefaultAiBehaviour(),
                    [new ContentModuleParameter { name = "protectedTargetType", value = "BuildingsAndHarvesters" }]);
                behaviour.OnInitialized();
                behaviour.Tick();
                if (!defender.LastExecutedCommand.HasValue ||
                    defender.LastExecutedCommand.Value.destination.x != attacker.Position.x ||
                    defender.LastExecutedCommand.Value.destination.z != attacker.Position.z)
                {
                    throw new InvalidOperationException("The union target mode did not respond to an AI " +
                        (harvesterTarget ? "Harvester." : "building."));
                }
            }
            finally
            {
                behaviour?.OnDestroyed();
                ExistingControllers.Instance.Reset();
                DefaultAiBehaviour.ResetRuntimeState();
            }
        }
    }),
    ("scheduled base-build AI parser rejects unknown duplicate and null parameters", () =>
    {
        ContentModuleParameter[][] invalidInputs =
        [
            [new ContentModuleParameter { name = "mystery", value = "1" }],
            [new ContentModuleParameter { name = "baseIndex", value = "0" },
                new ContentModuleParameter { name = "baseIndex", value = "1" }],
            [null!],
            [new ContentModuleParameter { name = null, value = "1" }],
            [new ContentModuleParameter { name = "baseIndex", value = null }]
        ];
        for (int i = 0; i < invalidInputs.Length; i++)
        {
            if (ScheduledBaseBuildParameterParser.TryParse(invalidInputs[i], out _, out string diagnostic) ||
                String.IsNullOrWhiteSpace(diagnostic))
            {
                throw new InvalidOperationException("The scheduled AI parser accepted malformed parameter input " + i + ".");
            }
        }
    }),
    ("scheduled base-build AI parser rejects unsafe or oversized entity IDs", () =>
    {
        string[] invalidIds = ["", "unit with spaces", "../unit", "unit/path", new string('x', 129)];
        for (int i = 0; i < invalidIds.Length; i++)
        {
            ContentModuleParameter[] parameters =
                [new ContentModuleParameter { name = "entityId", value = invalidIds[i] }];
            if (ScheduledBaseBuildParameterParser.TryParse(parameters, out _, out string diagnostic) ||
                diagnostic == null || !diagnostic.Contains("entityId", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The scheduled AI parser accepted unsafe entity ID " + i + ".");
            }
        }
    }),
    ("scheduled base-build AI parser enforces integer bounds", () =>
    {
        string[] invalidValues = ["-1", "32", "1.5", "2147483648"];
        for (int i = 0; i < invalidValues.Length; i++)
        {
            ContentModuleParameter[] parameters =
                [new ContentModuleParameter { name = "baseIndex", value = invalidValues[i] }];
            if (ScheduledBaseBuildParameterParser.TryParse(parameters, out _, out string diagnostic) ||
                diagnostic == null || !diagnostic.Contains("baseIndex", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The scheduled AI parser accepted invalid baseIndex " + i + ".");
            }
        }
    }),
    ("scheduled base-build AI parser rejects non-finite and out-of-range cooldowns", () =>
    {
        string[] invalidValues = ["0.99", "600.01", "NaN", "Infinity", "-Infinity"];
        for (int i = 0; i < invalidValues.Length; i++)
        {
            ContentModuleParameter[] parameters =
                [new ContentModuleParameter { name = "intervalSeconds", value = invalidValues[i] }];
            if (ScheduledBaseBuildParameterParser.TryParse(parameters, out _, out string diagnostic) ||
                diagnostic == null || !diagnostic.Contains("intervalSeconds", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The scheduled AI parser accepted invalid interval " + i + ".");
            }
        }
    }),
    ("scheduled base-build AI parser applies invariant float and cross-field limits", () =>
    {
        ContentModuleParameter[] valid =
        [
            new ContentModuleParameter { name = "intervalSeconds", value = "1.5" },
            new ContentModuleParameter { name = "maxSuccessfulBuilds", value = "2" },
            new ContentModuleParameter { name = "maxAttempts", value = "2" }
        ];
        if (!ScheduledBaseBuildParameterParser.TryParse(valid, out ScheduledBaseBuildSettings parsed,
            out string diagnostic) || parsed.IntervalSeconds != 1.5f || parsed.MaximumSuccessfulBuilds != 2 ||
            parsed.MaximumAttempts != 2 || diagnostic != null)
        {
            throw new InvalidOperationException("A valid invariant-culture AI parameter set did not parse.");
        }

        ContentModuleParameter[] inconsistent =
        [
            new ContentModuleParameter { name = "maxSuccessfulBuilds", value = "3" },
            new ContentModuleParameter { name = "maxAttempts", value = "2" }
        ];
        if (ScheduledBaseBuildParameterParser.TryParse(inconsistent, out _, out diagnostic) ||
            diagnostic == null || !diagnostic.Contains("maxAttempts", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The scheduled AI parser accepted fewer attempts than successes.");
        }
    }),
    ("scheduled base-build AI parser enforces build-count limits", () =>
    {
        (string Name, string Value)[] invalidCounts =
        [
            ("maxSuccessfulBuilds", "0"),
            ("maxSuccessfulBuilds", "33"),
            ("maxAttempts", "0"),
            ("maxAttempts", "129")
        ];
        for (int i = 0; i < invalidCounts.Length; i++)
        {
            ContentModuleParameter[] parameters =
                [new ContentModuleParameter { name = invalidCounts[i].Name, value = invalidCounts[i].Value }];
            if (ScheduledBaseBuildParameterParser.TryParse(parameters, out _, out string diagnostic) ||
                diagnostic == null || !diagnostic.Contains(invalidCounts[i].Name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The scheduled AI parser accepted invalid build count " + i + ".");
            }
        }
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
    ("stage generation validation rejects AI outside its stage and base ranges", () =>
    {
        StageGenerationAiCandidate ai = ValidStageAi("late-ai");
        ai.StageMax = 3;
        StageGenerationWorldCandidate[] worlds = [ValidStageWorld("sample-world")];
        StageGenerationLandscapeCandidate[] landscapes = [ValidStageLandscape("native-map", 53)];

        if (StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                20, 60, false, false, 19, [ai], worlds, landscapes, out string diagnostic) ||
            diagnostic == null || !diagnostic.Contains("No hay AI", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An AI outside the new stage's index range was accepted.");
        }

        ai = ValidStageAi("late-base-ai");
        ai.Bases = [new StageGenerationAiBaseCandidate
        {
            StartingInStage = 5,
            StartingInAscensionLevel = 0
        }];
        if (StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                20, 60, false, false, 19, [ai], worlds, landscapes, out diagnostic) ||
            diagnostic == null || !diagnostic.Contains("No hay AI", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An AI whose bases start after the stage was accepted.");
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
    ("stage generation validation follows AI and World restricted map selection", () =>
    {
        StageGenerationAiCandidate ai = ValidStageAi("restricted-ai");
        StageGenerationWorldCandidate world = ValidStageWorld("restricted-world");
        ai.RestrictedMaps = [ValidStageLandscape("ai-map", 53)];
        world.RestrictedMaps = [ValidStageLandscape("world-map", 81)];
        StageGenerationLandscapeCandidate[] landscapes = [ValidStageLandscape("default-map", 101)];

        if (!StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                53, 53, false, false, 19, [ai], [world], landscapes, out string diagnostic))
        {
            throw new InvalidOperationException("AI restricted maps should take precedence: " + diagnostic);
        }

        if (StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                81, 81, false, false, 19, [ai], [world], landscapes, out diagnostic) ||
            diagnostic == null || !diagnostic.Contains("LandscapeGenerator", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A stage ignored the selected AI's map restriction.");
        }

        ai.RestrictedMaps = [];
        if (!StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                81, 81, false, false, 19, [ai], [world], landscapes, out diagnostic))
        {
            throw new InvalidOperationException("World restricted maps should be selected when AI has no map restriction: " +
                diagnostic);
        }

        ai.RestrictedMaps = [ValidStageLandscape("usable-ai-map", 53), new StageGenerationLandscapeCandidate
        {
            Id = "disabled-ai-map",
            MapSize = 53,
            IsDeactivated = true,
            IsAllowedForDemo = true,
            RestrictedWorlds = []
        }];
        if (StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                53, 53, false, false, 19, [ai], [world], landscapes, out diagnostic))
        {
            throw new InvalidOperationException("An explicit AI map restriction accepted a selectable deactivated map.");
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
    ("later pack AI compatibility is checked against already loaded stages", () =>
    {
        StageGenerationAiCandidate firstPackAi = ValidStageAi("pack-a:ai");
        StageGenerationAiCandidate secondPackAi = ValidStageAi("pack-b:ai");
        StageGenerationLandscapeCandidate firstPackMap = ValidStageLandscape("pack-a:map", 53);
        StageGenerationLandscapeCandidate secondPackMap = ValidStageLandscape("pack-b:map", 81);
        firstPackAi.RestrictedMaps = [firstPackMap];
        secondPackAi.RestrictedMaps = [secondPackMap];
        StageGenerationStageCandidate[] stages =
        [
            new StageGenerationStageCandidate
            {
                Id = "pack-a:stage",
                StageIndex = 4,
                MapSizeMin = 53,
                MapSizeMax = 53,
                IsPartOfDemo = true,
                Worlds = [ValidStageWorld("pack-a:world")]
            },
            new StageGenerationStageCandidate
            {
                Id = "pack-b:stage",
                StageIndex = 5,
                MapSizeMin = 81,
                MapSizeMax = 81,
                IsPartOfDemo = true,
                Worlds = [ValidStageWorld("pack-b:world")]
            }
        ];

        if (StageGenerationCompatibility.TryValidateStages("default", false, false, 10,
                stages, [firstPackAi, secondPackAi], [firstPackMap, secondPackMap], out string diagnostic) ||
            diagnostic == null || !diagnostic.Contains("pack-a:stage", StringComparison.Ordinal) ||
            !diagnostic.Contains("pack-b:ai", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A later pack AI that breaks an earlier stage was not rejected precisely.");
        }
    }),
    ("stage generation honors Heat AI ascension bypass", () =>
    {
        StageGenerationAiCandidate ai = ValidStageAi("heat-ai");
        ai.AscensionMin = 10;
        ai.AscensionMax = 20;
        ai.Bases[0].StartingInAscensionLevel = 3;
        StageGenerationWorldCandidate[] worlds = [ValidStageWorld("heat-world")];
        StageGenerationLandscapeCandidate[] landscapes = [ValidStageLandscape("heat-map", 53)];

        if (StageGenerationCompatibility.TryValidate("heat:stage", "default", 4,
                20, 60, false, false, 3, [ai], worlds, landscapes, out string diagnostic) ||
            diagnostic == null || !diagnostic.Contains("No hay AI", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A normal progression ignored the AI ascension range.");
        }

        if (!StageGenerationCompatibility.TryValidate("heat:stage", "heat", 4,
                20, 60, false, true, 3, [ai], worlds, landscapes, out diagnostic))
        {
            throw new InvalidOperationException("Heat should bypass the AI range and validate at the reachable ascension cap: " +
                diagnostic);
        }

        ai.Bases[0].StartingInAscensionLevel = 4;
        if (StageGenerationCompatibility.TryValidate("heat:stage", "heat", 4,
                20, 60, false, true, 3, [ai], worlds, landscapes, out diagnostic) ||
            diagnostic == null || !diagnostic.Contains("No hay AI", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Heat validation accepted an AI base that starts beyond the ascension cap.");
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
    ("AI module registry validates and creates only registered versioned modules", () =>
    {
        var registry = new AiModuleRegistry();
        if (registry.TryValidate("missing:module", Array.Empty<ContentModuleParameter>(), out string diagnostic) ||
            diagnostic == null || !diagnostic.Contains("Unknown compiled AI module", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The real AI module registry accepted an unknown module ID.");
        }

        var createdBehaviour = new RecordingAiRuntimeBehaviour(new List<string>(), "registered");
        var module = new TestAiModule { Id = "test:scheduled", Behaviour = createdBehaviour };
        registry.Register(module);
        ContentModuleParameter[] parameters = [new ContentModuleParameter { name = "cooldownSeconds", value = "8" }];
        var gameBehaviour = new DefaultAiBehaviour();
        if (!registry.TryValidate(module.Id, parameters, out diagnostic) || diagnostic != null ||
            !registry.TryCreateBehaviour(module.Id, gameBehaviour, parameters,
                out IRogueCommandAiRuntimeBehaviour result, out diagnostic) ||
            diagnostic != null || !ReferenceEquals(result, createdBehaviour) ||
            !ReferenceEquals(module.LastGameBehaviour, gameBehaviour) ||
            !ReferenceEquals(module.LastParameters, parameters))
        {
            throw new InvalidOperationException("The real AI module registry did not validate and bind its module instance.");
        }

        var throwingModule = new TestAiModule
        {
            Id = "test:throws",
            ThrowOnValidation = true,
            Behaviour = createdBehaviour
        };
        registry.Register(throwingModule);
        if (registry.TryValidate(throwingModule.Id, parameters, out diagnostic) ||
            diagnostic == null || !diagnostic.Contains("Injected AI parameter validation failure", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The real AI module registry did not contain a validator exception.");
        }

        try
        {
            registry.Register(new TestAiModule { Id = "test:scheduled" });
            throw new InvalidOperationException("The real AI module registry accepted a duplicate module ID.");
        }
        catch (InvalidOperationException exception)
        {
            if (!exception.Message.Contains("already registered", StringComparison.Ordinal))
            {
                throw;
            }
        }
    }),
    ("composite AI lifecycle dispatches once in order and cleans up in reverse", () =>
    {
        var events = new List<string>();
        var first = new RecordingAiRuntimeBehaviour(events, "first");
        var second = new RecordingAiRuntimeBehaviour(events, "second");
        var composite = new CompositeAiRuntimeBehaviour("test:profile",
            new[] { "test:first", "test:second" }, new IRogueCommandAiRuntimeBehaviour[] { first, second });

        composite.OnInitialized();
        composite.OnInitialized();
        composite.Tick();
        composite.Tick();
        composite.OnDestroyed();
        composite.OnDestroyed();
        composite.Tick();

        string[] expected =
        [
            "first:init", "second:init", "first:tick", "second:tick", "first:tick", "second:tick",
            "second:destroy", "first:destroy"
        ];
        if (!events.SequenceEqual(expected))
        {
            throw new InvalidOperationException("The composite AI lifecycle changed callback order or repeated a terminal callback.");
        }
    }),
    ("composite AI initialization failure preserves cleanup failures and cleans every module in reverse", () =>
    {
        var events = new List<string>();
        var first = new RecordingAiRuntimeBehaviour(events, "first");
        var failing = new RecordingAiRuntimeBehaviour(events, "failing")
        {
            ThrowOnInitialize = true,
            ThrowOnDestroy = true
        };
        var notInitialized = new RecordingAiRuntimeBehaviour(events, "not-initialized") { ThrowOnDestroy = true };
        var composite = new CompositeAiRuntimeBehaviour("test:profile",
            new[] { "test:first", "test:failing", "test:last" },
            new IRogueCommandAiRuntimeBehaviour[] { first, failing, notInitialized });

        try
        {
            composite.OnInitialized();
            throw new InvalidOperationException("A failing AI module did not abort composite initialization.");
        }
        catch (AggregateException exception)
        {
            string details = exception.ToString();
            if (!details.Contains("test:failing", StringComparison.Ordinal) ||
                !details.Contains("test:last", StringComparison.Ordinal) ||
                !details.Contains("Injected initialization failure", StringComparison.Ordinal) ||
                !details.Contains("Injected cleanup failure", StringComparison.Ordinal))
            {
                throw;
            }
        }

        composite.Tick();
        composite.OnDestroyed();
        string[] expected =
        [
            "first:init", "failing:init", "not-initialized:destroy", "failing:destroy", "first:destroy"
        ];
        if (!events.SequenceEqual(expected))
        {
            throw new InvalidOperationException("Initialization failure did not clean every created module in reverse order.");
        }
    }),
    ("composite AI destruction reports all cleanup failures and continues in reverse", () =>
    {
        var events = new List<string>();
        var first = new RecordingAiRuntimeBehaviour(events, "first") { ThrowOnDestroy = true };
        var second = new RecordingAiRuntimeBehaviour(events, "second");
        var third = new RecordingAiRuntimeBehaviour(events, "third") { ThrowOnDestroy = true };
        var composite = new CompositeAiRuntimeBehaviour("test:profile",
            new[] { "test:first", "test:second", "test:third" },
            new IRogueCommandAiRuntimeBehaviour[] { first, second, third });
        composite.OnInitialized();

        try
        {
            composite.OnDestroyed();
            throw new InvalidOperationException("A failing AI cleanup did not report its errors.");
        }
        catch (AggregateException exception)
        {
            string details = exception.ToString();
            if (!details.Contains("test:first", StringComparison.Ordinal) ||
                !details.Contains("test:third", StringComparison.Ordinal) ||
                !details.Contains("Injected cleanup failure", StringComparison.Ordinal))
            {
                throw;
            }
        }

        composite.OnDestroyed();
        string[] expected =
        [
            "first:init", "second:init", "third:init", "third:destroy", "second:destroy", "first:destroy"
        ];
        if (!events.SequenceEqual(expected))
        {
            throw new InvalidOperationException("Destruction failure repeated callbacks or skipped reverse cleanup.");
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
    ("all native sample production pairs append without replacing the native factory mapping", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            var originals = new[]
            {
                RuntimeMutatorTestData.Row("NativeUnit"),
                RuntimeMutatorTestData.Row("NativeFactory", "NativeUnit")
            };
            for (int i = 0; i < originals.Length; i++)
            {
                EntityBalancingStore.EntityBalancingParametersList.Add(originals[i]);
                EntityBalancingStore.ParameterListIndexOf.Add(originals[i].entityId, i);
                EntityBalancingStore.ChangeableIntValueCache.Add(originals[i].entityId, new());
                EntityBalancingStore.ChangeableFloatValueCache.Add(originals[i].entityId, new());
            }
            EntityBalancingStore.FactoryEntityIdOf.Add("NativeUnit", "NativeFactory");
            var provider = new RuntimeMutatorTestProvider();
            foreach (ContentUnitDefinition unit in nativeSample.units)
            {
                provider.EntityRows.Add(RuntimeMutatorTestData.Row(unit.id));
                provider.EntityRows.Add(RuntimeMutatorTestData.Row(unit.productionBuilding.id, unit.id));
            }
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);
            bool firstRegistration = mutator.RegisterEntityRows();
            bool secondRegistration = mutator.RegisterEntityRows();
            int expectedRowCount = originals.Length + 2 * nativeSample.units.Length;
            int expectedFactoryCount = 1 + nativeSample.units.Length;
            if (!firstRegistration || !secondRegistration ||
                EntityBalancingStore.EntityBalancingParametersList.Count != expectedRowCount ||
                EntityBalancingStore.FactoryEntityIdOf.Count != expectedFactoryCount ||
                EntityBalancingStore.FactoryEntityIdOf["NativeUnit"] != "NativeFactory")
            {
                throw new InvalidOperationException(
                    "Production registration state was inconsistent. first=" + firstRegistration +
                    ", second=" + secondRegistration +
                    ", rows=" + EntityBalancingStore.EntityBalancingParametersList.Count + "/" + expectedRowCount +
                    ", factories=" + EntityBalancingStore.FactoryEntityIdOf.Count + "/" + expectedFactoryCount +
                    ", nativeFactory=" + EntityBalancingStore.FactoryEntityIdOf["NativeUnit"] + ".");
            }
            foreach (ContentUnitDefinition unit in nativeSample.units)
                if (EntityBalancingStore.FactoryEntityIdOf[unit.id] != unit.productionBuilding.id)
                    throw new InvalidOperationException("A new production building points at the wrong product.");
        }
        finally { EntityBalancingStore.Reset(); }
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
    ("rejected balance-row registration leaves provider-owned source rows unchanged", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            EntityBalancingParameters existingRow = RuntimeMutatorTestData.Row("sample:existing");
            existingRow.moveSpeed = 3f;
            EntityBalancingStore.EntityBalancingParametersList.Add(existingRow);
            EntityBalancingStore.ParameterListIndexOf.Add("sample:existing", 0);

            var provider = new RuntimeMutatorTestProvider();
            EntityBalancingParameters sourceRow = RuntimeMutatorTestData.Row("sample:existing");
            sourceRow.moveSpeed = 11f;
            provider.EntityRows.Add(sourceRow);
            provider.BalanceOverrides.Add(new BalanceOverride
            {
                EntityId = "sample:existing",
                Parameters = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["MoveSpeed"] = 14.5d
                }
            });
            var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);

            bool registered = mutator.RegisterEntityRows();
            EntityBalancingParameters providerRow = (EntityBalancingParameters)provider.EntityRows[0];
            EntityBalancingParameters registeredRow = EntityBalancingStore.EntityBalancingParametersList[0];
            if (registered || providerRow.moveSpeed != 11f || registeredRow.moveSpeed != 3f ||
                EntityBalancingStore.EntityBalancingParametersList.Count != 1 ||
                EntityBalancingStore.ParameterListIndexOf.Count != 1 ||
                EntityBalancingStore.FactoryEntityIdOf.Count != 0 ||
                provider.EntityRowsRegisteredCount != 0)
            {
                throw new InvalidOperationException("A rejected row registration mutated provider-owned balance data or committed registry changes.");
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
    ("runtime progression mutator adds stage and AI references once to every profile", () =>
    {
        var provider = new RuntimeMutatorTestProvider();
        var stage = new Stage();
        var ai = new DefaultAiBehaviour();
        provider.Contributions.Add(new ProgressionContentContribution(
            "sample:progression",
            new object[] { stage },
            new object[] { ai }));
        var mutator = new RuntimeContentMutator(typeof(EntityBalancingStore).Assembly, provider, null);
        var balancing = new GameBalancingScriptableObject();

        mutator.ApplyProgressionFrom(balancing);
        mutator.ApplyCachedProgression();

        LevelProgressionParametersScriptableObject[] profiles =
        [
            balancing.levelProgressionParameters,
            balancing.levelProgressionParametersHeat,
            balancing.levelProgressionParametersFirstRun
        ];
        for (int i = 0; i < profiles.Length; i++)
        {
            if (profiles[i].stages.Count != 1 || !ReferenceEquals(profiles[i].stages[0], stage) ||
                profiles[i].defaultAis.Count != 1 || !ReferenceEquals(profiles[i].defaultAis[0], ai))
            {
                throw new InvalidOperationException("Progression profile " + i + " did not receive exactly one stage and AI reference.");
            }
        }
    }),
    ("duplicate IDs are rejected before registry changes", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.stages[0].id = pack.units[0].id;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "id.duplicate-in-package");
        });
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
    ("unknown compiled AI modules are rejected", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.aiProfiles[0].modules[0].moduleId = "sample:unknown-module";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "module.invalid");
        });
    }),
    ("missing AssetBundle files are rejected", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            File.Delete(Path.Combine(root, pack.assetBundles[0].path.Replace('/', Path.DirectorySeparatorChar)));
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "bundle.file-missing");
        });
    }),
    ("traversal paths are rejected", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.assetBundles[0].path = "bundles/../outside.bundle";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "path.invalid");
        });
    }),
    ("reparse-point bundle paths are rejected", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            string linkPath = Path.Combine(root, "bundles", "linked-content.bundle");
            string targetPath = Path.Combine(root, "bundles", "test-content-0.bundle");
            try
            {
                File.CreateSymbolicLink(linkPath, targetPath);
            }
            catch (Exception exception) when (exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is PlatformNotSupportedException)
            {
                if (File.Exists(linkPath))
                {
                    File.Delete(linkPath);
                }

                throw new SkipTestException("This Windows environment does not permit file symlinks: " + exception.Message);
            }

            try
            {
                pack.assetBundles[0].path = "bundles/linked-content.bundle";
                ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "path.reparse-point");
            }
            finally
            {
                if (File.Exists(linkPath))
                {
                    File.Delete(linkPath);
                }
            }
        });
    }),
    ("IDs colliding with the existing registry are rejected", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            var registry = new EmptySnapshot("sample:scout-tank");
            ExpectDiagnostic(pack, root, registry, new KnownModuleValidator(), "id.collision");
        });
    }),
    ("Sandlance Scout declares starting blueprint eligibility and AI allowed flag", () =>
    {
        ContentUnitDefinition sandlance = nativeSample.units.Single(unit => unit.id == "sample-native:scout-tank");
        if (!sandlance.isAllowedAsStartingBlueprint)
        {
            throw new InvalidOperationException("Sandlance Scout should be marked as allowed as starting blueprint.");
        }
        if (!sandlance.isAllowedForAi)
        {
            throw new InvalidOperationException("Sandlance Scout should be marked as allowed for AI.");
        }
    }),
    ("Skirmish Strike Bike declares isAllowedForAi", () =>
    {
        ContentUnitDefinition bike = nativeSample.units.Single(unit => unit.id == "sample-native:skirmish-bike");
        if (!bike.isAllowedForAi)
        {
            throw new InvalidOperationException("Skirmish Strike Bike should be marked as allowed for AI.");
        }
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
    ("balance overrides accept rarity enum and reject invalid enum values", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            pack.units[0].balanceOverrides =
            [
                new ContentBalanceOverride { name = "rarity", value = "Rare" },
                new ContentBalanceOverride { name = "neededExperienceLevel", value = "0" },
                new ContentBalanceOverride { name = "roles", value = "Unit, Vehicle, Scout" },
                new ContentBalanceOverride { name = "offeredSystemTags", value = "Vehicle, Turbo" }
            ];
            pack.units[0].productionBuilding.balanceOverrides =
            [
                new ContentBalanceOverride { name = "rarity", value = "Rare" },
                new ContentBalanceOverride { name = "neededExperienceLevel", value = "0" },
                new ContentBalanceOverride { name = "roles", value = "Building, Factory" },
                new ContentBalanceOverride { name = "offeredSystemTags", value = "None" }
            ];
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());

            pack.units[0].balanceOverrides =
            [
                new ContentBalanceOverride { name = "rarity", value = "Mythic" },
                new ContentBalanceOverride { name = "neededExperienceLevel", value = "0" }
            ];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.balance-override-invalid");
        });
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
    ("production building role overrides preserve native factory classification", () =>
    {
        if (!BalanceOverrideFlags.TryParse("roles", "Building, Factory", false, true,
                out HashSet<string> roles, out string diagnostic) || diagnostic != null ||
            !roles.SetEquals(new[] { "Building", "Factory" }) ||
            BalanceOverrideFlags.TryParse("roles", "Building", false, true, out _, out diagnostic) ||
            diagnostic == null ||
            BalanceOverrideFlags.TryParse("roles", "Unit, Mech", false, false, out _, out diagnostic) ||
            diagnostic == null)
        {
            throw new InvalidOperationException("Factory role overrides must retain both native factory flags and use the factory-row context.");
        }
    }),
    ("balance overrides reject unknown and factory role flags on a unit product", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            pack.units[0].balanceOverrides = pack.units[0].balanceOverrides
                .Where(entry => entry.name != "roles")
                .Append(new ContentBalanceOverride { name = "roles", value = "Unit, Factory" })
                .ToArray();
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.balance-override-invalid");
        });
        WithPackage(nativeSample, (pack, root) =>
        {
            pack.units[0].balanceOverrides = pack.units[0].balanceOverrides
                .Where(entry => entry.name != "roles")
                .Append(new ContentBalanceOverride { name = "roles", value = "Unit, All" })
                .ToArray();
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.balance-override-invalid");
        });
        WithPackage(nativeSample, (pack, root) =>
        {
            pack.units[0].balanceOverrides = pack.units[0].balanceOverrides
                .Where(entry => entry.name != "offeredSystemTags")
                .Append(new ContentBalanceOverride { name = "offeredSystemTags", value = "Armor, NotAGameTag" })
                .ToArray();
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.balance-override-invalid");
        });
    }),
    ("harvester-raider AI parameter parser supplies bounded defaults and validates limits", () =>
    {
        if (!HarvesterRaiderParameterParser.TryParse(null, out HarvesterRaiderSettings defaults,
            out string diagnostic) || diagnostic != null || defaults.BaseIndex != 0 ||
            defaults.ScanIntervalSeconds != 10f || defaults.CooldownSeconds != 25f ||
            defaults.RaidSquadSize != 3 || defaults.MinGameTimeSeconds != 15f)
        {
            throw new InvalidOperationException("The harvester-raider AI parser did not return its documented defaults.");
        }

        ContentModuleParameter[] boundaries =
        [
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "scanIntervalSeconds", value = "120" },
            new ContentModuleParameter { name = "cooldownSeconds", value = "300" },
            new ContentModuleParameter { name = "raidSquadSize", value = "8" },
            new ContentModuleParameter { name = "minGameTimeSeconds", value = "600" }
        ];
        if (!HarvesterRaiderParameterParser.TryParse(boundaries, out HarvesterRaiderSettings maximums,
            out diagnostic) || diagnostic != null || maximums.BaseIndex != 31 ||
            maximums.ScanIntervalSeconds != 120f || maximums.CooldownSeconds != 300f ||
            maximums.RaidSquadSize != 8 || maximums.MinGameTimeSeconds != 600f)
        {
            throw new InvalidOperationException("The harvester-raider AI parser rejected valid upper-bound values.");
        }
    }),
    ("harvester-raider AI parameter parser rejects invalid values and unknown parameters", () =>
    {
        ContentModuleParameter[][] invalidInputs =
        [
            [new ContentModuleParameter { name = "unknownParam", value = "1" }],
            [new ContentModuleParameter { name = "baseIndex", value = "0" }, new ContentModuleParameter { name = "baseIndex", value = "1" }],
            [null!],
            [new ContentModuleParameter { name = "baseIndex", value = "32" }],
            [new ContentModuleParameter { name = "raidSquadSize", value = "9" }],
            [new ContentModuleParameter { name = "raidSquadSize", value = "0" }],
            [new ContentModuleParameter { name = "scanIntervalSeconds", value = "1.5" }],
            [new ContentModuleParameter { name = "cooldownSeconds", value = "NaN" }],
            [new ContentModuleParameter { name = "minGameTimeSeconds", value = "Infinity" }]
        ];

        for (int i = 0; i < invalidInputs.Length; i++)
        {
            if (HarvesterRaiderParameterParser.TryParse(invalidInputs[i], out _, out string diagnostic) ||
                diagnostic == null)
            {
                throw new InvalidOperationException("The harvester-raider AI parser accepted invalid input " + i + ".");
            }
        }
    }),
    ("Sandlance Scout declares demo and specialist flags", () =>
    {
        ContentUnitDefinition sandlance = nativeSample.units.Single(unit => unit.id == "sample-native:scout-tank");
        if (!sandlance.isAllowedForDemo)
        {
            throw new InvalidOperationException("Sandlance Scout should be marked as allowed for demo.");
        }
        if (sandlance.isForSpecialists)
        {
            throw new InvalidOperationException("Sandlance Scout should not be restricted to specialists.");
        }
    }),
    ("Skirmish Strike Bike declares demo and specialist flags", () =>
    {
        ContentUnitDefinition bike = nativeSample.units.Single(unit => unit.id == "sample-native:skirmish-bike");
        if (bike.isAllowedForDemo)
        {
            throw new InvalidOperationException("Skirmish Strike Bike should not be allowed for demo.");
        }
        if (!bike.isForSpecialists)
        {
            throw new InvalidOperationException("Skirmish Strike Bike should be restricted to specialists.");
        }
    }),
    ("balance overrides accept attackType and skillType enums and reject invalid values", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            pack.units[0].balanceOverrides =
            [
                new ContentBalanceOverride { name = "attackType", value = "Single" },
                new ContentBalanceOverride { name = "skillType", value = "LocationAoE" },
                new ContentBalanceOverride { name = "neededExperienceLevel", value = "0" },
                new ContentBalanceOverride { name = "rarity", value = "Common" }
            ];
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());

            pack.units[0].balanceOverrides =
            [
                new ContentBalanceOverride { name = "attackType", value = "Nuclear" },
                new ContentBalanceOverride { name = "neededExperienceLevel", value = "0" },
                new ContentBalanceOverride { name = "rarity", value = "Common" }
            ];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.balance-override-invalid");

            pack.units[0].balanceOverrides =
            [
                new ContentBalanceOverride { name = "skillType", value = "InvalidSkill" },
                new ContentBalanceOverride { name = "neededExperienceLevel", value = "0" },
                new ContentBalanceOverride { name = "rarity", value = "Common" }
            ];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit.balance-override-invalid");
        });
    }),
    ("siege-assault AI parameter parser supplies bounded defaults and validates limits", () =>
    {
        if (!SiegeAssaultParameterParser.TryParse(null, out SiegeAssaultSettings defaults,
            out string diagnostic) || diagnostic != null || defaults.BaseIndex != 0 ||
            defaults.CooldownSeconds != 40f || defaults.MinSquadSize != 4 ||
            defaults.TargetPriority != "ClosestBuilding" || defaults.MinGameTimeSeconds != 30f)
        {
            throw new InvalidOperationException("The siege-assault AI parser did not return its documented defaults.");
        }

        ContentModuleParameter[] boundaries =
        [
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "cooldownSeconds", value = "300" },
            new ContentModuleParameter { name = "minSquadSize", value = "12" },
            new ContentModuleParameter { name = "targetPriority", value = "Hq" },
            new ContentModuleParameter { name = "minGameTimeSeconds", value = "600" }
        ];
        if (!SiegeAssaultParameterParser.TryParse(boundaries, out SiegeAssaultSettings maximums,
            out diagnostic) || diagnostic != null || maximums.BaseIndex != 31 ||
            maximums.CooldownSeconds != 300f || maximums.MinSquadSize != 12 ||
            maximums.TargetPriority != "Hq" || maximums.MinGameTimeSeconds != 600f)
        {
            throw new InvalidOperationException("The siege-assault AI parser rejected valid upper-bound values.");
        }
    }),
    ("siege-assault AI parameter parser rejects invalid values and unknown parameters", () =>
    {
        ContentModuleParameter[][] invalidInputs =
        [
            [new ContentModuleParameter { name = "unknownParam", value = "1" }],
            [new ContentModuleParameter { name = "baseIndex", value = "0" }, new ContentModuleParameter { name = "baseIndex", value = "1" }],
            [null!],
            [new ContentModuleParameter { name = "baseIndex", value = "32" }],
            [new ContentModuleParameter { name = "minSquadSize", value = "0" }],
            [new ContentModuleParameter { name = "minSquadSize", value = "13" }],
            [new ContentModuleParameter { name = "targetPriority", value = "InvalidTarget" }],
            [new ContentModuleParameter { name = "cooldownSeconds", value = "9" }],
            [new ContentModuleParameter { name = "minGameTimeSeconds", value = "-1" }]
        ];

        for (int i = 0; i < invalidInputs.Length; i++)
        {
            if (SiegeAssaultParameterParser.TryParse(invalidInputs[i], out _, out string diagnostic) ||
                diagnostic == null)
            {
                throw new InvalidOperationException("The siege-assault AI parser accepted invalid input " + i + ".");
            }
        }
    }),
    ("retreat-and-regroup AI parameter parser supplies bounded defaults and validates limits", () =>
    {
        if (!RetreatAndRegroupParameterParser.TryParse(null, out RetreatAndRegroupSettings defaults,
            out string diagnostic) || diagnostic != null || defaults.BaseIndex != 0 ||
            defaults.HealthThresholdPercent != 30 || defaults.RegroupRadiusCells != 8 ||
            defaults.CooldownSeconds != 20f)
        {
            throw new InvalidOperationException("The retreat-and-regroup AI parser did not return its documented defaults.");
        }

        ContentModuleParameter[] boundaries =
        [
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "healthThresholdPercent", value = "99" },
            new ContentModuleParameter { name = "regroupRadiusCells", value = "32" },
            new ContentModuleParameter { name = "cooldownSeconds", value = "300" }
        ];
        if (!RetreatAndRegroupParameterParser.TryParse(boundaries, out RetreatAndRegroupSettings maximums,
            out diagnostic) || diagnostic != null || maximums.BaseIndex != 31 ||
            maximums.HealthThresholdPercent != 99 || maximums.RegroupRadiusCells != 32 ||
            maximums.CooldownSeconds != 300f)
        {
            throw new InvalidOperationException("The retreat-and-regroup AI parser rejected valid upper-bound values.");
        }
    }),
    ("retreat-and-regroup AI parameter parser rejects invalid values and unknown parameters", () =>
    {
        ContentModuleParameter[][] invalidInputs =
        [
            [new ContentModuleParameter { name = "unknownParam", value = "1" }],
            [new ContentModuleParameter { name = "baseIndex", value = "0" }, new ContentModuleParameter { name = "baseIndex", value = "1" }],
            [null!],
            [new ContentModuleParameter { name = "baseIndex", value = "32" }],
            [new ContentModuleParameter { name = "healthThresholdPercent", value = "0" }],
            [new ContentModuleParameter { name = "healthThresholdPercent", value = "100" }],
            [new ContentModuleParameter { name = "regroupRadiusCells", value = "0" }],
            [new ContentModuleParameter { name = "regroupRadiusCells", value = "33" }],
            [new ContentModuleParameter { name = "cooldownSeconds", value = "4" }],
            [new ContentModuleParameter { name = "cooldownSeconds", value = "301" }]
        ];

        for (int i = 0; i < invalidInputs.Length; i++)
        {
            if (RetreatAndRegroupParameterParser.TryParse(invalidInputs[i], out _, out string diagnostic) ||
                diagnostic == null)
            {
                throw new InvalidOperationException("The retreat-and-regroup AI parser accepted invalid input " + i + ".");
            }
        }
    }),
    ("harvester-escort AI parser supplies bounded defaults", () =>
    {
        if (!HarvesterEscortParameterParser.TryParse(null, out HarvesterEscortSettings defaults,
            out string diagnostic) || diagnostic != null || defaults.BaseIndex != 0 ||
            defaults.EscortRadiusCells != 8 || defaults.ScanIntervalSeconds != 5f ||
            defaults.MaxEscorts != 3 || !defaults.EngageAttackers)
        {
            throw new InvalidOperationException("The harvester-escort AI parser did not return its documented defaults.");
        }

        ContentModuleParameter[] boundaryValues =
        [
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "escortRadiusCells", value = "64" },
            new ContentModuleParameter { name = "scanIntervalSeconds", value = "60" },
            new ContentModuleParameter { name = "maxEscorts", value = "16" },
            new ContentModuleParameter { name = "engageAttackers", value = "false" }
        ];
        if (!HarvesterEscortParameterParser.TryParse(boundaryValues, out HarvesterEscortSettings maximums,
            out diagnostic) || maximums.BaseIndex != 31 || maximums.EscortRadiusCells != 64 ||
            maximums.ScanIntervalSeconds != 60f || maximums.MaxEscorts != 16 || maximums.EngageAttackers)
        {
            throw new InvalidOperationException("The harvester-escort AI parser rejected valid upper-bound values.");
        }
    }),
    ("harvester-escort AI parser rejects invalid and duplicate parameters", () =>
    {
        string[] invalidInputs =
        [
            "baseIndex=32",
            "escortRadiusCells=0",
            "scanIntervalSeconds=0",
            "maxEscorts=0",
            "engageAttackers=maybe",
            "unknown=value"
        ];

        for (int i = 0; i < invalidInputs.Length; i++)
        {
            string[] parts = invalidInputs[i].Split('=');
            ContentModuleParameter[] parameters =
            [
                new ContentModuleParameter { name = parts[0], value = parts[1] }
            ];

            if (HarvesterEscortParameterParser.TryParse(parameters, out _, out _))
            {
                throw new InvalidOperationException("The harvester-escort AI parser accepted invalid input " + i + ".");
            }
        }

        ContentModuleParameter[] duplicate =
        [
            new ContentModuleParameter { name = "baseIndex", value = "0" },
            new ContentModuleParameter { name = "baseIndex", value = "1" }
        ];
        if (HarvesterEscortParameterParser.TryParse(duplicate, out _, out _))
        {
            throw new InvalidOperationException("The harvester-escort AI parser accepted a duplicate parameter.");
        }
    }),
    ("patrol-and-intercept AI parser applies documented defaults and accepts upper bounds", () =>
    {
        if (!PatrolAndInterceptParameterParser.TryParse(null, out PatrolAndInterceptSettings defaults,
                out string diagnostic) || diagnostic != null || defaults.BaseIndex != 0 ||
            defaults.PatrolRadiusCells != 15 || defaults.InterceptRadiusCells != 10 ||
            defaults.ScanIntervalSeconds != 8f || defaults.MaxPatrolUnits != 3 ||
            defaults.PatrolMode != "AttackMove")
        {
            throw new InvalidOperationException("Patrol-and-intercept defaults do not match the content contract.");
        }

        ContentModuleParameter[] upperBounds =
        [
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "patrolRadiusCells", value = "32" },
            new ContentModuleParameter { name = "interceptRadiusCells", value = "32" },
            new ContentModuleParameter { name = "scanIntervalSeconds", value = "60" },
            new ContentModuleParameter { name = "maxPatrolUnits", value = "8" }
        ];
        if (!PatrolAndInterceptParameterParser.TryParse(upperBounds,
                out PatrolAndInterceptSettings maximums, out diagnostic) || diagnostic != null ||
            maximums.BaseIndex != 31 || maximums.PatrolRadiusCells != 32 ||
            maximums.InterceptRadiusCells != 32 || maximums.ScanIntervalSeconds != 60f ||
            maximums.MaxPatrolUnits != 8)
        {
            throw new InvalidOperationException("Patrol-and-intercept rejected a documented upper bound.");
        }
    }),
    ("patrol-and-intercept parser accepts both supported patrol modes", () =>
    {
        foreach (string mode in new[] { "AttackMove", "Native" })
        {
            ContentModuleParameter[] parameters =
            [new ContentModuleParameter { name = "patrolMode", value = mode }];
            if (!PatrolAndInterceptParameterParser.TryParse(parameters,
                    out PatrolAndInterceptSettings settings, out string diagnostic) ||
                diagnostic != null || settings.PatrolMode != mode)
            {
                throw new InvalidOperationException("Patrol-and-intercept rejected supported mode " + mode + ".");
            }
        }
    }),
    ("patrol-and-intercept parser rejects invalid or duplicate patrol modes", () =>
    {
        ContentModuleParameter[][] invalidModes =
        [
            [new ContentModuleParameter { name = "patrolMode", value = "" }],
            [new ContentModuleParameter { name = "patrolMode", value = "Unknown" }],
            [
                new ContentModuleParameter { name = "patrolMode", value = "Native" },
                new ContentModuleParameter { name = "patrolMode", value = "AttackMove" }
            ]
        ];
        for (int i = 0; i < invalidModes.Length; i++)
        {
            if (PatrolAndInterceptParameterParser.TryParse(invalidModes[i], out _, out string diagnostic) ||
                diagnostic == null)
            {
                throw new InvalidOperationException("Patrol-and-intercept accepted invalid patrol mode input " + i + ".");
            }
        }
    }),
    ("patrol-and-intercept AI parser rejects invalid and duplicate parameters", () =>
    {
        ContentModuleParameter[][] invalidInputs =
        [
            [new ContentModuleParameter { name = "unknown", value = "1" }],
            [new ContentModuleParameter { name = "baseIndex", value = "0" }, new ContentModuleParameter { name = "baseIndex", value = "1" }],
            [null!],
            [new ContentModuleParameter { name = "baseIndex", value = "32" }],
            [new ContentModuleParameter { name = "patrolRadiusCells", value = "4" }],
            [new ContentModuleParameter { name = "patrolRadiusCells", value = "33" }],
            [new ContentModuleParameter { name = "interceptRadiusCells", value = "4" }],
            [new ContentModuleParameter { name = "interceptRadiusCells", value = "33" }],
            [new ContentModuleParameter { name = "scanIntervalSeconds", value = "1" }],
            [new ContentModuleParameter { name = "scanIntervalSeconds", value = "61" }],
            [new ContentModuleParameter { name = "maxPatrolUnits", value = "0" }],
            [new ContentModuleParameter { name = "maxPatrolUnits", value = "9" }]
        ];
        for (int i = 0; i < invalidInputs.Length; i++)
        {
            if (PatrolAndInterceptParameterParser.TryParse(invalidInputs[i], out _, out string diagnostic) ||
                diagnostic == null)
            {
                throw new InvalidOperationException("Patrol-and-intercept accepted invalid input " + i + ".");
            }
        }
    }),
    ("patrol waypoint selection chooses the nearest reachable distinct cell", () =>
    {
        UnityEngine.Vector2Int[] cells =
        [
            new UnityEngine.Vector2Int(8, 0),
            new UnityEngine.Vector2Int(3, 0),
            new UnityEngine.Vector2Int(1, 1)
        ];
        if (!PatrolAndInterceptWaypointResolver.TrySelectNearest(
                new UnityEngine.Vector2Int(0, 0), cells, 5, out int selected) || selected != 2)
        {
            throw new InvalidOperationException("Patrol did not choose the nearest non-current waypoint inside its radius.");
        }

        if (PatrolAndInterceptWaypointResolver.TrySelectNearest(
                new UnityEngine.Vector2Int(0, 0),
                [new UnityEngine.Vector2Int(0, 0), new UnityEngine.Vector2Int(6, 0)], 5, out _))
        {
            throw new InvalidOperationException("Patrol accepted only a no-op or out-of-radius waypoint.");
        }

        if (PatrolAndInterceptWaypointResolver.TrySelectNearest(
                new UnityEngine.Vector2Int(Int32.MaxValue, Int32.MaxValue),
                [new UnityEngine.Vector2Int(Int32.MinValue, Int32.MinValue)], 5, out _))
        {
            throw new InvalidOperationException("Extreme cell coordinates overflowed the squared-distance check.");
        }
    }),
    ("native patrol route selects nearest distinct cells within the output cap", () =>
    {
        UnityEngine.Vector2Int[] cells =
        [
            new UnityEngine.Vector2Int(1, 0),
            new UnityEngine.Vector2Int(1, 0),
            new UnityEngine.Vector2Int(0, 1),
            new UnityEngine.Vector2Int(3, 0),
            new UnityEngine.Vector2Int(0, 0),
            new UnityEngine.Vector2Int(6, 0)
        ];
        int[] selected = new int[8];
        int count = PatrolAndInterceptWaypointResolver.SelectNearestRoute(
            new UnityEngine.Vector2Int(0, 0), cells, 5, selected);
        if (count != 3 || selected[0] != 2 || selected[1] != 0 || selected[2] != 3)
        {
            throw new InvalidOperationException("Native patrol route did not select unique nearest cells in deterministic order.");
        }

        int[] capped = new int[2];
        count = PatrolAndInterceptWaypointResolver.SelectNearestRoute(
            new UnityEngine.Vector2Int(0, 0), cells, 5, capped);
        if (count != 2 || capped[0] != 2 || capped[1] != 0)
        {
            throw new InvalidOperationException("Native patrol route exceeded its caller-provided output capacity.");
        }
    }),
    ("patrol-and-intercept default still dispatches attack-move waypoint orders", () =>
    {
        ExistingControllers.Instance.Reset();
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            UnityEngine.Time.time = 10f;
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                IsControlledByAi = true,
                IsBuilding = true,
                aiBaseIndex = 0,
                CurrentCell = new UnityEngine.Vector2Int(10, 10),
                Position = new UnityEngine.Vector3(10f, 0f, 10f)
            });
            var defender = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                CurrentCell = new UnityEngine.Vector2Int(9, 10)
            };
            ExistingControllers.Instance.AiUnits().Add(defender);

            behaviour = new PatrolAndInterceptAiModule().CreateBehaviour(
                new DefaultAiBehaviour(), null);
            behaviour.OnInitialized();
            behaviour.Tick();

            if (!defender.LastExecutedCommand.HasValue ||
                defender.LastExecutedCommand.Value.command != EntityCommand.Command.AttackMoveOnPosition ||
                defender.LastExecutedCommand.Value.destination.x != 10f ||
                defender.LastExecutedCommand.Value.destination.z != 10f ||
                defender.PatrolPositions.Count != 0)
            {
                throw new InvalidOperationException("The default patrol mode no longer uses attack-move waypoint orders.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("native patrol mode assigns capped routes only to idle defenders", () =>
    {
        ExistingControllers.Instance.Reset();
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            UnityEngine.Time.time = 10f;
            var targetBuilding = new EntityController
            {
                IsControlledByAi = true,
                IsBuilding = true,
                aiBaseIndex = 0,
                CurrentCell = new UnityEngine.Vector2Int(10, 10),
                Position = new UnityEngine.Vector3(10f, 0f, 10f),
                UniqueEntityId = 900
            };
            ExistingControllers.Instance.AiBuildings().Add(targetBuilding);
            for (int i = 1; i < 10; i++)
            {
                ExistingControllers.Instance.AiBuildings().Add(new EntityController
                {
                    IsControlledByAi = true,
                    IsBuilding = true,
                    aiBaseIndex = 0,
                    CurrentCell = new UnityEngine.Vector2Int(10 + i, 10),
                    Position = new UnityEngine.Vector3(10f + i, 0f, 10f),
                    UniqueEntityId = 900 + i
                });
            }

            var threat = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CurrentAttackTarget = targetBuilding,
                CurrentCell = new UnityEngine.Vector2Int(10, 11),
                Position = new UnityEngine.Vector3(30f, 0f, 40f),
                UniqueEntityId = 930
            };
            ExistingControllers.Instance.PlayerUnits().Add(threat);

            var idleA = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                CurrentCell = new UnityEngine.Vector2Int(9, 10),
                UniqueEntityId = 920
            };
            var idleB = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                CurrentCell = new UnityEngine.Vector2Int(8, 10),
                UniqueEntityId = 921
            };
            var idleC = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                CurrentCell = new UnityEngine.Vector2Int(13, 10),
                UniqueEntityId = 923
            };
            var moving = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentPlayerCommandName = EntityCommand.Command.Move,
                CurrentCell = new UnityEngine.Vector2Int(9, 11),
                UniqueEntityId = 922
            };
            idleA.AddPatrolPosition(new UnityEngine.Vector3(-1f, 0f, -1f));
            idleB.AddPatrolPosition(new UnityEngine.Vector3(-2f, 0f, -2f));
            idleC.AddPatrolPosition(new UnityEngine.Vector3(-4f, 0f, -4f));
            moving.AddPatrolPosition(new UnityEngine.Vector3(-3f, 0f, -3f));
            ExistingControllers.Instance.AiUnits().Add(idleA);
            ExistingControllers.Instance.AiUnits().Add(idleB);
            ExistingControllers.Instance.AiUnits().Add(idleC);
            ExistingControllers.Instance.AiUnits().Add(moving);

            behaviour = new PatrolAndInterceptAiModule().CreateBehaviour(
                new DefaultAiBehaviour { instanceId = 902 },
                [
                    new ContentModuleParameter { name = "patrolMode", value = "Native" },
                    new ContentModuleParameter { name = "patrolRadiusCells", value = "32" },
                    new ContentModuleParameter { name = "maxPatrolUnits", value = "2" }
                ]);
            behaviour.OnInitialized();
            behaviour.Tick();

            if (!idleA.LastExecutedCommand.HasValue ||
                idleA.LastExecutedCommand.Value.command != EntityCommand.Command.AttackMoveOnPosition ||
                idleA.LastExecutedCommand.Value.destination.x != threat.Position.x ||
                idleA.LastExecutedCommand.Value.destination.z != threat.Position.z ||
                !idleA.CurrentCommandIsTaggedWith(idleA.LastExecutedCommand.Value.tag) ||
                idleA.ClearPatrolPositionsCallCount != 0 || idleA.PatrolPositions.Count != 1)
            {
                throw new InvalidOperationException("Native patrol mode changed the existing attack-move intercept behavior.");
            }

            EntityController[] remainingIdleDefenders = [idleB, idleC];
            int nativePatrolDispatches = 0;
            int untouchedIdleDefenders = 0;
            foreach (EntityController defender in remainingIdleDefenders)
            {
                if (defender.LastExecutedCommand?.command == EntityCommand.Command.Patrol)
                {
                    nativePatrolDispatches++;
                    if (defender.LastExecutedCommand.Value.intValue != 0 ||
                        defender.LastCommandProcessingType != EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand ||
                        !defender.CurrentCommandIsTaggedWith(defender.LastExecutedCommand.Value.tag) ||
                        defender.ClearPatrolPositionsCallCount != 1 || defender.PatrolPositions.Count != 8 ||
                        defender.PatrolPositions[0].x != 10f || defender.PatrolPositions[7].x != 17f)
                    {
                        throw new InvalidOperationException("Native patrol did not install the bounded nearest route at index zero.");
                    }
                    continue;
                }

                if (defender.LastExecutedCommand.HasValue || defender.ClearPatrolPositionsCallCount != 0 ||
                    defender.PatrolPositions.Count != 1)
                {
                    throw new InvalidOperationException("Native patrol changed an idle defender after reaching its shared dispatch cap.");
                }
                untouchedIdleDefenders++;
            }

            if (nativePatrolDispatches != 1 || untouchedIdleDefenders != 1 || moving.LastExecutedCommand.HasValue ||
                moving.ClearPatrolPositionsCallCount != 0 || moving.PatrolPositions.Count != 1)
            {
                throw new InvalidOperationException("Native patrol exceeded its cap, used an invalid route, or changed a moving defender.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("native patrol mode preserves a queued Move behind DoNothing", () =>
    {
        ExistingControllers.Instance.Reset();
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            UnityEngine.Time.time = 10f;
            var building = new EntityController
            {
                IsControlledByAi = true,
                IsBuilding = true,
                aiBaseIndex = 0,
                CurrentCell = new UnityEngine.Vector2Int(10, 10),
                Position = new UnityEngine.Vector3(10f, 0f, 10f),
                UniqueEntityId = 900
            };
            ExistingControllers.Instance.AiBuildings().Add(building);

            var defender = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentPlayerCommandName = EntityCommand.Command.DoNothing,
                CurrentCell = new UnityEngine.Vector2Int(9, 10),
                UniqueEntityId = 924
            };
            defender.CommandChainForTest.AddLast(new EntityCommand
            {
                command = EntityCommand.Command.Move,
                destination = new UnityEngine.Vector3(20f, 0f, 20f)
            });
            defender.AddPatrolPosition(new UnityEngine.Vector3(-5f, 0f, -5f));
            ExistingControllers.Instance.AiUnits().Add(defender);

            behaviour = new PatrolAndInterceptAiModule().CreateBehaviour(
                new DefaultAiBehaviour { instanceId = 903 },
                [
                    new ContentModuleParameter { name = "patrolMode", value = "Native" },
                    new ContentModuleParameter { name = "patrolRadiusCells", value = "15" },
                    new ContentModuleParameter { name = "maxPatrolUnits", value = "1" }
                ]);
            behaviour.OnInitialized();
            behaviour.Tick();

            LinkedListNode<EntityCommand>? firstQueuedCommand = defender.CommandChainForTest.First;
            if (defender.LastExecutedCommand.HasValue || defender.ExecuteCommandCallCount != 0 ||
                defender.ClearPatrolPositionsCallCount != 0 || defender.PatrolPositions.Count != 1 ||
                defender.PatrolPositions[0].x != -5f || defender.CommandChainForTest.Count != 1 ||
                firstQueuedCommand == null || firstQueuedCommand.Value.command != EntityCommand.Command.Move ||
                firstQueuedCommand.Value.destination.x != 20f || firstQueuedCommand.Value.destination.z != 20f)
            {
                throw new InvalidOperationException("Native patrol superseded a queued Move behind a DoNothing current command.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("managed assemblies are rejected from content packs", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            File.WriteAllBytes(Path.Combine(root, "unexpected.dll"), [0x4D, 0x5A]);
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "package.dll-forbidden");
        });
    }),
    ("units must not share the same productionBuilding.id within a pack", () =>
    {
        ContentUnitDefinition[] units =
        [
            new ContentUnitDefinition { id = "sample:duplicate-factory-a", productionBuilding = new ContentProductionBuildingDefinition { id = "sample:shared-factory" } },
            new ContentUnitDefinition { id = "sample:duplicate-factory-b", productionBuilding = new ContentProductionBuildingDefinition { id = "sample:shared-factory" } }
        ];

        var diagnostics = new List<ContentDiagnostic>();
        ContentPackValidator.ValidateUnitProductionBuildingIdsUnique(units, "units", diagnostics);

        if (!diagnostics.Any(d => d.Code == "unit.production-building.id-duplicate"))
        {
            throw new InvalidOperationException("Duplicate productionBuilding IDs must be rejected.");
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
    ("scoped AI profiles must reference contiguous stage indices", () =>
    {
        ContentAiProfileDefinition profile = new ContentAiProfileDefinition
        {
            id = "sample:non-contiguous-ai",
            scopeToReferencedStages = true
        };
        ContentStageDefinition[] stages =
        [
            new ContentStageDefinition { id = "stage-0", aiProfileIds = new[] { profile.id } },
            new ContentStageDefinition { id = "stage-1" },
            new ContentStageDefinition { id = "stage-2", aiProfileIds = new[] { profile.id } }
        ];

        var diagnostics = new List<ContentDiagnostic>();
        ContentPackValidator.ValidateAiProfileStageRangeContiguity(profile, "aiProfiles[0]", stages, diagnostics);

        if (!diagnostics.Any(d => d.Code == "ai-profile.stage-range-not-contiguous"))
        {
            throw new InvalidOperationException("Non-contiguous AI stage ranges must be rejected.");
        }
    }),
    ("loader diagnostic formatter exposes factory, pool, and AI range details", () =>
    {
        string factoryLine = LoaderDiagnosticsFormatter.FormatUnitFactory(
            new UnitFactoryDiagnosticReport("test:unit", "test:factory", true, "Factory loaded."));
        string poolLine = LoaderDiagnosticsFormatter.FormatStagePoolScope(
            new StagePoolScopeDiagnosticReport("test:stage", "pool-a", new[] { "test:factory", "native:unit" }));
        string aiRangeLine = LoaderDiagnosticsFormatter.FormatAiProfileStageRange(
            new AiProfileStageRangeDiagnosticReport("test:ai", 2, 4, true));

        if (factoryLine != "test:unit -> test:factory (Factory loaded.)" ||
            poolLine != "test:stage / pool-a candidates: native:unit, test:factory" ||
            aiRangeLine != "test:ai: 2-4 (contiguous)")
        {
            throw new InvalidOperationException("Loader diagnostics formatting omitted a registered factory, pool, or AI stage range.");
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
    ("unrequested unit visual is not reported as resolved", () =>
    {
        UnitDiagnosticReport[] units =
        [
            new UnitDiagnosticReport("test:unit-no-visual", "test:pack", "unit-prefab", "unit-card",
                false, true, false, true, false)
        ];
        var resolvedResources = new HashSet<string>(new[] { "unit-prefab", "unit-card" }, StringComparer.Ordinal);
        IReadOnlyList<UnitAssetResolutionDiagnosticReport> assets =
            LoaderUnitDiagnosticsBuilder.CreateAssetResolutionReports(
                units, resolvedResources, new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));

        if (assets.Count != 1 || assets[0].VisualRequested || assets[0].VisualResolved ||
            LoaderUnitDiagnosticsBuilder.GetAssetResolutionSeverity(assets[0]) != UnitAssetResolutionSeverity.Muted ||
            !assets[0].StatusMessage.Contains("custom visual not requested", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A unit without a custom visual request must be explicitly neutral, not reported as resolved.");
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
    ("eligibility reflection failures are reported as unknown instead of ineligible", () =>
    {
        UnitDiagnosticReport[] units =
        [
            new UnitDiagnosticReport("test:unit-unknown-eligibility", "test:pack", "prefab", "card",
                false, false, false, false, false, "AllowedAsBlueprint")
        ];
        IReadOnlyList<UnitEligibilityDiagnosticReport> reports =
            LoaderUnitDiagnosticsBuilder.CreateEligibilityReports(units);

        if (reports.Count != 1 || reports[0].IsResolved ||
            LoaderDiagnosticsFormatter.FormatUnitEligibility(reports[0]) !=
                "test:unit-unknown-eligibility [Eligibility=Unknown; failed fields=AllowedAsBlueprint]")
        {
            throw new InvalidOperationException("A failed native eligibility read must be distinguished from a false flag.");
        }
    }),
    ("unit active skill rejects non-finite effect values", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].activeSkills = new[]
            {
                new ContentActiveSkillDefinition { id = "sample:bad-effect", skillType = "Self", effectValue = Single.NaN }
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "active-skill.effect-value-invalid");
        });
    }),
    ("unit synergy rejects non-finite radius", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].synergies = new[]
            {
                new ContentUnitSynergyDefinition { id = "sample:bad-radius", statName = "Damage1", radius = Single.PositiveInfinity }
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-synergy.radius-invalid");
        });
    }),
    ("unit visual variant rejects non-finite scale", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].visualVariants = new[]
            {
                new ContentUnitVisualVariantDefinition { id = "sample:bad-scale", scale = Single.NaN }
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-visual-variant.scale-invalid");
        });
    }),
    ("unit status effect rejects non-finite magnitude", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].statusEffects = new[]
            {
                new ContentUnitStatusEffectDefinition { id = "sample:bad-magnitude", effectType = "Damage", magnitude = Single.NegativeInfinity }
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-status-effect.magnitude-invalid");
        });
    }),
    ("unit effect values and visual scale preserve schema-allowed signed and zero values", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].activeSkills = new[]
            {
                new ContentActiveSkillDefinition { id = "sample:negative-effect", skillType = "Self", effectValue = -1 }
            };
            pack.units[0].statusEffects = new[]
            {
                new ContentUnitStatusEffectDefinition { id = "sample:negative-magnitude", effectType = "CustomEffect", magnitude = -1 }
            };
            pack.units[0].visualVariants = new[]
            {
                new ContentUnitVisualVariantDefinition { id = "sample:zero-scale", scale = 0 }
            };
            pack.units[0].weapons = new[]
            {
                new ContentUnitWeaponDefinition { weaponType = "CustomWeapon" }
            };
            pack.units[0].spawn = new ContentUnitSpawnDefinition { spawnLocation = "Orbit" };
            pack.units[0].aiBehavior = new ContentUnitAiBehaviorDefinition { aiTag = "UnmappedTag" };
            pack.units[0].experienceReward = new ContentUnitExperienceRewardDefinition
            {
                experienceType = "CustomExperience"
            };
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("unit numeric sub-definitions reject non-finite values", () =>
    {
        var cases = new (Action<ContentPackDefinition> Configure, string DiagnosticCode)[]
        {
            (pack => pack.units[0].activeSkills = new[] { new ContentActiveSkillDefinition { id = "sample:bad-cooldown", skillType = "Self", cooldown = Single.NaN } }, "active-skill.cooldown-invalid"),
            (pack => pack.units[0].activeSkills = new[] { new ContentActiveSkillDefinition { id = "sample:bad-mana", skillType = "Self", manaCost = Single.PositiveInfinity } }, "active-skill.manaCost-invalid"),
            (pack => pack.units[0].combatBoosts = new[] { new ContentCombatBoostDefinition { id = "sample:bad-boost", statName = "Damage1", multiplier = Single.NaN } }, "combat-boost.multiplier-invalid"),
            (pack => pack.units[0].combatBoosts = new[] { new ContentCombatBoostDefinition { id = "sample:bad-boost", statName = "Damage1", duration = Single.PositiveInfinity } }, "combat-boost.duration-invalid"),
            (pack => pack.units[0].upgrades = new[] { new ContentUnitUpgradeDefinition { targetUnitId = pack.units[0].id, cost = Single.NaN } }, "unit-upgrade.cost-invalid"),
            (pack => pack.units[0].experience = new ContentUnitExperienceDefinition { experienceGainMultiplier = Single.NaN }, "unit-experience.gain-multiplier-invalid"),
            (pack => pack.units[0].experience = new ContentUnitExperienceDefinition { levelUpBonus = Single.PositiveInfinity }, "unit-experience.level-up-bonus-invalid"),
            (pack => pack.units[0].synergies = new[] { new ContentUnitSynergyDefinition { id = "sample:bad-synergy", statName = "Damage1", multiplier = Single.NaN } }, "unit-synergy.multiplier-invalid"),
            (pack => pack.units[0].spawn = new ContentUnitSpawnDefinition { spawnRate = Single.NaN }, "unit-spawn.rate-invalid"),
            (pack => pack.units[0].spawn = new ContentUnitSpawnDefinition { spawnCooldown = Single.PositiveInfinity }, "unit-spawn.cooldown-invalid"),
            (pack => pack.units[0].statusEffects = new[] { new ContentUnitStatusEffectDefinition { id = "sample:bad-duration", effectType = "Damage", duration = Single.NaN } }, "unit-status-effect.duration-invalid"),
            (pack => pack.units[0].productionQueue = new ContentUnitProductionQueueDefinition { buildTime = Single.NaN }, "unit-production-queue.build-time-invalid"),
            (pack => pack.units[0].productionQueue = new ContentUnitProductionQueueDefinition { resourceCost = Single.PositiveInfinity }, "unit-production-queue.resource-cost-invalid"),
            (pack => pack.units[0].resourceCosts = new[] { new ContentUnitResourceCostDefinition { resourceType = "Credits", amount = Single.NaN } }, "unit-resource-cost.amount-invalid"),
            (pack => pack.units[0].detection = new ContentUnitDetectionDefinition { detectionRange = Single.NaN }, "unit-detection.range-invalid"),
            (pack => pack.units[0].weapons = new[] { new ContentUnitWeaponDefinition { weaponType = "CustomWeapon", damage = Single.NaN } }, "unit-weapon.damage-invalid"),
            (pack => pack.units[0].weapons = new[] { new ContentUnitWeaponDefinition { weaponType = "CustomWeapon", range = Single.PositiveInfinity } }, "unit-weapon.range-invalid"),
            (pack => pack.units[0].weapons = new[] { new ContentUnitWeaponDefinition { weaponType = "CustomWeapon", fireRate = Single.NaN } }, "unit-weapon.fire-rate-invalid"),
            (pack => pack.units[0].healing = new ContentUnitHealingDefinition { healAmount = Single.NaN }, "unit-healing.amount-invalid"),
            (pack => pack.units[0].healing = new ContentUnitHealingDefinition { healRange = Single.PositiveInfinity }, "unit-healing.range-invalid"),
            (pack => pack.units[0].healing = new ContentUnitHealingDefinition { healRate = Single.NaN }, "unit-healing.rate-invalid"),
            (pack => pack.units[0].healing = new ContentUnitHealingDefinition { repairAmount = Single.PositiveInfinity }, "unit-healing.repair-invalid"),
            (pack => pack.units[0].stealth = new ContentUnitStealthDefinition { stealthLevel = Single.NaN }, "unit-stealth.level-invalid"),
            (pack => pack.units[0].stealth = new ContentUnitStealthDefinition { cloakDuration = Single.PositiveInfinity }, "unit-stealth.duration-invalid"),
            (pack => pack.units[0].stealth = new ContentUnitStealthDefinition { detectionReduction = Single.NaN }, "unit-stealth.reduction-invalid"),
            (pack => pack.units[0].movement = new ContentUnitMovementDefinition { turnRate = Single.NaN }, "unit-movement.turn-rate-invalid"),
            (pack => pack.units[0].movement = new ContentUnitMovementDefinition { acceleration = Single.PositiveInfinity }, "unit-movement.acceleration-invalid"),
            (pack => pack.units[0].command = new ContentUnitCommandDefinition { commandRange = Single.NaN }, "unit-command.range-invalid"),
            (pack => pack.units[0].experienceReward = new ContentUnitExperienceRewardDefinition { experienceType = "CustomExperience", bonusMultiplier = Single.PositiveInfinity }, "unit-experience-reward.bonus-multiplier-invalid"),
            (pack => pack.units[0].aura = new ContentUnitAuraDefinition { auraRadius = Single.NaN }, "unit-aura.radius-invalid"),
            (pack => pack.units[0].aura = new ContentUnitAuraDefinition { buffAmount = Single.PositiveInfinity }, "unit-aura.buff-invalid"),
            (pack => pack.units[0].resourceGathering = new ContentUnitResourceGatheringDefinition { resourceType = "Credits", gatherRate = Single.NaN }, "unit-gathering.rate-invalid"),
            (pack => pack.units[0].resourceGathering = new ContentUnitResourceGatheringDefinition { resourceType = "Credits", gatherRange = Single.PositiveInfinity }, "unit-gathering.range-invalid"),
            (pack => pack.units[0].aiBehavior = new ContentUnitAiBehaviorDefinition { skillActivationCheckInterval = Single.NaN }, "unit-ai.interval-invalid")
        };
        var failures = new System.Collections.Generic.List<string>();
        foreach (var testCase in cases)
        {
            try
            {
                WithPackage(sample, (pack, root) =>
                {
                    testCase.Configure(pack);
                    ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), testCase.DiagnosticCode);
                });
            }
            catch (Exception exception)
            {
                failures.Add(testCase.DiagnosticCode + ": " + exception.Message);
            }
        }
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        }
    }),
    ("offline loader registry tool validates declared pack relationships", () =>
    {
        string output;
        int exitCode;
        using (var process = new System.Diagnostics.Process())
        {
            process.StartInfo.FileName = ".venv/Scripts/python.exe";
            process.StartInfo.Arguments = "tools/validate_loader_registry.py";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            exitCode = process.ExitCode;
        }

        if (exitCode != 0 || !output.Contains("PASS checked 3 manifests"))
        {
            throw new InvalidOperationException("Loader registry tool failed: " + output);
        }
    }),
    ("unit upgrade graph rejects circular references", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units = new ContentUnitDefinition[3];
            for (int i = 0; i < 3; i++)
            {
                pack.units[i] = new ContentUnitDefinition
                {
                    id = "sample:upgrade-" + i,
                    nativeTemplateId = "native:base"
                };
            }
            pack.units[0].upgrades = new[]
            {
                new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-1" }
            };
            pack.units[1].upgrades = new[]
            {
                new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-2" }
            };
            pack.units[2].upgrades = new[]
            {
                new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-0" }
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-upgrade.cycle-detected");
        });
    }),
    ("unit upgrade graph rejects depth exceeded", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            int count = 7;
            pack.units = new ContentUnitDefinition[count];
            for (int i = 0; i < count; i++)
            {
                pack.units[i] = new ContentUnitDefinition
                {
                    id = "sample:upgrade-" + i,
                    nativeTemplateId = "native:base"
                };
            }
            for (int i = 0; i < count - 1; i++)
            {
                pack.units[i].upgrades = new[]
                {
                    new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-" + (i + 1) }
                };
            }
            pack.units[count - 1].upgrades = Array.Empty<ContentUnitUpgradeDefinition>();
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-upgrade.depth-exceeded");
        });
    }),
    ("unit upgrade graph accepts exactly five edges", () =>
        {
            WithPackage(nativeSample, (pack, root) =>
        {
            const int count = 6;
            for (int i = 0; i < count; i++)
            {
                pack.units[i].upgrades = i < count - 1
                    ? new[] { new ContentUnitUpgradeDefinition { targetUnitId = pack.units[i + 1].id } }
                    : Array.Empty<ContentUnitUpgradeDefinition>();
            }
            ContentPackValidationResult result = ContentPackValidator.Validate(
                pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (!result.IsValid || result.Diagnostics.Any(d => d.Code == "unit-upgrade.depth-exceeded"))
            {
                throw new InvalidOperationException("A five-edge upgrade chain must validate cleanly: " +
                    string.Join(", ", result.Diagnostics.Select(d => d.Code)));
            }
        });
    }),
    ("unit upgrade depth check handles convergent paths", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            const int count = 7;
            pack.units = new ContentUnitDefinition[count];
            for (int i = 0; i < count; i++)
            {
                pack.units[i] = new ContentUnitDefinition
                {
                    id = "sample:upgrade-join-" + i,
                    nativeTemplateId = "native:base",
                    upgrades = Array.Empty<ContentUnitUpgradeDefinition>()
                };
            }

            pack.units[0].upgrades = new[]
            {
                new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-join-5" },
                new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-join-1" }
            };
            for (int i = 1; i < 5; i++)
            {
                pack.units[i].upgrades = new[]
                {
                    new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-join-" + (i + 1) }
                };
            }
            pack.units[5].upgrades = new[]
            {
                new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-join-6" }
            };

            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-upgrade.depth-exceeded");
        });
    }),
    ("unit synergy graph rejects circular references", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units = new ContentUnitDefinition[2];
            for (int i = 0; i < 2; i++)
            {
                pack.units[i] = new ContentUnitDefinition
                {
                    id = "sample:synergy-" + i,
                    nativeTemplateId = "native:base"
                };
            }
            pack.units[0].synergies = new[]
            {
                new ContentUnitSynergyDefinition
                {
                    id = "sample:s1",
                    statName = "damage",
                    multiplier = 1.0f,
                    radius = 1.0f,
                    targetUnitIds = new[] { "sample:synergy-1" }
                }
            };
            pack.units[1].synergies = new[]
            {
                new ContentUnitSynergyDefinition
                {
                    id = "sample:s2",
                    statName = "damage",
                    multiplier = 1.0f,
                    radius = 1.0f,
                    targetUnitIds = new[] { "sample:synergy-0" }
                }
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-synergy.cycle-detected");
        });
    }),
    ("unit synergy graph rejects excessive chain depth", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            const int count = 7;
            pack.units = new ContentUnitDefinition[count];
            for (int i = 0; i < count; i++)
            {
                pack.units[i] = new ContentUnitDefinition
                {
                    id = "sample:synergy-depth-" + i,
                    nativeTemplateId = "native:base"
                };
                pack.units[i].synergies = i < count - 1
                    ? new[]
                    {
                        new ContentUnitSynergyDefinition
                        {
                            id = "sample:synergy-depth-link-" + i,
                            statName = "damage",
                            multiplier = 1.0f,
                            radius = 1.0f,
                            targetUnitIds = new[] { "sample:synergy-depth-" + (i + 1) }
                        }
                    }
                    : Array.Empty<ContentUnitSynergyDefinition>();
            }

            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-synergy.depth-exceeded");
        });
    }),
    ("unit synergy depth check handles convergent paths", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            const int count = 7;
            pack.units = new ContentUnitDefinition[count];
            for (int i = 0; i < count; i++)
            {
                pack.units[i] = new ContentUnitDefinition
                {
                    id = "sample:synergy-join-" + i,
                    nativeTemplateId = "native:base"
                };
                pack.units[i].synergies = Array.Empty<ContentUnitSynergyDefinition>();
            }

            pack.units[0].synergies = new[]
            {
                new ContentUnitSynergyDefinition
                {
                    id = "sample:synergy-join-root",
                    statName = "damage",
                    multiplier = 1.0f,
                    radius = 1.0f,
                    targetUnitIds = new[] { "sample:synergy-join-5", "sample:synergy-join-1" }
                }
            };
            for (int i = 1; i < 5; i++)
            {
                pack.units[i].synergies = new[]
                {
                    new ContentUnitSynergyDefinition
                    {
                        id = "sample:synergy-join-link-" + i,
                        statName = "damage",
                        multiplier = 1.0f,
                        radius = 1.0f,
                        targetUnitIds = new[] { "sample:synergy-join-" + (i + 1) }
                    }
                };
            }
            pack.units[5].synergies = new[]
            {
                new ContentUnitSynergyDefinition
                {
                    id = "sample:synergy-join-tail",
                    statName = "damage",
                    multiplier = 1.0f,
                    radius = 1.0f,
                    targetUnitIds = new[] { "sample:synergy-join-6" }
                }
            };

            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-synergy.depth-exceeded");
        });
    }),
    ("unit synergy graph accepts exactly five edges", () =>
        {
            WithPackage(nativeSample, (pack, root) =>
        {
            const int count = 6;
            for (int i = 0; i < count; i++)
            {
                pack.units[i].synergies = i < count - 1
                    ? new[]
                    {
                        new ContentUnitSynergyDefinition
                        {
                            id = "sample:synergy-boundary-link-" + i,
                            statName = "damage", multiplier = 1.0f, radius = 1.0f,
                            targetUnitIds = new[] { pack.units[i + 1].id }
                        }
                    }
                    : Array.Empty<ContentUnitSynergyDefinition>();
            }
            ContentPackValidationResult result = ContentPackValidator.Validate(
                pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (!result.IsValid || result.Diagnostics.Any(d => d.Code == "unit-synergy.depth-exceeded"))
            {
                throw new InvalidOperationException("A five-edge synergy chain must validate cleanly: " +
                    string.Join(", ", result.Diagnostics.Select(d => d.Code)));
            }
        });
    }),
    ("unit synergy graph accepts convergent DAG", () =>
    {

        WithPackage(nativeSample, (pack, root) =>
        {
            const int count = 4;
            for (int i = 0; i < count; i++)
            {
                pack.units[i].synergies = Array.Empty<ContentUnitSynergyDefinition>();
            }
            pack.units[0].synergies = new[]
            {
                new ContentUnitSynergyDefinition
                {
                    id = "sample:d1",
                    statName = "damage",
                    multiplier = 1.0f,
                    radius = 1.0f,
                    targetUnitIds = new[] { pack.units[1].id, pack.units[2].id }
                }
            };
            pack.units[1].synergies = new[]
            {
                new ContentUnitSynergyDefinition
                {
                    id = "sample:d2",
                    statName = "damage",
                    multiplier = 1.0f,
                    radius = 1.0f,
                    targetUnitIds = new[] { pack.units[3].id }
                }
            };
            pack.units[2].synergies = new[]
            {
                new ContentUnitSynergyDefinition
                {
                    id = "sample:d3",
                    statName = "damage",
                    multiplier = 1.0f,
                    radius = 1.0f,
                    targetUnitIds = new[] { pack.units[3].id }
                }
            };
            pack.units[3].synergies = Array.Empty<ContentUnitSynergyDefinition>();
            ContentPackValidationResult result = ContentPackValidator.Validate(pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (!result.IsValid || result.Diagnostics.Any(d =>
                    d.Code == "unit-synergy.cycle-detected" || d.Code == "unit-synergy.depth-exceeded"))
            {
                throw new InvalidOperationException("Convergent DAG must be fully valid without cycle/depth diagnostics: " +
                    string.Join(", ", result.Diagnostics.Select(d => d.Code)));
            }
        });
    }),
    ("unit reference graph validation is stack-safe for a deep chain", () =>
    {
        var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.ArgumentList.Add("exec");
        process.StartInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        process.StartInfo.ArgumentList.Add(repositoryRoot);
        process.StartInfo.Environment["RCL_UNIT_GRAPH_STACK_PROBE"] = "1";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !output.Contains("UNIT-GRAPH-STACK-PROBE-PASS", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Deep graph child failed (exit " + process.ExitCode + "). stdout=" +
                output + " stderr=" + error);
        }
    }),
    ("production queue feasibility reports mismatched buildTime and resourceCost", () =>
        {
            WithPackage(nativeSample, (pack, root) =>
        {
            pack.units[0].productionQueue = new ContentUnitProductionQueueDefinition
            {
                buildTime = 10,
                resourceCost = 0,
                queueSize = 1
            };
            ContentPackValidationResult result = ContentPackValidator.Validate(
                pack, root, new EmptySnapshot(), new KnownModuleValidator());
            ContentDiagnostic warning = result.Diagnostics.Single(d => d.Code == "unit-production-queue.resource-cost-required");
            if (warning.Severity != DiagnosticSeverity.Warning || !result.IsValid)
            {
                throw new InvalidOperationException("Production queue feasibility diagnostic must be a non-invalidating warning.");
            }
        });
    }),
    ("production queue feasibility reports missing queueSize when buildTime is positive", () =>
        {
            WithPackage(nativeSample, (pack, root) =>
        {
            pack.units[0].productionQueue = new ContentUnitProductionQueueDefinition
            {
                buildTime = 10,
                resourceCost = 10,
                queueSize = 0
            };
            ContentPackValidationResult result = ContentPackValidator.Validate(
                pack, root, new EmptySnapshot(), new KnownModuleValidator());
            ContentDiagnostic warning = result.Diagnostics.Single(d => d.Code == "unit-production-queue.queue-size-required");
            if (warning.Severity != DiagnosticSeverity.Warning || !result.IsValid)
            {
                throw new InvalidOperationException("Production queue feasibility diagnostic must be a non-invalidating warning.");
            }
        });
    }),
    ("loader localization coverage report snapshots available languages", () =>
    {
        UnitDiagnosticReport[] units =
        [
            new UnitDiagnosticReport("test:unit", "test:pack", "unit-prefab", "unit-card",
                false, true, false, true, false)
        ];
        var sourceLanguages = new List<string> { "en-US", "es-XL" };
        IReadOnlyList<UnitLocalizationCoverageDiagnosticReport> reports =
            LoaderUnitDiagnosticsBuilder.CreateLocalizationCoverageReports(
                units, id => sourceLanguages, id => true);
        sourceLanguages[0] = "fr-FR";

        if (reports.Count != 1 || reports[0].AvailableLanguages.Count != 2 ||
            reports[0].AvailableLanguages[0] != "en-US" ||
            reports[0].AvailableLanguages[1] != "es-XL" ||
            !reports[0].HasEnglish || !reports[0].HasNativeTemplate)
        {
            throw new InvalidOperationException("Localization coverage report must snapshot language data and fallback coverage.");
        }

        IList<string>? mutableLanguages = reports[0].AvailableLanguages as IList<string>;
        if (mutableLanguages != null && !mutableLanguages.IsReadOnly)
        {
            throw new InvalidOperationException("Localization coverage report language snapshots must be immutable.");
        }

        string line = LoaderDiagnosticsFormatter.FormatUnitLocalizationCoverage(reports[0]);
        if (!line.Contains("Languages=en-US, es-XL", StringComparison.Ordinal) ||
            !line.Contains("English=True", StringComparison.Ordinal) ||
            !line.Contains("NativeTemplate=True", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Localization coverage formatter did not include expected language details: " + line);
        }
    }),
    ("factory-reinforcement AI parameter parser supplies bounded defaults and validates limits", () =>
    {
        if (!FactoryReinforcementParameterParser.TryParse(null, out FactoryReinforcementSettings defaults,
            out string diagnostic) || diagnostic != null || defaults.BaseIndex != -1 ||
            defaults.CheckIntervalSeconds != 10f || defaults.MaxQueuePerFactory != 1 ||
            defaults.MaxStandingArmy != 25 || !defaults.RequireEconomy || defaults.MinGameTimeSeconds != 15f)
        {
            throw new InvalidOperationException("The factory-reinforcement AI parser did not return its documented defaults.");
        }

        ContentModuleParameter[] boundaries =
        [
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "checkIntervalSeconds", value = "120" },
            new ContentModuleParameter { name = "maxQueuePerFactory", value = "5" },
            new ContentModuleParameter { name = "maxStandingArmy", value = "100" },
            new ContentModuleParameter { name = "requireEconomy", value = "false" },
            new ContentModuleParameter { name = "minGameTimeSeconds", value = "600" }
        ];
        if (!FactoryReinforcementParameterParser.TryParse(boundaries, out FactoryReinforcementSettings maximums,
            out diagnostic) || diagnostic != null || maximums.BaseIndex != 31 ||
            maximums.CheckIntervalSeconds != 120f || maximums.MaxQueuePerFactory != 5 ||
            maximums.MaxStandingArmy != 100 || maximums.RequireEconomy != false || maximums.MinGameTimeSeconds != 600f)
        {
            throw new InvalidOperationException("The factory-reinforcement AI parser rejected valid upper-bound values.");
        }
    }),
    ("factory-reinforcement AI parameter parser rejects invalid values and unknown parameters", () =>
    {
        ContentModuleParameter[][] invalidInputs =
        [
            [new ContentModuleParameter { name = "unknownParam", value = "1" }],
            [new ContentModuleParameter { name = "baseIndex", value = "0" }, new ContentModuleParameter { name = "baseIndex", value = "1" }],
            [null!],
            [new ContentModuleParameter { name = "baseIndex", value = "-2" }],
            [new ContentModuleParameter { name = "baseIndex", value = "32" }],
            [new ContentModuleParameter { name = "checkIntervalSeconds", value = "0.5" }],
            [new ContentModuleParameter { name = "checkIntervalSeconds", value = "121" }],
            [new ContentModuleParameter { name = "maxQueuePerFactory", value = "0" }],
            [new ContentModuleParameter { name = "maxQueuePerFactory", value = "6" }],
            [new ContentModuleParameter { name = "maxStandingArmy", value = "0" }],
            [new ContentModuleParameter { name = "maxStandingArmy", value = "101" }],
            [new ContentModuleParameter { name = "requireEconomy", value = "maybe" }],
            [new ContentModuleParameter { name = "minGameTimeSeconds", value = "-1" }],
            [new ContentModuleParameter { name = "minGameTimeSeconds", value = "601" }]
        ];

        for (int i = 0; i < invalidInputs.Length; i++)
        {
            if (FactoryReinforcementParameterParser.TryParse(invalidInputs[i], out _, out string diagnostic) ||
                diagnostic == null)
            {
                throw new InvalidOperationException("The factory-reinforcement AI parser accepted invalid input " + i + ".");
            }
        }
    }),
    ("flanking-strike AI parameter parser supplies bounded defaults and validates limits", () =>
    {
        if (!FlankingStrikeParameterParser.TryParse(null, out FlankingStrikeSettings defaults,
            out string diagnostic) || diagnostic != null || defaults.BaseIndex != 0 ||
            defaults.CooldownSeconds != 35f || defaults.SquadSize != 3 ||
            defaults.FlankAngleDegrees != 60f || defaults.FlankDistance != 35f ||
            defaults.MinGameTimeSeconds != 30f || defaults.TargetPreference != "Refinery")
        {
            throw new InvalidOperationException("The flanking-strike AI parser did not return its documented defaults.");
        }

        ContentModuleParameter[] boundaries =
        [
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "cooldownSeconds", value = "300" },
            new ContentModuleParameter { name = "squadSize", value = "12" },
            new ContentModuleParameter { name = "flankAngleDegrees", value = "90" },
            new ContentModuleParameter { name = "flankDistance", value = "120" },
            new ContentModuleParameter { name = "minGameTimeSeconds", value = "600" },
            new ContentModuleParameter { name = "targetPreference", value = "Harvester" }
        ];
        if (!FlankingStrikeParameterParser.TryParse(boundaries, out FlankingStrikeSettings maximums,
            out diagnostic) || diagnostic != null || maximums.BaseIndex != 31 ||
            maximums.CooldownSeconds != 300f || maximums.SquadSize != 12 ||
            maximums.FlankAngleDegrees != 90f || maximums.FlankDistance != 120f ||
            maximums.MinGameTimeSeconds != 600f || maximums.TargetPreference != "Harvester")
        {
            throw new InvalidOperationException("The flanking-strike AI parser rejected valid upper-bound values.");
        }
    }),
    ("flanking-strike AI parameter parser rejects invalid values and unknown parameters", () =>
    {
        ContentModuleParameter[][] invalidInputs =
        [
            [new ContentModuleParameter { name = "unknownParam", value = "1" }],
            [new ContentModuleParameter { name = "baseIndex", value = "0" }, new ContentModuleParameter { name = "baseIndex", value = "1" }],
            [null!],
            [new ContentModuleParameter { name = "baseIndex", value = "-1" }],
            [new ContentModuleParameter { name = "baseIndex", value = "32" }],
            [new ContentModuleParameter { name = "cooldownSeconds", value = "4" }],
            [new ContentModuleParameter { name = "cooldownSeconds", value = "301" }],
            [new ContentModuleParameter { name = "squadSize", value = "0" }],
            [new ContentModuleParameter { name = "squadSize", value = "13" }],
            [new ContentModuleParameter { name = "flankAngleDegrees", value = "14" }],
            [new ContentModuleParameter { name = "flankAngleDegrees", value = "91" }],
            [new ContentModuleParameter { name = "flankDistance", value = "9" }],
            [new ContentModuleParameter { name = "flankDistance", value = "121" }],
            [new ContentModuleParameter { name = "minGameTimeSeconds", value = "-1" }],
            [new ContentModuleParameter { name = "minGameTimeSeconds", value = "601" }],
            [new ContentModuleParameter { name = "targetPreference", value = "InvalidPref" }]
        ];

        for (int i = 0; i < invalidInputs.Length; i++)
        {
            if (FlankingStrikeParameterParser.TryParse(invalidInputs[i], out _, out string diagnostic) ||
                diagnostic == null)
            {
                throw new InvalidOperationException("The flanking-strike AI parser accepted invalid input " + i + ".");
            }
        }
    }),
    ("structured unit values reject a duplicate raw override", () =>
    {
        var fields = new (string Name, string Value,
            Action<ContentUnitDefinition> SetValue,
            Action<ContentUnitDefinition> ClearValue)[]
        {
            ("combatValue", "500", unit => unit.combatValue = 500, unit => unit.combatValue = null),
            ("gridSize", "2", unit => unit.gridSize = 2, unit => unit.gridSize = null),
            ("cardModelScalingFactor", "1.25", unit => unit.cardModelScalingFactor = 1.25f,
                unit => unit.cardModelScalingFactor = null),
            ("coinsAmount", "75", unit => unit.coinsAmount = 75, unit => unit.coinsAmount = null),
            ("firePointCount", "3", unit => unit.firePointCount = 3, unit => unit.firePointCount = null)
        };

        foreach (var field in fields)
        {
            ContentPackDefinition invalid = Clone(nativeSample);
            ContentUnitDefinition unit = invalid.units[0];
            field.SetValue(unit);
            unit.balanceOverrides = unit.balanceOverrides
                .Where(entry => entry.name != field.Name)
                .Append(new ContentBalanceOverride { name = field.Name, value = "2" })
                .ToArray();
            WithPackage(invalid, (pack, root) =>
            {
                ContentPackValidationResult result = ContentPackValidator.Validate(
                    pack, root, new EmptySnapshot(), new KnownModuleValidator());
                int rawIndex = Array.FindIndex(pack.units[0].balanceOverrides,
                    entry => entry != null && entry.name == field.Name);
                string rawPath = "units[0].balanceOverrides[" + rawIndex + "]";
                if (rawIndex < 0 || !result.Diagnostics.Any(diagnostic =>
                        diagnostic.Code == "unit.balance-override-redundant-with-structured" &&
                        diagnostic.Path == rawPath))
                {
                    throw new InvalidOperationException("Expected duplicate diagnostic for " + field.Name +
                        " at " + rawPath + ", got: " +
                        string.Join(", ", result.Diagnostics.Select(diagnostic => diagnostic.Code + " at " + diagnostic.Path)));
                }
            });

            ContentPackDefinition valid = Clone(nativeSample);
            unit = valid.units[0];
            field.ClearValue(unit);
            unit.balanceOverrides = unit.balanceOverrides
                .Where(entry => entry.name != field.Name)
                .Append(new ContentBalanceOverride { name = field.Name, value = field.Value })
                .ToArray();
            WithPackage(valid, (pack, root) =>
            {
                ContentPackValidationResult result = ContentPackValidator.Validate(
                    pack, root, new EmptySnapshot(), new KnownModuleValidator());
                int rawIndex = Array.FindIndex(pack.units[0].balanceOverrides,
                    entry => entry != null && entry.name == field.Name);
                string rawPath = "units[0].balanceOverrides[" + rawIndex + "]";
                if (rawIndex < 0 || result.Diagnostics.Any(diagnostic =>
                        diagnostic.Path == rawPath &&
                        (diagnostic.Code == "unit.balance-override-invalid" ||
                            diagnostic.Code == "unit.balance-override-redundant-with-structured")))
                {
                    throw new InvalidOperationException("Raw override " + field.Name +
                        " without its structured value was rejected at " + rawPath + ": " +
                        string.Join(", ", result.Diagnostics.Select(diagnostic => diagnostic.Code + " at " + diagnostic.Path)));
                }
            });
        }
    }),
    ("unit structured balancing parameters accept valid ranges and reject invalid or out-of-bounds values", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentUnitDefinition unit = pack.units[0];
            unit.combatValue = 500;
            unit.gridSize = 2;
            unit.cardModelScalingFactor = 1.25f;
            unit.coinsAmount = 75;
            unit.firePointCount = 2;

            ContentPackValidationResult result = ContentPackValidator.Validate(
                pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (result.Diagnostics.Any(d => d.Code.StartsWith("unit.") && d.Code.EndsWith("-invalid")))
            {
                throw new InvalidOperationException("Valid unit structured balancing parameters were rejected: " +
                    string.Join(", ", result.Diagnostics.Select(d => d.Code)));
            }

            // Test boundary 16777216
            unit.combatValue = UnitBalanceOverrideMapper.MaximumContiguousSingleExactInteger;
            unit.coinsAmount = UnitBalanceOverrideMapper.MaximumContiguousSingleExactInteger;
            result = ContentPackValidator.Validate(pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (result.Diagnostics.Any(d => d.Code == "unit.combatValue-invalid" || d.Code == "unit.coinsAmount-invalid"))
            {
                throw new InvalidOperationException("Boundary 16,777,216 was rejected.");
            }

            // Test overflow 16777217
            unit.combatValue = UnitBalanceOverrideMapper.MaximumContiguousSingleExactInteger + 1;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.combatValue-invalid");
            unit.combatValue = 100;

            unit.coinsAmount = UnitBalanceOverrideMapper.MaximumContiguousSingleExactInteger + 1;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.coinsAmount-invalid");
            unit.coinsAmount = 50;

            // Test negative combatValue
            unit.combatValue = -1;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.combatValue-invalid");
            unit.combatValue = null;

            // Test invalid gridSize
            unit.gridSize = 0;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.gridSize-invalid");
            unit.gridSize = 17;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.gridSize-invalid");
            unit.gridSize = null;

            // Test invalid cardModelScalingFactor
            unit.cardModelScalingFactor = 0f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.cardModelScalingFactor-invalid");
            unit.cardModelScalingFactor = 101f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.cardModelScalingFactor-invalid");
            unit.cardModelScalingFactor = float.NaN;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.cardModelScalingFactor-invalid");
            unit.cardModelScalingFactor = null;

            // Test invalid firePointCount
            unit.firePointCount = 0;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.firePointCount-invalid");
            unit.firePointCount = 65;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.firePointCount-invalid");
            unit.firePointCount = null;
        });
    }),
    ("new AI modules factory-reinforcement and flanking-strike are registered and validate parameters", () =>
    {
        var registry = new AiModuleRegistry();
        registry.Register(new FactoryReinforcementAiModule());
        registry.Register(new FlankingStrikeAiModule());

        if (!registry.TryValidate("sample:factory-reinforcement", null, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Factory reinforcement module failed validation: " + diagnostic);
        }

        if (!registry.TryValidate("sample:flanking-strike", null, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Flanking strike module failed validation: " + diagnostic);
        }
    }),
    ("unit native grounding parameters accept valid values and reject invalid values", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentUnitDefinition unit = pack.units[0];
            unit.balanceOverrides = null;

            unit.copyUpgradesToEntityId = "sample:custom-tank";
            unit.roles = new[] { "Unit", "Tank", "FrontalAttacker" };
            unit.attackType = "Artillery";
            unit.skillType = "LocationAoE";
            unit.rarity = "Rare";
            unit.tech = "Mech";
            unit.neededExperienceLevel = 5;
            unit.productionBuilding.balanceOverrides = new[]
            {
                new ContentBalanceOverride { name = "neededExperienceLevel", value = "5" },
                new ContentBalanceOverride { name = "rarity", value = "Rare" }
            };

            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());

            // copyUpgradesToEntityId cannot match unit.id
            unit.copyUpgradesToEntityId = unit.id;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.copyUpgradesToEntityId-invalid");
            unit.copyUpgradesToEntityId = null;

            // duplicate roles rejected
            unit.roles = new[] { "Tank", "Tank" };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.roles-invalid");

            // invalid role name rejected
            unit.roles = new[] { "NotARealRole" };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.roles-invalid");
            unit.roles = null;

            // invalid attackType rejected
            unit.attackType = "LaserBeam";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.attackType-invalid");
            unit.attackType = null;

            // invalid skillType rejected
            unit.skillType = "SuperNuke";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.skillType-invalid");
            unit.skillType = null;

            // invalid rarity rejected
            unit.rarity = "Mythic";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.rarity-invalid");
            unit.rarity = null;

            // invalid tech rejected
            unit.tech = "Alien";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.tech-invalid");
            unit.tech = null;

            // neededExperienceLevel out of bounds
            unit.neededExperienceLevel = -1;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.neededExperienceLevel-invalid");
            unit.neededExperienceLevel = 101;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit.neededExperienceLevel-invalid");
            unit.neededExperienceLevel = null;
        });
    }),
    ("focus-fire AI module parses defaults and validates limits", () =>
    {
        if (!FocusFireParameterParser.TryParse(null, out FocusFireSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Focus-fire default parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != -1 || settings.CheckIntervalSeconds != 3f || settings.MinUnitsInSquad != 2 || settings.PriorityMode != "LowestHealth")
        {
            throw new InvalidOperationException("Focus-fire default values did not match specification.");
        }

        var validParams = new[]
        {
            new ContentModuleParameter { name = "baseIndex", value = "1" },
            new ContentModuleParameter { name = "checkIntervalSeconds", value = "5.0" },
            new ContentModuleParameter { name = "squadRadius", value = "25.0" },
            new ContentModuleParameter { name = "minUnitsInSquad", value = "4" },
            new ContentModuleParameter { name = "priorityMode", value = "HighestThreat" },
            new ContentModuleParameter { name = "targetRole", value = "Sniper" }
        };
        if (!FocusFireParameterParser.TryParse(validParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Focus-fire custom parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != 1 || settings.SquadRadius != 25f || settings.PriorityMode != "HighestThreat")
        {
            throw new InvalidOperationException("Focus-fire parsed custom values incorrect.");
        }

        var invalidParams = new[]
        {
            new ContentModuleParameter { name = "priorityMode", value = "InvalidMode" }
        };
        if (FocusFireParameterParser.TryParse(invalidParams, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Focus-fire should reject invalid priorityMode.");
        }
    }),
    ("construction-expansion AI module parses defaults and validates limits", () =>
    {
        if (!ConstructionExpansionParameterParser.TryParse(null, out ConstructionExpansionSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Construction-expansion default parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != 0 || settings.CheckIntervalSeconds != 12f || settings.DesiredCount != 2 || settings.BuildingId != "PCXTurret")
        {
            throw new InvalidOperationException("Construction-expansion default values did not match specification.");
        }

        var validParams = new[]
        {
            new ContentModuleParameter { name = "baseIndex", value = "2" },
            new ContentModuleParameter { name = "checkIntervalSeconds", value = "20.0" },
            new ContentModuleParameter { name = "buildingId", value = "PCXRefinery" },
            new ContentModuleParameter { name = "desiredCount", value = "3" },
            new ContentModuleParameter { name = "queuePriority", value = "15" },
            new ContentModuleParameter { name = "minSurplusCrystals", value = "300" }
        };
        if (!ConstructionExpansionParameterParser.TryParse(validParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Construction-expansion custom parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != 2 || settings.BuildingId != "PCXRefinery" || settings.MinSurplusCrystals != 300)
        {
            throw new InvalidOperationException("Construction-expansion parsed values incorrect.");
        }

        var invalidParams = new[]
        {
            new ContentModuleParameter { name = "desiredCount", value = "0" }
        };
        if (ConstructionExpansionParameterParser.TryParse(invalidParams, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Construction-expansion should reject desiredCount < 1.");
        }
    }),
    ("tactical-regroup AI module parses defaults and validates limits", () =>
    {
        if (!TacticalRegroupParameterParser.TryParse(null, out TacticalRegroupSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Tactical-regroup default parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != -1 || settings.CheckIntervalSeconds != 4f || settings.HealthRetreatThreshold != 0.30f || settings.RegroupMinSquadSize != 3)
        {
            throw new InvalidOperationException("Tactical-regroup default values did not match specification.");
        }

        var validParams = new[]
        {
            new ContentModuleParameter { name = "baseIndex", value = "1" },
            new ContentModuleParameter { name = "healthRetreatThreshold", value = "0.25" },
            new ContentModuleParameter { name = "regroupMinSquadSize", value = "5" },
            new ContentModuleParameter { name = "regroupDistanceToBase", value = "30.0" }
        };
        if (!TacticalRegroupParameterParser.TryParse(validParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Tactical-regroup custom parse failed: " + diagnostic);
        }
        if (settings.BaseIndex != 1 || settings.HealthRetreatThreshold != 0.25f || settings.RegroupMinSquadSize != 5)
        {
            throw new InvalidOperationException("Tactical-regroup parsed values incorrect.");
        }

        var invalidParams = new[]
        {
            new ContentModuleParameter { name = "healthRetreatThreshold", value = "0.80" }
        };
        if (TacticalRegroupParameterParser.TryParse(invalidParams, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Tactical-regroup should reject retreat threshold > 0.60.");
        }
    }),
    ("focus-fire rejects an unknown targetRole when target-role priority is selected", () =>
    {
        var parameters = new[]
        {
            new ContentModuleParameter { name = "priorityMode", value = "TargetRole" },
            new ContentModuleParameter { name = "targetRole", value = "NotAUnitRole" }
        };
        if (FocusFireParameterParser.TryParse(parameters, out _, out string diagnostic) ||
            String.IsNullOrWhiteSpace(diagnostic))
        {
            throw new InvalidOperationException("Focus-fire should reject an unknown targetRole.");
        }
    }),
    ("tactical-regroup does not count another base as regroup reinforcement", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var wounded = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentHealth = 20f,
                MaxHealth = 100f,
                Position = new UnityEngine.Vector3(20f, 0f, 0f)
            };
            ExistingControllers.Instance.AiUnits().Add(wounded);
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                IsControlledByAi = true,
                IsBuilding = true,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero
            });
            for (int i = 0; i < 2; i++)
            {
                ExistingControllers.Instance.AiUnits().Add(new EntityController
                {
                    IsControlledByAi = true,
                    IsUnit = true,
                    IsCombatUnit = true,
                    CanAttackAndIsAttackEnabled = true,
                    CanMoveAndMovementIsEnabled = true,
                    aiBaseIndex = 1,
                    Position = wounded.Position
                });
                ExistingControllers.Instance.AiUnits().Add(new EntityController
                {
                    IsControlledByAi = true,
                    IsUnit = true,
                    CanAttackAndIsAttackEnabled = true,
                    CanMoveAndMovementIsEnabled = true,
                    aiBaseIndex = 0,
                    Position = wounded.Position
                });
            }

            behaviour = new TacticalRegroupAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "baseIndex", value = "0" },
                    new ContentModuleParameter { name = "checkIntervalSeconds", value = "4" },
                    new ContentModuleParameter { name = "regroupMinSquadSize", value = "2" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 4f;
            behaviour.Tick();
            if (!wounded.LastExecutedCommand.HasValue ||
                wounded.LastExecutedCommand.Value.command != EntityCommand.Command.Move)
            {
                throw new InvalidOperationException("The wounded unit did not receive its initial retreat order.");
            }

            int initialOrderCount = wounded.ExecuteCommandCallCount;
            UnityEngine.Time.time = 8f;
            behaviour.Tick();
            UnityEngine.Time.time = 12f;
            behaviour.Tick();
            if (wounded.ExecuteCommandCallCount != initialOrderCount)
            {
                throw new InvalidOperationException("Units from another AI base incorrectly released the retreat state.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("tactical-regroup does not duplicate a retreat order after reinforcement", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var wounded = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                CurrentHealth = 20f,
                MaxHealth = 100f,
                Position = new UnityEngine.Vector3(20f, 0f, 0f)
            };
            ExistingControllers.Instance.AiUnits().Add(wounded);
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                IsControlledByAi = true,
                IsBuilding = true,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero
            });
            ExistingControllers.Instance.AiUnits().Add(new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                CanAttackAndIsAttackEnabled = true,
                CanMoveAndMovementIsEnabled = true,
                aiBaseIndex = 0,
                Position = wounded.Position
            });

            behaviour = new TacticalRegroupAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "baseIndex", value = "0" },
                    new ContentModuleParameter { name = "checkIntervalSeconds", value = "4" },
                    new ContentModuleParameter { name = "regroupMinSquadSize", value = "2" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 4f;
            behaviour.Tick();
            int initialOrderCount = wounded.ExecuteCommandCallCount;
            UnityEngine.Time.time = 8f;
            behaviour.Tick();
            if (wounded.ExecuteCommandCallCount != initialOrderCount)
            {
                throw new InvalidOperationException("The retreat order was duplicated while the unit remained reinforced.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
        }
    }),
    ("tactical AI modules focus-fire, construction-expansion, and tactical-regroup are registered", () =>
    {
        var registry = new AiModuleRegistry();
        registry.Register(new FocusFireAiModule());
        registry.Register(new ConstructionExpansionAiModule());
        registry.Register(new TacticalRegroupAiModule());

        if (!registry.TryValidate("sample:focus-fire", null, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Focus-fire module failed registry validation: " + diagnostic);
        }
        if (!registry.TryValidate("sample:construction-expansion", null, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Construction-expansion module failed registry validation: " + diagnostic);
        }
        if (!registry.TryValidate("sample:tactical-regroup", null, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Tactical-regroup module failed registry validation: " + diagnostic);
        }
    }),
    ("unit systemTags accepts valid flags and rejects unknown, duplicate, or out-of-bounds tags", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentUnitDefinition unit = pack.units[0];
            unit.systemTags = new[] { "Mech", "Armor", "Tank" };

            ContentPackValidationResult result = ContentPackValidator.Validate(
                pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (result.Diagnostics.Any(d => d.Code.Contains("systemTags")))
            {
                throw new InvalidOperationException("Valid systemTags were rejected: " +
                    string.Join(", ", result.Diagnostics.Select(d => d.Code + " at " + d.Path)));
            }

            // Duplicate tag
            unit.systemTags = new[] { "Mech", "Mech" };
            result = ContentPackValidator.Validate(pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (!result.Diagnostics.Any(d => d.Code == "unit.systemTags-duplicate"))
            {
                throw new InvalidOperationException("Duplicate systemTags was not rejected.");
            }

            // Unknown tag
            unit.systemTags = new[] { "Mech", "InvalidTag" };
            result = ContentPackValidator.Validate(pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (!result.Diagnostics.Any(d => d.Code == "unit.systemTags-invalid"))
            {
                throw new InvalidOperationException("Unknown systemTags flag was not rejected.");
            }

            // Length > 16
            unit.systemTags = Enumerable.Repeat("Mech", 17).ToArray();
            result = ContentPackValidator.Validate(pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (!result.Diagnostics.Any(d => d.Code == "unit.systemTags-invalid" && d.Message.Contains("16")))
            {
                throw new InvalidOperationException("systemTags exceeding 16 entries was not rejected.");
            }
        });
    }),
    ("unit baselineStatusEffects accepts valid flags and rejects unknown, duplicate, or out-of-bounds effects", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentUnitDefinition unit = pack.units[0];
            unit.baselineStatusEffects = new[] { "OnFire", "SlowedDown" };

            ContentPackValidationResult result = ContentPackValidator.Validate(
                pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (result.Diagnostics.Any(d => d.Code.Contains("baselineStatusEffects")))
            {
                throw new InvalidOperationException("Valid baselineStatusEffects were rejected: " +
                    string.Join(", ", result.Diagnostics.Select(d => d.Code + " at " + d.Path)));
            }

            // Duplicate effect
            unit.baselineStatusEffects = new[] { "OnFire", "OnFire" };
            result = ContentPackValidator.Validate(pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (!result.Diagnostics.Any(d => d.Code == "unit.baselineStatusEffects-duplicate"))
            {
                throw new InvalidOperationException("Duplicate baselineStatusEffect was not rejected.");
            }

            // Unknown effect
            unit.baselineStatusEffects = new[] { "NotAStatusEffect" };
            result = ContentPackValidator.Validate(pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (!result.Diagnostics.Any(d => d.Code == "unit.baselineStatusEffects-invalid"))
            {
                throw new InvalidOperationException("Unknown baselineStatusEffects flag was not rejected.");
            }

            // Length > 16
            unit.baselineStatusEffects = Enumerable.Repeat("OnFire", 17).ToArray();
            result = ContentPackValidator.Validate(pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (!result.Diagnostics.Any(d => d.Code == "unit.baselineStatusEffects-invalid" && d.Message.Contains("16")))
            {
                throw new InvalidOperationException("baselineStatusEffects exceeding 16 entries was not rejected.");
            }
        });
    }),
    ("unit deep clone safely duplicates systemTags and baselineStatusEffects arrays", () =>
    {
        var original = new ContentPackDefinition
        {
            units = new[]
            {
                new ContentUnitDefinition
                {
                    id = "test-unit",
                    systemTags = new[] { "Mech", "Armor" },
                    baselineStatusEffects = new[] { "OnFire" }
                }
            }
        };
        var cloned = ContentPackCopy.DeepClone(original);
        if (cloned?.units == null || cloned.units.Length != 1)
        {
            throw new InvalidOperationException("Cloned pack or units was null.");
        }
        var clonedUnit = cloned.units[0];
        if (clonedUnit.systemTags == null || clonedUnit.systemTags.Length != 2 || ReferenceEquals(clonedUnit.systemTags, original.units[0].systemTags))
        {
            throw new InvalidOperationException("systemTags was not deeply cloned.");
        }
        if (clonedUnit.baselineStatusEffects == null || clonedUnit.baselineStatusEffects.Length != 1 || ReferenceEquals(clonedUnit.baselineStatusEffects, original.units[0].baselineStatusEffects))
        {
            throw new InvalidOperationException("baselineStatusEffects was not deeply cloned.");
        }

        var nullArrays = new ContentPackDefinition
        {
            units = new[] { new ContentUnitDefinition { id = "null-unit" } }
        };
        var nullCloned = ContentPackCopy.DeepClone(nullArrays);
        if (nullCloned.units[0].systemTags != null || nullCloned.units[0].baselineStatusEffects != null)
        {
            throw new InvalidOperationException("Null arrays should remain null after clone.");
        }
    }),
    ("unit balance-override offeredSystemTags is rejected when structured systemTags is present", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentUnitDefinition unit = pack.units[0];
            unit.systemTags = new[] { "Mech" };
            unit.balanceOverrides = unit.balanceOverrides
                .Concat(new[] { new ContentBalanceOverride { name = "offeredSystemTags", value = "16" } })
                .ToArray();

            ContentPackValidationResult result = ContentPackValidator.Validate(
                pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (!result.Diagnostics.Any(d => d.Code == "unit.balance-override-redundant-with-structured"))
            {
                throw new InvalidOperationException("Redundant offeredSystemTags balanceOverride was not rejected.");
            }
        });
    }),
    ("squad-formation AI module parses defaults and validates limits", () =>
    {
        if (!SquadFormationParameterParser.TryParse(null, out SquadFormationSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Squad-formation default parse failed: " + diagnostic);
        }
        if (settings.AnchorRole != "Tank" || settings.MaxFollowersPerCaptain != 4 || settings.FormationRadiusCells != 10f || settings.CooldownSeconds != 5f || settings.BaseIndex != -1)
        {
            throw new InvalidOperationException("Squad-formation default values did not match specification.");
        }

        var validParams = new[]
        {
            new ContentModuleParameter { name = "anchorRole", value = "Mech" },
            new ContentModuleParameter { name = "maxFollowersPerCaptain", value = "6" },
            new ContentModuleParameter { name = "formationRadiusCells", value = "20" },
            new ContentModuleParameter { name = "cooldownSeconds", value = "2.5" },
            new ContentModuleParameter { name = "baseIndex", value = "1" }
        };
        if (!SquadFormationParameterParser.TryParse(validParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Squad-formation custom parse failed: " + diagnostic);
        }
        if (settings.AnchorRole != "Mech" || settings.MaxFollowersPerCaptain != 6 || settings.FormationRadiusCells != 20f || settings.BaseIndex != 1)
        {
            throw new InvalidOperationException("Squad-formation parsed values incorrect.");
        }

        var invalidParams = new[]
        {
            new ContentModuleParameter { name = "maxFollowersPerCaptain", value = "25" }
        };
        if (SquadFormationParameterParser.TryParse(invalidParams, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Squad-formation should reject maxFollowersPerCaptain > 16.");
        }

        var invalidRoleParams = new[]
        {
            new ContentModuleParameter { name = "anchorRole", value = "InvalidUnitRole" }
        };
        if (SquadFormationParameterParser.TryParse(invalidRoleParams, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Squad-formation should reject invalid anchorRole.");
        }
    }),
    ("squad-formation AI module binds followers to anchor captain and releases on destroy", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        EntityBalancingStore.Reset();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            EntityBalancingStore.RolesOf["TankUnit"] = UnitRole.Tank;
            EntityBalancingStore.RolesOf["TrooperUnit"] = UnitRole.Unit;

            var captain = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                EntityId = "TankUnit",
                UniqueEntityId = 101,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero
            };
            var follower = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                EntityId = "TrooperUnit",
                UniqueEntityId = 102,
                aiBaseIndex = 0,
                Position = new UnityEngine.Vector3(5f, 0f, 0f)
            };

            ExistingControllers.Instance.AiUnits().Add(captain);
            ExistingControllers.Instance.AiUnits().Add(follower);

            behaviour = new SquadFormationAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "anchorRole", value = "Tank" },
                    new ContentModuleParameter { name = "baseIndex", value = "0" },
                    new ContentModuleParameter { name = "cooldownSeconds", value = "2" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 2f;
            behaviour.Tick();

            if (follower.CaptainOfThisEntity != captain)
            {
                throw new InvalidOperationException("Follower was not bound to captain.");
            }

            behaviour.OnDestroyed();
            if (follower.CaptainOfThisEntity != null)
            {
                throw new InvalidOperationException("Follower captain was not released upon OnDestroyed().");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
            EntityBalancingStore.Reset();
        }
    }),
    ("tactical-skill-activation AI module parses defaults and validates limits", () =>
    {
        if (!TacticalSkillActivationParameterParser.TryParse(null, out TacticalSkillActivationSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Tactical-skill-activation default parse failed: " + diagnostic);
        }
        if (settings.EnemyTriggerRadiusCells != 10f || settings.MinManaRatio != 0.5f || settings.CooldownSeconds != 4f || settings.BaseIndex != -1)
        {
            throw new InvalidOperationException("Tactical-skill-activation default values did not match specification.");
        }

        var validParams = new[]
        {
            new ContentModuleParameter { name = "enemyTriggerRadiusCells", value = "18" },
            new ContentModuleParameter { name = "minManaRatio", value = "0.3" },
            new ContentModuleParameter { name = "cooldownSeconds", value = "1.5" },
            new ContentModuleParameter { name = "baseIndex", value = "2" }
        };
        if (!TacticalSkillActivationParameterParser.TryParse(validParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Tactical-skill-activation custom parse failed: " + diagnostic);
        }
        if (settings.EnemyTriggerRadiusCells != 18f || settings.MinManaRatio != 0.3f || settings.BaseIndex != 2)
        {
            throw new InvalidOperationException("Tactical-skill-activation parsed values incorrect.");
        }

        var invalidRadius = new[]
        {
            new ContentModuleParameter { name = "enemyTriggerRadiusCells", value = "100" }
        };
        if (TacticalSkillActivationParameterParser.TryParse(invalidRadius, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Tactical-skill-activation should reject triggerRadius > 64.");
        }

        var invalidMana = new[]
        {
            new ContentModuleParameter { name = "minManaRatio", value = "1.5" }
        };
        if (TacticalSkillActivationParameterParser.TryParse(invalidMana, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Tactical-skill-activation should reject minManaRatio > 1.0.");
        }
    }),
    ("tactical-skill-activation AI module dispatches active skill when enemy is detected in range", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        EntityBalancingStore.Reset();
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var aiCaster = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                EntityId = "MageUnit",
                UniqueEntityId = 201,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero,
                CurrentMana = 100f,
                SkillInfo = new SkillInfo { targetOrigin = TargetOrigin.ChosenEntity }
            };
            var playerEnemy = new EntityController
            {
                IsControlledByPlayer = true,
                IsUnit = true,
                UniqueEntityId = 301,
                Position = new UnityEngine.Vector3(8f, 0f, 0f)
            };

            ExistingControllers.Instance.AiUnits().Add(aiCaster);
            ExistingControllers.Instance.PlayerEntities().Add(playerEnemy);

            behaviour = new TacticalSkillActivationAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "enemyTriggerRadiusCells", value = "12" },
                    new ContentModuleParameter { name = "cooldownSeconds", value = "1" },
                    new ContentModuleParameter { name = "baseIndex", value = "0" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 1f;
            behaviour.Tick();

            if (!aiCaster.LastExecutedCommand.HasValue ||
                aiCaster.LastExecutedCommand.Value.command != EntityCommand.Command.ActiveSkillOnTarget)
            {
                throw new InvalidOperationException("Tactical-skill-activation did not execute ActiveSkillOnTarget command.");
            }
            if (aiCaster.LastCommandProcessingType != EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand)
            {
                throw new InvalidOperationException("Tactical-skill-activation did not use ExecuteBeforeCurrentCommand.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
            EntityBalancingStore.Reset();
        }
    }),
    ("wave-escalation AI module parses defaults and validates limits", () =>
    {
        if (!WaveEscalationParameterParser.TryParse(null, out WaveEscalationSettings settings, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Wave-escalation default parse failed: " + diagnostic);
        }
        if (settings.WaveIntervalSeconds != 60f || settings.MaxWaves != 10 || settings.BaseIndex != -1)
        {
            throw new InvalidOperationException("Wave-escalation default values did not match specification.");
        }

        var validParams = new[]
        {
            new ContentModuleParameter { name = "waveIntervalSeconds", value = "30" },
            new ContentModuleParameter { name = "maxWaves", value = "5" },
            new ContentModuleParameter { name = "baseIndex", value = "0" }
        };
        if (!WaveEscalationParameterParser.TryParse(validParams, out settings, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Wave-escalation custom parse failed: " + diagnostic);
        }
        if (settings.WaveIntervalSeconds != 30f || settings.MaxWaves != 5 || settings.BaseIndex != 0)
        {
            throw new InvalidOperationException("Wave-escalation parsed values incorrect.");
        }

        var invalidInterval = new[]
        {
            new ContentModuleParameter { name = "waveIntervalSeconds", value = "3" }
        };
        if (WaveEscalationParameterParser.TryParse(invalidInterval, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Wave-escalation should reject waveIntervalSeconds < 5.");
        }

        var invalidMaxWaves = new[]
        {
            new ContentModuleParameter { name = "maxWaves", value = "150" }
        };
        if (WaveEscalationParameterParser.TryParse(invalidMaxWaves, out _, out diagnostic) || diagnostic == null)
        {
            throw new InvalidOperationException("Wave-escalation should reject maxWaves > 100.");
        }
    }),
    ("wave-escalation AI module increments attack wave counter and orders assault", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        AiBehaviourManager.CurrentAttackWaveNumber = 0;
        UnityEngine.Time.time = 0f;
        IRogueCommandAiRuntimeBehaviour? behaviour = null;
        try
        {
            var hq = new EntityController
            {
                IsControlledByPlayer = true,
                IsBuilding = true,
                EntityId = "EngineeringHq",
                Position = new UnityEngine.Vector3(50f, 0f, 0f)
            };
            ExistingControllers.Instance.EngineeringHqEntity = hq;

            var aiUnit = new EntityController
            {
                IsControlledByAi = true,
                IsUnit = true,
                IsCombatUnit = true,
                UniqueEntityId = 501,
                aiBaseIndex = 0,
                Position = UnityEngine.Vector3.zero
            };
            ExistingControllers.Instance.AiUnits().Add(aiUnit);

            behaviour = new WaveEscalationAiModule().CreateBehaviour(new DefaultAiBehaviour(),
                [
                    new ContentModuleParameter { name = "waveIntervalSeconds", value = "10" },
                    new ContentModuleParameter { name = "maxWaves", value = "3" },
                    new ContentModuleParameter { name = "baseIndex", value = "0" }
                ]);
            behaviour.OnInitialized();
            UnityEngine.Time.time = 10f;
            behaviour.Tick();

            if (AiBehaviourManager.CurrentAttackWaveNumber != 1)
            {
                throw new InvalidOperationException("Wave escalation did not increment CurrentAttackWaveNumber.");
            }
            if (!aiUnit.LastExecutedCommand.HasValue ||
                aiUnit.LastExecutedCommand.Value.command != EntityCommand.Command.AttackMoveOnTarget)
            {
                throw new InvalidOperationException("Wave escalation did not dispatch AttackMoveOnTarget to HQ.");
            }
        }
        finally
        {
            behaviour?.OnDestroyed();
            ExistingControllers.Instance.Reset();
            DefaultAiBehaviour.ResetRuntimeState();
            AiBehaviourManager.CurrentAttackWaveNumber = 0;
        }
    }),
    ("tactical AI modules squad-formation, tactical-skill-activation, and wave-escalation are registered", () =>
    {
        var registry = new AiModuleRegistry();
        registry.Register(new SquadFormationAiModule());
        registry.Register(new TacticalSkillActivationAiModule());
        registry.Register(new WaveEscalationAiModule());

        if (!registry.TryValidate("sample:squad-formation", null, out string diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Squad-formation module failed registry validation: " + diagnostic);
        }
        if (!registry.TryValidate("sample:tactical-skill-activation", null, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Tactical-skill-activation module failed registry validation: " + diagnostic);
        }
        if (!registry.TryValidate("sample:wave-escalation", null, out diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Wave-escalation module failed registry validation: " + diagnostic);
        }
    }),
};

int failures = 0;
int skipped = 0;
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (SkipTestException exception)
    {
        skipped++;
        Console.WriteLine($"SKIP {name}: {exception.Message}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures - skipped}/{tests.Length} validation checks passed; {skipped} skipped.");
return failures == 0 ? 0 : 1;

static StageGenerationAiCandidate ValidStageAi(string id)
{
    return new StageGenerationAiCandidate
    {
        Id = id,
        IsDeactivated = false,
        IsAllowedForDemo = true,
        StageMin = 0,
        StageMax = 100,
        AscensionMin = 0,
        AscensionMax = 100,
        Bases = [new StageGenerationAiBaseCandidate
        {
            StartingInStage = 0,
            StartingInAscensionLevel = 0
        }],
        RestrictedMaps = []
    };
}

static StageGenerationWorldCandidate ValidStageWorld(string id)
{
    return new StageGenerationWorldCandidate
    {
        Id = id,
        IsAllowedForDemo = true,
        RestrictedMaps = []
    };
}

static StageGenerationLandscapeCandidate ValidStageLandscape(string id, int mapSize)
{
    return new StageGenerationLandscapeCandidate
    {
        Id = id,
        MapSize = mapSize,
        IsDeactivated = false,
        IsAllowedForDemo = true,
        RestrictedWorlds = []
    };
}

static void WithPackage(ContentPackDefinition source, Action<ContentPackDefinition, string> action)
{
    string root = Directory.CreateTempSubdirectory("rogue-command-validation-").FullName;

    string ownerToken = Guid.NewGuid().ToString("N");
    string ownerMarker = Path.Combine(root, ".fixture-owner");
    bool created = true;
    try
    {
        EnsureNormalDirectory(root);
        using (var marker = new FileStream(ownerMarker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(marker))
        {
            writer.Write(ownerToken);
        }

        string bundlesDirectory = Path.Combine(root, "bundles");
        Directory.CreateDirectory(bundlesDirectory);
        EnsureNormalDirectory(bundlesDirectory);

        ContentPackDefinition pack = Clone(source);
        if (pack.assetBundles.Length > 0)
        {
            const string fixtureManifestPath = "bundles/test-manifest.bundle";
            pack.assetBundleManifestPath = fixtureManifestPath;
            File.WriteAllBytes(Path.Combine(bundlesDirectory, "test-manifest.bundle"), [0x01]);
        }
        else
        {
            pack.assetBundleManifestPath = null;
        }

        for (int index = 0; index < pack.assetBundles.Length; index++)
        {
            ContentAssetBundleDefinition bundle = pack.assetBundles[index];
            string fixtureBundleName = $"test-content-{index}.bundle";
            bundle.path = "bundles/" + fixtureBundleName;
            string bundlePath = Path.Combine(bundlesDirectory, fixtureBundleName);
            File.WriteAllBytes(bundlePath, [0x02]);
        }

        action(pack, root);
    }
    finally
    {
        if (created && Directory.Exists(root))
        {
            try
            {
                if (!HasOwnershipMarker(root, ownerMarker, ownerToken) || !DeleteFixtureTree(root))
                {
                    Console.Error.WriteLine($"Skipped cleanup because fixture ownership or directory safety could not be verified: {root}");
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Could not safely clean validation fixture '{root}': {exception.Message}");
            }
        }
    }
}

static void WithOwnedTemporaryDirectory(string prefix, Action<string> action)
{
    string root = Directory.CreateTempSubdirectory(prefix).FullName;
    string ownerToken = Guid.NewGuid().ToString("N");
    string ownerMarker = Path.Combine(root, ".fixture-owner");
    try
    {
        EnsureNormalDirectory(root);
        using (var marker = new FileStream(ownerMarker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(marker))
        {
            writer.Write(ownerToken);
        }

        action(root);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            try
            {
                if (!HasOwnershipMarker(root, ownerMarker, ownerToken) || !DeleteFixtureTree(root))
                {
                    Console.Error.WriteLine($"Skipped cleanup because fixture ownership or directory safety could not be verified: {root}");
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Could not safely clean compatibility fixture '{root}': {exception.Message}");
            }
        }
    }
}

static void EnsureNormalDirectory(string path)
{
    if (!IsNormalDirectory(path))
    {
        throw new IOException($"Validation fixture path is not a normal directory: {path}");
    }
}

static bool IsNormalDirectory(string path)
{
    try
    {
        FileAttributes attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.Directory) != 0
            && (attributes & FileAttributes.ReparsePoint) == 0;
    }
    catch (IOException)
    {
        return false;
    }
    catch (UnauthorizedAccessException)
    {
        return false;
    }
}

static bool HasOwnershipMarker(string root, string markerPath, string ownerToken)
{
    if (!IsNormalDirectory(root) || !File.Exists(markerPath))
    {
        return false;
    }

    try
    {
        FileAttributes markerAttributes = File.GetAttributes(markerPath);
        return (markerAttributes & FileAttributes.Directory) == 0
            && (markerAttributes & FileAttributes.ReparsePoint) == 0
            && String.Equals(File.ReadAllText(markerPath), ownerToken, StringComparison.Ordinal);
    }
    catch (IOException)
    {
        return false;
    }
    catch (UnauthorizedAccessException)
    {
        return false;
    }
}

static bool DeleteFixtureTree(string directory)
{
    if (!IsNormalDirectory(directory))
    {
        return false;
    }

    foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
    {
        FileAttributes attributes = File.GetAttributes(entry);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            if (!DeleteFixtureTree(entry))
            {
                return false;
            }
        }
        else
        {
            File.Delete(entry);
        }
    }

    Directory.Delete(directory, recursive: false);
    return true;
}

static ContentPackDefinition Clone(ContentPackDefinition source)
{
    var options = new JsonSerializerOptions { IncludeFields = true };
    return JsonSerializer.Deserialize<ContentPackDefinition>(JsonSerializer.Serialize(source, options), options)
        ?? throw new InvalidDataException("Could not clone the sample definition.");
}

static void SetUnitLoadoutVariants(ContentUnitDefinition unit, string variantsJson)
{
    FieldInfo variantsField = typeof(ContentUnitDefinition).GetField("variants")
        ?? throw new InvalidOperationException("The unit variants DTO field is missing.");
    if (!variantsField.FieldType.IsArray)
    {
        throw new InvalidOperationException("The unit variants DTO field must be an array.");
    }

    Array variants = (Array)(JsonSerializer.Deserialize(variantsJson, variantsField.FieldType,
        new JsonSerializerOptions { IncludeFields = true })
        ?? throw new InvalidOperationException("Variant JSON did not deserialize into an array."));
    variantsField.SetValue(unit, variants);
}

static void ExpectUnitLoadoutVariantDiagnostic(
    ContentPackDefinition source, string variantsJson, string expectedCode, string expectedPath)
{
    WithPackage(source, (pack, root) =>
    {
        SetUnitLoadoutVariants(pack.units[0], variantsJson);
        ContentPackValidationResult result = ContentPackValidator.Validate(
            pack, root, new EmptySnapshot(), new KnownModuleValidator());
        if (result.IsValid || !result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == expectedCode && diagnostic.Path == expectedPath))
        {
            throw new InvalidOperationException(
                $"Expected error '{expectedCode}' at '{expectedPath}', got: " +
                string.Join(", ", result.Diagnostics.Select(d => $"{d.Code} at {d.Path}")));
        }
    });
}

static void ExpectValid(
    ContentPackDefinition pack,
    string root,
    IContentRegistrySnapshot registry,
    IAiModuleValidator modules)
{
    ContentPackValidationResult result = ContentPackValidator.Validate(pack, root, registry, modules);
    if (!result.IsValid)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
    }
}

static void ExpectDiagnostic(
    ContentPackDefinition pack,
    string root,
    IContentRegistrySnapshot registry,
    IAiModuleValidator modules,
    string expectedCode)
{
    ContentPackValidationResult result = ContentPackValidator.Validate(pack, root, registry, modules);
    if (!result.Diagnostics.Any(diagnostic => diagnostic.Code == expectedCode))
    {
        throw new InvalidOperationException(
            $"Expected diagnostic '{expectedCode}', got: {string.Join(", ", result.Diagnostics.Select(x => x.Code))}");
    }
}

sealed class SnapshotWithUnits : IContentRegistrySnapshot
{
    private readonly HashSet<string> ids;
    public SnapshotWithUnits(IEnumerable<string> ids) => this.ids = new HashSet<string>(ids, StringComparer.Ordinal);
    public bool ContainsId(string id) => ids.Contains(id);
    public bool ContainsUnit(string id) => ids.Contains(id);
    public bool ContainsAiProfile(string id) => false;
}

sealed class EmptySnapshot : IContentRegistrySnapshot
{
    private readonly HashSet<string> ids;

    public EmptySnapshot(params string[] ids) => this.ids = new HashSet<string>(ids, StringComparer.Ordinal);
    public bool ContainsId(string id) => ids.Contains(id);
    public bool ContainsUnit(string id) => ids.Contains(id);
    public bool ContainsAiProfile(string id) => ids.Contains(id);
}

sealed class KnownModuleValidator : IAiModuleValidator
{
    public bool TryValidate(string moduleId, ContentModuleParameter[] parameters, out string diagnostic)
    {
        if (moduleId == "sample:harvester-raider")
        {
            return HarvesterRaiderParameterParser.TryParse(parameters, out _, out diagnostic);
        }
        if (moduleId == "sample:siege-assault")
                {
                    return SiegeAssaultParameterParser.TryParse(parameters, out _, out diagnostic);
                }
                if (moduleId == "sample:retreat-and-regroup")
                {
                    return RetreatAndRegroupParameterParser.TryParse(parameters, out _, out diagnostic);
                }
                if (moduleId == "sample:factory-reinforcement")
                {
                    return FactoryReinforcementParameterParser.TryParse(parameters, out _, out diagnostic);
                }
                if (moduleId == "sample:flanking-strike")
                {
                    return FlankingStrikeParameterParser.TryParse(parameters, out _, out diagnostic);
                }
                if (moduleId == "sample:focus-fire")
                {
                    return FocusFireParameterParser.TryParse(parameters, out _, out diagnostic);
                }
                if (moduleId == "sample:construction-expansion")
                {
                    return ConstructionExpansionParameterParser.TryParse(parameters, out _, out diagnostic);
                }
                if (moduleId == "sample:tactical-regroup")
                {
                    return TacticalRegroupParameterParser.TryParse(parameters, out _, out diagnostic);
                }

        bool known = moduleId is "sample:scheduled-base-build" or "sample:reactive-base-defense" or "sample:trace-default";
        diagnostic = known ? string.Empty : "Unknown compiled AI module: " + moduleId;
        return known;
    }
}

sealed class SkipTestException : Exception
{
    public SkipTestException(string message) : base(message)
    {
    }
}

sealed class ProductionBuildingEligibilityTestRow
{
    public bool isAllowedAsBlueprint = false;
    public bool isAllowedAsStartingBlueprint = false;
    public bool isAllowedForAi = false;
    public bool isAllowedForDemo = false;
}
