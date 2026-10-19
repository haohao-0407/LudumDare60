using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LudumDare60.Landing
{
    /// <summary>
    /// Draws a standby page inside an existing cockpit UI canvas. The mounted physical display,
    /// render texture and capture camera keep running; only the supplied input behaviours are gated.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CockpitStandbyScreen : MonoBehaviour
    {
        public RectTransform ScreenRoot;
        public TMP_FontAsset Font;
        public string SystemTitle = "AUXILIARY SYSTEM";
        [TextArea(2, 4)] public string StatusText = "SYSTEM AVAILABLE WHEN REQUIRED\nAWAITING ACTIVATION";
        [Tooltip("Only input scripts, such as SalvageScreenView or AssemblyScreenInput. Never the Canvas, screen mount, camera or renderer.")]
        public Behaviour[] InputBehaviours = Array.Empty<Behaviour>();
        [SerializeField] bool standby = true;

        public bool IsStandby => standby;

        static readonly Color Background = new Color(.008f, .019f, .014f, 1f);
        static readonly Color Ink = new Color(.68f, .96f, .76f, 1f);
        static readonly Color Dim = new Color(.34f, .61f, .47f, 1f);
        static readonly Color Frame = new Color(.17f, .36f, .28f, 1f);

        struct InputState
        {
            public Behaviour Behaviour;
            public bool Enabled;
        }

        readonly List<InputState> inputStates = new List<InputState>();
        RectTransform boundRoot;
        RectTransform overlay;
        CanvasGroup contentGroup;
        bool ownsContentGroup;
        bool groupWasEnabled;
        bool contentWasInteractable;
        bool contentBlockedRaycasts;
        bool applied;
        TMP_FontAsset resolvedFont;
        TextMeshProUGUI header;
        TextMeshProUGUI mode;
        TextMeshProUGUI title;
        TextMeshProUGUI description;
        TextMeshProUGUI footer;
        Image indicator;
        Vector2 lastSize;

        void OnEnable()
        {
            if (Application.isPlaying) ApplyState();
        }

        void OnDisable()
        {
            RestoreInteraction();
            if (overlay) overlay.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            ReleaseOverlay();
        }

        /// <summary>Locks the covered UI and its explicit input scripts, or restores their saved state.</summary>
        public void SetStandby(bool value)
        {
            standby = value;
            if (Application.isPlaying && isActiveAndEnabled) ApplyState();
        }

        void LateUpdate()
        {
            if (!Application.isPlaying) return;
            if (ScreenRoot != boundRoot || (standby && !overlay)) ApplyState();
            if (!standby || !overlay || !overlay.gameObject.activeSelf) return;

            // A curved-screen mount can move the canvas to its isolated capture layer after Awake.
            // Newly generated UI must follow that layer rather than disappearing from its camera.
            if (overlay.gameObject.layer != boundRoot.gameObject.layer)
                SetLayer(overlay, boundRoot.gameObject.layer);
            overlay.SetAsLastSibling();
            RefreshText();
            if (boundRoot.rect.size != lastSize) ResizeText();
            if (indicator)
                indicator.color = Color.Lerp(Dim, Ink, .55f + .2f * Mathf.Sin(Time.unscaledTime * 1.7f));
        }

        void ApplyState()
        {
            if (!ScreenRoot) ScreenRoot = transform as RectTransform;
            if (boundRoot != ScreenRoot) ReleaseOverlay();
            if (!ScreenRoot) return;
            if (!standby)
            {
                RestoreInteraction();
                if (overlay) overlay.gameObject.SetActive(false);
                else boundRoot = ScreenRoot;
                return;
            }

            EnsureOverlay();
            if (!overlay) return;
            SuppressInteraction();
            RefreshText();
            ResizeText();
            if (overlay.gameObject.layer != boundRoot.gameObject.layer)
                SetLayer(overlay, boundRoot.gameObject.layer);
            overlay.SetAsLastSibling();
            overlay.gameObject.SetActive(true);
        }

        void EnsureOverlay()
        {
            if (overlay) return;
            boundRoot = ScreenRoot;
            resolvedFont = Font;
            if (!resolvedFont)
            {
                foreach (TMP_Text existing in boundRoot.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (!existing.font) continue;
                    resolvedFont = existing.font;
                    break;
                }
            }
            if (!resolvedFont) resolvedFont = TMP_Settings.defaultFontAsset;

            contentGroup = boundRoot.GetComponent<CanvasGroup>();
            ownsContentGroup = !contentGroup;
            if (!contentGroup) contentGroup = boundRoot.gameObject.AddComponent<CanvasGroup>();

            overlay = Rect("__CockpitStandby", boundRoot, 0f, 0f, 1f, 1f);
            overlay.gameObject.SetActive(false);
            var background = overlay.gameObject.AddComponent<Image>();
            background.color = Background;
            background.raycastTarget = true;
            var overlayGroup = overlay.gameObject.AddComponent<CanvasGroup>();
            overlayGroup.ignoreParentGroups = true;
            overlayGroup.interactable = true;
            overlayGroup.blocksRaycasts = true;
            // EventTrigger implements the event interfaces even with no entries: a hit on the
            // standby backdrop must not bubble up to SalvageScreenView's root pointer handlers.
            overlay.gameObject.AddComponent<EventTrigger>();

            Panel("Frame Top", .04f, .95f, .96f, .9525f, Frame);
            Panel("Frame Bottom", .04f, .0475f, .96f, .05f, Frame);
            Panel("Frame Left", .04f, .05f, .042f, .95f, Frame);
            Panel("Frame Right", .958f, .05f, .96f, .95f, Frame);
            Panel("Header Rule", .075f, .805f, .925f, .807f, Frame);
            Panel("Footer Rule", .075f, .205f, .925f, .207f, Frame);
            for (int i = 1; i < 25; i++)
            {
                float y = .055f + i * .0355f;
                Panel("Scanline " + i, .044f, y, .956f, y + .001f, new Color(.3f, .65f, .42f, .035f));
            }

            header = Label("OS Header", "", .075f, .835f, .925f, .925f, Dim, TextAlignmentOptions.MidlineLeft);
            indicator = Panel("Power Indicator", .078f, .714f, .095f, .74f, Ink);
            mode = Label("Power State", "CONSOLE ONLINE / LOW POWER", .12f, .69f, .925f, .765f, Dim, TextAlignmentOptions.MidlineLeft);
            title = Label("Standby Title", "STANDBY", .075f, .445f, .925f, .66f, Ink, TextAlignmentOptions.Center);
            title.characterSpacing = 6f;
            description = Label("Standby Description", "", .1f, .29f, .9f, .415f, Dim, TextAlignmentOptions.Center);
            footer = Label("Footer", "READY / AWAITING COMMAND", .075f, .08f, .925f, .17f, Dim, TextAlignmentOptions.MidlineLeft);
            RefreshText();
            ResizeText();
        }

        void SuppressInteraction()
        {
            if (applied) return;
            applied = true;
            groupWasEnabled = contentGroup.enabled;
            contentWasInteractable = contentGroup.interactable;
            contentBlockedRaycasts = contentGroup.blocksRaycasts;
            contentGroup.enabled = true;
            contentGroup.interactable = false;
            contentGroup.blocksRaycasts = false;

            EventSystem events = EventSystem.current;
            if (events && events.currentSelectedGameObject &&
                events.currentSelectedGameObject.transform.IsChildOf(boundRoot))
                events.SetSelectedGameObject(null);

            inputStates.Clear();
            if (InputBehaviours == null) return;
            foreach (Behaviour input in InputBehaviours)
            {
                if (!input || input == this || input is Canvas || input is Camera || input is GraphicRaycaster) continue;
                bool duplicate = false;
                for (int i = 0; i < inputStates.Count; i++)
                    if (inputStates[i].Behaviour == input) { duplicate = true; break; }
                if (duplicate) continue;
                inputStates.Add(new InputState { Behaviour = input, Enabled = input.enabled });
                input.enabled = false;
            }
        }

        void RestoreInteraction()
        {
            if (!applied) return;
            applied = false;
            if (contentGroup)
            {
                contentGroup.interactable = contentWasInteractable;
                contentGroup.blocksRaycasts = contentBlockedRaycasts;
                contentGroup.enabled = groupWasEnabled;
            }
            foreach (InputState state in inputStates)
                if (state.Behaviour) state.Behaviour.enabled = state.Enabled;
            inputStates.Clear();
        }

        void ReleaseOverlay()
        {
            RestoreInteraction();
            if (overlay)
            {
                overlay.gameObject.SetActive(false);
                Destroy(overlay.gameObject);
            }
            if (ownsContentGroup && contentGroup) Destroy(contentGroup);
            overlay = null;
            boundRoot = null;
            contentGroup = null;
            ownsContentGroup = false;
        }

        void RefreshText()
        {
            if (header) header.text = "SHIP OS / " + (SystemTitle ?? string.Empty);
            if (description) description.text = StatusText ?? string.Empty;
        }

        void ResizeText()
        {
            if (!boundRoot) return;
            lastSize = boundRoot.rect.size;
            float unit = Mathf.Max(1f, Mathf.Min(Mathf.Abs(lastSize.x), Mathf.Abs(lastSize.y)));
            Size(header, unit * .043f);
            Size(mode, unit * .032f);
            Size(title, unit * .18f);
            Size(description, unit * .039f);
            Size(footer, unit * .03f);
        }

        static void Size(TextMeshProUGUI text, float maximum)
        {
            if (!text) return;
            text.enableAutoSizing = true;
            text.fontSizeMax = Mathf.Max(1f, maximum);
            text.fontSizeMin = Mathf.Max(.5f, maximum * .5f);
            text.fontSize = text.fontSizeMax;
        }

        Image Panel(string name, float x0, float y0, float x1, float y1, Color color)
        {
            var image = Rect(name, overlay, x0, y0, x1, y1).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        TextMeshProUGUI Label(string name, string value, float x0, float y0, float x1, float y1,
            Color color, TextAlignmentOptions alignment)
        {
            var text = Rect(name, overlay, x0, y0, x1, y1).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = resolvedFont;
            text.text = value;
            text.color = color;
            text.alignment = alignment;
            text.richText = false;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        static RectTransform Rect(string name, RectTransform parent, float x0, float y0, float x1, float y1)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = parent.gameObject.layer;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i), layer);
        }
    }
}
