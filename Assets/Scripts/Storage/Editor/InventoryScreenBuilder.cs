using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using LudumDare60.Salvage;

namespace LudumDare60.Storage.Editor
{
    public static class InventoryScreenBuilder
    {
        private const float Width = 1000f, Height = 620f;
        private static readonly Color Phosphor = new Color(0.68f, 0.96f, 0.76f);
        private static readonly Color Dim = new Color(0.33f, 0.57f, 0.48f);
        private static TMP_FontAsset font;

        [MenuItem("Tools/Storage/在当前场景搭建库存屏")]
        public static void BuildFromMenu() => Build();

        public static GameObject Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before building a screen.");
            var scene = SceneManager.GetActiveScene();
            var transforms = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            var source = transforms.Select(t => t.GetComponent<SalvageController>()).Where(s => s != null).Single();
            var inventories = transforms.Select(t => t.GetComponent<InventoryController>()).Where(s => s != null).ToArray();
            if (inventories.Length > 1) throw new InvalidOperationException("Choose one shared inventory owner before building the screen.");
            var inventory = inventories.SingleOrDefault() ?? Undo.AddComponent<InventoryController>(source.gameObject);
            var bridge = source.GetComponent<SalvageInventoryBridge>() ?? Undo.AddComponent<SalvageInventoryBridge>(source.gameObject);
            var binding = new SerializedObject(bridge);
            binding.FindProperty("salvageController").objectReferenceValue = source;
            binding.FindProperty("inventoryController").objectReferenceValue = inventory;
            binding.ApplyModifiedProperties();

            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Terminal/Glass_TTY_VT220 SDF.asset");
            if (font == null) throw new InvalidOperationException("Missing Glass TTY terminal font.");
            var existing = transforms.SingleOrDefault(t => t.name == "Storage Screen");
            GameObject root;
            if (existing == null)
            {
                root = new GameObject("Storage Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                Undo.RegisterCreatedObjectUndo(root, "Create inventory screen");
                root.transform.SetPositionAndRotation(source.transform.position + source.transform.right * 2.15f, source.transform.rotation);
            }
            else root = existing.gameObject;
            var previous = root.transform.Find("Inventory Display");
            if (previous != null) Undo.DestroyObjectImmediate(previous.gameObject);
            var canvas = root.GetComponent<Canvas>();
            Undo.RecordObjects(new UnityEngine.Object[] { root.transform, canvas }, "Configure inventory screen");
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(Width, Height);
            // Use the same physical text scale as the adjacent salvage screen.
            var parentScale = rect.parent != null ? rect.parent.lossyScale : Vector3.one;
            var sourceScale = source.transform.lossyScale;
            rect.localScale = new Vector3(sourceScale.x / parentScale.x, sourceScale.y / parentScale.y, sourceScale.z / parentScale.z);

            var display = NewRect("Inventory Display", root.transform);
            Fill(display);
            Undo.RegisterCreatedObjectUndo(display.gameObject, "Create inventory display");
            display.gameObject.AddComponent<Image>().color = new Color(0.005f, 0.012f, 0.01f);
            Fill(Panel("Phosphor Bezel", display, 7, 7, 986, 606, new Color(0.12f, 0.28f, 0.22f)), 7);
            Fill(Panel("Glass", display, 10, 10, 980, 600, new Color(0.012f, 0.026f, 0.021f)), 10);
            AnchorVertically(Text("Title", display, "SHIP OS  /  CARGO MANIFEST", 25, Phosphor, 32, 553, 936, 35).rectTransform, 1, 1);
            AnchorVertically(Text("Subtitle", display, "LOCAL HOLD   //   RECOVERY REGISTER", 18, Dim, 32, 517, 936, 28).rectTransform, 1, 1);
            AnchorVertically(Panel("Header Rule", display, 32, 503, 936, 1, Dim), 1, 1);
            AnchorVertically(Text("Number Column", display, "NO.", 19, Dim, 38, 462, 70, 32).rectTransform, 1, 1);
            AnchorVertically(Text("Name Column", display, "RECOVERED ITEM", 19, Dim, 114, 462, 664, 32).rectTransform, 1, 1);
            var quantityHeader = Text("Quantity Column", display, "QTY", 19, Dim, 798, 462, 160, 32);
            AnchorVertically(quantityHeader.rectTransform, 1, 1);
            quantityHeader.alignment = TextAlignmentOptions.MidlineRight;
            AnchorVertically(Panel("Column Rule", display, 32, 455, 936, 1, new Color(0.11f, 0.23f, 0.18f)), 1, 1);

            var viewport = NewRect("Manifest Viewport", display);
            Place(viewport, 32, 149, 936, 296);
            AnchorVertically(viewport, 0, 1);
            viewport.gameObject.AddComponent<Image>().color = new Color(0.012f, 0.025f, 0.02f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28;
            var content = NewRect("Items", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            var template = NewRect("Row Template", content);
            template.sizeDelta = new Vector2(936, 48);
            template.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.057f, 0.04f);
            template.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
            Text("Index", template, "01", 22, Dim, 6, 0, 70, 48);
            Text("Name", template, "RECOVERED ITEM", 24, Phosphor, 82, 0, 664, 48);
            var quantity = Text("Quantity", template, "0", 24, Phosphor, 766, 0, 160, 48);
            quantity.alignment = TextAlignmentOptions.MidlineRight;
            template.gameObject.SetActive(false);
            var empty = Text("Empty Hold", display, "NO RECOVERED ITEMS\nSUCCESSFUL SALVAGE WILL APPEAR HERE", 22, Dim, 48, 149, 904, 296);
            AnchorVertically(empty.rectTransform, 0, 1);
            empty.rectTransform.anchorMax = Vector2.one;
            empty.rectTransform.sizeDelta = new Vector2(-96, empty.rectTransform.sizeDelta.y);
            empty.alignment = TextAlignmentOptions.Center;
            Panel("Footer Rule", display, 32, 128, 936, 1, Dim);
            var summary = Text("Summary", display, "00 ITEM TYPES  /  0 UNITS", 22, Phosphor, 32, 83, 630, 34);
            var status = Text("Storage Status", display, "[ ONLINE ]", 19, Dim, 670, 83, 298, 34);
            status.alignment = TextAlignmentOptions.MidlineRight;
            Text("Footer", display, "SALVAGE LINK ACTIVE                  SCROLL TO INSPECT HOLD", 17, Dim, 32, 34, 936, 28);
            var overlay = NewRect("CRT Scanlines and Vignette", display);
            Fill(overlay);
            overlay.gameObject.AddComponent<InventoryCrtOverlay>().raycastTarget = false;
            var view = root.GetComponent<InventoryScreenView>() ?? Undo.AddComponent<InventoryScreenView>(root);
            Undo.RecordObject(view, "Bind inventory display");
            view.Configure(inventory, AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/Salvage/ItemCatalog.asset"),
                content, template, empty, summary, status);
            EditorUtility.SetDirty(view);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            return root;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Fill(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset; rect.offsetMax = Vector2.one * -inset;
        }

        private static void AnchorVertically(RectTransform rect, float minimum, float maximum)
        {
            // Preserve the authored 620px layout while following the mounted screen's height.
            rect.anchorMin = new Vector2(rect.anchorMin.x, minimum);
            rect.anchorMax = new Vector2(rect.anchorMax.x, maximum);
            rect.anchoredPosition -= Vector2.up * (Height * minimum);
            rect.sizeDelta -= Vector2.up * (Height * (maximum - minimum));
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
        }

        private static RectTransform Panel(string name, Transform parent, float x, float y, float width, float height, Color color)
        {
            var rect = NewRect(name, parent); Place(rect, x, y, width, height);
            var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
            return rect;
        }

        private static TMP_Text Text(string name, Transform parent, string value, float size, Color color,
            float x, float y, float width, float height)
        {
            var rect = NewRect(name, parent); Place(rect, x, y, width, height);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = size; text.color = color; text.text = value;
            text.richText = false; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            return text;
        }
    }
}
