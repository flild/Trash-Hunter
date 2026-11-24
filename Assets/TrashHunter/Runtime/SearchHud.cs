using UnityEngine;
using UnityEngine.UI;

namespace TrashHunter
{
    public sealed class SearchHud : MonoBehaviour
    {
        [SerializeField] private Text prompt;
        [SerializeField] private Text controls;
        [SerializeField] private Graphic reticle;
        [SerializeField] private BagView bag;

        public BagView Bag => bag;

        public void Show(string action, string bindings, bool showReticle)
        {
            prompt.text = action;
            prompt.enabled = action.Length != 0;
            controls.text = bindings;
            reticle.enabled = showReticle;
        }
    }
}
