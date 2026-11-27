using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace TrashHunter
{
    internal sealed class SceneCoordinator : MonoBehaviour
    {
        private readonly Dictionary<RunLocation, string> paths = new()
        {
            [RunLocation.Search] = "Assets/TrashHunter/Scenes/ContainerSearch.unity",
            [RunLocation.Home] = "Assets/TrashHunter/Scenes/Home.unity",
            [RunLocation.Trader] = "Assets/TrashHunter/Scenes/Trader.unity"
        };

        private GameState state;
        private LootCatalog catalog;
        private SearchSceneScope active;
        private RunLocation? pending;
        private int readyFrame;
        private int retryAfterFrame;
        private bool busy;
        private UiStrings startupStrings;

        internal bool HasControl => active != null && !busy && !pending.HasValue && Time.frameCount > readyFrame;
        internal double SlotReadMs { get; private set; }
        internal (double LoadMs, double WriteMs, double ActivationMs, double ViewsMs, double TotalMs) LastTransition { get; private set; }

        internal void Initialize(GameState gameState, LootCatalog lootCatalog)
        {
            state = gameState;
            catalog = lootCatalog;
            var start = Time.realtimeSinceStartupAsDouble;
            state.LoadSlot();
            SlotReadMs = Elapsed(start);
        }

        private void Start()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path == paths[state.Location] && TryGetScope(scene, state.Location, out var scope))
                Activate(scope);
            else
                BeginMove(state.Location);
        }

        internal bool SaveAndTravel(Func<bool> write)
        {
            if (!HasControl)
                return false;

            busy = true;
            var start = Time.realtimeSinceStartupAsDouble;
            if (!write())
            {
                busy = false;
                return false;
            }

            BeginMove(state.Location, start: start, writeMs: state.LastWriteMs);
            return true;
        }

        internal void Depart()
        {
            if (HasControl && state.AtHome)
                BeginMove(RunLocation.Search, state.Expedition);
        }

        internal bool RetrySlot()
        {
            if (!state.LoadSlot() || state.Location == active.Location)
                return true;

            BeginMove(state.Location);
            return false;
        }

        private void BeginMove(RunLocation location, int? departure = null, double? start = null, double writeMs = 0)
        {
            busy = true;
            pending = departure.HasValue ? null : location;
            active?.ShowTransition(false, departure.HasValue);
            StartCoroutine(Move(location, departure, start ?? Time.realtimeSinceStartupAsDouble, writeMs));
        }

        private IEnumerator Move(RunLocation location, int? departure, double start, double writeMs)
        {
            var source = SceneManager.GetActiveScene();
            var path = paths[location];
            var destination = default(Scene);
            var loadStart = Time.realtimeSinceStartupAsDouble;

            void OnLoaded(Scene scene, LoadSceneMode mode)
            {
                if (mode == LoadSceneMode.Additive && scene.path == path)
                    destination = scene;
            }

            SceneManager.sceneLoaded += OnLoaded;
            try
            {
                var load = Load(path);
                if (load != null)
                    yield return load;
            }
            finally
            {
                SceneManager.sceneLoaded -= OnLoaded;
            }

            var loadMs = Elapsed(loadStart);
            if (!destination.isLoaded || !TryGetScope(destination, location, out var scope))
            {
                if (destination.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(destination);
                busy = false;
                retryAfterFrame = Time.frameCount;
                active?.ShowTransition(true, departure.HasValue);
                yield break;
            }

            if (departure.HasValue)
            {
                var pile = scope.Session.PreparePile(catalog);
                if (!state.Depart(departure.Value, pile, scope.EntryPose))
                {
                    yield return SceneManager.UnloadSceneAsync(destination);
                    busy = false;
                    active.RestoreSettlement();
                    yield break;
                }
                writeMs = state.LastWriteMs;
                pending = location;
            }

            var activationStart = Time.realtimeSinceStartupAsDouble;
            SceneManager.SetActiveScene(destination);
            yield return SceneManager.UnloadSceneAsync(source);
            var activationMs = Elapsed(activationStart);
            var viewsStart = Time.realtimeSinceStartupAsDouble;
            Activate(scope);
            LastTransition = (loadMs, writeMs, activationMs, Elapsed(viewsStart), Elapsed(start));
        }

        private void Activate(SearchSceneScope scope)
        {
            active = scope;
            active.Mount();
            readyFrame = Time.frameCount;
            pending = null;
            busy = false;
        }

        private static bool TryGetScope(Scene scene, RunLocation location, out SearchSceneScope scope)
        {
            scope = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.TryGetComponent<SearchSceneScope>(out var candidate))
                    continue;
                if (scope != null || !candidate.IsConfiguredFor(location))
                    return false;
                scope = candidate;
            }
            return scope != null;
        }

        private static AsyncOperation Load(string path)
        {
            if (SceneUtility.GetBuildIndexByScenePath(path) < 0)
                return null;

            try
            {
                return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private void Update()
        {
            if (busy || !pending.HasValue || Time.frameCount <= retryAfterFrame)
                return;

            var keys = Keyboard.current;
            if (keys?.enterKey.wasPressedThisFrame == true || keys?.numpadEnterKey.wasPressedThisFrame == true)
                BeginMove(pending.Value);
            else if (keys?.escapeKey.wasPressedThisFrame == true)
                Application.Quit();
        }

        private void OnGUI()
        {
            if (active != null || busy || !pending.HasValue)
                return;

            // A failed cold load may have no scene HUD to display the retry prompt.
            startupStrings ??= new UiStrings();
            var box = new Rect((Screen.width - 460) / 2f, (Screen.height - 96) / 2f, 460, 96);
            GUILayout.BeginArea(box, GUI.skin.box);
            GUILayout.Label(startupStrings.Get("transition.failed"));
            GUILayout.Label(startupStrings.Get("transition.actions"));
            GUILayout.EndArea();
        }

        private static double Elapsed(double start) => (Time.realtimeSinceStartupAsDouble - start) * 1000;
    }
}
