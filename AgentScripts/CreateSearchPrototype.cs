using System;
using System.Collections.Generic;
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
        Assign(session, "containerFloor", container.Find("Floor").GetComponent<BoxCollider>());
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
        AddBag();
        return new { ScenePath, Player = AssetDatabase.GetAssetPath(player), Hud = AssetDatabase.GetAssetPath(hud) };
    }

    public static object AddBag()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || scene.isDirty || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the saved ContainerSearch scene in Edit Mode.");
        var session = Object.FindAnyObjectByType<SearchSession>();
        var serialized = new SerializedObject(session);
        if (serialized.FindProperty("starterBag").objectReferenceValue != null)
            throw new InvalidOperationException("The scene already has a starter bag.");

        var hudPath = Content + "/SearchHud.prefab";
        var hudRoot = PrefabUtility.LoadPrefabContents(hudPath);
        try
        {
            var hud = hudRoot.GetComponent<SearchHud>();
            var font = hudRoot.GetComponentsInChildren<Text>().First().font;
            if (hudRoot.GetComponentInChildren<BagView>(true) != null)
                throw new InvalidOperationException("The HUD already has a bag view.");
            Assign(hud, "bag", CreateBagView(hud.transform, font));
            if (PrefabUtility.SaveAsPrefabAsset(hudRoot, hudPath) == null)
                throw new InvalidOperationException("Failed to save the bag view.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(hudRoot);
        }

        Material Existing(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{Content}/{name}.mat");
        var dark = Existing("Dark_Metal");
        var gold = Existing("Safety_Paint");
        var cream = Existing("Ivory");
        var red = Existing("Oxide");

        var bag = new GameObject("Starter Waist Bag");
        bag.transform.position = new Vector3(-0.55f, 1.42f, -0.16f);
        var bagCollider = bag.AddComponent<BoxCollider>();
        bagCollider.size = new Vector3(0.66f, 0.27f, 0.32f);
        Box(bag.transform, "Canvas pouch", new(0, 0, 0), new(0.48f, 0.23f, 0.24f), red);
        Box(bag.transform, "Front pocket", new(0, -0.02f, -0.121f), new(0.39f, 0.15f, 0.045f), red);
        Box(bag.transform, "Zipper", new(0, 0.072f, -0.153f), new(0.394f, 0.022f, 0.024f), dark);
        Box(bag.transform, "Pull tab", new(0.143f, 0.05f, -0.172f), new(0.032f, 0.075f, 0.014f), gold);
        Box(bag.transform, "Waist strap", new(0, -0.062f, 0.086f), new(0.66f, 0.062f, 0.065f), dark);
        Box(bag.transform, "Buckle", new(0.28f, -0.06f, 0.075f), new(0.074f, 0.09f, 0.091f), gold);

        var strip = Item("Metal strip", "strip-a", "Металлическая рейка", new(-1.43f, 0.405f, 2.49f), new(0.75f, 0.07f, 0.13f));
        Box(strip.transform, "Metal strip", Vector3.zero, new(0.74f, 0.055f, 0.128f), Existing("Lamp_Enamel"));
        Box(strip.transform, "Raised edge", new(0, 0.025f, 0.05f), new(0.73f, 0.023f, 0.019f), dark);
        var spare = Item("Spare dial", "dial-a", "Запасная ручка настройки", new(0.78f, 0.265f, 1.91f), new(0.15f, 0.12f, 0.17f));
        var dial = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        dial.name = "Dial";
        dial.transform.SetParent(spare.transform, false);
        dial.transform.localScale = new Vector3(0.147f, 0.052f, 0.147f);
        dial.GetComponent<Renderer>().sharedMaterial = dark;
        Object.DestroyImmediate(dial.GetComponent<Collider>());
        Box(spare.transform, "Position mark", new(0, 0.053f, -0.037f), new(0.015f, 0.007f, 0.056f), cream);

        var items = serialized.FindProperty("items");
        var crate = (SearchItem)items.GetArrayElementAtIndex(0).objectReferenceValue;
        var lamp = (SearchItem)items.GetArrayElementAtIndex(1).objectReferenceValue;
        var radio = (SearchItem)items.GetArrayElementAtIndex(2).objectReferenceValue;
        BagItem(crate, "Ящик", 4, 2);
        BagItem(lamp, "Лампа", 1, 2);
        BagItem(radio, "Радио", 1, 1);
        BagItem(strip, "Рейка", 1, 3);
        BagItem(spare, "Ручка", 1, 1);
        items.arraySize = 5;
        items.GetArrayElementAtIndex(3).objectReferenceValue = strip;
        items.GetArrayElementAtIndex(4).objectReferenceValue = spare;
        serialized.FindProperty("starterBag").objectReferenceValue = bagCollider;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Failed to save ContainerSearch.");
        return new { ScenePath, Hud = hudPath, Bag = bag.name, Items = items.arraySize };
    }

    public static object AddExpedition()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || scene.isDirty || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the saved ContainerSearch scene in Edit Mode.");
        var session = Object.FindAnyObjectByType<SearchSession>();
        var serialized = new SerializedObject(session);
        if (serialized.FindProperty("starterBag").objectReferenceValue == null ||
            serialized.FindProperty("exit").objectReferenceValue != null)
            throw new InvalidOperationException("Apply AddExpedition once to the scene with the starter bag.");

        var items = serialized.FindProperty("items");
        var definitions = Enumerable.Range(0, items.arraySize)
            .Select(i => (SearchItem)items.GetArrayElementAtIndex(i).objectReferenceValue)
            .ToDictionary(item => item.Id);
        var layout = new (string key, Vector3 position, float yaw, int price, string condition)[]
        {
            ("crate-a", new(-0.68f, 0.58f, 3.93f), -23, 0, ""),
            ("lamp-a", new(1.23f, 0.44f, 2.62f), 147, 18, "Ушко треснуло, плафон болтается."),
            ("radio-a", new(-1.35f, 0.49f, 2.2f), 28, 26, "Корпус без видимых повреждений."),
            ("strip-a", new(0.42f, 0.32f, 3.76f), 24, 6, "Рейка погнута."),
            ("dial-a", new(-0.37f, 0.28f, 1.94f), 92, 4, "Ручка цела.")
        };
        if (layout.Length != definitions.Count || layout.Any(item => !definitions.ContainsKey(item.key)))
            throw new InvalidOperationException("Alternate layout must refer to the current five item definitions.");

        var hudPath = Content + "/SearchHud.prefab";
        var hudRoot = PrefabUtility.LoadPrefabContents(hudPath);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        try
        {
            var hud = hudRoot.GetComponent<SearchHud>();
            if (hudRoot.GetComponentInChildren<ExpeditionView>(true) != null)
                throw new InvalidOperationException("The HUD already has an expedition view.");
            Assign(hud, "expedition", CreateExpeditionView(hudRoot.transform, font));

            var feedback = Text(hudRoot.transform, font, "SaveError", new(0.5f, 0), new(0, 26), new(800, 42), 18);
            feedback.rectTransform.anchorMin = feedback.rectTransform.anchorMax = new Vector2(0.5f, 1);
            feedback.rectTransform.anchoredPosition = new Vector2(0, -37);
            feedback.color = ColorFromHex(0xffaa95);
            var outline = feedback.gameObject.AddComponent<Outline>();
            outline.effectColor = ColorFromHex(0x171b17);
            outline.effectDistance = new Vector2(2, -2);
            Assign(hud, "feedback", feedback);
            if (PrefabUtility.SaveAsPrefabAsset(hudRoot, hudPath) == null)
                throw new InvalidOperationException("Failed to save the expedition HUD.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(hudRoot);
        }

        var layoutRoot = new GameObject("Second Arrangement").transform;
        foreach (var definition in layout)
        {
            var marker = Marker(definition.key, definition.position, new(0, definition.yaw, 0));
            marker.SetParent(layoutRoot);
            var item = new SerializedObject(definitions[definition.key]);
            item.FindProperty("alternatePose").objectReferenceValue = marker;
            item.FindProperty("salePrice").intValue = definition.price;
            item.FindProperty("saleCondition").stringValue = definition.condition;
            item.ApplyModifiedPropertiesWithoutUndo();
        }

        var exit = Marker("Yard Exit", new(0, 0, -3.82f), Vector3.zero);
        Assign(session, "exit", exit);
        var dark = AssetDatabase.LoadAssetAtPath<Material>(Content + "/Dark_Metal.mat");
        var safety = AssetDatabase.LoadAssetAtPath<Material>(Content + "/Safety_Paint.mat");
        Box(exit, "Exit marking", new(0, 0.006f, 0.32f), new(2.55f, 0.026f, 0.08f), safety);
        Box(exit, "Sign post", new(1.34f, 0.57f, 0), new(0.06f, 1.14f, 0.06f), dark);
        Box(exit, "Exit sign", new(1.34f, 1.20f, 0), new(0.78f, 0.28f, 0.052f), dark);
        var sign = new GameObject("Yard Exit Label", typeof(RectTransform), typeof(Canvas)).GetComponent<RectTransform>();
        sign.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        sign.SetParent(exit, false);
        sign.localPosition = new Vector3(1.34f, 1.20f, 0.03f);
        sign.localRotation = Quaternion.Euler(0, 180, 0);
        sign.localScale = Vector3.one * 0.0015f;
        sign.sizeDelta = new Vector2(440, 150);
        var exitLabel = Text(sign, font, "Exit", new(0.5f, 0.5f), Vector2.zero, sign.sizeDelta, 72);
        exitLabel.fontStyle = FontStyle.Bold;
        exitLabel.color = ColorFromHex(0xf0c776);
        exitLabel.text = "ДОМОЙ";

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Failed to save ContainerSearch.");
        return new { ScenePath, hudPath, alternatePositions = layout.Length, exit = exit.name };
    }

    public static object AddPlaytestLayouts()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || scene.isDirty || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the saved ContainerSearch scene in Edit Mode.");

        var session = new SerializedObject(Object.FindAnyObjectByType<SearchSession>());
        var references = session.FindProperty("items");
        if (references.arraySize != 5 || session.FindProperty("exit").objectReferenceValue == null)
            throw new InvalidOperationException("Apply the playtest layouts to the five-item expedition scene.");

        var crate = (SearchItem)references.GetArrayElementAtIndex(0).objectReferenceValue;
        var legacyLamp = (SearchItem)references.GetArrayElementAtIndex(1).objectReferenceValue;
        var radio = (SearchItem)references.GetArrayElementAtIndex(2).objectReferenceValue;
        var strip = (SearchItem)references.GetArrayElementAtIndex(3).objectReferenceValue;
        var dial = (SearchItem)references.GetArrayElementAtIndex(4).objectReferenceValue;
        var intactLamp = Object.Instantiate(legacyLamp);
        var crackedLamp = Object.Instantiate(legacyLamp);
        var dark = Existing("Dark_Metal");
        var cream = Existing("Ivory");
        var enamel = Existing("Lamp_Enamel");
        var gold = Existing("Safety_Paint");
        var red = Existing("Oxide");

        foreach (var name in new[] { "Hinge upper fragment", "Hinge lower fragment", "Exposed hinge pin" })
            Object.DestroyImmediate(intactLamp.transform.Find(name).gameObject);

        Box(intactLamp.transform, "Intact hinge eye", new(-0.22f, 0.052f, 0.124f),
            new(0.12f, 0.115f, 0.092f), dark);
        Box(intactLamp.transform, "Intact hinge pin", new(-0.22f, 0.051f, 0.173f),
            new(0.032f, 0.032f, 0.014f), cream);
        Box(crackedLamp.transform, "Loose diffuser edge", new(0.16f, 0.023f, -0.171f),
            new(0.235f, 0.028f, 0.022f), cream, rotation: Quaternion.Euler(0, 0, -8));
        SetLamp(intactLamp, "lamp-intact", "Folding lamp, intact", 42, "Крепление целое, плафон держится.", enamel, gold);
        SetLamp(crackedLamp, "lamp-cracked", "Folding lamp, cracked", 18, "Ушко треснуло, плафон болтается.", gold, enamel);

        var legacy = new SerializedObject(legacyLamp);
        legacy.FindProperty("legacyLayoutOnly").boolValue = true;
        legacy.ApplyModifiedPropertiesWithoutUndo();
        legacyLamp.gameObject.SetActive(false);

        // The named finds keep authored positions. The peripheral pieces trade places within each side.
        var primary = new[]
        {
            (crate, new Vector2(0.16f, 3.08f), new Vector2(-0.59f, 3.77f), new Vector2(0.57f, 2.70f), 4f, -20f, 24f),
            (intactLamp, new Vector2(1.12f, 1.78f), new Vector2(0.45f, 3.83f), new Vector2(-0.67f, 3.29f), 155f, -135f, 117f),
            (crackedLamp, new Vector2(0.22f, 3.79f), new Vector2(-0.91f, 2.37f), new Vector2(1.18f, 3.42f), 180f, 33f, 52f),
            (radio, new Vector2(-0.94f, 2.70f), new Vector2(1.24f, 3.15f), new Vector2(-1.30f, 1.96f), 9f, 16f, 76f),
            (strip, new Vector2(-1.49f, 3.48f), new Vector2(1.57f, 2.64f), new Vector2(-1.33f, 2.87f), 90f, 83f, 87f),
            (dial, new Vector2(0.48f, 1.76f), new Vector2(-0.55f, 1.75f), new Vector2(0.66f, 1.81f), 0f, 78f, 140f)
        };
        var extra = new List<SearchItem>();
        var plug = Find("Plug", "plug-a", "Вилка", new(0.23f, 0.105f, 0.16f), dark, 1, 1, 8, "Штыри ровные.");
        Box(plug.transform, "Pins", new(0, 0, -0.081f), new(0.10f, 0.04f, 0.08f), cream);
        extra.Add(plug);
        extra.Add(Copy(plug, "plug-b", 3, "Один штырь погнут.", part: "Pins"));

        var motor = Find("Motor", "motor-a", "Моторчик", new(0.36f, 0.18f, 0.24f), enamel, 2, 1, 19, "Вал цел.");
        Box(motor.transform, "Shaft", new(0.18f, 0.02f, 0), new(0.094f, 0.06f, 0.06f), cream);
        extra.Add(motor);
        extra.Add(Copy(motor, "motor-b", 7, "Вал погнут.", part: "Shaft"));

        var adapter = Find("Adapter", "adapter-a", "Адаптер", new(0.29f, 0.14f, 0.23f), dark, 2, 1, 22, "Разъём цел.");
        Box(adapter.transform, "Socket", new(0, 0, -0.117f), new(0.085f, 0.056f, 0.018f), cream);
        Box(adapter.transform, "12V plate", new(-0.06f, 0.071f, 0), new(0.09f, 0.006f, 0.057f), gold);
        extra.Add(adapter);
        extra.Add(Copy(adapter, "adapter-b", 8, "Разъём продавлен.", part: "Socket"));

        var fuse = Find("Fuse holder", "fuse-a", "Держатель", new(0.20f, 0.11f, 0.15f), cream, 1, 1, 9, "Крышка на месте.");
        Box(fuse.transform, "Lid", new(0, 0.06f, 0), new(0.16f, 0.02f, 0.12f), dark);
        extra.Add(fuse);
        extra.Add(Copy(fuse, "fuse-b", 4, "Крышки нет.", part: "Lid", remove: true));

        var tin = Find("Metal tin", "tin-a", "Коробка", new(0.42f, 0.17f, 0.25f), gold, 2, 1, 10, "Крышка садится ровно.");
        Box(tin.transform, "Rim", new(0, 0.081f, 0), new(0.407f, 0.021f, 0.24f), cream);
        extra.Add(tin);
        extra.Add(Copy(tin, "tin-b", 5, "Крышка перекошена.", part: "Rim"));

        var bell = Find("Bell", "bell-a", "Звонок", new(0.26f, 0.14f, 0.24f), red, 1, 1, 13, "Кнопка на месте.");
        Box(bell.transform, "Button", new(0, 0.072f, 0), new(0.09f, 0.04f, 0.09f), cream);
        extra.Add(bell);
        extra.Add(Copy(bell, "bell-b", 6, "Кнопки нет.", part: "Button", remove: true));

        var pack = Find("Battery pack", "pack-a", "Блок питания", new(0.45f, 0.21f, 0.29f), enamel, 3, 2, 33,
            "Клеммы сухие.");
        Box(pack.transform, "Terminals", new(0, 0.106f, 0.06f), new(0.20f, 0.025f, 0.09f), cream);
        extra.Add(pack);
        var corroded = Copy(pack, "pack-b", 11, "На клеммах окисел.");
        corroded.transform.Find("Terminals").GetComponent<Renderer>().sharedMaterial = red;
        extra.Add(corroded);

        extra.Add(Copy(radio, "radio-b", 12, "Ручки настройки нет.", part: "Dial", remove: true));
        extra.Add(Copy(radio, "radio-c", 26, "Корпус без видимых повреждений."));
        extra.Add(Copy(strip, "strip-b", 11, "Рейка ровная.", straighten: true));
        extra.Add(Copy(dial, "dial-b", 4, "Ручка цела."));

        var edge = new[]
        {
            new Vector2(-1.59f, 1.43f), new Vector2(-1.60f, 1.99f), new Vector2(-1.58f, 2.60f),
            new Vector2(-1.55f, 4.08f), new Vector2(-1.55f, 4.62f), new Vector2(-0.82f, 4.75f),
            new Vector2(-0.21f, 4.82f), new Vector2(-0.99f, 3.86f), new Vector2(-0.91f, 1.34f),
            new Vector2(1.62f, 1.26f), new Vector2(1.76f, 1.82f), new Vector2(1.64f, 2.07f),
            new Vector2(1.65f, 3.58f), new Vector2(1.56f, 4.13f), new Vector2(1.50f, 4.74f),
            new Vector2(0.86f, 4.68f), new Vector2(0.37f, 4.86f), new Vector2(1.69f, 0.65f)
        };
        // Keep the widest objects away from the rim approach and the lamp/box comparison.
        var second = new[] { 8, 9, 3, 15, 6, 11, 4, 7, 1, 2, 10, 12, 5, 14, 0, 13, 16, 17 };
        var third = new[] { 1, 15, 14, 5, 3, 7, 0, 10, 6, 13, 11, 16, 12, 4, 9, 8, 17, 2 };
        var secondRoot = GameObject.Find("Second Arrangement").transform;
        var thirdRoot = new GameObject("Third Arrangement").transform;
        foreach (var entry in primary)
            SetPoses(entry.Item1, entry.Item2, entry.Item3, entry.Item4, entry.Item5, entry.Item6, entry.Item7);
        for (var i = 0; i < extra.Count; i++)
            SetPoses(extra[i], edge[i], edge[second[i]], edge[third[i]],
                90 + (i % 3 - 1) * 11, 90 + (i % 4 - 2) * 8, 90 + (i % 5 - 2) * 9);

        var pieces = primary.Select(entry => entry.Item1).Concat(extra).Append(legacyLamp).ToArray();
        references.arraySize = pieces.Length;
        for (var i = 0; i < pieces.Length; i++)
            references.GetArrayElementAtIndex(i).objectReferenceValue = pieces[i];
        session.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Failed to save the playtest scene.");
        return new { definitions = 12, activePieces = pieces.Length - 1, layouts = 3, legacyPieces = 1 };

        Material Existing(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{Content}/{name}.mat");

        void SetLamp(SearchItem item, string id, string name, int price, string condition, Material first, Material secondFinish)
        {
            item.name = name;
            BagItem(item, "Лампа", 2, 2);
            SetSale(item, id, price, condition);
            var shell = new[] { item.transform.Find("Base").GetComponent<Renderer>(), item.transform.Find("Shade").GetComponent<Renderer>() };
            var data = new SerializedObject(item);
            var renderers = data.FindProperty("lampShell");
            renderers.arraySize = shell.Length;
            for (var i = 0; i < shell.Length; i++)
            {
                shell[i].sharedMaterial = first;
                renderers.GetArrayElementAtIndex(i).objectReferenceValue = shell[i];
            }
            data.FindProperty("secondLayoutFinish").objectReferenceValue = secondFinish;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        SearchItem Find(string name, string id, string label, Vector3 size, Material finish, int width, int height,
            int price, string condition)
        {
            var item = Item(name, id, label, Vector3.zero, size);
            Box(item.transform, "Housing", Vector3.zero, size * 0.97f, finish);
            BagItem(item, label, width, height);
            SetSale(item, id, price, condition);
            return item;
        }

        SearchItem Copy(SearchItem template, string id, int price, string condition, string part = null,
            bool remove = false, bool straighten = false)
        {
            var item = Object.Instantiate(template);
            item.name = $"{template.name} ({id})";
            SetSale(item, id, price, condition);
            if (part != null)
            {
                var detail = item.transform.Find(part);
                if (remove)
                    Object.DestroyImmediate(detail.gameObject);
                else
                    detail.localRotation = Quaternion.Euler(0, 0, 27);
            }
            if (straighten)
                item.transform.Find("Raised edge").GetComponent<Renderer>().sharedMaterial = cream;
            return item;
        }

        void SetPoses(SearchItem item, Vector2 first, Vector2 secondPosition, Vector2 thirdPosition,
            float firstAngle, float secondAngle, float thirdAngle)
        {
            var height = item.GetComponent<BoxCollider>().size.y / 2f + 0.025f;
            var data = new SerializedObject(item);
            var previousMarker = (Transform)data.FindProperty("alternatePose").objectReferenceValue;
            var marker = previousMarker != null && previousMarker.name == item.Id
                ? previousMarker : Marker(item.Id, Vector3.zero, Vector3.zero);
            marker.SetParent(secondRoot);
            SetPose(marker, secondPosition, secondAngle, height);
            data.FindProperty("alternatePose").objectReferenceValue = marker;
            var last = Marker(item.Id, Vector3.zero, Vector3.zero);
            last.SetParent(thirdRoot);
            SetPose(last, thirdPosition, thirdAngle, height);
            data.FindProperty("thirdPose").objectReferenceValue = last;
            data.ApplyModifiedPropertiesWithoutUndo();
            SetPose(item.transform, first, firstAngle, height);
        }

        void SetPose(Transform target, Vector2 location, float yaw, float height)
        {
            var floor = Physics.RaycastAll(new Vector3(location.x, 1.40f, location.y), Vector3.down, 1.3f)
                .Where(hit => hit.transform.root.name is "Roll-off Container" or "Fixed Scrap")
                .Select(hit => hit.point.y)
                .DefaultIfEmpty(0.205f)
                .Max();
            target.SetPositionAndRotation(new Vector3(location.x, floor + height, location.y), Quaternion.Euler(0, yaw, 0));
        }
    }

    private static void SetSale(SearchItem item, string id, int price, string condition)
    {
        var data = new SerializedObject(item);
        data.FindProperty("itemId").stringValue = id;
        data.FindProperty("salePrice").intValue = price;
        data.FindProperty("saleCondition").stringValue = condition;
        data.ApplyModifiedPropertiesWithoutUndo();
    }


    private static ExpeditionView CreateExpeditionView(Transform parent, Font font)
    {
        var overlay = Rect(parent, "ExpeditionView", new(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(overlay, Vector2.zero);
        Fill(overlay, new Color32(14, 16, 14, 211));
        var view = overlay.gameObject.AddComponent<ExpeditionView>();
        var panel = Rect(overlay, "ExpeditionPanel", new(0.5f, 0.5f), Vector2.zero, new(690, 392));
        Fill(panel, ColorFromHex(0x262e28));

        Text Row(string name, float y, float height, int size, TextAnchor alignment)
        {
            var label = Text(panel, font, name, new(0.5f, 1), new(0, -y), new(618, height), size);
            label.alignment = alignment;
            label.color = ColorFromHex(0xe8e1d0);
            return label;
        }

        var heading = Row("Heading", 48, 42, 32, TextAnchor.MiddleLeft);
        heading.fontStyle = FontStyle.Bold;
        var subtitle = Row("Summary", 92, 29, 21, TextAnchor.MiddleLeft);
        subtitle.color = ColorFromHex(0xf0c776);
        var body = Row("Details", 193, 144, 21, TextAnchor.UpperLeft);
        body.lineSpacing = 1.2f;
        var feedback = Row("Feedback", 298, 53, 18, TextAnchor.MiddleLeft);
        var actions = Row("Actions", 349, 34, 18, TextAnchor.MiddleLeft);
        Fill(Rect(panel, "Action Divider", new(0.5f, 1), new(0, -322), new(618, 1)), ColorFromHex(0x556256));
        Assign(view, "heading", heading);
        Assign(view, "subtitle", subtitle);
        Assign(view, "body", body);
        Assign(view, "feedback", feedback);
        Assign(view, "actions", actions);
        overlay.gameObject.SetActive(false);
        return view;
    }

    private static BagView CreateBagView(Transform parent, Font font)
    {
        var overlay = Rect(parent, "BagView", new(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(overlay, Vector2.zero);
        Fill(overlay, new Color32(14, 16, 14, 140));
        var view = overlay.gameObject.AddComponent<BagView>();
        var panel = Rect(overlay, "BagPanel", new(0.5f, 0.5f), Vector2.zero, new(416, 402));
        Fill(panel, ColorFromHex(0x242923));
        var title = Text(panel, font, "BagTitle", new(0.5f, 1), new(0, -33), new(360, 32), 26);
        title.text = "Поясная сумка";
        title.fontStyle = FontStyle.Bold;

        var gridSize = new Vector2(BagInventory.Columns * BagView.CellSize, BagInventory.Rows * BagView.CellSize);
        var grid = Rect(panel, "BagGrid", new(0.5f, 1), new(-gridSize.x / 2, -65), gridSize);
        grid.pivot = new Vector2(0, 1);
        Fill(grid, ColorFromHex(0x262e28));
        grid.gameObject.AddComponent<RectMask2D>();
        for (var row = 0; row < BagInventory.Rows; row++)
        {
            for (var col = 0; col < BagInventory.Columns; col++)
            {
                var cell = Rect(grid, $"Cell{col}-{row}", new(0, 1),
                    new(col * BagView.CellSize + 1, -row * BagView.CellSize - 1),
                    Vector2.one * (BagView.CellSize - 2));
                cell.pivot = new Vector2(0, 1);
                Fill(cell, ColorFromHex(0x3e473e));
            }
        }

        var tile = Rect(grid, "ItemTile", new(0, 1), Vector2.zero, Vector2.one * BagView.CellSize);
        tile.pivot = new Vector2(0, 1);
        Fill(tile, ColorFromHex(0x617769));
        var label = Text(tile, font, "ItemName", new(0.5f, 0.5f), Vector2.zero, Vector2.zero, 20);
        label.fontStyle = FontStyle.Bold;
        Stretch(label.rectTransform, new Vector2(4, 4));
        tile.gameObject.SetActive(false);
        AddGridGuides(grid, tile);

        var outline = Rect(grid, "FitPreview", new(0, 1), Vector2.zero, Vector2.zero);
        outline.pivot = new Vector2(0, 1);
        var preview = Fill(outline, Color.clear);
        var edges = new[]
        {
            Edge(outline, "Top", new(0, 1), new(1, 1), new(0, -3), Vector2.zero),
            Edge(outline, "Bottom", new(0, 0), new(1, 0), Vector2.zero, new(0, 3)),
            Edge(outline, "Left", new(0, 0), new(0, 1), Vector2.zero, new(3, 0)),
            Edge(outline, "Right", new(1, 0), new(1, 1), new(-3, 0), Vector2.zero)
        };
        outline.gameObject.SetActive(false);

        var selection = Text(panel, font, "BagSelection", new(0.5f, 1), new(0, -288), new(390, 28), 21);
        selection.fontStyle = FontStyle.Bold;
        var feedback = Text(panel, font, "BagFeedback", new(0.5f, 1), new(0, -322), new(390, 26), 18);
        var controls = Text(panel, font, "BagControls", new(0.5f, 1), new(0, -369), new(390, 24), 17);
        controls.color = ColorFromHex(0xd3cdbd);
        Assign(view, "grid", grid);
        Assign(view, "itemTile", tile);
        Assign(view, "preview", preview);
        Assign(view, "title", title);
        Assign(view, "selection", selection);
        Assign(view, "feedback", feedback);
        Assign(view, "controls", controls);
        var serialized = new SerializedObject(view);
        var edgeRefs = serialized.FindProperty("previewEdges");
        edgeRefs.arraySize = edges.Length;
        for (var i = 0; i < edges.Length; i++)
            edgeRefs.GetArrayElementAtIndex(i).objectReferenceValue = edges[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        overlay.gameObject.SetActive(false);
        return view;
    }

    private static void AddGridGuides(RectTransform grid, RectTransform tile)
    {
        var itemsLayer = Rect(grid, "BagItems", Vector2.zero, Vector2.zero, Vector2.zero);
        Stretch(itemsLayer, Vector2.zero);
        tile.SetParent(itemsLayer, false);
        tile.anchoredPosition = Vector2.zero;

        var guide = new Color32(20, 24, 20, 115);
        var gridWidth = BagInventory.Columns * BagView.CellSize;
        var gridHeight = BagInventory.Rows * BagView.CellSize;
        for (var col = 1; col < BagInventory.Columns; col++)
            Fill(Rect(grid, $"ColumnGuide{col}", new(0, 1), new(col * BagView.CellSize, -gridHeight / 2f),
                new(2, gridHeight)), guide);
        for (var row = 1; row < BagInventory.Rows; row++)
            Fill(Rect(grid, $"RowGuide{row}", new(0, 1), new(gridWidth / 2f, -row * BagView.CellSize),
                new(gridWidth, 2)), guide);
    }

    private static Image Edge(RectTransform parent, string name, Vector2 min, Vector2 max, Vector2 low, Vector2 high)
    {
        var rect = Rect(parent, name, min, Vector2.zero, Vector2.zero);
        rect.anchorMax = max;
        rect.offsetMin = low;
        rect.offsetMax = high;
        return Fill(rect, Color.white);
    }

    private static Image Fill(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rect, Vector2 inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = inset;
        rect.offsetMax = -inset;
    }

    private static void BagItem(SearchItem item, string label, int width, int height)
    {
        var serialized = new SerializedObject(item);
        serialized.FindProperty("bagLabel").stringValue = label;
        serialized.FindProperty("bagSize").vector2IntValue = new Vector2Int(width, height);
        serialized.ApplyModifiedPropertiesWithoutUndo();
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
