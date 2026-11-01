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
    ("runtime log burst summary counts only adjacent equal messages", () =>
    {
        LoaderLogBurstSummary summary = LoaderLogBurstSummaryPolicy.Create(new[]
        {
            "12:00:00 loaded alpha",
            "12:00:01 loaded alpha",
            "12:00:02 loaded beta",
            "12:00:03 loaded alpha"
        });
        if (summary == null || summary.Message != "loaded alpha" || summary.RepeatCount != 0)
        {
            throw new InvalidOperationException("Only the final consecutive run should be summarized, without merging earlier messages.");
        }

        LoaderLogBurstSummary repeated = LoaderLogBurstSummaryPolicy.Create(new[]
        {
            "12:00:00 loaded alpha",
            "12:00:01 loaded alpha",
            "12:00:02 loaded alpha"
        });
        if (repeated == null || repeated.Message != "loaded alpha" || repeated.RepeatCount != 2 ||
            repeated.DisplayText != "loaded alpha (2)")
        {
            throw new InvalidOperationException("The summary should display the count of repeated messages after the first entry.");
        }
    }),
    ("runtime log burst summary preserves entries without a valid timestamp", () =>
    {
        LoaderLogBurstSummary summary = LoaderLogBurstSummaryPolicy.Create(new[]
        {
            "not a timestamp: same",
            "not a timestamp: same"
        });
        if (summary == null || summary.Message != "not a timestamp: same" || summary.RepeatCount != 1)
        {
            throw new InvalidOperationException("Non-timestamped messages must remain intact while still being grouped consecutively.");
        }

        LoaderLogBurstSummary singleRepeat = LoaderLogBurstSummaryPolicy.Create(new[]
        {
            "12:00:00 loaded beta",
            "12:00:01 loaded beta"
        });
        if (singleRepeat == null || singleRepeat.RepeatCount != 1 ||
            singleRepeat.DisplayText != "loaded beta (1)")
        {
            throw new InvalidOperationException("The first repeated entry must display a repeat count of one.");
        }

        if (LoaderLogBurstSummaryPolicy.Create(Array.Empty<string>()) != null)
        {
            throw new InvalidOperationException("An empty log should not produce a burst summary.");
        }
    }),
    ("inspector toggle policy opens, toggles, and gives close precedence", () =>
    {
        if (!InspectorTogglePolicy.Resolve(false, true, false) ||
            InspectorTogglePolicy.Resolve(true, true, false) ||
            InspectorTogglePolicy.Resolve(true, false, true) ||
            InspectorTogglePolicy.Resolve(false, true, true) ||
            InspectorTogglePolicy.Resolve(false, false, false))
        {
            throw new InvalidOperationException("Toggle should invert state unless close is pressed, which must leave the inspector closed.");
        }
    }),
    ("inspector toggle state serializes resolved open flag", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeInspectorToggleState(false, true, false);
        if (!json.Contains("\"resolvedOpen\":true"))
        {
            throw new InvalidOperationException("SerializeInspectorToggleState must report resolvedOpen=true for a toggle press.");
        }

        string closedJson = AgentDiagnosticsFormatter.SerializeInspectorToggleState(true, false, true);
        if (!closedJson.Contains("\"resolvedOpen\":false"))
        {
            throw new InvalidOperationException("SerializeInspectorToggleState must report resolvedOpen=false when close is pressed.");
        }
    }),
    ("range circle point count serializes validity and count", () =>
    {
        string validJson = AgentDiagnosticsFormatter.SerializeRangeCirclePointCount(32);
        if (!validJson.Contains("\"valid\":true") || !validJson.Contains("\"pointCount\":33"))
        {
            throw new InvalidOperationException("SerializeRangeCirclePointCount must report valid=true and pointCount=segments+1.");
        }

        string invalidJson = AgentDiagnosticsFormatter.SerializeRangeCirclePointCount(2);
        if (!invalidJson.Contains("\"valid\":false") || !invalidJson.Contains("\"pointCount\":0"))
        {
            throw new InvalidOperationException("SerializeRangeCirclePointCount must report valid=false and pointCount=0 for out-of-range segments.");
        }
    }),
    ("runtime hooks state serializes installed flags, owner id and xp count", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeRuntimeHooksState(true, 42, "RogueCommand.ExpansionLoader.Runtime", 7);
        if (!json.Contains("\"hooksInstalled\":true") || !json.Contains("\"installedHookCount\":42") ||
            !json.Contains("\"harmonyOwnerId\":\"RogueCommand.ExpansionLoader.Runtime\"") ||
            !json.Contains("\"registeredCombatXpCount\":7"))
        {
            throw new InvalidOperationException("SerializeRuntimeHooksState must report the installed flags, owner id and combat xp count.");
        }

        string nullOwner = AgentDiagnosticsFormatter.SerializeRuntimeHooksState(false, 0, null, 0);
        if (!nullOwner.Contains("\"harmonyOwnerId\":\"\""))
        {
            throw new InvalidOperationException("SerializeRuntimeHooksState must emit an empty owner id string when null.");
        }
    }),
    ("installed patch names serializes a de-duplicated name list with its count", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeInstalledPatchNames(new[] { "EntityControllerDiePostfix", "", null, "EntityTakeDamagePostfix" });
        if (!json.Contains("\"installedPatchCount\":2") ||
            !json.Contains("\"EntityControllerDiePostfix\"") ||
            !json.Contains("\"EntityTakeDamagePostfix\""))
        {
            throw new InvalidOperationException("SerializeInstalledPatchNames must drop null/empty entries and report the surviving count.");
        }

        string empty = AgentDiagnosticsFormatter.SerializeInstalledPatchNames(null);
        if (!empty.Contains("\"installedPatchCount\":0") || !empty.Contains("\"installedPatchNames\":[]"))
        {
            throw new InvalidOperationException("SerializeInstalledPatchNames must emit an empty list and count 0 for null input.");
        }
    }),
    ("registered profiles serializes ids with their count", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeRegisteredProfiles(new[] { "zebra", "alpha" });
        if (!json.Contains("\"registeredProfileCount\":2") ||
            !json.Contains("\"registeredProfileIds\":[\"zebra\",\"alpha\"]"))
        {
            throw new InvalidOperationException("SerializeRegisteredProfiles must preserve id order and report the count.");
        }

        string dropped = AgentDiagnosticsFormatter.SerializeRegisteredProfiles(new[] { "", null, "solo" });
        if (!dropped.Contains("\"registeredProfileCount\":1") || !dropped.Contains("\"registeredProfileIds\":[\"solo\"]"))
        {
            throw new InvalidOperationException("SerializeRegisteredProfiles must drop null/empty ids and report the surviving count.");
        }
    }),
    ("applied baseline effects serializes the entry count and names", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeAppliedBaselineEffects(3, new[] { "Burning", "Chilled", "Burning" });
        if (!json.Contains("\"appliedBaselineEffectEntryCount\":3") ||
            !json.Contains("\"appliedBaselineEffectNames\":[\"Burning\",\"Chilled\",\"Burning\"]"))
        {
            throw new InvalidOperationException("SerializeAppliedBaselineEffects must preserve names and report the entry count.");
        }

        string none = AgentDiagnosticsFormatter.SerializeAppliedBaselineEffects(0, null);
        if (!none.Contains("\"appliedBaselineEffectEntryCount\":0") || !none.Contains("\"appliedBaselineEffectNames\":[]"))
        {
            throw new InvalidOperationException("SerializeAppliedBaselineEffects must emit an empty name list for a null input.");
        }
    }),
    ("combat xp entries serializes entity keys and experience", () =>
    {
        var entries = new[]
        {
            new RuntimeUnitMechanicsDirector.CombatXpEntry(201, 12),
            new RuntimeUnitMechanicsDirector.CombatXpEntry(105, 4),
        };
        string json = AgentDiagnosticsFormatter.SerializeCombatXpEntries(entries);
        if (!json.Contains("\"combatXpEntryCount\":2") ||
            !json.Contains("\"entityKeyId\":201") || !json.Contains("\"experience\":12") ||
            !json.Contains("\"entityKeyId\":105") || !json.Contains("\"experience\":4"))
        {
            throw new InvalidOperationException("SerializeCombatXpEntries must report each entity key id and its experience.");
        }

        string empty = AgentDiagnosticsFormatter.SerializeCombatXpEntries(null);
        if (!empty.Contains("\"combatXpEntryCount\":0") || !empty.Contains("\"combatXpEntries\":[]"))
        {
            throw new InvalidOperationException("SerializeCombatXpEntries must emit an empty list and count 0 for null input.");
        }
    }),
    ("battlefield census serializer emits counts and rows", () =>
    {
        var rows = new[]
        {
            new BattlefieldUnitRow("unit:alpha", false, 60f, 100f, 20f, 30f, 2, false, true, true),
            new BattlefieldUnitRow("unit:beta", true, 0f, 50f, 0f, 0f, 0, true, false, false),
        };
        var census = new BattlefieldCensus(5, 2, 1, 1, 1, rows);
        string json = AgentDiagnosticsFormatter.SerializeBattlefieldCensus(census);
        if (!json.Contains("\"battlefieldEntityCount\":5") ||
            !json.Contains("\"playerUnitCount\":2") ||
            !json.Contains("\"aiUnitCount\":1") ||
            !json.Contains("\"aiBuildingCount\":1") ||
            !json.Contains("\"harvesterCount\":1") ||
            !json.Contains("\"playerUnitRowCount\":2") ||
            !json.Contains("\"entityKeyId\":\"unit:alpha\"") ||
            !json.Contains("\"healthRatio\":0.6") ||
            !json.Contains("\"isAlive\":false"))
        {
            throw new InvalidOperationException("SerializeBattlefieldCensus must report counts, rows, and per-unit health/armor/shield/rank state.");
        }

        string empty = AgentDiagnosticsFormatter.SerializeBattlefieldCensus(null);
        if (!empty.Contains("\"battlefieldEntityCount\":0") || !empty.Contains("\"playerUnitRows\":[]"))
        {
            throw new InvalidOperationException("SerializeBattlefieldCensus must emit an empty census for null input.");
        }
    }),
    ("entity balance lookup serializer emits base params", () =>
    {
        var row = new EntityBalanceRow("unit:alpha", false, 150, 200, 25f, 40, 120f, 3, 1.5f, 4f, 7.5f, 12f, 6f);
        string json = AgentDiagnosticsFormatter.SerializeEntityBalanceLookup(row);
        if (!json.Contains("\"entityId\":\"unit:alpha\"") ||
            !json.Contains("\"isBuilding\":false") ||
            !json.Contains("\"cost\":150") ||
            !json.Contains("\"maxHealth\":200") ||
            !json.Contains("\"maxArmor\":25") ||
            !json.Contains("\"maxShield\":40") ||
            !json.Contains("\"maxMana\":120") ||
            !json.Contains("\"maxRank\":3") ||
            !json.Contains("\"moveSpeed\":1.5") ||
            !json.Contains("\"weaponRange\":4") ||
            !json.Contains("\"dps\":7.5") ||
            !json.Contains("\"damage1\":12") ||
            !json.Contains("\"damage2\":6"))
        {
            throw new InvalidOperationException("SerializeEntityBalanceLookup must report the base balance parameters in field order.");
        }

        string empty = AgentDiagnosticsFormatter.SerializeEntityBalanceLookup(default(EntityBalanceRow));
        if (!empty.Contains("\"entityId\":\"\"") || !empty.Contains("\"cost\":0") || !empty.Contains("\"dps\":0"))
        {
            throw new InvalidOperationException("SerializeEntityBalanceLookup must emit neutral defaults for an empty row.");
        }
    }),
    ("card change request serializer emits request fields", () =>
    {
        var request = CardChangeRequest.Create(
            "MaxHealth", "Add", 25f, true, false, "Friendly", false,
            new[] { "unit:alpha" }, new[] { "unit:beta" }, "rcl-test");
        string json = AgentDiagnosticsFormatter.SerializeCardChangeRequest(request);
        if (!json.Contains("\"valueToChange\":\"MaxHealth\"") ||
            !json.Contains("\"operation\":\"Add\"") ||
            !json.Contains("\"value\":25") ||
            !json.Contains("\"multiplyValueWithCurrentHeatLevel\":true") ||
            !json.Contains("\"multiplyValueWithMetaProgressionMultiplier\":false") ||
            !json.Contains("\"side\":\"Friendly\"") ||
            !json.Contains("\"changeableValueHasToBeGreaterZero\":false") ||
            !json.Contains("\"onlyForTheseEntityIdsCount\":1") ||
            !json.Contains("\"entityIdsNotAllowedCount\":1") ||
            !json.Contains("\"sourceId\":\"rcl-test\""))
        {
            throw new InvalidOperationException("SerializeCardChangeRequest must report the request fields in field order.");
        }

        string empty = AgentDiagnosticsFormatter.SerializeCardChangeRequest(default(CardChangeRequest));
        if (!empty.Contains("\"valueToChange\":\"\"") || !empty.Contains("\"value\":0") || !empty.Contains("\"sourceId\":\"\""))
        {
            throw new InvalidOperationException("SerializeCardChangeRequest must emit neutral defaults for an empty request.");
        }
    }),
    ("runtime entity balance lookup reads base params from EntityBalancingStore", () =>
    {
        EntityBalancingStore.Reset();
        try
        {
            EntityBalancingStore.CostOf["unit:alpha"] = 150;
            EntityBalancingStore.MaxHealthOf["unit:alpha"] = 200;
            EntityBalancingStore.MaxArmorOf["unit:alpha"] = 25f;
            EntityBalancingStore.MaxShieldOf["unit:alpha"] = 40;
            EntityBalancingStore.MaxManaOf["unit:alpha"] = 120f;
            EntityBalancingStore.MaxRankOf["unit:alpha"] = 3;
            EntityBalancingStore.MoveSpeedOf["unit:alpha"] = 1.5f;
            EntityBalancingStore.WeaponRangeOf["unit:alpha"] = 4f;
            EntityBalancingStore.DpsOf["unit:alpha"] = 7.5f;
            EntityBalancingStore.Damage1Of["unit:alpha"] = 12f;
            EntityBalancingStore.Damage2Of["unit:alpha"] = 6f;
            EntityBalancingStore.BuildingEntityIds.Add("building:hq");

            EntityBalanceRow alpha = RuntimeEntityBalanceLookup.Current("unit:alpha");
            if (alpha.EntityId != "unit:alpha" || alpha.Cost != 150 || alpha.MaxHealth != 200 ||
                alpha.MaxArmor != 25f || alpha.MaxShield != 40 || alpha.MaxMana != 120f ||
                alpha.MaxRank != 3 || alpha.MoveSpeed != 1.5f || alpha.WeaponRange != 4f ||
                alpha.Dps != 7.5f || alpha.Damage1 != 12f || alpha.Damage2 != 6f || alpha.IsBuilding)
            {
                throw new InvalidOperationException("RuntimeEntityBalanceLookup must read base params from EntityBalancingStore.");
            }

            EntityBalanceRow building = RuntimeEntityBalanceLookup.Current("building:hq");
            if (!building.IsBuilding)
            {
                throw new InvalidOperationException("RuntimeEntityBalanceLookup must report IsBuilding from EntityBalancingStore.");
            }

            EntityBalanceRow unknown = RuntimeEntityBalanceLookup.Current("unit:unknown");
            if (unknown.EntityId != "unit:unknown" || unknown.Cost != 100 || unknown.MaxHealth != 100 ||
                unknown.MaxRank != 1 || unknown.IsBuilding)
            {
                throw new InvalidOperationException("RuntimeEntityBalanceLookup must return store defaults for an unknown entity id.");
            }

            EntityBalanceRow blank = RuntimeEntityBalanceLookup.Current("  ");
            if (!string.IsNullOrEmpty(blank.EntityId))
            {
                throw new InvalidOperationException("RuntimeEntityBalanceLookup must return an empty row for a blank entity id.");
            }
        }
        finally
        {
            EntityBalancingStore.Reset();
        }
    }),
    ("runtime battlefield census collects from the existing controllers registry", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            ExistingControllers.Instance.PlayerUnits().Add(new EntityController
            {
                entityId = "unit:player-1",
                CurrentHealth = 80f,
                MaxHealth = 100f,
                CurrentArmor = 10f,
                CurrentShield = 5f,
                CurrentRank = 1,
                IsBuilding = false,
                IsControlledByPlayer = true,
                IsControlledByAi = false,
            });
            ExistingControllers.Instance.AiUnits().Add(new EntityController
            {
                entityId = "unit:ai-1",
                CurrentHealth = 40f,
                MaxHealth = 40f,
                IsControlledByAi = true,
            });
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                entityId = "building:ai-hq",
                IsBuilding = true,
                IsControlledByAi = true,
            });
            ExistingControllers.Instance.Harvesters.Add(new EntityController
            {
                entityId = "unit:harvester-1",
                CurrentHealth = 30f,
                MaxHealth = 30f,
                IsControlledByPlayer = true,
            });

            BattlefieldCensus census = RuntimeBattlefieldCensus.Current();
            if (census.BattlefieldEntityCount != 3 ||
                census.PlayerUnitCount != 1 ||
                census.AiUnitCount != 1 ||
                census.AiBuildingCount != 1 ||
                census.HarvesterCount != 1 ||
                census.PlayerUnitRows.Count != 1 ||
                census.PlayerUnitRows[0].EntityKeyId != "unit:player-1" ||
                census.PlayerUnitRows[0].CurrentHealth != 80f ||
                census.PlayerUnitRows[0].HealthRatio != 0.8f ||
                census.PlayerUnitRows[0].CurrentArmor != 10f ||
                census.PlayerUnitRows[0].CurrentShield != 5f ||
                census.PlayerUnitRows[0].CurrentRank != 1)
            {
                throw new InvalidOperationException("RuntimeBattlefieldCensus must aggregate live player, AI, building, and harvester state from the registry.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("ai roster census serializer emits counts and rows", () =>
    {
        var rows = new[]
        {
            new AiRosterRow("unit:ai-1", false, 100, 80, 5f, 10f, 12f, 6f),
            new AiRosterRow("building:ai-hq", true, 300, 200, 0f, 0f, 0f, 0f),
        };
        var census = new AiRosterCensus(1, 1, new[] { rows[0] }, new[] { rows[1] });
        string json = AgentDiagnosticsFormatter.SerializeAiRoster(census);
        if (!json.Contains("\"aiUnitCount\":1") ||
            !json.Contains("\"aiBuildingCount\":1") ||
            !json.Contains("\"aiUnitRowCount\":1") ||
            !json.Contains("\"aiBuildingRowCount\":1") ||
            !json.Contains("\"entityKeyId\":\"unit:ai-1\"") ||
            !json.Contains("\"dps\":10") ||
            !json.Contains("\"entityKeyId\":\"building:ai-hq\""))
        {
            throw new InvalidOperationException("SerializeAiRoster must report counts and rows.");
        }

        string empty = AgentDiagnosticsFormatter.SerializeAiRoster(null);
        if (!empty.Contains("\"aiUnitCount\":0") || !empty.Contains("\"aiUnitRows\":[]") ||
            !empty.Contains("\"aiBuildingRows\":[]"))
        {
            throw new InvalidOperationException("SerializeAiRoster must emit empty arrays for null input.");
        }
    }),
    ("runtime ai roster census collects from the existing controllers registry", () =>
    {
        EntityBalancingStore.Reset();
        ExistingControllers.Instance.Reset();
        try
        {
            EntityBalancingStore.CostOf["unit:ai-1"] = 100;
            EntityBalancingStore.MaxHealthOf["unit:ai-1"] = 80;
            EntityBalancingStore.MoveSpeedOf["unit:ai-1"] = 5f;
            EntityBalancingStore.DpsOf["unit:ai-1"] = 10f;
            EntityBalancingStore.Damage1Of["unit:ai-1"] = 12f;
            EntityBalancingStore.WeaponRangeOf["unit:ai-1"] = 6f;

            ExistingControllers.Instance.AiUnits().Add(new EntityController
            {
                entityId = "unit:ai-1",
                IsControlledByAi = true,
            });
            ExistingControllers.Instance.AiBuildings().Add(new EntityController
            {
                entityId = "building:ai-hq",
                IsBuilding = true,
                IsControlledByAi = true,
            });

            AiRosterCensus census = RuntimeAiRosterCensus.Current();
            if (census.AiUnitCount != 1 || census.AiBuildingCount != 1 ||
                census.AiUnitRows.Count != 1 || census.AiBuildingRows.Count != 1 ||
                census.AiUnitRows[0].EntityKeyId != "unit:ai-1" ||
                census.AiUnitRows[0].Cost != 100 ||
                census.AiUnitRows[0].MaxHealth != 80 ||
                census.AiUnitRows[0].MoveSpeed != 5f ||
                census.AiUnitRows[0].Dps != 10f ||
                census.AiBuildingRows[0].EntityKeyId != "building:ai-hq" ||
                !census.AiBuildingRows[0].IsBuilding)
            {
                throw new InvalidOperationException("RuntimeAiRosterCensus must aggregate live AI units and buildings with balance.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            EntityBalancingStore.Reset();
        }
    }),
    ("player roster census serializer emits counts and rows", () =>
    {
        var rows = new[]
        {
            new PlayerRosterRow("unit:player-1", false, 80f, 100f, 0.8f, 150, 5f, 10f, 12f, 6f),
        };
        var census = new PlayerRosterCensus(1, rows);
        string json = AgentDiagnosticsFormatter.SerializePlayerRoster(census);
        if (!json.Contains("\"playerUnitCount\":1") ||
            !json.Contains("\"playerUnitRowCount\":1") ||
            !json.Contains("\"entityKeyId\":\"unit:player-1\"") ||
            !json.Contains("\"healthRatio\":0.8") ||
            !json.Contains("\"cost\":150"))
        {
            throw new InvalidOperationException("SerializePlayerRoster must report counts and rows.");
        }

        string empty = AgentDiagnosticsFormatter.SerializePlayerRoster(null);
        if (!empty.Contains("\"playerUnitCount\":0") || !empty.Contains("\"playerUnitRows\":[]"))
        {
            throw new InvalidOperationException("SerializePlayerRoster must emit empty arrays for null input.");
        }
    }),
    ("runtime player roster census collects from the battlefield census and balance store", () =>
    {
        EntityBalancingStore.Reset();
        ExistingControllers.Instance.Reset();
        try
        {
            EntityBalancingStore.CostOf["unit:player-1"] = 150;
            EntityBalancingStore.MaxHealthOf["unit:player-1"] = 100;
            EntityBalancingStore.MoveSpeedOf["unit:player-1"] = 5f;
            EntityBalancingStore.DpsOf["unit:player-1"] = 10f;
            EntityBalancingStore.Damage1Of["unit:player-1"] = 12f;
            EntityBalancingStore.WeaponRangeOf["unit:player-1"] = 6f;

            ExistingControllers.Instance.PlayerUnits().Add(new EntityController
            {
                entityId = "unit:player-1",
                CurrentHealth = 80f,
                MaxHealth = 100f,
                IsControlledByPlayer = true,
            });

            PlayerRosterCensus census = RuntimePlayerRosterCensus.Current();
            if (census.PlayerUnitCount != 1 || census.PlayerUnitRows.Count != 1 ||
                census.PlayerUnitRows[0].EntityKeyId != "unit:player-1" ||
                census.PlayerUnitRows[0].Cost != 150 ||
                census.PlayerUnitRows[0].MaxHealth != 100 ||
                census.PlayerUnitRows[0].CurrentHealth != 80f ||
                census.PlayerUnitRows[0].HealthRatio != 0.8f)
            {
                throw new InvalidOperationException("RuntimePlayerRosterCensus must aggregate live player units with balance.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            EntityBalancingStore.Reset();
        }
    }),
    ("identifier color policy hashes with FNV-1a and maps to a stable palette slot", () =>
    {
        if (IdentifierColorPolicy.Hash(string.Empty) != 0x811C9DC5u)
        {
            throw new InvalidOperationException("Empty identifier must yield the FNV-1a 32-bit offset basis.");
        }

        if (IdentifierColorPolicy.Hash("a") != 0xE40C292Cu)
        {
            throw new InvalidOperationException("The hash must follow the public FNV-1a 32-bit test vector for 'a'.");
        }

        if (IdentifierColorPolicy.Hash("foobar") != 0xBF9CF968u)
        {
            throw new InvalidOperationException("The hash must follow the public FNV-1a 32-bit test vector for 'foobar'.");
        }

        int paletteSize = IdentifierColorPolicy.PaletteSize;
        if (paletteSize <= 0 || (paletteSize & (paletteSize - 1)) != 0)
        {
            throw new InvalidOperationException("The palette size must be a positive power of two for even index distribution.");
        }

        int index = IdentifierColorPolicy.PaletteIndex("sample-native:scout-tank");
        if (index < 0 || index >= paletteSize)
        {
            throw new InvalidOperationException("The palette index must stay within the palette bounds.");
        }
    }),
    ("identifier color policy is deterministic and stable across calls", () =>
    {
        string id = "sample-native:expansion";
        UnityEngine.Color first = IdentifierColorPolicy.Resolve(id);
        UnityEngine.Color second = IdentifierColorPolicy.Resolve(id);
        if (first.r != second.r || first.g != second.g || first.b != second.b || first.a != second.a)
        {
            throw new InvalidOperationException("The same identifier must always resolve to the same accent color.");
        }

        if (IdentifierColorPolicy.Resolve(id) != IdentifierColorPolicy.Resolve(id))
        {
            throw new InvalidOperationException("Color equality must be consistent for the same identifier.");
        }
    }),
    ("agent diagnostics formatter exposes clean-room and visual utilities as JSON", () =>
    {
        var packs = new List<PackDiagnosticReport>
        {
            new PackDiagnosticReport(
                "sample-native:expansion", "sample-native-expansion", true, "Cargado correctamente.",
                1, 2048L, 5, 3, 3),
        };
        var units = new List<UnitDiagnosticReport>
        {
            new UnitDiagnosticReport(
                "sample-native:scout-tank", "sample-native:expansion", "scout.prefab", "scout.png",
                true, false, true, false, false, null, 0),
        };
        var stages = new List<StageDiagnosticReport>
        {
            new StageDiagnosticReport("stage-a", "sample-native:expansion", 24, 48,
                new[] { "sample-native:scout-tank" }, new[] { "default" }),
        };
        var aiProfiles = new List<AiProfileDiagnosticReport>
        {
            new AiProfileDiagnosticReport("ai-a", "sample-native:expansion", "ai.prefab",
                new[] { "module-x" }, 1),
        };
        var snapshot = new LoaderDiagnosticsSnapshot(
            "1.0.9", true, true, 1, 0, 2048L,
            packs, units, stages, aiProfiles,
            Array.Empty<UnitFactoryDiagnosticReport>(),
            Array.Empty<StagePoolScopeDiagnosticReport>(),
            Array.Empty<AiProfileStageRangeDiagnosticReport>(),
            Array.Empty<UnitEligibilityDiagnosticReport>(),
            Array.Empty<UnitAssetResolutionDiagnosticReport>(),
            Array.Empty<UnitLocalizationCoverageDiagnosticReport>(),
            new[] { "12:00:00 loaded alpha", "12:00:01 loaded alpha" });

        string snapshotJson = AgentDiagnosticsFormatter.SerializeSnapshot(snapshot);
        if (string.IsNullOrEmpty(snapshotJson) ||
            !snapshotJson.Contains("\"kind\":\"loaderDiagnostics\"") ||
            !snapshotJson.Contains("\"sample-native:scout-tank\""))
        {
            throw new InvalidOperationException("The snapshot JSON must expose packs, units, and identifiers.");
        }

        string utilitiesJson = AgentDiagnosticsFormatter.SerializeUtilities(snapshot, null, null);
        if (!utilitiesJson.Contains("\"kind\":\"utilities\"") ||
            !utilitiesJson.Contains("\"cleanRoom\"") ||
            !utilitiesJson.Contains("\"visual\"") ||
            !utilitiesJson.Contains("\"inspector\"") ||
            !utilitiesJson.Contains("\"rangeDisplay\""))
        {
            throw new InvalidOperationException("The utilities JSON must expose clean-room and visual utility state.");
        }

        string colorJson = AgentDiagnosticsFormatter.SerializeIdentifierColor("sample-native:scout-tank");
        if (!colorJson.Contains("\"hex\":\"#") || !colorJson.Contains("\"paletteSize\":16"))
        {
            throw new InvalidOperationException("The identifier color JSON must expose a hex color and palette size.");
        }
    }),
    ("range display policy exposes authored per-kind colors", () =>
    {
        UnityEngine.Color weapon = RangeDisplayPolicy.RangeColor(RangeDisplayKind.Weapon);
        UnityEngine.Color skill = RangeDisplayPolicy.RangeColor(RangeDisplayKind.Skill);
        UnityEngine.Color effect1 = RangeDisplayPolicy.RangeColor(RangeDisplayKind.Effect1);
        UnityEngine.Color effect2 = RangeDisplayPolicy.RangeColor(RangeDisplayKind.Effect2);
        UnityEngine.Color sight = RangeDisplayPolicy.RangeColor(RangeDisplayKind.Sight);

        if (Math.Abs(weapon.r - 0.95f) > 0.001f || Math.Abs(weapon.g - 0.18f) > 0.001f ||
            Math.Abs(weapon.b - 0.12f) > 0.001f || Math.Abs(weapon.a - 0.82f) > 0.001f)
        {
            throw new InvalidOperationException("The weapon range color must match the authored red value.");
        }

        if (Math.Abs(skill.r - 0.12f) > 0.001f || Math.Abs(skill.g - 0.48f) > 0.001f ||
            Math.Abs(skill.b - 0.95f) > 0.001f || Math.Abs(skill.a - 0.82f) > 0.001f)
        {
            throw new InvalidOperationException("The skill range color must match the authored blue value.");
        }

        if (Math.Abs(effect1.r - 0.98f) > 0.001f || Math.Abs(effect1.g - 0.48f) > 0.001f ||
            Math.Abs(effect1.b - 0.08f) > 0.001f || Math.Abs(effect1.a - 0.82f) > 0.001f)
        {
            throw new InvalidOperationException("The effect1 range color must match the authored orange value.");
        }

        if (Math.Abs(effect2.r - 0.96f) > 0.001f || Math.Abs(effect2.g - 0.72f) > 0.001f ||
            Math.Abs(effect2.b - 0.18f) > 0.001f || Math.Abs(effect2.a - 0.82f) > 0.001f)
        {
            throw new InvalidOperationException("The effect2 range color must match the authored gold value.");
        }

        if (Math.Abs(sight.r - 0.72f) > 0.001f || Math.Abs(sight.g - 0.76f) > 0.001f ||
            Math.Abs(sight.b - 0.82f) > 0.001f || Math.Abs(sight.a - 0.62f) > 0.001f)
        {
            throw new InvalidOperationException("The sight range color must fall back to the authored neutral value.");
        }
    }),
    ("identifier color policy exposes the full palette by index", () =>
    {
        if (IdentifierColorPolicy.PaletteSize != 16)
        {
            throw new InvalidOperationException("The authored palette must expose 16 entries.");
        }

        UnityEngine.Color first = IdentifierColorPolicy.PaletteColorAt(0);
        UnityEngine.Color firstAgain = IdentifierColorPolicy.PaletteColorAt(0);
        if (first != firstAgain)
        {
            throw new InvalidOperationException("Palette lookups must be deterministic.");
        }

        for (int i = 0; i < IdentifierColorPolicy.PaletteSize; i++)
        {
            IdentifierColorPolicy.PaletteColorAt(i);
        }

        bool threwLow = false;
        bool threwHigh = false;
        try
        {
            IdentifierColorPolicy.PaletteColorAt(-1);
        }
        catch (System.ArgumentOutOfRangeException)
        {
            threwLow = true;
        }

        try
        {
            IdentifierColorPolicy.PaletteColorAt(IdentifierColorPolicy.PaletteSize);
        }
        catch (System.ArgumentOutOfRangeException)
        {
            threwHigh = true;
        }

        if (!threwLow || !threwHigh)
        {
            throw new InvalidOperationException("Palette lookups must reject out-of-range indices.");
        }
    }),
    ("agent diagnostics formatter exposes range colors and the full palette as JSON", () =>
    {
        string rangeColorsJson = AgentDiagnosticsFormatter.SerializeRangeColors();
        if (!rangeColorsJson.Contains("\"kind\":\"rangeColors\"") ||
            !rangeColorsJson.Contains("\"weapon\":\"#") ||
            !rangeColorsJson.Contains("\"skill\":\"#") ||
            !rangeColorsJson.Contains("\"effect1\":\"#") ||
            !rangeColorsJson.Contains("\"effect2\":\"#") ||
            !rangeColorsJson.Contains("\"sight\":\"#"))
        {
            throw new InvalidOperationException("The range colors JSON must expose a hex color per range kind.");
        }

        string paletteJson = AgentDiagnosticsFormatter.SerializeIdentifierPalette();
        if (!paletteJson.Contains("\"kind\":\"identifierPalette\"") ||
            !paletteJson.Contains("\"paletteSize\":16") ||
            !paletteJson.Contains("\"colors\":["))
        {
            throw new InvalidOperationException("The palette JSON must expose the palette size and the color list.");
        }

        var overlayState = new OverlayStateView(
            true, false, "F8", 2, true, false, false, "alpha", "1.19.0", string.Empty, true, "ok");
        var rangeState = new RangeStateView(
            true, true, "F11", 3, true, false, true, false, 0.08f, 64, 0.15f);
        string utilitiesJson = AgentDiagnosticsFormatter.SerializeUtilities(null, overlayState, rangeState);
        if (!utilitiesJson.Contains("\"rangeColors\"") ||
            !utilitiesJson.Contains("\"identifierPalette\"") ||
            !utilitiesJson.Contains("\"selectedTab\":2") ||
            !utilitiesJson.Contains("\"guiDisabled\":false") ||
            !utilitiesJson.Contains("\"showWeaponRange\":true") ||
            !utilitiesJson.Contains("\"lineWidth\":0.08") ||
            !utilitiesJson.Contains("\"segments\":64") ||
            !utilitiesJson.Contains("\"heightOffset\":0.15") ||
            !utilitiesJson.Contains("\"pluginVersion\":\"1.19.0\"") ||
            !utilitiesJson.Contains("\"patchResultSuccess\":true"))
        {
            throw new InvalidOperationException("The utilities JSON must expose the extended overlay and range display fields.");
        }
    }),
    ("agent diagnostics formatter returns an available=false snapshot when provider is null", () =>
    {
        string snapshotJson = AgentDiagnosticsFormatter.SerializeSnapshot(null);
        if (!snapshotJson.Contains("\"available\":false"))
        {
            throw new InvalidOperationException("A null snapshot must serialize as available=false, not throw.");
        }
    }),
    ("range display resolves selected player radii in world units", () =>
    {
        RangeDisplayEntityState entity = new RangeDisplayEntityState(
            isSelected: true, isPlayerControlled: true, isDestroyed: false,
            canAttack: true, isMelee: false, weaponRange: 4f,
            hasActiveSkill: true, skillRangeIsUnlimited: false, skillRange: 6f,
            effectRadius1: 2f, effectRadius2: 3f, sightRadius: 8f);
        RangeDisplaySpec[] specs = RangeDisplayPolicy.Resolve(entity, RangeDisplayOptions.Default);
        RangeDisplaySpec[] disabled = RangeDisplayPolicy.Resolve(entity, RangeDisplayOptions.Default, enabled: false);
        if (disabled.Length != 0)
        {
            throw new InvalidOperationException("Disabled range display must not resolve circles.");
        }
        RangeDisplayKind[] expectedKinds =
        {
            RangeDisplayKind.Weapon, RangeDisplayKind.Skill,
            RangeDisplayKind.Effect1, RangeDisplayKind.Effect2
        };
        float[] expectedRadii = { 40f, 60f, 20f, 30f };
        if (specs.Length != expectedKinds.Length)
        {
            throw new InvalidOperationException("Range display must resolve each enabled non-sight radius and omit sight by default.");
        }
        for (int i = 0; i < specs.Length; i++)
        {
            if (specs[i].Kind != expectedKinds[i] || Math.Abs(specs[i].Radius - expectedRadii[i]) > 0.001f)
            {
                throw new InvalidOperationException("Range display kinds must be ordered and scaled from cells to world units.");
            }
        }
    }),
    ("range display rejects ineligible, unlimited, melee, tiny, and duplicate radii", () =>
    {
        RangeDisplayEntityState baseEntity = new RangeDisplayEntityState(
            isSelected: true, isPlayerControlled: true, isDestroyed: false,
            canAttack: true, isMelee: false, weaponRange: 0.2f,
            hasActiveSkill: true, skillRangeIsUnlimited: true, skillRange: 10f,
            effectRadius1: 2f, effectRadius2: 2.02f, sightRadius: 4f);
        RangeDisplaySpec[] specs = RangeDisplayPolicy.Resolve(baseEntity, new RangeDisplayOptions(
            showWeapon: true, showSkill: true, showEffects: true, showSight: true));
        if (specs.Length != 2 || specs[0].Kind != RangeDisplayKind.Effect1 ||
            specs[1].Kind != RangeDisplayKind.Sight || Math.Abs(specs[1].Radius - 40f) > 0.001f)
        {
            throw new InvalidOperationException("Range display must filter tiny weapon, unlimited skill, and coincident radii.");
        }

        RangeDisplayEntityState[] ineligible =
        {
            new RangeDisplayEntityState(false, true, false, true, false, 4f, false, false, 0f, 0f, 0f, 0f),
            new RangeDisplayEntityState(true, false, false, true, false, 4f, false, false, 0f, 0f, 0f, 0f),
            new RangeDisplayEntityState(true, true, true, true, false, 4f, false, false, 0f, 0f, 0f, 0f)
        };
        for (int i = 0; i < ineligible.Length; i++)
        {
            if (RangeDisplayPolicy.Resolve(ineligible[i], RangeDisplayOptions.Default).Length != 0)
            {
                throw new InvalidOperationException("Only living, selected, player-controlled entities may receive range circles.");
            }
        }

        RangeDisplayEntityState melee = new RangeDisplayEntityState(
            true, true, false, true, true, 8f, false, false, 0f, 0f, 0f, 0f);
        if (RangeDisplayPolicy.Resolve(melee, RangeDisplayOptions.Default).Length != 0)
        {
            throw new InvalidOperationException("Melee entities must not receive a weapon-range circle.");
        }
    }),
    ("range display selection tracker reports deterministic additions and removals", () =>
    {
        var tracker = new RangeDisplaySelectionTracker<int>();
        RangeDisplaySelectionDelta<int> first = tracker.Update(new[] { 3, 1, 2 });
        if (!first.Added.SequenceEqual(new[] { 1, 2, 3 }) || first.Removed.Length != 0)
        {
            throw new InvalidOperationException("A new selection snapshot must report sorted additions and no removals.");
        }

        RangeDisplaySelectionDelta<int> second = tracker.Update(new[] { 2, 4 });
        if (!second.Added.SequenceEqual(new[] { 4 }) || !second.Removed.SequenceEqual(new[] { 1, 3 }))
        {
            throw new InvalidOperationException("Selection reconciliation must report only the changed entities in stable order.");
        }

        RangeDisplaySelectionDelta<int> cleared = tracker.Update(Array.Empty<int>());
        if (cleared.Added.Length != 0 || !cleared.Removed.SequenceEqual(new[] { 2, 4 }))
        {
            throw new InvalidOperationException("Clearing a selection snapshot must release every tracked entity.");
        }
    }),
    ("range display circle geometry closes at the requested radius", () =>
    {
        const float radius = 40f;
        const int segments = 32;
        const float heightOffset = 0.15f;
        UnityEngine.Vector3[] points = RangeDisplayGeometry.CreateCirclePoints(radius, segments, heightOffset);
        if (points.Length != segments + 1)
        {
            throw new InvalidOperationException("Circle geometry must include one closing vertex beyond its segment count.");
        }

        UnityEngine.Vector3 first = points[0];
        UnityEngine.Vector3 last = points[points.Length - 1];
        if (Math.Abs(first.x - last.x) > 0.001f || Math.Abs(first.z - last.z) > 0.001f ||
            Math.Abs(first.y - heightOffset) > 0.001f)
        {
            throw new InvalidOperationException("The generated range circle must close and preserve its height offset.");
        }

        for (int i = 0; i < segments; i++)
        {
            float pointRadius = (float)Math.Sqrt(points[i].x * points[i].x + points[i].z * points[i].z);
            if (Math.Abs(pointRadius - radius) > 0.001f || Math.Abs(points[i].y - heightOffset) > 0.001f)
            {
                throw new InvalidOperationException("Every range circle vertex must lie on the requested XZ radius and height.");
            }
        }

        foreach (Action invalidGeometry in new Action[]
        {
            () => RangeDisplayGeometry.CreateCirclePoints(0f, segments, heightOffset),
            () => RangeDisplayGeometry.CreateCirclePoints(float.NaN, segments, heightOffset),
            () => RangeDisplayGeometry.CreateCirclePoints(radius, 2, heightOffset),
            () => RangeDisplayGeometry.CreateCirclePoints(radius, 257, heightOffset),
            () => RangeDisplayGeometry.CreateCirclePoints(radius, segments, float.PositiveInfinity)
        })
        {
            bool rejected = false;
            try
            {
                invalidGeometry();
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }
            if (!rejected)
            {
                throw new InvalidOperationException("Circle geometry must reject invalid radius, segment, and height inputs.");
            }
        }
    }),
    ("visual overlays correct only models authored facing backward", () =>
    {
        float backwardCorrection = VisualOverlayOrientation.CalculateYawCorrectionDegrees(-2f, 1f);
        float forwardCorrection = VisualOverlayOrientation.CalculateYawCorrectionDegrees(2f, -1f);
        float ambiguousCorrection = VisualOverlayOrientation.CalculateYawCorrectionDegrees(0.01f, 0f);
        if (Math.Abs(backwardCorrection - 180f) > 0.001f ||
            Math.Abs(forwardCorrection) > 0.001f ||
            Math.Abs(ambiguousCorrection) > 0.001f)
        {
            throw new InvalidOperationException("Overlay orientation must flip only when its muzzle lies clearly behind the rear anchor.");
        }
    }),
    ("visual overlay scale calibration fits both undersized and oversized models", () =>
    {
        float combinedExtent = VisualOverlayScaleCalibration.CalculateCombinedHalfExtent(new[]
        {
            new VisualRendererBounds(0f, 0f, 0f, 2f, 4f, 4f),
            new VisualRendererBounds(3f, 0f, 0f, 2f, 2f, 2f)
        });
        if (Math.Abs(combinedExtent - 2.5f) > 0.001f)
        {
            throw new InvalidOperationException("Overlay calibration must aggregate renderer bounds before choosing a uniform extent.");
        }

        if (Math.Abs(VisualOverlayScaleCalibration.CalculateTargetHalfExtent(5f, false) - 4f) > 0.001f ||
            Math.Abs(VisualOverlayScaleCalibration.CalculateTargetHalfExtent(0f, false) - 0.85f) > 0.001f ||
            Math.Abs(VisualOverlayScaleCalibration.CalculateTargetHalfExtent(float.NaN, true) - 1.60f) > 0.001f)
        {
            throw new InvalidOperationException("Native, unit-fallback, and building-fallback extents must be selected deterministically.");
        }

        float undersizedScale = VisualOverlayScaleCalibration.CalculateUniformScale(0.25f, 0.85f);
        float oversizedScale = VisualOverlayScaleCalibration.CalculateUniformScale(6.0f, 0.85f);
        float matchingScale = VisualOverlayScaleCalibration.CalculateUniformScale(0.85f, 0.85f);
        if (Math.Abs(undersizedScale - 3.4f) > 0.001f ||
            Math.Abs(oversizedScale - (0.85f / 6.0f)) > 0.001f ||
            Math.Abs(matchingScale - 1.0f) > 0.001f)
        {
            throw new InvalidOperationException(
                "Visual scale must match measured renderer extents without the current 2.5x cap.");
        }

        // Scale clamp limits.
        if (Math.Abs(VisualOverlayScaleCalibration.CalculateUniformScale(0.0011f, 100f) - 100f) > 0.001f ||
            Math.Abs(VisualOverlayScaleCalibration.CalculateUniformScale(100f, 1f) - 0.01f) > 0.001f)
        {
            throw new InvalidOperationException("Visual scale calibration must honor its 0.01 and 100 safety limits.");
        }

        // Invalid target extents (NaN, infinity, zero, negative) must be rejected.
        foreach (float invalidTarget in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            bool rejected = false;
            try
            {
                VisualOverlayScaleCalibration.CalculateUniformScale(1f, invalidTarget);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }
            if (!rejected)
            {
                throw new InvalidOperationException("Visual scale calibration must reject invalid target extents.");
            }
        }

        // Invalid model extents (NaN, infinity, negative) must be rejected.
        foreach (float invalidModel in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            bool rejected = false;
            try
            {
                VisualOverlayScaleCalibration.CalculateUniformScale(invalidModel, 0.85f);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }
            if (!rejected)
            {
                throw new InvalidOperationException("Visual scale calibration must reject invalid model extents.");
            }
        }

        // Building vs unit fallback selection.
        if (Math.Abs(VisualOverlayScaleCalibration.CalculateTargetHalfExtent(0f, true) - 1.60f) > 0.001f ||
            Math.Abs(VisualOverlayScaleCalibration.CalculateTargetHalfExtent(0f, false) - 0.85f) > 0.001f ||
            Math.Abs(VisualOverlayScaleCalibration.CalculateTargetHalfExtent(float.NaN, true) - 1.60f) > 0.001f ||
            Math.Abs(VisualOverlayScaleCalibration.CalculateTargetHalfExtent(float.PositiveInfinity, false) - 0.85f) > 0.001f)
        {
            throw new InvalidOperationException("Native, unit-fallback, and building-fallback extents must be selected deterministically.");
        }
        foreach (float invalidExtent in new[] { 0f, 0.001f })
        {
            bool rejectsInvalidExtent = false;
            try
            {
                VisualOverlayScaleCalibration.CalculateUniformScale(invalidExtent, 0.85f);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejectsInvalidExtent = true;
            }

            if (!rejectsInvalidExtent)
            {
                throw new InvalidOperationException("Visual scale calibration must reject zero and degenerate renderer extents.");
            }
        }
        if (Math.Abs(VisualOverlayScaleCalibration.CalculateUniformScale(0.0011f, 0.85f) - 100f) > 0.001f)
        {
            throw new InvalidOperationException("Visual scale calibration must keep valid tiny extents within its safety clamp.");
        }

        if (VisualOverlayScaleCalibration.IsUsableRendererExtent(0f) ||
            VisualOverlayScaleCalibration.IsUsableRendererExtent(0.001f) ||
            VisualOverlayScaleCalibration.IsUsableRendererExtent(float.NaN) ||
            VisualOverlayScaleCalibration.IsUsableRendererExtent(float.PositiveInfinity) ||
            !VisualOverlayScaleCalibration.IsUsableRendererExtent(0.0011f))
        {
            throw new InvalidOperationException("Renderer extents must reject empty bounds and accept measurable geometry.");
        }

        if (!VisualOverlayScaleCalibration.IsExcludedRendererObjectName("Armor_COLLIDER_Hull") ||
            !VisualOverlayScaleCalibration.IsExcludedRendererObjectName("AntennaMast") ||
            !VisualOverlayScaleCalibration.IsExcludedRendererObjectName("Socket_MuzzlePrimary") ||
            !VisualOverlayScaleCalibration.IsExcludedRendererHierarchy(new[] { "Vehicle", "Socket_MuzzlePrimary", "Mesh" }) ||
            VisualOverlayScaleCalibration.IsExcludedRendererObjectName("HullArmor") ||
            VisualOverlayScaleCalibration.IsExcludedRendererHierarchy(new[] { "Vehicle", "HullArmor" }))
        {
            throw new InvalidOperationException("Renderer-bound calibration must exclude helper geometry anywhere in the hierarchy.");
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
            "EntityAttackIsTargetInRangePostfix",
            "EntityControllerDestroyPostfix",
            "EntityControllerDiePostfix",
            "EntityControllerHealPostfix",
            "EntityControllerRepairArmorPostfix"
        ];

        foreach (string hookName in hookNames)
        {
            if (typeof(RuntimeHarmonyPatches).GetMethod(hookName,
                    BindingFlags.Public | BindingFlags.Static) == null)
            {
                throw new InvalidOperationException($"RuntimePatchInstaller expects missing hook '{hookName}'.");
            }
        }

        MethodInfo? destroyedEntityHook = typeof(RuntimeHarmonyPatches).GetMethod(
            "EntityControllerDestroyPostfix", BindingFlags.Public | BindingFlags.Static);
        ParameterInfo[] destroyedEntityParameters = destroyedEntityHook?.GetParameters() ?? Array.Empty<ParameterInfo>();
        if (destroyedEntityHook == null || destroyedEntityHook.ReturnType != typeof(void) ||
            destroyedEntityParameters.Length != 1 || destroyedEntityParameters[0].ParameterType != typeof(object))
        {
            throw new InvalidOperationException("EntityControllerDestroyPostfix has an invalid Harmony signature.");
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
    ("reused entity clears runtime mechanics when its next profile has no component mechanics", () =>
    {
        RuntimeUnitMechanicsDirector.ClearProfiles();
        UnityEngine.Time.time = 10f;

        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:reuse-source", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:reuse-source",
                ShieldRegen = new ContentUnitShieldRegenDefinition
                {
                    rechargeRate = 10f,
                    rechargeDelay = 0f
                }
            });
            RuntimeUnitMechanicsDirector.RegisterProfile("test:reuse-descriptive-only", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:reuse-descriptive-only",
                MinAttackRange = 4f
            });

            var reused = new EntityController
            {
                entityId = "test:reuse-source",
                UniqueEntityId = 401,
                HasShield = true,
                CurrentShield = 0f,
                MaxShield = 50f
            };

            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);
            RuntimeUnitMechanicsDirector director = reused.gameObject.GetComponent<RuntimeUnitMechanicsDirector>()
                ?? throw new InvalidOperationException("The source shield-regeneration profile did not attach its director.");

            reused.entityId = "test:reuse-descriptive-only";
            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);
            UnityEngine.Time.time = 11f;
            director.TickMechanics();
            if (reused.CurrentShield != 0f)
            {
                throw new InvalidOperationException("A reused entity with a descriptive-only profile retained the previous shield-regeneration mechanic.");
            }

            reused.entityId = "test:reuse-source";
            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);
            reused.entityId = "test:reuse-no-profile";
            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);
            UnityEngine.Time.time = 12f;
            director.TickMechanics();
            if (reused.CurrentShield != 0f)
            {
                throw new InvalidOperationException("A reused entity without a profile retained the previous shield-regeneration mechanic.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            UnityEngine.Time.time = 0f;
        }
    }),
    ("reused entity releases baseline status effects when its next profile has no mechanics", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();

        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:reuse-baseline", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:reuse-baseline",
                BaselineStatusEffects = new[] { "Armored" }
            });

            var reused = new EntityController
            {
                entityId = "test:reuse-baseline",
                UniqueEntityId = 811,
                IsUnit = true
            };
            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);
            if (!reused.AppliedStatusEffects.Any(effect => effect.Effect == StatusEffect.Armored && effect.DurationType == SetStatusEffect.DurationType.Unlimited))
            {
                throw new InvalidOperationException("The baseline status effect was not initially applied.");
            }

            reused.entityId = "test:reuse-no-profile";
            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);

            if (reused.HasActiveStatusEffect(StatusEffect.Armored))
            {
                throw new InvalidOperationException("A baseline status effect from the old profile remained after entity reuse.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
        }
    }),
    ("reused entity releases command followers when its next profile has no mechanics", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        UnityEngine.Time.time = 10f;

        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:reuse-commander", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:reuse-commander",
                Command = new ContentUnitCommandDefinition
                {
                    commandType = "Attack",
                    commandRange = 4f,
                    maxCommandChain = 4
                }
            });

            var reused = new EntityController
            {
                entityId = "test:reuse-commander",
                UniqueEntityId = 812,
                IsUnit = true,
                IsControlledByPlayer = true,
                Position = new UnityEngine.Vector3(0f, 0f, 0f)
            };
            var follower = new EntityController
            {
                entityId = "test:reuse-follower",
                UniqueEntityId = 813,
                IsUnit = true,
                IsControlledByPlayer = true,
                Position = new UnityEngine.Vector3(5f, 0f, 0f)
            };
            ExistingControllers.Instance.PlayerUnits().Add(reused);
            ExistingControllers.Instance.PlayerUnits().Add(follower);

            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);
            RuntimeUnitMechanicsDirector director = reused.gameObject.GetComponent<RuntimeUnitMechanicsDirector>()
                ?? throw new InvalidOperationException("The commander profile did not attach its director.");
            UnityEngine.Time.time = 10.6f;
            director.TickMechanics();
            if (!ReferenceEquals(follower.CaptainOfThisEntity, reused))
            {
                throw new InvalidOperationException("The command pulse did not attach its eligible follower.");
            }

            reused.entityId = "test:reuse-no-profile";
            RuntimeUnitMechanicsDirector.AttachIfApplicable(reused);
            if (follower.CaptainOfThisEntity != null)
            {
                throw new InvalidOperationException("A command follower remained attached to a commander after entity reuse.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            UnityEngine.Time.time = 0f;
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
    ("reattaching an active entity preserves its accumulated combat experience", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();

        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:reattach-xp", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:reattach-xp",
                Experience = new ContentUnitExperienceDefinition
                {
                    experiencePerLevel = 1,
                    maxLevel = 3,
                    experienceGainMultiplier = 1f
                },
                ShieldRegen = new ContentUnitShieldRegenDefinition
                {
                    rechargeRate = 1f,
                    rechargeDelay = 0f
                }
            });

            var attacker = new EntityController
            {
                entityId = "test:reattach-xp",
                UniqueEntityId = 702,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentRank = 0,
                MaxRank = 3
            };
            var target = new EntityController
            {
                entityId = "test:reattach-target",
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f
            };

            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);
            RuntimeUnitMechanicsDirector.AttachIfApplicable(attacker);
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);

            if (attacker.CurrentRank != 1)
            {
                throw new InvalidOperationException("Reattaching an active entity discarded its accumulated combat experience.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
        }
    }),
    ("destroyed entity combat experience is cleared by its lifecycle hook", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        Bank.Reset();

        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:destroy-cleanup", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:destroy-cleanup",
                Experience = new ContentUnitExperienceDefinition
                {
                    experiencePerLevel = 1,
                    maxLevel = 3,
                    experienceGainMultiplier = 1f
                }
            });

            var attacker = new EntityController
            {
                entityId = "test:destroy-cleanup",
                UniqueEntityId = 501,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentRank = 0,
                MaxRank = 3
            };
            var target = new EntityController
            {
                entityId = "test:destroy-target",
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f
            };

            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);

            MethodInfo destroyHook = typeof(RuntimeHarmonyPatches).GetMethod(
                "EntityControllerDestroyPostfix", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("EntityController.Destroy cleanup hook is missing.");
            destroyHook.Invoke(null, new object[] { attacker });

            var replacement = new EntityController
            {
                entityId = "test:destroy-cleanup",
                UniqueEntityId = 501,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentRank = 0,
                MaxRank = 3
            };
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, replacement);

            if (replacement.CurrentRank != 0)
            {
                throw new InvalidOperationException("Destroyed entity experience leaked into a later entity instance.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            Bank.Reset();
        }
    }),
    ("destroyed entity combat experience fallback key is cleared", () =>
    {
        ExistingControllers.Instance.Reset();
        RuntimeUnitMechanicsDirector.ClearProfiles();
        Bank.Reset();

        try
        {
            RuntimeUnitMechanicsDirector.RegisterProfile("test:destroy-fallback-cleanup", new UnitRuntimeMechanicsProfile
            {
                EntityId = "test:destroy-fallback-cleanup",
                Experience = new ContentUnitExperienceDefinition
                {
                    experiencePerLevel = 1,
                    maxLevel = 3,
                    experienceGainMultiplier = 1f
                }
            });

            var attacker = new EntityController
            {
                entityId = "test:destroy-fallback-cleanup",
                UniqueEntityId = -1,
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentRank = 0,
                MaxRank = 3
            };
            var target = new EntityController
            {
                entityId = "test:destroy-target",
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f
            };

            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);
            RuntimeUnitMechanicsDirector.OnEntityDestroyed(attacker);
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(target, 1f, attacker);

            if (attacker.CurrentRank != 0)
            {
                throw new InvalidOperationException("Destroyed entity retained combat XP under its fallback identity.");
            }
        }
        finally
        {
            RuntimeUnitMechanicsDirector.ClearProfiles();
            ExistingControllers.Instance.Reset();
            Bank.Reset();
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
                Experience = new ContentUnitExperienceDefinition
                {
                    experiencePerLevel = 1,
                    maxLevel = 3,
                    experienceGainMultiplier = 1.0f,
                    levelUpBonus = 0.2f
                },
                CombatBoosts = new[]
                {
                    new ContentCombatBoostDefinition
                    {
                        id = "test:friendly-hit-boost",
                        statName = "MoveSpeed",
                        multiplier = 1.3f,
                        duration = 3.0f
                    }
                },
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
                IsUnit = true,
                CurrentHealth = 60f,
                MaxHealth = 100f,
                CurrentRank = 0,
                MaxRank = 3
            };
            var friendlyTarget = new EntityController
            {
                entityId = "test:friendly-target",
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 10f,
                MaxHealth = 100f
            };

            if (attacker.IsEnemy(friendlyTarget))
            {
                throw new InvalidOperationException("Fixture reports same-player entities as enemies.");
            }

            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(friendlyTarget, 2f, attacker);
            if (attacker.CurrentRank != 0 || attacker.AppliedSpecificValueChanges.Count != 0 ||
                attacker.AppliedStatusEffects.Any(e => e.Effect == StatusEffect.Turbo || e.Effect == StatusEffect.CombatBoost))
            {
                throw new InvalidOperationException("Friendly hits must not trigger attacker combat boosts or veterancy.");
            }

            friendlyTarget.CurrentHealth = 0f;
            friendlyTarget.DestroyHasBeenStarted = true;
            RuntimeUnitMechanicsDirector.OnEntityDealtDamage(friendlyTarget, 10f, attacker);

            if (attacker.CurrentRank != 0 || attacker.AppliedSpecificValueChanges.Count != 0 ||
                attacker.AppliedStatusEffects.Any(e => e.Effect == StatusEffect.Turbo || e.Effect == StatusEffect.CombatBoost))
            {
                throw new InvalidOperationException("Expected no combat boost or veterancy for killing a friendly unit.");
            }

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
    ("on-hit status effects do not affect friendly units", () =>
    {
        RuntimeUnitMechanicsDirector.ClearProfiles();
        RuntimeHooks.OnHitStatusEffectsResolver = entityId => entityId == "test:on-hit-attacker"
            ? new List<ContentOnHitStatusEffectDefinition>
            {
                new() { statusEffect = "Stun", duration = 2f, chance = 1f }
            }
            : null;

        try
        {
            var attacker = new EntityController
            {
                entityId = "test:on-hit-attacker",
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f
            };
            var friendlyTarget = new EntityController
            {
                entityId = "test:on-hit-friendly-target",
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f
            };

            if (attacker.IsEnemy(friendlyTarget))
            {
                throw new InvalidOperationException("Fixture reports same-player entities as enemies.");
            }

            RuntimeHooks.EntityTakeDamagePostfix(friendlyTarget, 1f, attacker);
            if (friendlyTarget.AppliedStatusEffects.Any(effect => effect.Effect == StatusEffect.Stun))
            {
                throw new InvalidOperationException("On-hit status effects must not affect a friendly target.");
            }

            var hostileTarget = new EntityController
            {
                entityId = "test:on-hit-hostile-target",
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 100f
            };
            RuntimeHooks.EntityTakeDamagePostfix(hostileTarget, 1f, attacker);
            if (!hostileTarget.AppliedStatusEffects.Any(effect =>
                    effect.Effect == StatusEffect.Stun &&
                    effect.DurationType == SetStatusEffect.DurationType.Seconds &&
                    effect.Duration == 2f))
            {
                throw new InvalidOperationException("On-hit status effects should still affect a hostile target.");
            }
        }
        finally
        {
            RuntimeHooks.OnHitStatusEffectsResolver = null;
            RuntimeUnitMechanicsDirector.ClearProfiles();
        }
    }),
    ("runtime ai modules census captures registered modules and serializes cleanly", () =>
    {
        AiModuleRegistry prevActive = AiModuleRegistry.Active;
        try
        {
            var isolated = new AiModuleRegistry();
            AiModuleRegistry.Active = isolated;
            AiModulesCensus emptyCensus = RuntimeAiModulesCensus.Current();
            if (emptyCensus == null || emptyCensus.RegisteredModuleCount != 0 || emptyCensus.ModuleRows.Count != 0)
            {
                throw new InvalidOperationException("Initial AI modules census must be empty.");
            }

            string emptyJson = AgentDiagnosticsFormatter.SerializeAiModulesCensus(emptyCensus);
            using (var doc = JsonDocument.Parse(emptyJson))
            {
                if (doc.RootElement.GetProperty("registeredModuleCount").GetInt32() != 0 ||
                    doc.RootElement.GetProperty("moduleRowCount").GetInt32() != 0)
                {
                    throw new InvalidOperationException("Serialized empty AI modules census must report 0 counts.");
                }
            }

            isolated.Register(new ThreatPrioritizationAiModule());
            AiModulesCensus activeCensus = RuntimeAiModulesCensus.Current();
            if (activeCensus.RegisteredModuleCount != 1 || activeCensus.ModuleRows.Count != 1)
            {
                throw new InvalidOperationException("AI modules census must report registered threat-prioritization module.");
            }

            if (activeCensus.ModuleRows[0].ModuleId != "sample:threat-prioritization" ||
                !activeCensus.ModuleRows[0].IsRegistered)
            {
                throw new InvalidOperationException("AI module row did not capture correct module ID or registration state.");
            }

            string activeJson = AgentDiagnosticsFormatter.SerializeAiModulesCensus(activeCensus);
            using (var doc = JsonDocument.Parse(activeJson))
            {
                if (doc.RootElement.GetProperty("registeredModuleCount").GetInt32() != 1 ||
                    doc.RootElement.GetProperty("moduleRowCount").GetInt32() != 1)
                {
                    throw new InvalidOperationException("Serialized active census must reflect 1 registered module.");
                }

                JsonElement rows = doc.RootElement.GetProperty("moduleRows");
                if (rows.GetArrayLength() != 1 ||
                    rows[0].GetProperty("moduleId").GetString() != "sample:threat-prioritization" ||
                    !rows[0].GetProperty("isRegistered").GetBoolean())
                {
                    throw new InvalidOperationException("Serialized moduleRows array does not match registered module properties.");
                }
            }
        }
        finally
        {
            AiModuleRegistry.Active = prevActive;
        }
    }),
    ("runtime economy census captures match telemetry and degrades safely", () =>
    {
        EconomyCensus custom = new EconomyCensus(250, 400, 5, 120.5f, 3);
        if (custom.PlayerCredits != 250 || custom.AiCredits != 400 || custom.AttackWaveNumber != 5 ||
            Math.Abs(custom.ElapsedGameTimeSeconds - 120.5f) > 0.001f || custom.HarvesterCount != 3)
        {
            throw new InvalidOperationException("EconomyCensus constructor did not assign values accurately.");
        }

        string json = AgentDiagnosticsFormatter.SerializeEconomyCensus(custom);
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("playerCredits").GetInt32() != 250 ||
                doc.RootElement.GetProperty("aiCredits").GetInt32() != 400 ||
                doc.RootElement.GetProperty("attackWaveNumber").GetInt32() != 5 ||
                Math.Abs(doc.RootElement.GetProperty("elapsedGameTimeSeconds").GetSingle() - 120.5f) > 0.001f ||
                doc.RootElement.GetProperty("harvesterCount").GetInt32() != 3)
            {
                throw new InvalidOperationException("Serialized EconomyCensus JSON properties do not match expected values.");
            }
        }

        string nullJson = AgentDiagnosticsFormatter.SerializeEconomyCensus(null);
        using (var doc = JsonDocument.Parse(nullJson))
        {
            if (doc.RootElement.GetProperty("playerCredits").GetInt32() != 0 ||
                doc.RootElement.GetProperty("aiCredits").GetInt32() != 0 ||
                doc.RootElement.GetProperty("attackWaveNumber").GetInt32() != 0 ||
                doc.RootElement.GetProperty("elapsedGameTimeSeconds").GetSingle() != 0f ||
                doc.RootElement.GetProperty("harvesterCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("Serialized null EconomyCensus must fall back gracefully to zeroed primitives.");
            }
        }

        EconomyCensus live = RuntimeEconomyCensus.Current();
        if (live == null)
        {
            throw new InvalidOperationException("RuntimeEconomyCensus.Current() must never return null.");
        }
    }),
    ("runtime combat status census serializes loss bounds and tracking details", () =>
    {
        var rows = new List<CombatEntityStatusRow>
        {
            new CombatEntityStatusRow("test:unit-1", true, 10.0f, true, 2),
            new CombatEntityStatusRow("test:unit-2", false, 12.5f, false, 0),
        };
        CombatStatusCensus census = new CombatStatusCensus(1, 3, 1, rows.AsReadOnly());

        if (census.OmittedUnitCount != 1 || census.TotalCombatActiveUnitCount != 3 ||
            census.UnitsWithActiveStatusEffectsCount != 1 || census.CombatActiveUnits.Count != 2)
        {
            throw new InvalidOperationException("CombatStatusCensus failed to retain field values.");
        }

        string json = AgentDiagnosticsFormatter.SerializeCombatStatusCensus(census);
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("omittedUnitCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("totalCombatActiveUnitCount").GetInt32() != 3 ||
                doc.RootElement.GetProperty("unitsWithActiveStatusEffectsCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("trackedUnitRowCount").GetInt32() != 2)
            {
                throw new InvalidOperationException("Serialized CombatStatusCensus counts do not match expected bounds.");
            }

            JsonElement units = doc.RootElement.GetProperty("combatActiveUnits");
            if (units.GetArrayLength() != 2 ||
                units[0].GetProperty("entityKeyId").GetString() != "test:unit-1" ||
                !units[0].GetProperty("isControlledByPlayer").GetBoolean() ||
                units[0].GetProperty("activeStatusEffectsCount").GetInt32() != 2)
            {
                throw new InvalidOperationException("Serialized combat active units array does not match row elements.");
            }
        }

        string nullJson = AgentDiagnosticsFormatter.SerializeCombatStatusCensus(null);
        using (var doc = JsonDocument.Parse(nullJson))
        {
            if (doc.RootElement.GetProperty("omittedUnitCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("totalCombatActiveUnitCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("unitsWithActiveStatusEffectsCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("trackedUnitRowCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("Null CombatStatusCensus must serialize with zero counts.");
            }
        }

        CombatStatusCensus live = RuntimeCombatStatusCensus.Current();
        if (live == null)
        {
            throw new InvalidOperationException("RuntimeCombatStatusCensus.Current() must never return null.");
        }
    }),
    ("runtime unified system health synthesizes subsystems cleanly", () =>
    {
        var dummySnapshot = new LoaderDiagnosticsSnapshot(
            "1.0.9", true, true, 2, 0, 2048L,
            Array.Empty<PackDiagnosticReport>(),
            new List<UnitDiagnosticReport>
            {
                new UnitDiagnosticReport("sample:unit-a", "sample_pack", "prefab", "img", true)
            }.AsReadOnly(),
            Array.Empty<StageDiagnosticReport>(),
            Array.Empty<AiProfileDiagnosticReport>(),
            Array.Empty<UnitFactoryDiagnosticReport>(),
            Array.Empty<StagePoolScopeDiagnosticReport>(),
            Array.Empty<AiProfileStageRangeDiagnosticReport>(),
            new List<string> { "log1", "log2" }.AsReadOnly());

        var aiCensus = new AiModulesCensus(2, new List<AiModuleDiagnosticRow>
        {
            new AiModuleDiagnosticRow("sample:threat-prioritization", 1, true),
            new AiModuleDiagnosticRow("sample:skirmish-kiting", 1, true),
        }.AsReadOnly());

        var economyCensus = new EconomyCensus(500, 750, 4, 300f, 6);
        var combatCensus = new CombatStatusCensus(0, 5, 2, Array.Empty<CombatEntityStatusRow>());

        string json = AgentDiagnosticsFormatter.SerializeSystemHealth(
            dummySnapshot,
            aiCensus,
            economyCensus,
            combatCensus);

        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.GetProperty("isNativeRegistryReady").GetBoolean() ||
                doc.RootElement.GetProperty("activePackCount").GetInt32() != 2 ||
                doc.RootElement.GetProperty("failedPackCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("registeredUnitCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("registeredAiModuleCount").GetInt32() != 2 ||
                doc.RootElement.GetProperty("playerCredits").GetInt32() != 500 ||
                doc.RootElement.GetProperty("aiCredits").GetInt32() != 750 ||
                doc.RootElement.GetProperty("attackWaveNumber").GetInt32() != 4 ||
                Math.Abs(doc.RootElement.GetProperty("elapsedGameTimeSeconds").GetSingle() - 300f) > 0.001f ||
                doc.RootElement.GetProperty("omittedCombatUnitCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("totalCombatActiveUnitCount").GetInt32() != 5 ||
                doc.RootElement.GetProperty("unitsWithActiveStatusEffectsCount").GetInt32() != 2 ||
                doc.RootElement.GetProperty("recentLogCount").GetInt32() != 2)
            {
                throw new InvalidOperationException("Unified system health serialization did not preserve subsystem values.");
            }
        }

        string nullHealthJson = AgentDiagnosticsFormatter.SerializeSystemHealth(null, null, null, null);
        using (var doc = JsonDocument.Parse(nullHealthJson))
        {
            if (doc.RootElement.GetProperty("isNativeRegistryReady").GetBoolean() ||
                doc.RootElement.GetProperty("activePackCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("registeredUnitCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("registeredAiModuleCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("playerCredits").GetInt32() != 0 ||
                doc.RootElement.GetProperty("totalCombatActiveUnitCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("Null system health must serialize safe default primitive values.");
            }
        }
    }),
    ("runtime card change applier availability check handles unreflected state safely", () =>
    {
        if (RuntimeCardChangeApplier.IsAvailable(null))
        {
            throw new InvalidOperationException("IsAvailable must return false when native assembly/reflection is unavailable.");
        }
    }),
    ("threat-prioritization census serializes telemetry and live registry", () =>
    {
        ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour.ResetThreatPrioritizationRegistryForTests();
        string json = AgentDiagnosticsFormatter.SerializeThreatPrioritizationCensus(2, 5, 3, 1, 4);
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("activeBehaviourCount").GetInt32() != 2 ||
                doc.RootElement.GetProperty("processedAiUnitCount").GetInt32() != 5 ||
                doc.RootElement.GetProperty("processedAlliedUnitCount").GetInt32() != 3 ||
                doc.RootElement.GetProperty("lastRetargetedCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("totalRetargetCommandCount").GetInt32() != 4)
            {
                throw new InvalidOperationException("Threat-prioritization census did not preserve telemetry values.");
            }
        }

        string nullSafeJson = AgentDiagnosticsFormatter.SerializeThreatPrioritizationCensus(-1, -2, -3, -4, -5);
        using (var doc = JsonDocument.Parse(nullSafeJson))
        {
            if (doc.RootElement.GetProperty("activeBehaviourCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("processedAiUnitCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("Threat-prioritization census must clamp negative telemetry to zero.");
            }
        }

        if (ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour.ActiveBehaviourCount < 0)
        {
            throw new InvalidOperationException("Threat-prioritization registry count must be non-negative.");
        }
    }),
    ("diagnostic catalog lists available agent getters", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeDiagnosticCatalog();
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("kind").GetString() != "diagnosticCatalog" ||
                doc.RootElement.GetProperty("diagnosticCount").GetInt32() <= 0)
            {
                throw new InvalidOperationException("Diagnostic catalog must report a non-empty count.");
            }

            bool found = false;
            foreach (JsonElement item in doc.RootElement.GetProperty("diagnostics").EnumerateArray())
            {
                if (item.GetProperty("name").GetString() == "GetThreatPrioritizationCensusJson")
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                throw new InvalidOperationException("Diagnostic catalog must include threat-prioritization census getter.");
            }
        }
    }),
    ("production census serializes factory queue telemetry with omission bounds first", () =>
    {
        var rows = new List<ProductionFactoryDiagnosticRow>
        {
            new ProductionFactoryDiagnosticRow("factory:player-heavy", true, 2, true, 450f, 500f, 0.9f),
            new ProductionFactoryDiagnosticRow("factory:ai-barracks", false, 0, false, 300f, 300f, 1.0f)
        };
        var census = new ProductionCensus(0, 2, 1, 2, 1, 1, rows);
        string json = AgentDiagnosticsFormatter.SerializeProductionCensus(census);

        int omittedIndex = json.IndexOf("omittedFactoryCount");
        int rowsIndex = json.IndexOf("factoryRows");
        if (omittedIndex < 0 || rowsIndex < 0 || omittedIndex > rowsIndex)
        {
            throw new InvalidOperationException("Omission counter must be serialized before detail collection in production census.");
        }

        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("omittedFactoryCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("totalFactoryCount").GetInt32() != 2 ||
                doc.RootElement.GetProperty("activeProducingCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("totalQueuedUnitsCount").GetInt32() != 2 ||
                doc.RootElement.GetProperty("playerFactoryCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("aiFactoryCount").GetInt32() != 1 ||
                doc.RootElement.GetProperty("factoryRowCount").GetInt32() != 2)
            {
                throw new InvalidOperationException("Production census did not preserve factory telemetry metrics.");
            }

            JsonElement first = doc.RootElement.GetProperty("factoryRows")[0];
            if (first.GetProperty("entityKeyId").GetString() != "factory:player-heavy" ||
                !first.GetProperty("isControlledByPlayer").GetBoolean() ||
                first.GetProperty("inProductionCount").GetInt32() != 2 ||
                !first.GetProperty("isProducing").GetBoolean())
            {
                throw new InvalidOperationException("Production census did not serialize factory row correctly.");
            }
        }

        string nullJson = AgentDiagnosticsFormatter.SerializeProductionCensus(null);
        using (var doc = JsonDocument.Parse(nullJson))
        {
            if (doc.RootElement.GetProperty("omittedFactoryCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("totalFactoryCount").GetInt32() != 0 ||
                doc.RootElement.GetProperty("factoryRowCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("Null production census must serialize default values.");
            }
        }
    }),
    ("base integrity census serializes HQ and AI structure telemetry with omission bounds first", () =>
    {
        var rows = new List<BaseStructureDiagnosticRow>
        {
            new BaseStructureDiagnosticRow("building:player-hq", true, 900f, 1000f, 0.9f, true),
            new BaseStructureDiagnosticRow("building:ai-defense", false, 200f, 400f, 0.5f, false)
        };
        var census = new BaseIntegrityCensus(0, true, 900f, 1000f, 0.9f, 1, 0.5f, rows);
        string json = AgentDiagnosticsFormatter.SerializeBaseIntegrityCensus(census);

        int omittedIndex = json.IndexOf("omittedStructureCount");
        int rowsIndex = json.IndexOf("structureRows");
        if (omittedIndex < 0 || rowsIndex < 0 || omittedIndex > rowsIndex)
        {
            throw new InvalidOperationException("Omission counter must be serialized before detail collection in base integrity census.");
        }

        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.GetProperty("omittedStructureCount").GetInt32() != 0 ||
                !doc.RootElement.GetProperty("hasPlayerHq").GetBoolean() ||
                Math.Abs(doc.RootElement.GetProperty("playerHqHealthRatio").GetSingle() - 0.9f) > 0.001f ||
                doc.RootElement.GetProperty("livingAiBuildingCount").GetInt32() != 1 ||
                Math.Abs(doc.RootElement.GetProperty("averageAiStructureHealthRatio").GetSingle() - 0.5f) > 0.001f ||
                doc.RootElement.GetProperty("structureRowCount").GetInt32() != 2)
            {
                throw new InvalidOperationException("Base integrity census did not preserve structure metrics.");
            }
        }

        string nullJson = AgentDiagnosticsFormatter.SerializeBaseIntegrityCensus(null);
        using (var doc = JsonDocument.Parse(nullJson))
        {
            if (doc.RootElement.GetProperty("hasPlayerHq").GetBoolean() ||
                doc.RootElement.GetProperty("livingAiBuildingCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("Null base integrity census must serialize default values.");
            }
        }
    }),
    ("runtime production census queries living factories from existing controllers", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var pFactory = new EntityController
            {
                entityId = "building:player-factory",
                IsFactory = true,
                IsBuilding = true,
                IsControlledByPlayer = true,
                CurrentHealth = 400f,
                MaxHealth = 500f,
                InProductionCount = 1
            };
            var aiBuilding = new EntityController
            {
                entityId = "building:ai-turret-factory",
                IsFactory = true,
                IsBuilding = true,
                IsControlledByPlayer = false,
                CurrentHealth = 250f,
                MaxHealth = 250f,
                InProductionCount = 0
            };
            ExistingControllers.Instance.PlayerBuildingsKnownToAi().Add(pFactory);
            ExistingControllers.Instance.AiBuildings().Add(aiBuilding);

            ProductionCensus census = RuntimeProductionCensus.Current();
            if (census.TotalFactoryCount != 2 ||
                census.PlayerFactoryCount != 1 ||
                census.AiFactoryCount != 1 ||
                census.ActiveProducingCount != 1 ||
                census.TotalQueuedUnitsCount != 1 ||
                census.FactoryRows.Count != 2)
            {
                throw new InvalidOperationException($"RuntimeProductionCensus did not collect expected factory stats. Total={census.TotalFactoryCount}, Active={census.ActiveProducingCount}");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime base integrity census collects HQ and living AI buildings", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            var hq = new EntityController
            {
                entityId = "building:engineering-hq",
                IsBuilding = true,
                IsControlledByPlayer = true,
                CurrentHealth = 800f,
                MaxHealth = 1000f
            };
            var aiTurret = new EntityController
            {
                entityId = "building:ai-turret",
                IsBuilding = true,
                IsControlledByPlayer = false,
                CurrentHealth = 100f,
                MaxHealth = 200f
            };
            ExistingControllers.Instance.EngineeringHqEntity = hq;
            ExistingControllers.Instance.AiBuildings().Add(aiTurret);

            BaseIntegrityCensus census = RuntimeBaseIntegrityCensus.Current();
            if (!census.HasPlayerHq ||
                Math.Abs(census.PlayerHqHealthRatio - 0.8f) > 0.001f ||
                census.LivingAiBuildingCount != 1 ||
                Math.Abs(census.AverageAiStructureHealthRatio - 0.5f) > 0.001f ||
                census.StructureRows.Count != 2)
            {
                throw new InvalidOperationException($"RuntimeBaseIntegrityCensus did not collect expected HQ/AI structure metrics. HasHQ={census.HasPlayerHq}, AiBuildings={census.LivingAiBuildingCount}");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime gameplay production director orders production on player factory and validates constraints", () =>
    {
        ExistingControllers.Instance.Reset();
        try
        {
            ProductionOrderResult emptyRes = RuntimeGameplayProductionDirector.TryOrderProduction("");
            if (emptyRes.Success)
            {
                throw new InvalidOperationException("Production order on empty factory ID must fail.");
            }

            ProductionOrderResult notFoundRes = RuntimeGameplayProductionDirector.TryOrderProduction("building:ghost-factory");
            if (notFoundRes.Success)
            {
                throw new InvalidOperationException("Production order on nonexistent factory must fail.");
            }

            var aiFactory = new EntityController
            {
                entityId = "building:ai-factory-1",
                IsFactory = true,
                IsBuilding = true,
                IsControlledByPlayer = false,
                InProductionCount = 0
            };
            ExistingControllers.Instance.PlayerUnits().Add(aiFactory);

            ProductionOrderResult aiRes = RuntimeGameplayProductionDirector.TryOrderProduction("building:ai-factory-1");
            if (aiRes.Success)
            {
                throw new InvalidOperationException("Production order on non-player factory must fail.");
            }

            var playerFactory = new EntityController
            {
                entityId = "building:player-factory-1",
                IsFactory = true,
                IsBuilding = true,
                IsControlledByPlayer = true,
                InProductionCount = 3
            };
            ExistingControllers.Instance.PlayerBuildingsKnownToAi().Add(playerFactory);

            ProductionOrderResult successRes = RuntimeGameplayProductionDirector.TryOrderProduction("building:player-factory-1", instant: false, forFree: true);
            if (!successRes.Success || successRes.FactoryKeyId != "building:player-factory-1" || successRes.InProductionCount != 3)
            {
                throw new InvalidOperationException("Production order on player factory must succeed.");
            }

            string resultJson = AgentDiagnosticsFormatter.SerializeProductionOrderResult(successRes);
            using (var doc = JsonDocument.Parse(resultJson))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("factoryKeyId").GetString() != "building:player-factory-1" ||
                    doc.RootElement.GetProperty("inProductionCount").GetInt32() != 3)
                {
                    throw new InvalidOperationException("ProductionOrderResult serialization mismatch.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("diagnostic catalog contains new production and base integrity endpoints", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeDiagnosticCatalog();
        using (var doc = JsonDocument.Parse(json))
        {
            var names = new HashSet<string>();
            foreach (JsonElement item in doc.RootElement.GetProperty("diagnostics").EnumerateArray())
            {
                names.Add(item.GetProperty("name").GetString()!);
            }

            if (!names.Contains("GetProductionCensusJson") ||
                !names.Contains("GetBaseIntegrityCensusJson") ||
                !names.Contains("OrderFactoryProduceJson"))
            {
                throw new InvalidOperationException("Diagnostic catalog must include GetProductionCensusJson, GetBaseIntegrityCensusJson, and OrderFactoryProduceJson.");
            }
        }
    }),
    ("run progression census collects scene, stage, ascension/heat, relics and rewards with loss counters first", () =>
    {
        try
        {
            UnityEngine.SceneManagement.SceneManager.ActiveSceneName = "Desert_Combat_01";
            Game.CurrentLevel = 3;
            Game.IsLastLevel = false;
            Game.IsFirstLevelOrGameHasBeenResetted = false;
            Game.CardsInDeck = new List<string> { "card_1", "card_2", "card_3" };

            MetaGame.Instance.CurrentAscensionLevel = 2;
            MetaGame.Instance.CurrentHeat = 5;
            MetaGame.Instance.CurrentAscensionAndHeatLevelString = "Ascension 2 Heat 5";

            var testRelics = new List<string>();
            for (int i = 1; i <= 40; i++)
            {
                testRelics.Add("relic_item_" + i);
            }
            Game.Relics = testRelics;

            var testRewards = new List<OpenReward>();
            for (int i = 1; i <= 35; i++)
            {
                testRewards.Add(new OpenReward { blueprintPoolId = "pool_reward_" + i });
            }
            Game.OpenCardRewards = testRewards;

            RunProgressionCensus census = RuntimeRunProgressionCensus.Current();

            if (census.ActiveSceneName != "Desert_Combat_01" ||
                census.CurrentLevel != 3 ||
                census.IsLastLevel ||
                census.IsFirstLevel ||
                census.AscensionLevel != 2 ||
                census.HeatLevel != 5 ||
                census.AscensionAndHeatString != "Ascension 2 Heat 5" ||
                census.CardsInDeckCount != 3)
            {
                throw new InvalidOperationException("RunProgressionCensus failed to capture core stage/meta progression parameters.");
            }

            if (census.TotalRelicCount != 40 || census.ActiveRelicIds.Count != 32 || census.OmittedRelicCount != 8)
            {
                throw new InvalidOperationException("RunProgressionCensus must cap relics at 32 and track 8 omitted relics.");
            }

            if (census.TotalBlueprintRewardCount != 35 || census.OfferedBlueprintPoolIds.Count != 32 || census.OmittedBlueprintCount != 3)
            {
                throw new InvalidOperationException("RunProgressionCensus must cap blueprint pools at 32 and track 3 omitted blueprints.");
            }

            string json = AgentDiagnosticsFormatter.SerializeRunProgressionCensus(census);
            using (var doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                if (root.GetProperty("kind").GetString() != "runProgressionCensus" ||
                    root.GetProperty("omittedRelicCount").GetInt32() != 8 ||
                    root.GetProperty("omittedBlueprintCount").GetInt32() != 3 ||
                    root.GetProperty("activeSceneName").GetString() != "Desert_Combat_01" ||
                    root.GetProperty("currentLevel").GetInt32() != 3 ||
                    root.GetProperty("ascensionLevel").GetInt32() != 2 ||
                    root.GetProperty("heatLevel").GetInt32() != 5 ||
                    root.GetProperty("relicCount").GetInt32() != 32 ||
                    root.GetProperty("offeredBlueprintRewardCount").GetInt32() != 32)
                {
                    throw new InvalidOperationException("SerializeRunProgressionCensus JSON property mismatch.");
                }

                int omittedRelicIdx = json.IndexOf("\"omittedRelicCount\"");
                int activeRelicsIdx = json.IndexOf("\"activeRelicIds\"");
                int omittedBpIdx = json.IndexOf("\"omittedBlueprintCount\"");
                int offeredBpIdx = json.IndexOf("\"offeredBlueprintPoolIds\"");

                if (omittedRelicIdx < 0 || activeRelicsIdx < 0 || omittedRelicIdx >= activeRelicsIdx ||
                    omittedBpIdx < 0 || offeredBpIdx < 0 || omittedBpIdx >= offeredBpIdx)
                {
                    throw new InvalidOperationException("Loss/omission counters must strictly precede collections in JSON output.");
                }
            }
        }
        finally
        {
            Game.Relics.Clear();
            Game.OpenCardRewards.Clear();
            Game.CardsInDeck.Clear();
            UnityEngine.SceneManagement.SceneManager.ActiveSceneName = string.Empty;
        }
    }),
    ("gameplay command director grants credits with bounded limits and updates balance", () =>
    {
        try
        {
            Bank.Reset();

            CreditGrantResult grantSuccess = RuntimeGameplayCommandDirector.TryGrantCredits(750, "Player");
            if (!grantSuccess.Success || grantSuccess.AmountGranted != 750 || grantSuccess.AccountName != "Player")
            {
                throw new InvalidOperationException("Valid credit grant of 750 credits must succeed.");
            }

            CreditGrantResult grantZero = RuntimeGameplayCommandDirector.TryGrantCredits(0, "Player");
            if (grantZero.Success || grantZero.AmountGranted != 0)
            {
                throw new InvalidOperationException("Credit grant of 0 credits must fail.");
            }

            CreditGrantResult grantNegative = RuntimeGameplayCommandDirector.TryGrantCredits(-100, "Player");
            if (grantNegative.Success || grantNegative.AmountGranted != 0)
            {
                throw new InvalidOperationException("Negative credit grant must fail.");
            }

            CreditGrantResult grantExcessive = RuntimeGameplayCommandDirector.TryGrantCredits(1000000, "Player");
            if (grantExcessive.Success || grantExcessive.AmountGranted != 0)
            {
                throw new InvalidOperationException("Credit grant exceeding 500,000 cap must fail.");
            }

            string json = AgentDiagnosticsFormatter.SerializeCreditGrantResult(grantSuccess);
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("accountName").GetString() != "Player" ||
                    doc.RootElement.GetProperty("amountGranted").GetInt32() != 750)
                {
                    throw new InvalidOperationException("SerializeCreditGrantResult JSON property mismatch.");
                }
            }
        }
        finally
        {
            Bank.Reset();
        }
    }),
    ("gameplay command director dispatches unit move and attack commands and validates targets", () =>
    {
        try
        {
            ExistingControllers.Instance.Reset();

            var playerUnit = new EntityController
            {
                name = "player_strike_tank",
                EntityId = "unit:strike-tank-01",
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 250f,
                MaxHealth = 250f,
                Position = new Vector3(10f, 0f, 10f)
            };
            ExistingControllers.Instance.PlayerUnits().Add(playerUnit);

            var enemyUnit = new EntityController
            {
                name = "enemy_raider",
                EntityId = "unit:enemy-raider-01",
                IsControlledByAi = true,
                IsUnit = true,
                CurrentHealth = 120f,
                MaxHealth = 120f,
                Position = new Vector3(60f, 0f, 80f)
            };
            ExistingControllers.Instance.AiUnits().Add(enemyUnit);

            // Test Unit Move Command
            UnitCommandResult moveRes = RuntimeGameplayCommandDirector.TryOrderUnitMove("unit:strike-tank-01", 35f, 45f);
            if (!moveRes.Success || moveRes.CommandType != "Move" || moveRes.TargetX != 35f || moveRes.TargetZ != 45f)
            {
                throw new InvalidOperationException("TryOrderUnitMove must succeed for existing player unit.");
            }

            if (!playerUnit.LastExecutedCommand.HasValue ||
                playerUnit.LastExecutedCommand.Value.command != EntityCommand.Command.Move ||
                playerUnit.LastExecutedCommand.Value.destination.x != 35f ||
                playerUnit.LastExecutedCommand.Value.destination.z != 45f)
            {
                throw new InvalidOperationException("Player unit must have received EntityCommand.Move with destination (35, 45).");
            }

            // Test Unit Move on non-existent unit
            UnitCommandResult moveMissing = RuntimeGameplayCommandDirector.TryOrderUnitMove("unit:non-existent", 0f, 0f);
            if (moveMissing.Success)
            {
                throw new InvalidOperationException("TryOrderUnitMove must fail for missing entity.");
            }

            // Test Unit Attack Command
            UnitCommandResult attackRes = RuntimeGameplayCommandDirector.TryOrderUnitAttack("unit:strike-tank-01", "unit:enemy-raider-01");
            if (!attackRes.Success || attackRes.CommandType != "AttackMoveOnTarget" ||
                attackRes.TargetX != 60f || attackRes.TargetZ != 80f)
            {
                throw new InvalidOperationException("TryOrderUnitAttack must succeed for existing attacker and target.");
            }

            if (!playerUnit.LastExecutedCommand.HasValue ||
                playerUnit.LastExecutedCommand.Value.command != EntityCommand.Command.AttackMoveOnTarget ||
                playerUnit.LastExecutedCommand.Value.target != enemyUnit)
            {
                throw new InvalidOperationException("Player unit must have received EntityCommand.AttackMoveOnTarget with target enemyUnit.");
            }

            // Test Unit Attack with missing target
            UnitCommandResult attackMissingTarget = RuntimeGameplayCommandDirector.TryOrderUnitAttack("unit:strike-tank-01", "unit:missing-target");
            if (attackMissingTarget.Success)
            {
                throw new InvalidOperationException("TryOrderUnitAttack must fail for missing target.");
            }

            // Verify serialization
            string moveJson = AgentDiagnosticsFormatter.SerializeUnitCommandResult(moveRes);
            using (var doc = JsonDocument.Parse(moveJson))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("commandType").GetString() != "Move" ||
                    doc.RootElement.GetProperty("targetX").GetSingle() != 35f)
                {
                    throw new InvalidOperationException("SerializeUnitCommandResult JSON mismatch.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("diagnostic catalog contains run progression, credit grant, card change, and unit command endpoints", () =>
    {
        string json = AgentDiagnosticsFormatter.SerializeDiagnosticCatalog();
        using (var doc = JsonDocument.Parse(json))
        {
            var names = new HashSet<string>();
            foreach (JsonElement item in doc.RootElement.GetProperty("diagnostics").EnumerateArray())
            {
                names.Add(item.GetProperty("name").GetString()!);
            }

            string[] required =
            {
                "GetRunProgressionCensusJson",
                "GrantPlayerCreditsJson",
                "ApplyInGameCardChangeJson",
                "OrderUnitMoveJson",
                "OrderUnitAttackJson",
                "OrderUnitActiveSkillJson",
                "AdjustEntityVitalityJson",
                "ApplyStatusEffectJson",
                "ClearStatusEffectJson",
                "GetGridMetricsJson",
                "EvaluateGridPositionJson"
            };

            foreach (string req in required)
            {
                if (!names.Contains(req))
                {
                    throw new InvalidOperationException("Diagnostic catalog must include " + req + ".");
                }
            }
        }
    }),
    ("gameplay command director dispatches active skill commands across Self, Target, and Position modes", () =>
    {
        try
        {
            ExistingControllers.Instance.Reset();

            var caster = new EntityController
            {
                name = "test_arc_caster",
                EntityId = "unit:arc-caster-01",
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(15f, 0f, 20f)
            };
            ExistingControllers.Instance.PlayerUnits().Add(caster);

            var enemyTarget = new EntityController
            {
                name = "test_enemy_mech",
                EntityId = "unit:enemy-mech-01",
                IsControlledByAi = true,
                IsUnit = true,
                Position = new Vector3(40f, 0f, 60f)
            };
            ExistingControllers.Instance.AiUnits().Add(enemyTarget);

            // Self-targeted active skill
            ActiveSkillOrderResult selfRes = RuntimeGameplayCommandDirector.TryOrderActiveSkill("unit:arc-caster-01");
            if (!selfRes.Success || selfRes.SkillTargetMode != "Self" || selfRes.TargetX != 15f || selfRes.TargetZ != 20f)
            {
                throw new InvalidOperationException("Self-targeted skill command must succeed.");
            }
            if (!caster.LastExecutedCommand.HasValue ||
                caster.LastExecutedCommand.Value.command != EntityCommand.Command.ActiveSkillWithoutTarget)
            {
                throw new InvalidOperationException("Caster must receive ActiveSkillWithoutTarget command.");
            }

            // Target-entity active skill
            ActiveSkillOrderResult targetRes = RuntimeGameplayCommandDirector.TryOrderActiveSkill("unit:arc-caster-01", targetEntityKeyId: "unit:enemy-mech-01");
            if (!targetRes.Success || targetRes.SkillTargetMode != "Target" || targetRes.TargetX != 40f || targetRes.TargetZ != 60f)
            {
                throw new InvalidOperationException("Entity-targeted skill command must succeed.");
            }
            if (!caster.LastExecutedCommand.HasValue ||
                caster.LastExecutedCommand.Value.command != EntityCommand.Command.ActiveSkillOnTarget ||
                caster.LastExecutedCommand.Value.target != enemyTarget)
            {
                throw new InvalidOperationException("Caster must receive ActiveSkillOnTarget command with target.");
            }

            // Ground position active skill
            ActiveSkillOrderResult posRes = RuntimeGameplayCommandDirector.TryOrderActiveSkill("unit:arc-caster-01", targetX: 75f, targetZ: 85f);
            if (!posRes.Success || posRes.SkillTargetMode != "Position" || posRes.TargetX != 75f || posRes.TargetZ != 85f)
            {
                throw new InvalidOperationException("Position-targeted skill command must succeed.");
            }
            if (!caster.LastExecutedCommand.HasValue ||
                caster.LastExecutedCommand.Value.command != EntityCommand.Command.ActiveSkillAtPosition ||
                caster.LastExecutedCommand.Value.destination.x != 75f ||
                caster.LastExecutedCommand.Value.destination.z != 85f)
            {
                throw new InvalidOperationException("Caster must receive ActiveSkillAtPosition command with coords.");
            }

            // Failure handling
            ActiveSkillOrderResult missingCaster = RuntimeGameplayCommandDirector.TryOrderActiveSkill("unit:missing-caster");
            if (missingCaster.Success)
            {
                throw new InvalidOperationException("Skill command must fail when caster is missing.");
            }

            ActiveSkillOrderResult missingTarget = RuntimeGameplayCommandDirector.TryOrderActiveSkill("unit:arc-caster-01", targetEntityKeyId: "unit:missing-target");
            if (missingTarget.Success)
            {
                throw new InvalidOperationException("Skill command must fail when target entity is missing.");
            }

            // Serialization
            string json = AgentDiagnosticsFormatter.SerializeActiveSkillOrderResult(posRes);
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("skillTargetMode").GetString() != "Position" ||
                    doc.RootElement.GetProperty("targetX").GetSingle() != 75f)
                {
                    throw new InvalidOperationException("SerializeActiveSkillOrderResult JSON mismatch.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("gameplay command director adjusts vitality: Heal, RepairArmor, ChargeShield, Damage", () =>
    {
        try
        {
            ExistingControllers.Instance.Reset();

            var unit = new EntityController
            {
                name = "test_vitality_unit",
                EntityId = "unit:vitality-01",
                IsControlledByPlayer = true,
                IsUnit = true,
                CurrentHealth = 100f,
                MaxHealth = 200f,
                CurrentArmor = 50f,
                MaxArmor = 100f,
                CurrentShield = 20f,
                MaxShield = 100f
            };
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            // Heal
            VitalityAdjustmentResult healRes = RuntimeGameplayCommandDirector.TryAdjustEntityVitality("unit:vitality-01", "Heal", 40f);
            if (!healRes.Success || healRes.NewHealth != 140f)
            {
                throw new InvalidOperationException("Heal adjustment must succeed and increase health to 140.");
            }

            // RepairArmor
            VitalityAdjustmentResult repairRes = RuntimeGameplayCommandDirector.TryAdjustEntityVitality("unit:vitality-01", "RepairArmor", 30f);
            if (!repairRes.Success || repairRes.NewArmor != 80f)
            {
                throw new InvalidOperationException("RepairArmor adjustment must succeed and increase armor to 80.");
            }

            // ChargeShield
            VitalityAdjustmentResult shieldRes = RuntimeGameplayCommandDirector.TryAdjustEntityVitality("unit:vitality-01", "ChargeShield", 50f);
            if (!shieldRes.Success || shieldRes.NewShield != 70f)
            {
                throw new InvalidOperationException("ChargeShield adjustment must succeed and increase shield to 70.");
            }

            // Damage with Armor Absorption (60 damage: armor drops from 80 to 20, health untouched at 140)
            VitalityAdjustmentResult damageRes = RuntimeGameplayCommandDirector.TryAdjustEntityVitality("unit:vitality-01", "Damage", 60f, ignoreArmor: false);
            if (!damageRes.Success || damageRes.NewArmor != 20f || damageRes.NewHealth != 140f)
            {
                throw new InvalidOperationException("Damage adjustment must reduce armor first before health.");
            }

            // Damage Bypassing Armor (50 damage: armor remains 20, health drops from 140 to 90)
            VitalityAdjustmentResult directDmgRes = RuntimeGameplayCommandDirector.TryAdjustEntityVitality("unit:vitality-01", "Damage", 50f, ignoreArmor: true);
            if (!directDmgRes.Success || directDmgRes.NewHealth != 90f || directDmgRes.NewArmor != 20f)
            {
                throw new InvalidOperationException("Armor-bypassing damage must directly reduce health.");
            }

            // Negative amount validation
            VitalityAdjustmentResult negativeRes = RuntimeGameplayCommandDirector.TryAdjustEntityVitality("unit:vitality-01", "Heal", -20f);
            if (negativeRes.Success)
            {
                throw new InvalidOperationException("Negative vitality adjustment must fail.");
            }

            // Invalid type validation
            VitalityAdjustmentResult invalidTypeRes = RuntimeGameplayCommandDirector.TryAdjustEntityVitality("unit:vitality-01", "Explode", 50f);
            if (invalidTypeRes.Success)
            {
                throw new InvalidOperationException("Unsupported adjustment type must fail.");
            }

            // Serialization
            string json = AgentDiagnosticsFormatter.SerializeVitalityAdjustmentResult(healRes);
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("adjustmentType").GetString() != "Heal" ||
                    doc.RootElement.GetProperty("amountApplied").GetSingle() != 40f ||
                    doc.RootElement.GetProperty("newHealth").GetSingle() != 140f)
                {
                    throw new InvalidOperationException("SerializeVitalityAdjustmentResult JSON mismatch.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("gameplay command director applies and clears status effects on units", () =>
    {
        try
        {
            ExistingControllers.Instance.Reset();

            var unit = new EntityController
            {
                name = "test_status_unit",
                EntityId = "unit:status-01",
                IsControlledByPlayer = true,
                IsUnit = true
            };
            ExistingControllers.Instance.PlayerUnits().Add(unit);

            // Apply OnFire
            StatusEffectOrderResult fireRes = RuntimeGameplayCommandDirector.TryApplyStatusEffect("unit:status-01", "OnFire", 15f);
            if (!fireRes.Success || !fireRes.IsApplied || fireRes.DurationSeconds != 15f || !unit.HasActiveStatusEffect(StatusEffect.OnFire))
            {
                throw new InvalidOperationException("ApplyStatusEffect for OnFire must succeed.");
            }

            // Apply Stun
            StatusEffectOrderResult stunRes = RuntimeGameplayCommandDirector.TryApplyStatusEffect("unit:status-01", "Stun", 5f);
            if (!stunRes.Success || !unit.HasActiveStatusEffect(StatusEffect.Stun))
            {
                throw new InvalidOperationException("ApplyStatusEffect for Stun must succeed.");
            }

            // Clear OnFire
            StatusEffectOrderResult clearFireRes = RuntimeGameplayCommandDirector.TryClearStatusEffect("unit:status-01", "OnFire");
            if (!clearFireRes.Success || clearFireRes.IsApplied || unit.HasActiveStatusEffect(StatusEffect.OnFire))
            {
                throw new InvalidOperationException("ClearStatusEffect for OnFire must succeed.");
            }
            if (!unit.HasActiveStatusEffect(StatusEffect.Stun))
            {
                throw new InvalidOperationException("Stun effect must remain active after clearing OnFire.");
            }

            // Unknown status effect name
            StatusEffectOrderResult unknownRes = RuntimeGameplayCommandDirector.TryApplyStatusEffect("unit:status-01", "NonExistentEffect");
            if (unknownRes.Success)
            {
                throw new InvalidOperationException("ApplyStatusEffect must fail for unknown effect name.");
            }

            // Missing entity
            StatusEffectOrderResult missingRes = RuntimeGameplayCommandDirector.TryApplyStatusEffect("unit:missing", "OnFire");
            if (missingRes.Success)
            {
                throw new InvalidOperationException("ApplyStatusEffect must fail for missing entity.");
            }

            // Serialization
            string json = AgentDiagnosticsFormatter.SerializeStatusEffectOrderResult(fireRes);
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("statusEffectName").GetString() != "OnFire" ||
                    doc.RootElement.GetProperty("durationSeconds").GetSingle() != 15f ||
                    !doc.RootElement.GetProperty("isApplied").GetBoolean())
                {
                    throw new InvalidOperationException("SerializeStatusEffectOrderResult JSON mismatch.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }
    }),
    ("runtime grid census extracts map bounds and evaluates coordinates against world grid", () =>
    {
        GridMetricsCensus metrics = RuntimeGridCensus.Current();
        if (!metrics.IsGridReady || metrics.WorldWidth != 200f || metrics.WorldHeight != 200f ||
            metrics.HorizontalCellCount != 100 || metrics.VerticalCellCount != 100 || metrics.CellSize != 2f ||
            metrics.PlayerStartX != 10 || metrics.PlayerStartY != 10 ||
            metrics.AiStartX != 90 || metrics.AiStartY != 90)
        {
            throw new InvalidOperationException("RuntimeGridCensus.Current must extract accurate world dimensions and start cells.");
        }

        // Evaluate In-Bounds Position
        GridCellEvaluationResult inBounds = RuntimeGridCensus.EvaluatePosition(50f, 60f);
        if (!inBounds.IsWithinWorld || inBounds.ClampedX != 50f || inBounds.ClampedZ != 60f ||
            inBounds.CellX != 25 || inBounds.CellY != 30)
        {
            throw new InvalidOperationException("EvaluatePosition for in-bounds coordinates (50, 60) must succeed with cell (25, 30).");
        }

        // Evaluate Out-of-Bounds Position
        GridCellEvaluationResult outBounds = RuntimeGridCensus.EvaluatePosition(250f, -10f);
        if (outBounds.IsWithinWorld || outBounds.ClampedX != 200f || outBounds.ClampedZ != 0f)
        {
            throw new InvalidOperationException("EvaluatePosition for out-of-bounds coordinates must clamp to grid edge.");
        }

        // Serialization
        string metricsJson = AgentDiagnosticsFormatter.SerializeGridMetricsCensus(metrics);
        using (var doc = JsonDocument.Parse(metricsJson))
        {
            if (!doc.RootElement.GetProperty("isGridReady").GetBoolean() ||
                doc.RootElement.GetProperty("worldWidth").GetSingle() != 200f ||
                doc.RootElement.GetProperty("playerStartX").GetInt32() != 10)
            {
                throw new InvalidOperationException("SerializeGridMetricsCensus JSON mismatch.");
            }
        }

        string evalJson = AgentDiagnosticsFormatter.SerializeGridCellEvaluationResult(inBounds);
        using (var doc = JsonDocument.Parse(evalJson))
        {
            if (!doc.RootElement.GetProperty("isWithinWorld").GetBoolean() ||
                doc.RootElement.GetProperty("cellX").GetInt32() != 25 ||
                doc.RootElement.GetProperty("cellY").GetInt32() != 30)
            {
                throw new InvalidOperationException("SerializeGridCellEvaluationResult JSON mismatch.");
            }
        }
    }),
    ("runtime selection census and tactical command director manage unit selection and stance orders", () =>
    {
        try
        {
            ExistingControllers.Instance.Reset();
            PlayerUnitController.Instance.DeselectAllUnits();

            var unit1 = new EntityController { EntityId = "unit_alpha", tag = "Player", CurrentHealth = 100f, MaxHealth = 100f };
            unit1.gameObject.name = "unit_alpha";
            var unit2 = new EntityController { EntityId = "unit_beta", tag = "Player", CurrentHealth = 100f, MaxHealth = 100f };
            unit2.gameObject.name = "unit_beta";
            var harvester = new EntityController { EntityId = "harvester_1", tag = "Player", CurrentHealth = 80f, MaxHealth = 80f };
            harvester.gameObject.name = "harvester_1";

            ExistingControllers.Instance.PlayerUnits().Add(unit1);
            ExistingControllers.Instance.PlayerUnits().Add(unit2);
            ExistingControllers.Instance.Harvesters.Add(harvester);

            // Select units by keys
            SelectionCommandResult selectRes = RuntimeGameplayCommandDirector.TrySelectEntities(new[] { "unit_alpha", "unit_beta" });
            if (!selectRes.Success || selectRes.EntitiesSelectedCount != 2)
            {
                throw new InvalidOperationException("TrySelectEntities should select 2 entities.");
            }

            SelectionCensus census = RuntimeSelectionCensus.Current();
            if (!census.IsSelectionReady || census.SelectedCount != 2 || census.SelectedEntityKeys.Count != 2)
            {
                throw new InvalidOperationException("RuntimeSelectionCensus.Current should report 2 selected units.");
            }

            // Assign Control Group 1
            SelectionCommandResult assignGroupRes = RuntimeGameplayCommandDirector.TryAssignControlGroup(1);
            if (!assignGroupRes.Success || assignGroupRes.EntitiesSelectedCount != 2)
            {
                throw new InvalidOperationException("TryAssignControlGroup should bind 2 units to group 1.");
            }

            census = RuntimeSelectionCensus.Current();
            if (census.ControlGroupCounts[1] != 2)
            {
                throw new InvalidOperationException("ControlGroupCounts[1] should report 2 units.");
            }

            // Deselect all
            SelectionCommandResult deselectRes = RuntimeGameplayCommandDirector.TryDeselectAll();
            if (!deselectRes.Success)
            {
                throw new InvalidOperationException("TryDeselectAll should succeed.");
            }

            census = RuntimeSelectionCensus.Current();
            if (census.SelectedCount != 0)
            {
                throw new InvalidOperationException("Selection count should be 0 after DeselectAll.");
            }

            // Select Control Group 1
            SelectionCommandResult recallGroupRes = RuntimeGameplayCommandDirector.TrySelectControlGroup(1);
            if (!recallGroupRes.Success || recallGroupRes.EntitiesSelectedCount != 2)
            {
                throw new InvalidOperationException("TrySelectControlGroup should recall 2 units.");
            }

            census = RuntimeSelectionCensus.Current();

            // Select Category Harvesters
            SelectionCommandResult catRes = RuntimeGameplayCommandDirector.TrySelectCategory("Harvesters");
            if (!catRes.Success)
            {
                throw new InvalidOperationException("TrySelectCategory Harvesters should succeed.");
            }

            // Orders Stop & Hold Position
            UnitCommandResult stopRes = RuntimeGameplayCommandDirector.TryOrderStop("unit_alpha");
            if (!stopRes.Success || stopRes.CommandType != "Stop")
            {
                throw new InvalidOperationException("TryOrderStop should succeed.");
            }

            UnitCommandResult holdRes = RuntimeGameplayCommandDirector.TryOrderHoldPosition();
            if (!holdRes.Success || holdRes.CommandType != "HoldPosition")
            {
                throw new InvalidOperationException("TryOrderHoldPosition should succeed.");
            }

            UnitCommandResult attackMoveRes = RuntimeGameplayCommandDirector.TryOrderAttackMove("unit_alpha", 50f, 60f);
            if (!attackMoveRes.Success || attackMoveRes.TargetX != 50f || attackMoveRes.TargetZ != 60f)
            {
                throw new InvalidOperationException("TryOrderAttackMove should succeed with target coords (50, 60).");
            }

            // Serialization check
            string json = AgentDiagnosticsFormatter.SerializeSelectionCensus(census);
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.GetProperty("isSelectionReady").GetBoolean() ||
                    doc.RootElement.GetProperty("selectedCount").GetInt32() != 2 ||
                    doc.RootElement.GetProperty("controlGroupCounts")[1].GetInt32() != 2)
                {
                    throw new InvalidOperationException("SerializeSelectionCensus JSON mismatch.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            PlayerUnitController.Instance.DeselectAllUnits();
        }
    }),
    ("runtime fog census and fog command director safely toggle and reveal fog of war", () =>
    {
        FogStateCensus state = RuntimeFogCensus.Current();
        if (!state.IsAvailable || !state.IsActive || state.IsDisabled)
        {
            throw new InvalidOperationException("RuntimeFogCensus.Current should report available and active Fog of War.");
        }

        // Disable Fog of War
        FogCommandResult disableRes = RuntimeGameplayCommandDirector.TrySetFogOfWar(true);
        if (!disableRes.Success || !disableRes.IsDisabled || !UpdateFogOfWar.RevealedStaticCalled)
        {
            throw new InvalidOperationException("TrySetFogOfWar(true) should disable fog and call Reveal_Static.");
        }

        state = RuntimeFogCensus.Current();
        if (!state.IsDisabled || state.IsActive)
        {
            throw new InvalidOperationException("RuntimeFogCensus should report fog disabled.");
        }

        // Reveal Fog of War
        FogCommandResult revealRes = RuntimeGameplayCommandDirector.TryRevealFogOfWar();
        if (!revealRes.Success)
        {
            throw new InvalidOperationException("TryRevealFogOfWar should succeed.");
        }

        // Re-enable Fog of War
        RuntimeGameplayCommandDirector.TrySetFogOfWar(false);

        // Serialization check
        string json = AgentDiagnosticsFormatter.SerializeFogStateCensus(state);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.GetProperty("isAvailable").GetBoolean() ||
                !doc.RootElement.GetProperty("isDisabled").GetBoolean())
            {
                throw new InvalidOperationException("SerializeFogStateCensus JSON mismatch.");
            }
        }
    }),
    ("runtime game speed match control and entity spawning directors execute commands without GUI", () =>
    {
        try
        {
            // Game Speed
            GameSpeedCommandResult speedRes = RuntimeGameplayCommandDirector.TrySetGameTimeScale(2.5f);
            if (!speedRes.Success || speedRes.CurrentTimeScale != 2.5f || RuntimeGameplayCommandDirector.GetGameTimeScale() != 2.5f)
            {
                throw new InvalidOperationException("TrySetGameTimeScale should update Time.timeScale to 2.5f.");
            }

            RuntimeGameplayCommandDirector.TrySetGameTimeScale(1.0f);

            // Match Control
            FinishLevel.Reset();
            MatchControlResult winRes = RuntimeGameplayCommandDirector.TryTriggerMatchEnd(true);
            if (!winRes.Success || winRes.Outcome != "Win" || !FinishLevel.WonCalled)
            {
                throw new InvalidOperationException("TryTriggerMatchEnd(true) should invoke victory sequence.");
            }

            FinishLevel.Reset();

            // Entity Spawning
            EntitySpawnResult spawnRes = RuntimeGameplayCommandDirector.TrySpawnEntity("SampleScout", "Player", 45f, 55f, "SpawnedScout");
            if (!spawnRes.Success || spawnRes.EntityId != "SampleScout" || spawnRes.Side != "Player" ||
                spawnRes.SpawnedX != 45f || spawnRes.SpawnedZ != 55f || spawnRes.Health != 100f)
            {
                throw new InvalidOperationException("TrySpawnEntity should spawn SampleScout at (45, 55) with 100 health.");
            }

            // Serialization checks
            string speedJson = AgentDiagnosticsFormatter.SerializeGameSpeedCommandResult(speedRes);
            using (var doc = JsonDocument.Parse(speedJson))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("currentTimeScale").GetSingle() != 2.5f)
                {
                    throw new InvalidOperationException("SerializeGameSpeedCommandResult JSON mismatch.");
                }
            }

            string spawnJson = AgentDiagnosticsFormatter.SerializeEntitySpawnResult(spawnRes);
            using (var doc = JsonDocument.Parse(spawnJson))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("entityId").GetString() != "SampleScout" ||
                    doc.RootElement.GetProperty("side").GetString() != "Player")
                {
                    throw new InvalidOperationException("SerializeEntitySpawnResult JSON mismatch.");
                }
            }
        }
        finally
        {
            FinishLevel.Reset();
        }
    }),
    ("runtime entity catalog census queries registered balancing units buildings and factories", () =>
    {
        EntityBalancingStore.RolesOf["Unit_Prowler"] = UnitRole.Unit;
        EntityBalancingStore.RolesOf["Unit_Striker"] = UnitRole.Scout;
        EntityBalancingStore.BuildingEntityIds.Add("Building_Turret");
        EntityBalancingStore.BuildingEntityIds.Add("Building_Radar");
        EntityBalancingStore.FactoryEntityIdOf["Factory_Vehicles"] = "Building_Factory";

        EntityCatalogCensus catalog = RuntimeEntityCatalogCensus.Current();
        if (!catalog.IsCatalogReady || catalog.TotalUnits < 2 || catalog.TotalBuildings < 2 || catalog.TotalFactories < 1)
        {
            throw new InvalidOperationException("RuntimeEntityCatalogCensus should report registered units, buildings, and factories.");
        }

        if (!catalog.UnitIds.Contains("Unit_Prowler") || !catalog.BuildingIds.Contains("Building_Turret"))
        {
            throw new InvalidOperationException("EntityCatalogCensus should contain Unit_Prowler and Building_Turret.");
        }

        // Serialization check
        string json = AgentDiagnosticsFormatter.SerializeEntityCatalogCensus(catalog);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.GetProperty("isCatalogReady").GetBoolean() ||
                doc.RootElement.GetProperty("totalUnits").GetInt32() < 2 ||
                doc.RootElement.GetProperty("totalBuildings").GetInt32() < 2)
            {
                throw new InvalidOperationException("SerializeEntityCatalogCensus JSON mismatch.");
            }
        }

        // Verify Diagnostic Catalog includes new getters
        string catalogJson = AgentDiagnosticsFormatter.SerializeDiagnosticCatalog();
        using (var doc = JsonDocument.Parse(catalogJson))
        {
            var items = doc.RootElement.GetProperty("diagnostics");
            var names = new HashSet<string>();
            foreach (var item in items.EnumerateArray())
            {
                names.Add(item.GetProperty("name").GetString()!);
            }

            if (!names.Contains("GetSelectionCensusJson") ||
                !names.Contains("GetFogStateJson") ||
                !names.Contains("GetEntityCatalogJson") ||
                !names.Contains("SelectUnitsJson") ||
                !names.Contains("DeselectAllUnitsJson") ||
                !names.Contains("SetGameTimeScaleJson") ||
                !names.Contains("SpawnEntityJson"))
            {
                throw new InvalidOperationException("Diagnostic catalog should include newly exposed getters.");
            }
        }
    }),
("runtime ai director census and command director observe and manipulate attack waves and debug visuals", () =>
    {
        AiDirectorCensus census = RuntimeAiDirectorCensus.Current();
        if (!census.IsAiDirectorAvailable || census.CurrentAttackWaveNumber < 0)
        {
            throw new InvalidOperationException("RuntimeAiDirectorCensus.Current should report available AI director.");
        }

        // Set wave number
        AiDirectorCommandResult setRes = RuntimeGameplayCommandDirector.TrySetAttackWaveNumber(7);
        if (!setRes.Success || setRes.WaveNumber != 7)
        {
            throw new InvalidOperationException("TrySetAttackWaveNumber(7) should update wave number to 7.");
        }

        census = RuntimeAiDirectorCensus.Current();
        if (census.CurrentAttackWaveNumber != 7)
        {
            throw new InvalidOperationException("CurrentAttackWaveNumber should be 7.");
        }

        // Increment wave
        AiDirectorCommandResult incRes = RuntimeGameplayCommandDirector.TryIncrementAttackWave();
        if (!incRes.Success || incRes.WaveNumber != 8)
        {
            throw new InvalidOperationException("TryIncrementAttackWave should increment wave to 8.");
        }

        // Toggle debug visuals
        AiDirectorCommandResult visRes = RuntimeGameplayCommandDirector.TryToggleAiDebugVisuals(true, true);
        if (!visRes.Success)
        {
            throw new InvalidOperationException("TryToggleAiDebugVisuals should succeed.");
        }

        census = RuntimeAiDirectorCensus.Current();
        if (!census.ShowAiTags || !census.ShowCurrentCommands)
        {
            throw new InvalidOperationException("ShowAiTags and ShowCurrentCommands should be true.");
        }

        // Serialization check
        string json = AgentDiagnosticsFormatter.SerializeAiDirectorCensus(census);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.GetProperty("isAiDirectorAvailable").GetBoolean() ||
                doc.RootElement.GetProperty("currentAttackWaveNumber").GetInt32() != 8 ||
                !doc.RootElement.GetProperty("showAiTags").GetBoolean())
            {
                throw new InvalidOperationException("SerializeAiDirectorCensus JSON mismatch.");
            }
        }
    }),
    ("runtime camera census and director move focus and zoom camera viewport without GUI", () =>
    {
        CameraCensus census = RuntimeCameraCensus.Current();
        if (!census.IsCameraAvailable)
        {
            throw new InvalidOperationException("RuntimeCameraCensus.Current should report available camera.");
        }

        // Move camera
        CameraCommandResult moveRes = RuntimeGameplayCommandDirector.TryMoveCamera(120f, 180f);
        if (!moveRes.Success || moveRes.TargetX != 120f || moveRes.TargetZ != 180f)
        {
            throw new InvalidOperationException("TryMoveCamera should report moved target coords.");
        }

        // Set camera zoom
        CameraCommandResult zoomRes = RuntimeGameplayCommandDirector.TrySetCameraZoom(55f);
        if (!zoomRes.Success || zoomRes.FieldOfView != 55f)
        {
            throw new InvalidOperationException("TrySetCameraZoom should update camera FOV to 55f.");
        }

        // Focus camera on entity
        var scout = new EntityController { EntityId = "scout_rover", tag = "Player", CurrentHealth = 100f, MaxHealth = 100f };
        scout.gameObject.name = "scout_rover";
        scout.transform.position = new Vector3(40f, 0f, 60f);
        ExistingControllers.Instance.PlayerUnits().Add(scout);

        try
        {
            CameraCommandResult focusRes = RuntimeGameplayCommandDirector.TryFocusCameraOnEntity("scout_rover");
            if (!focusRes.Success || focusRes.TargetX != 40f || focusRes.TargetZ != 60f)
            {
                throw new InvalidOperationException("TryFocusCameraOnEntity should succeed focusing scout_rover.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
        }

        // Serialization check
        string json = AgentDiagnosticsFormatter.SerializeCameraCensus(census);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.GetProperty("isCameraAvailable").GetBoolean())
            {
                throw new InvalidOperationException("SerializeCameraCensus JSON mismatch.");
            }
        }
    }),
    ("runtime harvester census and director track crystal payloads and dispatch mining orders", () =>
    {
        try
        {
            ExistingControllers.Instance.Reset();
            Harvest.ClaimCalled = false;
            Harvest.RefineryCalled = false;

            var harvester = new EntityController { EntityId = "harvester_mining_1", tag = "Player", CurrentHealth = 120f, MaxHealth = 120f };
            harvester.gameObject.name = "harvester_mining_1";
            var harvestComp = harvester.gameObject.AddComponent<Harvest>();
            harvestComp._currentLoad = 40f;
            harvestComp.MaxValue = 100;
            harvestComp._loadFraction = 0.4f;

            ExistingControllers.Instance.Harvesters.Add(harvester);

            HarvesterCensus census = RuntimeHarvesterCensus.Current();
            if (census.TotalHarvesterCount != 1 || census.PlayerHarvesterCount != 1 || census.TotalCrystalsCarried != 40f)
            {
                throw new InvalidOperationException("RuntimeHarvesterCensus should report 1 player harvester with 40 crystals carried.");
            }

            if (census.HarvesterRows.Count != 1 || census.HarvesterRows[0].EntityKeyId != "harvester_mining_1")
            {
                throw new InvalidOperationException("HarvesterRows should include harvester_mining_1.");
            }

            // Command harvest
            HarvesterCommandResult claimRes = RuntimeGameplayCommandDirector.TryOrderHarvesterHarvest("harvester_mining_1");
            if (!claimRes.Success || !Harvest.ClaimCalled)
            {
                throw new InvalidOperationException("TryOrderHarvesterHarvest should invoke ClaimClosestCrystalAndMoveToIt.");
            }

            // Command return to refinery
            HarvesterCommandResult refRes = RuntimeGameplayCommandDirector.TryOrderHarvesterReturn("harvester_mining_1");
            if (!refRes.Success || !Harvest.RefineryCalled)
            {
                throw new InvalidOperationException("TryOrderHarvesterReturn should invoke MoveToClosestRefinery.");
            }

            // Serialization check
            string json = AgentDiagnosticsFormatter.SerializeHarvesterCensus(census);
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.GetProperty("totalHarvesterCount").GetInt32() != 1 ||
                    doc.RootElement.GetProperty("totalCrystalsCarried").GetSingle() != 40f ||
                    doc.RootElement.GetProperty("omittedHarvesterCount").GetInt32() != 0)
                {
                    throw new InvalidOperationException("SerializeHarvesterCensus JSON mismatch.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            Harvest.ClaimCalled = false;
            Harvest.RefineryCalled = false;
        }
    }),
    ("runtime audio census and volume director query and adjust audio mixer sliders", () =>
    {
        AudioCensus census = RuntimeAudioCensus.Current();
        if (!census.IsAudioAvailable)
        {
            throw new InvalidOperationException("RuntimeAudioCensus.Current should report available audio.");
        }

        // Set master volume
        AudioCommandResult masterRes = RuntimeGameplayCommandDirector.TrySetAudioVolume("Master", 0.55f);
        if (!masterRes.Success || masterRes.VolumeSlider != 0.55f || AudioMixerHelper.MasterSlider != 0.55f)
        {
            throw new InvalidOperationException("TrySetAudioVolume(Master, 0.55) should update Master slider to 0.55.");
        }

        // Set music volume
        AudioCommandResult musicRes = RuntimeGameplayCommandDirector.TrySetAudioVolume("Music", 0.35f);
        if (!musicRes.Success || musicRes.VolumeSlider != 0.35f || AudioMixerHelper.MusicSlider != 0.35f)
        {
            throw new InvalidOperationException("TrySetAudioVolume(Music, 0.35) should update Music slider to 0.35.");
        }

        // Serialization check
        string json = AgentDiagnosticsFormatter.SerializeAudioCensus(census);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.GetProperty("isAudioAvailable").GetBoolean())
            {
                throw new InvalidOperationException("SerializeAudioCensus JSON mismatch.");
            }
        }
    }),
    ("runtime tech catalog census queries registered upgrades research branches and relics", () =>
    {
        TechCatalogCensus census = RuntimeTechCatalogCensus.Current();
        if (!census.IsTechCatalogReady || census.TotalUpgrades < 3 || census.TotalResearch < 2 || census.TotalRelics < 3)
        {
            throw new InvalidOperationException("RuntimeTechCatalogCensus should report registered tech items.");
        }

        if (!census.UpgradeIds.Contains("Upgrade_HullReinforcement") ||
            !census.ResearchIds.Contains("Research_AdvancedRobotics") ||
            !census.RelicIds.Contains("Relic_FoundryCounterweight"))
        {
            throw new InvalidOperationException("Tech catalog should contain expected sample IDs.");
        }

        // Serialization check
        string json = AgentDiagnosticsFormatter.SerializeTechCatalogCensus(census);
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.GetProperty("isTechCatalogReady").GetBoolean() ||
                doc.RootElement.GetProperty("totalUpgrades").GetInt32() < 3 ||
                doc.RootElement.GetProperty("omittedUpgradeCount").GetInt32() != 0)
            {
                throw new InvalidOperationException("SerializeTechCatalogCensus JSON mismatch.");
            }
        }

        // Verify Diagnostic Catalog includes newest endpoints
        string catalogJson = AgentDiagnosticsFormatter.SerializeDiagnosticCatalog();
        using (var doc = JsonDocument.Parse(catalogJson))
        {
            var items = doc.RootElement.GetProperty("diagnostics");
            var names = new HashSet<string>();
            foreach (var item in items.EnumerateArray())
            {
                names.Add(item.GetProperty("name").GetString()!);
            }

            if (!names.Contains("GetAiDirectorCensusJson") ||
                !names.Contains("SetAttackWaveNumberJson") ||
                !names.Contains("IncrementAttackWaveJson") ||
                !names.Contains("ToggleAiDebugVisualsJson") ||
                !names.Contains("GetCameraCensusJson") ||
                !names.Contains("MoveCameraJson") ||
                !names.Contains("FocusCameraOnEntityJson") ||
                !names.Contains("SetCameraZoomJson") ||
                !names.Contains("GetHarvesterCensusJson") ||
                !names.Contains("OrderHarvesterHarvestJson") ||
                !names.Contains("OrderHarvesterReturnJson") ||
                !names.Contains("GetAudioCensusJson") ||
                !names.Contains("SetAudioVolumeJson") ||
                !names.Contains("GetTechCatalogJson") ||
                !names.Contains("GetVeterancyCensusJson") ||
                !names.Contains("OrderGrantCombatXpJson") ||
                !names.Contains("OrderSetVeterancyLevelJson") ||
                !names.Contains("GetCommandAuraCensusJson") ||
                !names.Contains("OrderCommandAuraPulseJson") ||
                !names.Contains("OrderClearCommandAuraLinksJson") ||
                !names.Contains("GetHazardCensusJson") ||
                !names.Contains("OrderSpawnHazardZoneJson") ||
                !names.Contains("OrderClearHazardZonesJson"))
            {
                throw new InvalidOperationException("Diagnostic catalog should include all newly exposed endpoints.");
            }
        }
    }),
    ("runtime veterancy census and director track XP, promote ranks, and format JSON", () =>
    {
        try
        {
            var pUnit = new EntityController
            {
                entityId = "unit:hero_mech",
                UniqueEntityId = 555,
                IsControlledByPlayer = true,
                IsUnit = true,
                MaxRank = 5,
                CurrentRank = 1,
            };
            ExistingControllers.Instance.PlayerUnits().Add(pUnit);

            VeterancyCensus census = RuntimeVeterancyCensus.Current();
            if (census == null || census.OmittedUnitCount != 0 || census.TotalVeterancyUnitsCount < 1)
            {
                throw new InvalidOperationException("RuntimeVeterancyCensus should report registered unit with rank > 0.");
            }

            // Grant XP command
            VeterancyCommandResult xpRes = RuntimeGameplayCommandDirector.TryGrantEntityCombatXp("555", 3);
            if (!xpRes.Success || xpRes.CurrentRank < 1)
            {
                throw new InvalidOperationException("TryGrantEntityCombatXp should grant XP successfully.");
            }

            // Set Rank command
            VeterancyCommandResult rankRes = RuntimeGameplayCommandDirector.TrySetEntityVeterancyLevel("555", 3);
            if (!rankRes.Success || rankRes.CurrentRank != 3)
            {
                throw new InvalidOperationException("TrySetEntityVeterancyLevel should advance unit to Rank 3.");
            }

            // Serialization checks
            string censusJson = AgentDiagnosticsFormatter.SerializeVeterancyCensus(census);
            using (var doc = JsonDocument.Parse(censusJson))
            {
                if (!doc.RootElement.TryGetProperty("omittedUnitCount", out var omittedProp) ||
                    omittedProp.GetInt32() != 0 ||
                    !doc.RootElement.TryGetProperty("units", out var unitsProp) ||
                    unitsProp.GetArrayLength() < 1)
                {
                    throw new InvalidOperationException("SerializeVeterancyCensus JSON format error.");
                }
            }

            string cmdJson = AgentDiagnosticsFormatter.SerializeVeterancyCommandResult(rankRes);
            using (var doc = JsonDocument.Parse(cmdJson))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("currentRank").GetInt32() != 3)
                {
                    throw new InvalidOperationException("SerializeVeterancyCommandResult JSON format error.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            RuntimeUnitMechanicsDirector.ClearProfiles();
        }
    }),
    ("runtime command aura census and director manage captain linkage and squad pulses", () =>
    {
        try
        {
            var captain = new EntityController
            {
                entityId = "unit:squad_captain",
                UniqueEntityId = 601,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(10f, 0f, 10f),
            };
            var follower = new EntityController
            {
                entityId = "unit:squad_grunt",
                UniqueEntityId = 602,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(12f, 0f, 11f),
            };
            ExistingControllers.Instance.PlayerUnits().Add(captain);
            ExistingControllers.Instance.PlayerUnits().Add(follower);

            // Dispatch command aura pulse
            CommandAuraResult pulseRes = RuntimeGameplayCommandDirector.TryDispatchCommandAuraPulse(
                "601", "Inspire", 15.0f, 30.0f);
            if (!pulseRes.Success || pulseRes.LinkedFollowersCount < 1 || follower.CaptainOfThisEntity != captain)
            {
                throw new InvalidOperationException("TryDispatchCommandAuraPulse should link nearby follower to captain.");
            }

            // Query census
            CommandAuraCensus census = RuntimeCommandAuraCensus.Current();
            if (census == null || census.OmittedAuraCount != 0 || census.TotalActiveAurasCount < 1 || census.TotalLinkedFollowersCount < 1)
            {
                throw new InvalidOperationException("RuntimeCommandAuraCensus should report active aura and linked follower.");
            }

            // Clear links
            CommandAuraResult clearRes = RuntimeGameplayCommandDirector.TryClearCommandAuraLinks("601");
            if (!clearRes.Success || clearRes.LinkedFollowersCount < 1 || follower.CaptainOfThisEntity != null)
            {
                throw new InvalidOperationException("TryClearCommandAuraLinks should remove captain link from follower.");
            }

            // Serialization checks
            string censusJson = AgentDiagnosticsFormatter.SerializeCommandAuraCensus(census);
            using (var doc = JsonDocument.Parse(censusJson))
            {
                if (!doc.RootElement.TryGetProperty("omittedAuraCount", out var omittedProp) ||
                    omittedProp.GetInt32() != 0 ||
                    !doc.RootElement.TryGetProperty("auras", out var aurasProp) ||
                    aurasProp.GetArrayLength() < 1)
                {
                    throw new InvalidOperationException("SerializeCommandAuraCensus JSON format error.");
                }
            }

            string cmdJson = AgentDiagnosticsFormatter.SerializeCommandAuraResult(pulseRes);
            using (var doc = JsonDocument.Parse(cmdJson))
            {
                if (!doc.RootElement.GetProperty("success").GetBoolean() ||
                    doc.RootElement.GetProperty("auraType").GetString() != "Inspire")
                {
                    throw new InvalidOperationException("SerializeCommandAuraResult JSON format error.");
                }
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            RuntimeUnitMechanicsDirector.ClearProfiles();
        }
    }),
    ("runtime battlefield environmental hazard zones enforce trigger colliders and amortized tick rates", () =>
    {
        try
        {
            var enemy = new EntityController
            {
                entityId = "unit:enemy_tank",
                UniqueEntityId = 701,
                IsControlledByPlayer = false,
                IsControlledByAi = true,
                IsUnit = true,
                Position = new Vector3(20f, 0f, 20f),
                CurrentHealth = 100f,
                MaxHealth = 100f,
            };
            var ally = new EntityController
            {
                entityId = "unit:ally_tank",
                UniqueEntityId = 702,
                IsControlledByPlayer = true,
                IsUnit = true,
                Position = new Vector3(21f, 0f, 20f),
                CurrentHealth = 50f,
                MaxHealth = 100f,
                CurrentArmor = 10f,
                MaxArmor = 50f,
            };
            ExistingControllers.Instance.AiUnits().Add(enemy);
            ExistingControllers.Instance.PlayerUnits().Add(ally);

            // Spawn firestorm
            HazardCommandResult fireRes = RuntimeGameplayCommandDirector.TrySpawnHazardZone(
                "Firestorm", 20f, 20f, 10f, 15f, 20f);
            if (!fireRes.Success || string.IsNullOrEmpty(fireRes.ZoneId))
            {
                throw new InvalidOperationException("TrySpawnHazardZone should succeed and assign zone ID.");
            }

            // Tick firestorm -> enemy should take damage and catch fire
            RuntimeHazardManager.UpdateTick(Time.time + 0.6f);
            if (enemy.CurrentHealth >= 100f || !enemy.HasActiveStatusEffect(StatusEffect.OnFire))
            {
                throw new InvalidOperationException("Firestorm hazard tick should deal damage and apply OnFire status.");
            }

            // Spawn NaniteField
            HazardCommandResult nanoRes = RuntimeGameplayCommandDirector.TrySpawnHazardZone(
                "NaniteField", 20f, 20f, 10f, 15f, 15f);
            if (!nanoRes.Success)
            {
                throw new InvalidOperationException("Spawn NaniteField should succeed.");
            }

            // Tick nanite -> ally should heal
            float allyHpBefore = ally.CurrentHealth;
            RuntimeHazardManager.UpdateTick(Time.time + 1.2f);
            if (ally.CurrentHealth <= allyHpBefore)
            {
                throw new InvalidOperationException("NaniteField hazard tick should heal damaged friendly units.");
            }

            // Census check
            HazardZoneCensus census = RuntimeHazardCensus.Current();
            if (census == null || census.OmittedZoneCount != 0 || census.TotalActiveZonesCount < 2)
            {
                throw new InvalidOperationException("RuntimeHazardCensus should report 2 active hazard zones.");
            }

            // Serialization checks
            string censusJson = AgentDiagnosticsFormatter.SerializeHazardCensus(census);
            using (var doc = JsonDocument.Parse(censusJson))
            {
                if (!doc.RootElement.TryGetProperty("omittedZoneCount", out var omittedProp) ||
                    omittedProp.GetInt32() != 0 ||
                    !doc.RootElement.TryGetProperty("activeZones", out var zonesProp) ||
                    zonesProp.GetArrayLength() < 2)
                {
                    throw new InvalidOperationException("SerializeHazardCensus JSON format error.");
                }
            }

            // Clear hazards
            HazardCommandResult clearRes = RuntimeGameplayCommandDirector.TryClearHazardZones();
            if (!clearRes.Success || RuntimeHazardManager.ActiveZoneCount != 0)
            {
                throw new InvalidOperationException("TryClearHazardZones should remove all active hazard zones.");
            }
        }
        finally
        {
            ExistingControllers.Instance.Reset();
            RuntimeHazardManager.ClearAll();
        }
    }),
    };
}
