using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class AuthorPileScraps
{
    public static object Apply()
    {
        const string meshPath = "Assets/TrashHunter/Content/Search/Meshes/LooseOffcuts.asset";
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty ||
            scene.path != "Assets/TrashHunter/Scenes/ContainerSearch.unity" || AssetDatabase.LoadAssetAtPath<Mesh>(meshPath))
            throw new InvalidOperationException("Expected the saved search scene before the offcuts pass.");

        var scrap = GameObject.Find("Fixed Scrap").transform;
        var cube = scrap.Find("Boards on left").GetComponent<MeshFilter>().sharedMesh;
        var finishes = new[] { "Cardboard", "Weathered_Timber", "Oxide" }
            .Select(name => AssetDatabase.LoadAssetAtPath<Material>($"Assets/TrashHunter/Content/Search/Materials/{name}.mat"))
            .ToArray();
        if (finishes.Any(material => material == null))
            throw new InvalidOperationException("Missing pile finish.");

        var random = new System.Random(200);
        var groups = finishes.Select(_ => new List<CombineInstance>()).ToArray();
        // Overlapping chips and splinters follow existing support surfaces. Leave the entry clear.
        for (var patch = 0; patch < 23; patch++)
        {
            var cx = Range(-1.60f, 1.55f);
            var cz = Range(2.0f, 4.93f);
            for (var piece = 0; piece < 5; piece++)
            {
                var x = Mathf.Clamp(cx + Range(-0.36f, 0.36f), -1.80f, 1.80f);
                var z = Mathf.Clamp(cz + Range(-0.34f, 0.34f), 1.70f, 4.98f);
                var surface = Physics.RaycastAll(new Vector3(x, 1.3f, z), Vector3.down, 1.25f)
                    .Where(h => h.rigidbody == null && h.normal.y > 0.75f)
                    .OrderBy(h => h.distance).First();
                var finish = random.Next(groups.Length);
                var size = new Vector3(Range(0.12f, 0.40f), Range(0.007f, 0.018f),
                    finish == 0 ? Range(0.09f, 0.24f) : Range(0.025f, 0.08f));
                var rotation = Quaternion.FromToRotation(Vector3.up, surface.normal) *
                    Quaternion.Euler(Range(-4f, 4f), Range(0f, 360f), Range(-5f, 5f));
                groups[finish].Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(
                    surface.point + surface.normal * (size.y * 0.5f + piece * 0.003f), rotation, size) });
            }
        }

        var sections = new List<CombineInstance>();
        try
        {
            foreach (var group in groups)
            {
                var section = new Mesh();
                section.CombineMeshes(group.ToArray());
                sections.Add(new CombineInstance { mesh = section, transform = Matrix4x4.identity });
            }
            var mesh = new Mesh { name = "Loose offcuts" };
            mesh.CombineMeshes(sections.ToArray(), false);
            AssetDatabase.CreateAsset(mesh, meshPath);
            var layer = new GameObject("Loose offcuts", typeof(MeshFilter), typeof(MeshRenderer));
            layer.transform.SetParent(scrap, true);
            layer.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = layer.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = finishes;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save search scene.");
            return new { pieces = groups.Sum(group => group.Count), mesh.subMeshCount };
        }
        finally
        {
            foreach (var section in sections)
                UnityEngine.Object.DestroyImmediate(section.mesh);
        }

        float Range(float low, float high) => low + (high - low) * (float)random.NextDouble();
    }
}
