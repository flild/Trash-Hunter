using UnityEngine;
using UnityEngine.UI;

namespace TrashHunter
{
    public sealed class BagView : MonoBehaviour
    {
        public const int CellSize = 98;

        [SerializeField] private RectTransform grid;
        [SerializeField] private RectTransform itemTile;
        [SerializeField] private Image preview;
        [SerializeField] private Image[] previewEdges;
        [SerializeField] private Text title;
        [SerializeField] private Text selection;
        [SerializeField] private Text feedback;
        [SerializeField] private Text controls;

        private static readonly Color Accent = new Color32(240, 199, 118, 255);
        private static readonly Color Error = new Color32(255, 170, 149, 255);
        private static readonly Color Neutral = new Color32(232, 225, 208, 255);
        private RectTransform[] tiles;
        private UiStrings strings;

        private void Awake()
        {
            tiles = new RectTransform[BagInventory.Columns * BagInventory.Rows];
            for (var i = 0; i < tiles.Length; i++)
                tiles[i] = Instantiate(itemTile, itemTile.transform.parent);
            preview.transform.SetAsLastSibling();
        }

        internal void SetStrings(UiStrings uiStrings)
        {
            strings = uiStrings;
            title.text = strings.Get("bag.title");
        }

        public Vector2Int CellAt(Vector2 screenPosition)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(grid, screenPosition, null, out var local))
                return new Vector2Int(-1, -1);

            var column = Mathf.FloorToInt((local.x - grid.rect.xMin) / CellSize);
            var row = Mathf.FloorToInt((grid.rect.yMax - local.y) / CellSize);
            return column >= 0 && column < BagInventory.Columns && row >= 0 && row < BagInventory.Rows
                ? new Vector2Int(column, row)
                : new Vector2Int(-1, -1);
        }

        public void ShowContents(BagInventory bag, SearchItem[] items, SearchItem moving = null)
        {
            var used = 0;
            var tileColor = itemTile.GetComponent<Image>().color;
            var sourceColor = new Color(tileColor.r, tileColor.g, tileColor.b, 0.45f);
            foreach (var item in items)
            {
                if (!bag.TryGetPlacement(item.Id, out var placement))
                    continue;

                var tile = tiles[used++];
                Position(tile, placement, 3);
                var selected = item == moving;
                tile.GetComponent<Image>().color = selected ? sourceColor : tileColor;
                tile.GetComponentInChildren<Text>(true).text = selected ? string.Empty : BagLabel(item);
                tile.gameObject.SetActive(true);
            }

            for (var i = used; i < tiles.Length; i++)
                tiles[i].gameObject.SetActive(false);
        }

        public void ShowSelection(SearchItem held, SearchItem hovered, SearchItem moving, BagPlacement placement,
            BagFit fit, bool overGrid, bool empty)
        {
            var showPreview = overGrid && (held != null || hovered != null || moving != null);
            preview.gameObject.SetActive(showPreview);
            if (showPreview)
            {
                var tint = fit == BagFit.Fits ? Accent : Error;
                Position(preview.rectTransform, placement, 2);
                preview.color = new Color(tint.r, tint.g, tint.b, 0.10f);
                foreach (var edge in previewEdges)
                    edge.color = tint;
            }

            var activeItem = moving != null ? moving : held;
            if (activeItem == null)
            {
                selection.text = hovered != null
                    ? strings.Get($"item.{hovered.Id}.name", strings.Get($"item.{hovered.Id}", hovered.DisplayName))
                    : strings.Get(empty ? "bag.empty" : "bag.emptyHand");
                feedback.text = hovered != null ? strings.Get("bag.take") : string.Empty;
                feedback.color = Neutral;
                controls.text = strings.Get(empty ? "bag.closeControls" : "bag.idleControls");
                return;
            }

            selection.text = string.Format(strings.Get(moving != null ? "bag.moving" : "bag.held"),
                BagLabel(activeItem), placement.Width, placement.Height);
            feedback.text = strings.Get(!overGrid ? "bag.chooseCell" : fit switch
            {
                BagFit.Outside => "bag.outside",
                BagFit.Occupied => "bag.occupied",
                _ => moving != null ? "bag.move" : "bag.pack"
            });
            feedback.color = !overGrid ? Neutral : fit == BagFit.Fits ? Accent : Error;
            controls.text = strings.Get(moving != null ? "bag.moveControls" :
                empty ? "bag.packControls" : "bag.fullControls");
        }

        private string BagLabel(SearchItem item) => strings.Get($"item.{item.Id}", item.BagLabel);

        private static void Position(RectTransform rect, BagPlacement placement, float inset)
        {
            rect.anchoredPosition = new Vector2(placement.Column * CellSize + inset, -placement.Row * CellSize - inset);
            rect.sizeDelta = new Vector2(placement.Width * CellSize - 2 * inset, placement.Height * CellSize - 2 * inset);
        }
    }
}
