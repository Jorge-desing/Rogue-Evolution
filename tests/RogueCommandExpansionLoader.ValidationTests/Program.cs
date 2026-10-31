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

return ValidationTestRunner.Run(args);

internal static partial class ValidationTestRunner
{
    internal static string repositoryRoot = string.Empty;
    internal static string sampleManifestPath = string.Empty;
    internal static string nativeSampleManifestPath = string.Empty;
    internal static string reactiveAiSampleManifestPath = string.Empty;
    internal static JsonSerializerOptions jsonOptions = new() { IncludeFields = true };
    internal static ContentPackDefinition sample = null!;
    internal static ContentPackDefinition nativeSample = null!;
    internal static ContentPackDefinition reactiveAiSample = null!;

    internal static int Run(string[] args)
    {
        bool isUnitGraphStackProbe = Environment.GetEnvironmentVariable("RCL_UNIT_GRAPH_STACK_PROBE") == "1";
        if (args.Length < 1 || args.Length > 2)
        {
            Console.Error.WriteLine("Usage: RogueCommandExpansionLoader.ValidationTests <repositoryRoot> [--suite <name>]");
            return 2;
        }

        repositoryRoot = Path.GetFullPath(args[0]);
        string? suiteFilter = args.Length == 2 ? args[1] : Environment.GetEnvironmentVariable("RCL_TEST_SUITE_FILTER");

        sampleManifestPath = Path.Combine(repositoryRoot, "content", "packs", "sample-expansion", "content.json");
        nativeSampleManifestPath = Path.Combine(repositoryRoot, "content", "packs", "sample-native-expansion", "content.json");
        reactiveAiSampleManifestPath = Path.Combine(repositoryRoot, "content", "packs", "sample-reactive-ai", "content.json");
        if (!File.Exists(sampleManifestPath) || !File.Exists(nativeSampleManifestPath) || !File.Exists(reactiveAiSampleManifestPath))
        {
            Console.Error.WriteLine("A required sample manifest was not found: " +
                (!File.Exists(sampleManifestPath) ? sampleManifestPath :
                    (!File.Exists(nativeSampleManifestPath) ? nativeSampleManifestPath : reactiveAiSampleManifestPath)));
            return 2;
        }

        sample = JsonSerializer.Deserialize<ContentPackDefinition>(
            File.ReadAllText(sampleManifestPath), jsonOptions)
            ?? throw new InvalidDataException("The sample content manifest is empty.");
        nativeSample = JsonSerializer.Deserialize<ContentPackDefinition>(
            File.ReadAllText(nativeSampleManifestPath), jsonOptions)
            ?? throw new InvalidDataException("The native sample content manifest is empty.");
        reactiveAiSample = JsonSerializer.Deserialize<ContentPackDefinition>(
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

        var suites = new (string SuiteName, IEnumerable<(string Name, Action Run)> Tests)[]
        {
            ("SchemaAndValidation", GetSchemaAndValidationTests()),
            ("UnitBalanceAndVariants", GetUnitBalanceAndVariantsValidationTests()),
            ("BuildingsAndRelics", GetBuildingsAndRelicsValidationTests()),
            ("StagesAndBlueprints", GetStagesAndBlueprintsValidationTests()),
            ("UnitGraphsAndRelations", GetUnitGraphsAndRelationsValidationTests()),
            ("RuntimeAndDiagnostics", GetRuntimeAndDiagnosticsValidationTests()),
            ("AiCoreModules", GetAiCoreModulesValidationTests()),
            ("AiTacticalModules", GetAiTacticalModulesValidationTests()),
        };

        var allTests = new List<(string Name, Action Run)>();
        foreach (var (suiteName, suiteTests) in suites)
        {
            if (!string.IsNullOrWhiteSpace(suiteFilter) &&
                suiteName.IndexOf(suiteFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            allTests.AddRange(suiteTests);
        }

        int failures = 0;
        int skipped = 0;
        foreach ((string name, Action run) in allTests)
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

        Console.WriteLine($"{allTests.Count - failures - skipped}/{allTests.Count} validation checks passed; {skipped} skipped.");
        return failures == 0 ? 0 : 1;
    }

    internal static StageGenerationAiCandidate ValidStageAi(string id)
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

    internal static StageGenerationWorldCandidate ValidStageWorld(string id)
    {
        return new StageGenerationWorldCandidate
        {
            Id = id,
            IsAllowedForDemo = true,
            RestrictedMaps = []
        };
    }

    internal static StageGenerationLandscapeCandidate ValidStageLandscape(string id, int mapSize)
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

    internal static ContentPackDefinition[] CreateCrossPackUnitChain(int edgeCount, bool synergy, bool closeCycle = false)
    {
        int unitCount = closeCycle ? edgeCount : edgeCount + 1;
        var units = new ContentUnitDefinition[unitCount];
        for (int i = 0; i < unitCount; i++)
        {
            units[i] = new ContentUnitDefinition { id = "test:unit-" + i };
        }

        for (int i = 0; i < edgeCount; i++)
        {
            int targetIndex = (i + 1) % unitCount;
            if (synergy)
            {
                units[i].synergies = new[]
                {
                    new ContentUnitSynergyDefinition
                    {
                        id = "test:synergy-" + i,
                        statName = "damage",
                        multiplier = 1f,
                        radius = 1f,
                        targetUnitIds = new[] { units[targetIndex].id }
                    }
                };
            }
            else
            {
                units[i].upgrades = new[]
                {
                    new ContentUnitUpgradeDefinition { targetUnitId = units[targetIndex].id }
                };
            }
        }

        return new[]
        {
            new ContentPackDefinition
            {
                packId = "test:alpha-pack",
                units = units.Where((_, index) => index % 2 == 0).ToArray()
            },
            new ContentPackDefinition
            {
                packId = "test:beta-pack",
                units = units.Where((_, index) => index % 2 != 0).ToArray()
            }
        };
    }

    internal static void WithPackage(ContentPackDefinition source, Action<ContentPackDefinition, string> action)
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

    internal static void WithOwnedTemporaryDirectory(string prefix, Action<string> action)
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

    internal static void EnsureNormalDirectory(string path)
    {
        if (!IsNormalDirectory(path))
        {
            throw new IOException($"Validation fixture path is not a normal directory: {path}");
        }
    }

    internal static bool IsNormalDirectory(string path)
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

    internal static bool HasOwnershipMarker(string root, string markerPath, string ownerToken)
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

    internal static bool DeleteFixtureTree(string directory)
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

    internal static ContentPackDefinition Clone(ContentPackDefinition source)
    {
        var options = new JsonSerializerOptions { IncludeFields = true };
        return JsonSerializer.Deserialize<ContentPackDefinition>(JsonSerializer.Serialize(source, options), options)
            ?? throw new InvalidDataException("Could not clone the sample definition.");
    }

    internal static void SetUnitLoadoutVariants(ContentUnitDefinition unit, string variantsJson)
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

    internal static void ExpectUnitLoadoutVariantDiagnostic(
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

    internal static void ExpectValid(
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

    internal static void ExpectDiagnostic(
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

    internal sealed class SnapshotWithUnits : IContentRegistrySnapshot
    {
        private readonly HashSet<string> ids;
        public SnapshotWithUnits(IEnumerable<string> ids) => this.ids = new HashSet<string>(ids, StringComparer.Ordinal);
        public bool ContainsId(string id) => ids.Contains(id);
        public bool ContainsUnit(string id) => ids.Contains(id);
        public bool ContainsAiProfile(string id) => false;
    }

    internal sealed class EmptySnapshot : IContentRegistrySnapshot
    {
        private readonly HashSet<string> ids;

        public EmptySnapshot(params string[] ids) => this.ids = new HashSet<string>(ids, StringComparer.Ordinal);
        public bool ContainsId(string id) => ids.Contains(id);
        public bool ContainsUnit(string id) => ids.Contains(id);
        public bool ContainsAiProfile(string id) => ids.Contains(id);
    }

    internal sealed class KnownModuleValidator : IAiModuleValidator
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
            if (moduleId == "sample:skirmish-kiting")
            {
                return SkirmishKitingParameterParser.TryParse(parameters, out _, out diagnostic);
            }
            if (moduleId == "sample:support-repair")
            {
                return SupportRepairParameterParser.TryParse(parameters, out _, out diagnostic);
            }
            if (moduleId == "sample:counter-battery")
            {
                return CounterBatteryParameterParser.TryParse(parameters, out _, out diagnostic);
            }
            if (moduleId == "sample:tactical-skill-activation")
            {
                return TacticalSkillActivationParameterParser.TryParse(parameters, out _, out diagnostic);
            }
            if (moduleId == "sample:squad-formation")
            {
                return SquadFormationParameterParser.TryParse(parameters, out _, out diagnostic);
            }
            if (moduleId == "sample:wave-escalation")
            {
                return WaveEscalationParameterParser.TryParse(parameters, out _, out diagnostic);
            }
            if (moduleId == "sample:threat-prioritization")
            {
                return ThreatPrioritizationParameterParser.TryParse(parameters, out _, out diagnostic);
            }

            bool known = moduleId is "sample:scheduled-base-build" or "sample:reactive-base-defense" or "sample:trace-default";
            diagnostic = known ? string.Empty : "Unknown compiled AI module: " + moduleId;
            return known;
        }
    }

    internal sealed class SkipTestException : Exception
    {
        public SkipTestException(string message) : base(message)
        {
        }
    }

    internal sealed class ProductionBuildingEligibilityTestRow
    {
        public bool isAllowedAsBlueprint = false;
        public bool isAllowedAsStartingBlueprint = false;
        public bool isAllowedForAi = false;
        public bool isAllowedForDemo = false;
    }
}
