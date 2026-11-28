using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrashHunter;
using Unity.Pipeline.Scripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;

public static class VerifyTradeAndBackpack
{
    private const string SlotVariable = "TRASH_HUNTER_EDITOR_SAVE_DIR";
    private const string LocaleVariable = "TRASH_HUNTER_LOCALE";
    private const string RestoreKey = "TrashHunter.I220.Environment";
    private const string Quest = "trader-turntable";
    private const string Repairer = "repairer";
    private const string Turntable = "turntable-rp07";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly HomeItem Lamp = new() { expedition = 1, key = "lamp-intact" };
    private static readonly HomeItem Radio = new() { expedition = 1, key = "radio-a" };
    private static readonly HomeItem Player = new() { expedition = 2, key = Turntable };
    private static readonly HomeItem Knob = new() { expedition = 1, key = "dial-a" };
    private static readonly HomeItem OtherLamp = new() { expedition = 2, key = "lamp-intact" };

    public static object Prepare(string language)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || language is not ("ru" or "en"))
            throw new InvalidOperationException("Prepare a RU or EN slot in Edit Mode.");
        if (SessionState.GetString(RestoreKey, "") == "")
            SessionState.SetString(RestoreKey, JsonConvert.SerializeObject(new[]
            {
                Environment.GetEnvironmentVariable(SlotVariable),
                Environment.GetEnvironmentVariable(LocaleVariable)
            }));

        var folder = Path.GetFullPath($"Logs/I-220/{language}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var slot = Read("AgentScripts/Fixtures/i110-pile-measurement.json");
        slot["format"] = 4;
        slot["location"] = (int)RunLocation.Trader;
        slot["expedition"] = 2;
        slot["hasBag"] = true;
        slot["world"] = new JArray();
        slot["player"] = new JObject();
        slot["held"] = "";
        slot["heldRotated"] = false;
        slot["heldInWorld"] = false;
        slot["cash"] = 0;
        slot["hasSold"] = false;
        slot["quests"] = new JArray();
        slot["contacts"] = new JArray();
        slot["home"] = new JArray(new[] { Lamp, Radio, Player, Knob, OtherLamp }.Select(item =>
            new JObject { ["expedition"] = item.expedition, ["key"] = item.key }));
        var payload = slot.ToString(Formatting.None);
        using var hash = SHA256.Create();
        File.WriteAllText(Path.Combine(folder, "slot.json"), new JObject
        {
            ["payload"] = payload,
            ["checksum"] = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(payload)))
        }.ToString(Formatting.None));
        Environment.SetEnvironmentVariable(SlotVariable, folder);
        Environment.SetEnvironmentVariable(LocaleVariable, language);
        return new { folder, language };
    }

    public static void Restore()
    {
        var previous = SessionState.GetString(RestoreKey, "");
        if (previous == "")
            return;
        var values = JsonConvert.DeserializeObject<string[]>(previous);
        Environment.SetEnvironmentVariable(SlotVariable, values[0]);
        Environment.SetEnvironmentVariable(LocaleVariable, values[1]);
        SessionState.EraseString(RestoreKey);
    }

    public static async Task<object> Run(string phase)
    {
        var folder = Environment.GetEnvironmentVariable(SlotVariable);
        var testRoot = Path.GetFullPath("Logs/I-220") + Path.DirectorySeparatorChar;
        if (!Application.isPlaying || folder == null || !folder.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Start Play Mode with an isolated I-220 slot.");

        await Wait.Until(() => Object.FindAnyObjectByType<SearchHud>() != null, because: "mounted chapter scene");
        await Frames();
        var state = Object.FindAnyObjectByType<SearchSceneScope>().Container.Resolve<GameState>();
        var slot = Path.Combine(folder, "slot.json");
        var pending = Path.Combine(folder, "slot.pending");
        var en = Environment.GetEnvironmentVariable(LocaleVariable) == "en";
        var checks = new List<string>();
        var settings = InputSystem.settings;
        var background = settings.backgroundBehavior;
        var inputBehavior = settings.editorInputBehaviorInPlayMode;
        var previousKeyboard = Keyboard.current;
        var previousMouse = Mouse.current;
        var keys = InputSystem.AddDevice<Keyboard>();
        var mouse = InputSystem.AddDevice<Mouse>();
        keys.MakeCurrent();
        mouse.MakeCurrent();
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

        try
        {
            await Frames();
            switch (phase)
            {
                case "trade":
                    Check(state.Location == RunLocation.Trader && Mode() == "Continue" &&
                        !state.HasBackpack && state.Bag.Height == 2 && state.Balance == 0 && state.Home.Count == 5,
                        "Format 4 restores stock, cash and the waist bag.");
                    await Tap(Key.Enter);
                    var startingBytes = File.ReadAllText(slot);
                    Check(state.Trade.RepairPrice(Player) == 0 && !state.Trade.Repair(Player),
                        "Repair is unavailable until the quest opens the contact.");
                    await Tap(Key.K);
                    Check(Mode() == "Backpack" && Text("Summary").Contains("45 ₽") &&
                        Text("Feedback").Contains(en ? "Not enough cash" : "Не хватает денег"),
                        "Backpack is offered from the start and states the price and shortage.");
                    Fits();
                    await Tap(Key.Enter);
                    Check(File.ReadAllText(slot) == startingBytes && !state.HasBackpack, "Unaffordable purchase makes no write.");
                    await Tap(Key.Escape);

                    await ClickCounter();
                    Check(Mode() == "Sale" && Displayed() == Lamp.key && Text("Summary") == "42 ₽",
                        "Clicking the physical counter displays the selected lamp with its price.");
                    Fits();
                    await Tap(Key.Escape);
                    Check(Displayed() == null && File.ReadAllText(slot) == startingBytes, "Cancel returns the lamp unchanged.");
                    await Sell(Lamp, 42);
                    await Sell(Radio, 26);
                    Check(state.Balance == 68 && state.Home.Contains(OtherLamp) && state.Quests.IsAvailable(Quest),
                        "First sales fund either purchase and open the request.");
                    await Tap(Key.J);
                    await Tap(Key.Enter);
                    Check(state.Quests.TryGetProgress(Quest, out var step) && step == "show-turntable", "The trader offers the existing RP-07 quest.");
                    await Tap(Key.Escape);
                    await Select(Player);
                    Fits();
                    await Tap(Key.P);
                    Check(Mode() == "Present" && Displayed() == Turntable, "Presentation shows the selected physical RP-07.");
                    Fits();
                    await Tap(Key.Enter);
                    Check(state.Quests.HasContact(Repairer) && state.Home.Contains(Player) &&
                        state.Trade.RepairPrice(Player) == 25 && state.Trade.RepairPrice(OtherLamp) == 0 && state.Balance == 68,
                        "Presentation opens one contact and keeps the same RP-07, with a 25 ₽ repair offer.");
                    await Tap(Key.Escape);


                    if (!en)
                    {
                        await BuyBackpack(23);
                        await RepairQuote();
                        var withoutFunds = File.ReadAllText(slot);
                        Check(Text("Feedback").Contains("Не хватает денег"), "Repair reports a cash shortage after buying the bag.");
                        await Tap(Key.Enter);
                        Check(File.ReadAllText(slot) == withoutFunds && state.Quote(Player).Price == 30,
                            "Unaffordable repair leaves the same condition and cash.");
                        await Tap(Key.Escape);
                        await Tap(Key.Escape);
                        await Sell(Knob, 4);
                    }

                    var stale = state.Quote(Player);
                    await RepairQuote();
                    Check(Text("Details").Contains(en ? "One speed will still fail" : "останется неисправным"),
                        "The quote gives the repair scope and the remaining fault.");
                    Fits();
                    var beforeRepair = File.ReadAllText(slot);
                    var beforeStock = state.Home.ToArray();
                    var beforeCash = state.Balance;
                    await Tap(Key.Escape);
                    Check(Mode() == "Sale" && File.ReadAllText(slot) == beforeRepair, "Canceling the repair keeps the offer and slot.");
                    await Tap(Key.M);
                    using (new FileStream(pending, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                        await Tap(Key.Enter);
                    Check(File.ReadAllText(slot) == beforeRepair && state.Error != null && state.Home.SequenceEqual(beforeStock) &&
                        state.Balance == beforeCash && state.Quote(Player).Price == 30,
                        "Repair save failure keeps the money, the turntable, and its old offer.");
                    await Tap(Key.Enter);
                    Check(Mode() == "Sale" && state.Error == null && state.Balance == beforeCash - 25 &&
                        state.Home.SequenceEqual(beforeStock) && state.Quote(Player).Price == 72 && Displayed() == Turntable,
                        "Successful repair charges once, returns the same instance and offers 72 ₽.");
                    Check(Text("Details").Contains(en ? "One speed still fails" : "режим скорости неисправен") &&
                        Text("Feedback").Contains(en ? "Turntable returned" : "Проигрыватель возвращён"),
                        "The result shows the remaining fault and confirms return.");
                    Fits();
                    var afterRepair = File.ReadAllText(slot);
                    Check(!state.Trade.Repair(Player) && !state.Sell(stale) && File.ReadAllText(slot) == afterRepair,
                        "Repeat repair and stale sale quote cannot change the saved run.");
                    if (en)
                    {
                        await Tap(Key.Escape);
                        await Sell(Knob, 4);
                        await BuyBackpack(2);
                    }
                    Check(state.HasBackpack && state.Balance == 2 && state.Home.Contains(Player),
                        "Both spending orders end with the repaired turntable, backpack and 2 ₽.");
                    break;

                case "resume":
                    Check(Mode() == "Continue" && state.Balance == 2 && state.HasBackpack &&
                        state.Home.SequenceEqual(new[] { Player, OtherLamp }) && state.Quote(Player).Price == 72 &&
                        state.Quests.HasContact(Repairer) && (int)Read(slot)["format"] == 5,
                        "A separate Play session restores format 5, cash, ownership and the same repaired stock.");
                    await Tap(Key.Enter);
                    Check(!Text("Actions").Contains("K  "), "The trader no longer offers the bought backpack.");
                    await Sell(Player, 72);
                    Check(state.Balance == 74 && state.Home.SequenceEqual(new[] { OtherLamp }),
                        "The repaired item can be sold once after restart.");
                    await Tap(Key.E);
                    await At(RunLocation.Home, "Home");
                    await Tap(Key.E);
                    await At(RunLocation.Search, "ContainerSearch");
                    Check(state.Expedition == 3 && state.HasBag && state.Bag.Height == 3 && state.Bag.Count == 0,
                        "A new expedition starts with an empty 3 × 3 backpack.");
                    break;

                case "pack":
                    var item = await AimAtFind();
                    await MouseAt(Vector2.zero, 1);
                    Check(state.HeldItemId == item.Id, "Normal LMB takes a generated find in hand.");
                    await Tap(Key.Tab);
                    var bag = Hud().Bag;
                    var grid = Field<RectTransform>(bag, "grid");
                    var third = RectTransformUtility.WorldToScreenPoint(null, grid.TransformPoint(
                        grid.rect.xMin + BagView.CellSize / 2f, grid.rect.yMax - 2.5f * BagView.CellSize, 0f));
                    await MouseAt(third, 0);
                    await MouseAt(third, 1);
                    await MouseAt(third, 0);
                    Check(bag.CellAt(third) == new Vector2Int(0, 2) && grid.rect.height == 294 &&
                        grid.Find("Cell0-2").gameObject.activeSelf && state.HeldItemId == null &&
                        state.Bag.TryGetPlacement(item.Id, out var placement) && placement.Row == 2 &&
                        Read(slot)["world"].Any(row => (string)row["key"] == item.Id && (bool)row["packed"] && (int)row["row"] == 2),
                        "The visible third row accepts the same find and saves its placement.");
                    Check(bag.GetComponentsInChildren<Text>().Any(text => text.name == "BagTitle" &&
                        text.text == (en ? "Backpack" : "Рюкзак")), "The inventory names the purchased carry item.");
                    Fits();
                    break;

                case "reload":
                    Check(state.Location == RunLocation.Search && state.Expedition == 3 && state.Balance == 74 &&
                        state.Home.SequenceEqual(new[] { OtherLamp }) && state.Bag.Height == 3 &&
                        Read(slot)["world"].Any(row => (bool)row["packed"] &&
                            state.Bag.TryGetPlacement((string)row["key"], out var restored) && restored.Row == 2),
                        "Cold resume in the yard restores cash, stock, backpack and a third-row item.");
                    await Tap(Key.Enter);
                    await Tap(Key.Tab);
                    Check(Hud().Bag.gameObject.activeSelf && Field<RectTransform>(Hud().Bag, "grid").rect.height == 294,
                        "The resumed run reopens the expanded grid.");
                    Fits();
                    break;

                default:
                    throw new ArgumentException("Unknown phase.", nameof(phase));
            }

            return new { phase, locale = en ? "en" : "ru", screen = $"{Screen.width}x{Screen.height}", count = checks.Count, checks };
        }
        finally
        {
            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(keys);
            if (previousMouse?.added == true) previousMouse.MakeCurrent();
            if (previousKeyboard?.added == true) previousKeyboard.MakeCurrent();
            settings.backgroundBehavior = background;
            settings.editorInputBehaviorInPlayMode = inputBehavior;
        }

        async Task BuyBackpack(int expectedBalance)
        {
            await Tap(Key.K);
            Check(Mode() == "Backpack" && Text("Summary").Contains("45 ₽") && Text("Details").Contains("3 × 3"),
                "Backpack quote shows the price and the future grid.");
            Fits();
            var before = File.ReadAllText(slot);
            await Tap(Key.Escape);
            Check(File.ReadAllText(slot) == before && !state.HasBackpack, "Canceling a backpack quote makes no write.");
            await Tap(Key.K);
            using (new FileStream(pending, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                await Tap(Key.Enter);
            Check(File.ReadAllText(slot) == before && !state.HasBackpack && state.Bag.Height == 2 && state.Error != null,
                "A failed purchase keeps the old bag and cash.");
            await Tap(Key.Enter);
            Check(state.HasBackpack && state.Balance == expectedBalance && (bool)Read(slot)["hasBackpack"],
                "The successful purchase publishes cash and ownership together.");
            var bought = File.ReadAllText(slot);
            Check(!state.Trade.BuyBackpack() && File.ReadAllText(slot) == bought, "The backpack can only be bought once.");
        }

        async Task RepairQuote()
        {
            await Select(Player);
            await ClickCounter();
            Check(Mode() == "Sale" && Displayed() == Turntable && Text("Summary") == "30 ₽" &&
                Text("Actions").Contains("M  "), $"The unrepaired RP-07 has a sale price and an unlocked repair action. " +
                $"Mode={Mode()}, displayed={Displayed()}, summary={Text("Summary")}, actions={Text("Actions")}");
            await Tap(Key.M);
            Check(Mode() == "Repair" && Text("Summary").Contains("25 ₽"), "Opening the repair displays its price.");
        }

        async Task ClickCounter()
        {
            var counter = Object.FindAnyObjectByType<CounterDisplay>();
            var camera = Object.FindAnyObjectByType<SearchPlayer>().ViewCamera;
            var pointer = camera.WorldToScreenPoint(Field<Collider>(counter, "surface").bounds.center);
            await MouseAt(pointer, 1);
            await MouseAt(pointer, 0);
        }

        async Task Sell(HomeItem item, int price)
        {
            await Select(item);
            await Tap(Key.Enter);
            var offer = state.Quote(item);
            var balance = state.Balance;
            Check(Mode() == "Sale" && offer.Price == price && Text("Summary") == $"{price} ₽" &&
                Displayed() == item.key, "The counter shows the selected " + item.key + ".");
            await Tap(Key.Enter);
            Check(Mode() == "Trader" && Displayed() == null && state.Balance == balance + price &&
                !state.Home.Contains(item) && !state.Sell(offer), "Sale pays once and removes only " + item.key + ".");
        }

        async Task Select(HomeItem item)
        {
            var session = Object.FindAnyObjectByType<SettlementSession>();
            var delta = state.Home.ToList().IndexOf(item) - Field<int>(session, "selection");
            for (var i = 0; i < Math.Abs(delta); i++)
                await Tap(delta > 0 ? Key.DownArrow : Key.UpArrow);
        }

        async Task<SearchItem> AimAtFind()
        {
            var search = Object.FindAnyObjectByType<SearchSession>();
            var player = Object.FindAnyObjectByType<SearchPlayer>();
            var floor = Field<BoxCollider>(search, "containerFloor").bounds;
            var height = player.ViewCamera.transform.position.y - player.transform.position.y;
            var marker = new GameObject("Verification position");
            Cursor.lockState = CursorLockMode.Locked;
            try
            {
                var candidates = Object.FindObjectsByType<SearchItem>().Where(item => item.Id is "radio-a" or "radio-b" or
                    "radio-c" or "dial-a" or "dial-b" or "plug-b" or "bell-a" or "bell-b" or "fuse-b" or "adapter-b");
                foreach (var item in candidates.OrderBy(item => item.Id))
                foreach (var direction in new[] { Vector3.back, Vector3.forward, Vector3.left, Vector3.right })
                {
                    var target = item.GetComponent<BoxCollider>().bounds.center;
                    var feet = new Vector3(target.x, floor.max.y + .035f, target.z) + direction * .85f;
                    if (feet.x < floor.min.x + .4f || feet.x > floor.max.x - .4f ||
                        feet.z < floor.min.z + .4f || feet.z > floor.max.z - .4f)
                        continue;
                    marker.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(target - feet - Vector3.up * height));
                    player.Place(marker.transform);
                    await Frames();
                    if (Field<SearchItem>(search, "aimedItem") == item)
                        return item;
                }
                throw new InvalidOperationException("No generated one-row item is reachable for the grid check.");
            }
            finally
            {
                Object.Destroy(marker);
            }
        }

        async Task At(RunLocation location, string scene)
        {
            await Wait.Until(() => state.Location == location && SceneManager.GetActiveScene().name == scene &&
                Object.FindObjectsByType<SearchHud>().Length == 1, because: scene);
            await Frames();
        }

        async Task Tap(Key key)
        {
            InputSystem.QueueStateEvent(keys, new KeyboardState(key));
            await Frames();
            InputSystem.QueueStateEvent(keys, new KeyboardState());
            await Frames();
        }

        async Task MouseAt(Vector2 pointer, ushort buttons)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer, buttons = buttons });
            await Frames();
        }

        string Mode() => Field<object>(Object.FindAnyObjectByType<SettlementSession>(), "mode").ToString();
        string Displayed() => Field<GameObject>(Object.FindAnyObjectByType<CounterDisplay>(), "displayed")?.name;
        string Text(string name) => Hud().Expedition.GetComponentsInChildren<Text>().Single(text => text.name == name).text;

        void Fits()
        {
            Canvas.ForceUpdateCanvases();
            var texts = Hud().GetComponentsInChildren<Text>().Where(text => text.enabled && !string.IsNullOrEmpty(text.text)).ToArray();
            Check(texts.Length > 0 && texts.All(text => text.preferredWidth <= text.rectTransform.rect.width + 1f &&
                text.preferredHeight <= text.rectTransform.rect.height + 1f), "Visible text fits. " +
                string.Join(", ", texts.Select(text => $"{text.name}={text.preferredWidth:0}×{text.preferredHeight:0}/{text.rectTransform.rect.size}")));
        }

        void Check(bool ok, string message)
        {
            if (!ok)
                throw new InvalidOperationException(string.Join("\n", checks.Append("FAILED: " + message)));
            checks.Add(message);
        }
    }

    private static SearchHud Hud() => Object.FindObjectsByType<SearchHud>().Single();
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
    private static JObject Read(string path) => JObject.Parse((string)JObject.Parse(File.ReadAllText(path))["payload"]);

    private static async Task Frames()
    {
        var next = Time.frameCount + 3;
        await Wait.Until(() => Time.frameCount >= next, because: "advancing frames");
    }
}
