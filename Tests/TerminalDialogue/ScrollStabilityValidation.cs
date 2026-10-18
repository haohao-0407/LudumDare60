using System;
using System.IO;
using System.Linq;
using LudumDare60.TerminalDialogue;
using LudumDare60.TerminalDialogue.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class TerminalScrollStabilityValidation
{
    private const string Key = "TerminalScrollStabilityValidation.Run";
    private static int frames, lastFrame, stage;
    private static TerminalScreenMount mount;
    private static ScrollRect source, copy;
    private static TMP_Text text, title;
    private static Camera camera;
    private static float minY, maxY, minTitle, maxTitle, minBody, maxBody;
    private static System.Collections.Generic.HashSet<int> hashes = new System.Collections.Generic.HashSet<int>();
    private static System.Text.StringBuilder report = new System.Text.StringBuilder();
    private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../..", "Logs"));
    static TerminalScrollStabilityValidation()
    {
        EditorApplication.playModeStateChanged += change =>
        { if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) { frames = stage = 0; lastFrame = -1; EditorApplication.update += Tick; } };
    }
    public static void Run()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/ValidationPipeline.asset");
            if (pipeline == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer, "Assets/ProbeRenderer.asset");
                pipeline = UniversalRenderPipelineAsset.Create(renderer); AssetDatabase.CreateAsset(pipeline, "Assets/ProbePipeline.asset");
            }
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            var physical = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CompactCockpit_WallIntegrated/Prefabs/Screens/Screen_NAVIGATION.prefab"));
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TerminalPrefabBuilder.PrefabPath));
            var mount = rig.GetComponent<TerminalScreenMount>(); mount.AttachTo(physical.transform);
            var conversation = ScriptableObject.CreateInstance<TerminalDialogueDefinition>(); conversation.Graph.EntryNodeId = "test";
            var node = new TerminalNode { Id = "test" }; node.Lines.Add(new TerminalLine { Speaker = TerminalSpeaker.System, Text = "ccc", WaitForContinue = true });
            conversation.Graph.Nodes.Add(node); AssetDatabase.CreateAsset(conversation, "Assets/ProbeConversation.asset");
            var controller = rig.GetComponent<TerminalDialogueController>(); controller.Configure(conversation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller); PrefabUtility.RecordPrefabInstancePropertyModifications(mount);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/ProbeScene.unity");
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        catch (Exception error) { Fail(error); }
    }
    private static void Tick()
    {
        try
        {
            if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
            if (++frames <= 30) return;
            if (stage == 0)
            {
                mount = UnityEngine.Object.FindAnyObjectByType<TerminalScreenMount>();
                mount.GetComponent<TerminalDialogueController>().Advance(); mount.FitToScreen();
                source = mount.ScreenCanvas.GetComponentInChildren<ScrollRect>();
                copy = UnityEngine.Object.FindObjectsByType<Canvas>().Single(item => item.name == "Captured Terminal UI").GetComponentInChildren<ScrollRect>();
                camera = copy.GetComponentInParent<Canvas>().worldCamera;
                text = copy.GetComponentsInChildren<TMP_Text>().Single(item => item.name == "Transcript");
                title = copy.GetComponentInParent<Canvas>().GetComponentsInChildren<TMP_Text>().Single(item => item.name == "Title");
                stage = 1; Reset(); return;
            }
            if (frames < 15) return;
            mount.FitToScreen();
            var y = copy.content.anchoredPosition.y;
            Require(source.enabled && !copy.enabled, "Only the interactive source should run ScrollRect logic.");
            Require((copy.content.anchoredPosition - source.content.anchoredPosition).sqrMagnitude < 0.000001f,
                "Capture did not follow the source's actual content position.");
            var bodyY = PixelY(text); var titleY = PixelY(title);
            minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            minBody = Mathf.Min(minBody, bodyY); maxBody = Mathf.Max(maxBody, bodyY);
            minTitle = Mathf.Min(minTitle, titleY); maxTitle = Mathf.Max(maxTitle, titleY);
            if (frames % 4 == 0) hashes.Add(ReadHash());
            if (frames < 180) return;
            report.AppendLine($"stage={stage}; copyScrollEnabled={copy.enabled}; contentYRange={maxY-minY:F6}; bodyPixelRange={maxBody-minBody:F6}; titlePixelRange={maxTitle-minTitle:F6}; imageVariants={hashes.Count}; ortho={camera.orthographicSize}; contentY={copy.content.anchoredPosition.y}; sourceY={source.content.anchoredPosition.y}");
            Require(maxY - minY < 0.001f && maxBody - minBody < 0.01f && hashes.Count == 1,
                "Static transcript moved or its rendered pixels changed: " + report);
            if (stage == 1)
            {
                var originalText = source.GetComponentsInChildren<TMP_Text>().Single(item => item.name == "Transcript");
                originalText.text = string.Join("\n\n", Enumerable.Range(0, 24).Select(i => "[SYSTEM] Log entry " + i + ": navigation link ready."));
                Canvas.ForceUpdateCanvases(); source.verticalNormalizedPosition = 0.37f; source.StopMovement();
                stage = 2; Reset(); return;
            }
            File.WriteAllText(Path.Combine(Output, "TerminalScrollStabilityValidation.txt"), "PASS: short and overflowing transcripts remain stable over 180-frame samples; source scroll stays enabled; display copy stays passive; manual scroll position is mirrored; rendered region has one pixel hash per scenario.\n" + report);
            Debug.Log("TERMINAL_SCROLL_STABILITY_OK " + report); SessionState.SetBool(Key, false); EditorApplication.Exit(0);
        }
        catch (Exception error) { Fail(error); }
    }
    private static float PixelY(TMP_Text item)
    {
        item.ForceMeshUpdate();
        var glyph = item.textInfo.characterInfo.First(character => character.isVisible);
        return camera.WorldToViewportPoint(item.transform.TransformPoint(glyph.bottomLeft)).y * mount.ScreenTexture.height;
    }
    private static int ReadHash()
    {
        var texture = mount.ScreenTexture; var previous = RenderTexture.active; RenderTexture.active = texture;
        int height = Mathf.RoundToInt(texture.height * 0.7f), y = Mathf.RoundToInt(texture.height * 0.25f);
        var pixels = new Texture2D(texture.width, height, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, y, texture.width, height), 0, 0); pixels.Apply();
        var bytes = pixels.GetRawTextureData<byte>(); int hash = 17; unchecked { for (int i = 0; i < bytes.Length; i++) hash = hash * 31 + bytes[i]; }
        RenderTexture.active = previous; UnityEngine.Object.Destroy(pixels); return hash;
    }
    private static void Reset() { frames = 0; hashes.Clear(); minY = minBody = minTitle = float.MaxValue; maxY = maxBody = maxTitle = float.MinValue; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Fail(Exception error) { SessionState.SetBool(Key, false); Debug.LogException(error); EditorApplication.Exit(1); }
}
