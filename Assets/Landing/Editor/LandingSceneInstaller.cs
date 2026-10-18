using System;
using System.Collections.Generic;
using System.Linq;
using LudumDare60.TerminalDialogue;
using LudumDare60.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LudumDare60.Landing.Editor
{
    /// <summary>Wires the authored scenes in place. Does not rebuild their art or terminal UI.</summary>
    public static class LandingSceneInstaller
    {
        public const string ShipPath = "Assets/Scenes/SHIP.unity";
        public const string MarsPath = "Assets/MarsBase/Scenes/MarsBase_RetroFuture.unity";
        private const string MaterialFolder = "Assets/Landing/Materials";
        private const string ProfilePath = "Assets/Landing/LandingCabinVolume.asset";

        [MenuItem("Tools/Landing/Install SHIP to Mars landing")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing landing.");
            EnsureFolder(MaterialFolder);
            var ship = GetOrOpen(ShipPath);
            bool marsWasLoaded = SceneManager.GetSceneByPath(MarsPath).isLoaded;
            var mars = GetOrOpen(MarsPath);
            try
            {
                ValidateInputs(ship, mars);
                SceneManager.SetActiveScene(mars);
                var environment = ConfigureMars(mars);
                EditorSceneManager.MarkSceneDirty(mars);
                SceneManager.SetActiveScene(ship);
                ConfigureShip(ship, environment);
                RegisterBuildScene(ShipPath);
                RegisterBuildScene(MarsPath);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(ship);
                if (!EditorSceneManager.SaveScene(mars)) throw new InvalidOperationException("Could not save Mars scene.");
                if (!EditorSceneManager.SaveScene(ship)) throw new InvalidOperationException("Could not save SHIP scene.");
            }
            finally
            {
                // Keep unsaved work available for recovery if a later editor operation fails.
                if (!marsWasLoaded && mars.isLoaded && !mars.isDirty) EditorSceneManager.CloseScene(mars, true);
                SceneManager.SetActiveScene(ship);
            }
            Debug.Log("Landing installed. Open SHIP and press Play: automatic 45-second descent; hold RMB to look, ESC to pause.");
        }

        private static void ValidateInputs(Scene ship, Scene mars)
        {
            foreach (var name in new[] { "Custom/Landing/SpaceToMarsSky", "Custom/Landing/SoftDust", "Custom/Landing/ExteriorHaze" })
            {
                var shader = Shader.Find(name);
                if (shader == null || !shader.isSupported || ShaderUtil.GetShaderMessages(shader).Any(m => m.severity.ToString() == "Error"))
                    throw new InvalidOperationException("Compile the landing shader before installation: " + name);
            }
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            if (actions == null || actions.FindAction("Player/Look", false) == null || actions.FindAction("UI/RightClick", false) == null)
                throw new InvalidOperationException("Landing needs the existing Player/Look and UI/RightClick input actions.");
            if (!Descendants(ship).Any(t => t.name == "CompactCockpit_WallIntegrated") ||
                !Components<Camera>(ship).Any(c => c.CompareTag("MainCamera") && c.targetTexture == null) ||
                !Components<Light>(ship).Any(l => l.type == LightType.Directional) || !Components<Volume>(ship).Any(v => v.isGlobal) ||
                !Descendants(mars).Any(t => t.name == "CompactCockpit_WallIntegrated") ||
                !Components<Light>(mars).Any(l => l.name == "Sun_Late_Martian_Afternoon") || !Components<Volume>(mars).Any(v => v.isGlobal))
                throw new InvalidOperationException("Required authored SHIP or Mars scene objects are missing.");
        }

        private static MarsLandingEnvironment ConfigureMars(Scene scene)
        {
            var context = Components<MarsLandingEnvironment>(scene).FirstOrDefault();
            if (context == null) context = NewObject("Mars Landing Environment", scene).AddComponent<MarsLandingEnvironment>();
            var originals = scene.GetRootGameObjects().Where(r => r != context.gameObject &&
                (r.hideFlags & HideFlags.DontSaveInEditor) == 0).ToArray();
            var duplicate = Descendants(scene).FirstOrDefault(t => t.name == "CompactCockpit_WallIntegrated");
            if (duplicate == null) throw new InvalidOperationException("Mars cockpit landing reference is missing.");
            if (context.LandingAnchor == null)
            {
                var anchor = NewObject("LandingAnchor", scene).transform;
                anchor.SetPositionAndRotation(duplicate.position, duplicate.rotation);
                anchor.SetParent(context.transform, true);
                context.LandingAnchor = anchor;
            }
            context.DuplicateCockpit = duplicate.gameObject;
            context.PresentationCameras = Components<Camera>(scene).Where(c => c.targetTexture == null).ToArray();
            context.PresentationListeners = Components<AudioListener>(scene).ToArray();
            context.Sun = RenderSettings.sun != null ? RenderSettings.sun : Components<Light>(scene).First(l => l.name == "Sun_Late_Martian_Afternoon");
            context.SupplementalLights = Components<Light>(scene).Where(l => l != context.Sun).ToArray();
            context.GlobalVolume = Components<Volume>(scene).First(v => v.isGlobal);
            context.SkyboxMaterial = RenderSettings.skybox;
            context.FogEnabled = RenderSettings.fog;
            context.FogColor = RenderSettings.fogColor;
            context.FogMode = RenderSettings.fogMode;
            context.FogDensity = RenderSettings.fogDensity;
            context.FogStartDistance = RenderSettings.fogStartDistance;
            context.FogEndDistance = RenderSettings.fogEndDistance;
            context.AmbientMode = RenderSettings.ambientMode;
            context.AmbientSkyColor = RenderSettings.ambientSkyColor;
            context.AmbientEquatorColor = RenderSettings.ambientEquatorColor;
            context.AmbientGroundColor = RenderSettings.ambientGroundColor;
            context.AmbientIntensity = RenderSettings.ambientIntensity;
            if (context.ExteriorRoot == null)
                context.ExteriorRoot = NewObject("Mars Exterior", scene);
            // Postprocessing must blend before the terrain appears, including while ExteriorRoot is hidden.
            var volumeRoot = context.GlobalVolume.transform;
            volumeRoot.SetParent(null, true);
            foreach (var root in originals)
                if (root != context.ExteriorRoot && root != volumeRoot.gameObject && root.transform.parent == null)
                    root.transform.SetParent(context.ExteriorRoot.transform, true);
            // Remains usable when Mars is opened alone; runtime gates it only during a SHIP landing.
            EditorUtility.SetDirty(context);
            return context;
        }

        private static void ConfigureShip(Scene scene, MarsLandingEnvironment mars)
        {
            var cockpit = Descendants(scene).First(t => t.name == "CompactCockpit_WallIntegrated");
            var camera = Components<Camera>(scene).First(c => c.CompareTag("MainCamera") && c.targetTexture == null);
            var sun = Components<Light>(scene).First(l => l.type == LightType.Directional);
            var volume = Components<Volume>(scene).First(v => v.isGlobal);
            var rig = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "ShipRig");
            if (rig == null)
            {
                rig = NewObject("ShipRig", scene);
                rig.transform.SetPositionAndRotation(cockpit.position, cockpit.rotation);
                cockpit.SetParent(rig.transform, true);
            }
            // Includes the second authored terminal, while keeping overlay menus and EventSystem stationary.
            foreach (var root in scene.GetRootGameObjects())
                if (root != rig && root.GetComponent<TerminalScreenMount>() != null)
                    root.transform.SetParent(rig.transform, true);

            var look = rig.transform.Find("CameraLook");
            if (look == null)
            {
                look = NewObject("CameraLook", scene).transform;
                look.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                look.SetParent(rig.transform, true);
            }
            var shake = look.Find("CameraShake");
            if (shake == null)
            {
                shake = NewObject("CameraShake", scene).transform;
                shake.SetParent(look, false);
            }
            camera.transform.SetParent(shake, true);
            var input = look.GetComponent<LandingCockpitLook>();
            if (input == null) input = look.gameObject.AddComponent<LandingCockpitLook>();
            input.Actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            input.PauseMenu = Components<EscMenuController>(scene).FirstOrDefault();
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            camera.clearFlags = CameraClearFlags.Skybox;

            var fillTransform = rig.transform.Find("Landing Window Fill");
            if (fillTransform == null)
            {
                fillTransform = NewObject("Landing Window Fill", scene).transform;
                fillTransform.SetParent(rig.transform, false);
                fillTransform.localPosition = new Vector3(-0.65f, 1.6f, 0.1f);
            }
            var fill = fillTransform.GetComponent<Light>();
            if (fill == null) fill = fillTransform.gameObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 4;
            fill.color = new Color(1, 0.45f, 0.18f);
            fill.intensity = 0;
            fill.shadows = LightShadows.None;

            var controller = Components<LandingSequenceController>(scene).FirstOrDefault();
            if (controller == null) controller = NewObject("Landing Sequence", scene).AddComponent<LandingSequenceController>();
            controller.ShipRig = rig.transform;
            controller.ViewCamera = camera;
            controller.ShakePivot = shake;
            controller.ShipSun = sun;
            controller.CabinFill = fill;
            controller.SourceVolume = volume;
            controller.DestinationScenePath = MarsPath;
            controller.TransitionSkyTemplate = CreateMaterial("SpaceToMarsSky", "Custom/Landing/SpaceToMarsSky", mars.SkyboxMaterial);
            controller.DustMaterialTemplate = CreateMaterial("LandingDust", "Custom/Landing/SoftDust", null);
            controller.HazeMaterialTemplate = CreateMaterial("ExteriorHaze", "Custom/Landing/ExteriorHaze", null);
            controller.HazeMaterialTemplate.SetColor("_Color", new Color(0.48f, 0.2f, 0.09f, 0));
            var sky = controller.TransitionSkyTemplate;
            sky.SetFloat("_AtmosphereBlend", 0);
            sky.SetFloat("_StarVisibility", 1);
            sky.SetFloat("_PlanetVisibility", 1);
            sky.SetFloat("_PlanetAngularRadius", 0.28f);
            var portWindow = cockpit.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "PortWindow_Right_Glass");
            var planetDirection = portWindow != null ? (portWindow.bounds.center - camera.transform.position).normalized : -rig.transform.right;
            sky.SetVector("_PlanetDirection", planetDirection);
            sky.SetVector("_SunDirection", -sun.transform.forward);
            sky.SetFloat("_EntryGlow", 0);
            EditorUtility.SetDirty(sky);
            EditorUtility.SetDirty(controller.HazeMaterialTemplate);
            volume.sharedProfile = CabinProfile();
            volume.weight = 1;
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.13f, 0.15f, 0.2f);
            RenderSettings.ambientEquatorColor = new Color(0.08f, 0.095f, 0.13f);
            RenderSettings.ambientGroundColor = new Color(0.035f, 0.04f, 0.055f);
            sun.intensity = 0.65f;
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                if ((t.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0) continue;
                var flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                if (flags != 0) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            }
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(input);
        }

        private static VolumeProfile CabinProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Landing Cabin Volume";
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.value = 0.35f; bloom.threshold.value = 1.1f;
            profile.Add<Tonemapping>(true).mode.value = TonemappingMode.ACES;
            profile.Add<ColorAdjustments>(true).postExposure.value = 0;
            AssetDatabase.CreateAsset(profile, ProfilePath);
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            return profile;
        }

        private static Material CreateMaterial(string name, string shaderName, Material source)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Missing/unsupported shader: " + shaderName);
            var path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(shader) { name = name };
            if (source != null) material.CopyPropertiesFromMaterial(source);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void RegisterBuildScene(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            var scene = scenes.Find(s => s.path == path);
            if (scene == null) scenes.Add(new EditorBuildSettingsScene(path, true));
            else scene.enabled = true;
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static Scene GetOrOpen(string path)
        {
            var scene = SceneManager.GetSceneByPath(path);
            return scene.IsValid() && scene.isLoaded ? scene : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        private static GameObject NewObject(string name, Scene scene)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private static IEnumerable<T> Components<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true));
        private static IEnumerable<Transform> Descendants(Scene scene) => Components<Transform>(scene);
        private static void EnsureFolder(string path)
        {
            var parent = "Assets";
            foreach (var part in path.Split('/').Skip(1))
            {
                var current = parent + "/" + part;
                if (!AssetDatabase.IsValidFolder(current)) AssetDatabase.CreateFolder(parent, part);
                parent = current;
            }
        }
    }
}
