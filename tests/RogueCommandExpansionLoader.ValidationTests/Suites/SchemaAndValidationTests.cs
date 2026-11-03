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
    internal static IEnumerable<(string Name, Action Run)> GetSchemaAndValidationTests() => new (string Name, Action Run)[]
    {
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
            ("aiBehavior", "unitAiBehavior", typeof(ContentUnitAiBehaviorDefinition), false),
            ("shieldRegeneration", "unitShieldRegen", typeof(ContentUnitShieldRegenDefinition), false),
            ("vampiricLeech", "unitVampiricLeech", typeof(ContentUnitVampiricLeechDefinition), false),
            ("tauntPulse", "unitTauntPulse", typeof(ContentUnitTauntDefinition), false),
            ("shieldOverloadNova", "unitShieldOverloadNova", typeof(ContentUnitShieldOverloadNovaDefinition), false)
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
    ("duplicate IDs are rejected before registry changes", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.stages[0].id = pack.units[0].id;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "id.duplicate-in-package");
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
    ("Skirmish Strike Bike declares isAllowedForAi", () =>
    {
        ContentUnitDefinition bike = nativeSample.units.Single(unit => unit.id == "sample-native:skirmish-bike");
        if (!bike.isAllowedForAi)
        {
            throw new InvalidOperationException("Skirmish Strike Bike should be marked as allowed for AI.");
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
    ("managed assemblies are rejected from content packs", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            File.WriteAllBytes(Path.Combine(root, "unexpected.dll"), [0x4D, 0x5A]);
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "package.dll-forbidden");
        });
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
    ("unit status effect limits are enforced in schema and package validation", () =>
    {
        using (JsonDocument schemaDocument = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repositoryRoot, "content", "schema", "content.schema.json"))))
        {
            JsonElement statusEffects = schemaDocument.RootElement.GetProperty("$defs").GetProperty("unit")
                .GetProperty("properties").GetProperty("statusEffects");
            if (!statusEffects.TryGetProperty("maxItems", out JsonElement maxItems) || maxItems.GetInt32() != 16)
            {
                throw new InvalidOperationException("unit.statusEffects must declare maxItems=16 in the JSON schema.");
            }

            JsonElement duration = schemaDocument.RootElement.GetProperty("$defs").GetProperty("unitStatusEffect")
                .GetProperty("properties").GetProperty("duration");
            if (!duration.TryGetProperty("maximum", out JsonElement maximum) || Math.Abs(maximum.GetDouble() - 300d) > 0.0001d)
            {
                throw new InvalidOperationException("unitStatusEffect.duration must declare maximum=300 in the JSON schema.");
            }
        }

        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].statusEffects = Enumerable.Range(0, 17)
                .Select(index => new ContentUnitStatusEffectDefinition
                {
                    id = "sample:status-" + index,
                    effectType = "Stun",
                    duration = 1f
                })
                .ToArray();
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-status-effect.count-invalid");
        });

        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].statusEffects = new[]
            {
                new ContentUnitStatusEffectDefinition
                {
                    id = "sample:status-too-long",
                    effectType = "Stun",
                    duration = 300.01f
                }
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-status-effect.duration-invalid");
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
            (pack => pack.units[0].aiBehavior = new ContentUnitAiBehaviorDefinition { skillActivationCheckInterval = Single.NaN }, "unit-ai.interval-invalid"),
            (pack => pack.units[0].aiBehavior = new ContentUnitAiBehaviorDefinition { unitCap = 33 }, "unit-ai.cap-too-large"),
            (pack => pack.units[0].aiBehavior = new ContentUnitAiBehaviorDefinition { combatUnitCap = 33 }, "unit-ai.combat-cap-too-large"),
            (pack => pack.units[0].aiBehavior = new ContentUnitAiBehaviorDefinition { skillActivationCheckInterval = 60.01f }, "unit-ai.interval-too-large"),
            (pack => pack.units[0].shieldRegeneration = new ContentUnitShieldRegenDefinition { rechargeDelay = Single.NaN }, "unit-shield-regen.delay-invalid"),
            (pack => pack.units[0].shieldRegeneration = new ContentUnitShieldRegenDefinition { rechargeRate = Single.PositiveInfinity }, "unit-shield-regen.rate-invalid"),
            (pack => pack.units[0].shieldRegeneration = new ContentUnitShieldRegenDefinition { burstAmount = Single.NaN }, "unit-shield-regen.burst-invalid"),
            (pack => pack.units[0].vampiricLeech = new ContentUnitVampiricLeechDefinition { siphonRatio = Single.NaN }, "unit-vampiric-leech.ratio-invalid"),
            (pack => pack.units[0].vampiricLeech = new ContentUnitVampiricLeechDefinition { maxSiphonPerHit = Single.PositiveInfinity }, "unit-vampiric-leech.max-invalid"),
            (pack => pack.units[0].tauntPulse = new ContentUnitTauntDefinition { pulseInterval = Single.NaN }, "unit-taunt.interval-invalid"),
            (pack => pack.units[0].tauntPulse = new ContentUnitTauntDefinition { tauntRadius = Single.PositiveInfinity }, "unit-taunt.radius-invalid"),
            (pack => pack.units[0].tauntPulse = new ContentUnitTauntDefinition { duration = Single.NaN }, "unit-taunt.duration-invalid"),
            (pack => pack.units[0].shieldOverloadNova = new ContentUnitShieldOverloadNovaDefinition { novaRadius = Single.NaN }, "unit-shield-nova.radius-invalid"),
            (pack => pack.units[0].shieldOverloadNova = new ContentUnitShieldOverloadNovaDefinition { damage = Single.PositiveInfinity }, "unit-shield-nova.damage-invalid"),
            (pack => pack.units[0].shieldOverloadNova = new ContentUnitShieldOverloadNovaDefinition { stunDuration = Single.NaN }, "unit-shield-nova.stun-invalid"),
            (pack => pack.units[0].shieldOverloadNova = new ContentUnitShieldOverloadNovaDefinition { cooldown = Single.PositiveInfinity }, "unit-shield-nova.cooldown-invalid")
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
            string venvPython = Path.Combine(repositoryRoot, ".venv", "Scripts", "python.exe");
            process.StartInfo.FileName = File.Exists(venvPython) ? venvPython : "python";
            process.StartInfo.Arguments = "tools/validate_loader_registry.py";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            exitCode = process.ExitCode;
        }

        if (exitCode != 0 || !System.Text.RegularExpressions.Regex.IsMatch(output, @"PASS checked \d+ manifests") || output.Contains("FAIL"))
        {
            throw new InvalidOperationException("Loader registry tool failed: " + output);
        }
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
    ("unit behavioral parameters and audio are validated and deep-cloned", () =>
    {
        ContentPackDefinition pack = ContentPackCopy.DeepClone(nativeSample);
        var unit = pack.units[0];
        unit.rotationDegreesPerSecond = 180f;
        unit.collisionRadius = 8f;
        unit.attackWarmUpTime = 0.5f;
        unit.canAttackWhileMoving = false;
        unit.shootAutomaticallyOnEnemiesWithinRange = true;
        unit.defendPositionAggressivelyWhenIdle = true;
        unit.rangeToChooseNewTargetAutomatically = 25;
        unit.prespawnCount = 20;

        WithPackage(pack, (p, root) =>
        {
            ExpectValid(p, root, new EmptySnapshot(), new KnownModuleValidator());
        });

        ContentPackDefinition clonedPack = ContentPackCopy.DeepClone(pack);
        var clonedUnit = clonedPack.units[0];
        if (clonedUnit.rotationDegreesPerSecond != 180f ||
            clonedUnit.collisionRadius != 8f ||
            clonedUnit.attackWarmUpTime != 0.5f ||
            clonedUnit.canAttackWhileMoving != false ||
            clonedUnit.shootAutomaticallyOnEnemiesWithinRange != true ||
            clonedUnit.defendPositionAggressivelyWhenIdle != true ||
            clonedUnit.rangeToChooseNewTargetAutomatically != 25 ||
            clonedUnit.prespawnCount != 20)
        {
            throw new InvalidOperationException("Unit behavioral properties were not cloned properly.");
        }

        // Test invalid ranges
        ContentPackDefinition invalidRot = ContentPackCopy.DeepClone(pack);
        invalidRot.units[0].rotationDegreesPerSecond = 5f;
        WithPackage(invalidRot, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "unit.rotationDegreesPerSecond-invalid"));

        ContentPackDefinition invalidCol = ContentPackCopy.DeepClone(pack);
        invalidCol.units[0].collisionRadius = 0.1f;
        WithPackage(invalidCol, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "unit.collisionRadius-invalid"));

        ContentPackDefinition invalidWarm = ContentPackCopy.DeepClone(pack);
        invalidWarm.units[0].attackWarmUpTime = -1f;
        WithPackage(invalidWarm, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "unit.attackWarmUpTime-invalid"));

        ContentPackDefinition invalidRange = ContentPackCopy.DeepClone(pack);
        invalidRange.units[0].rangeToChooseNewTargetAutomatically = -5;
        WithPackage(invalidRange, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "unit.rangeToChooseNewTargetAutomatically-invalid"));

        ContentPackDefinition invalidPrespawn = ContentPackCopy.DeepClone(pack);
        invalidPrespawn.units[0].prespawnCount = -1;
        WithPackage(invalidPrespawn, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "unit.prespawnCount-invalid"));

        ContentPackDefinition missingBundle = ContentPackCopy.DeepClone(pack);
        missingBundle.units[0].selectionSoundAssetName = "audio/select.wav";
        missingBundle.units[0].assetBundleId = null;
        WithPackage(missingBundle, (p, root) => ExpectDiagnostic(p, root, new EmptySnapshot(),
            new KnownModuleValidator(), "unit.audio-bundle-missing"));
    }),
    ("tactical-operations expansion manifest passes full content validation", () =>
    {
        string tacticalPath = Path.Combine(repositoryRoot, "content", "packs", "tactical-operations", "content.json");
        string tacticalRoot = Path.GetDirectoryName(tacticalPath)!;
        string json = File.ReadAllText(tacticalPath);
        ContentPackDefinition pack = JsonSerializer.Deserialize<ContentPackDefinition>(json, jsonOptions)
            ?? throw new InvalidDataException("The tactical-operations content manifest is empty.");
        ContentPackValidationResult result = ContentPackValidator.Validate(
            pack, tacticalRoot, new EmptySnapshot(), new KnownModuleValidator());
        if (!result.IsValid)
        {
            throw new InvalidOperationException("tactical-operations manifest validation failed: " +
                string.Join(", ", result.Diagnostics.Select(d => $"{d.Code} at {d.Path}: {d.Message}")));
        }
    }),
    };
}
