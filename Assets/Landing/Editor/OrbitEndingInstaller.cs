using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LudumDare60.Landing.Editor
{
    /// <summary>Creates editable overlay text without rebuilding the cockpit or its flight flow.</summary>
    public static class OrbitEndingInstaller
    {
        public const string CreditsPath = "Assets/Landing/OrbitCredits.asset";
        public const string PrefabPath = "Assets/Landing/Prefabs/Orbit Ending Overlay.prefab";
        const string FontPath = "Assets/Fonts/Terminal/Glass_TTY_VT220 SDF.asset";
        static readonly Color TextColor = new Color(.88f, 1f, .92f);
        static readonly Color MutedColor = new Color(.64f, .82f, .71f);

        [MenuItem("Tools/Landing/Install orbit ending overlay in SHIP")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Install the ending overlay in Edit Mode after compilation.");
            Scene scene = SceneManager.GetSceneByPath(LandingSceneInstaller.ShipPath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open SHIP before installing the ending overlay.");
            var launches = InScene<LaunchSequenceController>(scene);
            var openings = InScene<CockpitOpeningController>(scene);
            var endings = InScene<OrbitEndingUI>(scene);
            if (launches.Length != 1 || openings.Length != 1 || !openings[0].Terminal || endings.Length > 1)
                throw new InvalidOperationException("SHIP must contain one launch, one opening with a terminal, and at most one ending overlay.");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (!font) throw new InvalidOperationException("The existing terminal TMP font is missing.");

            var credits = AssetDatabase.LoadAssetAtPath<OrbitCreditsDefinition>(CreditsPath);
            if (!credits)
            {
                credits = ScriptableObject.CreateInstance<OrbitCreditsDefinition>();
                AssetDatabase.CreateAsset(credits, CreditsPath);
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) prefab = CreatePrefab(font, credits);
            OrbitEndingUI view;
            if (endings.Length == 0)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(instance, "Install orbit ending overlay");
                view = instance.GetComponent<OrbitEndingUI>();
            }
            else view = endings[0];
            Undo.RecordObject(view, "Bind orbit ending overlay");
            view.Launch = launches[0];
            view.Terminal = openings[0].Terminal;
            if (!view.Credits) view.Credits = credits;
            view.ResetPresentation();
            EditorUtility.SetDirty(view);
            PrefabUtility.RecordPrefabInstancePropertyModifications(view);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save SHIP.");
            Selection.activeGameObject = view.gameObject;
            Debug.Log("SHIP ending overlay saved: orbit completion and final dialogue lead to configurable thanks and credits.", view);
        }

        static GameObject CreatePrefab(TMP_FontAsset font, OrbitCreditsDefinition credits)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Landing/Prefabs"))
                AssetDatabase.CreateFolder("Assets/Landing", "Prefabs");
            Scene preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Orbit Ending Overlay", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000; // Keep the existing ESC menu (2000) above the ending.
                var scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = .5f;
                var group = Group(root);
                var content = Rect("Left Text Area", root.transform, new Vector2(.055f, .18f), new Vector2(.41f, .82f));
                var title = Text("Game Title", content, font, credits.GameTitle, 26f, MutedColor, new Vector2(0, .91f), Vector2.one);
                title.characterSpacing = 4f;

                var thanks = Rect("Thanks", content, new Vector2(0, .17f), new Vector2(1, .82f));
                var thanksGroup = Group(thanks.gameObject);
                var thanksText = Text("Thanks Text", thanks, font, credits.ThanksMessage, 76f, TextColor, Vector2.zero, Vector2.one);
                thanksText.alignment = TextAlignmentOptions.MidlineLeft;
                thanksText.lineSpacing = 5f;

                var creditArea = Rect("Credits", content, new Vector2(0, .13f), new Vector2(1, .84f));
                var creditGroup = Group(creditArea.gameObject);
                Text("Credits Heading", creditArea, font, "Credits", 38f, TextColor, new Vector2(0, .87f), Vector2.one);
                var viewport = Rect("Credits Viewport", creditArea, Vector2.zero, new Vector2(1, .82f));
                viewport.gameObject.AddComponent<RectMask2D>();
                var scroll = Rect("Credits Content", viewport, new Vector2(0, 1), Vector2.one);
                scroll.pivot = new Vector2(.5f, 1);
                scroll.sizeDelta = new Vector2(0, 200);
                var creditText = Text("Credits Text", scroll, font, credits.CreditsText, 30f, TextColor, Vector2.zero, Vector2.one);
                creditText.lineSpacing = 12f;
                var status = Text("Closing Line", content, font, "", 22f, MutedColor, Vector2.zero, new Vector2(1, .08f));
                status.characterSpacing = 2f;

                var view = root.AddComponent<OrbitEndingUI>();
                view.Credits = credits;
                view.RootGroup = group;
                view.ContentRoot = content;
                view.GameTitleText = title;
                view.ThanksGroup = thanksGroup;
                view.ThanksText = thanksText;
                view.CreditsGroup = creditGroup;
                view.CreditsViewport = viewport;
                view.CreditsContent = scroll;
                view.CreditsText = creditText;
                view.StatusText = status;
                view.ResetPresentation();
                var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (!saved) throw new InvalidOperationException("Could not save the ending prefab.");
                return saved;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        static T[] InScene<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        static CanvasGroup Group(GameObject target)
        {
            var group = target.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            return group;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = LayerMask.NameToLayer("UI");
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, string value,
            float size, Color color, Vector2 min, Vector2 max)
        {
            var rect = Rect(name, parent, min, max);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            // A small text shadow preserves legibility over the cockpit without a background panel.
            var shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, .02f, .01f, .85f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return text;
        }
    }

    [CustomEditor(typeof(OrbitEndingUI))]
    public sealed class OrbitEndingUIEditor : UnityEditor.Editor
    {
        float previewTime;
        OrbitEndingUI preview;

        void OnEnable()
        {
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += EndPreview;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (Application.isPlaying) return;
            var view = (OrbitEndingUI)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Edit the Credits asset for names and timing. Preview only changes this overlay; it does not run the flight.", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            previewTime = EditorGUILayout.Slider("Preview time (seconds)", previewTime, 0f,
                Mathf.Max(1f, preview ? preview.PreviewDurationSeconds : view.PreviewDurationSeconds));
            if (EditorGUI.EndChangeCheck())
            {
                if (!preview)
                {
                    var copy = Instantiate(view.gameObject);
                    copy.name = "Orbit Ending Preview (not saved)";
                    foreach (var child in copy.GetComponentsInChildren<Transform>(true))
                        child.gameObject.hideFlags = HideFlags.HideAndDontSave;
                    preview = copy.GetComponent<OrbitEndingUI>();
                }
                preview.PreviewFrame(previewTime);
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }
            if (GUILayout.Button("Hide preview")) EndPreview();
        }

        void OnSceneSaving(Scene scene, string path) => EndPreview();
        void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) EndPreview();
        }

        void EndPreview()
        {
            if (preview) DestroyImmediate(preview.gameObject);
            preview = null;
            previewTime = 0f;
        }

        void OnDisable()
        {
            EndPreview();
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= EndPreview;
        }
    }
}
