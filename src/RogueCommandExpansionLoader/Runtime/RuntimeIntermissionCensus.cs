using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// One OverWorldMapEnemy node on the intermission (level-select) map, captured
    /// for agent-readable diagnostics. Decoupled from the game type so it can be
    /// built, sorted, clamped, and unit-tested without a live scene.
    /// </summary>
    public readonly struct IntermissionNodeRow
    {
        public IntermissionNodeRow(
            int level,
            int field,
            bool isChoosable,
            bool isCurrentNodeOfPath,
            bool hasCardReward,
            string position,
            int instanceId)
        {
            Level = level;
            Field = field;
            IsChoosable = isChoosable;
            IsCurrentNodeOfPath = isCurrentNodeOfPath;
            HasCardReward = hasCardReward;
            Position = position ?? string.Empty;
            InstanceId = instanceId;
        }

        public int Level { get; }
        public int Field { get; }
        public bool IsChoosable { get; }
        public bool IsCurrentNodeOfPath { get; }
        public bool HasCardReward { get; }
        public string Position { get; }
        public int InstanceId { get; }
    }

    /// <summary>
    /// Pure, agent-readable census of the intermission level-select nodes.
    /// Omission counters are declared before the detail list so bounded
    /// diagnostic truncation never suppresses loss counts (repo rule, same
    /// ordering as RunProgressionCensus).
    /// </summary>
    public sealed class IntermissionCensus
    {
        public bool IsIntermissionActive { get; }
        public int OmittedNodeCount { get; }
        public int NodeCount { get; }
        public int CurrentStage { get; }
        public int CurrentLevel { get; }
        public IReadOnlyList<IntermissionNodeRow> Nodes { get; }

        public IntermissionCensus(
            bool isIntermissionActive,
            int omittedNodeCount,
            int nodeCount,
            int currentStage,
            int currentLevel,
            IReadOnlyList<IntermissionNodeRow> nodes)
        {
            IsIntermissionActive = isIntermissionActive;
            OmittedNodeCount = omittedNodeCount;
            NodeCount = nodeCount;
            CurrentStage = currentStage;
            CurrentLevel = currentLevel;
            Nodes = nodes ?? new List<IntermissionNodeRow>().AsReadOnly();
        }

        public static IntermissionCensus Empty { get; } =
            new IntermissionCensus(false, 0, 0, 0, 0, new List<IntermissionNodeRow>().AsReadOnly());

        /// <summary>
        /// Pure sort + clamp + omission count: sorts rows by level then field
        /// (ascending), keeps at most the clamped maxRows, and reports the
        /// remainder as OmittedNodeCount. NodeCount is the number of kept rows.
        /// Never throws on null input.
        /// </summary>
        public static IntermissionCensus Build(
            IReadOnlyList<IntermissionNodeRow> rows,
            bool isIntermissionActive,
            int currentStage,
            int currentLevel,
            int maxRows)
        {
            var list = new List<IntermissionNodeRow>();
            if (rows != null)
            {
                list.AddRange(rows);
            }

            list.Sort((a, b) =>
            {
                int byLevel = a.Level.CompareTo(b.Level);
                return byLevel != 0 ? byLevel : a.Field.CompareTo(b.Field);
            });

            int clamp = RuntimeIntermissionCensus.ClampMaxRows(maxRows);
            int kept = list.Count <= clamp ? list.Count : clamp;
            int omitted = list.Count - kept;

            var keptRows = list.GetRange(0, kept).AsReadOnly();
            return new IntermissionCensus(
                isIntermissionActive, omitted, kept, currentStage, currentLevel, keptRows);
        }

        /// <summary>Finds the choosable node index for a level/field pair, or -1.</summary>
        internal static int FindChoosableNodeIndex(
            IReadOnlyList<IntermissionNodeRow> rows, int level, int field)
        {
            if (rows == null)
            {
                return -1;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                IntermissionNodeRow row = rows[i];
                if (row.Level == level && row.Field == field && row.IsChoosable)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>Pure diagnostic result of an enter-level-node attempt.</summary>
    public readonly struct EnterLevelNodeResult
    {
        public EnterLevelNodeResult(bool success, string message, int level, int field)
        {
            Success = success;
            Message = message ?? string.Empty;
            Level = level;
            Field = field;
        }

        public bool Success { get; }
        public string Message { get; }
        public int Level { get; }
        public int Field { get; }
    }

    /// <summary>Pure diagnostic snapshot of the live Navigator pause/time state.</summary>
    public readonly struct NavigatorStateSnapshot
    {
        public NavigatorStateSnapshot(
            bool isPaused,
            bool isDebugPaused,
            float timeScale,
            float scaledTime,
            float unscaledTime)
        {
            IsPaused = isPaused;
            IsDebugPaused = isDebugPaused;
            TimeScale = timeScale;
            ScaledTime = scaledTime;
            UnscaledTime = unscaledTime;
        }

        public bool IsPaused { get; }
        public bool IsDebugPaused { get; }
        public float TimeScale { get; }
        public float ScaledTime { get; }
        public float UnscaledTime { get; }
    }

    /// <summary>Pure diagnostic result of a Navigator pause/unpause attempt.</summary>
    public readonly struct NavigatorPauseResult
    {
        public NavigatorPauseResult(bool success, string message, bool isPaused)
        {
            Success = success;
            Message = message ?? string.Empty;
            IsPaused = isPaused;
        }

        public bool Success { get; }
        public string Message { get; }
        public bool IsPaused { get; }
    }

    /// <summary>
    /// Read-only census of the live intermission map. Uses reflection for the
    /// game-only types (OverWorldMapEnemy) so this file compiles in the offline
    /// harness (which has no game assembly) and fails closed when they are absent.
    /// Never throws: every read degrades to an empty/neutral value.
    /// </summary>
    public static class RuntimeIntermissionCensus
    {
        public const int DefaultMaxRows = 64;

        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static int ClampMaxRows(int maxRows)
        {
            return maxRows <= 0 ? DefaultMaxRows : maxRows;
        }

        public static IntermissionCensus Current(int maxRows = DefaultMaxRows)
        {
            try
            {
                var rows = new List<IntermissionNodeRow>();
                try
                {
                    Type nodeType = FindGameType("OverWorldMapEnemy");
                    if (nodeType != null)
                    {
                        foreach (object node in ReadNodeObjects(nodeType))
                        {
                            if (node == null)
                            {
                                continue;
                            }

                            int level = ReadIntField(node, "_level", 0);
                            int field = ReadIntField(node, "_field", 0);
                            bool isChoosable = ReadBoolField(node, "_isChoosable", false);
                            bool isCurrentNodeOfPath = ReadBoolProperty(node, "IsCurrentNodeOfPath", false);
                            bool hasCardReward = DetectCardReward(node);
                            string position = ReadPosition(node);
                            int instanceId = ReadInstanceId(node);
                            rows.Add(new IntermissionNodeRow(
                                level, field, isChoosable, isCurrentNodeOfPath,
                                hasCardReward, position, instanceId));
                        }
                    }
                }
                catch
                {
                    // Degrade: keep whatever was read so far.
                }

                string sceneName = SafeActiveSceneName();
                bool isIntermissionActive =
                    sceneName.IndexOf("Intermission", StringComparison.OrdinalIgnoreCase) >= 0
                    || rows.Count > 0;

                int currentLevel = SafeCurrentLevel();
                int currentStage = SafeCurrentStage();

                return IntermissionCensus.Build(rows, isIntermissionActive, currentStage, currentLevel, maxRows);
            }
            catch
            {
                return IntermissionCensus.Empty;
            }
        }

        // ---- Reflection helpers (shared with enter-level + navigator) ----

        internal static Type FindGameType(string typeName)
        {
            try
            {
                // Prefer the game assembly (typeof(Game)) over every loaded assembly.
                try
                {
                    Type inGame = typeof(Game).Assembly.GetType(typeName, false);
                    if (inGame != null)
                    {
                        return inGame;
                    }
                }
                catch
                {
                }

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type candidate = null;
                    try
                    {
                        candidate = assembly.GetType(typeName, false);
                    }
                    catch
                    {
                        // Dynamic/partial assemblies can throw here; keep scanning.
                    }

                    if (candidate != null)
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        internal static object FindInstanceOf(Type type)
        {
            if (type == null)
            {
                return null;
            }

            try
            {
                MethodInfo finder = typeof(UnityEngine.Object).GetMethod(
                    "FindObjectOfType", new[] { typeof(Type) });
                if (finder != null)
                {
                    object instance = finder.Invoke(null, new object[] { type });
                    if (instance != null)
                    {
                        return instance;
                    }
                }
            }
            catch
            {
            }

            try
            {
                MethodInfo finderAll = typeof(UnityEngine.Object).GetMethod(
                    "FindObjectsOfType", new[] { typeof(Type) });
                if (finderAll != null)
                {
                    object array = finderAll.Invoke(null, new object[] { type });
                    var objects = array as System.Array;
                    if (objects != null && objects.Length > 0)
                    {
                        return objects.GetValue(0);
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        internal static object[] ReadNodeObjects(Type nodeType)
        {
            if (nodeType == null)
            {
                return new object[0];
            }

            try
            {
                MethodInfo method = typeof(UnityEngine.Object).GetMethod(
                    "FindObjectsOfType", new[] { typeof(Type), typeof(bool) });
                if (method != null)
                {
                    object array = method.Invoke(null, new object[] { nodeType, true });
                    if (array is System.Array span)
                    {
                        var result = new object[span.Length];
                        span.CopyTo(result, 0);
                        return result;
                    }
                }
            }
            catch
            {
            }

            try
            {
                MethodInfo method = typeof(UnityEngine.Object).GetMethod(
                    "FindObjectsOfType", new[] { typeof(Type) });
                if (method != null)
                {
                    object array = method.Invoke(null, new object[] { nodeType });
                    if (array is System.Array span)
                    {
                        var result = new object[span.Length];
                        span.CopyTo(result, 0);
                        return result;
                    }
                }
            }
            catch
            {
            }

            return new object[0];
        }

        internal static string SafeActiveSceneName()
        {
            try
            {
                Scene scene = SceneManager.GetActiveScene();
                return scene.name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        internal static int SafeCurrentLevel()
        {
            try
            {
                return Game.CurrentLevel;
            }
            catch
            {
                return 0;
            }
        }

        internal static int SafeCurrentStage()
        {
            try
            {
                PropertyInfo stageMapProp = typeof(Game).GetProperty(
                    "StageMap", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object stageMap = stageMapProp != null ? stageMapProp.GetValue(null, null) : null;
                if (stageMap == null)
                {
                    FieldInfo stageMapField = typeof(Game).GetField(
                        "StageMap", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (stageMapField != null)
                    {
                        stageMap = stageMapField.GetValue(null);
                    }
                }

                if (stageMap == null)
                {
                    return 0;
                }

                PropertyInfo currentStageProp = stageMap.GetType().GetProperty(
                    "CurrentStage", BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic);
                if (currentStageProp != null)
                {
                    object value = currentStageProp.GetValue(stageMap, null);
                    if (value is int stage)
                    {
                        return stage;
                    }
                }

                FieldInfo currentStageField = stageMap.GetType().GetField(
                        "CurrentStage", BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.Public | BindingFlags.NonPublic)
                    ?? stageMap.GetType().GetField(
                        "<CurrentStage>k__BackingField",
                        BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.Public | BindingFlags.NonPublic);
                if (currentStageField != null)
                {
                    object value = currentStageField.GetValue(stageMap);
                    if (value is int stage)
                    {
                        return stage;
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        internal static int ReadIntField(object target, string fieldName, int fallback)
        {
            try
            {
                FieldInfo field = target.GetType().GetField(fieldName, InstanceMembers);
                if (field != null)
                {
                    object value = field.GetValue(target);
                    if (value is int intValue)
                    {
                        return intValue;
                    }
                }
            }
            catch
            {
            }

            return fallback;
        }

        internal static bool ReadBoolField(object target, string fieldName, bool fallback)
        {
            try
            {
                FieldInfo field = target.GetType().GetField(fieldName, InstanceMembers);
                if (field != null)
                {
                    object value = field.GetValue(target);
                    if (value is bool boolValue)
                    {
                        return boolValue;
                    }
                }
            }
            catch
            {
            }

            return fallback;
        }

        internal static bool ReadBoolProperty(object target, string propertyName, bool fallback)
        {
            try
            {
                PropertyInfo property = target.GetType().GetProperty(propertyName, InstanceMembers);
                if (property != null && property.CanRead)
                {
                    object value = property.GetValue(target, null);
                    if (value is bool boolValue)
                    {
                        return boolValue;
                    }
                }
            }
            catch
            {
            }

            return fallback;
        }

        internal static int ReadInstanceId(object target)
        {
            try
            {
                MethodInfo method = target.GetType().GetMethod(
                    "GetInstanceID", BindingFlags.Instance | BindingFlags.Public);
                if (method != null)
                {
                    object value = method.Invoke(target, null);
                    if (value is int intValue)
                    {
                        return intValue;
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        internal static string ReadPosition(object target)
        {
            try
            {
                PropertyInfo transformProp = target.GetType().GetProperty(
                    "transform", InstanceMembers);
                object transform = transformProp != null ? transformProp.GetValue(target, null) : null;
                if (transform == null)
                {
                    return string.Empty;
                }

                PropertyInfo positionProp = transform.GetType().GetProperty(
                    "position", InstanceMembers);
                if (positionProp == null)
                {
                    return string.Empty;
                }

                object positionValue = positionProp.GetValue(transform, null);
                if (positionValue is Vector3 position)
                {
                    return position.x.ToString("R", CultureInfo.InvariantCulture)
                        + "," + position.y.ToString("R", CultureInfo.InvariantCulture)
                        + "," + position.z.ToString("R", CultureInfo.InvariantCulture);
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        /// <summary>
        /// Heuristic: true when any descendant transform name contains
        /// "CardReward" (children are inactive until the node is chosen, so the
        /// scan includes inactive transforms). Depth-capped to avoid runaway scans.
        /// Not provable statically beyond the pinned member names.
        /// </summary>
        internal static bool DetectCardReward(object target)
        {
            const int maxDepth = 4;
            const int visitBudget = 256;
            int budget = visitBudget;
            try
            {
                PropertyInfo transformProp = target.GetType().GetProperty("transform", InstanceMembers);
                object root = transformProp != null ? transformProp.GetValue(target, null) : null;
                if (root != null && ScanForCardReward(root, 0, maxDepth, ref budget))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool ScanForCardReward(object transform, int depth, int maxDepth, ref int budget)
        {
            if (transform == null || depth > maxDepth || budget <= 0)
            {
                return false;
            }

            budget = budget - 1;
            try
            {
                PropertyInfo nameProp = transform.GetType().GetProperty("name", InstanceMembers);
                object nameValue = nameProp != null ? nameProp.GetValue(transform, null) : null;
                if (nameValue is string name &&
                    name.IndexOf("CardReward", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                PropertyInfo childCountProp = transform.GetType().GetProperty(
                    "childCount", InstanceMembers);
                object childCountValue = childCountProp != null
                    ? childCountProp.GetValue(transform, null) : null;
                if (!(childCountValue is int childCount) || childCount <= 0)
                {
                    return false;
                }

                MethodInfo getChild = transform.GetType().GetMethod(
                    "GetChild", new[] { typeof(int) });
                if (getChild == null)
                {
                    return false;
                }

                for (int i = 0; i < childCount; i++)
                {
                    if (budget <= 0)
                    {
                        return false;
                    }

                    object child = getChild.Invoke(transform, new object[] { i });
                    if (ScanForCardReward(child, depth + 1, maxDepth, ref budget))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }
    }

    /// <summary>
    /// Enter-level-node mutation. Invokes the native OverWorldMapEnemy.OnMouseDown
    /// handler for a choosable node; if that alone does not leave Intermission,
    /// it starts the native LoadScene.LoadNextLevel coroutine via
    /// MonoBehaviour.StartCoroutine(String). It NEVER calls StageMap.JumpTo and
    /// never touches profile/save files. Success requires an observed scene
    /// (away from Intermission) or CurrentLevel change; anything else fails
    /// closed with the exact stop point in the message.
    /// </summary>
    public static class RuntimeIntermissionEnter
    {
        public static EnterLevelNodeResult TryEnterLevelNode(int level, int field)
        {
            try
            {
                Type nodeType = RuntimeIntermissionCensus.FindGameType("OverWorldMapEnemy");
                if (nodeType == null)
                {
                    return new EnterLevelNodeResult(false,
                        "OverWorldMapEnemy type is not available in this runtime for level " + level
                        + " field " + field + " (stop point: type lookup).",
                        level, field);
                }

                object target = null;
                try
                {
                    foreach (object node in RuntimeIntermissionCensus.ReadNodeObjects(nodeType))
                    {
                        if (node == null)
                        {
                            continue;
                        }

                        int nodeLevel = RuntimeIntermissionCensus.ReadIntField(node, "_level", int.MinValue);
                        int nodeField = RuntimeIntermissionCensus.ReadIntField(node, "_field", int.MinValue);
                        bool isChoosable = RuntimeIntermissionCensus.ReadBoolField(node, "_isChoosable", false);
                        if (nodeLevel == level && nodeField == field && isChoosable)
                        {
                            target = node;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    return new EnterLevelNodeResult(false,
                        "Failed scanning intermission nodes: " + ex.Message, level, field);
                }

                if (target == null)
                {
                    return new EnterLevelNodeResult(false,
                        "No choosable intermission node found for level " + level + " field " + field + ".",
                        level, field);
                }

                string beforeScene = RuntimeIntermissionCensus.SafeActiveSceneName();
                int beforeLevel = RuntimeIntermissionCensus.SafeCurrentLevel();

                MethodInfo onMouseDown = nodeType.GetMethod(
                    "OnMouseDown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (onMouseDown == null)
                {
                    return new EnterLevelNodeResult(false,
                        "Node located but OnMouseDown handler was not found on OverWorldMapEnemy (stop point: handler lookup).",
                        level, field);
                }

                try
                {
                    onMouseDown.Invoke(target, null);
                }
                catch (Exception ex)
                {
                    return new EnterLevelNodeResult(false,
                        "OnMouseDown threw for level " + level + " field " + field + ": " + ex.Message,
                        level, field);
                }

                string afterScene = RuntimeIntermissionCensus.SafeActiveSceneName();
                int afterLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
                if (HasTransitioned(beforeScene, beforeLevel, afterScene, afterLevel))
                {
                    return new EnterLevelNodeResult(true,
                        "OnMouseDown entered level " + level + " node field " + field
                        + " (scene '" + beforeScene + "' -> '" + afterScene
                        + "', level " + beforeLevel + " -> " + afterLevel + ").",
                        level, field);
                }

                // Native fallback: still in Intermission after the immediate call.
                Type loadSceneType = RuntimeIntermissionCensus.FindGameType("LoadScene");
                object loadSceneInstance = loadSceneType == null
                    ? null
                    : RuntimeIntermissionCensus.FindInstanceOf(loadSceneType);
                if (loadSceneInstance == null)
                {
                    return new EnterLevelNodeResult(false,
                        "OnMouseDown invoked but the scene stayed '" + afterScene + "'"
                        + " and the LoadScene instance is not available (stop point: fallback lookup).",
                        level, field);
                }

                MethodInfo startCoroutine = loadSceneInstance.GetType().GetMethod(
                    "StartCoroutine", new[] { typeof(string) });
                if (startCoroutine == null)
                {
                    return new EnterLevelNodeResult(false,
                        "OnMouseDown invoked but the scene stayed '" + afterScene + "'"
                        + " and MonoBehaviour.StartCoroutine(String) is not available (stop point: fallback lookup).",
                        level, field);
                }

                try
                {
                    startCoroutine.Invoke(loadSceneInstance, new object[] { "LoadNextLevel" });
                }
                catch (Exception ex)
                {
                    return new EnterLevelNodeResult(false,
                        "OnMouseDown invoked but the LoadNextLevel fallback threw: " + ex.Message,
                        level, field);
                }

                afterScene = RuntimeIntermissionCensus.SafeActiveSceneName();
                afterLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
                if (HasTransitioned(beforeScene, beforeLevel, afterScene, afterLevel))
                {
                    return new EnterLevelNodeResult(true,
                        "Level " + level + " node field " + field
                        + " started via the LoadScene.LoadNextLevel fallback"
                        + " (scene '" + beforeScene + "' -> '" + afterScene
                        + "', level " + beforeLevel + " -> " + afterLevel + ").",
                        level, field);
                }

                return new EnterLevelNodeResult(false,
                    "OnMouseDown invoked and the LoadNextLevel fallback was started, but no scene or level transition was observed yet (scene stays '" + afterScene + "').",
                    level, field);
            }
            catch (Exception ex)
            {
                return new EnterLevelNodeResult(false,
                    "Enter-level node failed: " + ex.Message, level, field);
            }
        }

        private static bool HasTransitioned(
            string beforeScene, int beforeLevel, string afterScene, int afterLevel)
        {
            bool sceneChanged = !string.Equals(beforeScene, afterScene, StringComparison.Ordinal);
            return sceneChanged || afterLevel != beforeLevel;
        }
    }

    /// <summary>
    /// Navigator pause/time diagnostics. Reads the live Navigator instance via
    /// reflection (field names _isPaused/_isDebugPaused from pinned evidence)
    /// and Unity Time for timeScale/scaledTime/unscaledTime. Null-safe: when
    /// the Navigator type or instance is missing, state reports unpaused and
    /// pause requests fail closed.
    /// </summary>
    public static class RuntimeNavigatorState
    {
        public static NavigatorStateSnapshot Current()
        {
            bool isPaused = false;
            bool isDebugPaused = false;
            try
            {
                Type navigatorType = RuntimeIntermissionCensus.FindGameType("Navigator");
                object instance = navigatorType == null
                    ? null
                    : RuntimeIntermissionCensus.FindInstanceOf(navigatorType);
                if (instance != null)
                {
                    isPaused = RuntimeIntermissionCensus.ReadBoolField(instance, "_isPaused", false);
                    isDebugPaused = RuntimeIntermissionCensus.ReadBoolField(instance, "_isDebugPaused", false);
                }
            }
            catch
            {
                isPaused = false;
                isDebugPaused = false;
            }

            return new NavigatorStateSnapshot(isPaused, isDebugPaused, SafeTimeScale(), SafeTime(), SafeUnscaledTime());
        }

        public static NavigatorPauseResult TrySetPaused(bool paused)
        {
            try
            {
                Type navigatorType = RuntimeIntermissionCensus.FindGameType("Navigator");
                if (navigatorType == null)
                {
                    return new NavigatorPauseResult(false,
                        "Navigator type is not available in this runtime.", paused);
                }

                object instance = RuntimeIntermissionCensus.FindInstanceOf(navigatorType);
                string methodName = paused ? "Pause" : "Unpause";
                MethodInfo method = navigatorType.GetMethod(
                    methodName, BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (method == null)
                {
                    return new NavigatorPauseResult(false,
                        "Navigator." + methodName + " was not found.", paused);
                }

                if (method.IsStatic)
                {
                    method.Invoke(null, null);
                }
                else if (instance != null)
                {
                    method.Invoke(instance, null);
                }
                else
                {
                    return new NavigatorPauseResult(false,
                        "Navigator." + methodName + " requires a live Navigator instance, but none was found.",
                        paused);
                }

                return new NavigatorPauseResult(true,
                    "Navigator." + methodName + " invoked.", paused);
            }
            catch (Exception ex)
            {
                return new NavigatorPauseResult(false,
                    "Navigator pause command failed: " + ex.Message, paused);
            }
        }

        private static float SafeTimeScale()
        {
            try
            {
                return Time.timeScale;
            }
            catch
            {
                return 0f;
            }
        }

        private static float SafeTime()
        {
            try
            {
                return Time.time;
            }
            catch
            {
                return 0f;
            }
        }

        private static float SafeUnscaledTime()
        {
            try
            {
                return Time.unscaledTime;
            }
            catch
            {
                return 0f;
            }
        }
    }
}
