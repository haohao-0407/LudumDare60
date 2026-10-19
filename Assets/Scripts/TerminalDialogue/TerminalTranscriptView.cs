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

        [Header("打字音")]
        [Tooltip("模板声源：音量以外的声道/混音/空间设置以它为准，其余声部运行时按它复制")]
        [SerializeField] private AudioSource typingAudio;
        [Tooltip("逐字随机挑一条播放。留空则不发声。")]
        [SerializeField] private AudioClip[] typingClips = new AudioClip[0];
        [Tooltip("同时发声的声部数量。1 个够用；音高变化明显时需要 3-4 个避免变调打断上一声。")]
        [SerializeField, Min(1)] private int typingVoices = 4;
        [Tooltip("两次击键的最小间隔。字数速度高于此值时以这个封顶，避免糊成一片。")]
        [SerializeField, Min(0.008f)] private float typingMinInterval = 0.035f;
        [Tooltip("随机音高范围（1 = 原调）")]
        [SerializeField] private Vector2 typingPitch = new Vector2(0.93f, 1.09f);
        [Tooltip("随机音量范围")]
        [SerializeField] private Vector2 typingVolume = new Vector2(0.65f, 1f);
        [Tooltip("一次刷新涌入超过这么多字时视为「跳过」，只出一次声")]
        [SerializeField, Min(1)] private int typingBurstLimit = 6;

        private TerminalDialogueSession displayedSession;
        private int transcriptRevision = -1;
        private int activeTextStart;
        private int lastVisible;
        private bool followPending;
        private AudioSource[] voices;
        private int voiceCursor;
        private float nextTypingTime;

        /// <summary>已播放的击键次数，调试用。</summary>
        public int TypingSoundsPlayed { get; private set; }
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

            // 逐字击键：字出现了才响，一次刷新最多响一声（跳过整段时只响一下）
            var newCharacters = session.VisibleCodeUnits - lastVisible;
            if (newCharacters > 0 && session.IsOpen && session.Phase == TerminalPhase.Typing
                && newCharacters <= typingBurstLimit)
                PlayTypingClick();
            lastVisible = session.VisibleCodeUnits;
        }

        private void PlayTypingClick()
        {
            if (typingAudio == null || typingClips == null || typingClips.Length == 0) return;
            if (Time.unscaledTime < nextTypingTime) return;
            EnsureVoices();

            nextTypingTime = Time.unscaledTime + typingMinInterval;
            var source = voices[voiceCursor];
            voiceCursor = (voiceCursor + 1) % voices.Length;
            if (source == null) return;

            source.pitch = Random.Range(typingPitch.x, typingPitch.y);
            source.PlayOneShot(typingClips[Random.Range(0, typingClips.Length)],
                Random.Range(typingVolume.x, typingVolume.y));
            TypingSoundsPlayed++;
        }

        /// <summary>按模板声源复制出若干声部，让随机音高互不打断。</summary>
        private void EnsureVoices()
        {
            if (voices != null && voices.Length > 0) return;
            var count = Mathf.Max(1, typingVoices);
            voices = new AudioSource[count];
            typingAudio.playOnAwake = false;
            typingAudio.loop = false;
            voices[0] = typingAudio;
            for (var i = 1; i < count; i++)
            {
                var go = new GameObject("Typing Voice " + i);
                go.transform.SetParent(typingAudio.transform, false);
                var copy = go.AddComponent<AudioSource>();
                copy.outputAudioMixerGroup = typingAudio.outputAudioMixerGroup;
                copy.spatialBlend = typingAudio.spatialBlend;
                copy.minDistance = typingAudio.minDistance;
                copy.maxDistance = typingAudio.maxDistance;
                copy.rolloffMode = typingAudio.rolloffMode;
                copy.dopplerLevel = typingAudio.dopplerLevel;
                copy.priority = typingAudio.priority;
                copy.volume = 1f;
                copy.playOnAwake = false;
                copy.loop = false;
                voices[i] = copy;
            }
            voiceCursor = 0;
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
