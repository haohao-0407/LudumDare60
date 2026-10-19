using System.Collections.Generic;
using LudumDare60.TerminalDialogue;
using Pioneer60.Localization;

namespace LudumDare60.Localization
{
    /// <summary>
    /// 对话图本地化。
    ///
    /// 对话正文存在 TerminalDialogueDefinition 资产里（Text / Label / PlayerText / DisplayName / Title），
    /// 没有 key。这里在开局时把整张图深拷贝一份，按「英文原文」逐条查词典后替换。
    ///
    /// 为什么在开局时翻而不是渲染时翻：转写区是靠字符数做打字机效果的
    /// （session 记的是 line.Text.Length），渲染时换文本会让字数对不上；
    /// 提前克隆出译文，session 直接从译文里算长度，打字机、选项、历史记录都不用改。
    /// 代价：中途切语言不会重写已经开始的这场对话，下一次开场才生效。
    /// </summary>
    public static class TerminalDialogueLocalizer
    {
        public static TerminalDialogueGraph Localize(TerminalDialogueGraph source)
        {
            if (source == null) return null;
            if (Loc.IsDefaultLanguage) return source;

            var copy = new TerminalDialogueGraph
            {
                AIName = Loc.Say(source.AIName),
                DefaultTextColor = source.DefaultTextColor,
                EntryNodeId = source.EntryNodeId,
                InitialFlags = CloneFlags(source.InitialFlags),
                Nodes = new List<TerminalNode>(source.Nodes != null ? source.Nodes.Count : 0)
            };
            if (source.Nodes == null) return copy;

            foreach (var node in source.Nodes)
            {
                if (node == null) continue;
                var n = new TerminalNode
                {
                    Id = node.Id,
                    Title = Loc.Say(node.Title),
                    PauseGameplayWhileReading = node.PauseGameplayWhileReading,
                    NextNodeId = node.NextNodeId,
                    Lines = new List<TerminalLine>(node.Lines != null ? node.Lines.Count : 0),
                    Choices = new List<TerminalChoice>(node.Choices != null ? node.Choices.Count : 0)
                };
                if (node.Lines != null)
                    foreach (var line in node.Lines)
                    {
                        if (line == null) continue;
                        n.Lines.Add(new TerminalLine
                        {
                            Speaker = line.Speaker,
                            DisplayName = Loc.Say(line.DisplayName),
                            Text = Loc.Say(line.Text),
                            UseCustomColor = line.UseCustomColor,
                            CustomColor = line.CustomColor,
                            WaitForContinue = line.WaitForContinue,
                            DelayAfter = line.DelayAfter
                        });
                    }
                if (node.Choices != null)
                    foreach (var choice in node.Choices)
                    {
                        if (choice == null) continue;
                        n.Choices.Add(new TerminalChoice
                        {
                            Id = choice.Id,
                            Label = Loc.Say(choice.Label),
                            PlayerText = Loc.Say(choice.PlayerText),
                            DisplayName = Loc.Say(choice.DisplayName),
                            UseCustomColor = choice.UseCustomColor,
                            CustomColor = choice.CustomColor,
                            NextNodeId = choice.NextNodeId,
                            Conditions = CloneFlags(choice.Conditions),
                            SetFlags = CloneFlags(choice.SetFlags),
                            Events = choice.Events != null ? new List<string>(choice.Events) : new List<string>()
                        });
                    }
                copy.Nodes.Add(n);
            }
            return copy;
        }

        private static List<TerminalFlag> CloneFlags(List<TerminalFlag> flags)
        {
            var list = new List<TerminalFlag>(flags != null ? flags.Count : 0);
            if (flags == null) return list;
            foreach (var f in flags)
                if (f != null) list.Add(new TerminalFlag { Key = f.Key, Value = f.Value });
            return list;
        }
    }
}
