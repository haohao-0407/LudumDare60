using System.Text;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace LudumDare60.TerminalDialogue
{
    /// <summary>Attach to the computer's world-space canvas. The controller does not own this object's transform.</summary>
    public sealed class TerminalTranscriptView : MonoBehaviour
    {
        [SerializeField] private TerminalDialogueController controller;
        [SerializeField] private TMP_Text transcriptText;
        [SerializeField] private TMP_Text cursorText;
        [SerializeField] private UnityEngine.UI.ScrollRect scrollRect;
        [SerializeField] private AudioSource typingAudio;
        [SerializeField] private AudioClip typingClip;
        [SerializeField, Min(0.02f)] private float soundInterval = 0.06f;
        private TerminalDialogueSession displayedSession;
        private int transcriptRevision = -1;
        private int activeTextStart;
        private int lastVisible;
        private bool followPending;
        private float nextSoundTime;
        private struct ColorSpan { public int End; public Color32 Color; }
        private readonly List<ColorSpan> colorSpans = new List<ColorSpan>();
        internal TMP_Text TranscriptText => transcriptText;

        public void Configure(TerminalDialogueController source, TMP_Text text, TMP_Text cursor,
            UnityEngine.UI.ScrollRect scroll)
        {
            Unsubscribe();
            controller = source; transcriptText = text; cursorText = cursor; scrollRect = scroll;
            if (isActiveAndEnabled) Subscribe();
            Refresh();
        }

        private void OnEnable() { Subscribe(); Refresh(); }
        private void OnDisable() => Unsubscribe();
        private void Subscribe()
        {
            if (controller != null) controller.Changed += Refresh;
            if (transcriptText != null) transcriptText.OnPreRenderText += ApplyTranscriptColors;
        }
        private void Unsubscribe()
        {
            if (controller != null) controller.Changed -= Refresh;
            if (transcriptText != null) transcriptText.OnPreRenderText -= ApplyTranscriptColors;
        }

        private void Refresh()
        {
            var session = controller != null ? controller.Session : null;
            if (session == null || transcriptText == null) return;
            if (displayedSession != session || transcriptRevision != session.TranscriptRevision)
            {
                followPending = displayedSession != session || scrollRect == null || scrollRect.verticalNormalizedPosition <= 0.025f;
                displayedSession = session;
                transcriptRevision = session.TranscriptRevision;
                var builder = new StringBuilder();
                colorSpans.Clear();
                for (var i = 0; i < session.Messages.Count; i++)
                {
                    var message = session.Messages[i];
                    if (i > 0) builder.Append("\n\n");
                    builder.Append(message.GetPrefix(session.AIName));
                    if (i == session.Messages.Count - 1) activeTextStart = builder.Length;
                    builder.Append(message.Text);
                    var color = message.TextColor;
                    colorSpans.Add(new ColorSpan { End = builder.Length, Color = new Color(color.R, color.G, color.B, color.A) });
                }
                // Plain text: authored '<...>' and player command symbols must remain literal.
                transcriptText.richText = false;
                transcriptText.text = builder.ToString();
                transcriptText.ForceMeshUpdate();
                lastVisible = 0;
            }
            var maxCodeUnits = session.HasActiveLine ? activeTextStart + session.VisibleCodeUnits : int.MaxValue;
            var visibleCharacters = transcriptText.textInfo.characterCount;
            for (var i = 0; i < transcriptText.textInfo.characterCount; i++)
            {
                if (transcriptText.textInfo.characterInfo[i].index < maxCodeUnits) continue;
                visibleCharacters = i;
                break;
            }
            transcriptText.maxVisibleCharacters = visibleCharacters;
            if (session.IsOpen && session.Phase == TerminalPhase.Typing && session.VisibleCodeUnits > lastVisible
                && typingAudio != null && typingClip != null && Time.unscaledTime >= nextSoundTime)
            {
                typingAudio.PlayOneShot(typingClip);
                nextSoundTime = Time.unscaledTime + soundInterval;
            }
            lastVisible = session.VisibleCodeUnits;
        }

        // Color actual glyph vertices, keeping authored <...> literal and the typing character indices intact.
        // The curved capture subscribes to the same callback so its disabled view needs no dialogue/audio logic.
        internal void ApplyTranscriptColors(TMP_TextInfo info)
        {
            var span = 0;
            for (var i = 0; i < info.characterCount; i++)
            {
                var character = info.characterInfo[i];
                while (span < colorSpans.Count && character.index >= colorSpans[span].End) span++;
                if (span >= colorSpans.Count) break;
                if (!character.isVisible) continue;
                var colors = info.meshInfo[character.materialReferenceIndex].colors32;
                for (var j = 0; j < 4; j++) colors[character.vertexIndex + j] = colorSpans[span].Color;
            }
        }

        private void LateUpdate()
        {
            if (cursorText != null)
                cursorText.enabled = controller != null && controller.Session != null
                    && controller.Session.IsOpen && (int)(Time.unscaledTime * 2) % 2 == 0;
            if (!followPending || scrollRect == null) return;
            followPending = false;
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0;
        }
    }
}
