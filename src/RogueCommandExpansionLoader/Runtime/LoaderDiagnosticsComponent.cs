using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Modules;
using RogueCommandExpansionLoader.Runtime;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Live, agent-readable bridge over the loader's clean-room and visual utilities.
    /// Attached to the plugin's persistent GameObject so the BepInEx MCP bridge can
    /// read its public members (component/get_details) and call its methods
    /// (component/call_method) to obtain structured JSON without capturing the
    /// IMGUI overlay. This exposes behavior only; it adds no gameplay dependency.
    /// </summary>
    public sealed class LoaderDiagnosticsComponent : MonoBehaviour
    {
        private IRuntimeContentProvider _provider;
        private RuntimeOverlayUi _overlay;
        private RangeDisplayRuntime _rangeDisplay;
        private VisualRenderCensus _liveVisualRenderCensus;
        private int _liveVisualRenderCensusFrame = -1;
        private VisualQualityCensus _liveVisualQualityCensus;
        private int _liveVisualQualityCensusFrame = -1;

        public void Initialize(
            IRuntimeContentProvider provider,
            RuntimeOverlayUi overlay,
            RangeDisplayRuntime rangeDisplay)
        {
            _provider = provider;
            _overlay = overlay;
            _rangeDisplay = rangeDisplay;
        }

        private VisualRenderCensus GetLiveVisualRenderCensus()
        {
            int frame = -1;
            try { frame = Time.frameCount; } catch { }
            if (_liveVisualRenderCensus == null || frame < 0 || _liveVisualRenderCensusFrame != frame)
            {
                _liveVisualRenderCensus = RuntimeVisualCensus.Current(32);
                _liveVisualRenderCensusFrame = frame;
            }
            return _liveVisualRenderCensus;
        }

        private VisualQualityCensus GetLiveVisualQualityCensus()
        {
            int frame = -1;
            try { frame = Time.frameCount; } catch { }
            if (_liveVisualQualityCensus == null || frame < 0 || _liveVisualQualityCensusFrame != frame)
            {
                _liveVisualQualityCensus = RuntimeVisualQualityCensus.Current(64);
                _liveVisualQualityCensusFrame = frame;
            }
            return _liveVisualQualityCensus;
        }

        private void Update()
        {
            try
            {
                RuntimeHazardManager.UpdateTick(Time.time);
            }
            catch
            {
            }
        }

        // ---- Public members readable via component/get_details (no arguments) ----

        public bool ProviderReady => _provider != null;
        public bool OverlayReady => _overlay != null;
        public bool RangeDisplayReady => _rangeDisplay != null;

        public bool OverlayOpen => _overlay != null && _overlay.IsOpen;
        public string OverlayToggleShortcut => _overlay != null ? _overlay.ToggleShortcutText : string.Empty;

        public bool RangeDisplayEnabled => _rangeDisplay != null && _rangeDisplay.IsEnabled;
        public string RangeDisplayToggleKey => _rangeDisplay != null ? _rangeDisplay.ToggleKey : string.Empty;
        public int RangeDisplayTrackedEntities => _rangeDisplay != null ? _rangeDisplay.TrackedEntityCount : 0;
        public bool RangeDisplayShowWeapon => _rangeDisplay != null && _rangeDisplay.ShowWeaponRange;
        public bool RangeDisplayShowSkill => _rangeDisplay != null && _rangeDisplay.ShowSkillRange;
        public bool RangeDisplayShowEffect => _rangeDisplay != null && _rangeDisplay.ShowEffectRadius;
        public bool RangeDisplayShowSight => _rangeDisplay != null && _rangeDisplay.ShowSightRange;
        public float RangeDisplayLineWidth => _rangeDisplay != null ? _rangeDisplay.LineWidth : 0f;
        public int RangeDisplaySegments => _rangeDisplay != null ? _rangeDisplay.Segments : 0;
        public float RangeDisplayHeightOffset => _rangeDisplay != null ? _rangeDisplay.HeightOffset : 0f;

        public int OverlaySelectedTab => _overlay != null ? _overlay.SelectedTab : -1;
        public bool OverlayWatermarkVisible => _overlay != null && _overlay.WatermarkVisible;
        public bool OverlayGuiDisabled => _overlay != null && _overlay.IsGuiDisabled;
        public bool OverlayGuiFailureReported => _overlay != null && _overlay.IsGuiFailureReported;
        public string OverlayLogFilter => _overlay != null ? _overlay.LogFilter : string.Empty;
        public string OverlayPluginVersion => _overlay != null ? _overlay.PluginVersion : string.Empty;
        public string OverlayCompatibilityError => _overlay != null ? _overlay.CompatibilityError : string.Empty;
        public bool OverlayPatchResultSuccess => _overlay != null && _overlay.IsPatchResultSuccess;
        public string OverlayPatchResultMessage => _overlay != null ? _overlay.PatchResultMessage : string.Empty;

        public string TargetGameVersion => _provider != null ? _provider.TargetGameVersion : string.Empty;
        public bool IsNativeRegistryReady
        {
            get
            {
                LoaderDiagnosticsSnapshot snapshot = SafeSnapshot();
                return snapshot != null && snapshot.IsNativeRegistryReady;
            }
        }

        public bool IsNativeCardChangePipelineReady => RuntimeCardChangeApplier.IsAvailable(null);

        public int ActiveHazardZoneCount => RuntimeHazardManager.ActiveZoneCount;
        public int ActiveCommandAuraCount => RuntimeCommandAuraCensus.Current().TotalActiveAurasCount;
        public int TrackedVeterancyUnitCount => RuntimeVeterancyCensus.Current().TotalVeterancyUnitsCount;
        public int RegisteredAgentSessionCount => RuntimeAgentCoordinator.RegisteredSessionCount;
        public int ActiveAgentLockCount => RuntimeAgentCoordinator.ActiveLockCount;
        public int ActiveAgentMessageCount => RuntimeAgentCoordinator.TotalMessageCount;

        public int ActivePackCount
        {
            get
            {
                LoaderDiagnosticsSnapshot snapshot = SafeSnapshot();
                return snapshot != null ? snapshot.ActivePackCount : 0;
            }
        }

        public int FailedPackCount
        {
            get
            {
                LoaderDiagnosticsSnapshot snapshot = SafeSnapshot();
                return snapshot != null ? snapshot.FailedPackCount : 0;
            }
        }

        public int RegisteredUnitCount
        {
            get
            {
                LoaderDiagnosticsSnapshot snapshot = SafeSnapshot();
                return snapshot != null ? snapshot.Units.Count : 0;
            }
        }

        public int RegisteredStageCount
        {
            get
            {
                LoaderDiagnosticsSnapshot snapshot = SafeSnapshot();
                return snapshot != null ? snapshot.Stages.Count : 0;
            }
        }

        public int RegisteredAiProfileCount
        {
            get
            {
                LoaderDiagnosticsSnapshot snapshot = SafeSnapshot();
                return snapshot != null ? snapshot.AiProfiles.Count : 0;
            }
        }

        public int RegisteredAiModuleCount => RuntimeAiModulesCensus.Current().RegisteredModuleCount;

        public int LivePlayerCredits => RuntimeEconomyCensus.Current().PlayerCredits;

        public int LiveAiCredits => RuntimeEconomyCensus.Current().AiCredits;

        public int LiveAttackWaveNumber => RuntimeEconomyCensus.Current().AttackWaveNumber;

        public int LiveCombatActiveUnitCount => RuntimeCombatStatusCensus.Current().TotalCombatActiveUnitCount;

        public int LiveUnitsWithStatusEffectsCount => RuntimeCombatStatusCensus.Current().UnitsWithActiveStatusEffectsCount;

        public int LiveTotalFactoryCount => RuntimeProductionCensus.Current().TotalFactoryCount;

        public int LiveActiveProducingFactoryCount => RuntimeProductionCensus.Current().ActiveProducingCount;

        public int LiveQueuedUnitCount => RuntimeProductionCensus.Current().TotalQueuedUnitsCount;

        public bool LiveHasPlayerHq => RuntimeBaseIntegrityCensus.Current().HasPlayerHq;

        public float LivePlayerHqHealthRatio => RuntimeBaseIntegrityCensus.Current().PlayerHqHealthRatio;

        public float LiveAiStructureHealthRatio => RuntimeBaseIntegrityCensus.Current().AverageAiStructureHealthRatio;

        public int LiveSkinnedMeshUnitCount => GetLiveVisualRenderCensus().SkinnedMeshCount;

        public int LiveAnimatedUnitCount => GetLiveVisualRenderCensus().AnimatedUnitCount;

        public int LivePbrShaderUnitCount => GetLiveVisualRenderCensus().PbrShaderCount;

        public int LiveVisualQualityPassCount => GetLiveVisualQualityCensus().PbrContractPassCount;

        public int LiveVisualQualityIssueCount => GetLiveVisualQualityCensus().PbrContractIssueCount;

        public int LiveVertexWearSampleCount => GetLiveVisualQualityCensus().VertexColorSampleCount;

        public int LiveTotalUpgradeCount => RuntimeUpgradeCensus.Current(0).TotalUpgrades;
        public int LiveActiveUpgradeCount => RuntimeUpgradeCensus.Current(0).ActiveUpgrades;
        public int LiveModdedUpgradeCount => RuntimeUpgradeCensus.Current(0).ModdedUpgrades;

        public int LiveTotalRelicCount => RuntimeRelicCensus.Current(0).TotalRelics;
        public int LiveActiveRelicCount => RuntimeRelicCensus.Current(0).ActiveRelics;
        public int LiveModdedRelicCount => RuntimeRelicCensus.Current(0).ModdedRelics;

        // ---- Methods callable via component/call_method ----

        /// <summary>
        /// Returns the full loader diagnostics snapshot as a single-line JSON object.
        /// Readable by an agent without a visual capture.
        /// </summary>
        public string GetDiagnosticsJson()
        {
            return AgentDiagnosticsFormatter.SerializeSnapshot(SafeSnapshot());
        }

        /// <summary>
        /// Returns the state of every clean-room and visual utility as JSON:
        /// identifier-color resolution, log burst summary, inspector toggle, and
        /// range display. Readable by an agent without a visual capture.
        /// </summary>
        public string GetUtilitiesJson()
        {
            var overlayState = new OverlayStateView(
                _overlay != null,
                _overlay != null && _overlay.IsOpen,
                _overlay != null ? _overlay.ToggleShortcutText : string.Empty,
                _overlay != null ? _overlay.SelectedTab : -1,
                _overlay != null && _overlay.WatermarkVisible,
                _overlay != null && _overlay.IsGuiDisabled,
                _overlay != null && _overlay.IsGuiFailureReported,
                _overlay != null ? _overlay.LogFilter : string.Empty,
                _overlay != null ? _overlay.PluginVersion : string.Empty,
                _overlay != null ? _overlay.CompatibilityError : string.Empty,
                _overlay != null && _overlay.IsPatchResultSuccess,
                _overlay != null ? _overlay.PatchResultMessage : string.Empty);
            var rangeState = new RangeStateView(
                _rangeDisplay != null,
                _rangeDisplay != null && _rangeDisplay.IsEnabled,
                _rangeDisplay != null ? _rangeDisplay.ToggleKey : string.Empty,
                _rangeDisplay != null ? _rangeDisplay.TrackedEntityCount : 0,
                _rangeDisplay != null && _rangeDisplay.ShowWeaponRange,
                _rangeDisplay != null && _rangeDisplay.ShowSkillRange,
                _rangeDisplay != null && _rangeDisplay.ShowEffectRadius,
                _rangeDisplay != null && _rangeDisplay.ShowSightRange,
                _rangeDisplay != null ? _rangeDisplay.LineWidth : 0f,
                _rangeDisplay != null ? _rangeDisplay.Segments : 0,
                _rangeDisplay != null ? _rangeDisplay.HeightOffset : 0f);

            return AgentDiagnosticsFormatter.SerializeUtilities(SafeSnapshot(), overlayState, rangeState);
        }

        /// <summary>
        /// Returns the per-kind range guide colors as JSON. Readable by an agent
        /// without a visual capture.
        /// </summary>
        public string GetRangeColorsJson()
        {
            return AgentDiagnosticsFormatter.SerializeRangeColors();
        }

        /// <summary>
        /// Returns the full deterministic accent palette as JSON. Readable by an
        /// agent without a visual capture.
        /// </summary>
        public string GetIdentifierPaletteJson()
        {
            return AgentDiagnosticsFormatter.SerializeIdentifierPalette();
        }

        /// <summary>
        /// Resolves the stable accent color the loader assigns to an identifier.
        /// Returns JSON like {"identifier":"...","index":N,"hex":"#RRGGBB"}.
        /// </summary>
        public string ResolveIdentifierColor(string identifier)
        {
            return AgentDiagnosticsFormatter.SerializeIdentifierColor(identifier);
        }

        /// <summary>
        /// Returns the number of registered unit runtime mechanics profiles.
        /// </summary>
        public int RegisteredUnitMechanicsProfileCount => RuntimeUnitMechanicsDirector.RegisteredProfileCount;

        /// <summary>
        /// Returns a compact textual summary of the runtime mechanics registered
        /// for a unit, or an empty string when none are registered.
        /// </summary>
        public string GetUnitMechanicsSummary(string entityId)
        {
            string summary = RuntimeUnitMechanicsDirector.FormatMechanicsSummary(entityId);
            return summary ?? string.Empty;
        }

        /// <summary>
        /// Resolves the inspector open state from toggle/close input.
        /// </summary>
        public bool ResolveInspectorOpenState(bool isOpen, bool togglePressed, bool closePressed)
        {
            return InspectorTogglePolicy.Resolve(isOpen, togglePressed, closePressed);
        }

        /// <summary>
        /// Returns the inspector toggle resolution as JSON. Readable by an agent
        /// without a visual capture.
        /// </summary>
        public string GetInspectorToggleStateJson(bool isOpen, bool togglePressed, bool closePressed)
        {
            return AgentDiagnosticsFormatter.SerializeInspectorToggleState(isOpen, togglePressed, closePressed);
        }

        /// <summary>
        /// Minimum segment count accepted by the range circle geometry builder.
        /// </summary>
        public int RangeDisplayGeometryMinimumSegments => 8;

        /// <summary>
        /// Maximum segment count accepted by the range circle geometry builder.
        /// </summary>
        public int RangeDisplayGeometryMaximumSegments => 256;

        public string RuntimeHarmonyOwnerId => RuntimePatchInstaller.HarmonyOwnerId;

        /// <summary>
        /// Returns the number of points produced for a range circle with the given
        /// segment count (segments + 1), or 0 when the count is out of range.
        /// </summary>
        public int GetRangeCirclePointCount(int segments)
        {
            const int minimumSegments = 8;
            const int maximumSegments = 256;
            if (segments < minimumSegments || segments > maximumSegments)
            {
                return 0;
            }

            return segments + 1;
        }

        /// <summary>
        /// Returns the range circle point count as JSON. Readable by an agent
        /// without a visual capture.
        /// </summary>
        public string GetRangeCirclePointCountJson(int segments)
        {
            return AgentDiagnosticsFormatter.SerializeRangeCirclePointCount(segments);
        }

        public bool IsRuntimeHooksInstalled => RuntimePatchInstaller.IsHooksInstalled;

        public int RuntimeInstalledHookCount => RuntimePatchInstaller.InstalledHookCount;

        public int RegisteredRuntimeCombatXpCount => RuntimeUnitMechanicsDirector.RegisteredCombatXpCount;

        public string GetRuntimeHooksStateJson()
        {
            return AgentDiagnosticsFormatter.SerializeRuntimeHooksState(
                RuntimePatchInstaller.IsHooksInstalled,
                RuntimePatchInstaller.InstalledHookCount,
                RuntimePatchInstaller.HarmonyOwnerId,
                RuntimeUnitMechanicsDirector.RegisteredCombatXpCount);
        }

        public string GetInstalledPatchNamesJson()
        {
            return AgentDiagnosticsFormatter.SerializeInstalledPatchNames(
                RuntimePatchInstaller.InstalledPatchNames);
        }

        public string GetRegisteredProfilesJson()
        {
            return AgentDiagnosticsFormatter.SerializeRegisteredProfiles(
                RuntimeUnitMechanicsDirector.RegisteredProfileIds);
        }

        public string GetAppliedBaselineEffectsJson()
        {
            return AgentDiagnosticsFormatter.SerializeAppliedBaselineEffects(
                RuntimeUnitMechanicsDirector.AppliedBaselineEffectsCount,
                RuntimeUnitMechanicsDirector.AppliedBaselineEffectNames);
        }

        public string GetCombatXpEntriesJson()
        {
            return AgentDiagnosticsFormatter.SerializeCombatXpEntries(
                RuntimeUnitMechanicsDirector.CombatXpEntries);
        }

        /// <summary>
        /// Number of live entities on the battlefield (player units + AI units +
        /// AI buildings), read through the verified ExistingControllers registry.
        /// </summary>
        public int LiveBattlefieldEntityCount => RuntimeBattlefieldCensus.Current().BattlefieldEntityCount;

        public int LivePlayerUnitCount => RuntimeBattlefieldCensus.Current().PlayerUnitCount;

        public int LiveAiUnitCount => RuntimeBattlefieldCensus.Current().AiUnitCount;

        public int LiveAiBuildingCount => RuntimeBattlefieldCensus.Current().AiBuildingCount;

        public int LiveHarvesterCount => RuntimeBattlefieldCensus.Current().HarvesterCount;

        public int LiveMechanicsDirectorCount => RuntimeGameplayCommandDirector.GetLiveMechanicsDirectorCount();

        public int LiveVisualDirectorCount => RuntimeGameplayCommandDirector.GetLiveVisualDirectorCount();

        public int LiveJammedTurretCount => RuntimeGameplayCommandDirector.GetLiveJammedTurretCount();

        public int LiveRegeneratingShieldCount => RuntimeGameplayCommandDirector.GetLiveRegeneratingShieldCount();

        /// <summary>
        /// Returns the live battlefield census (counts plus per-player-unit
        /// health/armor/shield/rank state) as JSON. Readable by an agent without
        /// a visual capture.
        /// </summary>
        public string GetBattlefieldCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeBattlefieldCensus(
                RuntimeBattlefieldCensus.Current());
        }

        /// <summary>
        /// Returns the live AI roster (units + buildings) enriched with base balance
        /// parameters (cost, max health, move speed, dps, damage, weapon range) as
        /// JSON an agent can read without a visual capture.
        /// </summary>
        public string GetAiRosterJson()
        {
            return AgentDiagnosticsFormatter.SerializeAiRoster(
                RuntimeAiRosterCensus.Current());
        }

        /// <summary>
        /// Returns a compact live match summary (battlefield + AI roster + hooks) as JSON.
        /// </summary>
        public string GetMatchSummaryJson()
        {
            return AgentDiagnosticsFormatter.SerializeMatchSummary(
                RuntimeBattlefieldCensus.Current(),
                RuntimeAiRosterCensus.Current(),
                RuntimePatchInstaller.IsHooksInstalled,
                RuntimePatchInstaller.InstalledHookCount);
        }

        /// <summary>
        /// Returns the live player roster enriched with base balance as JSON.
        /// </summary>
        public string GetPlayerRosterJson()
        {
            return AgentDiagnosticsFormatter.SerializePlayerRoster(
                RuntimePlayerRosterCensus.Current());
        }

        /// <summary>
        /// Returns the live unit threat assessment as JSON.
        /// </summary>
        public string GetUnitThreatAssessmentJson()
        {
            return AgentDiagnosticsFormatter.SerializeUnitThreatAssessment(
                RuntimeUnitThreatAssessment.Current());
        }

        /// <summary>
        /// Returns aggregated threat-prioritization telemetry collected from all active
        /// runtime behaviours as JSON an agent can read without a visual capture.
        /// </summary>
        public string GetThreatPrioritizationCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeThreatPrioritizationCensus(
                ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour.ActiveBehaviourCount,
                ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour.AggregateLastProcessedAiUnitCount,
                ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour.AggregateLastProcessedAlliedUnitCount,
                ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour.AggregateLastRetargetedCount,
                ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour.AggregateTotalRetargetCommandCount);
        }

        /// <summary>
        /// Returns a catalog of available diagnostic getters as JSON an agent can read
        /// without a visual capture.
        /// </summary>
        public string GetDiagnosticCatalogJson()
        {
            return AgentDiagnosticsFormatter.SerializeDiagnosticCatalog();
        }

        /// <summary>
        /// Returns one unit's base balance parameters (cost, max health/armor/shield/mana/rank,
        /// move speed, weapon range, dps, damage) as read from the game's own EntityBalancingStore,
        /// as JSON an agent can read without a visual capture. Pass the unit's entity id.
        /// </summary>
        public string GetEntityBalanceJson(string entityId)
        {
            return AgentDiagnosticsFormatter.SerializeEntityBalanceLookup(
                RuntimeEntityBalanceLookup.Current(entityId));
        }

        /// <summary>
        /// Serializes one mod-authored card-change request (the data-driven input to
        /// the game's native EntityBalancingStore.AddInGameCardChange pipeline) into
        /// JSON an agent can read without a visual capture.
        /// </summary>
        public string GetCardChangeRequestJson(CardChangeRequest request)
        {
            return AgentDiagnosticsFormatter.SerializeCardChangeRequest(request);
        }

        /// <summary>
        /// Builds a mod-authored card-change request from primitive parameters and
        /// returns it as JSON, so an agent can preview the request without a visual capture.
        /// </summary>
        public string GetCardChangeRequestJsonFromParams(
            string valueToChange,
            string operation = "Add",
            float value = 0f,
            string side = "Friendly",
            bool multiplyValueWithCurrentHeatLevel = false,
            string multiplyValueWithMetaProgressionMultiplier = null,
            bool changeableValueHasToBeGreaterZero = false,
            string sourceId = "rcl-card-change")
        {
            var request = CardChangeRequest.Create(
                valueToChange,
                operation,
                value,
                multiplyValueWithCurrentHeatLevel,
                multiplyValueWithMetaProgressionMultiplier,
                side,
                changeableValueHasToBeGreaterZero,
                null,
                null,
                sourceId);
            return AgentDiagnosticsFormatter.SerializeCardChangeRequest(request);
        }

        /// <summary>
        /// Returns the snapshot of registered compiled AI modules as JSON.
        /// Readable by an agent without a visual capture.
        /// </summary>
        public string GetAiModulesDiagnosticsJson()
        {
            return AgentDiagnosticsFormatter.SerializeAiModulesCensus(
                RuntimeAiModulesCensus.Current());
        }

        /// <summary>
        /// Returns the live frame performance census (FPS, frame time, time
        /// scale, managed memory, and budget verdict) as JSON.
        /// </summary>
        public string GetPerformanceCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializePerformanceCensus(
                RuntimePerformanceCensus.Current());
        }

        /// <summary>
        /// Returns the windowed frame-timing census (average/min/max/p95 frame time,
        /// stutter and hitch counts, and smoothness verdict) as JSON.
        /// </summary>
        public string GetFrameTimingCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeFrameTimingCensus(
                RuntimeFrameTimingCensus.Current());
        }

        /// <summary>
        /// Compares two JSON snapshot payloads at the top level and returns the
        /// bounded diff as JSON. Invalid JSON fails closed with
        /// <c>isValid=false</c> and a diagnostic in <c>error</c>.
        /// </summary>
        public string GetSnapshotDiffJson(string beforeJson, string afterJson, int maxChanges)
        {
            Dictionary<string, object> before;
            Dictionary<string, object> after;
            try
            {
                before = ParseSnapshotObject(beforeJson);
            }
            catch (Exception exception)
            {
                return AgentDiagnosticsFormatter.SerializeSnapshotDiff(
                    new SnapshotDiffResult(false, "beforeJson: " + exception.Message,
                        0, 0, 0, 0, 0, 0, 0, 0, 0,
                        new List<string>().AsReadOnly(),
                        new List<string>().AsReadOnly(),
                        new List<SnapshotDiffChange>().AsReadOnly()));
            }

            try
            {
                after = ParseSnapshotObject(afterJson);
            }
            catch (Exception exception)
            {
                return AgentDiagnosticsFormatter.SerializeSnapshotDiff(
                    new SnapshotDiffResult(false, "afterJson: " + exception.Message,
                        0, 0, 0, 0, 0, 0, 0, 0, 0,
                        new List<string>().AsReadOnly(),
                        new List<string>().AsReadOnly(),
                        new List<SnapshotDiffChange>().AsReadOnly()));
            }

            return AgentDiagnosticsFormatter.SerializeSnapshotDiff(
                RuntimeSnapshotDiff.DiffDictionaries(before, after, maxChanges));
        }

        private static Dictionary<string, object> ParseSnapshotObject(string json)
        {
            var parsed = new Dictionary<string, object>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return parsed;
            }

            JObject root = JObject.Parse(json);
            foreach (KeyValuePair<string, JToken> entry in root)
            {
                parsed[entry.Key] = ToClrValue(entry.Value);
            }

            return parsed;
        }

        private static object ToClrValue(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            if (token.Type == JTokenType.Integer)
            {
                return token.Value<long>();
            }

            if (token.Type == JTokenType.Float)
            {
                return token.Value<double>();
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            return token.ToString(Formatting.None);
        }

        /// <summary>
        /// Returns the live match economy census (player/AI crystal balances,
        /// wave number, elapsed game time, harvester count) as JSON.
        /// </summary>
        public string GetEconomyMatchCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeEconomyCensus(
                RuntimeEconomyCensus.Current());
        }

        /// <summary>
        /// Returns the live combat status census (active combatants, status effects,
        /// and loss/omission counts) as JSON.
        /// </summary>
        public string GetCombatStatusCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeCombatStatusCensus(
                RuntimeCombatStatusCensus.Current());
        }

        /// <summary>
        /// Returns the live factory production census (production queues, active producing counts,
        /// and living factory health) as JSON.
        /// </summary>
        public string GetProductionCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeProductionCensus(
                RuntimeProductionCensus.Current());
        }

        /// <summary>
        /// Returns the live base integrity census (player command HQ and enemy base structures
        /// survivability metrics) as JSON.
        /// </summary>
        public string GetBaseIntegrityCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeBaseIntegrityCensus(
                RuntimeBaseIntegrityCensus.Current());
        }

        /// <summary>
        /// Commands an in-game factory to produce a unit, supporting live automated testing
        /// and validation without requiring manual GUI interactions.
        /// </summary>
        public string OrderFactoryProduceJson(string factoryKeyId, bool instant = false, bool forFree = true)
        {
            return AgentDiagnosticsFormatter.SerializeProductionOrderResult(
                RuntimeGameplayProductionDirector.TryOrderProduction(factoryKeyId, instant, forFree));
        }

        /// <summary>
        /// Read-only intermission level-select census (OverWorldMapEnemy nodes)
        /// as agent-readable JSON. Field order puts omission counters before
        /// the node detail list.
        /// </summary>
        public string GetIntermissionCensusJson(int maxRows = 64)
        {
            return AgentDiagnosticsFormatter.SerializeIntermissionCensus(
                RuntimeIntermissionCensus.Current(maxRows));
        }

        /// <summary>
        /// Enters an intermission level node via the director entry flow
        /// (select-then-confirm through the native LoadScene.LoadNextLevel
        /// corridor). Compat shim kept for existing callers: the result type
        /// and serializer are unchanged; the body now delegates to
        /// <see cref="RuntimeIntermissionDirector.TryEnterLevelNodeFlow"/>
        /// with the same native semantics. Never uses StageMap.JumpTo and
        /// never touches profile/save files.
        /// </summary>
        public string TryEnterLevelNodeJson(int level, int field)
        {
            IntermissionLevelEntry entry = RuntimeIntermissionDirector.TryEnterLevelNodeFlow(level, field);
            return AgentDiagnosticsFormatter.SerializeEnterLevelNodeResult(
                new EnterLevelNodeResult(entry.Success, entry.Message, entry.Level, entry.Field));
        }

        /// <summary>
        /// Selects an intermission level node without loading (director layer).
        /// </summary>
        public string OrderSelectLevelNodeJson(int level, int field)
        {
            return AgentDiagnosticsFormatter.SerializeIntermissionSelectionResult(
                RuntimeIntermissionDirector.TrySelectLevelNode(level, field));
        }

        /// <summary>
        /// Enters an intermission level node via the full native director flow.
        /// </summary>
        public string OrderEnterLevelNodeJson(int level, int field)
        {
            return AgentDiagnosticsFormatter.SerializeIntermissionLevelEntryResult(
                RuntimeIntermissionDirector.TryEnterLevelNodeFlow(level, field));
        }

        /// <summary>
        /// Live Navigator pause/time state as agent-readable JSON.
        /// </summary>
        public string GetNavigatorStateJson()
        {
            return AgentDiagnosticsFormatter.SerializeNavigatorState(
                RuntimeNavigatorState.Current());
        }

        /// <summary>
        /// Pauses (true) or unpauses (false) via the native Navigator,
        /// failing closed when the Navigator type/instance is missing.
        /// </summary>
        public string SetNavigatorPausedJson(bool paused)
        {
            return AgentDiagnosticsFormatter.SerializeNavigatorPauseResult(
                RuntimeNavigatorState.TrySetPaused(paused));
        }

        /// <summary>
        /// Pure JSON telemetry of run progression, ascension/heat scaling, active relics,
        /// and offered blueprint reward pools.
        /// </summary>
        public string GetRunProgressionCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeRunProgressionCensus(
                RuntimeRunProgressionCensus.Current());
        }

        /// <summary>
        /// Grants economy credits to a bank account (default: "Player") for live testing
        /// and balance calibration without GUI interaction.
        /// </summary>
        public string GrantPlayerCreditsJson(int amount, string accountName = "Player")
        {
            return AgentDiagnosticsFormatter.SerializeCreditGrantResult(
                RuntimeGameplayCommandDirector.TryGrantCredits(amount, accountName));
        }

        /// <summary>
        /// Dispatches a data-driven card change to an entity in-game via the native
        /// EntityBalancingStore pipeline for dynamic balance testing.
        /// </summary>
        public string ApplyInGameCardChangeJson(string valueToChange, string operation, float value, string side = "Friendly", string targetEntityId = null)
        {
            var req = CardChangeRequest.Create(valueToChange, operation, value, false, null, side);
            return AgentDiagnosticsFormatter.SerializeCardChangeApplyResult(
                RuntimeGameplayCommandDirector.TryApplyCardChange(req, targetEntityId));
        }

        /// <summary>
        /// Dispatches a movement command to a living unit by entity key ID.
        /// </summary>
        public string OrderUnitMoveJson(string entityKeyId, float destinationX, float destinationZ)
        {
            return AgentDiagnosticsFormatter.SerializeUnitCommandResult(
                RuntimeGameplayCommandDirector.TryOrderUnitMove(entityKeyId, destinationX, destinationZ));
        }

        /// <summary>
        /// Dispatches an attack command on a target entity by key ID.
        /// </summary>
        public string OrderUnitAttackJson(string entityKeyId, string targetEntityKeyId)
        {
            return AgentDiagnosticsFormatter.SerializeUnitCommandResult(
                RuntimeGameplayCommandDirector.TryOrderUnitAttack(entityKeyId, targetEntityKeyId));
        }

        /// <summary>
        /// Dispatches an active tactical skill command on a unit (Self, on Target, or at Position).
        /// </summary>
        public string OrderUnitActiveSkillJson(
            string entityKeyId,
            string targetEntityKeyId = null,
            float targetX = 0f,
            float targetZ = 0f,
            bool hasTargetPosition = false)
        {
            float? tx = hasTargetPosition ? (float?)targetX : null;
            float? tz = hasTargetPosition ? (float?)targetZ : null;
            return AgentDiagnosticsFormatter.SerializeActiveSkillOrderResult(
                RuntimeGameplayCommandDirector.TryOrderActiveSkill(entityKeyId, targetEntityKeyId, tx, tz));
        }

        /// <summary>
        /// Safely adjusts health, armor, shield, or damage on an entity for testing and calibration.
        /// </summary>
        public string AdjustEntityVitalityJson(string entityKeyId, string adjustmentType, float amount, bool ignoreArmor = false)
        {
            return AgentDiagnosticsFormatter.SerializeVitalityAdjustmentResult(
                RuntimeGameplayCommandDirector.TryAdjustEntityVitality(entityKeyId, adjustmentType, amount, ignoreArmor));
        }

        /// <summary>
        /// Applies a status effect (e.g. Emp, Burn, SlowedDown, Stun) to an entity for testing.
        /// </summary>
        public string ApplyStatusEffectJson(string entityKeyId, string statusEffectName, float durationSeconds = 10f)
        {
            return AgentDiagnosticsFormatter.SerializeStatusEffectOrderResult(
                RuntimeGameplayCommandDirector.TryApplyStatusEffect(entityKeyId, statusEffectName, durationSeconds));
        }

        /// <summary>
        /// Clears an active status effect from an entity.
        /// </summary>
        public string ClearStatusEffectJson(string entityKeyId, string statusEffectName)
        {
            return AgentDiagnosticsFormatter.SerializeStatusEffectOrderResult(
                RuntimeGameplayCommandDirector.TryClearStatusEffect(entityKeyId, statusEffectName));
        }

        /// <summary>
        /// Pure JSON telemetry of match terrain dimensions, cell counts, and start cells.
        /// </summary>
        public string GetGridMetricsJson()
        {
            return AgentDiagnosticsFormatter.SerializeGridMetricsCensus(
                RuntimeGridCensus.Current());
        }

        /// <summary>
        /// Evaluates an (X, Z) coordinate against the battlefield grid bounds.
        /// </summary>
        public string EvaluateGridPositionJson(float x, float z)
        {
            return AgentDiagnosticsFormatter.SerializeGridCellEvaluationResult(
                RuntimeGridCensus.EvaluatePosition(x, z));
        }

        /// <summary>
        /// Returns player selection census and control group assignments as JSON.
        /// </summary>
        public string GetSelectionCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeSelectionCensus(
                RuntimeSelectionCensus.Current());
        }

        /// <summary>
        /// Returns Fog of War runtime status and update settings as JSON.
        /// </summary>
        public string GetFogStateJson()
        {
            return AgentDiagnosticsFormatter.SerializeFogStateCensus(
                RuntimeFogCensus.Current());
        }

        /// <summary>
        /// Returns all registered unit, building, and factory entity IDs as JSON.
        /// </summary>
        public string GetEntityCatalogJson()
        {
            return AgentDiagnosticsFormatter.SerializeEntityCatalogCensus(
                RuntimeEntityCatalogCensus.Current());
        }

        /// <summary>
        /// Selects units by comma-separated entity key IDs or names.
        /// </summary>
        public string SelectUnitsJson(string entityKeysCsv, bool additive = false)
        {
            var keys = new List<string>();
            if (!string.IsNullOrWhiteSpace(entityKeysCsv))
            {
                string[] parts = entityKeysCsv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    string trimmed = parts[i].Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        keys.Add(trimmed);
                    }
                }
            }

            return AgentDiagnosticsFormatter.SerializeSelectionCommandResult(
                RuntimeGameplayCommandDirector.TrySelectEntities(keys, additive));
        }

        /// <summary>
        /// Deselects all currently selected units.
        /// </summary>
        public string DeselectAllUnitsJson()
        {
            return AgentDiagnosticsFormatter.SerializeSelectionCommandResult(
                RuntimeGameplayCommandDirector.TryDeselectAll());
        }

        /// <summary>
        /// Selects all units belonging to a specific category (CombatUnits, Harvesters, IdleHarvesters, SkillBuildings, Factories).
        /// </summary>
        public string SelectCategoryJson(string category)
        {
            return AgentDiagnosticsFormatter.SerializeSelectionCommandResult(
                RuntimeGameplayCommandDirector.TrySelectCategory(category));
        }

        /// <summary>
        /// Orders Stop on a unit or all selected units.
        /// </summary>
        public string OrderUnitStopJson(string entityKeyId = null)
        {
            return AgentDiagnosticsFormatter.SerializeUnitCommandResult(
                RuntimeGameplayCommandDirector.TryOrderStop(entityKeyId));
        }

        /// <summary>
        /// Orders Hold Position on a unit or all selected units.
        /// </summary>
        public string OrderUnitHoldPositionJson(string entityKeyId = null)
        {
            return AgentDiagnosticsFormatter.SerializeUnitCommandResult(
                RuntimeGameplayCommandDirector.TryOrderHoldPosition(entityKeyId));
        }

        /// <summary>
        /// Orders AttackMove toward target coordinates.
        /// </summary>
        public string OrderUnitAttackMoveJson(string entityKeyId, float targetX, float targetZ)
        {
            return AgentDiagnosticsFormatter.SerializeUnitCommandResult(
                RuntimeGameplayCommandDirector.TryOrderAttackMove(entityKeyId, targetX, targetZ));
        }

        /// <summary>
        /// Dispatches a native patrol route to a unit by entity key ID and parallel X/Z waypoints.
        /// </summary>
        public string OrderUnitPatrolJson(string entityKeyId, IReadOnlyList<float> waypointX, IReadOnlyList<float> waypointZ)
        {
            return AgentDiagnosticsFormatter.SerializeUnitPatrolResult(
                RuntimeGameplayCommandDirector.TryOrderUnitPatrol(entityKeyId, waypointX, waypointZ));
        }

        /// <summary>
        /// Dispatches a single command type (Move, Stop, HoldPosition, AttackMove) to a group of units.
        /// </summary>
        public string OrderGroupCommandJson(IReadOnlyList<string> entityKeyIds, string commandType, float targetX, float targetZ)
        {
            return AgentDiagnosticsFormatter.SerializeGroupCommandResult(
                RuntimeGameplayCommandDirector.TryOrderGroupCommand(entityKeyIds, commandType, targetX, targetZ));
        }

        /// <summary>
        /// Dispatches a tactical formation movement to multiple units.
        /// Supported formations: Line, Wedge, Column, Box, Surround.
        /// </summary>
        public string OrderFormationMoveJson(IReadOnlyList<string> entityKeyIds, string formationType, float targetX, float targetZ, float spacing)
        {
            return AgentDiagnosticsFormatter.SerializeFormationOrderResult(
                RuntimeGameplayCommandDirector.TryOrderFormationMove(entityKeyIds, formationType, targetX, targetZ, spacing));
        }

        /// <summary>
        /// CSV-compatible bridge overload for native patrol waypoints. The HTTP
        /// reflection bridge cannot reliably materialize IReadOnlyList arguments.
        /// </summary>
        public string OrderUnitPatrolCsvJson(string entityKeyId, string waypointXCsv, string waypointZCsv)
        {
            IReadOnlyList<float> waypointX;
            IReadOnlyList<float> waypointZ;
            string diagnostic;
            if (!RuntimeBridgeArgumentParser.TryParseFloatCsv(waypointXCsv, 1, 32, out waypointX, out diagnostic) ||
                !RuntimeBridgeArgumentParser.TryParseFloatCsv(waypointZCsv, 1, 32, out waypointZ, out diagnostic))
            {
                return AgentDiagnosticsFormatter.SerializeUnitPatrolResult(
                    new UnitPatrolResult(false, entityKeyId, 0, diagnostic));
            }

            return AgentDiagnosticsFormatter.SerializeUnitPatrolResult(
                RuntimeGameplayCommandDirector.TryOrderUnitPatrol(entityKeyId, waypointX, waypointZ));
        }

        /// <summary>CSV-compatible bridge overload for group commands.</summary>
        public string OrderGroupCommandCsvJson(string entityKeysCsv, string commandType, float targetX, float targetZ)
        {
            IReadOnlyList<string> entityKeyIds;
            string diagnostic;
            if (!RuntimeBridgeArgumentParser.TryParseStringCsv(entityKeysCsv, 1, 32, out entityKeyIds, out diagnostic))
            {
                return AgentDiagnosticsFormatter.SerializeGroupCommandResult(
                    new GroupCommandResult(false, commandType, 0, 0, diagnostic));
            }

            return AgentDiagnosticsFormatter.SerializeGroupCommandResult(
                RuntimeGameplayCommandDirector.TryOrderGroupCommand(entityKeyIds, commandType, targetX, targetZ));
        }

        /// <summary>CSV-compatible bridge overload for tactical formations.</summary>
        public string OrderFormationMoveCsvJson(string entityKeysCsv, string formationType, float targetX, float targetZ, float spacing)
        {
            IReadOnlyList<string> entityKeyIds;
            string diagnostic;
            string normalizedFormation = SyntheticGameplayScenarioPlan.NormalizeFormation(formationType);
            if (!RuntimeBridgeArgumentParser.TryParseStringCsv(entityKeysCsv, 1, 32, out entityKeyIds, out diagnostic))
            {
                return AgentDiagnosticsFormatter.SerializeFormationOrderResult(
                    new FormationOrderResult(false, normalizedFormation, 0, 0, targetX, targetZ, spacing, diagnostic));
            }
            if (normalizedFormation.Length == 0)
            {
                return AgentDiagnosticsFormatter.SerializeFormationOrderResult(
                    new FormationOrderResult(false, formationType, 0, 0, targetX, targetZ, spacing,
                        "Unsupported formation type. Supported: Line, Wedge, Column, Box, Surround."));
            }

            return AgentDiagnosticsFormatter.SerializeFormationOrderResult(
                RuntimeGameplayCommandDirector.TryOrderFormationMove(entityKeyIds, normalizedFormation, targetX, targetZ, spacing));
        }

        /// <summary>CSV-compatible bridge overload for group tactical stances.</summary>
        public string OrderGroupTacticalStanceCsvJson(string entityKeysCsv, string stanceName)
        {
            IReadOnlyList<string> entityKeyIds;
            string diagnostic;
            if (!RuntimeBridgeArgumentParser.TryParseStringCsv(entityKeysCsv, 1, 32, out entityKeyIds, out diagnostic))
            {
                return AgentDiagnosticsFormatter.SerializeUnitTacticalStanceResult(
                    new UnitTacticalStanceResult(false, "Group", stanceName, 0, diagnostic));
            }

            return AgentDiagnosticsFormatter.SerializeUnitTacticalStanceResult(
                RuntimeGameplayCommandDirector.TrySetGroupTacticalStance(entityKeyIds, stanceName));
        }

        /// <summary>CSV-compatible bridge overload for coordinated assaults.</summary>
        public string OrderCoordinatedAssaultCsvJson(string targetEntityKeyId, string attackerEntityKeysCsv, int maxAttackers)
        {
            IReadOnlyList<string> attackerEntityKeyIds;
            string diagnostic;
            if (!RuntimeBridgeArgumentParser.TryParseStringCsv(attackerEntityKeysCsv, 1, 32, out attackerEntityKeyIds, out diagnostic))
            {
                return AgentDiagnosticsFormatter.SerializeCoordinatedAssaultResult(
                    new CoordinatedAssaultResult(false, targetEntityKeyId, 0, 0, 0,
                        new List<string>().AsReadOnly(), 0f, 0f, diagnostic));
            }

            return AgentDiagnosticsFormatter.SerializeCoordinatedAssaultResult(
                RuntimeGameplayCommandDirector.TryExecuteCoordinatedAssault(
                    targetEntityKeyId, attackerEntityKeyIds, maxAttackers));
        }

        /// <summary>
        /// Executes one bounded, paused synthetic gameplay seam. The scenario is
        /// intentionally self-cleaning: it only tracks entities created by this call,
        /// clears loader-owned effects, and destroys those synthetic instances before
        /// returning its primitive evidence. It is not natural offer/production/AI evidence.
        /// </summary>
        public string RunSyntheticGameplayScenarioJson(
            string unitIdsCsv,
            float targetX,
            float targetZ,
            string formationType = "Wedge",
            float spacing = 4f)
        {
            bool paused = RuntimeGameplayCommandDirector.GetGameTimeScale() == 0f;
            SyntheticGameplayScenarioPlan plan;
            string planDiagnostic;
            if (!SyntheticGameplayScenarioPlan.TryCreate(
                    unitIdsCsv, formationType, targetX, targetZ, spacing,
                    out plan, out planDiagnostic))
            {
                return AgentDiagnosticsFormatter.SerializeSyntheticGameplayScenarioResult(
                    CreateSyntheticScenarioFailure(
                        paused, 0, formationType, planDiagnostic, null, 0));
            }

            if (!paused)
            {
                return AgentDiagnosticsFormatter.SerializeSyntheticGameplayScenarioResult(
                    CreateSyntheticScenarioFailure(
                        false, plan.UnitIds.Count, plan.FormationType,
                        "Synthetic gameplay scenario requires Time.timeScale == 0 and native pause before mutation.",
                        null, 0));
            }

            var createdKeys = new List<string>();
            var failures = new List<string>();
            int omittedFailures = 0;
            int stanceAffected = 0;
            int formationTargeted = 0;
            int formationDispatched = 0;
            int loadoutSuccess = 0;
            int affixSuccess = 0;
            int veterancySuccess = 0;
            int visualFeedbackSuccess = 0;
            int auraLinkedFollowers = 0;
            int pulseAffected = 0;
            int activeHazards = 0;
            int mechanicsInstances = 0;
            int destroyedSyntheticEntities = 0;

            Action<string> recordFailure = message =>
            {
                string text = string.IsNullOrWhiteSpace(message) ? "unspecified synthetic seam failure" : message;
                if (failures.Count < 16) failures.Add(text);
                else omittedFailures++;
            };

            Action cleanup = () =>
            {
                try
                {
                    if (createdKeys.Count > 0)
                    {
                        RuntimeGameplayCommandDirector.TryClearCommandAuraLinks(createdKeys[0]);
                    }
                }
                catch { }

                try
                {
                    RuntimeGameplayCommandDirector.TryClearHazardZones();
                }
                catch { }

                for (int i = 0; i < createdKeys.Count; i++)
                {
                    string key = createdKeys[i];
                    try { RuntimeGameplayCommandDirector.TryClearStatusEffect(key, "SquadEffect"); } catch { }
                    try { RuntimeGameplayCommandDirector.TryRemoveUnitAffix(key, "All"); } catch { }
                    try { RuntimeGameplayCommandDirector.TryApplyUnitLoadout(key, "Standard"); } catch { }
                    try { RuntimeGameplayCommandDirector.ResetUnitTacticalStance(key); } catch { }

                    try
                    {
                        EntityController entity = FindSyntheticEntity(key);
                        if (entity != null && entity.gameObject != null)
                        {
                            RuntimeGameplayCommandDirector.OnEntityDestroyed(key);
                            UnityEngine.Object.Destroy(entity.gameObject);
                            destroyedSyntheticEntities++;
                        }
                    }
                    catch { }
                }
            };

            try
            {
                LoaderReconciliationResult reconciliation =
                    RuntimeGameplayCommandDirector.TryReconcileLoaderRuntimeState(SafeSnapshot());
                if (!reconciliation.Success)
                {
                    recordFailure(reconciliation.Message);
                    return AgentDiagnosticsFormatter.SerializeSyntheticGameplayScenarioResult(
                        new SyntheticGameplayScenarioResult(
                            false, true, plan.UnitIds.Count, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                            omittedFailures, plan.FormationType, createdKeys, failures,
                            "Loader reconciliation failed; no synthetic entities were created."));
                }

                float spawnZ = targetZ - Math.Max(8f, plan.Spacing * 2f);
                for (int i = 0; i < plan.UnitIds.Count; i++)
                {
                    float spawnX = targetX + (i - (plan.UnitIds.Count - 1) / 2f) * plan.Spacing;
                    EntitySpawnResult spawn = RuntimeGameplayCommandDirector.TrySpawnEntity(
                        plan.UnitIds[i], "Friendly", spawnX, spawnZ, "RCL_SyntheticSeam_" + i);
                    if (spawn.Success && !string.IsNullOrWhiteSpace(spawn.EntityKeyId))
                    {
                        createdKeys.Add(spawn.EntityKeyId);
                    }
                    else
                    {
                        recordFailure(spawn.Message);
                    }
                }

                if (createdKeys.Count != plan.UnitIds.Count)
                {
                    cleanup();
                    return AgentDiagnosticsFormatter.SerializeSyntheticGameplayScenarioResult(
                        new SyntheticGameplayScenarioResult(
                            false, true, plan.UnitIds.Count, createdKeys.Count, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                            destroyedSyntheticEntities, omittedFailures, plan.FormationType, createdKeys, failures,
                            "Synthetic entity creation was incomplete; the partial scenario was cleaned up."));
                }

                string[] stances = { "Kiting", "Aggressive", "Defensive", "Standard" };
                string[] loadouts = { "HE", "Speed", "Standard", "Standard" };
                string[] affixes = { "Swift", "Overcharged", "Bulwark", "Vampiric" };
                for (int i = 0; i < createdKeys.Count; i++)
                {
                    string key = createdKeys[i];
                    if (RuntimeGameplayCommandDirector.TrySetUnitTacticalStance(key, stances[Math.Min(i, stances.Length - 1)]).Success) stanceAffected++;
                    if (RuntimeGameplayCommandDirector.TryApplyUnitLoadout(key, loadouts[Math.Min(i, loadouts.Length - 1)]).Success) loadoutSuccess++;
                    if (RuntimeGameplayCommandDirector.TryApplyUnitAffix(key, affixes[Math.Min(i, affixes.Length - 1)]).Success) affixSuccess++;
                    if (RuntimeGameplayCommandDirector.TryGrantEntityCombatXp(key, 5 + i * 5).Success) veterancySuccess++;
                }

                FormationOrderResult formation = RuntimeGameplayCommandDirector.TryOrderFormationMove(
                    createdKeys, plan.FormationType, plan.TargetX, plan.TargetZ, plan.Spacing);
                formationTargeted = formation.TargetedCount;
                formationDispatched = formation.DispatchedCount;
                if (!formation.Success) recordFailure(formation.Message);

                CommandAuraResult aura = RuntimeGameplayCommandDirector.TryDispatchCommandAuraPulse(
                    createdKeys[0], "Inspire", 18f, 30f);
                auraLinkedFollowers = aura.LinkedFollowersCount;
                if (!aura.Success) recordFailure(aura.Message);

                TacticalPulseResult pulse = RuntimeGameplayCommandDirector.TryExecuteTacticalPulse(
                    createdKeys[createdKeys.Count - 1], "Repair", 15f, 12f, 8f, 3);
                pulseAffected = pulse.AffectedEntityCount;
                if (!pulse.Success) recordFailure(pulse.Message);

                HazardCommandResult hazard = RuntimeGameplayCommandDirector.TrySpawnHazardZone(
                    "CryoMist", plan.TargetX, plan.TargetZ, 8f, 30f, 0.5f);
                activeHazards = RuntimeHazardManager.ActiveZoneCount;
                if (!hazard.Success) recordFailure(hazard.Message);

                VisualFeedbackResult feedback = RuntimeGameplayCommandDirector.TryOrderUnitVisualFeedback(
                    createdKeys[0], "FootstepPulse", 0.5f);
                if (feedback.Success) visualFeedbackSuccess++;
                else recordFailure(feedback.Message);

                mechanicsInstances = RuntimeGameplayCommandDirector.GetLiveMechanicsDirectorCount();
                bool allCommandsSucceeded = stanceAffected == createdKeys.Count &&
                    loadoutSuccess == createdKeys.Count && affixSuccess == createdKeys.Count &&
                    veterancySuccess == createdKeys.Count && formation.Success &&
                    formationDispatched == createdKeys.Count && aura.Success && pulse.Success &&
                    hazard.Success && visualFeedbackSuccess > 0;
                string message = allCommandsSucceeded
                    ? "Synthetic gameplay scenario completed and cleaned up."
                    : "Synthetic gameplay scenario reached the runtime seam with one or more failed steps.";

                cleanup();
                return AgentDiagnosticsFormatter.SerializeSyntheticGameplayScenarioResult(
                    new SyntheticGameplayScenarioResult(
                        allCommandsSucceeded, true, plan.UnitIds.Count, createdKeys.Count, stanceAffected,
                        formationTargeted, formationDispatched, loadoutSuccess, affixSuccess,
                        veterancySuccess, visualFeedbackSuccess, auraLinkedFollowers, pulseAffected,
                        activeHazards, mechanicsInstances, destroyedSyntheticEntities, omittedFailures,
                        plan.FormationType, createdKeys, failures, message));
            }
            catch (Exception ex)
            {
                recordFailure("Synthetic scenario exception: " + ex.Message);
                cleanup();
                return AgentDiagnosticsFormatter.SerializeSyntheticGameplayScenarioResult(
                    new SyntheticGameplayScenarioResult(
                        false, true, plan.UnitIds.Count, createdKeys.Count, stanceAffected, formationTargeted,
                        formationDispatched, loadoutSuccess, affixSuccess, veterancySuccess, visualFeedbackSuccess,
                        auraLinkedFollowers, pulseAffected, activeHazards, mechanicsInstances,
                        destroyedSyntheticEntities, omittedFailures, plan.FormationType, createdKeys, failures,
                        "Synthetic gameplay scenario failed closed: " + ex.Message));
            }
        }

        private static SyntheticGameplayScenarioResult CreateSyntheticScenarioFailure(
            bool paused, int requestedUnitCount, string formationType, string message,
            IReadOnlyList<string> failures, int omittedFailureCount)
        {
            var boundedFailures = failures ?? new List<string>().AsReadOnly();
            return new SyntheticGameplayScenarioResult(
                false, paused, requestedUnitCount, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                omittedFailureCount, formationType, new List<string>().AsReadOnly(), boundedFailures, message);
        }

        private static EntityController FindSyntheticEntity(string entityKeyId)
        {
            ExistingControllers existing = ExistingControllers.Instance;
            if (existing == null || string.IsNullOrWhiteSpace(entityKeyId)) return null;
            var units = new List<EntityController>();
            try { if (existing.PlayerUnits() != null) units.AddRange(existing.PlayerUnits()); } catch { }
            try { if (existing.AiUnits() != null) units.AddRange(existing.AiUnits()); } catch { }
            for (int i = 0; i < units.Count; i++)
            {
                EntityController unit = units[i];
                if (unit == null) continue;
                if (string.Equals(unit.EntityId, entityKeyId, StringComparison.Ordinal) ||
                    string.Equals(unit.name, entityKeyId, StringComparison.Ordinal) ||
                    unit.UniqueEntityId.ToString() == entityKeyId)
                {
                    return unit;
                }
            }
            return null;
        }

        /// <summary>
        /// Assigns a tactical combat stance to a unit (Standard, Aggressive, Defensive, HoldFire, Kiting).
        /// </summary>
        public string OrderUnitTacticalStanceJson(string entityKeyId, string stanceName)
        {
            return AgentDiagnosticsFormatter.SerializeUnitTacticalStanceResult(
                RuntimeGameplayCommandDirector.TrySetUnitTacticalStance(entityKeyId, stanceName));
        }

        /// <summary>
        /// Assigns a tactical combat stance to a group of units (Standard, Aggressive, Defensive, HoldFire, Kiting).
        /// </summary>
        public string OrderGroupTacticalStanceJson(IReadOnlyList<string> entityKeyIds, string stanceName)
        {
            return AgentDiagnosticsFormatter.SerializeUnitTacticalStanceResult(
                RuntimeGameplayCommandDirector.TrySetGroupTacticalStance(entityKeyIds, stanceName));
        }

        /// <summary>
        /// Queries the tactical combat stance census across living units.
        /// </summary>
        public string GetTacticalStanceCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeTacticalStanceCensus(
                RuntimeGameplayCommandDirector.GetTacticalStanceCensus(32));
        }

        /// <summary>
        /// Applies an elite affix/trait to a unit (Swift, Juggernaut, Overcharged, Vampiric, Bulwark, Volatile).
        /// </summary>
        public string OrderApplyUnitAffixJson(string entityKeyId, string affixName)
        {
            return AgentDiagnosticsFormatter.SerializeUnitAffixResult(
                RuntimeGameplayCommandDirector.TryApplyUnitAffix(entityKeyId, affixName));
        }

        /// <summary>
        /// Removes an active elite affix from a unit (or "All" to clear).
        /// </summary>
        public string OrderRemoveUnitAffixJson(string entityKeyId, string affixName)
        {
            return AgentDiagnosticsFormatter.SerializeUnitAffixResult(
                RuntimeGameplayCommandDirector.TryRemoveUnitAffix(entityKeyId, affixName));
        }

        /// <summary>
        /// Executes a bounded tactical pulse around a living unit (Shockwave, Rally, or Repair).
        /// </summary>
        public string OrderUnitTacticalPulseJson(
            string entityKeyId,
            string pulseType,
            float radius,
            float magnitude,
            float durationSeconds,
            int maxTargets)
        {
            return AgentDiagnosticsFormatter.SerializeTacticalPulseResult(
                RuntimeGameplayCommandDirector.TryExecuteTacticalPulse(
                    entityKeyId, pulseType, radius, magnitude, durationSeconds, maxTargets));
        }

        /// <summary>
        /// Executes a bounded director threat sweep around a living unit (Mark or Suppress).
        /// </summary>
        public string OrderDirectorThreatSweepJson(
            string sourceEntityKeyId,
            string sweepType,
            float radius,
            float durationSeconds,
            int maxTargets)
        {
            return AgentDiagnosticsFormatter.SerializeThreatSweepResult(
                RuntimeGameplayCommandDirector.TryExecuteThreatSweep(
                    sourceEntityKeyId, sweepType, radius, durationSeconds, maxTargets));
        }


        /// <summary>
        /// Coordinates a bounded attack against one hostile target.
        /// </summary>
        public string OrderCoordinatedAssaultJson(string targetEntityKeyId, IReadOnlyList<string> attackerEntityKeyIds, int maxAttackers)
        {
            return AgentDiagnosticsFormatter.SerializeCoordinatedAssaultResult(
                RuntimeGameplayCommandDirector.TryExecuteCoordinatedAssault(targetEntityKeyId, attackerEntityKeyIds, maxAttackers));
        }

        /// <summary>
        /// Orders an evasive retreat away from a threat position.
        /// </summary>
        public string OrderRetreatJson(string entityKeyId, float threatX, float threatZ, float distance)
        {
            return AgentDiagnosticsFormatter.SerializeRetreatResult(
                RuntimeGameplayCommandDirector.TryOrderRetreat(entityKeyId, threatX, threatZ, distance));
        }

        /// <summary>
        /// Queries the unit elite affixes census across living units.
        /// </summary>
        public string GetUnitAffixCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeUnitAffixCensus(
                RuntimeGameplayCommandDirector.GetUnitAffixCensus(32));
        }

        /// <summary>
        /// Manually triggers and coordinates an AI assault wave with elevated aggression.
        /// </summary>
        public string OrderDispatchAiAssaultWaveJson(int waveNumber, float aggressionBoost, int targetBaseIndex)
        {
            return AgentDiagnosticsFormatter.SerializeAiWaveDispatchResult(
                RuntimeGameplayCommandDirector.TryDispatchAiAssaultWave(waveNumber, aggressionBoost, targetBaseIndex));
        }

        /// <summary>
        /// Assigns selected units to a control group index (0-9).
        /// </summary>
        public string AssignControlGroupJson(int groupNumber)
        {
            return AgentDiagnosticsFormatter.SerializeSelectionCommandResult(
                RuntimeGameplayCommandDirector.TryAssignControlGroup(groupNumber));
        }

        /// <summary>
        /// Selects units assigned to a control group index (0-9).
        /// </summary>
        public string SelectControlGroupJson(int groupNumber)
        {
            return AgentDiagnosticsFormatter.SerializeSelectionCommandResult(
                RuntimeGameplayCommandDirector.TrySelectControlGroup(groupNumber));
        }

        /// <summary>
        /// Enables or disables Fog of War and reveals the map if disabled.
        /// </summary>
        public string SetFogOfWarJson(bool disableFog)
        {
            return AgentDiagnosticsFormatter.SerializeFogCommandResult(
                RuntimeGameplayCommandDirector.TrySetFogOfWar(disableFog));
        }

        /// <summary>
        /// Triggers full-map Fog of War reveal.
        /// </summary>
        public string RevealFogOfWarJson()
        {
            return AgentDiagnosticsFormatter.SerializeFogCommandResult(
                RuntimeGameplayCommandDirector.TryRevealFogOfWar());
        }

        /// <summary>
        /// Sets match time scale (0.0 = pause, 1.0 = normal, up to 10.0 = fast forward).
        /// </summary>
        public string SetGameTimeScaleJson(float timeScale)
        {
            return AgentDiagnosticsFormatter.SerializeGameSpeedCommandResult(
                RuntimeGameplayCommandDirector.TrySetGameTimeScale(timeScale));
        }

        /// <summary>
        /// Returns current match time scale.
        /// </summary>
        public string GetGameTimeScaleJson()
        {
            float scale = RuntimeGameplayCommandDirector.GetGameTimeScale();
            return AgentDiagnosticsFormatter.SerializeGameSpeedCommandResult(
                new GameSpeedCommandResult(true, scale, scale, "Current time scale queried."));
        }

        /// <summary>
        /// Triggers match victory or defeat sequence.
        /// </summary>
        public string TriggerMatchEndJson(bool win)
        {
            return AgentDiagnosticsFormatter.SerializeMatchControlResult(
                RuntimeGameplayCommandDirector.TryTriggerMatchEnd(win));
        }

        /// <summary>
        /// Dynamically spawns a unit or building entity at coordinates.
        /// </summary>
        public string SpawnEntityJson(string entityId, string side, float targetX, float targetZ, string customName = "")
        {
            return AgentDiagnosticsFormatter.SerializeEntitySpawnResult(
                RuntimeGameplayCommandDirector.TrySpawnEntity(entityId, side, targetX, targetZ, customName));
        }

        /// <summary>
        /// Returns a unified system health and diagnostics overview as JSON.
        /// Synthesizes loader registry readiness, content counts, AI modules,
        /// economy parameters, and active combat telemetry.
        /// </summary>
        /// <summary>
        /// Returns native AI Director state, attack wave progress, and command queue counts as JSON.
        /// </summary>
        public string GetAiDirectorCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeAiDirectorCensus(
                RuntimeAiDirectorCensus.Current());
        }

        /// <summary>
        /// Sets the current AI attack wave number.
        /// </summary>
        public string SetAttackWaveNumberJson(int waveNumber)
        {
            return AgentDiagnosticsFormatter.SerializeAiDirectorCommandResult(
                RuntimeGameplayCommandDirector.TrySetAttackWaveNumber(waveNumber));
        }

        /// <summary>
        /// Increments the AI attack wave number by 1.
        /// </summary>
        public string IncrementAttackWaveJson()
        {
            return AgentDiagnosticsFormatter.SerializeAiDirectorCommandResult(
                RuntimeGameplayCommandDirector.TryIncrementAttackWave());
        }

        /// <summary>
        /// Toggles or sets AI debug visual tags and tactical commands on screen.
        /// </summary>
        public string ToggleAiDebugVisualsJson(bool showTags, bool showCommands)
        {
            return AgentDiagnosticsFormatter.SerializeAiDirectorCommandResult(
                RuntimeGameplayCommandDirector.TryToggleAiDebugVisuals(showTags, showCommands));
        }

        /// <summary>
        /// Returns camera viewport metrics, position, and FOV as JSON.
        /// </summary>
        public string GetCameraCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeCameraCensus(
                RuntimeCameraCensus.Current());
        }

        /// <summary>
        /// Moves the main camera to target ground coordinates (X, Z).
        /// </summary>
        public string MoveCameraJson(float targetX, float targetZ)
        {
            return AgentDiagnosticsFormatter.SerializeCameraCommandResult(
                RuntimeGameplayCommandDirector.TryMoveCamera(targetX, targetZ));
        }

        /// <summary>
        /// Centers the camera on an entity by key ID.
        /// </summary>
        public string FocusCameraOnEntityJson(string entityKeyId)
        {
            return AgentDiagnosticsFormatter.SerializeCameraCommandResult(
                RuntimeGameplayCommandDirector.TryFocusCameraOnEntity(entityKeyId));
        }

        /// <summary>
        /// Sets camera FOV / zoom level.
        /// </summary>
        public string SetCameraZoomJson(float fov)
        {
            return AgentDiagnosticsFormatter.SerializeCameraCommandResult(
                RuntimeGameplayCommandDirector.TrySetCameraZoom(fov));
        }

        /// <summary>
        /// Returns active harvesters, crystal payloads, and states as JSON.
        /// </summary>
        public string GetHarvesterCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeHarvesterCensus(
                RuntimeHarvesterCensus.Current());
        }

        /// <summary>
        /// Orders a harvester unit to harvest nearest crystal.
        /// </summary>
        public string OrderHarvesterHarvestJson(string harvesterKeyId)
        {
            return AgentDiagnosticsFormatter.SerializeHarvesterCommandResult(
                RuntimeGameplayCommandDirector.TryOrderHarvesterHarvest(harvesterKeyId));
        }

        /// <summary>
        /// Orders a harvester unit to return cargo to closest refinery.
        /// </summary>
        public string OrderHarvesterReturnJson(string harvesterKeyId)
        {
            return AgentDiagnosticsFormatter.SerializeHarvesterCommandResult(
                RuntimeGameplayCommandDirector.TryOrderHarvesterReturn(harvesterKeyId));
        }

        /// <summary>
        /// Returns audio mixer volume slider levels as JSON.
        /// </summary>
        public string GetAudioCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeAudioCensus(
                RuntimeAudioCensus.Current());
        }

        /// <summary>
        /// Returns visual surface material and vertex-wear telemetry as JSON.
        /// </summary>
        public string GetVisualSurfaceCensusJson(int maxTrackedUnits = 64)
        {
            return AgentDiagnosticsFormatter.SerializeVisualSurfaceCensus(
                RuntimeVisualSurfaceCensus.Current(maxTrackedUnits));
        }

        public string GetVisualQualityCensusJson(int maxTrackedUnits = 64)
        {
            return AgentDiagnosticsFormatter.SerializeVisualQualityCensus(
                RuntimeVisualQualityCensus.Current(maxTrackedUnits));
        }

        public string GetVisualHealthSummaryJson(int maxTrackedUnits = 64)
        {
            return AgentDiagnosticsFormatter.SerializeVisualHealthSummary(
                RuntimeVisualHealthCensus.Current(maxTrackedUnits));
        }

        public string GetVisualMaterialCensusJson(int maxTrackedUnits = 64)
        {
            return AgentDiagnosticsFormatter.SerializeVisualMaterialCensus(
                RuntimeVisualMaterialCensus.Current(maxTrackedUnits));
        }

        public string GetSocketAuditCensusJson(int maxTrackedUnits = 64)
        {
            return AgentDiagnosticsFormatter.SerializeSocketAuditCensus(
                RuntimeSocketAuditCensus.Current(maxTrackedUnits));
        }

        public string GetColliderAuditCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeColliderAuditCensus(
                RuntimeColliderAuditCensus.Current());
        }

        public string GetParticleVfxCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeParticleVfxCensus(
                RuntimeParticleVfxCensus.Current());
        }

        public string GetLodAuditCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeLodAuditCensus(
                RuntimeLodAuditCensus.Current());
        }

        /// <summary>
        /// Sets volume slider level for an audio channel (Master, Music, Gameplay, UI).
        /// </summary>
        public string SetAudioVolumeJson(string channel, float sliderValue)
        {
            return AgentDiagnosticsFormatter.SerializeAudioCommandResult(
                RuntimeGameplayCommandDirector.TrySetAudioVolume(channel, sliderValue));
        }

        /// <summary>
        /// Returns registered tech upgrades, research branches, and relics as JSON.
        /// </summary>
        public string GetTechCatalogJson()
        {
            return AgentDiagnosticsFormatter.SerializeTechCatalogCensus(
                RuntimeTechCatalogCensus.Current());
        }

        /// <summary>
        /// Returns telemetry census of registered card upgrades in the balancing store as JSON.
        /// </summary>
        public string GetUpgradeCensusJson(int maxEntries = 64)
        {
            return AgentDiagnosticsFormatter.SerializeUpgradeCensus(
                RuntimeUpgradeCensus.Current(maxEntries));
        }

        /// <summary>
        /// Returns telemetry census of registered player relics in the balancing store as JSON.
        /// </summary>
        public string GetRelicCensusJson(int maxEntries = 64)
        {
            return AgentDiagnosticsFormatter.SerializeRelicCensus(
                RuntimeRelicCensus.Current(maxEntries));
        }

        /// <summary>
        /// Returns live combat XP, rank tiers, and veterancy bonus multipliers as JSON.
        /// </summary>
        public string GetVeterancyCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeVeterancyCensus(
                RuntimeVeterancyCensus.Current());
        }

        /// <summary>
        /// Grants combat XP to a unit by entity key id.
        /// </summary>
        public string OrderGrantCombatXpJson(string entityKeyId, int xpAmount)
        {
            return AgentDiagnosticsFormatter.SerializeVeterancyCommandResult(
                RuntimeGameplayCommandDirector.TryGrantEntityCombatXp(entityKeyId, xpAmount));
        }

        /// <summary>
        /// Sets a unit's veterancy level directly by entity key id.
        /// </summary>
        public string OrderSetVeterancyLevelJson(string entityKeyId, int targetLevel)
        {
            return AgentDiagnosticsFormatter.SerializeVeterancyCommandResult(
                RuntimeGameplayCommandDirector.TrySetEntityVeterancyLevel(entityKeyId, targetLevel));
        }

        /// <summary>
        /// Returns active captain command auras and linked follower chains as JSON.
        /// </summary>
        public string GetCommandAuraCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeCommandAuraCensus(
                RuntimeCommandAuraCensus.Current());
        }

        /// <summary>
        /// Dispatches a dynamic command aura pulse from a captain entity.
        /// </summary>
        public string OrderCommandAuraPulseJson(string captainKeyId, string auraType, float radius, float durationSeconds)
        {
            return AgentDiagnosticsFormatter.SerializeCommandAuraResult(
                RuntimeGameplayCommandDirector.TryDispatchCommandAuraPulse(captainKeyId, auraType, radius, durationSeconds));
        }

        /// <summary>
        /// Clears command aura links for a captain entity.
        /// </summary>
        public string OrderClearCommandAuraLinksJson(string captainKeyId)
        {
            return AgentDiagnosticsFormatter.SerializeCommandAuraResult(
                RuntimeGameplayCommandDirector.TryClearCommandAuraLinks(captainKeyId));
        }

        /// <summary>
        /// Returns active battlefield environmental hazard zones and trigger metrics as JSON.
        /// </summary>
        public string GetHazardCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeHazardCensus(
                RuntimeHazardCensus.Current());
        }

        /// <summary>
        /// Spawns a battlefield environmental hazard zone.
        /// </summary>
        public string OrderSpawnHazardZoneJson(string hazardType, float x, float z, float radius, float durationSeconds, float tickDamage)
        {
            return AgentDiagnosticsFormatter.SerializeHazardCommandResult(
                RuntimeGameplayCommandDirector.TrySpawnHazardZone(hazardType, x, z, radius, durationSeconds, tickDamage));
        }

        /// <summary>
        /// Clears all active battlefield environmental hazard zones.
        /// </summary>
        public string OrderClearHazardZonesJson()
        {
            return AgentDiagnosticsFormatter.SerializeHazardCommandResult(
                RuntimeGameplayCommandDirector.TryClearHazardZones());
        }


        /// <summary>
        /// Collects diagnostic census of active unit loadouts and EntityFactory pre-spawn pools.
        /// </summary>
        public string GetUnitLoadoutCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeUnitLoadoutAndPoolCensus(
                RuntimeUnitLoadoutCensus.Current());
        }

        /// <summary>
        /// Dispatches a unit loadout / variant change to a living unit.
        /// </summary>
        public string OrderApplyUnitLoadoutJson(string entityKeyId, string loadoutId)
        {
            return AgentDiagnosticsFormatter.SerializeUnitLoadoutResult(
                RuntimeGameplayCommandDirector.TryApplyUnitLoadout(entityKeyId, loadoutId));
        }

        /// <summary>
        /// Manually warms an EntityFactory pre-spawn pool.
        /// </summary>
        public string OrderWarmPrespawnPoolJson(string entityId, int count)
        {
            return AgentDiagnosticsFormatter.SerializePrespawnWarmResult(
                RuntimeGameplayCommandDirector.TryWarmPrespawnPool(entityId, count));
        }

        /// <summary>
        /// Collects diagnostic census of active AI tactical doctrine and base deck entries.
        /// </summary>
        public string GetAiDoctrineCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeAiDoctrineAndDeckCensus(
                RuntimeAiDoctrineCensus.Current());
        }

        /// <summary>
        /// Sets the active AI tactical doctrine and aggression scaling multiplier.
        /// </summary>
        public string OrderSetAiDoctrineJson(string doctrineName, float aggressionMultiplier)
        {
            return AgentDiagnosticsFormatter.SerializeAiDoctrineResult(
                RuntimeGameplayCommandDirector.TrySetAiDoctrine(doctrineName, aggressionMultiplier));
        }

        /// <summary>
        /// Injects or updates a unit entry in an AI base deck.
        /// </summary>
        public string OrderInjectAiDeckUnitJson(int baseIndex, string unitId, int countAtStart, int maxLiving)
        {
            return AgentDiagnosticsFormatter.SerializeAiDeckInjectionResult(
                RuntimeGameplayCommandDirector.TryInjectAiDeckUnit(baseIndex, unitId, countAtStart, maxLiving));
        }

        /// <summary>
        /// Queries the visual render, rig architecture, and animation telemetry census across living units.
        /// </summary>
        public string GetVisualRenderCensusJson(int maxTrackedUnits = 32)
        {
            return AgentDiagnosticsFormatter.SerializeVisualRenderCensus(
                RuntimeGameplayCommandDirector.GetVisualRenderCensus(maxTrackedUnits));
        }

        /// <summary>
        /// Deeply inspects visual render evidence, rig hierarchy, animation parameters, and visual director state for a specific unit.
        /// </summary>
        public string InspectEntityVisualJson(string entityKeyId)
        {
            return AgentDiagnosticsFormatter.SerializeDetailedEntityVisualDiagnostic(
                RuntimeGameplayCommandDirector.InspectEntityVisual(entityKeyId));
        }

        public int AuditedUnitPrefabCount => RuntimeVisualCensus.AuditedPrefabCount;

        public string GetLoadedPrefabVisualAuditJson(int maxEntries = 64)
        {
            return AgentDiagnosticsFormatter.SerializeLoadedPrefabVisualAuditCensus(
                RuntimeGameplayCommandDirector.GetLoadedPrefabVisualAuditCensus(maxEntries));
        }

        public string GetAgentCoordinationCensusJson()
        {
            return AgentDiagnosticsFormatter.SerializeAgentCoordinationCensus(
                RuntimeAgentCoordinator.GetSnapshot(Time.time));
        }

        public string RegisterAgentSessionJson(string agentId, string agentRole, string notes, string assignedSector)
        {
            bool success = RuntimeAgentCoordinator.TryRegisterSession(
                agentId,
                agentRole,
                notes,
                assignedSector,
                null,
                Time.time,
                out string message);
            return AgentDiagnosticsFormatter.SerializeAgentOperationResult(success, agentId, "RegisterSession", message);
        }

        public string RegisterAgentSessionJson(string agentId, string agentRole, string notes)
        {
            return RegisterAgentSessionJson(agentId, agentRole, notes, "Global");
        }

        public string DeregisterAgentSessionJson(string agentId)
        {
            bool success = RuntimeAgentCoordinator.TryDeregisterSession(agentId, out string message);
            return AgentDiagnosticsFormatter.SerializeAgentOperationResult(success, agentId, "DeregisterSession", message);
        }

        public string PostAgentMessageJson(string senderAgentId, string messageType, string content, string targetAgentId)
        {
            bool success = RuntimeAgentCoordinator.TryPostMessage(
                senderAgentId, messageType, content, targetAgentId, Time.time, out string message);
            return AgentDiagnosticsFormatter.SerializeAgentMessageResult(success, "", senderAgentId, messageType, message);
        }

        public string PingAgentJson(string agentId, string message)
        {
            bool success = RuntimeAgentCoordinator.TryPing(
                agentId,
                message,
                Time.time,
                out string reply);
            return AgentDiagnosticsFormatter.SerializeAgentOperationResult(success, agentId, "Ping", reply);
        }

        public string AcquireAgentLockJson(string agentId, string resourceKey, float durationSeconds)
        {
            bool success = RuntimeAgentCoordinator.TryAcquireLock(
                agentId,
                resourceKey,
                durationSeconds,
                Time.time,
                out string message);
            return AgentDiagnosticsFormatter.SerializeAgentOperationResult(success, agentId, "AcquireLock", message);
        }

        public string AcquireAgentLockBatchCsvJson(string agentId, string resourceKeysCsv, float durationSeconds)
        {
            IReadOnlyList<string> resourceKeys;
            string diagnostic;
            if (!RuntimeBridgeArgumentParser.TryParseStringCsv(resourceKeysCsv, 1, 64, out resourceKeys, out diagnostic))
            {
                return AgentDiagnosticsFormatter.SerializeAgentBatchLockResult(false, agentId, 0, 0, "AcquireBatch", diagnostic);
            }

            bool success = RuntimeAgentCoordinator.TryAcquireLockBatch(
                agentId, resourceKeys, durationSeconds, Time.time, out string message);
            int count = resourceKeys.Count;
            return AgentDiagnosticsFormatter.SerializeAgentBatchLockResult(success, agentId, count, success ? count : 0, "AcquireBatch", message);
        }

        public string AcquireAgentLockBatchJson(string agentId, IReadOnlyList<string> resourceKeys, float durationSeconds)
        {
            bool success = RuntimeAgentCoordinator.TryAcquireLockBatch(
                agentId, resourceKeys, durationSeconds, Time.time, out string message);
            int count = resourceKeys?.Count ?? 0;
            return AgentDiagnosticsFormatter.SerializeAgentBatchLockResult(success, agentId, count, success ? count : 0, "AcquireBatch", message);
        }

        public string ReleaseAgentLockJson(string agentId, string resourceKey)
        {
            bool success = RuntimeAgentCoordinator.TryReleaseLock(
                agentId,
                resourceKey,
                out string message);
            return AgentDiagnosticsFormatter.SerializeAgentOperationResult(success, agentId, "ReleaseLock", message);
        }

        public string ReleaseAgentLockBatchCsvJson(string agentId, string resourceKeysCsv)
        {
            IReadOnlyList<string> resourceKeys;
            string diagnostic;
            if (!RuntimeBridgeArgumentParser.TryParseStringCsv(resourceKeysCsv, 1, 64, out resourceKeys, out diagnostic))
            {
                return AgentDiagnosticsFormatter.SerializeAgentBatchLockResult(false, agentId, 0, 0, "ReleaseBatch", diagnostic);
            }

            bool success = RuntimeAgentCoordinator.TryReleaseLockBatch(
                agentId, resourceKeys, out string message);
            int count = resourceKeys.Count;
            return AgentDiagnosticsFormatter.SerializeAgentBatchLockResult(success, agentId, count, success ? count : 0, "ReleaseBatch", message);
        }

        public string ReleaseAgentLockBatchJson(string agentId, IReadOnlyList<string> resourceKeys)
        {
            bool success = RuntimeAgentCoordinator.TryReleaseLockBatch(
                agentId, resourceKeys, out string message);
            int count = resourceKeys?.Count ?? 0;
            return AgentDiagnosticsFormatter.SerializeAgentBatchLockResult(success, agentId, count, success ? count : 0, "ReleaseBatch", message);
        }

        public string GetLoaderAuditCensusJson(int maxUnits = 64, int maxAiProfiles = 32)
        {
            return AgentDiagnosticsFormatter.SerializeLoaderRuntimeAuditCensus(
                RuntimeGameplayCommandDirector.GetLoaderRuntimeAuditCensus(SafeSnapshot(), maxUnits, maxAiProfiles));
        }

        public string OrderReconcileLoaderStateJson()
        {
            return AgentDiagnosticsFormatter.SerializeLoaderReconciliationResult(
                RuntimeGameplayCommandDirector.TryReconcileLoaderRuntimeState(SafeSnapshot()));
        }

        public string GetUnitMechanicsCensusJson(int maxUnits = 64)
        {
            return AgentDiagnosticsFormatter.SerializeUnitMechanicsCensus(
                RuntimeGameplayCommandDirector.GetUnitMechanicsCensus(maxUnits));
        }

        public string OrderUnitVisualFeedbackJson(string entityKeyId, string feedbackType, float magnitude = 0.22f)
        {
            return AgentDiagnosticsFormatter.SerializeVisualFeedbackResult(
                RuntimeGameplayCommandDirector.TryOrderUnitVisualFeedback(entityKeyId, feedbackType, magnitude));
        }

        public string OrderUnitAttachmentJson(string entityKeyId, string attachmentType, bool active)
        {
            return AgentDiagnosticsFormatter.SerializeVisualFeedbackResult(
                RuntimeGameplayCommandDirector.TryOrderUnitAttachment(entityKeyId, attachmentType, active));
        }

        public string GetGuiStateJson()
        {
            var overlayView = new OverlayStateView(
                OverlayReady,
                OverlayOpen,
                OverlayToggleShortcut,
                OverlaySelectedTab,
                OverlayWatermarkVisible,
                OverlayGuiDisabled,
                OverlayGuiFailureReported,
                OverlayLogFilter,
                OverlayPluginVersion,
                OverlayCompatibilityError,
                OverlayPatchResultSuccess,
                OverlayPatchResultMessage);
            var rangeView = new RangeStateView(
                RangeDisplayReady,
                RangeDisplayEnabled,
                RangeDisplayToggleKey,
                RangeDisplayTrackedEntities,
                RangeDisplayShowWeapon,
                RangeDisplayShowSkill,
                RangeDisplayShowEffect,
                RangeDisplayShowSight,
                RangeDisplayLineWidth,
                RangeDisplaySegments,
                RangeDisplayHeightOffset);
            var liveCamera = RuntimeCameraCensus.Current();
            var playbackView = new GuiPlaybackView(
                RuntimeGameplayCommandDirector.GetGameTimeScale(),
                IsRuntimeHooksInstalled,
                liveCamera != null ? liveCamera.PositionX : 0f,
                liveCamera != null ? liveCamera.PositionZ : 0f,
                liveCamera != null ? liveCamera.FieldOfView : 0f);
            return AgentDiagnosticsFormatter.SerializeGuiState(
                overlayView,
                rangeView,
                playbackView,
                0,
                0,
                RuntimeHazardManager.ActiveZoneCount);
        }

        public string SetOverlayOpenJson(bool open)
        {
            try
            {
                if (_overlay == null)
                {
                    return AgentDiagnosticsFormatter.SerializeGuiControlResult(false, "overlay unavailable", RuntimeGameplayCommandDirector.GetGameTimeScale(), 0f, 0f);
                }
                _overlay.SetOpen(open);
                return AgentDiagnosticsFormatter.SerializeGuiControlResult(true, "", RuntimeGameplayCommandDirector.GetGameTimeScale(), 0f, 0f);
            }
            catch (Exception ex)
            {
                return AgentDiagnosticsFormatter.SerializeGuiControlResult(false, ex.Message, RuntimeGameplayCommandDirector.GetGameTimeScale(), 0f, 0f);
            }
        }

        public string SetRangeDisplayEnabledJson(bool enabled)
        {
            try
            {
                if (_rangeDisplay == null)
                {
                    return AgentDiagnosticsFormatter.SerializeGuiControlResult(false, "range display unavailable", RuntimeGameplayCommandDirector.GetGameTimeScale(), 0f, 0f);
                }
                _rangeDisplay.SetEnabled(enabled);
                return AgentDiagnosticsFormatter.SerializeGuiControlResult(true, "", RuntimeGameplayCommandDirector.GetGameTimeScale(), 0f, 0f);
            }
            catch (Exception ex)
            {
                return AgentDiagnosticsFormatter.SerializeGuiControlResult(false, ex.Message, RuntimeGameplayCommandDirector.GetGameTimeScale(), 0f, 0f);
            }
        }

        public string SetOverlayTabJson(int tab)
        {
            int clamped;
            GuiControlPolicy.TryClampTab(tab, out clamped);
            try
            {
                if (_overlay == null)
                {
                    return AgentDiagnosticsFormatter.SerializeGuiControlResult(false, "overlay unavailable", RuntimeGameplayCommandDirector.GetGameTimeScale(), 0f, 0f);
                }
                _overlay.SetSelectedTab(clamped);
                return AgentDiagnosticsFormatter.SerializeGuiControlResult(true, "", RuntimeGameplayCommandDirector.GetGameTimeScale(), 0f, 0f);
            }
            catch (Exception ex)
            {
                return AgentDiagnosticsFormatter.SerializeGuiControlResult(false, ex.Message, RuntimeGameplayCommandDirector.GetGameTimeScale(), 0f, 0f);
            }
        }

        public string GetSystemHealthJson()
        {
            return AgentDiagnosticsFormatter.SerializeSystemHealth(
                SafeSnapshot(),
                RuntimeAiModulesCensus.Current(),
                RuntimeEconomyCensus.Current(),
                RuntimeCombatStatusCensus.Current(),
                ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour.ActiveBehaviourCount,
                ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour.AggregateLastProcessedAiUnitCount,
                ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour.AggregateLastProcessedAlliedUnitCount,
                ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour.AggregateLastRetargetedCount,
                ThreatPrioritizationAiModule.ThreatPrioritizationRuntimeBehaviour.AggregateTotalRetargetCommandCount,
                RuntimeBattlefieldCensus.Current().BattlefieldEntityCount,
                RuntimeBattlefieldCensus.Current().PlayerUnitCount,
                RuntimeBattlefieldCensus.Current().AiUnitCount,
                RuntimeBattlefieldCensus.Current().AiBuildingCount,
                RuntimeBattlefieldCensus.Current().HarvesterCount,
                RuntimeProductionCensus.Current().TotalFactoryCount,
                RuntimeProductionCensus.Current().ActiveProducingCount,
                RuntimeProductionCensus.Current().TotalQueuedUnitsCount,
                RuntimeBaseIntegrityCensus.Current().HasPlayerHq,
                RuntimeBaseIntegrityCensus.Current().PlayerHqHealthRatio,
                RuntimeBaseIntegrityCensus.Current().AverageAiStructureHealthRatio,
                RuntimeAiRosterCensus.Current().AiUnitCount,
                RuntimeAiRosterCensus.Current().AiBuildingCount);
        }

        private LoaderDiagnosticsSnapshot SafeSnapshot()
        {
            if (_provider == null)
            {
                return null;
            }

            try
            {
                return _provider.GetDiagnosticsSnapshot();
            }
            catch
            {
                return null;
            }
        }
    }
}
