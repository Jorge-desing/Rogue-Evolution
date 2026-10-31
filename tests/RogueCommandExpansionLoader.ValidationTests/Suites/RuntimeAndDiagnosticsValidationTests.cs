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
    };
}
