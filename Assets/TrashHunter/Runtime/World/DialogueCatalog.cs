using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrashHunter
{
    internal enum DialogueAction { AcceptQuest = 1, AskAboutBatch = 2 }

    [Serializable]
    internal sealed class DialogueTopic
    {
        public string id;
        public string textKey;
        public string replyKey;
        public string availableQuestId;
        public StoryFact requiresFacts;
        public StoryFact excludesFacts;
        public DialogueAction action;

        internal bool IsVisible(GameState state) =>
            (string.IsNullOrEmpty(availableQuestId) || state.Quests.IsAvailable(availableQuestId)) &&
            state.Knows(requiresFacts) &&
            (excludesFacts == StoryFact.None || !state.Knows(excludesFacts));
    }

    internal sealed class DialogueCatalog
    {
        private const StoryFact KnownFacts = StoryFact.Tag | StoryFact.Lead | StoryFact.Recorder;

        [Serializable]
        private sealed class Data
        {
            public DialogueTopic[] topics;
        }

        private readonly DialogueTopic[] topics;

        private DialogueCatalog(DialogueTopic[] topics, QuestCatalog quests)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var topic in topics)
            {
                if (string.IsNullOrWhiteSpace(topic?.id) || !ids.Add(topic.id) ||
                    string.IsNullOrWhiteSpace(topic.textKey) || string.IsNullOrWhiteSpace(topic.replyKey) ||
                    !Enum.IsDefined(typeof(DialogueAction), topic.action) ||
                    ((topic.requiresFacts | topic.excludesFacts) & ~KnownFacts) != 0)
                    throw new InvalidOperationException("Invalid or duplicate trader dialogue topic.");

                if ((!string.IsNullOrEmpty(topic.availableQuestId) || topic.action == DialogueAction.AcceptQuest) &&
                    !quests.TryGet(topic.availableQuestId, out _))
                    throw new InvalidOperationException($"Invalid quest in dialogue topic '{topic.id}'.");
            }

            this.topics = topics;
        }

        internal static DialogueCatalog Load(QuestCatalog quests) =>
            Parse(Resources.Load<TextAsset>("TraderDialogue")?.text, quests);

        internal static DialogueCatalog Parse(string json, QuestCatalog quests)
        {
            var data = json == null ? null : JsonUtility.FromJson<Data>(json);
            if (data?.topics is not { Length: > 0 })
                throw new InvalidOperationException("TraderDialogue is missing or invalid.");

            return new DialogueCatalog(data.topics, quests);
        }

        internal IEnumerable<DialogueTopic> VisibleTopics(GameState state)
        {
            if (state.Location != RunLocation.Trader)
                yield break;

            foreach (var topic in topics)
                if (topic.IsVisible(state))
                    yield return topic;
        }
    }
}
