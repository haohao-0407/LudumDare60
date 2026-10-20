using LudumDare60.Salvage;
using LudumDare60.ShipAssembly;
using LudumDare60.TerminalDialogue;
using UnityEngine;

namespace LudumDare60.Landing
{
    /// <summary>Story commands start flight; physical touchdown owns the crash and repair state.</summary>
    [DefaultExecutionOrder(-12000)]
    [DisallowMultipleComponent]
    public sealed class CockpitOpeningController : MonoBehaviour
    {
        public LandingSequenceController Landing;
        public ShipAssemblyController Assembly;
        public SalvageController Salvage;
        public TerminalDialogueController Terminal;
        public HardLandingDamageEffects DamageEffects;
        public HardLandingRepairBridge RepairBridge;
        public CockpitStandbyScreen AssemblyStandby;
        public CockpitStandbyScreen RepairInfoStandby;
        public CockpitStandbyScreen SalvageStandby;

        [Tooltip("选完「开始着陆」后，过多久把终端窗口关掉（秒）。让玩家看清窗口是可以开关的")]
        [Min(0f)] public float CloseTerminalDelay = 0.5f;

        public bool LandingStarted { get; private set; }
        public bool HasCrashed { get; private set; }
        public bool SalvageEnabled { get; private set; }
        bool initialized;
        bool crashReported;

        void Awake()
        {
            if (Landing) Landing.AutoStart = false;
            if (Assembly) Assembly.SetRepairsAvailable(false);
            if (Salvage) Salvage.SetFeatureEnabled(false);
            SetRepairStandby(true);
            if (SalvageStandby) SalvageStandby.SetStandby(true);
        }

        void OnEnable()
        {
            if (Terminal) Terminal.EventTriggered += HandleCommand;
            if (!Landing) return;
            // Subscribe before the damage presentation: its touchdown cue must see the damaged snapshot.
            Landing.OnTouchdown.AddListener(HandleTouchdown);
            Landing.OnLanded.AddListener(HandleLanded);
        }

        void Start()
        {
            // The repair screen clears editor preview data in Awake; initialize after all scene Awakes.
            initialized = Landing && Assembly && Salvage && Terminal && Assembly.InitializeOperationalDevices();
            if (!initialized)
            {
                Debug.LogError("[Cockpit Opening] Opening references or repair state are not configured.", this);
                return;
            }
            if (RepairBridge) RepairBridge.RefreshBindings();
            if (DamageEffects) DamageEffects.StartOperationalAmbience();
        }

        void OnDisable()
        {
            if (Terminal) Terminal.EventTriggered -= HandleCommand;
            if (!Landing) return;
            Landing.OnTouchdown.RemoveListener(HandleTouchdown);
            Landing.OnLanded.RemoveListener(HandleLanded);
        }

        void Update()
        {
            if (!initialized || !LandingStarted || HasCrashed || !Landing) return;
            if (Landing.Phase == LandingSequenceController.LandingPhase.Failed ||
                Landing.Phase == LandingSequenceController.LandingPhase.Cancelled)
                ReportLandingFailure();
        }

        void HandleCommand(string command)
        {
            if (!initialized) return;
            switch (command)
            {
                case "pioneer60.begin_landing":
                    if (LandingStarted || HasCrashed) return;
                    if (!Landing.BeginLanding())
                    {
                        ReportLandingFailure();
                        return;
                    }
                    LandingStarted = true;
                    Terminal.SetFlag("pioneer60.landing_started", true);
                    // 关掉终端窗口：让玩家知道这块屏可以收起来，降落过程看舷窗
                    StartCoroutine(CloseTerminalAfterDelay());
                    break;
                case "pioneer60.begin_repairs":
                    if (HasCrashed) SetRepairStandby(false);
                    break;
                case "pioneer60.enable_salvage":
                    if (!HasCrashed || SalvageEnabled) return;
                    SalvageEnabled = true;
                    Salvage.SetFeatureEnabled(true);
                    if (SalvageStandby) SalvageStandby.SetStandby(false);
                    Terminal.SetFlag("pioneer60.salvage_enabled", true);
                    // The existing screen starts its attempt when dialogue/menu pause permits input.
                    break;
            }
        }

        void HandleTouchdown()
        {
            if (!initialized || HasCrashed) return;
            if (!Assembly.ApplyCrashDamage())
            {
                Debug.LogError("[Cockpit Opening] Could not apply the touchdown damage snapshot.", this);
                return;
            }
            HasCrashed = true;
            Assembly.SetRepairsAvailable(true);
            SetRepairStandby(false);
            if (RepairBridge) RepairBridge.RefreshBindings();
            Terminal.SetFlag("pioneer60.crashed", true);
        }

        void HandleLanded()
        {
            if (!HasCrashed || crashReported) return;
            crashReported = true;
            // 落地后剧情继续：自动把终端窗口重新打开
            if (Terminal != null)
            {
                Terminal.Open();
                Terminal.GoToNode("landing_crash_report");
            }
        }

        /// <summary>延迟一点再关，避免玩家刚点完就看到窗口凭空消失。</summary>
        System.Collections.IEnumerator CloseTerminalAfterDelay()
        {
            if (CloseTerminalDelay > 0f) yield return new WaitForSecondsRealtime(CloseTerminalDelay);
            if (Terminal != null && Terminal.Session != null && Terminal.Session.IsOpen) Terminal.Close();
        }

        void ReportLandingFailure()
        {
            LandingStarted = false;
            Terminal.SetFlag("pioneer60.landing_started", false);
            Terminal.GoToNode("landing_retry");
        }

        void SetRepairStandby(bool standby)
        {
            if (AssemblyStandby) AssemblyStandby.SetStandby(standby);
            if (RepairInfoStandby) RepairInfoStandby.SetStandby(standby);
        }
    }
}
