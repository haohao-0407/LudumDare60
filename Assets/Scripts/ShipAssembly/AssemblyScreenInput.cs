using LudumDare60.AssemblyDemo;
using LudumDare60.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LudumDare60.ShipAssembly
{
    /// <summary>Converts clicks on the cockpit display into rays in the isolated assembly workspace.</summary>
    [DisallowMultipleComponent]
    public sealed class AssemblyScreenInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public ShipAssemblyController Controller;
        public RawImage Surface;
        public InputActionAsset Actions;
        public string ActionMapName = "Assembly";
        public EscMenuController PauseMenu;

        private InputActionAsset ownedActions;
        private InputActionMap actionMap;
        private InputAction point, click, move, rotate, fine, next, detach, reset, releaseFocus, lookHold;
        private readonly InputAction[] select = new InputAction[5];
        private bool dragging;
        private Vector2 dragOffset;
        private AssemblyModule draggedModule;
        private Camera eventCamera;

        private bool CanUseSurface => Controller != null && Surface != null && Surface.isActiveAndEnabled
            && Surface.canvas != null && Surface.canvas.isActiveAndEnabled && Application.isFocused
            && Time.timeScale > 0f && (PauseMenu == null || !PauseMenu.IsOpen);

        private void OnEnable()
        {
            if (Surface == null) Surface = GetComponent<RawImage>();
            if (Actions == null) return;
            // A private asset owns these bindings; disabling this screen never disables ship/UI actions.
            ownedActions = Instantiate(Actions);
            ownedActions.hideFlags = HideFlags.HideAndDontSave;
            actionMap = ownedActions.FindActionMap(ActionMapName, false);
            if (actionMap == null)
            {
                Debug.LogError("Assembly screen: missing Input Actions map '" + ActionMapName + "'.", this);
                return;
            }
            point = Find("Point"); click = Find("Click"); move = Find("Move"); rotate = Find("Rotate");
            fine = Find("Fine"); next = Find("Next"); detach = Find("Detach"); reset = Find("Reset");
            releaseFocus = Find("ReleaseFocus"); lookHold = Find("LookHold");
            for (int i = 0; i < select.Length; i++) select[i] = Find("Select" + (i + 1));
            actionMap.Enable();
        }

        private InputAction Find(string name) => actionMap.FindAction(name, false);

        private void OnDisable()
        {
            ReleaseControl();
            actionMap?.Disable();
            if (ownedActions != null) Destroy(ownedActions);
            ownedActions = null; actionMap = null;
            point = click = move = rotate = fine = next = detach = reset = releaseFocus = lookHold = null;
            for (int i = 0; i < select.Length; i++) select[i] = null;
        }

        private void OnApplicationFocus(bool focused) { if (!focused) ReleaseControl(); }
        private void OnApplicationPause(bool paused) { if (paused) ReleaseControl(); }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !CanUseSurface
                || Controller.IsSelectedCompleted || Controller.ActivePuzzle == null
                || (lookHold != null && lookHold.IsPressed())) return;

            eventCamera = eventData.pressEventCamera != null ? eventData.pressEventCamera : CanvasCamera();
            if (!TryGetRay(eventData.position, out Ray ray)) return;
            Controller.SetFocused(true);
            if (!Controller.InputEnabled) return;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            var puzzle = Controller.ActivePuzzle;
            dragging = false;
            if (!puzzle.TrySelect(ray) || puzzle.Selected == null) return;
            draggedModule = puzzle.Selected;
            var plane = new Plane(Vector3.forward, draggedModule.transform.position);
            if (!plane.Raycast(ray, out float distance)) return;
            // Preserve the grabbed point: clicking a corner must not jump the module to the cursor.
            dragOffset = draggedModule.Position - (Vector2)ray.GetPoint(distance);
            dragging = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) StopDragging();
        }

        public void OnPointerExit(PointerEventData eventData) => StopDragging();

        private void Update()
        {
            if (!CanUseSurface || (lookHold != null && lookHold.IsPressed()))
            {
                ReleaseControl();
                return;
            }
            if (Pressed(releaseFocus)) { ReleaseControl(); return; }
            if (Pressed(click) && point != null && !TryGetRay(point.ReadValue<Vector2>(), out _))
            {
                ReleaseControl();
                return;
            }
            if (!Controller.InputEnabled || Controller.ActivePuzzle == null)
            {
                StopDragging();
                return;
            }

            var puzzle = Controller.ActivePuzzle;
            if (Pressed(reset)) { StopDragging(); Controller.ResetSelectedPuzzle(); return; }
            if (Pressed(detach)) { StopDragging(); Controller.DetachSelected(); }

            bool changedSelection = false;
            for (int i = 0; i < select.Length; i++)
                if (Pressed(select[i])) { puzzle.SelectModule(i); changedSelection = true; break; }
            if (!changedSelection && Pressed(next)) { puzzle.SelectNext(); changedSelection = true; }
            if (changedSelection) StopDragging();

            Vector2 direction = move != null ? Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f) : Vector2.zero;
            float turn = rotate != null ? rotate.ReadValue<float>() : 0f;
            float precision = fine != null && fine.IsPressed() ? 0.25f : 1f;
            bool keyboardMotion = direction.sqrMagnitude > 0f || Mathf.Abs(turn) > 0.001f;
            if (keyboardMotion) StopDragging();
            if (click == null || !click.IsPressed() || puzzle.Selected != draggedModule) StopDragging();

            Vector2 translation = direction * puzzle.MoveSpeed * precision * Time.deltaTime;
            if (dragging && point != null)
            {
                if (TryGetRay(point.ReadValue<Vector2>(), out Ray ray))
                {
                    var plane = new Plane(Vector3.forward, draggedModule.transform.position);
                    if (plane.Raycast(ray, out float distance))
                        translation = (Vector2)ray.GetPoint(distance) + dragOffset - draggedModule.Position;
                }
                else StopDragging(); // Do not continue dragging through another cockpit screen.
            }
            puzzle.ApplyMotion(ClampTranslation(puzzle, translation), turn * puzzle.RotationSpeed * precision * Time.deltaTime);
            puzzle.TickSnap();
        }

        private Vector2 ClampTranslation(AssemblyDemoController puzzle, Vector2 translation)
        {
            var camera = Controller.AssemblyCamera;
            if (camera == null || puzzle.Selected == null || translation.sqrMagnitude == 0f) return translation;
            Vector3 target = puzzle.Selected.transform.position + (Vector3)translation;
            Vector3 viewport = camera.WorldToViewportPoint(target);
            if (viewport.z <= 0f) return Vector2.zero;
            viewport.x = Mathf.Clamp(viewport.x, 0.03f, 0.97f);
            viewport.y = Mathf.Clamp(viewport.y, 0.03f, 0.97f);
            var ray = camera.ViewportPointToRay(viewport);
            var plane = new Plane(Vector3.forward, puzzle.Selected.transform.position);
            return plane.Raycast(ray, out float distance)
                ? (Vector2)ray.GetPoint(distance) - puzzle.Selected.Position : Vector2.zero;
        }

        private bool TryGetRay(Vector2 screenPosition, out Ray ray)
        {
            ray = default;
            if (Surface == null || Controller == null || Controller.AssemblyCamera == null) return false;
            var rect = Surface.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPosition,
                eventCamera != null ? eventCamera : CanvasCamera(), out Vector2 local)) return false;
            Rect bounds = rect.rect;
            if (bounds.width <= 0f || bounds.height <= 0f || !bounds.Contains(local)) return false;
            Rect uv = Surface.uvRect;
            float u = uv.x + (local.x - bounds.xMin) / bounds.width * uv.width;
            float v = uv.y + (local.y - bounds.yMin) / bounds.height * uv.height;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
            ray = Controller.AssemblyCamera.ViewportPointToRay(new Vector3(u, v, 0f));
            return true;
        }

        private Camera CanvasCamera()
        {
            var canvas = Surface != null ? Surface.canvas : null;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        }

        private static bool Pressed(InputAction action) => action != null && action.WasPressedThisFrame();
        private void StopDragging() { dragging = false; draggedModule = null; }
        private void ReleaseControl()
        {
            StopDragging();
            if (Controller != null) Controller.SetFocused(false);
        }
    }
}
