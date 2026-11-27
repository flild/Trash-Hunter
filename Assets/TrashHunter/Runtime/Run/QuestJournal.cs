using System;
using System.Collections.Generic;

namespace TrashHunter
{
    [Serializable]
    internal struct QuestProgress
    {
        public string id;
        public string step;
    }

    public sealed class QuestJournal
    {
        private readonly GameState state;
        private readonly QuestCatalog catalog;

        internal QuestJournal(GameState state, QuestCatalog catalog)
        {
            this.state = state;
            this.catalog = catalog;
        }

        public bool IsAvailable(string id) =>
            state.HasRun && catalog.TryGet(id, out var quest) &&
            (!quest.requiresSale || state.Snapshot.hasSold) && Find(state.Snapshot, id) < 0;

        public bool TryGetProgress(string id, out string nextStep)
        {
            var index = state.HasRun ? Find(state.Snapshot, id) : -1;
            nextStep = index < 0 ? null : state.Snapshot.quests[index].step;
            return index >= 0;
        }

        public bool HasContact(string id) =>
            state.HasRun && !string.IsNullOrWhiteSpace(id) && Array.IndexOf(state.Snapshot.contacts, id) >= 0;

        public bool Accept(string id)
        {
            if (!IsAvailable(id) || state.Location != RunLocation.Trader || !catalog.TryGet(id, out var quest))
                return false;

            var next = state.Snapshot.Copy();
            var progress = new List<QuestProgress>(next.quests)
            {
                new() { id = id, step = quest.steps[0].id }
            };
            next.quests = progress.ToArray();
            return state.Publish(next);
        }

        internal bool HasInspectionStep(string item) =>
            Array.Exists(state.Snapshot.quests,
                progress => Match(progress, QuestAction.Inspect, item, out _, out _));

        internal bool RecordInspection(string item)
        {
            if (state.Location != RunLocation.Search || state.HeldItemId != item || !HasInspectionStep(item))
                return false;

            var next = state.Snapshot.Copy();
            next.heldRotated = state.HeldItemRotated;
            return ApplyFact(next, QuestAction.Inspect, item) && state.Publish(next);
        }

        internal bool Confirm(string id, string step, QuestAction action, HomeItem item)
        {
            if (state.Location != RunLocation.Trader || !state.HasRun)
                return false;

            var itemIndex = Array.IndexOf(state.Snapshot.home, item);
            var questIndex = Find(state.Snapshot, id);
            if (itemIndex < 0 || questIndex < 0)
                return false;

            var progress = state.Snapshot.quests[questIndex];
            if (progress.step != step || !Match(progress, action, item.key, out var quest, out var stepIndex))
                return false;

            var next = state.Snapshot.Copy();
            if (!Advance(next, questIndex, quest, stepIndex))
                return false;
            if (action == QuestAction.Deliver)
                next.RemoveHomeItem(itemIndex);
            return state.Publish(next);
        }

        // An observation or ordinary sale can satisfy one current step per accepted quest.
        internal bool ApplyFact(RunSnapshot next, QuestAction action, string item)
        {
            for (var index = 0; index < next.quests.Length; index++)
                if (Match(next.quests[index], action, item, out var quest, out var stepIndex) &&
                    !Advance(next, index, quest, stepIndex))
                    return false;

            return true;
        }

        internal bool Validate(RunSnapshot snapshot)
        {
            if (snapshot.quests == null || snapshot.contacts == null)
                return false;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var contacts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var progress in snapshot.quests)
            {
                if (!catalog.TryGet(progress.id, out var quest) || !ids.Add(progress.id) ||
                    (quest.requiresSale && !snapshot.hasSold))
                    return false;
                if (string.IsNullOrEmpty(progress.step))
                {
                    if (!string.IsNullOrWhiteSpace(quest.contact))
                        contacts.Add(quest.contact);
                }
                else if (Array.FindIndex(quest.steps, step => step.id == progress.step) < 0)
                    return false;
            }
            return contacts.Count == snapshot.contacts.Length && contacts.SetEquals(snapshot.contacts);
        }

        private static int Find(RunSnapshot snapshot, string id) =>
            Array.FindIndex(snapshot.quests, progress => progress.id == id);

        private bool Match(QuestProgress progress, QuestAction action, string item,
            out QuestDefinition quest, out int stepIndex)
        {
            stepIndex = -1;
            if (!catalog.TryGet(progress.id, out quest) || string.IsNullOrEmpty(progress.step))
                return false;

            stepIndex = Array.FindIndex(quest.steps, step => step.id == progress.step);
            return stepIndex >= 0 && quest.steps[stepIndex].Matches(action, item);
        }

        private static bool Advance(RunSnapshot next, int index, QuestDefinition quest, int stepIndex)
        {
            var finished = stepIndex + 1 == quest.steps.Length;
            if (finished && next.cash > long.MaxValue - quest.reward)
                return false;

            next.quests[index].step = finished ? string.Empty : quest.steps[stepIndex + 1].id;
            if (finished)
            {
                next.cash += quest.reward;
                if (!string.IsNullOrWhiteSpace(quest.contact) && Array.IndexOf(next.contacts, quest.contact) < 0)
                {
                    Array.Resize(ref next.contacts, next.contacts.Length + 1);
                    next.contacts[^1] = quest.contact;
                }
            }
            return true;
        }
    }
}
