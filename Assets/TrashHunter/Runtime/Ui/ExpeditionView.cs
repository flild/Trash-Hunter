using UnityEngine;
using UnityEngine.UI;

namespace TrashHunter
{
    public sealed class ExpeditionView : MonoBehaviour
    {
        [SerializeField] private Text heading;
        [SerializeField] private Text subtitle;
        [SerializeField] private Text body;
        [SerializeField] private Text feedback;
        [SerializeField] private Text actions;
        [SerializeField] private GameObject packageListBacking;
        [SerializeField] private RectTransform panel;

        private Vector2 defaultBodyPosition;
        private Vector2 defaultBodySize;
        private Vector2 defaultPanelSize;
        private Vector2 defaultPanelAnchor;
        private Vector2 defaultPanelPosition;
        private Vector2 defaultActionsSize;
        private Vector2 defaultActionsPosition;

        private void Awake()
        {
            defaultPanelSize = panel.sizeDelta;
            defaultPanelAnchor = panel.anchorMin;
            defaultPanelPosition = panel.anchoredPosition;
            defaultActionsSize = actions.rectTransform.sizeDelta;
            defaultActionsPosition = actions.rectTransform.anchoredPosition;
            defaultBodyPosition = body.rectTransform.anchoredPosition;
            defaultBodySize = body.rectTransform.sizeDelta;
        }

        private static readonly Color Neutral = new Color32(232, 225, 208, 255);
        private static readonly Color Warning = new Color32(255, 170, 149, 255);

        public void Hide() => gameObject.SetActive(false);

        public void Show(string title, string summary, string details, string keys,
            string message = null, bool isWarning = false, bool package = false, bool homeStock = false)
        {
            gameObject.SetActive(true);
            packageListBacking.SetActive(package);
            panel.anchorMin = panel.anchorMax = homeStock ? new Vector2(0.5f, 0f) : defaultPanelAnchor;
            panel.anchoredPosition = homeStock ? new Vector2(0f, defaultPanelSize.y / 2f + 16f) : defaultPanelPosition;
            panel.sizeDelta = package ? new Vector2(defaultPanelSize.x, 314f) : defaultPanelSize;
            body.rectTransform.anchoredPosition = package ? new Vector2(0f, -150f) : defaultBodyPosition;
            body.rectTransform.sizeDelta = package ? new Vector2(580f, 78f) : defaultBodySize;
            heading.text = title;
            subtitle.text = summary;
            body.text = details;
            var firstBreak = keys.IndexOf('\n');
            var thirdRow = firstBreak >= 0 && firstBreak != keys.LastIndexOf('\n');
            actions.rectTransform.sizeDelta = firstBreak < 0 ? defaultActionsSize
                : new Vector2(defaultActionsSize.x, thirdRow ? 66f : 52f);
            actions.rectTransform.anchoredPosition = defaultActionsPosition - (thirdRow ? Vector2.up * 9f : Vector2.zero);
            actions.text = keys;
            feedback.text = message ?? string.Empty;
            feedback.color = isWarning ? Warning : Neutral;
        }
    }
}
