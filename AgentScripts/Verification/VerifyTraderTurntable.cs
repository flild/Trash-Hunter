using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TrashHunter;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

const string quest = "trader-turntable";
const string step = "show-turntable";
const string model = "turntable-rp07";
const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
var folder = Option("-trash-hunter-save-dir", "TRASH_HUNTER_EDITOR_SAVE_DIR");
if (!Application.isPlaying || string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder))
    throw new InvalidOperationException("Enter Play Mode with an isolated slot.");

var primary = Path.Combine(folder, "slot.json");
var phase = Option("-trash-hunter-check", "TRASH_HUNTER_CHECK");
var english = Option("-trash-hunter-locale", "TRASH_HUNTER_LOCALE") == "en";
var original = File.ReadAllText(primary);
var run = Object.FindObjectsByType<LifetimeScope>().Single(scope => scope.name == "Run Scope");
var coordinator = Object.FindObjectsByType<LifetimeScope>().Single(scope => scope.name == "Application Scope")
    .GetComponents<MonoBehaviour>().Single(component => component.GetType().Name == "SceneCoordinator");
var state = run.Container.Resolve<GameState>();
var checks = new List<string>();
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
    await Until(Ready, "initial scene");
    Check(Mode() == "Continue", "Cold start opens continue");
    if (phase == "resume")
        CheckPile(Payload(original));
    await Tap(Key.Enter);
    Check(File.ReadAllText(primary) == original, "Continue preserves the original slot");

    switch (phase)
    {
        case "offer":
        case "offer-early":
            await OfferAndSearch(phase == "offer-early");
            break;
        case "resume":
            await CollectAndPresent();
            break;
        case "finished":
            Check(state.Quests.HasContact("repairer") && state.Home.Count(item => item.key == model) == 1,
                "Cold trader start restores the completed quest and the original item");
            await Tap(Key.J);
            CheckResult();
            break;
        default:
            throw new InvalidOperationException($"Unknown phase '{phase}'.");
    }
}
finally
{
    InputSystem.RemoveDevice(mouse);
    InputSystem.RemoveDevice(keyboard);
    previousMouse?.MakeCurrent();
    previousKeyboard?.MakeCurrent();
    settings.editorInputBehaviorInPlayMode = input;
    settings.backgroundBehavior = background;
}

return new { phase, locale = english ? "en" : "ru", passed = checks.Count, checks, state.Expedition };

async Task OfferAndSearch(bool earlyFinds)
{
    Check(state.Location == RunLocation.Trader && !state.Quests.IsAvailable(quest), "Before first sale no request is offered");
    await Tap(Key.J);
    Check(Mode() == "Trader", "J does not open the unavailable request");
    await Tap(Key.Enter);
    Check(Mode() == "Sale", "Enter still shows the normal sale");
    await Tap(Key.Enter);
    Check(state.Quests.IsAvailable(quest) && Text("Actions").Contains("J"), "First sale exposes the request");
    Fits();
    await Tap(Key.J);
    CheckBrief();
    Check(Text("Actions").Contains("Enter"), "Offer can be accepted");
    var before = File.ReadAllText(primary);
    using (new FileStream(Path.Combine(folder, "slot.pending"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        await Tap(Key.Enter);
    Check(state.Quests.IsAvailable(quest) && File.ReadAllText(primary) == before && Text("Feedback").Length > 0,
        "A failed accept leaves the offer and slot unchanged");
    await Tap(Key.Enter);
    Check(state.Quests.TryGetProgress(quest, out var next) && next == step &&
        (int)Saved()["quests"].Single()["acceptedExpedition"] == state.Expedition,
        "Acceptance publishes the starting expedition and active step");
    Check(!Text("Actions").Contains("Enter"), "The active request is read-only");
    Fits();
    await Tap(Key.Escape);
    await Travel(Key.E, RunLocation.Home);
    await Tap(Key.J);
    CheckBrief();
    await Tap(Key.Escape);

    for (var attempt = 1; attempt <= 4; attempt++)
    {
        await Travel(Key.E, RunLocation.Search);
        Check(state.TryGetPose(model, out _) == (attempt >= 3 || earlyFinds),
            $"Attempt {attempt} respects the seeded early rolls or guarantee");
        if (attempt == 3)
        {
            var turntable = Item();
            Check(turntable.gameObject.activeSelf && turntable.GetComponent<BoxCollider>().enabled,
                "The guaranteed turntable is a physical find in the yard");
            await Tap(Key.Escape);
            Check(Text("Actions").Contains("J"), "Pause advertises the active request");
            Fits();
            await Tap(Key.J);
            CheckBrief();
            await Tap(Key.Escape);
        }
        if (attempt < 4)
            await LeaveYard();
    }

    var beforeReload = Saved();
    var previous = SceneManager.GetActiveScene();
    await Tap(Key.F5);
    await Until(() => Ready() && SceneManager.GetActiveScene() != previous, "F5 scene reload");
    var saved = Saved();
    Check((int)saved["expedition"] == (int)beforeReload["expedition"] &&
        (int)saved["pileSeed"] == (int)beforeReload["pileSeed"] &&
        saved["world"].Select(item => (string)item["key"]).SequenceEqual(beforeReload["world"].Select(item => (string)item["key"])),
        "F5 keeps the expedition, seed and item IDs");
    CheckPile(saved, false);
}

async Task CollectAndPresent()
{
    Check(state.Quests.TryGetProgress(quest, out var next) && next == step, "The request remains active after cold start");
    var turntable = Item();
    var marker = new GameObject("Verification position");
    try
    {
        var target = turntable.GetComponent<BoxCollider>().bounds.center;
        var player = Player();
        var camera = player.ViewCamera.transform;
        var floor = Field<BoxCollider>(Session(), "containerFloor").bounds;
        var height = camera.position.y - player.transform.position.y;
        var directions = new[] { Vector3.back, Vector3.forward, Vector3.left, Vector3.right };
        foreach (var direction in directions.Concat(directions.Select(vector => vector * 1.5f)))
        {
            var feet = new Vector3(target.x, floor.max.y + 0.035f, target.z) + direction * 0.85f;
            if (feet.x < floor.min.x + 0.4f || feet.x > floor.max.x - 0.4f ||
                feet.z < floor.min.z + 0.4f || feet.z > floor.max.z - 0.4f)
                continue;

            marker.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(target - feet - Vector3.up * height));
            player.Place(marker.transform);
            Cursor.lockState = CursorLockMode.Locked;
            await Frames();
            var angle = Quaternion.LookRotation(turntable.GetComponent<Collider>().bounds.center - camera.position).eulerAngles;
            InputSystem.QueueDeltaStateEvent(mouse.delta, new Vector2(
                Mathf.DeltaAngle(camera.eulerAngles.y, angle.y), -Mathf.DeltaAngle(camera.eulerAngles.x, angle.x)) / 0.11f);
            await Frames();
            if (Field<SearchItem>(Session(), "aimedItem") == turntable)
                break;
        }
        Check(Field<SearchItem>(Session(), "aimedItem") == turntable, "The generated find is reachable by the normal aim ray");
        await Click(Vector2.zero);
        Check(state.HeldItemId == model, $"LMB picks up the generated world instance (error: {state.Error})");
    }
    finally
    {
        Object.Destroy(marker);
    }

    await Tap(Key.Q);
    Check(Object.FindObjectsByType<Text>().Any(text => text.text.Contains(english ? "RP-07" : "РП-07")),
        "The localized inspection identifies the requested model");
    await Tap(Key.Q);
    await Tap(Key.Tab);
    var grid = Field<RectTransform>(Hud().Bag, "grid");
    var pointer = RectTransformUtility.WorldToScreenPoint(null,
        grid.TransformPoint(new Vector3(grid.rect.xMin + 15, grid.rect.yMax - 15)));
    await Click(pointer);
    Check(state.Bag.TryGetPlacement(model, out var placement) && placement.Width == 2 && placement.Height == 2,
        "The actual bag UI packs the turntable into a 2×2 space");
    await Tap(Key.Tab);
    var item = new HomeItem { expedition = state.Expedition, key = model };
    await LeaveYard();
    Check(state.Home.Contains(item), "Returning home puts the same trip and item in stock");
    await Travel(Key.E, RunLocation.Search);
    Check(!state.TryGetPose(model, out _) && state.Home.Contains(item), "A model already in stock needs no second spawn");
    await LeaveYard();
    await Travel(Key.T, RunLocation.Trader);

    var stock = state.Home.ToArray();
    var bytes = File.ReadAllText(primary);
    Check(stock[0].key != model, "Fixture keeps an unrelated item before the turntable");
    await Tap(Key.P);
    Check(Mode() == "Trader" && File.ReadAllText(primary) == bytes, "The wrong selected item cannot be presented");
    await Tap(Key.DownArrow);
    Fits();
    Check(Text("Actions").Contains("P"), "The matching selection advertises presentation");
    await Tap(Key.P);
    Check(Mode() == "Present" && Text("Details") == (english ? "The turntable stays with you." : "Проигрыватель останется у вас."),
        "The confirmation says the item stays with the player");
    Fits();
    using (new FileStream(Path.Combine(folder, "slot.pending"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        await Tap(Key.Enter);
    Check(Mode() == "Present" && state.Home.SequenceEqual(stock) && !state.Quests.HasContact("repairer") &&
        File.ReadAllText(primary) == bytes, "A failed presentation writes neither completion nor contact");
    var balance = state.Balance;
    await Tap(Key.Enter);
    Check(state.Home.SequenceEqual(stock) && state.Balance == balance && state.Quests.HasContact("repairer") &&
        Saved()["contacts"].Values<string>().SequenceEqual(new[] { "repairer" }),
        "Presentation keeps both instances and balance and persists one contact");
    CheckResult();
    bytes = File.ReadAllText(primary);
    await Tap(Key.Enter);
    await Tap(Key.Escape);
    await Tap(Key.P);
    Check(Mode() == "Trader" && File.ReadAllText(primary) == bytes, "Repeat input cannot publish the finished quest again");
}

void CheckPile(JObject saved, bool checkPose = true)
{
    var world = saved["world"];
    Check(state.Expedition == (int)saved["expedition"] && (int)saved["pileGeneratorVersion"] == 3 &&
        world.Count(item => (string)item["key"] == model) == 1 &&
        world.All(item => state.TryGetPose((string)item["key"], out var pose) &&
            (!checkPose || (Vector3.Distance(pose.Position, Position(item["position"])) < 0.0001f &&
                Quaternion.Angle(pose.Rotation, Rotation(item["rotation"])) < 0.02f))),
        checkPose ? "Cold start restores all IDs and poses, including the turntable" : "F5 reload keeps every saved item");
}

void CheckBrief()
{
    Check(Mode() == "Quest" && Text("Summary") == (english ? "Trader" : "Старьёвщик") &&
        Text("Details").Contains(english ? "RP-07 turntable in the yard container" : "РП-07 в контейнере во дворе"),
        "The localized request names the model and its search location");
    Fits();
}

void CheckResult()
{
    Check(Mode() == "Quest" && Text("Summary") == (english ? "Contact unlocked: repairer" : "Открыт контакт мастера") &&
        Text("Details").StartsWith(english ? "The platter does not spin." : "Диск не вращается.", StringComparison.Ordinal),
        "The result reports the fault and the new contact");
    Fits();
}

async Task LeaveYard()
{
    Player().Place(Field<Transform>(Session(), "exit"));
    Cursor.lockState = CursorLockMode.Locked;
    await Tap(Key.E);
    Check(Mode() == "Return", "The yard exit opens return confirmation");
    await Travel(Key.Enter, RunLocation.Home);
}

async Task Travel(Key key, RunLocation location)
{
    await Tap(key);
    await Until(() => Ready() && state.Location == location && SceneManager.GetActiveScene().name ==
        (location == RunLocation.Search ? "ContainerSearch" : location.ToString()), "travel to " + location);
}

async Task Tap(Key key)
{
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
    await Frames();
    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    await Frames();
}

async Task Click(Vector2 pointer)
{
    InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer, buttons = 1 });
    await Frames();
    InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
    await Frames();
}

async Task Frames()
{
    var target = Time.frameCount + 3;
    await Until(() => Time.frameCount >= target, "advancing frames");
}

async Task Until(Func<bool> condition, string label)
{
    var deadline = Time.realtimeSinceStartupAsDouble + 10;
    await Wait.Until(() => condition() || Time.realtimeSinceStartupAsDouble >= deadline);
    if (!condition())
        throw new InvalidOperationException($"Timed out on {label}; scene={SceneManager.GetActiveScene().name}, error={state.Error}.");
}

void Fits()
{
    Canvas.ForceUpdateCanvases();
    var texts = Hud().Expedition.GetComponentsInChildren<Text>().Where(text => text.text.Length > 0).ToArray();
    Check(texts.All(text => text.preferredWidth <= text.rectTransform.rect.width + 1 &&
        text.preferredHeight <= text.rectTransform.rect.height + 1),
        $"{Mode()} text fits: " + string.Join(", ", texts.Select(text =>
            $"{text.name} {text.preferredWidth:F0}×{text.preferredHeight:F0}/{text.rectTransform.rect.width:F0}×{text.rectTransform.rect.height:F0}")));
}

void Check(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException(label);
    checks.Add(label);
}

bool Ready() => (bool)coordinator.GetType().GetProperty("HasControl", members).GetValue(coordinator);
SearchHud Hud() => Object.FindObjectsByType<SearchHud>().Single();
SearchPlayer Player() => Object.FindObjectsByType<SearchPlayer>().Single();
SearchItem Item() => Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include).Single(item => item.Id == model);
MonoBehaviour Session() => state.Location == RunLocation.Search
    ? Object.FindObjectsByType<SearchSession>().Single() : Object.FindObjectsByType<SettlementSession>().Single();
string Mode() => Field<object>(Session(), "mode").ToString();
string Text(string name) => Hud().Expedition.GetComponentsInChildren<Text>().Single(text => text.name == name).text;
T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, members).GetValue(instance);
JObject Saved() => Payload(File.ReadAllText(primary));
JObject Payload(string envelope) => JObject.Parse((string)JObject.Parse(envelope)["payload"]);
Vector3 Position(JToken data) => new((float)data["x"], (float)data["y"], (float)data["z"]);
Quaternion Rotation(JToken data) => new((float)data["x"], (float)data["y"], (float)data["z"], (float)data["w"]);

string Option(string argument, string variable)
{
    if (Application.isEditor)
        return Environment.GetEnvironmentVariable(variable);
    var args = Environment.GetCommandLineArgs();
    var index = Array.IndexOf(args, argument);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
