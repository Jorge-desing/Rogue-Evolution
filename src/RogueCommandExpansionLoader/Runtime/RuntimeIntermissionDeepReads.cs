using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure snapshot of the live <c>LevelMap</c> route state behind the
    /// intermission (level-select) map. Reads only the reflected
    /// <c>Game.StageMap.CurrentLevelMap</c> getter and its public
    /// properties/methods (pinned decompiled API, no mutation, no new
    /// subscriptions). All values degrade to neutral defaults when the
    /// game type or instance is absent.
    /// </summary>
    public readonly struct IntermissionRouteState
    {
        public IntermissionRouteState(
            bool isRouteAvailable,
            int currentLevel,
            int currentField,
            bool isCurrentLevelFinished,
            bool isFirstLevel,
            bool isLastLevelBeforeBoss,
            bool isBoss,
            int levelCount,
            int maxWidth,
            int chosenPathCount,
            int firstPossibleNextField,
            int choosableFieldCount,
            int totalFieldCount,
            string bossAiPrefabName,
            string bossWorldPrefabName,
            string bossMapName)
        {
            IsRouteAvailable = isRouteAvailable;
            CurrentLevel = currentLevel;
            CurrentField = currentField;
            IsCurrentLevelFinished = isCurrentLevelFinished;
            IsFirstLevel = isFirstLevel;
            IsLastLevelBeforeBoss = isLastLevelBeforeBoss;
            IsBoss = isBoss;
            LevelCount = levelCount;
            MaxWidth = maxWidth;
            ChosenPathCount = chosenPathCount;
            FirstPossibleNextField = firstPossibleNextField;
            ChoosableFieldCount = choosableFieldCount;
            TotalFieldCount = totalFieldCount;
            BossAiPrefabName = bossAiPrefabName ?? string.Empty;
            BossWorldPrefabName = bossWorldPrefabName ?? string.Empty;
            BossMapName = bossMapName ?? string.Empty;
        }

        public bool IsRouteAvailable { get; }
        public int CurrentLevel { get; }
        public int CurrentField { get; }
        public bool IsCurrentLevelFinished { get; }
        public bool IsFirstLevel { get; }
        public bool IsLastLevelBeforeBoss { get; }
        public bool IsBoss { get; }
        public int LevelCount { get; }
        public int MaxWidth { get; }
        public int ChosenPathCount { get; }
        public int FirstPossibleNextField { get; }
        public int ChoosableFieldCount { get; }
        public int TotalFieldCount { get; }
        public string BossAiPrefabName { get; }
        public string BossWorldPrefabName { get; }
        public string BossMapName { get; }

        public static IntermissionRouteState Unavailable { get; } =
            new IntermissionRouteState(false, -1, -1, false, false, false, false, 0, 0, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty);
    }

    /// <summary>
    /// Pure, agent-readable detail for a single intermission node
    /// (level, field). Reward classification comes from the native
    /// <c>LevelMap.Is*Reward(level, field)</c> predicates; connectivity
    /// counts come from <c>LevelMap.Connections(level)</c>.
    /// </summary>
    public readonly struct IntermissionNodeDetail
    {
        public IntermissionNodeDetail(
            bool found,
            int level,
            int field,
            bool isChoosable,
            bool isRestSite,
            bool isShop,
            bool hasCardReward,
            bool hasUpgradeReward,
            bool hasRelicReward,
            bool hasResearchReward,
            bool hasCoinReward,
            bool hasStartCrystalReward,
            bool hasResearchPointsReward,
            bool hasDropReward,
            string blueprintPoolId,
            int outgoingConnectionCount,
            int incomingConnectionCount,
            int fieldCountInLevel)
        {
            Found = found;
            Level = level;
            Field = field;
            IsChoosable = isChoosable;
            IsRestSite = isRestSite;
            IsShop = isShop;
            HasCardReward = hasCardReward;
            HasUpgradeReward = hasUpgradeReward;
            HasRelicReward = hasRelicReward;
            HasResearchReward = hasResearchReward;
            HasCoinReward = hasCoinReward;
            HasStartCrystalReward = hasStartCrystalReward;
            HasResearchPointsReward = hasResearchPointsReward;
            HasDropReward = hasDropReward;
            BlueprintPoolId = blueprintPoolId ?? string.Empty;
            OutgoingConnectionCount = outgoingConnectionCount;
            IncomingConnectionCount = incomingConnectionCount;
            FieldCountInLevel = fieldCountInLevel;
        }

        public bool Found { get; }
        public int Level { get; }
        public int Field { get; }
        public bool IsChoosable { get; }
        public bool IsRestSite { get; }
        public bool IsShop { get; }
        public bool HasCardReward { get; }
        public bool HasUpgradeReward { get; }
        public bool HasRelicReward { get; }
        public bool HasResearchReward { get; }
        public bool HasCoinReward { get; }
        public bool HasStartCrystalReward { get; }
        public bool HasResearchPointsReward { get; }
        public bool HasDropReward { get; }
        public string BlueprintPoolId { get; }
        public int OutgoingConnectionCount { get; }
        public int IncomingConnectionCount { get; }
        public int FieldCountInLevel { get; }

        public static IntermissionNodeDetail NotFound(int level, int field) =>
            new IntermissionNodeDetail(false, level, field, false, false, false, false, false, false,
                false, false, false, false, false, string.Empty, 0, 0, 0);
    }

    /// <summary>
    /// Read-only deep reads over the live <c>LevelMap</c> for the intermission
    /// map. Reflection-only over the pinned getter
    /// <c>Game.StageMap.CurrentLevelMap</c> (from pinned decompiled
    /// <c>LevelProgression.cs</c>) and the public <c>LevelMap</c> API
    /// (from pinned decompiled <c>LevelMap.cs</c>). Never mutates state.
    /// </summary>
    public static class RuntimeIntermissionDeepReads
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// Testable pure choosable-field computation mirroring the native
        /// <c>LevelMap.IsChoosable(level, field)</c> rule:
        /// <list type="bullet">
        /// <item><c>CurrentLevel == -1</c>: every field of level 0 is choosable.</item>
        /// <item>Otherwise only <c>level == CurrentLevel + 1</c> is choosable, and a
        /// field is choosable iff <c>(CurrentField, field)</c> appears in the
        /// connections of that level.</item>
        /// </list>
        /// </summary>
        public static int CountChoosableFields(
            int currentLevel, int level, IReadOnlyList<int> fieldsInLevel,
            int currentField, IReadOnlyList<(int X, int Y)> connectionsInLevel)
        {
            if (fieldsInLevel == null || fieldsInLevel.Count == 0)
            {
                return 0;
            }

            int count = 0;
            foreach (int field in fieldsInLevel)
            {
                if (IsChoosable(currentLevel, level, field, currentField, connectionsInLevel))
                {
                    count++;
                }
            }

            return count;
        }

        internal static bool IsChoosable(
            int currentLevel, int level, int field, int currentField,
            IReadOnlyList<(int X, int Y)> connectionsInLevel)
        {
            if (currentLevel == -1 && level == 0)
            {
                return true;
            }

            if (level != currentLevel + 1)
            {
                return false;
            }

            if (connectionsInLevel == null)
            {
                return false;
            }

            foreach ((int x, int y) connection in connectionsInLevel)
            {
                if (connection.x == currentField && connection.y == field)
                {
                    return true;
                }
            }

            return false;
        }

        public static IntermissionRouteState CurrentRouteState()
        {
            try
            {
                object levelMap = ResolveCurrentLevelMap();
                if (levelMap == null)
                {
                    return IntermissionRouteState.Unavailable;
                }

                Type type = levelMap.GetType();
                int currentLevel = ReadIntProperty(type, levelMap, "CurrentLevel", 0);
                int currentField = ReadIntProperty(type, levelMap, "CurrentField", 0);
                bool finished = ReadBoolProperty(type, levelMap, "IsCurrentLevelFinished", false);
                bool first = ReadBoolProperty(type, levelMap, "IsFirstLevel", false);
                bool lastBeforeBoss = ReadBoolProperty(type, levelMap, "IsLastLevelBeforeBoss", false);
                bool boss = ReadBoolProperty(type, levelMap, "IsBoss", false);
                int levelCount = ReadIntField(type, levelMap, "LevelCount", 0);
                int maxWidth = ReadIntField(type, levelMap, "MaxWidth", 0);
                int firstPossibleNext = ReadIntProperty(type, levelMap, "FirstPossibleNextField", 0);
                int totalFields = ReadIntProperty(type, levelMap, "OverallFieldCount", 0);
                int chosenPathCount = ReadIntListCount(type, levelMap, "ChosenPath");
                string bossAi = ReadStringField(type, levelMap, "BossAiPrefabName");
                string bossWorld = ReadStringField(type, levelMap, "BossWorldPrefabName");
                string bossMap = ReadStringField(type, levelMap, "BossMapScriptableObjectName");

                int nextLevel = currentLevel + 1;
                List<int> fieldsNext = ReadFields(levelMap, nextLevel);
                List<(int, int)> connections = ReadConnections(levelMap, nextLevel);
                int choosable = CountChoosableFields(currentLevel, nextLevel, fieldsNext, currentField, connections);

                return new IntermissionRouteState(
                    true, currentLevel, currentField, finished, first, lastBeforeBoss, boss,
                    levelCount, maxWidth, chosenPathCount, firstPossibleNext, choosable, totalFields,
                    bossAi, bossWorld, bossMap);
            }
            catch
            {
                return IntermissionRouteState.Unavailable;
            }
        }

        public static IntermissionNodeDetail InspectNode(int level, int field)
        {
            try
            {
                object levelMap = ResolveCurrentLevelMap();
                if (levelMap == null)
                {
                    return IntermissionNodeDetail.NotFound(level, field);
                }

                Type type = levelMap.GetType();
                if (!HasField(type, levelMap, level))
                {
                    return IntermissionNodeDetail.NotFound(level, field);
                }

                bool isChoosable = InvokeBoolMethod(type, levelMap, "IsChoosable", level, field, false);
                bool rest = InvokeBoolMethod(type, levelMap, "IsRestSite", level, field, false);
                bool shop = InvokeBoolMethod(type, levelMap, "IsShop", level, field, false);
                bool card = InvokeBoolMethod(type, levelMap, "IsCardReward", level, field, false);
                bool upgrade = InvokeBoolMethod(type, levelMap, "IsUpgradeReward", level, field, false);
                bool relic = InvokeBoolMethod(type, levelMap, "IsRelicReward", level, field, false);
                bool research = InvokeBoolMethod(type, levelMap, "IsResearchReward", level, field, false);
                bool coin = InvokeBoolMethod(type, levelMap, "IsCoinReward", level, field, false);
                bool crystal = InvokeBoolMethod(type, levelMap, "IsStartCrystalReward", level, field, false);
                bool points = InvokeBoolMethod(type, levelMap, "IsResearchPointsReward", level, field, false);
                bool drop = InvokeBoolMethod(type, levelMap, "IsDropReward", level, field, false);
                string poolId = InvokeStringMethod(type, levelMap, "BlueprintPoolId", level, field);

                List<(int, int)> outgoing = ReadConnections(levelMap, level + 1).FindAll(c => c.Item1 == field);
                List<(int, int)> incoming = ReadConnections(levelMap, level).FindAll(c => c.Item2 == field);
                int fieldCountInLevel = ReadFields(levelMap, level).Count;

                return new IntermissionNodeDetail(
                    true, level, field, isChoosable, rest, shop, card, upgrade, relic, research, coin,
                    crystal, points, drop, poolId, outgoing.Count, incoming.Count, fieldCountInLevel);
            }
            catch
            {
                return IntermissionNodeDetail.NotFound(level, field);
            }
        }

        private static bool HasField(Type type, object instance, int level)
        {
            try
            {
                object result = InvokeOneArg(type, instance, "Fields", level);
                return result is System.Collections.ICollection;
            }
            catch
            {
                return false;
            }
        }

        internal static object ResolveCurrentLevelMap()
        {
            try
            {
                Type gameType = RuntimeIntermissionCensus.FindGameType("Game");
                if (gameType == null)
                {
                    return null;
                }

                PropertyInfo stageMapProp = gameType.GetProperty(
                    "StageMap", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object stageMap = stageMapProp != null ? stageMapProp.GetValue(null, null) : null;
                if (stageMap == null)
                {
                    FieldInfo stageMapField = gameType.GetField(
                        "StageMap", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    stageMap = stageMapField != null ? stageMapField.GetValue(null) : null;
                }

                if (stageMap == null)
                {
                    return null;
                }

                PropertyInfo currentLevelMapProp = stageMap.GetType().GetProperty(
                    "CurrentLevelMap", InstanceMembers);
                if (currentLevelMapProp != null)
                {
                    return currentLevelMapProp.GetValue(stageMap, null);
                }

                FieldInfo currentLevelMapField = stageMap.GetType().GetField(
                    "CurrentLevelMap", InstanceMembers)
                    ?? stageMap.GetType().GetField(
                        "<CurrentLevelMap>k__BackingField", InstanceMembers);
                return currentLevelMapField != null ? currentLevelMapField.GetValue(stageMap) : null;
            }
            catch
            {
                return null;
            }
        }

        private static int ReadIntProperty(Type type, object instance, string name, int fallback)
        {
            try
            {
                PropertyInfo property = type.GetProperty(name, InstanceMembers);
                if (property != null && property.CanRead && property.PropertyType == typeof(int))
                {
                    return (int)property.GetValue(instance, null);
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static bool ReadBoolProperty(Type type, object instance, string name, bool fallback)
        {
            try
            {
                PropertyInfo property = type.GetProperty(name, InstanceMembers);
                if (property != null && property.CanRead && property.PropertyType == typeof(bool))
                {
                    return (bool)property.GetValue(instance, null);
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static int ReadIntField(Type type, object instance, string name, int fallback)
        {
            try
            {
                FieldInfo field = type.GetField(name, InstanceMembers);
                if (field != null && field.FieldType == typeof(int))
                {
                    return (int)field.GetValue(instance);
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static string ReadStringField(Type type, object instance, string name)
        {
            try
            {
                FieldInfo field = type.GetField(name, InstanceMembers);
                if (field != null && field.FieldType == typeof(string))
                {
                    return field.GetValue(instance) as string ?? string.Empty;
                }

                PropertyInfo property = type.GetProperty(name, InstanceMembers);
                if (property != null && property.CanRead && property.PropertyType == typeof(string))
                {
                    return property.GetValue(instance, null) as string ?? string.Empty;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static int ReadIntListCount(Type type, object instance, string name)
        {
            try
            {
                FieldInfo field = type.GetField(name, InstanceMembers);
                object value = field != null ? field.GetValue(instance) : null;
                if (value is System.Collections.ICollection collection)
                {
                    return collection.Count;
                }
            }
            catch
            {
            }

            return 0;
        }

        private static object InvokeOneArg(Type type, object instance, string name, int arg)
        {
            MethodInfo method = type.GetMethod(name, InstanceMembers, null, new[] { typeof(int) }, null);
            return method != null ? method.Invoke(instance, new object[] { arg }) : null;
        }

        private static bool InvokeBoolMethod(Type type, object instance, string name, int level, int field, bool fallback)
        {
            try
            {
                MethodInfo method = type.GetMethod(
                    name, InstanceMembers, null, new[] { typeof(int), typeof(int) }, null);
                if (method != null && method.ReturnType == typeof(bool))
                {
                    return (bool)method.Invoke(instance, new object[] { level, field });
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static string InvokeStringMethod(Type type, object instance, string name, int level, int field)
        {
            try
            {
                MethodInfo method = type.GetMethod(
                    name, InstanceMembers, null, new[] { typeof(int), typeof(int) }, null);
                if (method != null && method.ReturnType == typeof(string))
                {
                    return method.Invoke(instance, new object[] { level, field }) as string ?? string.Empty;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static List<int> ReadFields(object levelMap, int level)
        {
            var result = new List<int>();
            try
            {
                object value = InvokeOneArg(levelMap.GetType(), levelMap, "Fields", level);
                if (value is System.Collections.IEnumerable enumerable)
                {
                    foreach (object item in enumerable)
                    {
                        if (item is int intValue)
                        {
                            result.Add(intValue);
                        }
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static List<(int, int)> ReadConnections(object levelMap, int level)
        {
            var result = new List<(int, int)>();
            try
            {
                object value = InvokeOneArg(levelMap.GetType(), levelMap, "Connections", level);
                if (value is System.Collections.IEnumerable enumerable)
                {
                    foreach (object item in enumerable)
                    {
                        // Connection entries are Vector2Int; recover via its x/y fields.
                        if (item == null)
                        {
                            continue;
                        }

                        Type itemType = item.GetType();
                        int x = RuntimeIntermissionCensus.ReadIntField(item, "x", int.MinValue);
                        int y = RuntimeIntermissionCensus.ReadIntField(item, "y", int.MinValue);
                        if (x != int.MinValue && y != int.MinValue)
                        {
                            result.Add((x, y));
                        }
                    }
                }
            }
            catch
            {
            }

            return result;
        }
    }
}
