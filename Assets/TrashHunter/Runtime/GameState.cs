using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TrashHunter
{
    public sealed class GameState : IDisposable
    {
        private static readonly string[] LegacyLayout = { "crate-a", "lamp-a", "radio-a", "strip-a", "dial-a" };
        private readonly Dictionary<string, LootDefinition> catalog = new();
        private readonly HashSet<string> layoutKeys = new();
        private RunSnapshot current;
        private SaveSlot slot;
        private BagInventory bag = new();

        public BagInventory Bag => bag;
        public bool HasBag => current.hasBag;
        public string HeldItemId => string.IsNullOrEmpty(current.held) ? null : current.held;
        public bool HeldItemRotated { get; set; }
        public int Expedition => current.expedition;
        public bool AtHome => current.atHome;
        public long Balance => current.cash;
        public IReadOnlyList<HomeItem> Home => current.home;
        public string Error { get; private set; }
        public string RecoveryNotice => slot?.RecoveryNotice;
        internal PlayerPose Player => current.player;

        internal bool Open(SearchItem[] items, PlayerPose spawn, out bool resuming)
        {
            resuming = false;
            if (current != null)
                return true;

            foreach (var item in items)
            {
                catalog[item.Id] = item.Definition;
                if (!item.LegacyLayoutOnly)
                    layoutKeys.Add(item.Id);
            }

            try
            {
                slot = new SaveSlot(ResolveSlotFolder(), Validate);
                var loaded = slot.Load();
                resuming = loaded != null;
                current = loaded ?? new RunSnapshot
                {
                    format = RunSnapshot.CurrentFormat,
                    runId = Guid.NewGuid().ToString("N"),
                    expedition = 1,
                    player = spawn,
                    world = CreateLayout(items, 1),
                    home = Array.Empty<HomeItem>()
                };
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

        internal bool EquipBag()
        {
            if (AtHome || HasBag)
                return false;

            var next = current.Copy();
            next.hasBag = true;
            return Publish(next);
        }

        internal bool Take(string id)
        {
            var index = FindWorldItem(id);
            if (!HasBag || HeldItemId != null || index < 0)
                return false;

            var next = current.Copy();
            next.held = id;
            next.heldRotated = next.world[index].rotated;
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
            if (bag.Check(catalog[id].Placement(item), id) != BagFit.Fits)
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
            return Publish(next);
        }

        internal bool ReturnHome(int expedition)
        {
            if (AtHome || !HasBag || Expedition != expedition)
                return false;

            var next = current.Copy();
            var unpacked = new List<HomeItem>(current.home);
            foreach (var item in current.world)
            {
                if (item.packed)
                    unpacked.Add(new HomeItem { expedition = Expedition, key = item.key });
            }

            next.home = unpacked.ToArray();
            next.world = Array.Empty<WorldItem>();
            next.held = null;
            next.heldRotated = false;
            next.atHome = true;
            return Publish(next);
        }

        public SaleOffer Quote(HomeItem item) => new(item, catalog[item.key]);

        public bool Sell(SaleOffer offer)
        {
            if (!AtHome || !catalog.TryGetValue(offer.Item.key ?? string.Empty, out var definition) ||
                definition.Price <= 0 || definition.Price != offer.Price || Balance > long.MaxValue - offer.Price)
                return false;

            var index = Array.IndexOf(current.home, offer.Item);
            if (index < 0)
                return false;

            var next = current.Copy();
            var remaining = new List<HomeItem>(current.home);
            remaining.RemoveAt(index);
            next.home = remaining.ToArray();
            next.cash += offer.Price;
            return Publish(next);
        }

        internal bool Depart(int previousExpedition, SearchItem[] items, PlayerPose spawn)
        {
            if (!AtHome || Expedition != previousExpedition || Expedition == int.MaxValue)
                return false;

            var next = current.Copy();
            next.expedition++;
            next.atHome = false;
            next.world = CreateLayout(items, next.expedition);
            next.player = spawn;
            return Publish(next);
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

        private bool Publish(RunSnapshot next)
        {
            next.revision = current.revision + 1;
            next.utcTicks = DateTime.UtcNow.Ticks;
            var saved = slot.TryWrite(next, out var error);
            Error = error;
            if (!saved)
                return false;

            current = next;
            RestoreBag();
            return true;
        }

        private void RestoreBag()
        {
            bag = new BagInventory();
            foreach (var item in current.world)
            {
                if (item.packed)
                    bag.TryPack(item.key, catalog[item.key].Placement(item));
            }
            HeldItemRotated = current.heldRotated;
        }

        private int FindWorldItem(string id) => Array.FindIndex(current.world, item => item.key == id);

        private bool Validate(RunSnapshot data)
        {
            if (data.format != RunSnapshot.CurrentFormat || !Guid.TryParseExact(data.runId, "N", out _) ||
                data.revision <= 0 || data.utcTicks <= 0 || data.utcTicks > DateTime.MaxValue.Ticks ||
                data.expedition < 1 || data.cash < 0 || data.home == null || data.world == null ||
                !Finite(data.player.position) || !float.IsFinite(data.player.yaw) ||
                !float.IsFinite(data.player.pitch) || Mathf.Abs(data.player.pitch) > 82f)
                return false;

            var stored = new HashSet<HomeItem>();
            foreach (var item in data.home)
            {
                if (string.IsNullOrEmpty(item.key) || !catalog.TryGetValue(item.key, out var definition) ||
                    definition.Price <= 0 || item.expedition <= 0 || item.expedition > data.expedition ||
                    (!data.atHome && item.expedition == data.expedition) || !stored.Add(item))
                    return false;
            }

            var hasHand = !string.IsNullOrEmpty(data.held);
            if (data.atHome)
                return data.hasBag && data.world.Length == 0 && !hasHand && !data.heldRotated;
            if ((data.world.Length != layoutKeys.Count && data.world.Length != LegacyLayout.Length) ||
                (!data.hasBag && (hasHand || data.player.inside)) ||
                (!hasHand && data.heldRotated))
                return false;

            var keys = new HashSet<string>();
            var packed = new BagInventory();
            foreach (var item in data.world)
            {
                var r = item.rotation;
                if (string.IsNullOrEmpty(item.key) || !catalog.TryGetValue(item.key, out var definition) ||
                    !keys.Add(item.key) || !Finite(item.position) || !float.IsFinite(Quaternion.Dot(r, r)) ||
                    Mathf.Abs(Quaternion.Dot(r, r) - 1f) > 0.02f)
                    return false;
                if (item.packed && (!data.hasBag || item.key == data.held || definition.Price <= 0 ||
                    !packed.TryPack(item.key, definition.Placement(item))))
                    return false;
            }
            return (keys.SetEquals(layoutKeys) || keys.SetEquals(LegacyLayout)) && (!hasHand || keys.Contains(data.held));
        }

        private static bool Finite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static WorldItem[] CreateLayout(SearchItem[] items, int expedition)
        {
            var layout = new List<WorldItem>(items.Length);
            foreach (var item in items)
                if (!item.LegacyLayoutOnly)
                    layout.Add(item.LayoutPose(LayoutIndex(expedition)));
            return layout.ToArray();
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
