using LudumDare60.AssemblyDemo;
using LudumDare60.ReturnWindow;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using System.Collections.Generic;

namespace LudumDare60.ShipAssembly
{
    [DefaultExecutionOrder(-50)]
    public sealed class NativeRepairLevelPreview : MonoBehaviour
    {
        public GameObject[] levels;
        public int selectedLevel;
        public Camera viewCamera;
        public TMP_Text title, instructions;
        private AssemblyKeyGuide keyGuide;
        private readonly List<RaycastResult> pointerHits = new List<RaycastResult>();
        public AssemblyDemoController Current => levels[selectedLevel].GetComponent<AssemblyDemoController>();
        private void OnEnable()
        {
            if (instructions != null) keyGuide = AssemblyKeyGuide.Ensure(instructions.canvas.transform,instructions.font,true);
            if (instructions != null) instructions.enabled = false;
            StyleHeader();
            if (levels != null && levels.Length > 0) SelectLevel(selectedLevel);
        }

        private void StyleHeader()
        {
            var font = AssemblyKeyGuide.ReadableFont;
            if (font == null) return;
            if (title != null) { title.font = font; title.fontSize = 22; title.characterSpacing = 1; }
            if (instructions == null) return;
            string[] names = { "F1 / ENGINE", "F2 / LIFE SUPPORT" };
            for (int i = 0; i < names.Length; i++)
            {
                RectTransform tab = null;
                // Names contain a slash; Transform.Find would interpret it as a hierarchy path.
                foreach (Transform child in instructions.canvas.transform)
                    if (child.name == names[i]) { tab = child as RectTransform; break; }
                if (tab == null) continue;
                tab.anchorMin = tab.anchorMax = new Vector2(.04f, .85f);
                tab.pivot = Vector2.zero;
                tab.anchoredPosition = new Vector2(i * 200, 0);
                tab.sizeDelta = new Vector2(184, 38);
                var label = tab.GetComponentInChildren<TMP_Text>();
                if (label != null) { label.font = font; label.fontSize = 12; label.characterSpacing = 1; label.text = i == 0 ? "ENGINE" : "LIFE SUPPORT"; }
            }
        }
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
            if (Current != null)
            {
                Current.PointerInputBlocked = false;
                var mouse = Mouse.current;
                if (EventSystem.current != null && mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    pointerHits.Clear();
                    EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = mouse.position.ReadValue() }, pointerHits);
                    Current.PointerInputBlocked = pointerHits.Count > 0;
                }
            }
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
            if (keyGuide != null)
            {
                keyGuide.ShowInput = !Current.InputBlocked;
                keyGuide.SetState(Current.Selected != null ? Current.Selected.moduleLabel : "BODY",
                    Current.Modules.Length, Current.ConnectionCount, Current.RequiredConnectionCount,
                    Current.IsAssemblyComplete, !Current.InputBlocked, selectedLevel);
            }
            if (title != null) title.text = Current.GetComponent<DeviceAssemblyLayout>().deviceLabel + " / ASSEMBLY PLAYTEST";
        }
    }
}
