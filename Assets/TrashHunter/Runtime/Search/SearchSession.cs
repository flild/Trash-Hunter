using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TrashHunter
{
    public sealed class SearchSession : MonoBehaviour
    {
        private enum Mode { Search, Return, Pause, Continue, LoadError, Quest }

        [SerializeField] private Transform outside;
        [SerializeField] private Transform yardEntry;
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
        private readonly Dictionary<string, Vector3> drops = new();
        private readonly Dictionary<SearchItem, string> aimLabels = new();
        private bool knowsLootTiers;
        private bool moveRotated;
        private bool draggingInBag;
        private bool crossedDragThreshold;
        private Vector2 dragStart;
        private Vector2Int grabOffset;
        private string rejectedDrop;
        private UiStrings strings;
        private Quaternion inspectRotation;
        private StoryFact visibleEvidence;
        private Mode mode;
        private bool inspecting;
        private bool bagOpen;
        private bool awaitingGrip;
        private SceneCoordinator scenes;
        private PlayerPose entryPose;
        private bool searchPrepared;
        private bool quitting;
        private float blockedUntil;
        private float holdDistance;
        private int returnExpedition;

        internal Transform SearchEntry => yardEntry != null ? yardEntry : outside;

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

        internal SearchPile PreparePile(LootCatalog catalog, bool secondYard = false)
        {
            BindSearchItems(catalog);
            Physics.SyncTransforms();
            return Pile(secondYard);
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
                aimLabels.Add(item, item.IsPackage ? string.Format(strings.Get("package.openAim"), name)
                    : strings.Get($"item.{item.Id}.aim", name));
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
            var summary = state.AtSecondYard ? strings.Get("story.yard")
                : string.Format(strings.Get("continue.expedition"), state.Expedition);
            ShowOverlay(strings.Get("continue.title"), summary, string.Empty,
                strings.Get("continue.actions"), state.RecoveryNotice);
        }

        private SearchPile Pile(bool secondYard = false) =>
            pile ??= new SearchPile(items, containerFloor, inside.position, secondYard);

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
            awaitingGrip = held != null;
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
            if (!bagOpen && keyboard?.hKey.wasPressedThisFrame == true)
                hud.ToggleControls();
            var toggleBag = keyboard?.tabKey.wasPressedThisFrame == true;
            if (bagOpen)
            {
                if (toggleBag || (cancel && movingInBag == null))
                    CloseBag();
                else if (cancel)
                    CancelBagMove();
                else if (keyboard?.qKey.wasPressedThisFrame == true && held != null && movingInBag == null)
                {
                    CloseBag();
                    BeginInspection();
                }
                else
                    UpdateBag(keyboard, mouse);
                UpdateError();
                return;
            }

            if (keyboard?.jKey.wasPressedThisFrame == true && state.Quests.IsActive(QuestCatalog.TraderTurntable))
            {
                ShowQuest();
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

            if (toggleBag && state.HasBag)
            {
                EndInspection();
                bagOpen = true;
                held?.FreezeWorld(true);
                hud.Show(string.Empty, string.Empty, false);
                hud.Bag.gameObject.SetActive(true);
                hud.Bag.ShowContents(state.Bag, items);
                UpdateBag(null, null);
                UnlockCursor();
                return;
            }

            Vector2 delta = mouse == null ? Vector2.zero : mouse.delta.ReadValue();
            if (keyboard?.qKey.wasPressedThisFrame == true && held != null)
            {
                if (inspecting)
                    EndInspection();
                else
                    BeginInspection();
                delta = Vector2.zero;
            }
            if (inspecting)
            {
                inspectRotation = Quaternion.Euler(delta.y * 0.55f, -delta.x * 0.55f, 0f) * inspectRotation;
                if (held.Id == GameState.HomeTag && keyboard?.rKey.wasPressedThisFrame == true)
                    inspectRotation = Vector3.Dot(inspectRotation * Vector3.forward, Vector3.forward) >= 0
                        ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                InspectEvidence();
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
            if (held != null)
            {
                if (mouse?.rightButton.wasPressedThisFrame == true)
                    ReleaseHand(true);
                else if (mouse?.leftButton.isPressed == true)
                    awaitingGrip = false;
                else if (!awaitingGrip && held.HeldInWorld && Time.unscaledTime >= blockedUntil)
                    ReleaseHand(false);
            }
            else if (target != null && mouse?.leftButton.wasPressedThisFrame == true)
            {
                if (target.IsPackage)
                    SpillPackage(target);
                else
                    TakeInHand(target);
            }

            aimedItem = held == null ? target : null;
            var action = aimedItem != null ? AimLabel(aimedItem) : string.Empty;
            if (Time.unscaledTime < blockedUntil)
                action = strings.Get("search.blockedPlacement");
            var nearby = atExit ? strings.Get("search.goHome") : atRim
                ? strings.Get(inContainer ? "search.climbOut" : "search.climbIn") : string.Empty;
            if (action.Length == 0)
            {
                action = nearby;
                nearby = string.Empty;
            }
            hud.Show(action, strings.Get(held == null ? "search.controlsWithBag" : awaitingGrip
                ? "search.storedHandControls" : "search.holdControls"),
                true, aimedItem, nearby);
            UpdateError();
        }

        private void BeginInspection()
        {
            if (state.Quests.HasInspectionStep(state.HeldItemId))
            {
                CaptureWorld();
                state.Quests.RecordInspection(state.HeldItemId);
                UpdateError();
            }

            inspecting = true;
            visibleEvidence = StoryFact.None;
            inspectRotation = GameState.IsEvidence(held.Id) ? Quaternion.identity : held.InspectionStartRotation();
            held.BeginInspection();
            var label = ItemName(held);
            if (knowsLootTiers && held.Definition.Tier > 0)
                label = string.Format(strings.Get("search.inspectedTier"), label, held.Definition.Tier);
            hud.Inspection.Show(label, strings.Get($"item.{held.Id}.condition", string.Empty));
            hud.Show(string.Empty, strings.Get(held.Id == GameState.HomeTag
                ? "story.tagControls" : "search.inspectControls"), false);
            InspectEvidence();
        }

        private void InspectEvidence()
        {
            if (!GameState.IsEvidence(held.Id))
                return;

            var facing = Vector3.Dot(inspectRotation * Vector3.forward, Vector3.forward);
            var face = facing >= 0.82f ? held.Id == GameState.HomeTag ? StoryFact.Address : StoryFact.Recorder
                : facing <= -0.82f && held.Id == GameState.HomeTag ? StoryFact.Batch : StoryFact.None;
            if (visibleEvidence == face)
                return;

            visibleEvidence = face;
            if (face != StoryFact.None && !state.Knows(face))
            {
                CaptureWorld();
                state.ReadHeldEvidence(face);
                UpdateError();
            }
            hud.Inspection.ShowEvidence(state, strings, ItemName(held), held.Id, face);
        }
        private void EndInspection()
        {
            if (!inspecting)
                return;
            inspecting = false;
            held.EndInspection();
            hud.Inspection.Hide();
        }

        private string ItemName(SearchItem item) =>
            strings.ItemName(item.Id, item.DisplayName);

        private string AimLabel(SearchItem item)
        {
            if (item.IsPackage && state.IsPackageOpen(item.Id))
            {
                foreach (var content in item.InitialContents)
                    if (state.IsInPackage(content.Id))
                        return aimLabels[item];
                return strings.Get("package.empty");
            }
            return aimLabels[item];
        }

        private static bool HasRecognitionForPrototype()
        {
#if UNITY_EDITOR
            if (Environment.GetEnvironmentVariable("TRASH_HUNTER_RECOGNITION") == "1")
                return true;
#endif
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-trash-hunter-recognition") >= 0;
        }

        private void SpillPackage(SearchItem package)
        {
            drops.Clear();
            var mouth = package.Shape.bounds;
            var towardsPlayer = Vector3.ProjectOnPlane(player.transform.position - mouth.center, Vector3.up).normalized;
            if (towardsPlayer == Vector3.zero)
                towardsPlayer = Vector3.forward;
            var bottom = mouth.max.y + 0.04f;
            foreach (var item in package.InitialContents)
            {
                if (!state.IsInPackage(item.Id))
                    continue;
                // Separate spawn volumes keep the contents clear of each other and the shell.
                var position = new Vector3(mouth.center.x, bottom + item.HalfSize.y, mouth.center.z);
                if (Physics.CheckBox(position, item.HalfSize, Quaternion.identity,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    blockedUntil = Time.unscaledTime + 1.25f;
                    return;
                }
                drops.Add(item.Id, position);
                bottom = position.y + item.HalfSize.y + 0.04f;
            }
            if (drops.Count == 0 && state.IsPackageOpen(package.Id))
                return;
            CaptureWorld();
            if (!state.OpenPackage(package.Id, drops))
                return;
            package.ShowPackageOpened(true);
            for (var i = 0; i < package.InitialContents.Length; i++)
            {
                var item = package.InitialContents[i];
                if (drops.TryGetValue(item.Id, out var position))
                    item.Spill(position, Quaternion.AngleAxis(i % 2 == 0 ? -34f : 34f, Vector3.up) * towardsPlayer);
            }
        }

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
                case Mode.Quest:
                    if (cancel)
                        CloseOverlay();
                    break;
                case Mode.Pause:
                    if (keyboard.jKey.wasPressedThisFrame && state.Quests.IsActive(QuestCatalog.TraderTurntable))
                        ShowQuest();
                    else if (cancel || confirm)
                        CloseOverlay();
                    else if (keyboard.qKey.wasPressedThisFrame && SaveBeforeQuit())
                        Application.Quit();
                    break;
            }
        }

        private void ShowQuest()
        {
            EndInspection();
            mode = Mode.Quest;
            ShowOverlay(strings.Get("quest.title"), strings.Get("quest.trader"), strings.Get("quest.condition"),
                strings.Get("quest.closeActions"), null);
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
            var actions = strings.Get("pause.actions");
            if (state.Quests.IsActive(QuestCatalog.TraderTurntable))
                actions = $"{actions}\n{strings.Get("quest.view")}";
            ShowOverlay(strings.Get("pause.title"), string.Empty,
                strings.Get("pause.expedition"), actions, state.Error, state.Error != null);
        }

        private void ShowOverlay(string title, string summary, string details, string keys, string message,
            bool warning = false)
        {
            hud.Show(string.Empty, string.Empty, false);
            hud.ShowError(null);
            hud.Expedition.Show(title, summary, details, keys, message, warning);
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
            held?.FreezeWorld(false);
            CancelBagMove();
            hud.Bag.gameObject.SetActive(false);
            LockCursor();
        }

        private void CancelBagMove()
        {
            movingInBag = null;
            draggingInBag = false;
            rejectedDrop = null;
            hud.Bag.ShowContents(state.Bag, items);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && bagOpen && movingInBag != null)
                CancelBagMove();
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
            var pointer = mouse?.position.ReadValue() ?? Vector2.zero;
            var cell = mouse == null ? new Vector2Int(-1, -1) : hud.Bag.CellAt(pointer);
            var overGrid = cell.x >= 0;
            var overHand = mouse != null && hud.Bag.IsOverHand(pointer);
            var hovered = PackedAt(cell, out var hoveredPlacement);
            var left = mouse?.leftButton;
            var rightPressed = mouse?.rightButton.wasPressedThisFrame == true;
            var rotate = keyboard?.rKey.wasPressedThisFrame == true;
            if (left?.wasPressedThisFrame == true || rightPressed || rotate)
                rejectedDrop = null;

            if (rightPressed)
            {
                if (movingInBag != null)
                    CancelBagMove();
                else if (hovered != null)
                    BeginBagMove(hovered, hoveredPlacement.Rotated, false, Vector2Int.zero, pointer);
            }
            else if (left?.wasPressedThisFrame == true && movingInBag == null)
            {
                if (overHand && held != null)
                    BeginBagMove(held, state.HeldItemRotated, true, Vector2Int.zero, pointer);
                else if (hovered != null)
                    BeginBagMove(hovered, hoveredPlacement.Rotated, true,
                        cell - new Vector2Int(hoveredPlacement.Column, hoveredPlacement.Row), pointer);
            }

            if (rotate)
            {
                if (movingInBag != null)
                {
                    if (draggingInBag && movingInBag != held)
                    {
                        var size = movingInBag.Placement(0, 0, moveRotated);
                        grabOffset = moveRotated
                            ? new Vector2Int(grabOffset.y, size.Width - 1 - grabOffset.x)
                            : new Vector2Int(size.Height - 1 - grabOffset.y, grabOffset.x);
                    }
                    moveRotated = !moveRotated;
                    crossedDragThreshold = true;
                }
                else if (held != null)
                    state.HeldItemRotated = !state.HeldItemRotated;
            }

            var placement = PreviewPlacement(cell, out var fit);
            if (draggingInBag)
            {
                crossedDragThreshold |= (pointer - dragStart).sqrMagnitude >= 25f;
                if (left?.wasReleasedThisFrame == true)
                {
                    var rejection = DropInBag(overGrid, overHand, hovered, placement, fit);
                    CancelBagMove();
                    rejectedDrop = rejection;
                    hovered = PackedAt(cell, out _);
                    placement = PreviewPlacement(cell, out fit);
                }
                else if (left?.isPressed != true)
                    CancelBagMove();
            }
            else if (!rightPressed && overGrid && fit == BagFit.Fits && left?.wasPressedThisFrame == true)
            {
                if (ApplyBagPlacement(placement))
                    CancelBagMove();
            }

            hud.Bag.ShowSelection(held, hovered, movingInBag, placement, fit, overGrid, overHand, pointer,
                draggingInBag, rejectedDrop);
        }

        private SearchItem PackedAt(Vector2Int cell, out BagPlacement placement)
        {
            if (cell.x >= 0)
            {
                foreach (var item in items)
                    if (state.Bag.TryGetPlacement(item.Id, out placement) && placement.Contains(cell.x, cell.y))
                        return item;
            }

            placement = default;
            return null;
        }

        private void BeginBagMove(SearchItem item, bool rotated, bool drag, Vector2Int offset, Vector2 pointer)
        {
            movingInBag = item;
            moveRotated = rotated;
            draggingInBag = drag;
            crossedDragThreshold = false;
            grabOffset = offset;
            dragStart = pointer;
            hud.Bag.ShowContents(state.Bag, items, item);
        }

        private BagPlacement PreviewPlacement(Vector2Int cell, out BagFit fit)
        {
            var item = movingInBag != null ? movingInBag : held;
            if (item == null)
            {
                fit = BagFit.Fits;
                return default;
            }

            var anchor = movingInBag == null ? cell : cell - grabOffset;
            var placement = item.Placement(anchor.x, anchor.y, movingInBag == null ? state.HeldItemRotated : moveRotated);
            fit = state.Bag.Check(placement, item == held ? null : item.Id);
            return placement;
        }

        private string DropInBag(bool overGrid, bool overHand, SearchItem hovered, BagPlacement placement, BagFit fit)
        {
            if (overHand)
            {
                if (movingInBag == held)
                    return null;
                if (held != null)
                    return "bag.handOccupied";
                TakeInHand(movingInBag);
                return null;
            }

            if (!crossedDragThreshold && hovered == movingInBag)
            {
                if (held == null)
                    TakeInHand(movingInBag);
                return null;
            }

            if (!overGrid || fit != BagFit.Fits)
                return BagView.FitMessage(overGrid ? fit : BagFit.Outside);
            ApplyBagPlacement(placement);
            return null;
        }

        private bool ApplyBagPlacement(BagPlacement placement)
        {
            var item = movingInBag != null ? movingInBag : held;
            if (item == null)
                return false;
            if (item != held)
            {
                if (state.Bag.TryGetPlacement(item.Id, out var original) &&
                    original.Column == placement.Column && original.Row == placement.Row &&
                    original.Rotated == placement.Rotated)
                    return true;

                CaptureWorld();
                return state.MovePacked(item.Id, placement);
            }

            CaptureWorld();
            if (!state.Pack(placement))
                return false;
            held.Store();
            ClearHand();
            return true;
        }

        private bool TakeInHand(SearchItem item)
        {
            CaptureWorld();
            if (!state.Take(item.Id))
                return false;
            item.TakeInHand(state.HeldInWorld);
            held = item;
            awaitingGrip = !held.HeldInWorld;
            holdDistance = item.MinimumHoldDistance;
            blockedUntil = 0f;
            return true;
        }

        private void ClearHand()
        {
            held = null;
            awaitingGrip = false;
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
                Cursor.lockState != CursorLockMode.Locked ||
                (!held.HeldInWorld && Mouse.current?.leftButton.isPressed != true))
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
            var rotation = inspecting ? ItemInspectionView.EvidenceRotation(held.Id, inspectRotation) : Quaternion.identity;
            held.Present(view.TransformPoint(offset), view.rotation * rotation);
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

        private bool HasRoom(Vector3 position, Quaternion rotation)
        {
            var count = Physics.OverlapBoxNonAlloc(position, held.HalfSize, placementHits, rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return count == 0 || (count == 1 && placementHits[0] == held.Shape);
        }

        private void ReleaseHand(bool throwing)
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
                if (throwing)
                    held.Throw(view.forward);
                else
                    held.Release();
                ClearHand();
            }
            else
                blockedUntil = Time.unscaledTime + 1.25f;
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
