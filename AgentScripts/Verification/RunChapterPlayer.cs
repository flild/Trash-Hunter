#if !UNITY_EDITOR && DEBUG
using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using Debug = UnityEngine.Debug;

internal static class RunChapterPlayer
{
    public static string SlotFolder { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static async void Run()
    {
        var args = Environment.GetCommandLineArgs();
        var phase = Value("-trash-hunter-i240-check");
        if (phase == null)
            return;

        try
        {
            var root = Path.GetFullPath("Logs/I-240");
            var folder = Value("-trash-hunter-save-dir");
            if (folder == null || !Path.IsPathRooted(folder) || !Directory.Exists(folder))
                throw new InvalidOperationException("Pass an existing absolute path for an isolated slot.");
            SlotFolder = Path.GetFullPath(folder);
            if (!string.Equals(Path.GetDirectoryName(SlotFolder), root, StringComparison.OrdinalIgnoreCase) ||
                Value("-trash-hunter-locale") != "ru")
                throw new InvalidOperationException("Use an isolated slot directly under Logs/I-240 and locale ru.");

            var slotPath = Path.Combine(SlotFolder, "slot.json");
            var pid = Process.GetCurrentProcess().Id;
            Application.runInBackground = true;
            if (phase == "route" && !File.Exists(slotPath))
            {
                var first = await VerifyChapterEvidence.Run("first");
                var lead = await VerifyChapterEvidence.Run("player-lead");
                WriteResult(new { pid, unity = Application.unityVersion, first, lead });
            }
            else if (phase == "resume" && File.Exists(slotPath))
            {
                var before = File.ReadAllText(slotPath);
                var resumed = await VerifyChapterEvidence.Run("player-resume");
                if (before != File.ReadAllText(slotPath))
                    throw new InvalidOperationException("Continuing and re-reading the recorder must preserve the save.");
                WriteResult(new { pid, unity = Application.unityVersion, resumed, slotUnchangedBeforeQuit = true });
            }
            else
            {
                throw new InvalidOperationException("Run route on an empty slot and resume on its saved slot.");
            }
            Application.Quit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Application.Quit(1);
        }

        string Value(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        void WriteResult(object result) =>
            File.WriteAllText(Path.Combine(SlotFolder, phase + ".json"), JsonConvert.SerializeObject(result, Formatting.Indented));
    }
}
#endif
