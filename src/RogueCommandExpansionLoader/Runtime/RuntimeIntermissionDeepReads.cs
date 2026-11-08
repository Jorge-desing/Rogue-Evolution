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
    /// One node in the complete intermission LevelMap graph (regular levels 0..LevelCount-1 plus the Boss node at LevelCount).
    /// </summary>
    public readonly struct IntermissionGraphNode
    {
        public IntermissionGraphNode(
            int level,
            int field,
            string rewardType,
            string blueprintPoolId,
            bool isChoosable,
            bool isVisited,
            IReadOnlyList<int> toFields)
        {
            Level = level;
            Field = field;
            RewardType = rewardType ?? "Combat";
            BlueprintPoolId = blueprintPoolId ?? string.Empty;
            IsChoosable = isChoosable;
            IsVisited = isVisited;
            ToFields = toFields ?? Array.Empty<int>();
        }

        public int Level { get; }
        public int Field { get; }
        public string RewardType { get; }
        public string BlueprintPoolId { get; }
        public bool IsChoosable { get; }
        public bool IsVisited { get; }
        public IReadOnlyList<int> ToFields { get; }
    }

    /// <summary>
    /// One forward path from the current choosable frontier to the stage Boss node.
    /// </summary>
    public readonly struct IntermissionRouteOption
    {
        public IntermissionRouteOption(
            int rank,
            int score,
            int matchedPreferredCount,
            int totalRewardNodeCount,
            IReadOnlyList<string> steps,
            IReadOnlyList<string> rewardTypes)
        {
            Rank = rank;
            Score = score;
            MatchedPreferredCount = matchedPreferredCount;
            TotalRewardNodeCount = totalRewardNodeCount;
            Steps = steps ?? Array.Empty<string>();
            RewardTypes = rewardTypes ?? Array.Empty<string>();
        }

        public int Rank { get; }
        public int Score { get; }
        public int MatchedPreferredCount { get; }
        public int TotalRewardNodeCount { get; }
        public IReadOnlyList<string> Steps { get; }
        public IReadOnlyList<string> RewardTypes { get; }
    }

    /// <summary>
    /// Bounded snapshot of the full LevelMap graph and scored forward routes to the Boss node.
    /// Omission counters precede detail collections by contract.
    /// </summary>
    public sealed class IntermissionGraphSnapshot
    {
        public IntermissionGraphSnapshot(
            bool isAvailable,
            int omittedNodeCount,
            int omittedRouteCount,
            int currentLevel,
            int currentField,
            int levelCount,
            bool isLastLevelBeforeBoss,
            string preferredRewardType,
            int nodeCount,
            int routeCount,
            IReadOnlyList<IntermissionGraphNode> nodes,
            IReadOnlyList<IntermissionRouteOption> routes)
        {
            IsAvailable = isAvailable;
            OmittedNodeCount = omittedNodeCount;
            OmittedRouteCount = omittedRouteCount;
            CurrentLevel = currentLevel;
            CurrentField = currentField;
            LevelCount = levelCount;
            IsLastLevelBeforeBoss = isLastLevelBeforeBoss;
            PreferredRewardType = preferredRewardType ?? string.Empty;
            NodeCount = nodeCount;
            RouteCount = routeCount;
            Nodes = nodes ?? Array.Empty<IntermissionGraphNode>();
            Routes = routes ?? Array.Empty<IntermissionRouteOption>();
        }

        public bool IsAvailable { get; }
        public int OmittedNodeCount { get; }
        public int OmittedRouteCount { get; }
        public int CurrentLevel { get; }
        public int CurrentField { get; }
        public int LevelCount { get; }
        public bool IsLastLevelBeforeBoss { get; }
        public string PreferredRewardType { get; }
        public int NodeCount { get; }
        public int RouteCount { get; }
        public IReadOnlyList<IntermissionGraphNode> Nodes { get; }
        public IReadOnlyList<IntermissionRouteOption> Routes { get; }

        public static IntermissionGraphSnapshot Unavailable { get; } =
            new IntermissionGraphSnapshot(
                false, 0, 0, -1, -1, 0, false, string.Empty, 0, 0,
                Array.Empty<IntermissionGraphNode>(),
                Array.Empty<IntermissionRouteOption>());
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

        public static string ClassifyPrimaryRewardType(
            bool isBoss,
            bool isRestSite,
            bool isShop,
            bool hasCardReward,
            bool hasUpgradeReward,
            bool hasRelicReward,
            bool hasResearchReward,
            bool hasCoinReward,
            bool hasStartCrystalReward,
            bool hasResearchPointsReward,
            bool hasDropReward)
        {
            if (isBoss) return "Boss";
            if (isRestSite) return "RestSite";
            if (isShop) return "Shop";
            if (hasCardReward) return "CardReward";
            if (hasUpgradeReward) return "UpgradeReward";
            if (hasRelicReward) return "RelicReward";
            if (hasResearchReward) return "ResearchReward";
            if (hasDropReward) return "DropReward";
            if (hasCoinReward) return "CoinReward";
            if (hasStartCrystalReward) return "StartCrystalReward";
            if (hasResearchPointsReward) return "ResearchPointsReward";
            return "Combat";
        }

        public static IntermissionGraphSnapshot BuildIntermissionGraph(
            int currentLevel,
            int currentField,
            int levelCount,
            IReadOnlyList<IntermissionGraphNode> allNodes,
            string preferredRewardType = "",
            int maxRoutes = 8,
            int maxNodes = 64)
        {
            if (levelCount <= 0 || allNodes == null || allNodes.Count == 0)
            {
                return IntermissionGraphSnapshot.Unavailable;
            }

            string preferred = (preferredRewardType ?? string.Empty).Trim();
            int clampedMaxNodes = maxNodes <= 0 ? 64 : maxNodes;
            int clampedMaxRoutes = maxRoutes <= 0 ? 8 : maxRoutes;
            bool isLastBeforeBoss = currentLevel == levelCount - 1;

            var sortedNodes = new List<IntermissionGraphNode>(allNodes);
            sortedNodes.Sort((a, b) =>
            {
                int byLevel = a.Level.CompareTo(b.Level);
                return byLevel != 0 ? byLevel : a.Field.CompareTo(b.Field);
            });

            var nodeMap = new Dictionary<(int Level, int Field), IntermissionGraphNode>();
            for (int i = 0; i < sortedNodes.Count; i++)
            {
                IntermissionGraphNode node = sortedNodes[i];
                nodeMap[(node.Level, node.Field)] = node;
            }

            var rawPaths = new List<List<IntermissionGraphNode>>();
            var currentPath = new List<IntermissionGraphNode>();
            const int maxRawPaths = 256;

            for (int i = 0; i < sortedNodes.Count; i++)
            {
                IntermissionGraphNode startNode = sortedNodes[i];
                if (!startNode.IsChoosable)
                {
                    continue;
                }

                currentPath.Clear();
                currentPath.Add(startNode);
                ExpandForwardPaths(startNode, levelCount, nodeMap, currentPath, rawPaths, maxRawPaths);
            }

            var scoredRoutes = new List<IntermissionRouteOption>();
            for (int i = 0; i < rawPaths.Count; i++)
            {
                List<IntermissionGraphNode> path = rawPaths[i];
                int matched = 0;
                int totalRewards = 0;
                var steps = new List<string>(path.Count);
                var rewards = new List<string>(path.Count);
                for (int j = 0; j < path.Count; j++)
                {
                    IntermissionGraphNode step = path[j];
                    steps.Add(step.Level.ToString(CultureInfo.InvariantCulture)
                        + ":" + step.Field.ToString(CultureInfo.InvariantCulture));
                    rewards.Add(step.RewardType);
                    bool isNonBossReward = !string.Equals(step.RewardType, "Boss", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(step.RewardType, "Combat", StringComparison.OrdinalIgnoreCase);
                    if (isNonBossReward)
                    {
                        totalRewards++;
                    }

                    if (!string.IsNullOrEmpty(preferred)
                        && (string.Equals(step.RewardType, preferred, StringComparison.OrdinalIgnoreCase)
                            || (!string.IsNullOrEmpty(step.BlueprintPoolId)
                                && string.Equals(step.BlueprintPoolId, preferred, StringComparison.OrdinalIgnoreCase))))
                    {
                        matched++;
                    }
                }

                int score = matched * 100 + totalRewards * 10;
                scoredRoutes.Add(new IntermissionRouteOption(
                    0, score, matched, totalRewards, steps.AsReadOnly(), rewards.AsReadOnly()));
            }

            scoredRoutes.Sort((a, b) =>
            {
                int byScore = b.Score.CompareTo(a.Score);
                if (byScore != 0)
                {
                    return byScore;
                }

                string aSteps = string.Join(",", a.Steps);
                string bSteps = string.Join(",", b.Steps);
                return string.CompareOrdinal(aSteps, bSteps);
            });

            int keptRouteCount = scoredRoutes.Count <= clampedMaxRoutes ? scoredRoutes.Count : clampedMaxRoutes;
            int omittedRoutes = scoredRoutes.Count - keptRouteCount;
            var keptRoutes = new List<IntermissionRouteOption>(keptRouteCount);
            for (int i = 0; i < keptRouteCount; i++)
            {
                IntermissionRouteOption r = scoredRoutes[i];
                keptRoutes.Add(new IntermissionRouteOption(
                    i + 1, r.Score, r.MatchedPreferredCount, r.TotalRewardNodeCount, r.Steps, r.RewardTypes));
            }

            int keptNodeCount = sortedNodes.Count <= clampedMaxNodes ? sortedNodes.Count : clampedMaxNodes;
            int omittedNodes = sortedNodes.Count - keptNodeCount;
            var keptNodes = sortedNodes.GetRange(0, keptNodeCount).AsReadOnly();

            return new IntermissionGraphSnapshot(
                true,
                omittedNodes,
                omittedRoutes,
                currentLevel,
                currentField,
                levelCount,
                isLastBeforeBoss,
                preferred,
                keptNodeCount,
                keptRouteCount,
                keptNodes,
                keptRoutes.AsReadOnly());
        }

        private static void ExpandForwardPaths(
            IntermissionGraphNode current,
            int bossLevel,
            Dictionary<(int Level, int Field), IntermissionGraphNode> nodeMap,
            List<IntermissionGraphNode> currentPath,
            List<List<IntermissionGraphNode>> results,
            int maxPaths)
        {
            if (results.Count >= maxPaths)
            {
                return;
            }

            if (current.Level >= bossLevel || current.ToFields == null || current.ToFields.Count == 0)
            {
                results.Add(new List<IntermissionGraphNode>(currentPath));
                return;
            }

            int nextLevel = current.Level + 1;
            for (int i = 0; i < current.ToFields.Count; i++)
            {
                if (results.Count >= maxPaths)
                {
                    return;
                }

                int nextField = current.ToFields[i];
                IntermissionGraphNode nextNode;
                if (!nodeMap.TryGetValue((nextLevel, nextField), out nextNode))
                {
                    continue;
                }

                currentPath.Add(nextNode);
                ExpandForwardPaths(nextNode, bossLevel, nodeMap, currentPath, results, maxPaths);
                currentPath.RemoveAt(currentPath.Count - 1);
            }
        }

        public static IntermissionGraphSnapshot CurrentGraph(
            string preferredRewardType = "", int maxRoutes = 8, int maxNodes = 64)
        {
            try
            {
                object levelMap = ResolveCurrentLevelMap();
                if (levelMap == null)
                {
                    return IntermissionGraphSnapshot.Unavailable;
                }

                Type type = levelMap.GetType();
                int currentLevel = ReadIntProperty(type, levelMap, "CurrentLevel", -1);
                int currentField = ReadIntProperty(type, levelMap, "CurrentField", -1);
                bool lastBeforeBoss = ReadBoolProperty(type, levelMap, "IsLastLevelBeforeBoss", false);
                bool isBoss = ReadBoolProperty(type, levelMap, "IsBoss", false);
                int levelCount = ReadIntField(type, levelMap, "LevelCount", 0);
                if (levelCount <= 0)
                {
                    return IntermissionGraphSnapshot.Unavailable;
                }

                List<int> chosenPath = ReadIntList(type, levelMap, "ChosenPath");
                var nodes = new List<IntermissionGraphNode>();
                for (int level = 0; level < levelCount; level++)
                {
                    List<int> fields = ReadFields(levelMap, level);
                    List<(int, int)> nextConnections = level + 1 < levelCount
                        ? ReadConnections(levelMap, level + 1)
                        : new List<(int, int)>();
                    for (int f = 0; f < fields.Count; f++)
                    {
                        int field = fields[f];
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
                        string rewardType = ClassifyPrimaryRewardType(
                            false, rest, shop, card, upgrade, relic, research, coin, crystal, points, drop);

                        var toFields = new List<int>();
                        if (level == levelCount - 1)
                        {
                            toFields.Add(0);
                        }
                        else
                        {
                            for (int c = 0; c < nextConnections.Count; c++)
                            {
                                if (nextConnections[c].Item1 == field && !toFields.Contains(nextConnections[c].Item2))
                                {
                                    toFields.Add(nextConnections[c].Item2);
                                }
                            }

                            toFields.Sort();
                        }

                        bool isVisited = level < chosenPath.Count && chosenPath[level] == field;
                        nodes.Add(new IntermissionGraphNode(
                            level, field, rewardType, poolId, isChoosable, isVisited, toFields.AsReadOnly()));
                    }
                }

                nodes.Add(new IntermissionGraphNode(
                    levelCount, 0, "Boss", string.Empty, lastBeforeBoss, isBoss, Array.Empty<int>()));

                return BuildIntermissionGraph(
                    currentLevel, currentField, levelCount, nodes, preferredRewardType, maxRoutes, maxNodes);
            }
            catch
            {
                return IntermissionGraphSnapshot.Unavailable;
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

        private static List<int> ReadIntList(Type type, object instance, string name)
        {
            var result = new List<int>();
            try
            {
                FieldInfo field = type.GetField(name, InstanceMembers);
                object value = field != null ? field.GetValue(instance) : null;
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
