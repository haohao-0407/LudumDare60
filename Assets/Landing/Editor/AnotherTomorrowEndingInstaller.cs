using System;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace LudumDare60.Landing.Editor
{
    /// <summary>Builds the editable, bilingual theatre epilogue without changing any shared UI fonts.</summary>
    [InitializeOnLoad]
    public static class AnotherTomorrowEndingInstaller
    {
        public const string ScriptPath = "Assets/Landing/AnotherTomorrow.asset";
        public const string PrefabPath = "Assets/Landing/Prefabs/Another Tomorrow Ending.prefab";
        public const string LatinFontPath = "Assets/Fonts/Theatre/CormorantGaramond-Regular SDF.asset";
        public const string ChineseFontPath = "Assets/Fonts/Theatre/SourceHanSerifSC-Regular SDF.asset";
        const string LatinSourcePath = "Assets/Fonts/Theatre/CormorantGaramond-Regular.ttf";
        const string ChineseSourcePath = "Assets/Fonts/Theatre/SourceHanSerifSC-Regular.otf";
        static readonly Color Ivory = new Color32(233, 223, 206, 255);
        static readonly Color Gold = new Color32(151, 128, 88, 255);
        static readonly Color Muted = new Color32(163, 154, 138, 255);
        static AnotherTomorrowEndingUI preview;

        static AnotherTomorrowEndingInstaller()
        {
            EditorSceneManager.sceneSaving += (_, __) => HidePreview();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) HidePreview();
            };
            AssemblyReloadEvents.beforeAssemblyReload += HidePreview;
        }

        [MenuItem("Tools/Landing/Install Another Tomorrow theatre ending in SHIP")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Install the theatre ending in Edit Mode after compilation.");
            HidePreview();
            Scene scene = SceneManager.GetSceneByPath(LandingSceneInstaller.ShipPath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open SHIP before installing its theatre ending.");
            var missions = InScene<CockpitMissionController>(scene);
            var cameras = InScene<CockpitCameraController>(scene);
            var endings = InScene<AnotherTomorrowEndingUI>(scene);
            if (missions.Length != 1 || cameras.Length != 1 || endings.Length > 1 ||
                !missions[0].Opening || !missions[0].Opening.Terminal || !cameras[0].PauseMenu)
                throw new InvalidOperationException("SHIP needs one bound mission, one cockpit camera with its pause menu, and at most one theatre ending.");

            var script = AssetDatabase.LoadAssetAtPath<AnotherTomorrowDefinition>(ScriptPath);
            if (!script)
            {
                script = ScriptableObject.CreateInstance<AnotherTomorrowDefinition>();
                AssetDatabase.CreateAsset(script, ScriptPath);
            }
            var latin = EnsureFont(LatinSourcePath, LatinFontPath, Characters(script, false));
            var chinese = EnsureFont(ChineseSourcePath, ChineseFontPath, Characters(script, true));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) prefab = CreatePrefab(script, latin, chinese);

            AnotherTomorrowEndingUI view;
            if (endings.Length == 0)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(instance, "Install Another Tomorrow theatre ending");
                view = instance.GetComponent<AnotherTomorrowEndingUI>();
            }
            else view = endings[0];
            Undo.RecordObject(view, "Bind Another Tomorrow theatre ending");
            view.Mission = missions[0];
            view.Terminal = missions[0].Opening.Terminal;
            view.CockpitCamera = cameras[0];
            view.PauseMenu = cameras[0].PauseMenu;
            if (!view.Script) view.Script = script;
            if (!view.LatinFont) view.LatinFont = latin;
            if (!view.ChineseFont) view.ChineseFont = chinese;
            view.Hide();
            EditorUtility.SetDirty(view);
            PrefabUtility.RecordPrefabInstancePropertyModifications(view);
            foreach (var group in view.GetComponentsInChildren<CanvasGroup>(true))
            {
                EditorUtility.SetDirty(group);
                PrefabUtility.RecordPrefabInstancePropertyModifications(group);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save SHIP with its theatre ending.");
            Selection.activeGameObject = view.gameObject;
            Debug.Log("Another Tomorrow theatre ending installed: bilingual static fonts, four editable title cards and a restart action.", view);
        }

        public static string Characters(AnotherTomorrowDefinition script, bool chinese)
        {
            var text = new StringBuilder();
            for (int value = 32; value <= 126; value++) text.Append((char)value);
            foreach (var card in script.Cards)
            {
                text.Append(chinese ? card.ChineseCue : card.EnglishCue);
                text.Append(chinese ? card.ChineseTitle : card.EnglishTitle);
                text.Append(chinese ? card.ChineseBody : card.EnglishBody);
                text.Append(chinese ? card.ChineseFootnote : card.EnglishFootnote);
            }
            text.Append(chinese ? "ESC   /   菜单空格 或 单击   /   下一幕再次启程" :
                "ESC   /   MENUSPACE OR CLICK   /   NEXT ACTBEGIN AGAIN");
            return new string(text.ToString().Where(c => !char.IsControl(c)).Distinct().OrderBy(c => c).ToArray());
        }

        static TMP_FontAsset EnsureFont(string sourcePath, string assetPath, string characters)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (asset)
            {
                if (!asset.HasCharacters(characters, out uint[] missing, false, false))
                    throw new InvalidOperationException(assetPath + " is missing theatre glyphs: " +
                        string.Join(", ", missing.Select(c => "U+" + c.ToString("X4"))) +
                        ". Regenerate this dedicated font asset after editing the script.");
                return asset;
            }
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
            var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (!source) throw new InvalidOperationException("Missing theatre source font: " + sourcePath);
            asset = TMP_FontAsset.CreateFontAsset(source, 96, 8, GlyphRenderMode.SDFAA,
                2048, 2048, AtlasPopulationMode.Dynamic, false);
            if (!asset) throw new InvalidOperationException("Could not create theatre SDF font: " + sourcePath);
            asset.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (!asset.TryAddCharacters(characters, out string missingCharacters, true) ||
                !asset.HasCharacters(characters, out uint[] _, false, false))
            {
                UnityEngine.Object.DestroyImmediate(asset);
                throw new InvalidOperationException("Could not bake all theatre glyphs from " + sourcePath +
                    ". Missing: " + missingCharacters);
            }
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            asset.isMultiAtlasTexturesEnabled = false;
            AssetDatabase.CreateAsset(asset, assetPath);
            for (int i = 0; i < asset.atlasTextures.Length; i++)
            {
                var atlas = asset.atlasTextures[i];
                atlas.name = asset.name + " Atlas " + i;
                atlas.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(atlas, asset);
            }
            asset.material.name = asset.name + " Material";
            asset.material.hideFlags = HideFlags.None;
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        static GameObject CreatePrefab(AnotherTomorrowDefinition script, TMP_FontAsset latin, TMP_FontAsset chinese)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Landing/Prefabs"))
                AssetDatabase.CreateFolder("Assets/Landing", "Prefabs");
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Another Tomorrow Ending", typeof(RectTransform));
            root.layer = LayerMask.NameToLayer("UI");
            SceneManager.MoveGameObjectToScene(root, previewScene);
            try
            {
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1500; // The existing ESC menu remains above the epilogue.
                var scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                scaler.matchWidthOrHeight = .5f;
                root.AddComponent<GraphicRaycaster>();
                var blackout = Group(root);
                var backdrop = FullRect("Black stage", root.transform).gameObject.AddComponent<Image>();
                backdrop.color = Color.black;
                backdrop.raycastTarget = true;

                var content = FullRect("Title card", root.transform);
                var cardGroup = Group(content.gameObject);
                var cue = Text("Act cue", content, latin, 26, Gold, 780, 1550, 70);
                Rule("Opening rule", content, 735, 100, Gold);
                var title = Text("Title", content, latin, 88, Ivory, 630, 1550, 150);
                var body = Text("Narration", content, latin, 40, Ivory, 440, 1450, 240);
                var footnote = Text("Date and stage note", content, latin, 23, Muted, 240, 1550, 65);
                Rule("Closing rule", content, 295, 100, Gold);
                var hint = Text("Reading hint", root.transform, latin, 22, Muted, 65, 1500, 45);

                var actions = FullRect("Final curtain actions", root.transform);
                var actionGroup = Group(actions.gameObject);
                var buttonRect = PositionedRect("Begin again", actions, 155, 300, 60);
                var buttonImage = buttonRect.gameObject.AddComponent<Image>();
                buttonImage.color = Color.black;
                buttonImage.raycastTarget = true;
                var outline = buttonRect.gameObject.AddComponent<Outline>();
                outline.effectColor = Gold;
                outline.effectDistance = new Vector2(1, -1);
                outline.useGraphicAlpha = true;
                var button = buttonRect.gameObject.AddComponent<Button>();
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                var restart = FullRect("Button label", buttonRect).gameObject.AddComponent<TextMeshProUGUI>();
                SetText(restart, latin, 28, Ivory);
                restart.characterSpacing = 4f;
                ConfigureButton(button, restart);

                var view = root.AddComponent<AnotherTomorrowEndingUI>();
                view.Script = script;
                view.LatinFont = latin;
                view.ChineseFont = chinese;
                view.BlackoutGroup = blackout;
                view.CardGroup = cardGroup;
                view.ActionsGroup = actionGroup;
                view.CueText = cue;
                view.TitleText = title;
                view.BodyText = body;
                view.FootnoteText = footnote;
                view.HintText = hint;
                view.RestartText = restart;
                view.RestartButton = button;
                view.PreviewCard(0, false);
                view.Hide();
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (!prefab) throw new InvalidOperationException("Could not save the theatre ending prefab.");
                return prefab;
            }
            finally { EditorSceneManager.ClosePreviewScene(previewScene); }
        }

        static void ConfigureButton(Button button, TMP_Text label)
        {
            // Tint the lettering: tinting a black backdrop cannot reveal hover or keyboard focus.
            button.targetGraphic = label;
            button.transition = Selectable.Transition.ColorTint;
            label.color = Color.white;
            var colors = button.colors;
            colors.normalColor = Ivory;
            colors.highlightedColor = new Color32(220, 191, 133, 255);
            colors.pressedColor = Color.white;
            colors.selectedColor = colors.highlightedColor;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = .15f;
            button.colors = colors;
        }

        /// <summary>Updates only the restart action in existing assets; all authored layout is retained.</summary>
        [MenuItem("Tools/Landing/Update theatre restart button appearance")]
        public static void FixButtonAppearance()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Update the theatre button in Edit Mode after compilation.");
            HidePreview();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) throw new InvalidOperationException("Install the theatre ending before updating its button.");
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var view = contents.GetComponent<AnotherTomorrowEndingUI>();
                if (!view || !view.RestartButton || !view.RestartText)
                    throw new InvalidOperationException("The theatre ending prefab is missing its restart action.");
                ConfigureButton(view.RestartButton, view.RestartText);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            Scene scene = SceneManager.GetSceneByPath(LandingSceneInstaller.ShipPath);
            if (scene.IsValid() && scene.isLoaded)
            {
                foreach (var view in InScene<AnotherTomorrowEndingUI>(scene))
                {
                    if (!view.RestartButton || !view.RestartText) continue;
                    Undo.RecordObjects(new UnityEngine.Object[] { view.RestartButton, view.RestartText },
                        "Make theatre restart focus visible");
                    ConfigureButton(view.RestartButton, view.RestartText);
                    EditorUtility.SetDirty(view.RestartButton);
                    EditorUtility.SetDirty(view.RestartText);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(view.RestartButton);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(view.RestartText);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Could not save SHIP after updating its theatre button.");
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Landing/Preview Another Tomorrow/English final curtain")]
        public static void PreviewEnglish() => PreviewCard(3, false);

        [MenuItem("Tools/Landing/Preview Another Tomorrow/Chinese final curtain")]
        public static void PreviewChinese() => PreviewCard(3, true);

        public static void PreviewCard(int index, bool chinese)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!preview)
            {
                Scene scene = SceneManager.GetSceneByPath(LandingSceneInstaller.ShipPath);
                var views = scene.IsValid() && scene.isLoaded ? InScene<AnotherTomorrowEndingUI>(scene) :
                    Array.Empty<AnotherTomorrowEndingUI>();
                if (views.Length != 1)
                    throw new InvalidOperationException("Install the theatre ending in SHIP before previewing.");
                var clone = UnityEngine.Object.Instantiate(views[0].gameObject);
                clone.name = "Another Tomorrow Preview (not saved)";
                foreach (var component in clone.GetComponentsInChildren<Component>(true))
                    if (component) component.hideFlags = HideFlags.HideAndDontSave;
                foreach (var transform in clone.GetComponentsInChildren<Transform>(true))
                    transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
                preview = clone.GetComponent<AnotherTomorrowEndingUI>();
            }
            preview.PreviewCard(index, chinese);
            Canvas.ForceUpdateCanvases();
            foreach (var text in preview.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true);
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        [MenuItem("Tools/Landing/Preview Another Tomorrow/Hide preview")]
        public static void HidePreview()
        {
            if (preview) UnityEngine.Object.DestroyImmediate(preview.gameObject);
            preview = null;
        }

        static T[] InScene<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        static CanvasGroup Group(GameObject target)
        {
            var group = target.AddComponent<CanvasGroup>();
            group.alpha = 0;
            group.interactable = group.blocksRaycasts = false;
            return group;
        }

        static RectTransform FullRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = LayerMask.NameToLayer("UI");
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        static RectTransform PositionedRect(string name, Transform parent, float y, float width, float height)
        {
            var rect = FullRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0, y - 540);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font,
            float size, Color color, float y, float width, float height)
        {
            var text = PositionedRect(name, parent, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            SetText(text, font, size, color);
            return text;
        }

        static void SetText(TextMeshProUGUI text, TMP_FontAsset font, float size, Color color)
        {
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = false;
            text.richText = false;
            text.raycastTarget = false;
            text.text = "";
        }

        static void Rule(string name, Transform parent, float y, float width, Color color)
        {
            var line = PositionedRect(name, parent, y, width, 1).gameObject.AddComponent<Image>();
            line.color = color;
            line.raycastTarget = false;
        }
    }

    [CustomEditor(typeof(AnotherTomorrowEndingUI))]
    public sealed class AnotherTomorrowEndingUIEditor : UnityEditor.Editor
    {
        int cardIndex;
        bool chinese;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (Application.isPlaying) return;
            var view = (AnotherTomorrowEndingUI)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("The Script asset contains both languages and card timing. Preview uses a temporary copy and closes before saving or entering Play Mode.", MessageType.Info);
            int cardCount = view.Script && view.Script.Cards != null ? view.Script.Cards.Length : 0;
            if (cardCount == 0) return;
            cardIndex = EditorGUILayout.IntSlider("Preview card", cardIndex, 0, cardCount - 1);
            chinese = EditorGUILayout.Toggle("Chinese preview", chinese);
            if (GUILayout.Button("Show title card")) AnotherTomorrowEndingInstaller.PreviewCard(cardIndex, chinese);
            if (GUILayout.Button("Hide preview")) AnotherTomorrowEndingInstaller.HidePreview();
        }
    }
}
