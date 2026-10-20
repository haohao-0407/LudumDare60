using LudumDare60.Landing;
using LudumDare60.UI;
using UnityEngine;

namespace LudumDare60.Audio
{
    /// <summary>
    /// 坠毁后的 BGM 编排。
    ///
    /// 规则（按需求）：
    ///   1. 飞船砸到地面后，等 <see cref="firstDelay"/> 秒开始播；
    ///   2. 一曲播完，若局还没结束，等 <see cref="replayGap"/> 秒再播一遍，如此循环；
    ///   3. 「局结束」= 四台设备全部修复完成 → 主动收尾，但不是硬切：
    ///      用 <see cref="fadeOutSeconds"/> 秒把音量推到 0 再停，且此后不再重播。
    ///
    /// 时间用 <c>Time.deltaTime</c>（会被 timeScale 缩放），所以 ESC 暂停时倒计时也会停，
    /// 同时音乐跟着 Pause，恢复后 UnPause 接着放。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrashMusicDirector : MonoBehaviour
    {
        private enum Stage { WaitingCrash, WaitingFirstPlay, Playing, WaitingReplay, FadingOut, Finished }

        [Header("引用")]
        [Tooltip("播放 BGM 的声源（Loop 关闭、Play On Awake 关闭）")]
        [SerializeField] private AudioSource music;
        [Tooltip("坠毁来源：撞击发生（IsDamaged 变 true）后开始计时")]
        [SerializeField] private HardLandingDamageEffects crash;
        [Tooltip("维修进度来源：四台设备全部 Repaired 视为本局结束")]
        [SerializeField] private RepairScreenController repairScreen;

        [Header("时间")]
        [Tooltip("撞击后多久起播（秒）")]
        [SerializeField, Min(0f)] private float firstDelay = 30f;
        [Tooltip("一曲播完后隔多久重播（秒）")]
        [SerializeField, Min(0f)] private float replayGap = 30f;
        [Tooltip("结束时淡出时长（秒）")]
        [SerializeField, Min(0.1f)] private float fadeOutSeconds = 6f;
        [Tooltip("起播淡入时长（秒），0 = 直接到目标音量")]
        [SerializeField, Min(0f)] private float fadeInSeconds = 2f;

        [Header("音量")]
        [Tooltip("BGM 目标音量")]
        [SerializeField, Range(0f, 1f)] private float targetVolume = 0.55f;

        private Stage stage = Stage.WaitingCrash;
        private float timer;
        private bool pausedByUs;

        /// <summary>调试用：当前阶段。</summary>
        public string StageName { get { return stage.ToString(); } }
        /// <summary>调试用：本阶段已累计的秒数。</summary>
        public float Timer { get { return timer; } }

        private void Awake()
        {
            if (music == null) music = GetComponent<AudioSource>();
            if (music != null)
            {
                music.playOnAwake = false;
                music.loop = false;
                music.volume = 0f;
            }
        }

        private void Update()
        {
            // 没挂 BGM 就静默待命：把曲子拖到 AudioSource 的 Clip 上即可启用，逻辑不用改。
            if (music == null || music.clip == null) return;

            // 暂停：计时不走，音乐也停住（恢复后从原位置续播）
            if (Time.timeScale <= 0f)
            {
                if (!pausedByUs && music.isPlaying)
                {
                    music.Pause();
                    pausedByUs = true;
                }
                return;
            }
            if (pausedByUs)
            {
                music.UnPause();
                pausedByUs = false;
            }

            float dt = Time.deltaTime;
            switch (stage)
            {
                case Stage.WaitingCrash:
                    if (IsCrashed())
                    {
                        if (AllDevicesRepaired()) { stage = Stage.Finished; break; }
                        stage = Stage.WaitingFirstPlay;
                        timer = 0f;
                    }
                    break;

                case Stage.WaitingFirstPlay:
                    if (AllDevicesRepaired()) { stage = Stage.Finished; break; }
                    timer += dt;
                    if (timer >= firstDelay) BeginPlay();
                    break;

                case Stage.Playing:
                    if (AllDevicesRepaired()) { StartFadeOut(); break; }
                    // 曲终 → 进入重播等待
                    if (!music.isPlaying) { stage = Stage.WaitingReplay; timer = 0f; break; }
                    // 淡入
                    WriteVolume(fadeInSeconds <= 0f
                        ? targetVolume
                        : Mathf.MoveTowards(internalVolume, targetVolume, targetVolume * dt / fadeInSeconds));
                    break;

                case Stage.WaitingReplay:
                    if (AllDevicesRepaired()) { stage = Stage.Finished; break; }
                    timer += dt;
                    if (timer >= replayGap) BeginPlay();
                    break;

                case Stage.FadingOut:
                    timer += dt;
                    float t = fadeOutSeconds <= 0f ? 1f : Mathf.Clamp01(timer / fadeOutSeconds);
                    WriteVolume(Mathf.Lerp(fadeOutStartVolume, 0f, t));
                    if (t >= 1f)
                    {
                        music.Stop();
                        WriteVolume(0f);
                        stage = Stage.Finished;
                    }
                    break;
            }

            // 滑条可能随时被拖动：每帧按当前倍率重算一次，不用等下一个阶段切换
            if (music != null && music.clip != null) music.volume = internalVolume * MusicBus.Volume;
        }

        private float fadeOutStartVolume;

        /// <summary>导演自己的音量（0~1，不含 BGM 滑条）。实际写入声源时再乘 MusicBus.Volume。</summary>
        private float internalVolume;

        private void WriteVolume(float value)
        {
            internalVolume = Mathf.Clamp01(value);
            if (music != null) music.volume = internalVolume * MusicBus.Volume;
        }

        private void BeginPlay()
        {
            if (music.clip == null) { stage = Stage.Finished; return; }
            timer = 0f;
            stage = Stage.Playing;
            WriteVolume(fadeInSeconds <= 0f ? targetVolume : 0f);
            music.Play();
        }

        private void StartFadeOut()
        {
            // 已经停着就直接收工，不用淡出
            if (!music.isPlaying)
            {
                WriteVolume(0f);
                stage = Stage.Finished;
                return;
            }
            fadeOutStartVolume = internalVolume;
            timer = 0f;
            stage = Stage.FadingOut;
        }

        private bool IsCrashed() => crash != null && crash.IsDamaged;

        private bool AllDevicesRepaired()
        {
            if (repairScreen == null) return false;
            for (int i = 0; i < RepairScreenController.DeviceCount; i++)
                if (repairScreen.GetState(i) != RepairDeviceState.Repaired) return false;
            return true;
        }
    }
}
