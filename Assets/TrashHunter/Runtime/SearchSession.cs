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
        [SerializeField] private Collider starterBag;
        [SerializeField] private float rimReach = 1.15f;
        [SerializeField] private float itemReach = 3f;

        private GameState state;
        private SearchPlayer player;
        private SearchHud hud;
        private SearchItem held;
        private Quaternion inspectRotation;
        private bool inContainer;
        private bool inspecting;
        private bool bagOpen;
        private bool reloading;
        private float blockedUntil;

        public void Initialize(GameState gameState, SearchPlayer scenePlayer, SearchHud sceneHud)
        {
            state = gameState;
            player = scenePlayer;
            hud = sceneHud;
            starterBag.gameObject.SetActive(!state.HasBag);
            foreach (var item in items)
            {
                item.Restore(state);
                if (item.IsHeld)
                    held = item;
            }
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

            var cancel = keyboard?.escapeKey.wasPressedThisFrame == true;
            var toggleBag = keyboard?.tabKey.wasPressedThisFrame == true;
            if (bagOpen)
            {
                if (cancel || toggleBag)
                {
                    bagOpen = false;
                    hud.Bag.gameObject.SetActive(false);
                    LockCursor();
                }
                else
                    UpdateBag(keyboard, mouse);
                return;
            }

            if (cancel)
            {
                if (inspecting)
                    inspecting = false;
                else
                    UnlockCursor();
            }
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                hud.Show("ЛКМ  Вернуться к поиску", string.Empty, false);
                if (mouse?.leftButton.wasPressedThisFrame == true)
                    LockCursor();
                return;
            }

            if (toggleBag && state.HasBag && !inspecting)
            {
                bagOpen = true;
                hud.Show(string.Empty, string.Empty, false);
                hud.Bag.gameObject.SetActive(true);
                hud.Bag.ShowContents(state.Bag, items);
                hud.Bag.ShowSelection(held, null, HandPlacement(-1, -1), BagFit.Fits, false, state.Bag.Count == 0);
                UnlockCursor();
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
            var actionPressed = keyboard?.eKey.wasPressedThisFrame == true;
            if (!state.HasBag)
            {
                var canPickUp = AimHit().collider == starterBag;
                if (canPickUp && actionPressed)
                {
                    state.HasBag = true;
                    starterBag.gameObject.SetActive(false);
                    hud.Show("E  Залезть", "WASD  Движение     Tab  Сумка", true);
                    return;
                }

                hud.Show(canPickUp ? "E  Подобрать поясную сумку" : "Подберите поясную сумку",
                    "WASD  Движение", true);
                return;
            }

            var atRim = AtRim();
            if (atRim && actionPressed)
            {
                inContainer = !inContainer;
                player.Place(inContainer ? inside : outside);
            }

            var hit = AimHit();
            var target = hit.collider != null ? hit.collider.GetComponent<SearchItem>() : null;
            if (mouse?.leftButton.wasPressedThisFrame == true)
            {
                if (held != null)
                    TryPlace(hit);
                else if (target != null)
                {
                    target.Record(state);
                    TakeInHand(target, false);
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
            hud.Show(action, "WASD  Движение     Tab  Сумка     F5  Перезагрузить сцену", true);
        }

        private void UpdateBag(Keyboard keyboard, Mouse mouse)
        {
            if (held != null && keyboard?.rKey.wasPressedThisFrame == true)
                state.HeldItemRotated = !state.HeldItemRotated;

            var cell = mouse == null ? new Vector2Int(-1, -1) : hud.Bag.CellAt(mouse.position.ReadValue());
            var overGrid = cell.x >= 0 && cell.y >= 0;
            var placement = HandPlacement(cell.x, cell.y);
            var fit = held != null ? state.Bag.Check(placement) : BagFit.Fits;
            SearchItem hovered = null;

            if (overGrid && held == null)
            {
                foreach (var item in items)
                {
                    if (state.Bag.TryGetPlacement(item.Id, out var packed) && packed.Contains(cell.x, cell.y))
                    {
                        hovered = item;
                        placement = packed;
                        break;
                    }
                }
            }

            if (overGrid && mouse?.leftButton.wasPressedThisFrame == true)
            {
                if (held != null && state.Bag.TryPack(held.Id, placement))
                {
                    held.Store();
                    ClearHand();
                    hud.Bag.ShowContents(state.Bag, items);
                    return;
                }

                if (hovered != null)
                {
                    state.Bag.Remove(hovered.Id);
                    TakeInHand(hovered, placement.Rotated);
                    hud.Bag.ShowContents(state.Bag, items);
                    return;
                }
            }

            hud.Bag.ShowSelection(held, hovered, placement, fit, overGrid, state.Bag.Count == 0);
        }

        private BagPlacement HandPlacement(int column, int row) =>
            held == null ? default : held.Placement(column, row, state.HeldItemRotated);

        private void TakeInHand(SearchItem item, bool rotated)
        {
            item.TakeInHand();
            held = item;
            state.HeldItemId = item.Id;
            state.HeldItemRotated = rotated;
            blockedUntil = 0f;
        }

        private void ClearHand()
        {
            held = null;
            state.HeldItemId = null;
            state.HeldItemRotated = false;
            blockedUntil = 0f;
        }

        private RaycastHit AimHit()
        {
            var view = player.ViewCamera.transform;
            Physics.Raycast(view.position, view.forward, out var hit, itemReach,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return hit;
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
                    ClearHand();
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

        private static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDestroy()
        {
            if (state != null)
                UnlockCursor();
        }
    }
}
