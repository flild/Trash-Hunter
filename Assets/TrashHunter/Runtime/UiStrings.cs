using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrashHunter
{
    internal sealed class UiStrings
    {
        [Serializable]
        private sealed class Table
        {
            public Entry[] entries;
        }

        [Serializable]
        private sealed class Entry
        {
            public string key;
            public string value;
        }

        private readonly Dictionary<string, string> entries = new(StringComparer.Ordinal);

        public UiStrings()
        {
            Load("ru");
            if (string.Equals(RequestedLocale(), "en", StringComparison.OrdinalIgnoreCase))
                Load("en");
        }

        public string Get(string key, string fallback = null) =>
            entries.TryGetValue(key, out var value) ? value : fallback ?? key;

        private void Load(string locale)
        {
            var asset = Resources.Load<TextAsset>($"Localization/{locale}");
            if (asset == null)
                return;

            foreach (var entry in JsonUtility.FromJson<Table>(asset.text).entries)
                entries[entry.key] = entry.value;
        }

        private static string RequestedLocale()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-trash-hunter-locale");
            if (index >= 0 && index + 1 < args.Length)
                return args[index + 1];

#if UNITY_EDITOR
            return Environment.GetEnvironmentVariable("TRASH_HUNTER_LOCALE");
#else
            return null;
#endif
        }
    }
}
