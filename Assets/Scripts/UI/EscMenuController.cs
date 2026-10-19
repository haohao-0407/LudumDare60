using System;
using System.Collections.Generic;
using Pioneer60.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace LudumDare60.UI
{
    [Serializable]
    public sealed class EscMenuWidgets
    {
        public GameObject Overlay, MainPage, SettingsPage, QuitConfirmation;
        public TMP_Text Title, VolumeValue;
        public UnityEngine.UI.Button Resume, Settings, Quit, Back, CancelQuit, ConfirmQuit;
        public UnityEngine.UI.Slider Volume;
        public EscLanguageOption Language;
        public UnityEngine.UI.Toggle Fullscreen;
    }

    /// <summary>Menu view; UIManager owns the input even when this root is inactive.</summary>
    [DisallowMultipleComponent]
    public sealed class EscMenuController : MonoBehaviour
    {
        [SerializeField, Tooltip("在此 Input Actions 资产中配置 ESC 按键。")] private InputActionAsset inputActions;
        [SerializeField] private string toggleActionPath = "ESCMenu/Toggle";
        [SerializeField] private EscMenuWidgets widgets = new EscMenuWidgets();
        [SerializeField, Tooltip("打开菜单时暂停使用 Time.timeScale 的游戏逻辑，关闭时恢复之前的值。")]
        private bool pauseGame = true;
        [SerializeField] private UnityEvent<bool> openChanged = new UnityEvent<bool>();
        private float previousTimeScale;
        private bool previousCursorVisible;
        private CursorLockMode previousCursorLock;
        private GameObject previousSelection;

        // label → dictionary key. Filled once in Awake so language switches are just re-assignment.
        private readonly List<TMP_Text> _localizedLabels = new List<TMP_Text>();
        private readonly List<string> _localizedKeys = new List<string>();

        public bool IsOpen { get; private set; }
        internal InputAction ToggleInput => inputActions != null ? inputActions.FindAction(toggleActionPath, false) : null;

        public void Configure(InputActionAsset actions, EscMenuWidgets menuWidgets)
        { inputActions = actions; widgets = menuWidgets; }

        private void Awake()
        {
            if (widgets.Overlay == null) { Debug.LogError("ESC Menu: missing overlay reference.", this); enabled = false; return; }
            EnsureEventSystem();
            widgets.Resume.onClick.AddListener(Close);
            widgets.Settings.onClick.AddListener(ShowSettings);
            widgets.Quit.onClick.AddListener(RequestQuit);
            widgets.Back.onClick.AddListener(ShowMain);
            if (widgets.Volume != null) widgets.Volume.onValueChanged.AddListener(SetVolume);
            widgets.CancelQuit.onClick.AddListener(CancelQuit);
            widgets.ConfirmQuit.onClick.AddListener(ConfirmQuit);
            widgets.Fullscreen.onValueChanged.AddListener(SetFullscreen);
            widgets.Overlay.SetActive(false);
        }

        private void Start()
        {
            // Start 而不是 Awake：确保词典实例与整棵 UI 层级都已就绪。
            // The terminal font is ASCII-only; attach a CJK fallback before any Chinese text is assigned.
            LocFontFallback.EnsureFor(widgets.Title);
            ApplyLanguage();
        }

        private void OnDestroy()
        {
            Loc.LanguageChanged -= OnLanguageChanged;
            if (UIManager.Instance != null) UIManager.Instance.Unregister(this);
        }

        private void OnEnable()
        {
            Loc.LanguageChanged += OnLanguageChanged;
            if (ToggleInput == null) Debug.LogError("ESC Menu: configure Input Actions / " + toggleActionPath, this);
            UIManager.EnsureInstance().Register(this);
        }

        private void OnDisable()
        {
            Loc.LanguageChanged -= OnLanguageChanged;
            Close();
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }
        public void Open()
        {
            if (IsOpen || !enabled || widgets.Overlay == null) return;
            // Activating a never-enabled root first runs Awake and binds its widgets.
            gameObject.SetActive(true);
            if (!isActiveAndEnabled) return;
            previousTimeScale = Time.timeScale;
            previousCursorVisible = Cursor.visible; previousCursorLock = Cursor.lockState;
            previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            IsOpen = true;
            if (pauseGame) Time.timeScale = 0;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            widgets.Overlay.SetActive(true);
            widgets.Fullscreen.SetIsOnWithoutNotify(Screen.fullScreen);
            ShowMain(); openChanged.Invoke(true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            widgets.Overlay.SetActive(false);
            if (pauseGame) Time.timeScale = previousTimeScale;
            Cursor.lockState = previousCursorLock; Cursor.visible = previousCursorVisible;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy ? previousSelection : null);
            openChanged.Invoke(false);
        }

        public void ShowMain()
        {
            SetPageInteraction(true);
            widgets.MainPage.SetActive(true); widgets.SettingsPage.SetActive(false); widgets.QuitConfirmation.SetActive(false);
            widgets.Title.text = Loc.Get("esc.title.paused");
            Select(widgets.Resume.gameObject);
        }

        public void ShowSettings()
        {
            SetPageInteraction(true);
            widgets.MainPage.SetActive(false); widgets.SettingsPage.SetActive(true); widgets.QuitConfirmation.SetActive(false);
            widgets.Title.text = Loc.Get("esc.title.settings");
            if (widgets.Volume != null) { widgets.Volume.SetValueWithoutNotify(AudioListener.volume); UpdateVolumeLabel(); }
            Select(widgets.Back.gameObject);
        }

        public void RequestQuit() { SetPageInteraction(false); widgets.QuitConfirmation.SetActive(true); Select(widgets.CancelQuit.gameObject); }
        public void CancelQuit() { SetPageInteraction(true); widgets.QuitConfirmation.SetActive(false); Select(widgets.Quit.gameObject); }
        private void SetPageInteraction(bool enabled)
        {
            widgets.MainPage.GetComponent<CanvasGroup>().interactable = enabled;
            widgets.SettingsPage.GetComponent<CanvasGroup>().interactable = enabled;
            if (widgets.Language != null) widgets.Language.interactable = enabled;
            if (widgets.Volume != null) widgets.Volume.interactable = enabled;
        }
        public void ConfirmQuit()
        {
            Close();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void SetVolume(float value) { AudioListener.volume = Mathf.Clamp01(value); UpdateVolumeLabel(); }
        private void UpdateVolumeLabel()
        { if (widgets.VolumeValue != null && widgets.Volume != null) widgets.VolumeValue.text = Mathf.RoundToInt(widgets.Volume.value * 100) + "%"; }
        private void SetFullscreen(bool value) => Screen.fullScreen = value;
        private static void Select(GameObject target)
        { if (EventSystem.current != null) { EventSystem.current.SetSelectedGameObject(null); EventSystem.current.SetSelectedGameObject(target); } }
        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        // ── 本地化 ──────────────────────────────────────────────
        private void BindLocalizedText()
        {
            Add("Menu Overlay/Menu Window/Main Page/Resume/Hover Visual/Label", "esc.resume");
            Add("Menu Overlay/Menu Window/Main Page/Settings/Hover Visual/Label", "esc.settings");
            Add("Menu Overlay/Menu Window/Main Page/Quit/Hover Visual/Label", "esc.quit");
            Add("Menu Overlay/Menu Window/Main Page/Controls", "esc.hint.paused");
            Add("Menu Overlay/Menu Window/Settings Page/Back/Hover Visual/Label", "esc.back");
            Add("Menu Overlay/Menu Window/Settings Page/Fullscreen/Hover Visual/Label", "esc.fullscreen");
            Add("Menu Overlay/Menu Window/Settings Page/Settings Hint", "esc.hint.settings");
            Add("Menu Overlay/Menu Window/Settings Page/Language/Hover Visual/Label", "esc.language");
            Add("Menu Overlay/Menu Window/Settings Page/Master Volume/Label", "esc.volume");
            Add("Menu Overlay/Menu Window/Footer", "esc.footer");
            Add("Menu Overlay/Menu Window/Quit Confirmation/Question", "esc.quit.question");
            Add("Menu Overlay/Menu Window/Quit Confirmation/Cancel Quit/Label", "esc.quit.cancel");
            Add("Menu Overlay/Menu Window/Quit Confirmation/Confirm Quit/Label", "esc.quit.confirm");
        }

        private void Add(string path, string key)
        {
            Transform node = transform.Find(path) ?? transform.Find(path.Replace("/Hover Visual/", "/"));
            if (node == null) { Debug.LogWarning("ESC Menu: missing node " + path, this); return; }
            var text = node.GetComponent<TMP_Text>();
            if (text == null) { Debug.LogWarning("ESC Menu: no TMP_Text on " + path, this); return; }
            _localizedLabels.Add(text);
            _localizedKeys.Add(key);
        }

        private void OnLanguageChanged(string code) => ApplyLanguage();

        private void ApplyLanguage()
        {
            if (_localizedLabels.Count == 0) BindLocalizedText(); // 自愈：层级晚于脚本就绪时补绑
            for (int i = 0; i < _localizedLabels.Count; i++)
            {
                if (_localizedLabels[i] == null) continue;
                _localizedLabels[i].text = Loc.Get(_localizedKeys[i]);
            }
            if (widgets.Title != null)
                widgets.Title.text = Loc.Get(widgets.SettingsPage != null && widgets.SettingsPage.activeSelf
                    ? "esc.title.settings"
                    : "esc.title.paused");
        }
    }
}
