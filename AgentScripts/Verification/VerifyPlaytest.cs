using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TrashHunter;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;

var args = Environment.GetCommandLineArgs();
var phase = Option("-trash-hunter-check") ?? Environment.GetEnvironmentVariable("TRASH_HUNTER_CHECK");
var folder = Option("-trash-hunter-save-dir") ?? Environment.GetEnvironmentVariable("TRASH_HUNTER_EDITOR_SAVE_DIR");
if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder))
    throw new InvalidOperationException("Use an isolated absolute save path.");
var evidence = Path.GetFullPath("Logs/I-040");
var primary = Path.Combine(folder, "slot.json");
var checks = new List<string>();
var settings = InputSystem.settings;
var background = settings.backgroundBehavior;
var editorInput = settings.editorInputBehaviorInPlayMode;
var physicalKeyboard = Keyboard.current;
var physicalMouse = Mouse.current;
settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
var keyboard = InputSystem.AddDevice<Keyboard>();
var mouse = InputSystem.AddDevice<Mouse>();
keyboard.MakeCurrent();
mouse.MakeCurrent();
var pointer = Vector2.zero;
var frame = Time.frameCount;

try
{
    await Frames();
    Check(Time.frameCount > frame, "The game advances frames");
    var state = State();
    if (phase == "playtest-first")
    {
        Check(!File.Exists(primary) && state.Expedition == 1 && !state.HasBag, "Fresh expedition starts with the waist bag lesson");
        if (Cursor.lockState != CursorLockMode.Locked)
            await Click();
        await Aim(GameObject.Find("Starter Waist Bag").transform.position);
        await Tap(Key.E);
        Check(state.HasBag, "The waist bag is picked up through E");
        await Tap(Key.E);
        await Settle();
        Check(Player().transform.position.z > 0.7f, "The rim can be crossed into the first layout");
        CheckPile();
        var intact = Item("lamp-intact");
        var cracked = Item("lamp-cracked");
        var first = Save()["world"];
        File.WriteAllText(Path.Combine(folder, "first-layout.json"), first.ToString());
        Check(Shell(intact) == "Lamp_Enamel" && Shell(cracked) == "Safety_Paint", "The cracked lamp has the brighter shell first");
        var hinge = cracked.transform.Find("Hinge upper fragment").position;
        Check(LookingAt(hinge)?.name == "Wooden crate", "The box covers the cracked hinge from the entry approach");
        await Aim(hinge);
        await Capture("first-covered");
        await Take("crate-a");
        await PutDown(new Vector3(1.19f, 0, 2.64f), new Vector3(-0.26f, 0, 2.04f));
        await Settle();
        Check(LookingAt(cracked.transform.Find("Hinge upper fragment").position)?.GetComponent<SearchItem>() == cracked,
            "Moving the box reveals the damaged lamp from the approach");
        await Take("lamp-cracked");
        await Inspect("first-cracked");
        await Tap(Key.Tab);
        await Grid(0, 0);
        Check(state.Bag.TryGetPlacement(cracked.Id, out var placement) && placement.Width == 2 && placement.Height == 2,
            "The cracked lamp consumes a 2 by 2 block");
        await Tap(Key.Tab);
        await Take("lamp-intact");
        await Inspect("first-intact");
        await Tap(Key.Tab);
        await Grid(1, 0);
        Check(state.HeldItemId == intact.Id && state.Bag.Count == 1, "The two lamps cannot fit together");
        await Tap(Key.Tab);
        await PutDown(new Vector3(-0.42f, 0, 2.11f), new Vector3(0.41f, 0, 2.18f));
        await Take("dial-a");
        await ToYardExit();
        await Tap(Key.E);
        Check(Heading() == "Уйти домой?" && Overlay("Details").Contains("Вещь в руке"), "The exit warns about the item in the hand");
        await Tap(Key.Escape);
        Check(state.HeldItemId == "dial-a" && state.Bag.Count == 1, "Cancel preserves both item identities");
        await Tap(Key.Escape);
        Check(Heading() == "Пауза", "Ordinary quit is available after moving the crate and selecting loot");
        QuitAfterResult();
    }
    else if (phase == "playtest-resume")
    {
        Check(Heading() == "Продолжить" && state.Expedition == 1 && state.HeldItemId == "dial-a",
            "A new session opens the original expedition with its held item");
        Check(state.Bag.TryGetPlacement("lamp-cracked", out var packed) && packed.Column == 0 && packed.Row == 0 &&
            state.Bag.Count == 1, "The first lamp retains its cells");
        var saved = Save();
        var crate = saved["world"].Single(item => (string)item["key"] == "crate-a");
        Check(Vector3.Distance(Position(crate["position"]), Item("crate-a").transform.position) < 0.04f &&
            Vector3.Distance(Position(crate["position"]), new Vector3(0.16f, 0.5f, 3.08f)) > 0.65f,
            "The displaced crate persists in place");
        Check(Vector3.Distance(Position(saved["player"]["position"]), Player().transform.position) < 0.05f &&
            Player().transform.position.z < -2.7f, "Continue restores the saved location by the exit");
        await Tap(Key.Enter);
        await Tap(Key.E);
        await Tap(Key.Enter);
        Check(state.AtHome && state.Home.Count == 1 && state.Home[0].key == "lamp-cracked" &&
            state.HeldItemId == null, "Only the packed cracked lamp arrives home");
        await Sell("lamp-cracked", 18, "Ушко треснуло", 18, "first-sale");

        await Tap(Key.E);
        await Wait.Until(() => State().Expedition == 2 && !State().AtHome && Player() != null);
        await Frames();
        state = State();
        Check(state.Bag.Count == 0 && state.Balance == 18 && state.HasBag, "The second yard opens with the same cash and an empty bag");
        var firstLayout = JArray.Parse(File.ReadAllText(Path.Combine(folder, "first-layout.json")));
        var secondLayout = (JArray)Save()["world"];
        Check((int)Save()["expedition"] == 2 && LampsMoved(firstLayout, secondLayout),
            "Second expedition writes its own prepared arrangement");
        await Tap(Key.E);
        await Settle();
        CheckPile();
        Check(Shell(Item("lamp-intact")) == "Safety_Paint" && Shell(Item("lamp-cracked")) == "Lamp_Enamel",
            "The brighter shell now belongs to the intact lamp");
        await Take("dial-a");
        await Tap(Key.Tab);
        await Grid(1, 0);
        Check(state.Bag.Count == 1, "A small find occupies the central cell");
        await Tap(Key.Tab);
        await Take("lamp-cracked");
        await Inspect("second-cracked");
        await PutDown(new Vector3(0.56f, 0, 1.8f), new Vector3(0.50f, 0, 2.27f));
        await WalkToward(new Vector3(0, 0, 2.65f), 0.18f);
        await Take("lamp-intact");
        await Inspect("second-intact");
        await Tap(Key.Tab);
        await Grid(0, 0);
        await Grid(1, 0);
        Check(state.HeldItemId == "lamp-intact" && state.Bag.Count == 1,
            "The small find obstructs both lamp placements");
        await Tap(Key.Tab);
        await PutDown(new Vector3(0.65f, 0.335f, 3.72f), new Vector3(0.72f, 0, 3.1f));
        await Tap(Key.Tab);
        await Grid(1, 0);
        Check(state.HeldItemId == "dial-a" && state.Bag.Count == 0, "The same small find can be removed to make room");
        await Tap(Key.Tab);
        await PutDown(new Vector3(-0.44f, 0, 2.28f), new Vector3(-0.50f, 0, 3.12f));
        await Take("lamp-intact");
        await Tap(Key.Tab);
        await Grid(0, 0);
        Check(state.Bag.TryGetPlacement("lamp-intact", out _), "After replacement the intact lamp fits");
        await Tap(Key.Tab);
        await WalkToward(new Vector3(0, 0, 1.15f), 0.45f);
        await ToYardExit();
        await Tap(Key.E);
        await Tap(Key.Enter);
        Check(state.AtHome && state.Home.Count == 1 && state.Home[0].key == "lamp-intact", "Second return delivers the intact lamp");
        await Sell("lamp-intact", 42, "Крепление целое", 60, "second-sale");

        await Tap(Key.E);
        await Wait.Until(() => State().Expedition == 3 && !State().AtHome && Player() != null);
        await Frames();
        Check(State().Bag.Count == 0 && State().Balance == 60, "The optional third layout opens after the second sale");
        var thirdLayout = (JArray)Save()["world"];
        Check(LampsMoved(firstLayout, thirdLayout) && LampsMoved(secondLayout, thirdLayout),
            "Both lamps move again in the third layout");
        await Tap(Key.E);
        await Settle();
        Check(Player().transform.position.z > 0.7f, "The third layout remains accessible");
        CheckPile();
        await Capture("third-layout");
        await Tap(Key.E);
        Check(Player().transform.position.z < 0f, "The rim remains clear on exit from the third layout");
        await Tap(Key.Escape);
        QuitAfterResult();
    }
    else if (phase == "playtest-legacy")
    {
        Check(Heading() == "Продолжить" && (Save()["world"] as JArray).Count == 5,
            "I-030's five-item expedition loads directly");
        Check(!Item("lamp-intact").gameObject.activeSelf && !Item("lamp-cracked").gameObject.activeSelf,
            "No new items appear in the existing expedition");
        Check(state.Bag.TryGetPlacement("lamp-a", out var legacy) && legacy.Rotated && legacy.Width == 2 &&
            legacy.Height == 1 && Item("radio-a").IsHeld, "I-030's rotated lamp and held radio retain their identities");
        QuitAfterResult();
    }
    else
        throw new InvalidOperationException("Select a playtest phase.");

    return new { phase, count = checks.Count, checks };
}
finally
{
    InputSystem.RemoveDevice(keyboard);
    InputSystem.RemoveDevice(mouse);
    settings.backgroundBehavior = background;
    settings.editorInputBehaviorInPlayMode = editorInput;
    if (physicalKeyboard != null && physicalKeyboard.added)
        physicalKeyboard.MakeCurrent();
    if (physicalMouse != null && physicalMouse.added)
        physicalMouse.MakeCurrent();
}

void Check(bool pass, string description)
{
    if (!pass)
        throw new InvalidOperationException(description + " failed. Passed: " + string.Join(" | ", checks));
    checks.Add(description);
}

void CheckPile()
{
    var items = Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Exclude);
    Check(items.Length == 24 && items.Select(item => item.Id).Distinct().Count() == 24 &&
        items.Select(item => item.DisplayName).Distinct().Count() == 12 &&
        items.All(item => item.transform.position.y > 0.20f && item.transform.position.y < 1.4f &&
            Mathf.Abs(item.transform.position.x) < 2.1f && item.transform.position.z > 0.25f && item.transform.position.z < 5.24f),
        "24 distinct instances of 12 definitions settle inside the container");
}

async Task Sell(string id, int price, string condition, long balance, string capture)
{
    var state = State();
    Check(state.Home[0].key == id && !Overlay("Details").Contains("₽"), "The home list hides the offer");
    await Tap(Key.Enter);
    Check(Heading() == "Продать вещь?" && Overlay("Summary").Contains(price + " ₽") &&
        Overlay("Details").Contains(condition), "The offer identifies the selected hinge and its value");
    await Tap(Key.Enter);
    Check(state.Home.Count == 0 && state.Balance == balance && Overlay("Feedback").Contains(condition),
        "The receipt explains the condition and credits the correct balance");
    await Capture(capture);
}

async void QuitAfterResult()
{
    if (Application.isEditor)
        return;

    // Allow Pipeline to send the result before Unity runs the normal quit callbacks.
    await Task.Delay(1000);
    Application.Quit();
}

string Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

JToken Save() => JObject.Parse(JObject.Parse(File.ReadAllText(primary)).Value<string>("payload"));
Vector3 Position(JToken p) => new((float)p["x"], (float)p["y"], (float)p["z"]);
Vector3 LampPosition(JArray layout, string id) =>
    Position(layout.Single(item => (string)item["key"] == id)["position"]);
bool LampsMoved(JArray first, JArray second) =>
    new[] { "lamp-intact", "lamp-cracked" }.All(id => Vector3.Distance(LampPosition(first, id), LampPosition(second, id)) > 0.8f);
GameState State() => Object.FindAnyObjectByType<SearchSceneScope>().Container.Resolve<GameState>();
SearchPlayer Player() => Object.FindAnyObjectByType<SearchPlayer>();
SearchHud Hud() => Object.FindAnyObjectByType<SearchHud>();
SearchItem Item(string id) => Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include).Single(item => item.Id == id);
string Overlay(string name) => Hud().Expedition.GetComponentsInChildren<Text>(true).Single(text => text.name == name).text;
string Heading() => Overlay("Heading");
string Prompt() => Hud().GetComponentsInChildren<Text>().Single(text => text.name == "Prompt").text;
string Shell(SearchItem item) => item.transform.Find("Shade").GetComponent<Renderer>().sharedMaterial.name;

GameObject LookingAt(Vector3 point)
{
    var eye = Player().ViewCamera.transform.position;
    return Physics.Raycast(eye, point - eye, out var hit, 3f) ? hit.collider.gameObject : null;
}

async Task Frames(int count = 3)
{
    var start = Time.frameCount;
    await Wait.Until(() => Time.frameCount >= start + count);
}

async Task Settle()
{
    var start = Time.fixedTime;
    await Wait.Until(() => Time.fixedTime - start > 0.70f);
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

async Task Grid(int column, int row)
{
    var grid = Hud().Bag.GetComponentsInChildren<RectTransform>().Single(t => t.name == "BagGrid");
    Canvas.ForceUpdateCanvases();
    var size = BagView.CellSize;
    pointer = RectTransformUtility.WorldToScreenPoint(null,
        grid.TransformPoint(new Vector3(size * (column + 0.5f), -size * (row + 0.5f))));
    await Click();
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
    Check(Item(id).IsHeld, "Search input picks up " + id);
}

async Task Inspect(string capture)
{
    var item = Item(State().HeldItemId);
    await Tap(Key.Q);
    var facing = item.transform.rotation;
    InputSystem.QueueDeltaStateEvent(mouse.delta, new Vector2(284, -27));
    await Frames();
    Check(Quaternion.Angle(facing, item.transform.rotation) > 100f, "Inspect rotates the selected lamp to its hinge side");
    await Capture(capture);
    await Tap(Key.Q);
    Check(item.IsHeld, "Ending inspection keeps the same item in hand");
}

async Task PutDown(params Vector3[] points)
{
    var id = State().HeldItemId;
    foreach (var point in points)
    {
        var target = Physics.Raycast(point + Vector3.up * 1.35f, Vector3.down, out var hit, 1.5f) ? hit.point : point;
        await Aim(target);
        await Click();
        if (State().HeldItemId == null)
        {
            Check(!Item(id).IsHeld, "Search input puts down " + id);
            return;
        }
    }
    Check(false, "Search input finds a clear place to put down " + id);
}

async Task WalkToward(Vector3 target, float near)
{
    var player = Player();
    await Aim(new Vector3(target.x, player.ViewCamera.transform.position.y, target.z));
    await WalkWhile(Key.W, () => Vector3.ProjectOnPlane(player.transform.position - target, Vector3.up).magnitude >= near);
    Check(Vector3.ProjectOnPlane(player.transform.position - target, Vector3.up).magnitude < near + 0.12f,
        "Player can walk to " + target.ToString("F2") + " (reached " + player.transform.position.ToString("F2") + ")");
}

async Task ToYardExit()
{
    await Tap(Key.E);
    Check(Player().transform.position.z < 0f, "Climb out keeps the current expedition");
    await WalkWhile(Key.S, () => !Prompt().Contains("E  Домой"));
    Check(Prompt().Contains("E  Домой"),
        "Player reaches the exit from the rim (reached " + Player().transform.position.ToString("F2") +
        ", cursor " + Cursor.lockState + ")");
}

async Task WalkWhile(Key key, Func<bool> shouldMove)
{
    var pressed = new KeyboardState(key);
    var start = Time.unscaledTime;
    await Wait.Until(() =>
    {
        if (!shouldMove() || Time.unscaledTime - start > 15f)
            return true;
        keyboard.MakeCurrent();
        mouse.MakeCurrent();
        InputSystem.QueueStateEvent(keyboard, pressed);
        return false;
    });
    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    await Frames();
}

Task Capture(string name)
{
    var path = Path.Combine(evidence, $"{(Application.isEditor ? "editor" : "player")}-{name}.png");
    var camera = Player().ViewCamera;
    var canvas = Hud().GetComponent<Canvas>();
    var original = canvas.renderMode;
    var oldCamera = canvas.worldCamera;
    var oldDistance = canvas.planeDistance;
    var oldTarget = camera.targetTexture;
    var oldActive = RenderTexture.active;
    var surface = RenderTexture.GetTemporary(1280, 720, 24);
    var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
    try
    {
        camera.targetTexture = surface;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 0.2f;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = surface;
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        File.WriteAllBytes(path, image.EncodeToPNG());
    }
    finally
    {
        canvas.renderMode = original;
        canvas.worldCamera = oldCamera;
        canvas.planeDistance = oldDistance;
        camera.targetTexture = oldTarget;
        RenderTexture.active = oldActive;
        RenderTexture.ReleaseTemporary(surface);
        Object.Destroy(image);
    }
    return Task.CompletedTask;
}
