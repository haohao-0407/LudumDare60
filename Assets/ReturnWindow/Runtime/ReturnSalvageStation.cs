using LudumDare60.Salvage;
using LudumDare60.Storage;
using LudumDare60.TerminalDialogue;
using TMPro;
using Pioneer60.Localization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LudumDare60.ReturnWindow
{
    /// <summary>Optional supply recovery. Uses the existing simulation; never creates a second timer or save.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class ReturnSalvageStation : MonoBehaviour
    {
        public MissionController mission;
        public TerminalDialogueController terminal;
        public SalvageController salvage;
        public SalvageDefinition definition;
        public InventoryController inventory;
        public GameObject panel;
        public TMP_Text statusText;
        public TMP_Text progressText;
        public RectTransform targetMarker;
        public RectTransform controlZone;
        public Image progressFill;

        public bool IsOpen { get; private set; }
        private string resultText;

        private void Start()
        {
            if (mission != null) mission.StateChanged -= OnMissionStateChanged;
            mission = MissionController.Instance;
            if (mission != null)
            {
                mission.StateChanged += OnMissionStateChanged;
                inventory = mission.GetComponent<InventoryController>();
            }
        }

        private void OnEnable()
        {
            if (salvage != null)
            {
                salvage.SnapshotChanged += Refresh;
                salvage.Completed += OnCompleted;
            }
            if (mission != null) mission.StateChanged += OnMissionStateChanged;
        }

        private void OnDisable()
        {
            CloseSalvage(false);
            if (salvage != null)
            {
                salvage.SnapshotChanged -= Refresh;
                salvage.Completed -= OnCompleted;
            }
            if (mission != null) mission.StateChanged -= OnMissionStateChanged;
        }

        public void OpenSalvage()
        {
            if (IsOpen || mission == null || mission.State != MissionState.Running || mission.IsGameplayPaused) return;
            if (salvage == null || definition == null || inventory == null || panel == null)
            {
                terminal?.PostSystemMessage("Supply recovery is unavailable: station configuration is incomplete.");
                terminal?.Open();
                return;
            }
            IsOpen = true;
            panel.SetActive(true);
            terminal?.Close();
            RetrySalvage();
        }

        public void RetrySalvage()
        {
            if (!IsOpen || mission == null || mission.IsGameplayPaused || salvage == null || salvage.IsActive) return;
            resultText = null;
            if (!salvage.TryStartSalvage(definition)) resultText = "Unable to begin recovery. Return to the terminal and try again.";
            Refresh(salvage.Snapshot);
        }

        private void LateUpdate() { if (IsOpen && salvage != null) Refresh(salvage.Snapshot); }

        public void CloseSalvage() => CloseSalvage(true);

        public void CloseSalvage(bool reconnect)
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (salvage != null)
            {
                salvage.ReleaseControl();
                salvage.CancelSalvage();
            }
            if (panel != null) panel.SetActive(false);
            if (reconnect && mission != null && mission.State == MissionState.Running) terminal?.Open();
        }

        private void Update()
        {
            if (!IsOpen || salvage == null) return;
            if (mission == null || mission.State != MissionState.Running)
            {
                CloseSalvage(false);
                return;
            }
            if (mission.IsGameplayPaused)
            {
                salvage.ReleaseControl();
                if (statusText != null) statusText.text = Loc.Say("RECOVERY PAUSED");
                return;
            }

            var space = Keyboard.current?.spaceKey;
            if (salvage.Snapshot.State == SalvageState.SignalDetected && space != null && space.wasPressedThisFrame)
                salvage.TryLockTarget();
            salvage.SetControlHeld(salvage.Snapshot.State == SalvageState.Salvaging && space != null && space.isPressed);
        }

        private void OnMissionStateChanged(MissionState state)
        {
            if (state == MissionState.Departed || state == MissionState.Stayed) CloseSalvage(false);
        }

        private void OnCompleted(SalvageResult result)
        {
            if (result == null || mission == null || mission.State != MissionState.Running) return;
            if (result.IsSuccess)
            {
                // The mission inventory lives in memory for the current journey.
                // The attempt ID makes repeated notifications harmless; never call Save here.
                var stored = inventory != null && (inventory.Items.HasProcessedReward(result.AttemptId)
                    || inventory.Items.TryAddOnce(result.AttemptId, result.RewardId, result.RewardQuantity));
                resultText = stored
                    ? Loc.T("SUPPLIES RECOVERED: {0} x {1}. Return to repairs or recover another cache.",
                        Loc.Say(result.RewardId.Replace('_', ' ').ToUpperInvariant()), result.RewardQuantity)
                    : Loc.Say("Recovery complete, but supplies could not be stored.");
            }
            else resultText = result.Reason == SalvageEndReason.Cancelled
                ? Loc.Say("Recovery cancelled. No supplies recovered.")
                : Loc.T("SIGNAL LOST: {0}. Retry or return to repairs.", Loc.Say(ReasonText(result.Reason)));
            terminal?.PostSystemMessage(resultText);
            if (salvage != null) Refresh(salvage.Snapshot);
        }

        private void Refresh(SalvageSnapshot snapshot)
        {
            if (!IsOpen) return;
            if (statusText != null)
            {
                if (mission != null && mission.IsGameplayPaused) statusText.text = Loc.Say("RECOVERY PAUSED");
                else if (!string.IsNullOrEmpty(resultText)) statusText.text = resultText;
                else if (snapshot.State == SalvageState.Searching) statusText.text = Loc.Say("SEARCHING... Wait for the signal, then press SPACE.");
                else if (snapshot.State == SalvageState.SignalDetected) statusText.text = Loc.Say("SIGNAL DETECTED - PRESS SPACE NOW");
                else if (snapshot.State == SalvageState.Salvaging) statusText.text = Loc.Say("HOLD SPACE to lift the control band; release to lower it. Keep the target inside.");
                else statusText.text = Loc.Say("Optional supplies. Mission time continues during recovery.");
            }
            if (progressText != null) progressText.text = Loc.T("{0}% RECOVERED  /  {1}s ATTEMPT TIME\nMission departure clock keeps running.",
                Mathf.RoundToInt(snapshot.Progress * 100), Mathf.CeilToInt(snapshot.TimeRemaining));
            if (progressFill != null) progressFill.fillAmount = Mathf.Clamp01(snapshot.Progress);
            if (targetMarker != null)
            {
                targetMarker.anchorMin = new Vector2(0, snapshot.TargetMin);
                targetMarker.anchorMax = new Vector2(1, snapshot.TargetMax);
                targetMarker.offsetMin = targetMarker.offsetMax = Vector2.zero;
                if (snapshot.TargetSize == 0f)
                    targetMarker.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 2f);
            }
            if (controlZone != null)
            {
                controlZone.anchorMin = new Vector2(0, snapshot.ControlMin);
                controlZone.anchorMax = new Vector2(1, snapshot.ControlMax);
                controlZone.offsetMin = controlZone.offsetMax = Vector2.zero;
                var image = controlZone.GetComponent<Image>();
                if (image != null) image.color = snapshot.IsTargetInControl
                    ? new Color(.2f, .75f, .48f, .65f) : new Color(.9f, .55f, .17f, .55f);
            }
        }

        private static string ReasonText(SalvageEndReason reason)
        {
            if (reason == SalvageEndReason.SignalMissed) return "lock window missed";
            if (reason == SalvageEndReason.TargetLost) return "target left the control band";
            if (reason == SalvageEndReason.TimedOut) return "attempt time expired";
            return "recovery interrupted";
        }
    }
}
