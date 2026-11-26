using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrashHunter;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;

var phase = Environment.GetEnvironmentVariable("TRASH_HUNTER_CHECK");
var english = Environment.GetEnvironmentVariable("TRASH_HUNTER_LOCALE") == "en";
var folder = Environment.GetEnvironmentVariable("TRASH_HUNTER_EDITOR_SAVE_DIR");
if (string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder))
    throw new InvalidOperationException("Set an isolated absolute Editor save directory.");

var primary = Path.Combine(folder, "slot.json");
var lamp = "lamp-a";
var radio = "radio-a";
var strip = "strip-a";
var ids = new[] { "crate-a", lamp, radio, strip, "dial-a" };
if (phase == "bag-seed")
{
    if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
        throw new InvalidOperationException("The fixture needs an empty directory.");

    // A legacy five-item expedition, with a vertical lamp and a radio already packed.
    var payload = JsonConvert.SerializeObject(new
    {
        format = 1, runId = Guid.NewGuid().ToString("N"), revision = 1, utcTicks = DateTime.UtcNow.Ticks,
        expedition = 1, hasBag = true,
        player = new { position = new { x = 0f, y = 0.22f, z = 1.08f }, pitch = 28f, inside = true },
        world = ids.Select((key, index) => new
        {
            key,
            position = new { x = index == 3 ? 0f : -1.4f + index * 0.64f, y = 0.45f, z = index == 3 ? 2.55f : 4.2f },
            rotation = new { x = 0, y = 0, z = 0, w = 1 },
            packed = key == lamp || key == radio,
            column = key == lamp ? 1 : key == radio ? 2 : 0
        }),
        home = Array.Empty<object>()
    });
    using var hash = SHA256.Create();
    var checksum = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    Directory.CreateDirectory(folder);
    using var stream = new FileStream(primary, FileMode.CreateNew, FileAccess.Write);
    using var writer = new StreamWriter(stream);
    writer.Write(JsonConvert.SerializeObject(new { payload, checksum }));
    return new { phase, folder };
}

if (!Application.isPlaying)
    throw new InvalidOperationException("Run this phase in Play Mode.");

var checks = new List<string>();
var captures = new List<string>();
var captureDir = Path.GetFullPath("Logs/I-060");
Directory.CreateDirectory(captureDir);
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
var frame = Time.frameCount;

try
{
    await Frames();
    Check(Time.frameCount > frame, "The player loop advances");
    var state = Object.FindAnyObjectByType<SearchSceneScope>().Container.Resolve<GameState>();
    var hud = Object.FindAnyObjectByType<SearchHud>();
    var bag = hud.Bag;
    var originalInstances = Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include).ToDictionary(item => item.Id);
    var stripItem = originalInstances[strip];
    var heading = hud.Expedition.GetComponentsInChildren<Text>(true).Single(t => t.name == "Heading");
    Check(heading.text == "Продолжить", "The isolated autoslot opens through Continue");
    await Tap(Key.Enter);
    Check(Cursor.lockState == CursorLockMode.Locked, "Continue returns to search");
    if (phase == "bag-resume")
    {
        Check(state.Bag.Count == 3 && At(lamp, 0, 0, true) && At(radio, 2, 0) && At(strip, 0, 1, true) &&
            state.HeldItemId == null && !stripItem.gameObject.activeSelf && KeysMatch(Save()),
            "A fresh Play Mode restores the three packed instances with the correct rotations and an empty hand");
        await Tap(Key.Tab);
        await Point(1, 0);
        TextIs("BagSelection", "Складная лампа", "Folding lamp");
        await Capture("packed", inspectText: true);
        await Click(MouseButton.Left);
        Check(state.HeldItemId == lamp && originalInstances[lamp].IsHeld && state.HeldItemRotated &&
            state.Bag.Count == 2 && At(radio, 2, 0) && At(strip, 0, 1, true), "LMB still takes a packed item through its second cell");
    }
    else if (phase == "bag-before" || phase == "bag-move")
    {
        Check(state.Bag.Count == 2 && At(lamp, 1, 0) && At(radio, 2, 0) && state.HeldItemId == null,
            "The lamp spans the center column and the radio is in the top-right cell");
        var settledAt = Time.fixedTime + 0.6f;
        await Wait.Until(() => Time.fixedTime >= settledAt);
        var view = Object.FindAnyObjectByType<SearchPlayer>().ViewCamera.transform;
        var desired = Quaternion.LookRotation(stripItem.transform.position - view.position).eulerAngles;
        var current = view.eulerAngles;
        InputSystem.QueueDeltaStateEvent(mouse.delta,
            new Vector2(Mathf.DeltaAngle(current.y, desired.y), -Mathf.DeltaAngle(current.x, desired.x)) / 0.11f);
        await Frames();
        await Click(MouseButton.Left);
        Check(HoldingStrip(false), "LMB picks up the original strip from the world");
        await Tap(Key.Tab);
        await Tap(Key.R);
        var originalSlot = File.ReadAllText(primary);
        await Point(0, 1);
        TextIs("BagFeedback", "Эти ячейки заняты", "Cells occupied");
        await Click(MouseButton.Left);
        Check(OriginalLayout(), "A rail spanning the bottom row cannot be packed yet");
        await Point(1, 0);
        await Capture("blocked", inspectText: phase == "bag-move");
        await Click(MouseButton.Right);

        if (phase == "bag-before")
        {
            Check(OriginalLayout() && ReadText("BagSelection").Contains("Рейка") && File.ReadAllText(primary) == originalSlot,
                "Before I-060, the occupied tile cannot be selected while the strip is held");
        }
        else
        {
            TextIs("BagTitle", "Поясная сумка", "Waist bag");
            TextIs("BagSelection", "Перенос · Лампа   ·   1 × 2", "Moving · Lamp   ·   1 × 2");
            await Tap(Key.R);
            TextIs("BagSelection", "Перенос · Лампа   ·   2 × 1", "Moving · Lamp   ·   2 × 1");
            await Click(MouseButton.Left);
            TextIs("BagFeedback", "Эти ячейки заняты", "Cells occupied");
            Check(OriginalLayout(), "Overlap with the other packed item preserves both items and the hand");
            await Point(2, 0);
            await Click(MouseButton.Left);
            TextIs("BagFeedback", "Вещь выходит за край", "Item extends past the edge");
            Check(OriginalLayout(), "A destination outside the grid preserves the original layout");
            await Capture("outside", inspectText: true);
            await Tap(Key.Escape);
            Check(bag.gameObject.activeSelf && OriginalLayout() && File.ReadAllText(primary) == originalSlot,
                "Esc cancels the pending move without writing or closing the bag");

            await Point(1, 1);
            await Click(MouseButton.Right);
            await Tap(Key.R);
            await Tap(Key.Tab);
            Check(!bag.gameObject.activeSelf && OriginalLayout() && File.ReadAllText(primary) == originalSlot,
                "Tab closes the bag and discards the pending move");
            await Tap(Key.Tab);
            await Point(1, 1);
            await Click(MouseButton.Right);
            await Click(MouseButton.Right);
            Check(OriginalLayout() && File.ReadAllText(primary) == originalSlot, "RMB cancels the pending move");
            await Click(MouseButton.Right);
            await Tap(Key.R);
            await Point(0, 0);
            TextIs("BagFeedback", "ЛКМ  Переместить", "LMB  Move here");
            await Capture("move", inspectText: true);
            var pending = Path.Combine(folder, "slot.pending");
            Directory.CreateDirectory(pending);
            try
            {
                await Click(MouseButton.Left);
                Check(state.Error != null && OriginalLayout() && File.ReadAllText(primary) == originalSlot,
                    "A failed autosave leaves the original layout, slot and hand intact");
            }
            finally
            {
                Directory.Delete(pending);
            }

            await Click(MouseButton.Left);
            Check(state.Error == null, "Retrying after the write failure clears the error");
            Check(state.Bag.Count == 2 && At(lamp, 0, 0, true) && At(radio, 2, 0) && HoldingStrip(true) &&
                !originalInstances[lamp].gameObject.activeSelf, "A valid move may overlap its own old footprint");
            var saved = Save();
            Check(KeysMatch(saved) && (string)saved["held"] == strip && (bool)saved["heldRotated"] &&
                (int)Entry(saved, lamp)["column"] == 0 && (bool)Entry(saved, lamp)["rotated"],
                "Autosave writes the rotated lamp without changing the hand or any ID");
            await Point(0, 1);
            TextIs("BagFeedback", "ЛКМ  Уложить", "LMB  Pack");
            await Capture("ready", inspectText: true);
            await Click(MouseButton.Left);
            Check(state.Bag.Count == 3 && At(lamp, 0, 0, true) && At(radio, 2, 0) && At(strip, 0, 1, true) &&
                state.HeldItemId == null && !stripItem.gameObject.activeSelf, "The original strip fits in the freed row");
            var packedSave = Save();
            Check(string.IsNullOrEmpty((string)packedSave["held"]) && KeysMatch(packedSave) &&
                (bool)Entry(packedSave, strip)["packed"] && (bool)Entry(packedSave, strip)["rotated"] &&
                (int)Entry(packedSave, strip)["row"] == 1, "Packing writes the same five IDs and the correct strip placement");
        }
    }
    else
    {
        throw new InvalidOperationException("Use bag-seed, bag-before, bag-move or bag-resume.");
    }

    return new { phase, language = english ? "en" : "ru", resolution = $"{Screen.width}×{Screen.height}", checks, captures };

    bool At(string id, int column, int row, bool rotated = false) =>
        state.Bag.TryGetPlacement(id, out var placement) &&
        placement.Column == column && placement.Row == row && placement.Rotated == rotated;

    bool HoldingStrip(bool rotated) => state.HeldItemId == strip && stripItem.IsHeld &&
        state.HeldItemRotated == rotated &&
        stripItem.GetComponent<Collider>().enabled == (phase != "bag-before");

    bool OriginalLayout() => state.Bag.Count == 2 && At(lamp, 1, 0) && At(radio, 2, 0) && HoldingStrip(true);

    string ReadText(string name) => bag.transform.Find("BagPanel/" + name).GetComponent<Text>().text;

    void TextIs(string name, string ru, string en)
    {
        var expected = english ? en : ru;
        var actual = ReadText(name);
        Check(actual == expected, $"{name} reads '{expected}' (actual '{actual}')");
    }

    async Task Point(int column, int row)
    {
        var grid = (RectTransform)bag.transform.Find("BagPanel/BagGrid");
        Canvas.ForceUpdateCanvases();
        pointer = RectTransformUtility.WorldToScreenPoint(null, grid.TransformPoint(
            new Vector3((column + 0.5f) * BagView.CellSize, -(row + 0.5f) * BagView.CellSize)));
        InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
        await Frames();
        Check(bag.CellAt(mouse.position.ReadValue()) == new Vector2Int(column, row), $"Pointer addresses cell {column},{row}");
    }

    async Task Capture(string name, bool inspectText)
    {
        Canvas.ForceUpdateCanvases();
        if (inspectText)
        {
            foreach (var label in bag.GetComponentsInChildren<Text>().Where(t => t.text.Length > 0))
            {
                var rect = label.rectTransform.rect;
                Check(label.preferredHeight <= rect.height + 1 && label.preferredWidth <= rect.width + 1,
                    $"{label.name} fits ({label.text})");
            }
        }
        var path = Path.Combine(captureDir, $"{phase}-{(english ? "en" : "ru")}-{name}.png");
        ScreenCapture.CaptureScreenshot(path);
        await Frames();
        await Wait.Until(() => File.Exists(path));
        captures.Add(path);
    }
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

JObject Save() => JObject.Parse(JObject.Parse(File.ReadAllText(primary)).Value<string>("payload"));
JToken Entry(JObject save, string id) => save["world"].Single(item => (string)item["key"] == id);
bool KeysMatch(JObject save) => ids.OrderBy(id => id).SequenceEqual(
    save["world"].Select(item => (string)item["key"]).OrderBy(id => id));

void Check(bool pass, string description)
{
    if (!pass)
        throw new InvalidOperationException(description + " failed. Passed: " + string.Join(" | ", checks));
    checks.Add(description);
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

async Task Click(MouseButton button)
{
    InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer }.WithButton(button));
    await Frames();
    InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
    await Frames();
}
