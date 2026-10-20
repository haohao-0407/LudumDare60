using System.Collections.Generic;
using LudumDare60.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LudumDare60.Landing
{
    public enum CockpitViewState { Free, EnteringScreen, Screen, Returning }

    /// <summary>Pointer selection and camera docking; existing seated look owns free-view rotation.</summary>
    [DefaultExecutionOrder(100), DisallowMultipleComponent]
    public sealed class CockpitCameraController : MonoBehaviour
    {
        public Camera ViewCamera;
        public LandingCockpitLook FreeLook;
        public EscMenuController PauseMenu;
        public InputActionAsset Actions;
        [Tooltip("00 自由视角的编辑参考点；退出屏幕时恢复进入前的实际视角。")]
        public Transform FreeView;
        public CockpitScreenTarget[] Screens = new CockpitScreenTarget[0];
        [Min(0.05f)] public float TransitionDuration = 0.65f;
        [Min(0.1f)] public float RayDistance = 10;
        public LayerMask RayLayers = ~0;
        public bool RequireApplicationFocus = true;

        public CockpitViewState State { get; private set; } = CockpitViewState.Free;
        public CockpitScreenTarget CurrentScreen { get; private set; }
        public bool IsFreeView => State == CockpitViewState.Free;

        private InputAction point, click, back;
        private InputAction escBack;
        private System.Func<bool> blockMenuWhileFocused;
        private Vector3 freePosition, startPosition;
        private Quaternion freeRotation, startRotation;
        private float freeFov, startFov, elapsed;
        private bool hasFreePose;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>(16);
        private PointerEventData pointerData;
        private EventSystem pointerSystem;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            point = Clone("UI/Point"); click = Clone("UI/Click"); back = Clone("UI/RightClick");
            // 聚焦屏幕时 ESC 也当"返回上一级"：自己接一份 ESC 动作，
            // 同时给菜单挂否决权，避免返回的同时把菜单也弹出来。
            escBack = Clone("ESCMenu/Toggle");
            if (PauseMenu != null)
            {
                // 只要不在自由视角（进入中/已聚焦/退回中），ESC 都算"返回上一级"，不弹菜单。
                blockMenuWhileFocused = () => State != CockpitViewState.Free;
                PauseMenu.OpenBlocker = blockMenuWhileFocused;
            }
        }

        private InputAction Clone(string path)
        {
            var source = Actions != null ? Actions.FindAction(path, false) : null;
            if (source == null) { Debug.LogError("Cockpit camera: missing Input System action " + path, this); return null; }
            var result = source.Clone(); result.Enable(); return result;
        }

        // Reading a terminal can pause mission simulation; camera interaction still uses unscaled time.
        private bool CanOperate => ViewCamera != null &&
            (!RequireApplicationFocus || Application.isFocused) && (PauseMenu == null || !PauseMenu.IsOpen);

        private void Update()
        {
            if (!CanOperate) return;
            if (State != CockpitViewState.Free)
            {
                // 右键（老操作）或 ESC 都退回自由视角；ESC 在这里被吃掉，不会开到菜单。
                if ((back != null && back.WasPressedThisFrame()) ||
                    (escBack != null && escBack.WasPressedThisFrame())) ReturnToFree();
                return;
            }
            if (click == null || !click.WasPressedThisFrame() || point == null || (back != null && back.IsPressed())) return;
            var position = point.ReadValue<Vector2>();
            if (!PointerBlockedByOverlay(position) && TryPickScreen(position, out var target)) FocusScreen(target);
        }

        public bool TryPickScreen(Vector2 screenPosition, out CockpitScreenTarget target)
        {
            target = null;
            if (ViewCamera == null || !ViewCamera.pixelRect.Contains(screenPosition)) return false;
            var ray = ViewCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var hit, RayDistance, RayLayers, QueryTriggerInteraction.Collide)) return false;
            // Exact registered display colliders keep the frame and unrelated models from triggering a view.
            foreach (var screen in Screens)
                if (screen != null && screen.isActiveAndEnabled && screen.HitCollider == hit.collider && screen.ViewPoint != null)
                { target = screen; return true; }
            return false;
        }

        private bool PointerBlockedByOverlay(Vector2 position)
        {
            var system = EventSystem.current;
            if (system == null) return false;
            if (pointerSystem != system)
            { pointerSystem = system; pointerData = new PointerEventData(system); }
            pointerData.Reset(); pointerData.position = position;
            uiHits.Clear(); system.RaycastAll(pointerData, uiHits);
            foreach (var hit in uiHits)
            {
                if (!(hit.module is GraphicRaycaster graphic)) continue;
                var graphicCanvas = graphic.GetComponent<Canvas>();
                if (graphicCanvas == null) continue;   // 画布已被销毁时别再往下走
                var canvas = graphicCanvas.rootCanvas;
                if (canvas.renderMode == RenderMode.WorldSpace) continue;
                // A texture-capture canvas uses texture pixel coordinates, not the game window's
                // pointer space. Its background/cards can otherwise falsely cover the left screens.
                var uiCamera = canvas.worldCamera;
                if (canvas.renderMode == RenderMode.ScreenSpaceCamera && uiCamera != null &&
                    uiCamera != ViewCamera && uiCamera.targetTexture != null) continue;
                return true;
            }
            return false;
        }

        public void FocusScreen(CockpitScreenTarget screen)
        {
            if (!isActiveAndEnabled || !CanOperate || screen == null || screen.ViewPoint == null || State != CockpitViewState.Free) return;
            freePosition = ViewCamera.transform.localPosition;
            freeRotation = ViewCamera.transform.localRotation;
            freeFov = ViewCamera.fieldOfView; hasFreePose = true;
            CurrentScreen = screen;
            if (FreeLook != null) FreeLook.InputBlocked = true;
            BeginTransition(CockpitViewState.EnteringScreen);
        }

        public void ReturnToFree()
        {
            if (State == CockpitViewState.Free || State == CockpitViewState.Returning || !hasFreePose) return;
            if (FreeLook != null) FreeLook.WaitForRightRelease();
            BeginTransition(CockpitViewState.Returning);
        }

        private void BeginTransition(CockpitViewState state)
        {
            startPosition = ViewCamera.transform.localPosition;
            startRotation = ViewCamera.transform.localRotation;
            startFov = ViewCamera.fieldOfView;
            elapsed = 0; State = state;
        }

        private void LateUpdate()
        {
            if (!CanOperate || State == CockpitViewState.Free) return;
            if (State != CockpitViewState.Returning && (CurrentScreen == null || !CurrentScreen.isActiveAndEnabled || CurrentScreen.ViewPoint == null))
                ReturnToFree();
            var cameraTransform = ViewCamera.transform;
            Vector3 targetPosition;
            Quaternion targetRotation;
            float targetFov;
            if (State == CockpitViewState.Returning)
            { targetPosition = freePosition; targetRotation = freeRotation; targetFov = freeFov; }
            else
            {
                var view = CurrentScreen.ViewPoint;
                var parent = cameraTransform.parent;
                targetPosition = parent != null ? parent.InverseTransformPoint(view.position) : view.position;
                targetRotation = parent != null ? Quaternion.Inverse(parent.rotation) * view.rotation : view.rotation;
                targetFov = CurrentScreen.FieldOfView;
            }
            elapsed += Time.unscaledDeltaTime;
            var amount = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / Mathf.Max(0.05f, TransitionDuration)));
            cameraTransform.localPosition = Vector3.Lerp(startPosition, targetPosition, amount);
            cameraTransform.localRotation = Quaternion.Slerp(startRotation, targetRotation, amount);
            ViewCamera.fieldOfView = Mathf.Lerp(startFov, targetFov, amount);
            if (amount < 1) return;
            if (State == CockpitViewState.Returning)
            {
                State = CockpitViewState.Free; CurrentScreen = null;
                if (FreeLook != null) FreeLook.InputBlocked = false;
            }
            else State = CockpitViewState.Screen;
        }

        private void OnDisable()
        {
            point?.Dispose(); click?.Dispose(); back?.Dispose(); point = click = back = null;
            escBack?.Dispose(); escBack = null;
            if (PauseMenu != null && PauseMenu.OpenBlocker == blockMenuWhileFocused) PauseMenu.OpenBlocker = null;
            blockMenuWhileFocused = null;            if (hasFreePose && ViewCamera != null && State != CockpitViewState.Free)
            {
                ViewCamera.transform.SetLocalPositionAndRotation(freePosition, freeRotation);
                ViewCamera.fieldOfView = freeFov;
            }
            if (FreeLook != null) { FreeLook.InputBlocked = false; FreeLook.WaitForRightRelease(); }
            hasFreePose = false; CurrentScreen = null; State = CockpitViewState.Free;
        }
    }
}
