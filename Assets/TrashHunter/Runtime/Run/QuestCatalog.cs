using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrashHunter
{
    internal enum QuestAction { Inspect = 1, Present, Deliver, Sell }

    [Serializable]
    internal sealed class QuestStep
    {
        public string id;
        public QuestAction action;
        public string[] items;

        public bool Matches(QuestAction fact, string item) =>
            action == fact && Array.IndexOf(items, item) >= 0;
    }

    [Serializable]
    internal sealed class QuestDefinition
    {
        public string id;
        public bool requiresSale;
        public QuestStep[] steps;
        public string contact;
        public int reward;
    }

    internal sealed class QuestCatalog
    {
        [Serializable]
        private sealed class Data
        {
            public QuestDefinition[] quests;
        }

        private readonly Dictionary<string, QuestDefinition> definitions = new(StringComparer.Ordinal);

        private QuestCatalog(QuestDefinition[] quests, LootCatalog loot)
        {
            foreach (var quest in quests)
            {
                if (string.IsNullOrWhiteSpace(quest?.id) || quest.reward < 0 ||
                    quest.steps is not { Length: > 0 } || !definitions.TryAdd(quest.id, quest))
                    throw new InvalidOperationException("Invalid or duplicate quest definition.");

                var stepIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var step in quest.steps)
                {
                    if (string.IsNullOrWhiteSpace(step?.id) || !stepIds.Add(step.id) ||
                        !Enum.IsDefined(typeof(QuestAction), step.action) || step.items is not { Length: > 0 })
                        throw new InvalidOperationException($"Invalid step in quest '{quest.id}'.");

                    foreach (var item in step.items)
                        if (string.IsNullOrEmpty(item) || !loot.TryGetDefinition(item, out var definition) ||
                            loot.PackageKeys.Contains(item) || (step.action != QuestAction.Inspect && definition.Price <= 0))
                            throw new InvalidOperationException($"Invalid item '{item}' in quest '{quest.id}'.");
                }
            }
        }

        internal static QuestCatalog Load(LootCatalog loot) =>
            Parse(Resources.Load<TextAsset>("QuestCatalog")?.text, loot);

        internal static QuestCatalog Parse(string json, LootCatalog loot)
        {
            var data = json == null ? null : JsonUtility.FromJson<Data>(json);
            if (data?.quests == null)
                throw new InvalidOperationException("QuestCatalog is missing or invalid.");
            return new QuestCatalog(data.quests, loot);
        }

        public bool TryGet(string id, out QuestDefinition quest) =>
            definitions.TryGetValue(id ?? string.Empty, out quest);
    }
}
