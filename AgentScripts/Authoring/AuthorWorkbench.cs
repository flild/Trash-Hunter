using System;
using System.Linq;
using TrashHunter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class AuthorWorkbench
{
    private const string SearchPath = "Assets/TrashHunter/Scenes/ContainerSearch.unity";
    private const string HomePath = "Assets/TrashHunter/Scenes/Home.unity";
    private const string Materials = "Assets/TrashHunter/Content/Search/Materials";

    public static object Place()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s => s.isDirty))
            throw new InvalidOperationException("Workbench placement requires saved scenes in Edit Mode.");

        var original = SceneManager.GetActiveScene();
        var search = Open(SearchPath, out var closeSearch);
        var home = Open(HomePath, out var closeHome);
        try
        {
            var session = home.GetRootGameObjects().Select(root => root.GetComponent<SettlementSession>()).Single(x => x != null);
            var sessionData = new SerializedObject(session);
            var field = sessionData.FindProperty("workbench");
            if (field.objectReferenceValue != null)
                throw new InvalidOperationException("The workbench is already configured.");

            var bench = home.GetRootGameObjects().Single(root => root.name == "Environment").transform.Find("Bench");
            var surface = bench.GetComponent<Collider>();
            var items = search.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SearchItem>(true))
                .ToDictionary(item => item.Id);
            var intact = items["lamp-intact"];
            var cracked = items["lamp-cracked"];
            var rp07 = items["turntable-rp07"];
            var enamel = AssetDatabase.LoadAssetAtPath<Material>($"{Materials}/Lamp_Enamel.mat");
            var paint = AssetDatabase.LoadAssetAtPath<Material>($"{Materials}/Safety_Paint.mat");
            if (surface == null || enamel == null || paint == null)
                throw new InvalidOperationException("Missing bench surface or authored lamp materials.");

            SceneManager.SetActiveScene(home);
            var station = new GameObject("Workbench Display");
            station.transform.SetParent(bench.parent, false);
            var top = surface.bounds;
            station.transform.position = new Vector3(top.center.x, top.max.y, top.center.z);
            var component = station.AddComponent<Workbench>();

            var cameraPoint = new GameObject("Workbench View").transform;
            cameraPoint.SetParent(station.transform, false);
            cameraPoint.localPosition = new Vector3(.10f, .61f, -1.18f);
            var aim = station.transform.position + new Vector3(0f, .13f, 0f);
            cameraPoint.rotation = Quaternion.LookRotation(aim - cameraPoint.position);
            var light = new GameObject("Workbench Light").AddComponent<Light>();
            light.transform.SetParent(station.transform, false);
            light.transform.localPosition = new Vector3(-.5f, 1.4f, -.5f);
            light.transform.rotation = Quaternion.LookRotation(aim - light.transform.position);
            light.type = LightType.Spot;
            light.range = 2.6f;
            light.spotAngle = 60;
            light.intensity = 2.4f;
            light.color = new Color(1f, .87f, .69f);

            var data = new SerializedObject(component);
            data.FindProperty("surface").objectReferenceValue = surface;
            data.FindProperty("viewingPoint").objectReferenceValue = cameraPoint;
            data.FindProperty("lampEnamel").objectReferenceValue = enamel;
            data.FindProperty("safetyPaint").objectReferenceValue = paint;
            AddModel(data, station.transform, intact, "intactLamp", "intactShell", "SV-04 Intact", -12);
            AddModel(data, station.transform, cracked, "crackedLamp", "crackedShell", "SV-04 Cracked", -12);
            AddModel(data, station.transform, intact, "repairedLamp", "repairedShell", "SV-04 Repaired", -12);
            AddModel(data, station.transform, rp07, "turntable", null, "RP-07", -12);
            data.ApplyModifiedPropertiesWithoutUndo();
            field.objectReferenceValue = component;
            sessionData.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(home);
            if (!EditorSceneManager.SaveScene(home))
                throw new InvalidOperationException("Could not save the home scene.");
            return new { scene = home.path, models = 4, bench = bench.name };
        }
        finally
        {
            if (original.isLoaded)
                SceneManager.SetActiveScene(original);
            if (closeHome)
                EditorSceneManager.CloseScene(home, true);
            if (closeSearch)
                EditorSceneManager.CloseScene(search, true);
        }
    }

    private static Scene Open(string path, out bool openedHere)
    {
        var scene = SceneManager.GetSceneByPath(path);
        openedHere = !scene.isLoaded;
        return openedHere ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : scene;
    }

    private static void AddModel(SerializedObject data, Transform parent, SearchItem item, string field, string shell,
        string name, float yaw)
    {
        var model = Object.Instantiate(item.gameObject, parent, false);
        model.name = name;
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0, yaw, 0);
        model.SetActive(true);
        Object.DestroyImmediate(model.GetComponent<SearchItem>());
        Object.DestroyImmediate(model.GetComponent<Rigidbody>());
        foreach (var collider in model.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);

        var meshes = model.GetComponentsInChildren<MeshRenderer>(true);
        var lift = parent.position.y - meshes.Min(renderer => renderer.bounds.min.y);
        model.transform.position += Vector3.up * lift;
        model.SetActive(false);
        data.FindProperty(field).objectReferenceValue = model;
        if (shell == null)
            return;

        var renderers = data.FindProperty(shell);
        renderers.arraySize = 2;
        renderers.GetArrayElementAtIndex(0).objectReferenceValue = model.transform.Find("Base").GetComponent<Renderer>();
        renderers.GetArrayElementAtIndex(1).objectReferenceValue = model.transform.Find("Shade").GetComponent<Renderer>();
    }
}
