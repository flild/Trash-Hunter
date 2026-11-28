using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TrashHunter;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;

var folder = Environment.GetEnvironmentVariable("TRASH_HUNTER_EDITOR_SAVE_DIR");
if (!Application.isPlaying || string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder))
    throw new InvalidOperationException("Start Play Mode on an isolated, absolute save directory.");
var slot = Path.Combine(folder, "slot.json");
var language = Environment.GetEnvironmentVariable("TRASH_HUNTER_LOCALE") == "en" ? "en" : "ru";
var phase = Environment.GetEnvironmentVariable("TRASH_HUNTER_CHECK");
var hud = Object.FindAnyObjectByType<SearchHud>();
var bag = hud.Bag;
var grid = (RectTransform)bag.transform.Find("BagPanel/BagGrid");
var hand = (RectTransform)bag.transform.Find("BagPanel/HandSlot");
var inspection = hud.GetComponentInChildren<ItemInspectionView>(true);
var camera = Object.FindAnyObjectByType<SearchPlayer>().ViewCamera;
var view = camera.transform;
var state = Object.FindAnyObjectByType<SearchSceneScope>().Container.Resolve<GameState>();
var items = Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include).ToDictionary(item => item.Id);
var checks = new List<string>();
var settings = InputSystem.settings;
var background = settings.backgroundBehavior;
var editorInput = settings.editorInputBehaviorInPlayMode;
var previousMouse = Mouse.current;
var previousKeyboard = Keyboard.current;
settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
var mouse = InputSystem.AddDevice<Mouse>();
var keyboard = InputSystem.AddDevice<Keyboard>();
mouse.MakeCurrent();
keyboard.MakeCurrent();
var pointer = Vector2.zero;
ushort buttons = 0;
const string motor = "motor-b";
const string intact = "lamp-intact";
const string cracked = "lamp-cracked";

try
{
    var frame = Time.frameCount;
    await Frames(4);
    Check(Time.frameCount > frame && hud.Expedition.gameObject.activeSelf, "Live Play Mode on the Continue screen");
    Check(Object.FindObjectsByType<EventSystem>().Length == 1, "One active EventSystem");
    var originalKeys = ReadSlot()["world"].Select(row => (string)row["key"]).ToArray();
    await KeyPress(Key.Enter);
    Check(!hud.Expedition.gameObject.activeSelf, "Continued the isolated expedition");

    if (phase == "inventory-resume")
    {
        Check(state.HeldItemId == cracked && At(intact, 0, 0) && At(motor, 2, 0, true) && state.Bag.Count == 2 &&
            items[cracked].IsHeld && !items[intact].gameObject.activeSelf && !items[motor].gameObject.activeSelf,
            "Fresh Play Mode restored two placements and the same cracked lamp in hand");
        await KeyPress(Key.Tab);
        CheckBounds();
        Check(Localized(hand.GetComponentInChildren<Text>().text, "Лампа", "Lamp"), "Hand slot shows the resumed lamp");
        await KeyPress(Key.Q);
        Check(inspection.gameObject.activeSelf && !bag.gameObject.activeSelf && state.HeldItemId == cracked,
            "Q opens inspection from the bag with the resumed instance");
        await KeyPress(Key.Tab);
        Check(bag.gameObject.activeSelf && !inspection.gameObject.activeSelf, "Tab returns to the bag");
        return Result();
    }

    await Aim("crate-a");
    await MouseAt(pointer, 1);
    await LookAt(view.position + Vector3.left + Vector3.up * 0.25f);
    await Frames(35);
    await MouseAt(pointer, 0);
    Check(state.HeldItemId == null, "Moved the crate out of the aisle");
    var settleUntil = Time.fixedTime + 0.5f;
    await Wait.Until(() => Time.fixedTime >= settleUntil);
    await LookAt(view.position + Vector3.forward);
    var aisleStart = view.position.z;
    var walkingSince = Time.time;
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
    await Wait.Until(() => view.position.z >= aisleStart + 0.8f || Time.time - walkingSince > 0.8f);
    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    await Frames(3);
    Check(view.position.z >= aisleStart + 0.75f, "Walked up the cleared aisle toward the packages and lamps");
    await Open("sack-a");
    await Open("parcel-a");
    await Frames(60);
    await Aim(motor);
    await MouseAt(pointer, 1);
    Check(state.HeldItemId == motor && items[motor].IsHeld, "Picked up the motor spilled by the sack");
    await KeyPress(Key.Tab);
    await MouseAt(pointer, 0);
    CheckBounds();
    Check(Localized(hand.GetComponentInChildren<Text>().text, "Моторчик", "Motor"), "Hand slot displays the motor");

    var inHand = File.ReadAllText(slot);
    await MouseAt(Center(hand), 1);
    await KeyPress(Key.R);
    await MouseAt(Cell(2, 0), 1);
    Check(grid.Find("FitPreview").gameObject.activeSelf && state.HeldItemId == motor && !state.HeldItemRotated &&
        File.ReadAllText(slot) == inHand, "Rotated preview leaves the hand and autoslot unchanged before drop");
    await KeyPress(Key.Escape);
    await MouseAt(Cell(2, 0), 0);
    Check(bag.gameObject.activeSelf && state.HeldItemId == motor && !state.HeldItemRotated &&
        !bag.transform.Find("PointerTile").gameObject.activeSelf && File.ReadAllText(slot) == inHand,
        "Esc cancels the active drag and release makes no write");

    await MouseAt(Center(hand), 1);
    await KeyPress(Key.R);
    await MouseAt(Cell(2, 1), 0);
    Check(state.HeldItemId == motor && File.ReadAllText(slot) == inHand &&
        Localized(Feedback(), "Вещь выходит за край", "Item extends past the edge"),
        "Release past the grid edge preserves the original hand and shows the reason");

    await MouseAt(Center(hand), 1);
    await KeyPress(Key.R);
    await MouseAt(Cell(2, 0), 1);
    await MouseAt(Cell(2, 0), 0);
    Check(At(motor, 2, 0, true) && state.HeldItemId == null && !items[motor].gameObject.activeSelf &&
        (bool)Row(motor)["rotated"] && (string)Row(motor)["package"] == "",
        "Dropped the same motor from hand into the grid and saved its rotated placement");

    await Move(Cell(2, 1), Cell(0, 1));
    Check(At(motor, 0, 0, true), "Dragging the second cell moves the whole footprint with its grab offset");
    await MouseAt(Cell(0, 1), 1);
    await MouseAt(Cell(1, 1), 1);
    var beforeClose = File.ReadAllText(slot);
    await KeyPress(Key.Tab);
    await MouseAt(Cell(1, 1), 0);
    await KeyPress(Key.Tab);
    Check(At(motor, 0, 0, true) && File.ReadAllText(slot) == beforeClose, "Closing the bag cancels the drag");
    await Move(Cell(0, 1), Cell(2, 1));
    await Move(Cell(2, 1), Center(hand));
    Check(state.HeldItemId == motor && state.HeldItemRotated && state.Bag.Count == 0 && items[motor].IsHeld &&
        (string)ReadSlot()["held"] == motor && !(bool)Row(motor)["packed"],
        "Dragging to the empty hand slot removes the same motor from the grid and saves it in hand");

    await KeyPress(Key.Tab);
    await Frames(12);
    Check(state.HeldItemId == motor && !items[motor].GetComponent<Collider>().enabled,
        "An item extracted from the bag stays in hand outside the inventory");
    await KeyPress(Key.Tab);
    await Move(Center(hand), Cell(2, 0));
    Check(At(motor, 2, 0, true) && state.HeldItemId == null, "Returned the extracted motor to the same cells");
    await KeyPress(Key.Tab);

    var goodTitle = await Inspect(intact, "Ушко крепления целое", "The hinge tab is intact");
    Check(items[intact].transform.Find("Intact hinge eye") != null, "Intact lamp shows the continuous hinge");
    if (phase == "inventory-intact")
        return Result();

    await KeyPress(Key.Tab);
    Check(bag.gameObject.activeSelf && !inspection.gameObject.activeSelf, "Tab opens the bag directly from inspection");
    await MouseAt(pointer, 0);
    var lampHeld = File.ReadAllText(slot);
    await Move(Cell(2, 0), Center(hand));
    Check(state.HeldItemId == intact && At(motor, 2, 0, true) && File.ReadAllText(slot) == lampHeld &&
        Localized(Feedback(), "Рука занята", "Hand is occupied"), "A drop onto an occupied hand leaves both items in place");
    await Move(Center(hand), Cell(1, 0));
    Check(state.HeldItemId == intact && At(motor, 2, 0, true) && File.ReadAllText(slot) == lampHeld &&
        Localized(Feedback(), "Эти ячейки заняты", "Cells occupied"), "Blocked lamp footprint keeps both items unchanged");
    using (new FileStream(Path.Combine(folder, "slot.pending"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
    {
        await Move(Center(hand), Cell(0, 0));
        Check(state.Error != null && state.HeldItemId == intact && At(motor, 2, 0, true) &&
            File.ReadAllText(slot) == lampHeld, "Autoslot write failure leaves the lamp in hand and the layout unchanged");
    }
    await Move(Center(hand), Cell(0, 0));
    Check(state.Error == null && At(intact, 0, 0) && state.HeldItemId == null, "Retry saves the same lamp in the free cells");
    await KeyPress(Key.Tab);
    await Frames(5);

    var damagedTitle = await Inspect(cracked, "Ушко крепления треснуло", "The hinge tab is cracked");
    Check(damagedTitle == goodTitle && items[cracked].transform.Find("Exposed hinge pin") != null &&
        items[cracked].transform.Find("Loose diffuser edge") != null && items[cracked].transform.Find("Intact hinge eye") == null,
        "Same model marks, visibly different hinge and diffuser geometry before any trade");
    Check(ReadSlot()["world"].Select(row => (string)row["key"]).SequenceEqual(originalKeys),
        "All world instance IDs are preserved exactly once");
    return Result();
}
finally
{
    InputSystem.RemoveDevice(mouse);
    InputSystem.RemoveDevice(keyboard);
    if (previousMouse?.added == true) previousMouse.MakeCurrent();
    if (previousKeyboard?.added == true) previousKeyboard.MakeCurrent();
    settings.backgroundBehavior = background;
    settings.editorInputBehaviorInPlayMode = editorInput;
}

object Result() => new { phase, language, screen = $"{Screen.width}x{Screen.height}", checks };
JObject ReadSlot() => JObject.Parse((string)JObject.Parse(File.ReadAllText(slot))["payload"]);
JToken Row(string id) => ReadSlot()["world"].Single(row => (string)row["key"] == id);
bool At(string id, int column, int row, bool rotated = false) =>
    state.Bag.TryGetPlacement(id, out var p) && p.Column == column && p.Row == row && p.Rotated == rotated;
bool Localized(string actual, string russian, string english) => actual == (language == "en" ? english : russian);
string Feedback() => bag.transform.Find("BagPanel/BagFeedback").GetComponent<Text>().text;

void Check(bool ok, string message)
{
    if (!ok) throw new InvalidOperationException(string.Join("\n", checks.Append("FAILED: " + message)));
    checks.Add(message);
}

void CheckBounds()
{
    Canvas.ForceUpdateCanvases();
    var panel = (RectTransform)bag.transform.Find("BagPanel");
    var edges = new Vector3[4];
    panel.GetWorldCorners(edges);
    var min = RectTransformUtility.WorldToScreenPoint(null, edges[0]);
    var max = RectTransformUtility.WorldToScreenPoint(null, edges[2]);
    Check(min.x > 0f && min.y > 0f && max.x < Screen.width && max.y < Screen.height &&
        bag.CellAt(Cell(2, 1)) == new Vector2Int(2, 1), "Panel and pointer hit targets fit the current window");
    var controls = bag.transform.Find("BagPanel/BagControls").GetComponent<Text>();
    Check(controls.preferredHeight <= controls.rectTransform.rect.height &&
        controls.text.Contains(language == "en" ? "LMB  Drag" : "ЛКМ  Перетащить"), "Localized control hint fits");
}

async Task<string> Inspect(string id, string russian, string english)
{
    await Aim(id);
    await MouseAt(pointer, 1);
    Check(state.HeldItemId == id, "Held " + id);
    await KeyPress(Key.Q);
    var marking = items[id].transform.Find("Model plate");
    var label = marking.GetComponentInChildren<Text>();
    var screen = camera.WorldToViewportPoint(marking.position);
    var title = inspection.transform.Find("InspectionTitle").GetComponent<Text>().text;
    var detail = inspection.transform.Find("InspectionDetail").GetComponent<Text>().text;
    Check(inspection.gameObject.activeSelf && label.text == "SV-04 / 12V" &&
        Vector3.Dot(marking.forward, view.forward) > 0.95f && screen.x is > 0.1f and < 0.9f &&
        screen.y is > 0.1f and < 0.9f && screen.z > 0,
        "The stamp on the real model faces the camera: " + id);
    Check(detail.StartsWith(language == "en" ? english : russian) &&
        Localized(title, "Складная лампа", "Folding lamp"), "Visible condition and name are localized: " + id);
    var rotation = items[id].transform.rotation;
    var cameraRotation = view.rotation;
    await MouseAt(pointer, 1, new Vector2(46f, 13f));
    Check(Quaternion.Angle(items[id].transform.rotation, rotation) > 15f &&
        Quaternion.Angle(view.rotation, cameraRotation) < 0.1f, "Mouse rotates the item independently of the camera: " + id);
    await MouseAt(pointer, 1, new Vector2(-46f, -13f));
    return title;
}

async Task Open(string id)
{
    await Aim(id);
    await MouseAt(pointer, 1);
    await MouseAt(pointer, 0);
    Check((bool)Row(id)["opened"], "Opened " + id);
    await Frames(18);
}

async Task Aim(string id)
{
    var shape = items[id].GetComponent<BoxCollider>();
    var point = (from y in new[] { 0.43f, 0f, -0.40f }
                 from x in new[] { 0f, -0.42f, 0.42f }
                 from z in new[] { 0f, -0.42f, 0.42f }
                 let target = shape.transform.TransformPoint(shape.center + Vector3.Scale(shape.size, new Vector3(x, y, z)))
                 where Physics.Raycast(view.position, target - view.position, out var hit, 3f) && hit.collider == shape
                 select (Vector3?)target).FirstOrDefault();
    Check(point.HasValue, "Within reach from the fixture's aisle: " + id);
    await LookAt(point.Value);
}

Task LookAt(Vector3 point)
{
    var target = Quaternion.LookRotation(point - view.position).eulerAngles;
    var current = view.eulerAngles;
    return MouseAt(pointer, buttons, new Vector2(Mathf.DeltaAngle(current.y, target.y),
        -Mathf.DeltaAngle(current.x, target.x)) / 0.11f);
}

Vector2 Center(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
Vector2 Cell(int column, int row) => RectTransformUtility.WorldToScreenPoint(null, grid.TransformPoint(
    grid.rect.xMin + (column + 0.5f) * BagView.CellSize, grid.rect.yMax - (row + 0.5f) * BagView.CellSize, 0f));

async Task Move(Vector2 from, Vector2 to)
{
    await MouseAt(from, 1);
    await MouseAt(to, 1);
    await MouseAt(to, 0);
}

Task MouseAt(Vector2 position, ushort pressed, Vector2 delta = default)
{
    pointer = position;
    buttons = pressed;
    InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer, buttons = pressed, delta = delta });
    return Frames(3);
}

async Task KeyPress(Key key)
{
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
    await Frames(3);
    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    await Frames(3);
}

async Task Frames(int count)
{
    var end = Time.frameCount + count;
    await Wait.Until(() => Time.frameCount >= end);
}
