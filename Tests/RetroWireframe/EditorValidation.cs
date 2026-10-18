using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LudumDare60.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

public static class RetroWireframeValidation
{
    private static readonly List<GameObject> samples = new List<GameObject>();
    private static readonly List<Mesh> testMeshes = new List<Mesh>();
    private static Camera camera;
    private static GameObject gridObject;
    private static int frames;
    private static string output;
    private static double bakeMilliseconds;

    public static void Run()
    {
        try
        {
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../..", "Logs"));
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GeometryTests();
            // Replacing validation assets must not leave the previous pipeline pointing at a destroyed renderer.
            GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = null;
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, "Assets/ValidationRenderer.asset");
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.supportsCameraDepthTexture = true;
            pipeline.msaaSampleCount = 1;
            AssetDatabase.CreateAsset(pipeline, "Assets/ValidationPipeline.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            SetupPreview();
            frames = 0;
            EditorApplication.update += RenderPreview;
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void GeometryTests()
    {
        var square = CreateGrid(1, 1);
        BuildCheck(square, WireframeEdgeMode.RetroQuads, 5, 4, 1, "square diagonal");
        BuildCheck(square, WireframeEdgeMode.AllTriangles, 5, 5, 0, "triangle fallback");
        var grid = CreateGrid(2, 2);
        BuildCheck(grid, WireframeEdgeMode.RetroQuads, 16, 12, 4, "keep planar grid segmentation");
        BuildCheck(grid, WireframeEdgeMode.FeatureEdges, 16, 8, 0, "feature edges remain a separate mode");
        var seam = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.one,
            Vector3.zero, Vector3.one, Vector3.up } };
        // Work in XY: avoid Vector3.one's unintended Z component.
        seam.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1, 0), Vector3.zero, new Vector3(1, 1, 0), Vector3.up };
        seam.triangles = new[] { 0, 1, 2, 3, 4, 5 };
        BuildCheck(seam, WireframeEdgeMode.RetroQuads, 5, 4, 1, "weld coincident UV seams");
        var boundary = UnityEngine.Object.Instantiate(square);
        boundary.subMeshCount = 2;
        boundary.SetTriangles(new[] { 0, 1, 3 }, 0); boundary.SetTriangles(new[] { 0, 3, 2 }, 1);
        BuildCheck(boundary, WireframeEdgeMode.RetroQuads, 5, 5, 0, "preserve material boundaries");
        var authored = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1, 0), Vector3.up } };
        authored.SetIndices(new[] { 0, 1, 2, 3 }, MeshTopology.Quads, 0);
        BuildCheck(authored, WireframeEdgeMode.RetroQuads, 5, 4, 1, "authored quad topology");
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var source = cube.GetComponent<MeshFilter>().sharedMesh;
        BuildCheck(source, WireframeEdgeMode.RetroQuads, 18, 12, 6, "cube face diagonals and split normals");
        BuildCheck(source, WireframeEdgeMode.FeatureEdges, 18, 12, 0, "cube hard edges");
        var component = cube.AddComponent<Xray>();
        component.ApplyRetroPreset();
        Require(component.VisibleEdgeCount == 12, "Xray component did not use generated edge masks.");
        var generated = cube.GetComponentsInChildren<MeshRenderer>(true);
        Require(generated.Length == 2, "Unexpected overlay count.");
        var block = new MaterialPropertyBlock(); generated[1].GetPropertyBlock(block);
        Require(block.GetFloat("_UseEdgeMask") == 1 && block.GetFloat("_UseSceneDepth") == 0, "MaterialPropertyBlock contract failed.");
        component.RebuildOverlays();
        Require(cube.GetComponentsInChildren<MeshRenderer>(true).Length == 2, "Rebuild duplicated overlays.");
        component.enabled = false;
        Require(cube.GetComponent<MeshRenderer>().enabled && cube.transform.childCount == 0, "Disable did not restore surfaces and release overlays.");
        Require(cube.GetComponent<MeshFilter>().sharedMesh == source, "Original model mesh was modified.");
        UnityEngine.Object.DestroyImmediate(cube);
        var unreadable = UnityEngine.Object.Instantiate(square); unreadable.UploadMeshData(true);
        var rejected = false;
        try { RetroWireframeMeshBuilder.Build(unreadable, WireframeEdgeMode.RetroQuads, 10, 30, out _); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "Unreadable mesh was not diagnosed.");
        SparseTests();
        foreach (var mesh in new[] { square, grid, seam, boundary, authored, unreadable }) UnityEngine.Object.DestroyImmediate(mesh);
        Debug.Log("RETRO_WIREFRAME_GEOMETRY_OK: diagonals, grid, seams, materials, authored quads, creases, lifecycle, masks, unreadable input.");
    }

    private static void SparseTests()
    {
        var cylinder = CreateCylinder(32, 12);
        var sparse = RetroWireframeMeshBuilder.Build(cylinder, WireframeEdgeMode.SparseStructure, 10, 30, out var result, 4);
        Require(result.VisibleEdges > 0 && result.VisibleEdges < result.InputEdges / 4, "Dense cylinder was not reduced enough.");
        var parts = new Mesh();
        parts.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1, 0), Vector3.up,
            new Vector3(2, 0, 0), new Vector3(2.005f, 0, 0), new Vector3(2.005f, 0.005f, 0), new Vector3(2, 0.005f, 0) };
        parts.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
        var full = RetroWireframeMeshBuilder.Build(parts, WireframeEdgeMode.SparseStructure, 10, 30, out var fullStats, 4, 0);
        var reduced = RetroWireframeMeshBuilder.Build(parts, WireframeEdgeMode.SparseStructure, 10, 30, out var reducedStats, 4, 0.01f);
        Require(fullStats.VisibleEdges == 8 && reducedStats.VisibleEdges == 4 && reducedStats.RemovedParts == 1, "Small-part filtering failed.");
        Require(parts.vertexCount == 8, "Sparse filtering changed the original model.");
        var nearSeam = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1, 0),
            new Vector3(0.000001f, 0, 0), new Vector3(1.000001f, 1, 0), Vector3.up }, triangles = new[] { 0, 1, 2, 3, 4, 5 } };
        var welded = RetroWireframeMeshBuilder.Build(nearSeam, WireframeEdgeMode.SparseStructure, 10, 30, out var seamStats, 4, 0, 0.00001f);
        Require(seamStats.InputEdges == 5 && seamStats.VisibleEdges == 4, "Near-coincident seams left duplicate boundaries.");
        var layered = LayeredPlanes(0, 0.0005f);
        var merged = RetroWireframeMeshBuilder.Build(layered, WireframeEdgeMode.SparseStructure, 10, 30, out var mergedStats, 4, 0, 0, 0.005f);
        Require(mergedStats.VisibleEdges == 4, "Close parallel outlines were not merged.");
        var crossing = LayeredPlanes(1, 0);
        var preserved = RetroWireframeMeshBuilder.Build(crossing, WireframeEdgeMode.SparseStructure, 10, 30, out var crossingStats, 4, 0, 0, 0.005f);
        Require(crossingStats.VisibleEdges == 8, "Diverging structural edges were incorrectly merged.");
        foreach (var mesh in new[] { layered, merged, crossing, preserved }) UnityEngine.Object.DestroyImmediate(mesh);
        foreach (var mesh in new[] { cylinder, sparse, parts, full, reduced, nearSeam, welded }) UnityEngine.Object.DestroyImmediate(mesh);
        Debug.Log("SPARSE_STRUCTURE_TESTS_OK: curve reduction, small-part filtering, source preservation, seam welding.");
    }

    private static Mesh LayeredPlanes(float angle, float offset)
    {
        var vertices = new[] { new Vector3(-1, -0.1f, 0), new Vector3(1, -0.1f, 0), new Vector3(1, 0.1f, 0), new Vector3(-1, 0.1f, 0),
            Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero };
        for (var i = 0; i < 4; i++) vertices[i + 4] = Quaternion.Euler(0, 0, angle) * vertices[i] + Vector3.forward * offset;
        return new Mesh { vertices = vertices, triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 } };
    }

    private static void BuildCheck(Mesh source, WireframeEdgeMode mode, int input, int visible, int quads, string name)
    {
        var count = source.vertexCount;
        var display = RetroWireframeMeshBuilder.Build(source, mode, 10, 30, out var stats);
        Require(stats.InputEdges == input && stats.VisibleEdges == visible && stats.PairedQuads == quads,
            name + ": got " + stats.InputEdges + "/" + stats.VisibleEdges + "/" + stats.PairedQuads);
        Require(source.vertexCount == count, name + ": changed source vertices");
        var masks = new List<Vector3>(); display.GetUVs(3, masks);
        Require(masks.Count == display.vertexCount, name + ": missing UV4 masks");
        for (var i = 0; i < masks.Count; i += 3) Require(masks[i] == masks[i + 1] && masks[i] == masks[i + 2], name + ": per-triangle mask mismatch");
        UnityEngine.Object.DestroyImmediate(display);
    }

    private static Mesh CreateGrid(int width, int height)
    {
        var vertices = new List<Vector3>(); var indices = new List<int>();
        for (var y = 0; y <= height; y++) for (var x = 0; x <= width; x++) vertices.Add(new Vector3(x, y, 0));
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var a = y * (width + 1) + x; var b = a + 1; var d = a + width + 1; var c = d + 1;
            indices.AddRange(new[] { a, b, c, a, c, d });
        }
        var mesh = new Mesh { name = "Grid", vertices = vertices.ToArray(), triangles = indices.ToArray() };
        mesh.RecalculateBounds(); return mesh;
    }

    private static void SetupPreview()
    {
        var cameraObject = new GameObject("Preview Camera", typeof(Camera));
        camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 3.2f;
        camera.transform.position = new Vector3(6, 4.5f, -8);
        camera.transform.LookAt(new Vector3(0, 0, 0));
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.allowHDR = false; camera.allowMSAA = false;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = new Vector3(-2.3f, 0, 0); cube.transform.localScale = Vector3.one * 1.6f;
        samples.Add(cube);
        AddShape("Low-poly Sphere", CreateSphere(12, 6), Vector3.zero);
        AddShape("Low-poly Cylinder", CreateCylinder(12, 3), new Vector3(2.3f, 0, 0));
        foreach (var sample in samples)
        {
            var effect = sample.AddComponent<Xray>(); effect.ApplyRetroPreset(); effect.showOutline = false;
        }
        var grid = new GameObject("Grid", typeof(MeshFilter), typeof(MeshRenderer));
        gridObject = grid;
        var gridMesh = CreateGrid(10, 8); testMeshes.Add(gridMesh);
        grid.GetComponent<MeshFilter>().sharedMesh = gridMesh;
        grid.transform.rotation = Quaternion.Euler(90, 0, 0);
        grid.transform.position = new Vector3(-5, -1.1f, -4);
        var gridEffect = grid.AddComponent<Xray>(); gridEffect.ApplyRetroPreset();
        gridEffect.showOutline = false;
        gridEffect.visibleColor = new Color(0.10f, 0.12f, 0.19f, 1); gridEffect.visibleWidth = 0.3f; gridEffect.visibleOpacity = 0.6f;
        gridEffect.RebuildOverlays();
    }
    private static void AddShape(string name, Mesh mesh, Vector3 position)
    {
        testMeshes.Add(mesh);
        var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        item.GetComponent<MeshFilter>().sharedMesh = mesh; item.transform.position = position; samples.Add(item);
    }
    private static Mesh CreateSphere(int longitude, int latitude)
    {
        var vertices = new List<Vector3> { Vector3.up }; var indices = new List<int>();
        for (var y = 1; y < latitude; y++) for (var x = 0; x < longitude; x++)
        {
            var phi = Mathf.PI * y / latitude; var theta = Mathf.PI * 2 * x / longitude;
            vertices.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)));
        }
        var bottom = vertices.Count; vertices.Add(Vector3.down);
        for (var x = 0; x < longitude; x++)
        {
            var next = (x + 1) % longitude;
            indices.AddRange(new[] { 0, 1 + x, 1 + next });
            for (var y = 0; y < latitude - 2; y++)
            {
                var a = 1 + y * longitude + x; var b = 1 + y * longitude + next;
                var c = b + longitude; var d = a + longitude;
                indices.AddRange(new[] { a, d, c, a, c, b });
            }
            indices.AddRange(new[] { bottom, 1 + (latitude - 2) * longitude + next, 1 + (latitude - 2) * longitude + x });
        }
        var mesh = new Mesh { name = "Sphere 12x6", vertices = vertices.ToArray(), triangles = indices.ToArray() }; mesh.RecalculateBounds(); return mesh;
    }
    private static Mesh CreateCylinder(int segments, int bands)
    {
        var vertices = new List<Vector3>(); var indices = new List<int>();
        for (var y = 0; y <= bands; y++) for (var x = 0; x < segments; x++)
        {
            var angle = x * Mathf.PI * 2 / segments;
            vertices.Add(new Vector3(Mathf.Cos(angle) * 0.8f, -1 + y * 2f / bands, Mathf.Sin(angle) * 0.8f));
        }
        for (var y = 0; y < bands; y++) for (var x = 0; x < segments; x++)
        {
            var a = y * segments + x; var b = y * segments + (x + 1) % segments;
            var c = b + segments; var d = a + segments;
            indices.AddRange(new[] { a, d, c, a, c, b });
        }
        var mesh = new Mesh { name = "Cylinder 12x3", vertices = vertices.ToArray(), triangles = indices.ToArray() }; mesh.RecalculateBounds(); return mesh;
    }

    private static void RenderPreview()
    {
        if (++frames < 15) return;
        EditorApplication.update -= RenderPreview;
        try
        {
            var watch = Stopwatch.StartNew();
            foreach (var sample in samples) { sample.GetComponent<Xray>().edgeMode = WireframeEdgeMode.AllTriangles; sample.GetComponent<Xray>().RebuildOverlays(); }
            Capture("RetroWireframe-Before.png");
            watch.Restart();
            var report = new List<string>();
            foreach (var sample in samples)
            {
                var effect = sample.GetComponent<Xray>(); effect.edgeMode = WireframeEdgeMode.RetroQuads; effect.RebuildOverlays();
                report.Add(sample.name + ": " + effect.SourceEdgeCount + " -> " + effect.VisibleEdgeCount + " edges; " + effect.ReconstructedQuadCount + " quads");
            }
            watch.Stop(); bakeMilliseconds = watch.Elapsed.TotalMilliseconds;
            Capture("RetroWireframe-After.png");
            var shader = Shader.Find("X-Ray/Effects/XRay");
            Require(shader != null && shader.isSupported, "Wireframe shader not supported on validation GPU.");
            foreach (var message in ShaderUtil.GetShaderMessages(shader)) Require(message.severity.ToString() != "Error", message.message);
            report.Add("Overlay rebuild on these three low-poly samples: " + bakeMilliseconds.ToString("F3") + " ms (Editor CPU; not a game frame budget).");
            OutlineRenderTests(report);
            CaptureReferenceModel(report);
            report.Add("Geometry/lifecycle tests and actual URP shader render passed.");
            File.WriteAllLines(Path.Combine(output, "RetroWireframeValidation.txt"), report);
            Debug.Log("RETRO_WIREFRAME_RENDER_OK\n" + string.Join("\n", report));
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Fail(exception); }
    }
    private static void CaptureReferenceModel(List<string> report)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ReferenceModel.glb");
        if (prefab == null) return;
        foreach (var sample in samples) sample.SetActive(false);
        gridObject.SetActive(false);
        var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        root.SetActive(false);
        var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        Require(renderers.Length > 0, "Reference model has no static mesh renderers.");
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        var radius = Mathf.Max(bounds.extents.magnitude, 0.1f);
        camera.transform.position = bounds.center + new Vector3(1, 0.6f, -1).normalized * radius * 3;
        camera.transform.LookAt(bounds.center);
        camera.orthographicSize = radius * 0.85f;
        camera.nearClipPlane = 0.01f; camera.farClipPlane = Mathf.Max(100, radius * 10);
        var effect = root.AddComponent<Xray>();
        effect.showOutline = false;
        effect.ApplyRetroPreset(); effect.edgeMode = WireframeEdgeMode.AllTriangles;
        root.SetActive(true);
        Capture("RetroWireframe-Model-Before.png");
        effect.edgeMode = WireframeEdgeMode.RetroQuads;
        var watch = Stopwatch.StartNew(); effect.RebuildOverlays(); watch.Stop();
        Capture("RetroWireframe-Model-After.png");
        report.Add("Reference mechanical model: " + effect.SourceEdgeCount + " -> " + effect.VisibleEdgeCount + " edges; "
            + effect.ReconstructedQuadCount + " inferred quads; rebuild " + watch.Elapsed.TotalMilliseconds.ToString("F3") + " ms (Editor CPU).");
        effect.edgeMode = WireframeEdgeMode.FeatureEdges;
        foreach (var angle in new[] { 30f, 60f })
        {
            effect.featureAngle = angle; effect.RebuildOverlays();
            Capture("RetroWireframe-Model-Features" + angle + ".png");
            report.Add("Reference feature edges " + angle + " degrees: " + effect.VisibleEdgeCount);
        }
        effect.ApplySparsePreset();
        Capture("RetroWireframe-Model-Recommended.png");
        effect.showOutline = true;
        Capture("RetroWireframe-Model-Outlined.png");
        camera.orthographic = false; camera.fieldOfView = 38;
        Capture("RetroWireframe-Model-Outlined-Perspective.png");
        camera.orthographic = true;
        effect.showOutline = false;
        report.Add("Recommended sparse preset: " + effect.SourceEdgeCount + " -> " + effect.VisibleEdgeCount + " edges; small-part fraction " + effect.smallPartFraction + ".");
        foreach (var detail in new[] { 0f, 0.02f, 0.04f, 0.06f, 0.08f })
        {
            effect.smallPartFraction = detail; watch.Restart(); effect.RebuildOverlays(); watch.Stop();
            Capture("RetroWireframe-Model-Sparse" + Mathf.RoundToInt(detail * 100) + ".png");
            report.Add("Sparse see-through, detail " + detail + ": " + effect.SourceEdgeCount + " -> " + effect.VisibleEdgeCount
                + " edges; " + effect.RemovedPartCount + " small parts removed; " + watch.Elapsed.TotalMilliseconds.ToString("F3") + " ms rebuild.");
            Require(!effect.useSceneOcclusion && effect.showHidden && effect.hideOriginalModel, "Sparse mode lost see-through presentation.");
        }
        effect.smallPartFraction = 0.02f;
        foreach (var spacing in new[] { 0.005f, 0.01f, 0.02f })
        {
            effect.lineSpacingFraction = spacing; effect.RebuildOverlays();
            Capture("RetroWireframe-Model-Spacing" + Mathf.RoundToInt(spacing * 1000) + ".png");
            report.Add("Sparse spacing " + spacing + ": " + effect.VisibleEdgeCount + " retained edges.");
        }
    }
    private static void OutlineRenderTests(List<string> report)
    {
        foreach (var sample in samples) sample.SetActive(false);
        gridObject.SetActive(false);
        var savedPosition = camera.transform.position; var savedRotation = camera.transform.rotation;
        var savedSize = camera.orthographicSize;
        camera.transform.position = new Vector3(0, 0, -5); camera.transform.rotation = Quaternion.identity;
        camera.orthographicSize = 2;
        var root = new GameObject("Union outline test");
        var front = GameObject.CreatePrimitive(PrimitiveType.Cube);
        front.transform.SetParent(root.transform, false);
        var rear = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rear.transform.SetParent(root.transform, false); rear.transform.localPosition = new Vector3(0.4f, 0, 1);
        var effect = root.AddComponent<Xray>(); effect.ApplySparsePreset();
        effect.visibleOpacity = effect.hiddenOpacity = 0; effect.showOutline = false; effect.RebuildOverlays();
        var blank = Capture("XrayOutline-Disabled.png");
        effect.showOutline = true; effect.outlineWidth = 2;
        var thin = Capture("XrayOutline-Only.png");
        var white = 0; var interior = 0;
        for (var y = 0; y < 900; y++) for (var x = 0; x < 1600; x++)
        {
            var pixel = thin[y * 1600 + x];
            if (pixel.r > 150 && pixel.g > 150 && pixel.b > 150) white++;
            // Both cube silhouettes overlap here. Their shared edges must not become internal white seams.
            if (x > 700 && x < 990 && y > 360 && y < 540 && pixel.r > 20) interior++;
        }
        Require(white > 700, "Outline was not drawn in the URP Game camera.");
        Require(interior == 0, "Outline filled the interior or traced the internal overlap of the model.");
        Require(blank[450 * 1600 + 800].r == 0, "Disabled outline affected camera color.");
        effect.outlineWidth = 5;
        var thick = Capture("XrayOutline-Thick.png"); var thickWhite = 0;
        foreach (var pixel in thick) if (pixel.r > 150 && pixel.g > 150 && pixel.b > 150) thickWhite++;
        Require(thickWhite > white * 1.6f, "Pixel-width setting did not change the outline thickness.");
        effect.outlineColor = Color.red;
        var red = Capture("XrayOutline-Red.png"); var redCount = 0;
        foreach (var pixel in red) if (pixel.r > 150 && pixel.g < 20 && pixel.b < 20) redCount++;
        Require(redCount > 700, "Outline color setting was not applied.");
        effect.outlineColor = Color.white; effect.visibleOpacity = effect.hiddenOpacity = 0.4f; effect.RebuildOverlays();
        var enabled = Capture("XrayOutline-WithWireframe.png"); var restoredLines = 0;
        for (var y = 350; y < 550; y++) for (var x = 710; x < 980; x++)
            if (enabled[y * 1600 + x].r > thin[y * 1600 + x].r + 10) restoredLines++;
        Require(restoredLines > 50, "Outline prevented the internal X-ray lines from rendering.");
        effect.showOutline = false; effect.visibleOpacity = effect.hiddenOpacity = 0; effect.RebuildOverlays();
        var toggled = Capture("XrayOutline-ToggledOff.png");
        for (var i = 0; i < blank.Length; i++) Require(blank[i].Equals(toggled[i]), "Turning off the outline left rendered pixels behind.");
        var shader = Resources.Load<Shader>("XraySilhouette");
        Require(shader != null && shader.isSupported, "Outline shader is missing or unsupported.");
        foreach (var message in ShaderUtil.GetShaderMessages(shader)) Require(message.severity.ToString() != "Error", message.message);
        UnityEngine.Object.DestroyImmediate(root);
        camera.transform.SetPositionAndRotation(savedPosition, savedRotation); camera.orthographicSize = savedSize;
        report.Add("Outline render: union silhouette only, transparent interior, width/color controls, internal X-ray lines and toggle passed.");
        Debug.Log("XRAY_OUTLINE_RENDER_OK");
    }

    private static Color32[] Capture(string name)
    {
        var target = new RenderTexture(1600, 900, 24); target.Create(); camera.aspect = 1600f / 900;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var previous = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); texture.Apply();
        File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
        var pixels = texture.GetPixels32();
        RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); target.Release(); UnityEngine.Object.DestroyImmediate(target);
        return pixels;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Fail(Exception exception) { EditorApplication.update -= RenderPreview; Debug.LogException(exception); EditorApplication.Exit(1); }
}
