using LudumDare60.AssemblyDemo;
using TMPro;
using UnityEngine;

namespace LudumDare60.ReturnWindow
{
    public class DeviceAssemblyLayout : MonoBehaviour
    {
        public AssemblyDemoController assembly;
        public Vector3[] assembledLocalPositions;
        public Vector3[] assembledModelPositions;
        public Quaternion[] assembledModelRotations;
        public string deviceLabel = "NAVIGATION DEVICE";
        public Vector2 viewCenter = new Vector2(0, 1.4f);
        public Quaternion modelToBoard = Quaternion.identity;
        public float modelScale = 5;
        public Vector3[] nativeStartPositions;
        public Quaternion[] nativeStartRotations;
        public Vector3[] nativeTargetPositions;
        public Quaternion[] nativeTargetRotations;
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
                if (module.modelVisual != null)
                {
                    if (assembledModelPositions != null && i < assembledModelPositions.Length)
                        module.modelVisual.localPosition = assembledModelPositions[i];
                    if (assembledModelRotations != null && i < assembledModelRotations.Length)
                        module.modelVisual.localRotation = assembledModelRotations[i];
                }
                module.enabled = false;
            }
            if (status != null) status.text = deviceLabel + " / ASSEMBLY COMPLETE\nAll joints secured. The completed device stays intact until the next run.";
        }
    }
}
