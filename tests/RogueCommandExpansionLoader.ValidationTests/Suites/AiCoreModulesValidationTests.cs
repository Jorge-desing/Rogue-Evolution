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
    internal static IEnumerable<(string Name, Action Run)> GetAiCoreModulesValidationTests() => new (string Name, Action Run)[]
    {
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
            IsCombatUnit = true,
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
    ("harvester-raider excludes non-combat AI units from the raid squad", () =>
    {
        ExistingControllers.Instance.Reset();
        DefaultAiBehaviour.ResetRuntimeState();
        UnityEngine.Time.time = 15f;

        var target = new EntityController
        {
            IsControlledByPlayer = true,
            IsUnit = true,
            Position = new UnityEngine.Vector3(8f, 0f, 0f)
        };
        var nonCombatRaider = new EntityController
        {
            IsControlledByAi = true,
            IsUnit = true,
            IsCombatUnit = false,
            CanAttackAndIsAttackEnabled = true,
            CanMoveAndMovementIsEnabled = true,
            aiBaseIndex = 0,
            UniqueEntityId = 503,
            Position = new UnityEngine.Vector3(5f, 0f, 0f)
        };
        ExistingControllers.Instance.Harvesters.Add(target);
        ExistingControllers.Instance.AiUnits().Add(nonCombatRaider);

        IRogueCommandAiRuntimeBehaviour behaviour = new HarvesterRaiderAiModule().CreateBehaviour(
            new DefaultAiBehaviour { transform = new UnityEngine.Transform { position = UnityEngine.Vector3.zero } },
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
            if (nonCombatRaider.ExecuteCommandCallCount != 0)
            {
                throw new InvalidOperationException("Harvester raider dispatched a non-combat AI unit.");
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
            IsCombatUnit = true,
            CanAttackAndIsAttackEnabled = true,
            CanMoveAndMovementIsEnabled = true,
            aiBaseIndex = 0,
            UniqueEntityId = 601
        };
        var second = new EntityController
        {
            IsControlledByAi = true,
            IsUnit = true,
            IsCombatUnit = true,
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
            IsCombatUnit = true,
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
                IsCombatUnit = true,
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
            IsCombatUnit = true,
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
                IsControlledByPlayer = true, IsUnit = true, IsCombatUnit = true, CanAttackAndIsAttackEnabled = true,
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
                    IsCombatUnit = true,
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
    ("unknown compiled AI modules are rejected", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.aiProfiles[0].modules[0].moduleId = "sample:unknown-module";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "module.invalid");
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
    ("adaptive-commander AI parameter parser supplies bounded defaults and rejects invalid inputs", () =>
    {
        if (!AdaptiveCommanderParameterParser.TryParse(null, out AdaptiveCommanderSettings defaults, out string diagnostic) ||
            diagnostic != null ||
            defaults.BaseIndex != -1 ||
            defaults.ScanIntervalSeconds != 3.0f ||
            defaults.CommandRadius != 25.0f ||
            defaults.HealthyStanceThreshold != 0.70f ||
            defaults.DamagedStanceThreshold != 0.35f ||
            defaults.AuraType != "Inspire" ||
            defaults.PreferredLoadout != "AP" ||
            defaults.CommanderAffix != "Overcharged" ||
            defaults.XpGrantPerPulse != 1 ||
            !defaults.AdaptiveDoctrine)
        {
            throw new InvalidOperationException("AdaptiveCommanderParameterParser did not return expected defaults.");
        }

        ContentModuleParameter[] customParams =
        [
            new ContentModuleParameter { name = "baseIndex", value = "2" },
            new ContentModuleParameter { name = "scanIntervalSeconds", value = "5.0" },
            new ContentModuleParameter { name = "commandRadius", value = "40.0" },
            new ContentModuleParameter { name = "healthyStanceThreshold", value = "0.80" },
            new ContentModuleParameter { name = "damagedStanceThreshold", value = "0.25" },
            new ContentModuleParameter { name = "auraType", value = "Aegis" },
            new ContentModuleParameter { name = "preferredLoadout", value = "Siege" },
            new ContentModuleParameter { name = "commanderAffix", value = "Juggernaut" },
            new ContentModuleParameter { name = "xpGrantPerPulse", value = "3" },
            new ContentModuleParameter { name = "adaptiveDoctrine", value = "false" }
        ];

        if (!AdaptiveCommanderParameterParser.TryParse(customParams, out AdaptiveCommanderSettings custom, out diagnostic) ||
            diagnostic != null ||
            custom.BaseIndex != 2 ||
            custom.ScanIntervalSeconds != 5.0f ||
            custom.CommandRadius != 40.0f ||
            custom.AuraType != "Aegis" ||
            custom.PreferredLoadout != "Siege" ||
            custom.CommanderAffix != "Juggernaut" ||
            custom.XpGrantPerPulse != 3 ||
            custom.AdaptiveDoctrine)
        {
            throw new InvalidOperationException($"AdaptiveCommanderParameterParser rejected valid custom parameters: {diagnostic}");
        }

        ContentModuleParameter[][] invalidInputs =
        [
            [new ContentModuleParameter { name = "unknownParam", value = "1" }],
            [new ContentModuleParameter { name = "auraType", value = "InvalidAura" }],
            [new ContentModuleParameter { name = "preferredLoadout", value = "InvalidLoadout" }],
            [new ContentModuleParameter { name = "commanderAffix", value = "InvalidAffix" }],
            [new ContentModuleParameter { name = "healthyStanceThreshold", value = "0.40" }],
            [new ContentModuleParameter { name = "scanIntervalSeconds", value = "NaN" }],
            [new ContentModuleParameter { name = "xpGrantPerPulse", value = "99" }]
        ];

        for (int i = 0; i < invalidInputs.Length; i++)
        {
            if (AdaptiveCommanderParameterParser.TryParse(invalidInputs[i], out _, out string err) || string.IsNullOrEmpty(err))
            {
                throw new InvalidOperationException($"AdaptiveCommanderParameterParser accepted invalid input at index {i}.");
            }
        }
    }),
    ("adaptive-commander AI module promotes commander, pulses aura, modulates squad stances, and adapts doctrine", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
        UnityEngine.Time.time = 10.0f;

        var commanderUnit = new EntityController
        {
            entityId = "sample:heavy-tank",
            UniqueEntityId = 2001,
            IsControlledByAi = true,
            IsUnit = true,
            IsCombatUnit = true,
            CanAttackAndIsAttackEnabled = true,
            CanMoveAndMovementIsEnabled = true,
            IsBuilding = false,
            aiBaseIndex = 0,
            Position = new UnityEngine.Vector3(10f, 0f, 10f),
            MaxHealth = 1000f,
            CurrentHealth = 900f,
            MaxArmor = 200f
        };

        var damagedEscort = new EntityController
        {
            entityId = "sample:light-tank",
            UniqueEntityId = 2002,
            IsControlledByAi = true,
            IsUnit = true,
            IsCombatUnit = true,
            CanAttackAndIsAttackEnabled = true,
            CanMoveAndMovementIsEnabled = true,
            IsBuilding = false,
            aiBaseIndex = 0,
            Position = new UnityEngine.Vector3(14f, 0f, 12f),
            MaxHealth = 400f,
            CurrentHealth = 100f,
            MaxArmor = 0f
        };

        ExistingControllers.Instance.AiUnits().Add(commanderUnit);
        ExistingControllers.Instance.AiUnits().Add(damagedEscort);

        var module = new AdaptiveCommanderAiModule();
        var aiHost = new DefaultAiBehaviour();
        aiHost.gameObject = new UnityEngine.GameObject("AiCommanderHost");

        IRogueCommandAiRuntimeBehaviour behaviour = module.CreateBehaviour(aiHost,
        [
            new ContentModuleParameter { name = "auraType", value = "Inspire" },
            new ContentModuleParameter { name = "preferredLoadout", value = "HE" },
            new ContentModuleParameter { name = "commanderAffix", value = "Overcharged" },
            new ContentModuleParameter { name = "xpGrantPerPulse", value = "2" }
        ]);

        behaviour.OnInitialized();
        behaviour.Tick();

        string cmdKey = commanderUnit.UniqueEntityId.ToString();
        string escortKey = damagedEscort.UniqueEntityId.ToString();

        if (RuntimeGameplayCommandDirector.GetUnitTacticalStance(cmdKey) != "Aggressive")
        {
            throw new InvalidOperationException("Healthy AI commander should adopt Aggressive stance.");
        }
        if (RuntimeGameplayCommandDirector.GetUnitTacticalStance(escortKey) != "Defensive")
        {
            throw new InvalidOperationException("Damaged AI escort should adopt Defensive stance.");
        }

        CommandAuraCensus auraCensus = RuntimeCommandAuraCensus.Current();
        if (auraCensus.TotalActiveAurasCount < 1)
        {
            throw new InvalidOperationException("AdaptiveCommanderAiModule should dispatch a command aura pulse.");
        }

        AiDoctrineAndDeckCensus doctrineCensus = RuntimeAiDoctrineCensus.Current();
        if (doctrineCensus.ActiveDoctrine != "Assault")
        {
            throw new InvalidOperationException($"Expected AI doctrine Assault when outnumbering hostiles, got {doctrineCensus.ActiveDoctrine}");
        }

        behaviour.OnDestroyed();
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        RuntimeGameplayCommandDirector.ClearTacticalStancesAndAffixes();
    }),
    ("AiModuleRegistry Active property initializes defaults and ResetActive restores null state", () =>
    {
        AiModuleRegistry.ResetActive();
        try
        {
            AiModuleRegistry registry = AiModuleRegistry.Active;
            if (registry == null)
            {
                throw new InvalidOperationException("AiModuleRegistry.Active must not be null.");
            }
            if (!registry.RegisteredModuleIds.Contains("sample:flanking-strike"))
            {
                throw new InvalidOperationException("Default AiModuleRegistry must contain core modules.");
            }

            var customRegistry = new AiModuleRegistry();
            AiModuleRegistry.Active = customRegistry;
            if (AiModuleRegistry.Active != customRegistry)
            {
                throw new InvalidOperationException("AiModuleRegistry.Active must accept custom registry assignment.");
            }

            AiModuleRegistry.ResetActive();
            AiModuleRegistry restored = AiModuleRegistry.Active;
            if (restored == null || restored == customRegistry)
            {
                throw new InvalidOperationException("ResetActive must restore default registry creation.");
            }
        }
        finally
        {
            AiModuleRegistry.ResetActive();
        }
    }),
    };
}
