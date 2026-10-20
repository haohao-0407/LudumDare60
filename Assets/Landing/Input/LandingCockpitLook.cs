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
        [Tooltip("WASD 转视角的速度（度/秒）。0 = 关闭。只在没放大某块屏时才生效")]
        public float KeyboardLookSpeed = 70f;
        private InputAction look, hold;
        private Quaternion restRotation;
        private float yaw, pitch;
        private bool initialized;
        private bool waitForRightRelease;
        public bool InputBlocked { get; set; }

        /// <summary>当前水平朝向（度，0 = 初始朝前）。</summary>
        public float Yaw { get { return yaw; } }
        /// <summary>当前俯仰（度）。</summary>
        public float Pitch { get { return pitch; } }
        /// <summary>玩家这一帧是否自己转了视角（真实鼠标位移）。教学用它判断要不要让位。</summary>
        public bool PlayerLooked { get; private set; }

        /// <summary>外部驱动视角（教学用）。会立刻应用到旋转上。</summary>
        public void ApplyLook(float y, float p)
        {
            yaw = Mathf.Clamp(y, YawLimits.x, YawLimits.y);
            pitch = Mathf.Clamp(p, PitchLimits.x, PitchLimits.y);
            transform.localRotation = restRotation * Quaternion.Euler(pitch, yaw, 0);
        }
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

            // WASD 环视：不需要按住右键，但只在自由视角（InputBlocked 为 false）时才会走到这里
            if (KeyboardLookSpeed > 0f)
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard != null)
                {
                    float yawInput = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                    float pitchInput = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                    if (Mathf.Abs(yawInput) > 0.001f || Mathf.Abs(pitchInput) > 0.001f)
                    {
                        float step = KeyboardLookSpeed * Time.unscaledDeltaTime;
                        ApplyLook(yaw + yawInput * step, pitch + pitchInput * step);
                        PlayerLooked = true;
                    }
                }
            }
            if (hold.ReadValue<float>() < 0.5f) { waitForRightRelease = false; return; }
            if (waitForRightRelease) return;
            var delta = look.ReadValue<Vector2>();
            // UI/Navigate also uses the gamepad stick in this project. Leave its navigation intact.
            if (look.activeControl == null || !(look.activeControl.device is Pointer) ||
                hold.ReadValue<float>() < 0.5f) return;
            PlayerLooked = delta.sqrMagnitude > 0.000001f;
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
