using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using LudumDare60.ReturnWindow;
using LudumDare60.TerminalDialogue;
using LudumDare60.UI;
using Pioneer60.Localization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LudumDare60.Landing.Editor
{
    /// <summary>Exercises the installed theatre ending through real expiry, dialogue and scene reload.</summary>
    [InitializeOnLoad]
    public static class AnotherTomorrowEndingValidation
    {
        const string RunKey = "AnotherTomorrowEndingValidation.Running";
        const string ReportPath = "Logs/CockpitMission/theatre-ending-validation.txt";
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly StringBuilder Report = new StringBuilder();
        static CockpitMissionController mission;
        static AnotherTomorrowEndingUI ending;
        static AnotherTomorrowDefinition testScript;
        static CockpitCameraController camera;
        static TerminalDialogueController terminal;
        static EscMenuController menu;
        static int stage, assertions, frames, lastFrame, firstCardScreenshotFrame;
        static double started, waitUntil, lastFrameAt;
        static float heldElapsed;
        static Vector3 heldPosition;
        static Quaternion heldRotation;
        static string originalLanguage;
        static bool checkedNewCard;
        static bool capturedFirstCard;

        static AnotherTomorrowEndingValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Tools/Landing/Validate Another Tomorrow theatre ending in Play Mode")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before theatre ending validation.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for compilation and asset import before validation.");
            StageUtility.GoToMainStage();
            EditorSceneManager.OpenScene(LandingSceneInstaller.ShipPath, OpenSceneMode.Single);
            Directory.CreateDirectory("Logs/CockpitMission");
            File.WriteAllText(ReportPath, "Starting Another Tomorrow theatre ending Play Mode validation.\n");
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
                ending = null;
                testScript = null;
                originalLanguage = null;
                checkedNewCard = false;
                capturedFirstCard = false;
                Report.Clear();
                stage = assertions = frames = 0;
                lastFrame = -1;
                waitUntil = 0;
                started = lastFrameAt = EditorApplication.timeSinceStartup;
                Application.runInBackground = true;
                EditorApplication.update -= CheckRuntime;
                EditorApplication.update += CheckRuntime;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
                Finish(new InvalidOperationException("Play Mode ended before theatre ending validation completed."));
        }

        static void CheckRuntime()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - started > 150)
                    throw new TimeoutException("Theatre ending validation exceeded 150 seconds at stage " + stage + ".");
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
                if (stage < 10 && !mission) Prepare();
                if (stage < 10 && mission.isActiveAndEnabled)
                    mission.SendMessage("OnApplicationFocus", true);
                if (now < waitUntil) return;

                switch (stage)
                {
                    case 0:
                        if (!camera.IsFreeView) return;
                        Require(!ending.HasStarted && ending.BlackoutGroup.alpha == 0f,
                            "A fresh SHIP run has no ending overlay.");
                        // A dialogue node alone must never turn a live or successful run into this ending.
                        terminal.Open();
                        Require(terminal.GoToNode("another_tomorrow_07"), "The authored final epilogue node exists.");
                        AdvanceUntil(() => terminal.Session.Phase == TerminalPhase.Completed);
                        Require(!ending.HasStarted, "A completed epilogue node cannot trigger without a closed return window.");
                        terminal.GoToNode("operations_menu");
                        terminal.Close();
                        Require(mission.Clock.Start(Time.realtimeSinceStartupAsDouble), "The isolated work clock starts.");
                        Require(!mission.Clock.IsPaused, "Work clock is active before accelerated expiry.");
                        SetField(mission.Clock, "lastTimestamp",
                            Time.realtimeSinceStartupAsDouble - mission.ActiveWorkSeconds - 1);
                        mission.Clock.AdvanceTo(Time.realtimeSinceStartupAsDouble);
                        Require(mission.WindowClosed && !ending.HasStarted,
                            "Expiry queues the final calendar and dialogue without an early blackout.");
                        Next();
                        break;

                    case 1:
                        if (mission.IsCalendarPresentationActive ||
                            terminal.Session.CurrentNodeId != "another_tomorrow_open") return;
                        Require(!ending.HasStarted && mission.Calendar.IsCrossAnimationComplete(20),
                            "All twenty date crosses finish before the terminal epilogue; screen remains visible.");
                        var finalLine = terminal.Conversation.Graph.FindNode("another_tomorrow_07").Lines.Last().Text;
                        AdvanceUntil(() => terminal.Session.CurrentNodeId == "another_tomorrow_07" &&
                            terminal.Session.Phase == TerminalPhase.WaitingForContinue &&
                            terminal.Session.Messages.Last().Text == finalLine);
                        Require(!ending.HasStarted, "The last terminal line remains readable until the player acknowledges it.");
                        terminal.Advance();
                        Require(terminal.Session.Phase == TerminalPhase.Completed && ending.HasStarted,
                            "Acknowledging the final terminal line starts the black-screen ending once.");
                        ending.AdvanceCard();
                        Require(ending.CardIndex == -1,
                            "The last dialogue acknowledgement cannot skip the blackout or first act.");
                        Require(camera.PresentationBlocked && camera.FreeLook.InputBlocked,
                            "The ending captures camera docking and free-look input.");
                        Require(mission.WorkBlocked && mission.Opening.Assembly.GameplayBlocked &&
                                mission.Opening.Salvage.GameplayBlocked,
                            "Assembly and salvage stay blocked behind the ending.");
                        heldPosition = camera.ViewCamera.transform.localPosition;
                        heldRotation = camera.ViewCamera.transform.localRotation;
                        Next();
                        break;

                    case 2:
                        if (ending.CardIndex != 0 || ending.CardGroup.alpha < .999f) return;
                        if (!checkedNewCard)
                        {
                            ending.AdvanceCard();
                            Require(Field<object>(ending, "stage").ToString() == "Hold",
                                "A newly revealed act cannot be skipped before its minimum reading hold.");
                            checkedNewCard = true;
                        }
                        if (Field<float>(ending, "stageTime") < .8f) return;
                        if (!capturedFirstCard)
                        {
                            ScreenCapture.CaptureScreenshot("Logs/CockpitMission/theatre-act-one-en.png");
                            capturedFirstCard = true;
                            firstCardScreenshotFrame = Time.frameCount;
                            waitUntil = now + .3;
                            return;
                        }
                        if (Time.frameCount < firstCardScreenshotFrame + 2) return;
                        Require(ending.BlackoutGroup.alpha == 1f && ending.BlackoutGroup.blocksRaycasts,
                            "The first act has an opaque black backdrop that intercepts pointer input.");
                        Require(ending.BlackoutGroup.GetComponentsInChildren<Image>(true)
                            .Any(image => image.color.r < .01f && image.color.g < .01f &&
                                          image.color.b < .01f && image.color.a > .99f),
                            "The actual backdrop graphic is solid black.");
                        VerifyCard(0, false);
                        var hits = new List<RaycastResult>();
                        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current)
                        { position = new Vector2(Screen.width * .5f, Screen.height * .5f) }, hits);
                        Require(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(ending.transform),
                            "A central pointer ray hits the ending instead of cabin controls.");
                        Require(!EventSystem.current.currentSelectedGameObject,
                            "A previous terminal button cannot receive keyboard submit behind the blackout.");
                        camera.ReturnToFree();
                        camera.FocusScreen(mission.Countdown.GetComponent<CockpitScreenTarget>());
                        Require(Vector3.Distance(heldPosition, camera.ViewCamera.transform.localPosition) < .0001f &&
                                Quaternion.Angle(heldRotation, camera.ViewCamera.transform.localRotation) < .01f,
                            "Manual camera requests cannot move the view behind the ending.");
                        menu.Open();
                        Require(menu.IsOpen, "ESC menu can open while the terminal view is captured by the ending.");
                        var overlay = ending.GetComponentInParent<Canvas>();
                        Require(overlay && overlay.sortingOrder < 2000,
                            "Ending canvas is below the existing ESC canvas at order 2000.");
                        heldElapsed = ending.Elapsed;
                        Next(.25);
                        break;

                    case 3:
                        Require(Near(heldElapsed, ending.Elapsed), "ESC pauses the theatre timeline.");
                        ending.AdvanceCard();
                        Require(ending.CardIndex == 0, "Advance input cannot skip a card while ESC is open.");
                        LocalizationManager.Instance.SetLanguage("zh-Hans", false);
                        VerifyCard(0, true);
                        Require(Near(heldElapsed, ending.Elapsed), "Changing language preserves timeline and card progress.");
                        menu.Close();
                        ending.AdvanceCard();
                        Require(Field<object>(ending, "stage").ToString() == "Hold",
                            "The resume click cannot advance an otherwise skippable act in the same frame.");
                        ending.SendMessage("OnApplicationPause", true);
                        heldElapsed = ending.Elapsed;
                        Next(.25);
                        break;

                    case 4:
                        Require(Near(heldElapsed, ending.Elapsed), "Application suspension pauses the theatre timeline.");
                        ending.SendMessage("OnApplicationPause", false);
                        ending.RequireApplicationFocus = true;
                        ending.SendMessage("OnApplicationFocus", false);
                        heldElapsed = ending.Elapsed;
                        Next(.25);
                        break;

                    case 5:
                        Require(Near(heldElapsed, ending.Elapsed), "Lost application focus pauses the theatre timeline.");
                        ending.SendMessage("OnApplicationFocus", true);
                        ending.RequireApplicationFocus = false;
                        LocalizationManager.Instance.SetLanguage("en", false);
                        VerifyCard(0, false);
                        Next(1);
                        break;

                    case 6:
                        if (Field<float>(ending, "stageTime") < .8f) return;
                        Require(ending.Elapsed > heldElapsed, "Closing pauses resumes the theatre timeline despite story timeScale zero.");
                        ending.AdvanceCard();
                        Next();
                        break;

                    case 7:
                        if (ending.CardIndex != 1 || ending.CardGroup.alpha < .999f) return;
                        VerifyCard(1, false);
                        // Let this act advance automatically, covering the authored timed path too.
                        testScript.Cards[1].HoldSeconds = .9f;
                        Next();
                        break;

                    case 8:
                        if (ending.CardIndex != 2 || ending.CardGroup.alpha < .999f) return;
                        VerifyCard(2, false);
                        testScript.Cards[2].HoldSeconds = .9f;
                        Next();
                        break;

                    case 9:
                        if (!ending.IsComplete || ending.ActionsGroup.alpha < .999f) return;
                        VerifyCard(3, false);
                        Require(ending.CardIndex == 3 && ending.BlackoutGroup.alpha == 1f &&
                                ending.ActionsGroup.interactable && ending.RestartButton.IsInteractable(),
                            "Final curtain remains on black with a usable restart action.");
                        Require(camera.PresentationBlocked && camera.FreeLook.InputBlocked &&
                                mission.Clock.State == MissionState.Stayed,
                            "Final curtain keeps cabin input locked and the failed mission closed.");
                        ScreenCapture.CaptureScreenshot("Logs/CockpitMission/theatre-final-en.png");
                        stage = 11;
                        waitUntil = now + .3;
                        break;

                    case 11:
                        LocalizationManager.Instance.SetLanguage("zh-Hans", false);
                        VerifyCard(3, true);
                        Require(ending.RestartText.text == "再次启程", "Final restart action switches to Chinese.");
                        ScreenCapture.CaptureScreenshot("Logs/CockpitMission/theatre-final-zh.png");
                        stage = 12;
                        waitUntil = now + .3;
                        break;

                    case 12:
                        ending.Restart();
                        stage = 10;
                        break;

                    case 10:
                        var reloadedMission = UnityEngine.Object.FindAnyObjectByType<CockpitMissionController>();
                        var reloadedEnding = UnityEngine.Object.FindAnyObjectByType<AnotherTomorrowEndingUI>();
                        if (!reloadedMission || !reloadedEnding || ReferenceEquals(reloadedMission, mission)) return;
                        Require(!reloadedEnding.HasStarted && !reloadedEnding.IsComplete && reloadedEnding.CardIndex == -1,
                            "Restart loads a fresh SHIP scene with all theatre state reset.");
                        Require(reloadedEnding.BlackoutGroup.alpha == 0f &&
                                !reloadedEnding.BlackoutGroup.blocksRaycasts,
                            "The restarted scene removes the black screen and its input blocker.");
                        Require(!reloadedMission.WindowClosed && !reloadedMission.Opening.CockpitCamera.PresentationBlocked,
                            "Restart clears the expired mission and camera presentation lock.");
                        Require(reloadedMission.Clock.State == MissionState.Ready,
                            "The restarted mission waits for the normal story to begin repairs.");
                        Finish(null);
                        break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        static void Prepare()
        {
            mission = UnityEngine.Object.FindAnyObjectByType<CockpitMissionController>();
            ending = UnityEngine.Object.FindAnyObjectByType<AnotherTomorrowEndingUI>();
            Require(mission && ending && ending.Script && ending.Script.Cards.Length == 4,
                "SHIP contains the mission and an authored four-act theatre ending.");
            camera = mission.Opening.CockpitCamera;
            terminal = mission.Opening.Terminal;
            menu = camera.PauseMenu;
            Require(ending.Mission == mission && ending.Terminal == terminal &&
                    ending.CockpitCamera == camera && ending.PauseMenu == menu,
                "The theatre ending is bound to the actual SHIP mission, terminal, camera and menu.");
            originalLanguage = Loc.Language;
            LocalizationManager.Instance.SetLanguage("en", false);
            mission.Opening.StopAllCoroutines();
            mission.Opening.enabled = false;
            camera.RequireApplicationFocus = false;
            camera.TransitionDuration = .05f;
            mission.CalendarCrossHold = .01f;
            SetField(mission.Calendar, "strokeDuration", .05f);
            ending.RequireApplicationFocus = false;
            testScript = UnityEngine.Object.Instantiate(ending.Script);
            testScript.BlackoutSeconds = .2f;
            testScript.TextFadeSeconds = .15f;
            testScript.BetweenCardsSeconds = .1f;
            foreach (var card in testScript.Cards) card.HoldSeconds = 30f;
            ending.Script = testScript;
            terminal.GoToNode("operations_menu");
            terminal.Close();
            camera.ReturnToFree();
            mission.SendMessage("OnApplicationFocus", true);
        }

        static void VerifyCard(int index, bool chinese)
        {
            var card = testScript.Cards[index];
            Require(ending.CardIndex == index &&
                    ending.CueText.text == (chinese ? card.ChineseCue : card.EnglishCue) &&
                    ending.TitleText.text == (chinese ? card.ChineseTitle : card.EnglishTitle) &&
                    ending.BodyText.text == (chinese ? card.ChineseBody : card.EnglishBody) &&
                    ending.FootnoteText.text == (chinese ? card.ChineseFootnote : card.EnglishFootnote),
                "Act " + (index + 1) + " displays its complete " + (chinese ? "Chinese" : "English") + " text.");
            var font = chinese ? ending.ChineseFont : ending.LatinFont;
            Require(ending.TitleText.font == font && ending.BodyText.font == font && ending.CueText.font == font,
                "Act " + (index + 1) + " uses the authored " + (chinese ? "Chinese serif" : "Latin theatre") + " font.");
            foreach (var text in new[] { ending.CueText, ending.TitleText, ending.BodyText, ending.FootnoteText })
            {
                text.ForceMeshUpdate();
                Require(!text.isTextOverflowing, text.name + " stays inside its layout in " + (chinese ? "Chinese." : "English."));
            }
        }

        static void AdvanceUntil(Func<bool> done)
        {
            for (int i = 0; i < 400 && !done(); i++)
            {
                if (terminal.Session.Phase == TerminalPhase.WaitingForChoice)
                {
                    var choice = terminal.Session.Choices.FirstOrDefault();
                    if (choice == null || !terminal.Choose(choice.Id))
                        throw new InvalidOperationException("Cannot advance authored epilogue choice.");
                }
                else terminal.Advance();
            }
            if (!done()) throw new InvalidOperationException("Dialogue did not reach expected stage at " + terminal.Session.CurrentNodeId);
        }

        static void SetField(object owner, string name, object value) =>
            owner.GetType().GetField(name, PrivateInstance).SetValue(owner, value);
        static T Field<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, PrivateInstance).GetValue(owner);
        static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
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
            if (!string.IsNullOrEmpty(originalLanguage) && LocalizationManager.Instance)
                LocalizationManager.Instance.SetLanguage(originalLanguage, false);
            if (testScript) UnityEngine.Object.Destroy(testScript);
            Report.AppendLine(error == null ? "SUCCESS: " + assertions + " theatre ending assertions passed."
                : "FAIL at stage " + stage + ": " + error);
            Directory.CreateDirectory("Logs/CockpitMission");
            File.WriteAllText(ReportPath, Report.ToString());
            if (error == null) Debug.Log("[AnotherTomorrowEndingValidation] " + assertions + " assertions passed.");
            else Debug.LogException(error);
            EditorApplication.isPaused = false;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
    }
}
