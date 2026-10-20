using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace LudumDare60.TerminalDialogue
{
    [Serializable]
    public sealed class TerminalEventBinding
    {
        public string EventId;
        public UnityEvent Action = new UnityEvent();
    }

    [DisallowMultipleComponent]
    public sealed class TerminalDialogueController : MonoBehaviour
    {
        [SerializeField] private TerminalDialogueDefinition conversation;
        [SerializeField] private bool startAutomatically = true;
        [SerializeField, Min(0)] private float aiCharactersPerSecond = 30;
        [SerializeField, Min(0)] private float playerCharactersPerSecond = 55;
        [SerializeField, Min(2)] private int historyLimit = 150;
        [SerializeField] private bool useUnscaledTime = true;
        [SerializeField] private List<TerminalEventBinding> eventBindings = new List<TerminalEventBinding>();

        [Header("音效")]
        [Tooltip("留空则用本物体上的 AudioSource，没有就自动建一个子物体")]
        [SerializeField] private AudioSource sfxSource;
        [Tooltip("终端/AI 出现（UI_terminal_boot）")]
        [SerializeField] private AudioClip openClip;
        [Tooltip("终端/AI 消失（UI_terminal_close）")]
        [SerializeField] private AudioClip closeClip;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;
        [SerializeField] private UnityEvent<bool> interactionChanged = new UnityEvent<bool>();
        private TerminalDialogueSession session;
        private int lastRevision = -1;
        private bool dispatching;

        public TerminalDialogueSession Session => session;
        public TerminalDialogueDefinition Conversation => conversation;
        public bool IsStoryReading => session != null && session.IsStoryReading;
        public event Action Changed;
        public event Action<string> EventTriggered;
        /// <summary>Accepted input at click time; normal authored events still wait for player typing.</summary>
        public event Action<string, string> ChoiceAccepted;

        private void Awake()
        {
            EnsureSfxSource();
        }

        private void OnEnable()
        {
            Pioneer60.Localization.Loc.LanguageChanged += HandleLanguageChanged;
        }

        /// <summary>中途切换语言：不重置进度，只把已经显示的和还没播的台词换成新语言。</summary>
        private void HandleLanguageChanged(string code)
        {
            if (session == null) return;
            session.Relocalize(
                LudumDare60.Localization.TerminalDialogueLocalizer.Localize(conversation != null ? conversation.Graph : null),
                Pioneer60.Localization.Loc.Say);
            Publish();
        }

        private void Start()
        {
            if (startAutomatically && session == null) Begin();
        }

        private void Update()
        {
            if (session == null) return;
            session.Tick(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime,
                aiCharactersPerSecond, playerCharactersPerSecond);
            Publish();
        }

        private void OnDisable()
        {
            Pioneer60.Localization.Loc.LanguageChanged -= HandleLanguageChanged;
            Close();
        }

        public void Configure(TerminalDialogueDefinition definition) => conversation = definition;

        /// <summary>
        /// 音源在 Awake 建好。以前是首次播放时懒创建，如果那一刻正好在关闭终端
        /// （父物体正在停用）就会报 "Can't set the parent ... while activating or deactivating"
        /// 并且对象随场景关闭泄漏。
        /// </summary>
        private void EnsureSfxSource()
        {
            if (sfxSource != null) return;
            var go = new GameObject("Terminal SFX");
            go.transform.SetParent(transform, false);
            sfxSource = go.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
        }

        private void PlaySfx(AudioClip clip)
        {
            if (clip == null) return;
            EnsureSfxSource();
            if (sfxSource == null) return;
            sfxSource.PlayOneShot(clip, sfxVolume);
        }

        /// <summary>Explicit new run: resets history, progress and flags. Open resumes instead.</summary>
        public void Begin()
        {
            if (!isActiveAndEnabled || conversation == null)
            {
                Debug.LogWarning("Terminal Dialogue：请先设置 Conversation，且启用控制器。", this);
                return;
            }
            TerminalDialogueSession next;
            try { next = new TerminalDialogueSession(LudumDare60.Localization.TerminalDialogueLocalizer.Localize(conversation.Graph), historyLimit); }
            catch (ArgumentException exception) { Debug.LogError(exception.Message, conversation); return; }
            var wasOpen = session != null && session.IsOpen;
            session = next;
            lastRevision = -1;
            session.Start();
            if (!wasOpen) interactionChanged.Invoke(true);
            Publish();
        }

        public void Open()
        {
            if (!isActiveAndEnabled) return;
            if (session == null) { Begin(); return; }
            if (session.IsOpen) return;
            session.Open();
            PlaySfx(openClip);
            interactionChanged.Invoke(true);
            Publish();
        }

        public void Close()
        {
            if (session == null || !session.IsOpen) return;
            session.Close();
            PlaySfx(closeClip);
            interactionChanged.Invoke(false);
            Publish();
        }

        public void Advance() { session?.Advance(); Publish(); }
        public bool Choose(string choiceId)
        {
            var nodeId = session?.CurrentNodeId;
            var accepted = session != null && session.Choose(choiceId);
            if (accepted) ChoiceAccepted?.Invoke(nodeId, choiceId);
            Publish();
            return accepted;
        }
        public bool GoToNode(string nodeId)
        {
            var changed = session != null && session.GoToNode(nodeId);
            Publish();
            return changed;
        }
        public bool GetFlag(string key) => session != null && session.GetFlag(key);
        public void SetFlag(string key, bool value) { session?.SetFlag(key, value); Publish(); }
        public void SetFlagTrue(string key) => SetFlag(key, true);
        public void SetFlagFalse(string key) => SetFlag(key, false);
        public void PostSystemMessage(string text) { session?.PostSystemMessage(Pioneer60.Localization.Loc.Say(text)); Publish(); }

        private void Publish()
        {
            if (dispatching) return;
            dispatching = true;
            try
            {
                var source = session;
                var count = 0;
                while (source != null && ReferenceEquals(session, source) && source.TryDequeueEvent(out var id))
                {
                    if (++count > 128) { Debug.LogError("Terminal Dialogue：事件递归过深，请检查绑定。", this); break; }
                    var handled = EventTriggered != null;
                    EventTriggered?.Invoke(id);
                    foreach (var binding in eventBindings)
                    {
                        if (binding == null || binding.EventId != id) continue;
                        handled = true;
                        binding.Action?.Invoke();
                    }
                    if (!handled) Debug.LogWarning("Terminal Dialogue：事件尚未绑定：" + id, this);
                }
                if (session != null && lastRevision != session.Revision)
                {
                    lastRevision = session.Revision;
                    Changed?.Invoke();
                }
            }
            finally { dispatching = false; }
        }
    }
}
