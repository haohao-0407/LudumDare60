using System;
using LudumDare60.TerminalDialogue;

internal static class SessionTests
{
    private static int passed;
    private static int Main()
    {
        Run("input finishes before flags/events commit; double click rejected", CommitAfterTyping);
        Run("skip reveals one line and preserves manual continue", SkipDoesNotAdvanceTwice);
        Run("close pauses mid-input; reopen does not replay events", CloseAndResume);
        Run("conditions refresh and unknown flags default false", ConditionalChoices);
        Run("notification preserves pending choices", NotificationPreservesChoices);
        Run("notification waits until current line boundary", NotificationBetweenLines);
        Run("notification after completion returns to completion", NotificationAfterEnd);
        Run("graphemes are not partially revealed", UnicodeTyping);
        Run("asset edits cannot mutate an active session", GraphSnapshot);
        Run("global and per-line colors survive choices, notifications and snapshots", TextColors);
        Run("per-line display names preserve defaults, speakers and snapshots", DisplayNames);
        Run("history remains bounded across loops", BoundedHistory);
        Run("invalid targets, duplicate IDs and empty cycles rejected", ValidateBadGraphs);
        Run("invalid timing rejected", InvalidTiming);
        Run("story-reading state follows opened snapshotted nodes", StoryReadingState);
        Run("forced mission transition cancels uncommitted input and queued notifications", InterruptPendingInput);
        Run("forced closed transition preserves flags and transcript and resumes on open", InterruptWhileClosed);
        Console.WriteLine("Passed " + passed + " terminal dialogue tests.");
        return passed == 17 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception exception) { Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static TerminalDialogueGraph Graph()
    {
        var graph = new TerminalDialogueGraph { EntryNodeId = "start" };
        var start = new TerminalNode { Id = "start" };
        start.Lines.Add(new TerminalLine { Text = "收到信号。", DelayAfter = 0 });
        start.Choices.Add(new TerminalChoice { Id = "scan", Label = "扫描", PlayerText = "scan nearby", NextNodeId = "result",
            SetFlags = { new TerminalFlag { Key = "scanned" } }, Events = { "scan_event" } });
        var result = new TerminalNode { Id = "result" };
        result.Lines.Add(new TerminalLine { Text = "扫描完成。", DelayAfter = 0 });
        graph.Nodes.Add(start); graph.Nodes.Add(result);
        return graph;
    }
    private static TerminalDialogueSession Waiting(TerminalDialogueGraph graph = null)
    {
        var session = new TerminalDialogueSession(graph ?? Graph()); session.Start();
        session.Tick(1, 0, 0);
        Check(session.Phase == TerminalPhase.WaitingForChoice, "should await choice");
        return session;
    }
    private static void CommitAfterTyping()
    {
        var session = Waiting();
        Check(session.Choose("scan"), "first click rejected");
        Check(!session.Choose("scan"), "double click accepted");
        session.Tick(0.1f, 10, 1);
        Check(!session.GetFlag("scanned") && !session.TryDequeueEvent(out _), "committed before input completed");
        session.Advance();
        Check(session.GetFlag("scanned"), "flag not set");
        Check(session.TryDequeueEvent(out var id) && id == "scan_event", "missing event");
        Check(!session.TryDequeueEvent(out _), "event emitted twice");
        Check(session.Messages[2].Text == "扫描完成。", "wrong next node");
    }
    private static void SkipDoesNotAdvanceTwice()
    {
        var graph = Graph(); graph.Nodes[0].Lines[0].WaitForContinue = true;
        var session = new TerminalDialogueSession(graph); session.Start(); session.Advance();
        Check(session.Phase == TerminalPhase.WaitingForContinue, "skip also advanced");
        session.Advance(); Check(session.Phase == TerminalPhase.WaitingForChoice, "continue did not advance");
    }
    private static void CloseAndResume()
    {
        var session = Waiting(); session.Choose("scan"); session.Tick(0.5f, 2, 2);
        var visible = session.VisibleCodeUnits; session.Close(); session.Tick(100, 100, 100); session.Advance();
        Check(session.VisibleCodeUnits == visible && !session.TryDequeueEvent(out _), "closed session progressed");
        session.Open(); session.Advance(); Check(session.TryDequeueEvent(out _), "resume lost pending event");
        session.Close(); session.Open(); session.Advance();
        Check(!session.TryDequeueEvent(out _), "open replayed event");
    }
    private static void ConditionalChoices()
    {
        var graph = Graph(); graph.Nodes[0].Choices[0].Conditions.Add(new TerminalFlag { Key = "unlocked" });
        var session = Waiting(graph); Check(session.Choices.Count == 0, "unknown true condition was visible");
        session.SetFlag("unlocked", true); Check(session.Choices.Count == 1, "did not refresh");
        session.SetFlag("unlocked", false); Check(!session.Choose("scan"), "hidden choice accepted");
    }
    private static void NotificationPreservesChoices()
    {
        var session = Waiting(); session.PostSystemMessage("警报");
        Check(session.Phase == TerminalPhase.Typing && !session.Choose("scan"), "notification did not lock choices");
        session.Advance(); session.Advance();
        Check(session.Phase == TerminalPhase.WaitingForChoice && session.Choices.Count == 1, "lost choice after notification");
    }
    private static void NotificationBetweenLines()
    {
        var session = new TerminalDialogueSession(Graph()); session.Start(); session.Tick(0.1f, 1, 1);
        session.PostSystemMessage("新通知"); Check(session.Messages.Count == 1, "cut current line");
        session.Advance(); Check(session.Messages.Count == 2 && session.Messages[1].Text == "新通知", "notification not queued");
    }
    private static void NotificationAfterEnd()
    {
        var session = Waiting(); session.Choose("scan"); session.Advance(); session.Advance();
        Check(session.Phase == TerminalPhase.Completed, "not completed");
        session.PostSystemMessage("后台作业完成"); session.Advance(); session.Advance();
        Check(session.Phase == TerminalPhase.Completed, "notification restarted dialogue");
    }
    private static void UnicodeTyping()
    {
        var graph = Graph(); graph.Nodes[0].Lines[0].Text = "A😀e\u0301中";
        var session = new TerminalDialogueSession(graph); session.Start(); session.Tick(2, 1, 1);
        Check(session.VisibleCodeUnits == 3, "split surrogate pair");
        session.Tick(1, 1, 1); Check(session.VisibleCodeUnits == 5, "split combining sequence");
    }
    private static void GraphSnapshot()
    {
        var graph = Graph(); var session = Waiting(graph);
        graph.Nodes[0].Choices[0].PlayerText = "changed"; graph.Nodes[1].Lines[0].Text = "changed";
        session.Choose("scan"); Check(session.Messages[1].Text == "scan nearby", "choice changed");
        session.Advance(); Check(session.Messages[2].Text == "扫描完成。", "line changed");
    }
    private static void TextColors()
    {
        var graph = Graph();
        var global = new TerminalTextColor(0.2f, 0.7f, 0.3f);
        var special = new TerminalTextColor(1, 0.3f, 0.1f, 0.8f);
        var reply = new TerminalTextColor(0.1f, 0.6f, 1);
        graph.DefaultTextColor = global;
        graph.Nodes[0].Lines[0].UseCustomColor = true; graph.Nodes[0].Lines[0].CustomColor = special;
        graph.Nodes[0].Choices[0].UseCustomColor = true; graph.Nodes[0].Choices[0].CustomColor = reply;
        graph.Nodes[1].Lines[0].CustomColor = special; // Disabled override must use global.
        var session = Waiting(graph);
        graph.DefaultTextColor = new TerminalTextColor();
        graph.Nodes[0].Lines[0].CustomColor = new TerminalTextColor();
        graph.Nodes[0].Choices[0].CustomColor = new TerminalTextColor();
        Check(session.Messages[0].TextColor.Equals(special), "special line lost its snapshotted color");
        session.Choose("scan"); Check(session.Messages[1].TextColor.Equals(reply), "player reply lost special color");
        session.Advance(); Check(session.Messages[2].TextColor.Equals(global), "disabled override did not inherit global");
        session.Advance(); session.PostSystemMessage("Status update.");
        Check(session.Messages[3].TextColor.Equals(global), "system notification did not inherit global");
        var plain = Waiting(Graph()); plain.Choose("scan");
        Check(plain.Messages[0].TextColor.Equals(TerminalTextColor.Default)
            && plain.Messages[1].TextColor.Equals(TerminalTextColor.Default), "legacy dialogue colors changed");
    }
    private static void DisplayNames()
    {
        Check(new TerminalMessage(TerminalSpeaker.AI, "Hello.").GetPrefix("SHIP AI") == "[SHIP AI] ", "default AI prefix changed");
        Check(new TerminalMessage(TerminalSpeaker.System, "Ready.").GetPrefix("SHIP AI") == "[SYSTEM] ", "default system prefix changed");
        Check(new TerminalMessage(TerminalSpeaker.Player, "Go.").GetPrefix("SHIP AI") == "> ", "default player prefix changed");
        Check(new TerminalMessage(TerminalSpeaker.System, "Ready.", TerminalTextColor.Default, "  ").GetPrefix("SHIP AI") == "[SYSTEM] ", "whitespace did not use default");
        var graph = Graph();
        graph.Nodes[0].Lines[0].Speaker = TerminalSpeaker.System;
        graph.Nodes[0].Lines[0].DisplayName = " NAVIGATION ";
        graph.Nodes[0].Choices[0].DisplayName = "CAPTAIN";
        graph.Nodes[1].Lines[0].DisplayName = "RELAY";
        var session = Waiting(graph);
        graph.AIName = "Changed"; graph.Nodes[0].Lines[0].DisplayName = "Changed";
        graph.Nodes[0].Choices[0].DisplayName = "Changed"; graph.Nodes[1].Lines[0].DisplayName = "Changed";
        Check(session.Messages[0].GetPrefix(session.AIName) == "[NAVIGATION] " && session.Messages[0].Speaker == TerminalSpeaker.System,
            "custom system name changed classification or lost its snapshot");
        session.Choose("scan");
        Check(session.Messages[1].GetPrefix(session.AIName) == "[CAPTAIN] " && session.Messages[1].Speaker == TerminalSpeaker.Player,
            "custom reply name changed classification or lost its snapshot");
        session.Advance();
        Check(session.Messages[2].GetPrefix(session.AIName) == "[RELAY] ", "future line name was not snapshotted");
        session.Advance(); session.PostSystemMessage("Status update.");
        Check(session.Messages[3].GetPrefix(session.AIName) == "[SYSTEM] ", "custom name leaked into system notifications");
    }
    private static void BoundedHistory()
    {
        var graph = Graph(); graph.Nodes[1].NextNodeId = "start";
        var session = new TerminalDialogueSession(graph, 4); session.Start();
        for (var i = 0; i < 30; i++)
        {
            session.Tick(1, 0, 0);
            if (session.Phase == TerminalPhase.WaitingForChoice) session.Choose("scan");
        }
        Check(session.Messages.Count == 4, "history not capped");
    }
    private static void ValidateBadGraphs()
    {
        var graph = Graph(); graph.Nodes[0].Choices[0].NextNodeId = "missing";
        Check(graph.Validate().Count > 0, "missing target accepted");
        graph = Graph(); graph.Nodes.Add(new TerminalNode { Id = "start" });
        Check(graph.Validate().Count > 0, "duplicate node accepted");
        graph = new TerminalDialogueGraph { EntryNodeId = "loop" };
        graph.Nodes.Add(new TerminalNode { Id = "loop", NextNodeId = "loop" });
        Check(graph.Validate().Count > 0, "empty cycle accepted");
        try { new TerminalDialogueSession(graph); throw new Exception("invalid graph started"); }
        catch (ArgumentException) { }
    }
    private static void InvalidTiming()
    {
        var session = Waiting();
        try { session.Tick(float.NaN); throw new Exception("NaN accepted"); }
        catch (ArgumentOutOfRangeException) { }
    }

    private static void StoryReadingState()
    {
        var graph = Graph();
        graph.Nodes[0].PauseGameplayWhileReading = true;
        var session = new TerminalDialogueSession(graph);
        Check(!session.IsStoryReading, "idle conversation pauses gameplay");
        session.Start();
        Check(session.IsStoryReading, "story typing did not pause");
        graph.Nodes[0].PauseGameplayWhileReading = false;
        session.Advance();
        Check(session.IsStoryReading, "story choice did not pause or graph edit changed snapshot");
        session.Close();
        Check(!session.IsStoryReading, "closed terminal pauses gameplay");
        session.Open();
        Check(session.IsStoryReading, "reopening story failed to restore reading state");
        session.Choose("scan");
        Check(session.IsStoryReading, "story player reply stopped pause early");
        session.Advance();
        Check(!session.IsStoryReading, "operational node incorrectly pauses gameplay");
    }

    private static void InterruptPendingInput()
    {
        var session = Waiting();
        session.Choose("scan");
        session.PostSystemMessage("Stale warning");
        Check(!session.GoToNode("missing"), "invalid transition accepted");
        Check(session.GoToNode("result"), "valid transition rejected");
        Check(!session.GetFlag("scanned") && !session.TryDequeueEvent(out _), "interrupted choice committed");
        session.Advance();
        Check(session.Phase == TerminalPhase.Completed && session.Messages.Count == 3, "stale task or notification survived transition");
        Check(!session.TryDequeueEvent(out _), "cancelled event leaked later");
    }

    private static void InterruptWhileClosed()
    {
        var graph = Graph();
        graph.Nodes[1].PauseGameplayWhileReading = true;
        var session = Waiting(graph);
        session.SetFlag("external_flag", true);
        session.Close();
        Check(session.GoToNode("result"), "closed transition rejected");
        Check(!session.IsOpen && !session.IsStoryReading && session.Messages.Count == 1, "closed transition unexpectedly displayed");
        session.Open();
        Check(session.IsStoryReading && session.Messages.Count == 2, "closed transition failed to open target");
        Check(session.GetFlag("external_flag"), "transition lost external flag");
        session.Advance();
        Check(session.Phase == TerminalPhase.Completed, "ending did not complete");
    }
}
