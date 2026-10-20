using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using LudumDare60.ReturnWindow;
using LudumDare60.TerminalDialogue;
using LudumDare60.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LudumDare60.Landing.Editor
{
    /// <summary>Exercises the installed SHIP mission against actual dialogue, landing and launch components.</summary>
    [InitializeOnLoad]
    public static class CockpitMissionValidation
    {
        const string RunKey = "CockpitMissionValidation.Running";
        const string SuccessKey = "CockpitMissionValidation.SuccessRun";
        static bool SuccessRun => SessionState.GetBool(SuccessKey, false);
        static string ReportPath => "Logs/CockpitMission/" + (SuccessRun ? "success-validation.txt" : "validation.txt");
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly StringBuilder Report = new StringBuilder();
        static readonly List<double> Warnings = new List<double>();
        static CockpitMissionController mission;
        static CockpitOpeningController opening;
        static CockpitDepartureController departure;
        static TerminalDialogueController terminal;
        static EscMenuController menu;
        static Func<bool> menuBlocker;
        static int stage, frames, lastFrame, checkIndex, warningIndex, assertions;
        static double started, waitUntil, lastFrameAt, heldRemaining, observedElapsed;
        static bool simulatingLostFocus;
        static string beforeWarning;
        static readonly double[] WarningIntervals = { 600.05, 900.05, 1080.05 };

        static CockpitMissionValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Tools/Landing/Validate SHIP mission clock in Play Mode")]
        public static void Run() => BeginRun(false);

        [MenuItem("Tools/Landing/Validate SHIP mission through orbit in Play Mode")]
        public static void RunSuccess() => BeginRun(true);

        static void BeginRun(bool success)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before validating the SHIP mission.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for compilation and asset import before validation.");
            StageUtility.GoToMainStage();
            EditorSceneManager.OpenScene(LandingSceneInstaller.ShipPath, OpenSceneMode.Single);
            Directory.CreateDirectory("Logs/CockpitMission");
            SessionState.SetBool(SuccessKey, success);
            File.WriteAllText(ReportPath, "Starting SHIP mission Play Mode validation.\n");
            SessionState.SetBool(RunKey, true);
            EditorApplication.isPaused = false;
            var gameView = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (gameView != null) EditorWindow.GetWindow(gameView).Focus();
            EditorApplication.isPlaying = true;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(RunKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                Report.Clear();
                Warnings.Clear();
                stage = frames = checkIndex = warningIndex = assertions = 0;
                lastFrame = -1;
                waitUntil = 0;
                started = lastFrameAt = EditorApplication.timeSinceStartup;
                simulatingLostFocus = false;
                Application.runInBackground = true;
                EditorApplication.update -= CheckRuntime;
                EditorApplication.update += CheckRuntime;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
                Finish(new InvalidOperationException("Play Mode ended before validation completed."));
        }

        static void CheckRuntime()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - started > 150)
                    throw new TimeoutException("SHIP mission validation exceeded 150 seconds at stage " + stage + ".");
                Application.runInBackground = true;
                EditorApplication.isPaused = false;
                EditorApplication.QueuePlayerLoopUpdate();
                if (Time.frameCount == lastFrame)
                {
                    if (now - lastFrameAt > .5)
                    {
                        EditorApplication.Step();
                        lastFrameAt = now;
                    }
                    return;
                }
                lastFrame = Time.frameCount;
                lastFrameAt = now;
                if (++frames < 12) return;
                if (!mission)
                {
                    mission = UnityEngine.Object.FindFirstObjectByType<CockpitMissionController>();
                    Require(mission, "Installed SHIP mission controller exists.");
                    opening = mission.Opening;
                    departure = mission.Departure;
                    terminal = opening.Terminal;
                    menu = opening.CockpitCamera.PauseMenu;
                    Require(terminal && terminal.Session != null && menu, "Mission terminal and ESC menu are initialized.");
                    menuBlocker = menu.OpenBlocker;
                    // The dedicated calendar validation covers production timing. Keep this broad
                    // scenario fast while still waiting for every real camera/stroke presentation.
                    opening.CockpitCamera.TransitionDuration = .05f;
                    opening.CockpitCamera.RequireApplicationFocus = false;
                    SetField(mission.Calendar, "strokeDuration", .05f);
                    mission.CalendarCrossHold = .02f;
                    mission.Clock.WarningReached += Warnings.Add;
                }
                // An unattended Game view can lose OS focus while the editor still runs frames.
                if (!simulatingLostFocus && mission.isActiveAndEnabled) mission.SendMessage("OnApplicationFocus", true);
                if (now < waitUntil) return;

                switch (stage)
                {
                    case 0:
                        Require(mission.Clock.State == MissionState.Ready, "Opening starts Ready, before mission authorization.");
                        Require(mission.Clock.RemainingSeconds == 1200 && mission.Clock.RemainingMissionSeconds == 20 * 86400,
                            "Default budget is 20 active minutes / 20 simulated days.");
                        Require(mission.Calendar.DayCount == 20 && mission.Calendar.StartDate == new DateTime(1981, 8, 25),
                            "Calendar starts 25 AUG 1981 and contains twenty dates.");
                        Require(mission.Calendar.RepairChecklistMask == 0, "Intact opening systems do not mark repairs complete.");
                        Require(Field<long>(mission.Countdown, "displayedMinute") == 20 * 1440,
                            "Physical instrument displays twenty days before work begins.");
                        Require(opening.Assembly.GameplayBlocked && opening.Salvage.GameplayBlocked,
                            "Work is unavailable before BEGIN REPAIRS.");
                        CheckInstrumentSelection();
                        Next(.3);
                        break;

                    case 1:
                        Require(mission.Clock.ElapsedSeconds == 0, "Opening story does not consume the mission budget.");
                        opening.Landing.Duration = 5;
                        opening.Landing.HardLandingShakeDuration = .1f;
                        opening.TutorialFocusHold = opening.TutorialAfterReturnDelay = 0;
                        opening.CloseTerminalDelay = 0;
                        opening.LookHintYaw = 0;
                        terminal.Open();
                        terminal.GoToNode("log_25_aug");
                        ReachChoices();
                        Require(terminal.Choose("begin_landing"), "Authored BEGIN LANDING choice is accepted.");
                        AdvanceUntil(() => opening.LandingStarted);
                        Require(!mission.Clock.IsPaused && Time.timeScale > 0,
                            "Landing presentation runs while mission clock remains Ready.");
                        Next();
                        break;

                    case 2:
                        if (!opening.Landing.HasLanded) return;
                        Require(opening.HasCrashed && mission.Clock.ElapsedSeconds == 0,
                            "Actual touchdown applies damage without starting mission time.");
                        opening.StopAllCoroutines();
                        terminal.Open();
                        terminal.GoToNode("briefing_12");
                        ReachChoices();
                        Require(terminal.Choose("briefing_reply_13"), "Authored BEGIN REPAIRS choice is accepted.");
                        Require(mission.Clock.State == MissionState.Running, "Mission starts at accepted BEGIN REPAIRS click.");
                        AdvanceUntil(() => terminal.Session.CurrentNodeId == "operations_menu");
                        Require(!terminal.IsStoryReading && !mission.WorkBlocked && Time.timeScale > 0,
                            "Operational menu releases story pause and work gates.");
                        observedElapsed = mission.Clock.ElapsedSeconds;
                        Next(.3);
                        break;

                    case 3:
                        Require(mission.Clock.ElapsedSeconds > observedElapsed, "Ordinary operations consume real active time.");
                        terminal.GoToNode("briefing_09");
                        Require(mission.Clock.IsPaused && Time.timeScale == 0, "Story reading pauses mission and simulation.");
                        Require(opening.Assembly.GameplayBlocked && opening.Salvage.GameplayBlocked,
                            "Story reading blocks assembly and salvage input.");
                        heldRemaining = mission.Clock.RemainingSeconds;
                        Next(.3);
                        break;

                    case 4:
                        Require(Near(mission.Clock.RemainingSeconds, heldRemaining), "Story-reading wall time consumes no budget.");
                        menu.OpenBlocker = null;
                        menu.Open();
                        terminal.Close();
                        Require(menu.IsOpen && Time.timeScale == 0, "ESC remains paused after overlapping story closes.");
                        Next(.25);
                        break;

                    case 5:
                        Require(Near(mission.Clock.RemainingSeconds, heldRemaining), "Overlapping ESC pause consumes no budget.");
                        menu.Close();
                        Next(.25);
                        break;

                    case 6:
                        Require(Time.timeScale > 0 && !mission.Clock.IsPaused && mission.Clock.RemainingSeconds < heldRemaining,
                            "Closing overlapping ESC restores gameplay and counting.");
                        menu.Open();
                        terminal.Open();
                        terminal.GoToNode("briefing_09");
                        menu.Close();
                        mission.SendMessage("Update");
                        Require(Time.timeScale == 0 && mission.Clock.IsPaused, "Closing ESC preserves an active story pause.");
                        terminal.Close();
                        Require(Time.timeScale > 0 && !mission.Clock.IsPaused, "Closing story after ESC releases the final pause.");
                        simulatingLostFocus = true;
                        mission.SendMessage("OnApplicationFocus", false);
                        heldRemaining = mission.Clock.RemainingSeconds;
                        Next(.25);
                        break;

                    case 7:
                        Require(mission.Clock.IsPaused && Near(mission.Clock.RemainingSeconds, heldRemaining),
                            "Application focus loss freezes the mission budget.");
                        simulatingLostFocus = false;
                        mission.SendMessage("OnApplicationFocus", true);
                        Require(!mission.Clock.IsPaused && Time.timeScale > 0, "Regaining focus resumes work.");
                        terminal.GoToNode("operations_menu");
                        terminal.Close();
                        AdvanceMissionTo(59);
                        Require(mission.Clock.CompletedDays == 0, "Calendar does not cross out a day before its end.");
                        AdvanceMissionTo(60.05);
                        mission.RefreshViews();
                        Require(mission.Clock.CompletedDays == 1 && mission.Clock.CurrentDay == 2,
                            "Sixty active seconds complete exactly one simulated day.");
                        SetStage(30);
                        break;

                    case 30:
                        if (!CalendarPresentationFinished(1)) return;
                        Require(Field<TMP_Text>(mission.Calendar, "currentDayLabel").text.Contains("26 AUG 1981"),
                            "Calendar advances to 26 AUG 1981.");
                        var completed = Field<bool[]>(mission.Calendar, "completed");
                        Require(completed[0] && completed.Skip(1).All(value => !value),
                            "Only the completed date receives a red cross.");
                        var circle = Field<Transform>(mission.Calendar, "lastDayCircle");
                        Require(Vector3.Distance(circle.localPosition, ReturnCalendarView.CellPosition(19) + new Vector3(0, 0, .013f)) < .0001f,
                            "The sole deadline circle remains on 13 SEP 1981.");
                        var repairs = opening.Assembly.RepairScreen;
                        Require(repairs.CompleteRepair(3), "Life support can publish an independent completed repair.");
                        mission.RefreshViews();
                        Require(mission.Calendar.RepairChecklistMask == 8, "Calendar marks the actual life-support repair only.");
                        var ticks = Field<GameObject[]>(mission.Calendar, "checklistTicks");
                        Require(ticks.Length == 4 && ticks[3].activeSelf && ticks.Take(3).All(tick => !tick.activeSelf),
                            "Four physical checklist ticks follow their corresponding device states.");
                        repairs.SetState(3, RepairDeviceState.Unrepaired);
                        mission.RefreshViews();
                        Require(mission.Calendar.RepairChecklistMask == 0, "Lost repair verification removes its checklist mark.");
                        SetStage(13, .6);
                        break;

                    case 8:
                        Require(Field<LineRenderer[]>(mission.Calendar, "firstStrokes")[0].enabled &&
                                Field<LineRenderer[]>(mission.Calendar, "secondStrokes")[0].enabled,
                            "Completed date animates both strokes of its physical red cross.");
                        beforeWarning = terminal.Session.CurrentNodeId;
                        warningIndex = 0;
                        AdvanceMissionTo(WarningIntervals[warningIndex]);
                        SetStage(31);
                        break;

                    case 31:
                        if (!CalendarPresentationFinished(mission.Clock.CompletedDays)) return;
                        if (++warningIndex < WarningIntervals.Length)
                        {
                            AdvanceMissionTo(WarningIntervals[warningIndex]);
                            break;
                        }
                        Require(Warnings.SequenceEqual(new[] { .5, .25, .1 }), "50%, 25% and 10% warnings each fire once.");
                        Require(!terminal.Session.IsOpen && terminal.Session.CurrentNodeId == beforeWarning &&
                                !mission.Clock.IsPaused && Time.timeScale > 0,
                            "Warnings neither open/navigate the terminal nor pause work.");
                        for (int i = 0; i < RepairScreenController.DeviceCount; i++)
                            Require(opening.Assembly.RepairScreen.CompleteRepair(i), "Repair snapshot completes device " + i + ".");
                        mission.RefreshViews();
                        Require(mission.Calendar.RepairChecklistMask == 15, "All four verified repairs are reflected on the calendar.");
                        SetStage(9);
                        break;

                    case 9:
                        Require(!departure.RequestLaunch() && !mission.DepartureAuthorized,
                            "A fully repaired ship cannot launch before its real pre-flight checks.");
                        departure.CheckDuration = departure.EngineTestDuration = .5f;
                        terminal.Open();
                        terminal.GoToNode("departure_ready");
                        ReachChoices();
                        Require(terminal.Choose("run_preflight"), "Authored pre-flight sequence begins.");
                        AdvanceUntil(() => terminal.Session.CurrentNodeId == "departure_power");
                        ReachChoices();
                        AdvanceMissionTo(1140.05);
                        SetStage(33);
                        break;

                    case 33:
                        if (!mission.IsCalendarPresentationActive || !opening.CockpitCamera.IsForcedFocusSettled) return;
                        Require(mission.WorkBlocked && !mission.Clock.IsPaused,
                            "Daily calendar presentation blocks manual work while accepted commands may still complete.");
                        BeginCurrentCheck();
                        Require(mission.IsCalendarPresentationActive && departure.IsChecking,
                            "Accepted power-check command starts its actual diagnostic during the forced calendar close-up.");
                        SetStage(10, .65);
                        break;

                    case 10:
                        if (departure.IsChecking) return;
                        checkIndex++;
                        if (checkIndex < 4)
                        {
                            BeginCurrentCheck();
                            waitUntil = now + .65;
                            break;
                        }
                        Require(departure.PreflightComplete, "All four real timed pre-flight checks pass.");
                        Require(terminal.Session.CurrentNodeId == "departure_launch_ready", "Pre-flight ends at the authored launch choice.");
                        ReachChoices();
                        AdvanceMissionTo(mission.Clock.DurationSeconds - .75);
                        Require(terminal.Choose("launch"), "LAUNCH choice is accepted within the final active second.");
                        Require(mission.DepartureAuthorized && departure.Launch.IsLaunching && mission.Clock.RemainingSeconds > 0,
                            "Accepted click freezes a positive deadline and starts the real launch sequence.");
                        Require(terminal.Session.CurrentNodeId == "launch_countdown" && Time.timeScale > 0,
                            "Immediate authorization enters launch countdown without freezing the flight.");
                        Require(!mission.IsCalendarPresentationActive && !opening.CockpitCamera.IsForcedFocus,
                            "Accepted launch cancels a pending daily close-up and releases the camera for flight.");
                        heldRemaining = mission.Clock.RemainingSeconds;
                        Next(1.1);
                        break;

                    case 11:
                        Require(mission.DepartureAuthorized && Near(mission.Clock.RemainingSeconds, heldRemaining),
                            "Flight presentation does not consume the already-authorized mission budget.");
                        Require(departure.Launch.IsLaunching && departure.Launch.Progress > 0,
                            "Real launch presentation advances after mission authorization.");
                        if (SuccessRun)
                        {
                            SetStage(20);
                            break;
                        }
                        departure.Launch.CancelLaunch();
                        Require(mission.Clock.State == MissionState.Running && !departure.PreflightComplete,
                            "Cancelled launch resumes the same run and invalidates pre-flight checks.");
                        Require(Math.Abs(mission.Clock.RemainingSeconds - heldRemaining) < .1,
                            "Cancellation neither refills the budget nor charges the flight interval.");
                        AdvanceMissionTo(mission.Clock.DurationSeconds);
                        Require(mission.WindowClosed && mission.Clock.RemainingSeconds == 0, "Deadline expiry settles exactly at zero.");
                        Require(!departure.RequestLaunch() && !departure.Launch.IsLaunching, "Expired launch request is rejected.");
                        Require(!opening.Assembly.RepairsAvailable && !opening.Salvage.FeatureEnabled &&
                                opening.Assembly.GameplayBlocked && opening.Salvage.GameplayBlocked,
                            "Expiry closes salvage and repair work.");
                        Require(Field<long>(mission.Countdown, "displayedMinute") == 0 &&
                                Field<int>(mission.Countdown, "displayedState") == 2,
                            "Physical countdown changes to WINDOW CLOSED.");
                        SetStage(32);
                        break;

                    case 32:
                        if (!CalendarPresentationFinished(20)) return;
                        Require(terminal.Session.IsOpen && terminal.Session.CurrentNodeId == CockpitMissionStory.ExpiredEntryNodeId,
                            "Expiry opens the authored Another Tomorrow ending after the final calendar cross.");
                        Require(Field<TMP_Text>(mission.Calendar, "currentDayLabel").text.Contains("14 SEP 1981 / SOW") &&
                                Field<bool[]>(mission.Calendar, "completed").All(value => value),
                            "Calendar retains all twenty crossed dates and displays the sowing epilogue.");
                        SetStage(12, .25);
                        break;

                    case 12:
                        Require(terminal.Session.CurrentNodeId == CockpitMissionStory.ExpiredEntryNodeId,
                            "Departure updates do not overwrite the timeout ending.");
                        CompleteEnding();
                        Require(terminal.Session.CurrentNodeId == "another_tomorrow_07" &&
                                terminal.Session.Phase == TerminalPhase.Completed,
                            "All existing timeout dialogue choices reach the authored final page.");
                        Require(!departure.RequestLaunch() && mission.WindowClosed, "Completed epilogue cannot reopen departure.");
                        Finish(null);
                        break;

                    case 13:
                        mission.enabled = false;
                        heldRemaining = mission.Clock.RemainingSeconds;
                        Require(mission.WorkBlocked && mission.Clock.IsPaused &&
                                opening.Assembly.GameplayBlocked && opening.Salvage.GameplayBlocked,
                            "Disabling the mission during ordinary work preserves input and clock gates.");
                        Next(.3);
                        break;

                    case 14:
                        Require(Near(mission.Clock.RemainingSeconds, heldRemaining),
                            "Disabled mission does not spend its retained budget.");
                        mission.enabled = true;
                        Require(!mission.WorkBlocked && Time.timeScale > 0,
                            "Re-enabling an active mission restores its work gates.");
                        Next(.3);
                        break;

                    case 15:
                        Require(mission.Clock.RemainingSeconds < heldRemaining,
                            "Re-enabled mission resumes counting without restarting its budget.");
                        terminal.Open();
                        terminal.GoToNode("briefing_09");
                        menu.Open();
                        Require(menu.IsOpen && mission.Clock.IsPaused && Time.timeScale == 0,
                            "Story and ESC jointly own the pause before mission disable.");
                        mission.enabled = false;
                        terminal.Close();
                        heldRemaining = mission.Clock.RemainingSeconds;
                        Require(menu.IsOpen && Time.timeScale == 0 && mission.WorkBlocked,
                            "Disabling during story and ESC keeps the open menu paused.");
                        Next(.25);
                        break;

                    case 16:
                        Require(Near(mission.Clock.RemainingSeconds, heldRemaining),
                            "Disabled overlapping story/ESC pause preserves mission time.");
                        menu.Close();
                        Require(Time.timeScale > 0 && mission.WorkBlocked && mission.Clock.IsPaused,
                            "ESC closes to its restored timeScale baseline while disabled work remains gated.");
                        mission.enabled = true;
                        Require(!mission.WorkBlocked && !mission.Clock.IsPaused && Time.timeScale > 0,
                            "Re-enabling after overlapping pauses leaves no stuck global pause.");
                        Next(.25);
                        break;

                    case 17:
                        Require(mission.Clock.RemainingSeconds < heldRemaining,
                            "Work resumes after mission disable, story close and ESC close.");
                        terminal.GoToNode("operations_menu");
                        terminal.Close();
                        SetStage(8);
                        break;

                    case 20:
                        if (!Near(mission.Clock.RemainingSeconds, heldRemaining))
                            throw new InvalidOperationException("The authorized deadline changed during the ascent.");
                        if (!departure.Launch.HasReachedOrbit) return;
                        Require(mission.DepartureAuthorized && !mission.WindowClosed,
                            "The full production flight reaches orbit after the original deadline has passed.");
                        Require(!departure.Launch.IsLaunching && departure.Launch.Progress >= 1f,
                            "Launch sequence completes rather than stopping at an intermediate flight phase.");
                        Require(mission.Calendar.DepartureConfirmed && mission.Calendar.RepairChecklistMask == 15 &&
                                Field<int>(mission.Countdown, "displayedState") == 1,
                            "Orbit retains the authorized countdown and all four repair marks.");
                        Require(opening.Assembly.GameplayBlocked && opening.Salvage.GameplayBlocked,
                            "Surface work stays blocked after successful departure.");
                        Require(terminal.Session.CurrentNodeId == "launch_orbit",
                            "Real orbit event selects the authored return-flight dialogue.");
                        terminal.Open();
                        AdvanceUntil(() => terminal.Session.Phase == TerminalPhase.Completed);
                        SetStage(21, .25);
                        break;

                    case 21:
                        var ending = UnityEngine.Object.FindFirstObjectByType<OrbitEndingUI>();
                        Require(ending && Field<bool>(ending, "started"),
                            "Finishing the orbit dialogue starts the existing Thanks and Credits presentation.");
                        var theatre = UnityEngine.Object.FindAnyObjectByType<AnotherTomorrowEndingUI>();
                        Require(theatre && !theatre.HasStarted && theatre.BlackoutGroup.alpha == 0f,
                            "Successful return never starts the timeout theatre ending or covers the credits.");
                        Require(Time.timeScale > 0 && mission.DepartureAuthorized &&
                                Near(mission.Clock.RemainingSeconds, heldRemaining),
                            "Successful ending remains animated with its accepted departure time preserved.");
                        Finish(null);
                        break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        static void CheckInstrumentSelection()
        {
            var controller = opening.CockpitCamera;
            var camera = controller.ViewCamera;
            var rotation = camera.transform.rotation;
            try
            {
                camera.transform.rotation = rotation * Quaternion.Euler(0, 40, 0);
                Physics.SyncTransforms();
                foreach (var view in new Component[] { mission.Calendar, mission.Countdown })
                {
                    var target = view.GetComponent<CockpitScreenTarget>();
                    Require(target && controller.Screens.Contains(target) && target.ViewPoint,
                        view.name + " has a registered close-up view.");
                    var point = camera.WorldToScreenPoint(target.HitCollider.bounds.center);
                    Require(point.z > 0 && controller.TryPickScreen(point, out var picked) && picked == target,
                        view.name + " is clickable from the seated view looking right.");
                }
            }
            finally { camera.transform.rotation = rotation; }
        }

        static void BeginCurrentCheck()
        {
            string[] choices = { "check_power", "check_navigation", "check_propulsion", "check_life_support" };
            ReachChoices();
            Require(terminal.Choose(choices[checkIndex]), "Pre-flight choice accepted: " + choices[checkIndex] + ".");
            AdvanceUntil(() => departure.IsChecking);
            Require(departure.IsChecking, "Actual timed pre-flight check is running.");
        }

        static void CompleteEnding()
        {
            for (int i = 0; i < 400 && terminal.Session.Phase != TerminalPhase.Completed; i++)
            {
                if (terminal.Session.Phase == TerminalPhase.WaitingForChoice)
                {
                    var choice = terminal.Session.Choices.FirstOrDefault();
                    Require(choice != null && terminal.Choose(choice.Id), "Authored timeout choice advances.");
                }
                else terminal.Advance();
            }
        }

        static void ReachChoices() => AdvanceUntil(() => terminal.Session.Phase == TerminalPhase.WaitingForChoice);

        static void AdvanceUntil(Func<bool> done)
        {
            for (int i = 0; i < 200 && !done(); i++) terminal.Advance();
            if (!done()) throw new InvalidOperationException("Dialogue did not reach its expected state from " + terminal.Session.CurrentNodeId + ".");
        }

        static void AdvanceMissionTo(double elapsed)
        {
            Require(!mission.Clock.IsPaused && mission.Clock.State == MissionState.Running,
                "Accelerated test interval begins in active work.");
            double delta = elapsed - mission.Clock.ElapsedSeconds;
            if (delta < 0) throw new InvalidOperationException("Validation cannot rewind mission time.");
            double now = Time.realtimeSinceStartupAsDouble;
            // Only the test re-anchors the previous sample. Production keeps real monotonic time,
            // and no future timestamp is ever introduced into later real-frame updates.
            SetField(mission.Clock, "lastTimestamp", now - delta - .0000001);
            mission.Clock.AdvanceTo(now);
            mission.RefreshViews();
        }

        static bool CalendarPresentationFinished(int completedDays) =>
            !mission.IsCalendarPresentationActive &&
            Field<bool[]>(mission.Calendar, "completed").Take(completedDays).All(value => value);

        static void Next(double delay = 0) { stage++; waitUntil = EditorApplication.timeSinceStartup + delay; }
        static void SetStage(int value, double delay = 0) { stage = value; waitUntil = EditorApplication.timeSinceStartup + delay; }
        static bool Near(double a, double b) => Math.Abs(a - b) < .00001;
        static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, PrivateInstance).GetValue(owner);
        static void SetField(object owner, string name, object value) => owner.GetType().GetField(name, PrivateInstance).SetValue(owner, value);

        static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            assertions++;
            Report.AppendLine("PASS " + description);
            Directory.CreateDirectory("Logs/CockpitMission");
            File.WriteAllText(ReportPath, Report.ToString());
        }

        static void Finish(Exception error)
        {
            EditorApplication.update -= CheckRuntime;
            SessionState.SetBool(RunKey, false);
            if (mission?.Clock != null) mission.Clock.WarningReached -= Warnings.Add;
            if (menu) menu.OpenBlocker = menuBlocker;
            if (error == null)
                Report.AppendLine("SUCCESS: " + assertions + " SHIP mission assertions passed.");
            else
                Report.AppendLine("FAIL at stage " + stage + ": " + error);
            Directory.CreateDirectory("Logs/CockpitMission");
            File.WriteAllText(ReportPath, Report.ToString());
            if (error == null) Debug.Log("[CockpitMissionValidation] " + assertions + " assertions passed.");
            else Debug.LogException(error);
            EditorApplication.isPaused = false;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
    }
}
