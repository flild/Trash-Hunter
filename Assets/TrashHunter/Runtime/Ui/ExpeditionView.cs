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
        private Vector2 defaultActionsSize;

        private void Awake()
        {
            defaultPanelSize = panel.sizeDelta;
            defaultActionsSize = actions.rectTransform.sizeDelta;
            defaultBodyPosition = body.rectTransform.anchoredPosition;
            defaultBodySize = body.rectTransform.sizeDelta;
        }

        private static readonly Color Neutral = new Color32(232, 225, 208, 255);
        private static readonly Color Warning = new Color32(255, 170, 149, 255);

        public void Show(string title, string summary, string details, string keys,
            string message = null, bool isWarning = false, bool package = false)
        {
            gameObject.SetActive(true);
            packageListBacking.SetActive(package);
            panel.sizeDelta = package ? new Vector2(defaultPanelSize.x, 314f) : defaultPanelSize;
            body.rectTransform.anchoredPosition = package ? new Vector2(0f, -150f) : defaultBodyPosition;
            body.rectTransform.sizeDelta = package ? new Vector2(580f, 78f) : defaultBodySize;
            heading.text = title;
            subtitle.text = summary;
            body.text = details;
            actions.rectTransform.sizeDelta = keys.Contains('\n')
                ? new Vector2(defaultActionsSize.x, 52f) : defaultActionsSize;
            actions.text = keys;
            feedback.text = message ?? string.Empty;
            feedback.color = isWarning ? Warning : Neutral;
        }
    }
}
