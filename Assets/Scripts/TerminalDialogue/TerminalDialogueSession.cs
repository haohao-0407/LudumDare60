using System;
using System.Collections.Generic;
using System.Globalization;

namespace LudumDare60.TerminalDialogue
{
    /// <summary>Pure dialogue runner. No Unity, rendering, keyboard input or gameplay side effects.</summary>
    public sealed class TerminalDialogueSession
    {
        private enum TaskKind { Line, EnterNode, Choices, CommitChoice, End }
        private sealed class Task
        {
            public TaskKind Kind;
            public TerminalLine Line;
            public string NodeId;
            public TerminalChoice Choice;
        }

        private TerminalDialogueGraph graph;
        private readonly Dictionary<string, bool> flags = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Queue<Task> tasks = new Queue<Task>();
        private readonly Queue<string> events = new Queue<string>();
        private readonly Queue<string> notifications = new Queue<string>();
        private readonly List<TerminalMessage> messages = new List<TerminalMessage>();
        private readonly List<TerminalChoice> choices = new List<TerminalChoice>();
        private readonly IReadOnlyList<TerminalMessage> messageView;
        private readonly IReadOnlyList<TerminalChoice> choiceView;
        private readonly int historyLimit;
        private TerminalLine activeLine;
        private int[] textElements = Array.Empty<int>();
        private double typedElements;
        private double holdRemaining;

        public TerminalPhase Phase { get; private set; }
        public bool IsOpen { get; private set; }
        public string CurrentNodeId { get; private set; }
        public bool IsStoryReading => IsOpen && graph.FindNode(CurrentNodeId)?.PauseGameplayWhileReading == true;
        public string AIName => graph.AIName;

        /// <summary>台词里的 {callsign} 占位符填成呼号。舰名保持不变，所以中英文用同一个值。</summary>
        public string Callsign = "Pioneer-60";
        public IReadOnlyList<TerminalMessage> Messages => messageView;
        public IReadOnlyList<TerminalChoice> Choices => choiceView;
        public int VisibleCodeUnits { get; private set; }
        public bool HasActiveLine => activeLine != null;
        public int Revision { get; private set; }
        public int TranscriptRevision { get; private set; }
        public int OptionsRevision { get; private set; }

        public TerminalDialogueSession(TerminalDialogueGraph source, int historyLimit = 150)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var errors = source.Validate();
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors), nameof(source));
            // Snapshot authored data: editing an asset during play must not alter an in-flight choice.
            graph = CopyGraph(source);
            messageView = messages.AsReadOnly();
            choiceView = choices.AsReadOnly();
            this.historyLimit = Math.Max(2, historyLimit);
            if (graph.InitialFlags != null)
                foreach (var flag in graph.InitialFlags) flags[flag.Key] = flag.Value;
        }

        public void Start()
        {
            if (Phase != TerminalPhase.Idle || tasks.Count != 0 || messages.Count != 0)
                throw new InvalidOperationException("Create a new session to restart a conversation.");
            IsOpen = true;
            tasks.Enqueue(new Task { Kind = TaskKind.EnterNode, NodeId = graph.EntryNodeId });
            Touch();
            Pump();
        }

        public void Open() { if (IsOpen) return; IsOpen = true; Touch(); Pump(); }
        public void Close() { if (!IsOpen) return; IsOpen = false; Touch(); }

        /// <summary>Interrupts pending dialogue, preserving history and flags. For mission transitions.</summary>
        public bool GoToNode(string nodeId)
        {
            if (graph.FindNode(nodeId) == null) return false;
            tasks.Clear();
            events.Clear();
            notifications.Clear();
            choices.Clear();
            OptionsRevision++;
            // Complete the displayed line before moving on, without committing its pending choice.
            if (activeLine != null) VisibleCodeUnits = activeLine.Text.Length;
            activeLine = null;
            Phase = TerminalPhase.Idle;
            CurrentNodeId = nodeId;
            tasks.Enqueue(new Task { Kind = TaskKind.EnterNode, NodeId = nodeId });
            Touch();
            Pump();
            return true;
        }
        public bool GetFlag(string key) => key != null && flags.TryGetValue(key, out var value) && value;

        public void SetFlag(string key, bool value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Flag key is required.", nameof(key));
            flags[key] = value;
            if (Phase == TerminalPhase.WaitingForChoice) RefreshChoices();
            Touch();
        }

        public bool Choose(string choiceId)
        {
            if (!IsOpen || Phase != TerminalPhase.WaitingForChoice) return false;
            var choice = choices.Find(item => item.Id == choiceId);
            if (choice == null || !MeetsConditions(choice)) return false;
            choices.Clear();
            OptionsRevision++;
            tasks.Enqueue(new Task { Kind = TaskKind.Line, Line = new TerminalLine
                { Speaker = TerminalSpeaker.Player, Text = choice.PlayerText, DelayAfter = 0,
                    DisplayName = choice.DisplayName, UseCustomColor = choice.UseCustomColor, CustomColor = choice.CustomColor } });
            tasks.Enqueue(new Task { Kind = TaskKind.CommitChoice, Choice = choice });
            Phase = TerminalPhase.Idle;
            Touch();
            Pump();
            return true;
        }

        public void Tick(float deltaSeconds, float aiCharactersPerSecond = 30, float playerCharactersPerSecond = 55)
        {
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (!IsOpen) return;
            if (Phase == TerminalPhase.Typing)
            {
                var speed = activeLine.Speaker == TerminalSpeaker.Player ? playerCharactersPerSecond : aiCharactersPerSecond;
                if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0) throw new ArgumentOutOfRangeException(nameof(speed));
                // Zero speed means instant output. Consume at most one line per Tick for readable ordering.
                typedElements = speed == 0 ? textElements.Length : Math.Min(textElements.Length, typedElements + deltaSeconds * speed);
                var count = (int)typedElements;
                var visible = count >= textElements.Length ? activeLine.Text.Length : textElements[count];
                if (VisibleCodeUnits != visible) { VisibleCodeUnits = visible; Touch(); }
                if (count >= textElements.Length) FinishTyping();
            }
            else if (Phase == TerminalPhase.Holding)
            {
                holdRemaining -= deltaSeconds;
                if (holdRemaining <= 0) CompleteLine();
            }
        }

        public void Advance()
        {
            if (!IsOpen) return;
            if (Phase == TerminalPhase.Typing)
            {
                VisibleCodeUnits = activeLine.Text.Length;
                FinishTyping();
            }
            else if (Phase == TerminalPhase.WaitingForContinue || Phase == TerminalPhase.Holding) CompleteLine();
        }

        /// <summary>Queued between lines; never cuts a player's input or an AI line in half.</summary>
        /// <summary>
        /// 中途切换语言：把已经写进转写的台词、当前正在打的那一行、以及还没播的节点全部换成新语言。
        /// 不重置进度、变量和任务队列，玩家的选择与历史都保留。
        /// </summary>
        public void Relocalize(TerminalDialogueGraph localized, System.Func<string, string> translate)
        {
            if (translate == null) return;
            if (localized != null) graph = localized;

            // 词典里的中英文都带 {callsign} 占位符，翻译完必须再填一次值，
            // 否则切语言后会把已经填好的呼号重新变成占位符。
            if (!string.IsNullOrEmpty(Callsign))
            {
                var raw = translate;
                translate = s =>
                {
                    var value = raw(s);
                    return value != null ? value.Replace("{callsign}", Callsign) : null;
                };
            }

            for (int i = 0; i < messages.Count; i++) messages[i].Relocalize(translate);

            if (activeLine != null)
            {
                int oldLength = activeLine.Text != null ? activeLine.Text.Length : 0;
                bool fullyTyped = VisibleCodeUnits >= oldLength;
                var lineText = translate(activeLine.Text);
                if (!string.IsNullOrEmpty(lineText)) activeLine.Text = lineText;
                var lineName = translate(activeLine.DisplayName);
                if (!string.IsNullOrEmpty(lineName)) activeLine.DisplayName = lineName;
                textElements = StringInfo.ParseCombiningCharacters(activeLine.Text);
                VisibleCodeUnits = fullyTyped
                    ? activeLine.Text.Length
                    : System.Math.Min(VisibleCodeUnits, activeLine.Text.Length);
            }

            // 队列里还没播的台词引用的仍是切换前那份图的 TerminalLine，不换语言就会继续吐旧语言。
            foreach (var task in tasks)
            {
                if (task == null || task.Kind != TaskKind.Line || task.Line == null) continue;
                var queuedText = translate(task.Line.Text);
                if (!string.IsNullOrEmpty(queuedText)) task.Line.Text = queuedText;
                var queuedName = translate(task.Line.DisplayName);
                if (!string.IsNullOrEmpty(queuedName)) task.Line.DisplayName = queuedName;
            }

            // 待播的系统通知是已经翻译过的字符串，靠词典的双向查表换回另一种语言。
            if (notifications.Count > 0)
            {
                var pending = notifications.ToArray();
                notifications.Clear();
                foreach (var note in pending) notifications.Enqueue(translate(note) ?? note);
            }

            RefreshChoices();
            TranscriptRevision++;
            Touch();
        }

        public void PostSystemMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            notifications.Enqueue(text);
            if (Phase == TerminalPhase.WaitingForChoice)
            {
                choices.Clear();
                OptionsRevision++;
                tasks.Enqueue(new Task { Kind = TaskKind.Choices });
                Phase = TerminalPhase.Idle;
            }
            else if (Phase == TerminalPhase.Completed)
            {
                tasks.Enqueue(new Task { Kind = TaskKind.End });
                Phase = TerminalPhase.Idle;
            }
            Touch();
            Pump();
        }

        public bool TryDequeueEvent(out string eventId)
        {
            if (events.Count > 0) { eventId = events.Dequeue(); return true; }
            eventId = null;
            return false;
        }

        private void Pump()
        {
            var transitions = 0;
            while (IsOpen && Phase == TerminalPhase.Idle)
            {
                if (++transitions > 512) throw new InvalidOperationException("Too many automatic node transitions.");
                // Commit input before showing asynchronous notifications.
                if (notifications.Count > 0 && (tasks.Count == 0 || tasks.Peek().Kind != TaskKind.CommitChoice))
                {
                    BeginLine(new TerminalLine { Speaker = TerminalSpeaker.System, Text = notifications.Dequeue(), DelayAfter = 0.4f });
                    break;
                }
                if (tasks.Count == 0) { Phase = TerminalPhase.Completed; Touch(); break; }
                var task = tasks.Dequeue();
                switch (task.Kind)
                {
                    case TaskKind.Line: BeginLine(task.Line); break;
                    case TaskKind.EnterNode:
                        if (string.IsNullOrEmpty(task.NodeId)) { Phase = TerminalPhase.Completed; Touch(); break; }
                        var node = graph.FindNode(task.NodeId);
                        CurrentNodeId = node.Id;
                        if (node.Lines != null)
                            foreach (var line in node.Lines) tasks.Enqueue(new Task { Kind = TaskKind.Line, Line = line });
                        tasks.Enqueue(new Task { Kind = TaskKind.Choices });
                        break;
                    case TaskKind.Choices:
                        var current = graph.FindNode(CurrentNodeId);
                        if (current != null && current.Choices != null && current.Choices.Count > 0)
                        {
                            Phase = TerminalPhase.WaitingForChoice;
                            RefreshChoices();
                        }
                        else tasks.Enqueue(new Task { Kind = TaskKind.EnterNode, NodeId = current?.NextNodeId });
                        Touch();
                        break;
                    case TaskKind.CommitChoice:
                        // Each selected choice reaches this task exactly once, after its text finishes.
                        if (task.Choice.SetFlags != null)
                            foreach (var flag in task.Choice.SetFlags) flags[flag.Key] = flag.Value;
                        if (task.Choice.Events != null)
                            foreach (var id in task.Choice.Events) events.Enqueue(id);
                        tasks.Enqueue(new Task { Kind = TaskKind.EnterNode, NodeId = task.Choice.NextNodeId });
                        break;
                    case TaskKind.End: Phase = TerminalPhase.Completed; Touch(); break;
                }
            }
        }

        private void BeginLine(TerminalLine line)
        {
            // {callsign} 占位符在这里替换：词典里中英文都用同一个占位符，之前没有代码负责填值。
            // 复制一份再改，避免默认语言下 graph 直接引用了资产里的原对象。
            if (Callsign != null && line.Text != null && line.Text.IndexOf("{callsign}", System.StringComparison.Ordinal) >= 0)
            {
                line = new TerminalLine
                {
                    Speaker = line.Speaker,
                    Text = line.Text.Replace("{callsign}", Callsign),
                    DisplayName = line.DisplayName,
                    UseCustomColor = line.UseCustomColor,
                    CustomColor = line.CustomColor,
                    WaitForContinue = line.WaitForContinue,
                    DelayAfter = line.DelayAfter
                };
            }
            activeLine = line;
            messages.Add(new TerminalMessage(line.Speaker, line.Text,
                line.UseCustomColor ? line.CustomColor : graph.DefaultTextColor, line.DisplayName));
            if (messages.Count > historyLimit) messages.RemoveAt(0);
            TranscriptRevision++;
            textElements = StringInfo.ParseCombiningCharacters(line.Text);
            typedElements = 0;
            VisibleCodeUnits = 0;
            Phase = TerminalPhase.Typing;
            Touch();
        }

        private void FinishTyping()
        {
            VisibleCodeUnits = activeLine.Text.Length;
            if (activeLine.WaitForContinue) Phase = TerminalPhase.WaitingForContinue;
            else if (activeLine.DelayAfter > 0) { Phase = TerminalPhase.Holding; holdRemaining = activeLine.DelayAfter; }
            else { CompleteLine(); return; }
            Touch();
        }

        private void CompleteLine()
        {
            activeLine = null;
            Phase = TerminalPhase.Idle;
            Touch();
            Pump();
        }

        private void RefreshChoices()
        {
            choices.Clear();
            var node = graph.FindNode(CurrentNodeId);
            if (node?.Choices != null)
                foreach (var choice in node.Choices) if (MeetsConditions(choice)) choices.Add(choice);
            OptionsRevision++;
        }

        private bool MeetsConditions(TerminalChoice choice)
        {
            if (choice.Conditions != null)
                foreach (var condition in choice.Conditions)
                    if (GetFlag(condition.Key) != condition.Value) return false;
            return true;
        }

        private void Touch() => Revision++;

        private static List<TerminalFlag> CopyFlags(List<TerminalFlag> source)
        {
            var result = new List<TerminalFlag>();
            if (source != null) foreach (var f in source) result.Add(new TerminalFlag { Key = f.Key, Value = f.Value });
            return result;
        }

        private static TerminalDialogueGraph CopyGraph(TerminalDialogueGraph source)
        {
            var copy = new TerminalDialogueGraph { AIName = source.AIName, DefaultTextColor = source.DefaultTextColor, EntryNodeId = source.EntryNodeId,
                InitialFlags = CopyFlags(source.InitialFlags) };
            foreach (var node in source.Nodes)
            {
                var n = new TerminalNode { Id = node.Id, Title = node.Title, NextNodeId = node.NextNodeId,
                    PauseGameplayWhileReading = node.PauseGameplayWhileReading };
                if (node.Lines != null)
                    foreach (var line in node.Lines) n.Lines.Add(new TerminalLine { Speaker = line.Speaker,
                        Text = line.Text, DisplayName = line.DisplayName, WaitForContinue = line.WaitForContinue, DelayAfter = line.DelayAfter,
                        UseCustomColor = line.UseCustomColor, CustomColor = line.CustomColor });
                if (node.Choices != null)
                    foreach (var c in node.Choices) n.Choices.Add(new TerminalChoice { Id = c.Id, Label = c.Label,
                        PlayerText = c.PlayerText, DisplayName = c.DisplayName, UseCustomColor = c.UseCustomColor, CustomColor = c.CustomColor,
                        NextNodeId = c.NextNodeId, Conditions = CopyFlags(c.Conditions),
                        SetFlags = CopyFlags(c.SetFlags), Events = c.Events == null ? new List<string>() : new List<string>(c.Events) });
                copy.Nodes.Add(n);
            }
            return copy;
        }
    }
}
