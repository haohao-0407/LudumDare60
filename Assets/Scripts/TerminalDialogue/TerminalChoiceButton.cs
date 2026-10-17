using TMPro;
using UnityEngine;

namespace LudumDare60.TerminalDialogue
{
    [RequireComponent(typeof(UnityEngine.UI.Button))]
    public sealed class TerminalChoiceButton : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        private UnityEngine.UI.Button button;
        private TerminalDialogueController controller;
        private string choiceId;

        private void Awake()
        {
            button = GetComponent<UnityEngine.UI.Button>();
            button.onClick.AddListener(Select);
        }
        private void OnDestroy() { if (button != null) button.onClick.RemoveListener(Select); }
        public void ConfigureLabel(TMP_Text text) => label = text;
        public void Bind(TerminalDialogueController source, TerminalChoice choice)
        {
            controller = source;
            choiceId = choice.Id;
            if (label != null) { label.richText = false; label.text = choice.Label; }
        }
        private void Select() { if (controller != null) controller.Choose(choiceId); }
    }
}
