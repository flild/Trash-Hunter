using System;
using System.Linq;
using TrashHunter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class AuthorChapterEvidence
{
    private const string First = "Assets/TrashHunter/Scenes/ContainerSearch.unity";
    private const string Second = "Assets/TrashHunter/Scenes/SecondYard.unity";
    private const string Trader = "Assets/TrashHunter/Scenes/Trader.unity";

    public static object Add()
    {
        var original = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || original.path != First || original.isDirty ||
            SceneManager.sceneCount != 1 || AssetDatabase.LoadAssetAtPath<SceneAsset>(Second) != null)
            throw new InvalidOperationException("Open only the saved first yard in Edit Mode.");

        if (!AssetDatabase.CopyAsset(First, Second))
            throw new InvalidOperationException("Could not create the workshop yard.");

        var second = EditorSceneManager.OpenScene(Second, OpenSceneMode.Additive);
        var trader = EditorSceneManager.OpenScene(Trader, OpenSceneMode.Additive);
        try
        {
            var tag = AuthorTag(original);
            var recorder = AuthorRecorder(second);
            PlaceEvidence(tag);
            PlaceEvidence(recorder);
            ArrangeWorkshopYard(second);
            AddCounterModels(trader, tag, recorder);
            foreach (var scene in new[] { original, second, trader })
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException($"Could not save {scene.path}.");
            }
            EditorBuildSettings.scenes = EditorBuildSettings.scenes
                .Append(new EditorBuildSettingsScene(Second, true)).ToArray();
            AssetDatabase.SaveAssets();
            return new { firstYard = tag.Id, workshopYard = recorder.Id, buildScene = Second };
        }
        finally
        {
            SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(trader, true);
            EditorSceneManager.CloseScene(second, true);
        }
    }

    private static SearchItem AuthorTag(Scene scene)
    {
        var (item, sample) = AddItem(scene, "House Tag", "home-tag", "TAG", new(.24f, .034f, .16f), .28f);
        Part(item.transform, "Enamel plate", Vector3.zero, new(.24f, .024f, .16f), Finish("Ivory"));
        Part(item.transform, "Bent edge", new(-.107f, -.005f, 0), new(.024f, .032f, .16f), Finish("Oxide"));
        var ink = new Color32(37, 32, 25, 255);
        Label(sample, item.transform, "Address", new(.006f, .0125f, 0), Quaternion.Euler(90, 0, 0),
            "ТИХИЙ ПЕР. 6\nпод лестницей", .00048f, ink);
        Label(sample, item.transform, "Batch stamp", new(.006f, -.0125f, 0), Quaternion.Euler(-90, 0, 0),
            "ПАРТИЯ 17-Б", .00048f, ink);
        return item;
    }

    private static SearchItem AuthorRecorder(Scene scene)
    {
        var (item, sample) = AddItem(scene, "Damaged Cassette Recorder", "batch-recorder", "17-Б",
            new(.35f, .19f, .15f), 1.8f);
        var dark = Finish("Dark_Metal");
        var enamel = Finish("CabinetEnamel");
        Part(item.transform, "Housing", Vector3.zero, new(.35f, .15f, .127f), dark);
        Part(item.transform, "Cassette door", new(-.039f, -.011f, -.066f), new(.163f, .106f, .021f), enamel)
            .localRotation = Quaternion.Euler(0, -7, 6);
        Part(item.transform, "Cassette window", new(-.036f, .005f, -.081f), new(.115f, .026f, .006f), dark)
            .localRotation = Quaternion.Euler(0, -7, 6);
        var ivory = Finish("Ivory");
        foreach (var reel in new[] { -.072f, .006f })
            Part(item.transform, "Reel behind window", new(reel, .005f, -.085f), new(.016f, .016f, .004f), ivory);
        for (var slot = 0; slot < 4; slot++)
            Part(item.transform, "Speaker slot", new(.084f + slot * .016f, -.022f, -.065f),
                new(.005f, .052f, .008f), enamel);
        Part(item.transform, "Transport keys", new(0, .082f, -.019f), new(.125f, .018f, .036f), enamel);
        Label(sample, item.transform, "Batch stamp", new(.086f, .040f, -.065f), Quaternion.identity,
            "17-Б", .00028f, new Color32(236, 219, 186, 255));
        return item;
    }

    private static void ArrangeWorkshopYard(Scene scene)
    {
        SceneManager.SetActiveScene(scene);
        var yard = Find(scene, "Service Yard");
        yard.name = "Workshop Yard";
        var dark = Finish("Dark_Metal");
        var enamel = Finish("CabinetEnamel");
        var wood = Finish("Weathered_Timber");
        var entry = new GameObject("Workshop Gate Entry").transform;
        entry.SetParent(yard, false);
        entry.SetPositionAndRotation(new Vector3(3.5f, 0f, -4.9f), Quaternion.Euler(0, -20, 0));
        var concrete = yard.Find("Concrete rear wall").GetComponent<MeshRenderer>().sharedMaterial;
        Part(yard, "Workshop wall", new(4.98f, 1.55f, 1.85f), new(.36f, 3.1f, 10.7f), concrete, solid: true);
        Part(yard, "Service shutter", new(4.77f, 1.32f, .75f), new(.12f, 2.48f, 2.45f), dark);
        for (var strip = 0; strip < 8; strip++)
            Part(yard, "Shutter rib", new(4.69f, .37f + strip * .28f, .75f), new(.06f, .045f, 2.39f), enamel);
        Part(yard, "Gate pier", new(2.0f, 1.10f, -4.25f), new(.37f, 2.2f, .43f), concrete, solid: true);
        Part(yard, "Gate crossbar", new(3.5f, 2.33f, -4.25f), new(3.35f, .24f, .34f), dark, solid: true);
        Part(yard, "Stack at gate", new(2.3f, .53f, -2.2f), new(.92f, 1.06f, 1.85f), wood, solid: true);
        var exit = Find(scene, "Yard Exit");
        exit.position = new Vector3(3.5f, 0f, -6.15f);
        var session = Component<SearchSession>(scene);
        var data = new SerializedObject(session);
        data.FindProperty("yardEntry").objectReferenceValue = entry;
        var items = data.FindProperty("items");
        var firstOnly = new[] { "lamp-intact", "lamp-cracked", "radio-c", "turntable-rp07" };
        for (var i = items.arraySize - 1; i >= 0; i--)
        {
            var element = items.GetArrayElementAtIndex(i);
            var item = (SearchItem)element.objectReferenceValue;
            if (!firstOnly.Contains(item.Id))
                continue;
            element.objectReferenceValue = null;
            items.DeleteArrayElementAtIndex(i);
            Object.DestroyImmediate(item.gameObject);
        }
        data.ApplyModifiedPropertiesWithoutUndo();

        var container = Find(scene, "Roll-off Container");
        container.name = "Workshop Skip";
        foreach (var part in container.GetComponentsInChildren<MeshRenderer>())
            if (part.gameObject.name is "Left wall" or "Right wall" or "Back wall" or "Access rim" or "Wall rib")
                part.sharedMaterial = enamel;
        Find(scene, "Fixed Scrap").Find("Pallet under lamp").localPosition += new Vector3(-.48f, 0, -.40f);
    }

    private static void PlaceEvidence(SearchItem item)
    {
        var tag = item.Id == "home-tag";
        item.transform.SetPositionAndRotation(tag ? new Vector3(-1.35f, .25f, 1.18f) : new Vector3(-1.38f, .318f, 1.68f),
            Quaternion.Euler(0, tag ? 24 : -32, 0));
    }

    private static (SearchItem, RectTransform) AddItem(Scene scene, string name, string id, string bag, Vector3 size,
        float mass)
    {
        SceneManager.SetActiveScene(scene);
        var session = new SerializedObject(Component<SearchSession>(scene));
        var items = session.FindProperty("items");
        var models = Enumerable.Range(0, items.arraySize)
            .Select(i => (SearchItem)items.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
        var sign = (RectTransform)models.Single(item => item.Id == "lamp-a").transform.Find("Model plate");
        var root = new GameObject(name);
        root.SetActive(false);
        root.transform.SetParent(sign.parent.parent, false);
        var item = root.AddComponent<SearchItem>();
        item.GetComponent<BoxCollider>().size = size;
        var body = item.GetComponent<Rigidbody>();
        body.mass = mass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var data = new SerializedObject(item);
        data.FindProperty("itemId").stringValue = id;
        data.FindProperty("bagLabel").stringValue = bag;
        data.ApplyModifiedPropertiesWithoutUndo();
        items.arraySize++;
        items.GetArrayElementAtIndex(items.arraySize - 1).objectReferenceValue = item;
        session.ApplyModifiedPropertiesWithoutUndo();
        return (item, sign);
    }

    private static void Label(RectTransform sample, Transform parent, string name, Vector3 position, Quaternion rotation,
        string text, float scale, Color color)
    {
        var plate = Object.Instantiate(sample, parent, false);
        plate.name = name;
        plate.localPosition = position;
        plate.localRotation = rotation;
        plate.localScale = Vector3.one * scale;
        plate.sizeDelta = new Vector2(440, 245);
        var label = plate.GetComponentInChildren<Text>();
        label.rectTransform.sizeDelta = plate.sizeDelta;
        label.text = text;
        label.fontSize = 52;
        label.color = color;
    }

    private static void AddCounterModels(Scene scene, params SearchItem[] sources)
    {
        SceneManager.SetActiveScene(scene);
        var counter = Component<CounterDisplay>(scene);
        var data = new SerializedObject(counter);
        var models = data.FindProperty("models");
        foreach (var item in sources)
        {
            var model = Object.Instantiate(item.gameObject, counter.transform, false);
            model.name = item.Id;
            var tag = item.Id == "home-tag";
            model.transform.localPosition = new Vector3(0, tag ? .017f : .096f, 0);
            model.transform.localRotation = tag ? Quaternion.identity : Quaternion.Euler(0f, -12f, 0f);
            Object.DestroyImmediate(model.GetComponent<SearchItem>());
            Object.DestroyImmediate(model.GetComponent<Rigidbody>());
            Object.DestroyImmediate(model.GetComponent<BoxCollider>());
            models.arraySize++;
            var entry = models.GetArrayElementAtIndex(models.arraySize - 1);
            entry.FindPropertyRelative("key").stringValue = item.Id;
            entry.FindPropertyRelative("view").objectReferenceValue = model;
            entry.FindPropertyRelative("lampShell").arraySize = 0;
        }
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Transform Part(Transform parent, string name, Vector3 position, Vector3 scale, Material finish,
        bool solid = false)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        if (!solid)
            Object.DestroyImmediate(part.GetComponent<Collider>());
        part.GetComponent<MeshRenderer>().sharedMaterial = finish;
        return part.transform;
    }

    private static Transform Find(Scene scene, string name) =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Single(transform => transform.name == name);

    private static T Component<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();

    private static Material Finish(string name) =>
        AssetDatabase.LoadAssetAtPath<Material>($"Assets/TrashHunter/Content/Search/Materials/{name}.mat") ??
        throw new InvalidOperationException($"Missing search material: {name}");
}
