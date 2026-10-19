using System;
using System.Collections.Generic;

namespace LudumDare60.TerminalDialogue
{
    public enum TerminalSpeaker { AI, Player, System }
    public enum TerminalPhase { Idle, Typing, Holding, WaitingForContinue, WaitingForChoice, Completed }

    // Kept independent of UnityEngine so authored colors can also be used by the pure session runner.
    [Serializable]
    public struct TerminalTextColor
    {
        public float R, G, B, A;
        public TerminalTextColor(float r, float g, float b, float a = 1) { R = r; G = g; B = b; A = a; }
        public static TerminalTextColor Default => new TerminalTextColor(0.65f, 1, 0.76f);
    }

    [Serializable]
    public sealed class TerminalFlag
    {
        public string Key = "";
        public bool Value = true;
    }

    [Serializable]
    public sealed class TerminalLine
    {
        public TerminalSpeaker Speaker = TerminalSpeaker.AI;
        public string DisplayName = "";
        public string Text = "";
        public bool UseCustomColor;
        public TerminalTextColor CustomColor = TerminalTextColor.Default;
        public bool WaitForContinue;
        public float DelayAfter = 0.5f;
    }

    [Serializable]
    public sealed class TerminalChoice
    {
        public string Id = "";
        public string Label = "";
        public string PlayerText = "";
        public string DisplayName = "";
        public bool UseCustomColor;
        public TerminalTextColor CustomColor = TerminalTextColor.Default;
        public string NextNodeId = "";
        public List<TerminalFlag> Conditions = new List<TerminalFlag>();
        public List<TerminalFlag> SetFlags = new List<TerminalFlag>();
        public List<string> Events = new List<string>();
    }

    [Serializable]
    public sealed class TerminalNode
    {
        public string Id = "";
        public string Title = "";
        // Only explicit story-reading nodes freeze gameplay; operational menus stay live.
        public bool PauseGameplayWhileReading;
        public List<TerminalLine> Lines = new List<TerminalLine>();
        public List<TerminalChoice> Choices = new List<TerminalChoice>();
        // Used only when this node has no authored choices. Empty means end.
        public string NextNodeId = "";
    }

    [Serializable]
    public sealed class TerminalDialogueGraph
    {
        public string AIName = "SHIP AI";
        public TerminalTextColor DefaultTextColor = TerminalTextColor.Default;
        public string EntryNodeId = "";
        public List<TerminalFlag> InitialFlags = new List<TerminalFlag>();
        public List<TerminalNode> Nodes = new List<TerminalNode>();

        public TerminalNode FindNode(string id) => Nodes?.Find(node => node != null && node.Id == id);

        public List<string> Validate()
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (Nodes == null || Nodes.Count == 0) errors.Add("至少需要一个对话节点。");
            if (Nodes == null) return errors;
            foreach (var node in Nodes)
            {
                if (node == null) { errors.Add("存在空节点。"); continue; }
                if (string.IsNullOrWhiteSpace(node.Id) || !ids.Add(node.Id))
                    errors.Add("节点 ID 为空或重复：" + node.Id);
            }
            if (string.IsNullOrWhiteSpace(EntryNodeId) || !ids.Contains(EntryNodeId))
                errors.Add("入口节点不存在：" + EntryNodeId);
            ValidateFlags(InitialFlags, "初始变量", errors);
            foreach (var node in Nodes)
            {
                if (node == null) continue;
                ValidateTarget(node.NextNodeId, node.Id, ids, errors);
                if (node.Lines != null)
                    foreach (var line in node.Lines)
                    {
                        if (line == null) { errors.Add(node.Id + "：存在空台词。"); continue; }
                        if (string.IsNullOrWhiteSpace(line.Text)) errors.Add(node.Id + "：台词不能为空。");
                        if (float.IsNaN(line.DelayAfter) || float.IsInfinity(line.DelayAfter) || line.DelayAfter < 0)
                            errors.Add(node.Id + "：停顿时间必须为非负有限数。");
                    }
                var choiceIds = new HashSet<string>(StringComparer.Ordinal);
                if (node.Choices == null) continue;
                foreach (var choice in node.Choices)
                {
                    if (choice == null) { errors.Add(node.Id + "：存在空选项。"); continue; }
                    if (string.IsNullOrWhiteSpace(choice.Id) || !choiceIds.Add(choice.Id))
                        errors.Add(node.Id + "：选项 ID 为空或重复：" + choice.Id);
                    if (string.IsNullOrWhiteSpace(choice.Label) || string.IsNullOrWhiteSpace(choice.PlayerText))
                        errors.Add(node.Id + "：选项按钮文字和玩家台词都必须填写。");
                    ValidateTarget(choice.NextNodeId, node.Id + "/" + choice.Id, ids, errors);
                    ValidateFlags(choice.Conditions, node.Id + "/条件", errors);
                    ValidateFlags(choice.SetFlags, node.Id + "/写入变量", errors);
                    if (choice.Events != null)
                        foreach (var eventId in choice.Events)
                            if (string.IsNullOrWhiteSpace(eventId)) errors.Add(node.Id + "：事件 ID 不能为空。");
                }
            }
            // Empty automatic cycles would spin without ever displaying a line or a choice.
            foreach (var start in Nodes)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var node = start;
                while (node != null && (node.Lines == null || node.Lines.Count == 0)
                    && (node.Choices == null || node.Choices.Count == 0))
                {
                    if (!visited.Add(node.Id)) { errors.Add("存在无台词的自动跳转循环：" + node.Id); break; }
                    node = FindNode(node.NextNodeId);
                }
            }
            return errors;
        }

        private static void ValidateTarget(string id, string owner, HashSet<string> ids, List<string> errors)
        {
            if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) errors.Add(owner + "：跳转节点不存在：" + id);
        }

        private static void ValidateFlags(List<TerminalFlag> flags, string owner, List<string> errors)
        {
            if (flags == null) return;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var flag in flags)
                if (flag == null || string.IsNullOrWhiteSpace(flag.Key) || !keys.Add(flag.Key))
                    errors.Add(owner + "：变量名为空或重复。");
        }
    }

    public sealed class TerminalMessage
    {
        public TerminalSpeaker Speaker { get; }
        public string Text { get; }
        public TerminalTextColor TextColor { get; }
        public string DisplayName { get; }
        public TerminalMessage(TerminalSpeaker speaker, string text) : this(speaker, text, TerminalTextColor.Default) { }
        public TerminalMessage(TerminalSpeaker speaker, string text, TerminalTextColor color, string displayName = "")
        {
            Speaker = speaker; Text = text; TextColor = color;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "" : displayName.Trim();
        }

        public string GetPrefix(string aiName)
        {
            if (DisplayName.Length > 0) return "[" + DisplayName + "] ";
            if (Speaker == TerminalSpeaker.Player) return "> ";
            return "[" + (Speaker == TerminalSpeaker.System ? "SYSTEM" : aiName) + "] ";
        }
    }
}
