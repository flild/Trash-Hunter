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

        private Rigidbody body;
        private BoxCollider shape;

        public string Id => itemId;
        public string DisplayName => displayName;
        public string BagLabel => bagLabel;
        public Vector3 HalfSize => shape.size * 0.5f;
        public bool IsHeld { get; private set; }

        public BagPlacement Placement(int column, int row, bool rotated) =>
            new(column, row, bagSize.x, bagSize.y, rotated);

        public void Restore(GameState state)
        {
            body = GetComponent<Rigidbody>();
            shape = GetComponent<BoxCollider>();
            if (state.TryGetPose(Id, out var pose))
            {
                body.position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
                body.rotation = new Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W);
            }

            if (state.Bag.TryGetPlacement(Id, out _))
                Store();
            else if (state.HeldItemId == Id)
                TakeInHand();
        }

        public void Record(GameState state)
        {
            if (IsHeld || !gameObject.activeSelf)
                return;
            var p = body.position;
            var r = body.rotation;
            state.RecordPose(Id, new ItemPose(new(p.x, p.y, p.z), new(r.x, r.y, r.z, r.w)));
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
            transform.SetPositionAndRotation(position, rotation);
            IsHeld = false;
            shape.enabled = true;
            body.isKinematic = false;
            body.WakeUp();
        }
    }
}
