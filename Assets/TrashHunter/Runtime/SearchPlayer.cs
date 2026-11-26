using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TrashHunter
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class SearchPlayer : MonoBehaviour
    {
        private const float JumpHeight = 0.95f;
        private const float GroundSpeed = -2f;

        [SerializeField] private Camera viewCamera;
        [SerializeField] private float moveSpeed = 3.2f;
        [SerializeField] private float mouseSensitivity = 0.11f;

        private readonly List<Collider> supports = new();
        private CharacterController controller;
        private float pitch;
        private float verticalSpeed;

        public Camera ViewCamera => viewCamera;

        private void Awake() => controller = GetComponent<CharacterController>();

        public void Place(Transform marker) => Restore(PoseAt(marker));

        internal static PlayerPose PoseAt(Transform marker) => new()
        {
            position = marker.position,
            yaw = marker.eulerAngles.y,
            pitch = Mathf.DeltaAngle(0f, marker.eulerAngles.x)
        };

        internal PlayerPose Capture(bool inside) => new()
        {
            position = transform.position,
            yaw = transform.eulerAngles.y,
            pitch = pitch,
            inside = inside
        };

        internal void Restore(PlayerPose pose)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(pose.position, Quaternion.Euler(0f, pose.yaw, 0f));
            controller.enabled = true;
            supports.Clear();
            verticalSpeed = 0f;
            pitch = pose.pitch;
            viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        public void Move(Keyboard keys, Vector2 mouseDelta)
        {
            transform.Rotate(Vector3.up, mouseDelta.x * mouseSensitivity);
            pitch = Mathf.Clamp(pitch - mouseDelta.y * mouseSensitivity, -82f, 82f);
            viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            var input = Vector2.zero;
            if (keys != null)
            {
                input.x = Axis(keys.dKey.isPressed, keys.aKey.isPressed);
                input.y = Axis(keys.wKey.isPressed, keys.sKey.isPressed);
            }
            input = Vector2.ClampMagnitude(input, 1f);
            var grounded = controller.isGrounded && verticalSpeed <= 0f;
            if (grounded && keys?.spaceKey.wasPressedThisFrame == true)
                verticalSpeed = Mathf.Sqrt(-2f * Physics.gravity.y * JumpHeight);
            else
                verticalSpeed = grounded ? GroundSpeed : verticalSpeed + Physics.gravity.y * Time.deltaTime;

            var velocity = (transform.right * input.x + transform.forward * input.y) * moveSpeed;
            velocity.y = verticalSpeed;
            supports.Clear();
            var contacts = controller.Move(velocity * Time.deltaTime);
            if ((contacts & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
                verticalSpeed = 0f;
        }

        internal bool IsSupportedBy(Collider shape) => controller.isGrounded && supports.Contains(shape);

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.rigidbody != null && hit.moveDirection.y < 0f &&
                hit.normal.y >= Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad))
                supports.Add(hit.collider);
        }

        private static float Axis(bool positive, bool negative) => (positive ? 1 : 0) - (negative ? 1 : 0);
    }
}
