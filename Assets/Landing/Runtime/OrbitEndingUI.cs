using System.Collections;
using LudumDare60.TerminalDialogue;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LudumDare60.Landing
{
    /// <summary>Non-interactive overlay, shown after orbit and the final dialogue line.</summary>
    [DisallowMultipleComponent]
    public sealed class OrbitEndingUI : MonoBehaviour
    {
        public LaunchSequenceController Launch;
        public TerminalDialogueController Terminal;
        public OrbitCreditsDefinition Credits;
        public CanvasGroup RootGroup;
        public RectTransform ContentRoot;
        public TMP_Text GameTitleText;
        public CanvasGroup ThanksGroup;
        public TMP_Text ThanksText;
        public CanvasGroup CreditsGroup;
        public RectTransform CreditsViewport;
        public RectTransform CreditsContent;
        public TMP_Text CreditsText;
        public TMP_Text StatusText;

        LaunchSequenceController boundLaunch;
        TerminalDialogueController boundTerminal;
        Coroutine routine;
        Coroutine orbitGate;
        Vector2 authoredPosition;
        bool positionCaptured;
        bool armed;
        bool started;
        float elapsed;
        float contentHeight;
        float viewportHeight;
        int thanksCharacters;

        bool IsBound => Credits && RootGroup && ContentRoot && GameTitleText && ThanksGroup &&
                        ThanksText && CreditsGroup && CreditsViewport && CreditsContent && CreditsText && StatusText;
        float Fade => Mathf.Max(0f, Credits.FadeDuration);
        float ThanksStart => Mathf.Max(0f, Credits.OrbitDelay);
        float CreditsStart => ThanksStart + Fade + Mathf.Max(0f, Credits.ThanksHoldDuration);
        float ScrollStart => CreditsStart + Fade;
        float ScrollDuration => (viewportHeight + contentHeight) / Mathf.Max(1f, Credits.ScrollSpeed);
        float ReturnStart => ScrollStart + ScrollDuration + Mathf.Max(0f, Credits.CreditHoldDuration);
        public float PreviewDurationSeconds => IsBound ? ReturnStart + Fade : 0f;

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            PrepareVisuals();
            Hide();
            boundLaunch = Launch;
            boundTerminal = Terminal;
            if (boundLaunch)
            {
                boundLaunch.OnOrbitReached.AddListener(HandleOrbit);
                boundLaunch.OnLaunchCancelled.AddListener(ResetPresentation);
            }
            if (boundTerminal) boundTerminal.Changed += TryStartPresentation;
            if (Launch && Launch.HasReachedOrbit)
            {
                if (started && IsBound) routine = StartCoroutine(Play());
                else HandleOrbit();
            }
        }

        void OnDisable()
        {
            if (boundLaunch)
            {
                boundLaunch.OnOrbitReached.RemoveListener(HandleOrbit);
                boundLaunch.OnLaunchCancelled.RemoveListener(ResetPresentation);
            }
            if (boundTerminal) boundTerminal.Changed -= TryStartPresentation;
            boundLaunch = null;
            boundTerminal = null;
            StopAnimation();
            Hide();
        }

        void HandleOrbit()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || started || orbitGate != null) return;
            armed = true;
            orbitGate = StartCoroutine(WaitForOrbitDialogue());
        }

        IEnumerator WaitForOrbitDialogue()
        {
            // Departure's listener selects launch_orbit during the same UnityEvent dispatch.
            yield return null;
            var session = Terminal ? Terminal.Session : null;
            if (Terminal && Terminal.isActiveAndEnabled && session != null &&
                session.CurrentNodeId == "launch_orbit" && !session.IsOpen) Terminal.Open();
            orbitGate = null;
            TryStartPresentation();
        }

        void TryStartPresentation()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !armed || started || orbitGate != null ||
                !Launch || !Launch.HasReachedOrbit || !IsBound) return;
            var session = Terminal ? Terminal.Session : null;
            if (Terminal && (session == null || session.CurrentNodeId != "launch_orbit" ||
                session.Phase != TerminalPhase.Completed)) return;
            PrepareVisuals();
            started = true;
            elapsed = 0f;
            routine = StartCoroutine(Play());
        }

        IEnumerator Play()
        {
            ApplyFrame(elapsed);
            while (elapsed < PreviewDurationSeconds)
            {
                yield return null;
                if (Time.deltaTime <= 0f) continue;
                elapsed = Mathf.Min(PreviewDurationSeconds, elapsed + Time.deltaTime);
                ApplyFrame(elapsed);
            }
            routine = null;
        }

        /// <summary>Editor preview only; never starts a flight, dialogue, or runtime coroutine.</summary>
        public void PreviewFrame(float previewElapsed)
        {
            if (Application.isPlaying || !IsBound) return;
            PrepareVisuals();
            ApplyFrame(Mathf.Max(0f, previewElapsed));
        }

        public void ResetPresentation()
        {
            StopAnimation();
            armed = started = false;
            elapsed = 0f;
            Hide();
            positionCaptured = false;
        }

        void StopAnimation()
        {
            if (routine != null) StopCoroutine(routine);
            if (orbitGate != null) StopCoroutine(orbitGate);
            routine = orbitGate = null;
        }

        void PrepareVisuals()
        {
            if (!IsBound) return;
            if (!positionCaptured)
            {
                authoredPosition = ContentRoot.anchoredPosition;
                positionCaptured = true;
            }
            foreach (var graphic in RootGroup.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            foreach (var group in RootGroup.GetComponentsInChildren<CanvasGroup>(true))
            {
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            GameTitleText.text = Credits.GameTitle;
            ThanksText.text = Credits.ThanksMessage;
            ThanksText.maxVisibleCharacters = int.MaxValue;
            ThanksText.ForceMeshUpdate();
            thanksCharacters = ThanksText.textInfo.characterCount;
            CreditsText.text = Credits.CreditsText;
            CreditsText.maxVisibleCharacters = int.MaxValue;
            Canvas.ForceUpdateCanvases();
            viewportHeight = Mathf.Max(1f, CreditsViewport.rect.height);
            contentHeight = Mathf.Max(1f, CreditsText.GetPreferredValues(Credits.CreditsText,
                Mathf.Max(1f, CreditsViewport.rect.width), Mathf.Infinity).y);
            CreditsContent.anchorMin = new Vector2(0f, 1f);
            CreditsContent.anchorMax = new Vector2(1f, 1f);
            CreditsContent.pivot = new Vector2(.5f, 1f);
            CreditsContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
        }

        void ApplyFrame(float time)
        {
            if (!IsBound) return;
            float entrance = Progress(time, ThanksStart, Fade);
            float creditsIn = Progress(time, CreditsStart, Fade);
            float creditsOut = Progress(time, ReturnStart, Fade);
            RootGroup.alpha = entrance;
            ContentRoot.anchoredPosition = authoredPosition + Vector2.left * (24f * (1f - entrance));
            ThanksGroup.alpha = (1f - creditsIn) + creditsOut;
            CreditsGroup.alpha = creditsIn * (1f - creditsOut);
            ThanksText.maxVisibleCharacters = time >= CreditsStart ? int.MaxValue : Mathf.FloorToInt(thanksCharacters * entrance);
            float scroll = Mathf.Clamp01((time - ScrollStart) / Mathf.Max(.001f, ScrollDuration));
            CreditsContent.anchoredPosition = new Vector2(0f, Mathf.Lerp(-viewportHeight, contentHeight, scroll));
            StatusText.text = time >= ReturnStart ? Credits.EndMessage : string.Empty;
        }

        void Hide()
        {
            if (RootGroup)
            {
                RootGroup.alpha = 0f;
                RootGroup.blocksRaycasts = false;
                RootGroup.interactable = false;
            }
            if (ContentRoot && positionCaptured) ContentRoot.anchoredPosition = authoredPosition;
            if (ThanksGroup) ThanksGroup.alpha = 1f;
            if (CreditsGroup) CreditsGroup.alpha = 0f;
            if (ThanksText) ThanksText.maxVisibleCharacters = int.MaxValue;
            if (StatusText) StatusText.text = string.Empty;
        }

        static float Progress(float time, float start, float duration)
        {
            float t = duration <= 0f ? (time >= start ? 1f : 0f) : Mathf.Clamp01((time - start) / duration);
            return t * t * (3f - 2f * t);
        }
    }
}
