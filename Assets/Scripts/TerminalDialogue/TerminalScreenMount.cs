using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace LudumDare60.TerminalDialogue
{
    /// <summary>Mounts just the world Canvas to a physical display; the reply Canvas stays in screen space.</summary>
    [ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(100)]
    public sealed class TerminalScreenMount : MonoBehaviour
    {
        [SerializeField, InspectorName("终端画面 Canvas")] private Canvas screenCanvas;
        [SerializeField, InspectorName("目标屏幕")]
        [Tooltip("拖入 SCREEN_NAVIGATION 显示面，或它的 Screen_NAVIGATION 父节点。不要选择整个驾驶舱。")]
        private Transform screenTarget;
        [SerializeField, Tooltip("先把 UI 渲染到纹理，再显示到原屏幕曲面上，解决凸起玻璃穿过平面 Canvas。")]
        private bool curvedScreen = true;
        [SerializeField, Range(512, 2048)] private int textureWidth = 1024;
        [SerializeField, Range(0.5f, 1f), InspectorName("屏幕填充比例")] private float fill = 0.98f;
        [SerializeField, Min(0.0001f), InspectorName("离屏幕表面距离（米）")] private float surfaceOffset = 0.002f;
        [SerializeField, InspectorName("翻转正反面")] private bool flipFacing;
        [SerializeField, InspectorName("随屏幕移动")] private bool followTarget = true;
        private Transform cachedTarget;
        private MeshFilter cachedFilter;
        private bool refreshTarget = true;
        private TerminalCurvedDisplay curvedDisplay;

        public Canvas ScreenCanvas => screenCanvas;
        public Transform ScreenTarget => screenTarget;
        public bool CurvedScreen => curvedScreen;
        public RenderTexture ScreenTexture => curvedDisplay?.Texture;

        public void SetCurvedRendering(bool enabled)
        {
            curvedScreen = enabled;
            if (!enabled) ReleaseCurvedDisplay();
            refreshTarget = true;
        }

        public void Configure(Canvas canvas) { screenCanvas = canvas; }
        public bool RefreshScreen()
        {
            ReleaseCurvedDisplay();
            refreshTarget = true;
            return FitToScreen();
        }
        public bool AttachTo(Transform target)
        {
            screenTarget = target;
            refreshTarget = true;
            return FitToScreen();
        }

        private void Awake()
        {
            if (!Application.IsPlaying(gameObject)) return;
            EnsureEventSystem();
            AssignEventCamera();
            FitToScreen();
        }
        private void OnValidate() { refreshTarget = true; }
        private void OnDisable() { ReleaseCurvedDisplay(); }
        private void OnDestroy() { ReleaseCurvedDisplay(); }
        private void ReleaseCurvedDisplay() { curvedDisplay?.Dispose(); curvedDisplay = null; }
        private void LateUpdate()
        {
            if (Application.IsPlaying(gameObject)) AssignEventCamera();
            if (screenTarget != null && (curvedScreen || followTarget || refreshTarget)) FitToScreen();
            else if (screenTarget == null) ReleaseCurvedDisplay();
        }

        private void AssignEventCamera()
        {
            if (screenCanvas != null && screenCanvas.worldCamera == null) screenCanvas.worldCamera = Camera.main;
        }

        public static void EnsureEventSystem()
        {
            // Kept outside the prefab so several terminals can share one scene EventSystem.
            var existing = Object.FindAnyObjectByType<EventSystem>();
            if (existing != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        public bool FitToScreen()
        {
            if (!isActiveAndEnabled || screenCanvas == null || screenTarget == null) { ReleaseCurvedDisplay(); return false; }
            if (refreshTarget || cachedTarget != screenTarget)
            {
                cachedTarget = screenTarget;
                cachedFilter = ResolveDisplay(screenTarget);
                refreshTarget = false;
            }
            if (cachedFilter == null || cachedFilter.sharedMesh == null) { ReleaseCurvedDisplay(); return false; }
            var bounds = cachedFilter.sharedMesh.bounds;
            var size = bounds.size;
            var thinAxis = size.x < size.y && size.x < size.z ? 0 : size.y < size.z ? 1 : 2;
            var normal = thinAxis == 0 ? Vector3.right : thinAxis == 1 ? Vector3.up : Vector3.forward;
            var up = thinAxis == 1 ? Vector3.forward : Vector3.up;
            if (flipFacing) normal = -normal;
            var right = Vector3.Cross(up, normal);
            var matrix = cachedFilter.transform.localToWorldMatrix;
            var width = Mathf.Abs(Vector3.Dot(size, right)) * matrix.MultiplyVector(right).magnitude * fill;
            var height = Mathf.Abs(Vector3.Dot(size, up)) * matrix.MultiplyVector(up).magnitude * fill;
            if (width < 0.00001f || height < 0.00001f) return false;
            var worldNormal = matrix.inverse.transpose.MultiplyVector(normal).normalized;
            var worldUp = matrix.MultiplyVector(up).normalized;
            var front = bounds.center + normal * (size[thinAxis] * 0.5f);
            var position = matrix.MultiplyPoint3x4(front) + worldNormal * surfaceOffset;
            // A uGUI Canvas faces local -Z, while these physical screen meshes face local +Z.
            var rotation = Quaternion.LookRotation(-worldNormal, worldUp);
            var rect = (RectTransform)screenCanvas.transform;
            var pixelSize = new Vector2(1000, 1000 * height / width);
            var parentScale = rect.parent != null ? rect.parent.lossyScale : Vector3.one;
            var scale = width / 1000;
            var localScale = new Vector3(scale / Mathf.Max(Mathf.Abs(parentScale.x), 0.00001f),
                scale / Mathf.Max(Mathf.Abs(parentScale.y), 0.00001f), scale / Mathf.Max(Mathf.Abs(parentScale.z), 0.00001f));
            if (screenCanvas.renderMode != RenderMode.WorldSpace) screenCanvas.renderMode = RenderMode.WorldSpace;
            if (rect.sizeDelta != pixelSize) rect.sizeDelta = pixelSize;
            if ((rect.position - position).sqrMagnitude > 1e-12f) rect.position = position;
            if (Quaternion.Angle(rect.rotation, rotation) > 0.001f) rect.rotation = rotation;
            if ((rect.localScale - localScale).sqrMagnitude > 1e-14f) rect.localScale = localScale;
            if (curvedScreen)
            {
                if (curvedDisplay != null && !curvedDisplay.Matches(screenCanvas, cachedFilter, textureWidth)) ReleaseCurvedDisplay();
                if (curvedDisplay == null) curvedDisplay = new TerminalCurvedDisplay(screenCanvas, cachedFilter, textureWidth);
                curvedDisplay.Render(bounds, normal, up, fill, surfaceOffset);
            }
            else ReleaseCurvedDisplay();
            return true;
        }

        private static MeshFilter ResolveDisplay(Transform target)
        {
            var direct = target.GetComponent<MeshFilter>();
            if (direct != null && target.GetComponent<MeshRenderer>() != null) return direct;
            MeshFilter candidate = null;
            foreach (var filter in target.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.GetComponent<MeshRenderer>() == null) continue;
                if (filter.name.StartsWith("SCREEN_", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (candidate != null) return null; // A whole cockpit has several displays: require a specific one.
                    candidate = filter;
                }
            }
            return candidate;
        }
    }
}
