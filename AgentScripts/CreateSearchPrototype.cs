using System;
using System.Linq;
using TrashHunter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class CreateSearchPrototype
{
    private const string Root = "Assets/TrashHunter";
    private const string Content = Root + "/Content/Search";
    private const string ScenePath = Root + "/Scenes/ContainerSearch.unity";

    public static object Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Leave the Editor idle with all open scenes saved before creating the prototype.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            throw new InvalidOperationException("ContainerSearch already exists. Open and edit the saved scene.");

        EnsureFolder(Root + "/Scenes");
        EnsureFolder(Content);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (!font.HasCharacter('З'))
            throw new InvalidOperationException("The prototype font must support Cyrillic.");
        var player = CreatePlayer();
        var hud = CreateHud(font);
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        var asphalt = Material("Asphalt", 0x585952);
        var concrete = Material("Concrete", 0x9b9280);
        var green = Material("Container Paint", 0x3c6059);
        var dark = Material("Dark Metal", 0x313a37);
        var yellow = Material("Safety Paint", 0xd0a34f);
        var wood = Material("Weathered Timber", 0x886243);
        var lampPaint = Material("Lamp Enamel", 0x627e83);
        var cream = Material("Ivory", 0xf3dfad);
        var red = Material("Oxide", 0x914634);

        var yard = new GameObject("Service Yard").transform;
        Box(yard, "Asphalt", new(0, -0.18f, 2), new(17, 0.35f, 17), asphalt, true);
        Box(yard, "Concrete rear wall", new(0, 1.45f, 7.5f), new(17, 3, 0.4f), concrete, true);
        Box(yard, "Concrete side wall", new(-5.5f, 1.05f, 2.5f), new(0.4f, 2.2f, 10), concrete, true);
        Box(yard, "Drain", new(-3.35f, 0.005f, 2.7f), new(0.28f, 0.018f, 5.9f), dark);
        Box(yard, "Safety line", new(0, 0.005f, -1.85f), new(4.65f, 0.02f, 0.065f), yellow);

        var container = new GameObject("Roll-off Container").transform;
        Box(container, "Floor", new(0, 0.08f, 2.6f), new(4.45f, 0.25f, 5.65f), dark, true);
        Box(container, "Left wall", new(-2.19f, 0.82f, 2.6f), new(0.2f, 1.42f, 5.65f), green, true);
        Box(container, "Right wall", new(2.19f, 0.82f, 2.6f), new(0.2f, 1.42f, 5.65f), green, true);
        Box(container, "Back wall", new(0, 0.82f, 5.31f), new(4.45f, 1.42f, 0.2f), green, true);
        Box(container, "Access rim", new(0, 0.62f, -0.15f), new(4.45f, 1.15f, 0.23f), green, true);
        Box(container, "Front lip", new(0, 1.225f, -0.15f), new(4.5f, 0.11f, 0.32f), dark);
        Box(container, "Access marking", new(0, 1.285f, -0.15f), new(1.1f, 0.025f, 0.23f), yellow);
        Box(container, "Exterior step", new(0, 0.32f, -0.45f), new(1.06f, 0.13f, 0.45f), dark);
        Box(container, "Interior step", new(0, 0.51f, 0.09f), new(1.06f, 0.13f, 0.26f), dark);
        Box(container, "Access stripe outside", new(0, 0.003f, -1.02f), new(1.1f, 0.02f, 0.08f), yellow);
        Box(container, "Access stripe inside", new(0, 0.21f, 0.79f), new(1.1f, 0.02f, 0.08f), yellow);
        foreach (var side in new[] { -1f, 1f })
        {
            Box(container, "Side lip", new(side * 2.19f, 1.53f, 2.6f), new(0.32f, 0.12f, 5.65f), dark);
            for (var rib = 0; rib < 6; rib++)
                Box(container, "Wall rib", new(side * 2.28f, 0.82f, 0.2f + rib), new(0.19f, 1.34f, 0.16f), green);
        }

        var scrap = new GameObject("Fixed Scrap").transform;
        Box(scrap, "Boards on left", new(-1.45f, 0.28f, 3.17f), new(0.66f, 0.15f, 2.42f), wood, true,
            Quaternion.Euler(0, -7, 0));
        Box(scrap, "Folded sheet", new(-1.43f, 0.53f, 4.63f), new(0.82f, 0.07f, 1.34f), lampPaint, true,
            Quaternion.Euler(-15, 15, -11));
        Box(scrap, "Pallet under lamp", new(0.12f, 0.27f, 3.83f), new(1.45f, 0.13f, 0.82f), wood, true);
        Box(scrap, "Rubber mat", new(1.28f, 0.212f, 2.48f), new(0.95f, 0.026f, 1.42f), asphalt, true);
        Box(scrap, "Sheet metal against back", new(1.42f, 0.53f, 4.85f), new(0.75f, 0.08f, 0.87f), dark, true,
            Quaternion.Euler(-25, -14, 0));

        var crate = Item("Wooden crate", "crate-a", "Деревянный ящик", new(0.02f, 0.64f, 3.05f), new(0.83f, 0.53f, 0.56f));
        Box(crate.transform, "Crate shell", Vector3.zero, new(0.81f, 0.51f, 0.54f), wood);
        foreach (var side in new[] { -1f, 1f })
        {
            Box(crate.transform, "Frame Batten", new(side * 0.315f, 0, -0.274f), new(0.1f, 0.52f, 0.038f), dark);
            Box(crate.transform, "Top slat", new(0, 0.26f, side * 0.155f), new(0.81f, 0.022f, 0.17f), wood);
        }
        Box(crate.transform, "Crate handle", new(0, 0.105f, -0.28f), new(0.22f, 0.053f, 0.021f), dark);

        var lamp = Item("Folding lamp", "lamp-a", "Складная лампа", new(0.22f, 0.58f, 3.79f), new(0.62f, 0.32f, 0.34f));
        Box(lamp.transform, "Base", new(0, -0.10f, 0), new(0.61f, 0.11f, 0.33f), lampPaint);
        Box(lamp.transform, "Folded arm", new(-0.105f, 0.024f, 0.005f), new(0.35f, 0.061f, 0.087f),
            dark, rotation: Quaternion.Euler(0, 0, 17));
        Box(lamp.transform, "Shade", new(0.15f, 0.071f, 0.0f), new(0.29f, 0.148f, 0.294f), lampPaint);
        Box(lamp.transform, "Diffuser", new(0.15f, 0.071f, -0.152f), new(0.25f, 0.107f, 0.019f), cream);
        Box(lamp.transform, "Plate", new(0.025f, -0.09f, 0.167f), new(0.245f, 0.085f, 0.011f), cream);
        Marking(lamp.transform, font);
        Box(lamp.transform, "Hinge upper fragment", new(-0.21f, 0.077f, 0.126f), new(0.085f, 0.041f, 0.083f), red,
            rotation: Quaternion.Euler(0, 0, -25));
        Box(lamp.transform, "Hinge lower fragment", new(-0.255f, 0.01f, 0.12f), new(0.037f, 0.065f, 0.091f), red,
            rotation: Quaternion.Euler(0, 0, 17));
        Box(lamp.transform, "Exposed hinge pin", new(-0.205f, 0.034f, 0.12f), new(0.025f, 0.028f, 0.099f), cream);

        var radio = Item("Pocket radio", "radio-a", "Карманный радиоприёмник", new(1.26f, 0.4f, 3.53f), new(0.43f, 0.23f, 0.25f));
        Box(radio.transform, "Housing", Vector3.zero, new(0.42f, 0.225f, 0.24f), red);
        Box(radio.transform, "Speaker grille", new(-0.06f, 0, -0.123f), new(0.19f, 0.15f, 0.01f), dark);
        Box(radio.transform, "Dial", new(0.12f, 0.03f, -0.123f), new(0.09f, 0.045f, 0.015f), cream);

        var outside = Marker("Climb from yard", new(0, 0, -1.22f), new(7, 0, 0));
        var inside = Marker("Climb from container", new(0, 0.22f, 1.08f), new(28, 0, 0));
        var root = new GameObject("Search Scope");
        var session = root.AddComponent<SearchSession>();
        var scope = root.AddComponent<SearchSceneScope>();
        Assign(session, "outside", outside);
        Assign(session, "inside", inside);
        var serialized = new SerializedObject(session);
        var items = serialized.FindProperty("items");
        items.arraySize = 3;
        items.GetArrayElementAtIndex(0).objectReferenceValue = crate;
        items.GetArrayElementAtIndex(1).objectReferenceValue = lamp;
        items.GetArrayElementAtIndex(2).objectReferenceValue = radio;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Assign(scope, "session", session);
        Assign(scope, "playerPrefab", player);
        Assign(scope, "hudPrefab", hud);

        var light = new GameObject("Overcast daylight").AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = ColorFromHex(0xfff0d8);
        light.intensity = 1.55f;
        light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(54, -29, 0);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = ColorFromHex(0x949a91);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException("Failed to save ContainerSearch.");
        var scenes = EditorBuildSettings.scenes;
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(scenes).ToArray();
        AssetDatabase.SaveAssets();
        return new { ScenePath, Player = AssetDatabase.GetAssetPath(player), Hud = AssetDatabase.GetAssetPath(hud) };
    }

    private static SearchPlayer CreatePlayer()
    {
        var root = new GameObject("Search Player") { layer = 2 };
        var player = root.AddComponent<SearchPlayer>();
        var controller = root.GetComponent<CharacterController>();
        controller.height = 1.68f;
        controller.radius = 0.27f;
        controller.center = new Vector3(0, 0.84f, 0);
        controller.stepOffset = 0.23f;
        var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.SetParent(root.transform, false);
        camera.transform.localPosition = new Vector3(0, 1.56f, 0);
        camera.nearClipPlane = 0.04f;
        camera.farClipPlane = 70;
        camera.fieldOfView = 66;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = ColorFromHex(0xb7bcb6);
        Assign(player, "viewCamera", camera);
        return SavePrefab<SearchPlayer>(root, "SearchPlayer");
    }

    private static SearchHud CreateHud(Font font)
    {
        var root = new GameObject("Search HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(SearchHud));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;

        var aim = Rect(root.transform, "Aim", new(0.5f, 0.5f), Vector2.zero, new(5, 5));
        var reticle = aim.gameObject.AddComponent<Image>();
        reticle.color = ColorFromHex(0xfff6de);
        reticle.raycastTarget = false;
        var aimOutline = aim.gameObject.AddComponent<Outline>();
        aimOutline.effectColor = new Color(0, 0, 0, 0.8f);
        aimOutline.effectDistance = new Vector2(1, 1);

        var prompt = Text(root.transform, font, "Prompt", new(0.5f, 0), new(0, 83), new(780, 38), 24);
        prompt.fontStyle = FontStyle.Bold;
        var controls = Text(root.transform, font, "Controls", new(0.5f, 0), new(0, 38), new(720, 30), 17);
        foreach (var text in new[] { prompt, controls })
        {
            var shadow = text.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(0.09f, 0.10f, 0.08f, 0.95f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }
        var hud = root.GetComponent<SearchHud>();
        Assign(hud, "prompt", prompt);
        Assign(hud, "controls", controls);
        Assign(hud, "reticle", reticle);
        return SavePrefab<SearchHud>(root, "SearchHud");
    }

    private static void Marking(Transform lamp, Font font)
    {
        var panel = new GameObject("Model plate", typeof(RectTransform), typeof(Canvas));
        panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rect = panel.GetComponent<RectTransform>();
        rect.SetParent(lamp, false);
        rect.localPosition = new Vector3(0.025f, -0.09f, 0.174f);
        rect.localRotation = Quaternion.Euler(0, 180, 0);
        rect.localScale = Vector3.one * 0.00065f;
        rect.sizeDelta = new Vector2(360, 115);
        var text = Text(rect, font, "Stamped model", new(0.5f, 0.5f), Vector2.zero, rect.sizeDelta, 52);
        text.text = "SV-04 / 12V";
        text.fontStyle = FontStyle.Bold;
        text.color = ColorFromHex(0x222b27);
    }

    private static Text Text(Transform parent, Font font, string name, Vector2 anchor, Vector2 offset, Vector2 size, int fontSize)
    {
        var text = Rect(parent, name, anchor, offset, size).gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.color = ColorFromHex(0xfff6de);
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
        return rect;
    }

    private static SearchItem Item(string name, string id, string displayName, Vector3 position, Vector3 size)
    {
        var item = new GameObject(name).AddComponent<SearchItem>();
        item.transform.position = position;
        var body = item.GetComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.mass = size.x * 3f;
        item.GetComponent<BoxCollider>().size = size;
        var serialized = new SerializedObject(item);
        serialized.FindProperty("itemId").stringValue = id;
        serialized.FindProperty("displayName").stringValue = displayName;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    private static Transform Marker(string name, Vector3 position, Vector3 angle)
    {
        var marker = new GameObject(name).transform;
        marker.SetPositionAndRotation(position, Quaternion.Euler(angle));
        return marker;
    }

    private static void Box(Transform parent, string name, Vector3 position, Vector3 size, Material material,
        bool solid = false, Quaternion? rotation = null)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = position;
        box.transform.localScale = size;
        box.transform.localRotation = rotation ?? Quaternion.identity;
        box.GetComponent<Renderer>().sharedMaterial = material;
        if (!solid)
            Object.DestroyImmediate(box.GetComponent<Collider>());
    }

    private static Material Material(string name, int color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", ColorFromHex(color));
        material.SetFloat("_Smoothness", 0.18f);
        AssetDatabase.CreateAsset(material, $"{Content}/{name.Replace(' ', '_')}.mat");
        return material;
    }

    private static Color ColorFromHex(int color) =>
        new Color32((byte)(color >> 16), (byte)(color >> 8), (byte)color, 255);

    private static void Assign(Object owner, string field, Object value)
    {
        var serialized = new SerializedObject(owner);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static T SavePrefab<T>(GameObject root, string name) where T : Component
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Content}/{name}.prefab").GetComponent<T>();
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var separator = path.LastIndexOf('/');
        var parent = path[..separator];
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path[(separator + 1)..]);
    }
}
