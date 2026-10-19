using System;
using System.IO;
using System.Linq;
using LudumDare60.AssemblyDemo;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LudumDare60.ReturnWindow.Editor
{
    public static class ShipDeviceAssemblyInstaller
    {
        public const string LifePrefab = "Assets/AssemblyDemo/Prefabs/LifeSupportAssembly.prefab";
        public const string EnergyPrefab = "Assets/AssemblyDemo/Prefabs/EnergyAssembly.prefab";
        public const string PreviewScene = "Assets/AssemblyDemo/Scenes/ShipDeviceAssembly.unity";
        public const string LifeScene = "Assets/AssemblyDemo/Scenes/LifeSupportAssembly.unity";
        public const string EnergyScene = "Assets/AssemblyDemo/Scenes/EnergyAssembly.unity";
        private static readonly string[] Prefabs = { NavigationAssemblyInstaller.PrefabPath, LifePrefab, EnergyPrefab };

        public static void BuildDevices()
        {
            BuildDevice("Assets/Prefabs/生命装置.prefab", LifePrefab, "LIFE SUPPORT",
                new[] { "CUT_test", "TANK_01", "CUT_test.001", "CUT_test.003", "CUT_test.004" },
                new[] { "BODY", "TANK", "RESERVOIR", "FILTER", "TOP RAIL" },
                new[] { new Vector3(.298f,0,0), new Vector3(-.034f,-.022f,-.092f), new Vector3(.376f,-.044f,.018f), new Vector3(.055f,-.001f,-.007f), new Vector3(.312f,.866f,.007f) },
                new[] { "DOOR_TEST" }, Quaternion.Euler(0,200,0));
            var energySource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/能源装置.prefab");
            var core = FindPart(energySource, "tripo_part_1");
            var end = FindPart(energySource, "tripo_part_3");
            var cap = FindPart(energySource, "tripo_part_4");
            var fixedNames = energySource.GetComponentsInChildren<MeshFilter>(true)
                .Select(m => m.name).Where(n => n != core.name && n != end.name && n != cap.name && !n.StartsWith("__XRay")).ToArray();
            BuildDevice("Assets/Prefabs/能源装置.prefab", EnergyPrefab, "ENERGY DEVICE",
                new[] { "tripo_part_1", "tripo_part_3", "tripo_part_4" }, new[] { "CORE", "GENERATOR", "END CAP" },
                new[] { core.localPosition, new Vector3(end.localPosition.x,.157f,.227f), new Vector3(cap.localPosition.x,.192f,-.318f) },
                fixedNames, Quaternion.Euler(0,90,0));
            BuildPreview(PreviewScene, 0, true);
            BuildPreview(LifeScene, 1, false);
            BuildPreview(EnergyScene, 2, false);
            AssetDatabase.SaveAssets();
        }

        private static Transform FindPart(GameObject source, string name) => source.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
        private static Vector3 Project(Quaternion basis, Vector3 native) => basis * native * 5;

        private static void BuildDevice(string sourcePath, string outputPath, string title, string[] names, string[] labels,
            Vector3[] solved, string[] fixedNames, Quaternion basis)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new FileNotFoundException(sourcePath);
            var root = new GameObject(title + " Assembly"); root.SetActive(false);
            try
            {
                var controller = root.AddComponent<AssemblyDemoController>();
                controller.LockWhenComplete = true; controller.MoveSpeed = 1.8f; controller.RotationSpeed = 70;
                controller.Modules = new AssemblyModule[names.Length];
                var layout = root.AddComponent<DeviceAssemblyLayout>();
                layout.assembly = controller; layout.deviceLabel = title; layout.modelToBoard = basis;
                layout.assembledLocalPositions = new Vector3[names.Length];
                layout.assembledModelPositions = new Vector3[names.Length];
                layout.assembledModelRotations = new Quaternion[names.Length];
                layout.nativeStartPositions = new Vector3[names.Length];
                layout.nativeStartRotations = new Quaternion[names.Length];
                layout.nativeTargetPositions = solved;
                layout.nativeTargetRotations = Enumerable.Repeat(Quaternion.identity, names.Length).ToArray();
                var viewBounds = new Bounds(); bool firstBounds = true;
                for (int i = 0; i < names.Length; i++)
                {
                    var original = FindPart(source,names[i]);
                    layout.nativeStartPositions[i] = original.localPosition;
                    layout.nativeStartRotations[i] = original.localRotation;
                    var projectedStart = Project(basis, original.localPosition);
                    var projectedTarget = Project(basis, solved[i]);
                    var relative = basis * original.localRotation * Quaternion.Inverse(basis);
                    var up = relative * Vector3.up;
                    float turn = Mathf.Atan2(-up.x,up.y) * Mathf.Rad2Deg;
                    var moduleObject = new GameObject(names[i]); moduleObject.transform.SetParent(root.transform,false);
                    var module = moduleObject.AddComponent<AssemblyModule>(); controller.Modules[i] = module;
                    module.moduleLabel = labels[i];
                    var visual = new GameObject("Native model").transform; visual.SetParent(moduleObject.transform,false);
                    visual.localPosition = new Vector3(0,0,projectedStart.z);
                    visual.localRotation = Quaternion.Inverse(Quaternion.Euler(0,0,turn)) * basis * original.localRotation;
                    visual.localScale = Vector3.one * 5;
                    CloneMesh(original, visual, Vector3.zero, Quaternion.identity);
                    if (i == 0)
                        foreach (var name in fixedNames)
                        {
                            var extra = FindPart(source,name);
                            CloneMesh(extra, visual, Quaternion.Inverse(original.localRotation) * (extra.localPosition-original.localPosition), Quaternion.Inverse(original.localRotation)*extra.localRotation);
                        }
                    module.modelVisual = visual;
                    var bounds = NavigationAssemblyInstaller.LocalMeshBounds(moduleObject.transform,visual);
                    module.modelBoundsCenter = new Vector2(bounds.center.x,bounds.center.y);
                    module.modelBoundsSize = new Vector2(Mathf.Max(.14f,bounds.size.x),Mathf.Max(.14f,bounds.size.y));
                    module.radius = Mathf.Max(.1f,bounds.extents.magnitude);
                    module.portAngles = new float[i == 0 ? names.Length-1 : 1];
                    module.portDistances = new float[module.portAngles.Length]; module.portKeys = new string[module.portAngles.Length];
                    module.transform.localPosition = new Vector3(projectedStart.x,projectedStart.y,0);
                    module.transform.localRotation = Quaternion.Euler(0,0,turn);
                    layout.assembledLocalPositions[i] = new Vector3(projectedTarget.x,projectedTarget.y,0);
                    layout.assembledModelPositions[i] = new Vector3(0,0,projectedTarget.z);
                    layout.assembledModelRotations[i] = basis;
                    var startBounds = WorldBounds(visual);
                    if (firstBounds) { viewBounds = startBounds; firstBounds = false; } else viewBounds.Encapsulate(startBounds);
                    var initialPosition = module.transform.localPosition; var initialRotation = module.transform.localRotation;
                    var initialModelPosition = visual.localPosition; var initialModelRotation = visual.localRotation;
                    module.transform.localPosition = layout.assembledLocalPositions[i]; module.transform.localRotation = Quaternion.identity;
                    visual.localPosition = layout.assembledModelPositions[i]; visual.localRotation = basis;
                    viewBounds.Encapsulate(WorldBounds(visual));
                    module.transform.localPosition = initialPosition; module.transform.localRotation = initialRotation;
                    visual.localPosition = initialModelPosition; visual.localRotation = initialModelRotation;
                }
                layout.viewCenter = viewBounds.center;
                if (viewBounds.size.x > 16 || viewBounds.size.y > 8) Debug.LogWarning(title + " assembly extends beyond the normal workbench area.");
                for (int i = 1; i < names.Length; i++)
                {
                    Vector2 delta = layout.assembledLocalPositions[i] - layout.assembledLocalPositions[0];
                    if (delta.sqrMagnitude < .0001f) throw new InvalidOperationException("Coincident module pivots require a separate connector definition.");
                    float angle = AssemblySnapSolver.ClockwiseAngle(delta);
                    SetPort(controller.Modules[0],i-1,angle,delta.magnitude*.5f,labels[i]);
                    SetPort(controller.Modules[i],0,angle+180,delta.magnitude*.5f,labels[i]);
                }
                var effect = root.AddComponent<Xray>();
                var sourceEffect = source.GetComponent<Xray>();
                if (sourceEffect != null) EditorUtility.CopySerialized(sourceEffect,effect);
                effect.wireframeSourceRoot = root.transform; effect.hideOriginalModel = true;
                effect.outlineColor = effect.visibleColor = Color.white; effect.showOutline = true;
                root.SetActive(true);
                PrefabUtility.SaveAsPrefabAsset(root,outputPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void CloneMesh(Transform source, Transform parent, Vector3 position, Quaternion rotation)
        {
            var clone = UnityEngine.Object.Instantiate(source.gameObject,parent);
            clone.name = source.name; clone.transform.localPosition = position;
            clone.transform.localRotation = rotation; clone.transform.localScale = source.localScale;
            foreach (var renderer in clone.GetComponentsInChildren<MeshRenderer>(true)) renderer.enabled = true;
        }
        private static Bounds WorldBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
        private static void SetPort(AssemblyModule part,int index,float angle,float distance,string key)
        {
            part.portAngles[index] = AssemblySnapSolver.NormalizeAngle(angle); part.portDistances[index] = distance; part.portKeys[index] = key;
        }

        public static GameObject CreateWorkshop(Scene scene)
        {
            var root = new GameObject("Ship Device Workbenches"); root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root,scene);
            var workbench = root.AddComponent<DeviceAssemblyWorkbench>(); workbench.devices = new AssemblyDemoController[3];
            for (int i = 0; i < 3; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs[i]),scene);
                instance.transform.SetParent(root.transform,false);
                workbench.devices[i] = instance.GetComponent<AssemblyDemoController>(); instance.SetActive(i == 0);
            }
            return root;
        }

        [MenuItem("Tools/Assembly Demo/Install Life Support And Energy Devices")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before installing devices.");
            StageUtility.GoToMainStage(); EditorSceneManager.SaveOpenScenes();
            if (SceneManager.GetActiveScene().path != ReturnWindowSceneBuilder.ScenePath) EditorSceneManager.OpenScene(ReturnWindowSceneBuilder.ScenePath);
            Directory.CreateDirectory("Logs/ShipDeviceAssembly");
            if (!File.Exists("Logs/ShipDeviceAssembly/ReturnWindow.before-devices.unity"))
                File.Copy(ReturnWindowSceneBuilder.ScenePath,"Logs/ShipDeviceAssembly/ReturnWindow.before-devices.unity");
            BuildDevices();
            var repair = UnityEngine.Object.FindFirstObjectByType<ReturnRepairStation>(FindObjectsInactive.Include);
            var old = repair.workshop;
            var workshop = CreateWorkshop(SceneManager.GetActiveScene()); workshop.transform.position = new Vector3(50,0,0);
            repair.workshop = workshop; repair.deviceWorkbench = workshop.GetComponent<DeviceAssemblyWorkbench>();
            repair.assembly = repair.deviceWorkbench.Current;
            ConfigureMissionUI(repair);
            repair.GetComponent<MissionRepairBridge>().Configure(repair.deviceWorkbench);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            EditorUtility.SetDirty(repair); EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
        }

        public static void ConfigureMissionUI(ReturnRepairStation repair)
        {
            var bench = repair.deviceWorkbench; bench.viewCamera = repair.pilotCamera;
            foreach (var label in repair.workshopUI.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.name == "Title") bench.title = label;
                if (label.name == "Instructions") bench.instructions = label;
            }
            AddDeviceButtons(repair.workshopUI.transform,bench);
        }

        private static void BuildPreview(string path,int selected,bool all)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                var root = CreateWorkshop(scene); var bench = root.GetComponent<DeviceAssemblyWorkbench>(); bench.selectedDevice = selected;
                var cameraObject = new GameObject("Assembly Camera",typeof(Camera),typeof(AudioListener)); SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var camera = cameraObject.GetComponent<Camera>(); cameraObject.tag = "MainCamera";
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f,.028f,.032f);
                bench.viewCamera = camera;
                var canvasObject = new GameObject("Device Assembly Controls",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
                SceneManager.MoveGameObjectToScene(canvasObject,scene);
                canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1600,900);
                bench.title = Label(canvasObject.transform,"Title",new Vector2(.04f,.92f),new Vector2(.96f,.99f),30);
                bench.instructions = Label(canvasObject.transform,"Instructions",new Vector2(.04f,.02f),new Vector2(.96f,.19f),23);
                AddDeviceButtons(canvasObject.transform,bench);
                var events = new GameObject("EventSystem",typeof(UnityEngine.EventSystems.EventSystem),typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
                SceneManager.MoveGameObjectToScene(events,scene);
                root.SetActive(true); bench.SelectDevice(selected);
                if (!all) bench.title.text = (selected == 1 ? "LIFE SUPPORT" : "ENERGY DEVICE") + " / ASSEMBLY PLAYTEST";
                EditorSceneManager.SaveScene(scene,path);
            }
            finally { EditorSceneManager.CloseScene(scene,true); }
        }
        private static TMP_Text Label(Transform parent,string name,Vector2 min,Vector2 max,float size)
        {
            var go = new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)); var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent,false);
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<TextMeshProUGUI>(); text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Terminal/Glass_TTY_VT220 SDF.asset");
            text.fontSize = size; text.color = new Color(.7f,.94f,.82f); text.raycastTarget = false; return text;
        }
        private static void AddDeviceButtons(Transform parent,DeviceAssemblyWorkbench bench)
        {
            var old = parent.Find("Device Selection"); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var holder = new GameObject("Device Selection",typeof(RectTransform)); holder.transform.SetParent(parent,false);
            var rect = holder.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var actions = new UnityEngine.Events.UnityAction[] { bench.SelectNavigation,bench.SelectLifeSupport,bench.SelectEnergy };
            var names = new[] { "F1 / NAVIGATION", "F2 / LIFE SUPPORT", "F3 / ENERGY" };
            for (int i = 0; i < 3; i++)
            {
                var button = new GameObject(names[i],typeof(RectTransform),typeof(Image),typeof(Button)); button.transform.SetParent(holder.transform,false);
                var b = button.GetComponent<RectTransform>(); b.anchorMin = new Vector2(.04f+i*.31f,.85f); b.anchorMax = new Vector2(.33f+i*.31f,.91f); b.offsetMin = b.offsetMax = Vector2.zero;
                button.GetComponent<Image>().color = new Color(.07f,.14f,.13f,.96f);
                var label = Label(button.transform,"Label",Vector2.zero,Vector2.one,24); label.text = names[i]; label.alignment = TextAlignmentOptions.Center;
                UnityEventTools.AddPersistentListener(button.GetComponent<Button>().onClick,actions[i]);
            }
        }
    }
}
