using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    public enum IntermissionNodeFlowKind
    {
        Unknown = 0,
        Reward = 1,
        Combat = 2,
    }

    /// <summary>
    /// Pure result of selecting an intermission level node without loading.
    /// </summary>
    public readonly struct IntermissionNodeSelection
    {
        public IntermissionNodeSelection(int level, int field, int instanceId, bool selected, string reason)
        {
            Level = level;
            Field = field;
            InstanceId = instanceId;
            Selected = selected;
            Reason = reason ?? string.Empty;
        }

        public int Level { get; }
        public int Field { get; }
        public int InstanceId { get; }
        public bool Selected { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// Pure result of the full native intermission entry flow.
    /// </summary>
    public readonly struct IntermissionLevelEntry
    {
        public IntermissionLevelEntry(
            int level,
            int field,
            int instanceId,
            bool success,
            string message,
            bool selected,
            bool entered,
            string nodeFlowKind = "Unknown",
            bool nativeCallInvoked = false,
            bool effectObserved = false)
        {
            Level = level;
            Field = field;
            InstanceId = instanceId;
            Success = success;
            Message = message ?? string.Empty;
            Selected = selected;
            Entered = entered;
            NodeFlowKind = nodeFlowKind ?? "Unknown";
            NativeCallInvoked = nativeCallInvoked;
            EffectObserved = effectObserved;
        }

        public int Level { get; }
        public int Field { get; }
        public int InstanceId { get; }
        public bool Success { get; }
        public string Message { get; }
        public bool Selected { get; }
        public bool Entered { get; }
        public string NodeFlowKind { get; }
        public bool NativeCallInvoked { get; }
        public bool EffectObserved { get; }
    }

    /// <summary>
    /// Director layer for the intermission node-selection screen.
    /// Pure policy helpers (TryPickNode / TryPickChoosableFrontier) are
    /// unit-testable without a scene. Runtime commands are reflection-based
    /// and fail closed when the game types or instances are absent.
    /// NEVER uses StageMap.JumpTo; never touches profile/save files.
    /// </summary>
    public static class RuntimeIntermissionDirector
    {
        private const BindingFlags InstanceMethods =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // ---- Pure policy helpers ----

        /// <summary>
        /// Pure predicate used by both the idempotent select short-circuit and
        /// the post-click verification: true when the (level, field) node is
        /// already observed as <c>IsCurrentNodeOfPath</c> in the census.
        /// Matching by instance ID wins when available; otherwise the level and
        /// field coordinates must match exactly.
        /// </summary>
        public static bool IsAlreadySelected(
            IReadOnlyList<IntermissionNodeRow> rows, int level, int field)
        {
            if (rows == null)
            {
                return false;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                IntermissionNodeRow row = rows[i];
                if (row.Level == level && row.Field == field && row.IsCurrentNodeOfPath)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Picks the eligible intermission node for (level, field).
        /// An exact choosable level+field match is preferred over the frontier.
        /// When both coordinates are negative (-1,-1) the wildcard frontier
        /// ranking is used (any choosable node). Otherwise the exact match is
        /// required: unknown cells fail closed so an explicit order never
        /// silently redirects to a different node.
        /// </summary>
        public static bool TryPickNode(
            IReadOnlyList<IntermissionNodeRow> rows, int level, int field, out IntermissionNodeRow row)
        {
            row = default(IntermissionNodeRow);
            if (rows == null || rows.Count == 0)
            {
                return false;
            }

            if (level < 0 && field < 0)
            {
                return TryPickChoosableFrontier(rows, out row);
            }

            int index = IntermissionCensus.FindChoosableNodeIndex(rows, level, field);
            if (index >= 0)
            {
                row = rows[index];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Picks the native frontier: choosable rows ranked by
        /// IsCurrentNodeOfPath descending, then Level ascending,
        /// then Field ascending. For callers that already carry
        /// explicit level/field values but still want the frontier node.
        /// </summary>
        public static bool TryPickChoosableFrontier(
            IReadOnlyList<IntermissionNodeRow> rows, out IntermissionNodeRow row)
        {
            row = default(IntermissionNodeRow);
            if (rows == null || rows.Count == 0)
            {
                return false;
            }

            bool found = false;
            IntermissionNodeRow best = default(IntermissionNodeRow);
            for (int i = 0; i < rows.Count; i++)
            {
                IntermissionNodeRow candidate = rows[i];
                if (!candidate.IsChoosable)
                {
                    continue;
                }

                if (!found || CompareFrontier(candidate, best) < 0)
                {
                    best = candidate;
                    found = true;
                }
            }

            if (!found)
            {
                return false;
            }

            row = best;
            return true;
        }

        /// <summary>
        /// Classifies a native map detail without guessing when the map or its
        /// reward-predicate surface is unavailable. Bosses use the combat
        /// corridor; explicit rewards (including blueprint pools) use only
        /// LoadScene.LoadLevel and never fall through to LoadNextLevel.
        /// </summary>
        public static IntermissionNodeFlowKind ClassifyNodeFlow(
            IntermissionNodeDetail detail,
            bool nativeRewardPredicatesAvailable)
        {
            if (!detail.Found || !detail.IsChoosable)
            {
                return IntermissionNodeFlowKind.Unknown;
            }

            if (detail.IsBoss)
            {
                return IntermissionNodeFlowKind.Combat;
            }

            bool hasReward = detail.IsRestSite || detail.IsShop
                || detail.HasCardReward || detail.HasUpgradeReward || detail.HasRelicReward
                || detail.HasResearchReward || detail.HasCoinReward || detail.HasStartCrystalReward
                || detail.HasResearchPointsReward || detail.HasDropReward
                || !string.IsNullOrWhiteSpace(detail.BlueprintPoolId);
            if (hasReward)
            {
                return IntermissionNodeFlowKind.Reward;
            }

            return nativeRewardPredicatesAvailable
                ? IntermissionNodeFlowKind.Combat
                : IntermissionNodeFlowKind.Unknown;
        }

        /// <summary>Pure safety gate: only classified combat nodes use LoadNextLevel.</summary>
        public static bool ShouldStartCombatLoadNextLevelCorridor(IntermissionNodeFlowKind flowKind)
        {
            return flowKind == IntermissionNodeFlowKind.Combat;
        }

        private static int CompareFrontier(IntermissionNodeRow a, IntermissionNodeRow b)
        {
            if (a.IsCurrentNodeOfPath != b.IsCurrentNodeOfPath)
            {
                return a.IsCurrentNodeOfPath ? -1 : 1;
            }

            int byLevel = a.Level.CompareTo(b.Level);
            return byLevel != 0 ? byLevel : a.Field.CompareTo(b.Field);
        }

        // ---- Runtime selection (no load) ----

        /// <summary>
        /// Selects a choosable node without loading: census, pick, then a real
        /// coordinate click — the node's screen position is projected through
        /// the main camera and a physics raycast verifies the node is under the
        /// point before the engine-message dispatch (OnMouseEnter/OnMouseDown).
        /// Selected is true only when a subsequent census observes the node
        /// as IsCurrentNodeOfPath; otherwise the stop point is reported.
        /// </summary>
        public static IntermissionNodeSelection TrySelectLevelNode(int level, int field)
        {
            IntermissionNodeSelection selected;
            TrySelectLevelNode(level, field, out selected);
            return selected;
        }

        public static bool TrySelectLevelNode(int level, int field, out IntermissionNodeSelection selected)
        {
            selected = default(IntermissionNodeSelection);
            try
            {
                IntermissionCensus census = RuntimeIntermissionCensus.Current();
                IntermissionNodeRow picked;
                if (!TryPickNode(census.Nodes, level, field, out picked))
                {
                    selected = new IntermissionNodeSelection(
                        level, field, 0, false,
                        "No choosable intermission node for level " + level + " field " + field
                        + " (stop point: node pick).");
                    return false;
                }

                Type nodeType = RuntimeIntermissionCensus.FindGameType("OverWorldMapEnemy");
                Type bossType = RuntimeIntermissionCensus.FindGameType("OverWorldMapBoss");
                if (nodeType == null && bossType == null)
                {
                    selected = new IntermissionNodeSelection(
                        picked.Level, picked.Field, picked.InstanceId, false,
                        "OverWorldMapEnemy/OverWorldMapBoss types are not available for level " + picked.Level
                        + " field " + picked.Field + " (stop point: type lookup).");
                    return false;
                }

                // Idempotent short-circuit: a previously clicked node stays
                // selected (IsCurrentNodeOfPath) while re-projection after the
                // first click fails (camera moves after selection, so the
                // secondWorldToScreenPoint can project off-screen). Confirming
                // the selection from a fresh census avoids the redundant click.
                IntermissionCensus preClickCensus = RuntimeIntermissionCensus.Current();
                if (IsSelectedInCensus(preClickCensus, picked))
                {
                    selected = new IntermissionNodeSelection(
                        picked.Level, picked.Field, picked.InstanceId, true,
                        "Level " + picked.Level + " node field " + picked.Field
                        + " is already selected (observed IsCurrentNodeOfPath); coordinate click skipped.");
                    return true;
                }

                object target = FindLiveNode(nodeType, picked) ?? FindLiveBossNode(bossType, picked);
                if (target == null)
                {
                    selected = new IntermissionNodeSelection(
                        picked.Level, picked.Field, picked.InstanceId, false,
                        "Live node instance not found for level " + picked.Level
                        + " field " + picked.Field + " (stop point: instance lookup).");
                    return false;
                }

                GameObject nodeGameObject = (target as Component)?.gameObject;
                if (nodeGameObject == null)
                {
                    selected = new IntermissionNodeSelection(
                        picked.Level, picked.Field, picked.InstanceId, false,
                        "Live node has no GameObject for level " + picked.Level
                        + " field " + picked.Field + " (stop point: node visual/registry).");
                    return false;
                }

                string invokedVia;
                try
                {
                    UiClickResult click = RuntimeUiClickDirector.TryClickWorldObject(nodeGameObject);
                    if (!click.Clicked)
                    {
                        selected = new IntermissionNodeSelection(
                            picked.Level, picked.Field, picked.InstanceId, false,
                            "Coordinate click gate failed for level " + picked.Level + " field "
                            + picked.Field + ": " + click.Message);
                        return false;
                    }

                    invokedVia = "coordinate-click (WorldToScreenPoint + Physics.Raycast → SendMessage OnMouseEnter/OnMouseDown)"
                        + " at (" + click.ScreenX.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + ", " + click.ScreenY.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
                }
                catch (Exception ex)
                {
                    selected = new IntermissionNodeSelection(
                        picked.Level, picked.Field, picked.InstanceId, false,
                        "Coordinate click for level " + picked.Level + " field " + picked.Field
                        + " threw: " + ex.Message);
                    return false;
                }

                IntermissionCensus after = RuntimeIntermissionCensus.Current();
                if (IsSelectedInCensus(after, picked))
                {
                    selected = new IntermissionNodeSelection(
                        picked.Level, picked.Field, picked.InstanceId, true,
                        "Selected level " + picked.Level + " field " + picked.Field + " via "
                        + invokedVia + " (observed IsCurrentNodeOfPath).");
                    return true;
                }

                selected = new IntermissionNodeSelection(
                    picked.Level, picked.Field, picked.InstanceId, false,
                    "Clicked " + invokedVia + " for level " + picked.Level + " field "
                    + picked.Field + " but selection was not observable via census "
                    + "(stop point: selection observation).");
                return false;
            }
            catch (Exception ex)
            {
                selected = new IntermissionNodeSelection(
                    level, field, 0, false,
                    "Select-level node failed: " + ex.Message);
                return false;
            }
        }

        // ---- Runtime full entry flow ----

        /// <summary>
        /// Full native entry sequence: census, pick, select (no load), then the
        /// confirm path via the scene LoadScene.StartCoroutine("LoadNextLevel"),
        /// verified by an immediately observed scene/level transition.
        /// Success is true only on an observed transition. Never JumpTo, never saves.
        /// </summary>
        public static IntermissionLevelEntry TryEnterLevelNodeFlow(int level, int field)
        {
            IntermissionLevelEntry result;
            TryEnterLevelNodeFlow(level, field, out result);
            return result;
        }

        public static bool TryEnterLevelNodeFlow(int level, int field, out IntermissionLevelEntry result)
        {
            result = default(IntermissionLevelEntry);
            string beforeScene = RuntimeIntermissionCensus.SafeActiveSceneName();
            int beforeLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
            bool loadCallInvoked = false;
            bool coroutineInvoked = false;
            try
            {
                IntermissionCensus census = RuntimeIntermissionCensus.Current();
                IntermissionNodeRow picked;
                if (!TryPickNode(census.Nodes, level, field, out picked))
                {
                    result = new IntermissionLevelEntry(
                        level, field, 0, false,
                        "No choosable intermission node for level " + level + " field " + field
                        + " (stop point: node pick).",
                        false, false);
                    return false;
                }

                IntermissionNodeDetail nodeDetail = RuntimeIntermissionDeepReads.InspectNode(picked.Level, picked.Field);
                bool rewardPredicatesAvailable = HasNativeRewardPredicateSurface();
                IntermissionNodeFlowKind flowKind = ClassifyNodeFlow(nodeDetail, rewardPredicatesAvailable);
                string flowKindName = flowKind.ToString();
                if (flowKind == IntermissionNodeFlowKind.Unknown)
                {
                    result = new IntermissionLevelEntry(
                        picked.Level, picked.Field, picked.InstanceId, false,
                        "The selected node could not be classified from the current native LevelMap reward predicates; no load action was issued.",
                        false, false, flowKindName);
                    return false;
                }

                IntermissionRewardState beforeRewardState = flowKind == IntermissionNodeFlowKind.Reward
                    ? RuntimeIntermissionRewards.GetState(32)
                    : IntermissionRewardState.Unavailable;
                IntermissionNativePanelState beforeNativePanels = flowKind == IntermissionNodeFlowKind.Reward
                    ? RuntimeIntermissionNativePanels.GetState(32)
                    : IntermissionNativePanelState.Unavailable;
                string beforeNavigatorState = ReadNavigatorSceneState();

                IntermissionNodeSelection selection = TrySelectLevelNode(picked.Level, picked.Field);
                if (!selection.Selected)
                {
                    result = new IntermissionLevelEntry(
                        picked.Level, picked.Field, picked.InstanceId, false,
                        "Selection did not stick for level " + picked.Level + " field "
                        + picked.Field + ": " + selection.Reason
                        + " (stop point: selection).",
                        false, false, flowKindName);
                    return false;
                }

                string afterScene = RuntimeIntermissionCensus.SafeActiveSceneName();
                int afterLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
                bool hasOpenRewards = ReadPlayerHasOpenRewards();
                bool nextLevelLoading = RuntimeIntermissionCensus.IsNextLevelLoading();

                if (flowKind == IntermissionNodeFlowKind.Reward)
                {
                    IntermissionRewardState afterSelectionRewards = RuntimeIntermissionRewards.GetState(32);
                    IntermissionNativePanelState afterSelectionPanels = RuntimeIntermissionNativePanels.GetState(32);
                    IntermissionNodeDetail afterSelectionDetail = RuntimeIntermissionDeepReads.InspectNode(picked.Level, picked.Field);
                    bool rewardEffectObserved = HasRewardResolutionPostcondition(
                        beforeScene, beforeLevel, beforeNavigatorState, beforeRewardState, beforeNativePanels,
                        afterScene, afterLevel, hasOpenRewards, afterSelectionRewards, afterSelectionPanels,
                        nodeDetail, afterSelectionDetail);
                    bool nativeCallInvoked = false;

                    if (!rewardEffectObserved && !nextLevelLoading)
                    {
                        IntermissionActionResult rewardAction = RuntimeIntermissionRewards.TryTriggerLoadLevel();
                        nativeCallInvoked = rewardAction.NativeCallInvoked;
                        afterScene = RuntimeIntermissionCensus.SafeActiveSceneName();
                        afterLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
                        hasOpenRewards = ReadPlayerHasOpenRewards();
                        nextLevelLoading = RuntimeIntermissionCensus.IsNextLevelLoading();
                        IntermissionRewardState afterLoadRewards = RuntimeIntermissionRewards.GetState(32);
                        IntermissionNativePanelState afterLoadPanels = RuntimeIntermissionNativePanels.GetState(32);
                        IntermissionNodeDetail afterLoadDetail = RuntimeIntermissionDeepReads.InspectNode(picked.Level, picked.Field);
                        rewardEffectObserved = HasRewardResolutionPostcondition(
                            beforeScene, beforeLevel, beforeNavigatorState, beforeRewardState, beforeNativePanels,
                            afterScene, afterLevel, hasOpenRewards, afterLoadRewards, afterLoadPanels,
                            nodeDetail, afterLoadDetail);
                    }

                    result = new IntermissionLevelEntry(
                        picked.Level, picked.Field, picked.InstanceId, rewardEffectObserved,
                        rewardEffectObserved
                            ? "Reward node " + picked.Level + ":" + picked.Field
                                + " resolved through the native reward flow; no combat LoadNextLevel corridor was used."
                            : "Reward node " + picked.Level + ":" + picked.Field
                                + (nativeCallInvoked
                                    ? " was selected and LoadScene.LoadLevel was invoked, but no reward postcondition is observable yet; no LoadNextLevel corridor was used."
                                    : " was selected, but no reward postcondition is observable yet; no additional level load was issued."),
                        true, false, flowKindName, nativeCallInvoked, rewardEffectObserved);
                    return rewardEffectObserved;
                }

                if (!ShouldStartCombatLoadNextLevelCorridor(flowKind))
                {
                    result = new IntermissionLevelEntry(
                        picked.Level, picked.Field, picked.InstanceId, false,
                        "Node flow did not pass the combat LoadNextLevel gate; no level load was issued.",
                        true, false, flowKindName);
                    return false;
                }

                if (HasTransitioned(beforeScene, beforeLevel, afterScene, afterLevel))
                {
                    result = new IntermissionLevelEntry(
                        picked.Level, picked.Field, picked.InstanceId, true,
                        "Combat node " + picked.Level + ":" + picked.Field
                        + " entered through the native route (scene '" + beforeScene + "' -> '" + afterScene
                        + "', level " + beforeLevel + " -> " + afterLevel + ").",
                        true, true, flowKindName, false, true);
                    return true;
                }

                if (hasOpenRewards)
                {
                    result = new IntermissionLevelEntry(
                        picked.Level, picked.Field, picked.InstanceId, false,
                        "A reward screen is open after combat-node selection; the director stopped before LoadNextLevel.",
                        true, false, flowKindName, false, true);
                    return false;
                }

                if (nextLevelLoading)
                {
                    result = new IntermissionLevelEntry(
                        picked.Level, picked.Field, picked.InstanceId, false,
                        "The combat level load is already in progress; the director will not start a second corridor before scene transition evidence.",
                        true, false, flowKindName, false, true);
                    return false;
                }

                Type loadSceneType = RuntimeIntermissionCensus.FindGameType("LoadScene");
                object loadSceneInstance = loadSceneType == null
                    ? null
                    : RuntimeIntermissionCensus.FindInstanceOf(loadSceneType);
                if (loadSceneInstance == null)
                {
                    result = new IntermissionLevelEntry(
                        picked.Level, picked.Field, picked.InstanceId, false,
                        "Selection confirmed for level " + picked.Level + " field " + picked.Field
                        + " but the LoadScene corridor is not available"
                        + " (stop point: corridor missing).",
                        true, false, flowKindName);
                    return false;
                }

                MethodInfo loadLevelMethod = loadSceneInstance.GetType().GetMethod(
                    "LoadLevel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (loadLevelMethod != null)
                {
                    try
                    {
                        loadCallInvoked = true;
                        loadLevelMethod.Invoke(loadSceneInstance, null);
                    }
                    catch
                    {
                    }

                    afterScene = RuntimeIntermissionCensus.SafeActiveSceneName();
                    afterLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
                    hasOpenRewards = ReadPlayerHasOpenRewards();
                    nextLevelLoading = RuntimeIntermissionCensus.IsNextLevelLoading();
                    if (HasTransitioned(beforeScene, beforeLevel, afterScene, afterLevel))
                    {
                        result = new IntermissionLevelEntry(
                            picked.Level, picked.Field, picked.InstanceId, true,
                            "Level " + picked.Level + " node field " + picked.Field
                            + " entered via LoadScene.LoadLevel"
                            + " (scene '" + beforeScene + "' -> '" + afterScene
                            + "', level " + beforeLevel + " -> " + afterLevel + ").",
                            true, true, flowKindName, loadCallInvoked, true);
                        return true;
                    }

                    if (hasOpenRewards)
                    {
                        result = new IntermissionLevelEntry(
                            picked.Level, picked.Field, picked.InstanceId, false,
                            "A reward screen opened during combat-node LoadLevel; the director stopped before LoadNextLevel.",
                            true, false, flowKindName, loadCallInvoked, true);
                        return false;
                    }

                    if (nextLevelLoading)
                    {
                        result = new IntermissionLevelEntry(
                            picked.Level, picked.Field, picked.InstanceId, false,
                            "Combat LoadLevel started a transition; the director stopped before a duplicate LoadNextLevel call.",
                            true, false, flowKindName, loadCallInvoked, true);
                        return false;
                    }
                }

                MethodInfo startCoroutine = loadSceneInstance.GetType().GetMethod(
                    "StartCoroutine", new[] { typeof(string) });
                if (startCoroutine == null)
                {
                    result = new IntermissionLevelEntry(
                        picked.Level, picked.Field, picked.InstanceId, false,
                        "Selection confirmed but MonoBehaviour.StartCoroutine(String) is not available"
                        + " (stop point: corridor missing).",
                        true, false, flowKindName, loadCallInvoked, false);
                    return false;
                }

                if (!RuntimeIntermissionCensus.IsNextLevelLoading())
                {
                    try
                    {
                        startCoroutine.Invoke(loadSceneInstance, new object[] { "LoadNextLevel" });
                        coroutineInvoked = true;
                    }
                    catch (Exception ex)
                    {
                        result = new IntermissionLevelEntry(
                            picked.Level, picked.Field, picked.InstanceId, false,
                            "LoadNextLevel corridor threw: " + ex.Message
                            + " (stop point: corridor missing).",
                            true, false, flowKindName, loadCallInvoked || coroutineInvoked, false);
                        return false;
                    }
                }

                afterScene = RuntimeIntermissionCensus.SafeActiveSceneName();
                afterLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
                nextLevelLoading = RuntimeIntermissionCensus.IsNextLevelLoading();
                if (HasTransitioned(beforeScene, beforeLevel, afterScene, afterLevel))
                {
                    result = new IntermissionLevelEntry(
                        picked.Level, picked.Field, picked.InstanceId, true,
                        "Level " + picked.Level + " node field " + picked.Field
                        + " entered via LoadScene.LoadNextLevel"
                        + " (scene '" + beforeScene + "' -> '" + afterScene
                        + "', level " + beforeLevel + " -> " + afterLevel + ").",
                        true, true, flowKindName, loadCallInvoked || coroutineInvoked, true);
                    return true;
                }

                result = new IntermissionLevelEntry(
                    picked.Level, picked.Field, picked.InstanceId, false,
                    "LoadNextLevel started for level " + picked.Level + " field " + picked.Field
                    + " but no scene or level transition was observed yet (scene stays '"
                    + afterScene + "') (stop point: transition observation).",
                    true, false, flowKindName, loadCallInvoked || coroutineInvoked, nextLevelLoading);
                return false;
            }
            catch (Exception ex)
            {
                result = new IntermissionLevelEntry(
                    level, field, 0, false,
                    "Enter-level node flow failed: " + ex.Message,
                    false, false);
                return false;
            }
        }

        private static bool HasNativeRewardPredicateSurface()
        {
            object levelMap = RuntimeIntermissionDeepReads.ResolveCurrentLevelMap();
            if (levelMap == null) return false;
            string[] predicateNames =
            {
                "IsRestSite", "IsShop", "IsCardReward", "IsUpgradeReward", "IsRelicReward",
                "IsResearchReward", "IsCoinReward", "IsStartCrystalReward", "IsResearchPointsReward", "IsDropReward",
            };
            Type type = levelMap.GetType();
            Type[] parameters = { typeof(int), typeof(int) };
            for (int i = 0; i < predicateNames.Length; i++)
            {
                MethodInfo method = type.GetMethod(predicateNames[i], InstanceMethods, null, parameters, null);
                if (method == null || method.ReturnType != typeof(bool)) return false;
            }
            return true;
        }

        private static bool HasRewardResolutionPostcondition(
            string beforeScene,
            int beforeLevel,
            string beforeNavigatorState,
            IntermissionRewardState beforeRewards,
            IntermissionNativePanelState beforePanels,
            string afterScene,
            int afterLevel,
            bool hasOpenRewards,
            IntermissionRewardState afterRewards,
            IntermissionNativePanelState afterPanels,
            IntermissionNodeDetail beforeNode,
            IntermissionNodeDetail afterNode)
        {
            if (hasOpenRewards || beforeLevel != afterLevel
                || !string.Equals(beforeScene, afterScene, StringComparison.Ordinal)
                || !string.Equals(beforeNavigatorState, ReadNavigatorSceneState(), StringComparison.Ordinal))
            {
                return true;
            }

            if (beforeRewards.IsAvailable && afterRewards.IsAvailable
                && (beforeRewards.CurrentCoins != afterRewards.CurrentCoins
                    || beforeRewards.CurrentStartCrystals != afterRewards.CurrentStartCrystals
                    || beforeRewards.CurrentDropPodCount != afterRewards.CurrentDropPodCount
                    || beforeRewards.CurrentResearchPoints != afterRewards.CurrentResearchPoints
                    || beforeRewards.OpenEngineerRewardCount != afterRewards.OpenEngineerRewardCount
                    || beforeRewards.OpenEconomyRewardCount != afterRewards.OpenEconomyRewardCount
                    || beforeRewards.OpenSpecialistRewardCount != afterRewards.OpenSpecialistRewardCount
                    || beforeRewards.OpenSpecialistRelicRewardCount != afterRewards.OpenSpecialistRelicRewardCount
                    || beforeRewards.OpenCardRewardCount != afterRewards.OpenCardRewardCount
                    || beforeRewards.OpenRelicRewardCount != afterRewards.OpenRelicRewardCount
                    || beforeRewards.OpenUpgradeRewardCount != afterRewards.OpenUpgradeRewardCount
                    || beforeRewards.OpenResearchRewardCount != afterRewards.OpenResearchRewardCount))
            {
                return true;
            }

            if (beforePanels.IsAvailable && afterPanels.IsAvailable
                && (beforePanels.IsShopActive != afterPanels.IsShopActive
                    || beforePanels.IsRemoveCardActive != afterPanels.IsRemoveCardActive
                    || beforePanels.ShopOfferCount != afterPanels.ShopOfferCount))
            {
                return true;
            }

            return beforeNode.Found && afterNode.Found
                && beforeNode.IsChoosable && !afterNode.IsChoosable;
        }

        private static string ReadNavigatorSceneState()
        {
            try
            {
                Type type = RuntimeIntermissionCensus.FindGameType("Navigator");
                PropertyInfo prop = type?.GetProperty("CurrentSceneState", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.CanRead) return prop.GetValue(null, null)?.ToString() ?? string.Empty;
                FieldInfo field = type?.GetField("CurrentSceneState", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return field?.GetValue(null)?.ToString() ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        // ---- Live-node helpers (reflection, scene-bound) ----

        private static object FindLiveNode(Type nodeType, IntermissionNodeRow picked)
        {
            if (nodeType == null)
            {
                return null;
            }

            foreach (object node in RuntimeIntermissionCensus.ReadNodeObjects(nodeType))
            {
                if (node == null)
                {
                    continue;
                }

                if (picked.InstanceId != 0)
                {
                    int instanceId = RuntimeIntermissionCensus.ReadInstanceId(node);
                    if (instanceId == picked.InstanceId)
                    {
                        return node;
                    }
                }

                int nodeLevel = RuntimeIntermissionCensus.ReadIntField(node, "_level", int.MinValue);
                int nodeField = RuntimeIntermissionCensus.ReadIntField(node, "_field", int.MinValue);
                if (nodeLevel == picked.Level && nodeField == picked.Field)
                {
                    bool isChoosable = RuntimeIntermissionCensus.ReadBoolField(node, "_isChoosable", false);
                    if (isChoosable)
                    {
                        return node;
                    }
                }
            }

            return null;
        }

        private static object FindLiveBossNode(Type bossType, IntermissionNodeRow picked)
        {
            if (bossType == null)
            {
                return null;
            }

            foreach (object boss in RuntimeIntermissionCensus.ReadNodeObjects(bossType))
            {
                if (boss == null)
                {
                    continue;
                }

                if (picked.InstanceId != 0)
                {
                    int instanceId = RuntimeIntermissionCensus.ReadInstanceId(boss);
                    if (instanceId == picked.InstanceId)
                    {
                        return boss;
                    }
                }

                object levelMap = RuntimeIntermissionCensus.ReadObjectField(boss, "_levelMap");
                int bossLevel = RuntimeIntermissionCensus.ReadBossLevel(levelMap);
                if (bossLevel > 0 && picked.Level == bossLevel && picked.Field == 0)
                {
                    bool isChoosable = RuntimeIntermissionCensus.ReadBoolField(boss, "_isChoosable", false);
                    if (isChoosable)
                    {
                        return boss;
                    }
                }
            }

            return null;
        }

        private static bool ReadPlayerHasOpenRewards()
        {
            try
            {
                Type gameType = RuntimeIntermissionCensus.FindGameType("Game");
                if (gameType == null)
                {
                    return false;
                }

                PropertyInfo prop = gameType.GetProperty(
                    "PlayerHasOpenRewards", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.CanRead && prop.PropertyType == typeof(bool))
                {
                    return (bool)prop.GetValue(null, null);
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsSelectedInCensus(IntermissionCensus census, IntermissionNodeRow picked)
        {
            if (census == null || census.Nodes == null)
            {
                return false;
            }

            for (int i = 0; i < census.Nodes.Count; i++)
            {
                IntermissionNodeRow row = census.Nodes[i];
                bool matches = picked.InstanceId != 0
                    ? row.InstanceId == picked.InstanceId
                    : (row.Level == picked.Level && row.Field == picked.Field);
                if (matches && row.IsCurrentNodeOfPath)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasTransitioned(
            string beforeScene, int beforeLevel, string afterScene, int afterLevel)
        {
            bool sceneChanged = !string.Equals(beforeScene, afterScene, StringComparison.Ordinal);
            if (sceneChanged)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(afterScene)
                && afterScene.IndexOf("Intermission", StringComparison.OrdinalIgnoreCase) < 0
                && !string.IsNullOrEmpty(beforeScene)
                && beforeScene.IndexOf("Intermission", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return afterLevel != beforeLevel;
        }
    }
}
