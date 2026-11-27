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

var folder = Environment.GetEnvironmentVariable("TRASH_HUNTER_EDITOR_SAVE_DIR");
var phase = Environment.GetEnvironmentVariable("TRASH_HUNTER_CHECK");
var english = Environment.GetEnvironmentVariable("TRASH_HUNTER_LOCALE") == "en";
if (!Application.isPlaying || string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder))
    throw new InvalidOperationException("Start Play Mode with an isolated absolute save directory.");

var path = Path.Combine(folder, "slot.json");
var checks = new List<string>();
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

try
{
    var frame = Time.frameCount;
    await Frames();
    Check(Time.frameCount > frame, "The player loop advances");
    var state = Object.FindAnyObjectByType<SearchSceneScope>().Container.Resolve<GameState>();
    var player = Object.FindAnyObjectByType<SearchPlayer>();
    var camera = player.ViewCamera.transform;
    var hud = Object.FindAnyObjectByType<SearchHud>();
    var items = Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include).ToDictionary(item => item.Id);
    var cabinet = items["cabinet-a"];
    var tin = items["tin-a"];
    var crate = items["crate-a"];
    var cabinetBody = Body(cabinet);

    if (phase == "drag")
    {
        var layoutIds = items.Values.Where(item => item.gameObject.activeSelf).Select(item => item.Id).ToHashSet();
        Check(!File.Exists(path) && cabinet.gameObject.activeSelf && cabinetBody.mass == 24f, "A fresh layout includes the 24 kg cabinet");
        var starter = GameObject.Find("Starter Waist Bag").GetComponent<Collider>();
        await Aim(starter.bounds.center - camera.position, camera);
        await Tap(Key.E);
        Check(state.HasBag, "Search starts by picking up the original bag");

        // Use the clear yard to compare travel over the same interval.
        var cabinetSpawn = cabinetBody.position;
        var cabinetRotation = cabinetBody.rotation;
        var tinBody = Body(tin);
        MovePlayer(player, new Vector3(4f, 0f, -2f));
        SetPose(tinBody, new Vector3(4f, 0.10f, 0.65f), Quaternion.identity);
        await FixedTime(0.12f);
        var tinStart = tinBody.position.z;
        await Aim(tinBody.position - camera.position, camera);
        await Click(1);
        await FixedTime(0.34f);
        Check(ActiveHold(tin, state), "The tin stays dynamic and collidable in hand");
        var tinTravel = tinStart - tinBody.position.z;
        var forward = camera.forward;
        await Click(2);
        Check(state.HeldItemId == null && !tin.IsHeld && Vector3.Dot(tinBody.linearVelocity, forward) > 2.5f,
            "RMB releases the original tin with velocity along the view");
        await FixedTime(0.50f);
        SetPose(tinBody, new Vector3(5.5f, 0.10f, 2f), Quaternion.identity);

        SetPose(cabinetBody, new Vector3(4f, 0.57f, 0.65f), Quaternion.identity);
        await FixedTime(0.12f);
        var cabinetStart = cabinetBody.position.z;
        await Aim(cabinetBody.position - camera.position, camera);
        await Click(1);
        await FixedTime(0.34f);
        var cabinetTravel = cabinetStart - cabinetBody.position.z;
        Check(ActiveHold(cabinet, state) && cabinetTravel > 0.10f && tinTravel > cabinetTravel * 1.6f,
            $"Same pull interval moves tin {tinTravel:F2} m and cabinet {cabinetTravel:F2} m");
        var prompt = hud.GetComponentsInChildren<Text>().Single(text => text.name == "Prompt");
        var controls = hud.GetComponentsInChildren<Text>().Single(text => text.name == "Controls");
        Check(prompt.text.Contains(english ? "RMB  Throw" : "ПКМ  Бросить") &&
            controls.text.Contains(english ? "Wheel  Distance" : "Колесо  Дальность") &&
            prompt.preferredWidth <= prompt.rectTransform.rect.width && controls.preferredWidth <= controls.rectTransform.rect.width,
            "Localized throw and distance hints fit");
        await Tap(Key.Q);
        var inspected = cabinet.transform.rotation;
        await Aim(Quaternion.Euler(0f, 32f, 0f) * camera.forward, camera);
        Check(prompt.text == (english ? "Cabinet" : "Шкаф") && Quaternion.Angle(inspected, cabinet.transform.rotation) > 10f,
            "Q still opens the rotatable, localized inspection");
        await Tap(Key.Q);
        Check(ActiveHold(cabinet, state), "Closing inspection returns the same dynamic cabinet");
        await Aim(new Vector3(0.20f, -0.90f, 1f), camera);
        await Click(1);
        Check(!cabinet.IsHeld && state.HeldItemId == null, "LMB still places the item quietly on an aimed clear surface");
        SetPose(cabinetBody, cabinetSpawn, cabinetRotation);

        var crateBody = Body(crate);
        SetPose(crateBody, new Vector3(4f, 0.29f, 0.20f), Quaternion.identity);
        SetPose(tinBody, new Vector3(4.24f, 0.10f, -0.35f), Quaternion.identity);
        await FixedTime(0.10f);
        var tinBeforeContact = tinBody.position;
        await Aim(crateBody.position - camera.position, camera);
        await Click(1);
        Check(ActiveHold(crate, state), "The crate uses the same physical pickup");
        await Aim(new Vector3(0f, -2.25f, 1f), camera);
        await FixedTime(0.85f);
        Check(Vector3.Distance(tinBeforeContact, tinBody.position) > 0.10f,
            $"The collidable crate pushes the tin {Vector3.Distance(tinBeforeContact, tinBody.position):F2} m");
        await Click(2);
        Check(state.HeldItemId == null, "Releasing the crate clears the hand");

        player.Place(GameObject.Find("Climb from yard").transform);
        await Tap(Key.E);
        Check(player.transform.position.z > 1f, "E enters the existing container");
        await Aim(cabinetBody.position - camera.position, camera);
        await Click(1);
        Check(ActiveHold(cabinet, state), "The cabinet can be grabbed in the production layout");
        await Aim(Vector3.forward, camera);
        await Move(Key.D, 0.21f);
        await Aim(Vector3.right, camera);
        for (var i = 0; i < 6; i++)
            await Scroll();
        await FixedTime(1.5f);
        var blockedX = cabinetBody.position.x;
        Check(blockedX < 2.19f, $"Horizontal pull is blocked by the right wall at x={blockedX:F2}");
        await Aim(new Vector3(0.22f, 1f, 0f), camera);
        await Until(() => cabinetBody.position.y > 2.45f, 3f);
        Check(cabinetBody.position.y > 2.45f, $"Tilting up first lifts the cabinet above the wall at {cabinetBody.position}");
        await Aim(new Vector3(1f, 0.74f, 0f), camera);
        await Until(() => cabinetBody.position.x > 2.85f && cabinetBody.position.y > 2.15f, 4f);
        Check(cabinetBody.position.x > 2.85f && cabinetBody.position.y > 2.15f,
            $"Looking up clears the rim with the cabinet at {cabinetBody.position} from {player.transform.position}");
        await Tap(Key.F5);
        var snapshot = Saved();
        Check((string)snapshot["held"] == cabinet.Id && (bool)snapshot["heldInWorld"] &&
            Position(snapshot).x > 2.85f && snapshot["world"].Count() == layoutIds.Count &&
            layoutIds.SetEquals(snapshot["world"].Select(item => (string)item["key"])),
            "F5 records the outside pose and preserves the original IDs");
    }
    else if (phase == "drag-resume")
    {
        var snapshot = Saved();
        Check(ActiveSavedHold(cabinet, state) && cabinetBody.isKinematic &&
            Vector3.Distance(cabinetBody.position, Position(snapshot)) < 0.04f,
            "A new Play Mode restores the same held cabinet outside the wall at its saved pose");
        await Tap(Key.Enter);
        Check(ActiveHold(cabinet, state), "Continue restores the dynamic hold");
        await Click(2);
        Check(state.HeldItemId == null && !cabinet.IsHeld && !cabinetBody.isKinematic,
            "Throwing the restored cabinet releases it into the world");
    }
    else if (phase == "drag-legacy")
    {
        Check(state.Error == null && state.HeldItemId == tin.Id && tin.IsHeld &&
            !cabinet.gameObject.activeSelf && items.Values.Count(item => item.gameObject.activeSelf) == 24 &&
            !tin.GetComponent<Collider>().enabled, "The pre-cabinet 24-item save retains its layout and old hand");
        await Tap(Key.Enter);
        await FixedTime(0.18f);
        Check(ActiveHold(tin, state), "The legacy held tin becomes physical once the space in front is clear");
        await Click(2);
        Check(state.HeldItemId == null && !tin.IsHeld && tin.GetComponent<Collider>().enabled,
            "The migrated tin can be thrown and saved as a world item");
    }
    else
        throw new InvalidOperationException("Use drag, drag-resume or drag-legacy.");

    return new { phase, locale = english ? "en" : "ru", passed = checks.Count, checks };
}
finally
{
    InputSystem.RemoveDevice(mouse);
    InputSystem.RemoveDevice(keyboard);
    if (oldMouse?.added == true) oldMouse.MakeCurrent();
    if (oldKeyboard?.added == true) oldKeyboard.MakeCurrent();
    settings.backgroundBehavior = oldBackground;
    settings.editorInputBehaviorInPlayMode = oldEditorInput;
}

static Rigidbody Body(SearchItem item) => item.GetComponent<Rigidbody>();
static bool ActiveSavedHold(SearchItem item, GameState state) =>
    item.IsHeld && state.HeldItemId == item.Id && item.GetComponent<Collider>().enabled;
static bool ActiveHold(SearchItem item, GameState state) => ActiveSavedHold(item, state) && !Body(item).isKinematic;

void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(string.Join("\n", checks.Append("FAILED: " + description)));
    checks.Add(description);
}

JObject Saved() => JObject.Parse((string)JObject.Parse(File.ReadAllText(path))["payload"]);
static Vector3 Position(JObject save)
{
    var pose = save["world"].Single(item => (string)item["key"] == (string)save["held"])["position"];
    return new Vector3((float)pose["x"], (float)pose["y"], (float)pose["z"]);
}

static void SetPose(Rigidbody body, Vector3 position, Quaternion rotation)
{
    body.position = position;
    body.rotation = rotation;
    body.transform.SetPositionAndRotation(position, rotation);
    body.linearVelocity = Vector3.zero;
    body.angularVelocity = Vector3.zero;
    Physics.SyncTransforms();
}

static void MovePlayer(SearchPlayer player, Vector3 position)
{
    var start = new GameObject("Verification start");
    start.transform.position = position;
    player.Place(start.transform);
    Object.Destroy(start);
}

async Task Aim(Vector3 direction, Transform camera)
{
    var desired = Quaternion.LookRotation(direction).eulerAngles;
    var current = camera.eulerAngles;
    InputSystem.QueueDeltaStateEvent(mouse.delta,
        new Vector2(Mathf.DeltaAngle(current.y, desired.y), -Mathf.DeltaAngle(current.x, desired.x)) / 0.11f);
    await Frames();
}

async Task Click(ushort buttons)
{
    InputSystem.QueueStateEvent(mouse, new MouseState { buttons = buttons });
    await Frames();
    InputSystem.QueueStateEvent(mouse, new MouseState());
    await Frames();
}

async Task Tap(Key key) => await Move(key, 0f);
async Task Move(Key key, float seconds)
{
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
    await Frames();
    await FixedTime(seconds);
    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    await Frames();
}

async Task Scroll()
{
    InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0f, 120f) });
    await Frames();
}

async Task Until(Func<bool> condition, float seconds)
{
    var deadline = Time.fixedTime + seconds;
    await Wait.Until(() => condition() || Time.fixedTime >= deadline);
}

async Task FixedTime(float seconds)
{
    var end = Time.fixedTime + seconds;
    await Wait.Until(() => Time.fixedTime >= end);
}

async Task Frames()
{
    var frame = Time.frameCount;
    await Wait.Until(() => Time.frameCount >= frame + 3);
}
