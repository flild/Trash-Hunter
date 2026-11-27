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
using UnityEngine.Rendering;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;

var args = Environment.GetCommandLineArgs();
var folder = Option("-trash-hunter-save-dir");
if (Application.isEditor || string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder))
    throw new InvalidOperationException("Start the Windows QA Player with an isolated save directory.");

var phase = Option("-trash-hunter-check");
var english = Option("-trash-hunter-locale") == "en";
var primary = Path.Combine(folder, "slot.json");
var checks = new List<string>();
var samples = new List<object>();
var input = InputSystem.settings;
var previousBehavior = input.backgroundBehavior;
var previousBackground = Application.runInBackground;
var previousKeyboard = Keyboard.current;
var previousMouse = Mouse.current;
var keyboard = InputSystem.AddDevice<Keyboard>();
var mouse = InputSystem.AddDevice<Mouse>();
input.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
Application.runInBackground = true;
keyboard.MakeCurrent();
mouse.MakeCurrent();
var pointer = Vector2.zero;
var quit = false;

try
{
    await Frames();
    var state = State();
    if (phase == "i120-first")
    {
        var first = Save();
        Check((int)first["pileSeed"] == 706296053 && state.Expedition == 1 && !state.HasBag &&
            state.HeldItemId == null && first["world"].Count() == 27, "The fixed generated pile starts in the yard with no bag or hand item");
        Overlay("Heading", "Продолжить", "Continue");
        await Tap(Key.Enter);

        var player = Player();
        var controller = player.GetComponent<CharacterController>();
        await Until(() => controller.isGrounded, 1);
        var startY = player.transform.position.y;
        await Tap(Key.Space);
        await Until(() => player.transform.position.y > startY + 0.7f, 0.5f);
        Check(!controller.isGrounded && player.transform.position.y > startY + 0.7f, "Space jumps from the yard floor");
        await Until(() => controller.isGrounded, 1.5f);
        Check(controller.isGrounded && Mathf.Abs(player.transform.position.y - startY) < 0.12f, "The player lands in the yard");

        await AimAt(GameObject.Find("Starter Waist Bag").GetComponent<Collider>().bounds.center);
        await Tap(Key.E);
        Check(state.HasBag, "E equips the waist bag");
        await Tap(Key.E);
        Check(player.transform.position.z > 1f, "E crosses the rim into the generated pile");

        var bodies = Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Exclude)
            .ToDictionary(item => item.Id, item => item.GetComponent<Rigidbody>());
        var cabinet = bodies["cabinet-a"];
        Check(bodies.Count == 23 && cabinet.mass == 24f, "The closed pile has 23 active bodies and a 24 kg cabinet");
        await Aim(new Vector3(0.45f, -0.45f, 1f));
        await Until(() => bodies.Values.All(body => body.IsSleeping()), 6f);
        Check(bodies.Values.All(body => body.IsSleeping()), "The active pile settles before measuring");
        await Measure("rest", () => false, 3f);

        await AimAt(cabinet.position);
        Label(Prompt(), "ЛКМ  Взять  ·  Шкаф", "LMB  Take  ·  Cabinet");
        await Click(1);
        Check(state.HeldItemId == "cabinet-a" && !cabinet.isKinematic, "LMB grabs the dynamic cabinet");
        await Aim(Vector3.forward);
        await HoldUntil(Key.D, () => player.transform.position.x >= 0.96f, 1f);
        await Aim(Vector3.right);
        for (var i = 0; i < 6; i++)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0f, 120f) });
            await Frames();
        }
        await Aim(new Vector3(0.22f, 1f, 0f));
        await Measure("lift", () => cabinet.position.y > 2.45f, 6f);
        Check(cabinet.position.y > 2.45f, $"The cabinet clears the height of the wall ({cabinet.position})");
        await Aim(new Vector3(1f, 0.74f, 0f));
        await Measure("over-rim", () => cabinet.position.x > 2.85f && cabinet.position.y > 2.15f, 6f);
        Check(cabinet.position.x > 2.85f && cabinet.position.y > 2.15f, $"The cabinet crosses the side wall ({cabinet.position})");

        var throwStart = cabinet.position;
        var throwDirection = Vector3.ProjectOnPlane(player.ViewCamera.transform.forward, Vector3.up).normalized;
        await Click(2);
        await Until(() => Vector3.Dot(cabinet.position - throwStart, throwDirection) > 0.025f, 0.5f);
        Check(state.HeldItemId == null && !Item("cabinet-a").IsHeld && !cabinet.isKinematic &&
            Vector3.Dot(cabinet.position - throwStart, throwDirection) > 0.025f, "RMB throws the cabinet in the direction of view");

        await Aim(Vector3.forward);
        await HoldUntil(Key.A, () => player.transform.position.x <= 0.13f, 1f);
        await OpenPackage("sack-a", false, "Мешок", "Sack");
        Check(TextOf("Details").text.Contains(english ? "Plug" : "Вилка") &&
            TextOf("Details").text.Contains(english ? "Motor" : "Моторчик"), "The sack lists both choices in the selected language");
        await Capture("sack");
        await Tap(Key.DownArrow);
        await Tap(Key.Enter);
        Check(state.HeldItemId == "motor-b", "Down and Enter take the second find from the sack");
        await Tap(Key.Q);
        Label(Prompt(), "Моторчик  ·  Вал погнут", "Motor  ·  Shaft is bent");
        await Tap(Key.Q);
        await Tap(Key.Tab);
        await Tap(Key.R);
        await Point(1, 0);
        await Click(1);
        Check(Placed("motor-b", 1, 0, true), "The motor is packed vertically across the centre column");
        await Tap(Key.Tab);

        await OpenPackage("sack-a", true, "Мешок", "Sack");
        Overlay("Details", "›  Вилка", "›  Plug");
        await Tap(Key.Enter);
        Check(state.HeldItemId == "plug-a", "Reopening the sack lets the player take the remaining plug");
        await Tap(Key.Tab);
        await Point(2, 0);
        await Click(1);
        Check(Placed("plug-a", 2, 0), "The plug occupies the top-right cell");
        await Tap(Key.Tab);

        await OpenPackage("parcel-a", false, "Картонная коробка", "Cardboard box");
        await Tap(Key.DownArrow);
        await Tap(Key.Enter);
        Check(state.HeldItemId == "adapter-a", "The box yields the selected adapter");
        await Tap(Key.Tab);
        Label(BagText("BagTitle"), "Поясная сумка", "Waist bag");
        await Point(0, 1);
        Label(BagText("BagFeedback"), "Эти ячейки заняты", "Cells occupied");
        await Click(1);
        Check(state.HeldItemId == "adapter-a" && Placed("motor-b", 1, 0, true),
            "The rejected placement keeps the adapter in hand and the motor in place");
        await Point(1, 0);
        await Click(2);
        await Tap(Key.R);
        await Point(0, 0);
        Label(BagText("BagFeedback"), "ЛКМ  Переместить", "LMB  Move here");
        await Capture("move");
        await Click(1);
        Check(state.HeldItemId == "adapter-a" && Item("adapter-a").IsHeld && Placed("motor-b", 0, 0) &&
            Placed("plug-a", 2, 0), "Moving and rotating the motor preserves the plug and the exact adapter in hand");
        await Point(0, 1);
        Label(BagText("BagFeedback"), "ЛКМ  Уложить", "LMB  Pack");
        await Click(1);
        Check(state.HeldItemId == null && Placed("adapter-a", 0, 1) && state.Bag.Count == 3 &&
            (bool)Row(Save(), "adapter-a")["packed"], "The adapter fits in the freed row and is autosaved");
        await Tap(Key.Tab);

        await Tap(Key.E);
        Check(player.transform.position.z < -0.8f, "The exit over the rim remains available");
        var exit = GameObject.Find("Yard Exit").transform.position;
        await AimAt(new Vector3(exit.x, player.ViewCamera.transform.position.y, exit.z));
        await HoldUntil(Key.W, () => Vector2.Distance(new Vector2(player.transform.position.x, player.transform.position.z),
            new Vector2(exit.x, exit.z)) < 0.8f, 3f);
        await Tap(Key.E);
        Overlay("Heading", "Уйти домой?", "Go home?");
        await Tap(Key.Enter);
        Overlay("Heading", "Дома", "Home");
        Check(state.AtHome && state.Home.Select(item => item.key).ToHashSet()
            .SetEquals(new[] { "motor-b", "plug-a", "adapter-a" }), "Only the three packed instances arrive home");
        var adapter = state.Home.Select((item, index) => (item, index)).Single(row => row.item.key == "adapter-a");
        var price = state.Quote(adapter.item).Price;
        for (var i = 0; i < adapter.index; i++)
            await Tap(Key.DownArrow);
        await Tap(Key.Enter);
        Overlay("Heading", "Продать вещь?", "Sell this item?");
        Overlay("Summary", $"{price} ₽", $"{price} ₽");
        Overlay("Details", "Адаптер\nРазъём цел.", "Adapter\nThe socket is intact.");
        await Capture("offer");
        await Tap(Key.Enter);
        Overlay("Feedback", $"Адаптер  +{price} ₽\nРазъём цел.", $"Adapter  +{price} ₽\nThe socket is intact.");
        Check(state.Balance == price && state.Home.Count == 2 && state.Home.All(item => item.key != "adapter-a"),
            $"Only the selected adapter is sold and the balance increases by {price}");

        await Tap(Key.E);
        await Until(() => State().Expedition == 2 && Player() != player, 6f);
        state = State();
        var second = Save();
        Check(state.Expedition == 2 && !state.AtHome && state.Bag.Count == 0 && state.Balance == price &&
            state.Home.Count == 2 && (int)second["pileSeed"] != (int)first["pileSeed"] &&
            !JToken.DeepEquals(Row(first, "crate-a")["position"], Row(second, "crate-a")["position"]),
            "Departing generates a different pile and preserves balance and unsold finds");
        await Tap(Key.E);
        Check(Player().transform.position.z > 1f, "The second pile can be entered");
        await OpenPackage("sack-a", false, "Мешок", "Sack");
        await Tap(Key.Enter);
        Check(state.HeldItemId == "plug-a" && (bool)Row(Save(), "sack-a")["opened"],
            "The second expedition has a changed open-package state and an item in hand");
        await Tap(Key.Escape);
        Overlay("Heading", "Пауза", "Paused");
        quit = true;
    }
    else if (phase == "i120-resume")
    {
        var saved = Save();
        Overlay("Heading", "Продолжить", "Continue");
        Overlay("Summary", "Вылазка 2", "Expedition 2");
        Check(state.Expedition == 2 && !state.AtHome && state.HasBag && state.Bag.Count == 0 &&
            state.Balance == (long)saved["cash"] && state.Balance > 0 && state.Home.Count == 2,
            "A new process restores expedition, balance, bag and both unsold finds");
        Check(state.HeldItemId == "plug-a" && Item("plug-a").IsHeld &&
            Item("sack-a").transform.Find("Open mouth").gameObject.activeSelf &&
            (string)Row(saved, "motor-b")["package"] == "sack-a",
            "The same hand item, open package and remaining contents are restored");
        Check(Vector3.Distance(Player().transform.position, Position(saved["player"]["position"])) < 0.06f,
            "The player starts at the saved position");
        await Tap(Key.Enter);
        Check(!Hud().Expedition.gameObject.activeSelf && Cursor.lockState == CursorLockMode.Locked &&
            state.HeldItemId == "plug-a", "Enter resumes the saved expedition and retains the held item");
        Label(Prompt(), "ЛКМ  Положить     ПКМ  Бросить     Q  Осмотреть", "LMB  Place     RMB  Throw     Q  Inspect");
        var nearby = Hud().transform.Find("Nearby action").GetComponent<Text>();
        Label(nearby, "E  Вылезти", "E  Climb out");
        var handPrompt = Prompt().rectTransform;
        Check(nearby.rectTransform.anchoredPosition.y - handPrompt.anchoredPosition.y >=
            (nearby.rectTransform.rect.height + handPrompt.rect.height) / 2f,
            "The two prompts do not overlap on the first frame at the rim");
        await Tap(Key.Escape);
        Overlay("Heading", "Пауза", "Paused");
        quit = true;
    }
    else
        throw new InvalidOperationException("Use i120-first or i120-resume.");

    return new
    {
        phase, locale = english ? "en" : "ru", pid = System.Diagnostics.Process.GetCurrentProcess().Id,
        checks, samples, screen = $"{Screen.width}x{Screen.height}", quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
        api = SystemInfo.graphicsDeviceType.ToString(), cpu = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName,
        memoryMiB = SystemInfo.systemMemorySize, fixedStep = Time.fixedDeltaTime, vsync = QualitySettings.vSyncCount,
        targetRate = Application.targetFrameRate
    };
}
finally
{
    InputSystem.RemoveDevice(mouse);
    InputSystem.RemoveDevice(keyboard);
    if (previousMouse?.added == true) previousMouse.MakeCurrent();
    if (previousKeyboard?.added == true) previousKeyboard.MakeCurrent();
    input.backgroundBehavior = previousBehavior;
    Application.runInBackground = previousBackground;
    if (quit)
        QuitAfterReply();
}

async void QuitAfterReply()
{
    Application.runInBackground = true;
    input.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
    await Task.Delay(1500);
    var exitKeyboard = InputSystem.AddDevice<Keyboard>();
    exitKeyboard.MakeCurrent();
    InputSystem.QueueStateEvent(exitKeyboard, new KeyboardState(Key.Q));
}

string Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

GameState State() => Object.FindAnyObjectByType<SearchSceneScope>().Container.Resolve<GameState>();
SearchPlayer Player() => Object.FindAnyObjectByType<SearchPlayer>();
SearchHud Hud() => Object.FindAnyObjectByType<SearchHud>();
SearchItem Item(string id) => Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include).Single(item => item.Id == id);
Text Prompt() => Hud().transform.Find("Prompt").GetComponent<Text>();
Text TextOf(string name) => Hud().Expedition.transform.Find($"ExpeditionPanel/{name}").GetComponent<Text>();
Text BagText(string name) => Hud().Bag.transform.Find($"BagPanel/{name}").GetComponent<Text>();
JObject Save() => JObject.Parse((string)JObject.Parse(File.ReadAllText(primary))["payload"]);
static JToken Row(JObject save, string id) => save["world"].Single(row => (string)row["key"] == id);
static Vector3 Position(JToken p) => new((float)p["x"], (float)p["y"], (float)p["z"]);

void Check(bool pass, string name)
{
    if (!pass)
        throw new InvalidOperationException(string.Join("\n", checks.Append("FAILED: " + name)));
    checks.Add(name);
}

void Label(Text label, string ru, string en, bool stripMarkup = false)
{
    Canvas.ForceUpdateCanvases();
    var actual = stripMarkup ? System.Text.RegularExpressions.Regex.Replace(label.text, "<[^>]+>", "") : label.text;
    var expected = english ? en : ru;
    var rect = label.rectTransform.rect;
    Check(label.gameObject.activeInHierarchy && actual == expected &&
        label.preferredWidth <= rect.width + 1 && label.preferredHeight <= rect.height + 1,
        $"{label.name} fits: expected [{expected}], actual [{actual}], {label.preferredWidth}x{label.preferredHeight} in {rect.size}");
}

void Overlay(string name, string ru, string en)
{
    Label(TextOf(name), ru, en, true);
    var actions = TextOf("Actions");
    Check(actions.preferredHeight <= actions.rectTransform.rect.height + 1,
        $"{name} overlay actions fit: {actions.text}");
}

bool Placed(string id, int column, int row, bool rotated = false) =>
    State().Bag.TryGetPlacement(id, out var item) && item.Column == column && item.Row == row && item.Rotated == rotated;

async Task OpenPackage(string id, bool opened, string ru, string en)
{
    var shape = Item(id).GetComponent<BoxCollider>();
    var eye = Player().ViewCamera.transform.position;
    var point = (from x in new[] { 0f, -0.34f, 0.34f }
                 from y in new[] { 0.31f, 0f, -0.31f }
                 from z in new[] { 0f, -0.34f, 0.34f }
                 let candidate = shape.transform.TransformPoint(shape.center + Vector3.Scale(shape.size, new Vector3(x, y, z)))
                 where Physics.Raycast(eye, candidate - eye, out var hit, 3f, Physics.DefaultRaycastLayers,
                     QueryTriggerInteraction.Ignore) && hit.collider == shape
                 select (Vector3?)candidate).FirstOrDefault();
    Check(point.HasValue, $"{id} is within unobstructed reach from {Player().transform.position}");
    await AimAt(point.Value);
    Label(Prompt(), (opened ? "ЛКМ  Заглянуть  ·  " : "ЛКМ  Открыть  ·  ") + ru,
        (opened ? "LMB  Look inside  ·  " : "LMB  Open  ·  ") + en);
    Check(Hud().transform.Find("Package reticle").gameObject.activeSelf, "The package reticle indicates the interaction");
    await Click(1);
    Overlay("Heading", ru, en);
}

async Task Measure(string name, Func<bool> finished, float seconds)
{
    var camera = Player().ViewCamera;
    var values = new List<float>();
    var lastFrame = -1;
    RenderPipelineManager.endCameraRendering += Sample;
    try
    {
        await Until(finished, seconds);
    }
    finally
    {
        RenderPipelineManager.endCameraRendering -= Sample;
    }

    Check(values.Count > 0, $"{name} produced rendered camera frames");
    values.Sort();
    samples.Add(new { phase = name, frames = values.Count, medianMs = Quantile(0.5f), p95Ms = Quantile(0.95f), maxMs = values.Last() });

    float Quantile(float p) => values[Mathf.CeilToInt(values.Count * p) - 1];
    void Sample(ScriptableRenderContext context, Camera rendered)
    {
        if (rendered != camera || lastFrame == Time.frameCount)
            return;
        lastFrame = Time.frameCount;
        values.Add(Time.unscaledDeltaTime * 1000f);
    }
}

async Task Capture(string name)
{
    ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
    await Frames();
}

async Task Point(int column, int row)
{
    var grid = (RectTransform)Hud().Bag.transform.Find("BagPanel/BagGrid");
    Canvas.ForceUpdateCanvases();
    pointer = RectTransformUtility.WorldToScreenPoint(null, grid.TransformPoint(
        new Vector3((column + 0.5f) * BagView.CellSize, -(row + 0.5f) * BagView.CellSize)));
    InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
    await Frames();
    Check(Hud().Bag.CellAt(mouse.position.ReadValue()) == new Vector2Int(column, row), $"The pointer addresses cell {column},{row}");
}

async Task Click(ushort buttons)
{
    InputSystem.QueueStateEvent(mouse, new MouseState { buttons = buttons, position = pointer });
    await Frames();
    InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
    await Frames();
}

Task Tap(Key key) => HoldUntil(key, () => true, 0);

async Task HoldUntil(Key key, Func<bool> finished, float seconds)
{
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
    await Frames();
    await Until(finished, seconds);
    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    await Frames();
}

Task AimAt(Vector3 target) => Aim(target - Player().ViewCamera.transform.position);

async Task Aim(Vector3 direction)
{
    var desired = Quaternion.LookRotation(direction).eulerAngles;
    var current = Player().ViewCamera.transform.eulerAngles;
    InputSystem.QueueDeltaStateEvent(mouse.delta,
        new Vector2(Mathf.DeltaAngle(current.y, desired.y), -Mathf.DeltaAngle(current.x, desired.x)) / 0.11f);
    await Frames();
}

async Task Frames()
{
    var target = Time.frameCount + 3;
    await Wait.Until(() => Time.frameCount >= target);
}

async Task Until(Func<bool> condition, float seconds)
{
    var end = Time.realtimeSinceStartup + seconds;
    await Wait.Until(() => condition() || Time.realtimeSinceStartup >= end);
}
