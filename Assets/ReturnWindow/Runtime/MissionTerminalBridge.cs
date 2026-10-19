using System;
using LudumDare60.TerminalDialogue;
using UnityEngine;

namespace LudumDare60.ReturnWindow
{
    /// <summary>One authority supplies the AI, paper calendar and departure instrument.</summary>
    public sealed class MissionTerminalBridge : MonoBehaviour
    {
        public MissionController mission;
        public TerminalDialogueController terminal;
        public ReturnCalendarView calendar;
        public ReturnCountdownView countdown;
        public ReturnRepairStation repairStation;
        public ReturnSalvageStation salvageStation;
        public ReturnEndingView endingView;
        public AudioSource speaker;
        public AudioClip warningClip;
        public AudioClip repairClip;
        public AudioClip departureClip;
        private bool previousRepair;
        private bool bound;
        private TerminalDialogueDefinition runtimeStory;

        private void Awake()
        {
            mission = MissionController.Instance;
            if (terminal == null || mission == null || terminal.Conversation == null) return;
            // Instantiate before terminal.Start so tuning never mutates the authored asset.
            runtimeStory = Instantiate(terminal.Conversation);
            foreach (var node in runtimeStory.Graph.Nodes)
                foreach (var line in node.Lines)
                    line.Text = mission.Schedule.Resolve(line.Text, mission.Clock.DurationSeconds);
            terminal.Configure(runtimeStory);
        }

        private void Start()
        {
            mission = MissionController.Instance;
            if (mission == null || terminal == null) { enabled = false; return; }
            if (terminal.Session == null) terminal.Begin();
            terminal.ChoiceAccepted += OnChoice;
            terminal.Changed += OnDialogueChanged;
            mission.WarningReached += OnWarning;
            mission.StateChanged += OnState;
            bound = true;
            previousRepair = mission.RepairComplete;
            terminal.SetFlag("repair_ready", previousRepair);
            if (mission.State == MissionState.Running)
            {
                terminal.GoToNode("mission");
                terminal.Close();
            }
            OnDialogueChanged();
            if (mission.State == MissionState.Departed || mission.State == MissionState.Stayed)
                OnState(mission.State);
        }

        private void Update()
        {
            if (!bound) return;
            if (mission.RepairComplete != previousRepair)
            {
                previousRepair = mission.RepairComplete;
                terminal.SetFlag("repair_ready", previousRepair);
                terminal.PostSystemMessage(previousRepair
                    ? "PROPULSION MANIFOLD VERIFIED. All three connections are secure. You may now confirm departure."
                    : "MANIFOLD CONNECTION LOST. Repair verification is no longer valid.");
                if (previousRepair && speaker != null && repairClip != null) speaker.PlayOneShot(repairClip, .35f);
            }
            var clock = mission.Clock;
            bool stayed = mission.State == MissionState.Stayed;
            bool departed = mission.State == MissionState.Departed;
            if (calendar != null)
            {
                calendar.DayCount = clock.MissionDays;
                calendar.StartDate = mission.Schedule.StartDate;
                calendar.DepartureConfirmed = departed;
                calendar.Render(clock.CompletedDays, clock.CurrentDay, stayed, mission.RepairComplete ? 1f : 0f);
            }
            double simulatedSeconds = clock.RemainingFraction * clock.MissionDays * 86400d;
            countdown?.Render(simulatedSeconds, stayed, departed, clock.RemainingFraction <= .25d);
        }

        private void OnDialogueChanged() => mission.SetReadingPaused(terminal.IsStoryReading);

        private void OnChoice(string nodeId, string choiceId)
        {
            switch (choiceId)
            {
                case "start_mission":
                    terminal.GoToNode("mission");
                    mission.SetReadingPaused(false);
                    mission.StartMission();
                    terminal.Close();
                    break;
                case "status":
                    terminal.PostSystemMessage(StatusText());
                    break;
                case "read_story":
                    terminal.GoToNode("story_memory");
                    break;
                case "salvage":
                    terminal.GoToNode("mission");
                    terminal.Close();
                    salvageStation?.OpenSalvage();
                    break;
                case "open_repair":
                    terminal.GoToNode("mission");
                    terminal.Close();
                    repairStation?.OpenRepair();
                    break;
                case "confirm_launch":
                    if (!mission.TryLaunch() && mission.State == MissionState.Running)
                        terminal.PostSystemMessage("DEPARTURE NOT AUTHORIZED. Complete the manifold repair first.");
                    break;
                case "restart":
                case "restart_mission":
                    endingView?.RestartMission();
                    break;
            }
        }

        public string StatusText()
        {
            var clock = mission.Clock;
            long minutes = (long)Math.Ceiling(clock.RemainingFraction * clock.MissionDays * 1440d);
            return "DATE: " + MissionSchedule.Label(mission.Schedule.DateAt(clock.CurrentDay - 1))
                + " / LAST DATE: " + MissionSchedule.Label(mission.Schedule.LastDate) + "\n"
                + $"LATEST DEPARTURE: T-{minutes / 1440:00}D {minutes / 60 % 24:00}H {minutes % 60:00}M.\n"
                + (mission.RepairComplete ? "MANIFOLD: VERIFIED. Departure available." : "MANIFOLD: OFFLINE. Reconnect CONTROL / PRESSURE / MOUNT.");
        }

        private void OnWarning(double fraction)
        {
            if (mission.State != MissionState.Running) return;
            terminal.PostSystemMessage($"NAVIGATION WARNING: {Math.Round(fraction * 100):0}% of the departure interval remains.\n"
                + (mission.RepairComplete ? "The manifold is ready. Confirm departure when prepared." : "The manifold is still offline. Finish the three connections.")
                + "\n" + StatusText());
            // The physical instrument owns its 25% chirp; don't play two alerts together.
            if (Math.Abs(fraction - .25d) > .0001d && speaker != null && warningClip != null)
                speaker.PlayOneShot(warningClip, .3f);
        }

        private void OnState(MissionState state)
        {
            if (state != MissionState.Departed && state != MissionState.Stayed) return;
            repairStation?.CloseRepair(false);
            salvageStation?.CloseSalvage(false);
            terminal.GoToNode(state == MissionState.Departed ? "ending_home" : "ending_stay");
            terminal.Open();
            for (int i = 0; i < 100 && terminal.Session.Phase != TerminalPhase.WaitingForChoice; i++)
                terminal.Advance();
            terminal.Close();
            endingView?.Show(state, mission.Schedule);
            if (state == MissionState.Departed && speaker != null && departureClip != null)
                speaker.PlayOneShot(departureClip, .3f);
        }

        private void OnDestroy()
        {
            if (!bound) return;
            if (terminal != null) { terminal.ChoiceAccepted -= OnChoice; terminal.Changed -= OnDialogueChanged; }
            if (mission != null)
            {
                mission.WarningReached -= OnWarning;
                mission.StateChanged -= OnState;
                mission.SetReadingPaused(false);
            }
            if (runtimeStory != null) Destroy(runtimeStory);
        }
    }
}
