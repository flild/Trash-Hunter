using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

public static class VerifyChapterEvidence
{
    private const string SlotVariable = "TRASH_HUNTER_EDITOR_SAVE_DIR";
    private const string LocaleVariable = "TRASH_HUNTER_LOCALE";
    private const string RestoreKey = "TrashHunter.I230.Environment";
    private const string Tag = "home-tag";
    private const string Recorder = "batch-recorder";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    public static object Prepare(string language)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || language is not ("ru" or "en"))
            throw new InvalidOperationException("Prepare an isolated slot in Edit Mode.");
        if (SessionState.GetString(RestoreKey, "") == "")
            SessionState.SetString(RestoreKey, JsonConvert.SerializeObject(new[]
            {
                Environment.GetEnvironmentVariable(SlotVariable),
                Environment.GetEnvironmentVariable(LocaleVariable)
            }));

        var folder = Path.GetFullPath($"Logs/I-230/{language}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
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
        if (!Application.isPlaying || folder == null ||
            !folder.StartsWith(Path.GetFullPath("Logs/I-230") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Start Play Mode with an isolated I-230 slot.");

        await Wait.Until(() => Object.FindObjectsByType<SearchHud>().Length == 1, because: "mounted chapter scene");
        await Frames();
        var state = Object.FindAnyObjectByType<SearchSceneScope>().Container.Resolve<GameState>();
        var file = Path.Combine(folder, "slot.json");
        var pending = Path.Combine(folder, "slot.pending");
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
                case "first":
                    Check(state.Expedition == 1 && !state.HasBag && !state.TryGetPose(Tag, out _) &&
                        !state.TryGetPose(Recorder, out _), "Fresh run starts in the first yard without either clue.");
                    var starter = Field<Collider>(Search(), "starterBag");
                    var pos = Player().transform.position;
                    PositionPlayer(pos, starter.bounds.center);
                    Cursor.lockState = CursorLockMode.Locked;
                    await Tap(Key.E);
                    Check(state.HasBag, "Looking at the starter bag and pressing E equips it.");
                    await ClimbIn();
                    await Take("lamp-intact");
                    await Pack(0, 0);
                    await Take("radio-a");
                    await Pack(2, 0);
                    await GoHome();
                    Check(state.Home.Count == 2 && state.Home[0].key == "lamp-intact", "The two selected finds return home.");
                    await Tap(Key.B);
                    await Tap(Key.W);
                    await Tap(Key.D);
                    await Tap(Key.Escape);
                    var lamp = state.Home[0];
                    Check(state.Quote(lamp).Price == 42, "The workbench washes and diagnoses the same SV-04.");
                    await Tap(Key.V);
                    Check(SceneManager.GetActiveScene().name == "Home", "Workshop yard is initially locked.");
                    await GoTrader();
                    await Sell(lamp, 42);
                    await Sell(state.Home[0], 26);
                    await Tap(Key.K);
                    await Tap(Key.Enter);
                    Check(state.HasBackpack && state.Balance == 23, "The first sales buy the backpack for 45 ₽.");
                    await BackHome();
                    await Depart();
                    Check(state.Expedition == 2 && state.Bag.Height == 3 && WorldCount(Tag) == 1 && !HasStory(3),
                        "The first post-sale expedition contains one tag and uses the larger bag.");
                    await ClimbIn();
                    await Take(Tag, inspect: true);
                    Check(HasStory(1) && !HasStory(2) && InspectionText().Any(t => t.Contains("6")),
                        "The first face alone records the address and the owner's note.");
                    Fits();
                    break;
                case "lead":
                    Check(state.Expedition == 2 && state.HeldItemId == Tag && WorldCount(Tag) == 1 &&
                        HasStory(1) && !HasStory(2), "A new Play session restores the held tag and exactly one fact.");
                    await Tap(Key.Enter);
                    await GoHome();
                    Check(state.Home.Count == 0 && HasStory(1), "Leaving without the tag keeps the address and loses the item.");
                    await GoTrader();
                    await Tap(Key.G);
                    Check(Mode() == "Trader" && !HasStory(4), "The trader cannot use half the evidence.");
                    await BackHome();
                    await Depart();
                    Check(state.Expedition == 3 && WorldCount(Tag) == 1, "One replacement tag appears on the next eligible expedition.");
                    await ClimbIn();
                    await Take(Tag, inspect: true);
                    var before = File.ReadAllText(file);
                    using (new FileStream(pending, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                        await Tap(Key.R);
                    Check(File.ReadAllText(file) == before && !HasStory(2) && state.Error != null,
                        "A failed save while turning over the tag leaves the batch unread.");
                    await Tap(Key.R);
                    await Tap(Key.R);
                    Check(HasStory(3) && InspectionText().Any(t => t.Contains("17-Б")),
                        "Turning the tag over records the batch separately.");
                    Fits();
                    before = File.ReadAllText(file);
                    await Tap(Key.R);
                    await Tap(Key.R);
                    Check(File.ReadAllText(file) == before, "Re-reading either side does not write or duplicate the observations.");
                    await Tap(Key.Q);
                    await GoHome();
                    await GoTrader();
                    await Tap(Key.G);
                    Check(Mode() == "Lead" && ExpeditionText("Details").Contains("17-Б") && state.Home.Count == 0,
                        "The trader can compare recorded facts after the player leaves the tag behind.");
                    await Tap(Key.Enter);
                    Check(HasStory(7) && ExpeditionText("Details").Contains(IsEnglish() ? "workshop yard" : "двор мастерских"),
                        "Showing the address and batch opens the workshop yard.");
                    Fits();
                    await Tap(Key.Enter);
                    await BackHome();
                    before = File.ReadAllText(file);
                    Check(ExpeditionText("Actions").Contains("V"), "The shelter offers the new destination.");
                    using (new FileStream(pending, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        await Tap(Key.V);
                        await Wait.Until(() => Mode() == "Home" && state.Error != null, because: "rejected departure");
                    }
                    Check(state.Location == RunLocation.Home && File.ReadAllText(file) == before,
                        "A failed departure leaves the original location and slot intact.");
                    await Depart(second: true);
                    Check((bool)Saved()["secondYard"] && state.Expedition == 4 && WorldCount(Recorder) == 1 &&
                        WorldCount("lamp-intact") == 0 && WorldCount("lamp-cracked") == 0 && WorldCount(Tag) == 0 &&
                        WorldCount("parcel-a") == 1 && state.HasBackpack && state.Balance == 23,
                        "Workshop yard saves its own location, assortment, inventory size and cash.");
                    var entry = Field<Transform>(Search(), "yardEntry");
                    var rim = Field<Transform>(Search(), "outside");
                    Check(Vector3.Distance(entry.position, rim.position) > 4 &&
                        Vector3.Distance(Player().transform.position, entry.position) < .35f,
                        "The real arrival point is at the workshop gate rather than the container rim.");
                    await WalkTo(new Vector3(3.6f, 0, -.95f));
                    await WalkTo(rim.position);
                    await Tap(Key.E);
                    Check(Vector3.Distance(Player().transform.position, Field<Transform>(Search(), "inside").position) < .5f,
                        "Walking around the stack from the gate reaches the playable rim crossing.");
                    await Take(Recorder, inspect: true);
                    Check(HasStory(15) && InspectionText().Any(t => t.Contains("17-Б")) &&
                        InspectionText().Any(t => t.Contains(IsEnglish() ? "jammed" : "заклинившей")),
                        "Inspection matches the recorder batch and identifies the jammed cassette door.");
                    Fits();
                    break;
                case "resume":
                    Check(SceneManager.GetActiveScene().name == "SecondYard" &&
                        Mode() == "Continue" && state.HeldItemId == Recorder && WorldCount(Recorder) == 1 &&
                        HasStory(15) && state.Balance == 23 && state.Bag.Height == 3 && (int)Saved()["format"] == 6,
                        "A new Play session loads the workshop yard, held recorder, facts, money and backpack.");
                    await Tap(Key.Enter);
                    Check(Mode() == "Search" && Hud().gameObject.activeInHierarchy, "Continue hands control back in the workshop yard.");
                    await Tap(Key.Q);
                    Check(InspectionText().Any(t => t.Contains("17-Б")), "The same recorder remains inspectable after reload.");
                    Fits();
                    await Tap(Key.Q);
                    await Pack(0, 0);
                    await GoHome();
                    await GoTrader();
                    Fits();
                    await Tap(Key.Enter);
                    Check(Mode() == "Sale" && ExpeditionText("Details").Contains(IsEnglish() ? "recording" : "запись") &&
                        ExpeditionText("Summary") == "", "The counter explains the sale restriction and suppresses a price.");
                    Fits();
                    var recorder = state.Home.Single();
                    before = File.ReadAllText(file);
                    await Tap(Key.Enter);
                    Check(!state.Sell(state.Quote(recorder)) && state.Home.Contains(recorder) &&
                        File.ReadAllText(file) == before, "The unread recording blocks sale at both entry points.");
                    await Tap(Key.Q);
                    Check(Mode() == "Evidence" && InspectionText().Any(t => t.Contains("17-Б")),
                        "The stored physical recorder can be re-inspected at the counter.");
                    await Tap(Key.Q);
                    await BackHome();
                    await Depart(second: true);
                    Check(WorldCount(Recorder) == 0 && state.Home.Contains(recorder),
                        "A recorder kept in stock suppresses a duplicate on the next visit.");
                    break;
                default:
                    throw new ArgumentException("Unknown phase.", nameof(phase));
            }
            return new { phase, screen = $"{Screen.width}x{Screen.height}", count = checks.Count, checks };
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

        bool IsEnglish() => Environment.GetEnvironmentVariable(LocaleVariable) == "en";
        JObject Saved() => JObject.Parse((string)JObject.Parse(File.ReadAllText(file))["payload"]);
        bool HasStory(int mask) => ((int)Saved()["story"] & mask) == mask;
        int WorldCount(string id) => Saved()["world"].Count(row => (string)row["key"] == id);

        async Task Sell(HomeItem item, int price)
        {
            Check(state.Home[0].Equals(item) && state.Quote(item).Price == price, $"The trader quotes {price} ₽ for {item.key}.");
            var cash = state.Balance;
            await Tap(Key.Enter);
            await Tap(Key.Enter);
            Check(!state.Home.Contains(item) && state.Balance == cash + price, $"The sale of {item.key} commits.");
        }

        async Task At(string name)
        {
            await Wait.Until(() => SceneManager.GetActiveScene().name == name &&
                Object.FindObjectsByType<SearchHud>().Length == 1, because: name);
            await Frames();
        }

        async Task GoTrader()
        {
            await Tap(Key.T);
            await At("Trader");
        }

        async Task BackHome()
        {
            await Tap(Key.E);
            await At("Home");
        }

        async Task Depart(bool second = false)
        {
            await Tap(second ? Key.V : Key.E);
            await At(second ? "SecondYard" : "ContainerSearch");
        }

        async Task GoHome()
        {
            var exit = Field<Transform>(Search(), "exit").position;
            PositionPlayer(exit, exit + Vector3.up * 1.5f + Vector3.forward);
            Cursor.lockState = CursorLockMode.Locked;
            await Tap(Key.E);
            Check(Mode() == "Return", "The yard exit opens the return confirmation.");
            await Tap(Key.Enter);
            await At("Home");
        }

        async Task ClimbIn()
        {
            var rim = Field<Transform>(Search(), "outside");
            Player().Place(rim);
            Cursor.lockState = CursorLockMode.Locked;
            await Tap(Key.E);
            Check(Player().transform.position.z > 0.2f, "E crosses the container rim.");
        }

        async Task Take(string id, bool inspect = false)
        {
            var item = Object.FindObjectsByType<SearchItem>().Single(value => value.Id == id);
            var floor = Field<BoxCollider>(Search(), "containerFloor").bounds;
            var shape = item.GetComponent<BoxCollider>();
            foreach (var distance in new[] { .85f, 1.15f, 1.45f })
            foreach (var degrees in new[] { 0, 90, 180, 270, 45, 135, 225, 315 })
            {
                var target = shape.bounds.center;
                var offset = Quaternion.Euler(0, degrees, 0) * Vector3.back * distance;
                var feet = new Vector3(target.x, floor.max.y + .035f, target.z) + offset;
                if (feet.x < floor.min.x + .32f || feet.x > floor.max.x - .32f ||
                    feet.z < floor.min.z + .32f || feet.z > floor.max.z - .32f)
                    continue;
                PositionPlayer(feet, target);
                await Frames();
                if (Field<SearchItem>(Search(), "aimedItem") != item)
                    continue;

                await MouseAt(Vector2.zero, 1);
                Check(state.HeldItemId == id, $"LMB takes the physical {id}.");
                await Tap(inspect ? Key.Q : Key.Tab);
                await MouseAt(Vector2.zero, 0);
                return;
            }
            throw new InvalidOperationException($"Could not aim at {id}.");
        }

        async Task Pack(int col, int row)
        {
            var id = state.HeldItemId;
            var bag = Hud().Bag;
            if (!bag.gameObject.activeSelf)
                await Tap(Key.Tab);
            var grid = Field<RectTransform>(bag, "grid");
            var pointer = RectTransformUtility.WorldToScreenPoint(null, grid.TransformPoint(
                grid.rect.xMin + (col + .5f) * BagView.CellSize, grid.rect.yMax - (row + .5f) * BagView.CellSize, 0f));
            await MouseAt(pointer, 0);
            await MouseAt(pointer, 1);
            await MouseAt(pointer, 0);
            Check(state.Bag.TryGetPlacement(id, out var placement) && placement.Column == col && placement.Row == row,
                $"The bag stores {id} in the selected cells.");
            await Tap(Key.Tab);
        }

        async Task WalkTo(Vector3 target)
        {
            var player = Player();
            var start = player.transform.position;
            var direction = Vector3.ProjectOnPlane(target - start, Vector3.up);
            PositionPlayer(start, start + direction + Vector3.up * CameraHeight());
            InputSystem.QueueStateEvent(keys, new KeyboardState(Key.W));
            var initialTime = Time.realtimeSinceStartupAsDouble;
            await Wait.Until(() => Vector3.Dot(target - player.transform.position, direction.normalized) <= .20f ||
                Time.realtimeSinceStartupAsDouble - initialTime > 4, because: "walking to " + target);
            InputSystem.QueueStateEvent(keys, new KeyboardState());
            await Frames();
            Check(Vector3.Distance(Vector3.ProjectOnPlane(target - player.transform.position, Vector3.up), Vector3.zero) < .65f,
                $"The player can walk to {target} from {start}.");
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

    private static float CameraHeight() => Player().ViewCamera.transform.position.y - Player().transform.position.y;
    private static void PositionPlayer(Vector3 feet, Vector3 target)
    {
        var marker = new GameObject("Verification waypoint");
        marker.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(target - feet - Vector3.up * CameraHeight()));
        Player().Place(marker.transform);
        Object.Destroy(marker);
    }

    private static string[] InspectionText() =>
        Hud().GetComponentInChildren<ItemInspectionView>()?.GetComponentsInChildren<Text>()
            .Select(t => t.text).ToArray() ?? Array.Empty<string>();
    private static string ExpeditionText(string name) =>
        Hud().Expedition.GetComponentsInChildren<Text>().Single(text => text.name == name).text;
    private static SearchHud Hud() => Object.FindObjectsByType<SearchHud>().Single();
    private static SearchPlayer Player() => Object.FindAnyObjectByType<SearchPlayer>();
    private static SearchSession Search() => Object.FindAnyObjectByType<SearchSession>();
    private static string Mode()
    {
        var session = Object.FindAnyObjectByType<SettlementSession>();
        return Field<object>(session != null ? (object)session : Search(), "mode").ToString();
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);

    private static async Task Frames()
    {
        var next = Time.frameCount + 3;
        await Wait.Until(() => Time.frameCount >= next, because: "advancing frames");
    }
}
