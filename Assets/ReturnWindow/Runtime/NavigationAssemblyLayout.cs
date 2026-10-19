using LudumDare60.AssemblyDemo;
using TMPro;
using UnityEngine;

namespace LudumDare60.ReturnWindow
{
    /// <summary>Displays the actual device at its recorded solved pose after all three joints connect.</summary>
    public sealed class NavigationAssemblyLayout : MonoBehaviour
    {
        public AssemblyDemoController assembly;
        public Vector3[] assembledLocalPositions;
        public TMP_Text status;
        private bool presented;

        private void LateUpdate() => PresentCompletion();

        public void PresentCompletion()
        {
            if (presented || assembly == null || !assembly.IsAssemblyComplete) return;
            presented = true;
            for (int i = 0; i < assembly.Modules.Length; i++)
            {
                var module = assembly.Modules[i];
                module.transform.localPosition = assembledLocalPositions[i];
                module.transform.localRotation = Quaternion.identity;
                // Remove only the disposable selection/connector guides; retain the native model.
                module.enabled = false;
            }
            if (status != null) status.text = "NAVIGATION DEVICE / ASSEMBLY COMPLETE\nAll three joints secured. Return to the cabin to continue.";
        }
    }
}
