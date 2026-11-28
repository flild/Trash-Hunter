using System;
using UnityEngine;

namespace TrashHunter
{
    [Serializable]
    internal sealed class RunSnapshot
    {
        public const int CurrentFormat = 4;

        public int format;
        public string runId;
        public long revision;
        public long utcTicks;
        public int expedition;
        public int pileSeed;
        public int pileGeneratorVersion;
        public RunLocation location;
        public bool hasBag;
        public WorldItem[] world;
        public string held;
        public bool heldRotated;
        public bool heldInWorld;
        public PlayerPose player;
        public HomeItem[] home;
        public long cash;
        public bool hasSold;
        public QuestProgress[] quests = Array.Empty<QuestProgress>();
        public string[] contacts = Array.Empty<string>();

        public RunSnapshot Copy()
        {
            var copy = (RunSnapshot)MemberwiseClone();
            copy.world = (WorldItem[])world.Clone();
            copy.quests = (QuestProgress[])quests.Clone();
            return copy;
        }

        public void SetHomeItem(int index, HomeItem item)
        {
            home = (HomeItem[])home.Clone();
            home[index] = item;
        }

        public void RemoveHomeItem(int index)
        {
            var remaining = new HomeItem[home.Length - 1];
            Array.Copy(home, 0, remaining, 0, index);
            Array.Copy(home, index + 1, remaining, index, home.Length - index - 1);
            home = remaining;
        }
    }

    [Serializable]
    internal struct WorldItem
    {
        public string key;
        public Vector3 position;
        public Quaternion rotation;
        public string package;
        public bool opened;
        public bool packed;
        public int column;
        public int row;
        public bool rotated;
    }

    [Serializable]
    internal struct PlayerPose
    {
        public Vector3 position;
        public float yaw;
        public float pitch;
        public bool inside;
    }

    [Flags]
    internal enum HomeWork { None = 0, Dirty = 1, Diagnosed = 2, Repaired = 4 }

    [Serializable]
    public struct HomeItem : IEquatable<HomeItem>
    {
        public int expedition;
        public string key;
        [SerializeField] internal HomeWork work;

        internal bool IsDirty => (work & HomeWork.Dirty) != 0;
        internal bool IsDiagnosed => (work & HomeWork.Diagnosed) != 0;
        internal bool IsRepaired => (work & HomeWork.Repaired) != 0;

        public bool Equals(HomeItem other) => expedition == other.expedition && key == other.key;
        public override bool Equals(object other) => other is HomeItem item && Equals(item);
        public override int GetHashCode() => HashCode.Combine(expedition, key);
    }

    public readonly struct SaleOffer
    {
        public HomeItem Item { get; }
        public string Name { get; }
        public int Price { get; }
        public string Condition { get; }

        internal SaleOffer(HomeItem item, LootCatalog.Entry entry)
        {
            Item = item;
            Name = entry.name;
            Price = (item.IsRepaired ? entry.baseValue : entry.Definition.Price) -
                (item.IsDirty ? entry.dirtDiscount : 0);
            Condition = item.IsRepaired ? "Крепление заменено. Плафон держится." : entry.condition;
        }
    }

    internal readonly struct LootDefinition
    {
        public string Name { get; }
        public int Tier { get; }
        public int BaseValue { get; }
        public int Price { get; }
        public string Condition { get; }
        public Vector2Int Size { get; }

        public LootDefinition(string name, int tier, int baseValue, int conditionDiscount, string condition, Vector2Int size)
        {
            Name = name;
            Tier = tier;
            BaseValue = baseValue;
            Price = Mathf.Max(0, baseValue - conditionDiscount);
            Condition = condition;
            Size = size;
        }

        public BagPlacement Placement(WorldItem item) => new(item.column, item.row, Size.x, Size.y, item.rotated);
    }
}
