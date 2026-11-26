using UnityEngine;
using UnityEngine.Serialization;

namespace TrashHunter
{
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class SearchItem : MonoBehaviour
    {
        private const float MaximumPullForce = 450f;
        private const float MaximumThrowImpulse = 9f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color AimTint = new(1f, 0.84f, 0.46f);

        [SerializeField] private string itemId;
        [SerializeField] private string displayName;
        [SerializeField] private string bagLabel;
        [SerializeField] private Vector2Int bagSize;
        [FormerlySerializedAs("salePrice")]
        [SerializeField, Min(0)] private int baseValue;
        [SerializeField, Min(0)] private int conditionDiscount;
        [SerializeField, Min(0)] private int tier;
        [SerializeField] private SearchItem[] packageContents;
        [SerializeField] private GameObject packageClosure;
        [SerializeField] private GameObject packageInterior;
        [SerializeField] private string saleCondition;
        [SerializeField] private Transform alternatePose;
        [SerializeField] private Transform thirdPose;
        [SerializeField] private bool legacyLayoutOnly;
        [SerializeField] private Renderer[] lampShell;
        [SerializeField] private Material secondLayoutFinish;

        private Rigidbody body;
        private BoxCollider shape;
        private WorldItem firstPose;
        private bool showingInspection;
        private Vector3 inspectionPosition;
        private Quaternion inspectionRotation;
        private MeshRenderer[] highlightRenderers;

        public string Id => itemId;
        public string DisplayName => displayName;
        public string BagLabel => bagLabel;
        public Vector3 HalfSize => shape.size * 0.5f;
        public bool IsHeld { get; private set; }
        internal bool HeldInWorld { get; private set; }
        internal Collider Shape => shape;
        internal Vector3 BodyPosition => body.position;
        internal Quaternion BodyRotation => body.rotation;
        internal float MinimumHoldDistance => 0.85f + new Vector2(HalfSize.x, HalfSize.z).magnitude;
        internal LootDefinition Definition => new(displayName, tier, baseValue, conditionDiscount, saleCondition, bagSize);
        internal bool IsPackage => packageContents is { Length: > 0 };
        internal SearchItem[] InitialContents => packageContents;
        internal bool LegacyLayoutOnly => legacyLayoutOnly;

        internal void SetAimHighlight(bool highlighted, MaterialPropertyBlock properties)
        {
            highlightRenderers ??= GetComponentsInChildren<MeshRenderer>(true);
            foreach (var renderer in highlightRenderers)
            {
                if (!renderer.enabled)
                    continue;

                if (!highlighted)
                {
                    renderer.SetPropertyBlock(null);
                    continue;
                }

                var material = renderer.sharedMaterial;
                if (material == null || !material.HasProperty(BaseColor))
                    continue;
                properties.SetColor(BaseColor, Color.Lerp(material.GetColor(BaseColor), AimTint, 0.55f));
                renderer.SetPropertyBlock(properties);
            }
        }

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
            if (!state.TryGetPose(Id, out var pose) || state.IsInPackage(Id))
            {
                Store();
                return;
            }

            if (IsPackage)
                ShowPackageOpened(state.IsPackageOpen(Id));

            if (secondLayoutFinish != null && GameState.LayoutIndex(state.Expedition) == 1)
                foreach (var renderer in lampShell)
                    renderer.sharedMaterial = secondLayoutFinish;

            transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            body.position = pose.Position;
            body.rotation = pose.Rotation;
            if (state.Bag.TryGetPlacement(Id, out _))
                Store();
            else if (state.HeldItemId == Id)
                TakeInHand(state.HeldInWorld);
            else
                Place(pose.Position, pose.Rotation);
        }

        internal void ShowPackageOpened(bool opened)
        {
            packageClosure.SetActive(!opened);
            packageInterior.SetActive(opened);
        }

        public void Record(GameState state)
        {
            if (gameObject.activeSelf && (!IsHeld || HeldInWorld))
                state.RecordPose(Id, showingInspection ? inspectionPosition : body.position,
                    showingInspection ? inspectionRotation : body.rotation);
        }

        internal void TakeInHand(bool inWorld)
        {
            IsHeld = true;
            HeldInWorld = inWorld;
            showingInspection = false;
            if (!inWorld)
                RemoveFromPhysics();
            gameObject.SetActive(true);
        }

        internal void MaterializeHand(Vector3 position, Quaternion rotation)
        {
            SetWorldPose(position, rotation);
            HeldInWorld = true;
        }

        internal void PullTo(Vector3 target)
        {
            var speed = Mathf.Clamp(5f / Mathf.Sqrt(body.mass), 0.75f, 5f);
            var desiredVelocity = Vector3.ClampMagnitude((target - body.worldCenterOfMass) * 7f, speed);
            var force = body.mass * ((desiredVelocity - body.linearVelocity) * 12f - Physics.gravity);
            body.AddForce(Vector3.ClampMagnitude(force, MaximumPullForce), ForceMode.Force);
        }

        internal void BeginInspection()
        {
            if (!HeldInWorld)
                return;
            inspectionPosition = body.position;
            inspectionRotation = body.rotation;
            showingInspection = true;
            RemoveFromPhysics();
        }

        internal void EndInspection()
        {
            if (!showingInspection)
                return;
            showingInspection = false;
            SetWorldPose(inspectionPosition, inspectionRotation);
        }

        public void Store()
        {
            IsHeld = false;
            HeldInWorld = false;
            showingInspection = false;
            RemoveFromPhysics();
            gameObject.SetActive(false);
        }

        internal void FreezeWorld(bool frozen)
        {
            if (gameObject.activeSelf && shape.enabled)
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
            Release();
            SetWorldPose(position, rotation);
        }

        internal void Throw(Vector3 direction)
        {
            Release();
            body.linearVelocity = Vector3.zero;
            body.AddForce(direction * Mathf.Min(MaximumThrowImpulse, body.mass * 8f), ForceMode.Impulse);
        }

        private void Release()
        {
            IsHeld = false;
            HeldInWorld = false;
            showingInspection = false;
        }

        private void SetWorldPose(Vector3 position, Quaternion rotation)
        {
            gameObject.SetActive(true);
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            shape.enabled = true;
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }
    }
}
