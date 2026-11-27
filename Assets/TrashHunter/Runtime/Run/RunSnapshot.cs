using System;
using UnityEngine;

namespace TrashHunter
{
    [Serializable]
    internal sealed class RunSnapshot
    {
        public const int CurrentFormat = 1;

        public int format;
        public string runId;
        public long revision;
        public long utcTicks;
        public int expedition;
        public int pileSeed;
        public int pileGeneratorVersion;
        public bool atHome;
        public bool hasBag;
        public WorldItem[] world;
        public string held;
        public bool heldRotated;
        public bool heldInWorld;
        public PlayerPose player;
        public HomeItem[] home;
        public long cash;

        public RunSnapshot Copy()
        {
            var copy = (RunSnapshot)MemberwiseClone();
            copy.world = (WorldItem[])world.Clone();
            return copy;
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

    [Serializable]
    public struct HomeItem : IEquatable<HomeItem>
    {
        public int expedition;
        public string key;

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

        internal SaleOffer(HomeItem item, LootDefinition definition)
        {
            Item = item;
            Name = definition.Name;
            Price = definition.Price;
            Condition = definition.Condition;
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
