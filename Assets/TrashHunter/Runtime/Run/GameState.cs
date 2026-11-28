using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TrashHunter
{
    public enum RunLocation { Search = 0, Home = 1, Trader = 2 }

    public sealed class GameState : IDisposable
    {
        private const string CabinetId = "cabinet-a";
        internal const string HomeTag = "home-tag";
        internal const string BatchRecorder = "batch-recorder";
        private static readonly string[] LegacyLayout = { "crate-a", "lamp-a", "radio-a", "strip-a", "dial-a" };
        private readonly LootCatalog catalog;
        private RunSnapshot current;
        private SaveSlot slot;
        private BagInventory bag = new();

        internal GameState(LootCatalog catalog, QuestCatalog quests)
        {
            this.catalog = catalog;
            Trade = new TradeCounter(this, catalog);
            Quests = new QuestJournal(this, quests);
        }

        internal bool SlotLoaded => slot != null;
        internal bool HasRun => current != null;
        internal RunSnapshot Snapshot => current;
        internal bool ResumePending { get; private set; }
        public BagInventory Bag => bag;
        public bool HasBag => current.hasBag;
        public bool HasBackpack => current.hasBackpack;
        public string HeldItemId => string.IsNullOrEmpty(current.held) ? null : current.held;
        internal bool HeldInWorld => current.heldInWorld;
        internal bool AtSecondYard => current?.secondYard == true;
        internal bool Knows(StoryFact fact) => HasRun && (current.story & fact) == fact;
        public bool HeldItemRotated { get; set; }
        public int Expedition => current.expedition;
        public RunLocation Location => current?.location ?? RunLocation.Search;
        public bool AtHome => Location == RunLocation.Home;
        public long Balance => current.cash;
        public IReadOnlyList<HomeItem> Home => current.home;
        public TradeCounter Trade { get; }
        public QuestJournal Quests { get; }
        public string Error { get; private set; }
        public string RecoveryNotice => slot?.RecoveryNotice;
        internal PlayerPose Player => current.player;
        internal double LastWriteMs { get; private set; }

        internal bool LoadSlot()
        {
            if (SlotLoaded)
                return true;

            try
            {
                slot = new SaveSlot(ResolveSlotFolder(), Validate);
                current = slot.Load();
                ResumePending = HasRun;
                if (HasRun)
                    RestoreBag();
                Error = null;
                return true;
            }
            catch (Exception exception) when (SaveSlot.IsReadError(exception))
            {
                Dispose();
                Error = $"Не удалось открыть автосохранение. {SaveSlot.Describe(exception)}";
                return false;
            }
        }

        internal bool StartNewRun(SearchPile pile, PlayerPose spawn)
        {
            if (!SlotLoaded || HasRun)
                return false;

            var snapshot = new RunSnapshot
            {
                format = RunSnapshot.CurrentFormat,
                runId = Guid.NewGuid().ToString("N"),
                expedition = 1,
                player = spawn,
                home = Array.Empty<HomeItem>()
            };
            if (!TryGeneratePile(snapshot, pile))
                return false;

            current = snapshot;
            return true;
        }

        internal void AcknowledgeResume() => ResumePending = false;

        public bool TryGetPose(string id, out (Vector3 Position, Quaternion Rotation) pose)
        {
            var index = FindWorldItem(id);
            if (index < 0)
            {
                pose = default;
                return false;
            }

            var item = current.world[index];
            pose = (item.position, item.rotation);
            return true;
        }

        internal void RecordPose(string id, Vector3 position, Quaternion rotation)
        {
            var index = FindWorldItem(id);
            if (index < 0)
                return;

            current.world[index].position = position;
            current.world[index].rotation = rotation;
        }

        internal void RecordPlayer(PlayerPose pose) => current.player = pose;
        internal void RecordHeldInWorld(bool inWorld) => current.heldInWorld = inWorld;

        internal bool EquipBag()
        {
            if (Location != RunLocation.Search || HasBag)
                return false;

            var next = current.Copy();
            next.hasBag = true;
            return Publish(next);
        }

        internal bool IsPackageOpen(string id)
        {
            var index = FindWorldItem(id);
            return index >= 0 && current.world[index].opened;
        }

        internal bool IsInPackage(string id)
        {
            var index = FindWorldItem(id);
            return index >= 0 && !string.IsNullOrEmpty(current.world[index].package);
        }

        internal bool OpenPackage(string id, IReadOnlyDictionary<string, Vector3> drops)
        {
            var index = FindWorldItem(id);
            if (!HasBag || HeldItemId != null || index < 0 || !catalog.PackageKeys.Contains(id))
                return false;
            var next = current.Copy();
            var released = 0;
            for (var i = 0; i < next.world.Length; i++)
            {
                if (next.world[i].package != id)
                    continue;
                if (!drops.TryGetValue(next.world[i].key, out var position))
                    return false;
                next.world[i].package = null;
                next.world[i].position = position;
                next.world[i].rotation = Quaternion.identity;
                released++;
            }
            if (released != drops.Count || (released == 0 && next.world[index].opened))
                return false;
            next.world[index].opened = true;
            return Publish(next);
        }

        internal bool Take(string id)
        {
            var index = FindWorldItem(id);
            if (!HasBag || HeldItemId != null || index < 0 || catalog.PackageKeys.Contains(id))
                return false;

            if (!string.IsNullOrEmpty(current.world[index].package))
                return false;

            var next = current.Copy();
            next.held = id;
            next.heldRotated = next.world[index].rotated;
            next.heldInWorld = !next.world[index].packed;
            next.world[index].packed = false;
            next.world[index].rotated = false;
            return Publish(next);
        }

        internal bool Pack(BagPlacement placement)
        {
            var index = FindWorldItem(HeldItemId);
            if (index < 0 || bag.Check(placement) != BagFit.Fits)
                return false;

            var next = current.Copy();
            next.world[index].packed = true;
            next.world[index].column = placement.Column;
            next.world[index].row = placement.Row;
            next.world[index].rotated = placement.Rotated;
            next.held = null;
            next.heldRotated = false;
            next.heldInWorld = false;
            return Publish(next);
        }

        internal bool MovePacked(string id, BagPlacement placement)
        {
            var index = FindWorldItem(id);
            if (index < 0 || !current.world[index].packed)
                return false;

            var item = current.world[index];
            item.column = placement.Column;
            item.row = placement.Row;
            item.rotated = placement.Rotated;
            if (bag.Check(catalog[id].Definition.Placement(item), id) != BagFit.Fits)
                return false;

            var next = current.Copy();
            next.world[index] = item;
            next.heldRotated = HeldItemRotated;
            return Publish(next);
        }

        internal bool Place(Vector3 position, Quaternion rotation)
        {
            var index = FindWorldItem(HeldItemId);
            if (index < 0)
                return false;

            var next = current.Copy();
            next.world[index].position = position;
            next.world[index].rotation = rotation;
            next.held = null;
            next.heldRotated = false;
            next.heldInWorld = false;
            return Publish(next);
        }

        internal bool ReturnHome(int expedition)
        {
            if (Location != RunLocation.Search || !HasBag || Expedition != expedition)
                return false;

            var next = current.Copy();
            var unpacked = new List<HomeItem>(current.home);
            foreach (var item in current.world)
            {
                if (item.packed)
                {
                    unpacked.Add(new HomeItem
                    {
                        expedition = Expedition,
                        key = item.key,
                        work = catalog[item.key].dirtDiscount > 0 ? HomeWork.Dirty : HomeWork.None
                    });
                }
            }

            next.home = unpacked.ToArray();
            next.world = Array.Empty<WorldItem>();
            next.held = null;
            next.heldRotated = false;
            next.heldInWorld = false;
            next.location = RunLocation.Home;
            next.secondYard = false;
            next.player = default;
            return Publish(next);
        }

        internal bool TravelBetweenSettlements(RunLocation destination)
        {
            if (!((Location == RunLocation.Home && destination == RunLocation.Trader) ||
                (Location == RunLocation.Trader && destination == RunLocation.Home)))
                return false;

            var next = current.Copy();
            next.location = destination;
            return Publish(next);
        }

        public SaleOffer Quote(HomeItem item) => Trade.Quote(item);
        public bool Sell(SaleOffer offer) => Trade.Sell(offer);

        internal bool TryGetHomeItem(HomeItem item, out HomeItem stored)
        {
            var index = Array.IndexOf(current.home, item);
            stored = index < 0 ? default : current.home[index];
            return index >= 0;
        }

        internal bool TreatHomeItem(HomeItem item, HomeWork operation)
        {
            if (!AtHome)
                return false;

            var index = Array.IndexOf(current.home, item);
            if (index < 0)
                return false;

            var stored = current.home[index];
            var entry = catalog[stored.key];
            var partIndex = -1;
            switch (operation)
            {
                case HomeWork.Dirty when stored.IsDirty:
                    stored.work &= ~HomeWork.Dirty;
                    break;
                case HomeWork.Diagnosed when !stored.IsDiagnosed &&
                    entry.benchCheck is BenchCheck.Working or BenchCheck.LampHinge:
                    stored.work |= HomeWork.Diagnosed;
                    break;
                case HomeWork.Repaired when stored.IsDiagnosed && !stored.IsRepaired && entry.benchCheck == BenchCheck.LampHinge:
                    partIndex = FindRepairPart(entry.repairPart);
                    if (partIndex < 0)
                        return false;

                    stored.work |= HomeWork.Repaired;
                    break;
                default:
                    return false;
            }

            var next = current.Copy();
            next.SetHomeItem(index, stored);
            if (partIndex >= 0)
                next.RemoveHomeItem(partIndex);
            return Publish(next);
        }

        private int FindRepairPart(string key) =>
            Array.FindIndex(current.home, item => item.key == key);

        internal bool HasRepairPart(string key) => FindRepairPart(key) >= 0;

        internal bool CompleteRepair(HomeItem item, int price)
        {
            if (price <= 0 || Balance < price)
                return false;

            var index = Array.IndexOf(current.home, item);
            var repaired = current.home[index];
            repaired.work |= HomeWork.Diagnosed | HomeWork.Repaired;
            var next = current.Copy();
            next.SetHomeItem(index, repaired);
            next.cash -= price;
            return Publish(next);
        }

        internal bool CompleteBackpackPurchase(int price)
        {
            if (Location != RunLocation.Trader || HasBackpack || Balance < price)
                return false;

            var next = current.Copy();
            next.cash -= price;
            next.hasBackpack = true;
            return Publish(next);
        }

        internal bool CompleteSale(HomeItem item, int price)
        {
            if (Location != RunLocation.Trader || price <= 0 || Balance > long.MaxValue - price ||
                SaleBlock(item) != null)
                return false;

            var index = Array.IndexOf(current.home, item);
            if (index < 0)
                return false;

            var next = current.Copy();
            next.RemoveHomeItem(index);
            next.cash += price;
            next.hasSold = true;
            return Quests.ApplyFact(next, QuestAction.Sell, item.key) && Publish(next);
        }

        internal string SaleBlock(HomeItem item) => item.key switch
        {
            HomeTag when !Knows(StoryFact.Tag) => "story.saleTagBlocked",
            BatchRecorder => "story.saleRecorderBlocked",
            _ => null
        };

        internal static bool IsEvidence(string key) => key is HomeTag or BatchRecorder;

        internal bool ReadHeldEvidence(StoryFact fact)
        {
            if (Location != RunLocation.Search || !EvidenceMatches(HeldItemId, fact))
                return false;

            return SaveStoryFact(fact, HeldItemRotated);
        }

        internal bool ReadStockEvidence(HomeItem item, StoryFact fact) =>
            Location == RunLocation.Trader && TryGetHomeItem(item, out _) &&
            EvidenceMatches(item.key, fact) && SaveStoryFact(fact, false);

        internal bool AskAboutBatch() => Location == RunLocation.Trader && Knows(StoryFact.Tag) &&
            SaveStoryFact(StoryFact.Lead, false);

        private static bool EvidenceMatches(string key, StoryFact fact) => key switch
        {
            HomeTag => fact is StoryFact.Address or StoryFact.Batch,
            BatchRecorder => fact == StoryFact.Recorder,
            _ => false
        };

        private bool SaveStoryFact(StoryFact fact, bool heldRotated)
        {
            if (Knows(fact))
                return true;

            var next = current.Copy();
            next.story |= fact;
            next.heldRotated = heldRotated;
            return Publish(next);
        }

        internal RunSnapshot PrepareDeparture(int previousExpedition, SearchPile pile, PlayerPose spawn, bool secondYard)
        {
            if (!AtHome || Expedition != previousExpedition || Expedition == int.MaxValue ||
                (secondYard && !Knows(StoryFact.Lead)))
                return null;

            var next = current.Copy();
            next.expedition++;
            next.location = RunLocation.Search;
            next.secondYard = secondYard;
            if (!TryGeneratePile(next, pile))
                return null;
            next.player = spawn;
            return next;
        }

        internal bool Save()
        {
            var next = current.Copy();
            next.heldRotated = HeldItemRotated;
            return Publish(next);
        }

        public void Dispose()
        {
            slot?.Dispose();
            slot = null;
        }

        internal bool Publish(RunSnapshot next)
        {
            next.revision = current.revision + 1;
            next.utcTicks = DateTime.UtcNow.Ticks;
            var writeStart = Time.realtimeSinceStartupAsDouble;
            var saved = slot.TryWrite(next, out var error);
            LastWriteMs = (Time.realtimeSinceStartupAsDouble - writeStart) * 1000;
            Error = error;
            if (!saved)
                return false;

            current = next;
            RestoreBag();
            return true;
        }

        private void RestoreBag()
        {
            bag = new BagInventory(current.hasBackpack);
            foreach (var item in current.world)
            {
                if (item.packed)
                    bag.TryPack(item.key, catalog[item.key].Definition.Placement(item));
            }
            HeldItemRotated = current.heldRotated;
        }

        private int FindWorldItem(string id) => Array.FindIndex(current.world, item => item.key == id);

        private bool Validate(RunSnapshot data)
        {
            if (data.format != RunSnapshot.CurrentFormat || !Guid.TryParseExact(data.runId, "N", out _) ||
                data.revision <= 0 || data.utcTicks <= 0 || data.utcTicks > DateTime.MaxValue.Ticks ||
                data.location is not (RunLocation.Search or RunLocation.Home or RunLocation.Trader) ||
                data.expedition < 1 || data.cash < 0 || data.home == null || data.world == null ||
                data.pileGeneratorVersion is < 0 or > SearchPile.GeneratorVersion ||
                !Finite(data.player.position) || !float.IsFinite(data.player.yaw) ||
                !float.IsFinite(data.player.pitch) || Mathf.Abs(data.player.pitch) > 82f ||
                !Quests.Validate(data) || !ValidStory(data))
                return false;

            var stored = new HashSet<HomeItem>();
            foreach (var item in data.home)
            {
                if (string.IsNullOrEmpty(item.key) || !catalog.TryGetDefinition(item.key, out var definition) ||
                    definition.Price <= 0 || item.expedition <= 0 || item.expedition > data.expedition ||
                    (data.location == RunLocation.Search && item.expedition == data.expedition) || !stored.Add(item) ||
                    !ValidHomeWork(item, catalog[item.key]))
                    return false;
            }

            var hasHand = !string.IsNullOrEmpty(data.held);
            if (data.location != RunLocation.Search)
                return data.hasBag && data.world.Length == 0 && !hasHand && !data.heldRotated && !data.heldInWorld;
            if ((!data.hasBag && (hasHand || data.player.inside || data.hasBackpack)) ||
                (!hasHand && (data.heldRotated || data.heldInWorld)))
                return false;

            var hasPackages = data.pileGeneratorVersion >= 2;
            var keys = new HashSet<string>();
            var opened = new HashSet<string>();
            var extracted = new HashSet<string>();
            var packed = new BagInventory(data.hasBackpack);
            foreach (var item in data.world)
            {
                var r = item.rotation;
                if (string.IsNullOrEmpty(item.key) || !catalog.TryGetDefinition(item.key, out var definition) ||
                    !keys.Add(item.key) || !Finite(item.position) || !float.IsFinite(Quaternion.Dot(r, r)) ||
                    Mathf.Abs(Quaternion.Dot(r, r) - 1f) > 0.02f)
                    return false;
                var isPackage = catalog.PackageKeys.Contains(item.key);
                var nested = !string.IsNullOrEmpty(item.package);
                if (isPackage && (item.key == data.held || item.packed || nested) ||
                    item.opened && (!isPackage || !data.hasBag))
                    return false;
                if (item.opened)
                    opened.Add(item.key);
                if (nested && (!hasPackages || item.packed || item.key == data.held ||
                    !catalog.InitialPackages.TryGetValue(item.key, out var owner) || item.package != owner))
                    return false;
                if (hasPackages && !nested && catalog.InitialPackages.TryGetValue(item.key, out var originalPackage))
                    extracted.Add(originalPackage);
                if (item.packed && (!data.hasBag || item.key == data.held || definition.Price <= 0 ||
                    !packed.TryPack(item.key, definition.Placement(item))))
                    return false;
            }
            if (hasHand && !keys.Contains(data.held))
                return false;
            if (hasPackages)
            {
                if (!extracted.IsSubsetOf(opened) || !catalog.PackageKeys.IsSubsetOf(keys))
                    return false;
                keys.ExceptWith(catalog.PackageKeys);
                if (data.pileGeneratorVersion >= 3)
                    keys.ExceptWith(catalog.OptionalLayoutKeys);
                return keys.SetEquals(data.secondYard ? catalog.SecondYardKeys : catalog.LayoutKeys);
            }

            // I-040 expeditions predate the cabinet. Preserve their exact set of finds.
            var layoutKeys = catalog.LayoutKeys;
            var beforeCabinet = keys.Count + 1 == layoutKeys.Count && layoutKeys.Contains(CabinetId) &&
                !keys.Contains(CabinetId) && keys.IsSubsetOf(layoutKeys);
            return keys.SetEquals(layoutKeys) || keys.SetEquals(LegacyLayout) || beforeCabinet;
        }

        private static bool ValidStory(RunSnapshot data)
        {
            var story = data.story;
            var hasLead = (story & StoryFact.Lead) != 0;
            return (story & ~(StoryFact.Tag | StoryFact.Lead | StoryFact.Recorder)) == 0 &&
                (story == StoryFact.None || data.hasSold) &&
                (!hasLead || (story & StoryFact.Tag) == StoryFact.Tag) &&
                ((story & StoryFact.Recorder) == 0 || hasLead) &&
                (!data.secondYard || (data.location == RunLocation.Search && hasLead &&
                    data.pileGeneratorVersion >= 3)) &&
                !Array.Exists(data.world, item => item.key == HomeTag && (!data.hasSold || data.secondYard) ||
                    item.key == BatchRecorder && !data.secondYard) &&
                !Array.Exists(data.home, item => item.key == HomeTag && !data.hasSold ||
                    item.key == BatchRecorder && !hasLead);
        }

        private static bool Finite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static bool ValidHomeWork(HomeItem item, LootCatalog.Entry entry) =>
            (item.work & ~(HomeWork.Dirty | HomeWork.Diagnosed | HomeWork.Repaired)) == 0 &&
            (!item.IsDirty || entry.dirtDiscount > 0) &&
            (!item.IsDiagnosed || entry.benchCheck is BenchCheck.Working or BenchCheck.LampHinge ||
                item.IsRepaired && entry.specialistFee > 0) &&
            (!item.IsRepaired || item.IsDiagnosed && (entry.benchCheck == BenchCheck.LampHinge || entry.specialistFee > 0));

        private bool TryGeneratePile(RunSnapshot snapshot, SearchPile pile)
        {
            // A failed write and retry for the same expedition must produce the same pile.
            var runSeed = BitConverter.ToInt32(Guid.ParseExact(snapshot.runId, "N").ToByteArray(), 0);
            snapshot.pileSeed = unchecked(runSeed + snapshot.expedition * 1009);
            var questFind = snapshot.secondYard ? null : Quests.ExpeditionFind(snapshot);
            var evidence = ExpeditionEvidence(snapshot);
            snapshot.pileGeneratorVersion = questFind == null && evidence == null && !snapshot.secondYard
                ? SearchPile.BaseGeneratorVersion : SearchPile.GeneratorVersion;
            try
            {
                snapshot.world = pile.Generate(snapshot.pileSeed, questFind, evidence);
                Error = null;
                return true;
            }
            catch (InvalidDataException exception)
            {
                Error = exception.Message;
                return false;
            }
        }

        private static string ExpeditionEvidence(RunSnapshot snapshot)
        {
            if (snapshot.secondYard)
                return Array.Exists(snapshot.home, item => item.key == BatchRecorder) ? null : BatchRecorder;
            if (!snapshot.hasSold || (snapshot.story & StoryFact.Tag) == StoryFact.Tag ||
                Array.Exists(snapshot.home, item => item.key == HomeTag))
                return null;
            return HomeTag;
        }

        internal static int LayoutIndex(int expedition) => (expedition - 1) % 3;

        private static string ResolveSlotFolder()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-trash-hunter-save-dir");
            if (index >= 0 && index + 1 < args.Length && Path.IsPathRooted(args[index + 1]))
                return args[index + 1];

#if UNITY_EDITOR
            var editorSlot = Environment.GetEnvironmentVariable("TRASH_HUNTER_EDITOR_SAVE_DIR");
            if (!string.IsNullOrEmpty(editorSlot) && Path.IsPathRooted(editorSlot))
                return editorSlot;
            return Path.Combine(Application.persistentDataPath, "prototype-editor");
#else
            return Path.Combine(Application.persistentDataPath, "prototype");
#endif
        }
    }
}
