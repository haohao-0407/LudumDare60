using System;
using UnityEngine;

namespace LudumDare60.Salvage
{
    /// <summary>Optional Unity lifecycle adapter. Supply input from UI or the project's Input Actions.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Salvage/Salvage Controller")]
    public sealed class SalvageController : MonoBehaviour
    {
        [SerializeField] private SalvagePool pool;
        [SerializeField] private SalvageDefinition defaultDefinition;
        private SalvageSession session;
        private bool gameplayBlocked;
        private bool manuallyPaused;

        private SalvageSession Session
        {
            get
            {
                if (session != null) return session;
                session = new SalvageSession();
                session.SnapshotChanged += ForwardSnapshot;
                session.Completed += ForwardCompletion;
                return session;
            }
        }

        public SalvageSnapshot Snapshot => Session.Snapshot;
        public SalvageResult Result => Session.Result;
        public bool IsActive => Session.IsActive;
        public bool CanAcceptInput => isActiveAndEnabled && !gameplayBlocked && !manuallyPaused;
        public SalvagePool Pool => pool;
        public SalvageDefinition DefaultDefinition => defaultDefinition;

        /// <summary>Mission reading/menu/end-state pause, independent of a local salvage pause.</summary>
        public bool GameplayBlocked
        {
            get => gameplayBlocked;
            set
            {
                if (gameplayBlocked == value) return;
                gameplayBlocked = value;
                Session.SetPaused(gameplayBlocked || manuallyPaused);
                if (gameplayBlocked) Session.SetControlHeld(false);
            }
        }
        public event Action<SalvageSnapshot> SnapshotChanged;
        public event Action<SalvageResult> Completed;

        [Header("音效")]
        [Tooltip("单发音效用的声源。留空自动建一个子物体")]
        [SerializeField] private AudioSource sfxSource;
        [Tooltip("作业循环用的独立声源。留空自动建")]
        [SerializeField] private AudioSource workLoopSource;
        [Tooltip("按下左键/空格时的起手音（UI_relay_switch）")]
        [SerializeField] private AudioClip startClip;
        [Tooltip("开始打捞后循环播放（COM_Telegraph_ACK）")]
        [SerializeField] private AudioClip workLoopClip;
        [Tooltip("打捞成功（UI_confirm）")]
        [SerializeField] private AudioClip successClip;
        [Tooltip("打捞失败/取消（UI_error）")]
        [SerializeField] private AudioClip failureClip;
        [SerializeField, Range(0f, 1f)] private float startVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] private float workLoopVolume = 0.7f;
        [SerializeField, Range(0f, 1f)] private float successVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float failureVolume = 1f;
        [Tooltip("停止作业循环时的淡出时长，0 = 硬停（可能咔哒）")]
        [SerializeField, Min(0f)] private float loopFadeOut = 0.15f;
        [Tooltip("起手音之后多久进循环。0 = 同时；60~90ms 能听出「扳开关 → 机器转起来」的因果")]
        [SerializeField, Min(0f)] private float loopStartDelay = 0.06f;
        private Coroutine loopFade;
        private Coroutine loopStart;
        private bool engaged;

        /// <summary>Draws one target from the pool, falling back to the default definition when no pool is assigned.</summary>
        public bool TryStartSalvage() => TryStartRandomSalvage();

        /// <summary>The same seed drives both the pool draw and the simulation, so a seed reproduces the whole attempt.</summary>
        public bool TryStartRandomSalvage(int? randomSeed = null)
        {
            if (!CanAcceptInput || Session.IsActive) return false;
            var seed = randomSeed ?? Guid.NewGuid().GetHashCode();
            var definition = pool != null ? pool.Pick(seed) : defaultDefinition;
            if (definition == null) return false;
            var started = Session.TryStart(definition.CreateSettings(), definition.CreateTarget(), seed);
            // 新一轮：先静音，等玩家真的按下（鼠标左键 / 空格）才起循环
            if (started) { engaged = false; StopWorkLoop(); }
            return started;
        }

        public bool TryStartSalvage(SalvageDefinition definition, int? randomSeed = null)
        {
            if (!CanAcceptInput || definition == null || Session.IsActive) return false;
            var started = Session.TryStart(definition.CreateSettings(), definition.CreateTarget(), randomSeed);
            if (started) { engaged = false; StopWorkLoop(); }
            return started;
        }

        // Parameterless methods are also convenient targets for future UnityEvents.
        public void StartSalvage() => TryStartSalvage();
        public void PressControl() => SetControlHeld(true);
        public void ReleaseControl() => SetControlHeld(false);
        public void SetControlHeld(bool held)
        {
            if (!held || CanAcceptInput) Session.SetControlHeld(held);

            // 只有「这次按下真的生效」才出声：状态必须已经进入 Salvaging（滑块能动）。
            // 搜索阶段乱按、暂停中按下都不会响——那时候按下没有任何作用。
            if (held && CanAcceptInput && !engaged && Snapshot.State == SalvageState.Salvaging)
            {
                engaged = true;
                PlayStartAndScheduleLoop();
            }
        }
        public bool TryLockTarget() => CanAcceptInput && Session.TryLockTarget();
        public void SetPaused(bool paused) { manuallyPaused = paused; Session.SetPaused(paused || gameplayBlocked); }
        public void CancelSalvage() => Session.Cancel();

        private void Update() { if (!gameplayBlocked) Session.Tick(Time.deltaTime); }

        private void OnDisable()
        {
            // Stop the attempt and release input when its terminal/object is disabled or destroyed.
            session?.Cancel();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) session?.SetControlHeld(false);
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused) session?.SetControlHeld(false);
        }

        private void OnDestroy()
        {
            if (session == null) return;
            session.SnapshotChanged -= ForwardSnapshot;
            session.Completed -= ForwardCompletion;
        }

        private void ForwardSnapshot(SalvageSnapshot snapshot) => SnapshotChanged?.Invoke(snapshot);
        private void ForwardCompletion(SalvageResult result)
        {
            StopWorkLoop();
            // 玩家没按过键（空转超时）就不播结果音，否则界面开着会一直哔
            if (engaged) PlayResult(result != null && result.IsSuccess);
            engaged = false;
            Completed?.Invoke(result);
        }

        // ── 音效 ────────────────────────────────────────
        /// <summary>起手音立刻响，循环延后一点点进，避免两段瞬态撞在一起。</summary>
        private void PlayStartAndScheduleLoop()
        {
            EnsureAudioSources();
            if (startClip != null && sfxSource != null)
                sfxSource.PlayOneShot(startClip, startVolume);

            if (loopStart != null) StopCoroutine(loopStart);
            loopStart = null;
            if (loopStartDelay <= 0f) StartWorkLoop();
            else loopStart = StartCoroutine(StartLoopAfterDelay(loopStartDelay));
        }

        private System.Collections.IEnumerator StartLoopAfterDelay(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            loopStart = null;
            if (engaged) StartWorkLoop();
        }

        private void StartWorkLoop()
        {
            EnsureAudioSources();
            if (workLoopSource == null || workLoopClip == null) return;
            if (loopFade != null) { StopCoroutine(loopFade); loopFade = null; }
            workLoopSource.clip = workLoopClip;
            workLoopSource.loop = true;
            workLoopSource.volume = workLoopVolume;
            if (!workLoopSource.isPlaying) workLoopSource.Play();
        }

        private void StopWorkLoop()
        {
            if (loopStart != null) { StopCoroutine(loopStart); loopStart = null; }
            if (workLoopSource == null || !workLoopSource.isPlaying) return;
            if (loopFade != null) StopCoroutine(loopFade);
            loopFade = loopFadeOut > 0f ? StartCoroutine(FadeOutLoop(loopFadeOut)) : null;
            if (loopFade == null) workLoopSource.Stop();
        }

        private System.Collections.IEnumerator FadeOutLoop(float seconds)
        {
            float from = workLoopSource.volume;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                workLoopSource.volume = Mathf.Lerp(from, 0f, t / seconds);
                yield return null;
            }
            workLoopSource.Stop();
            workLoopSource.volume = workLoopVolume;
            loopFade = null;
        }

        private void PlayResult(bool success)
        {
            var clip = success ? successClip : failureClip;
            if (clip == null) return;
            EnsureAudioSources();
            if (sfxSource != null) sfxSource.PlayOneShot(clip, success ? successVolume : failureVolume);
        }

        private void EnsureAudioSources()
        {
            if (sfxSource == null) sfxSource = CreateVoice("Salvage SFX");
            if (workLoopSource == null) workLoopSource = CreateVoice("Salvage Work Loop");
        }

        private AudioSource CreateVoice(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 1f;
            return source;
        }
    }
}
