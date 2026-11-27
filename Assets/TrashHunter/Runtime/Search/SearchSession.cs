using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TrashHunter
{
    public sealed class SearchSession : MonoBehaviour
    {
        private enum Mode { Search, Return, Pause, Continue, LoadError, Package }

        [SerializeField] private Transform outside;
        [SerializeField] private Transform inside;
        [SerializeField] private BoxCollider containerFloor;
        [SerializeField] private Transform exit;
        [SerializeField] private SearchItem[] items = Array.Empty<SearchItem>();
        [SerializeField] private Collider starterBag;
        [SerializeField] private float rimReach = 1.15f;
        [SerializeField] private float itemReach = 3f;

        private readonly RaycastHit[] aimHits = new RaycastHit[12];
        private readonly Collider[] placementHits = new Collider[2];
        private GameState state;
        private SearchPile pile;
        private SearchPlayer player;
        private SearchHud hud;
        private SearchItem held;
        private SearchItem aimedItem;
        private SearchItem highlightedItem;
        private MaterialPropertyBlock aimProperties;
        private SearchItem movingInBag;
        private SearchItem browsingPackage;
        private readonly List<SearchItem> remainingContents = new();
        private readonly Dictionary<SearchItem, (string Action, string CheckContents)> aimLabels = new();
        private int packageSelection;
        private bool knowsLootTiers;
        private bool moveRotated;
        private UiStrings strings;
        private Quaternion inspectRotation;
        private Mode mode;
        private bool inspecting;
        private bool bagOpen;
        private SceneCoordinator scenes;
        private PlayerPose entryPose;
        private bool searchPrepared;
        private bool quitting;
        private float blockedUntil;
        private float holdDistance;
        private int returnExpedition;

        internal Transform SearchEntry => outside;

        internal bool HasSearchSetup =>
            outside != null && inside != null && containerFloor != null && exit != null &&
            starterBag != null && items.Length > 0 && items.All(item => item != null);

        internal void StageSearch()
        {
            if (!HasSearchSetup)
                return;

            foreach (var item in items)
            {
                item.CacheLayout();
                item.gameObject.SetActive(false);
            }
        }

        internal SearchPile PreparePile(LootCatalog catalog)
        {
            BindSearchItems(catalog);
            Physics.SyncTransforms();
            return Pile();
        }

        internal void Initialize(GameState gameState, LootCatalog catalog, SceneCoordinator coordinator,
            PlayerPose spawn, SearchPlayer scenePlayer, SearchHud sceneHud)
        {
            state = gameState;
            scenes = coordinator;
            entryPose = spawn;
            player = scenePlayer;
            hud = sceneHud;
            strings = new UiStrings();
            hud.Bag.SetStrings(strings);
            BindSearchItems(catalog);
            aimProperties = new MaterialPropertyBlock();
            knowsLootTiers = HasRecognitionForPrototype();
            foreach (var item in items)
            {
                var name = ItemName(item);
                aimLabels.Add(item, item.IsPackage
                    ? (string.Format(strings.Get("package.openAim"), name), string.Format(strings.Get("package.checkAim"), name))
                    : (string.Format(strings.Get("search.take"), name), null));
            }
            player.Restore(entryPose);
            Application.wantsToQuit += SaveBeforeQuit;
            Open();
        }

        private void BindSearchItems(LootCatalog catalog)
        {
            if (searchPrepared)
                return;

            var views = items.ToDictionary(item => item.Id, StringComparer.Ordinal);
            foreach (var item in items)
                item.Bind(catalog[item.Id], views);
            searchPrepared = true;
        }

        private void Open()
        {
            if (!state.SlotLoaded || (!state.HasRun && !state.StartNewRun(Pile(), entryPose)))
            {
                ShowLoadError();
                return;
            }

            ApplySnapshot();
            if (!state.ResumePending)
            {
                CloseOverlay();
                return;
            }

            mode = Mode.Continue;
            foreach (var item in items)
                item.FreezeWorld(true);
            var summary = string.Format(strings.Get("continue.expedition"), state.Expedition);
            ShowOverlay(strings.Get("continue.title"), summary, string.Empty,
                strings.Get("continue.actions"), state.RecoveryNotice);
        }

        private SearchPile Pile() => pile ??= new SearchPile(items, containerFloor, inside.position);

        private void ApplySnapshot()
        {
            held = null;
            starterBag.gameObject.SetActive(!state.HasBag);
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
            if (player == null || !scenes.HasControl)
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
                if (!scenes.SaveAndTravel(SaveWorld))
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
                hud.Show(strings.Get("search.returnFocus"), strings.Get("search.pauseControls"), false);
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
                    if (state.Quests.HasInspectionStep(state.HeldItemId))
                    {
                        CaptureWorld();
                        state.Quests.RecordInspection(state.HeldItemId);
                        UpdateError();
                    }
                    inspecting = true;
                    inspectRotation = Quaternion.identity;
                    held.BeginInspection();
                }
                delta = Vector2.zero;
            }
            if (inspecting)
            {
                inspectRotation = Quaternion.Euler(delta.y * 0.55f, -delta.x * 0.55f, 0f) * inspectRotation;
                hud.Show(InspectionLabel(held), strings.Get("search.inspectControls"), false);
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
                        hud.Show(strings.Get("search.climbIn"), strings.Get("search.controlsWithBag"), true);
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
            var target = held == null && hit.collider != null ? hit.collider.GetComponent<SearchItem>() : null;
            if (target != null && !target.IsPackage && player.IsSupportedBy(hit.collider))
                target = null;
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
                else if (target != null && target.IsPackage)
                {
                    if (BrowsePackage(target))
                        return;
                }
                else if (target != null)
                    TakeInHand(target);
            }
            else if (held != null && mouse?.rightButton.wasPressedThisFrame == true)
                TryThrow();

            aimedItem = held == null ? target : null;
            var action = held != null ? strings.Get("search.holdActions") :
                target != null ? AimLabel(target) : string.Empty;
            if (held != null && Time.unscaledTime < blockedUntil)
                action = strings.Get("search.blockedPlacement");
            var nearby = atExit ? strings.Get("search.goHome") : atRim
                ? strings.Get(inContainer ? "search.climbOut" : "search.climbIn") : string.Empty;
            if (action.Length == 0)
            {
                action = nearby;
                nearby = string.Empty;
            }
            hud.Show(action, strings.Get(held == null ? "search.controlsWithBag" : "search.holdControls"),
                true, aimedItem, nearby);
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
            strings.ItemName(item.Id, item.DisplayName);

        private string AimLabel(SearchItem item)
        {
            var labels = aimLabels[item];
            return item.IsPackage && state.IsPackageOpen(item.Id) ? labels.CheckContents : labels.Action;
        }

        private string InspectionLabel(SearchItem item)
        {
            var label = ItemName(item);
            if (knowsLootTiers && item.Definition.Tier > 0)
                label = string.Format(strings.Get("search.inspectedTier"), label, item.Definition.Tier);
            var condition = strings.Get($"item.{item.Id}.condition", string.Empty);
            return condition.Length == 0 ? label : $"{label}  ·  {condition}";
        }

        private static bool HasRecognitionForPrototype()
        {
#if UNITY_EDITOR
            if (Environment.GetEnvironmentVariable("TRASH_HUNTER_RECOGNITION") == "1")
                return true;
#endif
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-trash-hunter-recognition") >= 0;
        }

        private bool BrowsePackage(SearchItem package)
        {
            CaptureWorld();
            if (!state.OpenPackage(package.Id))
                return false;

            package.ShowPackageOpened(true);
            browsingPackage = package;
            remainingContents.Clear();
            foreach (var item in package.InitialContents)
                if (state.IsInPackage(item.Id))
                    remainingContents.Add(item);
            packageSelection = 0;
            mode = Mode.Package;
            ShowPackage();
            return true;
        }

        private void ShowPackage()
        {
            var rows = new StringBuilder();
            for (var i = 0; i < remainingContents.Count; i++)
            {
                var name = ItemName(remainingContents[i]);
                rows.AppendLine(i == packageSelection ? $"<color=#F0C776><b>›  {name}</b></color>" : $"    {name}");
            }

            var empty = remainingContents.Count == 0;
            ShowOverlay(ItemName(browsingPackage), string.Empty,
                empty ? strings.Get("package.empty") : rows.ToString().TrimEnd(),
                strings.Get(empty ? "package.close" : "package.controls"), state.Error, state.Error != null, package: true);
        }

        private void UpdatePackage(Keyboard keyboard, bool confirm, bool cancel)
        {
            if (cancel)
            {
                CloseOverlay();
                return;
            }
            if (remainingContents.Count == 0)
                return;

            if (keyboard.upArrowKey.wasPressedThisFrame)
                packageSelection = Mathf.Max(0, packageSelection - 1);
            else if (keyboard.downArrowKey.wasPressedThisFrame)
                packageSelection = Mathf.Min(remainingContents.Count - 1, packageSelection + 1);
            else if (confirm && TakeInHand(remainingContents[packageSelection]))
            {
                CloseOverlay();
                return;
            }
            else if (!confirm)
                return;
            ShowPackage();
        }

        private void UpdateOverlay(Keyboard keyboard)
        {
            if (keyboard == null)
                return;

            var confirm = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            var cancel = keyboard.escapeKey.wasPressedThisFrame;
            switch (mode)
            {
                case Mode.Package:
                    UpdatePackage(keyboard, confirm, cancel);
                    break;
                case Mode.Continue:
                    if (confirm)
                    {
                        state.AcknowledgeResume();
                        foreach (var item in items)
                            item.FreezeWorld(false);
                        CloseOverlay();
                    }
                    else if (cancel)
                        Application.Quit();
                    break;
                case Mode.LoadError:
                    if (confirm)
                    {
                        if (scenes.RetrySlot())
                            Open();
                    }
                    else if (cancel)
                        Application.Quit();
                    break;
                case Mode.Return:
                    if (cancel)
                        CloseOverlay();
                    else if (confirm)
                    {
                        if (!scenes.SaveAndTravel(() =>
                        {
                            CaptureWorld();
                            return state.ReturnHome(returnExpedition);
                        }))
                            ShowReturn();
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
            var warning = strings.Get(held == null ? "return.warning" : "return.warningWithHand");
            var summary = string.Format(strings.Get("return.bag"), state.Bag.Count);
            var details = $"{strings.Get("return.details")}\n<color=#FFAA95>{warning}</color>";
            ShowOverlay(strings.Get("return.title"), summary, details, strings.Get("return.actions"), state.Error, true);
        }

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
            ShowOverlay(strings.Get("pause.title"), string.Empty,
                strings.Get("pause.expedition"),
                strings.Get("pause.actions"), state.Error, state.Error != null);
        }

        private void ShowOverlay(string title, string summary, string details, string keys, string message,
            bool warning = false, bool package = false)
        {
            hud.Show(string.Empty, string.Empty, false);
            hud.ShowError(null);
            hud.Expedition.Show(title, summary, details, keys, message, warning, package);
            UnlockCursor();
        }

        private void CloseOverlay()
        {
            mode = Mode.Search;
            hud.Expedition.gameObject.SetActive(false);
            LockCursor();
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

        private bool SaveWorld()
        {
            CaptureWorld();
            return state.Save();
        }

        private bool SaveBeforeQuit()
        {
            if (quitting || mode == Mode.LoadError || mode == Mode.Continue || !scenes.HasControl)
                return true;

            if (!SaveWorld())
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
            state.RecordPlayer(player.Capture(IsInContainer()));
            state.RecordHeldInWorld(held != null && held.HeldInWorld);
            foreach (var item in items)
                item.Record(state);
        }

        private void FixedUpdate()
        {
            if (held == null || mode != Mode.Search || inspecting || bagOpen || !scenes.HasControl ||
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
            if (held == null || !scenes.HasControl || (held.HeldInWorld && !inspecting))
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

        internal void ShowTransition(bool failed)
        {
            ShowOverlay(strings.Get(failed ? "transition.failed" : "transition.loading"),
                string.Empty, string.Empty, failed ? strings.Get("transition.actions") : string.Empty, null);
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
