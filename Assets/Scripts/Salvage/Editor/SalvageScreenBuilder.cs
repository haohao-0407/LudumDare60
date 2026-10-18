using LudumDare60.Salvage;
using LudumDare60.Storage;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LudumDare60.Salvage.Editor
{
    /// <summary>Builds the second terminal screen that runs the salvage minigame beside the dialogue screen.</summary>
    public static class SalvageScreenBuilder
    {
        private const string RootName = "Salvage Screen (World Space)";
        private const string PoolPath = "Assets/Salvage/SalvagePool_Main.asset";
        private const float CanvasWidth = 1000f;
        private const float CanvasHeight = 620f;

        private static readonly Color BackColor = new Color(0.012f, 0.021f, 0.025f, 1f);
        private static readonly Color TitleColor = new Color(0.3f, 0.7f, 0.65f);
        private static readonly Color TextColor = new Color(0.8f, 0.94f, 0.9f);
        private static readonly Color DimColor = new Color(0.42f, 0.62f, 0.56f);
        private static readonly Color BezelColor = new Color(0.17f, 0.36f, 0.34f, 1f);
        private static readonly Color TrackColor = new Color(0.035f, 0.075f, 0.075f, 1f);
        private static readonly Color GridColor = new Color(0.19f, 0.42f, 0.39f, 0.55f);
        private static readonly Color ZoneColor = new Color(0.28f, 0.62f, 0.32f, 1f);
        private static readonly Color FishColor = new Color(0.95f, 0.66f, 0.12f, 1f);
        private static readonly Color ProgressColor = new Color(1f, 0.78f, 0.25f, 1f);

        [MenuItem("Tools/Salvage/在当前场景搭建打捞屏")]
        public static void BuildFromMenu() => Build();

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "Play mode is active. Exit play mode first.";

            var previous = GameObject.Find(RootName);
            if (previous != null) Object.DestroyImmediate(previous);

            var camera = Camera.main;
            if (camera == null) return "No Main Camera in the scene.";

            var pool = AssetDatabase.LoadAssetAtPath<SalvagePool>(PoolPath);
            if (pool == null) return "Missing salvage pool at " + PoolPath;

            var root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(root, "Create salvage screen");

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            var canvasRect = (RectTransform)root.transform;
            canvasRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);

            // 与第一块屏同一平面并排；第一块屏宽 2.5m、本屏宽 1.9m，中间留 0.15m 缝
            var anchor = GameObject.Find("Computer Screen (World Space)");
            if (anchor != null)
            {
                canvasRect.rotation = anchor.transform.rotation;
                canvasRect.localScale = Vector3.one * 0.0019f;
                canvasRect.position = anchor.transform.position + anchor.transform.right * 2.35f;
            }
            else
            {
                canvasRect.position = camera.transform.position + camera.transform.forward * 3f + camera.transform.right * 2.3f;
                canvasRect.rotation = camera.transform.rotation;
                canvasRect.localScale = Vector3.one * 0.0019f;
            }

            var background = root.AddComponent<UnityEngine.UI.Image>();
            background.color = BackColor;
            background.raycastTarget = true;

            var title = Text("Title", root.transform, "SHIP OS  /  SALVAGE CONTROL", 24, TitleColor);
            Place(title.rectTransform, 24, 558, CanvasWidth - 48, 46);

            // ---- 打捞轨道 ----
            const float trackX = 40f, trackY = 78f, trackW = 200f, trackH = 420f;
            var bezel = Panel("Track Bezel", root.transform, trackX - 4, trackY - 4, trackW + 8, trackH + 8, BezelColor);
            bezel.name = "Track Bezel";
            var track = Panel("Track", root.transform, trackX, trackY, trackW, trackH, TrackColor);

            for (var i = 1; i < 10; i++)
            {
                var line = Panel("Grid " + i, track, 0, trackH * i / 10f, trackW, 1f, GridColor);
                line.anchorMin = new Vector2(0f, 0f);
                line.anchorMax = new Vector2(1f, 0f);
                line.pivot = new Vector2(0.5f, 0.5f);
                line.anchoredPosition = new Vector2(0f, trackH * i / 10f);
                line.sizeDelta = new Vector2(0f, 1f);
            }

            var catchZone = Panel("Catch Zone", track, 0, 0, trackW - 8f, trackH * 0.25f, ZoneColor);
            catchZone.anchorMin = catchZone.anchorMax = new Vector2(0.5f, 0f);
            catchZone.pivot = new Vector2(0.5f, 0.5f);
            catchZone.anchoredPosition = new Vector2(0f, trackH * 0.25f);

            var fish = Panel("Target", track, 0, 0, trackW * 0.62f, trackH / 10f, FishColor);
            fish.anchorMin = fish.anchorMax = new Vector2(0.5f, 0f);
            fish.pivot = new Vector2(0.5f, 0.5f);
            fish.anchoredPosition = new Vector2(0f, trackH * 0.30f);

            var progressBezel = Panel("Progress Bezel", root.transform, trackX + trackW + 8, trackY - 4, 20, trackH + 8, BezelColor);
            progressBezel.name = "Progress Bezel";
            var progressTrack = Panel("Progress Track", root.transform, trackX + trackW + 10, trackY, 16, trackH, TrackColor);
            var progressFill = Panel("Progress Fill", progressTrack, 0, 0, 16, trackH * 0.3f, ProgressColor);
            progressFill.anchorMin = new Vector2(0f, 0f);
            progressFill.anchorMax = new Vector2(1f, 0f);
            progressFill.pivot = new Vector2(0.5f, 0f);
            progressFill.anchoredPosition = Vector2.zero;
            progressFill.sizeDelta = new Vector2(0f, trackH * 0.3f);

            // ---- 右侧读数 ----
            const float colX = 330f;
            var item = Text("Item", root.transform, "UNKNOWN SIGNAL", 26, TextColor);
            Place(item.rectTransform, colX, 498, CanvasWidth - colX - 32, 42);

            var state = Text("State", root.transform, ">> STANDBY", 20, DimColor);
            Place(state.rectTransform, colX, 452, CanvasWidth - colX - 32, 32);

            var zoneReadout = Text("Zone Readout", root.transform, "CATCH 0.00 - 0.00", 19, DimColor);
            Place(zoneReadout.rectTransform, colX, 300, CanvasWidth - colX - 32, 30);

            var fishReadout = Text("Target Readout", root.transform, "TARGET 0.00", 19, DimColor);
            Place(fishReadout.rectTransform, colX, 266, CanvasWidth - colX - 32, 30);

            var progressReadout = Text("Progress Readout", root.transform, "PROGRESS 0%", 19, DimColor);
            Place(progressReadout.rectTransform, colX, 232, CanvasWidth - colX - 32, 30);

            var hint = Text("Hint", root.transform, "HOLD  [SPACE]  OR  CLICK THE SCREEN", 18, DimColor);
            Place(hint.rectTransform, 24, 26, CanvasWidth - 48, 30);

            // ---- 逻辑 ----
            var poolSource = new SerializedObject(root.AddComponent<SalvageController>());
            poolSource.FindProperty("pool").objectReferenceValue = pool;
            poolSource.ApplyModifiedPropertiesWithoutUndo();
            var controller = root.GetComponent<SalvageController>();

            // Keep inventory and its completion listener alive for the full salvage screen lifetime.
            var inventory = root.AddComponent<InventoryController>();
            var inventoryBridge = new SerializedObject(root.AddComponent<SalvageInventoryBridge>());
            inventoryBridge.FindProperty("salvageController").objectReferenceValue = controller;
            inventoryBridge.FindProperty("inventoryController").objectReferenceValue = inventory;
            inventoryBridge.ApplyModifiedPropertiesWithoutUndo();

            var view = root.AddComponent<SalvageScreenView>();
            view.Configure(controller, track, catchZone, fish, progressFill, item, state,
                zoneReadout, fishReadout, progressReadout, hint);

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            return "Built " + RootName + " at " + canvasRect.position.ToString("0.00") + " (scale " + canvasRect.localScale.x + ")";
        }

        private static RectTransform Panel(string name, Transform parent, float x, float y, float w, float h, Color color)
        {
            var rect = NewRect(name, parent);
            Place(rect, x, y, w, h);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static TMP_Text Text(string name, Transform parent, string value, float size, Color color)
        {
            var rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = LudumDare60.TerminalDialogue.Editor.TerminalDialogueDemoBuilder.GetTerminalFont();
            text.fontSize = size;
            text.text = value;
            text.color = color;
            text.raycastTarget = false;
            text.richText = false;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            return text;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
        }
    }
}
