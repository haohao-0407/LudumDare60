using System;
using System.IO;
using System.Linq;
using System.Text;
using LudumDare60.AssemblyDemo;
using LudumDare60.Storage;
using LudumDare60.TerminalDialogue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LudumDare60.ReturnWindow.Editor
{
    /// <summary>Real Play-mode smoke checks; run after building the scene, without -quit.</summary>
    [InitializeOnLoad]
    public static class ReturnWindowValidation
    {
        private const string RunKey = "ReturnWindowValidation.Running";
        private static int frames, stage, lastPlayerFrame;
        private static double started, waitUntil, pausedRemaining, lastPlayerFrameAt;
        private static MissionController firstRun;
        private static ReturnRepairStation previousStation;
        private static AssemblyDemoController previousAssembly;
        private static AsyncOperation sceneTransition;
        private static double loadingElapsed, loadedElapsed;
        private static readonly StringBuilder report = new StringBuilder();

        static ReturnWindowValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(RunKey, false)) return;
                Application.runInBackground = true;
                EditorApplication.isPaused = false;
                frames = stage = 0; lastPlayerFrame = -1; started = lastPlayerFrameAt = EditorApplication.timeSinceStartup; report.Clear();
                Debug.Log("[ReturnWindowValidation] Entered Play; background updates enabled; starting real-frame validation.");
                EditorApplication.update += CheckRuntime;
            };
        }

        [MenuItem("Tools/Return Window/Validate Mission In Play Mode")]
        public static void Run()
        {
            try
            {
                Require(File.Exists(ReturnWindowSceneBuilder.ScenePath), "Build the Return Window mission first.");
                EditorSceneManager.OpenScene(ReturnWindowSceneBuilder.ScenePath, OpenSceneMode.Single);
                Application.runInBackground = true;
                EditorApplication.isPaused = false;
                var gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
                if (gameViewType != null)
                {
                    var gameView = EditorWindow.GetWindow(gameViewType);
                    gameView.Show(); gameView.Focus();
                }
                SessionState.SetBool(RunKey, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void CheckRuntime()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - started > 90) throw new TimeoutException("Mission smoke test exceeded 90 seconds.");
                // Editor update callbacks also run between player frames. Deferred Destroy,
                // coroutines and scene unloads must be given real player-loop frames to finish.
                EditorApplication.isPaused = false;
                Application.runInBackground = true;
                EditorApplication.QueuePlayerLoopUpdate();
                if (Time.frameCount == lastPlayerFrame)
                {
                    if (EditorApplication.timeSinceStartup - lastPlayerFrameAt > .5)
                    {
                        // Some unattended Editor layouts do not pump a Game view. Step runs
                        // a real player frame, including deferred destruction and coroutines.
                        EditorApplication.Step();
                        lastPlayerFrameAt = EditorApplication.timeSinceStartup;
                    }
                    return;
                }
                lastPlayerFrameAt = EditorApplication.timeSinceStartup;
                lastPlayerFrame = Time.frameCount;
                if (++frames < 20 || EditorApplication.timeSinceStartup < waitUntil) return;
                var mission = MissionController.Instance;
                var terminal = UnityEngine.Object.FindFirstObjectByType<TerminalDialogueController>();
                var station = UnityEngine.Object.FindFirstObjectByType<ReturnRepairStation>();
                if (mission == null || terminal == null || terminal.Session == null || station == null) return;
                // A batch-mode Editor has no user-focused Game view; simulate focus explicitly.
                mission.SendMessage("OnApplicationFocus", true);

                switch (stage)
                {
                    case 0:
                        Require(mission.State == MissionState.Ready && mission.IsGameplayPaused, "Initial briefing must not run time or repair work.");
                        ValidateDatedCalendar();
                        Require(!terminal.Session.Messages.Any(message => message.Text.Contains("{START_DATE}")), "Story date tokens were not resolved.");
                        Require(station.assembly.InputBlocked, "Inactive workbench was not protected during briefing.");
                        Capture("01-opening-terminal");
                        ReachChoices(terminal); Require(terminal.Choose("diagnose"), "Cannot begin diagnosis.");
                        ReachChoices(terminal); Require(terminal.Choose("make_schedule"), "Cannot request the schedule.");
                        ReachChoices(terminal); Require(terminal.Choose("start_mission"), "Cannot start the mission.");
                        Require(mission.State == MissionState.Running && !mission.IsGameplayPaused && Time.timeScale > 0, "BEGIN MISSION did not release gameplay.");
                        Pass("Briefing -> window -> schedule -> immediate mission start, actual dialogue callbacks");

                        terminal.Open(); terminal.GoToNode("story_memory");
                        Require(terminal.IsStoryReading && mission.IsGameplayPaused && Time.timeScale == 0, "Story reading failed to freeze simulation.");
                        Require(!mission.TryLaunch(), "Launch was accepted while story reading.");
                        pausedRemaining = mission.Clock.RemainingSeconds;
                        waitUntil = EditorApplication.timeSinceStartup + .25; stage = 1;
                        break;

                    case 1:
                        Equal(mission.Clock.RemainingSeconds, pausedRemaining, "Reading consumed mission time.");
                        mission.SetMenuPaused(true);
                        terminal.GoToNode("mission"); terminal.Close();
                        Require(mission.IsGameplayPaused && Time.timeScale == 0, "Closing reading bypassed the overlapping menu pause.");
                        mission.SetMenuPaused(false);
                        Require(!mission.IsGameplayPaused && Time.timeScale > 0, "Overlapping pauses failed to resume.");
                        Pass("Story typing/reading pause, launch guard, nested menu pause and clean resume");
                        var camera = Camera.main;
                        var savedLook = camera.transform.rotation;
                        var calendarView = UnityEngine.Object.FindFirstObjectByType<ReturnCalendarView>();
                        var countdownView = UnityEngine.Object.FindFirstObjectByType<ReturnCountdownView>();
                        var instruments = (calendarView.transform.position + countdownView.transform.position) * .5f + Vector3.up * .15f;
                        camera.transform.rotation = Quaternion.LookRotation(instruments - camera.transform.position, Vector3.up);
                        Capture("02-pilot-instruments");
                        camera.transform.rotation = savedLook;

                        station.OpenRepair();
                        Require(station.IsOpen, "Repair workstation did not open.");
                        var originalPosition = station.assembly.Selected.Position;
                        mission.SetReadingPaused(true);
                        station.assembly.ApplyMotion(Vector2.one, 90);
                        Require(!station.assembly.TrySnapSelected(), "Repair snapped while reading was paused.");
                        Require(station.assembly.Selected.Position == originalPosition, "Repair moved while reading was paused.");
                        mission.SetReadingPaused(false);
                        AssembleActualPorts(station.assembly);
                        UnityEngine.Object.FindFirstObjectByType<MissionRepairBridge>().RefreshRepairState();
                        if (station.deviceWorkbench != null)
                        {
                            Require(!mission.RepairComplete, "Navigation alone must not complete all device repairs.");
                            for (int deviceIndex = 1; deviceIndex < station.deviceWorkbench.devices.Length; deviceIndex++)
                            {
                                station.SelectDevice(deviceIndex);
                                AssembleActualPorts(station.assembly);
                                UnityEngine.Object.FindFirstObjectByType<MissionRepairBridge>().RefreshRepairState();
                                if (deviceIndex < station.deviceWorkbench.devices.Length-1)
                                    Require(!mission.RepairComplete, "One remaining device must block departure.");
                            }
                            station.SelectDevice(0);
                        }
                        Require(mission.RepairComplete, "Actual joints did not unlock departure.");
                        station.CloseRepair();
                        Require(mission.RepairComplete && !station.workshop.activeSelf, "Closing workstation lost the completed assembly state.");
                        Pass("Navigation, life support and energy physical joints, all-device launch gate, input guards and inactive-workshop persistence");

                        firstRun = mission;
                        new GameObject("Duplicate mission validation").AddComponent<MissionController>();
                        var temporary = SceneManager.CreateScene("ReturnWindow additive unload validation");
                        SceneManager.UnloadSceneAsync(temporary);
                        frames = 0; stage = 2;
                        break;

                    case 2:
                        var services = UnityEngine.Object.FindObjectsByType<MissionController>(FindObjectsSortMode.None);
                        Require(MissionController.Instance == firstRun && services.Length == 1,
                            "Duplicate service reset the run or survived after 20 real player frames. Instance retained="
                            + (MissionController.Instance == firstRun) + "; services=" + services.Length + "; frame=" + Time.frameCount
                            + "; names=" + string.Join(",", services.Select(service => service.name + "/enabled=" + service.enabled)));
                        Require(mission.State == MissionState.Running && !mission.IsGameplayPaused, "Unloading an additive scene left the mission paused.");
                        Pass("Persistent service rejects duplicates; additive scene unload does not freeze the mission");
                        var inventory = mission.GetComponent<InventoryController>();
                        Require(inventory != null && inventory.Items.TryAdd("validation_scene_sentinel", 3), "Cannot add session inventory sentinel.");
                        previousStation = station; previousAssembly = station.assembly;
                        loadedElapsed = double.NaN;
                        sceneTransition = mission.LoadSceneAsync("ReturnWindow");
                        loadingElapsed = mission.Clock.ElapsedSeconds;
                        Require(sceneTransition != null && mission.IsGameplayPaused, "Scene transition did not pause mission time.");
                        sceneTransition.completed += _ => loadedElapsed = firstRun.Clock.ElapsedSeconds;
                        frames = 0; stage = 20;
                        break;

                    case 20:
                        if (!sceneTransition.isDone || station == previousStation) return;
                        Require(mission == firstRun && mission.State == MissionState.Running, "Scene transition replaced the active mission run.");
                        Equal(loadedElapsed, loadingElapsed, "Asynchronous scene loading consumed mission time.");
                        Require(mission.Clock.ElapsedSeconds >= loadingElapsed && !mission.IsGameplayPaused, "Scene transition reset time or left it paused.");
                        Require(station.assembly == previousAssembly && station.assembly.IsAssemblyComplete && mission.RepairComplete,
                            "Scene transition lost the actual repaired joint graph.");
                        Require(terminal.GetFlag("repair_ready"), "New terminal did not restore verified repair status.");
                        Require(mission.GetComponent<InventoryController>().Items.GetQuantity("validation_scene_sentinel") == 3,
                            "Scene transition lost session inventory.");
                        if (station.deviceWorkbench != null)
                        {
                            station.OpenRepair();
                            Require(station.IsOpen, "Cannot reopen the retained device workbench.");
                            for (int deviceIndex = 1; deviceIndex < station.deviceWorkbench.devices.Length; deviceIndex++)
                            {
                                string prefix = "F" + (deviceIndex+1);
                                station.workshopUI.GetComponentsInChildren<UnityEngine.UI.Button>(true)
                                    .First(button => button.name.StartsWith(prefix)).onClick.Invoke();
                                Require(station.deviceWorkbench.selectedDevice == deviceIndex && station.assembly.IsAssemblyComplete,
                                    "Reloaded workbench button lost its persistent device target.");
                            }
                            station.SelectDevice(0); station.CloseRepair(false);
                            Pass("Device selection buttons reconnect to retained assemblies after scene reload");
                        }
                        Pass("Real asynchronous scene transition preserves clock, physical joints, AI status and inventory without loading-time cost");
                        terminal.GoToNode("launch"); terminal.Open(); ReachChoices(terminal);
                        Require(terminal.Choose("confirm_launch"), "Departure choice rejected.");
                        Require(mission.State == MissionState.Departed && mission.IsGameplayPaused && Time.timeScale == 0,
                            "Confirmed departure did not synchronously select and lock the home ending.");
                        Require(!mission.TryLaunch(), "Repeated departure was accepted.");
                        Pass("Immediate confirmed departure, repair gate, frozen calendar and mutually exclusive ending");
                        waitUntil = EditorApplication.timeSinceStartup + 2.8; frames = 0; stage = 3;
                        break;

                    case 3:
                        Capture("03-home-ending");
                        UnityEngine.Object.FindFirstObjectByType<ReturnEndingView>().RestartMission();
                        frames = 0; stage = 4;
                        break;

                    case 4:
                        if (mission == firstRun) return;
                        Require(mission.State == MissionState.Ready && !mission.RepairComplete, "New journey inherited mission completion.");
                        Require(mission.GetComponent<InventoryController>().Items.GetQuantity("validation_scene_sentinel") == 0,
                            "New journey inherited inventory from the previous run.");
                        Equal(mission.Clock.RemainingSeconds, 1200, "New journey did not receive a fresh deadline.");
                        terminal.GoToNode("mission"); terminal.Close(); mission.StartMission();
                        mission.Clock.AdvanceTo(Time.realtimeSinceStartupAsDouble + mission.Clock.DurationSeconds + 1);
                        Require(mission.State == MissionState.Stayed && mission.Clock.CompletedDays == 20 && mission.IsGameplayPaused,
                            "Expiry did not close all twenty dates and select the stay ending.");
                        Require(!mission.TryLaunch(), "Expired mission allowed launch.");
                        Pass("Restart clears run, exact expiry reaches stay ending, all twenty crosses recorded and launch locked");
                        waitUntil = EditorApplication.timeSinceStartup + 2.8; frames = 0; stage = 5;
                        break;

                    case 5:
                        var calendar = UnityEngine.Object.FindFirstObjectByType<ReturnCalendarView>();
                        Require(calendar != null && calendar.GetComponentsInChildren<TMPro.TMP_Text>(true).Any(t => t.text.Contains("SOW")),
                            "Stay ending lacks its sowing-day calendar content.");
                        Require(UnityEngine.Object.FindFirstObjectByType<ReturnCountdownView>() != null, "Physical countdown is missing.");
                        Capture("04-stay-ending");
                        Pass("Physical calendar and countdown survive both ending transitions");
                        Finish(null);
                        break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private static void ValidateDatedCalendar()
        {
            var original = UnityEngine.Object.FindFirstObjectByType<ReturnCalendarView>();
            var copy = UnityEngine.Object.Instantiate(original.gameObject, new Vector3(50,50,50), Quaternion.identity);
            try
            {
                var view = copy.GetComponent<ReturnCalendarView>();
                view.Render(3,4,false);
                var labels = copy.GetComponentsInChildren<TMPro.TMP_Text>(true).Where(label => label.name.StartsWith("Date ")).ToArray();
                Require(labels.Length == 20 && labels.Any(label => label.text == "08/25") && labels.Any(label => label.text == "08/31")
                    && labels.Any(label => label.text == "09/01") && labels.Any(label => label.text == "09/13"), "Physical date grid is missing dates or month rollover.");
                var crosses = copy.GetComponentsInChildren<LineRenderer>(true).Where(line => line.name.StartsWith("Cross day ")).ToArray();
                Require(crosses.Count(line => line.enabled) == 6, "Three completed dates must have six cross strokes.");
                view.Render(3,4,false);
                Require(crosses.Count(line => line.enabled) == 6, "Repeated rendering duplicated or removed crosses.");
                var circles = copy.GetComponentsInChildren<Transform>(true).Where(item => item.name == "Final day hand drawn ring").ToArray();
                Require(circles.Length == 1 && Vector2.Distance(circles[0].localPosition, ReturnCalendarView.CellPosition(19)) < .0001f,
                    "Only the final dated cell should have a circle.");
                view.Render(20,20,true);
                Require(crosses.Count(line => line.enabled) == 40 && copy.GetComponentsInChildren<TMPro.TMP_Text>(true).Any(label => label.text == "14 SEP 1981 / SOW"),
                    "Expiry must retain all dated crosses and show the following civil date.");
                Pass("Twenty MM/DD cells cross month boundary; completed dates use two-stroke crosses, only 09/13 is circled, next morning is 09/14");
            }
            finally { UnityEngine.Object.Destroy(copy); }
        }

        private static void AssembleActualPorts(AssemblyDemoController assembly)
        {
            var core = assembly.Modules.OrderByDescending(module => module.PortCount).First();
            foreach (var module in assembly.Modules)
            {
                if (module == core) continue;
                var matchingPort = -1;
                for (var i = 0; i < core.PortCount; i++)
                    if (module.PortKey(0) == core.PortKey(i)) matchingPort = i;
                Require(matchingPort >= 0, "No matching core port for " + module.name);
                module.RotationDegrees = core.RotationDegrees + core.PortAngle(matchingPort) + 180 - module.PortAngle(0);
                module.Position = core.PortPosition(matchingPort) - module.PortDirection(0) * module.PortDistance(0);
                assembly.SelectModule(Array.IndexOf(assembly.Modules, module));
                Require(assembly.TrySnapSelected(), "Real assembly solver rejected " + module.name);
            }
            Require(assembly.ConnectionCount == assembly.RequiredConnectionCount && assembly.IsAssemblyComplete,
                "Repair did not create all required physical joints.");
        }

        private static void ReachChoices(TerminalDialogueController terminal)
        {
            for (var i = 0; i < 100 && terminal.Session.Phase != TerminalPhase.WaitingForChoice; i++) terminal.Advance();
            Require(terminal.Session.Phase == TerminalPhase.WaitingForChoice, "Dialogue failed to reach choices at " + terminal.Session.CurrentNodeId);
        }

        // Overlay canvases normally bypass Camera.Render. Temporarily mount them to the same
        // camera so batch-mode captures include dialogue controls and the ending text as well.
        private static void Capture(string name)
        {
            var camera = Camera.main;
            Require(camera != null, "No pilot camera for visual verification.");
            var overlays = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(canvas => canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var oldCameras = overlays.Select(canvas => canvas.worldCamera).ToArray();
            var oldPlanes = overlays.Select(canvas => canvas.planeDistance).ToArray();
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var oldAspect = camera.aspect;
            var oldAsyncShaders = ShaderUtil.allowAsyncCompilation;
            var target = new RenderTexture(1600, 900, 24);
            var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                target.Create(); camera.targetTexture = target; camera.aspect = 1600f / 900;
                foreach (var canvas in overlays)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = .15f;
                }
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply();
                Directory.CreateDirectory("Logs/ReturnWindowCaptures");
                File.WriteAllBytes("Logs/ReturnWindowCaptures/" + name + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                for (var i = 0; i < overlays.Length; i++)
                {
                    overlays[i].renderMode = RenderMode.ScreenSpaceOverlay;
                    overlays[i].worldCamera = oldCameras[i]; overlays[i].planeDistance = oldPlanes[i];
                }
                camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = oldActive;
                ShaderUtil.allowAsyncCompilation = oldAsyncShaders;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Equal(double actual, double expected, string message) => Require(Math.Abs(actual - expected) < .00001, message);
        private static void Pass(string message) { report.AppendLine("PASS " + message); Debug.Log("[ReturnWindowValidation] PASS " + message); }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= CheckRuntime;
            SessionState.SetBool(RunKey, false);
            Directory.CreateDirectory("Logs");
            if (error != null) { report.AppendLine("FAIL " + error); Debug.LogException(error); }
            File.WriteAllText("Logs/ReturnWindowRuntimeValidation.txt", report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
