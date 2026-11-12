using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.SceneManagement;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry of match progression, ascension/heat scaling,
    /// active relic mutations, and offered blueprint reward pools.
    /// Decoupled from Unity types to support clean-room MCP telemetry and offline validation.
    /// </summary>
    public sealed class RunProgressionCensus
    {
        /// <summary>
        /// Loss/omission counters placed first to ensure bounded diagnostic trace bounds are respected.
        /// </summary>
        public int OmittedRelicCount { get; }
        public int OmittedBlueprintCount { get; }

        public string ActiveSceneName { get; }
        public int CurrentLevel { get; }
        public int CurrentStage { get; }
        public bool IsLastLevel { get; }
        public bool IsFirstLevel { get; }
        public int AscensionLevel { get; }
        public int HeatLevel { get; }
        public string AscensionAndHeatString { get; }
        public string ProgressionMode { get; }
        public int TotalRelicCount { get; }
        public int TotalBlueprintRewardCount { get; }
        public int CardsInDeckCount { get; }
        public IReadOnlyList<string> ActiveRelicIds { get; }
        public IReadOnlyList<string> OfferedBlueprintPoolIds { get; }

        public RunProgressionCensus(
            int omittedRelicCount,
            int omittedBlueprintCount,
            string activeSceneName,
            int currentLevel,
            int currentStage,
            bool isLastLevel,
            bool isFirstLevel,
            int ascensionLevel,
            int heatLevel,
            string ascensionAndHeatString,
            string progressionMode,
            int totalRelicCount,
            int totalBlueprintRewardCount,
            int cardsInDeckCount,
            IReadOnlyList<string> activeRelicIds,
            IReadOnlyList<string> offeredBlueprintPoolIds)
        {
            OmittedRelicCount = omittedRelicCount;
            OmittedBlueprintCount = omittedBlueprintCount;
            ActiveSceneName = activeSceneName ?? string.Empty;
            CurrentLevel = currentLevel;
            CurrentStage = currentStage;
            IsLastLevel = isLastLevel;
            IsFirstLevel = isFirstLevel;
            AscensionLevel = ascensionLevel;
            HeatLevel = heatLevel;
            AscensionAndHeatString = ascensionAndHeatString ?? string.Empty;
            ProgressionMode = progressionMode ?? string.Empty;
            TotalRelicCount = totalRelicCount;
            TotalBlueprintRewardCount = totalBlueprintRewardCount;
            CardsInDeckCount = cardsInDeckCount;
            ActiveRelicIds = activeRelicIds ?? new List<string>().AsReadOnly();
            OfferedBlueprintPoolIds = offeredBlueprintPoolIds ?? new List<string>().AsReadOnly();
        }

        public static RunProgressionCensus Empty { get; } =
            new RunProgressionCensus(0, 0, string.Empty, 0, 0, false, false, 0, 0, string.Empty, string.Empty, 0, 0, 0,
                new List<string>().AsReadOnly(), new List<string>().AsReadOnly());
    }

    /// <summary>
    /// Diagnostic collector that inspects run progression, active relics, offered rewards,
    /// and scene state across game and test stubs without throwing.
    /// </summary>
    public static class RuntimeRunProgressionCensus
    {
        public const int MaxTrackedRelics = 32;
        public const int MaxTrackedBlueprints = 32;

        private static readonly BindingFlags StaticOrInstance =
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static RunProgressionCensus Current()
        {
            try
            {
                string sceneName = string.Empty;
                try
                {
                    sceneName = SceneManager.GetActiveScene().name ?? string.Empty;
                }
                catch
                {
                    // Degrade gracefully
                }

                int currentLevel = 0;
                bool isLastLevel = false;
                bool isFirstLevel = false;
                try
                {
                    currentLevel = Game.CurrentLevel;
                    isLastLevel = Game.IsLastLevel;
                    isFirstLevel = Game.IsFirstLevelOrGameHasBeenResetted;
                }
                catch
                {
                    // Degrade gracefully
                }

                int currentStage = 0;
                try
                {
                    FieldInfo stageMapField = typeof(Game).GetField("StageMap", StaticOrInstance);
                    object stageMap = stageMapField != null ? stageMapField.GetValue(null) : null;
                    if (stageMap == null)
                    {
                        PropertyInfo stageMapProp = typeof(Game).GetProperty("StageMap", StaticOrInstance);
                        if (stageMapProp != null)
                        {
                            stageMap = stageMapProp.GetValue(null, null);
                        }
                    }

                    if (stageMap != null)
                    {
                        PropertyInfo currentStageProp = stageMap.GetType().GetProperty("CurrentStage", StaticOrInstance);
                        if (currentStageProp != null)
                        {
                            object val = currentStageProp.GetValue(stageMap, null);
                            if (val is int stageInt)
                            {
                                currentStage = stageInt;
                            }
                        }
                        else
                        {
                            FieldInfo currentStageField = stageMap.GetType().GetField("CurrentStage", StaticOrInstance)
                                ?? stageMap.GetType().GetField("<CurrentStage>k__BackingField", StaticOrInstance);
                            if (currentStageField != null)
                            {
                                object val = currentStageField.GetValue(stageMap);
                                if (val is int stageInt)
                                {
                                    currentStage = stageInt;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Degrade gracefully
                }

                string progressionMode = "Default";
                try
                {
                    PropertyInfo modeProp = typeof(Game).GetProperty("CurrentLevelProgressionMode", StaticOrInstance);
                    if (modeProp != null)
                    {
                        object val = modeProp.GetValue(null, null);
                        if (val != null)
                        {
                            progressionMode = val.ToString();
                        }
                    }
                    else
                    {
                        FieldInfo modeField = typeof(Game).GetField("CurrentLevelProgressionMode", StaticOrInstance)
                            ?? typeof(Game).GetField("<CurrentLevelProgressionMode>k__BackingField", StaticOrInstance);
                        if (modeField != null)
                        {
                            object val = modeField.GetValue(null);
                            if (val != null)
                            {
                                progressionMode = val.ToString();
                            }
                        }
                    }
                }
                catch
                {
                    // Degrade gracefully
                }

                int ascensionLevel = 0;
                int heatLevel = 0;
                string ascensionAndHeatString = string.Empty;
                try
                {
                    MetaGame meta = MetaGame.Instance;
                    if (meta != null)
                    {
                        ascensionLevel = meta.CurrentAscensionLevel;
                        heatLevel = meta.CurrentHeat;
                        ascensionAndHeatString = meta.CurrentAscensionAndHeatLevelString ?? string.Empty;
                    }
                }
                catch
                {
                    // Degrade gracefully
                }

                var rawRelics = new List<string>();
                int totalRelicCount = 0;
                try
                {
                    List<string> relics = Game.Relics;
                    if (relics != null)
                    {
                        totalRelicCount = relics.Count;
                        int limit = Math.Min(totalRelicCount, MaxTrackedRelics);
                        for (int i = 0; i < limit; i++)
                        {
                            if (!string.IsNullOrEmpty(relics[i]))
                            {
                                rawRelics.Add(relics[i]);
                            }
                        }
                    }
                }
                catch
                {
                    // Degrade gracefully
                }
                int omittedRelicCount = Math.Max(0, totalRelicCount - rawRelics.Count);

                var rawBlueprints = new List<string>();
                int totalBlueprintCount = 0;
                try
                {
                    List<OpenReward> openRewards = Game.OpenCardRewards;
                    if (openRewards != null)
                    {
                        totalBlueprintCount = openRewards.Count;
                        int limit = Math.Min(totalBlueprintCount, MaxTrackedBlueprints);
                        for (int i = 0; i < limit; i++)
                        {
                            OpenReward reward = openRewards[i];
                            if (!string.IsNullOrEmpty(reward.blueprintPoolId))
                            {
                                rawBlueprints.Add(reward.blueprintPoolId);
                            }
                        }
                    }
                }
                catch
                {
                    // Degrade gracefully
                }
                int omittedBlueprintCount = Math.Max(0, totalBlueprintCount - rawBlueprints.Count);

                int deckCount = 0;
                try
                {
                    if (Game.CardsInDeck != null)
                    {
                        deckCount = Game.CardsInDeck.Count;
                    }
                }
                catch
                {
                    // Degrade gracefully
                }

                return new RunProgressionCensus(
                    omittedRelicCount,
                    omittedBlueprintCount,
                    sceneName,
                    currentLevel,
                    currentStage,
                    isLastLevel,
                    isFirstLevel,
                    ascensionLevel,
                    heatLevel,
                    ascensionAndHeatString,
                    progressionMode,
                    totalRelicCount,
                    totalBlueprintCount,
                    deckCount,
                    rawRelics.AsReadOnly(),
                    rawBlueprints.AsReadOnly());
            }
            catch
            {
                return RunProgressionCensus.Empty;
            }
        }
    }
}
