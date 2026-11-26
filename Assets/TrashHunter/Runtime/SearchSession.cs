using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace TrashHunter
{
    public sealed class SearchSession : MonoBehaviour
    {
        private enum Mode { Search, Return, Home, Sale, Pause, Continue, LoadError }

        [SerializeField] private Transform outside;
        [SerializeField] private Transform inside;
        [SerializeField] private BoxCollider containerFloor;
        [SerializeField] private Transform exit;
        [SerializeField] private SearchItem[] items;
        [SerializeField] private Collider starterBag;
        [SerializeField] private float rimReach = 1.15f;
        [SerializeField] private float itemReach = 3f;

        private readonly RaycastHit[] aimHits = new RaycastHit[12];
        private readonly Collider[] placementHits = new Collider[2];
        private GameState state;
        private SearchPlayer player;
        private SearchHud hud;
        private SearchItem held;
        private SearchItem aimedItem;
        private SearchItem highlightedItem;
        private MaterialPropertyBlock aimProperties;
        private SearchItem movingInBag;
        private bool moveRotated;
        private UiStrings strings;
        private Quaternion inspectRotation;
        private Mode mode;
        private bool inspecting;
        private bool bagOpen;
        private bool reloading;
        private bool quitting;
        private float blockedUntil;
        private float holdDistance;
        private int selection;
        private int returnExpedition;
        private SaleOffer offer;
        private string receipt;

        public void Initialize(GameState gameState, SearchPlayer scenePlayer, SearchHud sceneHud)
        {
            state = gameState;
            player = scenePlayer;
            hud = sceneHud;
            aimProperties = new MaterialPropertyBlock();
            strings = new UiStrings();
            hud.Bag.SetStrings(strings);
            foreach (var item in items)
                item.CacheLayout();
            Application.wantsToQuit += SaveBeforeQuit;
            Open();
        }

        private void Open()
        {
            if (!state.Open(items, containerFloor.bounds, inside.position, SearchPlayer.PoseAt(outside), out var resuming))
            {
                ShowLoadError();
                return;
            }

            ApplySnapshot();
            if (!resuming)
            {
                CloseOverlay();
                return;
            }

            mode = Mode.Continue;
            foreach (var item in items)
                item.FreezeWorld(true);
            ShowOverlay("Продолжить", state.AtHome ? "Дома" : $"Вылазка {state.Expedition}", string.Empty,
                "Enter  Продолжить     Esc  Выйти", state.RecoveryNotice);
        }

        private void ApplySnapshot()
        {
            held = null;
            starterBag.gameObject.SetActive(!state.HasBag && !state.AtHome);
            foreach (var item in items)
            {
                item.Restore(state);
                if (item.IsHeld)
                    held = item;
            }
            player.Restore(state.Player);
            if (held != null)
            {
                var view = player.ViewCamera.transform;
                var savedDepth = Vector3.Dot(held.BodyPosition - view.position, view.forward);
                holdDistance = held.HeldInWorld
                    ? Mathf.Clamp(savedDepth, held.MinimumHoldDistance, itemReach)
                    : held.MinimumHoldDistance;
            }
            Physics.SyncTransforms();
            PresentHand();
        }

        private void Update()
        {
            aimedItem = null;
            if (player == null || reloading)
                return;

            var keyboard = Keyboard.current;
            if (mode != Mode.Search)
            {
                UpdateOverlay(keyboard);
                return;
            }

            var mouse = Mouse.current;
            if (keyboard?.f5Key.wasPressedThisFrame == true)
            {
                CaptureWorld();
                if (state.Save())
                    Reload();
                UpdateError();
                return;
            }

            var cancel = keyboard?.escapeKey.wasPressedThisFrame == true;
            var toggleBag = keyboard?.tabKey.wasPressedThisFrame == true;
            if (bagOpen)
            {
                if (toggleBag || (cancel && movingInBag == null))
                    CloseBag();
                else if (cancel)
                    CancelBagMove();
                else
                    UpdateBag(keyboard, mouse);
                UpdateError();
                return;
            }

            if (cancel)
            {
                if (inspecting)
                    EndInspection();
                else
                {
                    Pause();
                    return;
                }
            }
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                hud.Show("ЛКМ  Вернуться к поиску", "Esc  Пауза", false);
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
                hud.Bag.ShowSelection(held, null, null, HandPlacement(-1, -1), BagFit.Fits, false, state.Bag.Count == 0);
                UnlockCursor();
                return;
            }

            Vector2 delta = mouse == null ? Vector2.zero : mouse.delta.ReadValue();
            if (keyboard?.qKey.wasPressedThisFrame == true && held != null)
            {
                if (inspecting)
                    EndInspection();
                else
                {
                    inspecting = true;
                    inspectRotation = Quaternion.identity;
                    held.BeginInspection();
                }
                delta = Vector2.zero;
            }
            if (inspecting)
            {
                inspectRotation = Quaternion.Euler(delta.y * 0.55f, -delta.x * 0.55f, 0f) * inspectRotation;
                hud.Show(ItemName(held), strings.Get("search.inspectControls"), false);
                return;
            }

            player.Move(keyboard, delta);
            var actionPressed = keyboard?.eKey.wasPressedThisFrame == true;
            if (!state.HasBag)
            {
                var canPickUp = AimHit().collider == starterBag;
                if (canPickUp && actionPressed)
                {
                    CaptureWorld();
                    if (state.EquipBag())
                    {
                        starterBag.gameObject.SetActive(false);
                        hud.Show("E  Залезть", strings.Get("search.controlsWithBag"), true);
                        UpdateError();
                        return;
                    }
                }

                hud.Show(strings.Get(canPickUp ? "search.pickUpBag" : "search.findBag"),
                    strings.Get("search.controlsWithoutBag"), true);
                UpdateError();
                return;
            }

            var inContainer = IsInContainer();
            var atExit = !inContainer && Near(exit, 1.1f);
            if (atExit && actionPressed)
            {
                returnExpedition = state.Expedition;
                mode = Mode.Return;
                ShowReturn();
                return;
            }

            var atRim = Near(inContainer ? inside : outside, rimReach);
            if (atRim && actionPressed)
            {
                inContainer = !inContainer;
                player.Place(inContainer ? inside : outside);
            }

            var hit = AimHit();
            var target = held == null && hit.collider != null && !player.IsSupportedBy(hit.collider)
                ? hit.collider.GetComponent<SearchItem>() : null;
            if (held != null && mouse != null)
            {
                var scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f)
                    holdDistance = Mathf.Clamp(holdDistance + Mathf.Sign(scroll) * 0.35f,
                        held.MinimumHoldDistance, itemReach);
            }
            if (mouse?.leftButton.wasPressedThisFrame == true)
            {
                if (held != null)
                    TryPlace(hit);
                else if (target != null)
                    TakeInHand(target);
            }
            else if (held != null && mouse?.rightButton.wasPressedThisFrame == true)
                TryThrow();

            aimedItem = held == null ? target : null;
            var action = held != null ? strings.Get("search.holdActions") :
                target != null ? string.Format(strings.Get("search.take"), ItemName(target)) : string.Empty;
            if (held != null && Time.unscaledTime < blockedUntil)
                action = strings.Get("search.blockedPlacement");
            if (atExit || atRim)
            {
                var interaction = atExit ? "E  Домой" : inContainer ? "E  Вылезти" : "E  Залезть";
                action = action.Length == 0 ? interaction : $"{interaction}     {action}";
            }
            hud.Show(action, strings.Get(held == null ? "search.controlsWithBag" : "search.holdControls"), true);
            UpdateError();
        }

        private void EndInspection()
        {
            if (!inspecting)
                return;
            inspecting = false;
            held.EndInspection();
        }

        private string ItemName(SearchItem item) =>
            strings.Get($"item.{item.Id}.name", strings.Get($"item.{item.Id}", item.DisplayName));

        private void UpdateOverlay(Keyboard keyboard)
        {
            if (keyboard == null)
                return;

            var confirm = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            var cancel = keyboard.escapeKey.wasPressedThisFrame;
            switch (mode)
            {
                case Mode.Continue:
                    if (confirm)
                    {
                        foreach (var item in items)
                            item.FreezeWorld(false);
                        CloseOverlay();
                    }
                    else if (cancel)
                        Application.Quit();
                    break;
                case Mode.LoadError:
                    if (confirm)
                        Open();
                    else if (cancel)
                        Application.Quit();
                    break;
                case Mode.Return:
                    if (cancel)
                        CloseOverlay();
                    else if (confirm)
                    {
                        CaptureWorld();
                        if (state.ReturnHome(returnExpedition))
                        {
                            receipt = null;
                            selection = 0;
                            ApplySnapshot();
                            CloseOverlay();
                        }
                        else
                            ShowReturn();
                    }
                    break;
                case Mode.Home:
                    if (cancel)
                        Pause();
                    else if (keyboard.eKey.wasPressedThisFrame)
                    {
                        if (state.Depart(state.Expedition, SearchPlayer.PoseAt(outside)))
                            Reload();
                        else
                            ShowHome();
                    }
                    else if (state.Home.Count > 0 && (confirm || keyboard.upArrowKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame))
                    {
                        if (keyboard.upArrowKey.wasPressedThisFrame)
                            selection = Mathf.Max(0, selection - 1);
                        else if (keyboard.downArrowKey.wasPressedThisFrame)
                            selection = Mathf.Min(state.Home.Count - 1, selection + 1);
                        else if (confirm)
                        {
                            offer = state.Quote(state.Home[selection]);
                            mode = Mode.Sale;
                            ShowSale();
                            break;
                        }
                        ShowHome();
                    }
                    break;
                case Mode.Sale:
                    if (cancel)
                        CloseOverlay();
                    else if (confirm)
                    {
                        if (state.Sell(offer))
                        {
                            receipt = $"{offer.Name}  +{offer.Price} ₽\n{offer.Condition}";
                            CloseOverlay();
                        }
                        else
                            ShowSale();
                    }
                    break;
                case Mode.Pause:
                    if (cancel || confirm)
                        CloseOverlay();
                    else if (keyboard.qKey.wasPressedThisFrame && SaveBeforeQuit())
                        Application.Quit();
                    break;
            }
        }

        private void ShowReturn()
        {
            var warning = held == null ? "Остальные вещи будут потеряны." :
                "Вещь в руке будет потеряна вместе с оставленными находками.";
            ShowOverlay("Уйти домой?", $"В сумке  {state.Bag.Count}", $"Домой отправятся только вещи из сумки.\n<color=#FFAA95>{warning}</color>",
                "Enter  Уйти домой     Esc  Продолжить поиск", state.Error, true);
        }

        private void ShowHome()
        {
            mode = Mode.Home;
            hud.ShowError(null);
            selection = Mathf.Clamp(selection, 0, Mathf.Max(0, state.Home.Count - 1));
            hud.Show(string.Empty, string.Empty, false);
            hud.Expedition.ShowHome(state, selection, receipt);
            UnlockCursor();
        }

        private void ShowSale() => ShowOverlay("Продать вещь?", $"{offer.Price} ₽",
            $"{offer.Name}\n{offer.Condition}", "Enter  Продать     Esc  Отмена", state.Error, state.Error != null);

        private void ShowLoadError()
        {
            mode = Mode.LoadError;
            ShowOverlay("Автосохранение недоступно", string.Empty, state.Error,
                "Enter  Повторить чтение     Esc  Выйти", null, true);
        }

        private void Pause()
        {
            if (bagOpen)
                CloseBag();
            EndInspection();
            mode = Mode.Pause;
            ShowOverlay("Пауза", string.Empty,
                state.AtHome ? "Игра продолжится дома." : "При выходе сохранится эта вылазка.",
                "Enter / Esc  Продолжить     Q  Выйти из игры", state.Error, state.Error != null);
        }

        private void ShowOverlay(string title, string summary, string details, string keys, string message, bool warning = false)
        {
            hud.Show(string.Empty, string.Empty, false);
            hud.ShowError(null);
            hud.Expedition.Show(title, summary, details, keys, message, warning);
            UnlockCursor();
        }

        private void CloseOverlay()
        {
            if (state.AtHome)
                ShowHome();
            else
            {
                mode = Mode.Search;
                hud.Expedition.gameObject.SetActive(false);
                LockCursor();
            }
        }

        private void CloseBag()
        {
            bagOpen = false;
            movingInBag = null;
            hud.Bag.gameObject.SetActive(false);
            LockCursor();
        }

        private void CancelBagMove()
        {
            movingInBag = null;
            hud.Bag.ShowContents(state.Bag, items);
        }

        private void UpdateError() => hud.ShowError(state.Error);

        private bool SaveBeforeQuit()
        {
            if (quitting || mode == Mode.LoadError || mode == Mode.Continue || reloading)
                return true;

            CaptureWorld();
            if (!state.Save())
            {
                Pause();
                return false;
            }

            quitting = true;
            return true;
        }

        private void UpdateBag(Keyboard keyboard, Mouse mouse)
        {
            var cell = mouse == null ? new Vector2Int(-1, -1) : hud.Bag.CellAt(mouse.position.ReadValue());
            var overGrid = cell.x >= 0 && cell.y >= 0;
            var hoveredPlacement = default(BagPlacement);
            SearchItem hovered = null;
            if (overGrid)
            {
                foreach (var item in items)
                {
                    if (state.Bag.TryGetPlacement(item.Id, out var packed) && packed.Contains(cell.x, cell.y))
                    {
                        hovered = item;
                        hoveredPlacement = packed;
                        break;
                    }
                }
            }

            var rightClick = mouse?.rightButton.wasPressedThisFrame == true;
            if (rightClick)
            {
                if (movingInBag != null)
                    CancelBagMove();
                else if (hovered != null)
                {
                    movingInBag = hovered;
                    moveRotated = hoveredPlacement.Rotated;
                    hud.Bag.ShowContents(state.Bag, items, movingInBag);
                }
            }

            if (keyboard?.rKey.wasPressedThisFrame == true)
            {
                if (movingInBag != null)
                    moveRotated = !moveRotated;
                else if (held != null)
                    state.HeldItemRotated = !state.HeldItemRotated;
            }

            var placement = hoveredPlacement;
            var fit = BagFit.Fits;
            if (movingInBag != null)
            {
                placement = movingInBag.Placement(cell.x, cell.y, moveRotated);
                fit = state.Bag.Check(placement, movingInBag.Id);
            }
            else if (held != null)
            {
                placement = HandPlacement(cell.x, cell.y);
                fit = state.Bag.Check(placement);
            }

            if (overGrid && !rightClick && fit == BagFit.Fits && mouse?.leftButton.wasPressedThisFrame == true)
            {
                if (movingInBag != null)
                {
                    CaptureWorld();
                    if (state.MovePacked(movingInBag.Id, placement))
                    {
                        CancelBagMove();
                        return;
                    }
                }
                else if (held != null)
                {
                    CaptureWorld();
                    if (state.Pack(placement))
                    {
                        held.Store();
                        ClearHand();
                        hud.Bag.ShowContents(state.Bag, items);
                        return;
                    }
                }
                else if (hovered != null && TakeInHand(hovered))
                {
                    hud.Bag.ShowContents(state.Bag, items);
                    return;
                }
            }

            hud.Bag.ShowSelection(held, hovered, movingInBag, placement, fit, overGrid, state.Bag.Count == 0);
        }

        private BagPlacement HandPlacement(int column, int row) =>
            held == null ? default : held.Placement(column, row, state.HeldItemRotated);

        private bool TakeInHand(SearchItem item)
        {
            CaptureWorld();
            if (!state.Take(item.Id))
                return false;
            item.TakeInHand(state.HeldInWorld);
            held = item;
            holdDistance = item.MinimumHoldDistance;
            blockedUntil = 0f;
            return true;
        }

        private void ClearHand()
        {
            held = null;
            blockedUntil = 0f;
        }

        private RaycastHit AimHit()
        {
            var view = player.ViewCamera.transform;
            Physics.Raycast(view.position, view.forward, out var hit, itemReach,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (held == null || hit.collider != held.Shape)
                return hit;

            var hits = aimHits;
            var count = Physics.RaycastNonAlloc(view.position, view.forward, hits, itemReach,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == hits.Length)
            {
                hits = Physics.RaycastAll(view.position, view.forward, itemReach,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }

            var nearest = default(RaycastHit);
            var distance = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                if (hits[i].collider == held.Shape || hits[i].distance >= distance)
                    continue;
                nearest = hits[i];
                distance = nearest.distance;
            }
            return nearest;
        }

        private void CaptureWorld()
        {
            if (state.AtHome)
                return;
            state.RecordPlayer(player.Capture(IsInContainer()));
            state.RecordHeldInWorld(held != null && held.HeldInWorld);
            foreach (var item in items)
                item.Record(state);
        }

        private void FixedUpdate()
        {
            if (held == null || mode != Mode.Search || inspecting || bagOpen || reloading ||
                Cursor.lockState != CursorLockMode.Locked)
                return;

            var view = player.ViewCamera.transform;
            var target = HoldTarget(view);
            if (TryMaterializeHand(view, target) && !player.IsSupportedBy(held.Shape))
                held.PullTo(target);
        }

        private Vector3 HoldTarget(Transform view) => view.TransformPoint(new Vector3(0.24f, -0.12f, holdDistance));

        private bool TryMaterializeHand(Transform view, Vector3 position)
        {
            if (held.HeldInWorld)
                return true;

            // A bag extraction or older save has no physical hand pose yet.
            var offset = position - view.position;
            if (Physics.Raycast(view.position, offset.normalized, offset.magnitude, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) || !HasRoom(position, view.rotation))
                return false;

            held.MaterializeHand(position, view.rotation);
            return true;
        }

        private void LateUpdate()
        {
            SetAimHighlight(aimedItem);
            PresentHand();
        }

        private void SetAimHighlight(SearchItem item)
        {
            if (highlightedItem == item)
                return;

            if (highlightedItem != null)
                highlightedItem.SetAimHighlight(false, aimProperties);
            highlightedItem = item;
            if (highlightedItem != null)
                highlightedItem.SetAimHighlight(true, aimProperties);
        }

        private void OnDisable() => SetAimHighlight(null);

        private void PresentHand()
        {
            if (held == null || reloading || (held.HeldInWorld && !inspecting))
                return;

            var view = player.ViewCamera.transform;
            var offset = inspecting ? new Vector3(0f, -0.035f, Mathf.Max(0.88f, 2f * held.HalfSize.magnitude)) :
                new Vector3(0.37f, -0.27f, 0.82f);
            held.Present(view.TransformPoint(offset), view.rotation * (inspecting ? inspectRotation : Quaternion.identity));
        }

        private bool Near(Transform marker, float reach) =>
            Vector3.ProjectOnPlane(player.transform.position - marker.position, Vector3.up).sqrMagnitude <= reach * reach;

        private bool IsInContainer()
        {
            var bounds = containerFloor.bounds;
            var feet = player.transform.position;
            feet.y = bounds.center.y;
            return bounds.Contains(feet);
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
                    HasClearance(position, (IsInContainer() ? inside : outside).position, clearance) &&
                    HasRoom(position, rotation))
                {
                    CaptureWorld();
                    if (state.Place(position, rotation))
                    {
                        held.Place(position, rotation);
                        ClearHand();
                    }
                    return;
                }
            }
            blockedUntil = Time.unscaledTime + 1.25f;
        }

        private bool HasRoom(Vector3 position, Quaternion rotation)
        {
            var count = Physics.OverlapBoxNonAlloc(position, held.HalfSize, placementHits, rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return count == 0 || (count == 1 && placementHits[0] == held.Shape);
        }

        private void TryThrow()
        {
            var view = player.ViewCamera.transform;
            if (!TryMaterializeHand(view, HoldTarget(view)))
            {
                blockedUntil = Time.unscaledTime + 1.25f;
                return;
            }

            CaptureWorld();
            if (state.Place(held.BodyPosition, held.BodyRotation))
            {
                held.Throw(view.forward);
                ClearHand();
            }
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
            Application.wantsToQuit -= SaveBeforeQuit;
            if (state != null)
                UnlockCursor();
        }
    }
}
