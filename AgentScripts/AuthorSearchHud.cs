using System;
using TrashHunter;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class AuthorSearchHud
{
    private const string Path = "Assets/TrashHunter/Content/Search/SearchHud.prefab";
    private static readonly Color32 Accent = new(240, 199, 118, 255);

    public static object Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Use Edit Mode to update the search HUD.");

        var root = PrefabUtility.LoadPrefabContents(Path);
        try
        {
            var hud = root.GetComponent<SearchHud>();
            var prompt = root.transform.Find("Prompt").GetComponent<Text>();
            var aim = root.transform.Find("Aim").GetComponent<Image>();
            var expedition = hud.Expedition.transform.Find("ExpeditionPanel");
            var bag = hud.Bag.transform.Find("BagPanel");
            if (root.transform.Find("Package reticle") != null || prompt == null || aim == null || expedition == null || bag == null)
                throw new InvalidOperationException("Expected the I-100 search HUD hierarchy.");

            var center = new Vector2(0.5f, 0.5f);
            var backing = Rect(root.transform, "Prompt backing", center, new(0, -43), new(350, 38));
            Fill(backing, new Color32(24, 31, 30, 235));
            backing.SetSiblingIndex(prompt.transform.GetSiblingIndex());
            backing.gameObject.SetActive(false);

            var nearby = Rect(root.transform, "Nearby action", new(0.5f, 0), new(0, 83), new(780, 38));
            var nearbyText = nearby.gameObject.AddComponent<Text>();
            nearbyText.font = prompt.font;
            nearbyText.color = prompt.color;
            nearbyText.fontSize = 21;
            nearbyText.fontStyle = FontStyle.Bold;
            nearbyText.alignment = TextAnchor.MiddleCenter;
            nearbyText.raycastTarget = false;
            nearbyText.enabled = false;
            UseShadow(nearby, new(1.5f, -1.5f));

            var brackets = Rect(root.transform, "Package reticle", center, Vector2.zero, new(26, 26));
            foreach (var x in new[] { -1, 1 })
                foreach (var y in new[] { -1, 1 })
                {
                    DrawBracket(new(x * 9, y * 12), new(8, 2));
                    DrawBracket(new(x * 12, y * 9), new(2, 8));
                }
            brackets.gameObject.SetActive(false);

            var list = Rect(expedition, "Package list backing", new(0.5f, 1), new(0, -150), new(618, 104));
            Fill(list, new Color32(30, 37, 33, 255));
            var details = expedition.Find("Details").GetComponent<Text>();
            list.SetSiblingIndex(details.transform.GetSiblingIndex());
            list.gameObject.SetActive(false);

            var grid = bag.Find("BagGrid");
            foreach (Transform cell in grid)
                if (cell.name.StartsWith("Cell", StringComparison.Ordinal))
                    cell.GetComponent<Image>().color = new Color32(51, 61, 55, 255);
            bag.Find("BagGrid/BagItems/ItemTile").GetComponent<Image>().color = new Color32(76, 104, 90, 255);

            Assign(hud, "promptBacking", backing);
            Assign(hud, "nearbyAction", nearbyText);
            Assign(hud, "packageReticle", brackets.gameObject);
            Assign(hud.Expedition, "packageListBacking", list.gameObject);
            var panel = (RectTransform)expedition;
            foreach (var name in new[] { "Feedback", "Actions", "Action Divider" })
            {
                var row = panel.Find(name).GetComponent<RectTransform>();
                var fromBottom = row.anchoredPosition + Vector2.up * panel.sizeDelta.y;
                row.anchorMin = new Vector2(0.5f, 0);
                row.anchorMax = row.anchorMin;
                row.anchoredPosition = fromBottom;
            }
            Assign(hud.Expedition, "panel", panel);
            if (PrefabUtility.SaveAsPrefabAsset(root, Path) == null)
                throw new InvalidOperationException("Failed to save the search HUD.");
            return new { prefab = Path, reticle = "dot, diamond, package brackets" };

            void DrawBracket(Vector2 position, Vector2 size)
            {
                var stroke = Rect(brackets, "Stroke", center, position, size);
                Fill(stroke, Accent);
                UseShadow(stroke, new(1, -1));
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static void Fill(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private static void UseShadow(RectTransform rect, Vector2 distance)
    {
        var shadow = rect.gameObject.AddComponent<Outline>();
        shadow.effectColor = new Color32(12, 16, 16, 230);
        shadow.effectDistance = distance;
    }

    private static void Assign(Component component, string property, UnityEngine.Object value)
    {
        var data = new SerializedObject(component);
        data.FindProperty(property).objectReferenceValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }
}
