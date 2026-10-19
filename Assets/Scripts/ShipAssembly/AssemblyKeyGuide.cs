using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace LudumDare60.ShipAssembly
{
    /// <summary>Canvas-drawn control legend. Only the expand/collapse button receives pointer input.</summary>
    [DisallowMultipleComponent]
    public sealed class AssemblyKeyGuide : MaskableGraphic, LudumDare60.TerminalDialogue.ITerminalGraphicState
    {
        public bool ShowInput = true;
        public static TMP_FontAsset ReadableFont => Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        private const float Width = 928, Height = 180;
        private static readonly Color Ink = new Color(.78f, .86f, .85f, 1);
        private static readonly Color Muted = new Color(.42f, .55f, .55f, 1);
        private static readonly Color Mint = new Color(.48f, .79f, .69f, 1);
        private static readonly Color Amber = new Color(.86f, .69f, .44f, 1);
        private List<Keycap> keycaps = new List<Keycap>();
        private readonly List<TMP_Text> detailLabels = new List<TMP_Text>();
        private readonly List<TMP_Text> numberLabels = new List<TMP_Text>();
        private TMP_Text selectionLabel, progressLabel, modeLabel;
        private bool preview, completed, visualCopy;
        public bool IsExpanded { get; private set; }
        public Button ExpandButton { get; private set; }
        private int connections, required = 1, moduleCount = 4, level = -1;
        private ulong pressed;

        private struct Keycap
        {
            public Rect Rect;
            public Key Key;
            public int Icon;
            public bool Warm;
            public Keycap(Rect rect, Key key, int icon, bool warm)
            { Rect = rect; Key = key; Icon = icon; Warm = warm; }
        }

        public static AssemblyKeyGuide Ensure(Transform parent, TMP_FontAsset font, bool preview = false)
        {
            var existing = parent.Find("Assembly Key Guide");
            if (existing != null) return existing.GetComponent<AssemblyKeyGuide>();
            var go = new GameObject("Assembly Key Guide", typeof(RectTransform), typeof(CanvasRenderer), typeof(AssemblyKeyGuide));
            go.layer = parent.gameObject.layer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.sizeDelta = preview ? new Vector2(1021, 198) : new Vector2(930, 164);
            rect.anchoredPosition = preview ? new Vector2(40, 24) : new Vector2(35, 10);
            var guide = go.GetComponent<AssemblyKeyGuide>();
            guide.preview = preview;
            guide.raycastTarget = false;
            guide.Build(ReadableFont != null ? ReadableFont : font);
            guide.BuildToggle();
            guide.SetExpanded(false);
            return guide;
        }

        private void Build(TMP_FontAsset font)
        {
            Label(font, "SELECTED", 22, 161, 64, 12, Muted);
            selectionLabel = Label(font, "PART", 92, 161, 340, 14, Ink);
            modeLabel = Label(font, preview ? "ASSEMBLY / PLAYTEST" : "ASSEMBLY / CLICK TO FOCUS", 448, 161, 260, 12, Muted);
            progressLabel = Label(font, "0 / 2", 836, 161, 70, 12, Mint, TextAlignmentOptions.MidlineRight);
            detailLabels.Remove(Label(font, "MOVE / ROTATE", 22, 127, 142, 12, Muted));
            Label(font, "SELECT PART", 202, 127, 158, 12, Muted);
            Label(font, "ADJUST", 396, 127, 142, 12, Muted);
            Label(font, "ACTIONS", 574, 127, 148, 12, Muted);
            Label(font, preview ? "DEVICE" : "VIEW", 758, 127, 148, 12, Muted);
            string[] letters = { "Q", "W", "E", "A", "S", "D" };
            Key[] keys = { Key.Q, Key.W, Key.E, Key.A, Key.S, Key.D };
            for (int i = 0; i < 6; i++)
                detailLabels.Remove(Cap(font, letters[i], new Rect(22 + i % 3 * 47, i < 3 ? 74 : 26, 40, 40), keys[i], i, i == 0 || i == 2));
            for (int i = 0; i < 5; i++)
                numberLabels.Add(Cap(font, (i + 1).ToString(), new Rect(202 + i * 31, 80, 26, 30), Key.Digit1 + i));
            Cap(font, "TAB", new Rect(202, 30, 52, 30), Key.Tab);
            Label(font, "CYCLE", 264, 45, 52, 12, Ink);
            Label(font, "PICK", 338, 45, 36, 12, Ink);
            Cap(font, "SHIFT", new Rect(396, 80, 62, 30), Key.LeftShift);
            Label(font, "FINE", 492, 95, 48, 14, Ink);
            Label(font, "DRAG", 462, 45, 75, 14, Ink);
            Cap(font, "X", new Rect(574, 80, 30, 30), Key.X);
            Label(font, "DETACH", 644, 95, 82, 14, Ink);
            Cap(font, "R", new Rect(574, 30, 30, 30), Key.R);
            Label(font, "RESET", 644, 45, 82, 14, Ink);
            if (preview)
            {
                Cap(font, "F1", new Rect(758, 80, 36, 30), Key.F1);
                Label(font, "ENGINE", 808, 95, 96, 14, Ink);
                Cap(font, "F2", new Rect(758, 30, 36, 30), Key.F2);
                Label(font, "LIFE SUPPORT", 808, 45, 108, 12, Ink);
            }
            else
            {
                Cap(font, "ESC", new Rect(758, 80, 42, 30), Key.Escape);
                Label(font, "PAUSE", 814, 95, 90, 14, Ink);
                Label(font, "HOLD / LOOK", 792, 45, 112, 14, Ink);
            }
        }

        private TMP_Text Cap(TMP_FontAsset font, string text, Rect rect, Key key, int icon = -1, bool warm = false)
        {
            keycaps.Add(new Keycap(rect, key, icon, warm));
            return Label(font, text, rect.x, icon >= 0 ? rect.center.y + 8 : rect.center.y,
                rect.width, text.Length > 1 ? 12 : 14, warm ? Amber : Ink, TextAlignmentOptions.Midline);
        }

        private TMP_Text Label(TMP_FontAsset font, string text, float x, float y, float width, float size,
            Color ink, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = gameObject.layer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(x / Width, y / Height);
            rect.anchorMax = new Vector2((x + width) / Width, y / Height);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(0, 22);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.font = font; label.text = text;
            label.fontSize = size * rectTransform.sizeDelta.y / Height;
            label.alignment = alignment; label.color = ink; label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            detailLabels.Add(label);
            return label;
        }

        private void BuildToggle()
        {
            var go = new GameObject("Show all controls", typeof(RectTransform), typeof(Image), typeof(Button));
            go.layer = gameObject.layer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(164 / Width, 54 / Height);
            rect.anchorMax = new Vector2(196 / Width, 94 / Height);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.color = Color.clear;
            ExpandButton = go.GetComponent<Button>();
            ExpandButton.targetGraphic = image;
            // Change Image.color so the render-only terminal copy receives hover/press feedback too.
            ExpandButton.transition = Selectable.Transition.None;
            var pointer = go.AddComponent<EventTrigger>();
            TintOn(pointer,EventTriggerType.PointerEnter,image,new Color(.48f,.79f,.69f,.12f));
            TintOn(pointer,EventTriggerType.PointerExit,image,Color.clear);
            TintOn(pointer,EventTriggerType.PointerDown,image,new Color(.48f,.79f,.69f,.24f));
            TintOn(pointer,EventTriggerType.PointerUp,image,new Color(.48f,.79f,.69f,.12f));
            ExpandButton.navigation = new Navigation { mode = Navigation.Mode.None };
            ExpandButton.onClick.AddListener(() => SetExpanded(!IsExpanded));
        }

        private static void TintOn(EventTrigger trigger,EventTriggerType type,Image image,Color tint)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => image.color = tint);
            trigger.triggers.Add(entry);
        }

        public void SetExpanded(bool expanded)
        {
            IsExpanded = expanded;
            foreach (var label in detailLabels) label.gameObject.SetActive(expanded);
            RefreshNumberLabels();
            if (ExpandButton != null) ExpandButton.name = expanded ? "Hide extra controls" : "Show all controls";
            SetVerticesDirty();
        }

        private void RefreshNumberLabels()
        {
            for (int i = 0; i < numberLabels.Count; i++)
                numberLabels[i].gameObject.SetActive(IsExpanded && i < moduleCount && (!preview || i < 4));
        }

        public void SyncGraphicState(Graphic source)
        {
            if (!(source is AssemblyKeyGuide guide)) return;
            visualCopy = true;
            if (keycaps == guide.keycaps && pressed == guide.pressed && IsExpanded == guide.IsExpanded
                && connections == guide.connections && required == guide.required && moduleCount == guide.moduleCount
                && level == guide.level && preview == guide.preview) return;
            keycaps = guide.keycaps;
            pressed = guide.pressed; IsExpanded = guide.IsExpanded;
            connections = guide.connections; required = guide.required; moduleCount = guide.moduleCount;
            level = guide.level; preview = guide.preview;
            SetVerticesDirty();
        }

        public void SetState(string selected, int count, int joined, int total, bool complete, bool focused, int previewLevel = -1)
        {
            if (selectionLabel == null) return;
            selectionLabel.text = complete ? "ASSEMBLY COMPLETE" : selected;
            progressLabel.text = joined + " / " + total;
            modeLabel.text = complete ? "SYSTEM RESTORED" : preview ? "ASSEMBLY / PLAYTEST"
                : focused ? "ASSEMBLY / ACTIVE" : "ASSEMBLY / CLICK TO FOCUS";
            if (connections == joined && required == total && completed == complete && moduleCount == count && level == previewLevel) return;
            connections = joined; required = total; completed = complete; moduleCount = count; level = previewLevel;
            RefreshNumberLabels();
            SetVerticesDirty();
        }

        protected override void Awake() { base.Awake(); raycastTarget = false; }

        private void Update()
        {
            if (visualCopy) return;
            ulong mask = 0;
            var keyboard = Keyboard.current;
            if (ShowInput && !completed && Application.isFocused && Time.timeScale > 0 && keyboard != null)
                for (int i = 0; i < keycaps.Count; i++)
                    if (keyboard[keycaps[i].Key].isPressed || (keycaps[i].Key == Key.LeftShift && keyboard.rightShiftKey.isPressed))
                        mask |= 1ul << i;
            if (mask == pressed) return;
            pressed = mask; SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (IsExpanded)
            {
                RoundRect(vh, new Rect(0, 0, Width, Height), 9, new Color(.032f, .052f, .057f, .97f));
                Line(vh, new Vector2(22, 143), new Vector2(906, 143), .7f, new Color(.33f, .49f, .48f, .28f));
                foreach (float x in new[] { 194f, 378f, 556f, 738f })
                    Line(vh, new Vector2(x, 26), new Vector2(x, 115), .7f, new Color(.33f, .49f, .48f, .22f));
                int segments = Mathf.Clamp(required, 1, 8);
                for (int i = 0; i < segments; i++)
                    RoundRect(vh, new Rect(726 + i * (98f / segments), 157, 98f / segments - 4, 5), 2,
                        i < connections ? Mint : new Color(.15f, .24f, .24f, 1));
            }
            RoundRect(vh, new Rect(164, 54, 32, 40), 5, new Color(.1f, .16f, .17f, 1));
            Arrow(vh, new Vector2(180, 74), IsExpanded ? Vector2.left : Vector2.right, 5, Mint);
            for (int i = 0; i < keycaps.Count; i++)
            {
                if (!IsExpanded && i >= 6) break;
                var cap = keycaps[i];
                if (cap.Key >= Key.Digit1 && cap.Key <= Key.Digit5 && (cap.Key - Key.Digit1 >= moduleCount || (preview && cap.Key == Key.Digit5))) continue;
                bool down = (pressed & (1ul << i)) != 0;
                Color accent = cap.Warm ? Amber : Mint;
                if ((cap.Key == Key.F1 && level == 0) || (cap.Key == Key.F2 && level == 1)) down = true;
                RoundRect(vh, cap.Rect, 4, down ? accent * new Color(.58f, .58f, .58f, 1) : new Color(.19f, .27f, .28f, 1));
                var inset = new Rect(cap.Rect.x + 1, cap.Rect.y + 2, cap.Rect.width - 2, cap.Rect.height - 3);
                RoundRect(vh, inset, 3, down ? accent * new Color(.22f, .22f, .22f, 1) : new Color(.075f, .109f, .117f, 1));
                if (cap.Icon < 0) continue;
                var center = cap.Rect.center + new Vector2(0, -8);
                if (cap.Icon == 0 || cap.Icon == 2) Arc(vh, center, 7, cap.Icon == 2, accent);
                else Arrow(vh, center, cap.Icon == 1 ? Vector2.up : cap.Icon == 3 ? Vector2.left : cap.Icon == 4 ? Vector2.down : Vector2.right, 6, accent);
            }
            if (!IsExpanded) return;
            MouseIcon(vh, new Vector2(324, 45), false);
            Cross(vh, new Vector2(475, 95), 5, Muted);
            MouseIcon(vh, new Vector2(405, 45), false);
            foreach (var direction in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
                Arrow(vh, new Vector2(435, 45), direction, 5, Mint);
            // Separated connector halves distinguish detach from the circular reset symbol.
            Line(vh, new Vector2(616, 90), new Vector2(622, 90), 1.2f, Mint);
            Line(vh, new Vector2(616, 90), new Vector2(616, 100), 1.2f, Mint);
            Line(vh, new Vector2(616, 100), new Vector2(622, 100), 1.2f, Mint);
            Line(vh, new Vector2(633, 90), new Vector2(627, 90), 1.2f, Mint);
            Line(vh, new Vector2(633, 90), new Vector2(633, 100), 1.2f, Mint);
            Line(vh, new Vector2(633, 100), new Vector2(627, 100), 1.2f, Mint);
            Arc(vh, new Vector2(625, 45), 8, false, Mint);
            if (!preview) MouseIcon(vh, new Vector2(769, 45), true);
        }

        private void MouseIcon(VertexHelper vh, Vector2 center, bool right)
        {
            RoundRect(vh, new Rect(center.x - 8, center.y - 12, 16, 24), 7, Muted);
            RoundRect(vh, new Rect(center.x - 7, center.y - 11, 14, 22), 6, new Color(.032f, .052f, .057f, 1));
            RoundRect(vh, new Rect(center.x + (right ? 1 : -6), center.y + 2, 5, 7), 2, Mint);
            Line(vh, center + new Vector2(0, 3), center + new Vector2(0, 10), 1, Muted);
        }

        private void Cross(VertexHelper vh, Vector2 center, float radius, Color ink)
        {
            Line(vh, center - Vector2.right * radius, center + Vector2.right * radius, 1, ink);
            Line(vh, center - Vector2.up * radius, center + Vector2.up * radius, 1, ink);
        }

        private void Arc(VertexHelper vh, Vector2 center, float radius, bool clockwise, Color ink)
        {
            Vector2 last = Vector2.zero, tip = Vector2.zero, direction = Vector2.zero;
            for (int step = 0; step <= 24; step++)
            {
                float angle = (-35 + step * 280f / 24) * Mathf.Deg2Rad;
                tip = center + new Vector2(Mathf.Cos(angle) * (clockwise ? -1 : 1), Mathf.Sin(angle)) * radius;
                if (step > 0) { Line(vh, last, tip, 1.2f, ink); direction = (tip - last).normalized; }
                last = tip;
            }
            Head(vh, tip, direction, ink);
        }

        private void Arrow(VertexHelper vh, Vector2 center, Vector2 direction, float radius, Color ink)
        {
            var tip = center + direction * radius;
            Line(vh, center - direction * radius * .6f, tip, 1.2f, ink);
            Head(vh, tip, direction, ink);
        }

        private void Head(VertexHelper vh, Vector2 tip, Vector2 direction, Color ink)
        {
            var side = new Vector2(-direction.y, direction.x);
            Line(vh, tip - direction * 3 + side * 2.8f, tip, 1.2f, ink);
            Line(vh, tip - direction * 3 - side * 2.8f, tip, 1.2f, ink);
        }

        private void RoundRect(VertexHelper vh, Rect rect, float radius, Color ink)
        {
            Vector2 first = Vector2.zero, previous = Vector2.zero;
            for (int corner = 0; corner < 4; corner++)
            {
                var center = new Vector2(corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius,
                    corner < 2 ? rect.yMax - radius : rect.yMin + radius);
                for (int step = 0; step <= 4; step++)
                {
                    float angle = (corner * 90 + step * 22.5f) * Mathf.Deg2Rad;
                    var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    if (corner == 0 && step == 0) first = point;
                    else Triangle(vh, rect.center, previous, point, ink);
                    previous = point;
                }
            }
            Triangle(vh, rect.center, previous, first, ink);
        }

        private void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color ink)
        {
            var side = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            Triangle(vh, a - side, a + side, b + side, ink);
            Triangle(vh, a - side, b + side, b - side, ink);
        }

        private Vector2 Point(Vector2 design)
        {
            Rect rect = rectTransform.rect;
            return new Vector2(rect.xMin + design.x * rect.width / Width, rect.yMin + design.y * rect.height / Height);
        }

        private void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color ink)
        {
            int first = vh.currentVertCount;
            vh.AddVert(Point(a), ink, Vector2.zero); vh.AddVert(Point(b), ink, Vector2.zero); vh.AddVert(Point(c), ink, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2);
        }
    }
}
