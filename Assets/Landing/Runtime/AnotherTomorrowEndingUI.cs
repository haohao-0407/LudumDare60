using System;
using LudumDare60.TerminalDialogue;
using LudumDare60.UI;
using Pioneer60.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LudumDare60.Landing
{
    /// <summary>A theatrical epilogue after the player's last acknowledgement of the timeout dialogue.</summary>
    [DisallowMultipleComponent]
    public sealed class AnotherTomorrowEndingUI : MonoBehaviour
    {
        public CockpitMissionController Mission;
        public TerminalDialogueController Terminal;
        public CockpitCameraController CockpitCamera;
        public EscMenuController PauseMenu;
        public AnotherTomorrowDefinition Script;
        public TMP_FontAsset LatinFont, ChineseFont;
        public CanvasGroup BlackoutGroup, CardGroup, ActionsGroup;
        public TMP_Text CueText, TitleText, BodyText, FootnoteText, HintText, RestartText;
        public Button RestartButton;

        public bool HasStarted { get; private set; }
        public bool IsComplete { get; private set; }
        public int CardIndex { get; private set; } = -1;
        public float Elapsed { get; private set; }
        public bool RequireApplicationFocus = true;

        enum Stage { Blackout, FadeIn, Hold, FadeOut, Interval, Complete }
        Stage stage;
        float stageTime, inputCooldown;
        int startedFrame;
        bool inputCaptured, previousCameraBlock, previousLookBlock, previousCursorVisible, loading;
        bool focused, applicationPaused;
        CursorLockMode previousCursorLock;
        GameObject previousSelection;
        TerminalDialogueController boundTerminal;

        bool Bound => Script && Script.Cards != null && Script.Cards.Length > 0 &&
            LatinFont && ChineseFont && BlackoutGroup && CardGroup && ActionsGroup &&
            CueText && TitleText && BodyText && FootnoteText && HintText && RestartText && RestartButton;
        bool CanAdvance => !applicationPaused && (!RequireApplicationFocus || focused) &&
            (!PauseMenu || !PauseMenu.IsOpen);

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            focused = Application.isFocused;
            Loc.LanguageChanged += OnLanguageChanged;
            boundTerminal = Terminal;
            if (boundTerminal) boundTerminal.Changed += TryBegin;
            if (RestartButton) RestartButton.onClick.AddListener(Restart);
            if (HasStarted) CaptureInput();
            else Hide();
        }

        void OnDisable()
        {
            Loc.LanguageChanged -= OnLanguageChanged;
            if (boundTerminal) boundTerminal.Changed -= TryBegin;
            boundTerminal = null;
            if (RestartButton) RestartButton.onClick.RemoveListener(Restart);
            ReleaseInput();
            Hide();
        }

        void OnApplicationFocus(bool value) { focused = value; inputCooldown = .15f; }
        void OnApplicationPause(bool value) { applicationPaused = value; inputCooldown = .15f; }

        void TryBegin()
        {
            if (HasStarted || !isActiveAndEnabled || !Bound || !Mission || !Mission.WindowClosed) return;
            var session = Terminal ? Terminal.Session : null;
            if (session == null || session.CurrentNodeId != "another_tomorrow_07" ||
                session.Phase != TerminalPhase.Completed) return;
            HasStarted = true;
            IsComplete = false;
            Elapsed = stageTime = 0f;
            inputCooldown = .15f;
            CardIndex = -1;
            stage = Stage.Blackout;
            startedFrame = Time.frameCount;
            CaptureInput();
            Render();
        }

        void CaptureInput()
        {
            if (inputCaptured) return;
            inputCaptured = true;
            if (CockpitCamera)
            {
                previousCameraBlock = CockpitCamera.PresentationBlocked;
                CockpitCamera.PresentationBlocked = true;
                if (CockpitCamera.FreeLook)
                {
                    previousLookBlock = CockpitCamera.FreeLook.InputBlocked;
                    CockpitCamera.FreeLook.InputBlocked = true;
                }
            }
            previousCursorVisible = Cursor.visible;
            previousCursorLock = Cursor.lockState;
            previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void ReleaseInput()
        {
            if (!inputCaptured) return;
            inputCaptured = false;
            if (CockpitCamera)
            {
                CockpitCamera.PresentationBlocked = previousCameraBlock;
                if (CockpitCamera.FreeLook)
                {
                    CockpitCamera.FreeLook.InputBlocked = previousLookBlock;
                    CockpitCamera.FreeLook.WaitForRightRelease();
                }
            }
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            if (EventSystem.current && (!PauseMenu || !PauseMenu.IsOpen))
                EventSystem.current.SetSelectedGameObject(previousSelection && previousSelection.activeInHierarchy
                    ? previousSelection : null);
        }

        void Update()
        {
            if (!HasStarted) { TryBegin(); return; }
            if (!Bound || loading || Time.frameCount <= startedFrame) return;
            if (!CanAdvance) { inputCooldown = .15f; return; }
            // Story reading can own timeScale=0. Only the real pause menu / lost focus pauses the epilogue.
            float delta = Mathf.Min(Time.unscaledDeltaTime, .1f);
            inputCooldown = Mathf.Max(0f, inputCooldown - delta);
            Elapsed += delta;
            stageTime += delta;
            switch (stage)
            {
                case Stage.Blackout:
                    if (stageTime >= Script.BlackoutSeconds) ShowCard(0);
                    break;
                case Stage.FadeIn:
                    if (stageTime >= Script.TextFadeSeconds)
                    {
                        SetStage(CardIndex == Script.Cards.Length - 1 ? Stage.Complete : Stage.Hold);
                        if (stage == Stage.Complete)
                        {
                            IsComplete = true;
                            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(RestartButton.gameObject);
                        }
                    }
                    break;
                case Stage.Hold:
                    if (stageTime >= Script.Cards[CardIndex].HoldSeconds) SetStage(Stage.FadeOut);
                    else if (stageTime >= .75f && NextPressed()) AdvanceCard();
                    break;
                case Stage.FadeOut:
                    if (stageTime >= Script.TextFadeSeconds) SetStage(Stage.Interval);
                    break;
                case Stage.Interval:
                    if (stageTime >= Script.BetweenCardsSeconds) ShowCard(CardIndex + 1);
                    break;
            }
            Render();
        }

        static bool NextPressed() =>
            (Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame ||
                                         Keyboard.current.enterKey.wasPressedThisFrame)) ||
            (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);

        /// <summary>Advance only a fully revealed card; the dialogue's last click cannot skip the opening.</summary>
        public void AdvanceCard()
        {
            if (HasStarted && CanAdvance && inputCooldown <= 0f && stage == Stage.Hold && stageTime >= .75f)
                SetStage(Stage.FadeOut);
        }

        void SetStage(Stage value) { stage = value; stageTime = 0f; }
        void ShowCard(int index)
        {
            CardIndex = index;
            ApplyLanguage(IsChinese);
            SetStage(Stage.FadeIn);
        }

        bool IsChinese => Loc.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        void OnLanguageChanged(string language)
        {
            if (Bound && CardIndex >= 0)
                ApplyLanguage(language.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        }

        void ApplyLanguage(bool chinese)
        {
            if (!Bound || CardIndex < 0 || CardIndex >= Script.Cards.Length) return;
            var card = Script.Cards[CardIndex];
            var font = chinese ? ChineseFont : LatinFont;
            foreach (var label in new[] { CueText, TitleText, BodyText, FootnoteText, HintText, RestartText })
                label.font = font;
            CueText.text = chinese ? card.ChineseCue : card.EnglishCue;
            TitleText.text = chinese ? card.ChineseTitle : card.EnglishTitle;
            BodyText.text = chinese ? card.ChineseBody : card.EnglishBody;
            FootnoteText.text = chinese ? card.ChineseFootnote : card.EnglishFootnote;
            TitleText.fontSize = chinese ? 84f : 88f;
            BodyText.fontSize = chinese ? 34f : 40f;
            BodyText.lineSpacing = chinese ? 24f : 14f;
            CueText.characterSpacing = chinese ? 5f : 8f;
            TitleText.characterSpacing = chinese ? 9f : 0f;
            FootnoteText.characterSpacing = chinese ? 2f : 4f;
            HintText.text = CardIndex == Script.Cards.Length - 1
                ? (chinese ? "ESC   /   菜单" : "ESC   /   MENU")
                : (chinese ? "空格 或 单击   /   下一幕" : "SPACE OR CLICK   /   NEXT ACT");
            RestartText.text = chinese ? "再次启程" : "BEGIN AGAIN";
        }

        void Render()
        {
            if (!Bound) return;
            BlackoutGroup.alpha = stage == Stage.Blackout ? Smooth(stageTime / Script.BlackoutSeconds) : 1f;
            BlackoutGroup.blocksRaycasts = HasStarted;
            BlackoutGroup.interactable = HasStarted;
            float alpha = stage == Stage.FadeIn ? Smooth(stageTime / Script.TextFadeSeconds)
                : stage == Stage.FadeOut ? 1f - Smooth(stageTime / Script.TextFadeSeconds)
                : stage == Stage.Hold || stage == Stage.Complete ? 1f : 0f;
            CardGroup.alpha = alpha;
            ActionsGroup.alpha = IsComplete ? Smooth(stageTime / .8f) : 0f;
            ActionsGroup.interactable = ActionsGroup.blocksRaycasts = IsComplete && CanAdvance && !loading;
            HintText.alpha = (stage == Stage.Hold && stageTime >= .75f) || IsComplete ? .55f : 0f;
        }

        public void Restart()
        {
            if (!IsComplete || !CanAdvance || inputCooldown > 0f || loading) return;
            loading = true;
            ActionsGroup.interactable = false;
            Time.timeScale = 1f;
            SceneManager.LoadSceneAsync(gameObject.scene.path);
        }

        /// <summary>Static editor preview; does not advance the mission or invoke gameplay events.</summary>
        public void PreviewCard(int index, bool chinese)
        {
            if (Application.isPlaying || !Bound) return;
            CardIndex = Mathf.Clamp(index, 0, Script.Cards.Length - 1);
            ApplyLanguage(chinese);
            BlackoutGroup.alpha = CardGroup.alpha = 1f;
            BlackoutGroup.blocksRaycasts = BlackoutGroup.interactable = false;
            ActionsGroup.alpha = CardIndex == Script.Cards.Length - 1 ? 1f : 0f;
            ActionsGroup.interactable = ActionsGroup.blocksRaycasts = false;
            HintText.alpha = .55f;
        }

        public void Hide()
        {
            if (BlackoutGroup)
            {
                BlackoutGroup.alpha = 0f;
                BlackoutGroup.interactable = BlackoutGroup.blocksRaycasts = false;
            }
            if (CardGroup) CardGroup.alpha = 0f;
            if (ActionsGroup)
            {
                ActionsGroup.alpha = 0f;
                ActionsGroup.interactable = ActionsGroup.blocksRaycasts = false;
            }
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    }
}
