using UnityEditor;
using UnityEngine;

namespace LudumDare60.AssemblyDemo.Editor
{
    [CustomEditor(typeof(AssemblyModule))]
    [CanEditMultipleObjects]
    public sealed class AssemblyModuleInspector : UnityEditor.Editor
    {
        private SerializedProperty moduleLabel;
        private SerializedProperty radius;
        private SerializedProperty portAngles;
        private SerializedProperty artwork, artworkHeight, artworkOffset, portDistances, portKeys;

        private void OnEnable()
        {
            moduleLabel = serializedObject.FindProperty("moduleLabel");
            radius = serializedObject.FindProperty("radius");
            portAngles = serializedObject.FindProperty("portAngles");
            artwork = serializedObject.FindProperty("artwork");
            artworkHeight = serializedObject.FindProperty("artworkHeight");
            artworkOffset = serializedObject.FindProperty("artworkOffset");
            portDistances = serializedObject.FindProperty("portDistances");
            portKeys = serializedObject.FindProperty("portKeys");
            Undo.undoRedoPerformed += RebuildTargets;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RebuildTargets;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(moduleLabel, new GUIContent("名称 / Module Label"));
            EditorGUILayout.PropertyField(radius, new GUIContent("半径 / Radius"));
            EditorGUILayout.PropertyField(portAngles, new GUIContent("角度数组 / Port Angles"), true);
            EditorGUILayout.PropertyField(portDistances, new GUIContent("接口距离 / Port Distances"), true);
            EditorGUILayout.PropertyField(portKeys, new GUIContent("接口匹配 / Port Keys"), true);
            EditorGUILayout.PropertyField(artwork, new GUIContent("部件图像 / Artwork"));
            if (artwork.objectReferenceValue != null || artwork.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(artworkHeight, new GUIContent("图像高度 / Artwork Height"));
                EditorGUILayout.PropertyField(artworkOffset, new GUIContent("图像偏移 / Artwork Offset"));
            }
            EditorGUILayout.HelpBox(
                "0° 指向正上方，顺时针为正（90° 向右、180° 向下、270° 向左）。\n" +
                "角度数组为空时默认一个 0° 接口。接口距离缺省时使用 Radius；相同匹配名称的接口可连接，空名称接受任意接口。\n" +
                "有图像时以图像高度等比缩放；无图像时显示白模。请保持 Transform Scale 为 (1, 1, 1)。",
                MessageType.Info);

            // SerializedObject records parameter edits for Undo and prefab overrides.
            // Geometry is a disposable preview and is regenerated after the edit.
            if (serializedObject.ApplyModifiedProperties())
                RebuildTargets();
        }

        private void RebuildTargets()
        {
            foreach (Object item in targets)
                if (item is AssemblyModule module && module != null)
                    module.Rebuild();
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }
    }
}
