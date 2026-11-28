using UnityEngine;

namespace TrashHunter
{
    public sealed class ItemInspectionView : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Text title;
        [SerializeField] private UnityEngine.UI.Text detail;

        public void Show(string itemName, string visibleCondition, bool twoLines = false)
        {
            if (twoLines)
            {
                var panel = (RectTransform)transform;
                panel.sizeDelta = new Vector2(panel.sizeDelta.x, 108f);
                var description = detail.rectTransform;
                description.anchoredPosition = new Vector2(description.anchoredPosition.x, -67f);
                description.sizeDelta = new Vector2(description.sizeDelta.x, 54f);
            }

            title.text = itemName;
            detail.text = visibleCondition;
            detail.enabled = !string.IsNullOrEmpty(visibleCondition);
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
