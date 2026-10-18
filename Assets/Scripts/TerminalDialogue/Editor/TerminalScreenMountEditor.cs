using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LudumDare60.TerminalDialogue.Editor
{
    [CustomEditor(typeof(TerminalScreenMount))]
    public sealed class TerminalScreenMountEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("把 SCREEN_NAVIGATION（或它的 Screen_NAVIGATION 父节点）拖到「目标屏幕」。终端画面自动贴合，玩家回复仍显示在游戏画面上。", MessageType.Info);
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"), new GUIContent("脚本"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("screenCanvas"), new GUIContent("终端画面 Canvas"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("screenTarget"), new GUIContent("目标屏幕"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("curvedScreen"), new GUIContent("曲面贴合（推荐）"));
            if (serializedObject.FindProperty("curvedScreen").boolValue)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("textureWidth"), new GUIContent("屏幕纹理分辨率"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fill"), new GUIContent("屏幕填充比例"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("surfaceOffset"), new GUIContent("离表面距离（米）"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("flipFacing"), new GUIContent("翻转正反面"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("followTarget"), new GUIContent("随屏幕移动"));
            serializedObject.ApplyModifiedProperties();
            var mount = (TerminalScreenMount)target;
            if (GUILayout.Button("贴合目标屏幕", GUILayout.Height(30)))
            {
                if (mount.ScreenCanvas != null) Undo.RecordObject(mount.ScreenCanvas.transform, "Fit DOS terminal to display");
                if (!mount.RefreshScreen())
                    EditorUtility.DisplayDialog("没有找到屏幕", "请选择具体的显示面，或只有一个 SCREEN_ 显示面的屏幕父节点。", "确定");
                else
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(mount.ScreenCanvas.transform);
                    EditorSceneManager.MarkSceneDirty(mount.gameObject.scene);
                }
            }
            EditorGUILayout.HelpBox("凸起或弧面玻璃请启用「曲面贴合」：终端画面会沿原屏幕模型显示，覆盖原导航图。文字反了可切换「翻转正反面」。Conversation 用来选择对话内容。", MessageType.None);
        }
    }
}
