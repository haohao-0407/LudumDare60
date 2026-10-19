using TMPro;
using LudumDare60.Storage;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace LudumDare60.Salvage
{
    /// <summary>
    /// Drives a world-space salvage readout from a <see cref="SalvageController"/>.
    /// Hold Space or press the panel to lift the catch zone. Restarts by itself after each attempt.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-100)]
    public sealed class SalvageScreenView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private SalvageController controller;
        [SerializeField] private RectTransform track;
        [SerializeField] private RectTransform catchZone;
        [SerializeField] private RectTransform target;
        [SerializeField] private RectTransform progressFill;
        [SerializeField] private TMP_Text itemLabel;
        [SerializeField] private TMP_Text stateLabel;
        [SerializeField] private TMP_Text zoneLabel;
        [SerializeField] private TMP_Text fishLabel;
        [SerializeField] private TMP_Text progressLabel;
        [SerializeField] private TMP_Text hintLabel;
        [SerializeField] private float restartDelaySeconds = 1.4f;
        [SerializeField] private InputActionReference controlAction;

        [SerializeField] private Color idleZoneColor = new Color(0.28f, 0.62f, 0.32f);
        [SerializeField] private Color hitZoneColor = new Color(0.55f, 0.95f, 0.45f);
        [SerializeField] private Color idleFishColor = new Color(0.95f, 0.66f, 0.12f);
        [SerializeField] private Color hitFishColor = new Color(1f, 0.85f, 0.35f);

        private UnityEngine.UI.Image zoneImage;
        private UnityEngine.UI.Image fishImage;
        private UnityEngine.UI.Image progressImage;
        private float restartAt = -1f;
        private bool pointerHeld;
        private bool waitForRelease;
        private InputAction input;
        private SalvageController subscribedController;
        private SalvageInventoryBridge inventoryBridge;

        public void Configure(SalvageController source, RectTransform trackRect, RectTransform zoneRect,
            RectTransform targetRect, RectTransform progressRect, TMP_Text item, TMP_Text state,
            TMP_Text zoneReadout, TMP_Text fishReadout, TMP_Text progressReadout, TMP_Text hint)
        {
            Unsubscribe();
            if (Application.IsPlaying(gameObject) && controller != null) controller.ReleaseControl();
            controller = source;
            track = trackRect;
            catchZone = zoneRect;
            target = targetRect;
            progressFill = progressRect;
            itemLabel = item;
            stateLabel = state;
            zoneLabel = zoneReadout;
            fishLabel = fishReadout;
            progressLabel = progressReadout;
            hintLabel = hint;
            CacheGraphics();
            if (Application.IsPlaying(gameObject) && isActiveAndEnabled) Subscribe();
        }

        private void OnEnable()
        {
            CacheGraphics();
            if (controlAction != null && controlAction.action != null)
            {
                // Own a clone, never enable/disable another system's shared action.
                input = controlAction.action.Clone();
                input.Enable();
            }
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ReleaseInput();
            input?.Disable();
            input?.Dispose();
            input = null;
            restartAt = -1f;
        }

        private void CacheGraphics()
        {
            inventoryBridge = controller != null ? controller.GetComponent<SalvageInventoryBridge>() : null;
            zoneImage = catchZone != null ? catchZone.GetComponent<UnityEngine.UI.Image>() : null;
            fishImage = target != null ? target.GetComponent<UnityEngine.UI.Image>() : null;
            progressImage = progressFill != null ? progressFill.GetComponent<UnityEngine.UI.Image>() : null;
        }

        private void Subscribe()
        {
            Unsubscribe();
            subscribedController = controller;
            if (subscribedController == null) return;
            subscribedController.Completed += OnCompleted;
            if (!subscribedController.IsActive && subscribedController.Result != null)
                OnCompleted(subscribedController.Result);
        }

        private void Unsubscribe()
        {
            if (subscribedController != null) subscribedController.Completed -= OnCompleted;
            subscribedController = null;
        }

        private bool CanOperate => controller != null && controller.CanAcceptInput
            && !controller.Snapshot.IsPaused && Time.timeScale > 0f && Application.isFocused;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && CanOperate) pointerHeld = true;
        }
        public void OnPointerUp(PointerEventData eventData) => pointerHeld = false;

        private void OnApplicationFocus(bool focused) { if (!focused) ReleaseInput(); }
        private void OnApplicationPause(bool paused) { if (paused) ReleaseInput(); }

        private void ReleaseInput()
        {
            pointerHeld = false;
            waitForRelease = true;
            if (controller != null) controller.ReleaseControl();
        }

        private void Update()
        {
            if (controller == null) return;
            if (!CanOperate)
            {
                ReleaseInput();
                return;
            }

            var held = pointerHeld || (input != null ? input.IsPressed()
                : Keyboard.current != null && Keyboard.current.spaceKey.isPressed);
            if (waitForRelease)
            {
                if (!held) waitForRelease = false;
                controller.ReleaseControl();
            }
            else controller.SetControlHeld(held);

            if (controller.IsActive) restartAt = -1f;
            else if (controller.Snapshot.State == SalvageState.Idle || (restartAt >= 0f && Time.time >= restartAt))
            {
                if (controller.TryStartSalvage()) restartAt = -1f;
            }
        }

        // All controller Updates have finished; the curved screen captures later at execution order 100.
        private void LateUpdate() { if (controller != null) Render(controller.Snapshot); }

        private void Render(SalvageSnapshot snapshot)
        {
            // Anchor ranges follow the track even when its mounted screen resizes later this frame.
            SetVerticalRange(catchZone, snapshot.ControlMin, snapshot.ControlMax);
            SetVerticalRange(target, snapshot.TargetMin, snapshot.TargetMax);
            if (target != null && snapshot.TargetSize == 0f)
                target.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 2f);
            SetVerticalRange(progressFill, 0f, snapshot.Progress);

            var isHit = snapshot.IsTargetInControl || snapshot.State == SalvageState.Succeeded;
            var isPaused = snapshot.IsPaused || !controller.CanAcceptInput || Time.timeScale <= 0f;
            if (zoneImage != null) zoneImage.color = isHit ? hitZoneColor : idleZoneColor;
            if (fishImage != null) fishImage.color = isHit ? hitFishColor : idleFishColor;
            if (progressImage != null)
                progressImage.color = Color.Lerp(new Color(0.95f, 0.55f, 0.10f), new Color(1f, 0.80f, 0.28f), snapshot.Progress);

            if (zoneLabel != null)
                zoneLabel.text = "CATCH " + snapshot.ControlMin.ToString("0.00") + " - " + snapshot.ControlMax.ToString("0.00")
                               + "  /  " + snapshot.ControlSize.ToString("0.00");
            if (fishLabel != null) fishLabel.text = "TARGET " + snapshot.TargetPosition.ToString("0.00");
            if (progressLabel != null) progressLabel.text = "PROGRESS " + Mathf.RoundToInt(snapshot.Progress * 100f) + "%";

            if (stateLabel != null)
            {
                if (isPaused)
                {
                    stateLabel.text = ">> RECOVERY PAUSED";
                    stateLabel.color = new Color(1f, 0.78f, 0.25f);
                }
                else
                switch (snapshot.State)
                {
                    case SalvageState.Idle:
                        stateLabel.text = ">> STANDBY";
                        stateLabel.color = new Color(0.45f, 0.72f, 0.66f);
                        break;
                    case SalvageState.Searching:
                        stateLabel.text = ">> SCANNING DEBRIS FIELD";
                        stateLabel.color = new Color(0.45f, 0.72f, 0.66f);
                        break;
                    case SalvageState.SignalDetected:
                        stateLabel.text = ">> SIGNAL LOCK  -  PRESS NOW  [" + snapshot.TimeRemaining.ToString("0.0") + "s]";
                        stateLabel.color = new Color(1f, 0.78f, 0.25f);
                        break;
                    case SalvageState.Salvaging:
                        stateLabel.text = isHit ? ">> GRAPPLING  + STABLE" : ">> GRAPPLING  -  SLIPPING";
                        stateLabel.color = isHit ? new Color(0.62f, 0.95f, 0.55f) : new Color(0.9f, 0.42f, 0.40f);
                        break;
                    case SalvageState.Succeeded:
                        stateLabel.text = ">> RECOVERY COMPLETE";
                        stateLabel.color = new Color(0.62f, 0.95f, 0.55f);
                        break;
                    case SalvageState.Cancelled:
                        stateLabel.text = ">> RECOVERY CANCELLED";
                        stateLabel.color = new Color(0.9f, 0.42f, 0.40f);
                        break;
                    default:
                        stateLabel.text = ">> SIGNAL LOST";
                        stateLabel.color = new Color(0.9f, 0.42f, 0.40f);
                        break;
                }
            }

            if (hintLabel != null)
            {
                hintLabel.text = isPaused ? "PAUSED  /  RELEASE CONTROL BEFORE RESUMING"
                    : snapshot.State == SalvageState.Searching ? "WAIT FOR SIGNAL, THEN PRESS CONTROL"
                    : snapshot.State == SalvageState.SignalDetected ? "RELEASE AND PRESS TO LOCK TARGET"
                    : snapshot.IsActive ? "HOLD CONTROL TO LIFT  /  RELEASE TO LOWER"
                    : "RE-ACQUIRING TARGET ...";
            }
            RenderItem(snapshot);
        }

        private void OnCompleted(SalvageResult result)
        {
            restartAt = Time.time + Mathf.Max(0f, restartDelaySeconds);
        }

        private void RenderItem(SalvageSnapshot snapshot)
        {
            if (itemLabel == null) return;
            var result = controller.Result;
            if (!snapshot.IsActive && result != null && result.AttemptId == snapshot.AttemptId)
            {
                itemLabel.text = result.IsSuccess
                    ? "RECOVERED  " + Label(result.RewardId) + "  x" + result.RewardQuantity
                    : "SALVAGE FAILED  -  " + result.Reason.ToString().ToUpperInvariant();
                itemLabel.color = result.IsSuccess ? new Color(0.72f, 1f, 0.78f) : new Color(0.9f, 0.45f, 0.42f);
                if (result.IsSuccess && inventoryBridge != null && !inventoryBridge.HasCollected(result))
                {
                    itemLabel.text = "RECOVERED  " + Label(result.RewardId) + "  /  CARGO NOT STORED";
                    itemLabel.color = new Color(0.9f, 0.45f, 0.42f);
                }
            }
            else
            {
                itemLabel.text = snapshot.State == SalvageState.Searching || snapshot.State == SalvageState.Idle
                    ? "UNKNOWN SIGNAL" : "TARGET  " + Label(snapshot.TargetId);
                itemLabel.color = new Color(0.8f, 0.94f, 0.9f);
            }
        }

        private static void SetVerticalRange(RectTransform rect, float min, float max)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(rect.anchorMin.x, Mathf.Clamp01(min));
            rect.anchorMax = new Vector2(rect.anchorMax.x, Mathf.Clamp01(max));
            rect.offsetMin = new Vector2(rect.offsetMin.x, 0f);
            rect.offsetMax = new Vector2(rect.offsetMax.x, 0f);
        }

        /// <summary>ASCII-only label: the Glass TTY glyph atlas holds 95 printable ASCII characters.</summary>
        private static string Label(string rewardId) => string.IsNullOrEmpty(rewardId) ? "UNKNOWN" : rewardId.Replace('_', ' ').ToUpperInvariant();
    }
}
