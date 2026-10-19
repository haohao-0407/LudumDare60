using System.Collections.Generic;
using System.Text;
using LudumDare60.UI;
using UnityEngine;

namespace LudumDare60.Landing
{
    /// <summary>Checks the four repaired systems, then waits for pilot authorization to launch.</summary>
    [DisallowMultipleComponent]
    public sealed class CockpitDepartureController : MonoBehaviour
    {
        public CockpitOpeningController Opening;
        public LaunchSequenceController Launch;
        public AudioClip ReadyClip;
        public AudioClip[] CheckClips = new AudioClip[4];
        [Min(.5f)] public float CheckDuration = 2.5f;
        [Min(.5f)] public float EngineTestDuration = 3.5f;
        [Range(0f, 1f)] public float CheckVolume = .3f;

        public bool PreflightComplete => checkedMask == 15;
        public bool IsChecking => checkingDevice >= 0;

        static readonly string[] DeviceNames = { "POWER", "NAVIGATION", "PROPULSION", "LIFE SUPPORT" };
        static readonly string[] CheckCommands = { "pioneer60.check_power", "pioneer60.check_navigation", "pioneer60.check_propulsion", "pioneer60.check_life_support" };
        static readonly string[] NextChecks = { "departure_navigation", "departure_propulsion", "departure_life_support", "departure_launch_ready" };
        readonly List<PageState> pages = new List<PageState>();
        readonly StringBuilder checkText = new StringBuilder();
        RepairScreenController repairScreen;
        AudioSource checkAudio;
        bool audioPaused;
        bool announced;
        bool invalidated;
        bool inputsCaptured;
        bool recoverInterruptedFlow;
        bool repairsWereAvailable;
        bool salvageWasEnabled;
        int checkedMask;
        int checkingDevice = -1;
        float checkElapsed;
        LaunchSequenceController.LaunchPhase lastPhase;

        struct PageState
        {
            public CockpitStandbyScreen Page;
            public string Title, Mode, Power, Status, Footer;
            public bool Standby;
        }

        bool GroundReady => Opening && Opening.HasCrashed && Opening.Landing && Opening.Landing.HasLanded &&
                            repairScreen && repairScreen.AllRepaired;

        void OnEnable()
        {
            if (Opening && Opening.Assembly) repairScreen = Opening.Assembly.RepairScreen;
            if (repairScreen) repairScreen.StateChanged += HandleRepairChanged;
            if (Opening && Opening.Terminal) Opening.Terminal.EventTriggered += HandleCommand;
            if (!Launch) return;
            Launch.OnIgnition.AddListener(HandleIgnition);
            Launch.OnLiftoff.AddListener(HandleLiftoff);
            Launch.OnOrbitReached.AddListener(HandleOrbit);
            Launch.OnLaunchCancelled.AddListener(HandleCancelled);
        }

        void Start()
        {
            if (!Opening || !Opening.Terminal || !Opening.Assembly || !Opening.Salvage || !repairScreen || !Launch)
                Debug.LogError("[Departure] Assign the SHIP opening, repair and launch references.", this);
        }

        void OnDisable()
        {
            // While disabled we cannot observe repairs changing. Never reuse an old verification.
            recoverInterruptedFlow = (!Launch || !Launch.HasReachedOrbit) &&
                                     (announced || checkedMask != 0 || IsChecking || (Launch && Launch.IsLaunching));
            // Cancellation restores the ground snapshot before this controller releases its locks.
            if (Launch && Launch.IsLaunching) Launch.CancelLaunch();
            if (repairScreen) repairScreen.StateChanged -= HandleRepairChanged;
            if (Opening && Opening.Terminal) Opening.Terminal.EventTriggered -= HandleCommand;
            if (Launch)
            {
                Launch.OnIgnition.RemoveListener(HandleIgnition);
                Launch.OnLiftoff.RemoveListener(HandleLiftoff);
                Launch.OnOrbitReached.RemoveListener(HandleOrbit);
                Launch.OnLaunchCancelled.RemoveListener(HandleCancelled);
            }
            StopCheck();
            if (recoverInterruptedFlow) ResetChecks();
            if (!Launch || !Launch.HasReachedOrbit) RestoreControlsAndPages();
        }

        void OnDestroy()
        {
            if (checkAudio) Destroy(checkAudio.gameObject);
        }

        void HandleRepairChanged(int device, RepairDeviceState state)
        {
            // Defer dialogue and UI work until the assembly system has published its whole snapshot.
            if (state != RepairDeviceState.Repaired && (announced || checkedMask != 0 || IsChecking))
                invalidated = true;
        }

        void Update()
        {
            if (!Opening || !Opening.Terminal || !repairScreen || !Launch) return;
            PauseAudio();
            if (recoverInterruptedFlow)
            {
                recoverInterruptedFlow = false;
                SetFlag("pioneer60.launch_started", false);
                SetFlag("pioneer60.departure_ready", GroundReady);
                announced = GroundReady;
                Opening.Terminal.GoToNode(GroundReady ? "departure_ready" : "departure_blocked");
            }
            if (invalidated)
            {
                invalidated = false;
                bool wasLaunching = Launch.IsLaunching;
                ResetChecks();
                announced = false;
                if (wasLaunching) Launch.CancelLaunch();
                else
                {
                    RestoreControlsAndPages();
                    Opening.Terminal.GoToNode("departure_blocked");
                }
            }

            bool ready = GroundReady && !Launch.IsLaunching && !Launch.HasReachedOrbit;
            SetFlag("pioneer60.departure_ready", ready);
            if (ready && !announced)
            {
                announced = true;
                PlayCheckSound(ReadyClip);
                Opening.Terminal.GoToNode("departure_ready");
            }
            if (IsChecking)
            {
                if (!ready) { BlockDeparture(); return; }
                checkElapsed += Time.deltaTime;
                float duration = checkingDevice == 2 ? EngineTestDuration : CheckDuration;
                float progress = Mathf.Clamp01(checkElapsed / Mathf.Max(.5f, duration));
                ShowCheckPage(progress);
                if (progress >= 1f)
                {
                    int completed = checkingDevice;
                    checkedMask |= 1 << completed;
                    checkingDevice = -1;
                    if (checkAudio) checkAudio.Stop();
                    SetFlag("pioneer60.preflight_complete", PreflightComplete);
                    ShowCheckPage(1f);
                    Opening.Terminal.GoToNode(NextChecks[completed]);
                }
            }
            if (!Launch.IsLaunching) return;
            if (Launch.Phase != lastPhase)
            {
                lastPhase = Launch.Phase;
                if (lastPhase == LaunchSequenceController.LaunchPhase.AtmosphericExit)
                    Opening.Terminal.GoToNode("launch_exit");
            }
            ShowFlightPage();
        }

        void HandleCommand(string command)
        {
            if (!Opening || !Launch || !repairScreen) return;
            for (int i = 0; i < CheckCommands.Length; i++)
            {
                if (command != CheckCommands[i]) continue;
                BeginCheck(i);
                return;
            }
            if (command == "pioneer60.launch_requested") RequestLaunch();
        }

        void BeginCheck(int device)
        {
            if (IsChecking || Launch.IsLaunching || Launch.HasReachedOrbit) return;
            int preceding = (1 << device) - 1;
            if (!GroundReady || (checkedMask & preceding) != preceding)
            {
                BlockDeparture();
                return;
            }
            // A repeated earlier check invalidates the downstream checklist, never skips a test.
            checkedMask &= preceding;
            SetFlag("pioneer60.preflight_complete", false);
            checkingDevice = device;
            checkElapsed = 0f;
            CapturePage(Opening.RepairInfoStandby);
            ShowCheckPage(0f);
            PlayCheckSound(CheckClips != null && device < CheckClips.Length ? CheckClips[device] : null);
        }

        void RequestLaunch()
        {
            if (Launch.IsLaunching || Launch.HasReachedOrbit) return;
            if (!GroundReady || !PreflightComplete || IsChecking) { BlockDeparture(); return; }
            CapturePage(Opening.AssemblyStandby);
            CapturePage(Opening.SalvageStandby);
            CapturePage(Opening.RepairInfoStandby);
            repairsWereAvailable = Opening.Assembly.RepairsAvailable;
            salvageWasEnabled = Opening.Salvage.FeatureEnabled;
            inputsCaptured = true;
            Opening.Assembly.SetRepairsAvailable(false);
            Opening.Salvage.SetFeatureEnabled(false);
            SetPage(Opening.AssemblyStandby, "FLIGHT CONTROL", "ASCENT", "WORKBENCH SECURED", "ALL SYSTEMS VERIFIED\nPILOT AUTHORIZATION RECEIVED", "AUTOMATIC FLIGHT CONTROL");
            SetPage(Opening.SalvageStandby, "SALVAGE CONTROL", "OFFLINE", "SURFACE OPERATIONS CLOSED", "REMOTE LINK SECURED\nVEHICLE READY FOR DEPARTURE", "FLIGHT MODE");
            if (!Launch.BeginLaunch())
            {
                ResetChecks();
                RestoreControlsAndPages();
                Opening.Terminal.GoToNode("launch_failed");
                return;
            }
            lastPhase = Launch.Phase;
            SetFlag("pioneer60.launch_started", true);
            SetFlag("pioneer60.departure_ready", false);
            ShowFlightPage();
        }

        void HandleIgnition() => Opening.Terminal.GoToNode("launch_ignition");
        void HandleLiftoff() => Opening.Terminal.GoToNode("launch_ascent");

        void HandleOrbit()
        {
            SetPage(Opening.RepairInfoStandby, "FLIGHT STATUS", "ORBIT", "ALL SYSTEMS NOMINAL", "POWER / NAVIGATION: NOMINAL\nPROPULSION / LIFE SUPPORT: NOMINAL\nRETURN TRAJECTORY LOCKED", "PIONEER-60 / HOMEWARD BOUND");
            SetPage(Opening.AssemblyStandby, "FLIGHT CONTROL", "CRUISE", "ASCENT COMPLETE", "MARS ORBIT ESTABLISHED\nAUTOMATIC NAVIGATION ENGAGED", "PIONEER-60 / FLIGHT MODE");
            Opening.Terminal.GoToNode("launch_orbit");
        }

        void HandleCancelled()
        {
            ResetChecks();
            RestoreControlsAndPages();
            SetFlag("pioneer60.launch_started", false);
            SetFlag("pioneer60.departure_ready", GroundReady);
            if (isActiveAndEnabled) Opening.Terminal.GoToNode(GroundReady ? "launch_failed" : "departure_blocked");
        }

        void BlockDeparture()
        {
            ResetChecks();
            RestoreControlsAndPages();
            Opening.Terminal.GoToNode("departure_blocked");
        }

        void ResetChecks()
        {
            StopCheck();
            checkedMask = 0;
            SetFlag("pioneer60.preflight_complete", false);
        }

        void StopCheck()
        {
            checkingDevice = -1;
            if (checkAudio) checkAudio.Stop();
            audioPaused = false;
        }

        void ShowCheckPage(float progress)
        {
            checkText.Clear();
            for (int i = 0; i < 4; i++)
            {
                if (i != 0) checkText.Append('\n');
                checkText.Append(DeviceNames[i]).Append("  /  ");
                if ((checkedMask & (1 << i)) != 0) checkText.Append("PASS");
                else if (i == checkingDevice) checkText.Append(Mathf.FloorToInt(progress * 100f)).Append('%');
                else checkText.Append("PENDING");
            }
            SetPage(Opening.RepairInfoStandby, "PRE-FLIGHT CHECK", IsChecking ? "TESTING" : PreflightComplete ? "READY" : "CHECK", "ONBOARD DIAGNOSTICS", checkText.ToString(), PreflightComplete ? "AWAITING PILOT LAUNCH AUTHORIZATION" : "COMPONENT VERIFICATION");
        }

        void ShowFlightPage()
        {
            bool countdown = Launch.Phase == LaunchSequenceController.LaunchPhase.Countdown;
            string mode = countdown ? Mathf.CeilToInt(Launch.CountdownRemaining).ToString("00") : "ASCENT";
            string phase = Launch.Phase == LaunchSequenceController.LaunchPhase.AtmosphericExit ? "LEAVING MARS ATMOSPHERE" : Launch.Phase.ToString().ToUpperInvariant();
            SetPage(Opening.RepairInfoStandby, "FLIGHT STATUS", mode, phase, "ALL FOUR SYSTEMS NOMINAL\nFLIGHT SEQUENCE " + Mathf.FloorToInt(Launch.Progress * 100f) + "%", "AUTOMATIC FLIGHT CONTROL");
        }

        void CapturePage(CockpitStandbyScreen page)
        {
            if (!page || pages.Exists(state => state.Page == page)) return;
            pages.Add(new PageState { Page = page, Title = page.SystemTitle, Mode = page.ModeText, Power = page.PowerStateText,
                Status = page.StatusText, Footer = page.FooterText, Standby = page.IsStandby });
        }

        static void SetPage(CockpitStandbyScreen page, string title, string mode, string power, string status, string footer)
        {
            if (!page) return;
            page.SystemTitle = title;
            page.ModeText = mode;
            page.PowerStateText = power;
            page.StatusText = status;
            page.FooterText = footer;
            if (!page.IsStandby) page.SetStandby(true);
        }

        void RestoreControlsAndPages()
        {
            if (inputsCaptured && Opening)
            {
                if (Opening.Assembly) Opening.Assembly.SetRepairsAvailable(repairsWereAvailable);
                if (Opening.Salvage) Opening.Salvage.SetFeatureEnabled(salvageWasEnabled);
            }
            inputsCaptured = false;
            foreach (PageState state in pages)
            {
                if (!state.Page) continue;
                SetPage(state.Page, state.Title, state.Mode, state.Power, state.Status, state.Footer);
                state.Page.SetStandby(state.Standby);
            }
            pages.Clear();
        }

        void SetFlag(string name, bool value)
        {
            if (Opening && Opening.Terminal && Opening.Terminal.GetFlag(name) != value)
                Opening.Terminal.SetFlag(name, value);
        }

        void PlayCheckSound(AudioClip clip)
        {
            if (!clip) return;
            if (!checkAudio)
            {
                var sound = new GameObject("Pre-flight Check Audio");
                sound.transform.SetParent(transform, false);
                checkAudio = sound.AddComponent<AudioSource>();
                checkAudio.playOnAwake = false;
                checkAudio.spatialBlend = 0f;
            }
            checkAudio.Stop();
            audioPaused = false;
            checkAudio.volume = CheckVolume;
            checkAudio.clip = clip;
            checkAudio.Play();
            PauseAudio();
        }

        void PauseAudio()
        {
            if (!checkAudio) return;
            if (Time.timeScale <= 0f && !audioPaused && checkAudio.isPlaying)
            {
                checkAudio.Pause();
                audioPaused = true;
            }
            else if (Time.timeScale > 0f && audioPaused)
            {
                checkAudio.UnPause();
                audioPaused = false;
            }
        }
    }
}
