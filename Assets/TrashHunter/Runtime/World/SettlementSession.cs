using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TrashHunter
{
    public sealed class SettlementSession : MonoBehaviour
    {
        private const int VisibleRows = 5;
        private const string QuestId = QuestCatalog.TraderTurntable;
        private enum Mode { Home, Trader, Sale, Pause, Continue, Quest, Present }

        private GameState state;
        private LootCatalog catalog;
        private SceneCoordinator scenes;
        private ExpeditionView view;
        private UiStrings strings;
        private RunLocation location;
        private Mode mode;
        private int selection;
        private SaleOffer offer;
        private HomeItem presentedItem;
        private string presentationStep;
        private string receipt;
        private bool quitting;

        private bool AtTrader => location == RunLocation.Trader;
        private string Title => strings.Get(AtTrader ? "trader.title" : "home.title");

        internal void Initialize(GameState gameState, LootCatalog lootCatalog, SceneCoordinator coordinator,
            RunLocation sceneLocation, PlayerPose spawn, SearchPlayer player, SearchHud hud)
        {
            state = gameState;
            catalog = lootCatalog;
            scenes = coordinator;
            location = sceneLocation;
            view = hud.Expedition;
            strings = new UiStrings();
            player.Restore(spawn);
            hud.Show(string.Empty, string.Empty, false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Application.wantsToQuit += SaveBeforeQuit;

            if (state.ResumePending)
            {
                mode = Mode.Continue;
                view.Show(strings.Get("continue.title"), Title, string.Empty,
                    strings.Get("continue.actions"), state.RecoveryNotice);
            }
            else
                ShowStock();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (scenes == null || !scenes.HasControl || keyboard == null)
                return;

            var confirm = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            var cancel = keyboard.escapeKey.wasPressedThisFrame;
            switch (mode)
            {
                case Mode.Continue:
                    if (confirm)
                    {
                        state.AcknowledgeResume();
                        ShowStock();
                    }
                    else if (cancel)
                        Application.Quit();
                    break;
                case Mode.Pause:
                    if (cancel || confirm)
                        ShowStock();
                    else if (keyboard.qKey.wasPressedThisFrame && SaveBeforeQuit())
                        Application.Quit();
                    break;
                case Mode.Quest:
                    if (cancel)
                        ShowStock();
                    else if (confirm && AtTrader && state.Quests.IsAvailable(QuestId))
                    {
                        state.Quests.Accept(QuestId);
                        ShowQuest();
                    }
                    break;
                case Mode.Present:
                    if (cancel)
                        ShowStock();
                    else if (confirm)
                    {
                        if (state.Trade.Present(QuestId, presentationStep, presentedItem))
                            ShowQuest();
                        else
                            ShowPresentation();
                    }
                    break;
                case Mode.Sale:
                    if (cancel)
                        ShowStock();
                    else if (confirm)
                    {
                        if (state.Trade.Sell(offer))
                        {
                            receipt = $"{ItemLabel(offer.Item)}  +{offer.Price} ₽";
                            ShowStock();
                        }
                        else
                            ShowSale();
                    }
                    break;
                default:
                    UpdateStock(keyboard, confirm, cancel);
                    break;
            }
        }

        private void UpdateStock(Keyboard keyboard, bool confirm, bool cancel)
        {
            if (cancel)
                Pause();
            else if (keyboard.f5Key.wasPressedThisFrame)
            {
                if (!scenes.SaveAndTravel(state.Save))
                    ShowStock();
            }
            else if (keyboard.jKey.wasPressedThisFrame && CanViewQuest)
                ShowQuest();
            else if (AtTrader && keyboard.pKey.wasPressedThisFrame && state.Home.Count > 0 &&
                state.Quests.TryGetPresentationStep(QuestId, state.Home[selection], out presentationStep))
            {
                presentedItem = state.Home[selection];
                ShowPresentation();
            }
            else if (AtTrader && keyboard.eKey.wasPressedThisFrame)
                Travel(RunLocation.Home);
            else if (!AtTrader && keyboard.tKey.wasPressedThisFrame)
                Travel(RunLocation.Trader);
            else if (!AtTrader && keyboard.eKey.wasPressedThisFrame)
                scenes.Depart();
            else if (AtTrader ? state.Home.Count > 0 : state.Home.Count > VisibleRows)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame)
                    selection--;
                else if (keyboard.downArrowKey.wasPressedThisFrame)
                    selection++;
                else if (AtTrader && confirm)
                {
                    offer = state.Trade.Quote(state.Home[selection]);
                    mode = Mode.Sale;
                    ShowSale();
                    return;
                }
                else
                    return;

                ShowStock();
            }
        }

        private bool CanViewQuest => AtTrader
            ? state.Quests.IsAvailable(QuestId) || state.Quests.TryGetProgress(QuestId, out _)
            : state.Quests.IsActive(QuestId);

        private void ShowQuest()
        {
            mode = Mode.Quest;
            var available = AtTrader && state.Quests.IsAvailable(QuestId);
            var finished = state.Quests.TryGetProgress(QuestId, out var step) && string.IsNullOrEmpty(step);
            view.Show(strings.Get("quest.title"), strings.Get(finished ? "quest.contact" : "quest.trader"),
                strings.Get(finished ? "quest.result" : "quest.condition"),
                strings.Get(available ? "quest.acceptActions" : "quest.closeActions"),
                state.Error, state.Error != null);
        }

        private void ShowPresentation()
        {
            mode = Mode.Present;
            view.Show(strings.Get("quest.title"), ItemLabel(presentedItem), strings.Get("quest.presentDetail"),
                strings.Get("quest.presentActions"), state.Error, state.Error != null);
        }

        private void Travel(RunLocation destination)
        {
            if (!scenes.SaveAndTravel(() => state.TravelBetweenSettlements(destination)))
                ShowStock();
        }

        internal void ShowStock(string error = null)
        {
            mode = AtTrader ? Mode.Trader : Mode.Home;
            var count = state.Home.Count;
            selection = Mathf.Clamp(selection, 0, Mathf.Max(0, count - 1));
            var first = Mathf.Clamp(selection - VisibleRows / 2, 0, Mathf.Max(0, count - VisibleRows));
            var rows = new StringBuilder();
            for (var i = first; i < Mathf.Min(first + VisibleRows, count); i++)
            {
                var label = ItemLabel(state.Home[i]);
                rows.AppendLine(AtTrader && i == selection ? $"<color=#F0C776>›  {label}</color>" : $"    {label}");
            }

            var details = count == 0 ? strings.Get("home.empty") : rows.ToString().TrimEnd();
            var keys = strings.Get(AtTrader ? "trader.actions" : "home.actions");
            if (CanViewQuest)
                keys = $"{strings.Get(state.Quests.IsAvailable(QuestId) ? "quest.offer" : "quest.view")}     {keys}";
            if (AtTrader && count > 0)
            {
                var itemAction = state.Quests.TryGetPresentationStep(QuestId, state.Home[selection], out _)
                    ? $"     {strings.Get("quest.present")}" : string.Empty;
                var trade = $"{strings.Get("trader.offer")}{itemAction}";
                keys = CanViewQuest ? $"{trade}\n{keys}" : $"{trade}     {keys}";
            }
            else if (!AtTrader && count > VisibleRows)
                keys = CanViewQuest ? $"{strings.Get("home.browse")}\n{keys}" : $"{strings.Get("home.browse")}     {keys}";
            error ??= state.Error;
            view.Show(Title, string.Format(strings.Get("home.balance"), state.Balance), details, keys,
                error ?? receipt, error != null);
        }

        private string ItemLabel(HomeItem item) => string.Format(strings.Get("stock.item"),
            strings.ItemName(item.key, catalog[item.key].Definition.Name), item.expedition);

        private void ShowSale()
        {
            var condition = strings.Get($"item.{offer.Item.key}.saleCondition", offer.Condition);
            view.Show(strings.Get("sale.title"), $"{offer.Price} ₽", $"{ItemLabel(offer.Item)}\n{condition}",
                strings.Get("sale.actions"), state.Error, state.Error != null);
        }

        private void Pause()
        {
            mode = Mode.Pause;
            view.Show(strings.Get("pause.title"), string.Empty,
                strings.Get(AtTrader ? "pause.trader" : "pause.home"), strings.Get("pause.actions"),
                state.Error, state.Error != null);
        }

        internal void ShowTransition(bool failed, bool departure)
        {
            if (failed && departure)
                ShowStock(strings.Get("transition.failed"));
            else
                view.Show(strings.Get(failed ? "transition.failed" : "transition.loading"),
                    string.Empty, string.Empty, failed ? strings.Get("transition.actions") : string.Empty);
        }

        private bool SaveBeforeQuit()
        {
            if (quitting || mode == Mode.Continue || !scenes.HasControl)
                return true;

            if (!state.Save())
            {
                Pause();
                return false;
            }

            quitting = true;
            return true;
        }

        private void OnDestroy() => Application.wantsToQuit -= SaveBeforeQuit;
    }
}
