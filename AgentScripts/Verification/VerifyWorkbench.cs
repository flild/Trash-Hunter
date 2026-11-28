using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrashHunter;
using Unity.Pipeline.Scripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;

public static class VerifyWorkbench
{
    private const string SlotVariable = "TRASH_HUNTER_EDITOR_SAVE_DIR";
    private const string LocaleVariable = "TRASH_HUNTER_LOCALE";
    private const string RestoreKey = "TrashHunter.I210.Environment";
    private const string Intact = "lamp-intact";
    private const string Cracked = "lamp-cracked";
    private const string Strip = "strip-b";
    private const string RecordPlayer = "turntable-rp07";
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static object Prepare(string language)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || language is not ("ru" or "en"))
            throw new InvalidOperationException("Prepare an isolated RU or EN fixture in Edit Mode.");

        if (SessionState.GetString(RestoreKey, "") == "")
            SessionState.SetString(RestoreKey, JsonConvert.SerializeObject(new[]
            {
                Environment.GetEnvironmentVariable(SlotVariable),
                Environment.GetEnvironmentVariable(LocaleVariable)
            }));

        var folder = Path.GetFullPath($"Logs/I-210/{language}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var source = Snapshot("AgentScripts/Fixtures/i110-pile-measurement.json");
        source["format"] = 3;
        source["location"] = (int)RunLocation.Search;
        source["expedition"] = 3;
        source["hasSold"] = false;
        source["home"] = new JArray(
            Find(1, Intact), Find(1, Cracked), Find(2, Cracked), Find(1, Strip), Find(2, RecordPlayer));
        var lamp = source["world"].Single(item => (string)item["key"] == Intact);
        lamp["packed"] = true;
        lamp["column"] = 0;
        lamp["row"] = 0;
        var payload = source.ToString(Formatting.None);
        using var sha = SHA256.Create();
        File.WriteAllText(Path.Combine(folder, "slot.json"), new JObject
        {
            ["payload"] = payload,
            ["checksum"] = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)))
        }.ToString(Formatting.None));
        Environment.SetEnvironmentVariable(SlotVariable, folder);
        Environment.SetEnvironmentVariable(LocaleVariable, language);
        return new { folder, language };
    }

    public static void Restore()
    {
        var saved = SessionState.GetString(RestoreKey, "");
        if (saved == "")
            return;

        var values = JsonConvert.DeserializeObject<string[]>(saved);
        Environment.SetEnvironmentVariable(SlotVariable, values[0]);
        Environment.SetEnvironmentVariable(LocaleVariable, values[1]);
        SessionState.EraseString(RestoreKey);
    }

    public static async Task<object> Run(string phase)
    {
        var folder = Environment.GetEnvironmentVariable(SlotVariable);
        var root = Path.GetFullPath("Logs/I-210") + Path.DirectorySeparatorChar;
        if (!Application.isPlaying || folder == null || !folder.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Start Play Mode with the isolated I-210 test slot.");

        var slot = Path.Combine(folder, "slot.json");
        var pending = Path.Combine(folder, "slot.pending");
        var state = Object.FindAnyObjectByType<SearchSceneScope>().Container.Resolve<GameState>();
        var en = Environment.GetEnvironmentVariable(LocaleVariable) == "en";
        var checks = new List<string>();
        var settings = InputSystem.settings;
        var background = settings.backgroundBehavior;
        var editorInput = settings.editorInputBehaviorInPlayMode;
        var previousKeyboard = Keyboard.current;
        var previousMouse = Mouse.current;
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var mouse = InputSystem.AddDevice<Mouse>();
        keyboard.MakeCurrent();
        mouse.MakeCurrent();
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

        var oldIntact = new HomeItem { expedition = 1, key = Intact };
        var dirty = new HomeItem { expedition = 3, key = Intact };
        var broken = new HomeItem { expedition = 1, key = Cracked };
        var otherBroken = new HomeItem { expedition = 2, key = Cracked };
        var part = new HomeItem { expedition = 1, key = Strip };
        var complex = new HomeItem { expedition = 2, key = RecordPlayer };

        try
        {
            if (phase == "begin")
            {
                Check(state.Location == RunLocation.Search && state.Bag.Count == 1 &&
                    state.Bag.TryGetPlacement(Intact, out _) && state.Quote(oldIntact).Price == 42,
                    "Format 3 loads the bag and preserves the old clean lamp offer.");
                await Tap(Key.Enter);
                var search = Object.FindAnyObjectByType<SearchSession>();
                var exit = (Transform)search.GetType().GetField("exit", Members).GetValue(search);
                Player().Place(exit);
                Cursor.lockState = CursorLockMode.Locked;
                await Tap(Key.E);
                await Tap(Key.Enter);
                await At(RunLocation.Home);
                Check(state.Home.Last().Equals(dirty) && state.Home.Count == 6 && state.Bag.Count == 0,
                    "The confirmed return transfers the packed lamp once with trip 3.");
                Check((int)Snapshot(slot)["format"] == 6 && state.Quote(dirty).Price == 34 &&
                    state.Quote(oldIntact).Price == 42, "New dust discounts the delivered lamp only.");
                await Select(dirty);
                var bench = Object.FindAnyObjectByType<Workbench>();
                var surface = (Collider)bench.GetType().GetField("surface", Members).GetValue(bench);
                var point = (Vector2)Player().ViewCamera.WorldToScreenPoint(surface.bounds.center);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 });
                await Frames();
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
                await Frames();
                Check(Model() == "SV-04 Intact" && HasText("InspectionDetail", "грязь", "Dirt"),
                    "Clicking the world bench displays the dirty SV-04.");
                Check(Dust() != Color.clear, "The dirt changes the shell finish.");
                VerifyText();
            }
            else if (phase == "wash")
            {
                // Diagnose first to show that washing keeps a previously confirmed condition.
                var stale = state.Quote(dirty);
                await Tap(Key.D);
                Check(HasText("InspectionDetail", "исправна", "works") &&
                    state.Quote(dirty).Price == stale.Price, "Testing confirms the lamp works without changing the discount.");
                await Tap(Key.W);
                Check(state.Quote(dirty).Price == 42 && state.Quote(oldIntact).Price == 42 &&
                    !HasText("InspectionDetail", "грязь", "dirt") && HasText("InspectionDetail", "исправна", "works"),
                    "Washing clears dirt, retains the diagnosis and raises only the selected offer.");
                Check(Model() == "SV-04 Intact" && Dust() == Color.clear, "Washed lamp restores its original paint.");
                var bytes = File.ReadAllText(slot);
                await Tap(Key.W);
                Check(File.ReadAllText(slot) == bytes, "Washing again leaves the save intact.");
                VerifyText();
            }
            else if (phase == "repair")
            {
                await Tap(Key.Escape);
                await Select(complex);
                await Tap(Key.B);
                var bytes = File.ReadAllText(slot);
                await Tap(Key.D);
                await Tap(Key.R);
                Check(Model() == "RP-07" && HasText("InspectionDetail", "мастер", "specialist") &&
                    File.ReadAllText(slot) == bytes && state.Quote(complex).Price == 30,
                    "The turntable is displayed but home work cannot diagnose or repair it.");
                VerifyText();

                await Tap(Key.Escape);
                await Select(broken);
                await Tap(Key.B);
                Check(Model() == "SV-04 Cracked" && state.Quote(broken).Price == 18 &&
                    !HasText("InspectionDetail", "не держит", "cannot support"),
                    "The broken mount is visible before its fault is confirmed.");
                bytes = File.ReadAllText(slot);
                await Tap(Key.R);
                Check(File.ReadAllText(slot) == bytes, "A lamp requires diagnosis before repair.");
                await Tap(Key.D);
                Check(HasText("InspectionDetail", "не держит", "cannot support") &&
                    HasText("Controls", "ровной рейки", "straight strip"), "Diagnosis exposes the fault and the required part.");

                bytes = File.ReadAllText(slot);
                using (new FileStream(pending, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    await Tap(Key.R);
                Check(File.ReadAllText(slot) == bytes && state.Home.Contains(part) &&
                    state.Quote(broken).Price == 18 && Model() == "SV-04 Cracked" && state.Error != null,
                    "A failed save retains the damaged lamp, part and original slot.");
                await Tap(Key.R);
                Check(state.Error == null && Model() == "SV-04 Repaired" && state.Quote(broken).Price == 42 &&
                    state.Quote(otherBroken).Price == 18 && !state.Home.Contains(part) &&
                    HasText("InspectionDetail", "Крепление заменено", "Mount replaced"),
                    "Successful repair replaces the hinge, consumes the strip and affects one instance.");
                bytes = File.ReadAllText(slot);
                await Tap(Key.R);
                Check(File.ReadAllText(slot) == bytes, "Repair cannot be repeated on the restored lamp.");

                await Tap(Key.Escape);
                await Select(otherBroken);
                await Tap(Key.B);
                await Tap(Key.D);
                Check(HasText("Controls", "Нужна ровная", "Requires a straight"), "A missing part is explained.");
                bytes = File.ReadAllText(slot);
                await Tap(Key.R);
                Check(File.ReadAllText(slot) == bytes && Model() == "SV-04 Cracked", "A missing part blocks repair.");
                VerifyText();
                await Tap(Key.Escape);
                await Select(broken);
                await Tap(Key.B);
                VerifyText();
            }
            else if (phase == "resume")
            {
                Check(state.Location == RunLocation.Home && state.Home.Contains(broken) && state.Home.Contains(otherBroken) &&
                    !state.Home.Contains(part) && state.Quote(dirty).Price == 42 && state.Quote(broken).Price == 42 &&
                    state.Quote(otherBroken).Price == 18 && state.Quote(complex).Price == 30,
                    "A separate Play session restores treatment, prices and part consumption by instance.");
                Check(HasText("Heading", "Продолжить", "Continue"), "The home slot opens on Continue.");
                await Tap(Key.Enter);
                await Select(dirty);
                await Tap(Key.B);
                Check(Dust() == Color.clear && HasText("InspectionDetail", "исправна", "works"),
                    "The washed lamp retains its clean paint and diagnosis after reload.");
                await Tap(Key.Escape);
                await Select(broken);
                await Tap(Key.B);
                Check(Model() == "SV-04 Repaired", "The repaired model is shown after reload.");
                await Tap(Key.Escape);
                await Tap(Key.T);
                await At(RunLocation.Trader);
                var cash = state.Balance;
                foreach (var item in new[] { dirty, broken })
                {
                    await Select(item);
                    await Tap(Key.Enter);
                    Check(HasText("Summary", "42 ₽", "42 ₽") &&
                        HasText("Details", "исправна", "works"), "Trader shows the current offer and confirmed condition.");
                    VerifyText();
                    await Tap(Key.Enter);
                    cash += 42;
                    Check(state.Balance == cash && !state.Home.Contains(item) && state.Home.Contains(otherBroken) &&
                        state.Home.Contains(oldIntact), "Sale pays once and removes only the quoted instance.");
                }
            }
            else
                throw new ArgumentException("Unknown verification phase.", nameof(phase));

            return new { phase, language = en ? "en" : "ru", size = $"{Screen.width}x{Screen.height}", checks };
        }
        finally
        {
            settings.backgroundBehavior = background;
            settings.editorInputBehaviorInPlayMode = editorInput;
            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(keyboard);
            previousMouse?.MakeCurrent();
            previousKeyboard?.MakeCurrent();
        }

        async Task Tap(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            await Frames();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Frames();
        }

        async Task At(RunLocation location)
        {
            await Wait.Until(() => state.Location == location && SceneManager.GetActiveScene().name == location.ToString() &&
                Object.FindObjectsByType<SettlementSession>().Length == 1 &&
                Object.FindObjectsByType<SearchPlayer>().Length == 1 &&
                Object.FindObjectsByType<EventSystem>().Length <= 1, because: location.ToString());
            await Frames();
        }

        async Task Select(HomeItem item)
        {
            var session = Object.FindAnyObjectByType<SettlementSession>();
            var selection = typeof(SettlementSession).GetField("selection", Members);
            var target = state.Home.ToList().IndexOf(item);
            if (target < 0)
                throw new InvalidOperationException($"Missing {item.key} from trip {item.expedition}.");
            var delta = target - (int)selection.GetValue(session);
            for (var index = 0; index < Math.Abs(delta); index++)
                await Tap(delta > 0 ? Key.DownArrow : Key.UpArrow);
            Check((int)selection.GetValue(session) == target, $"Selected {item.key} from trip {item.expedition}.");
        }

        bool HasText(string label, string ru, string english) =>
            Object.FindAnyObjectByType<SearchHud>().GetComponentsInChildren<Text>().Any(text =>
                text.name == label && text.text.Contains(en ? english : ru, StringComparison.Ordinal));

        void VerifyText()
        {
            Canvas.ForceUpdateCanvases();
            var texts = Object.FindAnyObjectByType<SearchHud>().GetComponentsInChildren<Text>().Where(text =>
                text.enabled && !string.IsNullOrEmpty(text.text)).ToArray();
            Check(texts.Length > 0 && texts.All(text => text.preferredWidth <= text.rectTransform.rect.width + 1f &&
                text.preferredHeight <= text.rectTransform.rect.height + 1f),
                "Visible UI text fits its bounds. " + string.Join(", ", texts.Select(text =>
                    $"{text.name}={text.preferredWidth:0}x{text.preferredHeight:0}/{text.rectTransform.rect.size}")));
        }

        Color Dust()
        {
            var shell = Object.FindAnyObjectByType<Workbench>().transform.Find("SV-04 Intact/Base").GetComponent<Renderer>();
            var properties = new MaterialPropertyBlock();
            shell.GetPropertyBlock(properties);
            return properties.GetColor("_BaseColor");
        }

        void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(string.Join("\n", checks.Append("FAILED: " + message)));
            checks.Add(message);
        }
    }

    private static JObject Find(int trip, string key) => new() { ["expedition"] = trip, ["key"] = key };
    private static JObject Snapshot(string path) => JObject.Parse((string)JObject.Parse(File.ReadAllText(path))["payload"]);
    private static SearchPlayer Player() => Object.FindAnyObjectByType<SearchPlayer>();

    private static string Model()
    {
        var bench = Object.FindAnyObjectByType<Workbench>().transform;
        return bench.Cast<Transform>().Single(node => node.gameObject.activeSelf &&
            (node.name.StartsWith("SV-04", StringComparison.Ordinal) || node.name == "RP-07")).name;
    }

    private static async Task Frames()
    {
        var target = Time.frameCount + 3;
        await Wait.Until(() => Time.frameCount >= target, because: "advancing game frames");
    }
}
