using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TrashHunter;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VContainer;
using Object = UnityEngine.Object;

var folder = Environment.GetEnvironmentVariable("TRASH_HUNTER_EDITOR_SAVE_DIR");
if (!Application.isPlaying || string.IsNullOrEmpty(folder) || !File.Exists(Path.Combine(folder, "slot.json")))
    throw new InvalidOperationException("Start Play Mode with a copy of the I-110 measurement slot.");

var settings = InputSystem.settings;
var backgroundBehavior = settings.backgroundBehavior;
var editorInput = settings.editorInputBehaviorInPlayMode;
var previousKeyboard = Keyboard.current;
var previousMouse = Mouse.current;
var previousBackground = Application.runInBackground;
var vSync = QualitySettings.vSyncCount;
var targetRate = Application.targetFrameRate;
settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
Application.runInBackground = true;
QualitySettings.vSyncCount = 0;
Application.targetFrameRate = -1;
var keyboard = InputSystem.AddDevice<Keyboard>();
var mouse = InputSystem.AddDevice<Mouse>();
keyboard.MakeCurrent();
mouse.MakeCurrent();
var renderStats = new List<int>();
var phases = new List<object>();

try
{
    var player = Object.FindAnyObjectByType<SearchPlayer>();
    var view = player.ViewCamera.transform;
    var state = Object.FindAnyObjectByType<SearchSceneScope>().Container.Resolve<GameState>();
    var bodies = Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Exclude)
        .Select(item => (item.Id, Body: item.GetComponent<Rigidbody>())).ToDictionary(pair => pair.Id, pair => pair.Body);
    if (!state.HasBag || state.HeldItemId != null || bodies.Count != 23)
        throw new InvalidOperationException("Expected the closed-package measurement layout with 23 active bodies.");
    var cabinet = bodies["cabinet-a"];
    await Keys(0f, Key.Enter);
    if (Cursor.lockState != CursorLockMode.Locked)
        await Click(1);
    await Aim(new Vector3(0.45f, -0.45f, 1f));
    await Until(() => bodies.Values.All(body => body.IsSleeping()), 5f);
    if (bodies.Values.Any(body => !body.IsSleeping()))
        throw new InvalidOperationException("The measurement pile did not settle.");

    ScreenCapture.CaptureScreenshot(Path.Combine(folder, "rest.png"));
    await Until(() => false, 1f);
    phases.Add(await Sample("rest", () => false, 3f));

    await Aim(cabinet.position - view.position);
    ScreenCapture.CaptureScreenshot(Path.Combine(folder, "aim.png"));
    await Frames();
    await Click(1);
    if (state.HeldItemId != "cabinet-a")
        throw new InvalidOperationException("The production input did not grab the cabinet.");

    await Aim(Vector3.forward);
    await Keys(0.21f, Key.D);
    await Aim(Vector3.right);
    for (var i = 0; i < 6; i++)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0f, 120f) });
        await Frames();
    }
    await Aim(new Vector3(0.22f, 1f, 0f));
    phases.Add(await Sample("lift", () => cabinet.position.y > 2.45f, 5f));
    if (cabinet.position.y <= 2.45f)
        throw new InvalidOperationException($"The cabinet did not reach the clearance height: {cabinet.position}.");
    await Aim(new Vector3(1f, 0.74f, 0f));
    phases.Add(await Sample("over-rim", () => cabinet.position.x > 2.85f && cabinet.position.y > 2.15f, 5f));
    if (cabinet.position.x <= 2.85f || cabinet.position.y <= 2.15f)
        throw new InvalidOperationException($"The cabinet did not cross the rim: {cabinet.position}.");
    ScreenCapture.CaptureScreenshot(Path.Combine(folder, "over-rim.png"));
    await Frames();

    return new
    {
        unity = Application.unityVersion,
        cpu = SystemInfo.processorType,
        gpu = SystemInfo.graphicsDeviceName,
        api = SystemInfo.graphicsDeviceType.ToString(),
        memoryMiB = SystemInfo.systemMemorySize,
        width = Screen.width,
        height = Screen.height,
        quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
        pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name,
        fixedStep = Time.fixedDeltaTime,
        vSync = QualitySettings.vSyncCount,
        targetRate = Application.targetFrameRate,
        bodies = bodies.Count,
        cabinetMass = cabinet.mass,
        crossed = new[] { cabinet.position.x, cabinet.position.y, cabinet.position.z },
        phases = phases.ToArray()
    };

    async Task<object> Sample(string phase, Func<bool> done, float seconds)
    {
        var samples = new List<float>(1200);
        renderStats.Clear();
        var minAwake = bodies.Count;
        var maxAwake = 0;
        var lastFrame = -1;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += Capture;
        try
        {
            await Until(done, seconds);
        }
        finally
        {
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= Capture;
        }

        void Capture(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
        {
            if (camera != player.ViewCamera || Time.frameCount == lastFrame)
                return;

            lastFrame = Time.frameCount;
            samples.Add(Time.unscaledDeltaTime * 1000f);
            renderStats.Add(UnityStats.drawCalls);
            var awake = bodies.Values.Count(body => !body.IsSleeping());
            minAwake = Mathf.Min(minAwake, awake);
            maxAwake = Mathf.Max(maxAwake, awake);
        }

        samples.Sort();
        renderStats.Sort();
        return new
        {
            phase,
            frames = samples.Count,
            medianMs = Percentile(samples, 0.5f),
            p95Ms = Percentile(samples, 0.95f),
            maxMs = samples.Last(),
            medianDrawCalls = Percentile(renderStats, 0.5f),
            minAwake,
            maxAwake
        };
    }

    async Task Aim(Vector3 direction)
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
            await Until(() => false, seconds);
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
}
finally
{
    InputSystem.RemoveDevice(mouse);
    InputSystem.RemoveDevice(keyboard);
    if (previousMouse?.added == true) previousMouse.MakeCurrent();
    if (previousKeyboard?.added == true) previousKeyboard.MakeCurrent();
    settings.backgroundBehavior = backgroundBehavior;
    settings.editorInputBehaviorInPlayMode = editorInput;
    Application.runInBackground = previousBackground;
    QualitySettings.vSyncCount = vSync;
    Application.targetFrameRate = targetRate;
}

static T Percentile<T>(List<T> sorted, float fraction) =>
    sorted[Mathf.CeilToInt(sorted.Count * fraction) - 1];

async Task Frames(int count = 3)
{
    var target = Time.frameCount + count;
    await Wait.Until(() => Time.frameCount >= target);
}

async Task Until(Func<bool> condition, float seconds)
{
    var deadline = Time.fixedTime + seconds;
    await Wait.Until(() => condition() || Time.fixedTime >= deadline);
}
