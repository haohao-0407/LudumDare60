using System;
using System.IO;
using System.Linq;
using LudumDare60.AssemblyDemo;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LudumDare60.ReturnWindow.Editor
{
    public static class NavigationAssemblyInstaller
    {
        public const string SourcePath = "Assets/Prefabs/导航装置.prefab";
        public const string PrefabPath = "Assets/AssemblyDemo/Prefabs/NavigationAssembly.prefab";
        public const string PreviewPath = "Assets/AssemblyDemo/Scenes/NavigationAssembly.unity";
        public const float ModelScale = 5f;
        public static readonly string[] PartNames = { "tripo_part_0.001", "tripo_part_10.001", "tripo_part_27", "tripo_part_83" };
        // Model-local XYZ and X-axis rotations from the user's assembled/disassembled reference.
        public static readonly Vector3[] Solved = {
            new Vector3(-.01948432f, .023776542f, -.011849892f), new Vector3(.242f, .176f, .402f),
            new Vector3(0, .382f, .031f), new Vector3(-.257f, .487f, -.378f)
        };
        public static readonly Vector3[] Scattered = {
            Solved[0], new Vector3(.242f, .106f, -.721f),
            new Vector3(0, .436f, .527f), new Vector3(-.257f, .022f, -.994f)
        };
        public static readonly float[] InitialXAngles = { 0, -90, 107.552f, 0 };
        public static Vector3 Project(Vector3 point) => new Vector3(-point.z, point.y, 0) * ModelScale;

        [MenuItem("Tools/Assembly Demo/Install Navigation Device In Mission")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before installing the navigation workbench.");
            StageUtility.GoToMainStage();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ReturnWindowSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open the ReturnWindow mission before installing; current scene changes are preserved.");
            Directory.CreateDirectory("Logs/NavigationAssembly");
            // Save a copy including unsaved Inspector edits before replacing only the workbench.
            if (!EditorSceneManager.SaveScene(scene, "Assets/ReturnWindow/Scenes/ReturnWindow.before-navigation.unity", true))
                throw new IOException("Could not back up the current mission scene.");
            File.Copy("Assets/ReturnWindow/Scenes/ReturnWindow.before-navigation.unity", "Logs/NavigationAssembly/ReturnWindow.before-navigation.unity", true);
            AssetDatabase.DeleteAsset("Assets/ReturnWindow/Scenes/ReturnWindow.before-navigation.unity");
            BuildPrefab();
            var repair = UnityEngine.Object.FindFirstObjectByType<ReturnRepairStation>(FindObjectsInactive.Include);
            if (repair == null) throw new InvalidOperationException("Mission repair station was not found.");
            Undo.RecordObject(repair, "Install navigation repair workbench");
            var oldWorkshop = repair.workshop;
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            root.name = "Navigation Device - Assembly Workbench";
            root.transform.position = new Vector3(50, -1.4f, 0);
            var controller = root.GetComponent<AssemblyDemoController>();
            controller.ViewCamera = repair.pilotCamera;
            repair.workshop = root;
            repair.assembly = controller;
            repair.GetComponent<MissionRepairBridge>().Configure(controller);
            var layout = root.GetComponent<NavigationAssemblyLayout>();
            if (repair.workshopUI != null)
            {
                foreach (var label in repair.workshopUI.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (label.name == "Title") label.text = "NAVIGATION DEVICE / REPAIR WORKBENCH";
                    if (label.name == "Instructions")
                    {
                        label.text = "1-4 / CLICK: SELECT    WASD: MOVE    Q/E: ROTATE    SHIFT: FINE\nX: DISCONNECT    R: RESET PARTS    ESC: RETURN TO CABIN\nJoin matching TANK, OPTICS and ANTENNA ports. Completed assembly stays intact until the next run.";
                        layout.status = label;
                    }
                    EditorUtility.SetDirty(label);
                }
            }
            root.SetActive(false);
            Undo.RegisterCreatedObjectUndo(root, "Install navigation repair workbench");
            if (oldWorkshop != null && oldWorkshop != root) Undo.DestroyObjectImmediate(oldWorkshop);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the updated mission.");
            BuildPreview();
            AssetDatabase.SaveAssets();
            Debug.Log("[NavigationAssembly] Installed native model workbench in ReturnWindow. Source prefab preserved.");
        }

        public static void BuildPrefab()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            if (source == null) throw new FileNotFoundException(SourcePath);
            var root = new GameObject("Navigation Assembly");
            root.SetActive(false);
            try
            {
                var controller = root.AddComponent<AssemblyDemoController>();
                controller.LockWhenComplete = true;
                controller.MoveSpeed = 1.8f;
                controller.RotationSpeed = 70;
                controller.Modules = new AssemblyModule[4];
                var layout = root.AddComponent<NavigationAssemblyLayout>();
                layout.assembly = controller;
                layout.assembledLocalPositions = Solved.Select(Project).ToArray();
                for (int i = 0; i < PartNames.Length; i++)
                {
                    var original = source.GetComponentsInChildren<Transform>(true).Single(t => t.name == PartNames[i]);
                    var moduleObject = new GameObject(PartNames[i]);
                    moduleObject.transform.SetParent(root.transform, false);
                    var module = moduleObject.AddComponent<AssemblyModule>();
                    controller.Modules[i] = module;
                    module.moduleLabel = new[] { "BASE", "TANK", "OPTICS", "ANTENNA" }[i];
                    var visual = UnityEngine.Object.Instantiate(original.gameObject, moduleObject.transform);
                    visual.name = "Native model";
                    visual.transform.localPosition = new Vector3(0, 0, Scattered[i].x * ModelScale);
                    visual.transform.localRotation = Quaternion.Euler(0, -90, 0);
                    visual.transform.localScale = original.localScale * ModelScale;
                    foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true)) renderer.enabled = true;
                    module.modelVisual = visual.transform;
                    var bounds = LocalMeshBounds(moduleObject.transform, visual.transform);
                    module.modelBoundsCenter = new Vector2(bounds.center.x, bounds.center.y);
                    module.modelBoundsSize = new Vector2(Mathf.Max(.12f, bounds.size.x), Mathf.Max(.12f, bounds.size.y));
                    module.radius = Mathf.Max(.1f, bounds.extents.magnitude);
                    module.portAngles = new float[i == 0 ? 3 : 1];
                    module.portDistances = new float[module.portAngles.Length];
                    module.portKeys = new string[module.portAngles.Length];
                    moduleObject.transform.localPosition = Project(Scattered[i]);
                    moduleObject.transform.localRotation = Quaternion.Euler(0, 0, InitialXAngles[i]);
                }
                // Reciprocal radial ports are derived from the recorded target pivots. Each
                // unique pair can meet only at the exact target offset and relative rotation.
                for (int i = 1; i < 4; i++)
                {
                    Vector2 delta = Project(Solved[i]) - Project(Solved[0]);
                    float angle = AssemblySnapSolver.ClockwiseAngle(delta);
                    SetPort(controller.Modules[0], i-1, angle, delta.magnitude * .5f, controller.Modules[i].moduleLabel);
                    SetPort(controller.Modules[i], 0, angle + 180, delta.magnitude * .5f, controller.Modules[i].moduleLabel);
                }
                var xray = root.AddComponent<Xray>();
                var sourceEffect = source.GetComponent<Xray>();
                if (sourceEffect != null) EditorUtility.CopySerialized(sourceEffect, xray);
                xray.wireframeSourceRoot = root.transform;
                xray.hideOriginalModel = true;
                xray.showOutline = true;
                xray.outlineColor = xray.visibleColor = Color.white;
                root.SetActive(true);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void SetPort(AssemblyModule module, int index, float angle, float distance, string key)
        {
            module.portAngles[index] = AssemblySnapSolver.NormalizeAngle(angle);
            module.portDistances[index] = distance;
            module.portKeys[index] = key;
        }

        internal static Bounds LocalMeshBounds(Transform module, Transform visual)
        {
            bool first = true;
            var bounds = new Bounds();
            foreach (var mesh in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                var b = mesh.sharedMesh.bounds;
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                        {
                            var point = module.InverseTransformPoint(mesh.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x,y,z))));
                            if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                            else bounds.Encapsulate(point);
                        }
            }
            return bounds;
        }

        private static void BuildPreview()
        {
            var preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), preview);
                root.transform.position = new Vector3(0, -1.4f, 0);
                var cameraObject = new GameObject("Assembly Camera", typeof(Camera), typeof(AudioListener));
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                var camera = cameraObject.GetComponent<Camera>();
                cameraObject.tag = "MainCamera";
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true; camera.orthographicSize = 5.4f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f, .028f, .032f);
                root.GetComponent<AssemblyDemoController>().ViewCamera = camera;
                EditorSceneManager.SaveScene(preview, PreviewPath);
            }
            finally { EditorSceneManager.CloseScene(preview, true); }
        }
    }
}
