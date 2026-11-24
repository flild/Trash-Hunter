using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TrashHunter;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;

var checks = new List<string>();
var initialFrame = Time.frameCount;
var settings = InputSystem.settings;
var oldBackground = settings.backgroundBehavior;
var oldEditorInput = settings.editorInputBehaviorInPlayMode;
var oldKeyboard = Keyboard.current;
var oldMouse = Mouse.current;
settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
var keyboard = InputSystem.AddDevice<Keyboard>();
var mouse = InputSystem.AddDevice<Mouse>();
keyboard.MakeCurrent();
mouse.MakeCurrent();
var pointer = Vector2.zero;
var captureDir = Path.GetFullPath("Logs/I-020");
Directory.CreateDirectory(captureDir);
try
{
    await Frames();
    Require(Time.frameCount > initialFrame, "Player loop advances");
    var scope = Object.FindAnyObjectByType<SearchSceneScope>();
    var state = scope.Container.Resolve<GameState>();
    var player = Object.FindAnyObjectByType<SearchPlayer>();
    var camera = player.ViewCamera;
    var hud = Object.FindAnyObjectByType<SearchHud>();
    var bag = hud.Bag;
    var start = player.transform.position;
    var starter = GameObject.Find("Starter Waist Bag");
    if (Cursor.lockState != CursorLockMode.Locked)
        await Click();
    Require(!state.HasBag && Prompt(hud) == "Подберите поясную сумку" && Visible(camera, starter.transform.position),
        "Tutorial shows the bag and pickup objective at spawn");
    await Capture("tutorial");
    await Tap(Key.E);
    await Tap(Key.Tab);
    Require(Vector3.Distance(player.transform.position, start) < 0.15f && !bag.gameObject.activeSelf,
        "Climb and inventory are gated before pickup");
    await Aim(camera, starter.transform.position);
    Require(Prompt(hud).Contains("E  Подобрать поясную сумку"), "Aiming reveals the interaction");
    await Tap(Key.E);
    Require(state.HasBag && !starter.activeSelf && state.Bag.Count == 0, "Pickup equips one empty bag");
    await Tap(Key.E);
    Require(Prompt(hud).Contains("Вылезти") && player.transform.position.z > 0, "Climb after pickup");
    await Tap(Key.E);
    Require(Vector3.Distance(player.transform.position, start) < 0.15f && state.Bag.Count == 0, "Empty bag allows exit at the rim");
    await Tap(Key.E);

    var crate = Item("crate-a");
    var lamp = Item("lamp-a");
    var radio = Item("radio-a");
    var strip = Item("strip-a");
    var dial = Item("dial-a");

    await Take(camera, crate);
    await Tap(Key.Tab);
    await Hover(bag, 0, 0);
    await Click();
    await Tap(Key.R);
    await Click();
    Require(crate.IsHeld && state.Bag.Count == 0 && TextOf(bag, "BagFeedback") == "Вещь выходит за край",
        "Oversized crate cannot fit in either orientation");
    await Tap(Key.Tab);
    await Aim(camera, new Vector3(1.32f, 0.226f, 2.32f));
    await Click();
    Require(InWorld(crate) && Item(crate.Id) == crate, "Crate can be moved to expose the lamp");

    await Take(camera, lamp);
    await Tap(Key.Tab);
    Canvas.ForceUpdateCanvases();
    var grid = Grid(bag);
    Require(bag.gameObject.activeSelf && Cursor.lockState == CursorLockMode.None &&
        grid.rect.width > 0 && grid.rect.height > 0 && bag.GetComponentsInChildren<Text>()
            .All(t => t.rectTransform.rect.width > 0 && t.rectTransform.rect.height > 0),
        "Bag opens with free pointer and visible layout bounds");
    var viewRotation = camera.transform.rotation;
    var inside = player.transform.position;
    await Hover(bag, 2, 1);
    await Click();
    Require(lamp.IsHeld && state.Bag.Count == 0 && TextOf(bag, "BagFeedback") == "Вещь выходит за край",
        "Invalid anchor explains the edge and leaves the lamp in hand");
    await Capture("edge");
    Require(Quaternion.Angle(camera.transform.rotation, viewRotation) < 0.1f &&
        Vector3.Distance(player.transform.position, inside) < 0.1f, "Bag pointer does not move the player or camera");
    await Tap(Key.R);
    await Hover(bag, 0, 0);
    Require(TextOf(bag, "BagFeedback").Contains("Уложить") && TextOf(bag, "BagSelection").Contains("2 × 1"),
        "Quarter turn previews the horizontal footprint");
    await Capture("rotation");
    await Click();
    Require(Packed(state, lamp, 0, 0, 2, 1, true), "Packing retains the original lamp ID and orientation");
    await Tap(Key.Tab);

    await Take(camera, radio);
    await Tap(Key.Tab);
    await Hover(bag, 1, 0);
    await Click();
    Require(radio.IsHeld && state.Bag.Count == 1 && TextOf(bag, "BagFeedback") == "Эти ячейки заняты",
        "An occupied cell rejects the radio without shifting the lamp");
    await Hover(bag, 2, 0);
    await Click();
    Require(Packed(state, radio, 2, 0, 1, 1, false), "Radio fits next to the rotated lamp");
    await Tap(Key.Tab);

    await Take(camera, strip);
    await Tap(Key.Tab);
    await Hover(bag, 0, 1);
    Require(TextOf(bag, "BagFeedback") == "Вещь выходит за край", "Vertical strip cannot use the horizontal gap");
    await Tap(Key.R);
    await Click();
    Require(Packed(state, strip, 0, 1, 3, 1, true) && state.Bag.Count == 3,
        "Rotated strip fills the second row");
    await Capture("full");
    await Tap(Key.Tab);

    await Take(camera, dial);
    await Tap(Key.Q);
    Require(state.Bag.Count == 3 && dial.IsHeld && Prompt(hud) == dial.DisplayName,
        "Full bag still allows picking up and inspecting another find");
    var inspection = dial.transform.rotation;
    await Delta(new Vector2(65, 10));
    Require(Quaternion.Angle(inspection, dial.transform.rotation) > 15f, "Inspection rotates the find");
    await Tap(Key.Q);
    await Tap(Key.Tab);
    await Hover(bag, 2, 0);
    await Click();
    Require(dial.IsHeld && Packed(state, radio, 2, 0, 1, 1, false) &&
        TextOf(bag, "BagFeedback") == "Эти ячейки заняты", "Full bag refuses more loot without losing either instance");
    await Capture("occupied");
    await Tap(Key.Escape);
    Require(dial.IsHeld && !bag.gameObject.activeSelf && Cursor.lockState == CursorLockMode.Locked,
        "Cancelling the grid preserves the item in hand and restores mouse look");
    var dialDrop = new Vector3(0.16f, 0.205f, 2.53f);
    await Drop(camera, dial, dialDrop);

    await TakePacked(bag, radio, 2, 0);
    Require(state.Bag.Count == 2 && Item(radio.Id) == radio && !radio.GetComponent<Collider>().enabled,
        "Extraction takes the original radio and releases its cell");
    await Aim(camera, new Vector3(0, 0.92f, -0.15f));
    await Click();
    Require(radio.IsHeld && state.Bag.Count == 2, "Unsafe world placement preserves the extracted radio");
    await Drop(camera, radio, new Vector3(-0.68f, 0.205f, 2.76f));
    Require(Item(radio.Id) == radio && InWorld(radio), "Returned radio is the same physical find");
    await Take(camera, dial);
    await Tap(Key.Tab);
    await Hover(bag, 2, 0);
    await Click();
    Require(Packed(state, dial, 2, 0, 1, 1, false) && state.Bag.Count == 3,
        "Previously inspected dial replaces the returned radio");
    await Tap(Key.Tab);

    await Take(camera, radio);
    await Tap(Key.F5);
    await Wait.Until(() => scope == null && Object.FindAnyObjectByType<SearchSceneScope>() != null);
    await Frames();
    var reloadedScope = Object.FindAnyObjectByType<SearchSceneScope>();
    player = Object.FindAnyObjectByType<SearchPlayer>();
    hud = Object.FindAnyObjectByType<SearchHud>();
    camera = player.ViewCamera;
    bag = hud.Bag;
    radio = Item("radio-a");
    lamp = Item("lamp-a");
    dial = Item("dial-a");
    strip = Item("strip-a");
    Require(object.ReferenceEquals(state, reloadedScope.Container.Resolve<GameState>()) && state.HasBag &&
        GameObject.Find("Starter Waist Bag") == null && radio.IsHeld &&
        Packed(state, lamp, 0, 0, 2, 1, true) && Packed(state, strip, 0, 1, 3, 1, true) &&
        Packed(state, dial, 2, 0, 1, 1, false), "F5 preserves bag, packed shapes and hand by ID");
    var instances = Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include);
    Require(instances.Length == 5 && instances.Select(item => item.Id).Distinct().Count() == 5 &&
        Object.FindObjectsByType<BagView>(FindObjectsInactive.Include).Length == 1,
        "Reload preserves five unique item IDs and one inventory view");

    await Tap(Key.E);
    await Drop(camera, radio, new Vector3(-0.68f, 0.205f, 3.2f));
    await TakePacked(bag, lamp, 1, 0);
    Require(state.HeldItemRotated, "Taking the lamp by its second cell retains its orientation");
    await Drop(camera, lamp, new Vector3(0.32f, 0.205f, 3.1f));
    await TakePacked(bag, strip, 1, 1);
    await Drop(camera, strip, new Vector3(-0.43f, 0.205f, 2.06f));
    await TakePacked(bag, dial, 2, 0);
    await Drop(camera, dial, new Vector3(0.7f, 0.205f, 1.9f));
    await Tap(Key.Tab);
    Require(state.Bag.Count == 0 && state.HeldItemId == null && TextOf(bag, "BagSelection") == "Сумка пуста",
        "Unpacking all finds produces the empty bag state");
    await Capture("empty");
    await Tap(Key.Escape);
    await Tap(Key.E);
    Require(Vector3.Distance(player.transform.position, start) < 0.15f && state.Bag.Count == 0 &&
        Prompt(hud).Contains("Залезть"), "Rim stays usable after returning all baggage to the world");

    return new { frames = Time.frameCount - initialFrame, checks, captureDir };
}
finally
{
    InputSystem.RemoveDevice(keyboard);
    InputSystem.RemoveDevice(mouse);
    settings.backgroundBehavior = oldBackground;
    settings.editorInputBehaviorInPlayMode = oldEditorInput;
    if (oldKeyboard != null && oldKeyboard.added)
        oldKeyboard.MakeCurrent();
    if (oldMouse != null && oldMouse.added)
        oldMouse.MakeCurrent();
}

void Require(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description + " failed. Passed: " + string.Join(" | ", checks));
    checks.Add(description);
}

SearchItem Item(string id) =>
    Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include).Single(item => item.Id == id);

bool InWorld(SearchItem item) => item.gameObject.activeSelf && !item.IsHeld && item.GetComponent<Collider>().enabled;

bool Packed(GameState state, SearchItem item, int col, int row, int width, int height, bool rotated) =>
    !item.IsHeld && !item.gameObject.activeSelf && !item.GetComponent<Collider>().enabled &&
    state.Bag.TryGetPlacement(item.Id, out var placement) &&
    placement.Column == col && placement.Row == row && placement.Width == width &&
    placement.Height == height && placement.Rotated == rotated;

string Prompt(SearchHud hud) => hud.transform.Find("Prompt").GetComponent<Text>().text;
string TextOf(BagView bag, string name) => bag.transform.Find("BagPanel/" + name).GetComponent<Text>().text;
RectTransform Grid(BagView bag) => (RectTransform)bag.transform.Find("BagPanel/BagGrid");

bool Visible(Camera camera, Vector3 position)
{
    var point = camera.WorldToViewportPoint(position);
    return point.z > 0 && point.x > 0 && point.x < 1 && point.y > 0 && point.y < 1;
}

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

async Task Delta(Vector2 delta)
{
    InputSystem.QueueDeltaStateEvent(mouse.delta, delta);
    await Frames();
}

async Task Aim(Camera camera, Vector3 target)
{
    var angles = Quaternion.LookRotation(target - camera.transform.position).eulerAngles;
    var current = camera.transform.eulerAngles;
    await Delta(new Vector2(Mathf.DeltaAngle(current.y, angles.y), -Mathf.DeltaAngle(current.x, angles.x)) / 0.11f);
}

async Task Hover(BagView bag, int col, int row)
{
    var grid = Grid(bag);
    var point = new Vector3((col + 0.5f) * BagView.CellSize, -(row + 0.5f) * BagView.CellSize, 0);
    pointer = RectTransformUtility.WorldToScreenPoint(null, grid.TransformPoint(point));
    InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
    await Frames();
    Require(bag.CellAt(mouse.position.ReadValue()) == new Vector2Int(col, row), $"Mouse addresses cell {col},{row}");
}

async Task Take(Camera camera, SearchItem item)
{
    await Aim(camera, item.transform.position);
    await Click();
    Require(item.IsHeld && !item.GetComponent<Collider>().enabled, $"Took original {item.Id} from the world");
}

async Task TakePacked(BagView bag, SearchItem item, int col, int row)
{
    await Tap(Key.Tab);
    await Hover(bag, col, row);
    await Click();
    Require(item.IsHeld && item.gameObject.activeSelf, $"Took original {item.Id} from the bag");
    await Tap(Key.Tab);
}

async Task Drop(Camera camera, SearchItem item, Vector3 point)
{
    await Aim(camera, point);
    await Click();
    Require(InWorld(item), $"Placed original {item.Id} onto a free surface");
    await Wait.Until(() => item.GetComponent<Rigidbody>().IsSleeping());
}

async Task Capture(string name)
{
    var prefix = Application.isEditor ? "editor" : "player";
    var path = Path.Combine(captureDir, $"{prefix}-{name}.png");
    if (Application.isEditor)
        ScreenCapture.CaptureScreenshot(path);
    else
        CaptureOffscreen(path);
    await Frames();
}

void CaptureOffscreen(string path)
{
    // The Player is started hidden. Render the scene and HUD without using its window.
    var camera = Object.FindAnyObjectByType<SearchPlayer>().ViewCamera;
    var canvas = Object.FindAnyObjectByType<SearchHud>().GetComponent<Canvas>();
    var mode = canvas.renderMode;
    var worldCamera = canvas.worldCamera;
    var planeDistance = canvas.planeDistance;
    var renderTarget = camera.targetTexture;
    var activeTarget = RenderTexture.active;
    var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
    var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
    try
    {
        camera.targetTexture = target;
        canvas.worldCamera = camera;
        canvas.planeDistance = 0.2f;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = target;
        texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        File.WriteAllBytes(path, texture.EncodeToPNG());
    }
    finally
    {
        canvas.renderMode = mode;
        canvas.worldCamera = worldCamera;
        canvas.planeDistance = planeDistance;
        camera.targetTexture = renderTarget;
        RenderTexture.active = activeTarget;
        RenderTexture.ReleaseTemporary(target);
        Object.Destroy(texture);
    }
}
