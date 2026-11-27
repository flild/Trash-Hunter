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
    private const string StartupPath = "Assets/TrashHunter/Scenes/Startup.unity";
    private const string Prefabs = "Assets/TrashHunter/Content/Search";

    public static object Create()
    {
        var search = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || search.path != SearchPath || search.isDirty)
            throw new InvalidOperationException("Open the saved ContainerSearch scene in Edit Mode.");

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(HomePath) == null)
        {
            var home = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var root = new GameObject("Home Scope");
            SceneManager.MoveGameObjectToScene(root, home);
            var scope = new SerializedObject(root.AddComponent<SearchSceneScope>());
            var entry = new GameObject("Home Entry").transform;
            entry.SetParent(root.transform, false);
            scope.FindProperty("location").enumValueIndex = (int)RunLocation.Home;
            scope.FindProperty("entry").objectReferenceValue = entry;
            scope.FindProperty("session").objectReferenceValue = root.AddComponent<SearchSession>();
            scope.FindProperty("playerPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/SearchPlayer.prefab").GetComponent<SearchPlayer>();
            scope.FindProperty("hudPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/SearchHud.prefab").GetComponent<SearchHud>();
            scope.ApplyModifiedPropertiesWithoutUndo();
            SaveAndClose(home, HomePath);
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(StartupPath) == null)
            SaveAndClose(EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive), StartupPath);

        var world = new[] { StartupPath, SearchPath, HomePath };
        EditorBuildSettings.scenes = world.Select(path => new EditorBuildSettingsScene(path, true))
            .Concat(EditorBuildSettings.scenes.Where(scene => !world.Contains(scene.path))).ToArray();
        AssetDatabase.SaveAssets();
        SceneManager.SetActiveScene(search);
        return EditorBuildSettings.scenes.Select(scene => scene.path).ToArray();
    }

    private static void SaveAndClose(Scene scene, string path)
    {
        if (!EditorSceneManager.SaveScene(scene, path))
            throw new InvalidOperationException($"Could not save {path}.");

        EditorSceneManager.CloseScene(scene, true);
    }
}
