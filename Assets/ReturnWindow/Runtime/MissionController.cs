using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LudumDare60.ReturnWindow
{
    /// <summary>Persistent owner of the single-run clock. A scene duplicate never resets a run.</summary>
    [DefaultExecutionOrder(-9000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MissionPauseCoordinator))]
    public sealed class MissionController : MonoBehaviour
    {
        [SerializeField, Min(1)] private float durationSeconds = 1200;
        [SerializeField, Range(1, 99)] private int missionDays = MissionSchedule.DefaultDays;
        [SerializeField, Tooltip("Earth-standard date, yyyy-MM-dd")] private string startDate = MissionSchedule.DefaultStart;
        [SerializeField] private float[] warningFractions = { 0.5f, 0.25f, 0.1f };
        [SerializeField] private string nextOpportunityStart = MissionSchedule.DefaultNextStart;
        [SerializeField] private string nextOpportunityEnd = MissionSchedule.DefaultNextEnd;
        private MissionClock clock;
        private MissionPauseCoordinator pauseCoordinator;
        private bool readingPaused, menuPaused, sceneLoading, applicationPaused;
        private bool hasFocus = true;
        private bool repairComplete;
        private int pendingSceneLoads;

        public static MissionController Instance { get; private set; }
        public MissionClock Clock => clock;
        public MissionState State => clock?.State ?? MissionState.Ready;
        public bool RepairComplete => repairComplete;
        public bool IsGameplayPaused => clock == null || clock.IsPaused || State != MissionState.Running;
        public bool IsReadingPaused => readingPaused;
        public MissionSchedule Schedule { get; private set; }
        public event Action<int> DayCompleted;
        public event Action<double> WarningReached;
        public event Action WindowClosed;
        public event Action<MissionState> StateChanged;
        public event Action<bool> RepairChanged;
        public event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Destroy only this service object, never a scene hierarchy containing the cockpit.
                enabled = false;
                Destroy(gameObject);
                return;
            }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            var warnings = new double[warningFractions.Length];
            for (var i = 0; i < warnings.Length; i++) warnings[i] = warningFractions[i];
            clock = new MissionClock(durationSeconds, missionDays, warnings);
            Schedule = new MissionSchedule(startDate, missionDays, nextOpportunityStart, nextOpportunityEnd);
            clock.DayCompleted += day => DayCompleted?.Invoke(day);
            clock.WarningReached += fraction => WarningReached?.Invoke(fraction);
            clock.WindowClosed += () => WindowClosed?.Invoke();
            clock.StateChanged += OnStateChanged;
            clock.Changed += () => Changed?.Invoke();
            pauseCoordinator = GetComponent<MissionPauseCoordinator>();
            pauseCoordinator.Initialize(this);
            hasFocus = Application.isFocused;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SynchronizePause();
        }

        private void Update() => SynchronizePause();

        // UI choices run in Update and settle their timestamp before the routine expiry tick.
        private void LateUpdate()
        {
            if (clock == null) return;
            SynchronizePause();
            clock.AdvanceTo(Time.realtimeSinceStartupAsDouble);
        }

        public bool StartMission()
        {
            if (clock == null) return false;
            var started = clock.Start(Time.realtimeSinceStartupAsDouble);
            SynchronizePause();
            return started;
        }

        public bool TryLaunch()
        {
            double clickedAt = Time.realtimeSinceStartupAsDouble;
            if (clock == null || readingPaused || menuPaused || applicationPaused || !hasFocus || sceneLoading || pendingSceneLoads > 0 ||
                pauseCoordinator.HasExternalTimeScalePause) return false;
            // Refresh actual joint state on the click itself, not only on the bridge's next Update.
            var bridge = FindFirstObjectByType<MissionRepairBridge>();
            if (bridge != null) bridge.RefreshRepairState();
            return clock.TryLaunch(repairComplete, clickedAt);
        }

        public void SetRepairComplete(bool complete)
        {
            if (clock == null || clock.HasEnded || repairComplete == complete) return;
            repairComplete = complete;
            RepairChanged?.Invoke(complete);
            Changed?.Invoke();
        }

        public void SetReadingPaused(bool paused) { readingPaused = paused; SynchronizePause(); }
        public void SetMenuPaused(bool paused) { menuPaused = paused; SynchronizePause(); }
        public void SetSceneLoading(bool loading) { sceneLoading = loading; SynchronizePause(); }

        /// <summary>Use for asynchronous transitions so the entire loading interval is excluded.</summary>
        public AsyncOperation LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            pendingSceneLoads++;
            SynchronizePause();
            try
            {
                var operation = SceneManager.LoadSceneAsync(sceneName, mode);
                if (operation == null) CompleteSceneLoad();
                else operation.completed += _ => CompleteSceneLoad();
                return operation;
            }
            catch
            {
                CompleteSceneLoad();
                throw;
            }
        }

        private void CompleteSceneLoad()
        {
            pendingSceneLoads = Math.Max(0, pendingSceneLoads - 1);
            SynchronizePause();
        }

        private void OnApplicationFocus(bool focused) { hasFocus = focused; SynchronizePause(); }
        private void OnApplicationPause(bool paused) { applicationPaused = paused; SynchronizePause(); }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // A synchronous SceneManager load has no pre-load event. Exclude the blocked frame.
            clock?.DiscardSuspendedTime(Time.realtimeSinceStartupAsDouble);
            pauseCoordinator?.RefreshTargets();
            SetSceneLoading(false);
        }

        private void OnStateChanged(MissionState state)
        {
            SynchronizePause();
            StateChanged?.Invoke(state);
        }

        private void SynchronizePause()
        {
            if (clock == null || pauseCoordinator == null) return;
            var paused = readingPaused || menuPaused || sceneLoading || pendingSceneLoads > 0 || applicationPaused || !hasFocus ||
                State != MissionState.Running || pauseCoordinator.HasExternalTimeScalePause;
            clock.SetPaused(paused, Time.realtimeSinceStartupAsDouble);
            pauseCoordinator.SetGameplayPaused(paused || State != MissionState.Running);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            pauseCoordinator?.Release();
            Instance = null;
        }

        private void OnValidate()
        {
            durationSeconds = Mathf.Max(1, durationSeconds);
            missionDays = Mathf.Clamp(missionDays, 1, 99);
            if (warningFractions == null) warningFractions = new[] { 0.5f, 0.25f, 0.1f };
            for (var i = 0; i < warningFractions.Length; i++) warningFractions[i] = Mathf.Clamp(warningFractions[i], 0.001f, 0.999f);
        }
    }
}
