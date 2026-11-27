using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using TrashHunter;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
const int laps = 6;
var folder = Environment.GetEnvironmentVariable("TRASH_HUNTER_EDITOR_SAVE_DIR");
var expectedRoot = Path.GetFullPath("Logs/I-190") + Path.DirectorySeparatorChar;
if (!Application.isEditor || !Application.isPlaying || string.IsNullOrEmpty(folder) ||
    !Path.GetFullPath(folder).StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) ||
    !File.Exists(Path.Combine(folder, "slot.json")))
    throw new InvalidOperationException("Enter Play Mode with an isolated I-190 fixture.");
var scopes = Object.FindObjectsByType<LifetimeScope>();
var app = scopes.Single(scope => scope.name == "Application Scope");
var run = scopes.Single(scope => scope.name == "Run Scope");
var state = run.Container.Resolve<GameState>();
var coordinator = app.GetComponents<MonoBehaviour>().Single(behaviour => behaviour.GetType().Name == "SceneCoordinator");
var control = coordinator.GetType().GetProperty("HasControl", members);
var samples = new List<object>();
var settings = InputSystem.settings;
var background = settings.backgroundBehavior;
var input = settings.editorInputBehaviorInPlayMode;
var previous = Keyboard.current;
var keyboard = InputSystem.AddDevice<Keyboard>();
try
{
    settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
    settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
    keyboard.MakeCurrent();
    await Until(Ready, "cold entry");
    Sample("cold", -1, -1);
    await KeyState(Key.Enter);
    if (state.Location == RunLocation.Search)
    {
        await Travel(Key.F5, RunLocation.Search, "search.reload");
        for (var lap = 1; lap <= laps; lap++)
        {
            var session = Object.FindObjectsByType<SearchSession>().Single();
            Player().Place((Transform)typeof(SearchSession).GetField("exit", members).GetValue(session));
            Cursor.lockState = CursorLockMode.Locked;
            await KeyState(Key.E);
            await Travel(Key.Enter, RunLocation.Home, $"home.{lap}");
            await Travel(Key.T, RunLocation.Trader, $"trader.{lap}");
            await Travel(Key.E, RunLocation.Home, $"home.fromTrader.{lap}");
            await Travel(Key.E, RunLocation.Search, $"search.{lap}");
        }
    }
}
finally
{
    InputSystem.RemoveDevice(keyboard);
    previous?.MakeCurrent();
    settings.backgroundBehavior = background;
    settings.editorInputBehaviorInPlayMode = input;
}
return new { samples, state.Expedition };

async Task Travel(Key key, RunLocation target, string label)
{
    var source = SceneManager.GetActiveScene();
    var oldScope = Object.FindObjectsByType<SearchSceneScope>().Single();
    var oldPlayer = Player();
    var oldSession = (MonoBehaviour)Object.FindAnyObjectByType<SearchSession>() ?? Object.FindAnyObjectByType<SettlementSession>();
    var start = Time.realtimeSinceStartupAsDouble;
    var frame = Time.frameCount;
    var maxFrameMs = 0d;
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
    await Until(() =>
    {
        if (frame != Time.frameCount)
        {
            frame = Time.frameCount;
            maxFrameMs = Math.Max(maxFrameMs, Time.unscaledDeltaTime * 1000d);
        }
        return Ready() && SceneManager.GetActiveScene() != source;
    }, label);
    if (state.Location != target || source.isLoaded || oldScope != null || oldPlayer != null || oldSession != null)
        throw new InvalidOperationException($"Source was retained or destination was wrong at {label}.");
    Sample(label, maxFrameMs, (Time.realtimeSinceStartupAsDouble - start) * 1000);
    await KeyState();
}

void Sample(string label, double maxFrameMs, double inputToControlMs)
{
    var scene = SceneManager.GetActiveScene();
    var sceneScopes = Object.FindObjectsByType<SearchSceneScope>(FindObjectsInactive.Include);
    var players = Object.FindObjectsByType<SearchPlayer>(FindObjectsInactive.Include);
    var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
    var search = Object.FindObjectsByType<SearchSession>(FindObjectsInactive.Include);
    var settlement = Object.FindObjectsByType<SettlementSession>(FindObjectsInactive.Include);
    var eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
    var isSearch = state.Location == RunLocation.Search;
    if (SceneManager.sceneCount != 1 || sceneScopes.Length != 1 || players.Length != 1 ||
        cameras.Length != 1 || !cameras[0].isActiveAndEnabled || cameras[0] != players[0].ViewCamera ||
        search.Length != (isSearch ? 1 : 0) || settlement.Length != (isSearch ? 0 : 1) ||
        eventSystems.Length != (isSearch ? 1 : 0) || sceneScopes[0].gameObject.scene != scene ||
        sceneScopes[0].Parent != run || sceneScopes[0].Container.Resolve<GameState>() != state ||
        Object.FindObjectsByType<LifetimeScope>(FindObjectsInactive.Include).Length != 3)
        throw new InvalidOperationException($"Unexpected scopes, camera or input handlers at {label}.");

    var timing = ((double load, double write, double activation, double views, double total))
        coordinator.GetType().GetProperty("LastTransition", members).GetValue(coordinator);
    samples.Add(new
    {
        label, scene = scene.name, frame = Time.frameCount, maxFrameMs, inputToControlMs,
        slotReadMs = (double)coordinator.GetType().GetProperty("SlotReadMs", members).GetValue(coordinator),
        timing.load, timing.write, timing.activation, timing.views, timing.total,
        allocatedBytes = Profiler.GetTotalAllocatedMemoryLong(),
        sceneObjects = scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<Transform>(true).Length),
        scopes = Object.FindObjectsByType<LifetimeScope>(FindObjectsInactive.Include).Length,
        cameras = cameras.Length, inputSessions = search.Length + settlement.Length, eventSystems = eventSystems.Length
    });
}

SearchPlayer Player() => Object.FindObjectsByType<SearchPlayer>().Single();
bool Ready() => (bool)control.GetValue(coordinator);
async Task KeyState(params Key[] keys)
{
    InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    var target = Time.frameCount + 3;
    await Until(() => Time.frameCount >= target, "input frames");
}

async Task Until(Func<bool> condition, string label)
{
    var deadline = Time.realtimeSinceStartupAsDouble + 8;
    await Wait.Until(() => condition() || Time.realtimeSinceStartupAsDouble >= deadline);
    if (!condition())
        throw new InvalidOperationException($"Timed out at {label}; {state.Location}: {state.Error}");
}
