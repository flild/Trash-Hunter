using System;
using UnityEngine;

namespace TrashHunter
{
    public sealed class CounterDisplay : MonoBehaviour
    {
        [Serializable]
        private struct Model
        {
            public string key;
            public GameObject view;
            public Renderer[] lampShell;
        }

        [SerializeField] private Collider surface;
        [SerializeField] private Transform viewingPoint;
        [SerializeField] private Model[] models;
        [SerializeField] private Material lampEnamel;
        [SerializeField] private Material safetyPaint;

        private GameObject displayed;
        private Vector3 stockPosition;
        private Quaternion stockRotation;

        internal bool IsPointedAt(Camera camera, Vector2 pointer) =>
            surface.Raycast(camera.ScreenPointToRay(pointer), out _, camera.farClipPlane);

        internal void Frame(Camera camera) =>
            camera.transform.SetPositionAndRotation(viewingPoint.position, viewingPoint.rotation);

        internal void Show(HomeItem item)
        {
            Hide();
            var key = item.key == "lamp-a" ? "lamp-cracked" : item.key;
            var brokenLamp = key == "lamp-cracked";
            if (brokenLamp && item.IsRepaired)
                key = "lamp-intact";

            foreach (var model in models)
            {
                if (model.key != key)
                    continue;

                displayed = model.view;
                stockPosition = displayed.transform.localPosition;
                stockRotation = displayed.transform.localRotation;
                displayed.SetActive(true);
                if (model.lampShell.Length > 0)
                {
                    var alternate = GameState.LayoutIndex(item.expedition) == 1;
                    var enamel = brokenLamp ? alternate : !alternate;
                    Workbench.ApplyFinish(model.lampShell, enamel ? lampEnamel : safetyPaint, item.IsDirty);
                }
                break;
            }
        }

        internal void Inspect(Camera camera, string key, bool reverse)
        {
            if (displayed == null)
                return;

            var view = camera.transform;
            var rotation = reverse ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            displayed.transform.SetPositionAndRotation(view.TransformPoint(new Vector3(0f, -0.035f, 0.88f)),
                view.rotation * ItemInspectionView.EvidenceRotation(key, rotation));
        }

        internal void Hide()
        {
            if (displayed != null)
            {
                displayed.transform.SetLocalPositionAndRotation(stockPosition, stockRotation);
                displayed.SetActive(false);
            }
            displayed = null;
        }
    }
}
