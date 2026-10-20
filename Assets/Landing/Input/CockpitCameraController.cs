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
        public bool PresentationBlocked { get; set; }
        public bool IsForcedFocus { get; private set; }
        public bool IsForcedFocusSettled => IsForcedFocus && !restoringForcedFocus &&
            State == CockpitViewState.Screen && IsValidScreen(forcedScreen) && CurrentScreen == forcedScreen;

        private InputAction point, click, back;
        private InputAction escBack;
        private System.Func<bool> blockMenuWhileFocused;
        private Vector3 freePosition, startPosition;
        private Quaternion freeRotation, startRotation;
        private float freeFov, startFov, elapsed;
        private bool hasFreePose;
        private CockpitScreenTarget forcedScreen, forcedReturnScreen;
        private bool restoringForcedFocus;
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
                // 手动聚焦屏幕时，ESC 算"返回上一级"，不弹菜单。
                // A scripted calendar close-up must still let the player pause the game.
                blockMenuWhileFocused = () => State != CockpitViewState.Free && !IsForcedFocus && !PresentationBlocked;
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
        private bool CanOperate => !PresentationBlocked && ViewCamera != null &&
            (!RequireApplicationFocus || Application.isFocused) && (PauseMenu == null || !PauseMenu.IsOpen);

        private void Update()
        {
            if (!CanOperate || IsForcedFocus) return;
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
                var canvas = graphic.GetComponent<Canvas>().rootCanvas;
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
            if (IsForcedFocus || !isActiveAndEnabled || !CanOperate || !IsValidScreen(screen) || State != CockpitViewState.Free) return;
            freePosition = ViewCamera.transform.localPosition;
            freeRotation = ViewCamera.transform.localRotation;
            freeFov = ViewCamera.fieldOfView; hasFreePose = true;
            CurrentScreen = screen;
            if (FreeLook != null) FreeLook.InputBlocked = true;
            BeginTransition(CockpitViewState.EnteringScreen);
        }

        public void ReturnToFree()
        {
            if (PresentationBlocked || IsForcedFocus || State == CockpitViewState.Free || State == CockpitViewState.Returning || !hasFreePose) return;
            if (FreeLook != null) FreeLook.WaitForRightRelease();
            BeginTransition(CockpitViewState.Returning);
        }

        /// <summary>Temporarily docks the camera without losing its previous screen or free-view return pose.</summary>
        public bool BeginForcedFocus(CockpitScreenTarget target)
        {
            if (IsForcedFocus || !isActiveAndEnabled || !CanOperate || !IsValidScreen(target)) return false;
            forcedReturnScreen = State == CockpitViewState.Screen || State == CockpitViewState.EnteringScreen
                ? CurrentScreen : null;
            if (State == CockpitViewState.Free || !hasFreePose)
            {
                freePosition = ViewCamera.transform.localPosition;
                freeRotation = ViewCamera.transform.localRotation;
                freeFov = ViewCamera.fieldOfView;
                hasFreePose = true;
            }
            forcedScreen = target;
            restoringForcedFocus = false;
            IsForcedFocus = true;
            CurrentScreen = target;
            if (FreeLook != null) FreeLook.InputBlocked = true;
            BeginTransition(CockpitViewState.EnteringScreen);
            return true;
        }

        /// <summary>Restores the previous view, or releases immediately to free view for another cinematic.</summary>
        public void EndForcedFocus(bool restore = true)
        {
            if (!IsForcedFocus) return;
            if (!restore || ViewCamera == null || !hasFreePose)
            {
                ResetToFree();
                return;
            }
            if (restoringForcedFocus) return;
            restoringForcedFocus = true;
            if (FreeLook != null) FreeLook.WaitForRightRelease();
            if (IsValidScreen(forcedReturnScreen))
            {
                CurrentScreen = forcedReturnScreen;
                BeginTransition(CockpitViewState.EnteringScreen);
            }
            else
            {
                CurrentScreen = null;
                BeginTransition(CockpitViewState.Returning);
            }
        }

        private static bool IsValidScreen(CockpitScreenTarget screen) =>
            screen != null && screen.isActiveAndEnabled && screen.ViewPoint != null;

        private void ClearForcedFocus()
        {
            IsForcedFocus = false;
            restoringForcedFocus = false;
            forcedScreen = forcedReturnScreen = null;
        }

        private void ResetToFree()
        {
            if (hasFreePose && ViewCamera != null && State != CockpitViewState.Free)
            {
                ViewCamera.transform.SetLocalPositionAndRotation(freePosition, freeRotation);
                ViewCamera.fieldOfView = freeFov;
            }
            if (FreeLook != null) { FreeLook.InputBlocked = false; FreeLook.WaitForRightRelease(); }
            ClearForcedFocus();
            hasFreePose = false;
            CurrentScreen = null;
            State = CockpitViewState.Free;
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
            if (ViewCamera == null)
            {
                if (State != CockpitViewState.Free || IsForcedFocus) ResetToFree();
                return;
            }
            if (!CanOperate || State == CockpitViewState.Free) return;
            if (State != CockpitViewState.Returning && !IsValidScreen(CurrentScreen))
            {
                if (IsForcedFocus)
                {
                    if (!restoringForcedFocus) EndForcedFocus();
                    else { CurrentScreen = null; BeginTransition(CockpitViewState.Returning); }
                }
                else ReturnToFree();
                if (State == CockpitViewState.Free) return;
            }
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
            if (restoringForcedFocus) ClearForcedFocus();
        }

        private void OnDisable()
        {
            point?.Dispose(); click?.Dispose(); back?.Dispose(); point = click = back = null;
            escBack?.Dispose(); escBack = null;
            if (PauseMenu != null && PauseMenu.OpenBlocker == blockMenuWhileFocused) PauseMenu.OpenBlocker = null;
            blockMenuWhileFocused = null;
            ResetToFree();
        }
    }
}
