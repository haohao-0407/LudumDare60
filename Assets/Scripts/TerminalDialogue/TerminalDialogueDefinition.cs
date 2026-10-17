using UnityEngine;

namespace LudumDare60.TerminalDialogue
{
    [CreateAssetMenu(fileName = "TerminalDialogue", menuName = "Terminal Dialogue/Conversation")]
    public sealed class TerminalDialogueDefinition : ScriptableObject
    {
        [SerializeField] private TerminalDialogueGraph graph = new TerminalDialogueGraph();
        public TerminalDialogueGraph Graph => graph;
    }
}
