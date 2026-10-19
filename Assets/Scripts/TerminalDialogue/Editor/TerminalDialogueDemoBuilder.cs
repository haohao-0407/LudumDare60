using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TextCore.LowLevel;

namespace LudumDare60.TerminalDialogue.Editor
{
    public static class TerminalDialogueDemoBuilder
    {
        private static bool importingResources;
        [MenuItem("Tools/Terminal Dialogue/创建示例对话资产")]
        public static void CreateExampleAsset()
        {
            var definition = CreateDefinition();
            Selection.activeObject = definition;
            EditorGUIUtility.PingObject(definition);
        }

        [MenuItem("Tools/Terminal Dialogue/在当前场景搭建演示")]
        public static void CreateDemo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EnsureTMPResources(CreateDemo)) return;
            var root = new GameObject("Terminal Dialogue Demo");
            Undo.RegisterCreatedObjectUndo(root, "Create terminal dialogue demo");
            var camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Terminal Demo Camera", typeof(Camera), typeof(AudioListener));
                Undo.RegisterCreatedObjectUndo(cameraObject, "Create terminal demo camera");
                cameraObject.tag = "MainCamera";
                cameraObject.transform.position = new Vector3(0, 1.5f, -4);
                camera = cameraObject.GetComponent<Camera>();
            }
            var terminal = root.AddComponent<TerminalDialogueController>();
            terminal.Configure(CreateDefinition());
            var screen = CreateCanvas("Computer Screen (World Space)", root.transform, RenderMode.WorldSpace);
            screen.worldCamera = camera;
            var screenRect = (RectTransform)screen.transform;
            screenRect.sizeDelta = new Vector2(1000, 620);
            screenRect.position = camera.transform.position + camera.transform.forward * 3;
            screenRect.rotation = camera.transform.rotation;
            screenRect.localScale = Vector3.one * 0.0025f;
            var background = screen.gameObject.AddComponent<UnityEngine.UI.Image>();
            background.color = new Color(0.015f, 0.025f, 0.03f, 1);
            background.raycastTarget = false;
            var title = CreateText("Title", screen.transform, "SHIP OS  /  COMMUNICATION TERMINAL", 24);
            Stretch(title.rectTransform, new Vector2(24, 555), new Vector2(976, 603), screenRect.sizeDelta);
            title.color = new Color(0.3f, 0.7f, 0.65f);

            var scrollObject = CreateRect("History", screen.transform);
            Stretch(scrollObject, new Vector2(24, 50), new Vector2(976, 545), screenRect.sizeDelta);
            var scroll = scrollObject.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35;
            var viewport = CreateRect("Viewport", scrollObject);
            Fill(viewport);
            viewport.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.white;
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            var content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            var text = CreateText("Transcript", content, "", 27);
            text.color = new Color(0.65f, 1, 0.76f);
            text.textWrappingMode = TextWrappingModes.Normal;
            scroll.viewport = viewport; scroll.content = content;
            var cursor = CreateText("Cursor", screen.transform, "> _", 25);
            Stretch(cursor.rectTransform, new Vector2(24, 8), new Vector2(976, 45), screenRect.sizeDelta);
            screen.gameObject.AddComponent<TerminalTranscriptView>().Configure(terminal, text, cursor, scroll);

            var overlay = CreateCanvas("Reply UI (Screen Space)", root.transform, RenderMode.ScreenSpaceOverlay);
            var scaler = overlay.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            var window = CreateRect("Options Window", overlay.transform);
            window.anchorMin = window.anchorMax = new Vector2(1, 0);
            window.pivot = new Vector2(1, 0); window.anchoredPosition = new Vector2(-36, 36);
            window.sizeDelta = new Vector2(520, 430);
            window.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0.025f, 0.05f, 0.075f, 0.96f);
            var status = CreateText("Status", window, "SELECT A RESPONSE", 24);
            Stretch(status.rectTransform, new Vector2(20, 370), new Vector2(500, 410), window.sizeDelta);

            var choicesScrollObject = CreateRect("Choices Scroll", window);
            Stretch(choicesScrollObject, new Vector2(20, 85), new Vector2(500, 360), window.sizeDelta);
            var choicesScroll = choicesScrollObject.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            choicesScroll.horizontal = false; choicesScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            var choicesViewport = CreateRect("Viewport", choicesScrollObject); Fill(choicesViewport);
            choicesViewport.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.white;
            choicesViewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            var choices = CreateRect("Choices", choicesViewport);
            choices.anchorMin = new Vector2(0, 1); choices.anchorMax = Vector2.one;
            choices.pivot = new Vector2(0.5f, 1); choices.sizeDelta = Vector2.zero;
            var choicesLayout = choices.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            choicesLayout.spacing = 10; choicesLayout.childControlWidth = true; choicesLayout.childControlHeight = true;
            choicesLayout.childForceExpandWidth = true; choicesLayout.childForceExpandHeight = false;
            choices.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            choicesScroll.content = choices; choicesScroll.viewport = choicesViewport;
            var templateButton = CreateButton("Choice Template", choices, "RESPONSE", out var templateText);
            var template = templateButton.gameObject.AddComponent<TerminalChoiceButton>();
            template.ConfigureLabel(templateText); template.gameObject.SetActive(false);
            var advance = CreateButton("Advance", window, "SKIP", out var advanceText);
            Stretch((RectTransform)advance.transform, new Vector2(20, 20), new Vector2(245, 70), window.sizeDelta);
            var close = CreateButton("Close", window, "LEAVE TERMINAL", out _);
            Stretch((RectTransform)close.transform, new Vector2(255, 20), new Vector2(500, 70), window.sizeDelta);
            var open = CreateButton("Open", overlay.transform, "CONNECT", out _);
            var openRect = (RectTransform)open.transform;
            openRect.anchorMin = openRect.anchorMax = Vector2.one;
            openRect.pivot = Vector2.one; openRect.anchoredPosition = new Vector2(-36, -36); openRect.sizeDelta = new Vector2(240, 56);
            overlay.gameObject.AddComponent<TerminalOptionsPanel>().Configure(terminal, window.gameObject, choices,
                template, status, advance, advanceText, close, open);
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Undo.RegisterCreatedObjectUndo(eventSystem, "Create UI EventSystem");
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
        }

        public static TerminalDialogueDefinition CreateDefinition()
        {
            const string folder = "Assets/Dialogue";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Dialogue");
            var definition = ScriptableObject.CreateInstance<TerminalDialogueDefinition>();
            var graph = definition.Graph;
            graph.AIName = "NAV AI"; graph.EntryNodeId = "signal";
            var start = new TerminalNode { Id = "signal", Title = "发现信号" };
            start.Lines.Add(new TerminalLine { Text = "Captain, an unusual signal has been detected ahead." });
            start.Lines.Add(new TerminalLine { Text = "Its source is approximately three kilometers away." });
            start.Choices.Add(new TerminalChoice { Id = "analyze", Label = "ANALYZE SIGNAL", PlayerText = "Analyze the signal for me.", NextNodeId = "analysis" });
            start.Choices.Add(new TerminalChoice { Id = "risk", Label = "ASSESS RISK", PlayerText = "What are the risks of approaching it?", NextNodeId = "risk" });
            start.Choices.Add(new TerminalChoice { Id = "coordinates", Label = "SHOW COORDINATES", PlayerText = "Display the target coordinates.", NextNodeId = "coordinates",
                Conditions = { new TerminalFlag { Key = "has_coordinates" } } });
            start.Choices.Add(new TerminalChoice { Id = "leave", Label = "END TRANSMISSION", PlayerText = "Keep monitoring. End transmission.", NextNodeId = "end" });
            var analysis = new TerminalNode { Id = "analysis", Title = "信号分析", NextNodeId = "signal" };
            analysis.Lines.Add(new TerminalLine { Speaker = TerminalSpeaker.System, Text = "scan nearby\nScan complete: derelict transport identified." });
            analysis.Lines.Add(new TerminalLine { Text = "I have marked its position.", WaitForContinue = true });
            analysis.Choices.Add(new TerminalChoice { Id = "mark", Label = "SAVE COORDINATES", PlayerText = "Save the coordinates and return to the comms menu.",
                NextNodeId = "signal", SetFlags = { new TerminalFlag { Key = "has_coordinates" } } });
            var risk = new TerminalNode { Id = "risk", Title = "风险说明", NextNodeId = "signal" };
            risk.Lines.Add(new TerminalLine { Text = "There is heavy metal debris nearby. I recommend reducing speed.", WaitForContinue = true });
            var coordinates = new TerminalNode { Id = "coordinates", Title = "坐标记录", NextNodeId = "signal" };
            coordinates.Lines.Add(new TerminalLine { Speaker = TerminalSpeaker.System, Text = "Target coordinates: X 128 / Y -36 / Z 420." });
            var end = new TerminalNode { Id = "end", Title = "结束" };
            end.Lines.Add(new TerminalLine { Text = "Acknowledged. I will continue monitoring nearby signals." });
            graph.Nodes.AddRange(new[] { start, analysis, risk, coordinates, end });
            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/TerminalDialogueExample.asset");
            AssetDatabase.CreateAsset(definition, path);
            AssetDatabase.SaveAssets();
            return definition;
        }

        internal static bool EnsureTMPResources(Action continuation)
        {
            if (TMP_Settings.LoadDefaultSettings() != null && TMP_Settings.defaultFontAsset != null) return true;
            if (importingResources) return false;
            importingResources = true;
            AssetDatabase.ImportPackageCallback completed = null;
            completed = package =>
            {
                AssetDatabase.importPackageCompleted -= completed;
                importingResources = false;
                EditorApplication.delayCall += () =>
                {
                    TMP_Settings.LoadDefaultSettings();
                    continuation();
                };
            };
            AssetDatabase.importPackageCompleted += completed;
            // Non-interactive TMP import; never opens the blocking import dialog.
            TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
            return false;
        }

        private static Canvas CreateCanvas(string name, Transform parent, RenderMode mode)
        {
            var rect = CreateRect(name, parent);
            var canvas = rect.gameObject.AddComponent<Canvas>(); canvas.renderMode = mode;
            rect.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            rect.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            return canvas;
        }
        private static RectTransform CreateRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); return rect;
        }
        private static TMP_Text CreateText(string name, Transform parent, string value, float size)
        {
            var rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = GetTerminalFont(); text.fontSize = size; text.text = value;
            text.color = new Color(0.8f, 0.94f, 0.9f); text.raycastTarget = false; text.richText = false;
            return text;
        }
        private static UnityEngine.UI.Button CreateButton(string name, Transform parent, string label, out TMP_Text text)
        {
            var rect = CreateRect(name, parent);
            rect.sizeDelta = new Vector2(460, 64);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(0.08f, 0.17f, 0.19f);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 64;
            text = CreateText("Label", rect, label, 24); text.alignment = TextAlignmentOptions.MidlineLeft;
            Fill(text.rectTransform); text.rectTransform.offsetMin = new Vector2(16, 4); text.rectTransform.offsetMax = new Vector2(-16, -4);
            return button;
        }
        private static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        internal static TMP_FontAsset GetTerminalFont()
        {
            const string assetPath = "Assets/Fonts/Terminal/Glass_TTY_VT220 SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) { BakeAscii(existing); return existing; }
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Terminal/Glass_TTY_VT220.ttf");
            if (source == null) throw new InvalidOperationException("缺少用户指定的 Glass_TTY_VT220.ttf 字体。");
            var font = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (font == null) throw new InvalidOperationException("无法创建 Glass TTY 的 TMP 字体资产。");
            font.name = "Glass_TTY_VT220 SDF";
            AssetDatabase.CreateAsset(font, assetPath);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var texture in font.atlasTextures) AssetDatabase.AddObjectToAsset(texture, font);
            BakeAscii(font);
            AssetDatabase.SaveAssets();
            return font;
        }

        private static void BakeAscii(TMP_FontAsset font)
        {
            if (font.atlasPopulationMode == AtlasPopulationMode.Static && font.characterTable.Count >= 95) return;
            var characters = new char[95];
            for (var i = 0; i < characters.Length; i++) characters[i] = (char)(32 + i);
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            font.TryAddCharacters(new string(characters), out string missing);
            if (!string.IsNullOrEmpty(missing)) throw new InvalidOperationException("字体缺少 ASCII 字符：" + missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(font);
            EditorUtility.SetDirty(font.material);
            foreach (var texture in font.atlasTextures) EditorUtility.SetDirty(texture);
            AssetDatabase.SaveAssets();
        }
        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 parentSize)
        {
            rect.anchorMin = new Vector2(min.x / parentSize.x, min.y / parentSize.y);
            rect.anchorMax = new Vector2(max.x / parentSize.x, max.y / parentSize.y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
