using System.Text;
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

        private void Awake()
        {
            defaultPanelSize = panel.sizeDelta;
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
            actions.text = keys;
            feedback.text = message ?? string.Empty;
            feedback.color = isWarning ? Warning : Neutral;
        }

        internal void ShowHome(GameState state, int selection, string receipt, UiStrings strings, string error = null)
        {
            const int visibleRows = 5;
            var rows = new StringBuilder();
            var first = Mathf.Clamp(selection - visibleRows / 2, 0, Mathf.Max(0, state.Home.Count - visibleRows));
            var last = Mathf.Min(first + visibleRows, state.Home.Count);
            for (var i = first; i < last; i++)
            {
                var offer = state.Quote(state.Home[i]);
                var name = strings.ItemName(offer.Item.key, offer.Name);
                if (i == selection)
                    rows.AppendLine($"<color=#F0C776>›  {name}</color>");
                else
                    rows.AppendLine($"    {name}");
            }

            var details = state.Home.Count == 0 ? strings.Get("home.empty") : rows.ToString().TrimEnd();
            var keys = strings.Get("home.actions");
            if (state.Home.Count > 0)
                keys = $"{strings.Get("home.offer")}     {keys}";
            error ??= state.Error;
            Show(strings.Get("home.title"), string.Format(strings.Get("home.balance"), state.Balance), details, keys,
                error ?? receipt, error != null);
        }
    }
}
