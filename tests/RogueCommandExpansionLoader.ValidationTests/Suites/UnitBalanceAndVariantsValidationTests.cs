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
    internal static IEnumerable<(string Name, Action Run)> GetUnitBalanceAndVariantsValidationTests() => new (string Name, Action Run)[]
    {
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
    ("native rig retarget mappings deep clone nested mutable entries", () =>
    {
        var original = new ContentPackDefinition
        {
            units = new[]
            {
                new ContentUnitDefinition
                {
                    id = "test-native-unit",
                    nativeRigRetarget = new ContentNativeRigRetargetDefinition
                    {
                        mappings = new[]
                        {
                            new ContentNativeRigRetargetMappingDefinition
                            {
                                sourcePath = "Native/Leg",
                                targetPath = "Visual/Leg"
                            }
                        }
                    }
                }
            }
        };
        ContentPackDefinition clone = ContentPackCopy.DeepClone(original);
        ContentNativeRigRetargetDefinition sourceRetarget = original.units[0].nativeRigRetarget;
        ContentNativeRigRetargetDefinition cloneRetarget = clone.units[0].nativeRigRetarget;
        if (cloneRetarget == null || ReferenceEquals(sourceRetarget, cloneRetarget) ||
            ReferenceEquals(sourceRetarget.mappings, cloneRetarget.mappings) ||
            ReferenceEquals(sourceRetarget.mappings[0], cloneRetarget.mappings[0]))
        {
            throw new InvalidOperationException("Native rig retarget containers and mapping entries must be deeply cloned.");
        }

        cloneRetarget.mappings[0].sourcePath = "Changed/Source";
        cloneRetarget.mappings[0].targetPath = "Changed/Target";
        if (sourceRetarget.mappings[0].sourcePath != "Native/Leg" ||
            sourceRetarget.mappings[0].targetPath != "Visual/Leg")
        {
            throw new InvalidOperationException("Mutating a native rig retarget clone changed the source mapping.");
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
            unit.variants = null;
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
        ContentUnitDefinition unit = ContentPackCopy.DeepClone(nativeSample).units.First(u => u.variants == null);
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
    ("unit shield regeneration vampiric leech taunt and nova definitions validate bounds and clone safely", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            ContentUnitDefinition unit = pack.units[0];

            // 1. Shield Regen bounds
            unit.shieldRegeneration = new ContentUnitShieldRegenDefinition { rechargeDelay = -1f, rechargeRate = 10f };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-shield-regen.delay-invalid");
            unit.shieldRegeneration.rechargeDelay = 65f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-shield-regen.delay-invalid");
            unit.shieldRegeneration.rechargeDelay = 4.0f;

            unit.shieldRegeneration.rechargeRate = 0f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-shield-regen.rate-invalid");
            unit.shieldRegeneration.rechargeRate = 1500f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-shield-regen.rate-invalid");
            unit.shieldRegeneration.rechargeRate = 15.0f;

            unit.shieldRegeneration.burstAmount = -5f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-shield-regen.burst-invalid");
            unit.shieldRegeneration.burstAmount = 600f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-shield-regen.burst-invalid");
            unit.shieldRegeneration.burstAmount = 25f;

            // 2. Vampiric Leech bounds
            unit.vampiricLeech = new ContentUnitVampiricLeechDefinition { siphonRatio = 0f };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-vampiric-leech.ratio-invalid");
            unit.vampiricLeech.siphonRatio = 1.2f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-vampiric-leech.ratio-invalid");
            unit.vampiricLeech.siphonRatio = 0.25f;
            unit.vampiricLeech.siphonToShield = true;
            unit.vampiricLeech.maxSiphonPerHit = 30f;

            // 3. Taunt bounds
            unit.tauntPulse = new ContentUnitTauntDefinition { pulseInterval = 0.1f, tauntRadius = 15f };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-taunt.interval-invalid");
            unit.tauntPulse.pulseInterval = 3.0f;

            unit.tauntPulse.tauntRadius = 0.5f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-taunt.radius-invalid");
            unit.tauntPulse.tauntRadius = 80f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-taunt.radius-invalid");
            unit.tauntPulse.tauntRadius = 15f;
            unit.tauntPulse.duration = 2.5f;
            unit.tauntPulse.maxTargets = 8;

            // 4. Shield Overload Nova bounds
            unit.shieldOverloadNova = new ContentUnitShieldOverloadNovaDefinition { novaRadius = 0.5f };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-shield-nova.radius-invalid");
            unit.shieldOverloadNova.novaRadius = 12f;

            unit.shieldOverloadNova.stunDuration = 20f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-shield-nova.stun-invalid");
            unit.shieldOverloadNova.stunDuration = 2.5f;
            unit.shieldOverloadNova.damage = 25f;
            unit.shieldOverloadNova.cooldown = 10f;

            // 5. Damage Reflect bounds
            unit.damageReflect = new ContentUnitDamageReflectDefinition { reflectRatio = 0f };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-damage-reflect.ratio-invalid");
            unit.damageReflect.reflectRatio = 1.2f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-damage-reflect.ratio-invalid");
            unit.damageReflect.reflectRatio = 0.25f;
            unit.damageReflect.maxReflectPerHit = 40f;

            // 6. Death Nova bounds
            unit.deathNova = new ContentUnitDeathNovaDefinition { novaRadius = 0.5f };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-death-nova.radius-invalid");
            unit.deathNova.novaRadius = 60f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-death-nova.radius-invalid");
            unit.deathNova.novaRadius = 5f;
            unit.deathNova.damage = 600f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-death-nova.damage-invalid");
            unit.deathNova.damage = 30f;
            unit.deathNova.stunDuration = 20f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-death-nova.stun-invalid");
            unit.deathNova.stunDuration = 1.5f;
            unit.deathNova.slowDuration = 20f;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-death-nova.slow-invalid");
            unit.deathNova.slowDuration = 2.0f;
            unit.deathNova.maxTargets = 33;
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(), "unit-death-nova.max-targets-invalid");
            unit.deathNova.maxTargets = 6;

            // Deep clone verification
            ContentPackDefinition clonedPack = ContentPackCopy.DeepClone(pack);
            ContentUnitDefinition clonedUnit = clonedPack.units[0];
            if (clonedUnit.shieldRegeneration == null ||
                Math.Abs(clonedUnit.shieldRegeneration.rechargeDelay - 4.0f) > 0.001f ||
                Math.Abs(clonedUnit.shieldRegeneration.rechargeRate - 15.0f) > 0.001f ||
                Math.Abs(clonedUnit.shieldRegeneration.burstAmount - 25.0f) > 0.001f)
            {
                throw new InvalidOperationException("ShieldRegeneration was not deep cloned properly.");
            }
            if (clonedUnit.vampiricLeech == null ||
                Math.Abs(clonedUnit.vampiricLeech.siphonRatio - 0.25f) > 0.001f ||
                !clonedUnit.vampiricLeech.siphonToShield ||
                Math.Abs(clonedUnit.vampiricLeech.maxSiphonPerHit - 30.0f) > 0.001f)
            {
                throw new InvalidOperationException("VampiricLeech was not deep cloned properly.");
            }
            if (clonedUnit.tauntPulse == null ||
                Math.Abs(clonedUnit.tauntPulse.pulseInterval - 3.0f) > 0.001f ||
                Math.Abs(clonedUnit.tauntPulse.tauntRadius - 15.0f) > 0.001f ||
                clonedUnit.tauntPulse.maxTargets != 8)
            {
                throw new InvalidOperationException("TauntPulse was not deep cloned properly.");
            }
            if (clonedUnit.shieldOverloadNova == null ||
                Math.Abs(clonedUnit.shieldOverloadNova.novaRadius - 12.0f) > 0.001f ||
                Math.Abs(clonedUnit.shieldOverloadNova.damage - 25.0f) > 0.001f ||
                Math.Abs(clonedUnit.shieldOverloadNova.stunDuration - 2.5f) > 0.001f)
            {
                throw new InvalidOperationException("ShieldOverloadNova was not deep cloned properly.");
            }
            if (clonedUnit.damageReflect == null ||
                Math.Abs(clonedUnit.damageReflect.reflectRatio - 0.25f) > 0.001f ||
                Math.Abs(clonedUnit.damageReflect.maxReflectPerHit - 40.0f) > 0.001f)
            {
                throw new InvalidOperationException("DamageReflect was not deep cloned properly.");
            }
            if (clonedUnit.deathNova == null ||
                Math.Abs(clonedUnit.deathNova.novaRadius - 5.0f) > 0.001f ||
                Math.Abs(clonedUnit.deathNova.damage - 30.0f) > 0.001f ||
                Math.Abs(clonedUnit.deathNova.stunDuration - 1.5f) > 0.001f ||
                Math.Abs(clonedUnit.deathNova.slowDuration - 2.0f) > 0.001f ||
                clonedUnit.deathNova.maxTargets != 6)
            {
                throw new InvalidOperationException("DeathNova was not deep cloned properly.");
            }

            // Profile and summary verification
            var profile = new UnitRuntimeMechanicsProfile
            {
                EntityId = unit.id,
                ShieldRegen = unit.shieldRegeneration,
                VampiricLeech = unit.vampiricLeech,
                Taunt = unit.tauntPulse,
                ShieldOverloadNova = unit.shieldOverloadNova,
                DamageReflect = unit.damageReflect,
                DeathNova = unit.deathNova
            };
            if (!profile.HasRuntimeComponentMechanics || !profile.HasOnDamageDealtMechanics || !profile.HasAnyMechanics ||
                !profile.HasOnDamageTakenMechanics || !profile.HasOnDeathMechanics)
            {
                throw new InvalidOperationException("Profile does not detect new mechanics.");
            }
            RuntimeUnitMechanicsDirector.RegisterProfile(unit.id, profile);
            string? summary = RuntimeUnitMechanicsDirector.FormatMechanicsSummary(unit.id);
            if (summary == null ||
                !summary.Contains("Regen Escudo") ||
                !summary.Contains("Drenaje Vampírico") ||
                !summary.Contains("Provocación") ||
                !summary.Contains("Nova EMP") ||
                !summary.Contains("Reflejo") ||
                !summary.Contains("Nova Mortal"))
            {
                throw new InvalidOperationException("FormatMechanicsSummary does not describe the new mechanics: " + summary);
            }
        });
    }),
    ("RuntimeUnitVariantRegistry and RuntimeUnitVisualVariantRegistry query helpers return sorted variant IDs", () =>
    {
        RuntimeUnitVariantRegistry.Clear();
        RuntimeUnitVisualVariantRegistry.Clear();
        try
        {
            if (RuntimeUnitVariantRegistry.GetVariantIds("sample:scout").Count != 0 ||
                RuntimeUnitVisualVariantRegistry.GetVisualVariantIds("sample:scout").Count != 0 ||
                RuntimeUnitVisualVariantRegistry.GetAllVariants("sample:scout").Count != 0)
            {
                throw new InvalidOperationException("Variant queries on unknown units must return empty collections.");
            }

            var variantA = new RuntimeUnitVariant("beta-loadout", "ap", null);
            var variantB = new RuntimeUnitVariant("alpha-loadout", "he", null);
            RuntimeUnitVariantRegistry.Register("sample:scout", new[] { variantA, variantB });

            IReadOnlyList<string> variantIds = RuntimeUnitVariantRegistry.GetVariantIds("sample:scout");
            if (variantIds.Count != 2 || variantIds[0] != "alpha-loadout" || variantIds[1] != "beta-loadout")
            {
                throw new InvalidOperationException("GetVariantIds must return alphabetically sorted IDs.");
            }

            var visualA = new RuntimeUnitVisualVariant("camo-green", "sample:scout", "sample:scout/camo", "#00FF00", 1.0f, "Assets/Scout_Camo.prefab", "VisualRoot", false);
            var visualB = new RuntimeUnitVisualVariant("arctic-white", "sample:scout", "sample:scout/arctic", "#FFFFFF", 1.0f, "Assets/Scout_Arctic.prefab", "VisualRoot", true);
            RuntimeUnitVisualVariantRegistry.Register("sample:scout", new[] { visualA, visualB });

            IReadOnlyList<string> visualIds = RuntimeUnitVisualVariantRegistry.GetVisualVariantIds("sample:scout");
            if (visualIds.Count != 2 || visualIds[0] != "arctic-white" || visualIds[1] != "camo-green")
            {
                throw new InvalidOperationException("GetVisualVariantIds must return alphabetically sorted IDs.");
            }

            IReadOnlyList<RuntimeUnitVisualVariant> allVisuals = RuntimeUnitVisualVariantRegistry.GetAllVariants("sample:scout");
            if (allVisuals.Count != 2)
            {
                throw new InvalidOperationException("GetAllVariants must return all registered visual variants.");
            }
        }
        finally
        {
            RuntimeUnitVariantRegistry.Clear();
            RuntimeUnitVisualVariantRegistry.Clear();
        }
    }),
    ("unit balance map exposes complete verified metadata for all 25 per-unit parameters", () =>
    {
        IReadOnlyList<BalanceParameterDefinition> parameters =
            UnitBalanceOverrideMapper.AllParameters;
        if (parameters.Count != 25)
        {
            throw new InvalidOperationException("The canonical per-unit map must contain exactly 25 parameters.");
        }

        var canonicalNames = new HashSet<string>(StringComparer.Ordinal);
        var rowFields = new HashSet<string>(StringComparer.Ordinal);
        var ordinals = new HashSet<int>();
        int intCount = 0;
        foreach (BalanceParameterDefinition parameter in parameters)
        {
            if (!canonicalNames.Add(parameter.ChangeableValueName) ||
                !rowFields.Add(parameter.RowFieldName) ||
                !ordinals.Add(parameter.ChangeableValueOrdinal))
            {
                throw new InvalidOperationException("Canonical names, row fields, and enum ordinals must be unique: " +
                    parameter.ChangeableValueName);
            }

            string expectedGetter = parameter.StorageType == BalanceParameterType.Int
                ? "GetOriginalIntValue"
                : "GetOriginalFloatValue";
            if (parameter.NativeGetter != expectedGetter)
            {
                throw new InvalidOperationException("Native getter for " + parameter.ChangeableValueName +
                    " must be " + expectedGetter + ".");
            }

            if (parameter.StorageType == BalanceParameterType.Int)
            {
                intCount++;
            }
        }

        if (intCount != 8)
        {
            throw new InvalidOperationException("Exactly 8 per-unit parameters are read via GetOriginalIntValue.");
        }

        var expectedOrdinals = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Cost"] = 0, ["ProductionDuration"] = 1, ["MaxCapacity"] = 2, ["MaxHealth"] = 3,
            ["MaxShield"] = 4, ["MaxMana"] = 5, ["MaxRank"] = 6, ["MoveSpeed"] = 7,
            ["SightRadius"] = 8, ["Damage1"] = 9, ["Damage2"] = 10, ["WeaponRange"] = 11,
            ["Attack1Cooldown"] = 12, ["Attack2Cooldown"] = 13, ["EffectRadius1"] = 14,
            ["EffectRadius2"] = 15, ["SkillRange"] = 16, ["HealAmount1"] = 17,
            ["HealAmount2"] = 18, ["GainCreditsAmount"] = 19, ["SkillManaCost"] = 20,
            ["Duration1"] = 21, ["SpawnTtl"] = 22, ["MaxArmor"] = 24, ["ArmorProtection"] = 25
        };
        foreach (KeyValuePair<string, int> expected in expectedOrdinals)
        {
            if (!UnitBalanceOverrideMapper.TryGetBalanceParameterDefinition(expected.Key,
                    out BalanceParameterDefinition definition) ||
                definition.ChangeableValueOrdinal != expected.Value)
            {
                throw new InvalidOperationException("Enum ordinal mismatch for " + expected.Key + ".");
            }
        }

        if (UnitBalanceOverrideMapper.TryGetBalanceParameterDefinition(
                "ManaRechargePerSecond", out _))
        {
            throw new InvalidOperationException("Global-only ManaRechargePerSecond must stay unmapped per-unit.");
        }

        if (!UnitBalanceOverrideMapper.TryGetBalanceParameterDefinition("Attack1Cooldown",
                out BalanceParameterDefinition cooldown) ||
            cooldown.RowFieldName != "attackCooldown" ||
            !UnitBalanceOverrideMapper.TryGetBalanceParameterDefinition("SpawnTtl",
                out BalanceParameterDefinition spawnTtl) ||
            spawnTtl.RowFieldName != "duration2")
        {
            throw new InvalidOperationException("Non-obvious mappings Attack1Cooldown->attackCooldown and SpawnTtl->duration2 must be preserved.");
        }
    }),
    ("unit balance map reverse lookup round-trips every raw row field", () =>
    {
        foreach (BalanceParameterDefinition parameter in UnitBalanceOverrideMapper.AllParameters)
        {
            if (!UnitBalanceOverrideMapper.TryGetByRowFieldName(parameter.RowFieldName,
                    out BalanceParameterDefinition reverse) ||
                reverse.ChangeableValueName != parameter.ChangeableValueName ||
                reverse.RowFieldName != parameter.RowFieldName)
            {
                throw new InvalidOperationException("Reverse lookup failed for row field " + parameter.RowFieldName + ".");
            }
        }

        if (UnitBalanceOverrideMapper.TryGetByRowFieldName("duration", out _) ||
            UnitBalanceOverrideMapper.TryGetByRowFieldName("spawnTtl", out _) ||
            UnitBalanceOverrideMapper.TryGetByRowFieldName("attack1Cooldown", out _) ||
            UnitBalanceOverrideMapper.TryGetByRowFieldName("MoveSpeed", out _))
        {
            throw new InvalidOperationException("Reverse lookup must only accept exact native row field names.");
        }
    }),
    ("unit balance mapper rejects invalid customBalance values at mapping time", () =>
    {
        var invalidCases = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["MoveSpeed"] = -1d,
            ["MaxHealth"] = 130.5d,
            ["MaxHealth2"] = 1d,
            ["Cost"] = 16777217d,
            ["Damage1"] = Double.MaxValue,
            ["MoveSpeed2"] = Double.NaN
        };
        foreach (KeyValuePair<string, double> invalid in invalidCases)
        {
            var unit = new ContentUnitDefinition
            {
                id = "sample:invalid-balance",
                customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    [invalid.Key] = invalid.Value
                }
            };
            bool rejected = false;
            try
            {
                UnitBalanceOverrideMapper.CreateCustomBalance(unit);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            if (!rejected)
            {
                throw new InvalidOperationException("CreateCustomBalance must reject invalid value " +
                    invalid.Key + "=" + invalid.Value + ".");
            }
        }
    }),
    ("unit balance mapper emits parameters in canonical enum order", () =>
    {
        var unit = new ContentUnitDefinition
        {
            id = "sample:ordered-balance",
            customBalance = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["ArmorProtection"] = 1d,
                ["Cost"] = 10d,
                ["MoveSpeed"] = 3d,
                ["MaxHealth"] = 100d
            }
        };
        Dictionary<string, double> mapped = UnitBalanceOverrideMapper.CreateCustomBalance(unit);
        string[] keys = mapped.Keys.ToArray();
        string[] expected = { "Cost", "MaxHealth", "MoveSpeed", "ArmorProtection" };
        if (!keys.SequenceEqual(expected))
        {
            throw new InvalidOperationException("CreateCustomBalance must emit canonical enum order, got: " +
                string.Join(",", keys));
        }
    }),
    ("runtime mutator applies all 25 per-unit parameters to the registered row", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            var unit = new ContentUnitDefinition { id = "sample:all-balance" };
            var parameters = new Dictionary<string, double>(StringComparer.Ordinal);
            var expected = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (BalanceParameterDefinition definition in UnitBalanceOverrideMapper.AllParameters)
            {
                double value = definition.StorageType == BalanceParameterType.Int ? 7d : 2.5d;
                parameters[definition.ChangeableValueName] = value;
                expected[definition.RowFieldName] = definition.StorageType == BalanceParameterType.Int
                    ? (object)7
                    : (object)2.5f;
            }

            var provider = new RuntimeMutatorTestProvider();
            provider.EntityRows.Add(RuntimeMutatorTestData.Row(unit.id));
            provider.BalanceOverrides.Add(new BalanceOverride
            {
                EntityId = unit.id,
                Parameters = parameters
            });
            var mutator = new RuntimeContentMutator(
                typeof(EntityBalancingStore).Assembly, provider, null);
            if (!mutator.RegisterEntityRows())
            {
                throw new InvalidOperationException("Entity registration failed for the full 25-parameter row.");
            }

            EntityBalancingParameters registered = EntityBalancingStore.EntityBalancingParametersList[0];
            foreach (BalanceParameterDefinition definition in UnitBalanceOverrideMapper.AllParameters)
            {
                FieldInfo? field = typeof(EntityBalancingParameters).GetField(definition.RowFieldName);
                object? actual = field?.GetValue(registered);
                if (actual == null || !actual.Equals(expected[definition.RowFieldName]))
                {
                    throw new InvalidOperationException("Row field " + definition.RowFieldName +
                        " for " + definition.ChangeableValueName + " must be " +
                        expected[definition.RowFieldName] + ", got " + actual + ".");
                }
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    };
}
