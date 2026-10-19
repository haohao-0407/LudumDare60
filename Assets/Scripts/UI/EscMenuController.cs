using System;
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
        public UnityEngine.UI.Button Resume, Settings, Quit, Back, ResetAudio, CancelQuit, ConfirmQuit;
        public UnityEngine.UI.Slider Volume;
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
            widgets.ResetAudio.onClick.AddListener(ResetAudio);
            widgets.CancelQuit.onClick.AddListener(CancelQuit);
            widgets.ConfirmQuit.onClick.AddListener(ConfirmQuit);
            widgets.Volume.onValueChanged.AddListener(SetVolume);
            widgets.Fullscreen.onValueChanged.AddListener(SetFullscreen);
            widgets.Overlay.SetActive(false);
        }

        private void OnEnable()
        {
            if (ToggleInput == null) Debug.LogError("ESC Menu: configure Input Actions / " + toggleActionPath, this);
            UIManager.EnsureInstance().Register(this);
        }

        private void OnDisable()
        {
            Close();
        }

        private void OnDestroy() { if (UIManager.Instance != null) UIManager.Instance.Unregister(this); }
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
            widgets.Volume.SetValueWithoutNotify(AudioListener.volume); UpdateVolumeLabel();
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
            widgets.Title.text = "PAUSED / SHIP SYSTEMS";
            Select(widgets.Resume.gameObject);
        }

        public void ShowSettings()
        {
            SetPageInteraction(true);
            widgets.MainPage.SetActive(false); widgets.SettingsPage.SetActive(true); widgets.QuitConfirmation.SetActive(false);
            widgets.Title.text = "SETTINGS / SHIP SYSTEMS";
            Select(widgets.Back.gameObject);
        }

        public void RequestQuit() { SetPageInteraction(false); widgets.QuitConfirmation.SetActive(true); Select(widgets.CancelQuit.gameObject); }
        public void CancelQuit() { SetPageInteraction(true); widgets.QuitConfirmation.SetActive(false); Select(widgets.Quit.gameObject); }
        private void SetPageInteraction(bool enabled)
        {
            widgets.MainPage.GetComponent<CanvasGroup>().interactable = enabled;
            widgets.SettingsPage.GetComponent<CanvasGroup>().interactable = enabled;
            widgets.Volume.interactable = enabled;
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
        private void ResetAudio() { widgets.Volume.value = 1; SetVolume(1); }
        private void SetVolume(float value) { AudioListener.volume = Mathf.Clamp01(value); UpdateVolumeLabel(); }
        private void UpdateVolumeLabel() => widgets.VolumeValue.text = Mathf.RoundToInt(widgets.Volume.value * 100) + "%";
        private void SetFullscreen(bool value) => Screen.fullScreen = value;
        private static void Select(GameObject target)
        { if (EventSystem.current != null) { EventSystem.current.SetSelectedGameObject(null); EventSystem.current.SetSelectedGameObject(target); } }
        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
