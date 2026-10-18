using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LudumDare60.TerminalDialogue.Editor
{
    [CustomEditor(typeof(TerminalDialogueDefinition))]
    public sealed class TerminalDialogueDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var graph = serializedObject.FindProperty("graph");
            EditorGUILayout.HelpBox("在一个资产里管理整段对话。节点 ID 用于连接，创建后尽量保持不变；标题可以随时修改。空的下一节点表示结束。", MessageType.Info);
            EditorGUILayout.PropertyField(graph.FindPropertyRelative("AIName"), new GUIContent("AI 名称"));
            DrawColor(graph.FindPropertyRelative("DefaultTextColor"), "全局默认文字颜色");
            EditorGUILayout.HelpBox("未启用特殊颜色的台词和玩家终端回复使用全局颜色。每句可单独覆盖，颜色同时应用到说话者前缀。修改后重新开始对话生效。", MessageType.None);
            DrawNodePopup(graph.FindPropertyRelative("EntryNodeId"), "入口节点", graph, false);
            EditorGUILayout.PropertyField(graph.FindPropertyRelative("InitialFlags"), new GUIContent("初始剧情变量"), true);
            var nodes = graph.FindPropertyRelative("Nodes");
            for (var i = 0; i < nodes.arraySize; i++)
            {
                var node = nodes.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                var title = node.FindPropertyRelative("Title").stringValue;
                node.isExpanded = EditorGUILayout.Foldout(node.isExpanded,
                    string.IsNullOrEmpty(title) ? node.FindPropertyRelative("Id").stringValue : title, true);
                var remove = GUILayout.Button("删除节点", GUILayout.Width(75));
                EditorGUILayout.EndHorizontal();
                if (remove)
                {
                    nodes.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndVertical();
                    break;
                }
                if (node.isExpanded)
                {
                    EditorGUILayout.PropertyField(node.FindPropertyRelative("Id"), new GUIContent("节点 ID"));
                    EditorGUILayout.PropertyField(node.FindPropertyRelative("Title"), new GUIContent("编辑标题"));
                    var lines = node.FindPropertyRelative("Lines");
                    EditorGUILayout.LabelField("台词（按顺序播放）", EditorStyles.boldLabel);
                    for (var j = 0; j < lines.arraySize; j++)
                    {
                        var line = lines.GetArrayElementAtIndex(j);
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        EditorGUILayout.PropertyField(line.FindPropertyRelative("Speaker"), new GUIContent("说话者"));
                        DrawDisplayName(line);
                        DrawText(line.FindPropertyRelative("Text"), "台词");
                        DrawColorOverride(line);
                        EditorGUILayout.PropertyField(line.FindPropertyRelative("WaitForContinue"), new GUIContent("等待点击继续"));
                        if (!line.FindPropertyRelative("WaitForContinue").boolValue)
                            EditorGUILayout.PropertyField(line.FindPropertyRelative("DelayAfter"), new GUIContent("播完后停顿（秒）"));
                        var delete = GUILayout.Button("删除这句");
                        EditorGUILayout.EndVertical();
                        if (delete) { lines.DeleteArrayElementAtIndex(j); break; }
                    }
                    if (GUILayout.Button("＋ 添加一句台词"))
                    {
                        lines.arraySize++;
                        var line = lines.GetArrayElementAtIndex(lines.arraySize - 1);
                        line.FindPropertyRelative("Speaker").enumValueIndex = 0;
                        line.FindPropertyRelative("DisplayName").stringValue = "";
                        line.FindPropertyRelative("Text").stringValue = "New AI line.";
                        ResetColorOverride(line);
                        line.FindPropertyRelative("WaitForContinue").boolValue = false;
                        line.FindPropertyRelative("DelayAfter").floatValue = 0.5f;
                    }
                    var choices = node.FindPropertyRelative("Choices");
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("玩家选项", EditorStyles.boldLabel);
                    for (var j = 0; j < choices.arraySize; j++)
                    {
                        var choice = choices.GetArrayElementAtIndex(j);
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        EditorGUILayout.PropertyField(choice.FindPropertyRelative("Id"), new GUIContent("选项 ID"));
                        EditorGUILayout.PropertyField(choice.FindPropertyRelative("Label"), new GUIContent("按钮文字"));
                        DrawText(choice.FindPropertyRelative("PlayerText"), "玩家输入到终端的完整回复");
                        DrawDisplayName(choice);
                        DrawColorOverride(choice);
                        DrawNodePopup(choice.FindPropertyRelative("NextNodeId"), "下一节点", graph, true);
                        EditorGUILayout.PropertyField(choice.FindPropertyRelative("Conditions"), new GUIContent("显示条件（全部满足）"), true);
                        EditorGUILayout.PropertyField(choice.FindPropertyRelative("SetFlags"), new GUIContent("提交后写入变量"), true);
                        EditorGUILayout.PropertyField(choice.FindPropertyRelative("Events"), new GUIContent("提交后触发事件 ID"), true);
                        var delete = GUILayout.Button("删除这个选项");
                        EditorGUILayout.EndVertical();
                        if (delete) { choices.DeleteArrayElementAtIndex(j); break; }
                    }
                    if (GUILayout.Button("＋ 添加玩家选项"))
                    {
                        choices.arraySize++;
                        var choice = choices.GetArrayElementAtIndex(choices.arraySize - 1);
                        choice.FindPropertyRelative("Id").stringValue = "choice_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        choice.FindPropertyRelative("Label").stringValue = "New response";
                        choice.FindPropertyRelative("PlayerText").stringValue = "Player response.";
                        choice.FindPropertyRelative("DisplayName").stringValue = "";
                        ResetColorOverride(choice);
                        choice.FindPropertyRelative("NextNodeId").stringValue = "";
                        choice.FindPropertyRelative("Conditions").ClearArray();
                        choice.FindPropertyRelative("SetFlags").ClearArray();
                        choice.FindPropertyRelative("Events").ClearArray();
                    }
                    if (choices.arraySize == 0) DrawNodePopup(node.FindPropertyRelative("NextNodeId"), "台词结束后自动前往", graph, true);
                    else EditorGUILayout.HelpBox("有选项时由选项决定跳转。若条件让全部选项隐藏，对话会等待外部更新剧情变量。", MessageType.None);
                }
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("＋ 创建对话节点"))
            {
                nodes.arraySize++;
                var node = nodes.GetArrayElementAtIndex(nodes.arraySize - 1);
                node.FindPropertyRelative("Id").stringValue = "node_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                node.FindPropertyRelative("Title").stringValue = "新节点";
                node.FindPropertyRelative("Lines").ClearArray();
                node.FindPropertyRelative("Choices").ClearArray();
                node.FindPropertyRelative("NextNodeId").stringValue = "";
                node.isExpanded = true;
                if (nodes.arraySize == 1) graph.FindPropertyRelative("EntryNodeId").stringValue = node.FindPropertyRelative("Id").stringValue;
            }
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space();
            var errors = ((TerminalDialogueDefinition)target).Graph.Validate();
            if (errors.Count == 0) EditorGUILayout.HelpBox("校验通过。", MessageType.Info);
            else foreach (var error in errors) EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private static void DrawText(SerializedProperty property, string label)
        {
            EditorGUILayout.LabelField(label);
            property.stringValue = EditorGUILayout.TextArea(property.stringValue, GUILayout.MinHeight(48));
        }

        private static void DrawDisplayName(SerializedProperty item)
        {
            EditorGUILayout.PropertyField(item.FindPropertyRelative("DisplayName"), new GUIContent("显示名字（可选）",
                "只填写名字，不用输入方括号。留空沿用默认：AI 使用顶部 AI 名称，System 显示 SYSTEM，玩家使用 >。填写后这一句显示为 [名字]，不改变说话者分类和字速。"));
        }

        private static void DrawColorOverride(SerializedProperty item)
        {
            var enabled = item.FindPropertyRelative("UseCustomColor");
            EditorGUILayout.PropertyField(enabled, new GUIContent("启用特殊颜色"));
            if (enabled.boolValue) DrawColor(item.FindPropertyRelative("CustomColor"), "特殊文字颜色");
        }

        private static void DrawColor(SerializedProperty property, string label)
        {
            var color = new Color(property.FindPropertyRelative("R").floatValue, property.FindPropertyRelative("G").floatValue,
                property.FindPropertyRelative("B").floatValue, property.FindPropertyRelative("A").floatValue);
            EditorGUI.BeginChangeCheck();
            color = EditorGUILayout.ColorField(new GUIContent(label), color, true, true, false);
            if (!EditorGUI.EndChangeCheck()) return;
            property.FindPropertyRelative("R").floatValue = color.r;
            property.FindPropertyRelative("G").floatValue = color.g;
            property.FindPropertyRelative("B").floatValue = color.b;
            property.FindPropertyRelative("A").floatValue = color.a;
        }

        private static void ResetColorOverride(SerializedProperty item)
        {
            // Unity duplicates the last array entry when adding: do not inherit its special-color toggle.
            item.FindPropertyRelative("UseCustomColor").boolValue = false;
            var color = item.FindPropertyRelative("CustomColor");
            var defaults = TerminalTextColor.Default;
            color.FindPropertyRelative("R").floatValue = defaults.R;
            color.FindPropertyRelative("G").floatValue = defaults.G;
            color.FindPropertyRelative("B").floatValue = defaults.B;
            color.FindPropertyRelative("A").floatValue = defaults.A;
        }

        private static void DrawNodePopup(SerializedProperty property, string label, SerializedProperty graph, bool allowEnd)
        {
            var labels = new List<string> { allowEnd ? "（结束对话）" : "（请选择入口）" };
            var ids = new List<string> { "" };
            var nodes = graph.FindPropertyRelative("Nodes");
            for (var i = 0; i < nodes.arraySize; i++)
            {
                var node = nodes.GetArrayElementAtIndex(i);
                var id = node.FindPropertyRelative("Id").stringValue;
                var title = node.FindPropertyRelative("Title").stringValue;
                ids.Add(id);
                labels.Add(title + " [" + id + "]");
            }
            var index = ids.IndexOf(property.stringValue);
            if (index < 0) { index = ids.Count; ids.Add(property.stringValue); labels.Add("缺失：" + property.stringValue); }
            property.stringValue = ids[EditorGUILayout.Popup(label, index, labels.ToArray())];
        }
    }
}
