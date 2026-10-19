using System;
using System.IO;
using System.Linq;
using LudumDare60.Landing;
using LudumDare60.Landing.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

[InitializeOnLoad]
public static class CockpitCameraValidation
{
    private const string Key = "CockpitCameraValidation.Run";
    private static CockpitCameraController controller;
    private static Camera camera;
    private static int stage, frames, current;
    private static double started;
    private static Vector3 freePosition;
    private static Quaternion freeRotation;
    private static float freeFov;

    static CockpitCameraValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { stage = frames = current = 0; started = EditorApplication.timeSinceStartup; EditorApplication.update += Check; }
        };
    }

    public static void Run()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var rig = new GameObject("ShipRig").transform; rig.position = new Vector3(400, 200, -700);
            var lookRoot = new GameObject("CameraLook"); lookRoot.transform.SetParent(rig, false); lookRoot.SetActive(false);
            var look = lookRoot.AddComponent<LandingCockpitLook>();
            look.Actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            lookRoot.SetActive(true);
            var shake = new GameObject("CameraShake").transform; shake.SetParent(look.transform, false);
            var view = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            view.tag = "MainCamera"; view.transform.SetParent(shake, false);
            view.transform.localPosition = new Vector3(0.12f, 0.2f, -0.1f);
            view.transform.localRotation = Quaternion.Euler(3, 4, 0);
            view.fieldOfView = 60;
            var host = new GameObject("Camera System"); host.transform.SetParent(rig, false); host.SetActive(false);
            var system = host.AddComponent<CockpitCameraController>();
            system.ViewCamera = view; system.FreeLook = look; system.Actions = look.Actions;
            system.RequireApplicationFocus = false; system.TransitionDuration = 0.2f;
            system.Screens = new CockpitScreenTarget[5];
            for (var i = 0; i < 5; i++)
            {
                var screen = GameObject.CreatePrimitive(PrimitiveType.Quad); screen.name = "Screen " + i;
                screen.transform.SetParent(rig, false); screen.transform.localPosition = new Vector3((i - 2) * 1.25f, 0, 5);
                var target = screen.AddComponent<CockpitScreenTarget>(); target.HitCollider = screen.GetComponent<Collider>();
                var anchor = new GameObject("View " + i).transform; anchor.SetParent(rig, false);
                CockpitCameraInstaller.CalculateView(screen.GetComponent<MeshFilter>(), view.transform.position, rig.up, 45, 16f / 9f, out var p, out var q);
                anchor.SetPositionAndRotation(p, q); target.ViewPoint = anchor;
                system.Screens[i] = target;
            }
            host.SetActive(true);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/CameraValidation.unity");
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        catch (Exception error) { Fail(error); }
    }

    private static void Check()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - started > 25) throw new TimeoutException("Camera validation timed out at stage " + stage);
            if (++frames < 10) return;
            if (stage == 0)
            {
                controller = UnityEngine.Object.FindAnyObjectByType<CockpitCameraController>(); camera = controller.ViewCamera;
                camera.targetTexture = new RenderTexture(1280, 720, 24); camera.targetTexture.Create();
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                var mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>(); InputSystem.EnableDevice(mouse);
                freePosition = camera.transform.localPosition; freeRotation = camera.transform.localRotation; freeFov = camera.fieldOfView;
                Physics.SyncTransforms();
                foreach (var target in controller.Screens)
                {
                    var point = camera.WorldToScreenPoint(target.transform.position);
                    Require(controller.TryPickScreen(point, out var picked) && picked == target, "Screen ray picking failed: " + target.name);
                    var bounds = target.GetComponent<MeshFilter>().sharedMesh.bounds;
                    Require(Vector3.Dot(target.ViewPoint.forward, (target.transform.TransformPoint(bounds.center) - target.ViewPoint.position).normalized) > 0.999f,
                        "Generated view does not face screen center.");
                }
                var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.transform.position = Vector3.Lerp(camera.transform.position, controller.Screens[2].transform.position, 0.5f);
                blocker.transform.localScale = Vector3.one * 0.5f; Physics.SyncTransforms();
                Require(!controller.TryPickScreen(camera.WorldToScreenPoint(controller.Screens[2].transform.position), out _), "Ray clicked through an occluder.");
                UnityEngine.Object.DestroyImmediate(blocker);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (var i = 0; i < 1000; i++) controller.TryPickScreen(camera.WorldToScreenPoint(controller.Screens[i % 5].transform.position), out _);
                watch.Stop();
                Debug.Log("CAMERA_RAY_BENCHMARK: " + watch.Elapsed.TotalMilliseconds / 1000 + " ms/click");
                CheckRenderTextureInput();
                Time.timeScale = 0; // Story reading pauses simulation, but camera UI must still work.
                BeginScreen(); stage = 1;
            }
            else if (stage == 1 && controller.State == CockpitViewState.Screen)
            {
                var target = controller.Screens[current];
                Require(Vector3.Distance(camera.transform.position, target.ViewPoint.position) < 0.0002f && Quaternion.Angle(camera.transform.rotation, target.ViewPoint.rotation) < 0.02f,
                    "Docked camera pose mismatch.");
                Require(controller.FreeLook.InputBlocked, "Fixed view did not lock seated look.");
                var rig = controller.transform.parent;
                rig.position += new Vector3(0.2f, 0.1f, 0); rig.rotation *= Quaternion.Euler(0, 2, 0);
                stage = 2; frames = 0;
            }
            else if (stage == 2)
            {
                Require(Vector3.Distance(camera.transform.position, controller.CurrentScreen.ViewPoint.position) < 0.0002f, "Fixed view did not follow ship motion.");
                SendMouse(true, true); Require(controller.State == CockpitViewState.Returning, "Right click did not exit fixed view.");
                stage = 3;
            }
            else if (stage == 3 && controller.State == CockpitViewState.Free)
            {
                Require(Vector3.Distance(camera.transform.localPosition, freePosition) < 0.00001f && Quaternion.Angle(camera.transform.localRotation, freeRotation) < 0.01f &&
                    Mathf.Approximately(camera.fieldOfView, freeFov), "Return did not restore original local camera pose/FOV.");
                Require(!controller.FreeLook.InputBlocked, "Return left free look blocked.");
                SendMouse(false, false);
                if (++current < 5) { BeginScreen(); stage = 1; }
                else
                {
                    controller.FocusScreen(controller.Screens[0]); controller.ReturnToFree();
                    stage = 4;
                }
            }
            else if (stage == 4 && controller.State == CockpitViewState.Free)
            {
                controller.FocusScreen(controller.Screens[0]); controller.enabled = false;
                Require(controller.State == CockpitViewState.Free && !controller.FreeLook.InputBlocked &&
                    Vector3.Distance(camera.transform.localPosition, freePosition) < 0.00001f, "Disable leaked camera ownership.");
                controller.enabled = true; controller.FocusScreen(controller.Screens[0]); stage = 5;
            }
            else if (stage == 5 && controller.State == CockpitViewState.Screen)
            {
                controller.CurrentScreen.gameObject.SetActive(false); stage = 6;
            }
            else if (stage == 6 && controller.State == CockpitViewState.Free)
            {
                Time.timeScale = 1;
                var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Logs/CockpitCameraValidation.txt"));
                File.WriteAllText(output, "PASS: offscreen RenderTexture UI cannot block screen selection; main-camera UI still blocks click-through; all five display colliders ray-picked; blockers respected; straight-on anchors; Input System left/right clicks; smooth transition and pose/FOV restoration; free-look locking; moving ship; story pause interaction; cancellation; safe disable; disappearing screen recovery. See log for ray benchmark.");
                SessionState.SetBool(Key, false); EditorApplication.update -= Check;
                Debug.Log("COCKPIT_CAMERA_VALIDATION_OK"); EditorApplication.Exit(0);
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private static void BeginScreen()
    {
        Physics.SyncTransforms();
        SendMouse(false, false);
        SendMouse(true, false);
        Require(controller.State == CockpitViewState.EnteringScreen && controller.CurrentScreen == controller.Screens[current], "Left click did not select expected screen.");
        Require(Vector3.Distance(camera.transform.localPosition, freePosition) < 0.00001f, "Focus snapped instead of starting smooth transition.");
        SendMouse(false, false);
    }

    private static void CheckRenderTextureInput()
    {
        var eventSystem = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem)).GetComponent<UnityEngine.EventSystems.EventSystem>();
        var capture = new GameObject("Repair Capture", typeof(Camera)).GetComponent<Camera>();
        capture.transform.position = new Vector3(10000, 10000, 10000);
        capture.orthographic = true; capture.orthographicSize = 1; capture.cullingMask = 1 << 30; capture.enabled = false;
        capture.targetTexture = new RenderTexture(1280, 720, 24); capture.targetTexture.Create();
        var root = new GameObject("Repair UI", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
        root.layer = 30;
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = capture; canvas.planeDistance = 1;
        var image = new GameObject("Offscreen Background", typeof(RectTransform), typeof(UnityEngine.UI.Image)); image.layer = 30;
        var rect = image.GetComponent<RectTransform>(); rect.SetParent(root.transform, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        Canvas.ForceUpdateCanvases(); capture.Render();
        var position = (Vector2)camera.WorldToScreenPoint(controller.Screens[2].transform.position);
        var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        eventSystem.RaycastAll(new UnityEngine.EventSystems.PointerEventData(eventSystem) { position = position }, hits);
        Require(hits.Any(item => item.gameObject == image), "Regression setup did not reproduce the offscreen UI raycast hit.");
        var filter = typeof(CockpitCameraController).GetMethod("PointerBlockedByOverlay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Require(!(bool)filter.Invoke(controller, new object[] { position }), "RenderTexture capture UI incorrectly blocks physical screen selection.");
        canvas.worldCamera = camera; Canvas.ForceUpdateCanvases(); camera.Render();
        Require((bool)filter.Invoke(controller, new object[] { position }), "Visible main-camera UI no longer blocks click-through.");
        UnityEngine.Object.DestroyImmediate(root);
        var texture = capture.targetTexture; UnityEngine.Object.DestroyImmediate(capture.gameObject); texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(eventSystem.gameObject);
    }

    private static void SendMouse(bool pressed, bool right)
    {
        var target = controller.Screens[Mathf.Min(current, 4)];
        var state = new MouseState { position = camera.WorldToScreenPoint(target.transform.position) };
        if (pressed) state = state.WithButton(right ? MouseButton.Right : MouseButton.Left);
        InputSystem.QueueStateEvent(Mouse.current, state); InputSystem.Update();
        controller.SendMessage("Update", SendMessageOptions.RequireReceiver);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Fail(Exception error)
    { SessionState.SetBool(Key, false); EditorApplication.update -= Check; Time.timeScale = 1; Debug.LogException(error); EditorApplication.Exit(1); }
}
