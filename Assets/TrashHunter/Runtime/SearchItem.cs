using UnityEngine;

namespace TrashHunter
{
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class SearchItem : MonoBehaviour
    {
        [SerializeField] private string itemId;
        [SerializeField] private string displayName;
        [SerializeField] private string bagLabel;
        [SerializeField] private Vector2Int bagSize;
        [SerializeField] private int salePrice;
        [SerializeField] private string saleCondition;
        [SerializeField] private Transform alternatePose;
        [SerializeField] private Transform thirdPose;
        [SerializeField] private bool legacyLayoutOnly;
        [SerializeField] private Renderer[] lampShell;
        [SerializeField] private Material secondLayoutFinish;

        private Rigidbody body;
        private BoxCollider shape;
        private WorldItem firstPose;

        public string Id => itemId;
        public string DisplayName => displayName;
        public string BagLabel => bagLabel;
        public Vector3 HalfSize => shape.size * 0.5f;
        public bool IsHeld { get; private set; }
        internal LootDefinition Definition => new(displayName, salePrice, saleCondition, bagSize);
        internal bool LegacyLayoutOnly => legacyLayoutOnly;

        public BagPlacement Placement(int column, int row, bool rotated) =>
            new(column, row, bagSize.x, bagSize.y, rotated);

        internal void CacheLayout() =>
            firstPose = new WorldItem { key = Id, position = transform.position, rotation = transform.rotation };

        internal WorldItem LayoutPose(int layout) => layout switch
        {
            1 => PoseAt(alternatePose),
            2 => PoseAt(thirdPose),
            _ => firstPose
        };

        private WorldItem PoseAt(Transform marker) =>
            new() { key = Id, position = marker.position, rotation = marker.rotation };

        public void Restore(GameState state)
        {
            body = GetComponent<Rigidbody>();
            shape = GetComponent<BoxCollider>();
            if (!state.TryGetPose(Id, out var pose))
            {
                Store();
                return;
            }

            if (secondLayoutFinish != null && GameState.LayoutIndex(state.Expedition) == 1)
                foreach (var renderer in lampShell)
                    renderer.sharedMaterial = secondLayoutFinish;

            transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            body.position = pose.Position;
            body.rotation = pose.Rotation;
            if (state.Bag.TryGetPlacement(Id, out _))
                Store();
            else if (state.HeldItemId == Id)
                TakeInHand();
            else
                Place(pose.Position, pose.Rotation);
        }

        public void Record(GameState state)
        {
            if (!IsHeld && gameObject.activeSelf)
                state.RecordPose(Id, body.position, body.rotation);
        }

        public void TakeInHand()
        {
            IsHeld = true;
            RemoveFromPhysics();
            gameObject.SetActive(true);
        }

        public void Store()
        {
            IsHeld = false;
            RemoveFromPhysics();
            gameObject.SetActive(false);
        }

        internal void FreezeWorld(bool frozen)
        {
            if (!IsHeld && gameObject.activeSelf)
                body.isKinematic = frozen;
        }

        private void RemoveFromPhysics()
        {
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
            shape.enabled = false;
        }

        public void Present(Vector3 position, Quaternion rotation) => transform.SetPositionAndRotation(position, rotation);

        public void Place(Vector3 position, Quaternion rotation)
        {
            IsHeld = false;
            gameObject.SetActive(true);
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            shape.enabled = true;
            body.isKinematic = false;
            body.WakeUp();
        }
    }
}
