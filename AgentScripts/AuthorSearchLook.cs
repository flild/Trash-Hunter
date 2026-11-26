using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TrashHunter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// One-time authoring pass over the saved I-100 scene. Collision geometry stays in the scene.
public static class AuthorSearchLook
{
    private const string Content = "Assets/TrashHunter/Content/Search";
    private const string Art = Content + "/PileArt";
    private const string MeshPath = Art + "/PileMeshes.asset";

    public static object Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/TrashHunter/Scenes/ContainerSearch.unity" || scene.isDirty ||
            EditorApplication.isPlayingOrWillChangePlaymode || AssetDatabase.IsValidFolder(Art))
            throw new InvalidOperationException("Open the saved I-100 ContainerSearch scene in Edit Mode.");

        var container = GameObject.Find("Roll-off Container").transform;
        var light = GameObject.Find("Overcast daylight").GetComponent<Light>();
        var items = Object.FindObjectsByType<SearchItem>(FindObjectsInactive.Include)
            .ToDictionary(item => item.Id);
        var cabinet = items["cabinet-a"].transform;
        var sack = items["sack-a"].transform;
        var box = items["parcel-a"].transform;
        var cloth = sack.Find("Slumped cloth");
        var openedSack = sack.Find("Open mouth");
        var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        var cube = box.Find("Base").GetComponent<MeshFilter>().sharedMesh;
        var materialCount = 0;
        var meshCount = 0;
        var mergedSources = 0;
        var mergedRenderers = 0;
        AssetDatabase.CreateFolder(Content, "PileArt");

        var concrete = Finish(Existing("Concrete"), "Concrete", (x, y, n) =>
            Shade(n + (y is 0 or 63 ? -17 : 0) + (x is 0 or 63 ? -9 : 0)));
        concrete.mainTextureScale = new Vector2(7, 2);
        var asphalt = Finish(Existing("Asphalt"), "Grit", (x, y, n) =>
            Shade(n + (Hash(x, y) % 17 == 0 ? -26 : 0)));
        asphalt.mainTextureScale = new Vector2(8, 8);
        var containerPaint = Finish(Existing("Container_Paint"), "PaintedSteel", (x, y, n) =>
        {
            var wear = y < 6 + Hash(x / 3, 8) % 8 || Hash(x / 4, y) % 61 == 0;
            return wear ? Pixel(245 + n, 200 + n, 166 + n) : Shade(n + (y < 26 ? -14 : 0));
        });
        containerPaint.mainTextureScale = new Vector2(3, 1);
        var dark = Finish(Existing("Dark_Metal"), "DarkSteel", (x, y, n) =>
            Shade(n + (Hash(x / 5, y) % 43 == 0 ? -39 : 0)));
        var timber = Finish(Existing("Weathered_Timber"), "WoodGrain", (x, y, n) =>
        {
            var seam = (y + 2) % 22;
            var grain = Hash(x / 11, y) % 18 - 9;
            return Shade(n + grain + (seam == 0 ? -65 : seam == 1 ? -23 : 0));
        });
        var enamel = Finish(Existing("Lamp_Enamel"), "ScuffedEnamel", (x, y, n) =>
            Shade(n + (Hash(x / 7, y) % 53 == 0 ? -53 : 0)));
        var oxide = Finish(Existing("Oxide"), "Oxide", (x, y, n) => Shade(n));
        var ivory = Finish(Existing("Ivory"), "Plastic", (x, y, n) => Shade(n / 2));
        var paper = NewFinish("Cardboard", 0xbf9264, (x, y, n) =>
            Shade(n + (x < 2 || y < 3 ? -34 : 0) + (y == 49 - x / 5 ? -24 : 0)));
        var tape = NewFinish("PackingTape", 0xd1bd91, (x, y, n) =>
            Shade(n / 2 + (x == 0 || x == 63 ? -25 : 0)));
        var label = NewFinish("ParcelLabel", 0xd1c1a1, (x, y, n) =>
            Shade(x > 8 && x < 57 && y > 6 && y < 27 && Hash(x, 11) % 4 != 0 ? -147 :
                x > 8 && x < 39 && (y is 42 or 47) ? -98 : n / 2));
        var weave = NewFinish("SackWeave", 0x9a9670, (x, y, n) =>
            Shade(n + ((x + y) % 4 == 0 ? -25 : 0) + (x % 23 == 6 ? -38 : 0)));
        var grille = NewFinish("SpeakerGrille", 0x68665c, (x, y, n) =>
            Shade(x % 5 < 3 && y % 5 < 2 ? -138 : n));
        var cabinetPaint = NewFinish("CabinetEnamel", 0x60746f, (x, y, n) =>
            Shade(n + (x < 3 || x > 61 || y < 4 ? -42 : 0) + (Hash(x, y / 6) % 83 == 0 ? -50 : 0)));

        SetColor(concrete, 0x777972);
        SetColor(containerPaint, 0x465456);
        SetColor(dark, 0x4c5557);
        SetColor(timber, 0x937154);
        SetColor(enamel, 0x708787);
        SetColor(oxide, 0x775044);
        SetColor(ivory, 0xdcceaa);

        cloth.GetComponent<MeshFilter>().sharedMesh = SaveMesh(SackShell());
        cloth.localPosition = Vector3.zero;
        cloth.localScale = Vector3.one;
        cloth.GetComponent<MeshRenderer>().sharedMaterial = weave;
        sack.Find("Tied mouth/Gathered neck").GetComponent<MeshRenderer>().sharedMaterial = weave;
        sack.Find("Tied mouth/Tie").GetComponent<MeshRenderer>().sharedMaterial = tape;
        foreach (var fold in openedSack.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("Folded lip")))
            fold.sharedMaterial = weave;

        foreach (var face in box.GetComponentsInChildren<MeshRenderer>(true))
            face.sharedMaterial = face.name == "Tape" ? tape : paper;
        AddBox(box.Find("Taped lid"), "Shipping label", new(0.145f, 0.1285f, 0.045f),
            new(0.14f, 0.004f, 0.125f), label);

        cabinet.Find("Cabinet body").GetComponent<MeshRenderer>().sharedMaterial = cabinetPaint;
        foreach (var door in cabinet.Cast<Transform>().Where(part => part.name == "Door"))
            door.GetComponent<MeshRenderer>().sharedMaterial = cabinetPaint;
        AddBox(cabinet, "Louver left", new(-0.17f, 0.30f, -0.226f), new(0.20f, 0.12f, 0.004f), grille);
        AddBox(cabinet, "Louver right", new(0.17f, 0.30f, -0.226f), new(0.20f, 0.12f, 0.004f), grille);

        foreach (var radio in items.Values.Where(item => item.Id.StartsWith("radio-")))
            radio.transform.Find("Speaker grille").GetComponent<MeshRenderer>().sharedMaterial = grille;

        // Keep the existing collider-bearing nodes and only merge adjacent visuals of the same material.
        foreach (var side in new[] { -1f, 1f })
            Merge(container, $"Wall ribs {side}", container.Cast<Transform>()
                .Where(child => child.name == "Wall rib" && Mathf.Sign(child.localPosition.x) == side)
                .Select(child => child.GetComponent<MeshRenderer>()).ToArray());
        foreach (var item in items.Values)
        {
            // Lamp shell renderers receive the existing layout-dependent finish at runtime.
            if (item.Id.StartsWith("lamp-"))
                continue;

            MergeMatching(item.transform);
            if (item == items["parcel-a"])
                MergeMatching(box.Find("Open flaps"));
        }

        light.color = Color(0xd5e3ed);
        light.intensity = 1.28f;
        light.GetUniversalAdditionalLightData().softShadowQuality = SoftShadowQuality.Medium;
        RenderSettings.ambientLight = Color(0x9caaa9);
        // The lit yard and playable pile fit inside this range.
        pipeline.shadowDistance = 20;
        pipeline.shadowCascadeCount = 2;
        EditorUtility.SetDirty(pipeline);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Failed to save the updated search scene.");
        return new { materialCount, meshCount, mergedSources, mergedRenderers, shadowDistance = pipeline.shadowDistance, shadowCascades = pipeline.shadowCascadeCount };

        Material Existing(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>($"{Content}/{name}.mat") ??
            throw new InvalidOperationException("Missing search material: " + name);

        Material NewFinish(string name, int color, Func<int, int, int, Color32> pixel)
        {
            var material = new Material(enamel) { name = name };
            material.mainTextureScale = Vector2.one;
            SetColor(material, color);
            AssetDatabase.CreateAsset(material, $"{Art}/{name}.mat");
            materialCount++;
            return Finish(material, name, pixel);
        }

        Material Finish(Material material, string name, Func<int, int, int, Color32> pixel)
        {
            const int size = 64;
            var image = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var grain = Hash(x, y) % 17 - 8 + Hash(x / 8, y / 8) % 15 - 7;
                    pixels[y * size + x] = pixel(x, y, grain);
                }

            image.SetPixels32(pixels);
            image.Apply();
            var path = $"{Art}/{name}.png";
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 1;
            importer.SaveAndReimport();
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            material.SetFloat("_Smoothness", 0.12f);
            EditorUtility.SetDirty(material);
            return material;
        }

        Mesh SaveMesh(Mesh mesh)
        {
                if (meshCount++ == 0)
                AssetDatabase.CreateAsset(mesh, MeshPath);
            else
                AssetDatabase.AddObjectToAsset(mesh, MeshPath);
            return mesh;
        }

        void MergeMatching(Transform root)
        {
            var groups = root.Cast<Transform>().Select(child => child.GetComponent<MeshRenderer>())
                .Where(renderer => renderer != null && renderer.enabled)
                .GroupBy(renderer => renderer.sharedMaterial)
                .Select(group => group.ToArray()).Where(group => group.Length > 1).ToArray();
            foreach (var group in groups)
                Merge(root, root.name + " " + group[0].sharedMaterial.name, group);
        }

        void Merge(Transform root, string name, MeshRenderer[] sources)
        {
            if (sources.Length < 2)
                throw new InvalidOperationException("Expected repeated visual surfaces: " + name);
            var inputs = sources.Select(renderer => new CombineInstance
            {
                mesh = renderer.GetComponent<MeshFilter>().sharedMesh,
                transform = root.worldToLocalMatrix * renderer.localToWorldMatrix
            }).ToArray();
            var mesh = new Mesh { name = name };
            mesh.CombineMeshes(inputs, true, true);
            var renderer = AddRenderer(root, name, SaveMesh(mesh), sources[0].sharedMaterial);
            renderer.shadowCastingMode = sources[0].shadowCastingMode;
            renderer.receiveShadows = sources[0].receiveShadows;
            mergedSources += sources.Length;
            mergedRenderers++;
            foreach (var source in sources)
                source.enabled = false;
        }

        void AddBox(Transform root, string name, Vector3 position, Vector3 scale, Material material)
        {
            var child = AddRenderer(root, name, cube, material).transform;
            child.localPosition = position;
            child.localScale = scale;
        }
    }

    private static MeshRenderer AddRenderer(Transform parent, string name, Mesh mesh, Material material)
    {
        var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        part.transform.SetParent(parent, false);
        part.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    private static Mesh SackShell()
    {
        const int sides = 12;
        var heights = new[] { -0.145f, -0.08f, 0.029f, 0.092f, 0.045f };
        var widths = new[] { 0.14f, 0.216f, 0.224f, 0.152f, 0.106f };
        var depths = new[] { 0.105f, 0.169f, 0.183f, 0.122f, 0.084f };
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        for (var layer = 0; layer < heights.Length; layer++)
            for (var side = 0; side <= sides; side++)
            {
                var angle = side * Mathf.PI * 2 / sides;
                var fold = side % 3 == 1 ? 0.86f : 1f;
                var sag = (Hash(side % sides, layer) % 5 - 2) * 0.004f;
                vertices.Add(new Vector3(Mathf.Cos(angle) * widths[layer] * fold - layer * 0.004f,
                    heights[layer] + sag, Mathf.Sin(angle) * depths[layer] * fold));
                uv.Add(new Vector2((float)side / sides, (float)layer / (heights.Length - 1)));
                if (layer == 0 || side == sides)
                    continue;
                var i = layer * (sides + 1) + side;
                triangles.AddRange(new[] { i, i + 1, i - sides - 1, i + 1, i - sides, i - sides - 1 });
            }
        // The opening remains recessed, while the base is solid when the sack rolls.
        for (var side = 1; side < sides - 1; side++)
        {
            triangles.AddRange(new[] { 0, side, side + 1 });
            var inside = (heights.Length - 1) * (sides + 1);
            triangles.AddRange(new[] { inside, inside + side + 1, inside + side });
        }

        var mesh = new Mesh { name = "Folded sack shell" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static int Hash(int x, int y)
    {
        var hash = unchecked((uint)(x * 374761393 + y * 668265263 + 1597334677));
        hash = unchecked((hash ^ (hash >> 13)) * 1274126177);
        return (int)((hash ^ (hash >> 16)) & int.MaxValue);
    }

    private static Color32 Shade(int offset) => Pixel(220 + offset, 220 + offset, 220 + offset);

    private static Color32 Pixel(int red, int green, int blue) =>
        new((byte)Mathf.Clamp(red, 0, 255), (byte)Mathf.Clamp(green, 0, 255), (byte)Mathf.Clamp(blue, 0, 255), 255);

    private static Color Color(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

    private static void SetColor(Material material, int rgb)
    {
        material.color = Color(rgb);
        EditorUtility.SetDirty(material);
    }
}
