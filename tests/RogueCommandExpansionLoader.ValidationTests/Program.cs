#nullable enable
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using RogueCommandExpansionLoader.Compatibility;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Modules;
using RogueCommandExpansionLoader.Runtime;

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
    ?? throw new InvalidDataException("The reactive AI sample content manifest is empty.");

var tests = new (string Name, Action Run)[]
{
    ("runtime Harmony patch installer hook names resolve to declared methods", () =>
    {
        string[] hookNames =
        [
            "EntityBalancingStoreInitPostfix",
            "SetScriptableObjectPostfix",
            "CreateStageMapPrefix",
            "CreateLevelMapPrefix",
            "CreateLevelMapFinalizer",
            "InstantiateEntityPrefix",
            "CreateEntityMeshPrefix",
            "AiBehaviourUpdatePostfix",
            "DefaultAiBehaviourInitPostfix",
            "AiBehaviourDestroyPostfix",
            "BlueprintNamePrefix",
            "BlueprintDescriptionPrefix",
            "LandscapeLocalizationPrefix",
            "BlueprintPoolIsInPoolPrefix",
            "BlueprintPoolFindPrefix",
            "BlueprintPoolExclusiveBlueprintsPostfix"
        ];

        foreach (string hookName in hookNames)
        {
            if (typeof(RuntimeHarmonyPatches).GetMethod(hookName,
                    BindingFlags.Public | BindingFlags.Static) == null)
            {
                throw new InvalidOperationException($"RuntimePatchInstaller expects missing hook '{hookName}'.");
            }
        }

        MethodInfo? nameHook = typeof(RuntimeHarmonyPatches).GetMethod("BlueprintNamePrefix",
            BindingFlags.Public | BindingFlags.Static);
        MethodInfo? descriptionHook = typeof(RuntimeHarmonyPatches).GetMethod("BlueprintDescriptionPrefix",
            BindingFlags.Public | BindingFlags.Static);
        foreach (MethodInfo? hook in new[] { nameHook, descriptionHook })
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
            registry.AllowsModUnitInPool(firstPool, "sample:second-unit") ||
            registry.AllowsModUnitInPool(new object(), "sample:first-unit"))
        {
            throw new InvalidOperationException("Scoped unit eligibility escaped its exact cloned pool instance.");
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
            stage.aiProfileIds == null || !stage.aiProfileIds.Contains("sample-reactive-ai:base-defense"))
        {
            throw new InvalidOperationException("The counterstrike stage does not add the bike to its player and AI rosters.");
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
                new ContentBalanceOverride { name = "moveSpeed", value = "1.1" }
            ];
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
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
            unit.balanceOverrides = [new ContentBalanceOverride { name = "cost", value = "70" }];

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
    ("reactive base-defense AI parser supplies bounded defaults", () =>
    {
        if (!ReactiveBaseDefenseParameterParser.TryParse(null, out ReactiveBaseDefenseSettings defaults,
            out string diagnostic) || diagnostic != null || defaults.BaseIndex != 0 ||
            defaults.CooldownSeconds != 1f || defaults.DefenseRadiusCells != 12f || defaults.MaximumResponders != 2)
        {
            throw new InvalidOperationException("The reactive AI parser did not return its documented defaults.");
        }

        ContentModuleParameter[] boundaries =
        [
            new ContentModuleParameter { name = "baseIndex", value = "31" },
            new ContentModuleParameter { name = "cooldownSeconds", value = "60" },
            new ContentModuleParameter { name = "defenseRadiusCells", value = "64" },
            new ContentModuleParameter { name = "maxResponders", value = "4" }
        ];
        if (!ReactiveBaseDefenseParameterParser.TryParse(boundaries, out ReactiveBaseDefenseSettings maximums,
            out diagnostic) || diagnostic != null || maximums.BaseIndex != 31 || maximums.CooldownSeconds != 60f ||
            maximums.DefenseRadiusCells != 64f || maximums.MaximumResponders != 4)
        {
            throw new InvalidOperationException("The reactive AI parser rejected valid upper-bound values.");
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
    ("managed assemblies are rejected from content packs", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            File.WriteAllBytes(Path.Combine(root, "unexpected.dll"), [0x4D, 0x5A]);
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "package.dll-forbidden");
        });
    })
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
