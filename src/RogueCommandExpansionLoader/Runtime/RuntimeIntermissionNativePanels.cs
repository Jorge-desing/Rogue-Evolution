using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>One live offer registered with the native intermission Shop.</summary>
    public readonly struct IntermissionShopOfferRow
    {
        public IntermissionShopOfferRow(
            string offerId,
            string kind,
            string nativeId,
            string name,
            string priceText,
            int instanceId,
            bool hasPrice,
            int price)
        {
            OfferId = offerId ?? string.Empty;
            Kind = kind ?? string.Empty;
            NativeId = nativeId ?? string.Empty;
            Name = name ?? string.Empty;
            PriceText = priceText ?? string.Empty;
            InstanceId = instanceId;
            HasPrice = hasPrice;
            Price = price;
        }

        public string OfferId { get; }
        public string Kind { get; }
        public string NativeId { get; }
        public string Name { get; }
        public string PriceText { get; }
        public int InstanceId { get; }
        public bool HasPrice { get; }
        public int Price { get; }
    }

    /// <summary>A current CardNew candidate in the native RemoveCardView.</summary>
    public readonly struct IntermissionRemovalCardRow
    {
        public IntermissionRemovalCardRow(int slotIndex, string entityId, string productId, string name, int instanceId)
        {
            SlotIndex = slotIndex;
            EntityId = entityId ?? string.Empty;
            ProductId = productId ?? string.Empty;
            Name = name ?? string.Empty;
            InstanceId = instanceId;
        }

        public int SlotIndex { get; }
        public string EntityId { get; }
        public string ProductId { get; }
        public string Name { get; }
        public int InstanceId { get; }
    }

    /// <summary>
    /// Bounded screenshot-free state for native Shop and RemoveCardView panels.
    /// Omission counts precede collections in serialized output.
    /// </summary>
    public sealed class IntermissionNativePanelState
    {
        public IntermissionNativePanelState(
            bool isAvailable,
            bool isShopAvailable,
            bool isShopActive,
            int shopInstanceId,
            int currentCoins,
            int currentRerolls,
            int currentStartCrystals,
            int currentDropPodCount,
            int currentResearchPoints,
            bool canReroll,
            int shopOfferCount,
            int omittedShopOfferCount,
            int unsupportedShopOfferCount,
            IReadOnlyList<IntermissionShopOfferRow> shopOffers,
            bool isRemoveCardViewAvailable,
            bool isRemoveCardActive,
            bool isRemoveCardClickReady,
            int removeCardCoinCompensation,
            int removalCardCount,
            int omittedRemovalCardCount,
            int unsupportedRemovalCardCount,
            IReadOnlyList<IntermissionRemovalCardRow> removalCards)
        {
            IsAvailable = isAvailable;
            IsShopAvailable = isShopAvailable;
            IsShopActive = isShopActive;
            ShopInstanceId = shopInstanceId;
            CurrentCoins = currentCoins;
            CurrentRerolls = currentRerolls;
            CurrentStartCrystals = currentStartCrystals;
            CurrentDropPodCount = currentDropPodCount;
            CurrentResearchPoints = currentResearchPoints;
            CanReroll = canReroll;
            ShopOfferCount = shopOfferCount;
            OmittedShopOfferCount = omittedShopOfferCount;
            UnsupportedShopOfferCount = unsupportedShopOfferCount;
            ShopOffers = shopOffers ?? Array.Empty<IntermissionShopOfferRow>();
            IsRemoveCardViewAvailable = isRemoveCardViewAvailable;
            IsRemoveCardActive = isRemoveCardActive;
            IsRemoveCardClickReady = isRemoveCardClickReady;
            RemoveCardCoinCompensation = removeCardCoinCompensation;
            RemovalCardCount = removalCardCount;
            OmittedRemovalCardCount = omittedRemovalCardCount;
            UnsupportedRemovalCardCount = unsupportedRemovalCardCount;
            RemovalCards = removalCards ?? Array.Empty<IntermissionRemovalCardRow>();
        }

        public bool IsAvailable { get; }
        public bool IsShopAvailable { get; }
        public bool IsShopActive { get; }
        public int ShopInstanceId { get; }
        public int CurrentCoins { get; }
        public int CurrentRerolls { get; }
        public int CurrentStartCrystals { get; }
        public int CurrentDropPodCount { get; }
        public int CurrentResearchPoints { get; }
        public bool CanReroll { get; }
        public int ShopOfferCount { get; }
        public int OmittedShopOfferCount { get; }
        public int UnsupportedShopOfferCount { get; }
        public IReadOnlyList<IntermissionShopOfferRow> ShopOffers { get; }
        public bool IsRemoveCardViewAvailable { get; }
        public bool IsRemoveCardActive { get; }
        public bool IsRemoveCardClickReady { get; }
        public int RemoveCardCoinCompensation { get; }
        public int RemovalCardCount { get; }
        public int OmittedRemovalCardCount { get; }
        public int UnsupportedRemovalCardCount { get; }
        public IReadOnlyList<IntermissionRemovalCardRow> RemovalCards { get; }

        public static IntermissionNativePanelState Unavailable { get; } = new IntermissionNativePanelState(
            false, false, false, 0, 0, 0, 0, 0, 0, false, 0, 0, 0,
            Array.Empty<IntermissionShopOfferRow>(), false, false, false, 0, 0, 0, 0,
            Array.Empty<IntermissionRemovalCardRow>());
    }

    /// <summary>
    /// Read-only native panel census and guarded actions. Invocations dispatch only
    /// the game's current Shop/RemoveCardView methods; no Harmony patch or UI bypass is used.
    /// </summary>
    public static class RuntimeIntermissionNativePanels
    {
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const int MaxScanRows = 512;

        private static readonly HashSet<string> SupportedShopCardTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "RelicCard", "UpgradeCard", "ResearchCard", "DropPodCard",
            "StartCrystalsCard", "ResearchPointsCard", "RandomDropCard",
        };

        public static IntermissionNativePanelState GetState(int maxRows = 32)
        {
            try
            {
                int clamp = ClampMaxRows(maxRows);
                Type gameType = RuntimeIntermissionCensus.FindGameType("Game");
                object shop = ResolveSingleton("Shop");
                object removalView = ResolveSingleton("RemoveCardView");
                bool shopActive = IsComponentActive(shop);
                bool removalAvailable = removalView != null;
                bool removalActive = removalAvailable && ReadBoolField(removalView, "_isShowing", false);
                int currentCoins = ReadStaticInt(gameType, "CurrentCoins");
                int currentRerolls = ReadStaticInt(gameType, "CurrentRerollCount");
                int crystals = ReadStaticInt(gameType, "CurrentStartCrystals");
                int dropPods = ReadStaticInt(gameType, "CurrentDropPodCount");
                int researchPoints = ReadStaticInt(gameType, "CurrentResearchPoints");

                int unsupportedOffers;
                List<IntermissionShopOfferRow> allOffers = ReadShopOffers(shop, out unsupportedOffers);
                int keptOffers = Math.Min(clamp, allOffers.Count);
                int omittedOffers = allOffers.Count - keptOffers;
                var offerRows = allOffers.GetRange(0, keptOffers).AsReadOnly();
                bool canReroll = shopActive && HasActiveRerollButton(shop)
                    && (currentRerolls > 0 || ReadStaticBool("RerollButton", "RerollsAreEndless"));

                bool removalReady = removalActive && IsClickReady(removalView);
                int compensation = removalAvailable ? ReadIntMember(removalView, "coinAmountToEarn", 0) : 0;
                int unsupportedRemovalCards;
                List<IntermissionRemovalCardRow> allRemovalCards = removalActive
                    ? ReadRemovalCards(removalView, out unsupportedRemovalCards)
                    : EmptyRemovalCards(out unsupportedRemovalCards);
                int keptRemovalCards = Math.Min(clamp, allRemovalCards.Count);
                int omittedRemovalCards = allRemovalCards.Count - keptRemovalCards;
                var removalRows = allRemovalCards.GetRange(0, keptRemovalCards).AsReadOnly();

                bool available = shop != null || removalAvailable;
                if (!available)
                {
                    return IntermissionNativePanelState.Unavailable;
                }

                return new IntermissionNativePanelState(
                    true,
                    shop != null,
                    shopActive,
                    RuntimeIntermissionCensus.ReadInstanceId(shop),
                    currentCoins,
                    currentRerolls,
                    crystals,
                    dropPods,
                    researchPoints,
                    canReroll,
                    allOffers.Count,
                    omittedOffers,
                    unsupportedOffers,
                    offerRows,
                    removalAvailable,
                    removalActive,
                    removalReady,
                    compensation,
                    allRemovalCards.Count,
                    omittedRemovalCards,
                    unsupportedRemovalCards,
                    removalRows);
            }
            catch
            {
                return IntermissionNativePanelState.Unavailable;
            }
        }

        public static IntermissionActionResult TryBuyShopOffer(string offerId)
        {
            string requestedId = (offerId ?? string.Empty).Trim();
            bool nativeCallInvoked = false;
            try
            {
                IntermissionNativePanelState before = GetState(MaxScanRows);
                if (!before.IsShopActive)
                {
                    return Action(false, "BuyShopOffer", requestedId, "Shop is not active (stop point: active Shop gate).", false, false);
                }

                IntermissionShopOfferRow selected;
                if (!TryFindOffer(before.ShopOffers, requestedId, out selected))
                {
                    return Action(false, "BuyShopOffer", requestedId, "offerId is not present in the current Shop census (stop point: offer identity gate).", false, false);
                }

                object shop = ResolveSingleton("Shop");
                Component card = FindCurrentShopCard(shop, selected.InstanceId, selected.Kind);
                if (card == null || !card.gameObject.activeInHierarchy)
                {
                    return Action(false, "BuyShopOffer", requestedId, "The selected offer is no longer an active native Shop item (stop point: live offer recheck).", false, false);
                }

                MethodInfo pressed = card.GetType().GetMethod("PressedChooseButton", InstanceMembers, null, Type.EmptyTypes, null);
                if (pressed == null || pressed.ReturnType != typeof(void))
                {
                    return Action(false, "BuyShopOffer", requestedId, "The exact native PressedChooseButton() method is unavailable for this offer.", false, false);
                }

                IntermissionRewardState rewardBefore = RuntimeIntermissionRewards.GetState(MaxScanRows);
                nativeCallInvoked = true;
                pressed.Invoke(card, null);
                IntermissionNativePanelState after = GetState(MaxScanRows);
                IntermissionRewardState rewardAfter = RuntimeIntermissionRewards.GetState(MaxScanRows);
                bool effectObserved = !ContainsOffer(after.ShopOffers, requestedId)
                    || ShopCurrencyChanged(before, after)
                    || rewardAfter.PlayerHasOpenRewards != rewardBefore.PlayerHasOpenRewards
                    || rewardAfter.DeckCardCount != rewardBefore.DeckCardCount;
                return Action(effectObserved, "BuyShopOffer", requestedId,
                    effectObserved
                        ? "Invoked the native offer button and observed a Shop, currency, reward, or deck change."
                        : "Invoked the native offer button; its effect is not yet observable. Re-read the panel before taking another action.",
                    true, effectObserved);
            }
            catch (Exception ex)
            {
                return Action(false, "BuyShopOffer", requestedId,
                    "Native Shop purchase threw or its postcondition could not be read: " + ex.Message,
                    nativeCallInvoked, false);
            }
        }

        public static IntermissionActionResult TryRerollShop()
        {
            bool nativeCallInvoked = false;
            try
            {
                IntermissionNativePanelState before = GetState(MaxScanRows);
                if (!before.CanReroll)
                {
                    return Action(false, "RerollShop", "Shop", "Native Shop reroll control is not currently available (stop point: reroll gate).", false, false);
                }

                object shop = ResolveSingleton("Shop");
                MethodInfo reroll = shop?.GetType().GetMethod("ClickedOnRerollButton", InstanceMembers, null, Type.EmptyTypes, null);
                if (reroll == null || reroll.ReturnType != typeof(void))
                {
                    return Action(false, "RerollShop", "Shop", "The exact native Shop.ClickedOnRerollButton() method is unavailable.", false, false);
                }

                nativeCallInvoked = true;
                reroll.Invoke(shop, null);
                IntermissionNativePanelState after = GetState(MaxScanRows);
                bool effectObserved = ShopContentChanged(before.ShopOffers, after.ShopOffers)
                    || before.CurrentRerolls != after.CurrentRerolls
                    || before.CurrentCoins != after.CurrentCoins;
                return Action(effectObserved, "RerollShop", "Shop",
                    effectObserved
                        ? "Invoked the native reroll control and observed an offer or resource change."
                        : "Invoked the native reroll control; its effect is not yet observable. Re-read the panel before taking another action.",
                    true, effectObserved);
            }
            catch (Exception ex)
            {
                return Action(false, "RerollShop", "Shop",
                    "Native Shop reroll threw or its postcondition could not be read: " + ex.Message,
                    nativeCallInvoked, false);
            }
        }

        public static IntermissionActionResult TryRemoveCard(string entityId)
        {
            string requestedId = (entityId ?? string.Empty).Trim();
            bool nativeCallInvoked = false;
            try
            {
                IntermissionNativePanelState before = GetState(MaxScanRows);
                if (!before.IsRemoveCardActive)
                {
                    return Action(false, "RemoveIntermissionCard", requestedId, "RemoveCardView is not active (stop point: view gate).", false, false);
                }
                if (!before.IsRemoveCardClickReady)
                {
                    return Action(false, "RemoveIntermissionCard", requestedId, "RemoveCardView is still inside its native click delay; wait and re-read its state.", false, false);
                }
                if (string.IsNullOrEmpty(requestedId))
                {
                    return Action(false, "RemoveIntermissionCard", requestedId, "entityId is required.", false, false);
                }

                int matches = CountRemovalCards(before.RemovalCards, requestedId);
                if (matches != 1)
                {
                    return Action(false, "RemoveIntermissionCard", requestedId,
                        matches == 0
                            ? "entityId is not present in the current RemoveCardView candidates."
                            : "entityId appears more than once and the native string-only action cannot distinguish those rows.",
                        false, false);
                }

                object view = ResolveSingleton("RemoveCardView");
                MethodInfo remove = view?.GetType().GetMethod("RemoveCard", InstanceMembers, null, new[] { typeof(string) }, null);
                if (remove == null || remove.ReturnType != typeof(void))
                {
                    return Action(false, "RemoveIntermissionCard", requestedId, "The exact native RemoveCard(string) method is unavailable.", false, false);
                }

                nativeCallInvoked = true;
                remove.Invoke(view, new object[] { requestedId });
                IntermissionNativePanelState after = GetState(MaxScanRows);
                bool effectObserved = CountRemovalCards(after.RemovalCards, requestedId) == 0
                    || after.CurrentCoins != before.CurrentCoins;
                return Action(effectObserved, "RemoveIntermissionCard", requestedId,
                    effectObserved
                        ? "Invoked the native removal action and observed the card leave the active candidate list or a coin change."
                        : "Invoked the native removal action; its effect is not yet observable. Re-read the panel before taking another action.",
                    true, effectObserved);
            }
            catch (Exception ex)
            {
                return Action(false, "RemoveIntermissionCard", requestedId,
                    "Native card removal threw or its postcondition could not be read: " + ex.Message,
                    nativeCallInvoked, false);
            }
        }

        public static IntermissionActionResult TryCancelCardRemoval()
        {
            bool nativeCallInvoked = false;
            try
            {
                IntermissionNativePanelState before = GetState(MaxScanRows);
                if (!before.IsRemoveCardActive)
                {
                    return Action(false, "CancelCardRemoval", "RemoveCardView", "RemoveCardView is not active (stop point: view gate).", false, false);
                }

                object view = ResolveSingleton("RemoveCardView");
                MethodInfo cancel = view?.GetType().GetMethod("ClickedOnCancel", InstanceMembers, null, Type.EmptyTypes, null);
                if (cancel == null || cancel.ReturnType != typeof(void))
                {
                    return Action(false, "CancelCardRemoval", "RemoveCardView", "The exact native ClickedOnCancel() method is unavailable.", false, false);
                }

                nativeCallInvoked = true;
                cancel.Invoke(view, null);
                bool effectObserved = !GetState(MaxScanRows).IsRemoveCardActive;
                return Action(effectObserved, "CancelCardRemoval", "RemoveCardView",
                    effectObserved
                        ? "Invoked the native cancel control and observed RemoveCardView close."
                        : "Invoked the native cancel control; closure is not yet observable. Re-read the panel before taking another action.",
                    true, effectObserved);
            }
            catch (Exception ex)
            {
                return Action(false, "CancelCardRemoval", "RemoveCardView",
                    "Native removal cancellation threw or its postcondition could not be read: " + ex.Message,
                    nativeCallInvoked, false);
            }
        }

        public static IntermissionActionResult TryLeaveShop(int gameObjectId)
        {
            bool invoked = false;
            try
            {
                var before = GetState(MaxScanRows);
                if (!before.IsShopActive || before.IsRemoveCardActive)
                    return Action(false, "LeaveShop", "Shop", "An active Shop without a removal dialog is required.", false, false);
                Type type = RuntimeIntermissionCensus.FindGameType("ShopLeaveButton");
                MethodInfo finder = typeof(Resources).GetMethod("FindObjectsOfTypeAll", new[] { typeof(Type) });
                Array objects = type == null || finder == null ? null : finder.Invoke(null, new object[] { type }) as Array;
                Component target = null;
                int matches = 0;
                if (objects != null)
                    foreach (object obj in objects)
                    {
                        Component candidate = obj as Component;
                        if (IsComponentActive(candidate) && candidate.gameObject.scene.isLoaded && candidate.gameObject.GetInstanceID() == gameObjectId)
                        { target = candidate; matches++; }
                    }
                if (matches != 1)
                    return Action(false, "LeaveShop", "Shop", "Expected one active ShopLeaveButton with the current gameObjectId.", false, false);
                bool interactable = false;
                foreach (Component control in target.gameObject.GetComponents<Component>())
                {
                    if (control is Behaviour behaviour && !behaviour.enabled) continue;
                    if (control != null && control.GetType().GetMethod("IsInteractable", Type.EmptyTypes) is MethodInfo canInteract
                        && control.GetType().GetMethod("IsActive", Type.EmptyTypes) is MethodInfo isActive
                        && canInteract.ReturnType == typeof(bool) && isActive.ReturnType == typeof(bool))
                        interactable = (bool)canInteract.Invoke(control, null) && (bool)isActive.Invoke(control, null);
                    else if (control != null && control.GetType().FullName == "Michsky.UI.Reach.ButtonManager")
                        interactable = (control.GetType().GetField("isInteractable", InstanceMembers)?.GetValue(control) is bool enabled && enabled) && IsComponentActive(control);
                    if (interactable) break;
                }
                if (!interactable)
                    return Action(false, "LeaveShop", "Shop", "Native leave control is not interactable.", false, false);
                MethodInfo press = type.GetMethod("OnPress", InstanceMembers, null, Type.EmptyTypes, null);
                if (press == null || press.ReturnType != typeof(void))
                    return Action(false, "LeaveShop", "Shop", "Native OnPress() is unavailable.", false, false);
                invoked = true;
                press.Invoke(target, null);
                bool observed = !GetState(MaxScanRows).IsShopActive;
                return Action(observed, "LeaveShop", "Shop", observed ? "Native Shop exit invoked and Shop closed." : "Native Shop exit invoked; verify the asynchronous transition before another action.", true, observed);
            }
            catch (Exception ex)
            {
                return Action(false, "LeaveShop", "Shop", "Native Shop exit failed: " + ex.Message, invoked, false);
            }
        }

        private static IntermissionActionResult Action(
            bool success, string action, string target, string message, bool invoked, bool observed)
        {
            return new IntermissionActionResult(
                success, action, target, -1, string.Empty, false, message, invoked, observed);
        }

        private static bool TryFindOffer(IReadOnlyList<IntermissionShopOfferRow> offers, string offerId, out IntermissionShopOfferRow row)
        {
            row = default(IntermissionShopOfferRow);
            if (offers == null || string.IsNullOrEmpty(offerId)) return false;
            for (int i = 0; i < offers.Count; i++)
            {
                if (string.Equals(offers[i].OfferId, offerId, StringComparison.Ordinal))
                {
                    row = offers[i];
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsOffer(IReadOnlyList<IntermissionShopOfferRow> offers, string offerId)
        {
            IntermissionShopOfferRow ignored;
            return TryFindOffer(offers, offerId, out ignored);
        }

        private static bool ShopCurrencyChanged(IntermissionNativePanelState before, IntermissionNativePanelState after)
        {
            return before.CurrentCoins != after.CurrentCoins
                || before.CurrentRerolls != after.CurrentRerolls
                || before.CurrentStartCrystals != after.CurrentStartCrystals
                || before.CurrentDropPodCount != after.CurrentDropPodCount
                || before.CurrentResearchPoints != after.CurrentResearchPoints;
        }

        private static bool ShopContentChanged(
            IReadOnlyList<IntermissionShopOfferRow> before,
            IReadOnlyList<IntermissionShopOfferRow> after)
        {
            if (before == null || after == null || before.Count != after.Count) return true;
            for (int i = 0; i < before.Count; i++)
            {
                IntermissionShopOfferRow a = before[i];
                bool found = false;
                for (int j = 0; j < after.Count; j++)
                {
                    IntermissionShopOfferRow b = after[j];
                    if (a.InstanceId == b.InstanceId && a.Kind == b.Kind && a.NativeId == b.NativeId
                        && a.Name == b.Name && a.PriceText == b.PriceText
                        && a.HasPrice == b.HasPrice && a.Price == b.Price)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return true;
            }
            return false;
        }

        private static int CountRemovalCards(IReadOnlyList<IntermissionRemovalCardRow> rows, string entityId)
        {
            int count = 0;
            if (rows == null) return count;
            for (int i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].EntityId, entityId, StringComparison.OrdinalIgnoreCase)) count++;
            }
            return count;
        }

        private static Component FindCurrentShopCard(object shop, int instanceId, string kind)
        {
            if (shop == null || instanceId == 0 || !SupportedShopCardTypes.Contains(kind)) return null;
            object items = RuntimeIntermissionCensus.ReadObjectField(shop, "_changeableShopItems");
            if (!(items is IEnumerable enumerable)) return null;
            int scanned = 0;
            foreach (object item in enumerable)
            {
                if (++scanned > MaxScanRows) break;
                Component component = item as Component;
                if (component != null && component.GetType().Name == kind
                    && RuntimeIntermissionCensus.ReadInstanceId(component) == instanceId)
                {
                    return component;
                }
            }
            return null;
        }

        private static List<IntermissionShopOfferRow> ReadShopOffers(object shop, out int unsupportedCount)
        {
            unsupportedCount = 0;
            var rows = new List<IntermissionShopOfferRow>();
            object items = shop == null ? null : RuntimeIntermissionCensus.ReadObjectField(shop, "_changeableShopItems");
            if (!(items is IEnumerable enumerable)) return rows;

            int scanned = 0;
            foreach (object item in enumerable)
            {
                if (++scanned > MaxScanRows)
                {
                    unsupportedCount++;
                    break;
                }

                Component component = item as Component;
                if (component == null) { unsupportedCount++; continue; }
                if (component.gameObject == null || !component.gameObject.activeInHierarchy) continue;
                string kind = component.GetType().Name;
                if (!SupportedShopCardTypes.Contains(kind)) { unsupportedCount++; continue; }

                int instanceId = RuntimeIntermissionCensus.ReadInstanceId(component);
                string nativeId = ReadStringProperty(component, "RelicId", "UpgradeId", "ResearchId", "EntityId", "Id");
                string name = ReadUiText(component, "nameText", "_nameText");
                if (string.IsNullOrWhiteSpace(name)) name = component.gameObject != null ? component.gameObject.name : kind;
                bool hasPrice;
                int price = ReadIntProperty(component, "ShopPrice", out hasPrice);
                string priceText = ReadUiText(component, "shopPriceText", "_shopPriceText");
                string offerId = BuildOfferId(kind, instanceId);
                rows.Add(new IntermissionShopOfferRow(offerId, kind, nativeId, name, priceText, instanceId, hasPrice, price));
            }
            return rows;
        }

        private static List<IntermissionRemovalCardRow> ReadRemovalCards(object view, out int unsupportedCount)
        {
            unsupportedCount = 0;
            var rows = new List<IntermissionRemovalCardRow>();
            object cards = RuntimeIntermissionCensus.ReadObjectField(view, "_cards");
            if (!(cards is IEnumerable enumerable)) return rows;

            int slot = 0;
            foreach (object card in enumerable)
            {
                if (slot >= MaxScanRows) { unsupportedCount++; break; }
                int currentSlot = slot++;
                if (card == null || card.GetType().Name != "CardNew") { unsupportedCount++; continue; }
                string entityId = ReadStringProperty(card, "EntityId");
                if (string.IsNullOrWhiteSpace(entityId)) { unsupportedCount++; continue; }
                string name = ReadUiText(card, "nameText");
                if (string.IsNullOrWhiteSpace(name)) name = entityId;
                string productId = ResolveProductEntityId(entityId);
                rows.Add(new IntermissionRemovalCardRow(
                    currentSlot,
                    entityId,
                    productId,
                    name,
                    RuntimeIntermissionCensus.ReadInstanceId(card)));
            }
            return rows;
        }

        private static List<IntermissionRemovalCardRow> EmptyRemovalCards(out int unsupportedCount)
        {
            unsupportedCount = 0;
            return new List<IntermissionRemovalCardRow>();
        }

        public static string BuildOfferId(string kind, int instanceId) =>
            (kind ?? string.Empty) + ":" + instanceId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static bool HasActiveRerollButton(object shop)
        {
            Component button = RuntimeIntermissionCensus.ReadObjectField(shop, "rerollButtonRectTransform") as Component;
            return button != null && button.gameObject != null && button.gameObject.activeInHierarchy;
        }

        private static bool IsClickReady(object view)
        {
            try
            {
                float start = ReadFloatMember(view, "_displayStartTime", float.NaN);
                float delay = ReadFloatMember(view, "clickableAfterSeconds", float.NaN);
                return !float.IsNaN(start) && !float.IsNaN(delay)
                    && Time.realtimeSinceStartup - start >= Math.Max(0f, delay);
            }
            catch { return false; }
        }

        private static object ResolveSingleton(string typeName)
        {
            Type type = RuntimeIntermissionCensus.FindGameType(typeName);
            if (type == null) return null;
            try
            {
                FieldInfo singleton = type.GetField("_instance", StaticMembers);
                object value = singleton?.GetValue(null);
                if (!IsUnityNull(value)) return value;
            }
            catch { }
            return RuntimeIntermissionCensus.FindInstanceOf(type);
        }

        private static bool IsUnityNull(object value)
        {
            return value == null || (value is UnityEngine.Object unityObject && unityObject == null);
        }

        private static bool IsComponentActive(object value)
        {
            Component component = value as Component;
            return component != null && component.gameObject != null && component.gameObject.activeInHierarchy;
        }

        private static int ClampMaxRows(int maxRows) => Math.Max(1, Math.Min(128, maxRows <= 0 ? 32 : maxRows));

        private static int ReadStaticInt(Type type, string name)
        {
            if (type == null) return 0;
            try
            {
                PropertyInfo prop = type.GetProperty(name, StaticMembers);
                if (prop != null && prop.CanRead && prop.PropertyType == typeof(int)) return (int)prop.GetValue(null, null);
                FieldInfo field = type.GetField(name, StaticMembers);
                if (field != null && field.FieldType == typeof(int)) return (int)field.GetValue(null);
            }
            catch { }
            return 0;
        }

        private static bool ReadStaticBool(string typeName, string name)
        {
            Type type = RuntimeIntermissionCensus.FindGameType(typeName);
            if (type == null) return false;
            try
            {
                PropertyInfo prop = type.GetProperty(name, StaticMembers);
                if (prop != null && prop.CanRead && prop.PropertyType == typeof(bool)) return (bool)prop.GetValue(null, null);
                FieldInfo field = type.GetField(name, StaticMembers);
                if (field != null && field.FieldType == typeof(bool)) return (bool)field.GetValue(null);
            }
            catch { }
            return false;
        }

        private static bool ReadBoolField(object target, string name, bool fallback)
        {
            try
            {
                FieldInfo field = target?.GetType().GetField(name, InstanceMembers);
                if (field != null && field.FieldType == typeof(bool)) return (bool)field.GetValue(target);
            }
            catch { }
            return fallback;
        }

        private static int ReadIntMember(object target, string name, int fallback)
        {
            try
            {
                PropertyInfo prop = target?.GetType().GetProperty(name, InstanceMembers);
                if (prop != null && prop.CanRead && prop.PropertyType == typeof(int)) return (int)prop.GetValue(target, null);
                FieldInfo field = target?.GetType().GetField(name, InstanceMembers);
                if (field != null && field.FieldType == typeof(int)) return (int)field.GetValue(target);
            }
            catch { }
            return fallback;
        }

        private static float ReadFloatMember(object target, string name, float fallback)
        {
            try
            {
                PropertyInfo prop = target?.GetType().GetProperty(name, InstanceMembers);
                if (prop != null && prop.CanRead && prop.PropertyType == typeof(float)) return (float)prop.GetValue(target, null);
                FieldInfo field = target?.GetType().GetField(name, InstanceMembers);
                if (field != null && field.FieldType == typeof(float)) return (float)field.GetValue(target);
            }
            catch { }
            return fallback;
        }

        private static int ReadIntProperty(object target, string name, out bool found)
        {
            found = false;
            try
            {
                PropertyInfo prop = target?.GetType().GetProperty(name, InstanceMembers);
                if (prop != null && prop.CanRead && prop.PropertyType == typeof(int))
                {
                    found = true;
                    return (int)prop.GetValue(target, null);
                }
                FieldInfo field = target?.GetType().GetField(name, InstanceMembers);
                if (field != null && field.FieldType == typeof(int))
                {
                    found = true;
                    return (int)field.GetValue(target);
                }
            }
            catch { }
            return 0;
        }

        private static string ReadStringProperty(object target, params string[] names)
        {
            if (target == null || names == null) return string.Empty;
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    PropertyInfo prop = target.GetType().GetProperty(names[i], InstanceMembers);
                    if (prop != null && prop.CanRead && prop.PropertyType == typeof(string))
                    {
                        string value = prop.GetValue(target, null) as string;
                        if (!string.IsNullOrWhiteSpace(value)) return value;
                    }
                    FieldInfo field = target.GetType().GetField(names[i], InstanceMembers);
                    if (field != null && field.FieldType == typeof(string))
                    {
                        string value = field.GetValue(target) as string;
                        if (!string.IsNullOrWhiteSpace(value)) return value;
                    }
                }
                catch { }
            }
            return string.Empty;
        }

        private static string ReadUiText(object target, params string[] names)
        {
            if (target == null || names == null) return string.Empty;
            for (int i = 0; i < names.Length; i++)
            {
                object textObject = RuntimeIntermissionCensus.ReadObjectField(target, names[i]);
                string text = ReadStringProperty(textObject, "text");
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            return string.Empty;
        }

        private static string ResolveProductEntityId(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return string.Empty;
            try
            {
                Type type = RuntimeIntermissionCensus.FindGameType("EntityBalancingStore");
                MethodInfo method = type?.GetMethod("ProductEntityId", StaticMembers, null, new[] { typeof(string) }, null);
                if (method != null && method.ReturnType == typeof(string))
                    return method.Invoke(null, new object[] { entityId }) as string ?? string.Empty;
            }
            catch { }
            return string.Empty;
        }
    }
}
