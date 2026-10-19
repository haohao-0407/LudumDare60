using System;
using TMPro;
using Pioneer60.Localization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LudumDare60.UI
{
    /// <summary>单台装置的修复状态。数组顺序即屏内阅读顺序。</summary>
    public enum RepairDeviceState
    {
        Unrepaired = 0,
        Repairing = 1,
        Repaired = 2
    }

    [Serializable]
    public sealed class RepairDeviceWidgets
    {
        [Tooltip("屏内该装置的线框物体（挂 Xray 组件的那个）。")]
        public Xray Wireframe;

        [Tooltip("卡片四边框线，顺序固定：上 / 下 / 左 / 右。")]
        public Image[] Frame = new Image[4];

        [Tooltip("状态字：REPAIRED / REPAIRING / UNREPAIRED。")]
        public TMP_Text StatusLabel;

        [Tooltip("百分比字。")]
        public TMP_Text PercentLabel;

        [Tooltip("进度条填充条（左端固定，向右伸展）。")]
        public Image FillBar;

        [Range(1f, 4000f)]
        [Tooltip("进度条满格宽度（像素）。")]
        public float BarFullWidth = 332f;
    }

    /// <summary>
    /// 飞船修复进度屏的数据驱动层：一分真相是 <see cref="RepairDeviceState"/> + 0~1 进度，
    /// 内部同步 3D 线框颜色、卡片边框、状态字、百分比与进度条。
    /// 屏的视觉（RT / 相机 / UI）由 REPAIR_SCREEN_Rig 承载，本脚本只管状态。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class RepairScreenController : MonoBehaviour
    {
        public const int DeviceCount = 4;

        [Header("四台装置（顺序 = 屏内阅读顺序：左上 → 右上 → 左下 → 右下）")]
        [Tooltip("能源装置 / 导航装置 / 引擎 / 生命装置。")]
        [SerializeField] private RepairDeviceWidgets[] devices = new RepairDeviceWidgets[DeviceCount];

        [Header("三态配色")]
        [SerializeField] private Color unrepairedColor = new Color(1f, 0.29f, 0.29f, 1f);
        [SerializeField] private Color repairingColor = new Color(0.27f, 0.72f, 1f, 1f);
        [SerializeField] private Color repairedColor = new Color(0.24f, 1f, 0.54f, 1f);

        [Range(0f, 1f)]
        [Tooltip("背面线亮度系数（乘在三态色上）。")]
        [SerializeField] private float hiddenColorFactor = 0.35f;

        [Header("状态字")]
        [SerializeField] private string unrepairedLabel = "UNREPAIRED";
        [SerializeField] private string repairingLabel = "REPAIRING";
        [SerializeField] private string repairedLabel = "REPAIRED";

        [Header("表现")]
        [Range(0f, 5f)]
        [Tooltip("进度条追赶速度（每秒进度）。0 = 立刻到位。")]
        [SerializeField] private float fillSpeedPerSecond = 0.85f;

        [Range(0f, 2f)]
        [Tooltip("状态切换时线框闪白时长（秒）。0 = 关闭。")]
        [SerializeField] private float flashDuration = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("修复中卡片的呼吸亮度幅度。0 = 关闭。")]
        [SerializeField] private float repairingPulse = 0.18f;

        [Range(0.1f, 6f)]
        [SerializeField] private float repairingPulseSpeed = 1.6f;

        [Header("运行")]
        [SerializeField]
        [Tooltip("进入 Play 时把所有装置重置为 UNREPAIRED / 0%。关掉则沿用在编辑器里预览的状态。")]
        private bool resetOnPlay = true;

        [Header("事件")]
        [SerializeField] private UnityEvent<int, RepairDeviceState> stateChanged = new UnityEvent<int, RepairDeviceState>();

        // 运行时真相
        [SerializeField, HideInInspector] private RepairDeviceState[] states = new RepairDeviceState[DeviceCount];
        [SerializeField, HideInInspector] private float[] targetProgress = new float[DeviceCount];
        private readonly float[] shownProgress = new float[DeviceCount];
        private readonly float[] flashTimers = new float[DeviceCount];

        /// <summary>(deviceIndex, newState) —— 状态真正变化时触发。</summary>
        public event Action<int, RepairDeviceState> StateChanged;

        /// <summary>第一台还没修好的装置下标；全修好了返回 -1。</summary>
        public int CurrentIndex
        {
            get
            {
                for (int i = 0; i < DeviceCount; i++)
                    if (states[i] != RepairDeviceState.Repaired) return i;
                return -1;
            }
        }

        public bool AllRepaired => CurrentIndex < 0;

        // ========================
        // 生命周期
        // ========================

        private void Awake()
        {
            EnsureArrays();
            if (Application.isPlaying && resetOnPlay) ResetAll();
        }

        private void OnEnable()
        {
            EnsureArrays();
            for (int i = 0; i < DeviceCount; i++) shownProgress[i] = targetProgress[i];
            RefreshAll();
        }

        private void OnValidate()
        {
            EnsureArrays();
            if (!Application.isPlaying)
            {
                for (int i = 0; i < DeviceCount; i++) shownProgress[i] = targetProgress[i];
                RefreshAll();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            float dt = Time.unscaledDeltaTime;
            float pulse = repairingPulse > 0f
                ? repairingPulse * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * repairingPulseSpeed * Mathf.PI * 2f))
                : 0f;

            for (int i = 0; i < DeviceCount; i++)
            {
                bool dirty = false;

                if (!Mathf.Approximately(shownProgress[i], targetProgress[i]))
                {
                    shownProgress[i] = fillSpeedPerSecond <= 0f
                        ? targetProgress[i]
                        : Mathf.MoveTowards(shownProgress[i], targetProgress[i], fillSpeedPerSecond * dt);
                    dirty = true;
                }

                if (flashTimers[i] > 0f)
                {
                    flashTimers[i] = Mathf.Max(0f, flashTimers[i] - dt);
                    dirty = true;
                }

                if (dirty || states[i] == RepairDeviceState.Repairing) ApplyVisuals(i, PulseFor(i, pulse));
            }
        }

        private float PulseFor(int index, float pulse)
        {
            if (states[index] != RepairDeviceState.Repairing) return 0f;
            return pulse;
        }

        // ========================
        // 对外 API
        // ========================

        public RepairDeviceState GetState(int index) => states[Mathf.Clamp(index, 0, DeviceCount - 1)];

        public float GetProgress(int index) => targetProgress[Mathf.Clamp(index, 0, DeviceCount - 1)];

        /// <summary>Each device can be repaired independently while it is not yet repaired.</summary>
        public bool CanBeginRepair(int index)
        {
            if (index < 0 || index >= DeviceCount) return false;
            if (states[index] == RepairDeviceState.Repaired) return false;
            return true;
        }

        /// <summary>直接把某台设为指定状态。progress 省略时按状态取默认值（未修 0 / 完成 1）。</summary>
        public bool SetState(int index, RepairDeviceState state, float progress = -1f)
        {
            if (index < 0 || index >= DeviceCount)
            {
                Debug.LogWarning($"[RepairScreen] 装置下标越界：{index}", this);
                return false;
            }

            if (state != RepairDeviceState.Unrepaired && !CanBeginRepair(index))
            {
                Debug.LogWarning($"[RepairScreen] Device {index + 1} is already repaired.", this);
                return false;
            }

            if (progress < 0f)
                progress = state == RepairDeviceState.Repaired ? 1f : state == RepairDeviceState.Unrepaired ? 0f : targetProgress[index];

            if (state == RepairDeviceState.Unrepaired) progress = 0f;
            if (state == RepairDeviceState.Repaired) progress = 1f;

            targetProgress[index] = Mathf.Clamp01(progress);

            bool changed = states[index] != state;
            states[index] = state;

            if (changed)
            {
                // 编辑器里 Update 不跑，闪光不会自行衰减，会卡在偏白；预览时直接跳过。
                if (Application.isPlaying) flashTimers[index] = flashDuration;
                stateChanged?.Invoke(index, state);
                StateChanged?.Invoke(index, state);
            }

            ApplyVisuals(index, 0f);
            return true;
        }

        /// <summary>只推进进度（状态保持）。用于修复过程持续调用。</summary>
        public void SetProgress(int index, float progress)
        {
            if (index < 0 || index >= DeviceCount) return;
            if (states[index] == RepairDeviceState.Repaired) return;

            if (states[index] != RepairDeviceState.Repairing)
            {
                Debug.LogWarning($"[RepairScreen] 装置 {index + 1} 不在修复中，进度被忽略。先调 BeginRepair()。", this);
                return;
            }

            targetProgress[index] = Mathf.Clamp01(progress);
        }

        public bool BeginRepair(int index)
        {
            if (!CanBeginRepair(index))
            {
                Debug.LogWarning($"[RepairScreen] 装置 {index + 1} 现在不能开始修复。", this);
                return false;
            }

            return SetState(index, RepairDeviceState.Repairing, Mathf.Max(targetProgress[index], 0f));
        }

        public bool CompleteRepair(int index)
        {
            return SetState(index, RepairDeviceState.Repaired, 1f);
        }

        /// <summary>Publishes an intact starting ship without running individual repair commands or completion flashes.</summary>
        public void InitializeOperationalDevices()
        {
            SetAllDeviceStates(RepairDeviceState.Repaired, 1f);
        }

        public void ResetAll()
        {
            SetAllDeviceStates(RepairDeviceState.Unrepaired, 0f);
        }

        private void SetAllDeviceStates(RepairDeviceState state, float progress)
        {
            EnsureArrays();
            int changedDevices = 0;
            for (int i = 0; i < DeviceCount; i++)
            {
                if (states[i] != state) changedDevices |= 1 << i;
                states[i] = state;
                targetProgress[i] = progress;
                shownProgress[i] = progress;
                flashTimers[i] = 0f;
            }

            RefreshAll();
            // Commit every device before publication, so a listener always reads a complete snapshot.
            for (int i = 0; i < DeviceCount; i++)
            {
                if ((changedDevices & (1 << i)) == 0) continue;
                stateChanged?.Invoke(i, state);
                StateChanged?.Invoke(i, state);
            }
        }

        // ========================
        // 视觉同步
        // ========================

        public void RefreshAll()
        {
            EnsureArrays();
            for (int i = 0; i < DeviceCount; i++) ApplyVisuals(i, 0f);
        }

        private Color ColorFor(RepairDeviceState state) => state switch
        {
            RepairDeviceState.Repaired => repairedColor,
            RepairDeviceState.Repairing => repairingColor,
            _ => unrepairedColor
        };

        private string LabelFor(RepairDeviceState state) => state switch
        {
            RepairDeviceState.Repaired => repairedLabel,
            RepairDeviceState.Repairing => repairingLabel,
            _ => unrepairedLabel
        };

        private void ApplyVisuals(int index, float pulse)
        {
            if (devices == null || index < 0 || index >= devices.Length) return;

            // 编辑器预览没有 Update 插值，直接对齐目标值，避免百分比/进度条卡在 0。
            if (!Application.isPlaying) shownProgress[index] = targetProgress[index];

            var w = devices[index];
            if (w == null) return;

            Color baseColor = ColorFor(states[index]);
            float flash = flashDuration > 0f ? flashTimers[index] / flashDuration : 0f;
            Color tint = new Color(
                baseColor.r * (1f + pulse),
                baseColor.g * (1f + pulse),
                baseColor.b * (1f + pulse),
                baseColor.a);

            if (flash > 0f) tint = Color.Lerp(tint, Color.white, Mathf.Clamp01(flash) * 0.7f);

            if (w.Frame != null)
            {
                for (int f = 0; f < w.Frame.Length; f++)
                    if (w.Frame[f] != null) w.Frame[f].color = tint;
            }

            if (w.StatusLabel != null)
            {
                w.StatusLabel.text = Pioneer60.Localization.Loc.Say(LabelFor(states[index]));
                w.StatusLabel.color = tint;
            }

            if (w.PercentLabel != null)
            {
                w.PercentLabel.text = Mathf.RoundToInt(shownProgress[index] * 100f) + "%";
                w.PercentLabel.color = tint;
            }

            if (w.FillBar != null)
            {
                var barRect = w.FillBar.rectTransform;
                float full = Mathf.Max(1f, w.BarFullWidth);
                barRect.sizeDelta = new Vector2(full * Mathf.Clamp01(shownProgress[index]), barRect.sizeDelta.y);
                w.FillBar.color = tint;
            }

            if (w.Wireframe != null)
            {
                Color hidden = new Color(tint.r * hiddenColorFactor, tint.g * hiddenColorFactor, tint.b * hiddenColorFactor, tint.a);
                w.Wireframe.SetWireframeColors(tint, hidden);
            }
        }

        private void EnsureArrays()
        {
            if (devices == null || devices.Length != DeviceCount)
                Array.Resize(ref devices, DeviceCount);
            if (states == null || states.Length != DeviceCount) states = new RepairDeviceState[DeviceCount];
            if (targetProgress == null || targetProgress.Length != DeviceCount) targetProgress = new float[DeviceCount];
        }

        // ========================
        // 编辑器便捷：按名字自动绑定
        // ========================

        [ContextMenu("预览：全部未修复")]
        private void PreviewNone() => PlayDemo(0, 0f);

        [ContextMenu("预览：2 台修好 + 第 3 台 62%")]
        private void PreviewDemo() => PlayDemo(2, 0.62f);

        [ContextMenu("预览：全部修好")]
        private void PreviewAll() => PlayDemo(DeviceCount, 1f);

        /// <summary>预览用：前 repairedCount 台设为已完成，第 repairedCount 台设为修复中（进度 progress）。</summary>
        private void PlayDemo(int repairedCount, float progress)
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[RepairScreen] 预览只在编辑器非运行状态可用。", this);
                return;
            }

            EnsureArrays();
            for (int i = 0; i < DeviceCount; i++)
            {
                states[i] = RepairDeviceState.Unrepaired;
                targetProgress[i] = 0f;
                shownProgress[i] = 0f;
                flashTimers[i] = 0f;
            }

            for (int i = 0; i < Mathf.Min(repairedCount, DeviceCount); i++)
            {
                states[i] = RepairDeviceState.Repaired;
                targetProgress[i] = 1f;
                shownProgress[i] = 1f;
            }

            if (repairedCount >= 0 && repairedCount < DeviceCount && progress > 0f)
            {
                states[repairedCount] = RepairDeviceState.Repairing;
                targetProgress[repairedCount] = Mathf.Clamp01(progress);
                shownProgress[repairedCount] = targetProgress[repairedCount];
            }

            RefreshAll();
        }

        [ContextMenu("按名字自动绑定引用")]
        private void AutoBindByName()
        {
            EnsureArrays();
            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < DeviceCount; i++)
            {
                var slot = devices[i] ?? (devices[i] = new RepairDeviceWidgets());

                foreach (var t in all)
                {
                    if (t.name == "Card" + i)
                    {
                        Transform card = t;
                        if (slot.Frame == null || slot.Frame.Length != 4) slot.Frame = new Image[4];
                        slot.Frame[0] = FindDeep<Image>(card, "t");
                        slot.Frame[1] = FindDeep<Image>(card, "b");
                        slot.Frame[2] = FindDeep<Image>(card, "l");
                        slot.Frame[3] = FindDeep<Image>(card, "r");
                        slot.StatusLabel = FindDeep<TMP_Text>(card, "status");
                        slot.PercentLabel = FindDeep<TMP_Text>(card, "pct");
                        slot.FillBar = FindDeep<Image>(card, "fill");

                        var track = FindDeep<Image>(card, "barBg");
                        if (track != null) slot.BarFullWidth = track.rectTransform.sizeDelta.x;
                    }
                }

                if (slot.Wireframe == null)
                {
                    foreach (var x in GetComponentsInChildren<Xray>(true))
                        if (x.transform.GetSiblingIndex() == i) { slot.Wireframe = x; break; }
                }
            }

            RefreshAll();
            Debug.Log("[RepairScreen] 自动绑定完成。", this);
        }

        private static T FindDeep<T>(Transform root, string name) where T : Component
        {
            foreach (var c in root.GetComponentsInChildren<T>(true))
                if (c.name == name) return c;
            return null;
        }
    }
}
