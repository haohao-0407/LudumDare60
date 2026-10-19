using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace LudumDare60.UI
{
    /// <summary>Optional registration for ordinary UI roots, including initially inactive ones.</summary>
    [DisallowMultipleComponent]
    public sealed class ManagedUIPanel : MonoBehaviour
    {
        [SerializeField, Tooltip("全局唯一的界面 ID，用于 UIManager.Instance.Open / Close / Toggle。")]
        private string panelId = "panel";
        [SerializeField, Tooltip("可选：选择 Input System 中的唤出动作。输入由 manager 监听，隐藏本节点也能唤出。")]
        private InputActionReference toggleInput;
        [SerializeField] private UnityEvent opened = new UnityEvent();
        [SerializeField] private UnityEvent closed = new UnityEvent();

        public string PanelId => panelId;
        public bool IsOpen => gameObject.activeInHierarchy;
        internal InputAction ToggleInput => toggleInput != null ? toggleInput.action : null;

        private void OnEnable() => UIManager.EnsureInstance().Register(this);
        private void OnDestroy() { if (UIManager.Instance != null) UIManager.Instance.Unregister(this); }

        public void Open()
        {
            if (!enabled || IsOpen) return;
            gameObject.SetActive(true);
            if (!gameObject.activeInHierarchy)
            { Debug.LogWarning("UI Manager: enable this view's parent before opening it.", this); return; }
            opened.Invoke();
        }

        public void Close()
        {
            if (!gameObject.activeSelf) return;
            gameObject.SetActive(false);
            closed.Invoke();
        }
    }
}
