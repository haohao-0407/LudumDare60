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
    public static class ShipDeviceAssemblyValidation
    {
        [MenuItem("Tools/Assembly Demo/Validate All Ship Devices")]
        public static void Run()
        {
            Directory.CreateDirectory("Logs/ShipDeviceAssembly");
            var report = new StringBuilder();
            try
            {
                CheckDevice(ShipDeviceAssemblyInstaller.LifePrefab,"life-support",5,report);
                CheckDevice(ShipDeviceAssemblyInstaller.EnergyPrefab,"energy",3,report);
                File.WriteAllText("Logs/ShipDeviceAssembly/validation.txt",report.ToString());
                Debug.Log("[ShipDeviceAssembly] VALIDATION PASS\n" + report);
            }
            catch (Exception e)
            {
                File.WriteAllText("Logs/ShipDeviceAssembly/validation.txt",report + "FAIL " + e);
                throw;
            }
        }
        private static void CheckDevice(string path,string name,int count,StringBuilder report)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),scene);
                var assembly = root.GetComponent<AssemblyDemoController>(); var layout = root.GetComponent<DeviceAssemblyLayout>();
                typeof(AssemblyDemoController).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(assembly,null);
                Require(assembly.Modules.Length == count,name + " expected playable part count",report);
                for (int i = 0; i < count; i++)
                {
                    var part = assembly.Modules[i]; part.Rebuild();
                    var nativePivot = layout.modelToBoard * layout.nativeStartPositions[i] * layout.modelScale;
                    Require(Vector3.Distance(part.modelVisual.position,nativePivot) < .0001f,name + " initial XYZ including depth " + i,report);
                    Require(Quaternion.Angle(part.transform.localRotation * part.modelVisual.localRotation,layout.modelToBoard * layout.nativeStartRotations[i]) < .02f,
                        name + " exact initial 3D rotation " + i,report);
                    Require(part.ContainsLocalPoint(part.modelBoundsCenter),name + " clickable model " + i,report);
                }
                Require(!assembly.IsAssemblyComplete,name + " initially incomplete",report);
                Capture(root,scene,layout,name + "-before");
                assembly.SelectModule(count-1); var start = assembly.Selected.transform.localPosition;
                assembly.ApplyMotion(new Vector2(.4f,.6f),40); assembly.ResetDemo();
                Require(Vector3.Distance(assembly.Modules[count-1].transform.localPosition,start) < .0001f,name + " reset restores scatter",report);
                for (int i = 0; i < count; i++)
                {
                    assembly.Modules[i].transform.localPosition = layout.assembledLocalPositions[i];
                    assembly.Modules[i].RotationDegrees = 0;
                }
                assembly.SelectModule(1); assembly.Selected.RotationDegrees = 25;
                Require(!assembly.TrySnapSelected(),name + " incorrect rotation rejected",report);
                assembly.Selected.RotationDegrees = 0;
                for (int i = 1; i < count; i++)
                {
                    assembly.SelectModule(i);
                    assembly.Selected.Position += new Vector2(.006f,.004f);
                    Require(assembly.TrySnapSelected(),name + " keyed snap " + i,report);
                    if (i < count-1) Require(!assembly.IsAssemblyComplete,name + " missing part blocks completion " + i,report);
                }
                Require(assembly.IsAssemblyComplete && assembly.ConnectionCount == count-1,name + " full physical connection graph",report);
                layout.PresentCompletion();
                assembly.InputBlocked = false; assembly.DetachSelected(); assembly.ResetDemo(); assembly.ApplyMotion(Vector2.one,90);
                Require(assembly.IsAssemblyComplete && assembly.InputBlocked,name + " completed device is locked for this run",report);
                for (int i = 0; i < count; i++)
                {
                    var part = assembly.Modules[i];
                    var target = layout.modelToBoard * layout.nativeTargetPositions[i] * layout.modelScale;
                    Require(Vector3.Distance(part.modelVisual.position,target) < .0002f,name + " screenshot target XYZ " + i,report);
                    Require(Quaternion.Angle(part.transform.localRotation * part.modelVisual.localRotation,layout.modelToBoard * layout.nativeTargetRotations[i]) < .02f,
                        name + " screenshot target rotation " + i,report);
                }
                Capture(root,scene,layout,name + "-assembled");
            }
            finally { EditorSceneManager.CloseScene(scene,true); }
        }
        private static void Require(bool value,string label,StringBuilder report)
        {
            if (!value) throw new InvalidOperationException(label);
            report.AppendLine("PASS " + label);
        }
        private static void Capture(GameObject root,Scene scene,DeviceAssemblyLayout layout,string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            var go = new GameObject("Validation Camera",typeof(Camera)); SceneManager.MoveGameObjectToScene(go,scene);
            var camera = go.GetComponent<Camera>(); camera.transform.position = new Vector3(layout.viewCenter.x,layout.viewCenter.y,-10);
            camera.orthographic = true; camera.orthographicSize = 5.4f; camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f,.028f,.032f);
            var target = new RenderTexture(1600,900,24); var pixels = new Texture2D(1600,900,TextureFormat.RGB24,false);
            var oldActive = RenderTexture.active; bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false; target.Create(); camera.targetTexture = target; camera.aspect = 1600f/900;
                camera.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,1600,900),0,0); pixels.Apply();
                File.WriteAllBytes("Logs/ShipDeviceAssembly/" + name + ".png",pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = oldActive; ShaderUtil.allowAsyncCompilation = async;
                UnityEngine.Object.DestroyImmediate(pixels); target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
