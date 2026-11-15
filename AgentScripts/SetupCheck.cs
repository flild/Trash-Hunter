using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Run through Unity Pipeline's script run command. Kept outside Assets so this
// setup utility is not compiled into the game.
public static class SetupCheck
{
    private const string ScenePath = "Assets/Scenes/SetupCheck.unity";

    public static object Inspect()
    {
        return new
        {
            assets = Application.dataPath,
            version = Application.unityVersion,
            playing = EditorApplication.isPlaying,
            compiling = EditorApplication.isCompiling,
            serialization = EditorSettings.serializationMode.ToString(),
            metaFiles = UnityEditor.VersionControlSettings.mode,
            scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
            {
                var scene = SceneManager.GetSceneAt(i);
                return new { scene.path, scene.isDirty, scene.isLoaded };
            }).ToArray()
        };
    }

    public static object Create()
    {
        if (!Application.dataPath.Replace('\\', '/').Equals(
                "F:/UnityProjects/Trash-Hunter/Assets", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected Unity project.");
        if (Application.unityVersion != "6000.6.9f1")
            throw new InvalidOperationException("Unexpected Unity Editor version.");
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Editor must be idle in Edit Mode.");
        for (var i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save your existing scene changes first.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            throw new InvalidOperationException("SetupCheck already exists; inspect it instead.");

        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSettings.serializationMode = SerializationMode.ForceText;
        UnityEditor.VersionControlSettings.mode = "Visible Meta Files";

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(2f, 1f, 2f);

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Cube";
        cube.transform.position = new Vector3(0f, 0.5f, 0f);

        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(5f, 4f, -7f);
        cameraObject.transform.LookAt(cube.transform.position);
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.14f, 0.18f, 0.23f);

        var lightObject = new GameObject("Directional Light", typeof(Light));
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        var light = lightObject.GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.shadows = LightShadows.Soft;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.4f, 0.4f, 0.4f);
        RenderSettings.sun = light;

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException("Failed to save SetupCheck scene.");
        AssetDatabase.SaveAssets();
        return Verify();
    }

    public static object Verify()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            throw new InvalidOperationException("SetupCheck is not the active scene.");
        var roots = scene.GetRootGameObjects();
        var floor = roots.Single(go => go.name == "Floor");
        var cube = roots.Single(go => go.name == "Cube");
        var camera = roots.Single(go => go.name == "Main Camera").GetComponent<Camera>();
        var light = roots.Single(go => go.name == "Directional Light").GetComponent<Light>();
        if (roots.Length != 4 || floor.GetComponent<Collider>() == null ||
            cube.GetComponent<Collider>() == null || camera == null ||
            camera.GetComponent<AudioListener>() == null || light == null ||
            light.type != LightType.Directional || scene.isDirty)
            throw new InvalidOperationException("Unexpected SetupCheck scene contents or unsaved changes.");
        return new
        {
            scene = scene.path,
            saved = !scene.isDirty,
            objects = roots.Select(go => go.name).ToArray(),
            playing = EditorApplication.isPlaying,
            frameCount = Time.frameCount
        };
    }
}
