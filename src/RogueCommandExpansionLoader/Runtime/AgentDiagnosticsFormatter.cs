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
                        ["ownerAgentId"] = l.OwnerAgentId ?? string.Empty,
                        ["acquiredAtSeconds"] = l.AcquiredAtSeconds,
                        ["expiresAtSeconds"] = l.ExpiresAtSeconds,
                        ["remainingSeconds"] = l.RemainingSeconds,
                    });
                }
            }

            var view = new Dictionary<string, object>
            {
                ["kind"] = "agentCoordinationCensus",
                ["omittedSessionCount"] = snapshot != null ? snapshot.OmittedSessionCount : 0,
                ["omittedLockCount"] = snapshot != null ? snapshot.OmittedLockCount : 0,
                ["activeSessionCount"] = sessions.Count,
                ["activeLockCount"] = locks.Count,
                ["sessions"] = sessions,
                ["locks"] = locks,
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
                new Dictionary<string, object> { ["name"] = "GrantPlayerCreditsJson", ["description"] = "Grant economy credits to player bank balance." },
                new Dictionary<string, object> { ["name"] = "ApplyInGameCardChangeJson", ["description"] = "Apply a data-driven card change to an entity via native EntityBalancingStore." },
                new Dictionary<string, object> { ["name"] = "OrderUnitMoveJson", ["description"] = "Dispatch an in-game move command to a unit by entity key id." },
                new Dictionary<string, object> { ["name"] = "OrderUnitAttackJson", ["description"] = "Dispatch an in-game attack command on a target entity." },
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
