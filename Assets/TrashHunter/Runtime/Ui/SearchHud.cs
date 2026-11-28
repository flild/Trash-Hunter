using UnityEngine;
using UnityEngine.UI;

namespace TrashHunter
{
    public sealed class SearchHud : MonoBehaviour
    {
        [SerializeField] private Text prompt;
        [SerializeField] private RectTransform promptBacking;
        [SerializeField] private Text nearbyAction;
        [SerializeField] private Text controls;
        [SerializeField] private Graphic reticle;
        [SerializeField] private GameObject packageReticle;
        [SerializeField] private BagView bag;
        [SerializeField] private ItemInspectionView inspection;
        [SerializeField] private ExpeditionView expedition;
        [SerializeField] private Text feedback;

        private static readonly Color IdleTint = new Color32(255, 246, 222, 255);
        private static readonly Color TargetTint = new Color32(240, 199, 118, 255);
        private bool? previousHasTarget;

        public BagView Bag => bag;
        internal ItemInspectionView Inspection => inspection;
        public ExpeditionView Expedition => expedition;
        public void ShowError(string error) => feedback.text = error ?? string.Empty;

        internal void ToggleControls() => controls.enabled = !controls.enabled;

        private void Awake()
        {
            var corner = controls.rectTransform;
            corner.anchorMin = corner.anchorMax = corner.pivot = Vector2.zero;
            corner.anchoredPosition = new Vector2(18f, 14f);
            corner.sizeDelta = new Vector2(420f, 72f);
            controls.alignment = TextAnchor.LowerLeft;
        }

        public void Show(string action, string bindings, bool showReticle, SearchItem target = null, string nearby = "")
        {
            var hasTarget = target != null;
            var targetChanged = previousHasTarget != hasTarget;
            previousHasTarget = hasTarget;
            var isPackage = hasTarget && target.IsPackage;
            if (targetChanged)
            {
                var position = hasTarget ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 0f);
                var offset = new Vector2(0f, hasTarget ? -62f : 83f);
                prompt.fontSize = hasTarget ? 17 : 21;
                promptBacking.anchoredPosition = new Vector2(0f, -62f);
                prompt.rectTransform.anchorMin = position;
                prompt.rectTransform.anchorMax = position;
                prompt.rectTransform.anchoredPosition = offset;
                nearbyAction.rectTransform.anchoredPosition = new Vector2(0f, hasTarget ? 83f : 126f);
                reticle.rectTransform.sizeDelta = Vector2.one * (hasTarget ? 8f : 5f);
                reticle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, hasTarget ? 45f : 0f);
                reticle.color = hasTarget ? TargetTint : IdleTint;
            }

            if (targetChanged || prompt.text != action)
            {
                prompt.text = action;
                if (hasTarget)
                    promptBacking.sizeDelta = new Vector2(
                        Mathf.Min(prompt.preferredWidth + 16f, prompt.rectTransform.rect.width), 26f);
            }

            prompt.enabled = action.Length != 0;
            promptBacking.gameObject.SetActive(hasTarget);
            nearbyAction.text = nearby;
            nearbyAction.enabled = nearby.Length != 0;
            controls.text = bindings;
            reticle.enabled = showReticle && !isPackage;
            packageReticle.SetActive(showReticle && isPackage);
        }
    }
}
