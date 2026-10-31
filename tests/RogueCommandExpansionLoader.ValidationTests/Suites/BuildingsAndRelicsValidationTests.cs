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
    internal static IEnumerable<(string Name, Action Run)> GetBuildingsAndRelicsValidationTests() => new (string Name, Action Run)[]
    {
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
    ("production building asset requires a bundle", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentProductionBuildingDefinition building = pack.units[0].productionBuilding;
            building.cardImageAssetName = "assets/factory-card.png";
            building.assetBundleId = null;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "production-building.asset-bundle-required");
        });
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentProductionBuildingDefinition building = pack.units[0].productionBuilding;
            building.cardImageAssetName = "assets/factory-card.png";
            building.assetBundleId = pack.assetBundles[0].id;
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator());
        });
    }),
    ("production building asset rejects unknown bundle", () =>
    {
        WithPackage(nativeSample, (pack, root) =>
        {
            ContentProductionBuildingDefinition building = pack.units[0].productionBuilding;
            building.cardImageAssetName = "assets/factory-card.png";
            building.assetBundleId = "nonexistent:bundle";
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "reference.bundle-unresolved");
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
    ("unit and building validation checks onHitStatusEffects and criticalHitChance extensions", () =>
    {
        ContentPackDefinition testPack = ContentPackCopy.DeepClone(nativeSample);
        ContentUnitDefinition unit = testPack.units[0];
        unit.criticalHitChance = 0.25f;
        unit.onHitStatusEffects = new[]
        {
            new ContentOnHitStatusEffectDefinition
            {
                statusEffect = "Stun",
                duration = 2.5f,
                chance = 0.35f
            },
            new ContentOnHitStatusEffectDefinition
            {
                statusEffect = "OnFire",
                duration = 5.0f,
                chance = 1.0f
            }
        };

        testPack.buildings = new[]
        {
            new ContentBuildingDefinition
            {
                id = "sample-native:turret-test",
                nativeTemplateId = "MachineGunTurret",
                cost = 100,
                buildTime = 5f,
                combatValue = 10,
                rarity = "Common",
                tech = "Tank",
                criticalHitChance = 0.10f,
                localizations = new[]
                {
                    new ContentUnitLocalizationDefinition
                    {
                        language = "en-US",
                        name = "Test Turret",
                        description = "Test turret description"
                    }
                },
                onHitStatusEffects = new[]
                {
                    new ContentOnHitStatusEffectDefinition
                    {
                        statusEffect = "Battered",
                        duration = 4.0f,
                        chance = 0.5f
                    }
                }
            }
        };

        WithPackage(testPack, (p, root) => ExpectValid(p, root, new EmptySnapshot(), new KnownModuleValidator()));

        ContentPackDefinition invalidCrit = ContentPackCopy.DeepClone(testPack);
        invalidCrit.units[0].criticalHitChance = 1.5f;
        WithPackage(invalidCrit, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "unit.criticalHitChance-invalid"));

        ContentPackDefinition invalidBldgCrit = ContentPackCopy.DeepClone(testPack);
        invalidBldgCrit.buildings[0].criticalHitChance = -0.1f;
        WithPackage(invalidBldgCrit, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "building.criticalHitChance-invalid"));

        ContentPackDefinition invalidEffectFlag = ContentPackCopy.DeepClone(testPack);
        invalidEffectFlag.units[0].onHitStatusEffects[0].statusEffect = "SuperMegaNuke";
        WithPackage(invalidEffectFlag, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "on-hit-status-effect.type-invalid"));

        ContentPackDefinition invalidEffectDur = ContentPackCopy.DeepClone(testPack);
        invalidEffectDur.units[0].onHitStatusEffects[0].duration = 0f;
        WithPackage(invalidEffectDur, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "on-hit-status-effect.duration-invalid"));

        ContentPackDefinition invalidEffectChance = ContentPackCopy.DeepClone(testPack);
        invalidEffectChance.units[0].onHitStatusEffects[0].chance = 2.0f;
        WithPackage(invalidEffectChance, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "on-hit-status-effect.chance-invalid"));
    }),
    ("unit and building validation checks minAttackRange and active skill extensions", () =>
    {
        ContentPackDefinition testPack = ContentPackCopy.DeepClone(nativeSample);
        ContentUnitDefinition unit = testPack.units[0];
        unit.minAttackRange = 15.5f;
        unit.activeSkills = new[]
        {
            new ContentActiveSkillDefinition
            {
                id = "sample:tactical-emp",
                skillType = "LocationAoE",
                targetOrigin = "ChosenLocation",
                range = 25f,
                radius = 10f,
                manaCost = 50f,
                cooldown = 12f,
                helpAiToActivate = true,
                aiActivationTrigger = "EnemiesInRange",
                aiSkillCooldown = 15f
            }
        };

        WithPackage(testPack, (p, root) => ExpectValid(p, root, new EmptySnapshot(), new KnownModuleValidator()));

        ContentPackDefinition invalidMinRange = ContentPackCopy.DeepClone(testPack);
        invalidMinRange.units[0].minAttackRange = -5f;
        WithPackage(invalidMinRange, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "unit.minAttackRange-invalid"));

        ContentPackDefinition invalidOrigin = ContentPackCopy.DeepClone(testPack);
        invalidOrigin.units[0].activeSkills[0].targetOrigin = "InvalidOrigin";
        WithPackage(invalidOrigin, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "active-skill.targetOrigin-invalid"));

        ContentPackDefinition invalidSkillRange = ContentPackCopy.DeepClone(testPack);
        invalidSkillRange.units[0].activeSkills[0].range = 150f;
        WithPackage(invalidSkillRange, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "active-skill.range-invalid"));
    }),
    ("relic validation accepts canonical ChangeableValues and role/tech filters", () =>
    {
        ContentPackDefinition validPack = ContentPackCopy.DeepClone(nativeSample);
        ContentRelicDefinition relic = validPack.relics[0];
        relic.coinsAmount = 250;
        relic.inactive = false;
        relic.cardChanges = new[]
        {
            new ContentCardChangeDefinition
            {
                valueToChange = "MoveSpeed",
                operation = "Multiply",
                value = 1.15f,
                side = "False",
                cardMustHaveAllOfTheseRoles = new[] { "Mech" },
                cardMustNotHaveOneOfTheseTechs = new[] { "Ancient" },
                changeableValueHasToBeGreaterZero = true
            },
            new ContentCardChangeDefinition
            {
                valueToChange = "MaxArmor",
                operation = "Add",
                value = 20.0f,
                side = "False",
                cardMustHaveOneOfTheseRoles = new[] { "Tank", "Vehicle" }
            },
            new ContentCardChangeDefinition
            {
                valueToChange = "Damage1",
                operation = "Add",
                value = 4.0f,
                side = "False",
                onlyForTheseEntityIds = new[] { "sample-native:atlas-crane-mech" }
            }
        };

        WithPackage(validPack, (pack, root) =>
            ExpectValid(pack, root, new EmptySnapshot(), new KnownModuleValidator()));
    }),
    ("relic deep clone preserves coinsAmount, inactive, role filters, and exclusions", () =>
    {
        ContentPackDefinition sourcePack = ContentPackCopy.DeepClone(nativeSample);
        ContentRelicDefinition sourceRelic = sourcePack.relics[0];
        sourceRelic.coinsAmount = 350;
        sourceRelic.inactive = true;
        sourceRelic.cardChanges = new[]
        {
            new ContentCardChangeDefinition
            {
                valueToChange = "WeaponRange",
                operation = "Multiply",
                value = 1.25f,
                side = "False",
                cardMustHaveAllOfTheseRoles = new[] { "Sniper" },
                cardMustNotHaveOneOfTheseTechs = new[] { "Ancient" },
                entityIdsNotAllowed = new[] { "sample-native:scout-tank" },
                changeableValueHasToBeGreaterZero = true
            }
        };

        ContentPackDefinition clonedPack = ContentPackCopy.DeepClone(sourcePack);
        ContentRelicDefinition clonedRelic = clonedPack.relics[0];

        if (clonedRelic.coinsAmount != 350 || !clonedRelic.inactive)
        {
            throw new InvalidOperationException("Relic coinsAmount or inactive flag was not cloned correctly.");
        }

        // Mutate clone and assert source is isolated
        clonedRelic.coinsAmount = 0;
        clonedRelic.inactive = false;
        clonedRelic.cardChanges[0].cardMustHaveAllOfTheseRoles[0] = "Tank";
        clonedRelic.cardChanges[0].cardMustNotHaveOneOfTheseTechs[0] = "Laser";
        clonedRelic.cardChanges[0].entityIdsNotAllowed[0] = "other:unit";

        if (sourceRelic.coinsAmount != 350 || !sourceRelic.inactive ||
            sourceRelic.cardChanges[0].cardMustHaveAllOfTheseRoles[0] != "Sniper" ||
            sourceRelic.cardChanges[0].cardMustNotHaveOneOfTheseTechs[0] != "Ancient" ||
            sourceRelic.cardChanges[0].entityIdsNotAllowed[0] != "sample-native:scout-tank")
        {
            throw new InvalidOperationException("Relic deep clone shared mutable role filters, tech filters, or entity exclusions.");
        }
    }),
    ("relic validation rejects negative coinsAmount, invalid ChangeableValue, and duplicate roles", () =>
    {
        // Negative coinsAmount
        ContentPackDefinition invalidCoins = ContentPackCopy.DeepClone(nativeSample);
        invalidCoins.relics[0].coinsAmount = -50;
        WithPackage(invalidCoins, (pack, root) => ExpectDiagnostic(pack, root, new EmptySnapshot(),
            new KnownModuleValidator(), "relic.coins-amount-invalid"));

        // Invalid ChangeableValue
        ContentPackDefinition invalidStat = ContentPackCopy.DeepClone(nativeSample);
        invalidStat.relics[0].cardChanges[0].valueToChange = "BogusAttribute";
        WithPackage(invalidStat, (pack, root) => ExpectDiagnostic(pack, root, new EmptySnapshot(),
            new KnownModuleValidator(), "relic.change-value-unsupported"));

        // Duplicate role filter
        ContentPackDefinition duplicateRoles = ContentPackCopy.DeepClone(nativeSample);
        duplicateRoles.relics[0].cardChanges[0].cardMustHaveAllOfTheseRoles = new[] { "Mech", "Mech" };
        WithPackage(duplicateRoles, (pack, root) => ExpectDiagnostic(pack, root, new EmptySnapshot(),
            new KnownModuleValidator(), "relic.role-filter-duplicate"));
    }),
    ("productionBuilding structured parameters accept valid ranges and reject invalid values", () =>
    {
        ContentPackDefinition pack = ContentPackCopy.DeepClone(nativeSample);
        pack.units[0].balanceOverrides = pack.units[0].balanceOverrides
            .Where(o => o.name != "neededExperienceLevel" && o.name != "rarity")
            .ToArray();
        var pb = pack.units[0].productionBuilding;
        pb.balanceOverrides = Array.Empty<ContentBalanceOverride>();
        pb.gridSize = 2;
        pb.maxCapacity = 10;
        pb.cost = 250;
        pb.buildTime = 12.5f;
        pb.combatValue = 45;
        pb.cardModelScalingFactor = 1.2f;
        pb.rarity = "Rare";
        pb.tech = "Mech";
        pb.neededExperienceLevel = 3;
        pack.units[0].neededExperienceLevel = 3;
        pack.units[0].rarity = "Rare";
        pb.roles = new[] { "Building", "Factory", "Supporter" };
        pb.systemTags = new[] { "Turret", "Armor" };

        WithPackage(pack, (p, root) =>
        {
            ContentPackValidationResult result = ContentPackValidator.Validate(p, root,
                new EmptySnapshot(), new KnownModuleValidator());
            if (!result.IsValid)
            {
                throw new InvalidOperationException("Valid productionBuilding structured parameters produced unexpected errors: " +
                    string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            }
        });

        // Test invalid gridSize
        ContentPackDefinition invalidGrid = ContentPackCopy.DeepClone(pack);
        invalidGrid.units[0].productionBuilding.gridSize = 3;
        WithPackage(invalidGrid, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "production-building.grid-size-invalid"));

        // Test invalid maxCapacity
        ContentPackDefinition invalidCapacity = ContentPackCopy.DeepClone(pack);
        invalidCapacity.units[0].productionBuilding.maxCapacity = 150;
        WithPackage(invalidCapacity, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "production-building.max-capacity-invalid"));

        // Test invalid cost
        ContentPackDefinition invalidCost = ContentPackCopy.DeepClone(pack);
        invalidCost.units[0].productionBuilding.cost = -10;
        WithPackage(invalidCost, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "production-building.cost-invalid"));

        // Test invalid buildTime
        ContentPackDefinition invalidBuildTime = ContentPackCopy.DeepClone(pack);
        invalidBuildTime.units[0].productionBuilding.buildTime = -1.0f;
        WithPackage(invalidBuildTime, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "production-building.build-time-invalid"));
    }),
    ("productionBuilding structured parameters deep clone preserves isolation", () =>
    {
        ContentPackDefinition sourcePack = ContentPackCopy.DeepClone(nativeSample);
        var sourcePb = sourcePack.units[0].productionBuilding;
        sourcePb.gridSize = 2;
        sourcePb.maxCapacity = 15;
        sourcePb.cost = 300;
        sourcePb.buildTime = 14.0f;
        sourcePb.combatValue = 60;
        sourcePb.cardModelScalingFactor = 1.5f;
        sourcePb.rarity = "UltraRare";
        sourcePb.tech = "Tank";
        sourcePb.neededExperienceLevel = 5;
        sourcePb.roles = new[] { "Building", "Factory" };
        sourcePb.systemTags = new[] { "Tank", "Armor" };

        ContentPackDefinition clonedPack = ContentPackCopy.DeepClone(sourcePack);
        var clonedPb = clonedPack.units[0].productionBuilding;

        if (clonedPb.gridSize != 2 || clonedPb.maxCapacity != 15 || clonedPb.cost != 300 ||
            clonedPb.buildTime != 14.0f || clonedPb.combatValue != 60 ||
            clonedPb.rarity != "UltraRare" || clonedPb.tech != "Tank" ||
            clonedPb.neededExperienceLevel != 5)
        {
            throw new InvalidOperationException("productionBuilding structured scalar parameters were not cloned correctly.");
        }

        // Mutate clone and assert source isolation
        clonedPb.gridSize = 1;
        clonedPb.roles[0] = "Attacker";
        clonedPb.systemTags[0] = "Robo";

        if (sourcePb.gridSize != 2 || sourcePb.roles[0] != "Building" || sourcePb.systemTags[0] != "Tank")
        {
            throw new InvalidOperationException("productionBuilding deep clone shared mutable array references.");
        }
    }),
    ("standalone buildings validate parameters, unique IDs, and deep clone isolation", () =>
    {
        ContentPackDefinition pack = ContentPackCopy.DeepClone(nativeSample);
        pack.buildings = new[]
        {
            new ContentBuildingDefinition
            {
                id = "sample-native:turret-alpha",
                nativeTemplateId = "MachineGunTurret",
                gridSize = 2,
                maxCapacity = 4,
                cost = 175,
                buildTime = 9.0f,
                combatValue = 30,
                rarity = "Common",
                tech = "Tank",
                neededExperienceLevel = 0,
                roles = new[] { "Building", "Turret", "Supporter" },
                systemTags = new[] { "Turret", "Armor" },
                cardModelScalingFactor = 1.0f,
                firePointCount = 2,
                damage1 = 20.0f,
                attackCooldown = 1.0f,
                weaponRange = 15.0f,
                damage2 = 40.0f,
                attack2Cooldown = 3.0f,
                attackType = "Single",
                skillType = "None",
                isAllowedAsBlueprint = true,
                isAllowedAsStartingBlueprint = false,
                isAllowedForAi = true,
                isAllowedForDemo = true,
                isForSpecialists = false,
                localizations = new[]
                {
                    new ContentUnitLocalizationDefinition
                    {
                        language = "en-US",
                        name = "Twin Defense Turret",
                        description = "Automated perimeter defense gun."
                    }
                }
            }
        };

        WithPackage(pack, (p, root) =>
        {
            ContentPackValidationResult result = ContentPackValidator.Validate(p, root,
                new EmptySnapshot(), new KnownModuleValidator());
            if (!result.IsValid)
            {
                throw new InvalidOperationException("Valid standalone building produced unexpected errors: " +
                    string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            }
        });

        // Test building ID collision with unit
        ContentPackDefinition collideWithUnit = ContentPackCopy.DeepClone(pack);
        collideWithUnit.buildings[0].id = collideWithUnit.units[0].id;
        WithPackage(collideWithUnit, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "id.duplicate-in-package"));

        // Test duplicate building IDs
        ContentPackDefinition duplicateBuildings = ContentPackCopy.DeepClone(pack);
        duplicateBuildings.buildings = new[]
        {
            pack.buildings[0],
            ContentPackCopy.DeepClone(pack).buildings[0]
        };
        WithPackage(duplicateBuildings, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "id.duplicate-in-package"));

        // Test invalid building gridSize
        ContentPackDefinition invalidGrid = ContentPackCopy.DeepClone(pack);
        invalidGrid.buildings[0].gridSize = 0;
        WithPackage(invalidGrid, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "building.grid-size-invalid"));

        // Test invalid building maxCapacity
        ContentPackDefinition invalidCap = ContentPackCopy.DeepClone(pack);
        invalidCap.buildings[0].maxCapacity = -1;
        WithPackage(invalidCap, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "building.max-capacity-invalid"));

        // Test negative weapon damage
        ContentPackDefinition invalidDamage = ContentPackCopy.DeepClone(pack);
        invalidDamage.buildings[0].damage1 = -5.0f;
        WithPackage(invalidDamage, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "building.damage1-invalid"));

        // Deep clone test
        ContentPackDefinition clonedPack = ContentPackCopy.DeepClone(pack);
        var clonedBuilding = clonedPack.buildings[0];
        if (clonedBuilding.damage1 != 20.0f || clonedBuilding.weaponRange != 15.0f ||
            clonedBuilding.cost != 175 || clonedBuilding.gridSize != 2)
        {
            throw new InvalidOperationException("Standalone building fields were not cloned properly.");
        }
        clonedBuilding.roles[0] = "Attacker";
        clonedBuilding.systemTags[0] = "Robo";
        if (pack.buildings[0].roles[0] != "Building" || pack.buildings[0].systemTags[0] != "Turret")
        {
            throw new InvalidOperationException("Standalone building deep clone shared mutable array references.");
        }
    }),
    ("standalone building behavioral parameters and audio are validated and deep-cloned", () =>
    {
        ContentPackDefinition pack = ContentPackCopy.DeepClone(nativeSample);
        pack.buildings = new[]
        {
            new ContentBuildingDefinition
            {
                id = "sample-native:turret-beta",
                nativeTemplateId = "MachineGunTurret",
                gridSize = 2,
                maxCapacity = 4,
                cost = 175,
                buildTime = 9.0f,
                combatValue = 30,
                rarity = "Common",
                tech = "Tank",
                neededExperienceLevel = 0,
                roles = new[] { "Building", "Turret" },
                systemTags = new[] { "Turret" },
                rotationDegreesPerSecond = 90f,
                collisionRadius = 12f,
                attackWarmUpTime = 1.0f,
                canAttackWhileMoving = false,
                shootAutomaticallyOnEnemiesWithinRange = true,
                defendPositionAggressivelyWhenIdle = true,
                rangeToChooseNewTargetAutomatically = 30,
                prespawnCount = 5,
                localizations = new[]
                {
                    new ContentUnitLocalizationDefinition
                    {
                        language = "en-US",
                        name = "Heavy Defense Turret",
                        description = "Fortified defensive turret."
                    }
                }
            }
        };

        WithPackage(pack, (p, root) =>
        {
            ExpectValid(p, root, new EmptySnapshot(), new KnownModuleValidator());
        });

        ContentPackDefinition clonedPack = ContentPackCopy.DeepClone(pack);
        var clonedBuilding = clonedPack.buildings[0];
        if (clonedBuilding.rotationDegreesPerSecond != 90f ||
            clonedBuilding.collisionRadius != 12f ||
            clonedBuilding.attackWarmUpTime != 1.0f ||
            clonedBuilding.canAttackWhileMoving != false ||
            clonedBuilding.prespawnCount != 5)
        {
            throw new InvalidOperationException("Building behavioral properties were not cloned properly.");
        }

        // Test invalid ranges
        ContentPackDefinition invalidRot = ContentPackCopy.DeepClone(pack);
        invalidRot.buildings[0].rotationDegreesPerSecond = 800f;
        WithPackage(invalidRot, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "building.rotationDegreesPerSecond-invalid"));

        ContentPackDefinition invalidCol = ContentPackCopy.DeepClone(pack);
        invalidCol.buildings[0].collisionRadius = 60f;
        WithPackage(invalidCol, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "building.collisionRadius-invalid"));

        ContentPackDefinition invalidWarm = ContentPackCopy.DeepClone(pack);
        invalidWarm.buildings[0].attackWarmUpTime = 15f;
        WithPackage(invalidWarm, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "building.attackWarmUpTime-invalid"));

        ContentPackDefinition invalidPrespawn = ContentPackCopy.DeepClone(pack);
        invalidPrespawn.buildings[0].prespawnCount = 150;
        WithPackage(invalidPrespawn, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "building.prespawnCount-invalid"));

        ContentPackDefinition missingBundle = ContentPackCopy.DeepClone(pack);
        missingBundle.buildings[0].selectionSoundAssetName = "audio/turret_select.wav";
        missingBundle.buildings[0].assetBundleId = null;
        WithPackage(missingBundle, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "building.audio-bundle-missing"));
    }),
    ("production building factory row validation verifies building and factory roles", () =>
    {
        var validRow = new EntityBalancingParameters
        {
            roles = UnitRole.Building | UnitRole.Factory
        };
        if (!BalanceOverrideFlags.TryValidateFactoryRow(validRow, out string? diagnostic) || diagnostic != null)
        {
            throw new InvalidOperationException("Valid factory row with Building and Factory roles must pass validation.");
        }

        var missingFactoryRole = new EntityBalancingParameters
        {
            roles = UnitRole.Building
        };
        if (BalanceOverrideFlags.TryValidateFactoryRow(missingFactoryRole, out string? missingDiag) ||
            missingDiag != "A production building must retain both Building and Factory roles.")
        {
            throw new InvalidOperationException("Factory row missing Factory role must be rejected.");
        }

        if (BalanceOverrideFlags.TryValidateFactoryRow(null, out string? nullDiag) ||
            nullDiag != "A factory row is required.")
        {
            throw new InvalidOperationException("Null factory row must be rejected.");
        }

        var invalidType = new object();
        if (BalanceOverrideFlags.TryValidateFactoryRow(invalidType, out string? noFieldDiag) ||
            noFieldDiag != "The factory row has no verified UnitRole field.")
        {
            throw new InvalidOperationException("Row without UnitRole field must be rejected.");
        }
    }),
    };
}
