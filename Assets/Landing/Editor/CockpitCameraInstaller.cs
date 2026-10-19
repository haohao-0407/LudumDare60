using System;
using System.Linq;
using LudumDare60.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LudumDare60.Landing.Editor
{
    public static class CockpitCameraInstaller
    {
        private static readonly string[] ScreenNames =
            { "SCREEN_RADAR", "SCREEN_ATTITUDE", "SCREEN_NAVIGATION", "SCREEN_COMMUNICATIONS", "SCREEN_SYSTEMS" };

        [MenuItem("Tools/Camera/安装驾驶舱六机位镜头")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play。");
            var scene = SceneManager.GetActiveScene();
            var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            var camera = objects.Select(item => item.GetComponent<Camera>()).First(item => item != null && item.CompareTag("MainCamera") && item.targetTexture == null);
            var look = objects.Select(item => item.GetComponent<LandingCockpitLook>()).First(item => item != null);
            var rig = objects.Single(item => item.name == "ShipRig");
            var pause = objects.Select(item => item.GetComponent<EscMenuController>()).FirstOrDefault(item => item != null);
            var views = rig.Find("Cockpit Camera Views");
            if (views == null)
            {
                views = new GameObject("Cockpit Camera Views").transform;
                Undo.RegisterCreatedObjectUndo(views.gameObject, "Create cockpit camera views"); views.SetParent(rig, false);
            }
            var controller = views.GetComponent<CockpitCameraController>();
            if (controller == null) controller = Undo.AddComponent<CockpitCameraController>(views.gameObject);
            Undo.RecordObject(controller, "Configure cockpit camera");
            controller.ViewCamera = camera; controller.FreeLook = look; controller.PauseMenu = pause;
            controller.Actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            var free = views.Find("00 Free View");
            if (free == null)
            {
                free = new GameObject("00 Free View").transform; free.SetParent(views, false);
                free.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                Undo.RegisterCreatedObjectUndo(free.gameObject, "Create free view reference");
            }
            controller.FreeView = free;
            var targets = new CockpitScreenTarget[ScreenNames.Length];
            for (var i = 0; i < ScreenNames.Length; ++i)
            {
                var display = objects.Single(item => item.name == ScreenNames[i] && item.GetComponent<MeshFilter>() != null).GetComponent<MeshFilter>();
                var target = display.GetComponent<CockpitScreenTarget>();
                if (target == null) target = Undo.AddComponent<CockpitScreenTarget>(display.gameObject);
                Undo.RecordObject(target, "Configure screen view");
                var hit = display.GetComponent<MeshCollider>();
                if (hit == null) hit = Undo.AddComponent<MeshCollider>(display.gameObject);
                Undo.RecordObject(hit, "Configure screen click surface"); hit.sharedMesh = display.sharedMesh; hit.convex = false; hit.isTrigger = false; hit.enabled = true;
                target.HitCollider = hit;
                var view = views.Find((i + 1).ToString("00") + " " + ScreenNames[i]);
                if (view == null)
                {
                    view = new GameObject((i + 1).ToString("00") + " " + ScreenNames[i]).transform;
                    view.SetParent(views, false); Undo.RegisterCreatedObjectUndo(view.gameObject, "Create screen camera view");
                    CalculateView(display, free.position, rig.up, 45, camera.aspect > 0 ? camera.aspect : 16f / 9f, out var position, out var rotation);
                    view.SetPositionAndRotation(position, rotation);
                }
                target.ViewPoint = view; targets[i] = target;
                EditorUtility.SetDirty(target); EditorUtility.SetDirty(hit);
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(hit);
            }
            controller.Screens = targets;
            EditorUtility.SetDirty(controller); EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = views.gameObject;
        }

        public static void CalculateView(MeshFilter display, Vector3 observer, Vector3 cockpitUp, float fov, float aspect,
            out Vector3 position, out Quaternion rotation)
        {
            var bounds = display.sharedMesh.bounds;
            var size = bounds.size;
            var thin = size.x < size.y && size.x < size.z ? 0 : size.y < size.z ? 1 : 2;
            var localNormal = thin == 0 ? Vector3.right : thin == 1 ? Vector3.up : Vector3.forward;
            var matrix = display.transform.localToWorldMatrix;
            var normal = matrix.inverse.transpose.MultiplyVector(localNormal).normalized;
            var center = matrix.MultiplyPoint3x4(bounds.center);
            if (Vector3.Dot(normal, observer - center) < 0) normal = -normal;
            var up = Vector3.ProjectOnPlane(cockpitUp, normal).normalized;
            if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(display.transform.up, normal).normalized;
            var right = Vector3.Cross(up, normal).normalized;
            float width = 0, height = 0, depth = 0;
            for (var i = 0; i < 8; i++)
            {
                var offset = matrix.MultiplyVector(Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                width = Mathf.Max(width, Mathf.Abs(Vector3.Dot(offset, right)));
                height = Mathf.Max(height, Mathf.Abs(Vector3.Dot(offset, up)));
                depth = Mathf.Max(depth, Mathf.Abs(Vector3.Dot(offset, normal)));
            }
            var tangent = Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f);
            var distance = Mathf.Max(height / tangent, width / (tangent * Mathf.Max(0.1f, aspect))) * 1.15f + depth;
            position = center + normal * Mathf.Max(0.12f, distance);
            rotation = Quaternion.LookRotation(-normal, up);
        }
    }
}
