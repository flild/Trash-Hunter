using UnityEngine;

namespace TrashHunter
{
    public sealed class Workbench : MonoBehaviour
    {
        [SerializeField] private Collider surface;
        [SerializeField] private Transform viewingPoint;
        [SerializeField] private GameObject intactLamp;
        [SerializeField] private GameObject crackedLamp;
        [SerializeField] private GameObject repairedLamp;
        [SerializeField] private GameObject turntable;
        [SerializeField] private Renderer[] intactShell;
        [SerializeField] private Renderer[] crackedShell;
        [SerializeField] private Renderer[] repairedShell;
        [SerializeField] private Material safetyPaint;
        [SerializeField] private Material lampEnamel;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color DustTint = new(0.41f, 0.32f, 0.22f);
        private GameObject displayed;
        private Transform cameraPose;
        private Vector3 roomPosition;
        private Quaternion roomRotation;

        internal bool IsPointedAt(Camera camera, Vector2 position) =>
            surface.Raycast(camera.ScreenPointToRay(position), out _, camera.farClipPlane);

        internal void FrameStock(Camera camera)
        {
            var position = transform.TransformPoint(new Vector3(0f, 0.86f, -3.75f));
            var target = transform.TransformPoint(new Vector3(0f, -0.9f, 0f));
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
        }

        internal void Focus(Camera camera)
        {
            cameraPose = camera.transform;
            roomPosition = cameraPose.localPosition;
            roomRotation = cameraPose.localRotation;
            cameraPose.SetPositionAndRotation(viewingPoint.position, viewingPoint.rotation);
        }

        internal void Show(HomeItem item)
        {
            var model = item.key switch
            {
                "lamp-intact" => intactLamp,
                "lamp-cracked" or "lamp-a" => item.IsRepaired ? repairedLamp : crackedLamp,
                "turntable-rp07" => turntable,
                _ => null
            };
            if (displayed != model)
            {
                if (displayed != null)
                    displayed.SetActive(false);
                displayed = model;
                if (displayed != null)
                    displayed.SetActive(true);
            }

            var alternate = GameState.LayoutIndex(item.expedition) == 1;
            if (model == intactLamp)
                ApplyFinish(intactShell, alternate ? safetyPaint : lampEnamel, item.IsDirty);
            else if (model == repairedLamp || model == crackedLamp)
                ApplyFinish(item.IsRepaired ? repairedShell : crackedShell,
                    alternate ? lampEnamel : safetyPaint, false);
        }

        private static void ApplyFinish(Renderer[] renderers, Material finish, bool dirty)
        {
            var properties = new MaterialPropertyBlock();
            if (dirty)
                properties.SetColor(BaseColor, Color.Lerp(finish.GetColor(BaseColor), DustTint, 0.72f));
            foreach (var renderer in renderers)
            {
                renderer.sharedMaterial = finish;
                renderer.SetPropertyBlock(properties);
            }
        }

        internal void Close()
        {
            if (displayed != null)
            {
                displayed.SetActive(false);
                displayed = null;
            }

            if (cameraPose != null)
            {
                cameraPose.SetLocalPositionAndRotation(roomPosition, roomRotation);
                cameraPose = null;
            }
        }
    }
}
