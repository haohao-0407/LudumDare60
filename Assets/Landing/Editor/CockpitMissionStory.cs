using System;
using System.Collections.Generic;
using LudumDare60.TerminalDialogue;
using UnityEditor;
using UnityEngine;

namespace LudumDare60.Landing.Editor
{
    /// <summary>Connects the authored mission rules and timeout ending to the SHIP conversation.</summary>
    public static class CockpitMissionStory
    {
        public const string ExpiredEntryNodeId = "another_tomorrow_open";
        const string ExpiredStoryPath = "Assets/Dialogue/pioneer60/Endings/AnotherTomorrow.asset";

        public static void Configure(TerminalDialogueDefinition story)
        {
            if (!story || story.Graph == null)
                throw new ArgumentException("The SHIP mission needs its opening conversation.", nameof(story));

            var ending = AssetDatabase.LoadAssetAtPath<TerminalDialogueDefinition>(ExpiredStoryPath);
            if (!ending || ending.Graph == null || ending.Graph.FindNode(ExpiredEntryNodeId) == null)
                throw new InvalidOperationException("Missing authored return-window ending: " + ExpiredStoryPath);

            Validate(story.Graph, "SHIP opening");
            Validate(ending.Graph, "Return-window ending");

            // Build and validate a separate graph before changing the saved opening asset.
            // Keeping the exact authored lines also preserves their localization dictionary keys.
            var configured = Clone(story.Graph);
            var endingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in ending.Graph.Nodes) endingIds.Add(node.Id);
            configured.Nodes.RemoveAll(node => endingIds.Contains(node.Id));
            foreach (var node in ending.Graph.Nodes) configured.Nodes.Add(Clone(node));

            foreach (var node in configured.Nodes)
            {
                if (IsStoryNode(node.Id))
                    node.PauseGameplayWhileReading = true;
                else if (IsOperationalNode(node.Id))
                    node.PauseGameplayWhileReading = false;
            }

            Validate(configured, "Configured SHIP mission");
            Undo.RecordObject(story, "Connect SHIP mission reading and timeout story");
            story.Graph.Nodes = configured.Nodes;
            EditorUtility.SetDirty(story);
        }

        static bool IsStoryNode(string id)
        {
            return id.StartsWith("log_", StringComparison.Ordinal)
                || id.StartsWith("briefing_", StringComparison.Ordinal)
                || id.StartsWith("another_tomorrow_", StringComparison.Ordinal)
                || id == "landing_crash_report";
        }

        static bool IsOperationalNode(string id)
        {
            return id.StartsWith("departure_", StringComparison.Ordinal)
                || id.StartsWith("launch_", StringComparison.Ordinal)
                || id == "landing_descent"
                || id == "landing_retry"
                || id == "operations_menu"
                || id == "salvage_online";
        }

        static T Clone<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));

        static void Validate(TerminalDialogueGraph graph, string label)
        {
            var errors = graph.Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException(label + " conversation is invalid:\n" + string.Join("\n", errors));
        }
    }
}
