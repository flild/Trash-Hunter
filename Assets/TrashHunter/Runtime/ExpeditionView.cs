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

        private static readonly Color Neutral = new Color32(232, 225, 208, 255);
        private static readonly Color Warning = new Color32(255, 170, 149, 255);

        public void Show(string title, string summary, string details, string keys, string message = null, bool isWarning = false)
        {
            gameObject.SetActive(true);
            heading.text = title;
            subtitle.text = summary;
            body.text = details;
            actions.text = keys;
            feedback.text = message ?? string.Empty;
            feedback.color = isWarning ? Warning : Neutral;
        }

        public void ShowHome(GameState state, int selection, string receipt)
        {
            const int visibleRows = 5;
            var rows = new StringBuilder();
            var first = Mathf.Clamp(selection - visibleRows / 2, 0, Mathf.Max(0, state.Home.Count - visibleRows));
            var last = Mathf.Min(first + visibleRows, state.Home.Count);
            for (var i = first; i < last; i++)
            {
                var offer = state.Quote(state.Home[i]);
                if (i == selection)
                    rows.AppendLine($"<color=#F0C776>›  {offer.Name}</color>");
                else
                    rows.AppendLine($"    {offer.Name}");
            }

            var details = state.Home.Count == 0 ? "Дома нет вещей для продажи." : rows.ToString().TrimEnd();
            var sell = state.Home.Count == 0 ? string.Empty : "↑ / ↓  Выбрать     Enter  Узнать цену     ";
            Show("Дома", $"Баланс  {state.Balance} ₽", details, sell + "E  Новая вылазка     Esc  Пауза",
                state.Error ?? receipt, state.Error != null);
        }
    }
}
