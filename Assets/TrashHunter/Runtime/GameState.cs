using System.Collections.Generic;
using System.Numerics;

namespace TrashHunter
{
    public readonly struct ItemPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public ItemPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    public sealed class GameState
    {
        private readonly Dictionary<string, ItemPose> itemPoses = new();

        public bool TryGetPose(string id, out ItemPose pose) => itemPoses.TryGetValue(id, out pose);

        public void RecordPose(string id, ItemPose pose) => itemPoses[id] = pose;
    }
}
