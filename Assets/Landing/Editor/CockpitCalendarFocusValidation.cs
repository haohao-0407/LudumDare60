using System;
using System.IO;
using System.Reflection;
using System.Text;
using LudumDare60.ReturnWindow;
using LudumDare60.TerminalDialogue;
using LudumDare60.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LudumDare60.Landing.Editor
{
    /// <summary>Checks daily camera ownership and visible calendar strokes in the installed SHIP scene.</summary>
    [InitializeOnLoad]
    public static class CockpitCalendarFocusValidation
    {
        const string RunKey = "CockpitCalendarFocusValidation.Running";
        const string ReportPath = "Logs/CockpitMission/calendar-focus-validation.txt";
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly StringBuilder Report = new StringBuilder();
        static CockpitMissionController mission;
        static CockpitCameraController camera;
        static CockpitScreenTarget calendarScreen, previousScreen, countdownScreen;
        static TerminalDialogueController terminal;
        static EscMenuController menu;
        static int stage, assertions, lastFrame, frames, expectedDay;
        static double started, waitUntil, lastFrameAt, elapsedBefore, heldRemaining;
        static Vector3 freePosition, heldPosition;
        static Quaternion freeRotation, heldRotation;
        static float freeFov, heldStroke;

        static CockpitCalendarFocusValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Tools/Landing/Validate daily calendar camera in Play Mode")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before validating the daily calendar camera.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for compilation and asset import before validation.");
            StageUtility.GoToMainStage();
            EditorSceneManager.OpenScene(LandingSceneInstaller.ShipPath, OpenSceneMode.Single);
            Directory.CreateDirectory("Logs/CockpitMission");
            File.WriteAllText(ReportPath, "Starting daily calendar camera Play Mode validation.\n");
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
                mission = null;
                Report.Clear();
                stage = assertions = frames = 0;
                expectedDay = 4;
                lastFrame = -1;
                waitUntil = 0;
                started = lastFrameAt = EditorApplication.timeSinceStartup;
                Application.runInBackground = true;
                EditorApplication.update -= CheckRuntime;
                EditorApplication.update += CheckRuntime;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
                Finish(new InvalidOperationException("Play Mode ended before daily calendar validation completed."));
        }

        static void CheckRuntime()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - started > 150)
                    throw new TimeoutException("Daily calendar validation exceeded 150 seconds at stage " + stage + ".");
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
                    Require(mission && mission.Calendar && mission.Opening, "SHIP contains the installed mission and calendar.");
                    camera = mission.Opening.CockpitCamera;
                    terminal = mission.Opening.Terminal;
                    menu = camera.PauseMenu;
                    calendarScreen = mission.Calendar.GetComponent<CockpitScreenTarget>();
                    countdownScreen = mission.Countdown.GetComponent<CockpitScreenTarget>();
                    previousScreen = mission.TerminalScreen;
                    Require(camera && calendarScreen && previousScreen && countdownScreen && menu,
                        "Calendar, countdown, terminal and pause menu use the actual cockpit camera.");
                    // Landing and the accepted BEGIN REPAIRS choice have their own integration test.
                    // Isolate daily camera ownership here without saving any scene changes.
                    mission.Opening.StopAllCoroutines();
                    mission.Opening.enabled = false;
                    camera.RequireApplicationFocus = false;
                    terminal.GoToNode("operations_menu");
                    terminal.Close();
                    camera.ReturnToFree();
                    mission.SendMessage("OnApplicationFocus", true);
                    Require(mission.Clock.Start(Time.realtimeSinceStartupAsDouble), "Test work session starts once.");
                }
                if (mission.isActiveAndEnabled) mission.SendMessage("OnApplicationFocus", true);
                if (now < waitUntil) return;

                switch (stage)
                {
                    case 0:
                        if (!camera.IsFreeView) return;
                        Require(!mission.Clock.IsPaused && !mission.WorkBlocked,
                            "Ordinary work is active before a completed date.");
                        freePosition = camera.ViewCamera.transform.localPosition;
                        freeRotation = camera.ViewCamera.transform.localRotation;
                        freeFov = camera.ViewCamera.fieldOfView;
                        camera.TransitionDuration = 1.2f;
                        AdvanceMissionTo(60.01);
                        Next();
                        break;

                    case 1:
                        if (!mission.IsCalendarPresentationActive) return;
                        Require(camera.IsForcedFocus && camera.CurrentScreen == calendarScreen,
                            "A completed day forces the calendar view, never the countdown instrument.");
                        Require(!camera.IsForcedFocusSettled && !Field<bool[]>(mission.Calendar, "completed")[0],
                            "The first red cross waits until the camera arrives.");
                        camera.ReturnToFree();
                        camera.FocusScreen(countdownScreen);
                        Require(camera.IsForcedFocus && camera.CurrentScreen == calendarScreen,
                            "Manual back and screen selection cannot interrupt a daily cross.");
                        Require(mission.WorkBlocked && mission.Opening.Assembly.GameplayBlocked &&
                                mission.Opening.Salvage.GameplayBlocked,
                            "Assembly and salvage inputs are blocked during the camera presentation.");
                        Require(!mission.Clock.IsPaused && Time.timeScale > 0,
                            "Daily calendar focus does not pause mission time.");
                        elapsedBefore = mission.Clock.ElapsedSeconds;
                        Next(.25);
                        break;

                    case 2:
                        Require(mission.Clock.ElapsedSeconds > elapsedBefore,
                            "The deadline continues counting while the camera moves to the calendar.");
                        if (!mission.Calendar.IsCrossAnimationComplete(1)) return;
                        Require(camera.IsForcedFocusSettled && BothStrokesVisible(1),
                            "Both strokes of the first cross finish while the calendar remains in close-up.");
                        Next();
                        break;

                    case 3:
                        if (mission.IsCalendarPresentationActive || camera.IsForcedFocus) return;
                        Require(camera.IsFreeView && SameFreePose(),
                            "A daily close-up restores the exact prior free position, rotation and field of view.");
                        Require(!mission.WorkBlocked && !camera.FreeLook.InputBlocked,
                            "Work and free look resume after the daily presentation.");
                        camera.TransitionDuration = .2f;
                        camera.FocusScreen(previousScreen);
                        Next();
                        break;

                    case 4:
                        if (camera.State != CockpitViewState.Screen) return;
                        Require(camera.CurrentScreen == previousScreen, "The player begins the second day focused on another screen.");
                        camera.TransitionDuration = 1.2f;
                        SetField(mission.Calendar, "strokeDuration", .5f);
                        AdvanceMissionTo(120.01);
                        Next();
                        break;

                    case 5:
                        if (!mission.IsCalendarPresentationActive) return;
                        Require(camera.IsForcedFocus && !camera.IsForcedFocusSettled &&
                                !Field<bool[]>(mission.Calendar, "completed")[1],
                            "A focused screen is interrupted and the second cross still waits for arrival.");
                        menu.Open();
                        mission.SendMessage("Update");
                        Require(menu.IsOpen && mission.Clock.IsPaused,
                            "ESC can open the pause menu during a forced calendar transition.");
                        CapturePause(2);
                        Next(.25);
                        break;

                    case 6:
                        Require(PauseUnchanged(2) && camera.IsForcedFocus,
                            "ESC freezes the incoming camera, cross and countdown without abandoning the presentation.");
                        menu.Close();
                        Next();
                        break;

                    case 7:
                        float progress = StrokeProgress(2);
                        if (!camera.IsForcedFocusSettled || progress <= 0 || progress >= 2f) return;
                        menu.Open();
                        mission.SendMessage("Update");
                        CapturePause(2);
                        Next(.25);
                        break;

                    case 8:
                        Require(PauseUnchanged(2) && camera.IsForcedFocusSettled,
                            "ESC also freezes a partially drawn cross in the calendar close-up.");
                        menu.Close();
                        Next();
                        break;

                    case 9:
                        if (!mission.Calendar.IsCrossAnimationComplete(2)) return;
                        Require(camera.IsForcedFocusSettled && BothStrokesVisible(2),
                            "The second cross resumes and finishes visibly after closing ESC.");
                        Next();
                        break;

                    case 10:
                        if (mission.IsCalendarPresentationActive || camera.IsForcedFocus) return;
                        Require(camera.State == CockpitViewState.Screen && camera.CurrentScreen == previousScreen,
                            "The close-up returns to the player's previously focused screen.");
                        camera.ReturnToFree();
                        Next();
                        break;

                    case 11:
                        if (!camera.IsFreeView) return;
                        Require(SameFreePose(), "Returning from the restored screen still recovers the original free pose.");
                        typeof(CockpitMissionController).GetMethod("HandleWarning", PrivateInstance)
                            .Invoke(mission, new object[] { .5 });
                        Next(.25);
                        break;

                    case 12:
                        Require(camera.IsFreeView && !camera.IsForcedFocus && !mission.IsCalendarPresentationActive,
                            "A countdown warning alone never forces any camera target.");
                        camera.TransitionDuration = 1.2f;
                        AdvanceMissionTo(180.01);
                        Next();
                        break;

                    case 13:
                        if (!mission.IsCalendarPresentationActive) return;
                        mission.enabled = false;
                        Require(!camera.IsForcedFocus && !camera.FreeLook.InputBlocked &&
                                !mission.IsCalendarPresentationActive && mission.WorkBlocked,
                            "Disabling the mission during focus releases camera ownership and preserves the work gate.");
                        mission.enabled = true;
                        camera.TransitionDuration = .2f;
                        SetField(mission.Calendar, "strokeDuration", .1f);
                        Next();
                        break;

                    case 14:
                        if (mission.IsCalendarPresentationActive || camera.IsForcedFocus ||
                            !mission.Calendar.IsCrossAnimationComplete(3)) return;
                        Require(!mission.Clock.IsPaused && !mission.WorkBlocked,
                            "Re-enabling after interruption recovers the date and active work.");
                        AdvanceMissionTo(expectedDay * 60 + .01);
                        Next();
                        break;

                    case 15:
                        if (!mission.Calendar.IsCrossAnimationComplete(expectedDay)) return;
                        Require(camera.IsForcedFocusSettled && BothStrokesVisible(expectedDay),
                            "Day " + expectedDay + " completes its red cross visibly on the calendar.");
                        Next();
                        break;

                    case 16:
                        if (mission.IsCalendarPresentationActive || camera.IsForcedFocus) return;
                        if (++expectedDay < 20)
                        {
                            AdvanceMissionTo(expectedDay * 60 + .01);
                            stage = 15;
                            return;
                        }
                        AdvanceMissionTo(1200);
                        Next();
                        break;

                    case 17:
                        if (!mission.IsCalendarPresentationActive) return;
                        Require(mission.WindowClosed && mission.WorkBlocked &&
                                camera.IsForcedFocus && camera.CurrentScreen == calendarScreen,
                            "The exact deadline blocks work immediately and presents the final calendar date.");
                        Require(terminal.Session.CurrentNodeId != "another_tomorrow_open" &&
                                !Field<bool>(mission.Calendar, "sowing"),
                            "Timeout dialogue and the sowing page wait until the final cross is shown.");
                        Next();
                        break;

                    case 18:
                        if (!mission.Calendar.IsCrossAnimationComplete(20)) return;
                        Require(camera.IsForcedFocusSettled && BothStrokesVisible(20) &&
                                terminal.Session.CurrentNodeId != "another_tomorrow_open",
                            "The twentieth cross finishes visibly before the timeout terminal takes focus.");
                        Next();
                        break;

                    case 19:
                        if (mission.IsCalendarPresentationActive || camera.IsForcedFocus ||
                            camera.CurrentScreen != previousScreen || camera.State != CockpitViewState.Screen) return;
                        Require(terminal.Session.CurrentNodeId == "another_tomorrow_open" &&
                                Field<bool>(mission.Calendar, "sowing") && mission.WindowClosed,
                            "After the final cross, the authored timeout dialogue and sowing page appear normally.");
                        Require(camera.CurrentScreen != countdownScreen,
                            "The countdown instrument never receives a forced close-up.");
                        Finish(null);
                        break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        static void AdvanceMissionTo(double elapsed)
        {
            Require(!mission.Clock.IsPaused && mission.Clock.State == MissionState.Running,
                "Accelerated interval starts in active work.");
            double delta = elapsed - mission.Clock.ElapsedSeconds;
            if (delta < 0) throw new InvalidOperationException("The calendar test cannot rewind mission time.");
            double now = Time.realtimeSinceStartupAsDouble;
            SetField(mission.Clock, "lastTimestamp", now - delta - .0000001);
            mission.Clock.AdvanceTo(now);
            mission.RefreshViews();
        }

        static bool SameFreePose() =>
            Vector3.Distance(camera.ViewCamera.transform.localPosition, freePosition) < .0001f &&
            Quaternion.Angle(camera.ViewCamera.transform.localRotation, freeRotation) < .01f &&
            Mathf.Abs(camera.ViewCamera.fieldOfView - freeFov) < .001f;

        static bool BothStrokesVisible(int day) =>
            Field<LineRenderer[]>(mission.Calendar, "firstStrokes")[day - 1].enabled &&
            Field<LineRenderer[]>(mission.Calendar, "secondStrokes")[day - 1].enabled;

        static float StrokeProgress(int day) => Field<float[]>(mission.Calendar, "strokeProgress")[day - 1];
        static void CapturePause(int day)
        {
            heldPosition = camera.ViewCamera.transform.localPosition;
            heldRotation = camera.ViewCamera.transform.localRotation;
            heldStroke = StrokeProgress(day);
            heldRemaining = mission.Clock.RemainingSeconds;
        }

        static bool PauseUnchanged(int day) =>
            Vector3.Distance(heldPosition, camera.ViewCamera.transform.localPosition) < .0001f &&
            Quaternion.Angle(heldRotation, camera.ViewCamera.transform.localRotation) < .01f &&
            Mathf.Abs(heldStroke - StrokeProgress(day)) < .0001f &&
            Math.Abs(heldRemaining - mission.Clock.RemainingSeconds) < .00001;

        static T Field<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, PrivateInstance).GetValue(owner);
        static void SetField(object owner, string name, object value) =>
            owner.GetType().GetField(name, PrivateInstance).SetValue(owner, value);
        static void Next(double delay = 0) { stage++; waitUntil = EditorApplication.timeSinceStartup + delay; }

        static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            assertions++;
            Report.AppendLine("PASS " + description);
            File.WriteAllText(ReportPath, Report.ToString());
        }

        static void Finish(Exception error)
        {
            EditorApplication.update -= CheckRuntime;
            SessionState.SetBool(RunKey, false);
            if (menu && menu.IsOpen) menu.Close();
            Report.AppendLine(error == null
                ? "SUCCESS: " + assertions + " daily calendar camera assertions passed."
                : "FAIL at stage " + stage + ": " + error);
            Directory.CreateDirectory("Logs/CockpitMission");
            File.WriteAllText(ReportPath, Report.ToString());
            if (error == null) Debug.Log("[CockpitCalendarFocusValidation] " + assertions + " assertions passed.");
            else Debug.LogException(error);
            EditorApplication.isPaused = false;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
    }
}
