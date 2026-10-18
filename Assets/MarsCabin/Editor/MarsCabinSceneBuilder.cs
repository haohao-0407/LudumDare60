#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LudumDare60.MarsCabin.Editor
{
    /// <summary>Isolated cabin art/import tools. Does not change an existing game scene or install an input controller.</summary>
    public static class MarsCabinSceneBuilder
    {
        public const string Root = "Assets/MarsCabin";
        public const string ScenePath = Root + "/Scenes/MarsCabin_Interior.unity";
        public const string ModelPath = Root + "/Models/Cabin_Architecture.fbx";
        public const string PrefabPath = Root + "/Prefabs/Cabin_Architecture.prefab";
        public const string HeroModelPath = Root + "/Models/Cabin_HeroConsole.fbx";
        public const string HeroPrefabPath = Root + "/Prefabs/Cabin_HeroConsole.prefab";
        const string SourceRoot = "SourceArt/MarsCabin";

        [Serializable] public class ControlEntry { public string name, kind; public float[] pivot, worldAxis, secondaryWorldAxis; public float travel, rangeDegrees, minimumAngle, maximumAngle; public bool buttonUsesRotation; }
        [Serializable] public class InteractionManifest { public ControlEntry[] controls; }
        [Serializable] public class ValidationReport
        {
            public string status, scene = ScenePath;
            public int renderers, meshFilters, triangles, colliders, controls, screens, missingScripts;
            public Vector3 size, basisX, basisY, basisZ;
            public bool ceilingVisible, heroAuthored;
            public int decorativeMeshyProps, heroControls;
            public List<string> errors = new List<string>();
            public List<string> warnings = new List<string>();
        }
        [Serializable] public class PropMetric
        {
            public string name, asset;
            public Vector3 position, size, scale;
            public int triangles, materials, colliders;
        }
        [Serializable] public class PropReport { public List<PropMetric> props = new List<PropMetric>(); }

        struct Basis
        {
            public Vector3 origin, x, y, z;
            public Vector3 Point(Vector3 value) => origin + Direction(value);
            public Vector3 Direction(Vector3 value) => x * value.x + y * value.y + z * value.z;
            public Vector3 MetricPoint(Vector3 value) => origin + x.normalized * value.x + y.normalized * value.y + z.normalized * value.z;
        }

        [MenuItem("Tools/Mars Cabin/Build Interior Scene")]
        static void BuildMenu() { Debug.Log(Build()); }

        [MenuItem("Tools/Mars Cabin/Refresh Blender Hero Console")]
        static void HeroMenu() { Debug.Log(PlaceHeroConsole()); }

        [MenuItem("Tools/Mars Cabin/Place Decorative Meshy Props")]
        static void DecorationMenu() { Debug.Log(PlaceMeshyProps()); }

        public static string Build()
        {
            RequireEditMode();
            if (File.Exists(ScenePath)) return "{\"status\":\"existing_scene_preserved\"}";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null) throw new FileNotFoundException("Import the Blender cabin architecture first.", ModelPath);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(HeroModelPath) == null) throw new FileNotFoundException("Import the Blender-authored hero console first.", HeroModelPath);
            EnsureFolders();
            var materials = CreateMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var architecture = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            architecture.name = "Cabin_Architecture";
            architecture.transform.position = Vector3.zero;
            RemapArchitecture(architecture, materials);
            var basis = ReadBasis(architecture);
            AnnotateControls(architecture, basis);
            BindScreens(architecture, materials["MC_Screen"]);
            AddStructureColliders(architecture);
            PrefabUtility.SaveAsPrefabAssetAndConnect(architecture, PrefabPath, InteractionMode.AutomatedAction);
            new GameObject("Meshy_Cabin_Props");
            CreateLighting(basis);
            CreateCameras(basis);
            CreateVolume();
            UpdatePresentation(basis, scene);
            SaveScene(scene);
            foreach (var material in materials.Values) AssetDatabase.SaveAssetIfDirty(material);
            Selection.activeGameObject = architecture;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(WorldBounds(architecture), false);
            PlaceHeroConsole();
            PlaceMeshyProps();
            return Validate();
        }

        public static string RefreshArchitecture()
        {
            RequireEditMode();
            var scene = RequiredScene();
            var architecture = SceneRoot(scene, "Cabin_Architecture");
            var materials = CreateMaterials();
            RemapArchitecture(architecture, materials);
            AnnotateControls(architecture, ReadBasis(architecture));
            BindScreens(architecture, materials["MC_Screen"]);
            AddStructureColliders(architecture);
            PrefabUtility.ApplyPrefabInstance(architecture, InteractionMode.AutomatedAction);
            foreach (var material in materials.Values) AssetDatabase.SaveAssetIfDirty(material);
            SaveScene(scene);
            return Validate();
        }

        /// <summary>Imports only decorative generated assets. All primary controls are authored in Blender.</summary>
        public static string PlaceMeshyProps()
        {
            RequireEditMode();
            var scene = RequiredScene();
            var basis = ReadBasis(SceneRoot(scene, "Cabin_Architecture"));
            var parent = SceneRoot(scene, "Meshy_Cabin_Props").transform;
            var names = new[] { "CommsRack_T25", "ReelRecorder_T25", "PilotSeat_T25" };
            foreach (var name in names)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/Meshy/" + name + ".glb") == null)
                    throw new FileNotFoundException("Import the decorative Meshy prop: " + name);
            PlaceProp(names[0], new Vector3(-2.8f, 0, 0), 1.60f, 1, -90, basis, parent, scene);
            PlaceProp(names[1], new Vector3(2.8f, 0, 0), 1.20f, 1, 90, basis, parent, scene);
            PlaceProp(names[2], new Vector3(0, -.2f, 0), 1.62f, 1, 0, basis, parent, scene);
            foreach (var name in names) RemoveGeneratedInteractions(parent.Find(name).gameObject);
            FitPresentation();
            var report = new PropReport { props = names.Select(name => PlaceProp(name, Vector3.zero, 1, 0, 0, basis, parent, scene)).ToList() };
            var json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(SourceRoot); File.WriteAllText(SourceRoot + "/meshy_unity_metrics.json", json);
            return json;
        }

        public static string PlaceHeroConsole()
        {
            RequireEditMode();
            var scene = RequiredScene();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(HeroModelPath);
            if (source == null) throw new FileNotFoundException("The primary console must be authored in Blender before replacing the old generated props.", HeroModelPath);
            var materials = CreateMaterials();
            var hero = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Cabin_HeroConsole");
            if (hero == null) { hero = (GameObject)PrefabUtility.InstantiatePrefab(source, scene); hero.name = "Cabin_HeroConsole"; }
            // Unity's FBX root carries the centimetre and axis conversion even though the Blender authoring root is identity.
            hero.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            hero.transform.localScale = source.transform.localScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(hero.transform);
            var basis = ReadBasis(hero, "ORIENTATION_HERO_");
            if (Mathf.Abs(basis.x.magnitude - 1) > .01f || Mathf.Abs(basis.y.magnitude - 1) > .01f || Mathf.Abs(basis.z.magnitude - 1) > .01f)
                throw new InvalidOperationException("Hero console must retain metric scale independently from the 0.8 cabin architecture.");
            RemapArchitecture(hero, materials);
            AnnotateControls(hero, basis, SourceRoot + "/hero_interaction_manifest.json", "cabin.hero.");
            BindScreens(hero, materials["MC_Screen"]);
            hero.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            hero.transform.localScale = source.transform.localScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(hero.transform);
            foreach (var filter in hero.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null || filter.GetComponentInParent<CabinInteractable>() != null || filter.name.StartsWith("SCREEN_", StringComparison.Ordinal)) continue;
                var renderer = filter.GetComponent<Renderer>();
                if (renderer == null || Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.y, renderer.bounds.size.z) < .2f) continue;
                var collider = filter.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = filter.sharedMesh; collider.convex = false;
            }
            foreach (var control in hero.GetComponentsInChildren<CabinInteractable>(true))
                if (control.Kind == CabinControlKind.Joystick) TrimJoystickHitbox(control.transform);
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(hero) == HeroPrefabPath) PrefabUtility.ApplyPrefabInstance(hero, InteractionMode.AutomatedAction);
            else PrefabUtility.SaveAsPrefabAssetAndConnect(hero, HeroPrefabPath, InteractionMode.AutomatedAction);
            // Remove only the superseded generated foreground assets. Their source GLBs and old prefabs remain available.
            var props = SceneRoot(scene, "Meshy_Cabin_Props").transform;
            foreach (var name in new[] { "NavConsole_T25", "ThrottlePod_T25", "JoystickPod_T25" })
            { var old = props.Find(name); if (old != null) Object.DestroyImmediate(old.gameObject); }
            var oldMounts = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Cabin_Equipment_Mounts");
            if (oldMounts != null) Object.DestroyImmediate(oldMounts);
            foreach (Transform prop in props) { RemoveGeneratedInteractions(prop.gameObject); SaveWrapperPrefab(prop.gameObject); }
            foreach (var material in materials.Values) AssetDatabase.SaveAssetIfDirty(material);
            SaveScene(scene);
            return Validate();
        }

        static void RemoveGeneratedInteractions(GameObject decorativeProp)
        {
            foreach (var component in decorativeProp.GetComponentsInChildren<CabinInteractable>(true)) Object.DestroyImmediate(component);
        }

        public static string FitPresentation()
        {
            RequireEditMode();
            var scene = RequiredScene();
            var basis = ReadBasis(SceneRoot(scene, "Cabin_Architecture"));
            var props = SceneRoot(scene, "Meshy_Cabin_Props").transform;
            var comms = props.Find("CommsRack_T25"); var reel = props.Find("ReelRecorder_T25"); var seat = props.Find("PilotSeat_T25");
            if (comms != null) comms.position = basis.Point(new Vector3(-2.8f, 0, 0)) + Vector3.up * .025f;
            if (reel != null) reel.position = basis.Point(new Vector3(2.8f, 0, 0)) + Vector3.up * .025f;
            if (seat != null) seat.position = basis.Point(new Vector3(0, -.2f, 0));
            var bounce = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Cabin_Global_Bounce");
            if (bounce == null) { bounce = new GameObject("Cabin_Global_Bounce"); SceneManager.MoveGameObjectToScene(bounce, scene); bounce.AddComponent<Light>(); }
            var light = bounce.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = .65f;
            light.color = new Color(1, .85f, .68f); light.shadows = LightShadows.None; bounce.transform.rotation = Quaternion.Euler(45, 150, 0);
            UpdatePresentation(basis, scene);
            foreach (Transform prop in props) SaveWrapperPrefab(prop.gameObject);
            SaveScene(scene);
            return Validate();
        }

        static void TrimJoystickHitbox(Transform grip)
        {
            // The lower grip remains selectable while the trigger and top button have unobstructed hitboxes.
            var box = grip.GetComponent<BoxCollider>();
            var worldCenter = grip.position + Vector3.up * .105f;
            var bounds = new Bounds(grip.InverseTransformPoint(worldCenter), Vector3.zero);
            for (var i = 0; i < 8; i++)
                bounds.Encapsulate(grip.InverseTransformPoint(worldCenter + new Vector3((i & 1) == 0 ? -.042f : .042f,
                    (i & 2) == 0 ? -.055f : .055f, (i & 4) == 0 ? -.04f : .04f)));
            box.center = bounds.center; box.size = bounds.size; box.isTrigger = true;
        }

        static void UpdatePresentation(Basis basis, Scene scene)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.34f, .30f, .24f);
            RenderSettings.ambientEquatorColor = new Color(.29f, .25f, .20f);
            RenderSettings.ambientGroundColor = new Color(.16f, .14f, .11f);
            foreach (var root in scene.GetRootGameObjects()) foreach (var fixture in root.GetComponentsInChildren<Light>())
            {
                if (fixture.name.StartsWith("Warm_Ceiling_Fixture_", StringComparison.Ordinal))
                {
                    var index = int.Parse(fixture.name.Substring(fixture.name.Length - 2));
                    var angle = Mathf.PI * 2 * index / 6;
                    fixture.transform.position = basis.Point(new Vector3(Mathf.Cos(angle) * 2.65f, Mathf.Sin(angle) * 2.65f, 2.95f));
                    fixture.transform.LookAt(basis.Point(new Vector3(Mathf.Cos(angle) * 1.15f, Mathf.Sin(angle) * 1.15f, .4f)));
                    fixture.intensity = 4; fixture.shadowStrength = .35f;
                }
                else if (fixture.name == "Cabin_Central_Bounce")
                { fixture.transform.position = basis.Point(new Vector3(0, 0, 2.8f)); fixture.intensity = 4; }
            }
            foreach (var root in scene.GetRootGameObjects()) foreach (var camera in root.GetComponentsInChildren<Camera>())
            {
                Vector3 position, target; float fov;
                switch (camera.name)
                {
                    case "Cabin Presentation Camera": position = basis.MetricPoint(new Vector3(.45f, -3.03f, 2.5f)); target = basis.MetricPoint(new Vector3(0, .35f, .98f)); fov = 82; break;
                    case "Pilot Eye Camera": position = basis.MetricPoint(new Vector3(0, -.2f, 1.5f)); target = basis.MetricPoint(new Vector3(0, -2.1f, 1.05f)); fov = 76; break;
                    case "Rear View Camera": position = basis.Point(new Vector3(0, 3.25f, 2.9f)); target = basis.Point(new Vector3(0, -.7f, 1.3f)); fov = 84; break;
                    case "Ceiling Detail Camera": position = basis.MetricPoint(new Vector3(.3f, -.4f, 1.3f)); target = basis.Point(new Vector3(0, 0, 3.6f)); fov = 95; break;
                    case "Model Inspection Camera": position = basis.MetricPoint(new Vector3(1.4f, -1.35f, 1.7f)); target = basis.MetricPoint(new Vector3(.8f, -.45f, 1.13f)); fov = 36; break;
                    default: continue;
                }
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position)); camera.fieldOfView = fov;
            }
            var volume = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Volume>()).FirstOrDefault();
            if (volume != null && volume.sharedProfile != null && volume.sharedProfile.TryGet<ColorAdjustments>(out var color))
            { color.contrast.Override(0); color.postExposure.Override(.15f); EditorUtility.SetDirty(volume.sharedProfile); AssetDatabase.SaveAssetIfDirty(volume.sharedProfile); }
        }

        static void SaveWrapperPrefab(GameObject prop)
        {
            var position = prop.transform.position; var rotation = prop.transform.rotation; var parent = prop.transform.parent;
            prop.transform.SetParent(null, true); prop.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            PrefabUtility.ApplyPrefabInstance(prop, InteractionMode.AutomatedAction);
            prop.transform.SetParent(parent, true); prop.transform.SetPositionAndRotation(position, rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(prop.transform);
        }

        // axis 0 normalizes unrotated width; axis 1 normalizes height. Source yaw is exposed on the prefab instance for later art adjustment.
        static PropMetric PlaceProp(string name, Vector3 blenderPosition, float dimension, int axis, float yaw,
            Basis basis, Transform parent, Scene scene)
        {
            var assetPath = Root + "/Models/Meshy/" + name + ".glb";
            var existing = parent.Find(name);
            GameObject wrapper;
            if (existing != null) wrapper = existing.gameObject;
            else
            {
                wrapper = new GameObject(name);
                SceneManager.MoveGameObjectToScene(wrapper, scene);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                model.name = "Meshy_T25_OriginalPBR";
                model.transform.SetParent(wrapper.transform, false);
                var initial = WorldBounds(wrapper);
                var measure = axis == 1 ? initial.size.y : initial.size.x;
                if (measure < .0001f) throw new InvalidOperationException("Invalid Meshy bounds: " + name);
                model.transform.localScale *= dimension / measure;
                var scaled = WorldBounds(wrapper);
                model.transform.position += new Vector3(-scaled.center.x, .005f - scaled.min.y, -scaled.center.z);
                var box = wrapper.AddComponent<BoxCollider>();
                var colliderBounds = WorldBounds(wrapper);
                box.center = colliderBounds.center;
                box.size = colliderBounds.size;
                PrefabUtility.SaveAsPrefabAssetAndConnect(wrapper, Root + "/Prefabs/" + name + ".prefab", InteractionMode.AutomatedAction);
                wrapper.transform.SetParent(parent, true);
                wrapper.transform.position = basis.Point(blenderPosition);
                wrapper.transform.rotation = Quaternion.Euler(0, yaw, 0);
            }
            var bounds = WorldBounds(wrapper);
            var metric = new PropMetric { name = name, asset = assetPath, position = wrapper.transform.position, size = bounds.size,
                scale = wrapper.transform.GetChild(0).localScale, colliders = wrapper.GetComponentsInChildren<Collider>(true).Length };
            var mats = new HashSet<Material>();
            foreach (var renderer in wrapper.GetComponentsInChildren<Renderer>(true)) foreach (var mat in renderer.sharedMaterials) if (mat != null) mats.Add(mat);
            metric.materials = mats.Count;
            foreach (var filter in wrapper.GetComponentsInChildren<MeshFilter>(true)) metric.triangles += TriangleCount(filter.sharedMesh);
            return metric;
        }

        static void RemapArchitecture(GameObject architecture, Dictionary<string, Material> materials)
        {
            foreach (var renderer in architecture.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (var i = 0; i < slots.Length; i++)
                    if (slots[i] != null && materials.TryGetValue(slots[i].name.Replace(" (Instance)", ""), out var material)) slots[i] = material;
                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        static void AnnotateControls(GameObject architecture, Basis basis, string manifestPath = null, string idPrefix = "cabin.arch.")
        {
            var entries = new Dictionary<string, ControlEntry>(StringComparer.Ordinal);
            var path = manifestPath ?? SourceRoot + "/interaction_manifest.json";
            if (File.Exists(path))
            {
                var manifest = JsonUtility.FromJson<InteractionManifest>(File.ReadAllText(path));
                if (manifest?.controls != null) foreach (var entry in manifest.controls) if (!string.IsNullOrEmpty(entry.name)) entries[entry.name] = entry;
            }
            foreach (var pivot in architecture.GetComponentsInChildren<Transform>(true))
            {
                if (!pivot.name.StartsWith("INT_", StringComparison.Ordinal)) continue;
                var kind = InferKind(pivot.name);
                entries.TryGetValue(pivot.name, out var entry);
                if (entry != null && Enum.TryParse<CabinControlKind>(entry.kind, true, out var declaredKind)) kind = declaredKind;
                var axis = kind == CabinControlKind.Toggle ? Vector3.right : Vector3.forward;
                var angleRange = kind == CabinControlKind.Knob ? 270f : 50f;
                var travel = .008f;
                if (entry != null && entry.worldAxis != null && entry.worldAxis.Length == 3)
                {
                    axis = (Quaternion.Inverse(NeutralWorldRotation(pivot)) * basis.Direction(new Vector3(entry.worldAxis[0], entry.worldAxis[1], entry.worldAxis[2]))).normalized;
                    if (entry.rangeDegrees > 0) angleRange = entry.rangeDegrees;
                    if (entry.travel > 0) travel = entry.travel;
                }
                var secondaryAxis = Vector3.right;
                if (entry?.secondaryWorldAxis != null && entry.secondaryWorldAxis.Length == 3)
                    secondaryAxis = (Quaternion.Inverse(NeutralWorldRotation(pivot)) * basis.Direction(new Vector3(entry.secondaryWorldAxis[0], entry.secondaryWorldAxis[1], entry.secondaryWorldAxis[2]))).normalized;
                var minAngle = entry != null && entry.minimumAngle != entry.maximumAngle ? entry.minimumAngle : -angleRange * .5f;
                var maxAngle = entry != null && entry.minimumAngle != entry.maximumAngle ? entry.maximumAngle : angleRange * .5f;
                var box = pivot.GetComponent<BoxCollider>();
                if (box == null) box = pivot.gameObject.AddComponent<BoxCollider>();
                var bounds = LocalBounds(pivot.gameObject);
                box.center = bounds.center;
                var scale = pivot.lossyScale;
                var minimumSize = new Vector3(.035f / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                    .035f / Mathf.Max(.0001f, Mathf.Abs(scale.y)), .035f / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
                box.size = Vector3.Max(bounds.size, minimumSize);
                box.isTrigger = true;
                var control = pivot.GetComponent<CabinInteractable>();
                if (control == null)
                {
                    control = pivot.gameObject.AddComponent<CabinInteractable>();
                    control.Configure(idPrefix + pivot.name.ToLowerInvariant(), kind, pivot, axis, secondaryAxis,
                        minAngle, maxAngle, travel, 1.8f, entry != null && entry.buttonUsesRotation);
                }
                else
                {
                    // Sync revised authoring dimensions without capturing an already rotated toggle as its rest pose.
                    var serialized = new SerializedObject(control);
                    serialized.FindProperty("localAxis").vector3Value = axis;
                    serialized.FindProperty("secondaryLocalAxis").vector3Value = secondaryAxis;
                    serialized.FindProperty("minimumAngle").floatValue = minAngle;
                    serialized.FindProperty("maximumAngle").floatValue = maxAngle;
                    serialized.FindProperty("buttonUsesRotation").boolValue = entry != null && entry.buttonUsesRotation;
                    serialized.FindProperty("buttonTravel").floatValue = travel;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                // Reimport can reset the FBX transform while leaving the serialized control value intact.
                // Reapply that value and persist the pose so edit-mode reload matches runtime Awake.
                control.SetNormalizedValue(control.NormalizedValue);
                control.SetJoystick(control.JoystickValue);
                EditorUtility.SetDirty(control);
                PrefabUtility.RecordPrefabInstancePropertyModifications(control);
                if (control.MotionPivot != null)
                {
                    EditorUtility.SetDirty(control.MotionPivot);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(control.MotionPivot);
                }
            }
        }

        static CabinControlKind InferKind(string name)
        {
            if (name.StartsWith("INT_KNOB_", StringComparison.Ordinal)) return CabinControlKind.Knob;
            if (name.StartsWith("INT_TOGGLE_", StringComparison.Ordinal)) return CabinControlKind.Toggle;
            if (name.StartsWith("INT_LEVER_", StringComparison.Ordinal)) return CabinControlKind.Lever;
            if (name.StartsWith("INT_JOYSTICK_", StringComparison.Ordinal)) return CabinControlKind.Joystick;
            return CabinControlKind.Button;
        }

        static Quaternion NeutralWorldRotation(Transform transform)
        {
            var local = transform.localRotation;
            var control = transform.GetComponent<CabinInteractable>();
            if (control != null)
            {
                var serialized = new SerializedObject(control);
                if (serialized.FindProperty("restPoseCaptured").boolValue) local = serialized.FindProperty("restLocalRotation").quaternionValue;
            }
            return transform.parent != null ? NeutralWorldRotation(transform.parent) * local : local;
        }

        static void BindScreens(GameObject architecture, Material screenMaterial)
        {
            var index = 0;
            foreach (var renderer in architecture.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name.StartsWith("SCREEN_GRAPHICS_", StringComparison.Ordinal)) { renderer.gameObject.SetActive(false); continue; }
                if (!renderer.name.StartsWith("SCREEN_", StringComparison.Ordinal) || renderer.name.StartsWith("SCREEN_GRAPHICS_", StringComparison.Ordinal)) continue;
                var hero = renderer.name.StartsWith("SCREEN_HERO_", StringComparison.Ordinal);
                var texturePath = hero ? Root + "/Textures/HeroCRT_" + renderer.name.Substring("SCREEN_HERO_".Length) + ".png" : null;
                Texture2D texture;
                if (hero)
                {
                    if (!File.Exists(texturePath)) throw new FileNotFoundException("Import the authored hero CRT readout.", texturePath);
                    if (AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath) == null) AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                    if (importer.wrapMode != TextureWrapMode.Clamp || !importer.sRGBTexture || !importer.mipmapEnabled)
                    { importer.wrapMode = TextureWrapMode.Clamp; importer.sRGBTexture = true; importer.mipmapEnabled = true; importer.SaveAndReimport(); }
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                }
                else texture = CreateScreenTexture(index++ % 3);
                // Individual assets preserve screen textures in edit mode and exported prefab inspection.
                var matPath = Root + "/Materials/" + renderer.name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(screenMaterial) { name = renderer.name }; AssetDatabase.CreateAsset(mat, matPath);
                    mat.SetTexture("_BaseMap", texture); mat.SetTexture("_EmissionMap", texture);
                    mat.SetColor("_BaseColor", Color.white); mat.SetColor("_EmissionColor", hero ? Color.white : new Color(.18f, 1.1f, .35f));
                    mat.EnableKeyword("_EMISSION"); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                    if (hero)
                    {
                        mat.SetFloat("_Metallic", 0); mat.SetFloat("_Smoothness", .35f);
                        mat.SetFloat("_SpecularHighlights", 0); mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                        mat.SetFloat("_EnvironmentReflections", 0); mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    }
                }
                renderer.sharedMaterial = mat;
                var panel = renderer.GetComponent<CabinScreenPanel>();
                if (panel == null)
                {
                    panel = renderer.gameObject.AddComponent<CabinScreenPanel>();
                    panel.Configure("cabin.screen." + renderer.name.ToLowerInvariant(), renderer, texture, hero ? 1f : 1.5f);
                    if (hero) panel.SetEmissionTint(Color.white);
                }
                else panel.Apply();
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssetIfDirty(mat);
            }
        }

        static void AddStructureColliders(GameObject architecture)
        {
            foreach (var filter in architecture.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null || filter.GetComponent<CabinInteractable>() != null) continue;
                var name = filter.name.ToLowerInvariant();
                var path = AnimationUtility.CalculateTransformPath(filter.transform, architecture.transform).ToLowerInvariant();
                var structure = path.Contains("floor") || path.Contains("walls") || path.Contains("ceiling") || path.Contains("banks") || name.Contains("door_leaf") || name.Contains("hatch_panel") ||
                    (path.Contains("door/") && name.Contains("__mc_ivory"));
                var renderer = filter.GetComponent<Renderer>();
                var size = renderer != null ? renderer.bounds.size : Vector3.zero;
                if (!structure || Mathf.Max(size.x, size.y, size.z) < .3f) continue;
                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
            }
        }

        static Dictionary<string, Material> CreateMaterials()
        {
            var result = new Dictionary<string, Material>();
            result["MC_Ivory"] = Mat("MC_Ivory", new Color(.77f, .73f, .62f), .08f, .36f);
            result["MC_Orange"] = Mat("MC_Orange", new Color(.75f, .21f, .055f), .12f, .32f);
            result["MC_Metal"] = Mat("MC_Metal", new Color(.34f, .35f, .33f), .75f, .52f);
            result["MC_Dark"] = Mat("MC_Dark", new Color(.065f, .075f, .071f), .3f, .25f);
            result["MC_Rubber"] = Mat("MC_Rubber", new Color(.027f, .032f, .028f), 0, .23f);
            result["MC_Screen"] = Mat("MC_Screen", new Color(.006f, .032f, .014f), .05f, .4f, new Color(.015f, .12f, .045f));
            result["MC_GreenGlow"] = Mat("MC_GreenGlow", new Color(.15f, .8f, .23f), 0, .35f, new Color(.12f, 1.5f, .3f));
            result["MC_WarmGlow"] = Mat("MC_WarmGlow", new Color(1, .72f, .35f), 0, .35f, new Color(3.0f, 1.9f, .75f));
            result["MC_RedGlow"] = Mat("MC_RedGlow", new Color(.7f, .035f, .01f), 0, .35f, new Color(1.6f, .08f, .015f));
            result["MC_White"] = Mat("MC_White", new Color(.94f, .9f, .77f), 0, .3f);
            result["MC_Brass"] = Mat("MC_Brass", new Color(.46f, .31f, .12f), .7f, .45f);
            return result;
        }

        static Material Mat(string name, Color color, float metallic, float smoothness, Color? emission = null)
        {
            var path = Root + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat; // Keep later artist texture, shader and surface polish across rebuilds.
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name }; AssetDatabase.CreateAsset(mat, path);
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            mat.SetTexture("_EmissionMap", Texture2D.whiteTexture);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Cull", 0);
            mat.enableInstancing = true;
            if (emission.HasValue) { mat.SetColor("_EmissionColor", emission.Value); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; mat.EnableKeyword("_EMISSION"); }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture2D CreateScreenTexture(int variant)
        {
            var path = Root + "/Textures/CRT_Display_" + variant + ".png";
            if (!File.Exists(path))
            {
                const int width = 512, height = 320;
                var pixels = new Color[width * height];
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                {
                    var u = (x - width * .5f) / (height * .42f);
                    var v = (y - height * .5f) / (height * .42f);
                    var radius = Mathf.Sqrt(u * u + v * v);
                    var grid = (x % 32 == 0 || y % 32 == 0) ? .09f : 0;
                    var scan = y % 3 == 0 ? .82f : 1;
                    var value = .014f + grid;
                    if (variant == 0)
                    {
                        if (radius < 1.02f && (Mathf.Abs(radius * 4 - Mathf.Round(radius * 4)) < .012f || Mathf.Abs(u) < .008f || Mathf.Abs(v) < .008f)) value = .63f;
                        var angle = Mathf.Atan2(v, u);
                        if (radius < .98f && angle > .42f && angle < .52f) value = Mathf.Max(value, .44f * (1 - radius * .5f));
                        if (Mathf.Abs(u - .3f) < .035f && Mathf.Abs(v - .42f) < .035f) value = 1;
                        if (Mathf.Abs(u + .51f) < .02f && Mathf.Abs(v + .23f) < .03f) value = .85f;
                    }
                    else if (variant == 1)
                    {
                        var wave = Mathf.Sin(x * .026f) * 29 + Mathf.Sin(x * .091f) * 12 + 155;
                        if (Mathf.Abs(y - wave) < 1.3f || Mathf.Abs(y - (wave * .5f + 36)) < .9f) value = .8f;
                        if (x > 40 && x < 460 && y < 75 && x % 14 < 8 && y < 20 + Mathf.Abs(Mathf.Sin(x * .11f)) * 48) value = .55f;
                    }
                    else
                    {
                        var ridge = 135 + 38 * Mathf.Sin(x * .015f) + 19 * Mathf.Sin(x * .041f);
                        for (var line = 0; line < 6; line++) if (Mathf.Abs(y - ridge - line * 12) < 1) value = .7f - line * .07f;
                        if (x % 55 < 2 && y > 110 && y < ridge + 60) value = Mathf.Max(value, .34f);
                    }
                    if ((x > 12 && x < 499 && (y == 12 || y == 307)) || (y > 12 && y < 307 && (x == 12 || x == 499))) value = .44f;
                    // Small block telemetry rows avoid unreadable baked pseudo-text while leaving room for real UI later.
                    if (y > 280 && y < 291 && x > 26 && x < 235 && x % 9 < 5) value = Mathf.Max(value, .43f);
                    pixels[y * width + x] = new Color(value * .22f, value * scan, value * .37f);
                }
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.SetPixels(pixels); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = true; importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void CreateLighting(Basis basis)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.27f, .24f, .19f);
            RenderSettings.ambientEquatorColor = new Color(.22f, .19f, .15f);
            RenderSettings.ambientGroundColor = new Color(.09f, .08f, .06f);
            RenderSettings.fog = false;
            var holder = new GameObject("Cabin_Lighting");
            for (var i = 0; i < 6; i++)
            {
                var angle = Mathf.PI * 2 * i / 6;
                var light = new GameObject("Warm_Ceiling_Fixture_" + i.ToString("00")).AddComponent<Light>();
                light.transform.SetParent(holder.transform, false);
                light.transform.position = basis.Point(new Vector3(Mathf.Cos(angle) * 2.65f, Mathf.Sin(angle) * 2.65f, 2.95f));
                light.transform.LookAt(basis.Point(new Vector3(Mathf.Cos(angle) * 1.15f, Mathf.Sin(angle) * 1.15f, .4f)));
                light.type = LightType.Spot; light.spotAngle = 105; light.innerSpotAngle = 65;
                light.range = 7; light.intensity = 8; light.color = new Color(1, .80f, .56f);
                light.shadows = i % 2 == 0 ? LightShadows.Soft : LightShadows.None;
                light.shadowBias = .02f; light.shadowNormalBias = .08f;
                light.gameObject.AddComponent<UniversalAdditionalLightData>();
            }
            var fill = new GameObject("Cabin_Central_Bounce").AddComponent<Light>();
            fill.transform.SetParent(holder.transform, false); fill.transform.position = basis.Point(new Vector3(0, 0, 2.5f));
            fill.type = LightType.Point; fill.range = 12; fill.intensity = 8; fill.color = new Color(1, .89f, .73f); fill.shadows = LightShadows.None;
            var main = new GameObject("Cabin_Global_Bounce").AddComponent<Light>();
            main.type = LightType.Directional; main.intensity = .65f; main.color = new Color(1, .85f, .68f);
            main.shadows = LightShadows.None; main.transform.rotation = Quaternion.Euler(45, 150, 0);
            var probe = new GameObject("Cabin_Reflection_Probe").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(holder.transform, false); probe.transform.position = basis.Point(new Vector3(0, 0, 1.5f));
            probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.resolution = 128; probe.size = new Vector3(9, 4, 9); probe.boxProjection = true;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor; probe.backgroundColor = new Color(.16f, .13f, .08f);
        }

        static void CreateCameras(Basis basis)
        {
            var cameras = new GameObject("Cabin_Cameras").transform;
            // Default stays inside the complete cabin; no ceiling or wall is hidden for this view.
            MakeCamera("Cabin Presentation Camera", basis.Point(new Vector3(0, -3.25f, 2.72f)), basis.Point(new Vector3(0, .65f, 1.22f)), 86, true, cameras);
            MakeCamera("Pilot Eye Camera", basis.Point(new Vector3(0, -.2f, 1.5f)), basis.Point(new Vector3(0, -2.3f, 1.3f)), 78, false, cameras);
            MakeCamera("Rear View Camera", basis.Point(new Vector3(0, 3.25f, 2.55f)), basis.Point(new Vector3(0, -.7f, 1.2f)), 90, false, cameras);
            MakeCamera("Ceiling Detail Camera", basis.Point(new Vector3(.3f, -.4f, 1.2f)), basis.Point(new Vector3(0, 0, 3.6f)), 95, false, cameras);
            MakeCamera("Model Inspection Camera", basis.Point(new Vector3(6, -7, 6)), basis.Point(new Vector3(0, 0, 1.2f)), 48, false, cameras);
        }

        static void MakeCamera(string name, Vector3 position, Vector3 target, float fov, bool enabled, Transform parent)
        {
            var camera = new GameObject(name).AddComponent<Camera>();
            camera.transform.SetParent(parent, false); camera.transform.position = position; camera.transform.LookAt(target);
            camera.fieldOfView = fov; camera.aspect = 16f / 10f; camera.nearClipPlane = .025f; camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .022f, .018f);
            camera.allowHDR = true; camera.depth = 20; camera.enabled = enabled;
            var data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High; data.requiresDepthTexture = true;
            if (enabled) { camera.tag = "MainCamera"; camera.gameObject.AddComponent<AudioListener>(); }
        }

        static void CreateVolume()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Cabin_WarmInterior";
            AssetDatabase.CreateAsset(profile, Root + "/Settings/Cabin_WarmInterior.asset");
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1.1f); bloom.intensity.Override(.16f); bloom.scatter.Override(.42f);
            var color = profile.Add<ColorAdjustments>(true); color.postExposure.Override(.25f); color.contrast.Override(5); color.saturation.Override(-5);
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            var volume = new GameObject("Cabin_Warm_Interior_Volume").AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 30; volume.sharedProfile = profile;
        }

        public static string Validate()
        {
            var scene = RequiredScene();
            var report = new ValidationReport { status = "verified_static" };
            var architecture = SceneRoot(scene, "Cabin_Architecture");
            var basis = ReadBasis(architecture);
            report.size = WorldBounds(architecture).size; report.basisX = basis.x; report.basisY = basis.y; report.basisZ = basis.z;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var hero = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Cabin_HeroConsole");
            report.heroAuthored = hero != null && AssetDatabase.LoadAssetAtPath<GameObject>(HeroModelPath) != null;
            report.heroControls = hero != null ? hero.GetComponentsInChildren<CabinInteractable>(true).Length : 0;
            if (!report.heroAuthored || report.heroControls == 0) report.errors.Add("Missing Blender-authored primary console and its individual controls.");
            if (hero != null)
            {
                var heroBasis = ReadBasis(hero, "ORIENTATION_HERO_");
                if (Mathf.Abs(heroBasis.x.magnitude - 1) > .01f || Mathf.Abs(heroBasis.y.magnitude - 1) > .01f || Mathf.Abs(heroBasis.z.magnitude - 1) > .01f)
                    report.errors.Add("Hero FBX unit conversion was lost; verify its imported root transform.");
                if (WorldBounds(hero).size.magnitude < 2) report.errors.Add("Hero bounds are smaller than the authored metre-scale console assembly.");
            }
            var decorations = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Meshy_Cabin_Props");
            if (decorations != null)
            {
                report.decorativeMeshyProps = decorations.transform.childCount;
                if (decorations.GetComponentsInChildren<CabinInteractable>(true).Length > 0) report.errors.Add("Generated Meshy assets must be decorative only.");
                foreach (var retired in new[] { "NavConsole_T25", "ThrottlePod_T25", "JoystickPod_T25" })
                    if (decorations.transform.Find(retired) != null) report.errors.Add("Superseded generated foreground asset remains in scene: " + retired);
            }
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true)) report.missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    report.renderers++;
                    if (!Finite(renderer.bounds.center) || !Finite(renderer.bounds.size)) report.errors.Add("Invalid renderer bounds: " + renderer.name);
                    foreach (var mat in renderer.sharedMaterials) if (mat == null || mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader") report.errors.Add("Missing/error material: " + renderer.name);
                }
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                { report.meshFilters++; if (filter.sharedMesh == null) report.errors.Add("Missing mesh: " + filter.name); else report.triangles += TriangleCount(filter.sharedMesh); }
                foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                { report.colliders++; if (collider is MeshCollider mc && mc.sharedMesh == null) report.errors.Add("Missing collision mesh: " + collider.name); }
                foreach (var control in root.GetComponentsInChildren<CabinInteractable>(true))
                {
                    report.controls++;
                    if (string.IsNullOrWhiteSpace(control.StableId) || !ids.Add(control.StableId)) report.errors.Add("Missing/duplicate control ID: " + control.name);
                    if (control.GetComponent<Collider>() == null) report.errors.Add("Control lacks collider: " + control.name);
                }
                foreach (var screen in root.GetComponentsInChildren<CabinScreenPanel>(true))
                { report.screens++; if (screen.DisplayTexture == null) report.errors.Add("Screen has no editable texture: " + screen.name); }
            }
            var ceiling = architecture.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Ceiling");
            report.ceilingVisible = ceiling != null && ceiling.gameObject.activeInHierarchy;
            if (!report.ceilingVisible) report.warnings.Add("Ceiling group is not active or named differently; verify complete cabin enclosure.");
            if (report.missingScripts > 0) report.errors.Add("Missing script components: " + report.missingScripts);
            if (report.controls == 0) report.errors.Add("No individual interaction controls found.");
            if (report.screens == 0) report.errors.Add("No editable screens found.");
            report.warnings.Add("Input-ready components only; no player/controller is installed. Play Mode and player builds were not run.");
            if (report.errors.Count > 0) report.status = "validation_failed";
            Directory.CreateDirectory(SourceRoot);
            var json = JsonUtility.ToJson(report, true); File.WriteAllText(SourceRoot + "/unity_validation.json", json); return json;
        }

        static Basis ReadBasis(GameObject architecture, string prefix = "ORIENTATION_")
        {
            var transforms = architecture.GetComponentsInChildren<Transform>(true);
            var x = transforms.FirstOrDefault(t => t.name == prefix + "X_POS");
            var y = transforms.FirstOrDefault(t => t.name == prefix + "Y_POS");
            var z = transforms.FirstOrDefault(t => t.name == prefix + "Z_POS");
            if (x == null || y == null || z == null) throw new InvalidOperationException("Architecture needs all three ORIENTATION_* sentinels for verified coordinate conversion.");
            var origin = architecture.transform.position;
            return new Basis { origin = origin, x = x.position - origin, y = y.position - origin, z = z.position - origin };
        }

        static Bounds WorldBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            for (var i = 0; i < renderers.Length; i++) { if (i == 0) bounds = renderers[i].bounds; else bounds.Encapsulate(renderers[i].bounds); }
            return bounds;
        }

        static Bounds LocalBounds(GameObject root)
        {
            var result = new Bounds(Vector3.zero, Vector3.zero);
            var first = true;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var b = renderer.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var p = root.transform.InverseTransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (first) { result = new Bounds(p, Vector3.zero); first = false; } else result.Encapsulate(p);
                }
            }
            return result;
        }

        static int TriangleCount(Mesh mesh)
        { var count = 0; if (mesh != null) for (var i = 0; i < mesh.subMeshCount; i++) if (mesh.GetTopology(i) == MeshTopology.Triangles) count += (int)(mesh.GetIndexCount(i) / 3); return count; }
        static bool Finite(Vector3 value) => !(float.IsNaN(value.x) || float.IsInfinity(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));
        static void RequireEditMode() { if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run cabin import tools in Edit Mode."); }
        static Scene RequiredScene() { var scene = SceneManager.GetSceneByPath(ScenePath); if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Load " + ScenePath + " first."); return scene; }
        static GameObject SceneRoot(Scene scene, string name) => scene.GetRootGameObjects().FirstOrDefault(r => r.name == name) ?? throw new InvalidOperationException("Missing cabin root: " + name);
        static void SaveScene(Scene scene) { EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save the cabin scene."); }
        static void EnsureFolders() { foreach (var folder in new[] { "Materials", "Textures", "Prefabs", "Scenes", "Settings" }) Directory.CreateDirectory(Root + "/" + folder); AssetDatabase.Refresh(); }
    }
}
#endif
