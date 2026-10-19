using LudumDare60.AssemblyDemo;
using LudumDare60.Salvage;
using UnityEngine;

namespace LudumDare60.ReturnWindow
{
    /// <summary>Freezes simulation while the terminal continues typing with unscaled time.</summary>
    [DisallowMultipleComponent]
    public sealed class MissionPauseCoordinator : MonoBehaviour
    {
        private MissionController owner;
        private AssemblyDemoController[] assemblies = new AssemblyDemoController[0];
        private SalvageController[] salvages = new SalvageController[0];
        private bool isPaused;
        private bool ownsTimeScale;
        private float previousTimeScale = 1;

        public bool HasExternalTimeScalePause => !ownsTimeScale && Time.timeScale <= 0;

        public void Initialize(MissionController controller)
        {
            owner = controller;
            RefreshTargets();
        }

        public void RefreshTargets()
        {
            assemblies = FindObjectsByType<AssemblyDemoController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            salvages = FindObjectsByType<SalvageController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            ApplyInputBlock();
        }

        public void SetGameplayPaused(bool paused)
        {
            isPaused = paused;
            if (paused && !ownsTimeScale)
            {
                previousTimeScale = Time.timeScale;
                // Leave a pre-existing external pause owned by its original menu.
                ownsTimeScale = previousTimeScale > 0;
                if (ownsTimeScale) Time.timeScale = 0;
            }
            else if (!paused && ownsTimeScale)
            {
                Time.timeScale = previousTimeScale;
                ownsTimeScale = false;
            }
            if (paused && ownsTimeScale) Time.timeScale = 0;
            ApplyInputBlock();
        }

        private void ApplyInputBlock()
        {
            foreach (var assembly in assemblies) if (assembly != null) assembly.InputBlocked = isPaused;
            foreach (var salvage in salvages) if (salvage != null) salvage.GameplayBlocked = isPaused;
        }

        public void Release()
        {
            if (owner == null || MissionController.Instance != owner) return;
            SetGameplayPaused(false);
        }

        private void OnDestroy() => Release();
    }
}
