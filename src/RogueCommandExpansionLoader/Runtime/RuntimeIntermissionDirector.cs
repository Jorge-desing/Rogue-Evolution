using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
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
            bool entered)
        {
            Level = level;
            Field = field;
            InstanceId = instanceId;
            Success = success;
            Message = message ?? string.Empty;
            Selected = selected;
            Entered = entered;
        }

        public int Level { get; }
        public int Field { get; }
        public int InstanceId { get; }
        public bool Success { get; }
        public string Message { get; }
        public bool Selected { get; }
        public bool Entered { get; }
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

            for (int i = 0; i < rows.Count; i++)
            {
                IntermissionNodeRow candidate = rows[i];
                if (candidate.Level == level && candidate.Field == field && candidate.IsChoosable)
                {
                    row = candidate;
                    return true;
                }
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

                IntermissionNodeSelection selection = TrySelectLevelNode(picked.Level, picked.Field);
                if (!selection.Selected)
                {
                    result = new IntermissionLevelEntry(
                        picked.Level, picked.Field, picked.InstanceId, false,
                        "Selection did not stick for level " + picked.Level + " field "
                        + picked.Field + ": " + selection.Reason
                        + " (stop point: selection).",
                        false, false);
                    return false;
                }

                 string afterScene = RuntimeIntermissionCensus.SafeActiveSceneName();
                int afterLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
                bool hasOpenRewards = ReadPlayerHasOpenRewards();
                if (HasTransitioned(beforeScene, beforeLevel, afterScene, afterLevel) || hasOpenRewards)
                {
                    result = new IntermissionLevelEntry(
                        picked.Level, picked.Field, picked.InstanceId, true,
                        "Level " + picked.Level + " node field " + picked.Field
                        + " entered via coordinate click / LoadLevel"
                        + " (scene '" + beforeScene + "' -> '" + afterScene
                        + "', level " + beforeLevel + " -> " + afterLevel + ").",
                        true, true);
                    return true;
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
                        true, false);
                    return false;
                }

                MethodInfo loadLevelMethod = loadSceneInstance.GetType().GetMethod(
                    "LoadLevel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (loadLevelMethod != null)
                {
                    try
                    {
                        loadLevelMethod.Invoke(loadSceneInstance, null);
                    }
                    catch
                    {
                    }

                    afterScene = RuntimeIntermissionCensus.SafeActiveSceneName();
                    afterLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
                    hasOpenRewards = ReadPlayerHasOpenRewards();
                    if (HasTransitioned(beforeScene, beforeLevel, afterScene, afterLevel) || hasOpenRewards)
                    {
                        result = new IntermissionLevelEntry(
                            picked.Level, picked.Field, picked.InstanceId, true,
                            "Level " + picked.Level + " node field " + picked.Field
                            + " entered via LoadScene.LoadLevel"
                            + " (scene '" + beforeScene + "' -> '" + afterScene
                            + "', level " + beforeLevel + " -> " + afterLevel + ").",
                            true, true);
                        return true;
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
                        true, false);
                    return false;
                }

                try
                {
                    startCoroutine.Invoke(loadSceneInstance, new object[] { "LoadNextLevel" });
                }
                catch (Exception ex)
                {
                    result = new IntermissionLevelEntry(
                        picked.Level, picked.Field, picked.InstanceId, false,
                        "LoadNextLevel corridor threw: " + ex.Message
                        + " (stop point: corridor missing).",
                        true, false);
                    return false;
                }

                afterScene = RuntimeIntermissionCensus.SafeActiveSceneName();
                afterLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
                if (HasTransitioned(beforeScene, beforeLevel, afterScene, afterLevel))
                {
                    result = new IntermissionLevelEntry(
                        picked.Level, picked.Field, picked.InstanceId, true,
                        "Level " + picked.Level + " node field " + picked.Field
                        + " entered via LoadScene.LoadNextLevel"
                        + " (scene '" + beforeScene + "' -> '" + afterScene
                        + "', level " + beforeLevel + " -> " + afterLevel + ").",
                        true, true);
                    return true;
                }

                result = new IntermissionLevelEntry(
                    picked.Level, picked.Field, picked.InstanceId, false,
                    "LoadNextLevel started for level " + picked.Level + " field " + picked.Field
                    + " but no scene or level transition was observed yet (scene stays '"
                    + afterScene + "') (stop point: transition observation).",
                    true, false);
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
