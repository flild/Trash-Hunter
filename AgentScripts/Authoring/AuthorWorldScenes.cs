using System;
using System.Linq;
using TrashHunter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AuthorWorldScenes
{
    private const string SearchPath = "Assets/TrashHunter/Scenes/ContainerSearch.unity";
    private const string HomePath = "Assets/TrashHunter/Scenes/Home.unity";
    private const string TraderPath = "Assets/TrashHunter/Scenes/Trader.unity";
    private const string StartupPath = "Assets/TrashHunter/Scenes/Startup.unity";
    private const string SearchContent = "Assets/TrashHunter/Content/Search";

    public static object Create()
    {
        var search = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || search.path != SearchPath ||
            search.isDirty || SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Open only the saved ContainerSearch scene in Edit Mode.");

        ConfigureSettlement(HomePath, RunLocation.Home);
        ConfigureSettlement(TraderPath, RunLocation.Trader);
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(StartupPath) == null)
            SaveAndClose(EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive), StartupPath);

        var world = new[] { StartupPath, SearchPath, HomePath, TraderPath };
        EditorBuildSettings.scenes = world.Select(path => new EditorBuildSettingsScene(path, true))
            .Concat(EditorBuildSettings.scenes.Where(scene => !world.Contains(scene.path))).ToArray();
        AssetDatabase.SaveAssets();
        SceneManager.SetActiveScene(search);
        return EditorBuildSettings.scenes.Select(scene => scene.path).ToArray();
    }

    private static void ConfigureSettlement(string path, RunLocation location)
    {
        var exists = AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null;
        var scene = exists ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) :
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var label = location.ToString();
        var root = exists ? scene.GetRootGameObjects().Single(obj => obj.GetComponent<SearchSceneScope>() != null) :
            new GameObject(label + " Scope");
        if (root.TryGetComponent<SettlementSession>(out _))
        {
            EditorSceneManager.CloseScene(scene, true);
            return;
        }

        var entry = root.transform.Find(label + " Entry");
        if (entry == null)
        {
            entry = new GameObject(label + " Entry").transform;
            entry.SetParent(root.transform, false);
        }

        var scope = new SerializedObject(root.GetComponent<SearchSceneScope>() ?? root.AddComponent<SearchSceneScope>());
        scope.FindProperty("location").enumValueIndex = (int)location;
        scope.FindProperty("entry").objectReferenceValue = entry;
        scope.FindProperty("session").objectReferenceValue = null;
        scope.FindProperty("settlement").objectReferenceValue = root.AddComponent<SettlementSession>();
        scope.FindProperty("playerPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(SearchContent + "/Prefabs/SearchPlayer.prefab").GetComponent<SearchPlayer>();
        scope.FindProperty("hudPrefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(SearchContent + "/Prefabs/SearchHud.prefab").GetComponent<SearchHud>();
        scope.ApplyModifiedPropertiesWithoutUndo();
        if (root.TryGetComponent<SearchSession>(out var oldSession))
            UnityEngine.Object.DestroyImmediate(oldSession);

        entry.localPosition = new Vector3(0f, 0.02f, -2.6f);
        BuildRoom(location);
        SaveAndClose(scene, path);
    }

    private static void BuildRoom(RunLocation location)
    {
        var trader = location == RunLocation.Trader;
        var room = new GameObject("Environment").transform;
        var concrete = Material("Concrete");
        var timber = Material("Weathered_Timber");
        var enamel = Material(trader ? "Container_Paint" : "CabinetEnamel");
        Box(room, "Floor", new Vector3(0, -.15f, 0), new Vector3(8, .3f, 10), concrete);
        Box(room, "Left Wall", new Vector3(-4, 1.6f, 0), new Vector3(.2f, 3.2f, 10), concrete);
        Box(room, "Right Wall", new Vector3(4, 1.6f, 0), new Vector3(.2f, 3.2f, 10), concrete);
        Box(room, "Back Wall", new Vector3(0, 1.6f, 5), new Vector3(8, 3.2f, .2f), concrete);
        Box(room, "Door", new Vector3(2.55f, 1.1f, 4.85f), new Vector3(1.25f, 2.2f, .1f), enamel);
        Box(room, "Door Handle", new Vector3(2.12f, 1.02f, 4.75f), new Vector3(.18f, .05f, .12f), Material("Dark_Metal"));

        if (trader)
        {
            Box(room, "Counter Base", new Vector3(-.85f, .45f, 1.35f), new Vector3(3, .9f, .75f), enamel);
            Box(room, "Counter Top", new Vector3(-.85f, .95f, 1.35f), new Vector3(3.2f, .1f, .95f), timber);
            Box(room, "Stock Cabinet", new Vector3(-2.8f, 1, 4.35f), new Vector3(1.5f, 2, .65f), Material("Dark_Metal"));
        }
        else
        {
            Box(room, "Storage Cabinet", new Vector3(-2.4f, .9f, 3.8f), new Vector3(2, 1.8f, .8f), enamel);
            Box(room, "Storage Top", new Vector3(-2.4f, 1.84f, 3.8f), new Vector3(2.1f, .08f, .9f), timber);
            Box(room, "Bench", new Vector3(.3f, .45f, 3.8f), new Vector3(2.35f, .9f, .85f), timber);
        }

        var light = new GameObject("Room Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = trader ? new Color(1f, .84f, .66f) : new Color(.84f, .90f, 1f);
        light.intensity = 1.25f;
        light.transform.rotation = Quaternion.Euler(43f, -25f, 0f);
    }

    private static Material Material(string name) =>
        AssetDatabase.LoadAssetAtPath<Material>($"{SearchContent}/Materials/{name}.mat") ??
        throw new InvalidOperationException($"Missing room material {name}.");

    private static void Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = position;
        box.transform.localScale = size;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static void SaveAndClose(Scene scene, string path)
    {
        if (!EditorSceneManager.SaveScene(scene, path))
            throw new InvalidOperationException($"Could not save {path}.");

        EditorSceneManager.CloseScene(scene, true);
    }
}
