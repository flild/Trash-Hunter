using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using TrashHunter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class AuthorTradeCounter
{
    private const string Search = "Assets/TrashHunter/Scenes/ContainerSearch.unity";
    private const string Trader = "Assets/TrashHunter/Scenes/Trader.unity";
    private const string Hud = "Assets/TrashHunter/Content/Search/Prefabs/SearchHud.prefab";
    private const string Materials = "Assets/TrashHunter/Content/Search/Materials";

    public static object PlaceCounter()
    {
        RequireEditMode();
        var original = SceneManager.GetActiveScene();
        var search = Open(Search, out var closeSearch);
        var trader = Open(Trader, out var closeTrader);
        try
        {
            var room = trader.GetRootGameObjects().Single(root => root.name == "Environment").transform;
            var surface = room.Find("Counter Top").GetComponent<Collider>();
            var session = trader.GetRootGameObjects().Select(root => root.GetComponent<SettlementSession>())
                .Single(x => x != null);
            var sessionData = new SerializedObject(session);
            var counterField = sessionData.FindProperty("counter");
            if (counterField.objectReferenceValue != null)
                throw new InvalidOperationException("The counter is already configured.");

            var sources = search.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SearchItem>(true))
                .ToDictionary(item => item.Id);
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/TrashHunter/Resources/LootCatalog.json");
            var items = JObject.Parse(asset.text)["items"]
                .Where(entry => (int)entry["baseValue"] > (int)entry["conditionDiscount"])
                .Select(entry => (string)entry["id"])
                .Where(id => id != "lamp-a")
                .Select(id => sources[id]).ToArray();
            var enamel = AssetDatabase.LoadAssetAtPath<Material>($"{Materials}/Lamp_Enamel.mat");
            var paint = AssetDatabase.LoadAssetAtPath<Material>($"{Materials}/Safety_Paint.mat");
            if (surface == null || enamel == null || paint == null)
                throw new InvalidOperationException("Missing counter surface or lamp materials.");

            SceneManager.SetActiveScene(trader);
            var display = new GameObject("Counter Display");
            display.transform.SetParent(room, false);
            var top = surface.bounds;
            display.transform.position = new Vector3(top.center.x, top.max.y, top.center.z - .14f);
            var view = new GameObject("Counter View").transform;
            view.SetParent(display.transform, false);
            view.localPosition = new Vector3(.15f, .77f, -1.55f);
            var aim = display.transform.position - Vector3.up * .33f;
            view.rotation = Quaternion.LookRotation(aim - view.position);
            var light = new GameObject("Counter Light").AddComponent<Light>();
            light.transform.SetParent(display.transform, false);
            light.transform.localPosition = new Vector3(-.45f, 1.35f, -.55f);
            light.transform.rotation = Quaternion.LookRotation(display.transform.position - light.transform.position);
            light.type = LightType.Spot;
            light.range = 2.7f;
            light.spotAngle = 62;
            light.intensity = 2.4f;
            light.color = new Color(1, .88f, .73f);

            var component = display.AddComponent<CounterDisplay>();
            var data = new SerializedObject(component);
            data.FindProperty("surface").objectReferenceValue = surface;
            data.FindProperty("viewingPoint").objectReferenceValue = view;
            data.FindProperty("lampEnamel").objectReferenceValue = enamel;
            data.FindProperty("safetyPaint").objectReferenceValue = paint;
            var models = data.FindProperty("models");
            models.arraySize = items.Length;
            for (var i = 0; i < items.Length; i++)
                SetModel(models.GetArrayElementAtIndex(i), display.transform, items[i]);
            data.ApplyModifiedPropertiesWithoutUndo();
            counterField.objectReferenceValue = component;
            sessionData.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(trader);
            if (!EditorSceneManager.SaveScene(trader))
                throw new InvalidOperationException("Could not save the trader scene.");
            return new { scene = trader.path, models = items.Length };
        }
        finally
        {
            if (original.isLoaded)
                SceneManager.SetActiveScene(original);
            if (closeTrader)
                EditorSceneManager.CloseScene(trader, true);
            if (closeSearch)
                EditorSceneManager.CloseScene(search, true);
        }
    }

    public static object AddBackpackRow()
    {
        RequireEditMode();
        var root = PrefabUtility.LoadPrefabContents(Hud);
        try
        {
            var bag = root.GetComponentInChildren<BagView>(true);
            var data = new SerializedObject(bag);
            var grid = (RectTransform)data.FindProperty("grid").objectReferenceValue;
            var row = data.FindProperty("backpackRow");
            if (row.arraySize != 0)
                throw new InvalidOperationException("The backpack row is already configured.");

            row.arraySize = BagInventory.Columns + 1;
            for (var col = 0; col < BagInventory.Columns; col++)
            {
                var source = (RectTransform)grid.Find($"Cell{col}-1");
                var cell = DuplicateRowElement(source, grid, $"Cell{col}-2");
                row.GetArrayElementAtIndex(col).objectReferenceValue = cell;
                if (col == 0)
                    continue;

                var column = (RectTransform)grid.Find($"ColumnGuide{col}");
                column.anchorMin = new Vector2(0f, 0f);
                column.anchorMax = new Vector2(0f, 1f);
                column.anchoredPosition = new Vector2(column.anchoredPosition.x, 0f);
                column.sizeDelta = new Vector2(column.sizeDelta.x, 0f);
            }

            var guide = DuplicateRowElement((RectTransform)grid.Find("RowGuide1"), grid, "RowGuide2");
            row.GetArrayElementAtIndex(BagInventory.Columns).objectReferenceValue = guide;
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, Hud, out var saved);
            if (!saved)
                throw new InvalidOperationException("Could not save the HUD.");
            return new { prefab = Hud, backpackCells = BagInventory.Columns };
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static GameObject DuplicateRowElement(RectTransform source, RectTransform grid, string name)
    {
        var copy = Object.Instantiate(source, grid, false);
        copy.name = name;
        copy.anchoredPosition = source.anchoredPosition - Vector2.up * BagView.CellSize;
        copy.SetSiblingIndex(source.GetSiblingIndex() + 1);
        copy.gameObject.SetActive(false);
        return copy.gameObject;
    }

    private static void SetModel(SerializedProperty element, Transform parent, SearchItem source)
    {
        var model = Object.Instantiate(source.gameObject, parent, false);
        model.name = source.Id;
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, -12f, 0f);
        model.SetActive(true);
        Object.DestroyImmediate(model.GetComponent<SearchItem>());
        Object.DestroyImmediate(model.GetComponent<Rigidbody>());
        foreach (var collider in model.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);
        var lowestPoint = model.GetComponentsInChildren<MeshRenderer>().Min(renderer => renderer.bounds.min.y);
        model.transform.position += Vector3.up * (parent.position.y - lowestPoint);
        model.SetActive(false);

        element.FindPropertyRelative("key").stringValue = source.Id;
        element.FindPropertyRelative("view").objectReferenceValue = model;
        if (source.Id is "lamp-intact" or "lamp-cracked")
        {
            var shell = element.FindPropertyRelative("lampShell");
            shell.arraySize = 2;
            shell.GetArrayElementAtIndex(0).objectReferenceValue = model.transform.Find("Base").GetComponent<Renderer>();
            shell.GetArrayElementAtIndex(1).objectReferenceValue = model.transform.Find("Shade").GetComponent<Renderer>();
        }
    }

    private static Scene Open(string path, out bool openedHere)
    {
        var scene = SceneManager.GetSceneByPath(path);
        openedHere = !scene.isLoaded;
        return openedHere ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : scene;
    }

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s => s.isDirty))
            throw new InvalidOperationException("Authoring requires saved scenes in Edit Mode.");
    }
}
