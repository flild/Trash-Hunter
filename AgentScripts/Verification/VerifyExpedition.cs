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
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;

var args = Environment.GetCommandLineArgs();
var phase = Option("-trash-hunter-check") ?? Environment.GetEnvironmentVariable("TRASH_HUNTER_CHECK");
var folder = Option("-trash-hunter-save-dir") ?? Environment.GetEnvironmentVariable("TRASH_HUNTER_EDITOR_SAVE_DIR");
if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder))
    throw new InvalidOperationException("Use an isolated absolute save path for this route.");
var captures = Path.GetFullPath("Logs/I-030");
var primary = Path.Combine(folder, "slot.json");
var backup = Path.Combine(folder, "slot.backup.json");
var pending = Path.Combine(folder, "slot.pending");

var checks = new List<string>();
var measures = new List<object>();
var settings = InputSystem.settings;
var previousBackground = settings.backgroundBehavior;
var previousInput = settings.editorInputBehaviorInPlayMode;
var previousKeyboard = Keyboard.current;
var previousMouse = Mouse.current;
settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
var keyboard = InputSystem.AddDevice<Keyboard>();
var mouse = InputSystem.AddDevice<Mouse>();
keyboard.MakeCurrent();
mouse.MakeCurrent();
var pointer = Vector2.zero;
var initialFrame = Time.frameCount;

try
{
    await Frames();
    var state = State();
    Require(Time.frameCount > initialFrame, "Player loop advances");
    switch (phase)
    {
        case "prepare":
        {
            Require(!state.HasBag && !state.AtHome && state.Expedition == 1 && !File.Exists(primary), "Fresh slot starts the tutorial");
            if (Cursor.lockState != CursorLockMode.Locked)
                await Click();
            await Aim(GameObject.Find("Starter Waist Bag").transform.position);
            await Tap(Key.E);
            Require(state.HasBag && Read(primary).hasBag, "Picking up starter bag immediately writes it");
            await Tap(Key.E);
            await Take("crate-a");
            await Aim(new Vector3(1.32f, 0.226f, 2.32f));
            await Click();
            Require(!Item("crate-a").IsHeld && Item("crate-a").transform.position.x > 0.9f, "Original crate is moved onto a free surface");
            await Take("lamp-a");
            await Tap(Key.Tab);
            await Tap(Key.R);
            var grid = Hud().Bag.GetComponentsInChildren<RectTransform>().Single(t => t.name == "BagGrid");
            Canvas.ForceUpdateCanvases();
            pointer = RectTransformUtility.WorldToScreenPoint(null, grid.TransformPoint(new Vector3(49, -49)));
            await Click();
            Require(state.Bag.TryGetPlacement("lamp-a", out var packed) && packed.Rotated && packed.Width == 2, "Rotated lamp is packed via the grid");
            await Tap(Key.Tab);
            await Take("radio-a");
            await Tap(Key.E);
            Require(Player().transform.position.z < 0f && !state.AtHome, "Climbing out leaves the expedition active");
            await WalkToExit();
            await Tap(Key.E);
            Require(Heading() == "Уйти домой?" && Overlay("Details").Contains("<color=#FFAA95>Вещь в руке") &&
                Overlay("Details").Contains("только вещи из сумки"), "Exit warns in red about the hand and explains what travels");
            Capture("return");
            await Tap(Key.Escape);
            Require(!Hud().Expedition.gameObject.activeSelf && Item("radio-a").IsHeld &&
                state.Expedition == 1 && state.Bag.Count == 1, "Cancel preserves hand, bag and the active expedition");

            var before = File.ReadAllText(primary);
            Directory.CreateDirectory(pending);
            try
            {
                await Tap(Key.E);
                await Tap(Key.Enter);
                Require(Heading() == "Уйти домой?" && Overlay("Feedback").Contains("Не удалось сохранить") &&
                    !state.AtHome && Item("radio-a").IsHeld && state.Bag.Count == 1 &&
                    before == File.ReadAllText(primary), "Write failure keeps the return uncommitted and the old slot readable");
                await Tap(Key.Escape);
                await Tap(Key.Escape);
                await Tap(Key.Q);
                Require(Heading() == "Пауза" && Overlay("Feedback").Contains("Не удалось сохранить") &&
                    before == File.ReadAllText(primary), "A normal quit with a write failure stays in the game and shows the reason");
                Capture("quit-error");
            }
            finally
            {
                Directory.Delete(pending);
            }

            await Tap(Key.Escape);
            await Aim(new Vector3(1.5f, 1.25f, -0.5f));
            Require(Vector3.Distance(Read(primary).player.position, Player().transform.position) > 2f,
                "The live pose still needs to be captured on normal quit");
            var position = Player().transform.position;
            var plannedQuit = new JObject
            {
                ["position"] = new JObject { ["x"] = position.x, ["y"] = position.y, ["z"] = position.z },
                ["yaw"] = Player().transform.eulerAngles.y,
                ["pitch"] = Mathf.DeltaAngle(0, Player().ViewCamera.transform.eulerAngles.x)
            };
            File.WriteAllText(Path.Combine(folder, "quit-pose.json"), plannedQuit.ToString());
            QuitAfterResult();
            break;
        }
        case "resume":
        {
            var stored = Read(primary);
            Require(Heading() == "Продолжить" && stored.expedition == 1 && stored.held == "radio-a",
                "Restart opens the same expedition and the same held instance");
            var plannedQuit = JObject.Parse(File.ReadAllText(Path.Combine(folder, "quit-pose.json"))).ToObject<PlayerProbe>();
            Require(Vector3.Distance(stored.player.position, plannedQuit.position) < 0.08f &&
                Mathf.Abs(Mathf.DeltaAngle(stored.player.yaw, plannedQuit.yaw)) < 0.1f &&
                Mathf.Abs(stored.player.pitch - plannedQuit.pitch) < 0.1f,
                "Normal quit wrote the player's current position and view");
            Require(Item("radio-a").IsHeld && state.Bag.TryGetPlacement("lamp-a", out var spot) && spot.Rotated &&
                spot.Column == 0 && spot.Row == 0 && state.Bag.Count == 1, "The world, hand and grid project the saved ownership");
            var crate = stored.world.Single(item => item.key == "crate-a");
            Require(crate.position.x > 0.9f && Vector3.Distance(crate.position, Item("crate-a").transform.position) < 0.05f,
                "The displaced crate is restored at its saved position");
            await Tap(Key.Enter);
            Require(Vector3.Distance(stored.player.position, Player().transform.position) < 0.1f, "Continue places the player at the recorded exit");
            await Tap(Key.E);
            await Tap(Key.Enter);
            Require(state.AtHome && state.Home.Count == 1 && state.Home[0].key == "lamp-a" &&
                state.Home[0].expedition == 1 && state.HeldItemId == null && state.Bag.Count == 0 && state.Balance == 0 &&
                Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include).All(item => !item.gameObject.activeSelf),
                "Confirmed return delivers only the packed lamp and removes the hand and pile");
            Require(Heading() == "Дома" && !Overlay("Details").Contains("₽"), "Home lists the delivered lamp before a price is requested");
            Capture("home");
            var quote = state.Quote(state.Home[0]);
            await Tap(Key.Enter);
            Require(Heading() == "Продать вещь?" && Overlay("Summary").Contains($"{quote.Price} ₽") &&
                Overlay("Details").Contains("Ушко треснуло"), "Offer shows the exact price and this lamp's defect before confirmation");
            Capture("offer");
            var before = File.ReadAllText(primary);
            Directory.CreateDirectory(pending);
            try
            {
                await Tap(Key.Enter);
                Require(Heading() == "Продать вещь?" && state.Home.Count == 1 && state.Balance == 0 &&
                    File.ReadAllText(primary) == before, "Sale write failure keeps both the item and balance");
            }
            finally
            {
                Directory.Delete(pending);
            }
            await Tap(Key.Enter);
            var after = File.ReadAllText(primary);
            Require(Heading() == "Дома" && state.Home.Count == 0 && state.Balance == quote.Price &&
                Overlay("Feedback").Contains($"+{quote.Price} ₽") && Read(primary).cash == quote.Price,
                "Retry commits removal, proceeds and receipt together");
            await Tap(Key.Enter);
            Require(!state.Sell(quote) && File.ReadAllText(primary) == after && state.Balance == quote.Price,
                "Repeated confirmation and a stale offer cannot credit cash twice");
            Capture("receipt");
            QuitAfterResult();
            break;
        }
        case "home":
        {
            Require(Heading() == "Продолжить" && state.AtHome && state.Balance == 18 && state.Home.Count == 0,
                "A fresh process keeps the home phase, paid balance and sold item removed");
            await Tap(Key.Enter);
            await Tap(Key.E);
            await Wait.Until(() => !Hud().Expedition.gameObject.activeSelf && Prompt().Contains("Залезть"));
            Require(State() == state && state.Expedition == 2 && !state.AtHome && state.Bag.Count == 0 &&
                state.HeldItemId == null && state.HasBag && state.Balance == 18 && Read(primary).expedition == 2,
                "Departure immediately records a new expedition with an empty bag and the same balance");
            Require(Item("lamp-a").transform.position.x > 1f && Item("radio-a").transform.position.x < -1f &&
                Object.FindObjectsByType<SearchItem>().Length == 5, "Second arrangement is physically present with its own positions");
            await Tap(Key.E);
            await Take("dial-a");
            Require(Read(primary).held == "dial-a", "A held find is recorded in the second expedition");
            Measure(state);
            QuitAfterResult();
            break;
        }
        case "recovery":
        {
            var previous = Read(backup);
            Require(Heading() == "Продолжить" && Overlay("Feedback").Contains("Восстановлена более ранняя запись от"),
                "A damaged or missing main file opens the reserve and displays its date");
            Require(state.Expedition == previous.expedition && state.Balance == previous.cash &&
                state.AtHome == previous.atHome && (state.HeldItemId ?? "") == (previous.held ?? "") &&
                Vector3.Distance(Player().transform.position, previous.player.position) < 0.05f,
                "The reserve supplies the complete run and pose, independent of any pending file");
            Capture("recovery");
            await Tap(Key.Enter);
            QuitAfterResult();
            break;
        }
        case "invalid":
        {
            var before = File.ReadAllText(primary);
            Require(Heading() == "Автосохранение недоступно" && !string.IsNullOrWhiteSpace(Overlay("Details")),
                "An unusable slot shows a load error and never exposes a partial search");
            await Tap(Key.Enter);
            Require(Heading() == "Автосохранение недоступно" && File.ReadAllText(primary) == before,
                "Retry leaves the original file untouched");
            QuitAfterResult();
            break;
        }
        default:
            throw new InvalidOperationException("Unknown I-030 check phase: " + phase);
    }

    return new { phase, frames = Time.frameCount - initialFrame, checks, measures };
}
finally
{
    InputSystem.RemoveDevice(keyboard);
    InputSystem.RemoveDevice(mouse);
    settings.backgroundBehavior = previousBackground;
    settings.editorInputBehaviorInPlayMode = previousInput;
    if (previousKeyboard != null && previousKeyboard.added)
        previousKeyboard.MakeCurrent();
    if (previousMouse != null && previousMouse.added)
        previousMouse.MakeCurrent();
}

void Require(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description + " failed. Passed: " + string.Join(" | ", checks));
    checks.Add(description);
}

string Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

GameState State() => Object.FindAnyObjectByType<SearchSceneScope>().Container.Resolve<GameState>();
SearchPlayer Player() => Object.FindAnyObjectByType<SearchPlayer>();
SearchHud Hud() => Object.FindAnyObjectByType<SearchHud>();
SearchItem Item(string id) => Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include).Single(item => item.Id == id);
string Overlay(string name) => Hud().Expedition.GetComponentsInChildren<Text>(true).Single(text => text.name == name).text;
string Heading() => Overlay("Heading");
string Prompt() => Hud().GetComponentsInChildren<Text>().Single(text => text.name == "Prompt").text;

async Task Frames()
{
    var frame = Time.frameCount;
    await Wait.Until(() => Time.frameCount >= frame + 3);
}

async Task Tap(Key key)
{
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
    await Frames();
    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    await Frames();
}

async Task Click()
{
    InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer }.WithButton(MouseButton.Left));
    await Frames();
    InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
    await Frames();
}

async Task Aim(Vector3 target)
{
    var view = Player().ViewCamera.transform;
    var desired = Quaternion.LookRotation(target - view.position).eulerAngles;
    var current = view.eulerAngles;
    InputSystem.QueueDeltaStateEvent(mouse.delta,
        new Vector2(Mathf.DeltaAngle(current.y, desired.y), -Mathf.DeltaAngle(current.x, desired.x)) / 0.11f);
    await Frames();
}

async Task Take(string id)
{
    await Aim(Item(id).transform.position);
    await Click();
    Require(Item(id).IsHeld, "Search input picks up " + id);
}

async Task WalkToExit()
{
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S));
    await Wait.Until(() => Player().transform.position.z < -3.4f);
    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    await Frames();
    Require(Prompt().Contains("E  Домой"), "The distinct yard exit is reachable from the rim");
}

void Capture(string name)
{
    var path = Path.Combine(captures, $"{(Application.isEditor ? "editor" : "player")}-{name}.png");
    var camera = Player().ViewCamera;
    var canvas = Hud().GetComponent<Canvas>();
    var mode = canvas.renderMode;
    var oldCamera = canvas.worldCamera;
    var oldDistance = canvas.planeDistance;
    var oldTarget = camera.targetTexture;
    var oldActive = RenderTexture.active;
    var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
    var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
    try
    {
        camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 0.2f;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = target;
        image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
        File.WriteAllBytes(path, image.EncodeToPNG());
    }
    finally
    {
        canvas.renderMode = mode;
        canvas.worldCamera = oldCamera;
        canvas.planeDistance = oldDistance;
        camera.targetTexture = oldTarget;
        RenderTexture.active = oldActive;
        RenderTexture.ReleaseTemporary(target);
        Object.Destroy(image);
    }
}

ProbeSnapshot Read(string file) =>
    JObject.Parse(JObject.Parse(File.ReadAllText(file)).Value<string>("payload")).ToObject<ProbeSnapshot>();

void Measure(GameState state)
{
    var instance = BindingFlags.Instance | BindingFlags.NonPublic;
    var save = typeof(GameState).GetMethod("Save", instance);
    var session = Object.FindAnyObjectByType<SearchSession>();
    var capture = typeof(SearchSession).GetMethod("CaptureWorld", instance);
    var paths = Path.Combine(folder, "read-probe");
    Directory.CreateDirectory(paths);
    var slotType = typeof(GameState).Assembly.GetType("TrashHunter.SaveSlot", true);
    var validatorType = slotType.GetConstructors().Single().GetParameters()[1].ParameterType;
    var validate = Delegate.CreateDelegate(validatorType, state, typeof(GameState).GetMethod("Validate", instance));
    var load = slotType.GetMethod("Load");
    var writes = new List<double>();
    var reads = new List<double>();
    for (var i = 0; i < 12; i++)
    {
        var clock = Stopwatch.StartNew();
        capture.Invoke(session, null);
        var saved = (bool)save.Invoke(state, null);
        clock.Stop();
        writes.Add(clock.Elapsed.TotalMilliseconds);
        if (!saved)
            throw new InvalidOperationException(state.Error);
        File.Copy(primary, Path.Combine(paths, "slot.json"), true);
        clock.Restart();
        using (var reader = (IDisposable)Activator.CreateInstance(slotType, paths, validate))
        {
            if (load.Invoke(reader, null) == null)
                throw new InvalidOperationException("Measured snapshot failed to load.");
            clock.Stop();
            reads.Add(clock.Elapsed.TotalMilliseconds);
        }
    }
    measures.Add(new { bytes = new FileInfo(primary).Length, writeMs = writes, loadMs = reads });
}

async void QuitAfterResult()
{
    if (Application.isEditor)
        return;
    await Task.Delay(1000);
    Application.Quit();
}

[Serializable]
sealed class ProbeSnapshot
{
    public int expedition { get; set; }
    public bool hasBag { get; set; }
    public bool atHome { get; set; }
    public string held { get; set; }
    public long cash { get; set; }
    public PlayerProbe player { get; set; }
    public WorldProbe[] world { get; set; }
}

[Serializable]
sealed class PlayerProbe
{
    public Vector3 position { get; set; }
    public float yaw { get; set; }
    public float pitch { get; set; }
}

[Serializable]
sealed class WorldProbe
{
    public string key { get; set; }
    public Vector3 position { get; set; }
}
