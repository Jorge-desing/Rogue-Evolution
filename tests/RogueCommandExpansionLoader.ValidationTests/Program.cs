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
if (!File.Exists(sampleManifestPath) || !File.Exists(nativeSampleManifestPath))
{
    Console.Error.WriteLine("A required sample manifest was not found: " +
        (!File.Exists(sampleManifestPath) ? sampleManifestPath : nativeSampleManifestPath));
    return 2;
}

var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
ContentPackDefinition sample = JsonSerializer.Deserialize<ContentPackDefinition>(
    File.ReadAllText(sampleManifestPath), jsonOptions)
    ?? throw new InvalidDataException("The sample content manifest is empty.");
ContentPackDefinition nativeSample = JsonSerializer.Deserialize<ContentPackDefinition>(
    File.ReadAllText(nativeSampleManifestPath), jsonOptions)
    ?? throw new InvalidDataException("The native sample content manifest is empty.");

var tests = new (string Name, Action Run)[]
{
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
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
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
        ai.RestrictedMapSizes = [53];
        StageGenerationWorldCandidate world = ValidStageWorld("restricted-world");
        world.RestrictedMapSizes = [81];
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

        ai.RestrictedMapSizes = [];
        if (!StageGenerationCompatibility.TryValidate("sample:stage", "default", 4,
                81, 81, false, false, 19, [ai], [world], landscapes, out diagnostic))
        {
            throw new InvalidOperationException("World restricted maps should be selected when AI has no map restriction: " +
                diagnostic);
        }
    }),
    ("later pack AI compatibility is checked against already loaded stages", () =>
    {
        StageGenerationAiCandidate firstPackAi = ValidStageAi("pack-a:ai");
        firstPackAi.RestrictedMapSizes = [53];
        StageGenerationAiCandidate secondPackAi = ValidStageAi("pack-b:ai");
        secondPackAi.RestrictedMapSizes = [81];
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
                stages, [firstPackAi, secondPackAi], [], out string diagnostic) ||
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
    ("composite AI initialization failure cleans every created module in reverse", () =>
    {
        var events = new List<string>();
        var first = new RecordingAiRuntimeBehaviour(events, "first");
        var failing = new RecordingAiRuntimeBehaviour(events, "failing") { ThrowOnInitialize = true };
        var notInitialized = new RecordingAiRuntimeBehaviour(events, "not-initialized");
        var composite = new CompositeAiRuntimeBehaviour("test:profile",
            new[] { "test:first", "test:failing", "test:last" },
            new IRogueCommandAiRuntimeBehaviour[] { first, failing, notInitialized });

        try
        {
            composite.OnInitialized();
            throw new InvalidOperationException("A failing AI module did not abort composite initialization.");
        }
        catch (InvalidOperationException exception)
        {
            if (!exception.Message.Contains("test:failing", StringComparison.Ordinal))
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
    ("runtime entity mutator appends balancing rows indices and factory mappings idempotently", () =>
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
                provider.EntityRowsRegisteredCount != 1)
            {
                throw new InvalidOperationException("The runtime mutator left inconsistent registry rows, indices, or factory mappings.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime entity mutator rolls back a partial row append after index insertion fails", () =>
    {
        var comparer = new ThrowOnFirstProductHashComparer("sample:product");
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
                EntityBalancingStore.FactoryEntityIdOf.Count != 0 || provider.EntityRowsRegisteredCount != 0)
            {
                throw new InvalidOperationException("A failed index commit did not roll back all appended registry state: " +
                    "registered=" + registered +
                    ", injectedFailure=" + comparer.FailureTriggered +
                ", targetHashCalls=" + comparer.TargetHashCalls +
                ", comparerAttached=" + ReferenceEquals(EntityBalancingStore.ParameterListIndexOf.Comparer, comparer) +
                    ", rows=" + EntityBalancingStore.EntityBalancingParametersList.Count +
                    ", indices=" + EntityBalancingStore.ParameterListIndexOf.Count +
                    ", factoryMappings=" + EntityBalancingStore.FactoryEntityIdOf.Count +
                    ", notifications=" + provider.EntityRowsRegisteredCount + ".");
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
        RestrictedMapSizes = []
    };
}

static StageGenerationWorldCandidate ValidStageWorld(string id)
{
    return new StageGenerationWorldCandidate
    {
        Id = id,
        IsAllowedForDemo = true,
        RestrictedMapSizes = []
    };
}

static StageGenerationLandscapeCandidate ValidStageLandscape(string id, int mapSize)
{
    return new StageGenerationLandscapeCandidate
    {
        Id = id,
        MapSize = mapSize,
        IsDeactivated = false,
        IsAllowedForDemo = true
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
        bool known = moduleId is "sample:scheduled-base-build" or "sample:trace-default";
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
