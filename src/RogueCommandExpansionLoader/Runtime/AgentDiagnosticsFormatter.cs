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

        public static string SerializeGuiState(
            OverlayStateView overlay,
            RangeStateView range,
            GuiPlaybackView playback,
            int selectedEntityCount,
            int controlGroupCount,
            int hazardZoneCount)
        {
            int omittedGuiFieldCount = 0;
            var overlayView = new Dictionary<string, object>
            {
                ["ready"] = overlay != null && overlay.Ready,
                ["open"] = overlay != null && overlay.IsOpen,
                ["toggleShortcut"] = overlay != null ? overlay.ToggleShortcut : string.Empty,
                ["tab"] = overlay != null ? overlay.SelectedTab : 0,
                ["watermark"] = overlay != null && overlay.WatermarkVisible,
                ["guiDisabled"] = overlay != null && overlay.IsGuiDisabled,
                ["failureReported"] = overlay != null && overlay.IsGuiFailureReported,
                ["logFilter"] = overlay != null ? overlay.LogFilter : string.Empty,
                ["pluginVersion"] = overlay != null ? overlay.PluginVersion : string.Empty,
            };
            if (overlay == null) omittedGuiFieldCount++;
            var rangeView = new Dictionary<string, object>
            {
                ["ready"] = range != null && range.Ready,
                ["enabled"] = range != null && range.IsEnabled,
                ["toggleKey"] = range != null ? range.ToggleKey : string.Empty,
                ["tracked"] = range != null ? range.TrackedEntities : 0,
                ["showWeapon"] = range != null && range.ShowWeaponRange,
                ["showSkill"] = range != null && range.ShowSkillRange,
                ["showEffect"] = range != null && range.ShowEffectRadius,
                ["showSight"] = range != null && range.ShowSightRange,
                ["lineWidth"] = range != null ? range.LineWidth : 0f,
                ["segments"] = range != null ? range.Segments : 0,
                ["heightOffset"] = range != null ? range.HeightOffset : 0f,
            };
            if (range == null) omittedGuiFieldCount++;
            var playbackView = new Dictionary<string, object>
            {
                ["timeScale"] = playback != null ? playback.TimeScale : 1f,
                ["hooksInstalled"] = playback != null && playback.HooksInstalled,
                ["cameraX"] = playback != null ? playback.CameraX : 0f,
                ["cameraZ"] = playback != null ? playback.CameraZ : 0f,
                ["fov"] = playback != null ? playback.Fov : 0f,
            };
            if (playback == null) omittedGuiFieldCount++;
            var view = new Dictionary<string, object>
            {
                ["kind"] = "guiState",
                ["omittedGuiFieldCount"] = omittedGuiFieldCount,
                ["overlay"] = overlayView,
                ["overlayOpen"] = overlay != null && overlay.IsOpen,
                ["overlayTab"] = overlay != null ? overlay.SelectedTab : 0,
                ["range"] = rangeView,
                ["rangeEnabled"] = range != null && range.IsEnabled,
                ["playback"] = playbackView,
                ["selectedEntityCount"] = selectedEntityCount,
                ["controlGroupCount"] = controlGroupCount,
                ["hazardZoneCount"] = hazardZoneCount,
            };
            return Serialize(view);
        }

        public static string SerializeGuiControlResult(
            bool success,
            string message,
            float timeScale,
            float cameraX,
            float cameraZ)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "guiControlResult",
                ["omittedGuiFieldCount"] = 0,
                ["success"] = success,
                ["message"] = message ?? string.Empty,
                ["timeScale"] = timeScale,
                ["cameraX"] = cameraX,
                ["cameraZ"] = cameraZ,
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

        public static string SerializeRuntimeHooksState(
            bool hooksInstalled,
            int installedHookCount,
            string harmonyOwnerId,
            int registeredCombatXpCount)
        {
            var view = new Dictionary<string, object>
            {
                ["hooksInstalled"] = hooksInstalled,
                ["installedHookCount"] = installedHookCount,
                ["harmonyOwnerId"] = harmonyOwnerId ?? string.Empty,
                ["registeredCombatXpCount"] = registeredCombatXpCount,
                ["unitCreationHookCount"] = RuntimeHooks.UnitCreationHookCount,
                ["unitReuseHookCount"] = RuntimeHooks.UnitReuseHookCount,
                ["placementGhostHookCount"] = RuntimeHooks.PlacementGhostHookCount,
                ["factoryProduceHookCount"] = RuntimeHooks.FactoryProduceHookCount,
                ["entityRegistryResetCount"] = RuntimeHooks.EntityRegistryResetCount,
                ["entityRegisteredCount"] = RuntimeHooks.EntityRegisteredCount,
                ["entityUnregisteredCount"] = RuntimeHooks.EntityUnregisteredCount,
                ["activeRegisteredEntityCount"] = RuntimeHooks.ActiveRegisteredEntityCount,
                ["commandExecutedCount"] = RuntimeHooks.CommandExecutedCount,
                ["commandNextStepCount"] = RuntimeHooks.CommandNextStepCount,
                ["commandResetCount"] = RuntimeHooks.CommandResetCount,
                ["commandChainDeletedCount"] = RuntimeHooks.CommandChainDeletedCount,
                ["lastExecutedCommandKind"] = RuntimeHooks.LastExecutedCommandKind,
                ["unitCreationTranspilerCount"] = RuntimeHooks.UnitCreationTranspilerCount,
                ["entityRegisteredTranspilerCount"] = RuntimeHooks.EntityRegisteredTranspilerCount,
                ["entityUnregisteredTranspilerCount"] = RuntimeHooks.EntityUnregisteredTranspilerCount,
                ["unitReuseTranspilerCount"] = RuntimeHooks.UnitReuseTranspilerCount,
                ["initEntityControllerTranspilerCount"] = RuntimeHooks.InitEntityControllerTranspilerCount,
                ["entityOnInstantiatedTranspilerCount"] = RuntimeHooks.EntityOnInstantiatedTranspilerCount,
                ["entityReuseOnInstantiatedTranspilerCount"] = RuntimeHooks.EntityReuseOnInstantiatedTranspilerCount,
                ["factoryProduceTranspilerCount"] = RuntimeHooks.FactoryProduceTranspilerCount,
                ["entityRegistryResetTranspilerCount"] = RuntimeHooks.EntityRegistryResetTranspilerCount,
                ["placementGhostTranspilerCount"] = RuntimeHooks.PlacementGhostTranspilerCount,
                ["entityInitTranspilerCount"] = RuntimeHooks.EntityInitTranspilerCount,
                ["entityDestroyTranspilerCount"] = RuntimeHooks.EntityDestroyTranspilerCount,
                ["entityDestroyGameObjectTranspilerCount"] = RuntimeHooks.EntityDestroyGameObjectTranspilerCount,
                ["deathVisualRetirementCount"] = RuntimeHooks.DeathVisualRetirementCount,
                ["commandExecutedTranspilerCount"] = RuntimeHooks.CommandExecutedTranspilerCount,
                ["commandNextStepTranspilerCount"] = RuntimeHooks.CommandNextStepTranspilerCount,
                ["commandResetTranspilerCount"] = RuntimeHooks.CommandResetTranspilerCount,
                ["commandChainDeletedTranspilerCount"] = RuntimeHooks.CommandChainDeletedTranspilerCount,
            };
            return Serialize(view);
        }

        public static string SerializeInstalledPatchNames(IReadOnlyList<string> patchNames)
        {
            var names = new List<string>();
            if (patchNames != null)
            {
                for (int i = 0; i < patchNames.Count; i++)
                {
                    string name = patchNames[i];
                    if (!string.IsNullOrEmpty(name))
                    {
                        names.Add(name);
                    }
                }
            }

            var view = new Dictionary<string, object>
            {
                ["installedPatchCount"] = names.Count,
                ["installedPatchNames"] = names,
            };
            return Serialize(view);
        }

        public static string SerializeRegisteredProfiles(IReadOnlyList<string> profileIds)
        {
            var ids = new List<string>();
            if (profileIds != null)
            {
                for (int i = 0; i < profileIds.Count; i++)
                {
                    string id = profileIds[i];
                    if (!string.IsNullOrEmpty(id))
                    {
                        ids.Add(id);
                    }
                }
            }

            var view = new Dictionary<string, object>
            {
                ["registeredProfileCount"] = ids.Count,
                ["registeredProfileIds"] = ids,
            };
            return Serialize(view);
        }

        public static string SerializeAppliedBaselineEffects(int entryCount, IReadOnlyList<string> effectNames)
        {
            var names = new List<string>();
            if (effectNames != null)
            {
                for (int i = 0; i < effectNames.Count; i++)
                {
                    string name = effectNames[i];
                    if (!string.IsNullOrEmpty(name))
                    {
                        names.Add(name);
                    }
                }
            }

            var view = new Dictionary<string, object>
            {
                ["appliedBaselineEffectEntryCount"] = entryCount,
                ["appliedBaselineEffectNames"] = names,
            };
            return Serialize(view);
        }

        public static string SerializeCombatXpEntries(
            IReadOnlyList<RuntimeUnitMechanicsDirector.CombatXpEntry> entries)
        {
            var rows = new List<Dictionary<string, object>>();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    RuntimeUnitMechanicsDirector.CombatXpEntry entry = entries[i];
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = entry.EntityKeyId,
                        ["experience"] = entry.Experience,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["combatXpEntryCount"] = rows.Count,
                ["combatXpEntries"] = rows,
            };
            return Serialize(view);
        }

        public static string SerializeBattlefieldCensus(BattlefieldCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.PlayerUnitRows != null)
            {
                foreach (BattlefieldUnitRow row in census.PlayerUnitRows)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["runtimeEntityKeyId"] = row.UniqueEntityId > 0
                            ? row.UniqueEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            : null,
                        ["isBuilding"] = row.IsBuilding,
                        ["currentHealth"] = row.CurrentHealth,
                        ["maxHealth"] = row.MaxHealth,
                        ["healthRatio"] = row.HealthRatio,
                        ["currentArmor"] = row.CurrentArmor,
                        ["currentShield"] = row.CurrentShield,
                        ["currentRank"] = row.CurrentRank,
                        ["isControlledByAi"] = row.IsControlledByAi,
                        ["isControlledByPlayer"] = row.IsControlledByPlayer,
                        ["isAlive"] = row.IsAlive,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["battlefieldEntityCount"] = census != null ? census.BattlefieldEntityCount : 0,
                ["playerUnitCount"] = census != null ? census.PlayerUnitCount : 0,
                ["aiUnitCount"] = census != null ? census.AiUnitCount : 0,
                ["aiBuildingCount"] = census != null ? census.AiBuildingCount : 0,
                ["harvesterCount"] = census != null ? census.HarvesterCount : 0,
                ["playerUnitRowCount"] = rows.Count,
                ["playerUnitRows"] = rows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes a compact live match summary combining the battlefield census,
        /// the AI roster, and the runtime hook state. Readable by an agent without a
        /// visual capture.
        /// </summary>
        public static string SerializeMatchSummary(
            BattlefieldCensus battlefield,
            AiRosterCensus aiRoster,
            bool hooksInstalled,
            int installedHookCount)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "matchSummary",
                ["hooksInstalled"] = hooksInstalled,
                ["installedHookCount"] = installedHookCount,
                ["battlefieldEntityCount"] = battlefield != null ? battlefield.BattlefieldEntityCount : 0,
                ["playerUnitCount"] = battlefield != null ? battlefield.PlayerUnitCount : 0,
                ["aiUnitCount"] = battlefield != null ? battlefield.AiUnitCount : 0,
                ["aiBuildingCount"] = battlefield != null ? battlefield.AiBuildingCount : 0,
                ["harvesterCount"] = battlefield != null ? battlefield.HarvesterCount : 0,
                ["aiRosterUnitCount"] = aiRoster != null ? aiRoster.AiUnitCount : 0,
                ["aiRosterBuildingCount"] = aiRoster != null ? aiRoster.AiBuildingCount : 0,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes a live unit threat assessment (per-unit health ratio, base DPS,
        /// and combined threat score). Readable by an agent without a visual capture.
        /// </summary>
        public static string SerializeUnitThreatAssessment(UnitThreatAssessment assessment)
        {
            var rows = new List<Dictionary<string, object>>();
            if (assessment != null && assessment.Rows != null)
            {
                foreach (UnitThreatRow row in assessment.Rows)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["healthRatio"] = row.HealthRatio,
                        ["baseDps"] = row.BaseDps,
                        ["threatScore"] = row.ThreatScore,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["unitThreatCount"] = rows.Count,
                ["unitThreatRows"] = rows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes aggregated threat-prioritization telemetry collected from all
        /// active runtime behaviours. Readable by an agent without a visual capture.
        /// </summary>
        public static string SerializeThreatPrioritizationCensus(
            int activeBehaviourCount,
            int processedAiUnitCount,
            int processedAlliedUnitCount,
            int lastRetargetedCount,
            int totalRetargetCommandCount)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "threatPrioritizationCensus",
                ["activeBehaviourCount"] = activeBehaviourCount < 0 ? 0 : activeBehaviourCount,
                ["processedAiUnitCount"] = processedAiUnitCount < 0 ? 0 : processedAiUnitCount,
                ["processedAlliedUnitCount"] = processedAlliedUnitCount < 0 ? 0 : processedAlliedUnitCount,
                ["lastRetargetedCount"] = lastRetargetedCount < 0 ? 0 : lastRetargetedCount,
                ["totalRetargetCommandCount"] = totalRetargetCommandCount < 0 ? 0 : totalRetargetCommandCount,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the live player roster enriched with base balance parameters.
        /// </summary>
        public static string SerializePlayerRoster(PlayerRosterCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.PlayerUnitRows != null)
            {
                foreach (PlayerRosterRow row in census.PlayerUnitRows)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["isBuilding"] = row.IsBuilding,
                        ["currentHealth"] = row.CurrentHealth,
                        ["maxHealth"] = row.MaxHealth,
                        ["healthRatio"] = row.HealthRatio,
                        ["cost"] = row.Cost,
                        ["moveSpeed"] = row.MoveSpeed,
                        ["dps"] = row.Dps,
                        ["damage1"] = row.Damage1,
                        ["weaponRange"] = row.WeaponRange,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["playerUnitCount"] = census != null ? census.PlayerUnitCount : 0,
                ["playerUnitRowCount"] = rows.Count,
                ["playerUnitRows"] = rows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the live AI roster (units + buildings) enriched with base balance
        /// parameters, as read from the verified ExistingControllers registry plus the
        /// game's own EntityBalancingStore. Readable by an agent without a visual capture.
        /// </summary>
        public static string SerializeAiRoster(AiRosterCensus census)
        {
            var unitRows = new List<Dictionary<string, object>>();
            var buildingRows = new List<Dictionary<string, object>>();
            if (census != null)
            {
                if (census.AiUnitRows != null)
                {
                    foreach (AiRosterRow row in census.AiUnitRows)
                    {
                        unitRows.Add(BuildAiRosterRow(row));
                    }
                }
                if (census.AiBuildingRows != null)
                {
                    foreach (AiRosterRow row in census.AiBuildingRows)
                    {
                        buildingRows.Add(BuildAiRosterRow(row));
                    }
                }
            }

            var view = new Dictionary<string, object>
            {
                ["aiUnitCount"] = census != null ? census.AiUnitCount : 0,
                ["aiBuildingCount"] = census != null ? census.AiBuildingCount : 0,
                ["aiUnitRowCount"] = unitRows.Count,
                ["aiUnitRows"] = unitRows,
                ["aiBuildingRowCount"] = buildingRows.Count,
                ["aiBuildingRows"] = buildingRows,
            };
            return Serialize(view);
        }

        private static Dictionary<string, object> BuildAiRosterRow(AiRosterRow row)
        {
            return new Dictionary<string, object>
            {
                ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                ["runtimeEntityKeyId"] = row.UniqueEntityId > 0
                    ? row.UniqueEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : null,
                ["isBuilding"] = row.IsBuilding,
                ["cost"] = row.Cost,
                ["maxHealth"] = row.MaxHealth,
                ["moveSpeed"] = row.MoveSpeed,
                ["dps"] = row.Dps,
                ["damage1"] = row.Damage1,
                ["weaponRange"] = row.WeaponRange,
            };
        }

        /// <summary>
        /// Serializes one unit's base balance parameters (read from the game's own
        /// EntityBalancingStore) into a flat, agent-readable JSON object. Filters nothing but an
        /// empty entity id, and preserves the field order below so tail truncation never hides the
        /// core survivability numbers behind cosmetic ones.
        /// </summary>
        public static string SerializeEntityBalanceLookup(EntityBalanceRow row)
        {
            var view = new Dictionary<string, object>
            {
                ["entityId"] = row.EntityId ?? string.Empty,
                ["isBuilding"] = row.IsBuilding,
                ["cost"] = row.Cost,
                ["maxHealth"] = row.MaxHealth,
                ["maxArmor"] = row.MaxArmor,
                ["maxShield"] = row.MaxShield,
                ["maxMana"] = row.MaxMana,
                ["maxRank"] = row.MaxRank,
                ["moveSpeed"] = row.MoveSpeed,
                ["weaponRange"] = row.WeaponRange,
                ["dps"] = row.Dps,
                ["damage1"] = row.Damage1,
                ["damage2"] = row.Damage2,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes one mod-authored card-change request (the data-driven input to
        /// <see cref="RuntimeCardChangeApplier"/>) into a flat, agent-readable JSON
        /// object. Preserves field order so tail truncation never hides the core
        /// survivability delta behind cosmetic filters.
        /// </summary>
        public static string SerializeCardChangeRequest(CardChangeRequest request)
        {
            var view = new Dictionary<string, object>
            {
                ["valueToChange"] = request.ValueToChange ?? string.Empty,
                ["operation"] = request.Operation ?? string.Empty,
                ["value"] = request.Value,
                ["multiplyValueWithCurrentHeatLevel"] = request.MultiplyValueWithCurrentHeatLevel,
                ["multiplyValueWithMetaProgressionMultiplier"] = request.MultiplyValueWithMetaProgressionMultiplier,
                ["side"] = request.Side ?? string.Empty,
                ["changeableValueHasToBeGreaterZero"] = request.ChangeableValueHasToBeGreaterZero,
                ["onlyForTheseEntityIdsCount"] = request.OnlyForTheseEntityIds == null ? 0 : request.OnlyForTheseEntityIds.Length,
                ["entityIdsNotAllowedCount"] = request.EntityIdsNotAllowed == null ? 0 : request.EntityIdsNotAllowed.Length,
                ["sourceId"] = request.SourceId ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the snapshot of compiled AI modules registered in the active registry
        /// into an agent-readable JSON object without visual captures.
        /// </summary>
        public static string SerializeAiModulesCensus(AiModulesCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.ModuleRows != null)
            {
                foreach (AiModuleDiagnosticRow row in census.ModuleRows)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["moduleId"] = row.ModuleId ?? string.Empty,
                        ["apiVersion"] = row.ApiVersion,
                        ["isRegistered"] = row.IsRegistered,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["registeredModuleCount"] = census != null ? census.RegisteredModuleCount : 0,
                ["moduleRowCount"] = rows.Count,
                ["moduleRows"] = rows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes live match economy parameters (player and AI crystal balances,
        /// attack wave number, elapsed game time, harvester count) into an agent-readable JSON object.
        /// </summary>
        public static string SerializeEconomyCensus(EconomyCensus census)
        {
            var view = new Dictionary<string, object>
            {
                ["playerCredits"] = census != null ? census.PlayerCredits : 0,
                ["aiCredits"] = census != null ? census.AiCredits : 0,
                ["attackWaveNumber"] = census != null ? census.AttackWaveNumber : 0,
                ["elapsedGameTimeSeconds"] = census != null ? census.ElapsedGameTimeSeconds : 0f,
                ["harvesterCount"] = census != null ? census.HarvesterCount : 0,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes combat engagement telemetry (units taking recent damage, active target locks,
        /// and active status effects). Omission counters are serialized before detail collections
        /// to ensure bounded trace limits never hide dropped items.
        /// </summary>
        public static string SerializeCombatStatusCensus(CombatStatusCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.CombatActiveUnits != null)
            {
                foreach (CombatEntityStatusRow row in census.CombatActiveUnits)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["isControlledByPlayer"] = row.IsControlledByPlayer,
                        ["timeOfLastDamage"] = row.TimeOfLastDamage,
                        ["isUnderRecentAttack"] = row.IsUnderRecentAttack,
                        ["activeStatusEffectsCount"] = row.ActiveStatusEffectsCount,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["omittedUnitCount"] = census != null ? census.OmittedUnitCount : 0,
                ["totalCombatActiveUnitCount"] = census != null ? census.TotalCombatActiveUnitCount : 0,
                ["unitsWithActiveStatusEffectsCount"] = census != null ? census.UnitsWithActiveStatusEffectsCount : 0,
                ["trackedUnitRowCount"] = rows.Count,
                ["combatActiveUnits"] = rows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the live frame performance census. Omission counters are
        /// serialized before detail collections.
        /// </summary>
        public static string SerializePerformanceCensus(PerformanceCensus census)
        {
            var recommendations = new List<string>();
            if (census != null && census.Recommendations != null)
            {
                recommendations.AddRange(census.Recommendations);
            }

            var view = new Dictionary<string, object>
            {
                ["omittedSampleCount"] = census != null ? census.OmittedSampleCount : 0,
                ["frameCount"] = census != null ? census.FrameCount : 0,
                ["frameTimeMs"] = census != null ? census.FrameTimeMs : 0f,
                ["fps"] = census != null ? census.Fps : 0f,
                ["timeScale"] = census != null ? census.TimeScale : 1f,
                ["targetFrameRate"] = census != null ? census.TargetFrameRate : -1,
                ["gcMemoryMb"] = census != null ? census.GcMemoryMb : 0f,
                ["totalMemoryMb"] = census != null ? census.TotalMemoryMb : 0f,
                ["averageFrameTimeMs"] = census != null ? census.AverageFrameTimeMs : 0f,
                ["unityVersion"] = census != null && census.UnityVersion != null ? census.UnityVersion : string.Empty,
                ["verdict"] = census != null ? census.Verdict.ToString() : PerformanceVerdict.Critical.ToString(),
                ["verdictReason"] = census != null && census.VerdictReason != null ? census.VerdictReason : "performance unavailable",
                ["recommendationCount"] = recommendations.Count,
                ["recommendations"] = recommendations,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes a snapshot diff result. Omission counters are serialized
        /// before detail collections so bounded truncation never hides loss.
        /// </summary>
/// <summary>
        /// Serializes a windowed frame-timing census. Omission counters are placed
        /// before the recommendations list so bounded truncation never hides loss.
        /// </summary>
        public static string SerializeFrameTimingCensus(FrameTimingCensus census)
        {
            var recommendations = new List<string>();
            if (census != null && census.Recommendations != null)
            {
                recommendations.AddRange(census.Recommendations);
            }

            var view = new Dictionary<string, object>
            {
                ["omittedSampleCount"] = census != null ? census.OmittedSampleCount : 0,
                ["sampleCount"] = census != null ? census.SampleCount : 0,
                ["averageFrameTimeMs"] = census != null ? census.AverageFrameTimeMs : 0f,
                ["minFrameTimeMs"] = census != null ? census.MinFrameTimeMs : 0f,
                ["maxFrameTimeMs"] = census != null ? census.MaxFrameTimeMs : 0f,
                ["p95FrameTimeMs"] = census != null ? census.P95FrameTimeMs : 0f,
                ["stutterCount"] = census != null ? census.StutterCount : 0,
                ["hitchCount"] = census != null ? census.HitchCount : 0,
                ["verdict"] = census != null ? census.Verdict.ToString() : FrameTimingVerdict.MajorStutter.ToString(),
                ["verdictReason"] = census != null && census.VerdictReason != null ? census.VerdictReason : "frame timing unavailable",
                ["recommendationCount"] = recommendations.Count,
                ["recommendations"] = recommendations,
            };
            return Serialize(view);
        }

        public static string SerializeSnapshotDiff(SnapshotDiffResult result)
        {
            var added = new List<string>();
            if (result != null && result.AddedKeys != null)
            {
                added.AddRange(result.AddedKeys);
            }

            var removed = new List<string>();
            if (result != null && result.RemovedKeys != null)
            {
                removed.AddRange(result.RemovedKeys);
            }

            var changed = new List<Dictionary<string, object>>();
            if (result != null && result.ChangedKeys != null)
            {
                foreach (SnapshotDiffChange change in result.ChangedKeys)
                {
                    changed.Add(new Dictionary<string, object>
                    {
                        ["key"] = change.Key ?? string.Empty,
                        ["before"] = change.BeforeValue ?? string.Empty,
                        ["after"] = change.AfterValue ?? string.Empty,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["isValid"] = result != null && result.IsValid,
                ["error"] = result != null && result.Error != null ? result.Error : string.Empty,
                ["omittedAddedCount"] = result != null ? result.OmittedAddedCount : 0,
                ["omittedRemovedCount"] = result != null ? result.OmittedRemovedCount : 0,
                ["omittedChangedCount"] = result != null ? result.OmittedChangedCount : 0,
                ["addedKeyCount"] = result != null ? result.AddedKeyCount : 0,
                ["removedKeyCount"] = result != null ? result.RemovedKeyCount : 0,
                ["changedKeyCount"] = result != null ? result.ChangedKeyCount : 0,
                ["totalChangeCount"] = result != null ? result.TotalChangeCount : 0,
                ["addedRowCount"] = result != null ? result.AddedRowCount : 0,
                ["removedRowCount"] = result != null ? result.RemovedRowCount : 0,
                ["changedRowCount"] = result != null ? result.ChangedRowCount : 0,
                ["addedKeys"] = added,
                ["removedKeys"] = removed,
                ["changedKeys"] = changed,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes factory production queue metrics and living factory status across
        /// player and AI battlefield entities. Omission counters are serialized before detail collections.
        /// </summary>
        public static string SerializeProductionCensus(ProductionCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.FactoryRows != null)
            {
                foreach (ProductionFactoryDiagnosticRow row in census.FactoryRows)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["isControlledByPlayer"] = row.IsControlledByPlayer,
                        ["inProductionCount"] = row.InProductionCount,
                        ["isProducing"] = row.IsProducing,
                        ["currentHealth"] = row.CurrentHealth,
                        ["maxHealth"] = row.MaxHealth,
                        ["healthRatio"] = row.HealthRatio,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["omittedFactoryCount"] = census != null ? census.OmittedFactoryCount : 0,
                ["totalFactoryCount"] = census != null ? census.TotalFactoryCount : 0,
                ["activeProducingCount"] = census != null ? census.ActiveProducingCount : 0,
                ["totalQueuedUnitsCount"] = census != null ? census.TotalQueuedUnitsCount : 0,
                ["playerFactoryCount"] = census != null ? census.PlayerFactoryCount : 0,
                ["aiFactoryCount"] = census != null ? census.AiFactoryCount : 0,
                ["factoryRowCount"] = rows.Count,
                ["factoryRows"] = rows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes player command HQ and enemy base building survivability metrics.
        /// Omission counters are serialized before detail collections.
        /// </summary>
        public static string SerializeBaseIntegrityCensus(BaseIntegrityCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.StructureRows != null)
            {
                foreach (BaseStructureDiagnosticRow row in census.StructureRows)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["isControlledByPlayer"] = row.IsControlledByPlayer,
                        ["currentHealth"] = row.CurrentHealth,
                        ["maxHealth"] = row.MaxHealth,
                        ["healthRatio"] = row.HealthRatio,
                        ["isEngineeringHq"] = row.IsEngineeringHq,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["omittedStructureCount"] = census != null ? census.OmittedStructureCount : 0,
                ["hasPlayerHq"] = census != null && census.HasPlayerHq,
                ["playerHqHealth"] = census != null ? census.PlayerHqHealth : 0f,
                ["playerHqMaxHealth"] = census != null ? census.PlayerHqMaxHealth : 0f,
                ["playerHqHealthRatio"] = census != null ? census.PlayerHqHealthRatio : 0f,
                ["livingAiBuildingCount"] = census != null ? census.LivingAiBuildingCount : 0,
                ["averageAiStructureHealthRatio"] = census != null ? census.AverageAiStructureHealthRatio : 0f,
                ["structureRowCount"] = rows.Count,
                ["structureRows"] = rows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of an in-game factory production command triggered via MCP/diagnostics.
        /// </summary>
        public static string SerializeProductionOrderResult(ProductionOrderResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["success"] = result.Success,
                ["factoryKeyId"] = result.FactoryKeyId ?? string.Empty,
                ["inProductionCount"] = result.InProductionCount,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of a native selected-blueprint placement preview or commit.
        /// </summary>
        public static string SerializeBuildingPlacementCommandResult(BuildingPlacementCommandResult result)
        {
            return Serialize(BuildingPlacementCommandView(result));
        }

        /// <summary>
        /// Serializes a bounded, read-only nearby placement search and its best candidate diagnostics.
        /// </summary>
        public static string SerializeBuildingPlacementSearchResult(BuildingPlacementSearchResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["omittedCellDiagnosticCount"] = result.Candidate.OmittedCellDiagnosticCount,
                ["found"] = result.Found,
                ["originX"] = result.OriginX,
                ["originZ"] = result.OriginZ,
                ["radiusCells"] = result.RadiusCells,
                ["candidatesChecked"] = result.CandidatesChecked,
                ["message"] = result.Message ?? string.Empty,
                ["candidate"] = BuildingPlacementCommandView(result.Candidate),
            };
            return Serialize(view);
        }

        private static Dictionary<string, object> BuildingPlacementCommandView(BuildingPlacementCommandResult result)
        {
            const int maxRows = 16;
            const int maxOccupantRowsPerCell = 8;
            IReadOnlyList<BuildingPlacementCellDiagnostic> diagnostics = result.CellDiagnostics;
            int diagnosticCount = diagnostics != null ? diagnostics.Count : 0;
            int rowCount = Math.Min(diagnosticCount, maxRows);
            int omittedCount = result.OmittedCellDiagnosticCount + Math.Max(0, diagnosticCount - rowCount);
            int omittedOccupantEntityCount = 0;
            var cellRows = new List<Dictionary<string, object>>(rowCount);
            for (int index = 0; index < rowCount; index++)
            {
                BuildingPlacementCellDiagnostic cell = diagnostics[index];
                var reasons = new List<string>();
                if (cell.BlockingReasons != null)
                {
                    for (int reasonIndex = 0; reasonIndex < cell.BlockingReasons.Count; reasonIndex++)
                    {
                        string reason = cell.BlockingReasons[reasonIndex];
                        if (!string.IsNullOrEmpty(reason))
                        {
                            reasons.Add(reason);
                        }
                    }
                }

                IReadOnlyList<BuildingPlacementOccupantDiagnostic> occupants = cell.OccupantEntities;
                int occupantCount = occupants != null ? occupants.Count : 0;
                int occupantRowCount = Math.Min(occupantCount, maxOccupantRowsPerCell);
                omittedOccupantEntityCount += Math.Max(0, occupantCount - occupantRowCount);
                var occupantRows = new List<Dictionary<string, object>>(occupantRowCount);
                for (int occupantIndex = 0; occupantIndex < occupantRowCount; occupantIndex++)
                {
                    BuildingPlacementOccupantDiagnostic occupant = occupants[occupantIndex];
                    occupantRows.Add(new Dictionary<string, object>
                    {
                        ["entityId"] = occupant.EntityId ?? string.Empty,
                        ["entityKeyId"] = occupant.EntityKeyId ?? string.Empty,
                        ["uniqueEntityId"] = occupant.UniqueEntityId,
                        ["gameObjectName"] = occupant.GameObjectName ?? string.Empty,
                        ["isBuilding"] = occupant.IsBuilding,
                        ["isUnit"] = occupant.IsUnit,
                        ["gridSize"] = occupant.GridSize,
                    });
                }

                cellRows.Add(new Dictionary<string, object>
                {
                    ["gridX"] = cell.GridX,
                    ["gridY"] = cell.GridY,
                    ["worldObject"] = cell.WorldObject ?? string.Empty,
                    ["heightLayer"] = cell.HeightLayer ?? string.Empty,
                    ["withinGrid"] = cell.WithinGrid,
                    ["ground"] = cell.Ground,
                    ["obstacle"] = cell.Obstacle,
                    ["building"] = cell.Building,
                    ["ramp"] = cell.Ramp,
                    ["crystal"] = cell.Crystal,
                    ["unit"] = cell.Unit,
                    ["unitBlocksPlacement"] = cell.UnitBlocksPlacement,
                    ["multiLayer"] = cell.MultiLayer,
                    ["buildingInProgress"] = cell.BuildingInProgress,
                    ["secondaryFogOccluded"] = cell.SecondaryFogOccluded,
                    ["sameHeightLayerAsFootprint"] = cell.SameHeightLayerAsFootprint,
                    ["occupantEntities"] = occupantRows,
                    ["blockingReasons"] = reasons,
                });
            }

            return new Dictionary<string, object>
            {
                ["omittedCellDiagnosticCount"] = omittedCount,
                ["omittedOccupantEntityCount"] = omittedOccupantEntityCount,
                ["success"] = result.Success,
                ["committed"] = result.Committed,
                ["entityId"] = result.EntityId ?? string.Empty,
                ["cost"] = result.Cost,
                ["balanceBefore"] = result.BalanceBefore,
                ["worldX"] = result.WorldX,
                ["worldY"] = result.WorldY,
                ["worldZ"] = result.WorldZ,
                ["cellCount"] = result.CellCount,
                ["cellDiagnosticCount"] = rowCount,
                ["method"] = "native-PlaceBuildings-coroutine",
                ["message"] = result.Message ?? string.Empty,
                ["cellDiagnostics"] = cellRows,
            };
        }

        /// <summary>
        /// Serializes live match run progression, ascension/heat scaling, active relics,
        /// and offered blueprint reward pools. Omission counters are serialized before detail collections.
        /// </summary>
        public static string SerializeRunProgressionCensus(RunProgressionCensus census)
        {
            var relicList = new List<string>();
            if (census != null && census.ActiveRelicIds != null)
            {
                foreach (string id in census.ActiveRelicIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        relicList.Add(id);
                    }
                }
            }

            var blueprintList = new List<string>();
            if (census != null && census.OfferedBlueprintPoolIds != null)
            {
                foreach (string id in census.OfferedBlueprintPoolIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        blueprintList.Add(id);
                    }
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "runProgressionCensus",
                ["omittedRelicCount"] = census != null ? census.OmittedRelicCount : 0,
                ["omittedBlueprintCount"] = census != null ? census.OmittedBlueprintCount : 0,
                ["activeSceneName"] = census != null ? census.ActiveSceneName : string.Empty,
                ["currentLevel"] = census != null ? census.CurrentLevel : 0,
                ["currentStage"] = census != null ? census.CurrentStage : 0,
                ["isLastLevel"] = census != null && census.IsLastLevel,
                ["isFirstLevel"] = census != null && census.IsFirstLevel,
                ["ascensionLevel"] = census != null ? census.AscensionLevel : 0,
                ["heatLevel"] = census != null ? census.HeatLevel : 0,
                ["ascensionAndHeatString"] = census != null ? census.AscensionAndHeatString : string.Empty,
                ["progressionMode"] = census != null ? census.ProgressionMode : string.Empty,
                ["totalRelicCount"] = census != null ? census.TotalRelicCount : 0,
                ["totalBlueprintRewardCount"] = census != null ? census.TotalBlueprintRewardCount : 0,
                ["cardsInDeckCount"] = census != null ? census.CardsInDeckCount : 0,
                ["relicCount"] = relicList.Count,
                ["activeRelicIds"] = relicList,
                ["offeredBlueprintRewardCount"] = blueprintList.Count,
                ["offeredBlueprintPoolIds"] = blueprintList,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes stable StageMap identity and configured/live AI profile evidence separately.
        /// Omission counters precede bounded profile collections.
        /// </summary>
        public static string SerializeActiveStageAiAssociation(
            ActiveStageAiAssociationDiagnostic stage,
            LoaderDiagnosticsSnapshot loaderSnapshot,
            int maxProfiles = 32)
        {
            return SerializeActiveStageAiAssociation(stage, loaderSnapshot, null, maxProfiles);
        }

        /// <summary>
        /// Serializes stable StageMap identity, its bounded configured stage sequence, and
        /// configured/live AI profile evidence separately. Omission counters precede rows.
        /// </summary>
        public static string SerializeActiveStageAiAssociation(
            ActiveStageAiAssociationDiagnostic stage,
            LoaderDiagnosticsSnapshot loaderSnapshot,
            StageAiAssociationSequenceDiagnostic stageSequence,
            int maxProfiles = 32)
        {
            int limit = Math.Max(1, Math.Min(64, maxProfiles));
            var configuredProfileIds = new List<string>();
            int configuredProfileCount = stage != null && stage.DeclaredAiProfileIds != null
                ? stage.DeclaredAiProfileIds.Count
                : 0;
            for (int i = 0; stage != null && stage.DeclaredAiProfileIds != null &&
                i < stage.DeclaredAiProfileIds.Count && configuredProfileIds.Count < limit; i++)
            {
                string profileId = stage.DeclaredAiProfileIds[i];
                if (!String.IsNullOrWhiteSpace(profileId))
                {
                    configuredProfileIds.Add(profileId);
                }
            }

            var runtimeBoundProfiles = new List<Dictionary<string, object>>();
            int runtimeBoundProfileCount = 0;
            int runtimeBoundInstanceCount = 0;
            IReadOnlyList<AiProfileDiagnosticReport> profiles = loaderSnapshot != null
                ? loaderSnapshot.AiProfiles
                : null;
            if (profiles != null)
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    AiProfileDiagnosticReport profile = profiles[i];
                    if (profile == null || profile.ActiveAiInstances <= 0)
                    {
                        continue;
                    }

                    runtimeBoundProfileCount++;
                    runtimeBoundInstanceCount += profile.ActiveAiInstances;
                    if (runtimeBoundProfiles.Count < limit)
                    {
                        runtimeBoundProfiles.Add(new Dictionary<string, object>
                        {
                            ["profileId"] = profile.ProfileId ?? string.Empty,
                            ["packId"] = profile.PackId ?? string.Empty,
                            ["activeAiInstances"] = profile.ActiveAiInstances,
                        });
                    }
                }
            }

            var stageSequenceRows = new List<Dictionary<string, object>>();
            int totalStageSequenceCount = stageSequence != null ? stageSequence.TotalStageCount : 0;
            IReadOnlyList<StageAiAssociationSequenceEntry> stageSequenceEntries = stageSequence != null
                ? stageSequence.Entries
                : null;
            int stageSequenceLimit = Math.Max(1, Math.Min(64, limit));
            bool hasUnresolvedStageIdentity = false;
            if (stageSequenceEntries != null)
            {
                for (int i = 0; i < stageSequenceEntries.Count && stageSequenceRows.Count < stageSequenceLimit; i++)
                {
                    StageAiAssociationSequenceEntry entry = stageSequenceEntries[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    int profileLimit = Math.Min(16, limit);
                    int profileCount = entry.DeclaredAiProfileIds != null
                        ? entry.DeclaredAiProfileIds.Count
                        : 0;
                    var rowProfileIds = new List<string>();
                    for (int profileIndex = 0; entry.DeclaredAiProfileIds != null &&
                        profileIndex < entry.DeclaredAiProfileIds.Count && rowProfileIds.Count < profileLimit; profileIndex++)
                    {
                        string profileId = entry.DeclaredAiProfileIds[profileIndex];
                        if (!String.IsNullOrWhiteSpace(profileId))
                        {
                            rowProfileIds.Add(profileId);
                        }
                    }

                    bool rowIdentityResolved = entry.StageIdentityResolved;
                    hasUnresolvedStageIdentity |= !rowIdentityResolved;
                    stageSequenceRows.Add(new Dictionary<string, object>
                    {
                        ["stageMapIndex"] = entry.StageMapIndex,
                        ["isActiveStageMapIndex"] = stageSequence.ActiveStageMapIndex == entry.StageMapIndex,
                        ["stageIdentityStatus"] = rowIdentityResolved ? "resolved" : "unknown",
                        ["stageKind"] = entry.StageKind ?? string.Empty,
                        ["stageId"] = entry.StageId ?? string.Empty,
                        ["packId"] = entry.PackId ?? string.Empty,
                        ["qualifiedStageId"] = rowIdentityResolved
                            ? (!String.IsNullOrWhiteSpace(entry.PackId)
                                ? entry.PackId + ":" + entry.StageId
                                : (String.Equals(entry.StageKind, "native", StringComparison.Ordinal)
                                    ? "native:" + entry.StageId
                                    : entry.StageId))
                            : string.Empty,
                        ["identityFailureReason"] = entry.FailureReason ?? string.Empty,
                        ["declaredAssociationStatus"] = !rowIdentityResolved
                            ? "unknown"
                            : (profileCount > 0 ? "declared" : "none_declared"),
                        ["declaredAiProfileCount"] = profileCount,
                        ["omittedDeclaredAiProfileCount"] = Math.Max(0, profileCount - rowProfileIds.Count),
                        ["declaredAiProfileIds"] = rowProfileIds,
                    });
                }
            }

            int omittedStageSequenceCount = Math.Max(
                stageSequence != null ? stageSequence.OmittedStageCount : 0,
                Math.Max(0, totalStageSequenceCount - stageSequenceRows.Count));
            string stageSequenceStatus = stageSequence == null ||
                String.Equals(stageSequence.Status, "unavailable", StringComparison.Ordinal)
                ? "unavailable"
                : (omittedStageSequenceCount == 0 && !hasUnresolvedStageIdentity &&
                    String.Equals(stageSequence.Status, "resolved", StringComparison.Ordinal)
                    ? "resolved"
                    : "partial");
            string stageSequenceFailureReason = stageSequence != null
                ? stageSequence.FailureReason ?? string.Empty
                : "stage_identity_provider_unavailable";
            if (String.IsNullOrWhiteSpace(stageSequenceFailureReason) && omittedStageSequenceCount > 0)
            {
                stageSequenceFailureReason = "stage_sequence_truncated";
            }
            else if (String.IsNullOrWhiteSpace(stageSequenceFailureReason) && hasUnresolvedStageIdentity)
            {
                stageSequenceFailureReason = "stage_identity_unresolved";
            }

            bool identityResolved = stage != null && stage.StageIdentityResolved;
            var view = new Dictionary<string, object>
            {
                ["kind"] = "activeStageAiAssociation",
                ["omittedConfiguredAiProfileCount"] = Math.Max(0, configuredProfileCount - configuredProfileIds.Count),
                ["omittedRuntimeBoundProfileCount"] = Math.Max(0, runtimeBoundProfileCount - runtimeBoundProfiles.Count),
                ["omittedStageSequenceCount"] = omittedStageSequenceCount,
                ["stageSequenceRowCount"] = stageSequenceRows.Count,
                ["totalStageSequenceCount"] = totalStageSequenceCount,
                ["stageSequenceStatus"] = stageSequenceStatus,
                ["stageSequenceIdentitySource"] = stageSequence != null ? stageSequence.IdentitySource : "unknown",
                ["stageSequenceActiveIndex"] = stageSequence != null ? stageSequence.ActiveStageMapIndex : -1,
                ["stageSequenceFailureReason"] = stageSequenceFailureReason,
                ["stageIdentityStatus"] = identityResolved ? "resolved" : "unknown",
                ["stageMapIndex"] = stage != null ? stage.StageMapIndex : -1,
                ["stageKind"] = stage != null ? stage.StageKind : string.Empty,
                ["stageId"] = stage != null ? stage.StageId : string.Empty,
                ["packId"] = stage != null ? stage.PackId : string.Empty,
                ["qualifiedStageId"] = identityResolved
                    ? (!String.IsNullOrWhiteSpace(stage.PackId)
                        ? stage.PackId + ":" + stage.StageId
                        : (String.Equals(stage.StageKind, "native", StringComparison.Ordinal)
                            ? "native:" + stage.StageId
                            : stage.StageId))
                    : string.Empty,
                ["identitySource"] = stage != null ? stage.IdentitySource : "unknown",
                ["identityFailureReason"] = stage != null ? stage.FailureReason : "stage_identity_provider_unavailable",
                ["declaredAssociationStatus"] = !identityResolved
                    ? "unknown"
                    : (configuredProfileCount > 0 ? "declared" : "none_declared"),
                ["declaredAiProfileCount"] = configuredProfileCount,
                ["declaredAiProfileIds"] = configuredProfileIds,
                ["runtimeBindingStatus"] = profiles == null
                    ? "unavailable"
                    : (runtimeBoundProfileCount > 0 ? "bound" : "none_bound"),
                ["runtimeBoundAiProfileCount"] = runtimeBoundProfileCount,
                ["runtimeBoundAiInstanceCount"] = runtimeBoundInstanceCount,
                ["runtimeBoundAiProfiles"] = runtimeBoundProfiles,
                ["evidenceNote"] = "stageSequenceRows are configured identities from the captured StageMap order. Stage-declared profile IDs are configuration; runtimeBoundAiProfiles count initialized loader behaviours and do not prove tactical execution.",
                ["stageSequenceRows"] = stageSequenceRows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the intermission level-select census. Field order is fixed
        /// by contract: counters (omittedNodeCount, nodeCount) come BEFORE the
        /// detail list (nodes) so bounded truncation never hides loss counts.
        /// </summary>
        public static string SerializeIntermissionCensus(IntermissionCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.Nodes != null)
            {
                foreach (IntermissionNodeRow row in census.Nodes)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["level"] = row.Level,
                        ["field"] = row.Field,
                        ["isChoosable"] = row.IsChoosable,
                        ["isCurrentNodeOfPath"] = row.IsCurrentNodeOfPath,
                        ["hasCardReward"] = row.HasCardReward,
                        ["position"] = row.Position ?? string.Empty,
                        ["instanceId"] = row.InstanceId,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "intermissionCensus",
                ["isIntermissionActive"] = census != null && census.IsIntermissionActive,
                ["omittedNodeCount"] = census != null ? census.OmittedNodeCount : 0,
                ["nodeCount"] = census != null ? census.NodeCount : 0,
                ["currentStage"] = census != null ? census.CurrentStage : 0,
                ["currentLevel"] = census != null ? census.CurrentLevel : 0,
                ["nodes"] = rows,
            };
            return Serialize(view);
        }

        public static string SerializeEnterLevelNodeResult(EnterLevelNodeResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["success"] = result.Success,
                ["message"] = result.Message ?? string.Empty,
                ["level"] = result.Level,
                ["field"] = result.Field,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the synthetic stage-jump result. The synthetic outcome
        /// comes first and is always labeled, so the result can never be read
        /// as natural progression evidence.
        /// </summary>
        public static string SerializeSyntheticJumpResult(SyntheticJumpResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "syntheticJumpResult",
                ["success"] = result.Success,
                ["synthetic"] = result.Synthetic,
                ["method"] = result.Method ?? string.Empty,
                ["level"] = result.Level,
                ["finished"] = result.Finished,
                ["beforeStage"] = result.BeforeStage,
                ["beforeLevel"] = result.BeforeLevel,
                ["afterStage"] = result.AfterStage,
                ["afterLevel"] = result.AfterLevel,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the coordinate-click result. The clicked outcome comes
        /// before detail so bounded truncation never hides it.
        /// </summary>
        public static string SerializeUiClickResult(UiClickResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "uiClickResult",
                ["clicked"] = result != null && result.Clicked,
                ["via"] = result != null ? result.Via ?? string.Empty : string.Empty,
                ["targetName"] = result != null ? result.TargetName ?? string.Empty : string.Empty,
                ["topHitName"] = result != null ? result.TopHitName ?? string.Empty : string.Empty,
                ["screenX"] = result != null ? result.ScreenX : 0f,
                ["screenY"] = result != null ? result.ScreenY : 0f,
                ["phases"] = result != null ? result.Phases ?? Array.Empty<string>() : Array.Empty<string>(),
                ["message"] = result != null ? result.Message ?? string.Empty : string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes a bounded primitive UI-accessibility payload with the
        /// same dependency-free JSON writer used by the other diagnostics.
        /// </summary>
        public static string SerializeUiAccessibilityPayload(Dictionary<string, object> payload)
        {
            return Serialize(payload ?? new Dictionary<string, object>());
        }

        /// <summary>
        /// Serializes a same-thread control-surface snapshot. Intermission detail is
        /// included only when the active scene is an intermission scene; each captured
        /// section is a plain bounded JSON object and may independently carry an error.
        /// </summary>
        public static string SerializeRuntimeControlSurface(
            string activeSceneName,
            int frameCount,
            int maxUiResults,
            Dictionary<string, object> uiCensus,
            Dictionary<string, object> runProgression,
            Dictionary<string, object> intermissionCensus,
            Dictionary<string, object> intermissionGraph,
            Dictionary<string, object> intermissionRewardState,
            Dictionary<string, object> navigatorState,
            Dictionary<string, object> nativePanelState = null)
        {
            string sceneName = activeSceneName ?? string.Empty;
            bool intermissionActive = sceneName.IndexOf("Intermission", StringComparison.OrdinalIgnoreCase) >= 0;
            Dictionary<string, object> nativePanels = nativePanelState
                ?? new Dictionary<string, object> { ["error"] = "section_not_captured" };
            var intermission = new Dictionary<string, object>
            {
                ["included"] = intermissionActive,
            };
            if (intermissionActive)
            {
                intermission["runProgression"] = runProgression ?? new Dictionary<string, object> { ["error"] = "section_not_captured" };
                intermission["nodeCensus"] = intermissionCensus ?? new Dictionary<string, object> { ["error"] = "section_not_captured" };
                intermission["graphAndRoutes"] = intermissionGraph ?? new Dictionary<string, object> { ["error"] = "section_not_captured" };
                intermission["rewardAndDeckState"] = intermissionRewardState ?? new Dictionary<string, object> { ["error"] = "section_not_captured" };
                intermission["navigator"] = navigatorState ?? new Dictionary<string, object> { ["error"] = "section_not_captured" };
                intermission["nativePanels"] = nativePanels;
            }
            else
            {
                intermission["reason"] = "active_scene_is_not_intermission";
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "runtimeControlSurface",
                ["consistency"] = "singleUnityThread",
                ["activeSceneName"] = sceneName,
                ["frameCount"] = frameCount,
                ["maxUiResults"] = Math.Max(1, Math.Min(100, maxUiResults)),
                ["uiCensus"] = uiCensus ?? new Dictionary<string, object> { ["error"] = "section_not_captured" },
                ["nativePanelState"] = nativePanels,
                ["isIntermissionActive"] = intermissionActive,
                ["intermission"] = intermission,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the intermission node-selection result. Status fields come
        /// before the reason detail so bounded truncation never hides the outcome.
        /// </summary>
        public static string SerializeIntermissionSelectionResult(IntermissionNodeSelection result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "intermissionNodeSelection",
                ["selected"] = result.Selected,
                ["level"] = result.Level,
                ["field"] = result.Field,
                ["instanceId"] = result.InstanceId,
                ["reason"] = result.Reason ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the intermission level-entry result. Status counters
        /// (success/selected/entered) come before the message detail, matching
        /// the omission-counter-first ordering used by the censuses.
        /// </summary>
        public static string SerializeIntermissionLevelEntryResult(IntermissionLevelEntry result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "intermissionLevelEntry",
                ["success"] = result.Success,
                ["selected"] = result.Selected,
                ["entered"] = result.Entered,
                ["level"] = result.Level,
                ["field"] = result.Field,
                ["instanceId"] = result.InstanceId,
                ["nodeFlowKind"] = result.NodeFlowKind ?? "Unknown",
                ["nativeCallInvoked"] = result.NativeCallInvoked,
                ["effectObserved"] = result.EffectObserved,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        public static string SerializeNavigatorState(NavigatorStateSnapshot state)
        {
            var view = new Dictionary<string, object>
            {
                ["isPaused"] = state.IsPaused,
                ["isDebugPaused"] = state.IsDebugPaused,
                ["timeScale"] = state.TimeScale,
                ["scaledTime"] = state.ScaledTime,
                ["unscaledTime"] = state.UnscaledTime,
            };
            return Serialize(view);
        }

        public static string SerializeNavigatorPauseResult(NavigatorPauseResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["success"] = result.Success,
                ["message"] = result.Message ?? string.Empty,
                ["isPaused"] = result.IsPaused,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the deep LevelMap route state. The route scalars come
        /// before the boss detail so bounded diagnostic truncation can never
        /// suppress the choosable frontier signal.
        /// </summary>
        public static string SerializeLevelMapRouteState(IntermissionRouteState state)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "levelMapRouteState",
                ["isRouteAvailable"] = state.IsRouteAvailable,
                ["currentLevel"] = state.CurrentLevel,
                ["currentField"] = state.CurrentField,
                ["choosableFieldCount"] = state.ChoosableFieldCount,
                ["totalFieldCount"] = state.TotalFieldCount,
                ["chosenPathCount"] = state.ChosenPathCount,
                ["isCurrentLevelFinished"] = state.IsCurrentLevelFinished,
                ["isFirstLevel"] = state.IsFirstLevel,
                ["isLastLevelBeforeBoss"] = state.IsLastLevelBeforeBoss,
                ["isBoss"] = state.IsBoss,
                ["levelCount"] = state.LevelCount,
                ["maxWidth"] = state.MaxWidth,
                ["firstPossibleNextField"] = state.FirstPossibleNextField,
                ["bossAiPrefabName"] = state.BossAiPrefabName ?? string.Empty,
                ["bossWorldPrefabName"] = state.BossWorldPrefabName ?? string.Empty,
                ["bossMapName"] = state.BossMapName ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes a single intermission node inspection. The found /
        /// choosable flags come before the reward detail for the same
        /// truncation-safety reason as the other intermission views.
        /// </summary>
        public static string SerializeIntermissionNodeDetail(IntermissionNodeDetail detail)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "intermissionNodeDetail",
                ["found"] = detail.Found,
                ["level"] = detail.Level,
                ["field"] = detail.Field,
                ["isChoosable"] = detail.IsChoosable,
                ["isBoss"] = detail.IsBoss,
                ["isRestSite"] = detail.IsRestSite,
                ["isShop"] = detail.IsShop,
                ["hasCardReward"] = detail.HasCardReward,
                ["hasUpgradeReward"] = detail.HasUpgradeReward,
                ["hasRelicReward"] = detail.HasRelicReward,
                ["hasResearchReward"] = detail.HasResearchReward,
                ["hasCoinReward"] = detail.HasCoinReward,
                ["hasStartCrystalReward"] = detail.HasStartCrystalReward,
                ["hasResearchPointsReward"] = detail.HasResearchPointsReward,
                ["hasDropReward"] = detail.HasDropReward,
                ["blueprintPoolId"] = detail.BlueprintPoolId ?? string.Empty,
                ["outgoingConnectionCount"] = detail.OutgoingConnectionCount,
                ["incomingConnectionCount"] = detail.IncomingConnectionCount,
                ["fieldCountInLevel"] = detail.FieldCountInLevel,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the full intermission LevelMap graph and forward route planner.
        /// Omission counters precede detail collections by contract.
        /// </summary>
        public static string SerializeIntermissionGraph(IntermissionGraphSnapshot graph)
        {
            IntermissionGraphSnapshot g = graph ?? IntermissionGraphSnapshot.Unavailable;
            var nodeRows = new List<Dictionary<string, object>>();
            if (g.Nodes != null)
            {
                for (int i = 0; i < g.Nodes.Count; i++)
                {
                    IntermissionGraphNode n = g.Nodes[i];
                    nodeRows.Add(new Dictionary<string, object>
                    {
                        ["level"] = n.Level,
                        ["field"] = n.Field,
                        ["rewardType"] = n.RewardType ?? "Combat",
                        ["blueprintPoolId"] = n.BlueprintPoolId ?? string.Empty,
                        ["isChoosable"] = n.IsChoosable,
                        ["isVisited"] = n.IsVisited,
                        ["toFields"] = n.ToFields != null ? (object)n.ToFields : Array.Empty<int>(),
                    });
                }
            }

            var routeRows = new List<Dictionary<string, object>>();
            if (g.Routes != null)
            {
                for (int i = 0; i < g.Routes.Count; i++)
                {
                    IntermissionRouteOption r = g.Routes[i];
                    routeRows.Add(new Dictionary<string, object>
                    {
                        ["rank"] = r.Rank,
                        ["score"] = r.Score,
                        ["matchedPreferredCount"] = r.MatchedPreferredCount,
                        ["totalRewardNodeCount"] = r.TotalRewardNodeCount,
                        ["steps"] = r.Steps != null ? (object)r.Steps : Array.Empty<string>(),
                        ["rewardTypes"] = r.RewardTypes != null ? (object)r.RewardTypes : Array.Empty<string>(),
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "intermissionGraph",
                ["isAvailable"] = g.IsAvailable,
                ["omittedNodeCount"] = g.OmittedNodeCount,
                ["omittedRouteCount"] = g.OmittedRouteCount,
                ["currentLevel"] = g.CurrentLevel,
                ["currentField"] = g.CurrentField,
                ["levelCount"] = g.LevelCount,
                ["isLastLevelBeforeBoss"] = g.IsLastLevelBeforeBoss,
                ["preferredRewardType"] = g.PreferredRewardType ?? string.Empty,
                ["nodeCount"] = g.NodeCount,
                ["routeCount"] = g.RouteCount,
                ["nodes"] = nodeRows,
                ["routes"] = routeRows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the intermission reward, currency, deck, and sub-screen state.
        /// Omission counters precede detail collections by contract.
        /// </summary>
        public static string SerializeIntermissionRewardState(IntermissionRewardState state)
        {
            IntermissionRewardState s = state ?? IntermissionRewardState.Unavailable;
            var offeredRows = new List<Dictionary<string, object>>();
            if (s.OfferedCards != null)
            {
                for (int i = 0; i < s.OfferedCards.Count; i++)
                {
                    IntermissionOfferedCardRow c = s.OfferedCards[i];
                    offeredRows.Add(new Dictionary<string, object>
                    {
                        ["slotIndex"] = c.SlotIndex,
                        ["cardKind"] = c.CardKind ?? string.Empty,
                        ["id"] = c.Id ?? string.Empty,
                        ["productId"] = c.ProductId ?? string.Empty,
                        ["name"] = c.Name ?? string.Empty,
                        ["instanceId"] = c.InstanceId,
                    });
                }
            }

            var deckRows = new List<Dictionary<string, object>>();
            if (s.DeckCards != null)
            {
                for (int i = 0; i < s.DeckCards.Count; i++)
                {
                    IntermissionDeckCardRow d = s.DeckCards[i];
                    deckRows.Add(new Dictionary<string, object>
                    {
                        ["cardId"] = d.CardId ?? string.Empty,
                        ["productId"] = d.ProductId ?? string.Empty,
                        ["upgradeCount"] = d.UpgradeCount,
                        ["isViableForCurrentUpgrade"] = d.IsViableForCurrentUpgrade,
                        ["upgrades"] = d.Upgrades != null ? (object)d.Upgrades : Array.Empty<string>(),
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "intermissionRewardState",
                ["isAvailable"] = s.IsAvailable,
                ["omittedOfferedCardCount"] = s.OmittedOfferedCardCount,
                ["omittedDeckCardCount"] = s.OmittedDeckCardCount,
                ["activeSceneName"] = s.ActiveSceneName ?? string.Empty,
                ["navigatorSceneState"] = s.NavigatorSceneState ?? string.Empty,
                ["playerHasOpenRewards"] = s.PlayerHasOpenRewards,
                ["currentCoins"] = s.CurrentCoins,
                ["currentStartCrystals"] = s.CurrentStartCrystals,
                ["currentDropPodCount"] = s.CurrentDropPodCount,
                ["currentRerollCount"] = s.CurrentRerollCount,
                ["currentResearchPoints"] = s.CurrentResearchPoints,
                ["maxResearchPoints"] = s.MaxResearchPoints,
                ["openEngineerRewardCount"] = s.OpenEngineerRewardCount,
                ["openEconomyRewardCount"] = s.OpenEconomyRewardCount,
                ["openSpecialistRewardCount"] = s.OpenSpecialistRewardCount,
                ["openSpecialistRelicRewardCount"] = s.OpenSpecialistRelicRewardCount,
                ["openCardRewardCount"] = s.OpenCardRewardCount,
                ["openRelicRewardCount"] = s.OpenRelicRewardCount,
                ["openUpgradeRewardCount"] = s.OpenUpgradeRewardCount,
                ["openResearchRewardCount"] = s.OpenResearchRewardCount,
                ["isChooseCardActive"] = s.IsChooseCardActive,
                ["activeRewardCategory"] = s.ActiveRewardCategory ?? "None",
                ["isClickableNow"] = s.IsClickableNow,
                ["canSkip"] = s.CanSkip,
                ["canReroll"] = s.CanReroll,
                ["isDeckOverviewOpen"] = s.IsDeckOverviewOpen,
                ["isAssignUpgradeActive"] = s.IsAssignUpgradeActive,
                ["currentUpgradeId"] = s.CurrentUpgradeId ?? string.Empty,
                ["isRemoveCardActive"] = s.IsRemoveCardActive,
                ["removeCardCoinCompensation"] = s.RemoveCardCoinCompensation,
                ["offeredCardCount"] = s.OfferedCardCount,
                ["deckCardCount"] = s.DeckCardCount,
                ["offeredCards"] = offeredRows,
                ["deckCards"] = deckRows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the native Shop and RemoveCardView snapshot. Omission and
        /// unsupported-row counts precede their bounded detail collections.
        /// </summary>
        public static string SerializeIntermissionNativePanelState(IntermissionNativePanelState state)
        {
            return Serialize(BuildIntermissionNativePanelView(state ?? IntermissionNativePanelState.Unavailable));
        }

        private static Dictionary<string, object> BuildIntermissionNativePanelView(IntermissionNativePanelState s)
        {
            var shopRows = new List<Dictionary<string, object>>();
            if (s.ShopOffers != null)
            {
                for (int i = 0; i < s.ShopOffers.Count; i++)
                {
                    IntermissionShopOfferRow offer = s.ShopOffers[i];
                    shopRows.Add(new Dictionary<string, object>
                    {
                        ["offerId"] = offer.OfferId ?? string.Empty,
                        ["kind"] = offer.Kind ?? string.Empty,
                        ["nativeId"] = offer.NativeId ?? string.Empty,
                        ["name"] = offer.Name ?? string.Empty,
                        ["priceText"] = offer.PriceText ?? string.Empty,
                        ["instanceId"] = offer.InstanceId,
                        ["hasPrice"] = offer.HasPrice,
                        ["price"] = offer.Price,
                    });
                }
            }

            var removalRows = new List<Dictionary<string, object>>();
            if (s.RemovalCards != null)
            {
                for (int i = 0; i < s.RemovalCards.Count; i++)
                {
                    IntermissionRemovalCardRow card = s.RemovalCards[i];
                    removalRows.Add(new Dictionary<string, object>
                    {
                        ["slotIndex"] = card.SlotIndex,
                        ["entityId"] = card.EntityId ?? string.Empty,
                        ["productId"] = card.ProductId ?? string.Empty,
                        ["name"] = card.Name ?? string.Empty,
                        ["instanceId"] = card.InstanceId,
                    });
                }
            }

            return new Dictionary<string, object>
            {
                ["kind"] = "intermissionNativePanelState",
                ["isAvailable"] = s.IsAvailable,
                ["omittedShopOfferCount"] = s.OmittedShopOfferCount,
                ["unsupportedShopOfferCount"] = s.UnsupportedShopOfferCount,
                ["shopOfferCount"] = s.ShopOfferCount,
                ["omittedRemovalCardCount"] = s.OmittedRemovalCardCount,
                ["unsupportedRemovalCardCount"] = s.UnsupportedRemovalCardCount,
                ["removalCardCount"] = s.RemovalCardCount,
                ["isShopAvailable"] = s.IsShopAvailable,
                ["isShopActive"] = s.IsShopActive,
                ["shopInstanceId"] = s.ShopInstanceId,
                ["currentCoins"] = s.CurrentCoins,
                ["currentRerolls"] = s.CurrentRerolls,
                ["currentStartCrystals"] = s.CurrentStartCrystals,
                ["currentDropPodCount"] = s.CurrentDropPodCount,
                ["currentResearchPoints"] = s.CurrentResearchPoints,
                ["canReroll"] = s.CanReroll,
                ["shopOffers"] = shopRows,
                ["isRemoveCardViewAvailable"] = s.IsRemoveCardViewAvailable,
                ["isRemoveCardActive"] = s.IsRemoveCardActive,
                ["isRemoveCardClickReady"] = s.IsRemoveCardClickReady,
                ["removeCardCoinCompensation"] = s.RemoveCardCoinCompensation,
                ["removalCards"] = removalRows,
            };
        }

        /// <summary>
        /// Serializes an intermission sub-screen or LoadLevel action result with status flags first.
        /// </summary>
        public static string SerializeIntermissionActionResult(IntermissionActionResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "intermissionActionResult",
                ["success"] = result.Success,
                ["action"] = result.Action ?? string.Empty,
                ["targetId"] = result.TargetId ?? string.Empty,
                ["slotIndex"] = result.SlotIndex,
                ["navigatorSceneState"] = result.NavigatorSceneState ?? string.Empty,
                ["playerHasOpenRewards"] = result.PlayerHasOpenRewards,
                ["nativeCallInvoked"] = result.NativeCallInvoked,
                ["effectObserved"] = result.EffectObserved,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of an in-game credit grant command.
        /// </summary>
        public static string SerializeCreditGrantResult(CreditGrantResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "creditGrantResult",
                ["success"] = result.Success,
                ["accountName"] = result.AccountName ?? string.Empty,
                ["amountGranted"] = result.AmountGranted,
                ["currentBalance"] = result.CurrentBalance,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of a data-driven card change application on an in-game entity.
        /// </summary>
        public static string SerializeCardChangeApplyResult(CardChangeApplyResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "cardChangeApplyResult",
                ["success"] = result.Success,
                ["targetEntityKeyId"] = result.TargetEntityKeyId ?? string.Empty,
                ["valueToChange"] = result.ValueToChange ?? string.Empty,
                ["operation"] = result.Operation ?? string.Empty,
                ["value"] = result.Value,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of an in-game tactical unit command (move, attack, etc.).
        /// </summary>
        public static string SerializeUnitCommandResult(UnitCommandResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "unitCommandResult",
                ["success"] = result.Success,
                ["entityKeyId"] = result.EntityKeyId ?? string.Empty,
                ["commandType"] = result.CommandType ?? string.Empty,
                ["targetX"] = result.TargetX,
                ["targetZ"] = result.TargetZ,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of a native patrol route dispatched to a unit.
        /// </summary>
        public static string SerializeUnitPatrolResult(UnitPatrolResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "unitPatrolResult",
                ["success"] = result.Success,
                ["entityKeyId"] = result.EntityKeyId ?? string.Empty,
                ["waypointCount"] = result.WaypointCount,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of a group command dispatched to multiple units.
        /// </summary>
        public static string SerializeGroupCommandResult(GroupCommandResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "groupCommandResult",
                ["success"] = result.Success,
                ["commandType"] = result.CommandType ?? string.Empty,
                ["targetedCount"] = result.TargetedCount,
                ["dispatchedCount"] = result.DispatchedCount,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of a tactical formation movement dispatched to multiple units.
        /// </summary>
        public static string SerializeFormationOrderResult(FormationOrderResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "formationOrderResult",
                ["success"] = result.Success,
                ["formationType"] = result.FormationType ?? string.Empty,
                ["targetedCount"] = result.TargetedCount,
                ["dispatchedCount"] = result.DispatchedCount,
                ["targetX"] = result.TargetX,
                ["targetZ"] = result.TargetZ,
                ["spacing"] = result.Spacing,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of assigning a tactical stance to a unit or group.
        /// </summary>
        public static string SerializeSyntheticGameplayScenarioResult(SyntheticGameplayScenarioResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "syntheticGameplayScenario",
                ["omittedFailureCount"] = result != null ? result.OmittedFailureCount : 0,
                ["success"] = result != null && result.Success,
                ["pausedPrecondition"] = result != null && result.PausedPrecondition,
                ["requestedUnitCount"] = result != null ? result.RequestedUnitCount : 0,
                ["spawnedUnitCount"] = result != null ? result.SpawnedUnitCount : 0,
                ["stanceAffectedCount"] = result != null ? result.StanceAffectedCount : 0,
                ["formationTargetedCount"] = result != null ? result.FormationTargetedCount : 0,
                ["formationDispatchedCount"] = result != null ? result.FormationDispatchedCount : 0,
                ["loadoutSuccessCount"] = result != null ? result.LoadoutSuccessCount : 0,
                ["affixSuccessCount"] = result != null ? result.AffixSuccessCount : 0,
                ["veterancySuccessCount"] = result != null ? result.VeterancySuccessCount : 0,
                ["visualFeedbackSuccessCount"] = result != null ? result.VisualFeedbackSuccessCount : 0,
                ["auraLinkedFollowerCount"] = result != null ? result.AuraLinkedFollowerCount : 0,
                ["pulseAffectedEntityCount"] = result != null ? result.PulseAffectedEntityCount : 0,
                ["activeHazardCount"] = result != null ? result.ActiveHazardCount : 0,
                ["mechanicsInstanceCount"] = result != null ? result.MechanicsInstanceCount : 0,
                ["destroyedSyntheticEntityCount"] = result != null ? result.DestroyedSyntheticEntityCount : 0,
                ["formationType"] = result != null ? result.FormationType : string.Empty,
                ["createdEntityKeys"] = result != null && result.CreatedEntityKeys != null ? result.CreatedEntityKeys : Array.Empty<string>(),
                ["failures"] = result != null && result.Failures != null ? result.Failures : Array.Empty<string>(),
                ["message"] = result != null ? result.Message : string.Empty,
            };
            return Serialize(view);
        }

        public static string SerializeUnitTacticalStanceResult(UnitTacticalStanceResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "unitTacticalStanceResult",
                ["success"] = result.Success,
                ["entityKeyId"] = result.EntityKeyId ?? string.Empty,
                ["stance"] = result.Stance ?? string.Empty,
                ["affectedUnitCount"] = result.AffectedUnitCount,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes unit tactical combat stances across living units.
        /// Omission counters precede detail collections.
        /// </summary>
        public static string SerializeTacticalStanceCensus(TacticalStanceCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.StanceRows != null)
            {
                for (int i = 0; i < census.StanceRows.Count; i++)
                {
                    TacticalStanceDiagnosticRow row = census.StanceRows[i];
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["entityId"] = row.EntityId ?? string.Empty,
                        ["stance"] = row.Stance ?? string.Empty,
                        ["isControlledByPlayer"] = row.IsControlledByPlayer,
                        ["moveSpeed"] = row.MoveSpeed,
                        ["weaponRange"] = row.WeaponRange,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "tacticalStanceCensus",
                ["omittedUnitCount"] = census != null ? census.OmittedUnitCount : 0,
                ["totalTrackedUnits"] = census != null ? census.TotalTrackedUnits : 0,
                ["aggressiveCount"] = census != null ? census.AggressiveCount : 0,
                ["defensiveCount"] = census != null ? census.DefensiveCount : 0,
                ["kitingCount"] = census != null ? census.KitingCount : 0,
                ["holdFireCount"] = census != null ? census.HoldFireCount : 0,
                ["standardCount"] = census != null ? census.StandardCount : 0,
                ["stanceRows"] = rows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of adding or removing an elite affix on a unit.
        /// </summary>
        public static string SerializeUnitAffixResult(UnitAffixResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "unitAffixResult",
                ["success"] = result.Success,
                ["entityKeyId"] = result.EntityKeyId ?? string.Empty,
                ["affix"] = result.Affix ?? string.Empty,
                ["action"] = result.Action ?? string.Empty,
                ["activeAffixCount"] = result.ActiveAffixCount,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of a bounded tactical pulse around a unit.
        /// </summary>
        public static string SerializeTacticalPulseResult(TacticalPulseResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "tacticalPulseResult",
                ["success"] = result.Success,
                ["entityKeyId"] = result.EntityKeyId ?? string.Empty,
                ["pulseType"] = result.PulseType ?? string.Empty,
                ["affectedEntityCount"] = result.AffectedEntityCount,
                ["damageApplied"] = result.DamageApplied,
                ["healingApplied"] = result.HealingApplied,
                ["statusEffectApplicationCount"] = result.StatusEffectApplicationCount,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes a bounded director threat sweep result with omission counters before target details.
        /// </summary>
        public static string SerializeThreatSweepResult(ThreatSweepResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "directorThreatSweepResult",
                ["success"] = result.Success,
                ["omittedTargetCount"] = result.OmittedTargetCount,
                ["sourceEntityKeyId"] = result.SourceEntityKeyId ?? string.Empty,
                ["sweepType"] = result.SweepType ?? string.Empty,
                ["statusEffectName"] = result.StatusEffectName ?? string.Empty,
                ["radius"] = result.Radius,
                ["durationSeconds"] = result.DurationSeconds,
                ["affectedEntityCount"] = result.AffectedEntityCount,
                ["targetEntityKeyIds"] = result.TargetEntityKeyIds != null
                    ? result.TargetEntityKeyIds
                    : (object)Array.Empty<string>(),
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes a coordinated assault result with omission counters before attacker details.
        /// </summary>
        public static string SerializeCoordinatedAssaultResult(CoordinatedAssaultResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "coordinatedAssaultResult",
                ["success"] = result.Success,
                ["omittedAttackerCount"] = result.OmittedAttackerCount,
                ["targetEntityKeyId"] = result.TargetEntityKeyId ?? string.Empty,
                ["requestedAttackerCount"] = result.RequestedAttackerCount,
                ["dispatchedAttackerCount"] = result.DispatchedAttackerCount,
                ["targetX"] = result.TargetX,
                ["targetZ"] = result.TargetZ,
                ["dispatchedAttackerEntityKeyIds"] = result.DispatchedAttackerEntityKeyIds != null
                    ? result.DispatchedAttackerEntityKeyIds
                    : (object)Array.Empty<string>(),
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes an evasive retreat result.
        /// </summary>
        public static string SerializeRetreatResult(RetreatResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "retreatResult",
                ["success"] = result.Success,
                ["entityKeyId"] = result.EntityKeyId ?? string.Empty,
                ["threatX"] = result.ThreatX,
                ["threatZ"] = result.ThreatZ,
                ["retreatX"] = result.RetreatX,
                ["retreatZ"] = result.RetreatZ,
                ["distance"] = result.Distance,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes active elite affixes and traits applied to living units.
        /// Omission counters precede detail collections.
        /// </summary>
        public static string SerializeUnitAffixCensus(UnitAffixCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.AffixRows != null)
            {
                for (int i = 0; i < census.AffixRows.Count; i++)
                {
                    UnitAffixDiagnosticRow row = census.AffixRows[i];
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["entityId"] = row.EntityId ?? string.Empty,
                        ["isControlledByPlayer"] = row.IsControlledByPlayer,
                        ["affixes"] = row.Affixes != null ? row.Affixes : (object)Array.Empty<string>(),
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "unitAffixCensus",
                ["omittedUnitCount"] = census != null ? census.OmittedUnitCount : 0,
                ["totalTrackedUnits"] = census != null ? census.TotalTrackedUnits : 0,
                ["totalActiveAffixes"] = census != null ? census.TotalActiveAffixes : 0,
                ["affixRows"] = rows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the visual render, rig architecture, and animation telemetry census as JSON.
        /// Loss/omission counter placed strictly first to uphold clean-room bounded trace invariants.
        /// </summary>
        public static string SerializeVisualRenderCensus(VisualRenderCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.UnitRows != null)
            {
                for (int i = 0; i < census.UnitRows.Count; i++)
                {
                    VisualUnitDiagnosticRow row = census.UnitRows[i];
                    var socketNames = new List<string>();
                    if (row.DiscoveredSockets != null)
                    {
                        for (int s = 0; s < row.DiscoveredSockets.Count; s++)
                        {
                            string sName = row.DiscoveredSockets[s];
                            if (!string.IsNullOrEmpty(sName)) socketNames.Add(sName);
                        }
                    }

                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["entityId"] = row.EntityId ?? string.Empty,
                        ["isControlledByPlayer"] = row.IsControlledByPlayer,
                        ["rigType"] = row.RigType ?? "StaticVehicle",
                        ["hasAnimator"] = row.HasAnimator,
                        ["isAnimatorActive"] = row.IsAnimatorActive,
                        ["activeClipName"] = row.ActiveClipName ?? string.Empty,
                        ["clipNormalizedTime"] = row.ClipNormalizedTime,
                        ["clipSpeed"] = row.ClipSpeed,
                        ["isMovingParam"] = row.IsMovingParam,
                        ["fireTriggerParam"] = row.FireTriggerParam,
                        ["deathTriggerParam"] = row.DeathTriggerParam,
                        ["rendererCount"] = row.RendererCount,
                        ["totalVertexCount"] = row.TotalVertexCount,
                        ["primaryShaderName"] = row.PrimaryShaderName ?? string.Empty,
                        ["hasPbrKeywords"] = row.HasPbrKeywords,
                        ["isGpuInstanced"] = row.IsGpuInstanced,
                        ["isVisible"] = row.IsVisible,
                        ["discoveredSockets"] = socketNames,
                        ["socketAmbiguityCount"] = row.SocketAmbiguityCount
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "visualRenderCensus",
                ["omittedUnitCount"] = census != null ? census.OmittedUnitCount : 0,
                ["totalTrackedUnits"] = census != null ? census.TotalTrackedUnits : 0,
                ["skinnedMeshCount"] = census != null ? census.SkinnedMeshCount : 0,
                ["staticMeshCount"] = census != null ? census.StaticMeshCount : 0,
                ["animatedUnitCount"] = census != null ? census.AnimatedUnitCount : 0,
                ["pbrShaderCount"] = census != null ? census.PbrShaderCount : 0,
                ["unitRows"] = rows
            };
            return Serialize(view);
        }

        public static string SerializeLoadedPrefabVisualAuditCensus(LoadedPrefabVisualAuditCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.PrefabRows != null)
            {
                for (int i = 0; i < census.PrefabRows.Count; i++)
                {
                    LoadedPrefabVisualAuditRow row = census.PrefabRows[i];
                    var socketNames = new List<string>();
                    if (row.DiscoveredSockets != null)
                    {
                        for (int s = 0; s < row.DiscoveredSockets.Count; s++)
                        {
                            string sName = row.DiscoveredSockets[s];
                            if (!string.IsNullOrEmpty(sName)) socketNames.Add(sName);
                        }
                    }

                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityId"] = row.EntityId ?? string.Empty,
                        ["prefabName"] = row.PrefabName ?? string.Empty,
                        ["rigType"] = row.RigType ?? "StaticVehicle",
                        ["rootBoneName"] = row.RootBoneName ?? string.Empty,
                        ["boneCount"] = row.BoneCount,
                        ["rendererCount"] = row.RendererCount,
                        ["totalVertexCount"] = row.TotalVertexCount,
                        ["submeshCount"] = row.SubmeshCount,
                        ["primaryShaderName"] = row.PrimaryShaderName ?? string.Empty,
                        ["hasPbrKeywords"] = row.HasPbrKeywords,
                        ["isGpuInstanced"] = row.IsGpuInstanced,
                        ["hasAnimator"] = row.HasAnimator,
                        ["avatarIsValid"] = row.AvatarIsValid,
                        ["discoveredSockets"] = socketNames,
                        ["socketAmbiguityCount"] = row.SocketAmbiguityCount,
                        ["hasMuzzlePrimary"] = row.HasMuzzlePrimary,
                        ["hasFirePoint0"] = row.HasFirePoint0,
                        ["hasWeaponAudio"] = row.HasWeaponAudio,
                        ["hasEngineAudio"] = row.HasEngineAudio,
                        ["hasDamageSmoke"] = row.HasDamageSmoke
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "loadedPrefabVisualAuditCensus",
                ["omittedUnitCount"] = census != null ? census.OmittedUnitCount : 0,
                ["totalAuditedPrefabs"] = census != null ? census.TotalAuditedPrefabs : 0,
                ["skinnedPrefabCount"] = census != null ? census.SkinnedPrefabCount : 0,
                ["animatedPrefabCount"] = census != null ? census.AnimatedPrefabCount : 0,
                ["pbrPrefabCount"] = census != null ? census.PbrPrefabCount : 0,
                ["ambiguousSocketPrefabCount"] = census != null ? census.AmbiguousSocketPrefabCount : 0,
                ["prefabRows"] = rows
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the detailed entity visual diagnostic report as JSON.
        /// </summary>
        public static string SerializeDetailedEntityVisualDiagnostic(DetailedEntityVisualDiagnostic diag)
        {
            if (diag == null)
            {
                return Serialize(new Dictionary<string, object>
                {
                    ["kind"] = "detailedEntityVisualDiagnostic",
                    ["success"] = false,
                    ["entityKeyId"] = string.Empty,
                    ["entityId"] = string.Empty,
                    ["message"] = "Null diagnostic report."
                });
            }

            var rendererList = new List<Dictionary<string, object>>();
            if (diag.Renderers != null)
            {
                for (int i = 0; i < diag.Renderers.Count; i++)
                {
                    VisualRendererDetailRow r = diag.Renderers[i];
                    rendererList.Add(new Dictionary<string, object>
                    {
                        ["name"] = r.Name ?? string.Empty,
                        ["rendererType"] = r.RendererType ?? string.Empty,
                        ["enabled"] = r.Enabled,
                        ["isVisible"] = r.IsVisible,
                        ["meshName"] = r.MeshName ?? string.Empty,
                        ["vertexCount"] = r.VertexCount,
                        ["submeshCount"] = r.SubmeshCount,
                        ["materialNames"] = r.MaterialNames ?? new List<string>().AsReadOnly(),
                        ["shaderNames"] = r.ShaderNames ?? new List<string>().AsReadOnly(),
                        ["shaderKeywords"] = r.ShaderKeywords ?? new List<string>().AsReadOnly(),
                        ["isGpuInstanced"] = r.IsGpuInstanced
                    });
                }
            }

            var socketList = new List<Dictionary<string, object>>();
            if (diag.Sockets != null)
            {
                for (int i = 0; i < diag.Sockets.Count; i++)
                {
                    VisualSocketDetailRow s = diag.Sockets[i];
                    socketList.Add(new Dictionary<string, object>
                    {
                        ["socketName"] = s.SocketName ?? string.Empty,
                        ["canonicalRole"] = s.CanonicalRole ?? string.Empty,
                        ["localPosX"] = s.LocalPosX,
                        ["localPosY"] = s.LocalPosY,
                        ["localPosZ"] = s.LocalPosZ,
                        ["localRotX"] = s.LocalRotX,
                        ["localRotY"] = s.LocalRotY,
                        ["localRotZ"] = s.LocalRotZ,
                        ["isAmbiguous"] = s.IsAmbiguous,
                        ["duplicateCount"] = s.DuplicateCount
                    });
                }
            }

            var paramList = new List<Dictionary<string, object>>();
            if (diag.Parameters != null)
            {
                for (int i = 0; i < diag.Parameters.Count; i++)
                {
                    VisualAnimationParameterRow p = diag.Parameters[i];
                    paramList.Add(new Dictionary<string, object>
                    {
                        ["name"] = p.Name ?? string.Empty,
                        ["parameterType"] = p.ParameterType ?? string.Empty,
                        ["value"] = p.Value ?? string.Empty
                    });
                }
            }

            var nativeRetargetMappingList = new List<Dictionary<string, object>>();
            if (diag.NativeRigRetargetMappings != null)
            {
                for (int i = 0; i < diag.NativeRigRetargetMappings.Count; i++)
                {
                    VisualNativeRigRetargetMappingRow mapping = diag.NativeRigRetargetMappings[i];
                    nativeRetargetMappingList.Add(new Dictionary<string, object>
                    {
                        ["sourcePath"] = mapping.SourcePath ?? string.Empty,
                        ["targetPath"] = mapping.TargetPath ?? string.Empty,
                        ["sourceAvailable"] = mapping.SourceAvailable,
                        ["targetAvailable"] = mapping.TargetAvailable,
                        ["sourceLocalPose"] = SerializeRetargetLocalPose(mapping.SourcePose),
                        ["targetLocalPose"] = SerializeRetargetLocalPose(mapping.TargetPose),
                        ["expectedTargetLocalPose"] = SerializeRetargetLocalPose(mapping.ExpectedTargetPose)
                    });
                }
            }

            var renderSection = new Dictionary<string, object>
            {
                ["rendererCount"] = diag.RendererCount,
                ["totalVertexCount"] = diag.TotalVertexCount,
                ["submeshCount"] = diag.SubmeshCount,
                ["boundsSizeX"] = diag.BoundsSizeX,
                ["boundsSizeY"] = diag.BoundsSizeY,
                ["boundsSizeZ"] = diag.BoundsSizeZ,
                ["hasLODGroup"] = diag.HasLODGroup,
                ["lodCount"] = diag.LODCount,
                ["renderers"] = rendererList
            };

            var rigSection = new Dictionary<string, object>
            {
                ["rigType"] = diag.RigType ?? "StaticVehicle",
                ["rootBoneName"] = diag.RootBoneName ?? string.Empty,
                ["boneCount"] = diag.BoneCount,
                ["boneNames"] = diag.BoneNames ?? new List<string>().AsReadOnly(),
                ["sockets"] = socketList
            };

            var animSection = new Dictionary<string, object>
            {
                ["hasAnimator"] = diag.HasAnimator,
                ["isAnimatorActive"] = diag.IsAnimatorActive,
                ["controllerName"] = diag.ControllerName ?? string.Empty,
                ["avatarName"] = diag.AvatarName ?? string.Empty,
                ["avatarIsValid"] = diag.AvatarIsValid,
                ["currentStateName"] = diag.CurrentStateName ?? string.Empty,
                ["activeClipName"] = diag.ActiveClipName ?? string.Empty,
                ["clipLength"] = diag.ClipLength,
                ["normalizedTime"] = diag.NormalizedTime,
                ["speed"] = diag.Speed,
                ["parameters"] = paramList,
                ["nativeRigRetarget"] = new Dictionary<string, object>
                {
                    ["present"] = diag.NativeRigRetarget.Present,
                    ["isBound"] = diag.NativeRigRetarget.IsBound,
                    ["isSuspended"] = diag.NativeRigRetarget.IsSuspended,
                    ["configuredMappingCount"] = diag.NativeRigRetarget.ConfiguredMappingCount,
                    ["boundMappingCount"] = diag.NativeRigRetarget.BoundMappingCount,
                    ["omittedMappingCount"] = diag.NativeRigRetargetOmittedMappingCount,
                    ["mappings"] = nativeRetargetMappingList
                }
            };

            var directorSection = new Dictionary<string, object>
            {
                ["hasVisualDirector"] = diag.HasVisualDirector,
                ["isDamagedVfxActive"] = diag.IsDamagedVfxActive,
                ["isCriticalVfxActive"] = diag.IsCriticalVfxActive,
                ["isTurretJammed"] = diag.IsTurretJammed,
                ["recoilOffset"] = diag.RecoilOffset,
                ["doorOpenProgress"] = diag.DoorOpenProgress
            };

            var view = new Dictionary<string, object>
            {
                ["kind"] = "detailedEntityVisualDiagnostic",
                ["success"] = diag.Success,
                ["entityKeyId"] = diag.EntityKeyId ?? string.Empty,
                ["entityId"] = diag.EntityId ?? string.Empty,
                ["message"] = diag.Message ?? string.Empty,
                ["render"] = renderSection,
                ["rig"] = rigSection,
                ["animation"] = animSection,
                ["director"] = directorSection
            };

            return Serialize(view);
        }

        private static Dictionary<string, object> SerializeRetargetLocalPose(VisualRetargetLocalPoseRow pose)
        {
            return new Dictionary<string, object>
            {
                ["position"] = new Dictionary<string, object>
                {
                    ["x"] = pose.PositionX,
                    ["y"] = pose.PositionY,
                    ["z"] = pose.PositionZ
                },
                ["rotation"] = new Dictionary<string, object>
                {
                    ["x"] = pose.RotationX,
                    ["y"] = pose.RotationY,
                    ["z"] = pose.RotationZ,
                    ["w"] = pose.RotationW
                }
            };
        }

        /// <summary>
        /// Serializes the result of dispatching an AI assault wave.
        /// </summary>
        public static string SerializeAiWaveDispatchResult(AiWaveDispatchResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "aiWaveDispatchResult",
                ["success"] = result.Success,
                ["waveNumber"] = result.WaveNumber,
                ["aggressionBoost"] = result.AggressionBoost,
                ["dispatchedUnitCount"] = result.DispatchedUnitCount,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of an active skill command dispatched on a unit.
        /// </summary>
        public static string SerializeActiveSkillOrderResult(ActiveSkillOrderResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "activeSkillOrderResult",
                ["success"] = result.Success,
                ["entityKeyId"] = result.EntityKeyId ?? string.Empty,
                ["skillTargetMode"] = result.SkillTargetMode ?? string.Empty,
                ["targetX"] = result.TargetX,
                ["targetZ"] = result.TargetZ,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of an in-game entity vitality adjustment (Heal, RepairArmor, ChargeShield, Damage).
        /// </summary>
        public static string SerializeVitalityAdjustmentResult(VitalityAdjustmentResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "vitalityAdjustmentResult",
                ["success"] = result.Success,
                ["entityKeyId"] = result.EntityKeyId ?? string.Empty,
                ["adjustmentType"] = result.AdjustmentType ?? string.Empty,
                ["amountApplied"] = result.AmountApplied,
                ["newHealth"] = result.NewHealth,
                ["newArmor"] = result.NewArmor,
                ["newShield"] = result.NewShield,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of applying or clearing a status effect on an entity.
        /// </summary>
        public static string SerializeStatusEffectOrderResult(StatusEffectOrderResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "statusEffectOrderResult",
                ["success"] = result.Success,
                ["entityKeyId"] = result.EntityKeyId ?? string.Empty,
                ["statusEffectName"] = result.StatusEffectName ?? string.Empty,
                ["durationSeconds"] = result.DurationSeconds,
                ["isApplied"] = result.IsApplied,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes match terrain, map bounds, and world grid layout metrics.
        /// </summary>
        public static string SerializeGridMetricsCensus(GridMetricsCensus census)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "gridMetricsCensus",
                ["isGridReady"] = census != null && census.IsGridReady,
                ["worldWidth"] = census != null ? census.WorldWidth : 0f,
                ["worldHeight"] = census != null ? census.WorldHeight : 0f,
                ["horizontalCellCount"] = census != null ? census.HorizontalCellCount : 0,
                ["verticalCellCount"] = census != null ? census.VerticalCellCount : 0,
                ["cellSize"] = census != null ? census.CellSize : 0f,
                ["playerStartX"] = census != null ? census.PlayerStartX : 0,
                ["playerStartY"] = census != null ? census.PlayerStartY : 0,
                ["aiStartX"] = census != null ? census.AiStartX : 0,
                ["aiStartY"] = census != null ? census.AiStartY : 0,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the evaluation of an (X, Z) coordinate against the battlefield grid.
        /// </summary>
        public static string SerializeGridCellEvaluationResult(GridCellEvaluationResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "gridCellEvaluationResult",
                ["isWithinWorld"] = result.IsWithinWorld,
                ["clampedX"] = result.ClampedX,
                ["clampedZ"] = result.ClampedZ,
                ["cellX"] = result.CellX,
                ["cellY"] = result.CellY,
                ["surfaceHeight"] = result.SurfaceHeight,
                ["isBorderCell"] = result.IsBorderCell,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes player selection census and control group assignments.
        /// </summary>
        public static string SerializeSelectionCensus(SelectionCensus census)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "selectionCensus",
                ["omittedSelectedCount"] = census != null ? census.OmittedSelectedCount : 0,
                ["isSelectionReady"] = census != null && census.IsSelectionReady,
                ["selectedCount"] = census != null ? census.SelectedCount : 0,
                ["hasSelectedAiEntity"] = census != null && census.HasSelectedAiEntity,
                ["selectedAiEntityKey"] = census != null ? (census.SelectedAiEntityKey ?? string.Empty) : string.Empty,
                ["onlyBuildingsSelected"] = census != null && census.OnlyBuildingsSelected,
                ["onlyEntitiesThatCannotAttackSelected"] = census != null && census.OnlyEntitiesThatCannotAttackSelected,
                ["activeSkillSelectedCount"] = census != null ? census.ActiveSkillSelectedCount : 0,
                ["selectedEntityKeys"] = census != null && census.SelectedEntityKeys != null ? census.SelectedEntityKeys : (object)Array.Empty<string>(),
                ["selectedEntityIds"] = census != null && census.SelectedEntityIds != null ? census.SelectedEntityIds : (object)Array.Empty<string>(),
                ["controlGroupCounts"] = census != null && census.ControlGroupCounts != null ? census.ControlGroupCounts : (object)Array.Empty<int>(),
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes Fog of War runtime status and update settings.
        /// </summary>
        public static string SerializeFogStateCensus(FogStateCensus census)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "fogStateCensus",
                ["isAvailable"] = census != null && census.IsAvailable,
                ["isActive"] = census != null && census.IsActive,
                ["isDisabled"] = census != null && census.IsDisabled,
                ["updateIntervalSeconds"] = census != null ? census.UpdateIntervalSeconds : 0f,
                ["hasBlendTexture"] = census != null && census.HasBlendTexture,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes all registered unit, building, and factory entity IDs from the game balancing store.
        /// </summary>
        public static string SerializeEntityCatalogCensus(EntityCatalogCensus census)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "entityCatalogCensus",
                ["omittedUnitCount"] = census != null ? census.OmittedUnitCount : 0,
                ["omittedBuildingCount"] = census != null ? census.OmittedBuildingCount : 0,
                ["omittedFactoryCount"] = census != null ? census.OmittedFactoryCount : 0,
                ["isCatalogReady"] = census != null && census.IsCatalogReady,
                ["totalUnits"] = census != null ? census.TotalUnits : 0,
                ["totalBuildings"] = census != null ? census.TotalBuildings : 0,
                ["totalFactories"] = census != null ? census.TotalFactories : 0,
                ["unitIds"] = census != null && census.UnitIds != null ? census.UnitIds : (object)Array.Empty<string>(),
                ["buildingIds"] = census != null && census.BuildingIds != null ? census.BuildingIds : (object)Array.Empty<string>(),
                ["factoryIds"] = census != null && census.FactoryIds != null ? census.FactoryIds : (object)Array.Empty<string>(),
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of a selection command.
        /// </summary>
        public static string SerializeSelectionCommandResult(SelectionCommandResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "selectionCommandResult",
                ["success"] = result.Success,
                ["commandType"] = result.CommandType ?? string.Empty,
                ["entitiesSelectedCount"] = result.EntitiesSelectedCount,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of a Fog of War toggle or reveal command.
        /// </summary>
        public static string SerializeFogCommandResult(FogCommandResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "fogCommandResult",
                ["success"] = result.Success,
                ["isDisabled"] = result.IsDisabled,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of adjusting match time scale.
        /// </summary>
        public static string SerializeGameSpeedCommandResult(GameSpeedCommandResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "gameSpeedCommandResult",
                ["success"] = result.Success,
                ["previousTimeScale"] = result.PreviousTimeScale,
                ["currentTimeScale"] = result.CurrentTimeScale,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of triggering match victory or defeat.
        /// </summary>
        public static string SerializeMatchControlResult(MatchControlResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "matchControlResult",
                ["success"] = result.Success,
                ["outcome"] = result.Outcome ?? string.Empty,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of dynamically spawning an entity on the grid.
        /// </summary>
        public static string SerializeEntitySpawnResult(EntitySpawnResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "entitySpawnResult",
                ["success"] = result.Success,
                ["entityKeyId"] = result.EntityKeyId ?? string.Empty,
                ["entityId"] = result.EntityId ?? string.Empty,
                ["side"] = result.Side ?? string.Empty,
                ["spawnedX"] = result.SpawnedX,
                ["spawnedY"] = result.SpawnedY,
                ["spawnedZ"] = result.SpawnedZ,
                ["health"] = result.Health,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Unified health and status telemetry synthesizing loader state, content registries,
        /// compiled AI modules, match economy, and combat telemetry into a single agent-readable JSON.
        /// </summary>
        /// <summary>
        /// Serializes native AI Director state, attack wave progress, and command queue counts.
        /// </summary>
        public static string SerializeAiDirectorCensus(AiDirectorCensus census)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "aiDirectorCensus",
                ["isAiDirectorAvailable"] = census != null && census.IsAiDirectorAvailable,
                ["currentAttackWaveNumber"] = census != null ? census.CurrentAttackWaveNumber : 0,
                ["tickDuration"] = census != null ? census.TickDuration : 0f,
                ["showAiTags"] = census != null && census.ShowAiTags,
                ["showCurrentCommands"] = census != null && census.ShowCurrentCommands,
                ["attackCommandCount"] = census != null ? census.AttackCommandCount : 0,
                ["defendCommandCount"] = census != null ? census.DefendCommandCount : 0,
                ["scoutCommandCount"] = census != null ? census.ScoutCommandCount : 0,
                ["buildCommandCount"] = census != null ? census.BuildCommandCount : 0,
                ["produceCommandCount"] = census != null ? census.ProduceCommandCount : 0,
                ["otherCommandCount"] = census != null ? census.OtherCommandCount : 0,
                ["constructionQueueSummary"] = census != null ? (census.ConstructionQueueSummary ?? string.Empty) : string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of an AI Director command.
        /// </summary>
        public static string SerializeAiDirectorCommandResult(AiDirectorCommandResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "aiDirectorCommandResult",
                ["success"] = result.Success,
                ["commandType"] = result.CommandType ?? string.Empty,
                ["waveNumber"] = result.WaveNumber,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes camera viewport, world position, and zoom settings.
        /// </summary>
        public static string SerializeCameraCensus(CameraCensus census)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "cameraCensus",
                ["isCameraAvailable"] = census != null && census.IsCameraAvailable,
                ["positionX"] = census != null ? census.PositionX : 0f,
                ["positionY"] = census != null ? census.PositionY : 0f,
                ["positionZ"] = census != null ? census.PositionZ : 0f,
                ["fieldOfView"] = census != null ? census.FieldOfView : 0f,
                ["minFov"] = census != null ? census.MinFov : 0f,
                ["maxFov"] = census != null ? census.MaxFov : 0f,
                ["defaultFov"] = census != null ? census.DefaultFov : 0f,
                ["isOrthographic"] = census != null && census.IsOrthographic,
                ["orthographicSize"] = census != null ? census.OrthographicSize : 0f,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of a camera command.
        /// </summary>
        public static string SerializeCameraCommandResult(CameraCommandResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "cameraCommandResult",
                ["success"] = result.Success,
                ["commandType"] = result.CommandType ?? string.Empty,
                ["targetX"] = result.TargetX,
                ["targetZ"] = result.TargetZ,
                ["fieldOfView"] = result.FieldOfView,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes active harvesters, crystal payloads, and mining states across the battlefield.
        /// Loss/omission counters are serialized before detail collections.
        /// </summary>
        public static string SerializeHarvesterCensus(HarvesterCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.HarvesterRows != null)
            {
                foreach (HarvesterDiagnosticRow row in census.HarvesterRows)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["isPlayerControlled"] = row.IsPlayerControlled,
                        ["currentLoad"] = row.CurrentLoad,
                        ["maxLoad"] = row.MaxLoad,
                        ["loadFraction"] = row.LoadFraction,
                        ["currentState"] = row.CurrentState ?? string.Empty,
                        ["positionX"] = row.PositionX,
                        ["positionZ"] = row.PositionZ,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "harvesterCensus",
                ["omittedHarvesterCount"] = census != null ? census.OmittedHarvesterCount : 0,
                ["totalHarvesterCount"] = census != null ? census.TotalHarvesterCount : 0,
                ["playerHarvesterCount"] = census != null ? census.PlayerHarvesterCount : 0,
                ["aiHarvesterCount"] = census != null ? census.AiHarvesterCount : 0,
                ["idleHarvesterCount"] = census != null ? census.IdleHarvesterCount : 0,
                ["totalCrystalsCarried"] = census != null ? census.TotalCrystalsCarried : 0f,
                ["trackedHarvesterRowCount"] = rows.Count,
                ["harvesterRows"] = rows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of a harvester command.
        /// </summary>
        public static string SerializeHarvesterCommandResult(HarvesterCommandResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "harvesterCommandResult",
                ["success"] = result.Success,
                ["harvesterKeyId"] = result.HarvesterKeyId ?? string.Empty,
                ["commandType"] = result.CommandType ?? string.Empty,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes audio mixer volume slider levels.
        /// </summary>
        public static string SerializeAudioCensus(AudioCensus census)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "audioCensus",
                ["isAudioAvailable"] = census != null && census.IsAudioAvailable,
                ["masterVolumeSlider"] = census != null ? census.MasterVolumeSlider : 0f,
                ["musicVolumeSlider"] = census != null ? census.MusicVolumeSlider : 0f,
                ["gameplayVolumeSlider"] = census != null ? census.GameplayVolumeSlider : 0f,
                ["uiVolumeSlider"] = census != null ? census.UiVolumeSlider : 0f,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the visual surface census (renderers, materials, vertex-wear channels).
        /// Omission counters precede detail collections.
        /// </summary>
        public static string SerializeVisualSurfaceCensus(VisualSurfaceCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.Rows != null)
            {
                foreach (VisualSurfaceRow row in census.Rows)
                {
                    if (row == null) continue;
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["rendererCount"] = row.RendererCount,
                        ["rendererEnabledCount"] = row.RendererEnabledCount,
                        ["materialCount"] = row.MaterialCount,
                        ["vertexCount"] = row.VertexCount,
                        ["hasVertexColors"] = row.HasVertexColors,
                        ["vertexColorSampleCount"] = row.VertexColorSampleCount,

                        ["averageCavityAo"] = row.AverageCavityAo,
                        ["minCavityAo"] = row.MinCavityAo,
                        ["maxCavityAo"] = row.MaxCavityAo,
                        ["microReliefVertexCount"] = row.MicroReliefVertexCount,

                        ["averageEdgeWear"] = row.AverageEdgeWear,
                        ["minEdgeWear"] = row.MinEdgeWear,
                        ["maxEdgeWear"] = row.MaxEdgeWear,
                        ["weldVertexCount"] = row.WeldVertexCount,

                        ["averageThermalSoot"] = row.AverageThermalSoot,
                        ["minThermalSoot"] = row.MinThermalSoot,
                        ["maxThermalSoot"] = row.MaxThermalSoot,
                        ["mudVertexCount"] = row.MudVertexCount,
                        ["temperFringeVertexCount"] = row.TemperFringeVertexCount,

                        ["averageTeamMask"] = row.AverageTeamMask,
                        ["minTeamMask"] = row.MinTeamMask,
                        ["maxTeamMask"] = row.MaxTeamMask,

                        ["lodCount"] = row.LodCount,
                        ["hierarchyTruncated"] = row.HierarchyTruncated,
                        ["vertexColorSamplingBounded"] = row.VertexColorSamplingBounded,
                        ["nameSamplingBounded"] = row.NameSamplingBounded,
                        ["shaderNames"] = row.ShaderNames != null ? row.ShaderNames : (object)Array.Empty<string>(),
                        ["shaderKeywords"] = row.ShaderKeywords != null ? row.ShaderKeywords : (object)Array.Empty<string>(),
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "visualSurfaceCensus",
                ["omittedRowCount"] = census != null ? census.OmittedRowCount : 0,
                ["rowCount"] = rows.Count,
                ["rows"] = rows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes URP/PBR material compliance and procedural vertex-wear quality metrics.
        /// Omission counters precede bounded row details.
        /// </summary>
        public static string SerializeVisualQualityCensus(VisualQualityCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.Rows != null)
            {
                foreach (VisualQualityRow row in census.Rows)
                {
                    if (row == null) continue;
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["isLoaderOwned"] = row.IsLoaderOwned,
                        ["contractPass"] = row.ContractPass,
                        ["rendererCount"] = row.RendererCount,
                        ["visibleRendererCount"] = row.VisibleRendererCount,
                        ["materialCount"] = row.MaterialCount,
                        ["pbrMaterialCount"] = row.PbrMaterialCount,
                        ["standardMaterialCount"] = row.StandardMaterialCount,
                        ["unsupportedShaderCount"] = row.UnsupportedShaderCount,
                        ["pbrKeywordCompliantMaterialCount"] = row.PbrKeywordCompliantMaterialCount,
                        ["gpuInstancedMaterialCount"] = row.GpuInstancedMaterialCount,
                        ["metallicGlossMapCount"] = row.MetallicGlossMapCount,
                        ["normalMapCount"] = row.NormalMapCount,
                        ["emissionMapCount"] = row.EmissionMapCount,
                        ["vertexColorSampleCount"] = row.VertexColorSampleCount,
                        ["cavityAoLowFraction"] = row.CavityAoLowFraction,
                        ["edgeWearFraction"] = row.EdgeWearFraction,
                        ["thermalSootFraction"] = row.ThermalSootFraction,
                        ["teamMaskCoverage"] = row.TeamMaskCoverage,
                        ["lodGroupCount"] = row.LodGroupCount,
                        ["hierarchyTruncated"] = row.HierarchyTruncated,
                        ["vertexColorSamplingBounded"] = row.VertexColorSamplingBounded,
                        ["materialSamplingBounded"] = row.MaterialSamplingBounded,
                        ["qualityIssues"] = row.QualityIssues != null ? row.QualityIssues : (object)Array.Empty<string>(),
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "visualQualityCensus",
                ["omittedRowCount"] = census != null ? census.OmittedRowCount : 0,
                ["loaderOwnedUnitCount"] = census != null ? census.LoaderOwnedUnitCount : 0,
                ["pbrContractPassCount"] = census != null ? census.PbrContractPassCount : 0,
                ["pbrContractIssueCount"] = census != null ? census.PbrContractIssueCount : 0,
                ["pbrMaterialCount"] = census != null ? census.PbrMaterialCount : 0,
                ["vertexColorSampleCount"] = census != null ? census.VertexColorSampleCount : 0,
                ["rowCount"] = rows.Count,
                ["rows"] = rows,
            };
            return Serialize(view);
        }

        public static string SerializeVisualHealthSummary(VisualHealthSummary summary)
        {
            var rows = new List<Dictionary<string, object>>();
            if (summary != null && summary.Rows != null)
            {
                foreach (VisualHealthRow row in summary.Rows)
                {
                    if (row == null) continue;
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["isLoaderOwned"] = row.IsLoaderOwned,
                        ["contractPass"] = row.ContractPass,
                        ["qualityIssueCount"] = row.QualityIssueCount,
                        ["rendererCount"] = row.RendererCount,
                        ["materialCount"] = row.MaterialCount,
                        ["pbrMaterialCount"] = row.PbrMaterialCount,
                        ["vertexColorSampleCount"] = row.VertexColorSampleCount,
                        ["surfaceVertexCount"] = row.SurfaceVertexCount,
                        ["materialTextureCount"] = row.MaterialTextureCount,
                        ["socketCount"] = row.SocketCount,
                        ["ambiguousSocketCount"] = row.AmbiguousSocketCount,
                        ["totalIssueCount"] = row.TotalIssueCount,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "visualHealthSummary",
                ["omittedUnitCount"] = summary != null ? summary.OmittedUnitCount : 0,
                ["trackedUnitCount"] = summary != null ? summary.TrackedUnitCount : 0,
                ["loaderOwnedUnitCount"] = summary != null ? summary.LoaderOwnedUnitCount : 0,
                ["qualityPassCount"] = summary != null ? summary.QualityPassCount : 0,
                ["qualityIssueCount"] = summary != null ? summary.QualityIssueCount : 0,
                ["totalVertexColorSampleCount"] = summary != null ? summary.TotalVertexColorSampleCount : 0,
                ["totalMaterialCount"] = summary != null ? summary.TotalMaterialCount : 0,
                ["totalSocketCount"] = summary != null ? summary.TotalSocketCount : 0,
                ["ambiguousSocketUnitCount"] = summary != null ? summary.AmbiguousSocketUnitCount : 0,
                ["rowCount"] = rows.Count,
                ["rows"] = rows,
            };
            return Serialize(view);
        }

        public static string SerializeVisualMaterialCensus(VisualMaterialCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.Rows != null)
            {
                foreach (VisualMaterialRow row in census.Rows)
                {
                    if (row == null) continue;
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["rendererCount"] = row.RendererCount,
                        ["materialCount"] = row.MaterialCount,
                        ["materialNames"] = row.MaterialNames != null ? row.MaterialNames : (object)Array.Empty<string>(),
                        ["shaderNames"] = row.ShaderNames != null ? row.ShaderNames : (object)Array.Empty<string>(),
                        ["textureNames"] = row.TextureNames != null ? row.TextureNames : (object)Array.Empty<string>(),
                        ["hasMainTextureCount"] = row.HasMainTextureCount,
                        ["hasMetallicGlossMapCount"] = row.HasMetallicGlossMapCount,
                        ["hasNormalMapCount"] = row.HasNormalMapCount,
                        ["hasEmissionMapCount"] = row.HasEmissionMapCount,
                        ["hierarchyTruncated"] = row.HierarchyTruncated,
                        ["materialSamplingBounded"] = row.MaterialSamplingBounded,
                        ["nameSamplingBounded"] = row.NameSamplingBounded,
                        ["shaderKeywords"] = row.ShaderKeywords != null ? row.ShaderKeywords : (object)Array.Empty<string>(),
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "visualMaterialCensus",
                ["omittedRowCount"] = census != null ? census.OmittedRowCount : 0,
                ["rowCount"] = rows.Count,
                ["rows"] = rows,
            };
            return Serialize(view);
        }

        public static string SerializeSocketAuditCensus(SocketAuditCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.Rows != null)
            {
                foreach (SocketAuditRow row in census.Rows)
                {
                    if (row == null) continue;
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["socketCount"] = row.SocketCount,
                        ["socketNames"] = row.SocketNames != null ? row.SocketNames : (object)Array.Empty<string>(),
                        ["canonicalSocketCount"] = row.CanonicalSocketCount,
                        ["ambiguousSocketCount"] = row.AmbiguousSocketCount,
                        ["hasMuzzlePrimary"] = row.HasMuzzlePrimary,
                        ["hasDamageSmoke"] = row.HasDamageSmoke,
                        ["hasFootContact"] = row.HasFootContact,
                        ["hasEngineAudio"] = row.HasEngineAudio,
                        ["hierarchyTruncated"] = row.HierarchyTruncated,
                        ["socketNameListBounded"] = row.SocketNameListBounded,
                    });
                }
            }
            var view = new Dictionary<string, object>
            {
                ["kind"] = "socketAuditCensus",
                ["omittedRowCount"] = census != null ? census.OmittedRowCount : 0,
                ["rowCount"] = rows.Count,
                ["rows"] = rows,
            };
            return Serialize(view);
        }

        public static string SerializeColliderAuditCensus(ColliderAuditCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.Rows != null)
            {
                foreach (ColliderAuditRow row in census.Rows)
                {
                    if (row == null) continue;
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["colliderCount"] = row.ColliderCount,
                        ["triggerColliderCount"] = row.TriggerColliderCount,
                        ["solidColliderCount"] = row.SolidColliderCount,
                        ["colliderTypes"] = row.ColliderTypes != null ? row.ColliderTypes : (object)Array.Empty<string>(),
                        ["hasRigidbody"] = row.HasRigidbody,
                        ["rigidbodyIsKinematic"] = row.RigidbodyIsKinematic,
                    });
                }
            }
            var view = new Dictionary<string, object>
            {
                ["kind"] = "colliderAuditCensus",
                ["omittedRowCount"] = census != null ? census.OmittedRowCount : 0,
                ["rowCount"] = rows.Count,
                ["rows"] = rows,
            };
            return Serialize(view);
        }

        public static string SerializeParticleVfxCensus(ParticleVfxCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.Rows != null)
            {
                foreach (ParticleVfxRow row in census.Rows)
                {
                    if (row == null) continue;
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["particleSystemCount"] = row.ParticleSystemCount,
                        ["activeParticleSystemCount"] = row.ActiveParticleSystemCount,
                        ["trailRendererCount"] = row.TrailRendererCount,
                        ["lineRendererCount"] = row.LineRendererCount,
                        ["particleSystemNames"] = row.ParticleSystemNames != null ? row.ParticleSystemNames : (object)Array.Empty<string>(),
                    });
                }
            }
            var view = new Dictionary<string, object>
            {
                ["kind"] = "particleVfxCensus",
                ["omittedRowCount"] = census != null ? census.OmittedRowCount : 0,
                ["rowCount"] = rows.Count,
                ["rows"] = rows,
            };
            return Serialize(view);
        }

        public static string SerializeLodAuditCensus(LodAuditCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.Rows != null)
            {
                foreach (LodAuditRow row in census.Rows)
                {
                    if (row == null) continue;
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["lodGroupCount"] = row.LodGroupCount,
                        ["lodLevelCount"] = row.LodLevelCount,
                        ["hasLodGroup"] = row.HasLodGroup,
                        ["rendererNames"] = row.RendererNames != null ? row.RendererNames : (object)Array.Empty<string>(),
                    });
                }
            }
            var view = new Dictionary<string, object>
            {
                ["kind"] = "lodAuditCensus",
                ["omittedRowCount"] = census != null ? census.OmittedRowCount : 0,
                ["rowCount"] = rows.Count,
                ["rows"] = rows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the result of an audio volume adjustment command.
        /// </summary>
        public static string SerializeAudioCommandResult(AudioCommandResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "audioCommandResult",
                ["success"] = result.Success,
                ["channel"] = result.Channel ?? string.Empty,
                ["volumeSlider"] = result.VolumeSlider,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes registered tech upgrades, research branches, and relics from balancing stores.
        /// Omission counters precede detail collections.
        /// </summary>
        public static string SerializeTechCatalogCensus(TechCatalogCensus census)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "techCatalogCensus",
                ["omittedUpgradeCount"] = census != null ? census.OmittedUpgradeCount : 0,
                ["omittedResearchCount"] = census != null ? census.OmittedResearchCount : 0,
                ["omittedRelicCount"] = census != null ? census.OmittedRelicCount : 0,
                ["isTechCatalogReady"] = census != null && census.IsTechCatalogReady,
                ["totalUpgrades"] = census != null ? census.TotalUpgrades : 0,
                ["totalResearch"] = census != null ? census.TotalResearch : 0,
                ["totalRelics"] = census != null ? census.TotalRelics : 0,
                ["upgradeIds"] = census != null && census.UpgradeIds != null ? census.UpgradeIds : (object)Array.Empty<string>(),
                ["researchIds"] = census != null && census.ResearchIds != null ? census.ResearchIds : (object)Array.Empty<string>(),
                ["relicIds"] = census != null && census.RelicIds != null ? census.RelicIds : (object)Array.Empty<string>(),
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the detailed card upgrade telemetry census.
        /// Omission counters precede detail collections.
        /// </summary>
        public static string SerializeUpgradeCensus(UpgradeCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.Upgrades != null)
            {
                for (int i = 0; i < census.Upgrades.Count; i++)
                {
                    UpgradeDetailEntry entry = census.Upgrades[i];
                    if (entry == null) continue;
                    rows.Add(new Dictionary<string, object>
                    {
                        ["id"] = entry.Id ?? string.Empty,
                        ["rarity"] = entry.Rarity ?? string.Empty,
                        ["tech"] = entry.Tech ?? string.Empty,
                        ["coinsAmount"] = entry.CoinsAmount,
                        ["neededExperienceLevel"] = entry.NeededExperienceLevel,
                        ["isAllowedForDemo"] = entry.IsAllowedForDemo,
                        ["isForSpecialists"] = entry.IsForSpecialists,
                        ["inactive"] = entry.Inactive,
                        ["offeredSystemTags"] = entry.OfferedSystemTags ?? string.Empty,
                        ["neededSystemTags"] = entry.NeededSystemTags ?? string.Empty,
                        ["applyToFactoryWhereApplicable"] = entry.ApplyToFactoryWhereApplicable,
                        ["cardChangeCount"] = entry.CardChangeCount,
                        ["isModded"] = entry.IsModded,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "upgradeCensus",
                ["omittedUpgradeCount"] = census != null ? census.OmittedUpgradeCount : 0,
                ["totalUpgrades"] = census != null ? census.TotalUpgrades : 0,
                ["activeUpgrades"] = census != null ? census.ActiveUpgrades : 0,
                ["moddedUpgrades"] = census != null ? census.ModdedUpgrades : 0,
                ["isReady"] = census != null && census.IsReady,
                ["upgrades"] = rows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes the detailed player relic telemetry census.
        /// Omission counters precede detail collections.
        /// </summary>
        public static string SerializeRelicCensus(RelicCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.Relics != null)
            {
                for (int i = 0; i < census.Relics.Count; i++)
                {
                    RelicDetailEntry entry = census.Relics[i];
                    if (entry == null) continue;
                    rows.Add(new Dictionary<string, object>
                    {
                        ["id"] = entry.Id ?? string.Empty,
                        ["rarity"] = entry.Rarity ?? string.Empty,
                        ["tech"] = entry.Tech ?? string.Empty,
                        ["coinsAmount"] = entry.CoinsAmount,
                        ["neededExperienceLevel"] = entry.NeededExperienceLevel,
                        ["isAllowedForDemo"] = entry.IsAllowedForDemo,
                        ["isForSpecialists"] = entry.IsForSpecialists,
                        ["inactive"] = entry.Inactive,
                        ["offeredSystemTags"] = entry.OfferedSystemTags ?? string.Empty,
                        ["neededSystemTags"] = entry.NeededSystemTags ?? string.Empty,
                        ["cardChangeCount"] = entry.CardChangeCount,
                        ["isModded"] = entry.IsModded,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "relicCensus",
                ["omittedRelicCount"] = census != null ? census.OmittedRelicCount : 0,
                ["totalRelics"] = census != null ? census.TotalRelics : 0,
                ["activeRelics"] = census != null ? census.ActiveRelics : 0,
                ["moddedRelics"] = census != null ? census.ModdedRelics : 0,
                ["isReady"] = census != null && census.IsReady,
                ["relics"] = rows,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes unit veterancy, combat XP, rank tiers, and stat multipliers.
        /// Omission counters precede detail collections.
        /// </summary>
        public static string SerializeVeterancyCensus(VeterancyCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.Units != null)
            {
                for (int i = 0; i < census.Units.Count; i++)
                {
                    UnitVeterancyDiagnosticRow row = census.Units[i];
                    rows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = row.EntityKeyId ?? string.Empty,
                        ["entityId"] = row.EntityId ?? string.Empty,
                        ["isControlledByPlayer"] = row.IsControlledByPlayer,
                        ["currentRank"] = row.CurrentRank,
                        ["maxRank"] = row.MaxRank,
                        ["combatXp"] = row.CombatXp,
                        ["xpToNextRank"] = row.XpToNextRank,
                        ["healthBonusMultiplier"] = row.HealthBonusMultiplier,
                        ["damageBonusMultiplier"] = row.DamageBonusMultiplier,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "veterancyCensus",
                ["omittedUnitCount"] = census != null ? census.OmittedUnitCount : 0,
                ["totalVeterancyUnitsCount"] = census != null ? census.TotalVeterancyUnitsCount : 0,
                ["totalLivingUnitsCount"] = census != null ? census.TotalLivingUnitsCount : 0,
                ["veteranRankUnitsCount"] = census != null ? census.VeteranRankUnitsCount : 0,
                ["units"] = rows,
            };
            return Serialize(view);
        }

        public static string SerializeVeterancyCommandResult(VeterancyCommandResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "veterancyCommandResult",
                ["success"] = result.Success,
                ["entityKeyId"] = result.EntityKeyId ?? string.Empty,
                ["currentRank"] = result.CurrentRank,
                ["currentXp"] = result.CurrentXp,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes active captain command auras and subordinate links.
        /// Omission counters precede detail collections.
        /// </summary>
        public static string SerializeCommandAuraCensus(CommandAuraCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.Auras != null)
            {
                for (int i = 0; i < census.Auras.Count; i++)
                {
                    CommandAuraDiagnosticRow row = census.Auras[i];
                    rows.Add(new Dictionary<string, object>
                    {
                        ["captainEntityKeyId"] = row.CaptainEntityKeyId ?? string.Empty,
                        ["captainEntityId"] = row.CaptainEntityId ?? string.Empty,
                        ["isControlledByPlayer"] = row.IsControlledByPlayer,
                        ["linkedFollowerCount"] = row.LinkedFollowerCount,
                        ["commandRadius"] = row.CommandRadius,
                        ["activeAuraType"] = row.ActiveAuraType ?? string.Empty,
                        ["remainingDurationSeconds"] = row.RemainingDurationSeconds,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "commandAuraCensus",
                ["omittedAuraCount"] = census != null ? census.OmittedAuraCount : 0,
                ["totalActiveAurasCount"] = census != null ? census.TotalActiveAurasCount : 0,
                ["totalLinkedFollowersCount"] = census != null ? census.TotalLinkedFollowersCount : 0,
                ["auras"] = rows,
            };
            return Serialize(view);
        }

        public static string SerializeCommandAuraResult(CommandAuraResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "commandAuraResult",
                ["success"] = result.Success,
                ["captainKeyId"] = result.CaptainKeyId ?? string.Empty,
                ["auraType"] = result.AuraType ?? string.Empty,
                ["linkedFollowersCount"] = result.LinkedFollowersCount,
                ["radius"] = result.Radius,
                ["durationSeconds"] = result.DurationSeconds,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        /// <summary>
        /// Serializes battlefield environmental hazard zones and trigger metrics.
        /// Omission counters precede detail collections.
        /// </summary>
        public static string SerializeHazardCensus(HazardZoneCensus census)
        {
            var rows = new List<Dictionary<string, object>>();
            if (census != null && census.ActiveZones != null)
            {
                for (int i = 0; i < census.ActiveZones.Count; i++)
                {
                    HazardZoneDiagnosticRow row = census.ActiveZones[i];
                    rows.Add(new Dictionary<string, object>
                    {
                        ["zoneId"] = row.ZoneId ?? string.Empty,
                        ["hazardType"] = row.HazardType ?? string.Empty,
                        ["centerX"] = row.CenterX,
                        ["centerZ"] = row.CenterZ,
                        ["radius"] = row.Radius,
                        ["tickDamage"] = row.TickDamage,
                        ["tickIntervalSeconds"] = row.TickIntervalSeconds,
                        ["remainingDurationSeconds"] = row.RemainingDurationSeconds,
                        ["affectedEntitiesLastTick"] = row.AffectedEntitiesLastTick,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "hazardZoneCensus",
                ["omittedZoneCount"] = census != null ? census.OmittedZoneCount : 0,
                ["totalActiveZonesCount"] = census != null ? census.TotalActiveZonesCount : 0,
                ["activeZones"] = rows,
            };
            return Serialize(view);
        }

        public static string SerializeHazardCommandResult(HazardCommandResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "hazardCommandResult",
                ["success"] = result.Success,
                ["zoneId"] = result.ZoneId ?? string.Empty,
                ["hazardType"] = result.HazardType ?? string.Empty,
                ["centerX"] = result.CenterX,
                ["centerZ"] = result.CenterZ,
                ["radius"] = result.Radius,
                ["durationSeconds"] = result.DurationSeconds,
                ["tickDamage"] = result.TickDamage,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }


        public static string SerializeUnitLoadoutAndPoolCensus(UnitLoadoutAndPoolCensus census)
        {
            var unitRows = new List<Dictionary<string, object>>();
            if (census != null && census.UnitRows != null)
            {
                for (int i = 0; i < census.UnitRows.Count; i++)
                {
                    UnitLoadoutDiagnosticRow row = census.UnitRows[i];
                    unitRows.Add(new Dictionary<string, object>
                    {
                        ["unitKeyId"] = row.UnitKeyId ?? string.Empty,
                        ["entityId"] = row.EntityId ?? string.Empty,
                        ["activeVariantId"] = row.ActiveVariantId ?? string.Empty,
                        ["loadoutName"] = row.LoadoutName ?? string.Empty,
                        ["isPlayerControlled"] = row.IsPlayerControlled,
                        ["moveSpeed"] = row.MoveSpeed,
                        ["weaponRange"] = row.WeaponRange,
                        ["damage"] = row.Damage,
                        ["attackCooldown"] = row.AttackCooldown,
                    });
                }
            }

            var poolRows = new List<Dictionary<string, object>>();
            if (census != null && census.PoolRows != null)
            {
                for (int i = 0; i < census.PoolRows.Count; i++)
                {
                    PrespawnPoolDiagnosticRow row = census.PoolRows[i];
                    poolRows.Add(new Dictionary<string, object>
                    {
                        ["entityId"] = row.EntityId ?? string.Empty,
                        ["targetQuota"] = row.TargetQuota,
                        ["prespawnedCount"] = row.PrespawnedCount,
                        ["isBuilding"] = row.IsBuilding,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "unitLoadoutAndPoolCensus",
                ["omittedUnitCount"] = census != null ? census.OmittedUnitCount : 0,
                ["totalTrackedUnits"] = census != null ? census.TotalTrackedUnits : 0,
                ["omittedPoolCount"] = census != null ? census.OmittedPoolCount : 0,
                ["totalPoolCount"] = census != null ? census.TotalPoolCount : 0,
                ["units"] = unitRows,
                ["pools"] = poolRows,
            };
            return Serialize(view);
        }

        public static string SerializeUnitLoadoutResult(UnitLoadoutResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "unitLoadoutResult",
                ["success"] = result.Success,
                ["entityKeyId"] = result.EntityKeyId ?? string.Empty,
                ["loadoutId"] = result.LoadoutId ?? string.Empty,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        public static string SerializeUnitVisualVariantResult(UnitVisualVariantResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "unitVisualVariantResult",
                ["success"] = result.Success,
                ["entityKeyId"] = result.EntityKeyId ?? string.Empty,
                ["variantId"] = result.VariantId ?? string.Empty,
                ["presetName"] = result.PresetName ?? string.Empty,
                ["moduleCount"] = result.ModuleCount,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        public static string SerializeVisualVariantCensus(int maxUnits = 64)
        {
            IReadOnlyList<KeyValuePair<string, IReadOnlyList<RuntimeUnitVisualVariant>>> registeredUnits =
                RuntimeUnitVisualVariantRegistry.GetAllUnitsSnapshot();
            int totalUnits = registeredUnits.Count;
            int includedUnitCount = maxUnits > 0 ? Math.Min(totalUnits, maxUnits) : totalUnits;
            int omittedUnits = totalUnits - includedUnitCount;
            var allVariantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var includedVariantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < registeredUnits.Count; i++)
            {
                IReadOnlyList<RuntimeUnitVisualVariant> variants = registeredUnits[i].Value;
                if (variants != null)
                {
                    for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
                    {
                        RuntimeUnitVisualVariant variant = variants[variantIndex];
                        if (variant == null || string.IsNullOrWhiteSpace(variant.VariantId))
                        {
                            continue;
                        }

                        allVariantIds.Add(variant.VariantId);
                        if (i < includedUnitCount)
                        {
                            includedVariantIds.Add(variant.VariantId);
                        }
                    }
                }
            }
            int totalVariants = allVariantIds.Count;
            int omittedVariants = totalVariants - includedVariantIds.Count;

            var unitRows = new List<Dictionary<string, object>>(includedUnitCount);
            for (int unitIndex = 0; unitIndex < includedUnitCount; unitIndex++)
            {
                KeyValuePair<string, IReadOnlyList<RuntimeUnitVisualVariant>> unit = registeredUnits[unitIndex];
                IReadOnlyList<RuntimeUnitVisualVariant> variants = unit.Value ?? Array.Empty<RuntimeUnitVisualVariant>();
                var variantRows = new List<Dictionary<string, object>>(variants.Count);
                RuntimeUnitVisualVariant primaryVariant = null;
                int tacticalVariantCount = 0;

                for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
                {
                    RuntimeUnitVisualVariant variant = variants[variantIndex];
                    if (variant == null)
                    {
                        continue;
                    }

                    if (variant.IsPrimary)
                    {
                        primaryVariant = primaryVariant ?? variant;
                    }
                    else
                    {
                        tacticalVariantCount++;
                    }

                    variantRows.Add(new Dictionary<string, object>
                    {
                        ["variantId"] = variant.VariantId ?? string.Empty,
                        ["tier"] = variant.IsPrimary ? "Level 1 Primary Chassis" : "Level 2 Tactical Variant",
                        ["presetName"] = variant.PresetName ?? string.Empty,
                        ["colorTintHex"] = variant.ColorTintHex ?? string.Empty,
                        ["scale"] = variant.Scale,
                        ["declaredVisualAssetName"] = variant.VisualAssetName ?? string.Empty,
                        ["declaredVisualRootPath"] = variant.VisualRootPath ?? string.Empty,
                        ["moduleCount"] = variant.Modules != null ? variant.Modules.Count : 0,
                        ["modules"] = SerializeVisualVariantModules(variant.Modules),
                        ["runtimeEffect"] = variant.IsPrimary
                            ? "Level 1 authored chassis; Level 2 module slots use chassis defaults"
                            : "visual-only mechanical hardpoint modules with optional tint and scale"
                    });
                }

                unitRows.Add(new Dictionary<string, object>
                {
                    ["unitId"] = unit.Key ?? string.Empty,
                    ["primaryVariantId"] = primaryVariant != null ? primaryVariant.VariantId : string.Empty,
                    ["primaryPresetName"] = primaryVariant != null ? primaryVariant.PresetName : string.Empty,
                    ["tacticalVariantCount"] = tacticalVariantCount,
                    ["variants"] = variantRows
                });
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "visualVariantCensus",
                ["omittedUnitCount"] = omittedUnits,
                ["omittedVariantCount"] = omittedVariants,
                ["totalUnits"] = totalUnits,
                ["totalVariants"] = totalVariants,
                ["systemTier"] = "Two-Level Canonical PBR System (Level 1: Primary Chassis, Level 2: Tactical Variants)",
                ["formAuthority"] = "Level 1 owns chassis geometry, functional hierarchy, rig, pivots, sockets, LOD and PBR authority; Level 2 adds sparse visual-only mechanical modules at canonical hardpoints and may adjust tint or scale.",
                ["units"] = unitRows
            };
            return Serialize(view);
        }

        private static object[] SerializeVisualVariantModules(IReadOnlyList<RuntimeUnitVisualModule> modules)
        {
            if (modules == null || modules.Count == 0) return Array.Empty<object>();
            var rows = new List<object>(modules.Count);
            for (int i = 0; i < modules.Count; i++)
            {
                RuntimeUnitVisualModule module = modules[i];
                if (module == null) continue;
                rows.Add(new Dictionary<string, object>
                {
                    ["slotId"] = module.SlotId ?? string.Empty,
                    ["assetName"] = module.AssetName ?? string.Empty
                });
            }
            return rows.ToArray();
        }

        public static string SerializePrespawnWarmResult(PrespawnWarmResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "prespawnWarmResult",
                ["success"] = result.Success,
                ["entityId"] = result.EntityId ?? string.Empty,
                ["requestedCount"] = result.RequestedCount,
                ["warmedCount"] = result.WarmedCount,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        public static string SerializeAiDoctrineAndDeckCensus(AiDoctrineAndDeckCensus census)
        {
            var deckRows = new List<Dictionary<string, object>>();
            if (census != null && census.DeckEntries != null)
            {
                for (int i = 0; i < census.DeckEntries.Count; i++)
                {
                    AiDeckEntryDiagnosticRow row = census.DeckEntries[i];
                    deckRows.Add(new Dictionary<string, object>
                    {
                        ["baseIndex"] = row.BaseIndex,
                        ["unitId"] = row.UnitId ?? string.Empty,
                        ["countAtStart"] = row.CountAtStart,
                        ["maxLiving"] = row.MaxLiving,
                        ["currentLivingCount"] = row.CurrentLivingCount,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "aiDoctrineAndDeckCensus",
                ["omittedDeckEntryCount"] = census != null ? census.OmittedDeckEntryCount : 0,
                ["totalDeckEntries"] = census != null ? census.TotalDeckEntries : 0,
                ["activeDoctrine"] = census != null ? census.ActiveDoctrine : "Standard",
                ["aggressionMultiplier"] = census != null ? census.AggressionMultiplier : 1.0f,
                ["profileCount"] = census != null ? census.ProfileCount : 0,
                ["deckEntries"] = deckRows,
            };
            return Serialize(view);
        }

        public static string SerializeAiDoctrineResult(AiDoctrineResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "aiDoctrineResult",
                ["success"] = result.Success,
                ["doctrineName"] = result.DoctrineName ?? string.Empty,
                ["aggressionMultiplier"] = result.AggressionMultiplier,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        public static string SerializeAiDeckInjectionResult(AiDeckInjectionResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "aiDeckInjectionResult",
                ["success"] = result.Success,
                ["baseIndex"] = result.BaseIndex,
                ["unitId"] = result.UnitId ?? string.Empty,
                ["countAtStart"] = result.CountAtStart,
                ["maxLiving"] = result.MaxLiving,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        public static string SerializeAgentCoordinationCensus(AgentCoordinationSnapshot snapshot)
        {
            var sessions = new List<Dictionary<string, object>>();
            if (snapshot != null && snapshot.ActiveSessions != null)
            {
                for (int i = 0; i < snapshot.ActiveSessions.Count; i++)
                {
                    AgentSessionRecord s = snapshot.ActiveSessions[i];
                    sessions.Add(new Dictionary<string, object>
                    {
                        ["agentId"] = s.AgentId ?? string.Empty,
                        ["agentRole"] = s.AgentRole ?? string.Empty,
                        ["notes"] = s.Notes ?? string.Empty,
                        ["status"] = s.Status ?? "Active",
                        ["secondsSinceLastPing"] = s.SecondsSinceLastPing,
                        ["assignedSector"] = s.AssignedSector ?? "Global",
                        ["capabilities"] = s.Capabilities != null ? s.Capabilities : (object)Array.Empty<string>(),
                        ["commandsDispatchedCount"] = s.CommandsDispatchedCount,
                        ["registeredAtSeconds"] = s.RegisteredAtSeconds,
                        ["lastPingSeconds"] = s.LastPingSeconds,
                        ["pingCount"] = s.PingCount,
                    });
                }
            }

            var locks = new List<Dictionary<string, object>>();
            if (snapshot != null && snapshot.ActiveLocks != null)
            {
                for (int i = 0; i < snapshot.ActiveLocks.Count; i++)
                {
                    AgentAdvisoryLock l = snapshot.ActiveLocks[i];
                    locks.Add(new Dictionary<string, object>
                    {
                        ["resourceKey"] = l.ResourceKey ?? string.Empty,
                        ["resourceCategory"] = l.ResourceCategory ?? "Global",
                        ["ownerAgentId"] = l.OwnerAgentId ?? string.Empty,
                        ["acquiredAtSeconds"] = l.AcquiredAtSeconds,
                        ["expiresAtSeconds"] = l.ExpiresAtSeconds,
                        ["remainingSeconds"] = l.RemainingSeconds,
                        ["contentionCount"] = l.ContentionCount,
                    });
                }
            }

            var messages = new List<Dictionary<string, object>>();
            if (snapshot != null && snapshot.RecentMessages != null)
            {
                for (int i = 0; i < snapshot.RecentMessages.Count; i++)
                {
                    AgentCoordinationMessage m = snapshot.RecentMessages[i];
                    messages.Add(new Dictionary<string, object>
                    {
                        ["messageId"] = m.MessageId ?? string.Empty,
                        ["senderAgentId"] = m.SenderAgentId ?? string.Empty,
                        ["messageType"] = m.MessageType ?? string.Empty,
                        ["content"] = m.Content ?? string.Empty,
                        ["timestampSeconds"] = m.TimestampSeconds,
                        ["targetAgentId"] = m.TargetAgentId ?? "all",
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "agentCoordinationCensus",
                ["omittedSessionCount"] = snapshot != null ? snapshot.OmittedSessionCount : 0,
                ["omittedLockCount"] = snapshot != null ? snapshot.OmittedLockCount : 0,
                ["omittedMessageCount"] = snapshot != null ? snapshot.OmittedMessageCount : 0,
                ["totalSessionCount"] = snapshot != null ? snapshot.TotalSessionCount : sessions.Count,
                ["totalLockCount"] = snapshot != null ? snapshot.TotalLockCount : locks.Count,
                ["totalMessageCount"] = snapshot != null ? snapshot.TotalMessageCount : messages.Count,
                ["activeSessionCount"] = sessions.Count,
                ["activeLockCount"] = locks.Count,
                ["activeMessageCount"] = messages.Count,
                ["sessions"] = sessions,
                ["locks"] = locks,
                ["messages"] = messages,
            };
            return Serialize(view);
        }

        public static string SerializeAgentMessageResult(bool success, string messageId, string senderAgentId, string messageType, string message)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "agentMessageResult",
                ["success"] = success,
                ["messageId"] = messageId ?? string.Empty,
                ["senderAgentId"] = senderAgentId ?? string.Empty,
                ["messageType"] = messageType ?? string.Empty,
                ["message"] = message ?? string.Empty,
            };
            return Serialize(view);
        }

        public static string SerializeAgentBatchLockResult(bool success, string agentId, int requestedCount, int processedCount, string action, string message)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "agentBatchLockResult",
                ["success"] = success,
                ["agentId"] = agentId ?? string.Empty,
                ["requestedCount"] = requestedCount,
                ["processedCount"] = processedCount,
                ["action"] = action ?? string.Empty,
                ["message"] = message ?? string.Empty,
            };
            return Serialize(view);
        }

        public static string SerializeAgentOperationResult(bool success, string agentId, string action, string message)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "agentOperationResult",
                ["success"] = success,
                ["agentId"] = agentId ?? string.Empty,
                ["action"] = action ?? string.Empty,
                ["message"] = message ?? string.Empty,
            };
            return Serialize(view);
        }

        public static string SerializeSystemHealth(
            LoaderDiagnosticsSnapshot snapshot,
            AiModulesCensus aiCensus,
            EconomyCensus economyCensus,
            CombatStatusCensus combatCensus,
            int threatActiveBehaviourCount = 0,
            int threatProcessedAiUnitCount = 0,
            int threatProcessedAlliedUnitCount = 0,
            int threatLastRetargetedCount = 0,
            int threatTotalRetargetCommandCount = 0,
            int battlefieldEntityCount = 0,
            int livePlayerUnitCount = 0,
            int liveAiUnitCount = 0,
            int liveAiBuildingCount = 0,
            int liveHarvesterCount = 0,
            int totalFactoryCount = 0,
            int activeProducingFactoryCount = 0,
            int queuedUnitCount = 0,
            bool hasPlayerHq = false,
            float playerHqHealthRatio = 0f,
            float aiStructureHealthRatio = 0f,
            int aiRosterUnitCount = 0,
            int aiRosterBuildingCount = 0)
        {
            var view = new Dictionary<string, object>
            {
                ["isNativeRegistryReady"] = snapshot != null && snapshot.IsNativeRegistryReady,
                ["activePackCount"] = snapshot != null ? snapshot.ActivePackCount : 0,
                ["failedPackCount"] = snapshot != null ? snapshot.FailedPackCount : 0,
                ["registeredUnitCount"] = snapshot != null && snapshot.Units != null ? snapshot.Units.Count : 0,
                ["registeredAiModuleCount"] = aiCensus != null ? aiCensus.RegisteredModuleCount : 0,
                ["playerCredits"] = economyCensus != null ? economyCensus.PlayerCredits : 0,
                ["aiCredits"] = economyCensus != null ? economyCensus.AiCredits : 0,
                ["attackWaveNumber"] = economyCensus != null ? economyCensus.AttackWaveNumber : 0,
                ["elapsedGameTimeSeconds"] = economyCensus != null ? economyCensus.ElapsedGameTimeSeconds : 0f,
                ["omittedCombatUnitCount"] = combatCensus != null ? combatCensus.OmittedUnitCount : 0,
                ["totalCombatActiveUnitCount"] = combatCensus != null ? combatCensus.TotalCombatActiveUnitCount : 0,
                ["unitsWithActiveStatusEffectsCount"] = combatCensus != null ? combatCensus.UnitsWithActiveStatusEffectsCount : 0,
                ["threatActiveBehaviourCount"] = threatActiveBehaviourCount < 0 ? 0 : threatActiveBehaviourCount,
                ["threatProcessedAiUnitCount"] = threatProcessedAiUnitCount < 0 ? 0 : threatProcessedAiUnitCount,
                ["threatProcessedAlliedUnitCount"] = threatProcessedAlliedUnitCount < 0 ? 0 : threatProcessedAlliedUnitCount,
                ["threatLastRetargetedCount"] = threatLastRetargetedCount < 0 ? 0 : threatLastRetargetedCount,
                ["threatTotalRetargetCommandCount"] = threatTotalRetargetCommandCount < 0 ? 0 : threatTotalRetargetCommandCount,
                ["battlefieldEntityCount"] = battlefieldEntityCount < 0 ? 0 : battlefieldEntityCount,
                ["livePlayerUnitCount"] = livePlayerUnitCount < 0 ? 0 : livePlayerUnitCount,
                ["liveAiUnitCount"] = liveAiUnitCount < 0 ? 0 : liveAiUnitCount,
                ["liveAiBuildingCount"] = liveAiBuildingCount < 0 ? 0 : liveAiBuildingCount,
                ["liveHarvesterCount"] = liveHarvesterCount < 0 ? 0 : liveHarvesterCount,
                ["totalFactoryCount"] = totalFactoryCount < 0 ? 0 : totalFactoryCount,
                ["activeProducingFactoryCount"] = activeProducingFactoryCount < 0 ? 0 : activeProducingFactoryCount,
                ["queuedUnitCount"] = queuedUnitCount < 0 ? 0 : queuedUnitCount,
                ["hasPlayerHq"] = hasPlayerHq,
                ["playerHqHealthRatio"] = playerHqHealthRatio < 0f ? 0f : (playerHqHealthRatio > 1f ? 1f : playerHqHealthRatio),
                ["aiStructureHealthRatio"] = aiStructureHealthRatio < 0f ? 0f : (aiStructureHealthRatio > 1f ? 1f : aiStructureHealthRatio),
                ["aiRosterUnitCount"] = aiRosterUnitCount < 0 ? 0 : aiRosterUnitCount,
                ["aiRosterBuildingCount"] = aiRosterBuildingCount < 0 ? 0 : aiRosterBuildingCount,
                ["recentLogCount"] = snapshot != null && snapshot.RecentLogs != null ? snapshot.RecentLogs.Count : 0,
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
                ["kind"] = "rangeColors",
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
                ["kind"] = "identifierPalette",
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

        public static string SerializeDiagnosticCatalog()
        {
            var diagnostics = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { ["name"] = "GetRuntimeHooksStateJson", ["description"] = "Installed Harmony hooks and registered combat XP count." },
                new Dictionary<string, object> { ["name"] = "GetInstalledPatchNamesJson", ["description"] = "Names of installed Harmony patches." },
                new Dictionary<string, object> { ["name"] = "GetRegisteredProfilesJson", ["description"] = "Registered AI module profile ids." },
                new Dictionary<string, object> { ["name"] = "GetAppliedBaselineEffectsJson", ["description"] = "Applied baseline effects count and names." },
                new Dictionary<string, object> { ["name"] = "GetCombatXpEntriesJson", ["description"] = "Combat XP entries by entity key." },
                new Dictionary<string, object> { ["name"] = "GetBattlefieldCensusJson", ["description"] = "Live battlefield census counts and player unit rows." },
                new Dictionary<string, object> { ["name"] = "GetEntityBalanceJson", ["description"] = "Base balance parameters for one unit by entity id." },
                new Dictionary<string, object> { ["name"] = "GetCardChangeRequestJson", ["description"] = "Serialized mod-authored card change request." },
                new Dictionary<string, object> { ["name"] = "GetAiRosterJson", ["description"] = "Live AI units and buildings with base balance params." },
                new Dictionary<string, object> { ["name"] = "GetPlayerRosterJson", ["description"] = "Live player units with base balance params." },
                new Dictionary<string, object> { ["name"] = "GetThreatPrioritizationCensusJson", ["description"] = "Aggregated threat-prioritization telemetry." },
                new Dictionary<string, object> { ["name"] = "GetUnitThreatAssessmentJson", ["description"] = "Per-unit threat score (DPS x health ratio)." },
                new Dictionary<string, object> { ["name"] = "GetMatchSummaryJson", ["description"] = "Unified match summary across battlefield and AI roster." },
                new Dictionary<string, object> { ["name"] = "GetEconomyMatchCensusJson", ["description"] = "Match economy, wave, and harvester census." },
                new Dictionary<string, object> { ["name"] = "GetCombatStatusCensusJson", ["description"] = "Combat-active status effects census." },
                new Dictionary<string, object> { ["name"] = "GetProductionCensusJson", ["description"] = "Live factory production queue metrics and active producing counts." },
                new Dictionary<string, object> { ["name"] = "GetBaseIntegrityCensusJson", ["description"] = "Player command HQ and enemy base structure integrity telemetry." },
                new Dictionary<string, object> { ["name"] = "OrderFactoryProduceJson", ["description"] = "Trigger in-game unit production order on a player factory." },
                new Dictionary<string, object> { ["name"] = "GetRunProgressionCensusJson", ["description"] = "Run progression parameters, active relics, offered blueprints, and scene telemetry." },
                new Dictionary<string, object> { ["name"] = "GetRuntimeControlSurfaceJson", ["description"] = "Single-Unity-thread accessible UI snapshot with native Shop/RemoveCardView, conditional intermission, route, reward/deck, and navigator state." },
                new Dictionary<string, object> { ["name"] = "GetIntermissionNativePanelStateJson", ["description"] = "Bounded current native Shop offers and RemoveCardView candidates with reroll/removal readiness." },
                new Dictionary<string, object> { ["name"] = "OrderBuyIntermissionShopOfferJson", ["description"] = "Invoke one current native Shop offer by scene-local offer id and report observed postconditions." },
                new Dictionary<string, object> { ["name"] = "OrderRerollIntermissionShopJson", ["description"] = "Invoke the active native Shop reroll control and report observed postconditions." },
                new Dictionary<string, object> { ["name"] = "OrderRemoveIntermissionCardJson", ["description"] = "Invoke native RemoveCardView.RemoveCard for one unique current CardNew entity id." },
                new Dictionary<string, object> { ["name"] = "OrderCancelIntermissionCardRemovalJson", ["description"] = "Invoke the native RemoveCardView cancellation control." },
                new Dictionary<string, object> { ["name"] = "GrantPlayerCreditsJson", ["description"] = "Grant economy credits to player bank balance." },
                new Dictionary<string, object> { ["name"] = "ApplyInGameCardChangeJson", ["description"] = "Apply a data-driven card change to an entity via native EntityBalancingStore." },
                new Dictionary<string, object> { ["name"] = "OrderUnitMoveJson", ["description"] = "Dispatch an in-game move command to a unit by entity key id." },
                new Dictionary<string, object> { ["name"] = "OrderUnitAttackJson", ["description"] = "Dispatch an in-game attack command on a target entity." },
                new Dictionary<string, object> { ["name"] = "OrderUnitTacticalPulseJson", ["description"] = "Execute a bounded Shockwave, Rally, or Repair pulse around a unit." },
                new Dictionary<string, object> { ["name"] = "OrderDirectorThreatSweepJson", ["description"] = "Apply a bounded Mark or Suppress sweep to nearby hostile combat units." },
new Dictionary<string, object> { ["name"] = "OrderCoordinatedAssaultJson", ["description"] = "Coordinate a bounded attack against one hostile target using only eligible hostile combat attackers." },
new Dictionary<string, object> { ["name"] = "OrderRetreatJson", ["description"] = "Order an evasive retreat away from a threat position." },
                new Dictionary<string, object> { ["name"] = "OrderUnitActiveSkillJson", ["description"] = "Dispatch an active tactical skill command on a unit (Self, Target, or Position)." },
                new Dictionary<string, object> { ["name"] = "AdjustEntityVitalityJson", ["description"] = "Safely adjust health, armor, shield, or damage on an entity for testing." },
                new Dictionary<string, object> { ["name"] = "ApplyStatusEffectJson", ["description"] = "Apply a status effect (e.g. Emp, Burn, SlowedDown, Stun) to an entity." },
                new Dictionary<string, object> { ["name"] = "ClearStatusEffectJson", ["description"] = "Clear an active status effect from an entity." },
                new Dictionary<string, object> { ["name"] = "GetGridMetricsJson", ["description"] = "Telemetry of terrain dimensions, cell counts, and start cells." },
                new Dictionary<string, object> { ["name"] = "EvaluateGridPositionJson", ["description"] = "Evaluate an (X, Z) coordinate against the battlefield grid bounds." },
                new Dictionary<string, object> { ["name"] = "GetSystemHealthJson", ["description"] = "Unified system health across subsystems." },
                new Dictionary<string, object> { ["name"] = "GetSelectionCensusJson", ["description"] = "Telemetry of active player selection and control group assignments." },
                new Dictionary<string, object> { ["name"] = "GetFogStateJson", ["description"] = "Telemetry of Fog of War state and update intervals." },
                new Dictionary<string, object> { ["name"] = "GetEntityCatalogJson", ["description"] = "Catalog of registered units, buildings, and factories." },
                new Dictionary<string, object> { ["name"] = "SelectUnitsJson", ["description"] = "Select in-game units by entity key IDs." },
                new Dictionary<string, object> { ["name"] = "DeselectAllUnitsJson", ["description"] = "Deselect all currently selected units." },
                new Dictionary<string, object> { ["name"] = "SelectCategoryJson", ["description"] = "Select units by category (CombatUnits, Harvesters, IdleHarvesters, SkillBuildings, Factories)." },
                new Dictionary<string, object> { ["name"] = "OrderUnitStopJson", ["description"] = "Order Stop on a unit or all selected units." },
                new Dictionary<string, object> { ["name"] = "OrderUnitHoldPositionJson", ["description"] = "Order Hold Position on a unit or all selected units." },
                new Dictionary<string, object> { ["name"] = "OrderUnitAttackMoveJson", ["description"] = "Order AttackMove toward coordinates." },
                new Dictionary<string, object> { ["name"] = "AssignControlGroupJson", ["description"] = "Assign selected units to control group (0-9)." },
                new Dictionary<string, object> { ["name"] = "SelectControlGroupJson", ["description"] = "Select units assigned to control group (0-9)." },
                new Dictionary<string, object> { ["name"] = "SetFogOfWarJson", ["description"] = "Enable or disable Fog of War." },
                new Dictionary<string, object> { ["name"] = "RevealFogOfWarJson", ["description"] = "Reveal Fog of War across the entire map." },
                new Dictionary<string, object> { ["name"] = "SetGameTimeScaleJson", ["description"] = "Adjust match time scale (speed/pause)." },
                new Dictionary<string, object> { ["name"] = "GetGameTimeScaleJson", ["description"] = "Query current match time scale." },
                new Dictionary<string, object> { ["name"] = "TriggerMatchEndJson", ["description"] = "Trigger match victory or defeat." },
                new Dictionary<string, object> { ["name"] = "SpawnEntityJson", ["description"] = "Spawn a unit or building entity at coordinates." },
                new Dictionary<string, object> { ["name"] = "GetAiDirectorCensusJson", ["description"] = "Telemetry of native AI Director, attack wave progress, and command queues." },
                new Dictionary<string, object> { ["name"] = "SetAttackWaveNumberJson", ["description"] = "Set current AI attack wave number." },
                new Dictionary<string, object> { ["name"] = "IncrementAttackWaveJson", ["description"] = "Increment AI attack wave number by 1." },
                new Dictionary<string, object> { ["name"] = "ToggleAiDebugVisualsJson", ["description"] = "Toggle or set AI debug visual tags and tactical commands." },
                new Dictionary<string, object> { ["name"] = "GetCameraCensusJson", ["description"] = "Telemetry of camera viewport, ground coordinates, and FOV." },
                new Dictionary<string, object> { ["name"] = "MoveCameraJson", ["description"] = "Move camera to ground coordinates (X, Z)." },
                new Dictionary<string, object> { ["name"] = "FocusCameraOnEntityJson", ["description"] = "Center camera on entity by key ID." },
                new Dictionary<string, object> { ["name"] = "SetCameraZoomJson", ["description"] = "Set camera FOV / zoom level." },
                new Dictionary<string, object> { ["name"] = "GetHarvesterCensusJson", ["description"] = "Telemetry of active harvesters, crystal payloads, and states." },
                new Dictionary<string, object> { ["name"] = "OrderHarvesterHarvestJson", ["description"] = "Order harvester to harvest nearest crystal." },
                new Dictionary<string, object> { ["name"] = "OrderHarvesterReturnJson", ["description"] = "Order harvester to return to refinery." },
                new Dictionary<string, object> { ["name"] = "GetAudioCensusJson", ["description"] = "Telemetry of audio mixer volume slider levels." },
new Dictionary<string, object> { ["name"] = "GetVisualSurfaceCensusJson", ["description"] = "Telemetry of visual surface materials and vertex-wear channels (welds, mud, micro-relief)." },
                new Dictionary<string, object> { ["name"] = "GetVisualQualityCensusJson", ["description"] = "URP/PBR material compliance, texture bindings, instancing, and procedural vertex-wear quality metrics." },
                new Dictionary<string, object> { ["name"] = "GetVisualHealthSummaryJson", ["description"] = "Consolidated visual health verdict across quality, surface, material, and socket censuses." },
                new Dictionary<string, object> { ["name"] = "SetAudioVolumeJson", ["description"] = "Set volume slider level for Master, Music, Gameplay, UI." },
                new Dictionary<string, object> { ["name"] = "GetTechCatalogJson", ["description"] = "Catalog of registered upgrades, research branches, and relics." },
                                new Dictionary<string, object> { ["name"] = "GetVeterancyCensusJson", ["description"] = "Live combat XP, rank tiers, and veterancy bonus multipliers." },
                new Dictionary<string, object> { ["name"] = "OrderGrantCombatXpJson", ["description"] = "Grant combat experience (XP) to a unit." },
                new Dictionary<string, object> { ["name"] = "OrderSetVeterancyLevelJson", ["description"] = "Set veterancy level of a unit directly." },
                new Dictionary<string, object> { ["name"] = "GetCommandAuraCensusJson", ["description"] = "Live captain command auras and linked follower chains." },
                new Dictionary<string, object> { ["name"] = "OrderCommandAuraPulseJson", ["description"] = "Dispatch dynamic command aura pulse from a captain entity." },
                new Dictionary<string, object> { ["name"] = "OrderClearCommandAuraLinksJson", ["description"] = "Clear command aura links for a captain entity." },
                new Dictionary<string, object> { ["name"] = "GetHazardCensusJson", ["description"] = "Live battlefield environmental hazard zones and trigger collider metrics." },
                new Dictionary<string, object> { ["name"] = "OrderSpawnHazardZoneJson", ["description"] = "Spawn a battlefield environmental hazard zone." },
                new Dictionary<string, object> { ["name"] = "OrderClearHazardZonesJson", ["description"] = "Clear all active battlefield environmental hazard zones." },
                new Dictionary<string, object> { ["name"] = "GetAgentCoordinationCensusJson", ["description"] = "Multi-agent coordination census: active agent sessions, heartbeats, and advisory locks." },
                new Dictionary<string, object> { ["name"] = "RegisterAgentSessionJson", ["description"] = "Register or refresh an agent session with role and notes." },
                new Dictionary<string, object> { ["name"] = "PingAgentJson", ["description"] = "Send an agent heartbeat ping to the game runtime." },
                new Dictionary<string, object> { ["name"] = "AcquireAgentLockJson", ["description"] = "Acquire or renew an advisory lock on a game resource." },
                new Dictionary<string, object> { ["name"] = "ReleaseAgentLockJson", ["description"] = "Release an advisory lock on a game resource." },
                new Dictionary<string, object> { ["name"] = "GetLoadedPrefabVisualAuditJson", ["description"] = "Load-time visual, rig, shader, and socket audit for loaded unit and building prefabs." },
                new Dictionary<string, object> { ["name"] = "GetLoaderAuditCensusJson", ["description"] = "Runtime readiness audit of loader-registered units, buildings, pre-spawn pools, and AI profiles." },
                new Dictionary<string, object> { ["name"] = "OrderReconcileLoaderStateJson", ["description"] = "Actively reconcile loader runtime state: warm pre-spawn pools, attach visual/mechanics directors, and apply AI squad defaults." },
                new Dictionary<string, object> { ["name"] = "GetUnitMechanicsCensusJson", ["description"] = "Live census of active unit mechanics: healing, auras, shield regen, XP/veterancy, combat stances, and visual director state." },
                new Dictionary<string, object> { ["name"] = "OrderUnitVisualFeedbackJson", ["description"] = "Order visual feedback pulse (WeaponRecoil, JamTurret, UnjamTurret, OpenBayDoors, CloseBayDoors, FootstepPulse) on a unit." },
                new Dictionary<string, object> { ["name"] = "OrderUnitAttachmentJson", ["description"] = "Command hardpoint attachment state (APS, ERA, FuelDrums, Armor, Shield) on a unit visual hierarchy." },
                new Dictionary<string, object> { ["name"] = "GetUpgradeCensusJson", ["description"] = "Telemetry census of registered card upgrades in the UpgradeBalancingStore." },
                new Dictionary<string, object> { ["name"] = "GetRelicCensusJson", ["description"] = "Telemetry census of registered player relics in the RelicBalancingStore." },
                new Dictionary<string, object> { ["name"] = "GetDiagnosticCatalogJson", ["description"] = "Catalog of available diagnostic getters." },
            };

            var view = new Dictionary<string, object>
            {
                ["kind"] = "diagnosticCatalog",
                ["diagnosticCount"] = diagnostics.Count,
                ["diagnostics"] = diagnostics,
            };
            return Serialize(view);
        }

        public static string SerializeLoaderRuntimeAuditCensus(LoaderRuntimeAuditCensus census)
        {
            var unitRows = new List<Dictionary<string, object>>();
            if (census != null && census.UnitRows != null)
            {
                for (int i = 0; i < census.UnitRows.Count; i++)
                {
                    LoaderUnitAuditRow r = census.UnitRows[i];
                    unitRows.Add(new Dictionary<string, object>
                    {
                        ["unitId"] = r.UnitId ?? string.Empty,
                        ["packId"] = r.PackId ?? string.Empty,
                        ["factoryId"] = r.FactoryId ?? string.Empty,
                        ["isBuilding"] = r.IsBuilding,
                        ["hasMechanicsProfile"] = r.HasMechanicsProfile,
                        ["variantCount"] = r.VariantCount,
                        ["prefabAudited"] = r.PrefabAudited,
                        ["rigType"] = r.RigType ?? "StaticMesh",
                        ["hasAmbiguousSockets"] = r.HasAmbiguousSockets,
                        ["targetPrespawnQuota"] = r.TargetPrespawnQuota,
                        ["prespawnedPoolCount"] = r.PrespawnedPoolCount,
                        ["livingCount"] = r.LivingCount,
                        ["defaultStance"] = r.DefaultStance ?? string.Empty,
                        ["defaultLoadout"] = r.DefaultLoadout ?? string.Empty,
                        ["startingVeterancyRank"] = r.StartingVeterancyRank,
                    });
                }
            }

            var aiRows = new List<Dictionary<string, object>>();
            if (census != null && census.AiProfileRows != null)
            {
                for (int i = 0; i < census.AiProfileRows.Count; i++)
                {
                    LoaderAiProfileAuditRow p = census.AiProfileRows[i];
                    aiRows.Add(new Dictionary<string, object>
                    {
                        ["profileId"] = p.ProfileId ?? string.Empty,
                        ["packId"] = p.PackId ?? string.Empty,
                        ["prefabAssetName"] = p.PrefabAssetName ?? string.Empty,
                        ["moduleCount"] = p.ModuleCount,
                        ["activeInstanceCount"] = p.ActiveInstanceCount,
                        ["doctrine"] = p.Doctrine ?? "Standard",
                        ["aggressionMultiplier"] = p.AggressionMultiplier,
                        ["defaultUnitStance"] = p.DefaultUnitStance ?? string.Empty,
                        ["defaultUnitLoadout"] = p.DefaultUnitLoadout ?? string.Empty,
                        ["startingUnitAffixes"] = p.StartingUnitAffixes != null ? p.StartingUnitAffixes : (object)Array.Empty<string>(),
                        ["startingUnitVeterancyRank"] = p.StartingUnitVeterancyRank,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "loaderRuntimeAuditCensus",
                ["omittedUnitCount"] = census != null ? census.OmittedUnitCount : 0,
                ["totalRegisteredUnits"] = census != null ? census.TotalRegisteredUnits : 0,
                ["omittedAiProfileCount"] = census != null ? census.OmittedAiProfileCount : 0,
                ["totalRegisteredAiProfiles"] = census != null ? census.TotalRegisteredAiProfiles : 0,
                ["mechanicsProfileCount"] = census != null ? census.MechanicsProfileCount : 0,
                ["auditedPrefabCount"] = census != null ? census.AuditedPrefabCount : 0,
                ["warmedPoolUnitCount"] = census != null ? census.WarmedPoolUnitCount : 0,
                ["activeAiInstanceCount"] = census != null ? census.ActiveAiInstanceCount : 0,
                ["units"] = unitRows,
                ["aiProfiles"] = aiRows,
            };
            return Serialize(view);
        }

        public static string SerializeLoaderReconciliationResult(LoaderReconciliationResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "loaderReconciliationResult",
                ["success"] = result.Success,
                ["reconciledUnitCount"] = result.ReconciledUnitCount,
                ["warmedPoolCount"] = result.WarmedPoolCount,
                ["attachedMechanicsCount"] = result.AttachedMechanicsCount,
                ["attachedVisualDirectorCount"] = result.AttachedVisualDirectorCount,
                ["appliedAiUnitDefaultsCount"] = result.AppliedAiUnitDefaultsCount,
                ["reconciledAiProfileCount"] = result.ReconciledAiProfileCount,
                ["message"] = result.Message ?? string.Empty,
            };
            return Serialize(view);
        }

        public static string SerializeUnitMechanicsCensus(UnitMechanicsCensus census)
        {
            var unitRows = new List<Dictionary<string, object>>();
            if (census != null && census.Rows != null)
            {
                for (int i = 0; i < census.Rows.Count; i++)
                {
                    UnitMechanicsCensusRow r = census.Rows[i];
                    if (r == null) continue;

                    unitRows.Add(new Dictionary<string, object>
                    {
                        ["entityKeyId"] = r.EntityKeyId ?? string.Empty,
                        ["entityId"] = r.EntityId ?? string.Empty,
                        ["isControlledByPlayer"] = r.IsControlledByPlayer,
                        ["healthRatio"] = r.HealthRatio,
                        ["shieldRatio"] = r.ShieldRatio,
                        ["tacticalStance"] = r.TacticalStance ?? "Standard",
                        ["loadoutVariant"] = r.LoadoutVariant ?? "Standard",
                        ["veterancyRank"] = r.VeterancyRank,
                        ["combatXp"] = r.CombatXp,
                        ["affixes"] = r.Affixes != null ? r.Affixes : (object)Array.Empty<string>(),
                        ["hasHealing"] = r.HasHealing,
                        ["hasAura"] = r.HasAura,
                        ["hasShieldRegen"] = r.HasShieldRegen,
                        ["criticalHitChance"] = r.CriticalHitChance,
                        ["minAttackRange"] = r.MinAttackRange,
                        ["activeStatusEffectsCount"] = r.ActiveStatusEffectsCount,
                        ["activeStatusEffectsSummary"] = r.ActiveStatusEffectsSummary ?? string.Empty,
                        ["hasVisualDirector"] = r.HasVisualDirector,
                        ["isVisualTurretJammed"] = r.IsVisualTurretJammed,
                        ["visualRecoilOffset"] = r.VisualRecoilOffset,
                        ["visualDoorProgress"] = r.VisualDoorProgress,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "unitMechanicsCensus",
                ["omittedUnitCount"] = census != null ? census.OmittedUnitCount : 0,
                ["totalTrackedUnits"] = census != null ? census.TotalTrackedUnits : 0,
                ["totalMechanicsInstancesCount"] = census != null ? census.TotalMechanicsInstancesCount : 0,
                ["activeHealerCount"] = census != null ? census.ActiveHealerCount : 0,
                ["activeAuraCount"] = census != null ? census.ActiveAuraCount : 0,
                ["activeShieldRegenCount"] = census != null ? census.ActiveShieldRegenCount : 0,
                ["units"] = unitRows,
            };
            return Serialize(view);
        }

        public static string SerializeVisualFeedbackResult(VisualFeedbackResult result)
        {
            var view = new Dictionary<string, object>
            {
                ["kind"] = "visualFeedbackResult",
                ["success"] = result != null ? result.Success : false,
                ["entityKeyId"] = result != null ? (result.EntityKeyId ?? string.Empty) : string.Empty,
                ["feedbackType"] = result != null ? (result.FeedbackType ?? string.Empty) : string.Empty,
                ["message"] = result != null ? (result.Message ?? string.Empty) : string.Empty,
            };
            return Serialize(view);
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

        public static string SerializeAiArchitectureCensus(RogueCommandExpansionLoader.Runtime.Ai.AiArchitectureCensus census)
        {
            RogueCommandExpansionLoader.Runtime.Ai.AiArchitectureCensus c =
                census ?? RogueCommandExpansionLoader.Runtime.Ai.AiArchitectureCensus.Empty;

            var instanceRows = new List<Dictionary<string, object>>();
            if (c.Instances != null)
            {
                for (int i = 0; i < c.Instances.Count; i++)
                {
                    RogueCommandExpansionLoader.Runtime.Ai.AiArchitectureInstanceDiagnosticRow row = c.Instances[i];

                    instanceRows.Add(new Dictionary<string, object>
                    {
                        ["profileId"] = row.ProfileId,
                        ["coordinatedModuleCount"] = row.CoordinatedModuleCount,
                        ["activeDoctrine"] = row.ActiveDoctrine,
                        ["trackedAiCombatUnits"] = row.TrackedAiCombatUnits,
                        ["trackedHostileCombatUnits"] = row.TrackedHostileCombatUnits,
                        ["trackedHazardZones"] = row.TrackedHazardZones,
                        ["underHarvesterOrBasePressure"] = row.UnderHarvesterOrBasePressure,
                        ["approvedOrdersLastTick"] = row.ApprovedOrdersLastTick,
                        ["conflictsResolvedLastTick"] = row.ConflictsResolvedLastTick,
                        ["lockoutBlockedLastTick"] = row.LockoutBlockedLastTick,
                        ["hysteresisBlockedLastTick"] = row.HysteresisBlockedLastTick,
                        ["budgetOmittedLastTick"] = row.BudgetOmittedLastTick
                    });
                }
            }

            var root = new Dictionary<string, object>
            {
                ["omittedInstanceCount"] = c.OmittedInstanceCount,
                ["kind"] = "aiArchitectureCensus",
                ["activeCompositeCount"] = c.ActiveCompositeCount,
                ["totalCoordinatedModuleCount"] = c.TotalCoordinatedModuleCount,
                ["totalTrackedHazardZones"] = c.TotalTrackedHazardZones,
                ["totalApprovedOrdersLastTick"] = c.TotalApprovedOrdersLastTick,
                ["totalConflictsResolvedLastTick"] = c.TotalConflictsResolvedLastTick,
                ["totalLockoutBlockedLastTick"] = c.TotalLockoutBlockedLastTick,
                ["totalHysteresisBlockedLastTick"] = c.TotalHysteresisBlockedLastTick,
                ["totalBudgetOmittedLastTick"] = c.TotalBudgetOmittedLastTick,
                ["instances"] = instanceRows
            };

            return Serialize(root);
        }

        internal static string SerializePrimitivePayload(Dictionary<string, object> view)
        {
            return Serialize(view);
        }

        private static string Serialize(Dictionary<string, object> view)
        {
            int estimatedCapacity = view != null ? Math.Max(512, view.Count * 64) : 256;
            var builder = new StringBuilder(estimatedCapacity);
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
                case IList list:
                    WriteList(builder, list);
                    break;
                case IEnumerable sequence when !(value is string):
                    WriteArray(builder, sequence);
                    break;
                case float single:
                    WriteFiniteNumber(builder, single);
                    break;
                case double number:
                    WriteFiniteNumber(builder, number);
                    break;
                default:
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        private static void WriteFiniteNumber(StringBuilder builder, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                builder.Append("null");
                return;
            }

            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteFiniteNumber(StringBuilder builder, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                builder.Append("null");
                return;
            }

            builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
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

        private static void WriteList(StringBuilder builder, IList list)
        {
            builder.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                WriteValue(builder, list[i]);
            }
            builder.Append(']');
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
            if (text == null)
            {
                builder.Append("\"\"");
                return;
            }

            builder.Append('"');
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
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
