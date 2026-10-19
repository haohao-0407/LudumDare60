using System;
using System.IO;
using System.Reflection;
using System.Text;
using LudumDare60.AssemblyDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LudumDare60.ReturnWindow.Editor
{
    public static class NavigationAssemblyValidation
    {
        [MenuItem("Tools/Assembly Demo/Validate Navigation Device")]
        public static void Run()
        {
            var report = new StringBuilder();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var previousActive = SceneManager.GetActiveScene();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NavigationAssemblyInstaller.PrefabPath);
                Require(prefab != null, "Navigation prefab exists", report);
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var controller = root.GetComponent<AssemblyDemoController>();
                var layout = root.GetComponent<NavigationAssemblyLayout>();
                var modules = controller.Modules;
                typeof(AssemblyDemoController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                Require(modules.Length == 4, "Four native mesh modules", report);
                for (int i = 0; i < 4; i++)
                {
                    Require(Vector3.Distance(modules[i].transform.localPosition, NavigationAssemblyInstaller.Project(NavigationAssemblyInstaller.Scattered[i])) < .0001f,
                        "Initial XYZ projection " + i, report);
                    Require(Quaternion.Angle(modules[i].transform.localRotation, Quaternion.Euler(0,0,NavigationAssemblyInstaller.InitialXAngles[i])) < .001f,
                        "Initial rotation " + i, report);
                    Require(Mathf.Abs(modules[i].modelVisual.localPosition.z - NavigationAssemblyInstaller.Scattered[i].x * NavigationAssemblyInstaller.ModelScale) < .0001f,
                        "Native depth retained " + i, report);
                    Require(modules[i].ContainsLocalPoint(modules[i].modelBoundsCenter), "Mesh hit bounds " + i, report);
                    Require(!modules[i].ContainsLocalPoint(modules[i].modelBoundsCenter + modules[i].modelBoundsSize * 2), "Outside mesh hit bounds " + i, report);
                }
                Require(!controller.TrySnapSelected() && !controller.IsAssemblyComplete, "Scattered parts do not auto-complete", report);
                Capture(root, scene, "before");
                // Reset returns to the supplied poses rather than the solved coordinates.
                controller.SelectModule(1);
                controller.ApplyMotion(new Vector2(.5f,.2f), 15);
                controller.ResetDemo();
                Require(Vector3.Distance(modules[1].transform.localPosition, NavigationAssemblyInstaller.Project(NavigationAssemblyInstaller.Scattered[1])) < .0001f,
                    "Reset restores recorded start", report);
                for (int i = 0; i < 4; i++)
                {
                    modules[i].transform.localPosition = layout.assembledLocalPositions[i];
                    modules[i].RotationDegrees = 0;
                }
                controller.SelectModule(1);
                modules[1].RotationDegrees = 20;
                Require(!controller.TrySnapSelected(), "Incorrect orientation rejected at correct coordinates", report);
                modules[1].RotationDegrees = 0;
                modules[1].Position += new Vector2(.015f, -.01f);
                Require(controller.TrySnapSelected(), "TANK port captures and corrects nearby pose", report);
                Require(!controller.IsAssemblyComplete, "One joint cannot mark repair complete", report);
                for (int i = 2; i < 4; i++)
                {
                    controller.SelectModule(i);
                    Require(controller.TrySnapSelected(), "Unique port capture " + i, report);
                }
                Require(controller.ConnectionCount == 3 && controller.IsAssemblyComplete, "All three physical connections complete repair", report);
                for (int i = 0; i < 4; i++)
                    Require(Vector3.Distance(modules[i].transform.localPosition, layout.assembledLocalPositions[i]) < .0001f,
                        "Snap reconstructs exact target " + i, report);
                layout.PresentCompletion();
                controller.InputBlocked = false;
                controller.ApplyMotion(Vector2.one, 30);
                controller.DetachSelected();
                controller.ResetDemo();
                Require(controller.IsAssemblyComplete && controller.InputBlocked, "Completed device stays assembled despite reset/detach/pause unlock", report);
                for (int i = 0; i < 4; i++)
                    Require(Vector3.Distance(modules[i].transform.localPosition, layout.assembledLocalPositions[i]) < .0001f,
                        "Final display retains recorded target " + i, report);
                Capture(root, scene, "assembled");
                var repair = UnityEngine.Object.FindFirstObjectByType<ReturnRepairStation>(FindObjectsInactive.Include);
                Require(repair != null && repair.assembly != null && repair.assembly.GetComponent<NavigationAssemblyLayout>() != null,
                    "ReturnWindow repair station references native navigation assembly", report);
                File.WriteAllText("Logs/NavigationAssembly/validation.txt", report.ToString());
                Debug.Log("[NavigationAssembly] VALIDATION PASS\n" + report);
            }
            catch (Exception e)
            {
                Directory.CreateDirectory("Logs/NavigationAssembly");
                File.WriteAllText("Logs/NavigationAssembly/validation.txt", report + "FAIL " + e);
                throw;
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            }
        }

        private static void Require(bool condition, string message, StringBuilder report)
        {
            if (!condition) throw new InvalidOperationException(message);
            report.AppendLine("PASS " + message);
        }

        private static void Capture(GameObject root, Scene scene, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            var go = new GameObject("Navigation validation camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(go, scene);
            var camera = go.GetComponent<Camera>();
            camera.transform.position = new Vector3(1, 1.4f, -10);
            camera.orthographic = true; camera.orthographicSize = 5.4f;
            camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f,.028f,.032f);
            var target = new RenderTexture(1600, 900, 24);
            var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var oldActive = RenderTexture.active;
            bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                target.Create(); camera.targetTexture = target; camera.aspect = 1600f/900;
                camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0,0,1600,900),0,0); texture.Apply();
                Directory.CreateDirectory("Logs/NavigationAssembly");
                File.WriteAllBytes("Logs/NavigationAssembly/" + name + ".png", texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = oldActive;
                ShaderUtil.allowAsyncCompilation = async;
                UnityEngine.Object.DestroyImmediate(texture); target.Release(); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
