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
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

const string searchPath = "Assets/TrashHunter/Scenes/ContainerSearch.unity";
const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
var folder = Environment.GetEnvironmentVariable("TRASH_HUNTER_EDITOR_SAVE_DIR");
if (!Application.isPlaying || string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder))
    throw new InvalidOperationException("Start Play Mode in an empty scene with an isolated editor save directory.");

var checks = new List<string>();
var slot = Path.Combine(folder, "slot.json");
var original = File.Exists(slot) ? File.ReadAllText(slot) : null;
var expected = original == null ? null : JObject.Parse((string)JObject.Parse(original)["payload"]);
var initialScene = SceneManager.GetActiveScene();
Check(initialScene.rootCount == 0 && SceneManager.sceneCount == 1 &&
    Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include).Length == 0,
    "Cold startup uses an empty scene with no search items or other loaded scenes");

var run = Scopes("Run Scope").Single();
var app = Scopes("Application Scope").Single();
Check(run.Parent == app && app.gameObject.scene.name == "DontDestroyOnLoad",
    "The application and run initialize independently of the gameplay scene");
var state = run.Container.Resolve<GameState>();
Check(state.Error == null && Property<bool>("SlotLoaded"), "Bootstrap reads the slot successfully");

if (expected == null)
{
    Check(!Property<bool>("HasRun") && !Property<bool>("ResumePending") && state.Location == RunLocation.Search &&
        !File.Exists(slot), "An empty slot selects Search without creating a run, a pile or a save");
}
else
{
    var expectedLocation = (bool)expected["atHome"] ? RunLocation.Home : RunLocation.Search;
    Check(Property<bool>("HasRun") && Property<bool>("ResumePending") && state.Location == expectedLocation &&
        state.Expedition == (int)expected["expedition"] && state.HasBag == (bool)expected["hasBag"],
        "The old home flag selects the initial zone and the run is ready to resume");
    Check(state.Balance == (long)expected["cash"] &&
        state.Home.SequenceEqual(expected["home"].ToObject<HomeItem[]>()), "Cash and stored instance IDs match the slot");
    Check(state.Home.All(item => state.Quote(item).Price > 0), "Stored finds can be quoted without search views");
    Check((state.HeldItemId ?? "") == ((string)expected["held"] ?? "") &&
        state.HeldItemRotated == ((bool?)expected["heldRotated"] ?? false) &&
        Property<bool>("HeldInWorld") == ((bool?)expected["heldInWorld"] ?? false), "Hand ID and flags match the slot");

    var world = (JArray)expected["world"];
    var snapshot = JObject.Parse(JsonUtility.ToJson(typeof(GameState).GetField("current", members).GetValue(state)));
    Check((string)snapshot["runId"] == (string)expected["runId"] &&
        (long)snapshot["revision"] == (long)expected["revision"] &&
        (int)snapshot["pileSeed"] == ((int?)expected["pileSeed"] ?? 0) &&
        (int)snapshot["pileGeneratorVersion"] == ((int?)expected["pileGeneratorVersion"] ?? 0) &&
        snapshot["world"].Select(item => (string)item["key"]).SequenceEqual(world.Select(item => (string)item["key"])),
        "Run identity, revision, generator and all world IDs match the slot");
    Check(Vector(snapshot["player"]["position"]) == Vector(expected["player"]["position"]) &&
        (float)snapshot["player"]["yaw"] == (float)expected["player"]["yaw"] &&
        (float)snapshot["player"]["pitch"] == (float)expected["player"]["pitch"] &&
        (bool)snapshot["player"]["inside"] == (bool)expected["player"]["inside"], "Player pose matches the slot");
    Check(world.All(item =>
    {
        return state.TryGetPose((string)item["key"], out var pose) &&
            Vector3.Distance(pose.Position, Vector(item["position"])) < 0.0001f &&
            Quaternion.Angle(pose.Rotation, Rotation(item["rotation"])) < 0.01f;
    }), "All world positions and rotations match the slot");
    Check(world.Zip(snapshot["world"], (before, after) =>
        ((string)before["package"] ?? "") == ((string)after["package"] ?? "") &&
        ((bool?)before["opened"] ?? false) == (bool)after["opened"]).All(same => same),
        "Package contents and opened flags match the slot");
    var packed = world.Where(item => (bool)item["packed"]).ToArray();
    Check(state.Bag.Count == packed.Length && packed.All(item =>
    {
        return state.Bag.TryGetPlacement((string)item["key"], out var cell) &&
            cell.Column == (int)item["column"] && cell.Row == (int)item["row"] && cell.Rotated == (bool)item["rotated"];
    }), "Packed IDs, grid cells and rotation match the slot");
    Check(File.ReadAllText(slot) == original, "Cold loading preserves the original slot bytes");
}

await Wait.For(SceneManager.LoadSceneAsync(searchPath, LoadSceneMode.Single));
await Frames();
var session = Object.FindAnyObjectByType<SearchSession>();
var sceneScope = session.GetComponent<SearchSceneScope>();
Check(sceneScope.Parent == run && sceneScope.Container.Resolve<GameState>() == state &&
    Scopes("Run Scope").Single() == run && Scopes("Application Scope").Single() == app,
    "ContainerSearch attaches to the same application and GameState");

if (expected == null)
{
    var player = Object.FindAnyObjectByType<SearchPlayer>();
    var marker = GameObject.Find("Climb from yard").transform;
    var bag = GameObject.Find("Starter Waist Bag").GetComponent<Collider>();
    Check(state.Expedition == 1 && !state.HasBag && Mode() == "Search" &&
        Vector3.Distance(player.transform.position, marker.position) < 0.15f &&
        state.TryGetPose("sack-a", out _) && state.TryGetPose("parcel-a", out _) && !File.Exists(slot),
        "The first pile is generated on entry and the player starts at the bag tutorial");

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
        Cursor.lockState = CursorLockMode.Locked;
        var view = player.ViewCamera.transform;
        var target = Quaternion.LookRotation(bag.bounds.center - view.position).eulerAngles;
        var angle = view.eulerAngles;
        InputSystem.QueueDeltaStateEvent(mouse.delta,
            new Vector2(Mathf.DeltaAngle(angle.y, target.y), -Mathf.DeltaAngle(angle.x, target.x)) / 0.11f);
        await Frames();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
        await Frames();
        Check(state.HasBag && !bag.gameObject.activeSelf && File.Exists(slot), "E picks up the first bag and writes the slot");
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
}
else
{
    var views = Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include);
    Check(Mode() == "Continue" && views.Where(item => item.IsHeld).Select(item => item.Id)
        .SequenceEqual(state.HeldItemId == null ? Array.Empty<string>() : new[] { state.HeldItemId }),
        "The search presentation opens the continue overlay and restores the hand");
    Check(File.ReadAllText(slot) == original, "Attaching the scene preserves the original slot bytes");
}

return new { folder = Path.GetFileName(folder), initialLocation = state.Location.ToString(), state.Expedition, state.Balance,
    worldCount = expected?["world"].Count(), state.HeldItemId, bagCount = state.Bag.Count, passed = checks.Count, checks };

void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
    checks.Add(message);
}

IEnumerable<LifetimeScope> Scopes(string name) =>
    Object.FindObjectsByType<LifetimeScope>().Where(scope => scope.name == name);
T Property<T>(string name) => (T)typeof(GameState).GetProperty(name, members).GetValue(state);
string Mode() => typeof(SearchSession).GetField("mode", members).GetValue(session).ToString();
Vector3 Vector(JToken value) => new((float)value["x"], (float)value["y"], (float)value["z"]);
Quaternion Rotation(JToken value) => new((float)value["x"], (float)value["y"], (float)value["z"], (float)value["w"]);
async Task Frames()
{
    var target = Time.frameCount + 3;
    await Wait.Until(() => Time.frameCount >= target);
}
