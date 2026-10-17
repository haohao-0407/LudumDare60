#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LudumDare60.MarsBase.Editor
{
    /// <summary>Builds only the dedicated MarsBase art scene. Never edits existing game scenes or project settings.</summary>
    public static class MarsBaseSceneBuilder
    {
        public const string Root = "Assets/MarsBase";
        public const string ScenePath = Root + "/Scenes/MarsBase_RetroFuture.unity";
        public const string ModelPath = Root + "/Models/MarsBase_Architecture.fbx";
        public const string PrefabPath = Root + "/Prefabs/MarsBase_Architecture.prefab";
        const string ReportPath = "SourceArt/MarsBase/unity_validation.json";

        [Serializable]
        public sealed class PropMetrics
        {
            public string name, source, prefab;
            public Vector3 position, boundsCenter, boundsSize, modelScale;
            public int renderers, triangles, colliders;
            public List<string> materials = new List<string>();
            public List<string> textures = new List<string>();
        }

        [Serializable]
        public sealed class PropsReport
        {
            public string status = "placed_static_props";
            public List<PropMetrics> props = new List<PropMetrics>();
        }

        [Serializable]
        public sealed class Report
        {
            public string status;
            public string scene = ScenePath;
            public string model = ModelPath;
            public string prefab = PrefabPath;
            public int renderers, meshFilters, uniqueMeshes, triangles, materials, colliders;
            public int missingScripts, missingMeshes, missingMaterials, invalidBounds;
            public Vector3 architectureCenter, architectureSize;
            public Vector3 importedRootScale;
            public List<string> errors = new List<string>();
            public List<string> warnings = new List<string>();
        }

        [MenuItem("Tools/Mars Base/Build Presentation Scene")]
        static void BuildMenu() { Debug.Log(Build()); }

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build the Mars base in Edit Mode.");
            if (File.Exists(ScenePath))
                return JsonUtility.ToJson(new Report { status = "already_exists_preserved" }, true);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
                throw new FileNotFoundException("Import the Blender architecture FBX before building.", ModelPath);
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                throw new InvalidOperationException("The project's URP/Lit shader is not available.");

            EnsureFolders();
            var materials = CreateMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var architecture = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            architecture.name = "MarsBase_Architecture";
            architecture.transform.position = Vector3.zero;
            foreach (var renderer in architecture.GetComponentsInChildren<Renderer>(true))
            {
                var assigned = renderer.sharedMaterials;
                for (var i = 0; i < assigned.Length; i++)
                {
                    if (assigned[i] == null) continue;
                    var key = assigned[i].name.Replace(" (Instance)", "");
                    if (materials.TryGetValue(key, out var replacement)) assigned[i] = replacement;
                }
                renderer.sharedMaterials = assigned;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
            }
            AddArchitectureColliders(architecture);
            var bounds = GetBounds(architecture);
            PrefabUtility.SaveAsPrefabAssetAndConnect(architecture, PrefabPath, InteractionMode.AutomatedAction);
            CreatePresentationSurface(bounds, materials["MB_RedSoil"]);
            CreateLighting(bounds);
            CreateCameras(bounds);
            CreateVolume();
            // A separate empty root is reserved for the independently generated Meshy hero props.
            new GameObject("Meshy_HeroProps");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Could not save the dedicated MarsBase scene.");
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = architecture;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Frame(bounds, false);
            return Validate();
        }

        [MenuItem("Tools/Mars Base/Validate Presentation Scene")]
        static void ValidateMenu() { Debug.Log(Validate()); }

        [MenuItem("Tools/Mars Base/Place Meshy Hero Props")]
        static void PlaceMeshyMenu() { Debug.Log(PlaceMeshyProps()); }

        public static string PolishPresentation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Polish the presentation in Edit Mode.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Load the dedicated MarsBase scene first.");
            SceneManager.SetActiveScene(scene);
            var materials = CreateMaterials();
            foreach (var material in materials.Values) AssetDatabase.SaveAssetIfDirty(material);
            ConfigureSky();
            ConfigureAmbient();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "Sun_Late_Martian_Afternoon")
                {
                    var light = root.GetComponent<Light>();
                    light.intensity = 2;
                    root.transform.rotation = Quaternion.Euler(26, 225, 0);
                    RenderSettings.sun = light;
                }
                else if (root.name == "Sky_Bounce_Cool_Fill")
                {
                    root.GetComponent<Light>().intensity = .45f;
                    root.transform.rotation = Quaternion.Euler(38, -40, 0);
                }
                else if (root.name == "Presentation_Surface_Flat_Mars_Sand")
                    root.transform.localScale = new Vector3(400, 1, 400);
                else if (root.name == "Main Camera - Mars Base")
                {
                    root.transform.position = new Vector3(-50, 18, 80);
                    root.transform.LookAt(new Vector3(0, 7, -1));
                    root.GetComponent<Camera>().fieldOfView = 32;
                    root.GetComponent<Camera>().farClipPlane = 5000;
                    root.GetComponent<Camera>().clearFlags = CameraClearFlags.Skybox;
                }
                else if (root.name == "Overview Camera - Disabled")
                    root.GetComponent<Camera>().farClipPlane = 5000;
            }
            return RefreshArchitectureColliders();
        }

        public static string RefreshArchitectureColliders()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Refresh architectural collision in Edit Mode.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Load the dedicated MarsBase scene first.");
            GameObject architecture = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "MarsBase_Architecture") architecture = root;
            if (architecture == null)
                throw new InvalidOperationException("The dedicated architecture root is missing.");
            AddArchitectureColliders(architecture);
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(architecture) == PrefabPath)
                PrefabUtility.ApplyPrefabInstance(architecture, InteractionMode.AutomatedAction);
            else
                PrefabUtility.SaveAsPrefabAssetAndConnect(architecture, PrefabPath, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Could not save architectural collision in the dedicated scene.");
            return Validate();
        }

        public static string PlaceMeshyProps()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Place the Mars base props in Edit Mode.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Load the dedicated MarsBase scene first.");
            const string roverPath = Root + "/Models/Meshy/Rover_T25.glb";
            const string supportPath = Root + "/Models/Meshy/LifeSupport_T25.glb";
            var roverSource = AssetDatabase.LoadAssetAtPath<GameObject>(roverPath);
            var supportSource = AssetDatabase.LoadAssetAtPath<GameObject>(supportPath);
            if (roverSource == null || supportSource == null)
                throw new FileNotFoundException("Import both Rover_T25.glb and LifeSupport_T25.glb before placement.");
            Transform propsRoot = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "Meshy_HeroProps") propsRoot = root.transform;
            if (propsRoot == null)
            {
                var holder = new GameObject("Meshy_HeroProps");
                SceneManager.MoveGameObjectToScene(holder, scene);
                propsRoot = holder.transform;
            }
            var report = new PropsReport();
            var rover = EnsureProp(roverSource, "Rover_T25", "Rover_T25", 6.8f, 20,
                new Vector3(18, 0, 24), true, propsRoot, scene);
            var support = EnsureProp(supportSource, "LifeSupport_T25_01", "LifeSupport_T25", 4.2f, 0,
                new Vector3(-10, 0, 21), false, propsRoot, scene);
            var supportDuplicate = EnsureProp(supportSource, "LifeSupport_T25_02", "LifeSupport_T25", 4.2f, 0,
                new Vector3(-14, 0, 20), false, propsRoot, scene);
            report.props.Add(MeasureProp(rover, roverPath, "Rover_T25"));
            report.props.Add(MeasureProp(support, supportPath, "LifeSupport_T25"));
            report.props.Add(MeasureProp(supportDuplicate, supportPath, "LifeSupport_T25"));
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Could not save Meshy props in the dedicated scene.");
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("SourceArt/MarsBase");
            var json = JsonUtility.ToJson(report, true);
            File.WriteAllText("SourceArt/MarsBase/meshy_unity_metrics.json", json);
            Validate();
            return json;
        }

        static GameObject EnsureProp(GameObject source, string name, string prefabName, float longestDimension,
            float yaw, Vector3 position, bool rover, Transform parent, Scene scene)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing.gameObject;
            var prefabPath = Root + "/Prefabs/" + prefabName + ".prefab";
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject wrapper;
            if (saved != null)
                wrapper = (GameObject)PrefabUtility.InstantiatePrefab(saved, scene);
            else
            {
                wrapper = new GameObject(prefabName);
                SceneManager.MoveGameObjectToScene(wrapper, scene);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                model.name = "Meshy_T25_OriginalPBR";
                model.transform.SetParent(wrapper.transform, false);
                model.transform.localRotation = Quaternion.Euler(0, yaw, 0) * model.transform.localRotation;
                var original = GetBounds(wrapper);
                var longest = Mathf.Max(original.size.x, Mathf.Max(original.size.y, original.size.z));
                if (!Finite(original.size) || longest < .00001f)
                {
                    Object.DestroyImmediate(wrapper);
                    throw new InvalidOperationException("Invalid imported Meshy bounds: " + source.name);
                }
                model.transform.localScale *= longestDimension / longest;
                var normalized = GetBounds(wrapper);
                model.transform.position += new Vector3(-normalized.center.x, -.04f - normalized.min.y, -normalized.center.z);
                foreach (var renderer in wrapper.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }
                // Preserve every imported material and texture reference; only the wrapper owns collision.
                if (rover)
                {
                    var box = wrapper.AddComponent<BoxCollider>();
                    var body = GetBounds(wrapper);
                    box.center = body.center;
                    box.size = body.size;
                }
                else
                {
                    foreach (var filter in wrapper.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                        var collider = filter.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = filter.sharedMesh;
                        collider.convex = false;
                    }
                }
                PrefabUtility.SaveAsPrefabAssetAndConnect(wrapper, prefabPath, InteractionMode.AutomatedAction);
            }
            wrapper.name = name;
            wrapper.transform.SetParent(parent, true);
            wrapper.transform.position = position;
            return wrapper;
        }

        static PropMetrics MeasureProp(GameObject prop, string source, string prefabName)
        {
            var bounds = GetBounds(prop);
            var metrics = new PropMetrics
            {
                name = prop.name, source = source, prefab = Root + "/Prefabs/" + prefabName + ".prefab",
                position = prop.transform.position, boundsCenter = bounds.center, boundsSize = bounds.size,
                modelScale = prop.transform.childCount > 0 ? prop.transform.GetChild(0).localScale : Vector3.one,
                colliders = prop.GetComponentsInChildren<Collider>(true).Length
            };
            var materials = new HashSet<Material>();
            var textures = new HashSet<Texture>();
            foreach (var renderer in prop.GetComponentsInChildren<Renderer>(true))
            {
                metrics.renderers++;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !materials.Add(material)) continue;
                    metrics.materials.Add(material.name + " [" + (material.shader != null ? material.shader.name : "missing shader") + "]");
                    foreach (var property in material.GetTexturePropertyNames())
                    {
                        var texture = material.GetTexture(property);
                        if (texture != null && textures.Add(texture)) metrics.textures.Add(texture.name);
                    }
                }
            }
            foreach (var filter in prop.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    if (mesh.GetTopology(submesh) == MeshTopology.Triangles)
                        metrics.triangles += (int)(mesh.GetIndexCount(submesh) / 3);
            }
            return metrics;
        }

        public static string Validate()
        {
            var report = new Report { status = "verified_static" };
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                report.status = "scene_not_loaded";
                report.errors.Add("Load " + ScenePath + " before validation.");
                return WriteReport(report);
            }
            var meshes = new HashSet<Mesh>();
            var materials = new HashSet<Material>();
            GameObject architecture = null;
            var activeCameras = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "MarsBase_Architecture") architecture = root;
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    report.missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                    if (!Finite(transform.position) || !Finite(transform.lossyScale)) report.invalidBounds++;
                }
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    report.renderers++;
                    if (!Finite(renderer.bounds.center) || !Finite(renderer.bounds.size)) report.invalidBounds++;
                    var slots = renderer.sharedMaterials;
                    if (slots.Length == 0) report.missingMaterials++;
                    foreach (var material in slots)
                    {
                        if (material == null || material.shader == null) { report.missingMaterials++; continue; }
                        materials.Add(material);
                        if (material.shader.name == "Hidden/InternalErrorShader")
                            report.errors.Add("Error shader: " + renderer.name + " / " + material.name);
                    }
                }
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    report.meshFilters++;
                    var mesh = filter.sharedMesh;
                    if (mesh == null) { report.missingMeshes++; continue; }
                    meshes.Add(mesh);
                    for (var s = 0; s < mesh.subMeshCount; s++)
                        if (mesh.GetTopology(s) == MeshTopology.Triangles)
                            report.triangles += (int)(mesh.GetIndexCount(s) / 3);
                }
                foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                {
                    report.colliders++;
                    if (collider is MeshCollider meshCollider && meshCollider.sharedMesh == null)
                        report.errors.Add("MeshCollider has no mesh: " + collider.name);
                    if (!Finite(collider.bounds.center) || !Finite(collider.bounds.size)) report.invalidBounds++;
                }
                foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                    if (camera.enabled && camera.gameObject.activeInHierarchy) activeCameras++;
            }
            report.uniqueMeshes = meshes.Count;
            report.materials = materials.Count;
            if (architecture != null)
            {
                var bounds = GetBounds(architecture);
                report.architectureCenter = bounds.center;
                report.architectureSize = bounds.size;
                report.importedRootScale = architecture.transform.localScale;
                var width = Mathf.Max(bounds.size.x, bounds.size.z);
                if (width < 20 || width > 150)
                    report.warnings.Add("Architecture width is " + width.ToString("F2") + " m; verify Blender export unit scale.");
            }
            else report.errors.Add("Architecture root is missing.");
            if (report.missingScripts != 0) report.errors.Add("Missing script components: " + report.missingScripts);
            if (report.missingMeshes != 0) report.errors.Add("Missing meshes: " + report.missingMeshes);
            if (report.missingMaterials != 0) report.errors.Add("Missing material slots: " + report.missingMaterials);
            if (report.invalidBounds != 0) report.errors.Add("Non-finite transforms or bounds: " + report.invalidBounds);
            if (report.colliders == 0) report.errors.Add("No colliders found.");
            if (activeCameras != 1) report.warnings.Add("Dedicated scene has " + activeCameras + " enabled cameras.");
            report.warnings.Add("Static editor validation only; Play Mode and player builds were not run.");
            if (report.errors.Count > 0) report.status = "validation_failed";
            return WriteReport(report);
        }

        static string WriteReport(Report report)
        {
            var json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, json);
            return json;
        }

        static void EnsureFolders()
        {
            foreach (var child in new[] { "Materials", "Textures", "Prefabs", "Scenes", "Settings" })
                Directory.CreateDirectory(Root + "/" + child);
            AssetDatabase.Refresh();
        }

        static Dictionary<string, Material> CreateMaterials()
        {
            var painted = CreateSurfaceTexture("MB_PaintedWeathering", false);
            var soil = CreateSurfaceTexture("MB_SandGrain", true);
            var result = new Dictionary<string, Material>(StringComparer.Ordinal);
            result["MB_Ceramic"] = Material("MB_Ceramic", new Color(.76f, .72f, .61f), .12f, .36f, painted);
            result["MB_Orange"] = Material("MB_Orange", new Color(.75f, .19f, .055f), .16f, .3f, painted);
            result["MB_Metal"] = Material("MB_Metal", new Color(.35f, .34f, .30f), .75f, .39f, painted);
            result["MB_Dark"] = Material("MB_Dark", new Color(.075f, .095f, .105f), .45f, .35f, painted);
            result["MB_Glass"] = Material("MB_Glass", new Color(.25f, .43f, .40f, .16f), 0, .5f, null);
            ConfigureGlass(result["MB_Glass"]);
            result["MB_WindowGlow"] = Material("MB_WindowGlow", new Color(.82f, .52f, .22f), .05f, .55f, null);
            result["MB_WindowGlow"].SetColor("_EmissionColor", new Color(1.5f, .78f, .29f));
            result["MB_WindowGlow"].EnableKeyword("_EMISSION");
            result["MB_WindowGlow"].globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            result["MB_Green"] = Material("MB_Green", new Color(.18f, .27f, .19f), .16f, .32f, painted);
            result["MB_GreenLight"] = Material("MB_GreenLight", new Color(.39f, .53f, .19f), 0, .27f, painted);
            result["MB_Sign"] = Material("MB_Sign", new Color(.93f, .88f, .72f), .05f, .3f, null);
            result["MB_Blue"] = Material("MB_Blue", new Color(.035f, .075f, .15f), .5f, .6f, null);
            result["MB_RedLight"] = Material("MB_RedLight", new Color(.75f, .045f, .015f), .05f, .55f, null);
            result["MB_RedLight"].SetColor("_EmissionColor", new Color(2.1f, .10f, .025f));
            result["MB_RedLight"].EnableKeyword("_EMISSION");
            result["MB_RedLight"].globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            result["MB_RedSoil"] = Material("MB_RedSoil", new Color(.44f, .22f, .125f), 0, .07f, soil);
            result["MB_RedSoil"].SetTextureScale("_BaseMap", new Vector2(1000, 1000));
            foreach (var material in result.Values) EditorUtility.SetDirty(material);
            return result;
        }

        static void ConfigureGlass(Material material)
        {
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_BlendModePreserveSpecular", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_SpecularHighlights", 0);
            material.SetFloat("_EnvironmentReflections", 0);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.SetShaderPassEnabled("DepthOnly", false);
            material.renderQueue = (int)RenderQueue.Transparent;
            material.doubleSidedGI = true;
        }

        static Material Material(string name, Color color, float metallic, float smoothness, Texture2D texture)
        {
            var path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetTexture("_BaseMap", texture);
            material.enableInstancing = true;
            return material;
        }

        static Texture2D CreateSurfaceTexture(string name, bool sand)
        {
            var path = Root + "/Textures/" + name + ".png";
            if (!File.Exists(path))
            {
                const int size = 512;
                var texture = new Texture2D(size, size, TextureFormat.RGB24, true);
                var pixels = new Color[size * size];
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var u = (float)x / size;
                    var v = (float)y / size;
                    var coarse = TileNoise(u, v, sand ? 7 : 4);
                    var fine = TileNoise(u, v, sand ? 160 : 90);
                    var streak = TileNoise(u, v, 30, 3);
                    var value = sand ? .69f + coarse * .21f + fine * .1f
                        : .87f + coarse * .07f + fine * .035f + streak * .025f;
                    pixels[y * size + x] = new Color(value, value, value);
                }
                texture.SetPixels(pixels);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static float TileNoise(float u, float v, float frequency, float verticalFrequency = 0)
        {
            var fy = verticalFrequency > 0 ? verticalFrequency : frequency;
            var a = Mathf.PerlinNoise(u * frequency + 13.1f, v * fy + 7.7f);
            var b = Mathf.PerlinNoise((u - 1) * frequency + 13.1f, v * fy + 7.7f);
            var c = Mathf.PerlinNoise(u * frequency + 13.1f, (v - 1) * fy + 7.7f);
            var d = Mathf.PerlinNoise((u - 1) * frequency + 13.1f, (v - 1) * fy + 7.7f);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        static void AddArchitectureColliders(GameObject root)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                var name = filter.name.ToLowerInvariant();
                var size = filter.GetComponent<Renderer>()?.bounds.size ?? Vector3.zero;
                if (Mathf.Max(size.x, size.y, size.z) < 1.2f) continue;
                // The Blender export batches each architectural module by material, e.g.
                // HAB-02_West__MB_Ceramic. Ceramic carries the main pressure hulls and corridor shells.
                var mainShell = name.Contains("__mb_ceramic");
                var greenhouseShell = name.StartsWith("bio-", StringComparison.Ordinal) && name.Contains("__mb_glass");
                var siteWalkway = name.StartsWith("site-", StringComparison.Ordinal) &&
                    (name.Contains("__mb_metal") || name.Contains("__mb_dark"));
                var namedStructure = name.Contains("deck") || name.Contains("platform") || name.Contains("ramp") ||
                      name.Contains("stair") || name.Contains("floor") || name.Contains("hull") ||
                      name.Contains("shell") || name.Contains("body") || name.Contains("foundation") ||
                      name.Contains("corridor") || name.Contains("habitat");
                if (!(mainShell || greenhouseShell || siteWalkway || namedStructure)) continue;
                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
            }
        }

        static void CreatePresentationSurface(Bounds bounds, Material material)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Presentation_Surface_Flat_Mars_Sand";
            plane.transform.position = new Vector3(bounds.center.x, -.045f, bounds.center.z);
            var span = Mathf.Max(4000, Mathf.Max(bounds.size.x, bounds.size.z) * 3.2f);
            plane.transform.localScale = new Vector3(span / 10, 1, span / 10);
            plane.GetComponent<Renderer>().sharedMaterial = material;
        }

        static void CreateLighting(Bounds bounds)
        {
            ConfigureSky();
            ConfigureAmbient();
            var sun = new GameObject("Sun_Late_Martian_Afternoon").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1, .78f, .55f);
            sun.intensity = 2.0f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .82f;
            sun.shadowBias = .035f;
            sun.shadowNormalBias = .22f;
            sun.transform.rotation = Quaternion.Euler(26, 225, 0);
            sun.gameObject.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = sun;
            var fill = new GameObject("Sky_Bounce_Cool_Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(.55f, .68f, .86f);
            fill.intensity = .45f;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(38, -40, 0);
            fill.gameObject.AddComponent<UniversalAdditionalLightData>();
            var probe = new GameObject("Base_Reflection_Probe").AddComponent<ReflectionProbe>();
            probe.transform.position = bounds.center + Vector3.up * 3;
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.resolution = 128;
            probe.size = bounds.size + Vector3.one * 15;
            probe.boxProjection = true;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.backgroundColor = new Color(.4f, .36f, .32f);
        }

        static void ConfigureSky()
        {
            var panorama = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/MB_MarsDuskPanoramic.mat");
            if (panorama != null && panorama.HasProperty("_MainTex") && panorama.GetTexture("_MainTex") != null)
            {
                RenderSettings.skybox = panorama;
                return;
            }
            var dustShader = Shader.Find("MarsBase/DustSky");
            if (dustShader != null)
            {
                var path = Root + "/Materials/MB_MartianSunsetSky.mat";
                var dust = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (dust == null)
                {
                    dust = new Material(dustShader) { name = "MB_MartianSunsetSky" };
                    AssetDatabase.CreateAsset(dust, path);
                }
                dust.shader = dustShader;
                dust.SetColor("_TopColor", new Color(.42f, .23f, .15f));
                dust.SetColor("_HorizonColor", new Color(.85f, .48f, .26f));
                dust.SetColor("_GroundColor", new Color(.25f, .12f, .07f));
                dust.SetVector("_SunDirection", new Vector4(-.5f, .3f, .8f, 0));
                dust.SetColor("_SunColor", new Color(5, 3.5f, 2, 1));
                EditorUtility.SetDirty(dust);
                AssetDatabase.SaveAssetIfDirty(dust);
                RenderSettings.skybox = dust;
                return;
            }
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                var skyPath = Root + "/Materials/MB_MartianSunsetSky.mat";
                var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
                if (sky == null)
                {
                    sky = new Material(skyShader) { name = "MB_MartianSunsetSky" };
                    AssetDatabase.CreateAsset(sky, skyPath);
                }
                sky.SetFloat("_SunDisk", 2);
                sky.SetFloat("_SunSize", .035f);
                sky.SetFloat("_SunSizeConvergence", 5);
                sky.SetFloat("_AtmosphereThickness", .8f);
                sky.SetColor("_SkyTint", new Color(.54f, .52f, .47f));
                sky.SetColor("_GroundColor", new Color(.37f, .25f, .18f));
                sky.SetFloat("_Exposure", 1.05f);
                sky.EnableKeyword("_SUNDISK_HIGH_QUALITY");
                EditorUtility.SetDirty(sky);
                RenderSettings.skybox = sky;
            }
        }

        static void ConfigureAmbient()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.55f, .48f, .42f);
            RenderSettings.ambientEquatorColor = new Color(.43f, .34f, .27f);
            RenderSettings.ambientGroundColor = new Color(.23f, .16f, .11f);
            RenderSettings.ambientIntensity = 1;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            var panoramicHaze = RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_HazeColor");
            RenderSettings.fogColor = panoramicHaze
                ? ((Color)RenderSettings.skybox.GetVector("_HazeColor")).gamma
                : new Color(.51f, .38f, .30f);
            RenderSettings.fogDensity = .0022f;
        }

        static void CreateCameras(Bounds bounds)
        {
            var span = Mathf.Max(bounds.size.x, bounds.size.z);
            var target = new Vector3(bounds.center.x, Mathf.Max(3, bounds.center.y * .75f), bounds.center.z);
            var hero = Camera("Main Camera - Mars Base", new Vector3(-50, 18, 80), new Vector3(0, 7, -1), 32);
            hero.tag = "MainCamera";
            hero.depth = 10;
            hero.gameObject.AddComponent<AudioListener>();
            var overview = Camera("Overview Camera - Disabled", target + new Vector3(.9f, 1.05f, 1.05f) * span, target, 43);
            overview.enabled = false;
        }

        static Camera Camera(string name, Vector3 position, Vector3 target, float fieldOfView)
        {
            var camera = new GameObject(name).AddComponent<Camera>();
            camera.transform.position = position;
            camera.transform.LookAt(target);
            camera.fieldOfView = fieldOfView;
            camera.aspect = 16f / 9f;
            camera.nearClipPlane = .15f;
            camera.farClipPlane = 5000;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(.51f, .38f, .30f);
            camera.allowHDR = true;
            camera.allowMSAA = true;
            var additional = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            additional.renderPostProcessing = true;
            additional.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            additional.antialiasingQuality = AntialiasingQuality.High;
            additional.requiresDepthTexture = true;
            return camera;
        }

        static void CreateVolume()
        {
            var path = Root + "/Settings/MarsBase_SunsetVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "MarsBase_SunsetVolume";
                AssetDatabase.CreateAsset(profile, path);
                var tone = profile.Add<Tonemapping>(true);
                tone.mode.Override(TonemappingMode.ACES);
                var bloom = profile.Add<Bloom>(true);
                bloom.threshold.Override(1.1f);
                bloom.intensity.Override(.18f);
                bloom.scatter.Override(.48f);
                var grade = profile.Add<ColorAdjustments>(true);
                grade.postExposure.Override(.1f);
                grade.contrast.Override(8);
                grade.saturation.Override(-5);
                foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
                EditorUtility.SetDirty(profile);
            }
            var volume = new GameObject("Mars Base - Sunset Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;
            volume.sharedProfile = profile;
        }

        static Bounds GetBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (i == 0) bounds = renderers[i].bounds;
                else bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        static bool Finite(Vector3 value)
        {
            return !(float.IsNaN(value.x) || float.IsInfinity(value.x) || float.IsNaN(value.y) ||
                float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));
        }
    }
}
#endif
