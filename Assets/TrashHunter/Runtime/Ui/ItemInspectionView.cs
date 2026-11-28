using UnityEngine;

namespace TrashHunter
{
    public sealed class ItemInspectionView : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Text title;
        [SerializeField] private UnityEngine.UI.Text detail;

        public void Show(string itemName, string visibleCondition)
        {
            title.text = itemName;
            detail.text = visibleCondition;
            detail.enabled = !string.IsNullOrEmpty(visibleCondition);
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
