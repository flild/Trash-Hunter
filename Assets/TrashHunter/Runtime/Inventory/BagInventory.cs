using System.Collections.Generic;

namespace TrashHunter
{
    public enum BagFit
    {
        Fits,
        Outside,
        Occupied
    }

    public readonly struct BagPlacement
    {
        public int Column { get; }
        public int Row { get; }
        public int Width { get; }
        public int Height { get; }
        public bool Rotated { get; }

        public BagPlacement(int column, int row, int width, int height, bool rotated)
        {
            Column = column;
            Row = row;
            Width = rotated ? height : width;
            Height = rotated ? width : height;
            Rotated = rotated;
        }

        public bool Contains(int column, int row) =>
            column >= Column && column < Column + Width && row >= Row && row < Row + Height;

        public bool Overlaps(BagPlacement other) =>
            Column < other.Column + other.Width && Column + Width > other.Column &&
            Row < other.Row + other.Height && Row + Height > other.Row;
    }

    public sealed class BagInventory
    {
        public const int Columns = 3;
        public const int Rows = 2;
        public const int BackpackRows = 3;

        private readonly Dictionary<string, BagPlacement> items = new();

        public BagInventory(bool backpack = false) => Height = backpack ? BackpackRows : Rows;

        public int Height { get; }
        public int Count => items.Count;
        public bool TryGetPlacement(string id, out BagPlacement placement) => items.TryGetValue(id, out placement);

        public BagFit Check(BagPlacement placement, string ignoredId = null)
        {
            if (placement.Width <= 0 || placement.Height <= 0 ||
                placement.Column < 0 || placement.Row < 0 ||
                placement.Column > Columns - placement.Width || placement.Row > Height - placement.Height)
                return BagFit.Outside;

            foreach (var item in items)
            {
                if (item.Key != ignoredId && placement.Overlaps(item.Value))
                    return BagFit.Occupied;
            }

            return BagFit.Fits;
        }

        public bool TryPack(string id, BagPlacement placement) =>
            Check(placement) == BagFit.Fits && items.TryAdd(id, placement);

        public void Remove(string id) => items.Remove(id);
    }
}
