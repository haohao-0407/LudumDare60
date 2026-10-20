using UnityEngine;

namespace LudumDare60.Audio
{
    /// <summary>
    /// 长时间循环的环境音"疲劳"处理：播满 holdSeconds 之后，把音量平滑降到 targetVolumeFactor，
    /// 同时（可选）用低通把刺耳的高频一点点收掉，声音变钝而不是单纯变小。
    /// 音源停止后自动复位，下次重新播又从头算。
    ///
    /// 不抢音量控制权：每帧先看 AudioSource 当前音量有没有被别的脚本改过，
    /// 改过就把它当成新的"外部基准"，自己只在基准上乘倍率。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class AmbienceFatigue : MonoBehaviour
    {
        [Header("目标")]
        [Tooltip("留空则取本物体上的 AudioSource")]
        [SerializeField] private AudioSource source;

        [Header("音量衰减")]
        [Tooltip("从开始播放算起，维持原音量的时长（秒）")]
        [SerializeField, Min(0f)] private float holdSeconds = 30f;
        [Tooltip("衰减过程时长（秒）。太短会听出变化，8~15 秒比较自然")]
        [SerializeField, Min(0.01f)] private float fadeSeconds = 8f;
        [Tooltip("稳定后的音量倍率。0.55 = 降到原来的 55%，约 -5dB")]
        [SerializeField, Range(0.05f, 1f)] private float targetVolumeFactor = 0.55f;

        [Header("高频收敛（可选）")]
        [Tooltip("开启后同时把低通截止频率从 startCutoff 收到 endCutoff，减轻刺耳感")]
        [SerializeField] private bool tameHighs = true;
        [Tooltip("起始截止频率。22050 = 44.1kHz 下的上限，等于完全不滤波")]
        [SerializeField, Range(500f, 22050f)] private float startCutoff = 22050f;
        [Tooltip("稳定后的截止频率。5000 左右声音明显变钝；低于 2500 会闷")]
        [SerializeField, Range(500f, 22050f)] private float endCutoff = 5000f;

        private AudioLowPassFilter lowPass;
        private float externalVolume = -1f;   // 别人（或作者）设定的音量基准
        private float lastWritten = -1f;      // 上一帧我自己写回去的值
        private float elapsed;
        private bool wasPlaying;

        private void Awake()
        {
            if (source == null) source = GetComponent<AudioSource>();
            if (tameHighs)
            {
                lowPass = GetComponent<AudioLowPassFilter>();
                if (lowPass == null) lowPass = gameObject.AddComponent<AudioLowPassFilter>();
                lowPass.lowpassResonanceQ = 0.7f;
                lowPass.cutoffFrequency = startCutoff;
            }
        }

        private void OnEnable()
        {
            if (source != null)
            {
                externalVolume = source.volume;
                lastWritten = source.volume;
            }
            elapsed = 0f;
            wasPlaying = false;
        }

        private void OnDisable()
        {
            // 关掉或换场景时还原，避免下次进来还是闷的/小声的
            if (source != null && externalVolume >= 0f) source.volume = externalVolume;
            if (lowPass != null) lowPass.cutoffFrequency = startCutoff;
            elapsed = 0f;
            wasPlaying = false;
        }

        private void Update()
        {
            if (source == null) return;

            if (!source.isPlaying)
            {
                if (wasPlaying) ResetToStart();
                wasPlaying = false;
                return;
            }
            wasPlaying = true;

            // 当前值 ≠ 我上帧写的值 ⇒ 有别的脚本在改音量，把它当作新基准，别跟它抢
            float current = source.volume;
            if (lastWritten < 0f || Mathf.Abs(current - lastWritten) > 1e-4f) externalVolume = current;

            elapsed += Time.unscaledDeltaTime;
            float t = fadeSeconds <= 0f ? 1f : Mathf.Clamp01((elapsed - holdSeconds) / fadeSeconds);
            float eased = t * t * (3f - 2f * t);   // smoothstep：起止都不突兀

            float factor = Mathf.Lerp(1f, targetVolumeFactor, eased);
            float wanted = externalVolume * factor;
            if (Mathf.Abs(current - wanted) > 1e-5f) source.volume = wanted;
            lastWritten = wanted;

            if (lowPass != null)
                lowPass.cutoffFrequency = Mathf.Lerp(startCutoff, endCutoff, eased);
        }

        private void ResetToStart()
        {
            elapsed = 0f;
            if (source != null && externalVolume >= 0f) { source.volume = externalVolume; lastWritten = externalVolume; }
            if (lowPass != null) lowPass.cutoffFrequency = startCutoff;
        }
    }
}
