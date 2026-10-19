using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LudumDare60.UI
{
    /// <summary>Owns UI input independently of the active state of each view.</summary>
    [DefaultExecutionOrder(-1000), DisallowMultipleComponent]
    public sealed class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }
        public const string EscMenuId = "esc";

        private sealed class Binding : IDisposable
        {
            public Component Owner;
            public string Id;
            public Action Open, Close;
            public Func<bool> IsOpen;
            public InputAction Input;
            public void Dispose() { Input?.Dispose(); Input = null; }
        }

        private readonly List<Binding> bindings = new List<Binding>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => EnsureInstance().Refresh();

        public static UIManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindAnyObjectByType<UIManager>();
            if (existing != null) return existing;
            return new GameObject("manager").AddComponent<UIManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            // Only the manager persists. Scene-owned views are never moved out of their scene.
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Refresh();
        }

        private void OnDisable()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            foreach (var binding in bindings) binding.Dispose();
            bindings.Clear();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Refresh();
        private void OnSceneUnloaded(Scene scene) => RemoveDestroyedViews();

        /// <summary>Also discovers views that have never been active, so Awake/OnEnable were not called.</summary>
        public void Refresh()
        {
            RemoveDestroyedViews();
            foreach (var menu in FindObjectsByType<EscMenuController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Register(menu);
            foreach (var panel in FindObjectsByType<ManagedUIPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Register(panel);
        }

        public void Register(EscMenuController menu)
        {
            if (menu == null || !menu.gameObject.scene.isLoaded) return;
            Register(menu, EscMenuId, menu.Open, menu.Close, () => menu.IsOpen, menu.ToggleInput);
        }

        public void Register(ManagedUIPanel panel)
        {
            if (panel == null || !panel.gameObject.scene.isLoaded) return;
            Register(panel, panel.PanelId, panel.Open, panel.Close, () => panel.IsOpen, panel.ToggleInput);
        }

        private void Register(Component owner, string id, Action open, Action close, Func<bool> isOpen, InputAction source)
        {
            if (Instance != this || !isActiveAndEnabled || bindings.Exists(item => item.Owner == owner)) return;
            if (string.IsNullOrWhiteSpace(id)) { Debug.LogError("UI Manager: panel ID is empty.", owner); return; }
            if (bindings.Exists(item => item.Owner != null && item.Id == id))
            { Debug.LogWarning("UI Manager: duplicate panel ID '" + id + "'; keeping the registered view.", owner); return; }
            var binding = new Binding { Owner = owner, Id = id, Open = open, Close = close, IsOpen = isOpen };
            if (source != null)
            {
                // Clone only this action. Never change shared PlayerInput/action-map state.
                binding.Input = source.Clone();
                binding.Input.performed += _ => { if (binding.Owner != null) Toggle(binding.Id); };
                binding.Input.Enable();
            }
            bindings.Add(binding);
        }

        public void Unregister(Component owner)
        {
            for (var i = bindings.Count - 1; i >= 0; --i)
                if (bindings[i].Owner == owner) { bindings[i].Dispose(); bindings.RemoveAt(i); }
        }

        private void RemoveDestroyedViews()
        {
            for (var i = bindings.Count - 1; i >= 0; --i)
                if (bindings[i].Owner == null) { bindings[i].Dispose(); bindings.RemoveAt(i); }
        }

        public bool IsOpen(string id) => Find(id)?.IsOpen() ?? false;
        public void Open(string id) => Find(id)?.Open();
        public void Close(string id) => Find(id)?.Close();
        public void Toggle(string id)
        {
            var binding = Find(id);
            if (binding == null) return;
            if (binding.IsOpen()) binding.Close(); else binding.Open();
        }

        private Binding Find(string id)
        {
            RemoveDestroyedViews();
            return bindings.Find(item => item.Id == id);
        }
    }
}
