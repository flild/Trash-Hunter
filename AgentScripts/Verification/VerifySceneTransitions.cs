using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TrashHunter;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

const string search = "Assets/TrashHunter/Scenes/ContainerSearch.unity";
const string home = "Assets/TrashHunter/Scenes/Home.unity";
const string trader = "Assets/TrashHunter/Scenes/Trader.unity";
const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
var phase = Environment.GetEnvironmentVariable("TRASH_HUNTER_CHECK");
var folder = Environment.GetEnvironmentVariable("TRASH_HUNTER_EDITOR_SAVE_DIR");
if (!Application.isPlaying || string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder))
    throw new InvalidOperationException("Enter Play Mode with an absolute, isolated slot directory.");

var primary = Path.Combine(folder, "slot.json");
var staged = Path.Combine(folder, "slot.pending");
var original = File.Exists(primary) ? File.ReadAllText(primary) : null;
var app = Scope("Application Scope");
var run = Scope("Run Scope");
var state = run.Container.Resolve<GameState>();
var coordinator = app.GetComponents<MonoBehaviour>().Single(component => component.GetType().Name == "SceneCoordinator");
var routes = Field<Dictionary<RunLocation, string>>(coordinator, "paths");
var control = coordinator.GetType().GetProperty("HasControl", members);
var checks = new List<string>();
var timings = new Dictionary<string, object>();
await Until(() => Ready(), "initial zone");
var initial = SceneManager.GetActiveScene().path;
VerifyScene(routes[state.Location]);
timings.Add("startup", Timing());

var settings = InputSystem.settings;
var background = settings.backgroundBehavior;
var input = settings.editorInputBehaviorInPlayMode;
var previousKeyboard = Keyboard.current;
var previousMouse = Mouse.current;
var keyboard = InputSystem.AddDevice<Keyboard>();
var mouse = InputSystem.AddDevice<Mouse>();
try
{
    settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
    settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
    keyboard.MakeCurrent();
    mouse.MakeCurrent();

    if (original != null)
    {
        var saved = Snapshot(original);
        Check(File.ReadAllText(primary) == original && state.Expedition == (int)saved["expedition"] &&
            state.Location == SavedLocation(saved) &&
            state.Balance == (long)saved["cash"] && Mode() == "Continue",
            "Cold start selects the saved zone and preserves slot bytes before continuing");
        var expectedPacked = saved["world"].Where(item => (bool)item["packed"]).ToArray();
        Check(state.Home.SequenceEqual(saved["home"].ToObject<HomeItem[]>()) &&
            (state.HeldItemId ?? "") == ((string)saved["held"] ?? "") &&
            state.HeldItemRotated == ((bool?)saved["heldRotated"] ?? false) &&
            state.Bag.Count == expectedPacked.Length && expectedPacked.All(item =>
                state.Bag.TryGetPlacement((string)item["key"], out var placement) &&
                placement.Column == (int)item["column"] && placement.Row == (int)item["row"] &&
                placement.Rotated == (bool)item["rotated"]), "Saved stock, hand and bag grid are restored");
        Check(saved["world"].All(item => state.TryGetPose((string)item["key"], out var pose) &&
            Vector3.Distance(pose.Position, Position(item["position"])) < 0.0001f &&
            Quaternion.Angle(pose.Rotation, Rotation(item["rotation"])) < 0.01f),
            "Saved world poses and stable IDs survive scene selection");
        await Tap(Key.Enter);
        Check(Mode() != "Continue", "Enter enables controls in the saved zone");
    }

    switch (phase)
    {
        case "new":
            Check(original == null && !state.HasBag && state.Expedition == 1 && Mode() == "Search" &&
                !File.Exists(primary) && state.TryGetPose("parcel-a", out _), "New game starts at the first bag with a generated pile");
            VerifyEntry("Climb from yard");
            Cursor.lockState = CursorLockMode.Locked;
            var bag = GameObject.Find("Starter Waist Bag").GetComponent<Collider>();
            var view = Player().ViewCamera.transform;
            var angle = Quaternion.LookRotation(bag.bounds.center - view.position).eulerAngles;
            InputSystem.QueueDeltaStateEvent(mouse.delta, new Vector2(
                Mathf.DeltaAngle(view.eulerAngles.y, angle.y), -Mathf.DeltaAngle(view.eulerAngles.x, angle.x)) / 0.11f);
            await Frames();
            await Tap(Key.E);
            Check(state.HasBag && File.Exists(primary), "E equips the first bag and publishes the first snapshot");
            break;
        case "cold":
            if (state.Location is RunLocation.Home or RunLocation.Trader)
            {
                VerifyEntry(state.Location + " Entry");
                Check(Mode() == state.Location.ToString() &&
                    Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include).Length == 0,
                    "Cold settlement entry uses its own session without loading the search pile");
                VerifyOverlay();
            }
            else
            {
                var saved = Snapshot(original);
                var actor = Player();
                Check(Mode() == "Search" && actor.ViewCamera.isActiveAndEnabled &&
                    Vector3.Distance(actor.transform.position, Position(saved["player"]["position"])) < .15f &&
                    Mathf.Abs(Mathf.DeltaAngle(actor.transform.eulerAngles.y, (float)saved["player"]["yaw"])) < .1f,
                    "Cold search entry restores the saved player pose and enables the camera");
            }
            Check(File.ReadAllText(primary) == original, "Continue does not write the slot");
            break;
        case "route":
        case "failures":
            await Route(phase == "failures");
            break;
        default:
            throw new InvalidOperationException($"Unexpected phase {phase}.");
    }
}
finally
{
    routes[RunLocation.Home] = home;
    routes[RunLocation.Trader] = trader;
    routes[RunLocation.Search] = search;
    InputSystem.RemoveDevice(mouse);
    InputSystem.RemoveDevice(keyboard);
    previousMouse?.MakeCurrent();
    previousKeyboard?.MakeCurrent();
    settings.editorInputBehaviorInPlayMode = input;
    settings.backgroundBehavior = background;
}

return new { phase, initial, final = SceneManager.GetActiveScene().path, passed = checks.Count, checks, timings, state.Expedition };

async Task Route(bool failures)
{
    Check(initial == search && state.HasBag && state.Bag.Count > 0, "Route starts with packed finds in the expedition fixture");
    var previous = SceneManager.GetActiveScene();
    var expedition = state.Expedition;
    var packed = Snapshot(File.ReadAllText(primary))["world"].Where(item => (bool)item["packed"])
        .Select(item => new HomeItem { expedition = expedition, key = (string)item["key"] }).ToArray();
    var stock = state.Home.Concat(packed).ToArray();
    var balance = state.Balance;
    var revision = Revision();

    Cursor.lockState = CursorLockMode.Locked;
    await Tap(Key.F5);
    await Until(() => Ready() && SceneManager.GetActiveScene() != previous, "F5 reload");
    VerifyScene(search);
    var reload = Snapshot(File.ReadAllText(primary));
    Check((int)reload["format"] == 2 && SavedLocation(reload) == RunLocation.Search &&
        Revision() == ++revision && state.Expedition == expedition && state.Bag.Count == packed.Length,
        "F5 writes format 2 once and reloads with the same expedition and bag");
    timings.Add("reload", Timing());

    previous = SceneManager.GetActiveScene();
    var actor = Player();
    var exit = Field<Transform>(Session(), "exit");
    actor.Place(exit);
    Cursor.lockState = CursorLockMode.Locked;
    await Tap(Key.E);
    Check(Mode() == "Return", "E at the yard exit opens return confirmation");

    if (failures)
    {
        var bytes = File.ReadAllText(primary);
        var held = state.HeldItemId;
        using (new FileStream(staged, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            await Tap(Key.Enter, Key.NumpadEnter);
        VerifyScene(search);
        Check(state.Error != null && Ready() && Mode() == "Return" && !state.AtHome &&
            Player() == actor && state.Expedition == expedition && state.Bag.Count == packed.Length &&
            state.HeldItemId == held && state.Home.SequenceEqual(stock.Except(packed)) && File.ReadAllText(primary) == bytes,
            "A blocked pending file leaves the original player, scene, items, bag and save intact");

        routes[RunLocation.Home] = "Assets/TrashHunter/Scenes/MissingHome.unity";
        await Tap(Key.Enter, Key.NumpadEnter);
        var committed = File.ReadAllText(primary);
        Check(!Ready() && state.AtHome && state.Expedition == expedition && state.Home.SequenceEqual(stock) &&
            Revision() == revision + 1 && SceneManager.GetActiveScene() == previous && SceneManager.sceneCount == 1,
            "Unavailable destination leaves the source visible and commits the confirmed return only once");
        Check(Hud().Expedition.GetComponentsInChildren<Text>().Single(text => text.name == "Heading").text ==
            (Environment.GetEnvironmentVariable("TRASH_HUNTER_LOCALE") == "en"
                ? "Could not load the location" : "Не удалось загрузить локацию"), "The existing HUD offers a load retry");
        await Tap(Key.E, Key.F5, Key.Tab);
        await Tap(Key.Enter, Key.NumpadEnter);
        Check(!Ready() && File.ReadAllText(primary) == committed && state.Home.SequenceEqual(stock),
            "Blocked gameplay input and a failed retry leave the committed bytes and ownership unchanged");
        routes[RunLocation.Home] = home;
        await Tap(Key.Enter, Key.NumpadEnter);
        await Until(() => Ready() && SceneManager.GetActiveScene().path == home, "confirmed home retry");
        Check(File.ReadAllText(primary) == committed, "Successful retry loads the committed zone without another write");
    }
    else
    {
        await Tap(Key.Enter, Key.NumpadEnter);
        await Until(() => Ready() && SceneManager.GetActiveScene().path == home, "return home");
    }

    VerifyScene(home);
    VerifyEntry("Home Entry");
    Check(actor == null && Revision() == ++revision && state.Home.SequenceEqual(stock) && state.Balance == balance &&
        state.HeldItemId == null && state.Bag.Count == 0 && Mode() == "Home",
        "The previous player is disposed and return transfers each packed item exactly once");
    timings.Add("return", Timing());

    VerifyOverlay();
    var offer = state.Trade.Quote(state.Home[^1]);
    Check(!state.Trade.Sell(offer), "Selling directly from home is rejected");
    await Tap(Key.Enter);
    Check(Mode() == "Home" && Revision() == revision, "Home shows storage without a sale confirmation");

    if (failures)
    {
        var beforeTravel = File.ReadAllText(primary);
        using (new FileStream(staged, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            await Tap(Key.T);
        VerifyScene(home);
        Check(Ready() && Mode() == "Home" && state.Error != null && File.ReadAllText(primary) == beforeTravel,
            "A failed travel write keeps the home scene, stock and controls");
    }

    await Tap(Key.T, Key.Enter);
    await Until(() => Ready() && SceneManager.GetActiveScene().path == trader, "travel to trader");
    VerifyScene(trader);
    VerifyEntry("Trader Entry");
    VerifyOverlay();
    Check(state.Location == RunLocation.Trader && state.Expedition == expedition && state.Balance == balance &&
        state.Home.SequenceEqual(stock) && Revision() == ++revision && Mode() == "Trader",
        "Travel preserves trip number, balance and every stored instance");
    timings.Add("trader", Timing());

    for (var i = 1; i < stock.Length; i++)
        await Tap(Key.DownArrow);
    await Tap(Key.Enter);
    Check(Mode() == "Sale" && Revision() == revision, "Trader quotes the selected instance without publishing");
    VerifyOverlay();
    if (failures)
    {
        var beforeSale = File.ReadAllText(primary);
        using (new FileStream(staged, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            await Tap(Key.Enter, Key.NumpadEnter);
        Check(Mode() == "Sale" && state.Error != null && state.Home.SequenceEqual(stock) &&
            state.Balance == balance && File.ReadAllText(primary) == beforeSale,
            "A failed sale write preserves the offer, both instances and cash");
    }

    await Tap(Key.Enter, Key.NumpadEnter);
    Check(Revision() == ++revision && state.Balance == balance + offer.Price &&
        state.Home.SequenceEqual(stock.Take(stock.Length - 1)), "One confirmation sells exactly the selected instance");
    var afterSale = File.ReadAllText(primary);
    Check(!state.Trade.Sell(offer) && File.ReadAllText(primary) == afterSale &&
        state.Balance == balance + offer.Price, "Replaying the sold instance does not charge or remove anything");
    File.Copy(primary, Path.Combine(folder, "trader-resume.json"));

    await Tap(Key.E, Key.Enter);
    await Until(() => Ready() && SceneManager.GetActiveScene().path == home, "trader return");
    VerifyScene(home);
    VerifyEntry("Home Entry");
    Check(state.AtHome && state.Expedition == expedition && state.Balance == balance + offer.Price &&
        state.Home.SequenceEqual(stock.Take(stock.Length - 1)) && Revision() == ++revision,
        "Trader return preserves trip number, credited cash and remaining stock");
    timings.Add("traderReturn", Timing());
    File.Copy(primary, Path.Combine(folder, "home-resume.json"));

    if (failures)
    {
        var bytes = File.ReadAllText(primary);
        var homeScene = SceneManager.GetActiveScene();
        using (new FileStream(staged, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            await Tap(Key.E);
            await Until(() => Ready(), "depart write failure cleanup");
        }
        VerifyScene(home);
        Check(state.Error != null && state.AtHome && state.Expedition == expedition &&
            SceneManager.GetActiveScene() == homeScene && Mode() == "Home" && File.ReadAllText(primary) == bytes,
            "A departure write failure discards the prepared scene and keeps the home slot and controls");
    }

    await Tap(Key.E);
    await Until(() => Ready() && SceneManager.GetActiveScene().path == search, "depart");
    VerifyScene(search);
    VerifyEntry("Climb from yard");
    Check(Revision() == ++revision && state.Expedition == expedition + 1 && state.Balance == balance + offer.Price &&
        state.TryGetPose("sack-a", out _) && Mode() == "Search" && !state.AtHome,
        "Departure writes one new expedition with its prepared pile and restores ready search controls");
    timings.Add("depart", Timing());
    File.Copy(primary, Path.Combine(folder, "search-resume.json"));
}

void VerifyScene(string path)
{
    var scene = SceneManager.GetActiveScene();
    var scope = Object.FindObjectsByType<SearchSceneScope>().Single();
    var players = Object.FindObjectsByType<SearchPlayer>();
    var cameras = Object.FindObjectsByType<Camera>().Where(camera => camera.isActiveAndEnabled);
    Check(scene.path == path && SceneManager.sceneCount == 1 && scope.gameObject.scene == scene &&
        players.Length == 1 && players[0].gameObject.scene == scene && cameras.Single() == players[0].ViewCamera &&
        Object.FindObjectsByType<AudioListener>().Count(listener => listener.isActiveAndEnabled) == 1 &&
        Object.FindObjectsByType<EventSystem>().Count(system => system.isActiveAndEnabled) == (path == search ? 1 : 0) &&
        Hud().gameObject.scene == scene && Session().isActiveAndEnabled && scope.Parent == run &&
        Scope("Application Scope") == app && Scope("Run Scope") == run && scope.Container.Resolve<GameState>() == state,
        $"One scene, player, camera, listener, HUD and input session are ready in {scene.name}; app and run are unchanged");
}

void VerifyEntry(string name)
{
    var marker = GameObject.Find(name).transform;
    Check(Vector3.Distance(Player().transform.position, marker.position) < 0.15f &&
        Mathf.Abs(Mathf.DeltaAngle(Player().transform.eulerAngles.y, marker.eulerAngles.y)) < 0.1f,
        $"Player spawns at {name}");
}

object Timing()
{
    var (load, write, activation, views, total) =
        ((double, double, double, double, double))coordinator.GetType().GetProperty("LastTransition", members).GetValue(coordinator);
    return new { readMs = (double)coordinator.GetType().GetProperty("SlotReadMs", members).GetValue(coordinator),
        loadMs = load, writeMs = write, activationMs = activation, viewsMs = views, totalMs = total };
}

async Task Tap(params Key[] keys)
{
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    await Until(() => keys.All(key => keyboard[key].isPressed), "key down: " + string.Join(", ", keys));
    await Frames();
    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    await Until(() => keys.All(key => !keyboard[key].isPressed), "key up: " + string.Join(", ", keys));
    await Frames();
}

async Task Frames()
{
    var target = Time.frameCount + 2;
    await Until(() => Time.frameCount >= target, "advancing frames");
}

async Task Until(Func<bool> predicate, string description)
{
    var deadline = Time.realtimeSinceStartupAsDouble + 10;
    await Wait.Until(() => predicate() || Time.realtimeSinceStartupAsDouble >= deadline);
    if (!predicate())
        throw new InvalidOperationException($"Timed out waiting for {description}. " +
            $"Scene={SceneManager.GetActiveScene().name}, location={state.Location}, control={Ready()}, error={state.Error}. " +
            $"Keyboard={Keyboard.current?.deviceId}.");
}

void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
    checks.Add(message);
}

bool Ready() => (bool)control.GetValue(coordinator);
long Revision() => (long)Snapshot(File.ReadAllText(primary))["revision"];
JObject Snapshot(string bytes) => JObject.Parse((string)JObject.Parse(bytes)["payload"]);
LifetimeScope Scope(string name) => Object.FindObjectsByType<LifetimeScope>().Single(scope => scope.name == name);
SearchPlayer Player() => Object.FindObjectsByType<SearchPlayer>().Single();
SearchHud Hud() => Object.FindObjectsByType<SearchHud>().Single();
MonoBehaviour Session() => SceneManager.GetActiveScene().path == search
    ? Object.FindObjectsByType<SearchSession>().Single()
    : Object.FindObjectsByType<SettlementSession>().Single();
string Mode() => Field<object>(Session(), "mode").ToString();
RunLocation SavedLocation(JObject saved) => (int)saved["format"] == 1
    ? ((bool)saved["atHome"] ? RunLocation.Home : RunLocation.Search)
    : saved["location"].ToObject<RunLocation>();

void VerifyOverlay()
{
    Canvas.ForceUpdateCanvases();
    var texts = Hud().Expedition.GetComponentsInChildren<Text>().Where(text => !string.IsNullOrEmpty(text.text)).ToArray();
    Check(texts.Length > 0 && texts.All(text =>
        text.preferredWidth <= text.rectTransform.rect.width + 1f &&
        text.preferredHeight <= text.rectTransform.rect.height + 1f),
        $"Overlay text fits its bounds in {Mode()}: " + string.Join(", ", texts.Select(text =>
            $"{text.name}={text.preferredWidth:F0}x{text.preferredHeight:F0}/{text.rectTransform.rect.width:F0}x{text.rectTransform.rect.height:F0}")));
}
T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, members).GetValue(instance);
Vector3 Position(JToken token) => new((float)token["x"], (float)token["y"], (float)token["z"]);
Quaternion Rotation(JToken token) => new((float)token["x"], (float)token["y"], (float)token["z"], (float)token["w"]);
