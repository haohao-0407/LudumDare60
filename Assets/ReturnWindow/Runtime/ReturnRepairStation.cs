using LudumDare60.AssemblyDemo;
using LudumDare60.TerminalDialogue;
using UnityEngine;

namespace LudumDare60.ReturnWindow
{
    /// <summary>The existing assembly puzzle is a repair workbench in this single mission scene.</summary>
    public sealed class ReturnRepairStation : MonoBehaviour
    {
        public Camera pilotCamera;
        public ReturnPilotController pilot;
        public TerminalDialogueController terminal;
        public MissionController mission;
        public GameObject workshop;
        public GameObject workshopUI;
        public AssemblyDemoController assembly;
        public DeviceAssemblyWorkbench deviceWorkbench;
        public Vector3 workbenchCameraPosition = new Vector3(50, 0, -10);
        public bool IsOpen { get; private set; }
        private Vector3 seatPosition;
        private Quaternion seatRotation;
        private CameraClearFlags seatClear;
        private Color seatBackground;
        private float seatFieldOfView;
        private void Start()
        {
            mission = MissionController.Instance;
            // Keep the physical joint graph with the run, just like its inventory and clock.
            var existingWorkbench = mission.GetComponentInChildren<DeviceAssemblyWorkbench>(true);
            var existing = mission.GetComponentInChildren<AssemblyDemoController>(true);
            if (existingWorkbench != null && existingWorkbench != deviceWorkbench)
            {
                Destroy(workshop);
                deviceWorkbench = existingWorkbench;
                workshop = existingWorkbench.gameObject;
                assembly = existingWorkbench.Current;
            }
            else if (existingWorkbench == null && existing != null && existing != assembly)
            {
                Destroy(workshop);
                assembly = existing;
                workshop = existing.gameObject;
            }
            else workshop.transform.SetParent(mission.transform, true);
            assembly.ViewCamera = pilotCamera;
            if (deviceWorkbench != null)
            {
                deviceWorkbench.viewCamera = pilotCamera;
                if (workshopUI != null)
                {
                    foreach (var label in workshopUI.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    {
                        if (label.name == "Title") deviceWorkbench.title = label;
                        if (label.name == "Instructions") deviceWorkbench.instructions = label;
                    }
                    foreach (var button in workshopUI.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                    {
                        if (!button.name.StartsWith("F")) continue;
                        button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                        if (button.name.StartsWith("F1")) button.onClick.AddListener(() => SelectDevice(0));
                        if (button.name.StartsWith("F2")) button.onClick.AddListener(() => SelectDevice(1));
                        if (button.name.StartsWith("F3")) button.onClick.AddListener(() => SelectDevice(2));
                    }
                }
            }
            workshop.SetActive(false);
            if (deviceWorkbench != null) GetComponent<MissionRepairBridge>().Configure(deviceWorkbench);
            else GetComponent<MissionRepairBridge>().Configure(assembly);
        }

        private void Update() { if (deviceWorkbench != null) assembly = deviceWorkbench.Current; }
        public void SelectDevice(int index)
        {
            if (deviceWorkbench == null) return;
            deviceWorkbench.SelectDevice(index);
            assembly = deviceWorkbench.Current;
        }

        public void OpenRepair()
        {
            if (IsOpen || mission.State != MissionState.Running || mission.IsGameplayPaused) return;
            seatPosition = pilotCamera.transform.position;
            seatRotation = pilotCamera.transform.rotation;
            seatClear = pilotCamera.clearFlags;
            seatBackground = pilotCamera.backgroundColor;
            seatFieldOfView = pilotCamera.fieldOfView;
            IsOpen = true;
            workshop.SetActive(true);
            workshopUI.SetActive(true);
            pilotCamera.transform.SetPositionAndRotation(workbenchCameraPosition, Quaternion.identity);
            pilotCamera.orthographic = true;
            pilotCamera.orthographicSize = 5.4f;
            pilotCamera.clearFlags = CameraClearFlags.SolidColor;
            pilotCamera.backgroundColor = new Color(.018f, .028f, .032f);
            assembly.ViewCamera = pilotCamera;
            if (deviceWorkbench != null) SelectDevice(deviceWorkbench.selectedDevice);
        }

        public void CloseRepair() => CloseRepair(true);
        public void CloseRepair(bool reconnect)
        {
            if (!IsOpen) return;
            // Publish the verified physical state before disabling the workbench.
            mission.SetRepairComplete(deviceWorkbench != null ? deviceWorkbench.IsComplete : assembly.IsAssemblyComplete);
            IsOpen = false;
            workshop.SetActive(false);
            workshopUI.SetActive(false);
            pilotCamera.orthographic = false;
            pilotCamera.fieldOfView = seatFieldOfView;
            pilotCamera.clearFlags = seatClear;
            pilotCamera.backgroundColor = seatBackground;
            pilotCamera.transform.SetPositionAndRotation(seatPosition, seatRotation);
            if (reconnect && mission.State == MissionState.Running) terminal.Open();
        }
    }
}
