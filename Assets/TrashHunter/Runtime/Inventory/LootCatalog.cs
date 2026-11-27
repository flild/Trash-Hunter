using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrashHunter
{
    internal sealed class LootCatalog
    {
        [Serializable]
        internal sealed class Entry
        {
            public string id;
            public string name;
            public int tier;
            public int baseValue;
            public int conditionDiscount;
            public string condition;
            public Vector2Int size;
            public bool legacyLayoutOnly;
            public bool optionalLayout;
            public string[] contents = Array.Empty<string>();

            public LootDefinition Definition => new(name, tier, baseValue, conditionDiscount, condition, size);
        }

        [Serializable]
        private sealed class Data
        {
            public Entry[] items = Array.Empty<Entry>();
        }

        private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

        public HashSet<string> LayoutKeys { get; } = new(StringComparer.Ordinal);
        public HashSet<string> OptionalLayoutKeys { get; } = new(StringComparer.Ordinal);
        public HashSet<string> PackageKeys { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> InitialPackages { get; } = new(StringComparer.Ordinal);

        private LootCatalog(Entry[] items)
        {
            foreach (var item in items)
            {
                entries.Add(item.id, item);
                if (item.contents.Length > 0)
                {
                    PackageKeys.Add(item.id);
                    foreach (var id in item.contents)
                        InitialPackages.Add(id, item.id);
                }
                else if (item.optionalLayout)
                    OptionalLayoutKeys.Add(item.id);
                else if (!item.legacyLayoutOnly)
                    LayoutKeys.Add(item.id);
            }

            foreach (var id in InitialPackages.Keys)
                if (!LayoutKeys.Contains(id))
                    throw new InvalidOperationException($"Invalid package content in LootCatalog: '{id}'.");
        }

        public static LootCatalog Load()
        {
            var asset = Resources.Load<TextAsset>("LootCatalog");
            var data = asset != null ? JsonUtility.FromJson<Data>(asset.text) : null;
            if (data?.items is not { Length: > 0 })
                throw new InvalidOperationException("LootCatalog is missing or empty.");

            return new LootCatalog(data.items);
        }

        public Entry this[string id] => entries[id];

        public bool TryGetDefinition(string id, out LootDefinition definition)
        {
            var found = entries.TryGetValue(id, out var item);
            definition = found ? item.Definition : default;
            return found;
        }
    }
}
