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
        [SerializeField] private Text selection;
        [SerializeField] private Text feedback;
        [SerializeField] private Text controls;

        private static readonly Color Accent = new Color32(240, 199, 118, 255);
        private static readonly Color Error = new Color32(255, 170, 149, 255);
        private static readonly Color Neutral = new Color32(232, 225, 208, 255);
        private RectTransform[] tiles;

        private void Awake()
        {
            tiles = new RectTransform[BagInventory.Columns * BagInventory.Rows];
            for (var i = 0; i < tiles.Length; i++)
                tiles[i] = Instantiate(itemTile, itemTile.transform.parent);
            preview.transform.SetAsLastSibling();
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

        public void ShowContents(BagInventory bag, SearchItem[] items)
        {
            var used = 0;
            foreach (var item in items)
            {
                if (!bag.TryGetPlacement(item.Id, out var placement))
                    continue;

                var tile = tiles[used++];
                Position(tile, placement, 3);
                tile.GetComponentInChildren<Text>(true).text = item.BagLabel;
                tile.gameObject.SetActive(true);
            }

            for (var i = used; i < tiles.Length; i++)
                tiles[i].gameObject.SetActive(false);
        }

        public void ShowSelection(SearchItem held, SearchItem hovered, BagPlacement placement, BagFit fit, bool overGrid, bool empty)
        {
            preview.gameObject.SetActive(overGrid && (held != null || hovered != null));
            if (overGrid && (held != null || hovered != null))
            {
                var tint = fit == BagFit.Fits ? Accent : Error;
                Position(preview.rectTransform, placement, 2);
                preview.color = new Color(tint.r, tint.g, tint.b, 0.10f);
                foreach (var edge in previewEdges)
                    edge.color = tint;
            }

            if (held == null)
            {
                selection.text = hovered != null ? hovered.DisplayName : empty ? "Сумка пуста" : "В руке пусто";
                feedback.text = hovered != null ? "ЛКМ  Достать" : string.Empty;
                feedback.color = Neutral;
                controls.text = "Tab / Esc  Закрыть";
                return;
            }

            selection.text = $"{held.BagLabel} в руке   ·   {placement.Width} × {placement.Height}";
            feedback.text = !overGrid ? "Выберите ячейку" : fit switch
            {
                BagFit.Outside => "Вещь выходит за край",
                BagFit.Occupied => "Эти ячейки заняты",
                _ => "ЛКМ  Уложить"
            };
            feedback.color = !overGrid ? Neutral : fit == BagFit.Fits ? Accent : Error;
            controls.text = "R  Повернуть     Tab / Esc  Закрыть";
        }

        private static void Position(RectTransform rect, BagPlacement placement, float inset)
        {
            rect.anchoredPosition = new Vector2(placement.Column * CellSize + inset, -placement.Row * CellSize - inset);
            rect.sizeDelta = new Vector2(placement.Width * CellSize - 2 * inset, placement.Height * CellSize - 2 * inset);
        }
    }
}
