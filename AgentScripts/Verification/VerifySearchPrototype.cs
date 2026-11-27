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

// Unity Pipeline script eval, in Editor Play Mode or the local Windows development build.
var checks = new List<string>();
var initialFrame = Time.frameCount;
var inputSettings = InputSystem.settings;
var backgroundBehavior = inputSettings.backgroundBehavior;
var editorInputBehavior = inputSettings.editorInputBehaviorInPlayMode;
var previousKeyboard = Keyboard.current;
var previousMouse = Mouse.current;
inputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
inputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
var keys = InputSystem.AddDevice<Keyboard>();
var mouse = InputSystem.AddDevice<Mouse>();
keys.MakeCurrent();
mouse.MakeCurrent();
var prefix = Application.isEditor ? "editor" : "player";
var starterBag = GameObject.Find("Starter Waist Bag");
var captureDir = Path.GetFullPath(starterBag != null ? "Logs/I-020/search-regression" : "Logs/I-010");
Directory.CreateDirectory(captureDir);
try
{
    await Frames();
    Require(Time.frameCount > initialFrame, "Player loop advances");
    var first = Object.FindAnyObjectByType<SearchSceneScope>();
    var state = first.Container.Resolve<GameState>();
    var player = Object.FindAnyObjectByType<SearchPlayer>();
    var hud = Object.FindAnyObjectByType<SearchHud>();
    var view = player.ViewCamera;
    var origin = player.transform.position;
    Require(Count<SearchPlayer>() == 1 && Count<Camera>() == 1 && Count<SearchHud>() == 1,
        "One player, camera and HUD");
    Require(first.Parent != null && first.Parent.Parent != null, "Scene, run and app scopes");
    Canvas.ForceUpdateCanvases();
    Require(hud.GetComponentsInChildren<Text>().All(text => text.rectTransform.rect.width > 0 &&
        text.rectTransform.rect.height > 0), "HUD text has visible layout bounds");
    if (Cursor.lockState != CursorLockMode.Locked)
        await Click();
    if (starterBag != null)
    {
        await Aim(view, starterBag.transform.position);
        await KeyTap(Key.E);
        Require(!starterBag.activeSelf, "Starter bag unlocked the search route");
    }
    Require(Prompt().Contains("Залезть"), "Climb prompt at the access rim");
    await Capture("entry");
    await KeyTap(Key.E);
    var inside = player.transform.position;
    Require(inside.z > 0 && Prompt().Contains("Вылезти"), "Climb into the container");
    await KeyTap(Key.E);
    Require(Vector3.Distance(player.transform.position, origin) < 0.15f, "Exit initial arrangement");
    await KeyTap(Key.E);
    await KeyHold(Key.W, 0.18f);
    Require(player.transform.position.z > inside.z + 0.25f, "Walk inside");
    await KeyHold(Key.S, 0.17f);

    var crate = FindItem("crate-a");
    var lamp = FindItem("lamp-a");
    var crateStart = crate.transform.position;
    var lampToEye = lamp.transform.position - view.transform.position;
    Require(Physics.Raycast(view.transform.position, lampToEye.normalized,
        out var obstruction, lampToEye.magnitude + 0.25f, Physics.DefaultRaycastLayers)
        && obstruction.collider.GetComponent<SearchItem>() == crate, "Crate occludes the lamp");
    await Aim(view, crate.transform.position);
    Require(Prompt().Contains("Деревянный ящик"), "Ray selects the crate");
    await Capture("covered");
    await Click();
    Require(crate.IsHeld && !crate.GetComponent<Collider>().enabled, "Click picks up the original crate");
    await Click();
    Require(crate.IsHeld, "Placement against the obstruction is refused");
    await Aim(view, new Vector3(1.32f, 0.226f, 2.32f));
    await Click();
    Require(!crate.IsHeld && crate.GetComponent<Collider>().enabled, "Click releases the crate onto the mat");
    await Wait.Until(() => crate.GetComponent<Rigidbody>().IsSleeping());
    var displaced = crate.transform.position;
    Require(Vector3.Distance(crateStart, displaced) > 0.9f && FindItem("crate-a") == crate,
        "Crate is moved without duplication");
    await Aim(view, lamp.transform.position);
    Require(Prompt().Contains("Складная лампа"), "Moving the crate reveals the lamp");
    await Click();
    Require(lamp.IsHeld, "Click picks up the lamp");
    await KeyTap(Key.Q);
    Require(Prompt() == lamp.DisplayName, "Inspection mode shows the name");
    var plate = lamp.GetComponentsInChildren<Text>().Single();
    Require(plate.text == "SV-04 / 12V" && !FacesCamera(view, plate.transform), "Marking starts facing away");
    await MouseDelta(new Vector2(180f / 0.55f, 0));
    Require(FacesCamera(view, plate.transform) && OnScreen(view, plate.transform.position) &&
        OnScreen(view, lamp.transform.Find("Exposed hinge pin").position),
        "Rotation reveals the marking and damaged hinge in view");
    await Capture("inspection");
    await KeyTap(Key.Q);
    Require(lamp.IsHeld && Prompt().Contains("Положить"), "Closing inspection keeps the same lamp in hand");
    await Aim(view, new Vector3(0f, 0.205f, 2.32f));
    await Click();
    Require(!lamp.IsHeld && FindItem("lamp-a") == lamp && lamp.GetComponent<Collider>().enabled,
        "Lamp can be returned to the pile");
    await Wait.Until(() => lamp.GetComponent<Rigidbody>().IsSleeping());
    await KeyTap(Key.E);
    Require(Vector3.Distance(player.transform.position, origin) < 0.15f &&
        Vector3.Distance(crate.transform.position, displaced) < 0.03f, "Exit leaves the moved crate in place");
    await KeyTap(Key.E);
    Require(player.transform.position.z > 0 && FindItem("lamp-a") == lamp, "Re-entry preserves the items");
    await KeyTap(Key.E);
    Require(state.TryGetPose("crate-a", out _), "Run state contains the displaced crate pose");

    await KeyTap(Key.F5);
    await Wait.Until(() => first == null && Object.FindAnyObjectByType<SearchSceneScope>() != null);
    await Frames();
    var second = Object.FindAnyObjectByType<SearchSceneScope>();
    Require(first.Container == null && player == null && view == null && hud == null, "Old scene container, player, camera and HUD are disposed");
    Require(Count<SearchPlayer>() == 1 && Count<Camera>() == 1 && Count<SearchHud>() == 1,
        "Reload creates exactly one player, camera and HUD");
    Require(object.ReferenceEquals(second.Container.Resolve<GameState>(), state) &&
        Vector3.Distance(FindItem("crate-a").transform.position, displaced) < 0.03f,
        "Reload preserves the same run state and the crate position");
    await Capture("reload");
    return new { platform = Application.platform.ToString(), frames = Time.frameCount - initialFrame, checks, captureDir };
}
finally
{
    InputSystem.RemoveDevice(keys);
    InputSystem.RemoveDevice(mouse);
    inputSettings.backgroundBehavior = backgroundBehavior;
    inputSettings.editorInputBehaviorInPlayMode = editorInputBehavior;
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

int Count<T>() where T : Component => Object.FindObjectsByType<T>().Length;

SearchItem FindItem(string itemId) =>
    Object.FindObjectsByType<SearchItem>().Single(item => item.Id == itemId);

string Prompt() => Object.FindAnyObjectByType<SearchHud>().transform.Find("Prompt").GetComponent<Text>().text;

bool FacesCamera(Camera view, Transform plate) =>
    Vector3.Dot(-plate.forward, (view.transform.position - plate.position).normalized) > 0.65f;

bool OnScreen(Camera view, Vector3 position)
{
    var point = view.WorldToViewportPoint(position);
    return point.z > 0 && point.x > 0 && point.x < 1 && point.y > 0 && point.y < 1;
}

async Task Frames()
{
    var frame = Time.frameCount;
    await Wait.Until(() => Time.frameCount >= frame + 3);
}

async Task KeyTap(Key key)
{
    InputSystem.QueueStateEvent(keys, new KeyboardState(key));
    await Frames();
    InputSystem.QueueStateEvent(keys, new KeyboardState());
    await Frames();
}

async Task KeyHold(Key key, float duration)
{
    InputSystem.QueueStateEvent(keys, new KeyboardState(key));
    var end = Time.unscaledTime + duration;
    await Wait.Until(() => Time.unscaledTime >= end);
    InputSystem.QueueStateEvent(keys, new KeyboardState());
    await Frames();
}

async Task Click()
{
    InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
    await Frames();
    InputSystem.QueueStateEvent(mouse, new MouseState());
    await Frames();
}

async Task MouseDelta(Vector2 delta)
{
    InputSystem.QueueDeltaStateEvent(mouse.delta, delta);
    await Frames();
}

async Task Aim(Camera view, Vector3 target)
{
    var angles = Quaternion.LookRotation(target - view.transform.position).eulerAngles;
    var current = view.transform.eulerAngles;
    await MouseDelta(new Vector2(Mathf.DeltaAngle(current.y, angles.y),
        -Mathf.DeltaAngle(current.x, angles.x)) / 0.11f);
}

async Task Capture(string stage)
{
    ScreenCapture.CaptureScreenshot(Path.Combine(captureDir, $"{prefix}-{stage}.png"));
    await Frames();
}
