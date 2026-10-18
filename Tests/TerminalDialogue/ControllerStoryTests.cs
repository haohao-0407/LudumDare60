using System;
using LudumDare60.TerminalDialogue;
using LudumDare60.ReturnWindow.Editor;
using LudumDare60.ReturnWindow;

// Runs the actual controller + story builder with minimal Unity API stand-ins.
// Unity editor validation remains responsible for engine/component/asset integration.
internal static class ControllerStoryTests
{
    private static int passed;
    private static int Main()
    {
        Run("accepted input fires before Changed and authored commit", ImmediateAcceptance);
        Run("rejected input never fires acceptance", RejectedInput);
        Run("acceptance can replace pending launch dialogue immediately", AcceptedTransition);
        Run("story is valid and uses supported ASCII display text", StoryValidation);
        Run("story routes through explicit start and gates departure on actual repair flag", StoryRoute);
        Run("both endings remain open and have no score labels", EndingCopy);
        Console.WriteLine("Passed " + passed + " controller/story tests.");
        return passed == 6 ? 0 : 1;
    }
    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static TerminalDialogueController Controller()
    {
        var asset = new TerminalDialogueDefinition();
        asset.Graph.EntryNodeId = "menu";
        var menu = new TerminalNode { Id = "menu" };
        menu.Choices.Add(new TerminalChoice { Id = "launch", Label = "Launch", PlayerText = "Take us home.", NextNodeId = "menu",
            Events = { "delayed_event" }, SetFlags = { new TerminalFlag { Key = "committed" } } });
        asset.Graph.Nodes.Add(menu);
        var end = new TerminalNode { Id = "ending", PauseGameplayWhileReading = true };
        end.Lines.Add(new TerminalLine { Text = "On the way home.", DelayAfter = 0 });
        asset.Graph.Nodes.Add(end);
        var controller = new TerminalDialogueController();
        controller.Configure(asset);
        controller.Begin();
        return controller;
    }
    private static void ImmediateAcceptance()
    {
        var controller = Controller();
        var accepted = 0; var committed = 0; var changed = 0;
        controller.Changed += () => changed++;
        controller.EventTriggered += _ => committed++;
        controller.ChoiceAccepted += (node, choice) =>
        {
            accepted++;
            Check(node == "menu" && choice == "launch", "wrong source identifiers");
            Check(changed == 0 && committed == 0 && !controller.GetFlag("committed"), "acceptance arrived after publication or delayed commit");
            Check(controller.Session.Phase == TerminalPhase.Typing, "acceptance did not follow accepted input");
        };
        Check(controller.Choose("launch") && accepted == 1 && changed == 1, "immediate event missing");
        Check(committed == 0 && !controller.GetFlag("committed"), "authored event no longer waits for typing");
        controller.Advance();
        Check(committed == 1 && controller.GetFlag("committed"), "delayed event failed to commit");
        controller.Close(); controller.Open();
        Check(committed == 1 && accepted == 1, "reopening replayed input");
    }
    private static void RejectedInput()
    {
        var controller = Controller(); var count = 0;
        controller.ChoiceAccepted += (_, __) => count++;
        Check(!controller.Choose("missing") && count == 0, "invalid input accepted");
        controller.Close();
        Check(!controller.Choose("launch") && count == 0, "closed input accepted");
        controller.Open(); controller.Choose("launch");
        Check(!controller.Choose("launch") && count == 1, "double click accepted");
    }
    private static void AcceptedTransition()
    {
        var controller = Controller(); var events = 0;
        controller.EventTriggered += _ => events++;
        controller.ChoiceAccepted += (_, __) => controller.GoToNode("ending");
        Check(controller.Choose("launch"), "launch click rejected");
        Check(controller.Session.CurrentNodeId == "ending" && controller.IsStoryReading, "ending delayed until player typing");
        controller.Advance();
        Check(events == 0 && !controller.GetFlag("committed"), "stale choice side effect reached ending");
    }
    private static TerminalDialogueGraph Story()
    {
        var graph = new TerminalDialogueGraph(); ReturnStoryBuilder.Populate(graph);
        foreach (var node in graph.Nodes) foreach (var line in node.Lines) line.Text = new MissionSchedule().Resolve(line.Text);
        return graph;
    }
    private static void StoryValidation()
    {
        var graph = Story();
        Check(graph.Validate().Count == 0, "invalid authored graph");
        foreach (var node in graph.Nodes)
        {
            foreach (var line in node.Lines) Ascii(line.Text);
            foreach (var choice in node.Choices) { Ascii(choice.Label); Ascii(choice.PlayerText); }
        }
        foreach (var id in new[] { "intro", "window", "schedule", "story_memory", "ending_home", "ending_stay" })
            Check(graph.FindNode(id).PauseGameplayWhileReading, id + " must be protected reading");
        foreach (var id in new[] { "mission", "status", "tasks", "repair_ready", "launch" })
            Check(!graph.FindNode(id).PauseGameplayWhileReading, id + " must consume mission time");
        Check(graph.FindNode("intro").Lines[0].Text.Contains("08 SEP 1980") && graph.FindNode("intro").Lines[0].Text.Contains("25 AUG 1981"), "arrival and playable dates were conflated");
        Check(graph.FindNode("window").Lines[2].Text.Contains("09 NOV 1983") && !graph.FindNode("window").Lines[2].Text.Contains("218"), "archived later opportunity missing");
        Check(graph.FindNode("window").Lines[3].Text.Contains("waiting orbit"), "early repair wrongly implies immediate interplanetary departure");
    }
    private static void Ascii(string text)
    {
        foreach (var character in text) Check(character == '\n' || character >= 32 && character <= 126, "unsupported font character: " + character);
    }
    private static void FinishLines(TerminalDialogueSession session)
    {
        for (var i = 0; i < 100 && session.Phase != TerminalPhase.WaitingForChoice && session.Phase != TerminalPhase.Completed; i++) session.Advance();
    }
    private static void Select(TerminalDialogueSession session, string id)
    {
        FinishLines(session); Check(session.Choose(id), "missing choice " + id); FinishLines(session);
    }
    private static void StoryRoute()
    {
        var session = new TerminalDialogueSession(Story()); session.Start();
        Select(session, "diagnose"); Select(session, "make_schedule");
        Check(session.CurrentNodeId == "schedule" && session.IsStoryReading, "missing protected start confirmation");
        Select(session, "start_mission");
        Check(session.CurrentNodeId == "mission" && !session.IsStoryReading, "work menu paused");
        Check(!session.Choose("launch"), "unrepaired ship can launch");
        Select(session, "read_story"); Check(session.IsStoryReading, "log failed to pause");
        Select(session, "return_mission"); Check(!session.IsStoryReading, "closed log kept time frozen");
        session.SetFlag("repair_ready", true); Select(session, "launch");
        Check(session.CurrentNodeId == "launch" && !session.IsStoryReading, "launch menu is paused");
        session.SetFlag("repair_ready", false);
        Check(!session.Choose("confirm_launch"), "stale repair flag still allowed launch");
        session.GoToNode("ending_stay"); FinishLines(session);
        Check(session.IsStoryReading && session.CurrentNodeId == "ending_stay", "expiry could not interrupt launch menu");
    }
    private static void EndingCopy()
    {
        var graph = Story();
        foreach (var id in new[] { "ending_home", "ending_stay" })
        {
            var node = graph.FindNode(id);
            foreach (var line in node.Lines)
            {
                var text = line.Text.ToUpperInvariant();
                Check(!text.Contains("VICTORY") && !text.Contains("YOU LOSE") && !text.Contains("GAME OVER"), "ending assigns a score label");
            }
        }
        Check(graph.FindNode("ending_stay").Lines[4].Text.Contains("14 SEP 1981: SOW"), "missing dated next morning");
        Check(graph.FindNode("ending_home").Lines[4].Text.Contains("on your way back"), "home ending claims arrival");
    }
}
