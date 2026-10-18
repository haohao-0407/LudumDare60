using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace LudumDare60.Salvage
{
    /// <summary>
    /// Drives a world-space salvage readout from a <see cref="SalvageController"/>.
    /// Hold Space or press the panel to lift the catch zone. Restarts by itself after each attempt.
    /// </summary>
    [DisallowMultipleComponent]
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

        [SerializeField] private Color idleZoneColor = new Color(0.28f, 0.62f, 0.32f);
        [SerializeField] private Color hitZoneColor = new Color(0.55f, 0.95f, 0.45f);
        [SerializeField] private Color idleFishColor = new Color(0.95f, 0.66f, 0.12f);
        [SerializeField] private Color hitFishColor = new Color(1f, 0.85f, 0.35f);

        private UnityEngine.UI.Image zoneImage;
        private UnityEngine.UI.Image fishImage;
        private UnityEngine.UI.Image progressImage;
        private float restartAt = -1f;
        private bool pointerHeld;

        public void Configure(SalvageController source, RectTransform trackRect, RectTransform zoneRect,
            RectTransform targetRect, RectTransform progressRect, TMP_Text item, TMP_Text state,
            TMP_Text zoneReadout, TMP_Text fishReadout, TMP_Text progressReadout, TMP_Text hint)
        {
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
            zoneImage = catchZone != null ? catchZone.GetComponent<UnityEngine.UI.Image>() : null;
            fishImage = target != null ? target.GetComponent<UnityEngine.UI.Image>() : null;
            progressImage = progressFill != null ? progressFill.GetComponent<UnityEngine.UI.Image>() : null;
        }

        private void OnEnable()
        {
            if (controller != null) controller.Completed += OnCompleted;
        }

        private void OnDisable()
        {
            if (controller != null) controller.Completed -= OnCompleted;
            pointerHeld = false;
            if (controller != null) controller.SetControlHeld(false);
        }

        private void Start()
        {
            if (controller != null && !controller.IsActive) controller.TryStartSalvage();
        }

        public void OnPointerDown(PointerEventData eventData) => pointerHeld = true;
        public void OnPointerUp(PointerEventData eventData) => pointerHeld = false;

        private void Update()
        {
            if (controller == null || track == null) return;

            var held = pointerHeld || (Keyboard.current != null && Keyboard.current.spaceKey.isPressed);
            controller.SetControlHeld(held);

            if (restartAt > 0f && Time.time >= restartAt)
            {
                restartAt = -1f;
                controller.TryStartSalvage();
            }

            Render(controller.Snapshot);
        }

        private void Render(SalvageSnapshot snapshot)
        {
            var height = track.rect.height;
            if (height <= 1f) return;

            var zoneHeight = Mathf.Clamp(snapshot.ControlSize, 0.01f, 1f) * height;
            var zoneSize = catchZone.sizeDelta;
            catchZone.sizeDelta = new Vector2(zoneSize.x, zoneHeight);
            catchZone.anchoredPosition = new Vector2(0f, snapshot.ControlPosition * height);

            var cell = height / 10f;
            var fishSize = target.sizeDelta;
            target.sizeDelta = new Vector2(fishSize.x, cell);
            target.anchoredPosition = new Vector2(0f, snapshot.TargetPosition * height);

            if (progressFill != null)
            {
                var size = progressFill.sizeDelta;
                progressFill.sizeDelta = new Vector2(size.x, snapshot.Progress * height);
            }

            var isHit = snapshot.IsTargetInControl;
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
                switch (snapshot.State)
                {
                    case SalvageState.Searching:
                        stateLabel.text = ">> SCANNING DEBRIS FIELD";
                        stateLabel.color = new Color(0.45f, 0.72f, 0.66f);
                        break;
                    case SalvageState.SignalDetected:
                        stateLabel.text = ">> SIGNAL LOCK  -  HOLD NOW  [" + snapshot.TimeRemaining.ToString("0.0") + "s]";
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
                    default:
                        stateLabel.text = ">> SIGNAL LOST";
                        stateLabel.color = new Color(0.9f, 0.42f, 0.40f);
                        break;
                }
            }

            if (hintLabel != null)
            {
                hintLabel.text = snapshot.IsActive
                    ? "HOLD  [SPACE]  OR  CLICK THE SCREEN"
                    : "RE-ACQUIRING TARGET ...";
            }
        }

        private void OnCompleted(SalvageResult result)
        {
            if (itemLabel != null)
            {
                itemLabel.text = result.IsSuccess
                    ? "RECOVERED  " + Label(result.RewardId) + "  x" + result.RewardQuantity
                    : "SALVAGE FAILED  -  " + result.Reason.ToString().ToUpperInvariant();
                itemLabel.color = result.IsSuccess ? new Color(0.72f, 1f, 0.78f) : new Color(0.9f, 0.45f, 0.42f);
            }
            restartAt = Time.time + restartDelaySeconds;
        }

        /// <summary>ASCII-only label: the Glass TTY glyph atlas holds 95 printable ASCII characters.</summary>
        private static string Label(string rewardId) => string.IsNullOrEmpty(rewardId) ? "UNKNOWN" : rewardId.Replace('_', ' ').ToUpperInvariant();
    }
}
