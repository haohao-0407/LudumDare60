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

        private void OnEnable()
        {
            moduleLabel = serializedObject.FindProperty("moduleLabel");
            radius = serializedObject.FindProperty("radius");
            portAngles = serializedObject.FindProperty("portAngles");
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
            EditorGUILayout.HelpBox(
                "0° 指向正上方，顺时针为正（90° 向右、180° 向下、270° 向左）。\n" +
                "每个角度生成一个从圆心指向圆周的箭头；数组为空时默认一个 0° 箭头。\n" +
                "尺寸请使用 Radius 调整，并保持 Transform Scale 为 (1, 1, 1)。",
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
