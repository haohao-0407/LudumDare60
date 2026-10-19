using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LudumDare60.UI.Editor
{
    public static class UIManagerInstaller
    {
        [MenuItem("Tools/UI/安装全局 UI Manager 到当前场景")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play。");
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("请先保存目标场景。");
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<UIManager>(true) is UIManager existing)
                {
                    Undo.RecordObject(existing.gameObject, "Enable UI Manager");
                    existing.gameObject.SetActive(true);
                    Undo.RecordObject(existing, "Enable UI Manager");
                    existing.enabled = true;
                    Selection.activeGameObject = existing.gameObject;
                    EditorSceneManager.MarkSceneDirty(scene);
                    return;
                }
            var manager = new GameObject("manager", typeof(UIManager));
            SceneManager.MoveGameObjectToScene(manager, scene);
            Undo.RegisterCreatedObjectUndo(manager, "Install UI Manager");
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = manager;
        }
    }
}
