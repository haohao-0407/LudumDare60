using System.Collections;
using LudumDare60.ReturnWindow;
using LudumDare60.TerminalDialogue;
using LudumDare60.UI;
using UnityEngine;

namespace LudumDare60.Landing
{
    /// <summary>The SHIP deadline, independent of the landing and launch presentation clocks.</summary>
    [DefaultExecutionOrder(-9000), DisallowMultipleComponent]
    public sealed class CockpitMissionController : MonoBehaviour
    {
        public CockpitOpeningController Opening;
        public CockpitDepartureController Departure;
        public ReturnCalendarView Calendar;
        public ReturnCountdownView Countdown;
        public CockpitScreenTarget TerminalScreen;
        public TerminalDialogueDefinition[] WarningStories = new TerminalDialogueDefinition[3];
        [Min(1)] public float ActiveWorkSeconds = 1200f;
        [Min(0)] public float CalendarCrossHold = .35f;

        public MissionClock Clock { get; private set; }
        public MissionSchedule Schedule { get; private set; }
        public bool WindowClosed => Clock != null && Clock.State == MissionState.Stayed;
        public bool DepartureAuthorized => Clock != null && Clock.State == MissionState.Departed;
        public bool IsCalendarPresentationActive { get; private set; }
        public bool WorkBlocked => !isActiveAndEnabled || Clock == null || Clock.State != MissionState.Running ||
                                   Clock.IsPaused || IsCalendarPresentationActive;

        bool focused, applicationPaused, ownsTimeScale, synchronizing, startingRepairs;
        bool timeoutPresented;
        int queuedCalendarDays, visibleCalendarDays, presentedCalendarDays;
        float previousTimeScale = 1f;
        TerminalDialogueController terminal;
        EscMenuController menu;

        void Awake()
        {
            Clock = new MissionClock(ActiveWorkSeconds, MissionSchedule.DefaultDays);
            Schedule = new MissionSchedule();
            focused = Application.isFocused;
            terminal = Opening ? Opening.Terminal : null;
            menu = Opening && Opening.CockpitCamera ? Opening.CockpitCamera.PauseMenu : null;
            Clock.WindowClosed += HandleWindowClosed;
            Clock.WarningReached += HandleWarning;
            Clock.DayCompleted += HandleDayCompleted;
            if (Calendar)
            {
                Calendar.DayCount = Schedule.DayCount;
                Calendar.StartDate = Schedule.StartDate;
            }
        }

        void OnEnable()
        {
            if (terminal)
            {
                terminal.ChoiceAccepted += HandleChoice;
                terminal.Changed += HandleTerminalChanged;
            }
            Clock?.DiscardSuspendedTime(Time.realtimeSinceStartupAsDouble);
            SynchronizePause();
        }

        void Start()
        {
            if (!Opening || !terminal || !Departure || !Calendar || !Countdown)
                Debug.LogError("[Mission] Assign the SHIP opening, departure, calendar and countdown references.", this);
            RefreshViews();
        }

        // Resolve pause before simulation; settle elapsed time after this frame's player input.
        void Update() => SynchronizePause();
        void LateUpdate()
        {
            SynchronizePause();
            Clock?.AdvanceTo(Time.realtimeSinceStartupAsDouble);
            if (!IsCalendarPresentationActive && queuedCalendarDays > presentedCalendarDays && !DepartureAuthorized)
            {
                IsCalendarPresentationActive = true;
                StartCoroutine(PresentCalendarDays());
            }
            ApplyWorkBlock();
            RefreshViews();
        }

        void HandleTerminalChanged()
        {
            if (startingRepairs && terminal.Session?.CurrentNodeId != "briefing_12")
                startingRepairs = false;
            SynchronizePause();
        }

        void HandleChoice(string node, string choice)
        {
            if (node == "briefing_12" && choice == "briefing_reply_13")
            {
                // The reply still types on the briefing node. Work begins at the accepted click.
                startingRepairs = true;
                if (Opening && Opening.HasCrashed) Clock.Start(Time.realtimeSinceStartupAsDouble);
                SynchronizePause();
            }
            else if (node == "departure_launch_ready" && choice == "launch")
            {
                // Do not wait for the player's typed reply to dispatch launch_requested.
                Departure.RequestLaunch();
            }
        }

        void SynchronizePause()
        {
            if (Clock == null || synchronizing || !isActiveAndEnabled) return;
            synchronizing = true;
            try
            {
                bool reading = terminal && terminal.IsStoryReading &&
                               !(startingRepairs && terminal.Session.CurrentNodeId == "briefing_12");
                bool hold = reading || !focused || applicationPaused || (menu && menu.IsOpen);
                // Retain ownership across an overlapping ESC pause. The menu may have captured
                // our zero timeScale; releasing only after it closes avoids restoring that zero.
                if (hold)
                {
                    if (!ownsTimeScale && Time.timeScale > 0f)
                    {
                        previousTimeScale = Time.timeScale;
                        ownsTimeScale = true;
                    }
                    if (ownsTimeScale) Time.timeScale = 0f;
                }
                else if (ownsTimeScale)
                {
                    Time.timeScale = previousTimeScale;
                    ownsTimeScale = false;
                }
                Clock.SetPaused(hold || (!ownsTimeScale && Time.timeScale <= 0f), Time.realtimeSinceStartupAsDouble);
                ApplyWorkBlock();
            }
            finally { synchronizing = false; }
        }

        void ApplyWorkBlock()
        {
            if (!Opening) return;
            if (Opening.Assembly) Opening.Assembly.GameplayBlocked = WorkBlocked;
            if (Opening.Salvage) Opening.Salvage.GameplayBlocked = WorkBlocked;
        }

        public bool CanAuthorizeLaunch(double timestamp)
        {
            SynchronizePause();
            Clock.AdvanceTo(timestamp);
            return Clock.State == MissionState.Running && !Clock.IsPaused &&
                   Opening && Opening.Assembly && Opening.Assembly.RepairScreen &&
                   Opening.Assembly.RepairScreen.AllRepaired && Departure && Departure.PreflightComplete;
        }

        public bool ConfirmLaunch(double timestamp)
        {
            if (!Departure || !Departure.Launch || !Departure.Launch.IsLaunching ||
                !CanAuthorizeLaunch(timestamp)) return false;
            bool accepted = Clock.TryLaunch(true, timestamp);
            if (accepted) CancelCalendarPresentation(true);
            ApplyWorkBlock();
            RefreshViews();
            return accepted;
        }

        public void CancelDeparture()
        {
            if (Clock == null || !Clock.CancelDeparture(Time.realtimeSinceStartupAsDouble)) return;
            SynchronizePause();
            RefreshViews();
        }

        void HandleWarning(double fraction)
        {
            if (WindowClosed || !terminal) return;
            int index = fraction >= .5 ? 0 : fraction >= .25 ? 1 : 2;
            if (WarningStories == null || index >= WarningStories.Length || !WarningStories[index]) return;
            var graph = WarningStories[index].Graph;
            var node = graph.FindNode(graph.EntryNodeId);
            // Status updates are non-modal: no navigation, lost choice or free reading pause.
            if (node != null && node.Lines.Count > 0)
                terminal.PostSystemMessage(node.Lines[0].Text);
        }

        void HandleDayCompleted(int day)
        {
            queuedCalendarDays = Mathf.Max(queuedCalendarDays, day);
            if (Calendar) Calendar.StrokeAnimationPaused = true;
        }

        bool CanAnimateCalendar => focused && !applicationPaused && !(menu && menu.IsOpen) && Time.timeScale > 0f;

        IEnumerator PresentCalendarDays()
        {
            var camera = Opening ? Opening.CockpitCamera : null;
            var target = Calendar ? Calendar.GetComponent<CockpitScreenTarget>() : null;
            try
            {
                if (!Calendar || !target || !target.isActiveAndEnabled || !target.ViewPoint || !camera ||
                    !camera.isActiveAndEnabled || !camera.ViewCamera)
                {
                    visibleCalendarDays = presentedCalendarDays = queuedCalendarDays;
                    yield break;
                }
                Calendar.StrokeAnimationPaused = true;
                while (camera && !camera.BeginForcedFocus(target))
                {
                    if (!camera || !camera.isActiveAndEnabled || !target || !target.isActiveAndEnabled) yield break;
                    yield return null;
                }
                if (!camera) yield break;
                while (camera && !camera.IsForcedFocusSettled)
                {
                    if (!camera || !camera.IsForcedFocus) yield break;
                    yield return null;
                }
                while (presentedCalendarDays < queuedCalendarDays)
                {
                    int day = presentedCalendarDays + 1;
                    while (!CanAnimateCalendar)
                    {
                        if (!camera || !camera.IsForcedFocusSettled) yield break;
                        yield return null;
                    }
                    if (!Calendar || !camera || !camera.IsForcedFocusSettled) yield break;
                    visibleCalendarDays = day;
                    Calendar.StrokeAnimationPaused = false;
                    RefreshViews();
                    // Reveal the cross only after the camera arrives, including the final date.
                    while (!Calendar.IsCrossAnimationComplete(day))
                    {
                        if (!camera || !camera.IsForcedFocusSettled) yield break;
                        Calendar.StrokeAnimationPaused = !CanAnimateCalendar;
                        yield return null;
                    }
                    float held = 0f;
                    while (held < CalendarCrossHold)
                    {
                        if (!camera || !camera.IsForcedFocusSettled) yield break;
                        if (CanAnimateCalendar) held += Time.unscaledDeltaTime;
                        yield return null;
                    }
                    presentedCalendarDays = day;
                }
                camera.EndForcedFocus(!WindowClosed);
                while (camera && camera.IsForcedFocus) yield return null;
            }
            finally
            {
                if (camera && camera.IsForcedFocus) camera.EndForcedFocus(false);
                IsCalendarPresentationActive = false;
                if (Calendar) Calendar.StrokeAnimationPaused = queuedCalendarDays > presentedCalendarDays;
                ApplyWorkBlock();
                RefreshViews();
                if (isActiveAndEnabled && WindowClosed && presentedCalendarDays >= queuedCalendarDays)
                    PresentTimeoutEnding();
            }
        }

        void CancelCalendarPresentation(bool completePendingDays)
        {
            StopAllCoroutines();
            if (Opening && Opening.CockpitCamera && IsCalendarPresentationActive)
                Opening.CockpitCamera.EndForcedFocus(false);
            IsCalendarPresentationActive = false;
            if (completePendingDays)
                visibleCalendarDays = presentedCalendarDays = queuedCalendarDays = Clock.CompletedDays;
            if (Calendar) Calendar.StrokeAnimationPaused = queuedCalendarDays > presentedCalendarDays;
        }

        void HandleWindowClosed()
        {
            if (Departure) Departure.CloseReturnWindow();
            if (Opening)
            {
                if (Opening.Assembly) Opening.Assembly.SetRepairsAvailable(false);
                if (Opening.Salvage) Opening.Salvage.SetFeatureEnabled(false);
            }
            ApplyWorkBlock();
            RefreshViews();
            if (presentedCalendarDays >= queuedCalendarDays) PresentTimeoutEnding();
        }

        void PresentTimeoutEnding()
        {
            if (timeoutPresented) return;
            timeoutPresented = true;
            RefreshViews();
            if (!terminal) return;
            terminal.SetFlag("pioneer60.departure_ready", false);
            terminal.SetFlag("pioneer60.return_window_closed", true);
            terminal.Open();
            terminal.GoToNode("another_tomorrow_open");
            if (Opening && Opening.CockpitCamera && TerminalScreen)
                StartCoroutine(FocusEndingTerminal());
        }

        IEnumerator FocusEndingTerminal()
        {
            var camera = Opening.CockpitCamera;
            // FocusScreen deliberately ignores a second target while docked. Leave the workbench
            // first; both transitions use unscaled time and still work during the ending's pause.
            while (!Application.isFocused || (menu && menu.IsOpen)) yield return null;
            if (camera.CurrentScreen == TerminalScreen) yield break;
            camera.ReturnToFree();
            while (camera && !camera.IsFreeView) yield return null;
            if (camera) camera.FocusScreen(TerminalScreen);
        }

        public void RefreshViews()
        {
            if (Clock == null) return;
            if (Calendar)
            {
                int mask = 0;
                var repairs = Opening && Opening.Assembly ? Opening.Assembly.RepairScreen : null;
                // Intact systems before touchdown are not completed repair tasks.
                if (Opening && Opening.HasCrashed && repairs)
                    for (int i = 0; i < RepairScreenController.DeviceCount; i++)
                        if (repairs.GetState(i) == RepairDeviceState.Repaired) mask |= 1 << i;
                Calendar.RepairChecklistMask = mask;
                Calendar.DepartureConfirmed = DepartureAuthorized;
                Calendar.Render(visibleCalendarDays, Mathf.Min(visibleCalendarDays + 1, Clock.MissionDays),
                    WindowClosed && timeoutPresented);
            }
            if (Countdown)
                Countdown.Render(Clock.RemainingMissionSeconds, WindowClosed, DepartureAuthorized,
                    Clock.State == MissionState.Running && Clock.RemainingFraction <= .1);
        }

        void OnApplicationFocus(bool value) { focused = value; SynchronizePause(); }
        void OnApplicationPause(bool value) { applicationPaused = value; SynchronizePause(); }

        void OnDisable()
        {
            CancelCalendarPresentation(false);
            if (terminal)
            {
                terminal.ChoiceAccepted -= HandleChoice;
                terminal.Changed -= HandleTerminalChanged;
            }
            Clock?.SetPaused(true, Time.realtimeSinceStartupAsDouble);
            if (ownsTimeScale)
            {
                if (menu && menu.IsOpen) menu.SetPauseResumeTimeScale(previousTimeScale);
                else Time.timeScale = previousTimeScale;
            }
            ownsTimeScale = false;
            ApplyWorkBlock();
        }
    }
}
