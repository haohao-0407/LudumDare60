using UnityEngine;
using UnityEngine.InputSystem;
using LudumDare60.UI;

namespace LudumDare60.Landing
{
    /// <summary>Seated look, separate from the animation/shake pivot and terminal pointer.</summary>
    [DisallowMultipleComponent]
    public sealed class LandingCockpitLook : MonoBehaviour
    {
        public InputActionAsset Actions;
        public EscMenuController PauseMenu;
        [Min(0)] public float MouseSensitivity = 0.12f;
        public Vector2 YawLimits = new Vector2(-150, 150);
        public Vector2 PitchLimits = new Vector2(-65, 65);
        private InputAction look, hold;
        private Quaternion restRotation;
        private float yaw, pitch;
        private bool initialized;
        private bool waitForRightRelease;
        public bool InputBlocked { get; set; }
        public void WaitForRightRelease() => waitForRightRelease = true;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            initialized = true;
            restRotation = transform.localRotation;
            yaw = pitch = 0;
            var sourceLook = Actions != null ? Actions.FindAction("Player/Look", false) : null;
            var sourceHold = Actions != null ? Actions.FindAction("UI/RightClick", false) : null;
            if (sourceLook == null || sourceHold == null) return;
            // Clone only these actions; never toggle the shared UI/Player maps.
            look = sourceLook.Clone(); hold = sourceHold.Clone();
            look.Enable(); hold.Enable();
        }

        private void Update()
        {
            if (look == null || !Application.isFocused ||
                InputBlocked || (PauseMenu != null && PauseMenu.IsOpen)) return;
            if (hold.ReadValue<float>() < 0.5f) { waitForRightRelease = false; return; }
            if (waitForRightRelease) return;
            var delta = look.ReadValue<Vector2>();
            // UI/Navigate also uses the gamepad stick in this project. Leave its navigation intact.
            if (look.activeControl == null || !(look.activeControl.device is Pointer) ||
                hold.ReadValue<float>() < 0.5f) return;
            delta *= MouseSensitivity;
            yaw = Mathf.Clamp(yaw + delta.x, YawLimits.x, YawLimits.y);
            pitch = Mathf.Clamp(pitch - delta.y, PitchLimits.x, PitchLimits.y);
            transform.localRotation = restRotation * Quaternion.Euler(pitch, yaw, 0);
        }

        private void OnDisable()
        {
            if (!initialized) return;
            look?.Dispose(); hold?.Dispose(); look = hold = null;
            transform.localRotation = restRotation;
            initialized = false;
        }
    }
}
