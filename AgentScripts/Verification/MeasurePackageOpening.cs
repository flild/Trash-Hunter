using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrashHunter;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

var folder = Environment.GetEnvironmentVariable("TRASH_HUNTER_EDITOR_SAVE_DIR");
if (!Application.isPlaying || string.IsNullOrEmpty(folder))
    throw new InvalidOperationException("Use an isolated copy of the I-110 measurement slot.");
var player = Object.FindAnyObjectByType<SearchPlayer>();
var view = player.ViewCamera;
var sack = Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Exclude).Single(i => i.Id == "sack-a");
var settings = InputSystem.settings;
var background = settings.backgroundBehavior;
var input = settings.editorInputBehaviorInPlayMode;
var previousKeyboard = Keyboard.current;
var previousMouse = Mouse.current;
settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
var keyboard = InputSystem.AddDevice<Keyboard>();
var mouse = InputSystem.AddDevice<Mouse>();
var vsync = QualitySettings.vSyncCount;
var frameRate = Application.targetFrameRate;
var samples = new List<float>(240);
var draws = new List<int>(240);
try
{
    QualitySettings.vSyncCount = 0;
    Application.targetFrameRate = -1;
    await Frames(4);
    keyboard.MakeCurrent();
    mouse.MakeCurrent();
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
    await Frames(3);
    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    await Frames(3);
    if (Object.FindAnyObjectByType<SearchHud>().Expedition.gameObject.activeSelf)
        throw new InvalidOperationException("Continue did not close.");
    if (Cursor.lockState != CursorLockMode.Locked)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
        await Frames(3);
        InputSystem.QueueStateEvent(mouse, new MouseState());
        await Frames(3);
    }
    var shape = sack.GetComponent<BoxCollider>();
    var direction = (from y in new[] { 0.42f, 0f, -0.35f }
                     from x in new[] { 0f, -0.4f, 0.4f }
                     from z in new[] { 0f, -0.4f, 0.4f }
                     let point = shape.transform.TransformPoint(shape.center + Vector3.Scale(shape.size, new Vector3(x,y,z)))
                     let ray = point - view.transform.position
                     where Physics.Raycast(view.transform.position, ray, out var probe, 3f) && probe.collider == shape
                     select ray).First();
    var angles = Quaternion.LookRotation(direction).eulerAngles;
    var current = view.transform.eulerAngles;
    InputSystem.QueueDeltaStateEvent(mouse.delta, new Vector2(Mathf.DeltaAngle(current.y, angles.y),
        -Mathf.DeltaAngle(current.x, angles.x)) / 0.11f);
    await Frames(30);
    if (!Physics.Raycast(view.transform.position, view.transform.forward, out var hit, 3f) || hit.collider.gameObject != sack.gameObject)
        throw new InvalidOperationException("The fixture camera must target the closed sack.");

    RenderPipelineManager.endCameraRendering += Sample;
    await Frames(120);
    var rest = Summary();
    samples.Clear();
    draws.Clear();
    InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
    await Frames(3);
    InputSystem.QueueStateEvent(mouse, new MouseState());
    await Frames(117);
    if (!Newtonsoft.Json.Linq.JObject.Parse((string)Newtonsoft.Json.Linq.JObject.Parse(
        System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "slot.json")))["payload"])["world"]
        .Any(row => (string)row["key"] == "sack-a" && (bool?)row["opened"] == true))
        throw new InvalidOperationException("The measured input must actually open the sack.");
    return new { width = Screen.width, height = Screen.height, rest, opening = Summary() };
}
finally
{
    RenderPipelineManager.endCameraRendering -= Sample;
    InputSystem.RemoveDevice(mouse);
    InputSystem.RemoveDevice(keyboard);
    if (previousMouse?.added == true) previousMouse.MakeCurrent();
    if (previousKeyboard?.added == true) previousKeyboard.MakeCurrent();
    settings.backgroundBehavior = background;
    settings.editorInputBehaviorInPlayMode = input;
    QualitySettings.vSyncCount = vsync;
    Application.targetFrameRate = frameRate;
}

object Summary()
{
    if (samples.Count < 100) throw new InvalidOperationException("Insufficient rendered frames.");
    samples.Sort();
    draws.Sort();
    return new { frames = samples.Count, medianMs = samples[samples.Count / 2],
        p95Ms = samples[Mathf.CeilToInt(samples.Count * 0.95f) - 1], peakMs = samples.Last(),
        medianDraws = draws[draws.Count / 2] };
}
void Sample(ScriptableRenderContext context, Camera camera)
{
    if (camera != view) return;
    samples.Add(Time.unscaledDeltaTime * 1000f);
    draws.Add(UnityStats.drawCalls);
}
async Task Frames(int count)
{
    var target = Time.frameCount + count;
    await Wait.Until(() => Time.frameCount >= target);
}
