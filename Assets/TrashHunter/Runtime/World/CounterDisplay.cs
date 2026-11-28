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

        internal void Hide()
        {
            if (displayed != null)
                displayed.SetActive(false);
            displayed = null;
        }
    }
}
