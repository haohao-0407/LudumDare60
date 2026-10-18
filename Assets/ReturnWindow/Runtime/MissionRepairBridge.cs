using LudumDare60.AssemblyDemo;
using UnityEngine;

namespace LudumDare60.ReturnWindow
{
    /// <summary>Uses the assembly's real compatible-port connection graph, never a UI flag.</summary>
    [DisallowMultipleComponent]
    public sealed class MissionRepairBridge : MonoBehaviour
    {
        [SerializeField] private AssemblyDemoController assembly;
        public AssemblyDemoController Assembly => assembly;

        private void Start()
        {
            if (assembly == null) assembly = FindFirstObjectByType<AssemblyDemoController>();
            RefreshRepairState();
            MissionController.Instance?.GetComponent<MissionPauseCoordinator>()?.RefreshTargets();
        }

        private void Update() => RefreshRepairState();

        public void Configure(AssemblyDemoController source)
        {
            assembly = source;
            RefreshRepairState();
            MissionController.Instance?.GetComponent<MissionPauseCoordinator>()?.RefreshTargets();
        }

        public void RefreshRepairState()
        {
            if (assembly != null && MissionController.Instance != null)
                MissionController.Instance.SetRepairComplete(assembly.IsAssemblyComplete);
        }
    }
}
