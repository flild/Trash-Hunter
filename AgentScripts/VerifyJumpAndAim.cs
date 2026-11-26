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

var slot = Environment.GetEnvironmentVariable("TRASH_HUNTER_EDITOR_SAVE_DIR");
if (!Application.isPlaying || string.IsNullOrEmpty(slot) || !Path.IsPathRooted(slot) ||
    File.Exists(Path.Combine(slot, "slot.json")))
    throw new InvalidOperationException("Start Play Mode with a fresh isolated save directory.");

var english = Environment.GetEnvironmentVariable("TRASH_HUNTER_LOCALE") == "en";
var checks = new List<string>();
var settings = InputSystem.settings;
var background = settings.backgroundBehavior;
var editorInput = settings.editorInputBehaviorInPlayMode;
var previousKeyboard = Keyboard.current;
var previousMouse = Mouse.current;
settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
var keyboard = InputSystem.AddDevice<Keyboard>();
var mouse = InputSystem.AddDevice<Mouse>();
keyboard.MakeCurrent();
mouse.MakeCurrent();

try
{
    await Frames();
    var state = Object.FindAnyObjectByType<SearchSceneScope>().Container.Resolve<GameState>();
    var player = Object.FindAnyObjectByType<SearchPlayer>();
    var controller = player.GetComponent<CharacterController>();
    var view = player.ViewCamera.transform;
    var hud = Object.FindAnyObjectByType<SearchHud>();
    var controls = hud.GetComponentsInChildren<Text>().Single(text => text.name == "Controls");
    var items = Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Exclude).ToDictionary(item => item.Id);
    var crate = items["crate-a"];
    var tin = items["tin-a"];
    var cabinet = items["cabinet-a"];
    var outside = GameObject.Find("Climb from yard").transform;
    var rim = GameObject.Find("Roll-off Container/Access rim").GetComponent<Collider>();
    var crateShape = crate.GetComponent<Collider>();
    var tint = new MaterialPropertyBlock();
    var baseColor = Shader.PropertyToID("_BaseColor");

    if (Cursor.lockState != CursorLockMode.Locked)
        await Click(1);
    await Until(() => controller.isGrounded, 1f);
    Check(!state.HasBag && controller.isGrounded, "The live player starts grounded in the yard");
    CheckJumpHint("before bag pickup");
    var floorY = player.transform.position.y;
    await Keys(0.36f, Key.Space);
    Check(!controller.isGrounded && player.transform.position.y - floorY > 0.70f, "Space jumps from the yard floor");
    await Keys(0f, Key.Space);
    await Seconds(0.28f);
    Check(player.transform.position.y - floorY < 0.73f, "Another Space press in the air does not relaunch the player");
    await Until(() => controller.isGrounded, 1f);
    Check(controller.isGrounded && Mathf.Abs(player.transform.position.y - floorY) < controller.skinWidth + 0.025f,
        $"The player lands back on the yard floor ({floorY:F3} to {player.transform.position.y:F3})");
    floorY = player.transform.position.y;

    await AimAt(GameObject.Find("Starter Waist Bag").GetComponent<Collider>().bounds.center, view);
    await Keys(0f, Key.E);
    Check(state.HasBag, "Original bag pickup remains available");
    CheckJumpHint("with the bag");

    PlacePlayer(player, new Vector3(4f, 0f, -2.5f));
    PlaceItem(crate, new Vector3(4f, 0.29f, -0.6f));
    PlaceItem(tin, new Vector3(5.4f, 0.10f, -0.8f));
    await Seconds(0.16f);
    await AimAt(crateShape.bounds.center, view);
    Check(IsTinted(crate) && !IsTinted(tin), "Only the first reachable item is tinted");
    ScreenCapture.CaptureScreenshot(Path.Combine(slot, "aim-item.png"));
    await Frames();
    await AimAt(tin.GetComponent<Collider>().bounds.center, view);
    Check(IsTinted(tin) && !IsTinted(crate), "Turning transfers the tint to the second item");
    await Keys(0f, Key.Tab);
    Check(hud.Bag.gameObject.activeSelf && !IsTinted(tin), "Opening the bag clears the tint");
    await Keys(0f, Key.Tab);
    await AimAt(tin.GetComponent<Collider>().bounds.center, view);
    Check(IsTinted(tin), "Returning to search reacquires the target");
    await Aim(new Vector3(0f, 0.7f, 1f), view);
    Check(!IsTinted(tin) && !IsTinted(crate), "Aiming into empty space clears the tint");
    PlaceItem(tin, new Vector3(4f, 0.10f, 1.1f));
    await AimAt(tin.GetComponent<Collider>().bounds.center, view);
    Check(!IsTinted(tin), "A find beyond pickup range is not tinted");

    player.Place(outside);
    PlaceItem(tin, new Vector3(0f, 0.32f, 0.52f));
    await Seconds(0.18f);
    await AimAt(tin.GetComponent<Collider>().bounds.center, view);
    Check(Physics.Raycast(view.position, view.forward, out var hit, 3f, Physics.DefaultRaycastLayers) &&
        hit.collider == rim && !IsTinted(tin) && !HasTint(rim.GetComponent<Renderer>()),
        "The container wall occludes the find and has no tint of its own");
    ScreenCapture.CaptureScreenshot(Path.Combine(slot, "aim-container.png"));
    await Frames();
    PlaceItem(tin, new Vector3(5.4f, 0.10f, 2f));

    PlacePlayer(player, crateShape.bounds.center + Vector3.up * (crateShape.bounds.extents.y + 0.09f));
    await Until(() => controller.isGrounded, 1f);
    var supportY = player.transform.position.y;
    await AimAt(crateShape.bounds.center, view);
    Check(controller.isGrounded && supportY > floorY + 0.4f &&
        Physics.Raycast(view.position, view.forward, out hit, 3f, Physics.DefaultRaycastLayers) &&
        hit.collider == crateShape && !IsTinted(crate), "The aimed crate is the player's current support");
    var supportPosition = crateShape.bounds.center;
    await Click(1);
    await Seconds(0.3f);
    Check(state.HeldItemId == null && !crate.IsHeld && Mathf.Abs(player.transform.position.y - supportY) < 0.05f &&
        Vector3.Distance(crateShape.bounds.center, supportPosition) < 0.05f, "LMB cannot lift the crate from under the player");
    await Keys(0.34f, Key.Space);
    Check(!controller.isGrounded && player.transform.position.y - supportY > 0.70f, "Space jumps from the stable crate");
    await Until(() => controller.isGrounded, 1.2f);
    Check(controller.isGrounded && Mathf.Abs(player.transform.position.y - supportY) < controller.skinWidth + 0.025f,
        "The player lands back on the crate");

    PlacePlayer(player, new Vector3(4f, 0f, -2.5f));
    await AimAt(crateShape.bounds.center, view);
    Check(IsTinted(crate), "The same crate is available from the ground");
    await Click(1);
    Check(crate.IsHeld && state.HeldItemId == crate.Id && !IsTinted(crate), "LMB grabs the crate and clears its tint");
    CheckJumpHint("while holding a find");
    await Click(2);
    Check(state.HeldItemId == null, "Throw still releases the crate");

    player.Place(outside);
    await Keys(0f, Key.E);
    Check(player.transform.position.z > 1f, "E crosses the container rim from the yard");
    var cabinetBody = cabinet.GetComponent<Rigidbody>();
    await AimAt(cabinetBody.position, view);
    await Click(1);
    Check(state.HeldItemId == cabinet.Id && cabinetBody.mass == 24f, "The production cabinet can be grabbed");
    await Aim(Vector3.forward, view);
    await Keys(0.21f, Key.D);
    await Aim(Vector3.right, view);
    for (var i = 0; i < 6; i++)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0f, 120f) });
        await Frames();
    }
    await Aim(new Vector3(0.22f, 1f, 0f), view);
    await Until(() => cabinetBody.position.y > 2.45f, 4f);
    await Aim(new Vector3(1f, 0.74f, 0f), view);
    await Until(() => cabinetBody.position.x > 2.85f && cabinetBody.position.y > 2.15f, 4f);
    Check(cabinetBody.position.x > 2.85f && cabinetBody.position.y > 2.15f, "Dragging takes the cabinet over the wall");
    await Click(2);

    // Isolate the traversal from random contacts by settling the movable crate at the access rim.
    PlaceItem(crate, new Vector3(0f, 0.50f, 0.58f));
    await Seconds(0.25f);
    PlacePlayer(player, crateShape.bounds.center + Vector3.up * (crateShape.bounds.extents.y + 0.08f));
    await Aim(Vector3.forward, view);
    await Until(() => controller.isGrounded, 1f);
    Check(controller.isGrounded && player.transform.position.y > 0.65f, "The crate provides a grounded step inside the rim");
    await Keys(0.53f, Key.Space, Key.S);
    await Until(() => controller.isGrounded, 1.5f);
    Check(controller.isGrounded && player.transform.position.z < -0.8f &&
        Mathf.Abs(player.transform.position.y - floorY) < 0.07f, "The player jumps out after moving the cabinet and lands in the yard");
    await Keys(0f, Key.E);
    Check(player.transform.position.z > 1f, "After the physical crossing E again enters from the outside");
    await Keys(0f, Key.E);
    Check(player.transform.position.z < -0.8f, "E still exits after the cabinet has been moved");
    await Keys(0.78f, Key.S);
    await Keys(0f, Key.E);
    Check(hud.Expedition.gameObject.activeSelf &&
        hud.Expedition.GetComponentsInChildren<Text>().Any(text => text.text == "Уйти домой?"),
        "The yard exit opens the return confirmation");

    return new { locale = english ? "en" : "ru", passed = checks.Count, checks };

    void CheckJumpHint(string context) =>
        Check(controls.text.Contains(english ? "Space  Jump" : "Пробел  Прыжок") &&
            controls.preferredWidth <= controls.rectTransform.rect.width, $"Localized jump controls fit {context}");

    bool IsTinted(SearchItem item) => item.GetComponentsInChildren<MeshRenderer>().Any(HasTint);

    bool HasTint(Renderer renderer)
    {
        renderer.GetPropertyBlock(tint);
        return !tint.isEmpty && tint.GetColor(baseColor) != renderer.sharedMaterial.GetColor(baseColor);
    }
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

void Check(bool passed, string description)
{
    if (!passed)
        throw new InvalidOperationException(string.Join("\n", checks.Append("FAILED: " + description)));
    checks.Add(description);
}

static void PlaceItem(SearchItem item, Vector3 position)
{
    var body = item.GetComponent<Rigidbody>();
    body.position = position;
    body.rotation = Quaternion.identity;
    body.transform.SetPositionAndRotation(position, Quaternion.identity);
    body.linearVelocity = Vector3.zero;
    body.angularVelocity = Vector3.zero;
    body.WakeUp();
    Physics.SyncTransforms();
}

static void PlacePlayer(SearchPlayer player, Vector3 position)
{
    var marker = new GameObject("Verification start");
    marker.transform.position = position;
    player.Place(marker.transform);
    Object.Destroy(marker);
}

async Task AimAt(Vector3 position, Transform view) => await Aim(position - view.position, view);

async Task Aim(Vector3 direction, Transform view)
{
    var target = Quaternion.LookRotation(direction).eulerAngles;
    var current = view.eulerAngles;
    InputSystem.QueueDeltaStateEvent(mouse.delta,
        new Vector2(Mathf.DeltaAngle(current.y, target.y), -Mathf.DeltaAngle(current.x, target.x)) / 0.11f);
    await Frames();
}

async Task Keys(float seconds, params Key[] keys)
{
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    await Frames();
    if (seconds > 0f)
        await Seconds(seconds);
    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    await Frames();
}

async Task Click(ushort buttons)
{
    InputSystem.QueueStateEvent(mouse, new MouseState { buttons = buttons });
    await Frames();
    InputSystem.QueueStateEvent(mouse, new MouseState());
    await Frames();
}

async Task Frames()
{
    var target = Time.frameCount + 3;
    await Wait.Until(() => Time.frameCount >= target);
}

async Task Seconds(float duration)
{
    var target = Time.fixedTime + duration;
    await Wait.Until(() => Time.fixedTime >= target);
}

async Task Until(Func<bool> condition, float seconds)
{
    var deadline = Time.fixedTime + seconds;
    await Wait.Until(() => condition() || Time.fixedTime >= deadline);
}
