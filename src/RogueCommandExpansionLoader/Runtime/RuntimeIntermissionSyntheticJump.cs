using System;
using System.Collections.Generic;
using System.Reflection;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure diagnostic result of a user-authorized synthetic stage jump
    /// (StageMap.JumpTo). The Synthetic flag is always true: the call advanced
    /// game state by synthetic means, so the result can never be read as
    /// natural map progression evidence.
    /// </summary>
    public readonly struct SyntheticJumpResult
    {
        public SyntheticJumpResult(
            bool success,
            string method,
            int level,
            bool finished,
            int beforeStage,
            int beforeLevel,
            int afterStage,
            int afterLevel,
            string message)
        {
            Success = success;
            Synthetic = true;
            Method = method ?? string.Empty;
            Level = level;
            Finished = finished;
            BeforeStage = beforeStage;
            BeforeLevel = beforeLevel;
            AfterStage = afterStage;
            AfterLevel = afterLevel;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public bool Synthetic { get; }
        public string Method { get; }
        public int Level { get; }
        public bool Finished { get; }
        public int BeforeStage { get; }
        public int BeforeLevel { get; }
        public int AfterStage { get; }
        public int AfterLevel { get; }
        public string Message { get; }
    }

    /// <summary>
    /// User-authorized synthetic stage advancement for runtime-validation
    /// automation (explicit project-rule exception: synthetic state calls are
    /// allowed under the isolated-QA-profile + checkpoint discipline; results
    /// are labeled Synthetic and never prove natural progression).
    /// Uses reflection for the game-only StageMap type so it compiles without
    /// the game assembly and fails closed when the method is absent. Never
    /// touches profile/save files. One-shot by design: it performs no probe
    /// attachment and leaves no components behind.
    /// </summary>
    public static class RuntimeIntermissionSyntheticJump
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public const int MinLevel = 0;
        public const int MaxLevel = 100;

        /// <summary>
        /// Invokes StageMap.JumpTo(level[, finished]) via reflection. Overload
        /// candidates are attempted in a fixed order; the first that resolves
        /// is reported in <see cref="SyntheticJumpResult.Method"/>. Success
        /// means the method was invoked without throwing; scene transition is
        /// asynchronous and is NOT claimed here (verify with a follow-up
        /// intermission census).
        /// </summary>
        public static SyntheticJumpResult TryJumpToLevel(int level, bool finished)
        {
            if (level < MinLevel || level > MaxLevel)
            {
                return new SyntheticJumpResult(
                    false, string.Empty, level, finished, 0, 0, 0, 0,
                    "stop point: argument. level must be between "
                    + MinLevel + " and " + MaxLevel + ".");
            }

            object stageMap = FindStageMap();
            if (stageMap == null)
            {
                return new SyntheticJumpResult(
                    false, string.Empty, level, finished, 0, 0, 0, 0,
                    "stop point: StageMap not found in the active scene.");
            }

            int beforeStage = RuntimeIntermissionCensus.SafeCurrentStage();
            int beforeLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
            Type mapType = stageMap.GetType();

            string[] attempted = new string[0];
            MethodInfo method = null;
            object[] callArgs = null;
            string resolved = string.Empty;

            var candidates = new List<Tuple<string, Type[], object[]>>
            {
                new Tuple<string, Type[], object[]>(
                    "JumpTo(bool,int)",
                    new[] { typeof(bool), typeof(int) },
                    new object[] { finished, level }),
            };

            // Try the bool overload first (most informative: finished+level),
            // then the bare level overload when the map only exposes that one.
            var fallback = new Tuple<string, Type[], object[]>(
                "JumpTo(int)",
                new[] { typeof(int) },
                new object[] { level });

            foreach (Tuple<string, Type[], object[]> c in candidates)
            {
                attempted = new[] { c.Item1 };
                method = mapType.GetMethod(
                    "JumpTo", InstanceMembers, null, c.Item2, null);
                callArgs = c.Item3;
                if (method != null)
                {
                    resolved = c.Item1;
                    break;
                }
            }

            if (method == null)
            {
                method = mapType.GetMethod(
                    "JumpTo", InstanceMembers, null, fallback.Item2, null);
                callArgs = fallback.Item3;
                attempted = new[] { attempted.Length > 0 ? attempted[0] : string.Empty, fallback.Item1 };
                if (method != null)
                {
                    resolved = fallback.Item1;
                }
            }

            if (method == null)
            {
                return new SyntheticJumpResult(
                    false, string.Empty, level, finished, beforeStage, beforeLevel,
                    beforeStage, beforeLevel,
                    "stop point: method resolution. No JumpTo overload matched "
                    + string.Join(", ", attempted) + " on StageMap.");
            }

            try
            {
                method.Invoke(stageMap, callArgs);
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException
                    : ex;
                return new SyntheticJumpResult(
                    false, resolved, level, finished, beforeStage, beforeLevel,
                    beforeStage, beforeLevel,
                    "stop point: invocation. " + resolved + " threw: " + inner.Message);
            }

            int afterStage = RuntimeIntermissionCensus.SafeCurrentStage();
            int afterLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
            return new SyntheticJumpResult(
                true, resolved, level, finished, beforeStage, beforeLevel,
                afterStage, afterLevel,
                "synthetic: invoked StageMap." + resolved + " (level=" + level
                + ", finished=" + finished + "). Scene transition is asynchronous; "
                + "verify with diagnostics_get_intermission_census.");
        }

        /// <summary>
        /// Starts the native LoadScene.LoadNextLevel corridor on the live
        /// LoadScene instance (the same coroutine the intermission director
        /// starts after a node selection). Fail-closed when the corridor is
        /// unavailable. The scene transition is asynchronous and is NOT
        /// claimed here (verify with a follow-up intermission census).
        /// </summary>
        public static SyntheticJumpResult TryStartNextLevelLoad()
        {
            int beforeStage = RuntimeIntermissionCensus.SafeCurrentStage();
            int beforeLevel = RuntimeIntermissionCensus.SafeCurrentLevel();

            Type loadSceneType = RuntimeIntermissionCensus.FindGameType("LoadScene");
            object loadSceneInstance = loadSceneType == null
                ? null
                : RuntimeIntermissionCensus.FindInstanceOf(loadSceneType);
            if (loadSceneInstance == null)
            {
                return new SyntheticJumpResult(
                    false, string.Empty, beforeLevel, false, beforeStage, beforeLevel,
                    beforeStage, beforeLevel,
                    "stop point: LoadScene not found in the active scene.");
            }

            MethodInfo startCoroutine = loadSceneInstance.GetType().GetMethod(
                "StartCoroutine", new[] { typeof(string) });
            if (startCoroutine == null)
            {
                return new SyntheticJumpResult(
                    false, string.Empty, beforeLevel, false, beforeStage, beforeLevel,
                    beforeStage, beforeLevel,
                    "stop point: MonoBehaviour.StartCoroutine(String) is not available.");
            }

            try
            {
                startCoroutine.Invoke(loadSceneInstance, new object[] { "LoadNextLevel" });
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException
                    : ex;
                return new SyntheticJumpResult(
                    false, "LoadScene.LoadNextLevel", beforeLevel, false,
                    beforeStage, beforeLevel, beforeStage, beforeLevel,
                    "stop point: invocation. LoadNextLevel corridor threw: " + inner.Message);
            }

            int afterStage = RuntimeIntermissionCensus.SafeCurrentStage();
            int afterLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
            return new SyntheticJumpResult(
                true, "LoadScene.LoadNextLevel", beforeLevel, false,
                beforeStage, beforeLevel, afterStage, afterLevel,
                "synthetic: invoked LoadScene.StartCoroutine(\"LoadNextLevel\"). "
                + "Scene transition is asynchronous; verify with "
                + "diagnostics_get_intermission_census and diagnostics_get_system_health.");
        }

        internal static object FindStageMap()
        {
            // Primary path (same as RuntimeIntermissionCensus.SafeCurrentStage):
            // the plugin assembly references Assembly-CSharp at load time, so
            // `typeof(Game)` resolves the real game type in-game and the stub
            // compiler type in the offline harness.
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
                if (stageMap != null)
                {
                    return stageMap;
                }
            }
            catch
            {
            }

            // Fallback: type-scan the live scene objects for the StageMap type.
            try
            {
                Type mapType = typeof(Game).Assembly.GetType("StageMap", false);
                if (mapType != null)
                {
                    object instance = RuntimeIntermissionCensus.FindInstanceOf(mapType);
                    if (instance != null)
                    {
                        return instance;
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
