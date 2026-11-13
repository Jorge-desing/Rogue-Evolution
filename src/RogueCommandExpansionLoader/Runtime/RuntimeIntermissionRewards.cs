using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// One offered reward card currently instantiated in <c>ChooseCard</c>.
    /// </summary>
    public readonly struct IntermissionOfferedCardRow
    {
        public IntermissionOfferedCardRow(
            int slotIndex,
            string cardKind,
            string id,
            string productId,
            string name,
            int instanceId)
        {
            SlotIndex = slotIndex;
            CardKind = cardKind ?? string.Empty;
            Id = id ?? string.Empty;
            ProductId = productId ?? string.Empty;
            Name = name ?? string.Empty;
            InstanceId = instanceId;
        }

        public int SlotIndex { get; }
        public string CardKind { get; }
        public string Id { get; }
        public string ProductId { get; }
        public string Name { get; }
        public int InstanceId { get; }
    }

    /// <summary>
    /// One card in the player's current deck (<c>Game.CardsInDeck</c>) with its attached upgrades
    /// (<c>Game.CardUpgrades</c>) and viability state when <c>AssignUpgradeToCardView</c> is open.
    /// </summary>
    public readonly struct IntermissionDeckCardRow
    {
        public IntermissionDeckCardRow(
            string cardId,
            string productId,
            int upgradeCount,
            IReadOnlyList<string> upgrades,
            bool isViableForCurrentUpgrade)
        {
            CardId = cardId ?? string.Empty;
            ProductId = productId ?? string.Empty;
            UpgradeCount = upgradeCount;
            Upgrades = upgrades ?? Array.Empty<string>();
            IsViableForCurrentUpgrade = isViableForCurrentUpgrade;
        }

        public string CardId { get; }
        public string ProductId { get; }
        public int UpgradeCount { get; }
        public IReadOnlyList<string> Upgrades { get; }
        public bool IsViableForCurrentUpgrade { get; }
    }

    /// <summary>
    /// Pure, agent-readable snapshot of the intermission sub-screens (<c>ChooseCard</c>,
    /// <c>DeckOverview</c>, <c>AssignUpgradeToCardView</c>, <c>RemoveCardView</c>),
    /// currencies, open reward queues, offered reward slots, and player deck cards.
    /// Omission counters precede detail collections by contract.
    /// </summary>
    public sealed class IntermissionRewardState
    {
        public IntermissionRewardState(
            bool isAvailable,
            int omittedOfferedCardCount,
            int omittedDeckCardCount,
            string activeSceneName,
            string navigatorSceneState,
            bool playerHasOpenRewards,
            int currentCoins,
            int currentStartCrystals,
            int currentDropPodCount,
            int currentRerollCount,
            int currentResearchPoints,
            int maxResearchPoints,
            int openEngineerRewardCount,
            int openEconomyRewardCount,
            int openSpecialistRewardCount,
            int openSpecialistRelicRewardCount,
            int openCardRewardCount,
            int openRelicRewardCount,
            int openUpgradeRewardCount,
            int openResearchRewardCount,
            bool isChooseCardActive,
            string activeRewardCategory,
            bool isClickableNow,
            bool canSkip,
            bool canReroll,
            bool isDeckOverviewOpen,
            bool isAssignUpgradeActive,
            string currentUpgradeId,
            bool isRemoveCardActive,
            int removeCardCoinCompensation,
            int offeredCardCount,
            int deckCardCount,
            IReadOnlyList<IntermissionOfferedCardRow> offeredCards,
            IReadOnlyList<IntermissionDeckCardRow> deckCards)
        {
            IsAvailable = isAvailable;
            OmittedOfferedCardCount = omittedOfferedCardCount;
            OmittedDeckCardCount = omittedDeckCardCount;
            ActiveSceneName = activeSceneName ?? string.Empty;
            NavigatorSceneState = navigatorSceneState ?? string.Empty;
            PlayerHasOpenRewards = playerHasOpenRewards;
            CurrentCoins = currentCoins;
            CurrentStartCrystals = currentStartCrystals;
            CurrentDropPodCount = currentDropPodCount;
            CurrentRerollCount = currentRerollCount;
            CurrentResearchPoints = currentResearchPoints;
            MaxResearchPoints = maxResearchPoints;
            OpenEngineerRewardCount = openEngineerRewardCount;
            OpenEconomyRewardCount = openEconomyRewardCount;
            OpenSpecialistRewardCount = openSpecialistRewardCount;
            OpenSpecialistRelicRewardCount = openSpecialistRelicRewardCount;
            OpenCardRewardCount = openCardRewardCount;
            OpenRelicRewardCount = openRelicRewardCount;
            OpenUpgradeRewardCount = openUpgradeRewardCount;
            OpenResearchRewardCount = openResearchRewardCount;
            IsChooseCardActive = isChooseCardActive;
            ActiveRewardCategory = activeRewardCategory ?? "None";
            IsClickableNow = isClickableNow;
            CanSkip = canSkip;
            CanReroll = canReroll;
            IsDeckOverviewOpen = isDeckOverviewOpen;
            IsAssignUpgradeActive = isAssignUpgradeActive;
            CurrentUpgradeId = currentUpgradeId ?? string.Empty;
            IsRemoveCardActive = isRemoveCardActive;
            RemoveCardCoinCompensation = removeCardCoinCompensation;
            OfferedCardCount = offeredCardCount;
            DeckCardCount = deckCardCount;
            OfferedCards = offeredCards ?? Array.Empty<IntermissionOfferedCardRow>();
            DeckCards = deckCards ?? Array.Empty<IntermissionDeckCardRow>();
        }

        public bool IsAvailable { get; }
        public int OmittedOfferedCardCount { get; }
        public int OmittedDeckCardCount { get; }
        public string ActiveSceneName { get; }
        public string NavigatorSceneState { get; }
        public bool PlayerHasOpenRewards { get; }
        public int CurrentCoins { get; }
        public int CurrentStartCrystals { get; }
        public int CurrentDropPodCount { get; }
        public int CurrentRerollCount { get; }
        public int CurrentResearchPoints { get; }
        public int MaxResearchPoints { get; }
        public int OpenEngineerRewardCount { get; }
        public int OpenEconomyRewardCount { get; }
        public int OpenSpecialistRewardCount { get; }
        public int OpenSpecialistRelicRewardCount { get; }
        public int OpenCardRewardCount { get; }
        public int OpenRelicRewardCount { get; }
        public int OpenUpgradeRewardCount { get; }
        public int OpenResearchRewardCount { get; }
        public bool IsChooseCardActive { get; }
        public string ActiveRewardCategory { get; }
        public bool IsClickableNow { get; }
        public bool CanSkip { get; }
        public bool CanReroll { get; }
        public bool IsDeckOverviewOpen { get; }
        public bool IsAssignUpgradeActive { get; }
        public string CurrentUpgradeId { get; }
        public bool IsRemoveCardActive { get; }
        public int RemoveCardCoinCompensation { get; }
        public int OfferedCardCount { get; }
        public int DeckCardCount { get; }
        public IReadOnlyList<IntermissionOfferedCardRow> OfferedCards { get; }
        public IReadOnlyList<IntermissionDeckCardRow> DeckCards { get; }

        public static IntermissionRewardState Unavailable { get; } =
            new IntermissionRewardState(
                false, 0, 0, string.Empty, string.Empty, false, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0, false, "None", false, false, false,
                false, false, string.Empty, false, 0, 0, 0,
                Array.Empty<IntermissionOfferedCardRow>(),
                Array.Empty<IntermissionDeckCardRow>());

        public static IntermissionRewardState Build(
            bool isAvailable,
            string activeSceneName,
            string navigatorSceneState,
            bool playerHasOpenRewards,
            int currentCoins,
            int currentStartCrystals,
            int currentDropPodCount,
            int currentRerollCount,
            int currentResearchPoints,
            int maxResearchPoints,
            int openEngineerRewardCount,
            int openEconomyRewardCount,
            int openSpecialistRewardCount,
            int openSpecialistRelicRewardCount,
            int openCardRewardCount,
            int openRelicRewardCount,
            int openUpgradeRewardCount,
            int openResearchRewardCount,
            bool isChooseCardActive,
            string activeRewardCategory,
            bool isClickableNow,
            bool canSkip,
            bool canReroll,
            bool isDeckOverviewOpen,
            bool isAssignUpgradeActive,
            string currentUpgradeId,
            bool isRemoveCardActive,
            int removeCardCoinCompensation,
            IReadOnlyList<IntermissionOfferedCardRow> offeredCards,
            IReadOnlyList<IntermissionDeckCardRow> deckCards,
            int maxCards = 32)
        {
            int clamp = maxCards <= 0 ? 32 : maxCards;
            var offeredList = new List<IntermissionOfferedCardRow>();
            if (offeredCards != null)
            {
                offeredList.AddRange(offeredCards);
            }

            int keptOffered = offeredList.Count <= clamp ? offeredList.Count : clamp;
            int omittedOffered = offeredList.Count - keptOffered;

            var deckList = new List<IntermissionDeckCardRow>();
            if (deckCards != null)
            {
                deckList.AddRange(deckCards);
            }

            int keptDeck = deckList.Count <= clamp ? deckList.Count : clamp;
            int omittedDeck = deckList.Count - keptDeck;

            return new IntermissionRewardState(
                isAvailable,
                omittedOffered,
                omittedDeck,
                activeSceneName,
                navigatorSceneState,
                playerHasOpenRewards,
                currentCoins,
                currentStartCrystals,
                currentDropPodCount,
                currentRerollCount,
                currentResearchPoints,
                maxResearchPoints,
                openEngineerRewardCount,
                openEconomyRewardCount,
                openSpecialistRewardCount,
                openSpecialistRelicRewardCount,
                openCardRewardCount,
                openRelicRewardCount,
                openUpgradeRewardCount,
                openResearchRewardCount,
                isChooseCardActive,
                activeRewardCategory,
                isClickableNow,
                canSkip,
                canReroll,
                isDeckOverviewOpen,
                isAssignUpgradeActive,
                currentUpgradeId,
                isRemoveCardActive,
                removeCardCoinCompensation,
                keptOffered,
                keptDeck,
                offeredList.GetRange(0, keptOffered).AsReadOnly(),
                deckList.GetRange(0, keptDeck).AsReadOnly());
        }
    }

    /// <summary>
    /// Pure diagnostic result of an intermission sub-screen or LoadLevel action.
    /// </summary>
    public readonly struct IntermissionActionResult
    {
        public IntermissionActionResult(
            bool success,
            string action,
            string targetId,
            int slotIndex,
            string navigatorSceneState,
            bool playerHasOpenRewards,
            string message,
            bool nativeCallInvoked = false,
            bool effectObserved = false)
        {
            Success = success;
            Action = action ?? string.Empty;
            TargetId = targetId ?? string.Empty;
            SlotIndex = slotIndex;
            NavigatorSceneState = navigatorSceneState ?? string.Empty;
            PlayerHasOpenRewards = playerHasOpenRewards;
            Message = message ?? string.Empty;
            NativeCallInvoked = nativeCallInvoked;
            EffectObserved = effectObserved;
        }

        public bool Success { get; }
        public string Action { get; }
        public string TargetId { get; }
        public int SlotIndex { get; }
        public string NavigatorSceneState { get; }
        public bool PlayerHasOpenRewards { get; }
        public string Message { get; }
        public bool NativeCallInvoked { get; }
        public bool EffectObserved { get; }
    }

    /// <summary>
    /// Runtime telemetry and fail-closed actions for the Intermission sub-screens:
    /// <c>ChooseCard</c>, <c>DeckOverview</c>, <c>AssignUpgradeToCardView</c>,
    /// <c>RemoveCardView</c>, and <c>LoadScene.LoadLevel()</c>.
    /// Reflection-only over pinned game types so it compiles in the offline harness.
    /// </summary>
    public static class RuntimeIntermissionRewards
    {
        private const BindingFlags StaticMembers =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static string ResolveActiveRewardCategory(
            bool firstRelicsThenCards,
            int openEngineer,
            int openEconomy,
            int openSpecialist,
            int openSpecialistRelic,
            int openCard,
            int openRelic,
            int openUpgrade,
            int openResearch)
        {
            if (openEngineer > 0) return "Engineer";
            if (openEconomy > 0) return "Economy";
            if (openSpecialist > 0) return "Specialist";
            if (openSpecialistRelic > 0) return "SpecialistRelic";
            if ((firstRelicsThenCards || openCard < 1) && openRelic > 0) return "Relic";
            if (openCard > 0) return "Card";
            if (openUpgrade > 0) return "Upgrade";
            if (openResearch > 0) return "Research";
            return "None";
        }

        public static IntermissionRewardState GetState(int maxCards = 32)
        {
            try
            {
                Type gameType = RuntimeIntermissionCensus.FindGameType("Game");
                if (gameType == null)
                {
                    return IntermissionRewardState.Unavailable;
                }

                string sceneName = RuntimeIntermissionCensus.SafeActiveSceneName();
                string navState = ReadNavigatorSceneState();
                bool hasOpenRewards = ReadStaticBool(gameType, "PlayerHasOpenRewards", false);
                int coins = ReadStaticInt(gameType, "CurrentCoins", 0);
                int crystals = ReadStaticInt(gameType, "CurrentStartCrystals", 0);
                int dropPods = ReadStaticInt(gameType, "CurrentDropPodCount", 0);
                int rerolls = ReadStaticInt(gameType, "CurrentRerollCount", 0);
                int researchPts = ReadStaticInt(gameType, "CurrentResearchPoints", 0);
                int maxResearchPts = ReadStaticInt(gameType, "MaxResearchPoints", 0);

                int openEng = ReadStaticCollectionCount(gameType, "OpenEngineerRewards");
                int openEcon = ReadStaticCollectionCount(gameType, "OpenEconomyRewards");
                int openSpec = ReadStaticCollectionCount(gameType, "OpenSpecialistRewards");
                int openSpecRelic = ReadStaticCollectionCount(gameType, "OpenSpecialistRelicRewards");
                int openCard = ReadStaticCollectionCount(gameType, "OpenCardRewards");
                int openRelic = ReadStaticCollectionCount(gameType, "OpenRelicRewards");
                int openUpg = ReadStaticCollectionCount(gameType, "OpenUpgradeRewards");
                int openRes = ReadStaticCollectionCount(gameType, "OpenResearchRewards");

                object chooseCard = ResolveSingleton("ChooseCard");
                bool firstRelics = chooseCard != null && RuntimeIntermissionCensus.ReadBoolField(chooseCard, "firstRelicsThenCards", false);
                string activeCategory = ResolveActiveRewardCategory(
                    firstRelics, openEng, openEcon, openSpec, openSpecRelic, openCard, openRelic, openUpg, openRes);

                bool isChooseCardActive = false;
                bool isClickableNow = false;
                bool canSkip = false;
                bool canReroll = false;
                var offeredCards = new List<IntermissionOfferedCardRow>();

                if (chooseCard != null)
                {
                    GameObject chooseGo = (chooseCard as Component)?.gameObject;
                    isChooseCardActive = (chooseGo != null && chooseGo.activeInHierarchy)
                        && (hasOpenRewards || string.Equals(navState, "RewardScreen", StringComparison.OrdinalIgnoreCase));
                    float displayStart = ReadFloatField(chooseCard, "_displayStartTime", 0f);
                    float clickableAfter = ReadFloatField(chooseCard, "clickableAfterSeconds", 0.5f);
                    float now = SafeRealtimeSinceStartup();
                    isClickableNow = isChooseCardActive && (now - displayStart >= clickableAfter);

                    GameObject skipGo = RuntimeIntermissionCensus.ReadObjectField(chooseCard, "skipButtonGameObject") as GameObject;
                    canSkip = isChooseCardActive && skipGo != null && skipGo.activeInHierarchy;

                    Component rerollComp = RuntimeIntermissionCensus.ReadObjectField(chooseCard, "rerollButton") as Component;
                    bool rerollGoActive = rerollComp != null && rerollComp.gameObject != null && rerollComp.gameObject.activeInHierarchy;
                    canReroll = isChooseCardActive && rerollGoActive && (rerolls > 0 || ReadRerollsAreEndless());

                    offeredCards = ReadOfferedCards(chooseCard);
                    if (offeredCards.Count > 0 && !isChooseCardActive && hasOpenRewards)
                    {
                        isChooseCardActive = true;
                        isClickableNow = now - displayStart >= clickableAfter;
                    }
                }

                bool isDeckOpen = ReadDeckOverviewOpen();
                object assignView = ResolveSingleton("AssignUpgradeToCardView");
                bool isAssignActive = assignView != null && RuntimeIntermissionCensus.ReadBoolField(assignView, "_isShowing", false);
                string currentUpgradeId = isAssignActive
                    ? ReadStringField(assignView, "_currentUpgradeId")
                    : string.Empty;

                object removeView = ResolveSingleton("RemoveCardView");
                bool isRemoveActive = removeView != null && RuntimeIntermissionCensus.ReadBoolField(removeView, "_isShowing", false);
                int removeCoins = removeView != null
                    ? RuntimeIntermissionCensus.ReadIntField(removeView, "coinAmountToEarn", 100)
                    : 0;

                List<IntermissionDeckCardRow> deckCards = ReadDeckCards(gameType, assignView, isAssignActive);

                bool isAvailable = chooseCard != null || assignView != null || removeView != null
                    || (!string.IsNullOrEmpty(sceneName) && sceneName.IndexOf("Intermission", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!isAvailable)
                {
                    return IntermissionRewardState.Unavailable;
                }

                return IntermissionRewardState.Build(
                    true,
                    sceneName,
                    navState,
                    hasOpenRewards,
                    coins,
                    crystals,
                    dropPods,
                    rerolls,
                    researchPts,
                    maxResearchPts,
                    openEng,
                    openEcon,
                    openSpec,
                    openSpecRelic,
                    openCard,
                    openRelic,
                    openUpg,
                    openRes,
                    isChooseCardActive,
                    activeCategory,
                    isClickableNow,
                    canSkip,
                    canReroll,
                    isDeckOpen,
                    isAssignActive,
                    currentUpgradeId,
                    isRemoveActive,
                    removeCoins,
                    offeredCards,
                    deckCards,
                    maxCards);
            }
            catch
            {
                return IntermissionRewardState.Unavailable;
            }
        }

        /// <summary>
        /// Triggers the native <c>LoadScene.LoadLevel()</c> flow when a node or boss is selected on <c>CurrentLevelMap</c>.
        /// Reports whether an instant reward resolved in-place, a reward sub-screen opened, or a scene load started.
        /// </summary>
        public static IntermissionActionResult TryTriggerLoadLevel()
        {
            bool nativeCallInvoked = false;
            try
            {
                object levelMap = RuntimeIntermissionDeepReads.ResolveCurrentLevelMap();
                if (levelMap == null)
                {
                    return new IntermissionActionResult(
                        false, "TriggerLoadLevel", string.Empty, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "CurrentLevelMap is not available (stop point: levelMap lookup).");
                }

                Type lmType = levelMap.GetType();
                int chosenField = RuntimeIntermissionCensus.ReadIntField(levelMap, "ChosenField", -1);
                bool isLastBeforeBoss = RuntimeIntermissionCensus.ReadBoolProperty(levelMap, "IsLastLevelBeforeBoss", false);
                if (chosenField < 0 && isLastBeforeBoss)
                {
                    FieldInfo cfField = lmType.GetField("ChosenField", InstanceMembers);
                    if (cfField != null && cfField.FieldType == typeof(int))
                    {
                        cfField.SetValue(levelMap, 0);
                        chosenField = 0;
                    }
                }

                if (chosenField < 0)
                {
                    return new IntermissionActionResult(
                        false, "TriggerLoadLevel", string.Empty, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "LevelMap.ChosenField is -1; select a choosable node first (stop point: precondition).");
                }

                Type loadSceneType = RuntimeIntermissionCensus.FindGameType("LoadScene");
                object loadScene = loadSceneType != null ? RuntimeIntermissionCensus.FindInstanceOf(loadSceneType) : null;
                if (loadScene == null)
                {
                    return new IntermissionActionResult(
                        false, "TriggerLoadLevel", string.Empty, chosenField,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "LoadScene instance is not available in the active scene (stop point: LoadScene lookup).");
                }

                bool alreadyLoading = RuntimeIntermissionCensus.ReadBoolField(loadScene, "_nextLevelIsLoading", false);
                if (alreadyLoading)
                {
                    return new IntermissionActionResult(
                        false, "TriggerLoadLevel", string.Empty, chosenField,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "LoadScene._nextLevelIsLoading is already true (stop point: busy guard).");
                }

                int beforeLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
                string beforeScene = RuntimeIntermissionCensus.SafeActiveSceneName();

                MethodInfo loadLevelMethod = loadSceneType.GetMethod(
                    "LoadLevel", InstanceMembers, null, Type.EmptyTypes, null);
                if (loadLevelMethod == null)
                {
                    return new IntermissionActionResult(
                        false, "TriggerLoadLevel", string.Empty, chosenField,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "LoadScene.LoadLevel() method was not found (stop point: method lookup).");
                }

                nativeCallInvoked = true;
                loadLevelMethod.Invoke(loadScene, null);

                int afterLevel = RuntimeIntermissionCensus.SafeCurrentLevel();
                string afterScene = RuntimeIntermissionCensus.SafeActiveSceneName();
                string navState = ReadNavigatorSceneState();
                bool hasOpenRewards = ReadStaticPlayerHasOpenRewards();
                bool nextLoading = RuntimeIntermissionCensus.ReadBoolField(loadScene, "_nextLevelIsLoading", false);

                string targetDesc = afterLevel + ":" + chosenField;
                if (hasOpenRewards || string.Equals(navState, "RewardScreen", StringComparison.OrdinalIgnoreCase))
                {
                    return new IntermissionActionResult(
                        true, "TriggerLoadLevel", targetDesc, chosenField, navState, hasOpenRewards,
                        "LoadScene.LoadLevel() opened RewardScreen for node " + targetDesc + ".", true, true);
                }

                if (afterLevel != beforeLevel && !nextLoading)
                {
                    return new IntermissionActionResult(
                        true, "TriggerLoadLevel", targetDesc, chosenField, navState, hasOpenRewards,
                        "LoadScene.LoadLevel() resolved instant reward at node " + targetDesc
                        + " (level " + beforeLevel + " -> " + afterLevel + ").", true, true);
                }

                if (!string.Equals(beforeScene, afterScene, StringComparison.Ordinal) || nextLoading)
                {
                    return new IntermissionActionResult(
                        true, "TriggerLoadLevel", targetDesc, chosenField, navState, hasOpenRewards,
                        "LoadScene.LoadLevel() initiated level/shop transition for node " + targetDesc
                        + " (scene '" + beforeScene + "' -> '" + afterScene + "', loading=" + nextLoading + ").", true, true);
                }

                return new IntermissionActionResult(
                    false, "TriggerLoadLevel", targetDesc, chosenField, navState, hasOpenRewards,
                    "LoadScene.LoadLevel() invoked but no reward, level advance, or scene load was observed.", nativeCallInvoked, false);
            }
            catch (Exception ex)
            {
                return new IntermissionActionResult(
                    false, "TriggerLoadLevel", string.Empty, -1,
                    ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                    "TriggerLoadLevel failed: " + ex.Message, nativeCallInvoked, false);
            }
        }

        /// <summary>
        /// Selects an offered reward card in <c>ChooseCard</c> by <paramref name="rewardId"/> or <paramref name="slotIndex"/>.
        /// Fails closed when <c>ChooseCard</c> is not active, not yet clickable, or no matching card is offered.
        /// </summary>
        public static IntermissionActionResult TryChooseReward(string rewardId = "", int slotIndex = -1)
        {
            string trimmedId = (rewardId ?? string.Empty).Trim();
            try
            {
                object chooseCard = ResolveSingleton("ChooseCard");
                if (chooseCard == null)
                {
                    return new IntermissionActionResult(
                        false, "ChooseReward", trimmedId, slotIndex,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "ChooseCard instance is not available in the current scene (stop point: ChooseCard lookup).");
                }

                if (!ReadStaticPlayerHasOpenRewards())
                {
                    return new IntermissionActionResult(
                        false, "ChooseReward", trimmedId, slotIndex,
                        ReadNavigatorSceneState(), false,
                        "PlayerHasOpenRewards is false; no reward is currently open to choose (stop point: open-reward gate).");
                }

                EnsureClickReady(chooseCard);
                List<(IntermissionOfferedCardRow Row, Component CardComponent)> liveCards = ReadLiveOfferedCardComponents(chooseCard);
                if (liveCards.Count == 0)
                {
                    return new IntermissionActionResult(
                        false, "ChooseReward", trimmedId, slotIndex,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "No instantiated reward cards found in ChooseCard slots (stop point: slot scan).");
                }

                (IntermissionOfferedCardRow Row, Component CardComponent) matched = default;
                bool found = false;
                for (int i = 0; i < liveCards.Count; i++)
                {
                    var entry = liveCards[i];
                    if (slotIndex >= 0 && entry.Row.SlotIndex == slotIndex)
                    {
                        if (string.IsNullOrEmpty(trimmedId)
                            || string.Equals(entry.Row.Id, trimmedId, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(entry.Row.ProductId, trimmedId, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = entry;
                            found = true;
                            break;
                        }
                    }
                    else if (slotIndex < 0 && !string.IsNullOrEmpty(trimmedId))
                    {
                        if (string.Equals(entry.Row.Id, trimmedId, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(entry.Row.ProductId, trimmedId, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = entry;
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    return new IntermissionActionResult(
                        false, "ChooseReward", trimmedId, slotIndex,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "No offered card matched rewardId='" + trimmedId + "' slotIndex=" + slotIndex + " (stop point: card match).");
                }

                MethodInfo pressMethod = matched.CardComponent.GetType().GetMethod(
                    "PressedChooseButton", InstanceMembers, null, Type.EmptyTypes, null);
                if (pressMethod != null)
                {
                    pressMethod.Invoke(matched.CardComponent, null);
                }
                else
                {
                    string clickMethodName = null;
                    if (string.Equals(matched.Row.CardKind, "RelicCard", StringComparison.OrdinalIgnoreCase))
                    {
                        clickMethodName = "ClickedOnRelicCard";
                    }
                    else if (string.Equals(matched.Row.CardKind, "ResearchCard", StringComparison.OrdinalIgnoreCase))
                    {
                        clickMethodName = "ClickedOnResearchCard";
                    }
                    else if (string.Equals(matched.Row.CardKind, "UpgradeCard", StringComparison.OrdinalIgnoreCase))
                    {
                        clickMethodName = "ClickedOnUpgradeCard";
                    }

                    if (clickMethodName == null)
                    {
                        return new IntermissionActionResult(
                            false, "ChooseReward", matched.Row.Id, matched.Row.SlotIndex,
                            ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                            "Card component '" + matched.Row.CardKind + "' has no PressedChooseButton or fallback handler.");
                    }

                    MethodInfo clickMethod = chooseCard.GetType().GetMethod(
                        clickMethodName, InstanceMembers, null, new[] { typeof(string) }, null);
                    if (clickMethod == null)
                    {
                        return new IntermissionActionResult(
                            false, "ChooseReward", matched.Row.Id, matched.Row.SlotIndex,
                            ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                            "ChooseCard." + clickMethodName + "(string) was not found.");
                    }

                    clickMethod.Invoke(chooseCard, new object[] { matched.Row.Id });
                }

                return new IntermissionActionResult(
                    true, "ChooseReward", matched.Row.Id, matched.Row.SlotIndex,
                    ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                    "Chose offered " + matched.Row.CardKind + " '" + matched.Row.Id + "' at slot " + matched.Row.SlotIndex + ".");
            }
            catch (Exception ex)
            {
                return new IntermissionActionResult(
                    false, "ChooseReward", trimmedId, slotIndex,
                    ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                    "ChooseReward failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Skips the active reward in <c>ChooseCard</c> via <c>ClickedOnSkipButton()</c> if skipping is allowed.
        /// </summary>
        public static IntermissionActionResult TrySkipReward()
        {
            try
            {
                object chooseCard = ResolveSingleton("ChooseCard");
                if (chooseCard == null)
                {
                    return new IntermissionActionResult(
                        false, "SkipReward", string.Empty, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "ChooseCard instance is not available in the current scene (stop point: ChooseCard lookup).");
                }

                if (!ReadStaticPlayerHasOpenRewards())
                {
                    return new IntermissionActionResult(
                        false, "SkipReward", string.Empty, -1,
                        ReadNavigatorSceneState(), false,
                        "PlayerHasOpenRewards is false; no reward is open to skip (stop point: open-reward gate).");
                }

                GameObject skipGo = RuntimeIntermissionCensus.ReadObjectField(chooseCard, "skipButtonGameObject") as GameObject;
                if (skipGo == null || !skipGo.activeInHierarchy)
                {
                    return new IntermissionActionResult(
                        false, "SkipReward", string.Empty, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "Skip button is inactive; current reward category is not skippable (stop point: skip gate).");
                }

                EnsureClickReady(chooseCard);
                MethodInfo skipMethod = chooseCard.GetType().GetMethod(
                    "ClickedOnSkipButton", InstanceMembers, null, Type.EmptyTypes, null);
                if (skipMethod == null)
                {
                    return new IntermissionActionResult(
                        false, "SkipReward", string.Empty, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "ChooseCard.ClickedOnSkipButton() was not found.");
                }

                skipMethod.Invoke(chooseCard, null);
                return new IntermissionActionResult(
                    true, "SkipReward", string.Empty, -1,
                    ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                    "Invoked ChooseCard.ClickedOnSkipButton().");
            }
            catch (Exception ex)
            {
                return new IntermissionActionResult(
                    false, "SkipReward", string.Empty, -1,
                    ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                    "SkipReward failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Rerolls the active reward offer in <c>ChooseCard</c> via <c>ClickedOnRerollButton()</c> when rerolls are available.
        /// </summary>
        public static IntermissionActionResult TryRerollReward()
        {
            try
            {
                object chooseCard = ResolveSingleton("ChooseCard");
                if (chooseCard == null)
                {
                    return new IntermissionActionResult(
                        false, "RerollReward", string.Empty, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "ChooseCard instance is not available in the current scene (stop point: ChooseCard lookup).");
                }

                if (!ReadStaticPlayerHasOpenRewards())
                {
                    return new IntermissionActionResult(
                        false, "RerollReward", string.Empty, -1,
                        ReadNavigatorSceneState(), false,
                        "PlayerHasOpenRewards is false; no reward is open to reroll (stop point: open-reward gate).");
                }

                Component rerollComp = RuntimeIntermissionCensus.ReadObjectField(chooseCard, "rerollButton") as Component;
                if (rerollComp == null || rerollComp.gameObject == null || !rerollComp.gameObject.activeInHierarchy)
                {
                    return new IntermissionActionResult(
                        false, "RerollReward", string.Empty, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "Reroll button is not active for the current reward (stop point: reroll button gate).");
                }

                Type gameType = RuntimeIntermissionCensus.FindGameType("Game");
                int rerolls = gameType != null ? ReadStaticInt(gameType, "CurrentRerollCount", 0) : 0;
                if (rerolls <= 0 && !ReadRerollsAreEndless())
                {
                    return new IntermissionActionResult(
                        false, "RerollReward", string.Empty, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "CurrentRerollCount is 0 and RerollsAreEndless is false (stop point: reroll count gate).");
                }

                MethodInfo rerollMethod = chooseCard.GetType().GetMethod(
                    "ClickedOnRerollButton", InstanceMembers, null, Type.EmptyTypes, null);
                if (rerollMethod == null)
                {
                    return new IntermissionActionResult(
                        false, "RerollReward", string.Empty, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "ChooseCard.ClickedOnRerollButton() was not found.");
                }

                rerollMethod.Invoke(chooseCard, null);
                return new IntermissionActionResult(
                    true, "RerollReward", string.Empty, -1,
                    ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                    "Invoked ChooseCard.ClickedOnRerollButton().");
            }
            catch (Exception ex)
            {
                return new IntermissionActionResult(
                    false, "RerollReward", string.Empty, -1,
                    ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                    "RerollReward failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Assigns the currently pending upgrade in <c>AssignUpgradeToCardView</c> to a viable deck card
        /// (<paramref name="entityId"/>), or cancels the view when <paramref name="cancel"/> is true.
        /// </summary>
        public static IntermissionActionResult TryAssignUpgradeToCard(string entityId = "", bool cancel = false)
        {
            string trimmedId = (entityId ?? string.Empty).Trim();
            try
            {
                object assignView = ResolveSingleton("AssignUpgradeToCardView");
                if (assignView == null)
                {
                    return new IntermissionActionResult(
                        false, "AssignUpgrade", trimmedId, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "AssignUpgradeToCardView instance is not available (stop point: view lookup).");
                }

                bool isShowing = RuntimeIntermissionCensus.ReadBoolField(assignView, "_isShowing", false);
                if (!isShowing)
                {
                    return new IntermissionActionResult(
                        false, "AssignUpgrade", trimmedId, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "AssignUpgradeToCardView is not currently showing (stop point: showing gate).");
                }

                if (cancel)
                {
                    MethodInfo cancelMethod = assignView.GetType().GetMethod(
                        "Cancel", InstanceMembers, null, Type.EmptyTypes, null);
                    if (cancelMethod == null)
                    {
                        return new IntermissionActionResult(
                            false, "AssignUpgrade", "cancel", -1,
                            ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                            "AssignUpgradeToCardView.Cancel() was not found.");
                    }

                    cancelMethod.Invoke(assignView, null);
                    return new IntermissionActionResult(
                        true, "AssignUpgrade", "cancel", -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "Cancelled AssignUpgradeToCardView.");
                }

                if (string.IsNullOrEmpty(trimmedId))
                {
                    return new IntermissionActionResult(
                        false, "AssignUpgrade", string.Empty, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "entityId is required when cancel is false (stop point: argument validation).");
                }

                object cardsObj = RuntimeIntermissionCensus.ReadObjectField(assignView, "_cards");
                object matchedCard = null;
                string resolvedEntityId = trimmedId;
                if (cardsObj is IEnumerable cardsEnum)
                {
                    foreach (object card in cardsEnum)
                    {
                        if (card == null)
                        {
                            continue;
                        }

                        string cardEntityId = ReadStringProperty(card, "EntityId");
                        if (string.Equals(cardEntityId, trimmedId, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedCard = card;
                            resolvedEntityId = cardEntityId;
                            break;
                        }
                    }
                }

                if (matchedCard == null)
                {
                    return new IntermissionActionResult(
                        false, "AssignUpgrade", trimmedId, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "Deck card '" + trimmedId + "' was not found in AssignUpgradeToCardView (stop point: card lookup).");
                }

                GameObject nonViableGo = RuntimeIntermissionCensus.ReadObjectField(matchedCard, "nonViableStateGameObject") as GameObject;
                if (nonViableGo != null && nonViableGo.activeSelf)
                {
                    return new IntermissionActionResult(
                        false, "AssignUpgrade", resolvedEntityId, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "Deck card '" + resolvedEntityId + "' is marked non-viable for upgrade '"
                        + ReadStringField(assignView, "_currentUpgradeId") + "' (stop point: viability gate).");
                }

                EnsureClickReady(assignView);
                MethodInfo clickCardMethod = assignView.GetType().GetMethod(
                    "ClickedOnBlueprintCard", InstanceMembers, null, new[] { typeof(string) }, null);
                if (clickCardMethod == null)
                {
                    return new IntermissionActionResult(
                        false, "AssignUpgrade", resolvedEntityId, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "AssignUpgradeToCardView.ClickedOnBlueprintCard(string) was not found.");
                }

                clickCardMethod.Invoke(assignView, new object[] { resolvedEntityId });
                return new IntermissionActionResult(
                    true, "AssignUpgrade", resolvedEntityId, -1,
                    ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                    "Assigned upgrade '" + ReadStringField(assignView, "_currentUpgradeId")
                    + "' to deck card '" + resolvedEntityId + "'.");
            }
            catch (Exception ex)
            {
                return new IntermissionActionResult(
                    false, "AssignUpgrade", trimmedId, -1,
                    ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                    "AssignUpgrade failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Opens or closes the native <c>DeckOverview</c> screen and verifies the resulting state.
        /// </summary>
        public static IntermissionActionResult TrySetDeckOverviewOpen(bool open)
        {
            string targetDesc = open ? "open" : "closed";
            try
            {
                object deckOverview = ResolveSingleton("DeckOverview");
                if (deckOverview == null)
                {
                    return new IntermissionActionResult(
                        false, "SetDeckOverviewOpen", targetDesc, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "DeckOverview instance is not available in the current scene (stop point: DeckOverview lookup).");
                }

                bool currentlyOpen = ReadDeckOverviewOpen();
                if (currentlyOpen != open)
                {
                    string methodName = open ? "Show" : "Hide";
                    MethodInfo method = deckOverview.GetType().GetMethod(
                        methodName, InstanceMembers, null, Type.EmptyTypes, null);
                    if (method == null)
                    {
                        return new IntermissionActionResult(
                            false, "SetDeckOverviewOpen", targetDesc, -1,
                            ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                            "DeckOverview." + methodName + "() was not found.");
                    }

                    method.Invoke(deckOverview, null);
                }

                bool afterOpen = ReadDeckOverviewOpen();
                if (afterOpen != open)
                {
                    return new IntermissionActionResult(
                        false, "SetDeckOverviewOpen", targetDesc, -1,
                        ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                        "DeckOverview state remained " + (afterOpen ? "open" : "closed") + " after invoking Show/Hide.");
                }

                return new IntermissionActionResult(
                    true, "SetDeckOverviewOpen", targetDesc, -1,
                    ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                    "DeckOverview is now " + targetDesc + ".");
            }
            catch (Exception ex)
            {
                return new IntermissionActionResult(
                    false, "SetDeckOverviewOpen", targetDesc, -1,
                    ReadNavigatorSceneState(), ReadStaticPlayerHasOpenRewards(),
                    "SetDeckOverviewOpen failed: " + ex.Message);
            }
        }

        // ---- Reflection helpers ----

        private static object ResolveSingleton(string typeName)
        {
            Type type = RuntimeIntermissionCensus.FindGameType(typeName);
            if (type == null)
            {
                return null;
            }

            try
            {
                FieldInfo instanceField = type.GetField("_instance", StaticMembers);
                if (instanceField != null)
                {
                    object value = instanceField.GetValue(null);
                    if (value != null && !IsUnityNull(value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            return RuntimeIntermissionCensus.FindInstanceOf(type);
        }

        private static bool IsUnityNull(object obj)
        {
            if (obj == null)
            {
                return true;
            }

            if (obj is UnityEngine.Object unityObj)
            {
                return unityObj == null;
            }

            return false;
        }

        private static void EnsureClickReady(object viewInstance)
        {
            if (viewInstance == null)
            {
                return;
            }

            try
            {
                FieldInfo displayStartField = viewInstance.GetType().GetField("_displayStartTime", InstanceMembers);
                if (displayStartField != null && displayStartField.FieldType == typeof(float))
                {
                    float startTime = (float)displayStartField.GetValue(viewInstance);
                    float clickableAfter = ReadFloatField(viewInstance, "clickableAfterSeconds", 0.5f);
                    float now = SafeRealtimeSinceStartup();
                    if (now - startTime < clickableAfter)
                    {
                        displayStartField.SetValue(viewInstance, now - clickableAfter - 0.1f);
                    }
                }
            }
            catch
            {
            }
        }

        private static float SafeRealtimeSinceStartup()
        {
            try
            {
                return Time.realtimeSinceStartup;
            }
            catch
            {
                return 0f;
            }
        }

        private static bool ReadDeckOverviewOpen()
        {
            try
            {
                Type type = RuntimeIntermissionCensus.FindGameType("DeckOverview");
                if (type != null)
                {
                    MethodInfo method = type.GetMethod("IsShowing_Static", StaticMembers, null, Type.EmptyTypes, null);
                    if (method != null && method.ReturnType == typeof(bool))
                    {
                        return (bool)method.Invoke(null, null);
                    }
                }

                object instance = ResolveSingleton("DeckOverview");
                GameObject go = (instance as Component)?.gameObject;
                return go != null && go.activeSelf;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadNavigatorSceneState()
        {
            try
            {
                Type navType = RuntimeIntermissionCensus.FindGameType("Navigator");
                if (navType == null)
                {
                    return string.Empty;
                }

                PropertyInfo prop = navType.GetProperty("CurrentSceneState", StaticMembers);
                if (prop != null && prop.CanRead)
                {
                    object val = prop.GetValue(null, null);
                    return val != null ? val.ToString() : string.Empty;
                }

                FieldInfo field = navType.GetField("CurrentSceneState", StaticMembers);
                if (field != null)
                {
                    object val = field.GetValue(null);
                    return val != null ? val.ToString() : string.Empty;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static bool ReadStaticPlayerHasOpenRewards()
        {
            Type gameType = RuntimeIntermissionCensus.FindGameType("Game");
            return gameType != null && ReadStaticBool(gameType, "PlayerHasOpenRewards", false);
        }

        private static bool ReadRerollsAreEndless()
        {
            try
            {
                Type rerollType = RuntimeIntermissionCensus.FindGameType("RerollButton");
                if (rerollType == null)
                {
                    return false;
                }

                return ReadStaticBool(rerollType, "RerollsAreEndless", false);
            }
            catch
            {
                return false;
            }
        }

        private static List<IntermissionOfferedCardRow> ReadOfferedCards(object chooseCard)
        {
            var rows = new List<IntermissionOfferedCardRow>();
            List<(IntermissionOfferedCardRow Row, Component CardComponent)> live = ReadLiveOfferedCardComponents(chooseCard);
            for (int i = 0; i < live.Count; i++)
            {
                rows.Add(live[i].Row);
            }

            return rows;
        }

        private static List<(IntermissionOfferedCardRow Row, Component CardComponent)> ReadLiveOfferedCardComponents(object chooseCard)
        {
            var results = new List<(IntermissionOfferedCardRow Row, Component CardComponent)>();
            if (chooseCard == null)
            {
                return results;
            }

            var seenInstanceIds = new HashSet<int>();
            ScanSlotList(chooseCard, "cardSlotTransforms", results, seenInstanceIds);
            ScanSlotList(chooseCard, "cardSlotTransforms4Slots", results, seenInstanceIds);
            return results;
        }

        private static void ScanSlotList(
            object chooseCard,
            string slotListFieldName,
            List<(IntermissionOfferedCardRow Row, Component CardComponent)> results,
            HashSet<int> seenInstanceIds)
        {
            try
            {
                object listObj = RuntimeIntermissionCensus.ReadObjectField(chooseCard, slotListFieldName);
                if (!(listObj is IEnumerable enumerable))
                {
                    return;
                }

                int slotIdx = 0;
                foreach (object item in enumerable)
                {
                    int currentSlot = slotIdx++;
                    Transform slotTransform = item as Transform;
                    if (slotTransform == null || slotTransform.childCount <= 0)
                    {
                        continue;
                    }

                    for (int c = 0; c < slotTransform.childCount; c++)
                    {
                        Transform child = slotTransform.GetChild(c);
                        if (child == null || child.gameObject == null || !child.gameObject.activeInHierarchy)
                        {
                            continue;
                        }

                        if (TryExtractOfferedCard(child.gameObject, currentSlot, out IntermissionOfferedCardRow row, out Component cardComp))
                        {
                            if (seenInstanceIds.Add(row.InstanceId))
                            {
                                results.Add((row, cardComp));
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool TryExtractOfferedCard(
            GameObject cardGo,
            int slotIndex,
            out IntermissionOfferedCardRow row,
            out Component matchedComponent)
        {
            row = default;
            matchedComponent = null;
            if (cardGo == null)
            {
                return false;
            }

            try
            {
                Component[] components = cardGo.GetComponents<Component>();
                if (components == null)
                {
                    return false;
                }

                for (int i = 0; i < components.Length; i++)
                {
                    Component comp = components[i];
                    if (comp == null)
                    {
                        continue;
                    }

                    string typeName = comp.GetType().Name;
                    if (string.Equals(typeName, "CardNew", StringComparison.Ordinal))
                    {
                        string entityId = ReadStringProperty(comp, "EntityId");
                        string refineryId = ReadStringField(comp, "_refineryId");
                        string primaryId = !string.IsNullOrEmpty(refineryId) ? refineryId : entityId;
                        string productId = !string.IsNullOrEmpty(refineryId) ? entityId : string.Empty;
                        string displayName = ReadUiText(comp, "nameText");
                        int instanceId = RuntimeIntermissionCensus.ReadInstanceId(comp);
                        row = new IntermissionOfferedCardRow(slotIndex, "CardNew", primaryId, productId, displayName, instanceId);
                        matchedComponent = comp;
                        return true;
                    }

                    if (string.Equals(typeName, "UpgradeCard", StringComparison.Ordinal))
                    {
                        string upgradeId = ReadStringProperty(comp, "UpgradeId");
                        string displayName = ReadUiText(comp, "nameText");
                        int instanceId = RuntimeIntermissionCensus.ReadInstanceId(comp);
                        row = new IntermissionOfferedCardRow(slotIndex, "UpgradeCard", upgradeId, string.Empty, displayName, instanceId);
                        matchedComponent = comp;
                        return true;
                    }

                    if (string.Equals(typeName, "RelicCard", StringComparison.Ordinal))
                    {
                        string relicId = ReadCardIdentifier(comp, "RelicId", "_relicId", "relicId");
                        string displayName = ReadUiText(comp, "nameText");
                        int instanceId = RuntimeIntermissionCensus.ReadInstanceId(comp);
                        row = new IntermissionOfferedCardRow(slotIndex, "RelicCard", relicId, string.Empty, displayName, instanceId);
                        matchedComponent = comp;
                        return true;
                    }

                    if (string.Equals(typeName, "ResearchCard", StringComparison.Ordinal))
                    {
                        string researchId = ReadCardIdentifier(comp, "ResearchId", "_researchId", "researchId");
                        string displayName = ReadUiText(comp, "nameText");
                        int instanceId = RuntimeIntermissionCensus.ReadInstanceId(comp);
                        row = new IntermissionOfferedCardRow(slotIndex, "ResearchCard", researchId, string.Empty, displayName, instanceId);
                        matchedComponent = comp;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static List<IntermissionDeckCardRow> ReadDeckCards(
            Type gameType, object assignView, bool isAssignActive)
        {
            var rows = new List<IntermissionDeckCardRow>();
            if (gameType == null)
            {
                return rows;
            }

            try
            {
                var viabilityByProductId = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                if (isAssignActive && assignView != null)
                {
                    object cardsObj = RuntimeIntermissionCensus.ReadObjectField(assignView, "_cards");
                    if (cardsObj is IEnumerable assignCards)
                    {
                        foreach (object card in assignCards)
                        {
                            if (card == null)
                            {
                                continue;
                            }

                            string entityId = ReadStringProperty(card, "EntityId");
                            if (string.IsNullOrEmpty(entityId))
                            {
                                continue;
                            }

                            GameObject nonViableGo = RuntimeIntermissionCensus.ReadObjectField(card, "nonViableStateGameObject") as GameObject;
                            bool viable = nonViableGo == null || !nonViableGo.activeSelf;
                            viabilityByProductId[entityId] = viable;
                        }
                    }
                }

                Dictionary<string, List<string>> upgradesByEntity = ReadCardUpgradesMap(gameType);
                object deckObj = ReadStaticObject(gameType, "CardsInDeck");
                if (deckObj is IEnumerable deckEnum)
                {
                    foreach (object item in deckEnum)
                    {
                        if (!(item is string cardId) || string.IsNullOrEmpty(cardId))
                        {
                            continue;
                        }

                        string productId = ResolveProductEntityId(cardId);
                        string lookupKey = !string.IsNullOrEmpty(productId) ? productId : cardId;
                        var cardUpgrades = new List<string>();
                        if (upgradesByEntity.TryGetValue(lookupKey, out List<string> upgList))
                        {
                            cardUpgrades.AddRange(upgList);
                        }
                        else if (!string.Equals(lookupKey, cardId, StringComparison.OrdinalIgnoreCase)
                            && upgradesByEntity.TryGetValue(cardId, out List<string> factoryUpgList))
                        {
                            cardUpgrades.AddRange(factoryUpgList);
                        }

                        bool isViable = false;
                        if (isAssignActive)
                        {
                            if (!viabilityByProductId.TryGetValue(lookupKey, out isViable))
                            {
                                viabilityByProductId.TryGetValue(cardId, out isViable);
                            }
                        }

                        rows.Add(new IntermissionDeckCardRow(
                            cardId,
                            productId,
                            cardUpgrades.Count,
                            cardUpgrades.AsReadOnly(),
                            isViable));
                    }
                }
            }
            catch
            {
            }

            return rows;
        }

        private static Dictionary<string, List<string>> ReadCardUpgradesMap(Type gameType)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                object mapObj = ReadStaticObject(gameType, "CardUpgrades");
                if (mapObj is IDictionary dict)
                {
                    foreach (DictionaryEntry entry in dict)
                    {
                        string key = entry.Key as string;
                        if (string.IsNullOrEmpty(key))
                        {
                            continue;
                        }

                        var list = new List<string>();
                        if (entry.Value is IEnumerable values)
                        {
                            foreach (object v in values)
                            {
                                if (v is string s && !string.IsNullOrEmpty(s))
                                {
                                    list.Add(s);
                                }
                            }
                        }

                        result[key] = list;
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static string ResolveProductEntityId(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                return string.Empty;
            }

            try
            {
                Type storeType = RuntimeIntermissionCensus.FindGameType("EntityBalancingStore");
                if (storeType != null)
                {
                    MethodInfo method = storeType.GetMethod(
                        "ProductEntityId", StaticMembers, null, new[] { typeof(string) }, null);
                    if (method != null && method.ReturnType == typeof(string))
                    {
                        return method.Invoke(null, new object[] { cardId }) as string ?? string.Empty;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string ReadCardIdentifier(object target, params string[] candidateNames)
        {
            if (target == null || candidateNames == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < candidateNames.Length; i++)
            {
                string propVal = ReadStringProperty(target, candidateNames[i]);
                if (!string.IsNullOrEmpty(propVal))
                {
                    return propVal;
                }

                string fieldVal = ReadStringField(target, candidateNames[i]);
                if (!string.IsNullOrEmpty(fieldVal))
                {
                    return fieldVal;
                }
            }

            return string.Empty;
        }

        private static string ReadUiText(object target, string textFieldName)
        {
            try
            {
                object textObj = RuntimeIntermissionCensus.ReadObjectField(target, textFieldName);
                if (textObj == null)
                {
                    return string.Empty;
                }

                return ReadStringProperty(textObj, "text");
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int ReadStaticInt(Type type, string name, int fallback)
        {
            try
            {
                PropertyInfo prop = type.GetProperty(name, StaticMembers);
                if (prop != null && prop.CanRead && prop.PropertyType == typeof(int))
                {
                    return (int)prop.GetValue(null, null);
                }

                FieldInfo field = type.GetField(name, StaticMembers);
                if (field != null && field.FieldType == typeof(int))
                {
                    return (int)field.GetValue(null);
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static bool ReadStaticBool(Type type, string name, bool fallback)
        {
            try
            {
                PropertyInfo prop = type.GetProperty(name, StaticMembers);
                if (prop != null && prop.CanRead && prop.PropertyType == typeof(bool))
                {
                    return (bool)prop.GetValue(null, null);
                }

                FieldInfo field = type.GetField(name, StaticMembers);
                if (field != null && field.FieldType == typeof(bool))
                {
                    return (bool)field.GetValue(null);
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static object ReadStaticObject(Type type, string name)
        {
            try
            {
                PropertyInfo prop = type.GetProperty(name, StaticMembers);
                if (prop != null && prop.CanRead)
                {
                    return prop.GetValue(null, null);
                }

                FieldInfo field = type.GetField(name, StaticMembers);
                if (field != null)
                {
                    return field.GetValue(null);
                }
            }
            catch
            {
            }

            return null;
        }

        private static int ReadStaticCollectionCount(Type type, string name)
        {
            try
            {
                object obj = ReadStaticObject(type, name);
                if (obj is ICollection collection)
                {
                    return collection.Count;
                }
            }
            catch
            {
            }

            return 0;
        }

        private static float ReadFloatField(object target, string name, float fallback)
        {
            if (target == null)
            {
                return fallback;
            }

            try
            {
                FieldInfo field = target.GetType().GetField(name, InstanceMembers);
                if (field != null && field.FieldType == typeof(float))
                {
                    return (float)field.GetValue(target);
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static string ReadStringField(object target, string name)
        {
            if (target == null)
            {
                return string.Empty;
            }

            try
            {
                FieldInfo field = target.GetType().GetField(name, InstanceMembers);
                if (field != null && field.FieldType == typeof(string))
                {
                    return field.GetValue(target) as string ?? string.Empty;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string ReadStringProperty(object target, string name)
        {
            if (target == null)
            {
                return string.Empty;
            }

            try
            {
                PropertyInfo prop = target.GetType().GetProperty(name, InstanceMembers);
                if (prop != null && prop.CanRead && prop.PropertyType == typeof(string))
                {
                    return prop.GetValue(target, null) as string ?? string.Empty;
                }
            }
            catch
            {
            }

            return string.Empty;
        }
    }
}
