using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LudumDare60.AssemblyDemo.Editor
{
    public static class AssemblyDemoTools
    {
        public const string ScenePath = "Assets/AssemblyDemo/Scenes/AssemblyDemo.unity";
        public const string AssemblyPrefabPath = "Assets/AssemblyDemo/Prefabs/WhiteboxAssembly.prefab";
        public const string ModulePrefabPath = "Assets/WhiteboxModule/Prefabs/WhiteboxModule.prefab";
        private static TestRunnerApi testApi;
        private static ResultCallbacks callbacks;

        [MenuItem("Tools/Assembly Demo/Open Demo Scene", priority = 10)]
        public static void OpenDemo()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("Stop Play mode before opening the demo scene."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath))
            {
                var existing = EditorSceneManager.OpenScene(ScenePath);
                if (!File.Exists(AssemblyPrefabPath)) PrepareWhiteboxScene(existing);
                return;
            }
            Directory.CreateDirectory("Assets/AssemblyDemo/Scenes");
            Directory.CreateDirectory("Assets/WhiteboxModule/Resources/WhiteboxModule");
            AssetDatabase.Refresh();
            var fill = CreateMaterial("Whitebox", new Color(0.91f,0.94f,0.95f));
            var ink = CreateMaterial("VertexInk", Color.white);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "AssemblyDemo";
            RenderSettings.skybox = null;
            var cameraObject = new GameObject("Assembly Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4.3f;
            camera.transform.position = new Vector3(0,0,-10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f,0.11f,0.13f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 30;
            camera.allowHDR = false;
            cameraObject.AddComponent<AudioListener>();
            var root = new GameObject("Whitebox Assembly");
            var controller = root.AddComponent<AssemblyDemoController>();
            controller.Modules = new[] {
                CreateModule(root.transform, "A / COMM", new Vector2(-3.6f,-0.04f), 87, new float[0], fill, ink),
                CreateModule(root.transform, "B / CORE", Vector2.zero, 0, new[] { 270f, 0f, 90f }, fill, ink),
                CreateModule(root.transform, "C / RELAY", new Vector2(3.3f,-1.8f), 0, new[] { 30f, 90f }, fill, ink)
            };
            PrepareWhiteboxScene(scene);
        }

        private static Material CreateMaterial(string name, Color color)
        {
            string path = $"Assets/WhiteboxModule/Resources/WhiteboxModule/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("AssemblyDemo/WhiteboxVertexColor");
            if (shader == null) throw new System.InvalidOperationException("The whitebox shader has not compiled yet.");
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static AssemblyModule CreateModule(Transform parent, string label, Vector2 position,
            float rotation, float[] angles, Material fill, Material ink)
        {
            var go = new GameObject(label.Replace(" / ", " - "));
            go.transform.SetParent(parent);
            var module = go.AddComponent<AssemblyModule>();
            module.moduleLabel = label;
            module.radius = 0.9f;
            module.portAngles = angles;
            module.Position = position;
            module.RotationDegrees = rotation;
            module.ConfigureVisuals(fill, ink);
            return module;
        }

        private static void PrepareWhiteboxScene(Scene scene)
        {
            AssemblyDemoController controller = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "Workspace grid") { Object.DestroyImmediate(root); continue; }
                var found = root.GetComponent<AssemblyDemoController>();
                if (found != null) controller = found;
                var camera = root.GetComponent<Camera>();
                if (camera != null)
                {
                    camera.rect = new Rect(0,0,1,1);
                    camera.backgroundColor = new Color(0.10f,0.11f,0.13f);
                }
            }
            if (controller == null) throw new System.InvalidOperationException("The scene has no assembly controller.");
            // The removed demo-only HUD can still be serialized in an older scene.
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(controller.gameObject);
            controller.gameObject.name = "Whitebox Assembly";
            controller.ViewCamera = null;
            controller.Modules[0].Position = new Vector2(-3.6f,-0.04f);
            controller.Modules[0].RotationDegrees = 87;
            Directory.CreateDirectory("Assets/AssemblyDemo/Prefabs");
            AssetDatabase.Refresh();
            PrefabUtility.SaveAsPrefabAssetAndConnect(controller.gameObject, AssemblyPrefabPath, InteractionMode.AutomatedAction);

            var fill = AssetDatabase.LoadAssetAtPath<Material>("Assets/WhiteboxModule/Resources/WhiteboxModule/Whitebox.mat");
            var ink = AssetDatabase.LoadAssetAtPath<Material>("Assets/WhiteboxModule/Resources/WhiteboxModule/VertexInk.mat");
            var module = CreateModule(null, "MODULE", Vector2.zero, 0, new float[0], fill, ink);
            module.gameObject.name = "Whitebox Module";
            PrefabUtility.SaveAsPrefabAsset(module.gameObject, ModulePrefabPath);
            Object.DestroyImmediate(module.gameObject);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = controller.gameObject;
            Debug.Log("[AssemblyDemo] Whiteboxes ready. Prefabs contain no camera, background or HUD. Fixed capture limits: 5 degrees / 0.16 lateral / 0.12 contact. Press Play, then D to connect.");
        }

        [MenuItem("Tools/Assembly Demo/Run EditMode Tests", priority = 20)]
        public static void RunTests()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("Stop Play mode before running EditMode tests."); return; }
            if (testApi == null) testApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            if (callbacks != null) testApi.UnregisterCallbacks(callbacks);
            callbacks = new ResultCallbacks();
            testApi.RegisterCallbacks(callbacks);
            testApi.Execute(new ExecutionSettings(new Filter {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { "LudumDare60.AssemblyDemo.Tests" }
            }) { runSynchronously = true });
        }

        private sealed class ResultCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor tests) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                string path = Path.GetFullPath("Library/AssemblyDemoTestResults.xml");
                TestRunnerApi.SaveResultToFile(result, path);
                string summary = $"[AssemblyDemo] Tests: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped. Report: {path}";
                if (result.FailCount > 0) Debug.LogError(summary); else Debug.Log(summary);
            }
        }
    }
}
