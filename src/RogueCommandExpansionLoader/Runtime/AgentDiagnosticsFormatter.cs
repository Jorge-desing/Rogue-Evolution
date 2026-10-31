using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Runtime;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>Primitive view of the inspector overlay state, decoupled from Unity types.</summary>
    public sealed class OverlayStateView
    {
        public OverlayStateView(
            bool ready,
            bool isOpen,
            string toggleShortcut,
            int selectedTab,
            bool watermarkVisible,
            bool isGuiDisabled,
            bool isGuiFailureReported,
            string logFilter,
            string pluginVersion,
            string compatibilityError,
            bool isPatchResultSuccess,
            string patchResultMessage)
        {
            Ready = ready;
            IsOpen = isOpen;
            ToggleShortcut = toggleShortcut ?? string.Empty;
            SelectedTab = selectedTab;
            WatermarkVisible = watermarkVisible;
            IsGuiDisabled = isGuiDisabled;
            IsGuiFailureReported = isGuiFailureReported;
            LogFilter = logFilter ?? string.Empty;
            PluginVersion = pluginVersion ?? string.Empty;
            CompatibilityError = compatibilityError ?? string.Empty;
            IsPatchResultSuccess = isPatchResultSuccess;
            PatchResultMessage = patchResultMessage ?? string.Empty;
        }

        public bool Ready { get; }
        public bool IsOpen { get; }
        public string ToggleShortcut { get; }
        public int SelectedTab { get; }
        public bool WatermarkVisible { get; }
        public bool IsGuiDisabled { get; }
        public bool IsGuiFailureReported { get; }
        public string LogFilter { get; }
        public string PluginVersion { get; }
        public string CompatibilityError { get; }
        public bool IsPatchResultSuccess { get; }
        public string PatchResultMessage { get; }
    }

    /// <summary>Primitive view of the range display state, decoupled from Unity types.</summary>
    public sealed class RangeStateView
    {
        public RangeStateView(
            bool ready,
            bool enabled,
            string toggleKey,
            int trackedEntities,
            bool showWeaponRange,
            bool showSkillRange,
            bool showEffectRadius,
            bool showSightRange,
            float lineWidth,
            int segments,
            float heightOffset)
        {
            Ready = ready;
            IsEnabled = enabled;
            ToggleKey = toggleKey ?? string.Empty;
            TrackedEntities = trackedEntities;
            ShowWeaponRange = showWeaponRange;
            ShowSkillRange = showSkillRange;
            ShowEffectRadius = showEffectRadius;
            ShowSightRange = showSightRange;
            LineWidth = lineWidth;
            Segments = segments;
            HeightOffset = heightOffset;
        }

        public bool Ready { get; }
        public bool IsEnabled { get; }
        public string ToggleKey { get; }
        public int TrackedEntities { get; }
        public bool ShowWeaponRange { get; }
        public bool ShowSkillRange { get; }
        public bool ShowEffectRadius { get; }
        public bool ShowSightRange { get; }
        public float LineWidth { get; }
        public int Segments { get; }
        public float HeightOffset { get; }
    }

    /// <summary>
    /// Pure, dependency-free JSON formatting for the loader's agent-readable
    /// diagnostics. Kept separate from the MonoBehaviour so it can be unit-tested
    /// without a game runtime. The serializer is a small hand-rolled writer over the
    /// dictionary/list/primitive values used here, so it needs no third-party JSON
    /// package in either the plugin or the test harness.
    /// </summary>
    public static class AgentDiagnosticsFormatter
    {
        public static string SerializeSnapshot(LoaderDiagnosticsSnapshot snapshot)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "loaderDiagnostics",
                ["available"] = snapshot != null,
            };

            if (snapshot == null)
            {
                return Serialize(view);
            }

            view["targetGameVersion"] = snapshot.TargetGameVersion;
            view["isNativeRegistryReady"] = snapshot.IsNativeRegistryReady;
            view["isLoaded"] = snapshot.IsLoaded;
            view["activePackCount"] = snapshot.ActivePackCount;
            view["failedPackCount"] = snapshot.FailedPackCount;
            view["totalBundleBytes"] = snapshot.TotalBundleBytes;
            view["unitCount"] = snapshot.Units.Count;
            view["stageCount"] = snapshot.Stages.Count;
            view["aiProfileCount"] = snapshot.AiProfiles.Count;
            view["packs"] = BuildPackRows(snapshot.Packs);
            view["units"] = BuildUnitRows(snapshot.Units);
            view["stages"] = BuildStageRows(snapshot.Stages);
            view["aiProfiles"] = BuildAiProfileRows(snapshot.AiProfiles);

            LoaderLogBurstSummary summary = LoaderLogBurstSummaryPolicy.Create(snapshot.RecentLogs);
            view["recentLogCount"] = snapshot.RecentLogs.Count;
            view["recentLogSummary"] = summary != null ? summary.DisplayText : string.Empty;

            return Serialize(view);
        }

        public static string SerializeUtilities(
            LoaderDiagnosticsSnapshot snapshot,
            OverlayStateView overlayState,
            RangeStateView rangeState)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "utilities",
                ["cleanRoom"] = BuildCleanRoomUtilities(snapshot),
                ["visual"] = BuildVisualUtilities(overlayState, rangeState),
            };
            return Serialize(view);
        }

        public static string SerializeInspectorToggleState(bool isOpen, bool togglePressed, bool closePressed)
        {
            bool resolved = InspectorTogglePolicy.Resolve(isOpen, togglePressed, closePressed);
            var view = new Dictionary<string, object>
            {
                ["isOpen"] = isOpen,
                ["togglePressed"] = togglePressed,
                ["closePressed"] = closePressed,
                ["resolvedOpen"] = resolved,
            };
            return Serialize(view);
        }

        public static string SerializeRangeCirclePointCount(int segments)
        {
            const int minimumSegments = 8;
            const int maximumSegments = 256;
            bool valid = segments >= minimumSegments && segments <= maximumSegments;
            int pointCount = valid ? segments + 1 : 0;
            var view = new Dictionary<string, object>
            {
                ["segments"] = segments,
                ["minimumSegments"] = minimumSegments,
                ["maximumSegments"] = maximumSegments,
                ["valid"] = valid,
                ["pointCount"] = pointCount,
            };
            return Serialize(view);
        }

        public static string SerializeIdentifierColor(string identifier)
        {
            string id = identifier ?? string.Empty;
            int index = IdentifierColorPolicy.PaletteIndex(id);
            var view = new Dictionary<string, object>
            {
                ["identifier"] = id,
                ["index"] = index,
                ["hex"] = ToHex(IdentifierColorPolicy.Resolve(id)),
                ["paletteSize"] = IdentifierColorPolicy.PaletteSize,
            };
            return Serialize(view);
        }

        private static List<Dictionary<string, object>> BuildPackRows(IReadOnlyList<PackDiagnosticReport> packs)
        {
            var rows = new List<Dictionary<string, object>>();
            if (packs == null)
            {
                return rows;
            }

            for (int i = 0; i < packs.Count; i++)
            {
                PackDiagnosticReport pack = packs[i];
                if (pack == null)
                {
                    continue;
                }

                rows.Add(new Dictionary<string, object>
                {
                    ["packId"] = pack.PackId,
                    ["folderName"] = pack.FolderName,
                    ["isLoaded"] = pack.IsLoaded,
                    ["statusMessage"] = pack.StatusMessage,
                    ["bundleCount"] = pack.BundleCount,
                    ["totalBundleBytes"] = pack.TotalBundleBytes,
                    ["unitCount"] = pack.UnitCount,
                    ["stageCount"] = pack.StageCount,
                    ["aiProfileCount"] = pack.AiProfileCount,
                });
            }

            return rows;
        }

        private static List<Dictionary<string, object>> BuildUnitRows(IReadOnlyList<UnitDiagnosticReport> units)
        {
            var rows = new List<Dictionary<string, object>>();
            if (units == null)
            {
                return rows;
            }

            for (int i = 0; i < units.Count; i++)
            {
                UnitDiagnosticReport unit = units[i];
                if (unit == null)
                {
                    continue;
                }

                rows.Add(new Dictionary<string, object>
                {
                    ["unitId"] = unit.UnitId,
                    ["packId"] = unit.PackId,
                    ["prefabKey"] = unit.PrefabKey,
                    ["imageKey"] = unit.ImageKey,
                    ["isAllowedAsBlueprint"] = unit.IsAllowedAsBlueprint,
                    ["isAllowedForAi"] = unit.IsAllowedForAi,
                    ["variantCount"] = unit.VariantCount,
                    ["accentHex"] = ToHex(IdentifierColorPolicy.Resolve(unit.UnitId)),
                });
            }

            return rows;
        }

        private static List<Dictionary<string, object>> BuildStageRows(IReadOnlyList<StageDiagnosticReport> stages)
        {
            var rows = new List<Dictionary<string, object>>();
            if (stages == null)
            {
                return rows;
            }

            for (int i = 0; i < stages.Count; i++)
            {
                StageDiagnosticReport stage = stages[i];
                if (stage == null)
                {
                    continue;
                }

                rows.Add(new Dictionary<string, object>
                {
                    ["stageId"] = stage.StageId,
                    ["packId"] = stage.PackId,
                    ["mapSizeMin"] = stage.MapSizeMin,
                    ["mapSizeMax"] = stage.MapSizeMax,
                    ["unitCount"] = stage.UnitIds != null ? stage.UnitIds.Count : 0,
                    ["aiProfileCount"] = stage.AiProfileIds != null ? stage.AiProfileIds.Count : 0,
                });
            }

            return rows;
        }

        private static List<Dictionary<string, object>> BuildAiProfileRows(IReadOnlyList<AiProfileDiagnosticReport> profiles)
        {
            var rows = new List<Dictionary<string, object>>();
            if (profiles == null)
            {
                return rows;
            }

            for (int i = 0; i < profiles.Count; i++)
            {
                AiProfileDiagnosticReport profile = profiles[i];
                if (profile == null)
                {
                    continue;
                }

                rows.Add(new Dictionary<string, object>
                {
                    ["profileId"] = profile.ProfileId,
                    ["packId"] = profile.PackId,
                    ["prefabAssetName"] = profile.PrefabAssetName,
                    ["activeAiInstances"] = profile.ActiveAiInstances,
                    ["moduleIdCount"] = profile.ModuleIds != null ? profile.ModuleIds.Count : 0,
                });
            }

            return rows;
        }

        private static Dictionary<string, object> BuildCleanRoomUtilities(LoaderDiagnosticsSnapshot snapshot)
        {
            return new Dictionary<string, object>
            {
                ["identifierColorPolicy"] = BuildIdentifierColorUtility(snapshot),
                ["logBurstSummaryPolicy"] = BuildLogBurstUtility(snapshot),
                ["inspectorTogglePolicy"] = new Dictionary<string, object>
                {
                    ["description"] = "Resolves the inspector open state from toggle/close input.",
                    ["sample"] = new Dictionary<string, object>
                    {
                        ["isOpen"] = false,
                        ["togglePressed"] = true,
                        ["closePressed"] = false,
                        ["resolvedOpen"] = InspectorTogglePolicy.Resolve(false, true, false),
                    },
                },
                ["rangeDisplayGeometry"] = new Dictionary<string, object>
                {
                    ["description"] = "Builds world-space circle points for range guides.",
                    ["minimumSegments"] = 8,
                    ["maximumSegments"] = 256,
                    ["sampleSegments"] = 32,
                    ["samplePointCount"] = 33,
                },
                ["rangeDisplayPolicy"] = new Dictionary<string, object>
                {
                    ["description"] = "Resolves per-kind range specs for selected entities.",
                },
            };
        }

        private static Dictionary<string, object> BuildIdentifierColorUtility(LoaderDiagnosticsSnapshot snapshot)
        {
            var samples = new List<Dictionary<string, object>>();
            if (snapshot != null)
            {
                for (int i = 0; i < snapshot.Units.Count && i < 8; i++)
                {
                    UnitDiagnosticReport unit = snapshot.Units[i];
                    if (unit == null)
                    {
                        continue;
                    }

                    samples.Add(new Dictionary<string, object>
                    {
                        ["identifier"] = unit.UnitId,
                        ["index"] = IdentifierColorPolicy.PaletteIndex(unit.UnitId),
                        ["hex"] = ToHex(IdentifierColorPolicy.Resolve(unit.UnitId)),
                    });
                }
            }

            return new Dictionary<string, object>
            {
                ["description"] = "Deterministic FNV-1a color per identifier.",
                ["paletteSize"] = IdentifierColorPolicy.PaletteSize,
                ["samples"] = samples,
            };
        }

        private static Dictionary<string, object> BuildLogBurstUtility(LoaderDiagnosticsSnapshot snapshot)
        {
            LoaderLogBurstSummary summary = snapshot != null
                ? LoaderLogBurstSummaryPolicy.Create(snapshot.RecentLogs)
                : null;

            return new Dictionary<string, object>
            {
                ["description"] = "Summarizes consecutive identical log messages.",
                ["recentLogCount"] = snapshot != null ? snapshot.RecentLogs.Count : 0,
                ["message"] = summary != null ? summary.Message : string.Empty,
                ["repeatCount"] = summary != null ? summary.RepeatCount : 0,
                ["displayText"] = summary != null ? summary.DisplayText : string.Empty,
            };
        }

        private static Dictionary<string, object> BuildVisualUtilities(
            OverlayStateView overlayState,
            RangeStateView rangeState)
        {
            return new Dictionary<string, object>
            {
                ["inspector"] = new Dictionary<string, object>
                {
                    ["description"] = "IMGUI diagnostics overlay.",
                    ["ready"] = overlayState != null && overlayState.Ready,
                    ["isOpen"] = overlayState != null && overlayState.IsOpen,
                    ["toggleShortcut"] = overlayState != null ? overlayState.ToggleShortcut : string.Empty,
                    ["selectedTab"] = overlayState != null ? overlayState.SelectedTab : -1,
                    ["watermarkVisible"] = overlayState != null && overlayState.WatermarkVisible,
                    ["guiDisabled"] = overlayState != null && overlayState.IsGuiDisabled,
                    ["guiFailureReported"] = overlayState != null && overlayState.IsGuiFailureReported,
                    ["logFilter"] = overlayState != null ? overlayState.LogFilter : string.Empty,
                    ["pluginVersion"] = overlayState != null ? overlayState.PluginVersion : string.Empty,
                    ["compatibilityError"] = overlayState != null ? overlayState.CompatibilityError : string.Empty,
                    ["patchResultSuccess"] = overlayState != null && overlayState.IsPatchResultSuccess,
                    ["patchResultMessage"] = overlayState != null ? overlayState.PatchResultMessage : string.Empty,
                },
                ["rangeDisplay"] = new Dictionary<string, object>
                {
                    ["description"] = "World-space range guides for the player selection.",
                    ["ready"] = rangeState != null && rangeState.Ready,
                    ["enabled"] = rangeState != null && rangeState.IsEnabled,
                    ["toggleKey"] = rangeState != null ? rangeState.ToggleKey : string.Empty,
                    ["trackedEntities"] = rangeState != null ? rangeState.TrackedEntities : 0,
                    ["showWeaponRange"] = rangeState != null && rangeState.ShowWeaponRange,
                    ["showSkillRange"] = rangeState != null && rangeState.ShowSkillRange,
                    ["showEffectRadius"] = rangeState != null && rangeState.ShowEffectRadius,
                    ["showSightRange"] = rangeState != null && rangeState.ShowSightRange,
                    ["lineWidth"] = rangeState != null ? rangeState.LineWidth : 0f,
                    ["segments"] = rangeState != null ? rangeState.Segments : 0,
                    ["heightOffset"] = rangeState != null ? rangeState.HeightOffset : 0f,
                },
                ["rangeColors"] = BuildRangeColors(),
                ["identifierPalette"] = BuildIdentifierPalette(),
            };
        }

        private static Dictionary<string, object> BuildRangeColors()
        {
            return new Dictionary<string, object>
            {
                ["description"] = "Per-kind range guide colors (world-space circles).",
                ["weapon"] = ToHex(RangeDisplayPolicy.RangeColor(RangeDisplayKind.Weapon)),
                ["skill"] = ToHex(RangeDisplayPolicy.RangeColor(RangeDisplayKind.Skill)),
                ["effect1"] = ToHex(RangeDisplayPolicy.RangeColor(RangeDisplayKind.Effect1)),
                ["effect2"] = ToHex(RangeDisplayPolicy.RangeColor(RangeDisplayKind.Effect2)),
                ["sight"] = ToHex(RangeDisplayPolicy.RangeColor(RangeDisplayKind.Sight)),
            };
        }

        private static Dictionary<string, object> BuildIdentifierPalette()
        {
            var colors = new List<string>();
            for (int i = 0; i < IdentifierColorPolicy.PaletteSize; i++)
            {
                colors.Add(ToHex(IdentifierColorPolicy.PaletteColorAt(i)));
            }

            return new Dictionary<string, object>
            {
                ["description"] = "Deterministic FNV-1a accent palette, indexed by identifier hash.",
                ["paletteSize"] = IdentifierColorPolicy.PaletteSize,
                ["colors"] = colors,
            };
        }

        /// <summary>
        /// Serializes the per-kind range guide colors as JSON. Readable by an agent
        /// without a visual capture.
        /// </summary>
        public static string SerializeRangeColors()
        {
            return Serialize(BuildRangeColors());
        }

        /// <summary>
        /// Serializes the full deterministic accent palette as JSON. Readable by an
        /// agent without a visual capture.
        /// </summary>
        public static string SerializeIdentifierPalette()
        {
            return Serialize(BuildIdentifierPalette());
        }

        private static string ToHex(UnityEngine.Color color)
        {
            int r = Clamp255(color.r);
            int g = Clamp255(color.g);
            int b = Clamp255(color.b);
            return "#" + ToHexByte(r) + ToHexByte(g) + ToHexByte(b);
        }

        private static int Clamp255(float value)
        {
            if (value < 0f)
            {
                return 0;
            }

            if (value > 1f)
            {
                return 255;
            }

            return (int)Math.Round(value * 255f);
        }

        private static string ToHexByte(int value)
        {
            const string digits = "0123456789ABCDEF";
            return new string(new[] { digits[value >> 4], digits[value & 0xF] });
        }

        // ---- Minimal dependency-free JSON writer ----

        private static string Serialize(Dictionary<string, object> view)
        {
            var builder = new StringBuilder();
            WriteValue(builder, view);
            return builder.ToString();
        }

        private static void WriteValue(StringBuilder builder, object value)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    break;
                case bool boolean:
                    builder.Append(boolean ? "true" : "false");
                    break;
                case string text:
                    WriteString(builder, text);
                    break;
                case Dictionary<string, object> map:
                    WriteObject(builder, map);
                    break;
                case IEnumerable sequence when !(value is string):
                    WriteArray(builder, sequence);
                    break;
                default:
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        private static void WriteObject(StringBuilder builder, Dictionary<string, object> map)
        {
            builder.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> entry in map)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                WriteString(builder, entry.Key);
                builder.Append(':');
                WriteValue(builder, entry.Value);
            }

            builder.Append('}');
        }

        private static void WriteArray(StringBuilder builder, IEnumerable sequence)
        {
            builder.Append('[');
            bool first = true;
            foreach (object item in sequence)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                WriteValue(builder, item);
            }

            builder.Append(']');
        }

        private static void WriteString(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }
    }
}
