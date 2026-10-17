using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace LudumDare60.TerminalDialogue
{
    /// <summary>Screen-space UI. Keep this component outside windowRoot so it can reopen the window.</summary>
    public sealed class TerminalOptionsPanel : MonoBehaviour
    {
        [SerializeField] private TerminalDialogueController controller;
        [SerializeField] private GameObject windowRoot;
        [SerializeField] private Transform choicesRoot;
        [SerializeField] private TerminalChoiceButton choiceTemplate;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text advanceLabel;
        [SerializeField] private UnityEngine.UI.Button advanceButton;
        [SerializeField] private UnityEngine.UI.Button closeButton;
        [SerializeField] private UnityEngine.UI.Button openButton;
        private readonly List<TerminalChoiceButton> pool = new List<TerminalChoiceButton>();
        private TerminalDialogueSession displayedSession;
        private TerminalPhase lastPhase;
        private int optionsRevision = -1;
        private bool wasOpen;

        public void Configure(TerminalDialogueController source, GameObject window, Transform choices,
            TerminalChoiceButton template, TMP_Text status, UnityEngine.UI.Button advance, TMP_Text advanceText,
            UnityEngine.UI.Button close, UnityEngine.UI.Button open)
        {
            Unsubscribe();
            controller = source; windowRoot = window; choicesRoot = choices; choiceTemplate = template;
            statusText = status; advanceButton = advance; advanceLabel = advanceText; closeButton = close; openButton = open;
            if (choiceTemplate != null) choiceTemplate.gameObject.SetActive(false);
            if (isActiveAndEnabled) Subscribe();
            Refresh();
        }

        private void OnEnable() { Subscribe(); optionsRevision = -1; Refresh(); }
        private void OnDisable() => Unsubscribe();
        private void Subscribe()
        {
            if (controller != null) controller.Changed += Refresh;
            if (advanceButton != null) advanceButton.onClick.AddListener(Advance);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (openButton != null) openButton.onClick.AddListener(Open);
        }
        private void Unsubscribe()
        {
            if (controller != null) controller.Changed -= Refresh;
            if (advanceButton != null) advanceButton.onClick.RemoveListener(Advance);
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
            if (openButton != null) openButton.onClick.RemoveListener(Open);
        }
        private void Advance() { if (controller != null) controller.Advance(); }
        private void Close() { if (controller != null) controller.Close(); }
        private void Open() { if (controller != null) controller.Open(); }

        private void Refresh()
        {
            var session = controller != null ? controller.Session : null;
            var open = session != null && session.IsOpen;
            if (windowRoot != null) windowRoot.SetActive(open);
            if (openButton != null) openButton.gameObject.SetActive(!open);
            if (session == displayedSession && session != null && optionsRevision == session.OptionsRevision
                && lastPhase == session.Phase && wasOpen == open) return;
            displayedSession = session;
            wasOpen = open;
            optionsRevision = session?.OptionsRevision ?? -1;
            lastPhase = session?.Phase ?? TerminalPhase.Idle;
            if (choiceTemplate != null) choiceTemplate.gameObject.SetActive(false);
            var showChoices = open && session.Phase == TerminalPhase.WaitingForChoice;
            var count = showChoices ? session.Choices.Count : 0;
            if (choiceTemplate != null && choicesRoot != null)
            {
                while (pool.Count < count)
                {
                    var item = Instantiate(choiceTemplate, choicesRoot);
                    pool.Add(item);
                }
                for (var i = 0; i < pool.Count; i++)
                {
                    if (i < count) pool[i].Bind(controller, session.Choices[i]);
                    pool[i].gameObject.SetActive(i < count);
                }
            }
            var canAdvance = open && (lastPhase == TerminalPhase.Typing || lastPhase == TerminalPhase.Holding
                || lastPhase == TerminalPhase.WaitingForContinue);
            if (advanceButton != null) advanceButton.gameObject.SetActive(canAdvance);
            if (advanceLabel != null) advanceLabel.text = lastPhase == TerminalPhase.Typing ? "SHOW FULL TEXT" : "CONTINUE";
            if (statusText != null)
                statusText.text = !open ? "TERMINAL OFFLINE" : lastPhase == TerminalPhase.WaitingForChoice
                    ? (count > 0 ? "SELECT A RESPONSE" : "WAITING FOR AVAILABLE ACTIONS")
                    : lastPhase == TerminalPhase.Completed ? "SESSION ENDED" : lastPhase == TerminalPhase.WaitingForContinue
                        ? "AWAITING CONTINUE" : "TRANSMITTING...";
        }
    }
}
