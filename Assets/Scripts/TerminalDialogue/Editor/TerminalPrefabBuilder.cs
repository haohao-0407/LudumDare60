using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LudumDare60.TerminalDialogue.Editor
{
    public static class TerminalPrefabBuilder
    {
        public const string PrefabPath = "Assets/Prefabs/TerminalDialogue/DOS Terminal System.prefab";
        private const string DemoPath = "Assets/Scenes/TerminalDialogueDemo.unity";

        [MenuItem("Tools/Terminal Dialogue/从已保存演示场景制作 DOS 预制体")]
        public static void CreatePrefab()
        {
            var path = AssetDatabase.GenerateUniqueAssetPath(PrefabPath);
            Build(path);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        public static void Build(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play 模式。");
            EnsureFolder("Assets/Prefabs/TerminalDialogue");
            var preview = EditorSceneManager.OpenPreviewScene(DemoPath);
            try
            {
                var controller = preview.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TerminalDialogueController>(true)).Single();
                var root = controller.gameObject;
                root.name = "DOS Terminal System";
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                var screen = root.GetComponentInChildren<TerminalTranscriptView>(true).GetComponent<Canvas>();
                if (screen == null) throw new InvalidOperationException("演示终端缺少世界空间 Canvas。");
                screen.worldCamera = null;
                var rect = (RectTransform)screen.transform;
                rect.localPosition = Vector3.zero;
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one * 0.001f;
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.worldCamera = null;
                var mount = root.GetComponent<TerminalScreenMount>() ?? root.AddComponent<TerminalScreenMount>();
                mount.Configure(screen);
                // The controller, transcript, choices and font references are retained from the authored demo.
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new InvalidOperationException("无法保存 DOS 预制体。");
                AssetDatabase.SaveAssets();
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        [MenuItem("GameObject/Terminal Dialogue/添加 DOS 终端到所选屏幕", false, 20)]
        private static void AddToScreen()
        {
            var selected = Selection.activeTransform;
            if (selected == null || !selected.gameObject.scene.IsValid() || EditorUtility.IsPersistent(selected)) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Build(PrefabPath); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, selected.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(instance, "Add DOS terminal to screen");
            var mount = instance.GetComponent<TerminalScreenMount>();
            Undo.RecordObject(mount, "Set terminal display");
            if (!mount.AttachTo(selected)) Debug.LogWarning("请选择一个 SCREEN_ 显示面或屏幕父节点，然后在「目标屏幕」中重新指定。", instance);
            PrefabUtility.RecordPrefabInstancePropertyModifications(mount);
            PrefabUtility.RecordPrefabInstancePropertyModifications(mount.ScreenCanvas.transform);
            EditorSceneManager.MarkSceneDirty(instance.scene);
            Selection.activeGameObject = instance;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
