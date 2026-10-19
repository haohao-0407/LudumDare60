using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LudumDare60.UI.Editor
{
    public static class EscMenuBuilder
    {
        public const string PrefabPath = "Assets/Prefabs/UI/ESC Menu.prefab";
        private const string Art = "Assets/ArtAssets/UI/ui1111_components/";
        private const float Scale = 1200f / 5237f;
        private static readonly Color Ink = new Color(0.025f, 0.1f, 0.075f);
        private static readonly Color Light = new Color(0.77f, 1, 0.84f);
        private static TMP_FontAsset font;

        [MenuItem("Tools/UI/创建独立 ESC 菜单预制体")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play。");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            { Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); return; }
            EnsureFolder("Assets/Prefabs/UI");
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Terminal/Glass_TTY_VT220 SDF.asset");
            if (font == null) throw new InvalidOperationException("Missing Glass TTY TMP font.");
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            if (actions == null || actions.FindAction("ESCMenu/Toggle", false) == null)
                throw new InvalidOperationException("Input System needs ESCMenu/Toggle.");
            var preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("ESC Menu", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2000;
                var scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
                root.AddComponent<GraphicRaycaster>();
                var overlay = Rect("Menu Overlay", root.transform); Stretch(overlay);
                var dim = overlay.gameObject.AddComponent<UnityEngine.UI.Image>(); dim.color = new Color(0, 0.015f, 0.01f, 0.78f); dim.raycastTarget = true;
                var window = Rect("Menu Window", overlay); window.sizeDelta = new Vector2(1200, 3406 * Scale);
                var background = window.gameObject.AddComponent<UnityEngine.UI.Image>(); background.sprite = Sprite("00_Background.png"); background.raycastTarget = false;
                Picture("Header Frame", window, "01_HeaderFrame.png", 175, 45, 4895, 630);
                var title = Text("Title", window, "PAUSED / SHIP SYSTEMS", 44, Light);
                Place(title.rectTransform, 80, 25, 1040, 125); title.alignment = TextAlignmentOptions.Center;
                var ui = new EscMenuWidgets { Overlay = overlay.gameObject, Title = title };
                var main = Rect("Main Page", window); Stretch(main); ui.MainPage = main.gameObject;
                main.gameObject.AddComponent<CanvasGroup>();
                ui.Resume = ArtButton("Resume", main, "03_BarLong.png", "RESUME", 313, 925, 2921, 346);
                ui.Settings = ArtButton("Settings", main, "04_BarMedium.png", "SETTINGS", 308, 1410, 1703, 329);
                ui.Quit = ArtButton("Quit", main, "05_BarShort.png", "QUIT GAME", 308, 1883, 1557, 314);
                var instructions = Text("Controls", main, "SESSION PAUSED\n\nESC      RESUME\nENTER    SELECT\nARROWS   NAVIGATE", 23, Light);
                Place(instructions.rectTransform, 835, 235, 300, 245); instructions.lineSpacing = 14;
                var settings = Rect("Settings Page", window); Stretch(settings); ui.SettingsPage = settings.gameObject;
                settings.gameObject.AddComponent<CanvasGroup>();
                ui.Back = ArtButton("Back", settings, "03_BarLong.png", "BACK", 313, 925, 2921, 346);
                ui.Fullscreen = Fullscreen(settings);
                var settingsHelp = Text("Settings Hint", settings, "DISPLAY / AUDIO / LANGUAGE\n\nSwitch interface language\nwith the left and right arrows.", 23, Light);
                Place(settingsHelp.rectTransform, 835, 235, 300, 245); settingsHelp.lineSpacing = 14;
                settings.gameObject.SetActive(false);
                BuildLanguageOption(settings, ui);
                BuildVolume(settings, ui);
                Picture("Footer Strip", window, "09_FooterStrip.png", 95, 3210, 5025, 190);
                var footer = Text("Footer", window, "ESC / RESUME       SHIP OS / SYSTEM CONTROL", 19, Light);
                Place(footer.rectTransform, 40, 736, 1110, 40); footer.alignment = TextAlignmentOptions.Center;
                BuildConfirmation(window, ui);
                AddHoverEffects(root);
                root.AddComponent<EscMenuController>().Configure(actions, ui);
                overlay.gameObject.SetActive(false);
                if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null) throw new InvalidOperationException("Could not create ESC Menu prefab.");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        [MenuItem("Tools/UI/把 ESC 菜单安装到当前场景")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play。");
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("请先保存目标场景。");
            UIManagerInstaller.Install();
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<EscMenuController>(true) != null) { Selection.activeGameObject = root; return; }
            Build();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            Undo.RegisterCreatedObjectUndo(instance, "Install ESC Menu");
            EscMenuController.EnsureEventSystem();
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = instance;
        }

        [MenuItem("Tools/UI/更新 ESC 菜单悬停效果")]
        public static void UpdateHoverEffects()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play。");
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try { AddHoverEffects(root); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void AddHoverEffects(GameObject root)
        {
            const string window = "Menu Overlay/Menu Window/";
            foreach (var path in new[] { "Main Page/Resume", "Main Page/Settings", "Main Page/Quit",
                "Settings Page/Back", "Settings Page/Fullscreen", "Settings Page/Language" })
            {
                var target = root.transform.Find(window + path);
                if (target == null) continue;
                var hitImage = target.GetComponent<UnityEngine.UI.Image>();
                var visual = target.Find("Hover Visual") as RectTransform;
                if (visual == null)
                {
                    visual = Rect("Hover Visual", target); Stretch(visual);
                    var picture = visual.gameObject.AddComponent<UnityEngine.UI.Image>();
                    picture.sprite = hitImage.sprite; picture.color = hitImage.color;
                }
                var image = visual.GetComponent<UnityEngine.UI.Image>(); image.raycastTarget = false;
                // Keep the Selectable and hit rectangle stationary. Move art, labels, value and checkmark together.
                for (var i = target.childCount - 1; i >= 0; i--)
                {
                    var child = target.GetChild(i);
                    if (child != visual) child.SetParent(visual, false);
                }
                hitImage.color = Color.clear;
                target.GetComponent<UnityEngine.UI.Selectable>().targetGraphic = image;
                var sweep = visual.GetComponentInChildren<EscMenuSweepGraphic>(true);
                if (sweep == null) sweep = CreateSweep(visual);
                sweep.transform.SetSiblingIndex(0);
                var effect = target.GetComponent<EscMenuHoverEffect>();
                if (effect == null) effect = target.gameObject.AddComponent<EscMenuHoverEffect>();
                effect.Configure(visual, sweep, 22);
            }
            foreach (var name in new[] { "Master Volume" })
            {
                var row = root.transform.Find(window + "Settings Page/" + name);
                if (row == null || row.GetComponent<EscMenuHoverEffect>() != null) continue;
                var sweep = CreateSweep(row); sweep.transform.SetSiblingIndex(0);
                row.gameObject.AddComponent<EscMenuHoverEffect>().Configure(null, sweep, 0);
            }
        }

        private static EscMenuSweepGraphic CreateSweep(Transform parent)
        {
            var rect = Rect("Hover Sweep", parent); Stretch(rect);
            var graphic = rect.gameObject.AddComponent<EscMenuSweepGraphic>(); graphic.raycastTarget = false;
            return graphic;
        }

        private static void BuildLanguageOption(Transform window, EscMenuWidgets ui)
        {
            var row = Picture("Language", window, "04_BarMedium.png", 308, 1883, 2921, 314);
            row.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            var label = Text("Label", row, "LANGUAGE", 23, Ink);
            Place(label.rectTransform, 24, 0, 180, 314 * Scale); label.alignment = TextAlignmentOptions.MidlineLeft;
            var value = Text("Value", row, "English", 23, Ink);
            Place(value.rectTransform, 210, 0, 435, 314 * Scale); value.alignment = TextAlignmentOptions.MidlineRight;

            var option = row.gameObject.AddComponent<EscLanguageOption>();
            option.Configure(label, value);
            var colors = option.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.13f, 1.13f, 1.13f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.65f, 0.85f, 0.7f);
            colors.fadeDuration = 0.08f;
            option.colors = colors;
            option.targetGraphic = row.GetComponent<UnityEngine.UI.Image>();
            ui.Language = option;
        }

        private static void BuildVolume(Transform settings, EscMenuWidgets ui)
        {
            var row = Picture("Master Volume", settings, "06_SliderBackground.png", 313, 2399, 2921, 321);
            var label = Text("Label", row, "VOLUME", 23, Ink);
            Place(label.rectTransform, 20, 0, 155, 321 * Scale); label.alignment = TextAlignmentOptions.MidlineLeft;
            var slider = row.gameObject.AddComponent<UnityEngine.UI.Slider>(); slider.minValue = 0; slider.maxValue = 1; slider.value = 1;
            var area = Rect("Handle Slide Area", row); Place(area, 835 * Scale, 117 * Scale, 1756 * Scale, 69 * Scale);
            var handle = Rect("Handle", area); handle.sizeDelta = new Vector2(133 * Scale, (133 - 69) * Scale);
            var image = handle.gameObject.AddComponent<UnityEngine.UI.Image>(); image.sprite = Sprite("08_SliderHandle.png"); image.raycastTarget = true;
            slider.handleRect = handle; slider.targetGraphic = image; slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            row.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            var value = Text("Value", row, "100%", 21, Ink);
            Place(value.rectTransform, 603, 0, 60, 321 * Scale); value.alignment = TextAlignmentOptions.Center;
            ui.Volume = slider; ui.VolumeValue = value;
        }

        [MenuItem("Tools/UI/更新 ESC 设置页语言与音量布局")]
        public static void UpdateSettingsLayout()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play。");
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try { UpgradeSettingsLayout(root); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void UpgradeSettingsLayout(GameObject root)
        {
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Terminal/Glass_TTY_VT220 SDF.asset");
            var window = root.transform.Find("Menu Overlay/Menu Window");
            var settings = window.Find("Settings Page");
            var language = settings.Find("Language") ?? window.Find("Language");
            if (language == null) throw new InvalidOperationException("Missing language option.");
            language.SetParent(settings, false);
            Place((RectTransform)language, 308 * Scale, 1883 * Scale, 2921 * Scale, 314 * Scale);
            (language.Find("Hover Visual") ?? language).GetComponent<UnityEngine.UI.Image>().sprite = Sprite("04_BarMedium.png");
            // The previous conversion left the old volume handle inside the language row.
            var legacySlider = language.GetComponent<UnityEngine.UI.Slider>();
            if (legacySlider != null) UnityEngine.Object.DestroyImmediate(legacySlider);
            var legacyHandle = language.Find("Handle Slide Area");
            if (legacyHandle != null) UnityEngine.Object.DestroyImmediate(legacyHandle.gameObject);
            var option = language.GetComponent<EscLanguageOption>();
            var label = option.RowLabel;
            var value = option.ValueLabel;
            label.text = "LANGUAGE"; label.fontSize = 23;
            Place(label.rectTransform, 24, 0, 180, 314 * Scale); label.alignment = TextAlignmentOptions.MidlineLeft;
            Place(value.rectTransform, 210, 0, 435, 314 * Scale); value.alignment = TextAlignmentOptions.MidlineRight;
            option.Configure(label, value); option.targetGraphic = language.GetComponent<UnityEngine.UI.Image>();
            var reset = settings.Find("Reset Audio");
            if (reset != null) UnityEngine.Object.DestroyImmediate(reset.gameObject);
            var ui = new EscMenuWidgets();
            var volume = settings.Find("Master Volume");
            if (volume == null) BuildVolume(settings, ui);
            else { ui.Volume = volume.GetComponent<UnityEngine.UI.Slider>(); ui.VolumeValue = volume.Find("Value").GetComponent<TMP_Text>(); }
            var data = new SerializedObject(root.GetComponent<EscMenuController>());
            data.FindProperty("widgets.Language").objectReferenceValue = option;
            data.FindProperty("widgets.Volume").objectReferenceValue = ui.Volume;
            data.FindProperty("widgets.VolumeValue").objectReferenceValue = ui.VolumeValue;
            data.ApplyModifiedPropertiesWithoutUndo();
            AddHoverEffects(root);
        }

        private static UnityEngine.UI.Toggle Fullscreen(Transform parent)
        {
            var row = Picture("Fullscreen", parent, "04_BarMedium.png", 308, 1410, 1703, 329);
            var label = Text("Label", row, "FULLSCREEN", 30, Ink); Stretch(label.rectTransform); label.rectTransform.offsetMin = new Vector2(24, 0); label.alignment = TextAlignmentOptions.MidlineLeft;
            var mark = Rect("Checkmark", row); mark.anchorMin = mark.anchorMax = new Vector2(1, 0.5f); mark.anchoredPosition = new Vector2(-30, 0); mark.sizeDelta = new Vector2(20, 20);
            var graphic = mark.gameObject.AddComponent<UnityEngine.UI.Image>(); graphic.sprite = Sprite("08_SliderHandle.png"); graphic.color = Ink; graphic.raycastTarget = false;
            var toggle = row.gameObject.AddComponent<UnityEngine.UI.Toggle>(); toggle.graphic = graphic; toggle.targetGraphic = row.GetComponent<UnityEngine.UI.Image>();
            row.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            return toggle;
        }

        private static void BuildConfirmation(Transform window, EscMenuWidgets ui)
        {
            var panel = Rect("Quit Confirmation", window); Stretch(panel);
            var blocker = panel.gameObject.AddComponent<UnityEngine.UI.Image>(); blocker.color = new Color(0.005f, 0.035f, 0.025f, 0.97f);
            var text = Text("Question", panel, "QUIT GAME?\n\nReturn to desktop?", 42, Light); Place(text.rectTransform, 140, 185, 920, 190); text.alignment = TextAlignmentOptions.Center;
            ui.CancelQuit = ArtButton("Cancel Quit", panel, "04_BarMedium.png", "CANCEL", 850, 1900, 1703, 329);
            ui.ConfirmQuit = ArtButton("Confirm Quit", panel, "05_BarShort.png", "QUIT", 2800, 1900, 1557, 314);
            ui.QuitConfirmation = panel.gameObject; panel.gameObject.SetActive(false);
        }

        private static UnityEngine.UI.Button ArtButton(string name, Transform parent, string sprite, string caption, float x, float y, float w, float h)
        {
            var rect = Picture(name, parent, sprite, x, y, w, h);
            var image = rect.GetComponent<UnityEngine.UI.Image>(); image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(1.13f, 1.13f, 1.13f); colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.65f, 0.85f, 0.7f); colors.fadeDuration = 0.08f; button.colors = colors;
            var label = Text("Label", rect, caption, 30, Ink); Stretch(label.rectTransform); label.rectTransform.offsetMin = new Vector2(24, 0); label.alignment = TextAlignmentOptions.MidlineLeft;
            return button;
        }
        private static RectTransform Picture(string name, Transform parent, string sprite, float x, float y, float w, float h)
        {
            var rect = Rect(name, parent); Place(rect, x * Scale, y * Scale, w * Scale, h * Scale);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.sprite = Sprite(sprite); image.type = UnityEngine.UI.Image.Type.Simple; image.raycastTarget = false;
            return rect;
        }
        private static Sprite Sprite(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + file) ?? throw new InvalidOperationException("Missing UI sprite: " + file);
        private static TMP_Text Text(string name, Transform parent, string content, float size, Color color)
        {
            var rect = Rect(name, parent); var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = content; text.fontSize = size; text.color = color; text.richText = false; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal; return text;
        }
        private static RectTransform Rect(string name, Transform parent)
        { var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); return rect; }
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        { rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 0.5f); rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void EnsureFolder(string path)
        { if (AssetDatabase.IsValidFolder(path)) return; var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path)); }
    }
}
