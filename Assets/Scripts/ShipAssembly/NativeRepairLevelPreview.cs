using LudumDare60.AssemblyDemo;
using LudumDare60.ReturnWindow;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LudumDare60.ShipAssembly
{
    public sealed class NativeRepairLevelPreview : MonoBehaviour
    {
        public GameObject[] levels;
        public int selectedLevel;
        public Camera viewCamera;
        public TMP_Text title, instructions;
        public AssemblyDemoController Current => levels[selectedLevel].GetComponent<AssemblyDemoController>();
        private void OnEnable() { if (levels != null && levels.Length > 0) SelectLevel(selectedLevel); }
        public void SelectEngine() => SelectLevel(0);
        public void SelectLifeSupport() => SelectLevel(1);
        public void SelectLevel(int index)
        {
            if (levels == null || index < 0 || index >= levels.Length) return;
            selectedLevel = index;
            for (int i = 0; i < levels.Length; i++) levels[i].SetActive(i == index);
            Current.ViewCamera = viewCamera; RefreshLabels();
        }
        private void Update()
        {
            var keys = Keyboard.current;
            if (Application.isFocused && keys != null)
            {
                if (keys.f1Key.wasPressedThisFrame) SelectEngine();
                if (keys.f2Key.wasPressedThisFrame) SelectLifeSupport();
            }
            RefreshLabels();
        }
        private void RefreshLabels()
        {
            if (title != null) title.text = Current.GetComponent<DeviceAssemblyLayout>().deviceLabel + " / ASSEMBLY PLAYTEST";
            if (instructions != null) instructions.text = Current.IsAssemblyComplete
                ? "ASSEMBLY COMPLETE / DEVICE LOCKED\nF1: ENGINE    F2: LIFE SUPPORT    Stop and restart Play to reset completed devices."
                : "SELECTED: " + (Current.Selected != null ? Current.Selected.moduleLabel : "BODY") + "    JOINTS " + Current.ConnectionCount + "/" + Current.RequiredConnectionCount
                + "\n1-4 / TAB / CLICK: SELECT    WASD: MOVE    Q/E: ROTATE    SHIFT: FINE\nX: DETACH    R: RESET    F1: ENGINE    F2: LIFE SUPPORT";
        }
    }
}
