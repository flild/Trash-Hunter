using UnityEngine;
using UnityEngine.UI;

namespace TrashHunter
{
    public sealed class BagView : MonoBehaviour
    {
        public const int CellSize = 98;

        [SerializeField] private RectTransform grid;
        [SerializeField] private RectTransform itemTile;
        [SerializeField] private GameObject[] backpackRow;
        [SerializeField] private Image preview;
        [SerializeField] private Image[] previewEdges;
        [SerializeField] private Text title;
        [SerializeField] private Text selection;
        [SerializeField] private Text feedback;
        [SerializeField] private Text controls;
        [SerializeField] private Text handTitle;
        [SerializeField] private ItemTileView handSlot;
        [SerializeField] private ItemTileView pointerTile;

        private static readonly Color Accent = new Color32(240, 199, 118, 255);
        private static readonly Color Error = new Color32(255, 170, 149, 255);
        private static readonly Color Neutral = new Color32(232, 225, 208, 255);
        private ItemTileView[] tiles;
        private UiStrings strings;
        private int rows = BagInventory.Rows;

        private void Awake()
        {
            var template = itemTile.GetComponent<ItemTileView>();
            tiles = new ItemTileView[BagInventory.Columns * BagInventory.BackpackRows];
            for (var i = 0; i < tiles.Length; i++)
                tiles[i] = Instantiate(template, itemTile.transform.parent);
            preview.transform.SetAsLastSibling();
            OnRectTransformDimensionsChange();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (title == null)
                return;

            var bounds = ((RectTransform)transform).rect.size - Vector2.one * 32f;
            var panel = (RectTransform)title.transform.parent;
            var size = panel.rect.size;
            var scale = Mathf.Clamp(Mathf.Min(bounds.x / size.x, bounds.y / size.y), 0.01f, 1f);
            panel.localScale = Vector3.one * scale;
        }

        internal void SetStrings(UiStrings uiStrings)
        {
            strings = uiStrings;
            title.text = strings.Get("bag.title");
            handTitle.text = strings.Get("bag.handSlot");
            controls.text = strings.Get("bag.dragControls");
        }

        private void SetRows(int height)
        {
            if (height == rows)
                return;

            var delta = Vector2.up * ((height - rows) * CellSize);
            rows = height;
            grid.sizeDelta += delta;
            ((RectTransform)title.transform.parent).sizeDelta += delta;
            selection.rectTransform.anchoredPosition -= delta;
            feedback.rectTransform.anchoredPosition -= delta;
            controls.rectTransform.anchoredPosition -= delta;
            var backpack = height == BagInventory.BackpackRows;
            foreach (var cell in backpackRow)
                cell.SetActive(backpack);
            title.text = strings.Get(backpack ? "bag.backpack" : "bag.title");
            OnRectTransformDimensionsChange();
        }

        internal bool IsOverHand(Vector2 screenPosition) =>
            RectTransformUtility.RectangleContainsScreenPoint(handSlot.Rect, screenPosition);

        public Vector2Int CellAt(Vector2 screenPosition)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(grid, screenPosition, null, out var local))
                return new Vector2Int(-1, -1);

            var column = Mathf.FloorToInt((local.x - grid.rect.xMin) / CellSize);
            var row = Mathf.FloorToInt((grid.rect.yMax - local.y) / CellSize);
            return column >= 0 && column < BagInventory.Columns && row >= 0 && row < rows
                ? new Vector2Int(column, row)
                : new Vector2Int(-1, -1);
        }

        public void ShowContents(BagInventory bag, SearchItem[] items, SearchItem moving = null)
        {
            SetRows(bag.Height);
            var used = 0;
            foreach (var item in items)
            {
                if (!bag.TryGetPlacement(item.Id, out var placement))
                    continue;

                var tile = tiles[used++];
                Position(tile.Rect, placement, 3);
                tile.Show(BagLabel(item), source: item == moving);
                tile.gameObject.SetActive(true);
            }

            for (var i = used; i < tiles.Length; i++)
                tiles[i].gameObject.SetActive(false);
        }

        internal void ShowSelection(SearchItem held, SearchItem hovered, SearchItem moving, BagPlacement placement,
            BagFit fit, bool overGrid, bool overHand, Vector2 pointer, bool dragging, string rejectedDrop)
        {
            var item = moving != null ? moving : held;
            var overHandDrop = dragging && overHand && moving != held;
            var handTarget = overHandDrop && held == null;
            handSlot.Show(held == null ? strings.Get("bag.emptyHand") : BagLabel(held),
                handTarget || (overHand && held != null && !overHandDrop), dragging && moving == held);

            pointerTile.gameObject.SetActive(dragging);
            if (dragging)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, pointer, null, out var point);
                var limits = ((RectTransform)transform).rect;
                var half = pointerTile.Rect.rect.size * 0.5f;
                pointerTile.Rect.anchoredPosition = new Vector2(
                    Mathf.Clamp(point.x, limits.xMin + half.x, limits.xMax - half.x),
                    Mathf.Clamp(point.y + 24f, limits.yMin + half.y, limits.yMax - half.y));
                pointerTile.Show(BagLabel(moving));
            }

            var showPreview = overGrid && item != null;
            preview.gameObject.SetActive(showPreview);
            if (showPreview)
            {
                var tint = fit == BagFit.Fits ? Accent : Error;
                Position(preview.rectTransform, placement, 2);
                preview.color = new Color(tint.r, tint.g, tint.b, fit == BagFit.Fits ? 0.22f : 0.30f);
                foreach (var edge in previewEdges)
                    edge.color = tint;
            }

            selection.text = item != null
                ? string.Format(strings.Get(moving != null ? "bag.moving" : "bag.held"),
                    BagLabel(item), placement.Width, placement.Height)
                : hovered != null ? strings.ItemName(hovered.Id, hovered.DisplayName) : string.Empty;

            var message = rejectedDrop;
            if (message == null && handTarget)
                message = "bag.dropInHand";
            if (message == null && overHandDrop)
                message = "bag.handOccupied";
            if (message == null && item != null && overGrid)
                message = fit != BagFit.Fits ? FitMessage(fit) : dragging ? "bag.dropHere" : moving != null ? "bag.move" : "bag.pack";
            if (message == null && item == null && hovered != null)
                message = "bag.take";

            feedback.text = message == null ? string.Empty : strings.Get(message);
            feedback.color = rejectedDrop != null || message == "bag.handOccupied" || (overGrid && fit != BagFit.Fits)
                ? Error : message == null ? Neutral : Accent;
            controls.text = strings.Get(moving == null ? "bag.dragControls" : "bag.moveControls");
        }

        internal static string FitMessage(BagFit fit) => fit == BagFit.Occupied ? "bag.occupied" : "bag.outside";

        private string BagLabel(SearchItem item) => strings.Get($"item.{item.Id}", item.BagLabel);

        private static void Position(RectTransform rect, BagPlacement placement, float inset)
        {
            rect.anchoredPosition = new Vector2(placement.Column * CellSize + inset, -placement.Row * CellSize - inset);
            rect.sizeDelta = new Vector2(placement.Width * CellSize - 2 * inset, placement.Height * CellSize - 2 * inset);
        }
    }
}
