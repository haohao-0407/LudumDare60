using System;
using System.IO;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace LudumDare60.TerminalDialogue.Editor
{
    /// <summary>Batch-mode integration smoke check. Writes an isolated demo scene, leaving gameplay scenes untouched.</summary>
    [InitializeOnLoad]
    public static class TerminalDialogueValidation
    {
        private const string RunKey = "TerminalDialogue.Validation";
        private static int frames;
        private static bool checkedRuntime;

        static TerminalDialogueValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (SessionState.GetBool(RunKey, false) && EditorApplication.isPlaying)
                EditorApplication.update += CheckRuntime;
        }

        public static void CreateDemoScene()
        {
            if (!TerminalDialogueDemoBuilder.EnsureTMPResources(CreateDemoScene)) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TerminalDialogueDemoBuilder.CreateDemo();
            var scene = SceneManager.GetActiveScene();
            Require(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1, "Exactly one EventSystem required.");
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            Require(Array.Exists(canvases, item => item.renderMode == RenderMode.WorldSpace), "World-space terminal missing.");
            Require(Array.Exists(canvases, item => item.renderMode == RenderMode.ScreenSpaceOverlay), "Screen-space replies missing.");
            foreach (var root in scene.GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, "Missing script: " + transform.name);
            Require(EditorSceneManager.SaveScene(scene, "Assets/Scenes/TerminalDialogueDemo.unity"), "Demo scene could not be saved.");
            AssetDatabase.SaveAssets();
            Debug.Log("TERMINAL_DIALOGUE_SCENE_OK");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // Invoke without -quit: exits after checking actual runtime button listeners and generated glyphs.
        public static void RunPlayModeSmoke()
        {
            TerminalDialogueDemoBuilder.GetTerminalFont();
            EditorSceneManager.OpenScene("Assets/Scenes/TerminalDialogueDemo.unity");
            SessionState.SetBool(RunKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(RunKey, false)) return;
            frames = 0; checkedRuntime = false;
            EditorApplication.update -= CheckRuntime;
            EditorApplication.update += CheckRuntime;
        }

        private static void CheckRuntime()
        {
            if (!EditorApplication.isPlaying || ++frames < 20 || checkedRuntime) return;
            checkedRuntime = true;
            try
            {
                var controller = UnityEngine.Object.FindAnyObjectByType<TerminalDialogueController>();
                Require(controller != null && controller.Session != null, "Controller did not start.");
                var session = controller.Session;
                for (var i = 0; i < 16 && session.Phase != TerminalPhase.WaitingForChoice; i++) controller.Advance();
                Require(session.Phase == TerminalPhase.WaitingForChoice, "Opening dialogue never reached choices.");
                var panel = UnityEngine.Object.FindAnyObjectByType<TerminalOptionsPanel>();
                Require(panel != null, "Options panel missing.");
                var buttons = panel.GetComponentsInChildren<TerminalChoiceButton>();
                Require(buttons.Length == 3, "Unexpected initial choice count.");
                var analyze = Array.Find(buttons, button => button.GetComponentInChildren<TMP_Text>().text == "ANALYZE SIGNAL");
                Require(analyze != null, "Analyze choice missing.");
                Canvas.ForceUpdateCanvases();
                // A batch editor has no active Game view; submit a frame so new Graphics receive render depths.
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) CapturePreview();
                Canvas.ForceUpdateCanvases();
                var hits = new List<RaycastResult>();
                var rect = (RectTransform)analyze.transform;
                var pointer = new PointerEventData(EventSystem.current)
                    { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
                EventSystem.current.RaycastAll(pointer, hits);
                if (!hits.Exists(hit => hit.gameObject == analyze.gameObject))
                {
                    Debug.Log("Raycast diagnostics: screen=" + Screen.width + "x" + Screen.height + ", pointer=" + pointer.position + ", button=" + rect.rect
                        + ", position=" + rect.position + ", hits=" + string.Join(",", hits.ConvertAll(hit => hit.gameObject.name)));
                    for (var parent = rect.parent; parent != null; parent = parent.parent)
                        if (parent is RectTransform parentRect) Debug.Log(parent.name + " rect=" + parentRect.rect + " position=" + parentRect.position);
                    if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) CapturePreview();
                }
                Require(hits.Exists(hit => hit.gameObject == analyze.gameObject), "Choice button cannot receive UI raycasts.");
                analyze.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Require(session.Phase == TerminalPhase.Typing && session.Messages[session.Messages.Count - 1].Speaker == TerminalSpeaker.Player,
                    "UI click did not start simulated player input.");
                Require(!session.Choose("risk"), "A second click was accepted during input.");
                controller.Advance();
                for (var i = 0; i < 12 && session.Phase != TerminalPhase.WaitingForChoice; i++) controller.Advance();
                Require(session.CurrentNodeId == "analysis" && session.Phase == TerminalPhase.WaitingForChoice, "Branch did not reach analysis choices.");
                controller.Choose("mark"); controller.Advance();
                for (var i = 0; i < 12 && session.Phase != TerminalPhase.WaitingForChoice; i++) controller.Advance();
                Require(session.GetFlag("has_coordinates") && session.Choices.Count == 4, "Conditional choice was not unlocked.");
                var screen = UnityEngine.Object.FindAnyObjectByType<TerminalTranscriptView>();
                var position = screen.transform.position;
                var close = Array.Find(panel.GetComponentsInChildren<UnityEngine.UI.Button>(), button => button.name == "Close");
                Require(close != null, "Close button missing."); close.onClick.Invoke();
                Require(!session.IsOpen, "Close button did not close dialogue.");
                var open = Array.Find(panel.GetComponentsInChildren<UnityEngine.UI.Button>(), button => button.name == "Open");
                Require(open != null, "Open button missing."); open.onClick.Invoke();
                Require(session.IsOpen && session.GetFlag("has_coordinates"), "Open reset dialogue progress.");
                Require(screen.transform.position == position, "World-space screen moved with dialogue UI.");
                var transcript = screen.GetComponentInChildren<TMP_Text>();
                Require(transcript.font != null && transcript.font.name == "Glass_TTY_VT220 SDF", "User-supplied terminal font was not applied.");
                foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    Require(text.font == transcript.font, "UI font mismatch: " + text.name);
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/TerminalDialogueSmoke.txt", "PASS: UI raycasts, runtime buttons, input animation, branching, flag conditions, close/reopen, world-space transform, shared Glass TTY font.\n");
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) CapturePreview();
                Debug.Log("TERMINAL_DIALOGUE_PLAYMODE_OK");
                SessionState.SetBool(RunKey, false);
                EditorApplication.update -= CheckRuntime;
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SessionState.SetBool(RunKey, false);
                EditorApplication.update -= CheckRuntime;
                EditorApplication.Exit(1);
            }
        }

        private static void CapturePreview()
        {
            var camera = Camera.main;
            var overlay = Array.Find(UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None),
                canvas => canvas.renderMode == RenderMode.ScreenSpaceOverlay);
            var target = new RenderTexture(1600, 900, 24);
            target.Create();
            var previousTarget = camera.targetTexture;
            var previousAspect = camera.aspect;
            var previousClearFlags = camera.clearFlags;
            var previousBackground = camera.backgroundColor;
            var previousMode = overlay.renderMode;
            var previousCamera = overlay.worldCamera;
            var previousDistance = overlay.planeDistance;
            camera.targetTexture = target;
            camera.aspect = 1600f / 900;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.06f);
            overlay.renderMode = RenderMode.ScreenSpaceCamera;
            overlay.worldCamera = camera;
            overlay.planeDistance = 0.5f;
            foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None)) text.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();
            var history = UnityEngine.Object.FindAnyObjectByType<TerminalTranscriptView>().GetComponentInChildren<UnityEngine.UI.ScrollRect>();
            history.verticalNormalizedPosition = 0;
            Canvas.ForceUpdateCanvases();
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
            File.WriteAllBytes("Logs/TerminalDialoguePreview.png", image.EncodeToPNG());
            RenderTexture.active = previous;
            camera.targetTexture = previousTarget;
            camera.aspect = previousAspect;
            camera.clearFlags = previousClearFlags;
            camera.backgroundColor = previousBackground;
            overlay.renderMode = previousMode;
            overlay.worldCamera = previousCamera;
            overlay.planeDistance = previousDistance;
            UnityEngine.Object.Destroy(image);
            target.Release(); UnityEngine.Object.Destroy(target);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
