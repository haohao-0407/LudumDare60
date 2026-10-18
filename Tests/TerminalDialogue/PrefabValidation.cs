using System;
using System.IO;
using System.Linq;
using LudumDare60.TerminalDialogue;
using LudumDare60.TerminalDialogue.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class TerminalPrefabValidation
{
    private const string RunKey = "TerminalPrefabValidation.Run";
    private static int frames;
    static TerminalPrefabValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(RunKey, false))
            { frames = 0; EditorApplication.update += CheckRuntime; }
        };
    }

    public static void Run()
    {
        try
        {
            GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
            var legacy = AssetDatabase.LoadAssetAtPath<TerminalDialogueDefinition>("Assets/Dialogue/222.asset");
            Require(legacy.Graph.DefaultTextColor.Equals(TerminalTextColor.Default), "Legacy asset did not retain the original green default.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TerminalPrefabBuilder.Build(TerminalPrefabBuilder.PrefabPath);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(TerminalPrefabBuilder.PrefabPath);
            Require(asset != null, "Prefab not created.");
            Require(asset.GetComponentsInChildren<Canvas>(true).Length == 2, "Both UI canvases must be packaged.");
            Require(asset.GetComponentsInChildren<Camera>(true).Length == 0, "A demo camera leaked into the prefab.");
            Require(asset.GetComponentsInChildren<EventSystem>(true).Length == 0, "Prefab must share the scene EventSystem.");
            foreach (var behaviour in asset.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Require(behaviour != null, "Missing script in prefab.");
                var serialized = new SerializedObject(behaviour); var iterator = serialized.GetIterator();
                while (iterator.Next(true))
                    if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.objectReferenceValue is Component component)
                        Require(component.transform.IsChildOf(asset.transform) || component.transform == asset.transform,
                            "External scene reference in prefab: " + behaviour.name + "." + iterator.propertyPath);
            }
            foreach (var text in asset.GetComponentsInChildren<TMP_Text>(true))
                Require(text.font != null && text.font.name == "Glass_TTY_VT220 SDF", "Incorrect terminal font.");

            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, "Assets/ValidationRenderer.asset");
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, "Assets/ValidationPipeline.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            var screenAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CompactCockpit_WallIntegrated/Prefabs/Screens/Screen_NAVIGATION.prefab");
            var physicalScreen = (GameObject)PrefabUtility.InstantiatePrefab(screenAsset);
            physicalScreen.name = "Physical Navigation Screen";
            physicalScreen.transform.rotation = Quaternion.Euler(-12, 15, 0);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            var mount = rig.GetComponent<TerminalScreenMount>();
            mount.SetCurvedRendering(false);
            Require(mount.AttachTo(physicalScreen.transform), "Could not identify the actual navigation display.");
            var display = physicalScreen.GetComponentsInChildren<MeshFilter>().Single(item => item.name.StartsWith("SCREEN_"));
            var canvas = mount.ScreenCanvas; var rect = (RectTransform)canvas.transform;
            var normal = display.transform.forward;
            var bounds = display.sharedMesh.bounds;
            var worldCenter = display.transform.TransformPoint(bounds.center);
            Require(Vector3.Dot(-rect.forward, normal) > 0.999f, "Canvas is facing the wrong way.");
            Require(Vector3.Dot(rect.position - worldCenter, normal) > 0, "Canvas is behind the screen surface.");
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Require(Mathf.Abs(Vector3.Distance(corners[0], corners[3]) - bounds.size.x * 0.98f) < 0.001f, "Display width was not fitted.");
            Require(Mathf.Abs(Vector3.Distance(corners[0], corners[1]) - bounds.size.y * 0.98f) < 0.001f, "Display height was not fitted.");
            var overlay = rig.GetComponentsInChildren<Canvas>().Single(item => item.renderMode == RenderMode.ScreenSpaceOverlay);
            var overlayPosition = overlay.transform.position;
            var oldPosition = rect.position;
            physicalScreen.transform.position += Vector3.right * 0.2f;
            mount.FitToScreen();
            Require((rect.position - oldPosition - Vector3.right * 0.2f).sqrMagnitude < 1e-8f, "Terminal did not follow its physical screen.");
            Require(overlay.transform.position == overlayPosition, "Mounting moved the screen-space replies.");
            physicalScreen.transform.position -= Vector3.right * 0.2f; mount.FitToScreen();
            Require(!mount.AttachTo(new GameObject("Empty target").transform), "Empty target should be rejected.");
            mount.AttachTo(physicalScreen.transform);
            var originalMaterial = display.GetComponent<MeshRenderer>().sharedMaterial;
            mount.SetCurvedRendering(true); mount.FitToScreen();
            Require(mount.ScreenTexture != null && mount.ScreenTexture.IsCreated(), "Curved display texture was not allocated.");
            var curvedSurface = display.transform.Find("__DOS_CurvedSurface");
            Require(curvedSurface != null && curvedSurface.GetComponent<MeshFilter>().sharedMesh == display.sharedMesh,
                "Curved output must use the exact original screen geometry.");
            Require(display.GetComponent<MeshRenderer>().sharedMaterial == originalMaterial, "Original shared screen material was changed.");
            var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = rect.position + normal * 1.4f + display.transform.up * 0.03f;
            camera.transform.LookAt(rect.position + display.transform.right * -0.25f, display.transform.up);
            camera.fieldOfView = 42;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.035f, 0.045f, 0.055f);
            camera.nearClipPlane = 0.01f;
            var light = new GameObject("Key Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 2; light.transform.rotation = camera.transform.rotation;
            // Exactly one pre-existing EventSystem must be reused by every prefab instance.
            TerminalScreenMount.EnsureEventSystem(); TerminalScreenMount.EnsureEventSystem();
            Require(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1, "EventSystem duplicated.");
            var conversation = ScriptableObject.CreateInstance<TerminalDialogueDefinition>();
            conversation.Graph.AIName = "SHIP AI"; conversation.Graph.EntryNodeId = "hello";
            var hello = new TerminalNode { Id = "hello" };
            hello.Lines.Add(new TerminalLine { Text = "Hello, Captain. The terminal is ready." });
            hello.Lines.Add(new TerminalLine { Speaker = TerminalSpeaker.System, DisplayName = "REACTOR",
                Text = "Warning: <b>signal unstable</b>.", UseCustomColor = true, CustomColor = new TerminalTextColor(1, 0.55f, 0.1f) });
            hello.Choices.Add(new TerminalChoice { Id = "ready", Label = "READY", PlayerText = "Ready. Continue.", NextNodeId = "next",
                DisplayName = "CAPTAIN", UseCustomColor = true, CustomColor = new TerminalTextColor(0.2f, 0.8f, 1) });
            var next = new TerminalNode { Id = "next" }; next.Lines.Add(new TerminalLine { Text = "Navigation link established." });
            conversation.Graph.Nodes.Add(hello); conversation.Graph.Nodes.Add(next);
            AssetDatabase.CreateAsset(conversation, "Assets/ValidationConversation.asset");
            var controller = rig.GetComponent<TerminalDialogueController>();
            controller.Configure(conversation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(mount);
            PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
            PrefabUtility.RecordPrefabInstancePropertyModifications(physicalScreen.transform);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/ValidationScene.unity");
            Require(!File.ReadAllText("Assets/ValidationScene.unity").Contains("__DOS_Curved"), "Temporary capture objects leaked into the saved scene.");
            SessionState.SetBool(RunKey, true); EditorApplication.isPlaying = true;
        }
        catch (Exception error) { Fail(error); }
    }

    private static void CheckRuntime()
    {
        if (++frames < 30) return;
        EditorApplication.update -= CheckRuntime;
        try
        {
            var controller = UnityEngine.Object.FindAnyObjectByType<TerminalDialogueController>();
            Require(controller.Session != null, "Prefab conversation did not start.");
            for (var i = 0; i < 10 && controller.Session.Phase != TerminalPhase.WaitingForChoice; i++) controller.Advance();
            Require(controller.Session.Phase == TerminalPhase.WaitingForChoice, "Prefab never displayed replies.");
            var choice = controller.GetComponentInChildren<TerminalOptionsPanel>().GetComponentsInChildren<TerminalChoiceButton>().Single();
            Require(choice.GetComponentInChildren<TMP_Text>().text == "READY", "Reply button is not bound to the prefab controller.");
            Canvas.ForceUpdateCanvases();
            var mount = controller.GetComponent<TerminalScreenMount>(); mount.FitToScreen();
            Require(mount.CurvedScreen && mount.ScreenTexture != null && mount.ScreenTexture.IsCreated(), "Curved capture did not survive entering Play mode.");
            var sourceText = mount.ScreenCanvas.GetComponentsInChildren<TMP_Text>().Single(item => item.name == "Transcript");
            var capturedCanvas = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Single(item => item.name == "Captured Terminal UI");
            var capturedText = capturedCanvas.GetComponentsInChildren<TMP_Text>().Single(item => item.name == "Transcript");
            foreach (var text in new[] { sourceText, capturedText })
            {
                Require(!text.richText, "Color support enabled authored markup unexpectedly.");
                Require(text.text.Contains("[SHIP AI] Hello") && text.text.Contains("[REACTOR] Warning"), "Default/custom names did not render in " + text.name);
                AssertGlyphColor(text, "Hello", new Color(0.65f, 1, 0.76f));
                AssertGlyphColor(text, "Warning", new Color(1, 0.55f, 0.1f));
                AssertGlyphColor(text, "REACTOR", new Color(1, 0.55f, 0.1f));
                AssertGlyphColor(text, "<b>", new Color(1, 0.55f, 0.1f));
            }
            var capture = mount.ScreenTexture;
            var previous = RenderTexture.active; RenderTexture.active = capture;
            var pixels = new Texture2D(capture.width, capture.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, capture.width, capture.height), 0, 0); pixels.Apply();
            RenderTexture.active = previous;
            Require(pixels.GetPixels32().Count(pixel => pixel.g > 50 && pixel.g > pixel.r * 1.2f) > 100, "Curved texture contains no terminal text.");
            UnityEngine.Object.Destroy(pixels);
            Require(mount.ScreenCanvas.GetComponentsInChildren<CanvasRenderer>().All(item => item.GetAlpha() == 0), "Original flat UI is still visible.");
            Capture();
            Require(!ShaderUtil.ShaderHasError(Resources.Load<Shader>("TerminalCurvedScreen")), "Curved screen shader failed to compile.");
            var results = new System.Collections.Generic.List<RaycastResult>();
            var rect = (RectTransform)choice.transform;
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) }, results);
            Require(results.Any(result => result.gameObject == choice.gameObject), "Reply cannot receive a pointer raycast.");
            choice.GetComponent<Button>().onClick.Invoke();
            Require(controller.Session.Messages.Last().Speaker == TerminalSpeaker.Player, "Reply did not start terminal command typing.");
            controller.Advance(); controller.Advance();
            Require(controller.Session.CurrentNodeId == "next", "Reply did not enter the next conversation node.");
            controller.Advance(); mount.FitToScreen();
            foreach (var text in new[] { sourceText, capturedText })
            {
                Require(text.text.Contains("[CAPTAIN] Ready.") && text.text.Contains("[SHIP AI] Navigation"), "Custom reply name leaked into next line or was lost.");
                AssertGlyphColor(text, "CAPTAIN", new Color(0.2f, 0.8f, 1));
                AssertGlyphColor(text, "Ready.", new Color(0.2f, 0.8f, 1));
                AssertGlyphColor(text, "Navigation", new Color(0.65f, 1, 0.76f));
                AssertGlyphColor(text, "Warning", new Color(1, 0.55f, 0.1f));
            }
            Capture();
            Require(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1, "Runtime duplicated EventSystem.");
            mount.SetCurvedRendering(false);
            Require(mount.ScreenTexture == null && mount.ScreenCanvas.GetComponentsInChildren<CanvasRenderer>().All(item => item.GetAlpha() > 0), "Disabling curved mode did not restore the original UI.");
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../..", "Logs"));
            File.WriteAllText(Path.Combine(output, "TerminalPrefabValidation.txt"), "PASS: default/custom speaker display names and colors in flat and curved text, custom player name, literal markup, legacy asset defaults, prefab references/font, curved navigation mesh and texture pixels, original material preserved, UI restoration, independent reply raycast and dialogue progression.");
            Debug.Log("TERMINAL_PREFAB_VALIDATION_OK");
            SessionState.SetBool(RunKey, false); EditorApplication.Exit(0);
        }
        catch (Exception error) { Fail(error); }
    }

    private static void AssertGlyphColor(TMP_Text text, string fragment, Color32 expected)
    {
        text.ForceMeshUpdate();
        var index = text.text.IndexOf(fragment, StringComparison.Ordinal);
        Require(index >= 0, "Missing rendered fragment: " + fragment);
        var character = text.textInfo.characterInfo.Take(text.textInfo.characterCount).First(item => item.index == index);
        Require(character.isVisible, "Color check fragment is not visible: " + fragment);
        var actual = text.textInfo.meshInfo[character.materialReferenceIndex].colors32[character.vertexIndex];
        Require(actual.Equals(expected), "Incorrect glyph color in " + text.name + ": " + fragment + " " + actual + " expected " + expected);
    }

    private static void Capture()
    {
        var camera = Camera.main;
        var overlay = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Single(item => item.renderMode == RenderMode.ScreenSpaceOverlay);
        var texture = new RenderTexture(1600, 900, 24); texture.Create(); camera.aspect = 1600f / 900;
        overlay.renderMode = RenderMode.ScreenSpaceCamera; overlay.worldCamera = camera; overlay.planeDistance = 0.2f;
        Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
        var previous = RenderTexture.active; RenderTexture.active = texture;
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../..", "Logs"));
        File.WriteAllBytes(Path.Combine(output, "DOS-Terminal-On-Navigation-Screen.png"), image.EncodeToPNG());
        RenderTexture.active = previous;
        overlay.renderMode = RenderMode.ScreenSpaceOverlay; overlay.worldCamera = null;
        UnityEngine.Object.Destroy(image); texture.Release(); UnityEngine.Object.Destroy(texture);
        Canvas.ForceUpdateCanvases();
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Fail(Exception error)
    {
        SessionState.SetBool(RunKey, false); EditorApplication.update -= CheckRuntime;
        Debug.LogException(error); EditorApplication.Exit(1);
    }
}
