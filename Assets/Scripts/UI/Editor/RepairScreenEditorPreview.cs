using UnityEditor;
using UnityEngine;

namespace LudumDare60.UI.EditorTools
{
    /// <summary>
    /// 编辑器里手动摆 REPAIR_SCREEN_Rig 下的装置时，让屏幕那块 RenderTexture 实时刷新。
    /// 原因：不在 Play 模式时，带 targetTexture 的相机不会自己重绘，拖模型时屏上画面是死的。
    /// 开关在菜单 Tools/Repair Screen/编辑器实时预览，默认关闭；只在打开时才有一点渲染开销。
    /// </summary>
    [InitializeOnLoad]
    public static class RepairScreenEditorPreview
    {
        private const string MenuPath = "Tools/Repair Screen/编辑器实时预览";
        private const string PrefKey = "LudumDare60.RepairScreen.LivePreview";
        private const double RenderInterval = 0.05; // 20fps 足够，省 CPU

        private static Camera cachedCamera;
        private static double nextRenderTime;

        static RepairScreenEditorPreview()
        {
            EditorApplication.update += Tick;
        }

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            private set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem(MenuPath, false, 20)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            Debug.Log("[RepairScreen] 编辑器实时预览：" + (Enabled ? "开" : "关"));
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void Tick()
        {
            if (!Enabled) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.timeSinceStartup < nextRenderTime) return;
            nextRenderTime = EditorApplication.timeSinceStartup + RenderInterval;

            if (cachedCamera == null)
            {
                var rig = GameObject.Find("REPAIR_SCREEN_Rig");
                if (rig == null) return;
                var camTransform = rig.transform.Find("REPAIR_SCREEN_Camera");
                if (camTransform == null) return;
                cachedCamera = camTransform.GetComponent<Camera>();
            }

            if (cachedCamera == null || cachedCamera.targetTexture == null) return;
            cachedCamera.Render();
        }
    }
}
