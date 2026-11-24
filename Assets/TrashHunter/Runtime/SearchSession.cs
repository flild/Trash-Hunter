using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace TrashHunter
{
    public sealed class SearchSession : MonoBehaviour
    {
        [SerializeField] private Transform outside;
        [SerializeField] private Transform inside;
        [SerializeField] private SearchItem[] items;
        [SerializeField] private float rimReach = 1.15f;
        [SerializeField] private float itemReach = 3f;

        private GameState state;
        private SearchPlayer player;
        private SearchHud hud;
        private SearchItem held;
        private Quaternion inspectRotation;
        private bool inContainer;
        private bool inspecting;
        private bool reloading;
        private float blockedUntil;

        public void Initialize(GameState gameState, SearchPlayer scenePlayer, SearchHud sceneHud)
        {
            state = gameState;
            player = scenePlayer;
            hud = sceneHud;
            foreach (var item in items)
                item.Restore(state);
            player.Place(outside);
            LockCursor();
        }

        private void Update()
        {
            if (player == null || reloading)
                return;

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard?.f5Key.wasPressedThisFrame == true)
            {
                Reload();
                return;
            }

            if (keyboard?.escapeKey.wasPressedThisFrame == true)
            {
                if (inspecting)
                    inspecting = false;
                else
                    Cursor.lockState = CursorLockMode.None;
            }
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                hud.Show("ЛКМ  Вернуться к поиску", string.Empty, false);
                if (mouse?.leftButton.wasPressedThisFrame == true)
                    LockCursor();
                return;
            }

            Vector2 delta = mouse == null ? Vector2.zero : mouse.delta.ReadValue();
            if (keyboard?.qKey.wasPressedThisFrame == true && held != null)
            {
                inspecting = !inspecting;
                inspectRotation = Quaternion.identity;
                delta = Vector2.zero;
            }
            if (inspecting)
            {
                inspectRotation = Quaternion.Euler(delta.y * 0.55f, -delta.x * 0.55f, 0f) * inspectRotation;
                hud.Show(held.DisplayName, "Мышь  Повернуть     Q / Esc  Закончить осмотр", false);
                return;
            }

            player.Move(keyboard, delta);
            var atRim = AtRim();
            if (atRim && keyboard?.eKey.wasPressedThisFrame == true)
            {
                inContainer = !inContainer;
                player.Place(inContainer ? inside : outside);
                atRim = true;
            }

            var ray = new Ray(player.ViewCamera.transform.position, player.ViewCamera.transform.forward);
            SearchItem target = null;
            if (Physics.Raycast(ray, out var hit, itemReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                target = hit.collider.GetComponent<SearchItem>();

            if (mouse?.leftButton.wasPressedThisFrame == true)
            {
                if (held != null)
                    TryPlace(hit);
                else if (target != null)
                {
                    held = target;
                    held.PickUp(state);
                }
            }

            var action = held != null ? "ЛКМ  Положить     Q  Осмотреть" :
                target != null ? $"ЛКМ  Взять  ·  {target.DisplayName}" : string.Empty;
            if (held != null && Time.unscaledTime < blockedUntil)
                action = "Наведите на свободную поверхность";
            if (atRim)
            {
                var rimAction = inContainer ? "E  Вылезти" : "E  Залезть";
                action = action.Length == 0 ? rimAction : $"{rimAction}     {action}";
            }
            hud.Show(action, "WASD  Движение     F5  Перезагрузить сцену", true);
        }

        private void LateUpdate()
        {
            if (state == null || reloading)
                return;
            foreach (var item in items)
                item.Record(state);
            if (held == null)
                return;

            var view = player.ViewCamera.transform;
            var offset = inspecting ? new Vector3(0f, -0.035f, 0.88f) : new Vector3(0.37f, -0.27f, 0.82f);
            held.Present(view.TransformPoint(offset), view.rotation * (inspecting ? inspectRotation : Quaternion.identity));
        }

        private bool AtRim()
        {
            var offset = player.transform.position - (inContainer ? inside.position : outside.position);
            offset.y = 0f;
            return offset.sqrMagnitude <= rimReach * rimReach;
        }

        private void TryPlace(RaycastHit hit)
        {
            if (hit.collider != null && hit.normal.y > 0.85f)
            {
                var rotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
                var halfSize = held.HalfSize;
                var position = hit.point + Vector3.up * (halfSize.y + 0.045f);
                var clearance = 0.36f + new Vector2(halfSize.x, halfSize.z).magnitude;
                if (HasClearance(position, player.transform.position, clearance) &&
                    HasClearance(position, (inContainer ? inside : outside).position, clearance) &&
                    !Physics.CheckBox(position, halfSize, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    held.Place(position, rotation);
                    held.Record(state);
                    held = null;
                    blockedUntil = 0f;
                    return;
                }
            }
            blockedUntil = Time.unscaledTime + 1.25f;
        }

        private static bool HasClearance(Vector3 position, Vector3 reserved, float clearance)
        {
            var offset = Vector3.ProjectOnPlane(position - reserved, Vector3.up);
            return offset.sqrMagnitude > clearance * clearance;
        }

        private void Reload()
        {
            var scenePath = gameObject.scene.path;
            if (SceneUtility.GetBuildIndexByScenePath(scenePath) < 0)
                throw new InvalidOperationException($"Add {scenePath} to the build scene list.");

            reloading = true;
            SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
        }

        private static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDestroy()
        {
            if (state != null)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }
}
