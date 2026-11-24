using UnityEngine;
using UnityEngine.InputSystem;

namespace TrashHunter
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class SearchPlayer : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private float moveSpeed = 3.2f;
        [SerializeField] private float mouseSensitivity = 0.11f;

        private CharacterController controller;
        private float pitch;
        private float verticalSpeed;

        public Camera ViewCamera => viewCamera;

        private void Awake() => controller = GetComponent<CharacterController>();

        public void Place(Transform marker)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(marker.position, Quaternion.Euler(0f, marker.eulerAngles.y, 0f));
            controller.enabled = true;
            verticalSpeed = 0f;
            pitch = Mathf.DeltaAngle(0f, marker.eulerAngles.x);
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
            verticalSpeed = controller.isGrounded ? -2f : verticalSpeed + Physics.gravity.y * Time.deltaTime;
            var velocity = (transform.right * input.x + transform.forward * input.y) * moveSpeed;
            velocity.y = verticalSpeed;
            controller.Move(velocity * Time.deltaTime);
        }

        private static float Axis(bool positive, bool negative) => (positive ? 1 : 0) - (negative ? 1 : 0);
    }
}
