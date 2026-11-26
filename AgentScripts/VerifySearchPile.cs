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
using VContainer;
using Object = UnityEngine.Object;

var folder = Environment.GetEnvironmentVariable("TRASH_HUNTER_EDITOR_SAVE_DIR");
var phase = Environment.GetEnvironmentVariable("TRASH_HUNTER_CHECK");
if (!Application.isPlaying || string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder) ||
    phase is not ("pile" or "pile-resume" or "pile-legacy"))
    throw new InvalidOperationException("Select an isolated slot and a verification phase before starting Play Mode.");

var slot = Path.Combine(folder, "slot.json");
var checks = new List<string>();
var expectedSlots = new List<int>();
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
    if (phase == "pile-legacy")
    {
        CheckRestored(ReadSlot(), 24);
        Check(!FindItems()["cabinet-a"].gameObject.activeSelf &&
            (int?)ReadSlot()["pileGeneratorVersion"] is null or 0, "The legacy expedition has no cabinet or generator metadata");
        await KeyInput(Key.Enter);
        if (!State().HasBag)
        {
            await AimAt(GameObject.Find("Starter Waist Bag").GetComponent<Collider>().bounds.center, Player());
            await KeyInput(Key.E);
            Check(State().HasBag, "Bag pickup works after loading before the old tutorial");
        }
        await TraverseRim();
    }
    else if (phase == "pile-resume")
    {
        var snapshot = ReadSlot();
        Check((int)snapshot["pileGeneratorVersion"] == 1 && (int)snapshot["expedition"] == 3,
            "A new Play Mode opens the third generated expedition");
        CheckRestored(snapshot, 25);
        Check(Position(FindItem(snapshot, "cabinet-a")).x > 2.75f, "The released cabinet remains outside");
        await KeyInput(Key.Enter);
        await TraverseRim();
    }
    else
    {
        Check(!File.Exists(slot) && !State().HasBag, "A fresh expedition starts with the original bag pickup");
        await AimAt(GameObject.Find("Starter Waist Bag").GetComponent<Collider>().bounds.center, Player());
        await KeyInput(Key.E);
        Check(State().HasBag, "The bag can be picked up");

        var original = ReadSlot();
        for (var expedition = 1; expedition <= 3; expedition++)
        {
            var initial = ReadSlot();
            Check(State().Expedition == expedition && (int)initial["pileGeneratorVersion"] == 1 &&
                (string)initial["runId"] == (string)original["runId"], $"Expedition {expedition} uses the current generator");
            expectedSlots.Add((int)initial["pileSeed"]);
            Check(expectedSlots.Distinct().Count() == expedition, $"Expedition {expedition} has a new seed");
            if (expedition > 1)
                Check(initial["world"].Count(item => Vector3.Distance(Position(item),
                    Position(FindItem(original, (string)item["key"]))) > 0.2f) >= 13,
                    $"Expedition {expedition} moves at least half the original finds");
            Check(initial["world"].Count() == 25 && initial["world"].Select(item => (string)item["key"]).Distinct().Count() == 25,
                $"Expedition {expedition} has the full set of unique instances");

            var player = Player();
            var start = GameObject.Find("Climb from yard").transform;
            var inside = GameObject.Find("Climb from container").transform;
            Check(Vector3.Distance(player.transform.position, start.position) < 0.15f, "The player starts at the rim");
            await KeyInput(Key.E);
            Check(Vector3.Distance(player.transform.position, inside.position) < 0.15f, "E enters at the clear landing");

            var cabinet = FindItems()["cabinet-a"].GetComponent<Rigidbody>();
            await AimAt(cabinet.worldCenterOfMass, player);
            await Click(1);
            Check(State().HeldItemId == "cabinet-a", "The generated cabinet is reachable from the landing");
            await Aim(Vector3.forward, player);
            await KeyInput(Key.D, 0.21f);
            for (var scroll = 0; scroll < 6; scroll++)
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0f, 120f) });
                await Frames();
            }
            await Aim(new Vector3(0.22f, 1f, 0f), player);
            await Until(() => cabinet.position.y > 2.45f, 4f);
            await Aim(new Vector3(1f, 0.74f, 0f), player);
            await Until(() => cabinet.position.x > 2.85f && cabinet.position.y > 2.15f, 4f);
            Check(cabinet.position.x > 2.85f && cabinet.position.y > 2.15f,
                $"Pulling lifts the cabinet over the right wall ({cabinet.position:F2})");
            await Click(2);
            Check(State().HeldItemId == null, "Throw releases the cabinet outside");

            await Aim(Vector3.forward, player);
            await KeyInput(Key.A, 0.24f);
            await KeyInput(Key.W, 0.22f);
            var find = FindReachableFind(player);
            var movedBody = find.Shape.attachedRigidbody;
            var findStart = movedBody.position;
            await AimAt(find.Target, player);
            await Click(1);
            Check(State().HeldItemId == find.Id, $"A visible item can be grabbed from the heap ({find.Id})");
            var towardFind = findStart - player.transform.position;
            towardFind.y = 0;
            await Aim(towardFind.normalized + Vector3.up, player);
            await Until(() => movedBody.position.y > findStart.y + 0.5f, 3f);
            Check(movedBody.position.y > findStart.y + 0.5f, $"Pulling lifts {find.Id} clear of its neighbors");
            await Click(2);
            Check(State().HeldItemId == null && Vector3.Distance(movedBody.position, findStart) > 0.4f,
                "Pull and release rearrange the generated heap");

            await Aim(Vector3.forward, player);
            await KeyInput(Key.S, 0.16f);
            await KeyInput(Key.E);
            Check(Vector3.Distance(player.transform.position, start.position) < 0.20f,
                $"E exits after the heap is disturbed ({player.transform.position:F2})");
            var oldSession = Session();
            await KeyInput(Key.F5);
            await Until(() => Session() != null && Session() != oldSession, 4f);
            Check(Session() != oldSession, "F5 reloads the search scene");
            var saved = ReadSlot();
            Check((int)saved["pileSeed"] == (int)initial["pileSeed"] &&
                Vector3.Distance(Position(FindItem(saved, find.Id)), Position(FindItem(initial, find.Id))) > 0.4f &&
                Position(FindItem(saved, "cabinet-a")).x > 2.75f,
                "F5 writes the original seed and both moved positions");
            CheckRestored(saved, 25, frozen: false);
            if (expedition == 3)
                break;

            Player().Place(GameObject.Find("Yard Exit").transform);
            await KeyInput(Key.E);
            await KeyInput(Key.Enter);
            Check(State().AtHome, "Confirming return finishes the current expedition");
            oldSession = Session();
            await KeyInput(Key.E);
            await Until(() => Session() != null && Session() != oldSession, 4f);
            Check(Session() != oldSession && State().Expedition == expedition + 1, "E departs and reloads the search scene");
        }
    }
    return new { phase, passed = checks.Count, seeds = expectedSlots, checks };
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

void CheckRestored(JObject snapshot, int count, bool frozen = true)
{
    var state = State();
    var items = FindItems();
    var world = snapshot["world"].ToArray();
    Check(state.Error == null && state.HasBag == (bool)snapshot["hasBag"] && state.Expedition == (int)snapshot["expedition"] && world.Length == count,
        $"Continue accepts the {count}-item saved expedition");

    foreach (var row in world)
    {
        var id = (string)row["key"];
        var body = items[id].GetComponent<Rigidbody>();
        var position = Position(row);
        var rotation = new Quaternion((float)row["rotation"]["x"], (float)row["rotation"]["y"],
            (float)row["rotation"]["z"], (float)row["rotation"]["w"]);
        if (!state.TryGetPose(id, out var pose) || Vector3.Distance(pose.Position, position) > 0.0001f ||
            Quaternion.Angle(pose.Rotation, rotation) > 0.05f ||
            (frozen && (Vector3.Distance(body.position, position) > 0.002f ||
                Quaternion.Angle(body.rotation, rotation) > 0.10f || !body.isKinematic)))
            throw new InvalidOperationException($"{id} does not match its saved position and orientation on Continue.");
    }
    checks.Add(frozen ? $"Restores all {count} bodies at their saved poses before physics resumes" :
        $"Retains all {count} IDs and saved poses across the scene reload");
}

async Task TraverseRim()
{
    var player = Player();
    player.Place(GameObject.Find("Climb from yard").transform);
    await KeyInput(Key.E);
    Check(player.transform.position.z > 1f, "E enters after continuation");
    await KeyInput(Key.E);
    Check(player.transform.position.z < -0.8f, "E exits after continuation");
}

void Check(bool passed, string description)
{
    if (!passed)
        throw new InvalidOperationException(string.Join("\n", checks.Append("FAILED: " + description)));
    checks.Add(description);
}

JObject ReadSlot() => JObject.Parse((string)JObject.Parse(File.ReadAllText(slot))["payload"]);
static JToken FindItem(JObject save, string id) => save["world"].Single(item => (string)item["key"] == id);
static Vector3 Position(JToken row) =>
    new((float)row["position"]["x"], (float)row["position"]["y"], (float)row["position"]["z"]);
static GameState State() => Object.FindAnyObjectByType<SearchSceneScope>()?.Container?.Resolve<GameState>();
static SearchSession Session() => Object.FindAnyObjectByType<SearchSession>();
static SearchPlayer Player() => Object.FindAnyObjectByType<SearchPlayer>();
static Dictionary<string, SearchItem> FindItems() =>
    Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include).ToDictionary(item => item.Id);

static (string Id, BoxCollider Shape, Vector3 Target) FindReachableFind(SearchPlayer player)
{
    var view = player.ViewCamera.transform.position;
    foreach (var item in FindItems().Values.Where(item => item.Id != "cabinet-a" && item.gameObject.activeSelf)
        .OrderBy(item => item.Id != "crate-a"))
    {
        var shape = item.GetComponent<BoxCollider>();
        foreach (var height in new[] { 0f, -0.30f, 0.30f })
        foreach (var side in new[] { 0f, -0.30f, 0.30f })
        {
            var point = shape.transform.TransformPoint(shape.center + Vector3.Scale(shape.size, new Vector3(side, height, 0f)));
            if (Physics.Raycast(view, point - view, out var hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                hit.collider == shape && hit.distance > 0.6f)
                return (item.Id, shape, point);
        }
    }
    throw new InvalidOperationException("No visible item in reach from the tested approach.");
}

async Task AimAt(Vector3 point, SearchPlayer player) => await Aim(point - player.ViewCamera.transform.position, player);

async Task Aim(Vector3 direction, SearchPlayer player)
{
    var view = player.ViewCamera.transform;
    var target = Quaternion.LookRotation(direction).eulerAngles;
    var current = view.eulerAngles;
    InputSystem.QueueDeltaStateEvent(mouse.delta,
        new Vector2(Mathf.DeltaAngle(current.y, target.y), -Mathf.DeltaAngle(current.x, target.x)) / 0.11f);
    await Frames();
}

async Task KeyInput(Key key, float duration = 0)
{
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
    await Frames();
    if (duration > 0)
    {
        var target = Time.fixedTime + duration;
        await Wait.Until(() => Time.fixedTime >= target);
    }
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

async Task Until(Func<bool> condition, float seconds)
{
    var deadline = Time.fixedTime + seconds;
    await Wait.Until(() => condition() || Time.fixedTime >= deadline);
}
