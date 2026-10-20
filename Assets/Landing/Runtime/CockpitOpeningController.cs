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

        [Header("落地镜头教学（告诉玩家能左键拉近、右键/ESC 返回）")]
        [Tooltip("座舱相机。留空则自动找")]
        public CockpitCameraController CockpitCamera;
        [Tooltip("拉近看哪块屏。留空则自动挑名字里带 SYSTEMS / COMMUNICATION 的一块")]
        public CockpitScreenTarget TutorialScreen;
        [Tooltip("拉近后停多久再自动回到上级（秒）")]
        [Min(0f)] public float TutorialFocusHold = 1f;
        [Tooltip("回到自由视角后等多久再开终端（秒）")]
        [Min(0f)] public float TutorialAfterReturnDelay = 0.4f;

        [Header("回收 → 组装 教学（教滑页切换）")]
        [Tooltip("打捞屏（留空自动找 ATTITUDE）")]
        public CockpitScreenTarget SalvageScreen;
        [Tooltip("组装屏（留空自动找 RADAR）")]
        public CockpitScreenTarget AssemblyScreen;
        [Tooltip("等材料凑够的最长时间（秒），超时就放弃教学，不卡住玩家")]
        [Min(1f)] public float SalvageWaitTimeout = 600f;
        [Tooltip("切到组装屏后停多久再开始演示滑页（秒）")]
        [Min(0f)] public float AssemblyFocusHold = 0.9f;
        [Tooltip("每翻一页的间隔（秒）")]
        [Min(0.2f)] public float PageInterval = 1.1f;

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
                    StartCoroutine(SalvageAssemblyTutorial());
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
            StartCoroutine(PlayLandingTutorial());
        }

        /// <summary>
        /// 落地教学：镜头自动拉近那块变红的屏（示范左键点击的效果）→ 停 1 秒 →
        /// 自动退回上级视角（示范右键 / ESC）→ 再打开终端把剧情接上。
        /// </summary>
        System.Collections.IEnumerator PlayLandingTutorial()
        {
            var cam = CockpitCamera != null ? CockpitCamera : FindAnyObjectByType<CockpitCameraController>();
            var screen = TutorialScreen != null ? TutorialScreen : PickTutorialScreen(cam);
            if (cam != null && screen != null)
            {
                cam.FocusScreen(screen);
                if (TutorialFocusHold > 0f) yield return new WaitForSecondsRealtime(TutorialFocusHold);
                cam.ReturnToFree();
                if (TutorialAfterReturnDelay > 0f) yield return new WaitForSecondsRealtime(TutorialAfterReturnDelay);
            }
            if (Terminal != null)
            {
                Terminal.Open();
                Terminal.GoToNode("landing_crash_report");
            }
        }

        /// <summary>
        /// 回收教学：镜头先拉近打捞屏 → 等材料凑够任意一件 → 切到组装屏 →
        /// 自动翻一整轮页（就算只有一页也绕一圈）告诉玩家这里可以滑页。
        /// 中途玩家有任何操作就立刻停手，把控制权还给他。
        /// </summary>
        System.Collections.IEnumerator SalvageAssemblyTutorial()
        {
            var cam = CockpitCamera != null ? CockpitCamera : FindAnyObjectByType<CockpitCameraController>();
            var salvage = SalvageScreen != null ? SalvageScreen : FindScreenByName(cam, "ATTITUDE");
            if (cam != null && salvage != null) cam.FocusScreen(salvage);

            float waited = 0f;
            int ready = -1;
            while (waited < SalvageWaitTimeout)
            {
                if (PlayerActed()) yield break;
                ready = Assembly != null ? Assembly.FindCraftableRecipe() : -1;
                if (ready >= 0) break;
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (ready < 0 || Assembly == null) yield break;

            var bench = AssemblyScreen != null ? AssemblyScreen : FindScreenByName(cam, "RADAR");
            if (cam != null && bench != null) cam.FocusScreen(bench);
            if (AssemblyFocusHold > 0f) yield return new WaitForSecondsRealtime(AssemblyFocusHold);
            if (PlayerActed()) yield break;

            var recipes = Assembly.Recipes;
            if (recipes == null || recipes.Length == 0) yield break;

            // 先停到材料够的那一件上
            Assembly.SelectRecipe(ready);
            yield return new WaitForSecondsRealtime(PageInterval);

            // 再绕一整圈：让玩家看清左右能翻页
            for (int step = 0; step < recipes.Length; step++)
            {
                if (PlayerActed()) yield break;
                Assembly.SelectRecipe((Assembly.SelectedRecipeIndex + 1) % recipes.Length);
                yield return new WaitForSecondsRealtime(PageInterval);
            }
        }

        /// <summary>玩家是否自己动了手：任何按键 / 左右键点击。动了就不抢操作。</summary>
        static bool PlayerActed()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) return true;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) return true;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            return pad != null && pad.buttonSouth.wasPressedThisFrame;
        }

        static CockpitScreenTarget FindScreenByName(CockpitCameraController cam, string fragment)
        {
            if (cam == null || cam.Screens == null) return null;
            foreach (var s in cam.Screens)
                if (s != null && s.gameObject.name.ToUpperInvariant().Contains(fragment)) return s;
            return null;
        }

        CockpitScreenTarget PickTutorialScreen(CockpitCameraController cam)
        {
            if (cam == null || cam.Screens == null) return null;
            foreach (var s in cam.Screens)
            {
                if (s == null) continue;
                var n = s.gameObject.name;
                // 那块变红的损伤屏在 COMMUNICATIONS 上（截图里镜框写着 COMMUNICATION）
                if (n.Contains("COMMUNICATION")) return s;
            }
            foreach (var s in cam.Screens)
            {
                if (s == null) continue;
                var n = s.gameObject.name;
                if (n.Contains("SYSTEM")) return s;
            }
            return cam.Screens.Length > 0 ? cam.Screens[0] : null;
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
