using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TrashHunter
{
    public sealed class SettlementSession : MonoBehaviour
    {
        private const int VisibleRows = 5;
        private const string QuestId = QuestCatalog.TraderTurntable;
        private enum Mode { Home, Trader, Sale, Pause, Continue, Quest, Present, Bench, Repair, Backpack }

        [SerializeField] private Workbench workbench;
        [SerializeField] private CounterDisplay counter;

        private GameState state;
        private LootCatalog catalog;
        private SceneCoordinator scenes;
        private ExpeditionView view;
        private SearchHud hud;
        private Camera viewCamera;
        private HomeItem benchItem;
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
            RunLocation sceneLocation, PlayerPose spawn, SearchPlayer player, SearchHud searchHud)
        {
            state = gameState;
            catalog = lootCatalog;
            scenes = coordinator;
            location = sceneLocation;
            hud = searchHud;
            view = hud.Expedition;
            viewCamera = player.ViewCamera;
            strings = new UiStrings();
            player.Restore(spawn);
            workbench?.FrameStock(viewCamera);
            counter?.Frame(viewCamera);
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
                case Mode.Bench:
                    if (cancel)
                        ShowStock();
                    else
                        UpdateBench(keyboard);
                    break;
                case Mode.Repair:
                    if (cancel)
                        ShowSale();
                    else if (confirm && state.Balance >= state.Trade.RepairPrice(offer.Item))
                    {
                        if (state.Trade.Repair(offer.Item))
                        {
                            offer = state.Trade.Quote(offer.Item);
                            ShowSale(strings.Get("repair.returned"));
                        }
                        else
                            ShowRepair(state.Error != null);
                    }
                    break;
                case Mode.Backpack:
                    if (cancel)
                        ShowStock();
                    else if (confirm && state.Balance >= TradeCounter.BackpackPrice)
                    {
                        if (state.Trade.BuyBackpack())
                        {
                            receipt = strings.Get("backpack.bought");
                            ShowStock();
                        }
                        else
                            ShowBackpack(state.Error != null);
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
                        {
                            offer = state.Trade.Quote(offer.Item);
                            if (offer.Price > 0)
                                ShowSale();
                            else
                                ShowStock();
                        }
                    }
                    else if (keyboard.mKey.wasPressedThisFrame && state.Trade.RepairPrice(offer.Item) > 0)
                        ShowRepair();
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
            else if (AtTrader && !state.HasBackpack && keyboard.kKey.wasPressedThisFrame)
                ShowBackpack();
            else if (AtTrader && keyboard.eKey.wasPressedThisFrame)
                Travel(RunLocation.Home);
            else if (!AtTrader && keyboard.tKey.wasPressedThisFrame)
                Travel(RunLocation.Trader);
            else if (!AtTrader && keyboard.eKey.wasPressedThisFrame)
                scenes.Depart();
            else if (!AtTrader && CanUseBench && (keyboard.bKey.wasPressedThisFrame || ClickedBench()))
            {
                benchItem = state.Home[selection];
                mode = Mode.Bench;
                workbench.Focus(viewCamera);
                ShowBench();
            }
            else if (state.Home.Count > 0)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame)
                    selection--;
                else if (keyboard.downArrowKey.wasPressedThisFrame)
                    selection++;
                else if (AtTrader && (confirm || ClickedCounter()))
                {
                    offer = state.Trade.Quote(state.Home[selection]);
                    ShowSale();
                    return;
                }
                else
                    return;

                ShowStock();
            }
        }

        private bool ClickedCounter()
        {
            var mouse = Mouse.current;
            return counter != null && mouse != null && mouse.leftButton.wasPressedThisFrame &&
                counter.IsPointedAt(viewCamera, mouse.position.ReadValue());
        }

        private bool CanUseBench => workbench != null && state.Home.Count > 0 &&
            catalog[state.Home[selection].key].benchCheck != BenchCheck.None;

        private bool ClickedBench()
        {
            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame &&
                workbench.IsPointedAt(viewCamera, mouse.position.ReadValue());
        }

        private void UpdateBench(Keyboard keyboard)
        {
            var entry = catalog[benchItem.key];
            if (benchItem.IsDirty && keyboard.wKey.wasPressedThisFrame)
                ApplyWork(HomeWork.Dirty);
            else if (CanDiagnose(benchItem, entry) && keyboard.dKey.wasPressedThisFrame)
                ApplyWork(HomeWork.Diagnosed);
            else if (NeedsRepair(benchItem, entry) && state.HasRepairPart(entry.repairPart) &&
                keyboard.rKey.wasPressedThisFrame)
                ApplyWork(HomeWork.Repaired);
        }

        private void ApplyWork(HomeWork operation)
        {
            state.TreatHomeItem(benchItem, operation);
            ShowBench();
        }

        private void ShowBench()
        {
            if (!state.TryGetHomeItem(benchItem, out benchItem))
            {
                ShowStock();
                return;
            }

            var entry = catalog[benchItem.key];
            workbench.Show(benchItem);
            view.Hide();
            hud.Inspection.Show(ItemLabel(benchItem), Describe(benchItem, entry, entry.condition, true), twoLines: true);
            var keys = new StringBuilder();
            if (benchItem.IsDirty)
                keys.AppendLine(strings.Get("bench.wash"));
            if (CanDiagnose(benchItem, entry))
                keys.AppendLine(strings.Get("bench.diagnose"));
            if (NeedsRepair(benchItem, entry))
                keys.AppendLine(strings.Get(state.HasRepairPart(entry.repairPart) ? "bench.repair" : "bench.missingPart"));
            keys.Append(strings.Get("bench.close"));
            hud.Show(string.Empty, keys.ToString(), false);
            hud.ShowError(state.Error == null ? null : strings.Get("bench.saveFailed"));
        }

        private static bool CanDiagnose(HomeItem item, LootCatalog.Entry entry) =>
            !item.IsDiagnosed && entry.benchCheck is BenchCheck.Working or BenchCheck.LampHinge;

        private static bool NeedsRepair(HomeItem item, LootCatalog.Entry entry) =>
            item.IsDiagnosed && !item.IsRepaired && entry.benchCheck == BenchCheck.LampHinge;

        private string Describe(HomeItem item, LootCatalog.Entry entry, string fallback, bool atBench)
        {
            if (item.IsRepaired && entry.benchCheck == BenchCheck.Specialist)
                return strings.Get("repair.condition");

            var visible = item.IsRepaired ? strings.Get("bench.repaired", fallback)
                : strings.Get($"item.{item.key}.{(atBench ? "condition" : "saleCondition")}", fallback);
            if (item.IsDirty)
                return $"{visible}\n{strings.Get(item.IsDiagnosed ? "bench.dirtyWorking" : "bench.dirty")}";
            if (item.IsDiagnosed)
            {
                var status = NeedsRepair(item, entry) ? "bench.broken" : "bench.working";
                return $"{visible}\n{strings.Get(status)}";
            }

            return atBench && entry.benchCheck == BenchCheck.Specialist
                ? $"{visible}\n{strings.Get("bench.specialist")}" : visible;
        }

        private bool CanViewQuest => AtTrader
            ? state.Quests.IsAvailable(QuestId) || state.Quests.TryGetProgress(QuestId, out _)
            : state.Quests.IsActive(QuestId);

        private void ShowQuest()
        {
            mode = Mode.Quest;
            counter?.Hide();
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
            counter?.Show(presentedItem);
            view.Show(strings.Get("quest.title"), ItemLabel(presentedItem), strings.Get("quest.presentDetail"),
                strings.Get("quest.presentActions"), state.Error, state.Error != null, homeStock: counter != null);
        }

        private void Travel(RunLocation destination)
        {
            if (!scenes.SaveAndTravel(() => state.TravelBetweenSettlements(destination)))
                ShowStock();
        }

        internal void ShowStock(string error = null)
        {
            CloseBench();
            counter?.Hide();
            mode = AtTrader ? Mode.Trader : Mode.Home;
            var count = state.Home.Count;
            selection = Mathf.Clamp(selection, 0, Mathf.Max(0, count - 1));
            var first = Mathf.Clamp(selection - VisibleRows / 2, 0, Mathf.Max(0, count - VisibleRows));
            var rows = new StringBuilder();
            for (var i = first; i < Mathf.Min(first + VisibleRows, count); i++)
            {
                var label = StockLabel(state.Home[i]);
                rows.AppendLine(i == selection ? $"<color=#F0C776>›  {label}</color>" : $"    {label}");
            }

            var details = count == 0 ? strings.Get("home.empty") : rows.ToString().TrimEnd();
            var keys = strings.Get(AtTrader ? "trader.actions" : "home.actions");
            if (AtTrader && !state.HasBackpack)
                keys = $"{strings.Get("backpack.open")}     {keys}";
            if (CanViewQuest)
                keys = $"{strings.Get(state.Quests.IsAvailable(QuestId) ? "quest.offer" : "quest.view")}     {keys}";
            if (AtTrader && count > 0)
            {
                var itemAction = state.Quests.TryGetPresentationStep(QuestId, state.Home[selection], out _)
                    ? $"     {strings.Get("quest.present")}" : string.Empty;
                var trade = $"{strings.Get("trader.offer")}{itemAction}";
                keys = $"{trade}\n{keys}";
            }
            else if (!AtTrader && count > 0)
            {
                var stockActions = count > 1 ? strings.Get("home.browse") : string.Empty;
                if (CanUseBench)
                    stockActions = string.IsNullOrEmpty(stockActions) ? strings.Get("bench.open")
                        : $"{stockActions}     {strings.Get("bench.open")}";
                if (!string.IsNullOrEmpty(stockActions))
                    keys = $"{stockActions}\n{keys}";
            }
            error ??= state.Error;
            view.Show(Title, BalanceLabel(), details, keys,
                error ?? receipt, error != null, homeStock: workbench != null || counter != null);
        }

        private string StockLabel(HomeItem item)
        {
            var label = ItemLabel(item);
            var check = catalog[item.key].benchCheck;
            var feature = item.IsDirty ? "bench.stockDirty" : item.IsRepaired
                ? (check == BenchCheck.Specialist ? "repair.stockRepaired" : "bench.stockRepaired")
                : check == BenchCheck.LampHinge ? "bench.stockDamaged" : null;
            return feature == null ? label : $"{label} · {strings.Get(feature)}";
        }

        private string ItemLabel(HomeItem item) => string.Format(strings.Get("stock.item"),
            strings.ItemName(item.key, catalog[item.key].Definition.Name), item.expedition);

        private void ShowSale(string message = null)
        {
            mode = Mode.Sale;
            counter?.Show(offer.Item);
            var condition = Describe(offer.Item, catalog[offer.Item.key], offer.Condition, false);
            var keys = strings.Get("sale.actions");
            if (state.Trade.RepairPrice(offer.Item) > 0)
                keys = $"{strings.Get("repair.open")}     {keys}";
            view.Show(strings.Get("sale.title"), $"{offer.Price} ₽", $"{ItemLabel(offer.Item)}\n{condition}",
                keys, state.Error ?? message, state.Error != null, homeStock: counter != null);
        }

        private void ShowRepair(bool saveFailed = false)
        {
            var price = state.Trade.RepairPrice(offer.Item);
            if (price <= 0)
            {
                ShowStock();
                return;
            }

            mode = Mode.Repair;
            var enough = state.Balance >= price;
            var message = saveFailed ? strings.Get("trade.saveFailed") :
                enough ? null : strings.Get("trade.insufficient");
            view.Show(strings.Get("repair.title"), $"{price} ₽  ·  {BalanceLabel()}",
                $"{ItemLabel(offer.Item)}\n{strings.Get("repair.detail")}",
                strings.Get(enough ? "repair.actions" : "trade.cancel"), message,
                message != null, homeStock: counter != null);
        }

        private void ShowBackpack(bool saveFailed = false)
        {
            mode = Mode.Backpack;
            var enough = state.Balance >= TradeCounter.BackpackPrice;
            var message = saveFailed ? strings.Get("trade.saveFailed") :
                enough ? null : strings.Get("trade.insufficient");
            view.Show(strings.Get("bag.backpack"), $"{TradeCounter.BackpackPrice} ₽  ·  {BalanceLabel()}",
                strings.Get("backpack.detail"), strings.Get(enough ? "backpack.actions" : "trade.cancel"),
                message, message != null, homeStock: counter != null);
        }

        private string BalanceLabel() => string.Format(strings.Get("home.balance"), state.Balance);

        private void CloseBench()
        {
            if (mode != Mode.Bench)
                return;

            workbench.Close();
            hud.Inspection.Hide();
            hud.Show(string.Empty, string.Empty, false);
            hud.ShowError(null);
        }

        private void Pause()
        {
            CloseBench();
            counter?.Hide();
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
