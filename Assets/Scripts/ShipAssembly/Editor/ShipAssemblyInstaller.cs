using System;
using System.IO;
using System.Linq;
using LudumDare60.Storage;
using LudumDare60.TerminalDialogue;
using LudumDare60.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LudumDare60.ShipAssembly.Editor
{
    /// <summary>Installs only the SHIP workbench. Never opens or rewrites the source demo scene/prefab.</summary>
    public static class ShipAssemblyInstaller
    {
        private const string AssetFolder = "Assets/ShipAssembly";
        private const string InputPath = "Assets/InputSystem_Actions.inputactions";
        private const int WorkbenchLayer = 29;
        private static readonly Color Ink = new Color(.68f, .96f, .76f);
        private static readonly Color Dim = new Color(.34f, .61f, .47f);
        private static TMP_FontAsset font;

        [MenuItem("Tools/Ship Assembly/Install In SHIP And Save")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before editing SHIP.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SHIP.unity")
                throw new InvalidOperationException("Open SHIP before installing. Other scenes are preserved.");

            var objects = scene.GetRootGameObjects()
                .Where(root => (root.hideFlags & HideFlags.DontSave) == 0)
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            var screen = objects.Single(t => t.name == "SCREEN_RADAR" && t.GetComponent<MeshFilter>() != null);
            var inventoryBridge = objects.Select(t => t.GetComponent<SalvageInventoryBridge>())
                .First(b => b != null && b.InventoryController != null);
            var repair = objects.Select(t => t.GetComponent<RepairScreenController>()).Single(c => c != null);
            var pilotCamera = objects.Select(t => t.GetComponent<Camera>()).Single(c => c != null && c.CompareTag("MainCamera"));
            var pause = objects.Select(t => t.GetComponent<EscMenuController>()).FirstOrDefault(c => c != null);
            var puzzle = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssemblyDemo/Prefabs/NavigationAssembly.prefab");
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/Salvage/ItemCatalog.asset");
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Terminal/Glass_TTY_VT220 SDF.asset");
            if (puzzle == null || catalog == null || font == null)
                throw new InvalidOperationException("Missing assembly prefab, item catalog or terminal font.");
            var yields = AssetDatabase.LoadAssetAtPath<SalvageMaterialYieldCatalog>("Assets/Salvage/SalvageMaterialYields.asset");
            if (yields == null) throw new InvalidOperationException("Missing salvage material yields.");

            EnsureFolder(AssetFolder);
            EnsureFolder(AssetFolder + "/Recipes");
            EnsureLayer();
            var actions = EnsureInputActions();
            var recipes = new[]
            {
                Recipe("PowerUnit", "power_unit", "Power Unit", RepairDeviceId.PowerUnit, new[] { 6, 4, 2, 7, 3 }, puzzle),
                Recipe("NavigationSystem", "interstellar_navigation", "Interstellar Navigation System", RepairDeviceId.Navigation, new[] { 4, 5, 7, 2, 2 }, puzzle),
                Recipe("PropulsionEngine", "propulsion_engine", "Propulsion Engine", RepairDeviceId.PropulsionEngine, new[] { 7, 3, 3, 6, 4 }, puzzle),
                Recipe("LifeSupport", "life_support", "Life Support System", RepairDeviceId.LifeSupport, new[] { 5, 3, 2, 2, 7 }, puzzle)
            };

            // A rerun preserves recipe edits, input bindings and the installed scene hierarchy.
            var service = objects.Select(t => t.GetComponent<ShipAssemblyController>()).FirstOrDefault(c => c != null);
            if (service == null)
            {
                var root = new GameObject("Ship Assembly System");
                Undo.RegisterCreatedObjectUndo(root, "Install ship assembly");
                SceneManager.MoveGameObjectToScene(root, scene);
                service = root.AddComponent<ShipAssemblyController>();
                var workspace = new GameObject("Assembly Workspace").transform;
                workspace.SetParent(root.transform, false);
                workspace.position = new Vector3(100, 100, 100);
                var cameraObject = new GameObject("Assembly Camera");
                cameraObject.transform.SetParent(workspace, false);
                cameraObject.transform.localPosition = new Vector3(0, 0, -10);
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5.4f;
                camera.nearClipPlane = .05f; camera.farClipPlane = 30f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.008f, .018f, .014f);
                camera.cullingMask = 1 << WorkbenchLayer;
                camera.depth = -20;
                camera.allowHDR = false; camera.allowMSAA = false;
                camera.useOcclusionCulling = false;
                camera.targetTexture = EnsureTexture();
                var cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.renderPostProcessing = false; cameraData.renderShadows = false;
                cameraData.requiresColorOption = CameraOverrideOption.Off;
                cameraData.requiresDepthOption = CameraOverrideOption.Off;
                service.Workspace = workspace;
                service.AssemblyCamera = camera;
                service.WorkspaceLayer = WorkbenchLayer;
                service.Recipes = recipes;
                service.EnforceSequentialOrder = true;
            }
            Undo.RecordObject(service, "Connect ship assembly");
            service.Inventory = inventoryBridge.InventoryController;
            service.RepairScreen = repair;
            EditorUtility.SetDirty(service);
            if (!objects.Any(t => t.GetComponent<AssemblyScreenView>() != null))
                BuildScreen(scene, screen, pilotCamera, service, catalog, actions, pause);

            Undo.RecordObject(pilotCamera, "Isolate assembly camera layer");
            pilotCamera.cullingMask &= ~(1 << WorkbenchLayer);
            EditorUtility.SetDirty(pilotCamera);
            var bridgeFields = new SerializedObject(inventoryBridge);
            bridgeFields.FindProperty("materialYieldCatalog").objectReferenceValue = yields;
            bridgeFields.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(inventoryBridge);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save SHIP.");
            Selection.activeGameObject = service.gameObject;
            Debug.Log("[ShipAssembly] Saved SHIP workbench, four recipes and salvage material yields. No Play Mode or tests were run.");
        }

        private static AssemblyRecipe Recipe(string filename, string id, string title, RepairDeviceId device, int[] quantities, GameObject puzzle)
        {
            var path = AssetFolder + "/Recipes/" + filename + ".asset";
            var recipe = AssetDatabase.LoadAssetAtPath<AssemblyRecipe>(path);
            if (recipe != null) return recipe;
            recipe = ScriptableObject.CreateInstance<AssemblyRecipe>();
            recipe.RecipeId = id; recipe.DisplayName = title; recipe.Device = device;
            recipe.PuzzlePrefab = puzzle;
            var ids = new[] { "rolled_electromechanical_alloy_sheets", "tinned_copper_wire_windings", "precision_drive_gear_blanks", "heat_resistant_ceramic_insulators", "vulcanized_airtight_rubber_liners" };
            recipe.Materials = ids.Select((material, index) => new InventoryCost { ItemId = material, Quantity = quantities[index] }).ToArray();
            AssetDatabase.CreateAsset(recipe, path);
            return recipe;
        }

        private static RenderTexture EnsureTexture()
        {
            var path = AssetFolder + "/AssemblyScreenRT.renderTexture";
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (rt != null) return rt;
            rt = new RenderTexture(1536, 1024, 24, RenderTextureFormat.ARGB32)
            {
                name = "AssemblyScreenRT", filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, antiAliasing = 1
            };
            AssetDatabase.CreateAsset(rt, path);
            return rt;
        }

        private static void BuildScreen(Scene scene, Transform target, Camera pilot, ShipAssemblyController service,
            ItemCatalog catalog, InputActionAsset actions, EscMenuController pause)
        {
            var root = new GameObject("Assembly Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(root, "Create assembly screen");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.SetActive(false);
            var rig = target.root;
            root.transform.SetParent(rig, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = pilot;
            ((RectTransform)root.transform).sizeDelta = new Vector2(1000, 800);
            var backdrop = root.AddComponent<Image>();
            backdrop.color = new Color(.008f, .019f, .014f); backdrop.raycastTarget = true;
            var view = root.AddComponent<AssemblyScreenView>();
            view.Controller = service; view.Catalog = catalog;

            Text("OS Header", root.transform, "SHIP OS / ASSEMBLY WORKBENCH", 24, Dim, .035f, .94f, .965f, .99f);
            view.Title = Text("Recipe", root.transform, "Power Unit", 35, Ink, .12f, .85f, .88f, .94f);
            view.Title.alignment = TextAlignmentOptions.Center;
            view.PreviousButton = Button("Previous Recipe", root.transform, "<", .035f, .855f, .105f, .935f, out _);
            view.NextButton = Button("Next Recipe", root.transform, ">", .895f, .855f, .965f, .935f, out _);
            Panel("Header Line", root.transform, Dim, .035f, .842f, .965f, .845f);

            var requirementsPanel = Rect("Requirements Panel", root.transform, .035f, .25f, .965f, .82f);
            view.RequirementsPanel = requirementsPanel.gameObject;
            view.Requirements = Text("Materials", requirementsPanel, "", 28, Ink, 0, 0, 1, 1);
            view.Requirements.richText = true;
            view.Requirements.alignment = TextAlignmentOptions.TopLeft;
            view.Requirements.enableAutoSizing = true; view.Requirements.fontSizeMin = 23; view.Requirements.fontSizeMax = 28;
            var materialNames = new[] { "Rolled Electromechanical Alloy Sheets", "Tinned Copper Wire & Windings", "Precision Drive Gear Blanks", "Heat-Resistant Ceramic Insulators", "Vulcanized Airtight Rubber Liners" };
            var counts = new[] { 6, 4, 2, 7, 3 };
            view.Requirements.text = string.Join("\n", materialNames.Select((name, i) => name + "\n    OWNED 0 / REQUIRED " + counts[i]));

            var puzzlePanel = Rect("Puzzle Panel", root.transform, .035f, .25f, .965f, .82f);
            view.PuzzlePanel = puzzlePanel.gameObject;
            Panel("Puzzle Background", puzzlePanel, new Color(.005f, .012f, .008f), 0, 0, 1, 1);
            var surface = Rect("Assembly Viewport", puzzlePanel, 0, 0, 1, 1);
            var image = surface.gameObject.AddComponent<RawImage>();
            image.texture = service.AssemblyCamera.targetTexture; image.color = Color.white;
            var aspect = surface.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent; aspect.aspectRatio = 1.5f;
            view.PuzzleImage = image;
            var input = surface.gameObject.AddComponent<AssemblyScreenInput>();
            input.Controller = service; input.Surface = image; input.Actions = actions; input.PauseMenu = pause;
            puzzlePanel.gameObject.SetActive(false);

            view.Status = Text("Status", root.transform, "Collect materials to begin.", 24, Ink, .035f, .19f, .965f, .245f);
            view.ProgressLabel = Text("Progress", root.transform, "00% / NOT STARTED", 22, Dim, .035f, .145f, .965f, .19f);
            view.StartButton = Button("Start Or Resume", root.transform, "START ASSEMBLY", .035f, .075f, .47f, .14f, out var startLabel);
            view.StartLabel = startLabel;
            view.ResetButton = Button("Reset Parts", root.transform, "RESET PARTS", .49f, .075f, .73f, .14f, out _);
            view.DetachButton = Button("Detach Part", root.transform, "DETACH", .75f, .075f, .965f, .14f, out _);
            view.ResetButton.interactable = false; view.DetachButton.interactable = false;
            view.StartButton.interactable = false;
            view.Hint = Text("Controls", root.transform, "Materials are charged once. Switching screens keeps your progress.", 18, Dim, .035f, .008f, .965f, .07f);
            view.Hint.alignment = TextAlignmentOptions.Center;
            var mount = root.AddComponent<TerminalScreenMount>();
            mount.Configure(canvas);
            mount.SetCurvedRendering(true);
            // Bind while inactive, then fit the authored Canvas once all its children exist.
            mount.AttachTo(target);
            root.SetActive(true);
            mount.RefreshScreen();
            EditorUtility.SetDirty(root); EditorUtility.SetDirty(view); EditorUtility.SetDirty(input); EditorUtility.SetDirty(mount);
        }

        private static InputActionAsset EnsureInputActions()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
            if (asset == null) throw new InvalidOperationException("Missing project Input Actions.");
            if (asset.FindActionMap("Assembly", false) != null) return asset;
            var copy = InputActionAsset.FromJson(asset.ToJson());
            try
            {
                var map = copy.AddActionMap("Assembly");
                map.AddAction("Point", InputActionType.Value, "<Pointer>/position", expectedControlLayout: "Vector2");
                map.AddAction("Click", InputActionType.Button, "<Mouse>/leftButton");
                map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2")
                    .AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
                map.AddAction("Rotate", InputActionType.Value, expectedControlLayout: "Axis")
                    .AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/q").With("Positive", "<Keyboard>/e");
                var fine = map.AddAction("Fine", InputActionType.Button, "<Keyboard>/leftShift");
                fine.AddBinding("<Keyboard>/rightShift");
                map.AddAction("Next", InputActionType.Button, "<Keyboard>/tab");
                for (var i = 1; i <= 4; i++) map.AddAction("Select" + i, InputActionType.Button, "<Keyboard>/" + i);
                map.AddAction("Detach", InputActionType.Button, "<Keyboard>/x");
                map.AddAction("Reset", InputActionType.Button, "<Keyboard>/r");
                map.AddAction("ReleaseFocus", InputActionType.Button, "<Keyboard>/escape");
                map.AddAction("LookHold", InputActionType.Button, "<Mouse>/rightButton");
                File.WriteAllText(InputPath, copy.ToJson() + "\n", new System.Text.UTF8Encoding(false));
            }
            finally { Object.DestroyImmediate(copy); }
            AssetDatabase.ImportAsset(InputPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
        }

        private static void EnsureLayer()
        {
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layer = settings.FindProperty("layers").GetArrayElementAtIndex(WorkbenchLayer);
            if (!string.IsNullOrEmpty(layer.stringValue) && layer.stringValue != "ShipAssembly")
                throw new InvalidOperationException("Layer 29 is already assigned; select a free workbench layer.");
            layer.stringValue = "ShipAssembly"; settings.ApplyModifiedProperties();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static RectTransform Rect(string name, Transform parent, float x0, float y0, float x1, float y1)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static void Panel(string name, Transform parent, Color color, float x0, float y0, float x1, float y1)
        {
            var image = Rect(name, parent, x0, y0, x1, y1).gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false;
        }

        private static TMP_Text Text(string name, Transform parent, string value, float size, Color color, float x0, float y0, float x1, float y1)
        {
            var text = Rect(name, parent, x0, y0, x1, y1).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = size; text.color = color; text.text = value;
            text.richText = false; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            return text;
        }

        private static Button Button(string name, Transform parent, string label, float x0, float y0, float x1, float y1, out TMP_Text text)
        {
            var rect = Rect(name, parent, x0, y0, x1, y1);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.035f, .12f, .075f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(.7f, 1f, .8f);
            colors.pressedColor = new Color(.3f, .7f, .45f); colors.disabledColor = new Color(.3f, .35f, .3f);
            button.colors = colors;
            text = Text("Label", rect, label, 24, Ink, .02f, 0, .98f, 1);
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }
    }
}
