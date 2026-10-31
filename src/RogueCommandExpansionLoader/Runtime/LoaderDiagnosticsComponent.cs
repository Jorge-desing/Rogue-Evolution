using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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

        public void Initialize(
            IRuntimeContentProvider provider,
            RuntimeOverlayUi overlay,
            RangeDisplayRuntime rangeDisplay)
        {
            _provider = provider;
            _overlay = overlay;
            _rangeDisplay = rangeDisplay;
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
        /// Returns the snapshot of registered compiled AI modules as JSON.
        /// Readable by an agent without a visual capture.
        /// </summary>
        public string GetAiModulesDiagnosticsJson()
        {
            return AgentDiagnosticsFormatter.SerializeAiModulesCensus(
                RuntimeAiModulesCensus.Current());
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
            var req = CardChangeRequest.Create(valueToChange, operation, value, false, false, side);
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
        /// Returns a unified system health and diagnostics overview as JSON.
        /// Synthesizes loader registry readiness, content counts, AI modules,
        /// economy parameters, and active combat telemetry.
        /// </summary>
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
