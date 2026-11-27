using System;
using System.Linq;
using TrashHunter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class AuthorTraderTurntable
{
    public static object Add()
    {
        const string id = "turntable-rp07";
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty ||
            scene.path != "Assets/TrashHunter/Scenes/ContainerSearch.unity")
            throw new InvalidOperationException("Open the saved ContainerSearch scene in Edit Mode.");

        var references = new SerializedObject(Object.FindAnyObjectByType<SearchSession>());
        var items = references.FindProperty("items");
        var existing = Enumerable.Range(0, items.arraySize)
            .Select(i => (SearchItem)items.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
        if (existing.Any(item => item.Id == id))
            throw new InvalidOperationException("The turntable is already authored.");

        var wood = Finish("Weathered_Timber");
        var dark = Finish("Dark_Metal");
        var enamel = Finish("CabinetEnamel");
        var ivory = Finish("Ivory");
        var root = new GameObject("RP-07 Turntable");
        root.SetActive(false);
        root.transform.SetParent(existing.Single(item => item.Id == "radio-a").transform.parent, false);
        var item = root.AddComponent<SearchItem>();
        item.GetComponent<BoxCollider>().size = new Vector3(0.43f, 0.16f, 0.35f);
        var body = item.GetComponent<Rigidbody>();
        body.mass = 3.5f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var data = new SerializedObject(item);
        data.FindProperty("itemId").stringValue = id;
        data.FindProperty("bagLabel").stringValue = "RP-07";
        data.ApplyModifiedPropertiesWithoutUndo();

        Part("Wooden case", PrimitiveType.Cube, new(0f, -0.023f, 0f), new(0.43f, 0.11f, 0.35f), wood);
        Part("Deck", PrimitiveType.Cube, new(0f, 0.037f, 0f), new(0.415f, 0.014f, 0.335f), enamel);
        Part("Platter", PrimitiveType.Cylinder, new(-0.05f, 0.054f, 0.012f), new(0.255f, 0.01f, 0.255f), dark);
        Part("Spindle", PrimitiveType.Cylinder, new(-0.05f, 0.069f, 0.012f), new(0.014f, 0.009f, 0.014f), ivory);
        Part("Tonearm pivot", PrimitiveType.Cylinder, new(0.141f, 0.058f, 0.115f), new(0.054f, 0.014f, 0.054f), dark);
        Part("Tonearm", PrimitiveType.Cube, new(0.131f, 0.071f, 0.027f), new(0.012f, 0.012f, 0.172f), ivory);
        Part("Cartridge", PrimitiveType.Cube, new(0.131f, 0.06f, -0.064f), new(0.029f, 0.023f, 0.034f), dark);
        Part("Speed switch", PrimitiveType.Cube, new(0.123f, 0.049f, -0.123f), new(0.032f, 0.012f, 0.02f), ivory);

        AddPlate(root.transform, existing);
        items.arraySize++;
        items.GetArrayElementAtIndex(items.arraySize - 1).objectReferenceValue = item;
        references.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save ContainerSearch.");

        return new { id, items = items.arraySize };

        void Part(string label, PrimitiveType primitive, Vector3 position, Vector3 scale, Material finish)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = label;
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.GetComponent<MeshRenderer>().sharedMaterial = finish;
        }
    }

    private static void AddPlate(Transform turntable, SearchItem[] items)
    {
        var sample = items.Single(item => item.Id == "lamp-a").transform.Find("Model plate");
        if (sample == null)
            throw new InvalidOperationException("Missing existing model plate.");

        var plate = (RectTransform)Object.Instantiate(sample, turntable);
        plate.name = sample.name;
        plate.localPosition = new Vector3(0.058f, -0.027f, -0.176f);
        plate.localRotation = Quaternion.identity;
        plate.localScale = Vector3.one * 0.0006f;
        var label = plate.GetComponentInChildren<Text>(true);
        label.text = "RP-07";
        label.color = new Color32(232, 225, 208, 255);
    }

    private static Material Finish(string name) =>
        AssetDatabase.LoadAssetAtPath<Material>($"Assets/TrashHunter/Content/Search/Materials/{name}.mat") ??
        throw new InvalidOperationException($"Missing search material: {name}");
}
