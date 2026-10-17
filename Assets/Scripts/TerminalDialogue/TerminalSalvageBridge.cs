using LudumDare60.Salvage;
using UnityEngine;

namespace LudumDare60.TerminalDialogue
{
    /// <summary>Optional bridge; closing the dialogue pauses dialogue only, not the salvage simulation.</summary>
    public sealed class TerminalSalvageBridge : MonoBehaviour
    {
        [SerializeField] private TerminalDialogueController terminal;
        [SerializeField] private SalvageController salvage;
        [SerializeField] private string startEventId = "start_salvage";

        private void OnEnable()
        {
            if (terminal != null) terminal.EventTriggered += OnEvent;
            if (salvage != null) salvage.Completed += OnCompleted;
        }
        private void OnDisable()
        {
            if (terminal != null) terminal.EventTriggered -= OnEvent;
            if (salvage != null) salvage.Completed -= OnCompleted;
        }
        private void OnEvent(string id)
        {
            if (id != startEventId) return;
            var started = salvage != null && salvage.TryStartSalvage();
            terminal.SetFlag("salvage_started", started);
            if (started) terminal.SetFlag("salvage_succeeded", false);
            terminal.PostSystemMessage(started ? "Salvage program started." : "Unable to start salvage. Check the target configuration and current operation.");
        }
        private void OnCompleted(SalvageResult result)
        {
            if (terminal == null) return;
            terminal.SetFlag("salvage_started", false);
            terminal.SetFlag("salvage_succeeded", result.IsSuccess);
            terminal.PostSystemMessage(result.IsSuccess
                ? "Salvage complete: " + result.RewardId + " x " + result.RewardQuantity
                : "Salvage ended: " + result.Reason);
        }
    }
}
