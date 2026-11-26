using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
if (!Application.isPlaying || string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder) ||
    phase is not ("packages" or "packages-resume" or "packages-legacy"))
    throw new InvalidOperationException("Select an isolated slot and package verification phase in the Editor.");

var english = Environment.GetEnvironmentVariable("TRASH_HUNTER_LOCALE") == "en";
var recognition = Environment.GetEnvironmentVariable("TRASH_HUNTER_RECOGNITION") == "1";
var slot = Path.Combine(folder, "slot.json");
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
    var items = Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include).ToDictionary(item => item.Id);
    var state = Object.FindAnyObjectByType<SearchSceneScope>().Container.Resolve<GameState>();
    var player = Object.FindAnyObjectByType<SearchPlayer>();
    var hud = Object.FindAnyObjectByType<SearchHud>();
    var prompt = hud.transform.Find("Prompt").GetComponent<Text>();
    var overlay = hud.Expedition.transform.Find("ExpeditionPanel");
    var details = overlay.Find("Details").GetComponent<Text>();
    var actions = overlay.Find("Actions").GetComponent<Text>();

    if (phase == "packages-legacy")
    {
        var old = ReadSlot();
        Check(state.Error == null && (int?)old["pileGeneratorVersion"] is null or 0 or 1,
            "The prior-generation slot opens");
        Check(!items["sack-a"].gameObject.activeSelf && !items["parcel-a"].gameObject.activeSelf &&
            old["world"].All(item => state.TryGetPose((string)item["key"], out _)),
            "The legacy world keeps its IDs and receives no package bodies");
        await KeyInput(Key.Enter);
        Check(!hud.Expedition.gameObject.activeSelf, "Continue resumes a legacy expedition");
        if (!state.HasBag)
        {
            await AimAt(GameObject.Find("Starter Waist Bag").GetComponent<Collider>().bounds.center);
            await KeyInput(Key.E);
        }
        player.Place(GameObject.Find("Yard Exit").transform);
        await KeyInput(Key.E);
        await KeyInput(Key.Enter);
        Check(state.AtHome, "The legacy expedition can end normally");
        var session = Object.FindAnyObjectByType<SearchSession>();
        await KeyInput(Key.E);
        await Wait.Until(() => Object.FindAnyObjectByType<SearchSession>() != session &&
            Object.FindAnyObjectByType<SearchSession>() != null);
        await Frames();
        var newRun = ReadSlot();
        Check((int)newRun["pileGeneratorVersion"] == 2 && Nested(newRun, "plug-a", "sack-a") &&
            Nested(newRun, "adapter-a", "parcel-a"), "The next expedition uses both packages");
    }
    else if (phase == "packages")
    {
        Check(!File.Exists(slot), "The route starts with a fresh slot");
        CheckTiers();
        await AimAt(GameObject.Find("Starter Waist Bag").GetComponent<Collider>().bounds.center);
        await KeyInput(Key.E);
        Check(state.HasBag, "The waist bag is equipped through normal input");
        await KeyInput(Key.E);
        Check(player.transform.position.z > 1f, "E enters the generated pile");

        var fresh = ReadSlot();
        Check((int)fresh["pileGeneratorVersion"] == 2 && fresh["world"].Count() == 27 &&
            fresh["world"].Select(row => (string)row["key"]).Distinct().Count() == 27,
            "The generated expedition contains 27 distinct IDs");
        Check(Nested(fresh, "plug-a", "sack-a") && Nested(fresh, "motor-b", "sack-a") &&
            Nested(fresh, "fuse-a", "parcel-a") && Nested(fresh, "adapter-a", "parcel-a") &&
            new[] { "plug-a", "motor-b", "fuse-a", "adapter-a" }.All(id => !items[id].gameObject.activeSelf),
            "Closed packages carry four saved instances without active bodies");

        var pending = Path.Combine(folder, "slot.pending");
        await AimPackage("sack-a", false);
        using (new FileStream(pending, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            await Click(1);
            Check(state.Error != null && !hud.Expedition.gameObject.activeSelf &&
                (bool?)Row(ReadSlot(), "sack-a")["opened"] != true,
                "A failed write leaves the sack closed");
        }
        await Open("sack-a", false);
        CheckDetails("Вилка", "Моторчик", "Plug", "Motor");
        await KeyInput(Key.DownArrow);
        Check(details.text.Contains(english ? "›  Motor" : "›  Моторчик"), "The second row can be chosen");
        using (new FileStream(pending, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            await KeyInput(Key.Enter);
            Check(state.Error != null && state.HeldItemId == null && hud.Expedition.gameObject.activeSelf &&
                Nested(ReadSlot(), "motor-b", "sack-a") && !items["motor-b"].gameObject.activeSelf,
                "A failed extraction leaves the instance in the sack");
        }
        await KeyInput(Key.Enter);
        await Inspect("motor-b", 2, english ? "Shaft is bent" : "Вал погнут");
        await Pack(0, 0);
        await Open("sack-a", true);
        CheckDetails("Вилка", null, "Plug", null);
        await KeyInput(Key.Escape);
        await Open("parcel-a", false);
        CheckDetails("Держатель", "Адаптер", "Holder", "Adapter");
        await KeyInput(Key.Escape);
        var saved = ReadSlot();
        Check((bool)Row(saved, "sack-a")["opened"] && (bool)Row(saved, "parcel-a")["opened"] &&
            (bool)Row(saved, "motor-b")["packed"] && !Nested(saved, "motor-b", "sack-a") &&
            Nested(saved, "plug-a", "sack-a") && Nested(saved, "fuse-a", "parcel-a") &&
            Nested(saved, "adapter-a", "parcel-a"), "The slot records both open states and the exact ownership of every find");
    }
    else
    {
        Check(state.Error == null && state.Bag.TryGetPlacement("motor-b", out var packed) &&
            packed.Column == 0 && packed.Row == 0 && !items["motor-b"].gameObject.activeSelf,
            "Continue restores the selected motor in its bag cell");
        Check(items["sack-a"].transform.Find("Open mouth").gameObject.activeSelf &&
            items["parcel-a"].transform.Find("Open flaps").gameObject.activeSelf,
            "Both open package forms are restored before resuming");
        await KeyInput(Key.Enter);

        await Open("sack-a", true);
        CheckDetails("Вилка", null, "Plug", null);
        await KeyInput(Key.Enter);
        await Inspect("plug-a", 1, english ? "Pins are straight" : "Штыри ровные");
        await Pack(2, 0);
        await Open("sack-a", true);
        Check(details.text == (english ? "Empty" : "Пусто"), "The empty sack can be revisited");
        await KeyInput(Key.Enter);
        Check(state.HeldItemId == null, "Enter in an empty package cannot create another find");
        await KeyInput(Key.Escape);

        await Open("parcel-a", true);
        CheckDetails("Держатель", "Адаптер", "Holder", "Adapter");
        await KeyInput(Key.DownArrow);
        await KeyInput(Key.Enter);
        await Inspect("adapter-a", 2, english ? "Socket is intact" : "Разъём цел");
        await Pack(0, 1);
        await Open("parcel-a", true);
        CheckDetails("Держатель", null, "Holder", null);
        await KeyInput(Key.Escape);

        player.Place(GameObject.Find("Yard Exit").transform);
        await KeyInput(Key.E);
        await KeyInput(Key.Enter);
        Check(state.AtHome && state.Home.Select(item => item.key).ToHashSet()
            .SetEquals(new[] { "motor-b", "plug-a", "adapter-a" }), "Only the three packed finds go home");
        var offers = state.Home.ToDictionary(item => item.key, item => state.Quote(item).Price);
        Check(offers["adapter-a"] > offers["plug-a"] && offers["plug-a"] > offers["motor-b"],
            "Real offers respect both base value and condition discounts");
    }

    return new { phase, locale = english ? "en" : "ru", recognition, passed = checks.Count, checks };

    void CheckTiers()
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var tier = typeof(SearchItem).GetField("tier", Fields);
        var value = typeof(SearchItem).GetField("baseValue", Fields);
        var ranked = items.Values.Select(item => (Tier: (int)tier.GetValue(item), Base: (int)value.GetValue(item)))
            .Where(item => item.Tier > 0).ToArray();
        Check(ranked.Select(item => item.Tier).Distinct().Count() == 3 &&
            ranked.All(item => ranked.Where(other => other.Tier < item.Tier).All(other => other.Base < item.Base)),
            "Every higher tier has a higher base value than every lower tier");
    }

    async Task AimPackage(string id, bool opened)
    {
        var shape = items[id].GetComponent<BoxCollider>();
        var view = player.ViewCamera.transform.position;
        var point = (from x in new[] { 0f, -0.34f, 0.34f }
                     from y in new[] { 0.31f, 0f, -0.31f }
                     from z in new[] { 0f, -0.34f, 0.34f }
                     let local = shape.center + Vector3.Scale(shape.size, new Vector3(x, y, z))
                     let target = shape.transform.TransformPoint(local)
                     where Physics.Raycast(view, target - view, out var hit, 3f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.collider == shape
                     select (Vector3?)target).FirstOrDefault();
        Check(point.HasValue, $"The package {id} can be aimed at in the generated pile");
        await AimAt(point.Value);
        var expected = opened ? (english ? "Look inside" : "Заглянуть") : (english ? "Open" : "Открыть");
        Check(prompt.text.Contains(expected) && !HasTier(prompt.text) && Fits(prompt),
            $"The aim prompt identifies the interaction for {id}");
        var trace = new MaterialPropertyBlock();
        Check(items[id].GetComponentsInChildren<MeshRenderer>().Any(renderer =>
        {
            renderer.GetPropertyBlock(trace);
            return !trace.isEmpty;
        }), $"The aimed package {id} uses the interaction highlight");
    }

    async Task Open(string id, bool opened)
    {
        await AimPackage(id, opened);
        await Click(1);
        Check(hud.Expedition.gameObject.activeSelf && state.HeldItemId == null && Fits(actions),
            $"Clicking {id} shows its contents without taking the packaging");
    }

    void CheckDetails(string firstRu, string secondRu, string firstEn, string secondEn)
    {
        var names = details.text.Split('\n');
        var expected = new[] { english ? firstEn : firstRu, english ? secondEn : secondRu }.Where(name => name != null).ToArray();
        Check(names.Length == expected.Length && expected.All(name => names.Any(row => row.Contains(name))) &&
            !HasTier(details.text) && Fits(details), $"The package lists exactly {string.Join(", ", expected)} without tiers");
    }

    async Task Inspect(string id, int tier, string condition)
    {
        Check(state.HeldItemId == id && items[id].IsHeld && !HasTier(prompt.text),
            $"{id} is in hand before inspection and its tier is hidden");
        await KeyInput(Key.Q);
        var tierLabel = english ? $"Tier {tier}" : $"Тир {tier}";
        Check(prompt.text.Contains(condition) && Fits(prompt) &&
            (recognition ? prompt.text.Contains(tierLabel) : !HasTier(prompt.text)),
            $"Inspection shows the condition of {id} and {(recognition ? "reveals its tier" : "keeps its tier hidden")}");
        await KeyInput(Key.Q);
        Check(!HasTier(prompt.text), "Ending inspection hides the tier in the search HUD");
    }

    async Task Pack(int column, int row)
    {
        var id = state.HeldItemId;
        await KeyInput(Key.Tab);
        var grid = (RectTransform)hud.Bag.transform.Find("BagPanel/BagGrid");
        Canvas.ForceUpdateCanvases();
        var pointer = RectTransformUtility.WorldToScreenPoint(null, grid.TransformPoint(
            new Vector3((column + 0.5f) * BagView.CellSize, -(row + 0.5f) * BagView.CellSize)));
        await Click(1, pointer);
        Check(state.HeldItemId == null && state.Bag.TryGetPlacement(id, out var placed) &&
            placed.Column == column && placed.Row == row, $"The selected instance {id} fits into the chosen cell");
        await KeyInput(Key.Tab);
    }

    async Task AimAt(Vector3 world)
    {
        var view = player.ViewCamera.transform;
        var rotation = Quaternion.LookRotation(world - view.position).eulerAngles;
        var current = view.eulerAngles;
        InputSystem.QueueDeltaStateEvent(mouse.delta,
            new Vector2(Mathf.DeltaAngle(current.y, rotation.y), -Mathf.DeltaAngle(current.x, rotation.x)) / 0.11f);
        await Frames();
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

JObject ReadSlot() => JObject.Parse((string)JObject.Parse(File.ReadAllText(slot))["payload"]);
static JToken Row(JObject save, string id) => save["world"].Single(item => (string)item["key"] == id);
static bool Nested(JObject save, string id, string package) => (string)Row(save, id)["package"] == package;
static bool HasTier(string text) => text.Contains("Tier") || text.Contains("Тир");
static bool Fits(Text label) => label.preferredWidth <= label.rectTransform.rect.width + 1 &&
    label.preferredHeight <= label.rectTransform.rect.height + 1;

void Check(bool passed, string description)
{
    if (!passed)
        throw new InvalidOperationException(string.Join("\n", checks.Append("FAILED: " + description)));
    checks.Add(description);
}

async Task KeyInput(Key key)
{
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
    await Frames();
    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    await Frames();
}

async Task Click(ushort buttons, Vector2 position = default)
{
    InputSystem.QueueStateEvent(mouse, new MouseState { buttons = buttons, position = position });
    await Frames();
    InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
    await Frames();
}

async Task Frames()
{
    var target = Time.frameCount + 3;
    await Wait.Until(() => Time.frameCount >= target);
}
