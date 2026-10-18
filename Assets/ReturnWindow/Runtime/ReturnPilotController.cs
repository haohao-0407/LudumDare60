using LudumDare60.TerminalDialogue;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LudumDare60.ReturnWindow
{
    public sealed class ReturnPilotController : MonoBehaviour
    {
        public MissionController mission;
        public TerminalDialogueController terminal;
        public ReturnRepairStation repairStation;
        public ReturnSalvageStation salvageStation;
        public Transform terminalLookTarget;
        public GameObject pausePanel;
        public float sensitivity = .09f;
        private Quaternion seatRotation;
        private float yaw, pitch;
        public bool MenuOpen { get; private set; }
        public bool Ending { get; set; }

        private void Awake() => seatRotation = transform.rotation;
        private void Start() => mission = MissionController.Instance;

        private void Update()
        {
            var keys = Keyboard.current;
            bool repairing = repairStation != null && repairStation.IsOpen;
            bool salvaging = salvageStation != null && salvageStation.IsOpen;
            if (!Ending && keys != null && keys.escapeKey.wasPressedThisFrame)
            {
                if (MenuOpen) SetMenu(false);
                else if (terminal.Session != null && terminal.Session.IsOpen) terminal.Close();
                else if (repairing) repairStation.CloseRepair();
                else if (salvaging) salvageStation.CloseSalvage();
                else SetMenu(true);
            }
            if (!Ending && !MenuOpen && !repairing && !salvaging && keys != null && keys.eKey.wasPressedThisFrame)
            {
                if (terminal.Session != null && terminal.Session.IsOpen) terminal.Close();
                else terminal.Open();
            }
            bool terminalOpen = terminal.Session != null && terminal.Session.IsOpen;
            bool ui = Ending || MenuOpen || repairing || salvaging || terminalOpen;
            Cursor.lockState = ui || !Application.isFocused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = ui || !Application.isFocused;
            if (!Ending && !MenuOpen && terminalOpen && terminalLookTarget != null)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(terminalLookTarget.position - transform.position, Vector3.up),
                    8f * Time.unscaledDeltaTime);
            if (ui || !Application.isFocused || Mouse.current == null) return;
            Vector2 delta = Mouse.current.delta.ReadValue() * sensitivity;
            yaw = Mathf.Clamp(yaw + delta.x, -105, 105);
            pitch = Mathf.Clamp(pitch - delta.y, -48, 50);
            transform.rotation = seatRotation * Quaternion.Euler(pitch, yaw, 0);
        }

        public void SetMenu(bool open)
        {
            MenuOpen = open;
            if (pausePanel != null) pausePanel.SetActive(open);
            mission.SetMenuPaused(open);
        }

        public void ResetLook()
        {
            yaw = pitch = 0;
            transform.rotation = seatRotation;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
