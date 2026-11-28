using UnityEngine;

namespace TrashHunter
{
    [RequireComponent(typeof(UnityEngine.UI.Image))]
    public sealed class ItemTileView : MonoBehaviour
    {
        private UnityEngine.UI.Image background;
        private UnityEngine.UI.Text label;
        private Color normal;
        private int defaultFontSize;
        private float labelWidth = -1f;

        public RectTransform Rect => (RectTransform)transform;

        public void Show(string name, bool highlighted = false, bool source = false)
        {
            if (background == null)
            {
                background = GetComponent<UnityEngine.UI.Image>();
                label = GetComponentInChildren<UnityEngine.UI.Text>(true);
                normal = background.color;
                defaultFontSize = label.fontSize;
            }

            var width = label.rectTransform.rect.width;
            if (label.text != name || !Mathf.Approximately(labelWidth, width))
            {
                labelWidth = width;
                label.text = name;
                label.fontSize = defaultFontSize;
                var preferredWidth = label.preferredWidth;
                if (width > 0f && preferredWidth > width)
                    label.fontSize = Mathf.Max(12, Mathf.FloorToInt(defaultFontSize * width / preferredWidth));
            }
            var tint = highlighted ? Color.Lerp(normal, new Color32(240, 199, 118, 255), 0.25f) : normal;
            tint.a *= source ? 0.45f : 1f;
            background.color = tint;
        }
    }
}
