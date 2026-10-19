using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LudumDare60.TerminalDialogue
{
    /// <summary>Disposable UI capture and curved surface overlay. Authored geometry/materials/UI remain intact.</summary>
    internal sealed class TerminalCurvedDisplay : IDisposable
    {
        private readonly Canvas source;
        private readonly MeshFilter display;
        private readonly int requestedWidth;
        private readonly GameObject captureRoot, stagingRoot, surface;
        private readonly Camera camera;
        private readonly Canvas captureCanvas;
        private readonly Material material;
        private readonly Dictionary<Transform, VisualCopy> visuals = new Dictionary<Transform, VisualCopy>();
        private readonly List<Transform> removed = new List<Transform>();
        private readonly List<CanvasRenderer> sourceRenderers = new List<CanvasRenderer>();
        private readonly List<float> originalAlphas = new List<float>();
        private readonly UnityEngine.UI.ScrollRect sourceScroll, captureScroll;
        private readonly TerminalTranscriptView sourceTranscript;
        private readonly TMP_Text capturedTranscript;
        private bool disposed;
        public RenderTexture Texture { get; }

        private sealed class VisualCopy
        {
            public Transform Original, Copy;
            public Graphic OriginalGraphic, CopyGraphic;
            public TMP_Text OriginalText, CopyText;
            public Image OriginalImage, CopyImage;
            public CanvasGroup OriginalGroup, CopyGroup;
        }

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
            stagingRoot = new GameObject("Inactive UI Clone Staging");
            stagingRoot.transform.SetParent(captureRoot.transform, false);
            stagingRoot.SetActive(false);
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
            PrepareVisualCopy(copy);
            RegisterTree(source.transform, copy.transform);
            sourceTranscript = source.GetComponent<TerminalTranscriptView>();
            var copiedView = copy.GetComponent<TerminalTranscriptView>();
            capturedTranscript = copiedView != null ? copiedView.TranscriptText : null;
            if (sourceTranscript != null && capturedTranscript != null)
                capturedTranscript.OnPreRenderText += sourceTranscript.ApplyTranscriptColors;
            captureCanvas = copy.GetComponent<Canvas>();
            captureCanvas.enabled = true; captureCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            captureCanvas.worldCamera = camera; captureCanvas.planeDistance = 1;
            var scaler = copy.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = rect.sizeDelta; scaler.matchWidthOrHeight = 0.5f;
            }
            sourceScroll = source.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
            captureScroll = copy.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
            if (captureScroll != null)
            {
                // This is a display-only copy. A second ScrollRect otherwise clamps/integrates its
                // content, then fights the position copied from the interactive source every frame.
                captureScroll.StopMovement();
                captureScroll.enabled = false;
            }
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

        private static void PrepareVisualCopy(GameObject root)
        {
            // Clones are render-only. A second SalvageController/InventoryController/view must never
            // start its own attempt, subscribe to rewards, poll input, or run a nested screen mount.
            // Run this while the copy is beneath an inactive parent, before any OnEnable/Start.
            foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
            {
                var visual = behaviour is Canvas || (behaviour is UIBehaviour
                    && !(behaviour is BaseRaycaster) && !(behaviour is EventSystem)
                    && !(behaviour is BaseInputModule) && !(behaviour is Selectable)
                    && !(behaviour is ScrollRect));
                if (!visual) behaviour.enabled = false;
            }
            foreach (var item in root.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = 31;
            foreach (var renderer in root.GetComponentsInChildren<CanvasRenderer>(true)) renderer.SetAlpha(1);
        }

        private void RegisterTree(Transform original, Transform copy)
        {
            visuals[original] = new VisualCopy
            {
                Original = original, Copy = copy,
                OriginalGraphic = original.GetComponent<Graphic>(), CopyGraphic = copy.GetComponent<Graphic>(),
                OriginalText = original.GetComponent<TMP_Text>(), CopyText = copy.GetComponent<TMP_Text>(),
                OriginalImage = original.GetComponent<Image>(), CopyImage = copy.GetComponent<Image>(),
                OriginalGroup = original.GetComponent<CanvasGroup>(), CopyGroup = copy.GetComponent<CanvasGroup>()
            };
            var renderer = original.GetComponent<CanvasRenderer>();
            if (renderer != null)
            {
                sourceRenderers.Add(renderer);
                originalAlphas.Add(renderer.GetAlpha());
            }
            for (var i = 0; i < original.childCount && i < copy.childCount; i++)
                RegisterTree(original.GetChild(i), copy.GetChild(i));
        }

        private void SyncVisualTree(Transform original, Transform copy, bool isRoot = false)
        {
            var visual = visuals[original];
            if (!isRoot)
            {
                if (copy.parent != visuals[original.parent].Copy)
                    copy.SetParent(visuals[original.parent].Copy, false);
                copy.SetSiblingIndex(original.GetSiblingIndex());
                copy.localRotation = original.localRotation;
                copy.localScale = original.localScale;
                if (original is RectTransform originalRect && copy is RectTransform copyRect)
                {
                    copyRect.anchorMin = originalRect.anchorMin;
                    copyRect.anchorMax = originalRect.anchorMax;
                    copyRect.pivot = originalRect.pivot;
                    copyRect.sizeDelta = originalRect.sizeDelta;
                    copyRect.anchoredPosition3D = originalRect.anchoredPosition3D;
                }
                else copy.localPosition = original.localPosition;
                if (copy.gameObject.activeSelf != original.gameObject.activeSelf)
                    copy.gameObject.SetActive(original.gameObject.activeSelf);
            }
            if (visual.OriginalGraphic != null && visual.CopyGraphic != null)
            {
                visual.CopyGraphic.enabled = visual.OriginalGraphic.enabled;
                visual.CopyGraphic.color = visual.OriginalGraphic.color;
                visual.CopyGraphic.raycastTarget = false;
            }
            if (visual.OriginalText != null && visual.CopyText != null)
            {
                var text = visual.OriginalText;
                var captured = visual.CopyText;
                if (captured.text != text.text) captured.text = text.text;
                captured.maxVisibleCharacters = text.maxVisibleCharacters;
                captured.fontSize = text.fontSize;
                captured.richText = text.richText;
            }
            if (visual.OriginalImage != null && visual.CopyImage != null)
            {
                var image = visual.OriginalImage;
                var captured = visual.CopyImage;
                captured.sprite = image.sprite;
                captured.type = image.type;
                captured.fillMethod = image.fillMethod;
                captured.fillOrigin = image.fillOrigin;
                captured.fillClockwise = image.fillClockwise;
                captured.fillAmount = image.fillAmount;
                captured.preserveAspect = image.preserveAspect;
            }
            if (visual.OriginalGroup != null && visual.CopyGroup != null)
                visual.CopyGroup.alpha = visual.OriginalGroup.alpha;

            for (var i = 0; i < original.childCount; i++)
            {
                var child = original.GetChild(i);
                if (!visuals.TryGetValue(child, out var childVisual))
                {
                    // Inventory rows can appear after the first capture. Clone only their visuals.
                    var newCopy = UnityEngine.Object.Instantiate(child.gameObject, stagingRoot.transform, false);
                    PrepareVisualCopy(newCopy);
                    RegisterTree(child, newCopy.transform);
                    newCopy.transform.SetParent(copy, false);
                    childVisual = visuals[child];
                }
                SyncVisualTree(child, childVisual.Copy);
            }
        }

        private void RemoveDestroyedVisuals()
        {
            removed.Clear();
            foreach (var pair in visuals)
            {
                if (pair.Key != null && pair.Value.Copy != null) continue;
                if (pair.Value.Copy != null)
                {
                    pair.Value.Copy.gameObject.SetActive(false);
                    Destroy(pair.Value.Copy.gameObject);
                }
                removed.Add(pair.Key);
            }
            foreach (var key in removed) visuals.Remove(key);
        }

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
            // Resolve source layout first, then mirror transforms, colors, fills and dynamic rows.
            // Text-only copying leaves a curved salvage track showing an unrelated/stale position.
            Canvas.ForceUpdateCanvases();
            RemoveDestroyedVisuals();
            SyncVisualTree(source.transform, captureCanvas.transform, true);
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
            for (var i = 0; i < sourceRenderers.Count; i++) if (sourceRenderers[i] != null) sourceRenderers[i].SetAlpha(originalAlphas[i]);
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
