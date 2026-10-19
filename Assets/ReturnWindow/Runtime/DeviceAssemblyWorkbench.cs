using LudumDare60.AssemblyDemo;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LudumDare60.ReturnWindow
{
    /// <summary>Switches complete device puzzles without resetting their physical connection graphs.</summary>
    public sealed class DeviceAssemblyWorkbench : MonoBehaviour
    {
        public AssemblyDemoController[] devices;
        public Camera viewCamera;
        public TMP_Text title;
        public TMP_Text instructions;
        public int selectedDevice;
        public AssemblyDemoController Current => devices[selectedDevice];
        public bool IsComplete
        {
            get
            {
                if (devices == null || devices.Length == 0) return false;
                foreach (var device in devices) if (device == null || !device.IsAssemblyComplete) return false;
                return true;
            }
        }

        private void OnEnable() { if (devices != null && devices.Length > 0) SelectDevice(selectedDevice); }
        private void Update()
        {
            var keys = Keyboard.current;
            if (keys != null && Application.isFocused && (MissionController.Instance == null || !MissionController.Instance.IsGameplayPaused))
            {
                if (keys.f1Key.wasPressedThisFrame) SelectDevice(0);
                if (keys.f2Key.wasPressedThisFrame) SelectDevice(1);
                if (keys.f3Key.wasPressedThisFrame) SelectDevice(2);
            }
            RefreshLabels();
        }
        public void SelectNavigation() => SelectDevice(0);
        public void SelectLifeSupport() => SelectDevice(1);
        public void SelectEnergy() => SelectDevice(2);
        public void SelectDevice(int index)
        {
            if (devices == null || index < 0 || index >= devices.Length) return;
            if (Application.isPlaying && MissionController.Instance != null && MissionController.Instance.IsGameplayPaused) return;
            selectedDevice = index;
            for (int i = 0; i < devices.Length; i++) devices[i].gameObject.SetActive(i == index);
            var layout = Current.GetComponent<DeviceAssemblyLayout>();
            if (viewCamera != null)
            {
                Current.ViewCamera = viewCamera;
                viewCamera.transform.SetPositionAndRotation(Current.transform.TransformPoint(new Vector3(layout.viewCenter.x, layout.viewCenter.y, -10)), Quaternion.identity);
                viewCamera.orthographic = true;
                viewCamera.orthographicSize = 5.4f;
            }
            RefreshLabels();
        }
        private void RefreshLabels()
        {
            if (devices == null || devices.Length == 0) return;
            var layout = Current.GetComponent<DeviceAssemblyLayout>();
            int complete = 0;
            foreach (var device in devices) if (device.IsAssemblyComplete) complete++;
            if (title != null) title.text = layout.deviceLabel + " / REPAIR WORKBENCH    DEVICES " + complete + "/" + devices.Length;
            if (instructions != null)
                instructions.text = Current.IsAssemblyComplete
                    ? "ASSEMBLY COMPLETE / " + layout.deviceLabel + "\nF1: NAVIGATION    F2: LIFE SUPPORT    F3: ENERGY\nCompleted devices stay intact. Restart the run to try again."
                    : "SELECTED: " + (Current.Selected != null ? Current.Selected.moduleLabel : "BASE") + "    JOINTS " + Current.ConnectionCount + "/" + Current.RequiredConnectionCount
                    + "    1-" + Current.Modules.Length + " / TAB / CLICK: SELECT\nWASD: MOVE    Q/E: ROTATE    SHIFT: FINE    X: DISCONNECT    R: RESET\nF1: NAVIGATION    F2: LIFE SUPPORT    F3: ENERGY";
        }
    }
}
