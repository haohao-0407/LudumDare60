using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LudumDare60.TerminalDialogue
{
    /// <summary>Disposable UI capture and curved surface overlay. Authored geometry/materials/UI remain intact.</summary>
    internal sealed class TerminalCurvedDisplay : IDisposable
    {
        private readonly Canvas source;
        private readonly MeshFilter display;
        private readonly int requestedWidth;
        private readonly GameObject captureRoot, surface;
        private readonly Camera camera;
        private readonly Canvas captureCanvas;
        private readonly Material material;
        private readonly TMP_Text[] sourceTexts, captureTexts;
        private readonly CanvasRenderer[] sourceRenderers;
        private readonly float[] originalAlphas;
        private readonly UnityEngine.UI.ScrollRect sourceScroll, captureScroll;
        private readonly TerminalTranscriptView sourceTranscript;
        private readonly TMP_Text capturedTranscript;
        private bool disposed;
        public RenderTexture Texture { get; }

        public TerminalCurvedDisplay(Canvas source, MeshFilter display, int width)
        {
            this.source = source; this.display = display; requestedWidth = width;
            var rect = (RectTransform)source.transform;
            var resolution = Mathf.Clamp(width, 512, 2048);
            var height = Mathf.Clamp(Mathf.RoundToInt(resolution * rect.rect.height / rect.rect.width), 128, 4096);
            var shader = Resources.Load<Shader>("TerminalCurvedScreen");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("TerminalCurvedScreen shader is missing or unsupported.");
            Texture = new RenderTexture(resolution, height, 24, RenderTextureFormat.ARGB32)
            { name = "DOS Screen Capture", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Texture.Create();
            captureRoot = new GameObject("__DOS_CurvedCapture") { hideFlags = HideFlags.HideAndDontSave };
            captureRoot.SetActive(false);
            SceneManager.MoveGameObjectToScene(captureRoot, source.gameObject.scene);
            captureRoot.transform.position = new Vector3(30000 + ((uint)source.GetHashCode() % 1024) * 8, 30000, 30000);
            var cameraObject = new GameObject("Capture Camera"); cameraObject.transform.SetParent(captureRoot.transform, false);
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 0.5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 3; camera.cullingMask = 1 << 31;
            camera.targetTexture = Texture; camera.allowHDR = false; camera.allowMSAA = false;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false; cameraData.renderShadows = false;
            cameraData.requiresColorOption = CameraOverrideOption.Off; cameraData.requiresDepthOption = CameraOverrideOption.Off;
            // Instantiate beneath an inactive parent: cloned dialogue/audio scripts never start or subscribe.
            var copy = UnityEngine.Object.Instantiate(source.gameObject, captureRoot.transform, false);
            copy.name = "Captured Terminal UI";
            sourceTranscript = source.GetComponent<TerminalTranscriptView>();
            var copiedView = copy.GetComponent<TerminalTranscriptView>();
            capturedTranscript = copiedView != null ? copiedView.TranscriptText : null;
            foreach (var view in copy.GetComponentsInChildren<TerminalTranscriptView>(true)) view.enabled = false;
            if (sourceTranscript != null && capturedTranscript != null)
                capturedTranscript.OnPreRenderText += sourceTranscript.ApplyTranscriptColors;
            foreach (var raycaster in copy.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true)) raycaster.enabled = false;
            foreach (var item in copy.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = 31;
            foreach (var renderer in copy.GetComponentsInChildren<CanvasRenderer>(true)) renderer.SetAlpha(1);
            captureCanvas = copy.GetComponent<Canvas>();
            captureCanvas.enabled = true; captureCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            captureCanvas.worldCamera = camera; captureCanvas.planeDistance = 1;
            var scaler = copy.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = rect.sizeDelta; scaler.matchWidthOrHeight = 0.5f;
            }
            sourceTexts = source.GetComponentsInChildren<TMP_Text>(true);
            captureTexts = copy.GetComponentsInChildren<TMP_Text>(true);
            sourceScroll = source.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
            captureScroll = copy.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
            if (captureScroll != null)
            {
                // This is a display-only copy. A second ScrollRect otherwise clamps/integrates its
                // content, then fights the position copied from the interactive source every frame.
                captureScroll.StopMovement();
                captureScroll.enabled = false;
            }
            sourceRenderers = source.GetComponentsInChildren<CanvasRenderer>(true);
            originalAlphas = new float[sourceRenderers.Length];
            for (var i = 0; i < sourceRenderers.Length; i++) originalAlphas[i] = sourceRenderers[i].GetAlpha();
            material = CoreUtils.CreateEngineMaterial(shader);
            material.SetTexture("_TerminalTexture", Texture);
            surface = new GameObject("__DOS_CurvedSurface", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.HideAndDontSave };
            surface.layer = display.gameObject.layer;
            surface.transform.SetParent(display.transform, false);
            surface.GetComponent<MeshFilter>().sharedMesh = display.sharedMesh;
            var meshRenderer = surface.GetComponent<MeshRenderer>(); meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off; meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off; meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            captureRoot.SetActive(true);
            Canvas.willRenderCanvases += HideFlatGraphics;
        }

        public bool Matches(Canvas canvas, MeshFilter filter, int width) => !disposed && source == canvas && display == filter && requestedWidth == width;

        private void HideFlatGraphics()
        {
            // CanvasRenderer opacity is runtime-only; no source Image/Text/Canvas property is serialized as hidden.
            if (disposed) return;
            foreach (var renderer in sourceRenderers) if (renderer != null) renderer.SetAlpha(0);
        }

        public void Render(Bounds bounds, Vector3 normal, Vector3 up, float fill, float offset)
        {
            if (disposed || camera == null || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return;
            var right = Vector3.Cross(up, -normal);
            material.SetVector("_ScreenCenter", bounds.center);
            material.SetVector("_ScreenRight", right);
            material.SetVector("_ScreenUp", up);
            material.SetVector("_ScreenNormal", normal);
            material.SetVector("_TerminalSurfaceSize", new Vector4(Mathf.Abs(Vector3.Dot(bounds.size, right)), Mathf.Abs(Vector3.Dot(bounds.size, up)), fill, offset));
            for (var i = 0; i < sourceTexts.Length && i < captureTexts.Length; i++)
            {
                var original = sourceTexts[i]; var copy = captureTexts[i];
                if (copy.text != original.text) copy.text = original.text;
                copy.maxVisibleCharacters = original.maxVisibleCharacters;
                copy.enabled = original.enabled;
            }
            Canvas.ForceUpdateCanvases();
            if (sourceScroll != null && captureScroll != null && sourceScroll.content != null && captureScroll.content != null)
                // Identical UI hierarchies share UI coordinates. Avoid normalized-position setters:
                // those recalculate world-space bounds and amplify rounding in the remote capture rig.
                captureScroll.content.anchoredPosition = sourceScroll.content.anchoredPosition;
            HideFlatGraphics();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = Texture });
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            Canvas.willRenderCanvases -= HideFlatGraphics;
            if (sourceTranscript != null && capturedTranscript != null)
                capturedTranscript.OnPreRenderText -= sourceTranscript.ApplyTranscriptColors;
            for (var i = 0; i < sourceRenderers.Length; i++) if (sourceRenderers[i] != null) sourceRenderers[i].SetAlpha(originalAlphas[i]);
            if (camera != null) camera.targetTexture = null;
            Destroy(surface); Destroy(captureRoot); CoreUtils.Destroy(material);
            if (Texture != null) { Texture.Release(); Destroy(Texture); }
        }
        private static void Destroy(UnityEngine.Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(item); else UnityEngine.Object.DestroyImmediate(item);
        }
    }
}
