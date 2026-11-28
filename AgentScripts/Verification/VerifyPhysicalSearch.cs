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
if (!Application.isPlaying || string.IsNullOrEmpty(folder))
    throw new InvalidOperationException("Start with an isolated copy of the I-110 fixture or a partially opened legacy slot.");
var path = Path.Combine(folder, "slot.json");
var initial = Read();
var state = Object.FindAnyObjectByType<SearchSceneScope>().Container.Resolve<GameState>();
var player = Object.FindAnyObjectByType<SearchPlayer>();
var view = player.ViewCamera.transform;
var hud = Object.FindAnyObjectByType<SearchHud>();
var prompt = hud.transform.Find("Prompt").GetComponent<Text>();
var controls = hud.transform.Find("Controls").GetComponent<Text>();
var items = Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include).ToDictionary(i => i.Id);
var checks = new List<string>();
var settings = InputSystem.settings;
var background = settings.backgroundBehavior;
var input = settings.editorInputBehaviorInPlayMode;
var prevMouse = Mouse.current;
var prevKeyboard = Keyboard.current;
settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
var keyboard = InputSystem.AddDevice<Keyboard>();
var mouse = InputSystem.AddDevice<Mouse>();
try
{
    keyboard.MakeCurrent();
    mouse.MakeCurrent();
    await Frames(5);
    if (Opened("parcel-a", initial))
    {
        foreach (var id in new[] { "sack-a", "parcel-a", "motor-b", "plug-a", "fuse-a", "adapter-a" })
        {
            var row = Row(initial, id);
            var position = row["position"].ToObject<Vector3>();
            var body = items[id].GetComponent<Rigidbody>();
            Check(items[id].gameObject.activeSelf && Vector3.Distance(body.position, position) < 0.002f &&
                Quaternion.Angle(body.rotation, row["rotation"].ToObject<Quaternion>()) < 0.1f,
                "Restored instance and saved pose: " + id);
        }
        await Keys(Key.Enter);
        Check(!hud.Expedition.gameObject.activeSelf, "Continued the saved expedition");
        return checks;
    }

    await Keys(Key.Enter);
    Check(!hud.Expedition.gameObject.activeSelf, "Continued the fixture");
    if (Cursor.lockState != CursorLockMode.Locked) { await Buttons(1); await Buttons(0); }
    if (Opened("sack-a", initial))
    {
        Check((string)initial["held"] == "plug-a" && (string)Row(initial,"motor-b")["package"] == "sack-a",
            "Legacy slot had plug in hand and only motor nested");
        Check(items["plug-a"].gameObject.activeSelf && !items["motor-b"].gameObject.activeSelf,
            "Legacy hand remains accessible");
        await Aim("sack-a");
        await Buttons(1);
        await Buttons(0);
        var save = Read();
        Check(string.IsNullOrEmpty((string)Row(save,"motor-b")["package"]) && items["motor-b"].gameObject.activeSelf &&
            state.HeldItemId == null && items["plug-a"].gameObject.activeSelf &&
            save["world"].Count() == initial["world"].Count(), "Only the remaining motor spilled");
        return checks;
    }

    await Aim("crate-a");
    var crate = items["crate-a"].GetComponent<Rigidbody>();
    var start = crate.position;
    await Buttons(1);
    Check(state.HeldItemId == "crate-a", "Grabbed obstructing crate");
    await AimPoint(view.position + Vector3.left + Vector3.up * 0.25f);
    await Frames(35);
    Check(Vector3.Distance(crate.position,start) > 0.12f, "Dragged crate with LMB held");
    await Buttons(0);
    Check(state.HeldItemId == null && !crate.isKinematic, "Released crate in place");

    await Aim("sack-a");
    Check(controls.rectTransform.anchorMin == Vector2.zero && !controls.text.Contains("WASD") &&
        prompt.fontSize <= 18 && prompt.rectTransform.anchoredPosition.y <= -55, "Compact aim and corner bindings");
    await Keys(Key.H);
    Check(!controls.enabled && prompt.enabled, "H hides bindings only");
    await Keys(Key.H);
    Check(controls.enabled, "H restores bindings");

    var unchanged = File.ReadAllText(path);
    using (new FileStream(Path.Combine(folder,"slot.pending"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
    {
        await Buttons(1);
        await Buttons(0);
        Check(state.Error != null && File.ReadAllText(path) == unchanged && !items["plug-a"].gameObject.activeSelf &&
            !items["motor-b"].gameObject.activeSelf, "Failed write kept the sack and both nested objects intact");
    }
    await Open("sack-a", "plug-a", "motor-b");
    await Open("parcel-a", "fuse-a", "adapter-a");
    // The spilled motor must be reachable from the same aisle once the pile settles.
    await Frames(60);
    var find = "motor-b";
    await Aim(find);
    Check(!prompt.text.Contains("ЛКМ") && !prompt.text.Contains("LMB"), "Loose find has a name-only caption");
    await Buttons(1);
    Check(state.HeldItemId == find, "Picked up spilled motor");
    await Keys(Key.Q);
    await Buttons(0);
    Check(state.HeldItemId == find && !items[find].GetComponent<Collider>().enabled, "Inspection owns mouse release");
    await Buttons(1);
    await Keys(Key.Q);
    await Keys(Key.Tab);
    await Buttons(0);
    Check(state.HeldItemId == find && hud.Bag.gameObject.activeSelf, "Bag owns mouse release");
    await Buttons(1);
    await Keys(Key.Tab);
    await Buttons(3);
    Check(state.HeldItemId == null && items[find].GetComponent<Rigidbody>().linearVelocity.magnitude > 1f,
        "RMB threw the same find after returning to world input");
    await Buttons(0);

    var outside = GameObject.Find("Climb from yard").transform;
    var inside = GameObject.Find("Climb from container").transform;
    player.Place(inside);
    await Keys(Key.E);
    Check(Vector3.Distance(player.transform.position, outside.position) < 0.15f, "Used the rim exit");
    await AimPoint(view.position + Vector3.back);
    var walk = await Speed(Key.W);
    var sprint = await Speed(Key.W, Key.LeftShift);
    Check(sprint > walk * 1.45f, $"Walk {walk:F2}, sprint {sprint:F2} m/s");
    await Frames(8);
    var ground = player.transform.position.y;
    var began = Time.time;
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
    await Wait.Until(() => player.transform.position.y > ground + 0.12f || Time.time - began > 0.3f);
    Check(player.transform.position.y > ground + 0.12f, "Jumped from stable ground");
    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    await Wait.Until(() => player.GetComponent<CharacterController>().isGrounded || Time.time - began > 1f);
    Check(Time.time - began < 0.75f, $"Landed in {Time.time-began:F2} s");

    await Keys(Key.F5);
    await Wait.Until(() => Object.FindAnyObjectByType<SearchPlayer>() != player);
    Check(Opened("parcel-a", Read()) && Read()["world"].Count() == initial["world"].Count(),
        "Saved both opened packages and all instance IDs");
    return checks;

    async Task Open(string id, params string[] contents)
    {
        await Aim(id);
        await Buttons(1);
        var atOpen = Read();
        Check(!hud.Expedition.gameObject.activeSelf && Opened(id, atOpen) && contents.All(key =>
            string.IsNullOrEmpty((string)Row(atOpen,key)["package"]) && items[key].gameObject.activeSelf &&
            !items[key].GetComponent<Rigidbody>().isKinematic), "Physical spill: " + id);
        await Buttons(0);
        await Frames(18);
        Check(contents.All(key => Vector3.Distance(Row(atOpen,key)["position"].ToObject<Vector3>(),
            items[key].GetComponent<Rigidbody>().position) > 0.015f), "Both bodies moved: " + id);
        await Aim(id);
        var revision = (long)Read()["revision"];
        await Buttons(1);
        await Buttons(0);
        Check((long)Read()["revision"] == revision, "Repeated opening made no new write: " + id);
    }

    async Task Aim(string id)
    {
        var shape = items[id].GetComponent<BoxCollider>();
        var point = (from y in new[] { 0.43f, 0f, -0.40f }
                     from x in new[] { 0f, -0.42f, 0.42f }
                     from z in new[] { 0f, -0.42f, 0.42f }
                     let target = shape.transform.TransformPoint(shape.center + Vector3.Scale(shape.size, new Vector3(x,y,z)))
                     where Physics.Raycast(view.position,target-view.position,out var hit,3f) && hit.collider == shape
                     select (Vector3?)target).FirstOrDefault();
        Check(point.HasValue, "Reachable from the aisle: " + id);
        await AimPoint(point.Value);
    }

    async Task AimPoint(Vector3 point)
    {
        var target = Quaternion.LookRotation(point-view.position).eulerAngles;
        var current = view.eulerAngles;
        InputSystem.QueueDeltaStateEvent(mouse.delta,new Vector2(Mathf.DeltaAngle(current.y,target.y),
            -Mathf.DeltaAngle(current.x,target.x))/0.11f);
        await Frames(3);
    }
    async Task<float> Speed(params Key[] keys)
    {
        var position = player.transform.position;
        var time = Time.time;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
        await Wait.Until(() => Time.time-time >= 0.25f);
        var speed = Vector3.ProjectOnPlane(player.transform.position-position, Vector3.up).magnitude/(Time.time-time);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());
        await Frames(3);
        return speed;
    }
}
finally
{
    InputSystem.RemoveDevice(keyboard);
    InputSystem.RemoveDevice(mouse);
    if (prevMouse?.added == true) prevMouse.MakeCurrent();
    if (prevKeyboard?.added == true) prevKeyboard.MakeCurrent();
    settings.backgroundBehavior = background;
    settings.editorInputBehaviorInPlayMode = input;
}
JObject Read() => JObject.Parse((string)JObject.Parse(File.ReadAllText(path))["payload"]);
JToken Row(JObject snapshot,string id) => snapshot["world"].Single(row => (string)row["key"] == id);
bool Opened(string id,JObject snapshot) => (bool?)Row(snapshot,id)["opened"] == true;
void Check(bool success,string message)
{
    if (!success) throw new InvalidOperationException(string.Join("\n",checks.Append("FAILED: " + message)));
    checks.Add(message);
}
async Task Buttons(ushort buttons)
{
    InputSystem.QueueStateEvent(mouse,new MouseState { buttons = buttons });
    await Frames(3);
}
async Task Keys(Key key)
{
    InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));
    await Frames(3);
    InputSystem.QueueStateEvent(keyboard,new KeyboardState());
    await Frames(3);
}
async Task Frames(int count)
{
    var end = Time.frameCount+count;
    await Wait.Until(() => Time.frameCount >= end);
}
