using System;
using LudumDare60.TerminalDialogue;
using UnityEditor;
using UnityEngine;

namespace LudumDare60.ReturnWindow.Editor
{
    /// <summary>Reproducible, editable story asset. Runtime mission decisions belong to MissionTerminalBridge.</summary>
    public static class ReturnStoryBuilder
    {
        public const string AssetPath = "Assets/ReturnWindow/Story/ReturnWindowStory.asset";

        [MenuItem("Tools/Return Window/Rebuild Story")]
        public static void RebuildStory() => Selection.activeObject = CreateStory();

        public static TerminalDialogueDefinition CreateStory()
        {
            EnsureFolder("Assets/ReturnWindow");
            EnsureFolder("Assets/ReturnWindow/Story");
            var definition = AssetDatabase.LoadAssetAtPath<TerminalDialogueDefinition>(AssetPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<TerminalDialogueDefinition>();
                AssetDatabase.CreateAsset(definition, AssetPath);
            }
            Populate(definition.Graph);
            var errors = definition.Graph.Validate();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            return definition;
        }

        public static void Populate(TerminalDialogueGraph graph)
        {
            graph.AIName = "NAV AI";
            graph.EntryNodeId = "intro";
            graph.DefaultTextColor = TerminalTextColor.Default;
            graph.InitialFlags.Clear();
            graph.Nodes.Clear();
            graph.InitialFlags.Add(new TerminalFlag { Key = "repair_ready", Value = false });

            var intro = Node(graph, "intro", "无人殖民地", true);
            Say(intro, "ARRIVAL RECORD / {ARRIVAL_DATE}.\nNOW / {START_DATE}. After months at a remote survey station, you return to the colony. Nobody comes to meet you.", TerminalSpeaker.System);
            Say(intro, "Colony control, this is the return craft. Does anyone read me?", TerminalSpeaker.Player);
            Say(intro, "All inhabited sectors are silent. No crew transponders. No response from the surface relay.");
            Say(intro, "Then I want to go home.", TerminalSpeaker.Player);
            Say(intro, "Checking propulsion, reserves and the departure assembly. There is a problem.");
            Choice(intro, "diagnose", "SHOW ME THE DAMAGE", "Tell me what we have left.", "window");

            var window = Node(graph, "window", "低能耗返航窗口", true);
            Say(window, "The damaged propulsion system has very little fuel margin. Rebuild the departure assembly, and we can still reach a low-energy Earth-return trajectory.");
            Say(window, "Our navigation archive lists 03-13 SEP 1981 for the planned Mars-to-Earth departure. The lower-energy route is the only one inside our remaining fuel budget. Missing our cutoff would demand more fuel than we can safely commit.");
            Say(window, "The next return period listed in the study is {NEXT_OPPORTUNITY}: more than two years later. It is a planning reference, not a guarantee for this damaged craft. Our stored food cannot cover that wait. We would have to restore the greenhouse.");
            Say(window, "The amber instrument marks our LATEST DEPARTURE authorization. The circled date is {LAST_DATE}; the ascent allowance is already reserved. If we repair early, we can launch to a waiting orbit, then make the Earth-return burn inside the planned window.");
            Choice(window, "make_schedule", "MAKE A PLAN", "Give me a schedule. We are not staying here.", "schedule");

            var schedule = Node(graph, "schedule", "任务日程与开始确认", true);
            Say(schedule, "The paper calendar runs from {START_DATE} through {LAST_DATE}: {DAY_COUNT} dated workdays. The cells use MONTH/DAY, not numbered mission days. I will add a red cross after each date has ended. Only the final deadline is circled.");
            Say(schedule, "The simulation compresses {DAY_COUNT} Earth-standard 24-hour dates into {ACTIVE_MINUTES} minutes of active work. Explicit story reading pauses the mission and all work. Repairs, salvage and ordinary terminal queries use time.", TerminalSpeaker.System);
            Say(schedule, "The countdown begins when you select BEGIN MISSION. Completing repairs will not launch the craft. You must return here and confirm departure before the clock reaches zero.");
            Say(schedule, "MOUSE: look around. E: connect or leave this terminal. ESC: return from a workstation, close the terminal, or pause the mission.", TerminalSpeaker.System);
            Choice(schedule, "start_mission", "BEGIN MISSION", "Start the clock. I am ready.", "mission");
            Choice(schedule, "review_window", "REVIEW THE ROUTE", "Explain the departure window once more.", "window");

            var mission = Node(graph, "mission", "运行中的任务终端", false);
            Say(mission, "Mission channel ready. The clock is running. Check the calendar and the amber departure instrument beside your seat.");
            Choice(mission, "status", "QUERY TIME AND REPAIRS", "Report the time remaining and the assembly status.", "status");
            Choice(mission, "tasks", "VIEW WORK SCHEDULE", "Show the work sequence.", "tasks");
            Choice(mission, "salvage", "RECOVER OPTIONAL SUPPLIES", "Start the salvage program.", "mission");
            Choice(mission, "open_repair", "REBUILD DEPARTURE ASSEMBLY", "Open the repair workstation.", "mission");
            Choice(mission, "read_story", "READ THE COLONY LOG", "Let me read the last colony log.", "story_memory");
            Choice(mission, "launch", "PREPARE TO DEPART", "Run the departure checks.", "launch").Conditions.Add(new TerminalFlag { Key = "repair_ready" });

            var status = Node(graph, "status", "实时状态查询", false);
            Say(status, "Reading the live mission instruments. The remaining time and assembly report follow in the system channel. Querying does not stop the clock.");
            Choice(status, "return_mission", "RETURN TO MISSION", "Back to the mission channel.", "mission");

            var tasks = Node(graph, "tasks", "维修清单", false);
            Say(tasks, "1. RECOVER: salvage optional supplies if you can spare the time. The repair parts are already at the workstation; extra supplies are not required to depart.\n2. REBUILD: fit every required assembly part into its socket.");
            Say(tasks, "3. CHECK: wait for the assembly to report complete.\n4. DEPART: select PREPARE TO DEPART, then confirm while time remains. The complete dated calendar is available; its dates are planning marks, not separate task deadlines.");
            Choice(tasks, "open_repair", "OPEN REPAIR WORKSTATION", "Open the repair workstation now.", "mission");
            Choice(tasks, "return_mission", "RETURN TO MISSION", "I have the sequence.", "mission");

            var memory = Node(graph, "story_memory", "殖民地日志阅读", true);
            Say(memory, "PERSONAL LOG / HABITAT 03\nIf anyone returns, the seed cabinet is under the greenhouse bench. Leave the small lamp on. It helps to think somebody is coming back.", TerminalSpeaker.System);
            Say(memory, "There is no departure entry after this recording. I cannot tell you where they went.");
            Say(memory, "Keep the log. Someone should remember them.", TerminalSpeaker.Player);
            Say(memory, "Saved. Your work will resume when you leave this reading window.");
            Choice(memory, "return_mission", "CLOSE LOG / RESUME WORK", "Close the log. We still have work to do.", "mission");

            var ready = Node(graph, "repair_ready", "维修完成提示", false);
            Say(ready, "The departure assembly reports complete. That is enough for the low-energy return plan. We can leave as soon as you authorize departure; the window is still counting down.");
            Choice(ready, "launch", "PREPARE TO DEPART", "Run the departure checks.", "launch").Conditions.Add(new TerminalFlag { Key = "repair_ready" });
            Choice(ready, "return_mission", "RETURN TO MISSION", "Keep the mission channel open.", "mission");

            var launch = Node(graph, "launch", "出发确认", false);
            Say(launch, "Departure authorization. The assembly must remain complete, and the latest-departure clock must be above zero. Your confirmation is registered at the moment you select it.");
            Say(launch, "The route gets us away from Mars. I cannot promise what waits at Earth. Shall we go?");
            Choice(launch, "confirm_launch", "CONFIRM DEPARTURE", "Take us home.", "mission").Conditions.Add(new TerminalFlag { Key = "repair_ready" });
            Choice(launch, "return_mission", "KEEP WORKING", "Not yet. Return to the mission channel.", "mission");

            var home = Node(graph, "ending_home", "开放结局：回家的路", true);
            Say(home, "DEPARTURE ACCEPTED. The countdown has stopped at your authorization time. Ascent to the waiting orbit is cleared.", TerminalSpeaker.System);
            Say(home, "The colony is below us. We reach the waiting orbit. Later, inside the planned return window, the Earth-return burn carries us away from Mars.");
            Say(home, "Is anyone waiting for us?", TerminalSpeaker.Player);
            Say(home, "I do not know. I will keep listening.");
            Say(home, "You do not know whether home still remembers you. But at last, you are on your way back.", TerminalSpeaker.System);
            Choice(home, "restart", "BEGIN ANOTHER JOURNEY", "Begin another journey.", "intro");

            var stay = Node(graph, "ending_stay", "开放结局：下一天", true);
            Say(stay, "WINDOW CLOSED. We can no longer commit to this return plan with the fuel margin available.", TerminalSpeaker.System);
            Say(stay, "The next period in our navigation study is {NEXT_OPPORTUNITY}. We must reassess the craft before using it. Stored food will not last that long. I am sorry.");
            Say(stay, "Then tell me what we do tomorrow.", TerminalSpeaker.Player);
            Say(stay, "The greenhouse still has a sealed seed cabinet. We can bring its water loop back online. I have made a new entry on the calendar.");
            Say(stay, "{SOWING_DATE}: SOW.\nOutside, the dust moves across an empty settlement. Tomorrow, you will plant the first seed.", TerminalSpeaker.System);
            Choice(stay, "restart", "BEGIN ANOTHER JOURNEY", "Begin another journey.", "intro");
        }

        private static TerminalNode Node(TerminalDialogueGraph graph, string id, string title, bool story)
        {
            var node = new TerminalNode { Id = id, Title = title, PauseGameplayWhileReading = story };
            graph.Nodes.Add(node);
            return node;
        }

        private static void Say(TerminalNode node, string text, TerminalSpeaker speaker = TerminalSpeaker.AI)
        {
            node.Lines.Add(new TerminalLine { Speaker = speaker, Text = text, DelayAfter = 0.3f });
        }

        private static TerminalChoice Choice(TerminalNode node, string id, string label, string text, string next)
        {
            var choice = new TerminalChoice { Id = id, Label = label, PlayerText = text, NextNodeId = next };
            node.Choices.Add(choice);
            return choice;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var split = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, split));
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }
    }
}
