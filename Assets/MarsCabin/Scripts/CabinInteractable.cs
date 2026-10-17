using System;
using UnityEngine;
using UnityEngine.Events;

namespace LudumDare60.MarsCabin
{
    public enum CabinControlKind { Device, Button, Knob, Toggle, Lever, Joystick }

    /// <summary>Input-independent control state and local pivot motion. A future interaction system calls this API.</summary>
    [DisallowMultipleComponent]
    public sealed class CabinInteractable : MonoBehaviour
    {
        [SerializeField] string stableId;
        [SerializeField] CabinControlKind kind;
        [SerializeField, Min(.1f)] float interactionRange = 1.8f;
        [SerializeField] Transform motionPivot;
        [SerializeField] Vector3 localAxis = Vector3.up;
        [SerializeField] Vector3 secondaryLocalAxis = Vector3.right;
        [SerializeField] float minimumAngle = -45;
        [SerializeField] float maximumAngle = 45;
        [SerializeField, Min(0)] float buttonTravel = .012f;
        [SerializeField] bool buttonUsesRotation;
        [SerializeField, Range(0, 1)] float normalizedValue;
        [SerializeField] Vector2 joystickValue;
        [SerializeField] Vector3 restLocalPosition;
        [SerializeField] Quaternion restLocalRotation = Quaternion.identity;
        [SerializeField] bool restPoseCaptured;
        [SerializeField] UnityEvent onActivated = new UnityEvent();
        [SerializeField] UnityEvent<float> onValueChanged = new UnityEvent<float>();
        [SerializeField] UnityEvent<Vector2> onJoystickChanged = new UnityEvent<Vector2>();

        public string StableId => stableId;
        public CabinControlKind Kind => kind;
        public float InteractionRange => interactionRange;
        public float NormalizedValue => normalizedValue;
        public Vector2 JoystickValue => joystickValue;
        public Transform MotionPivot => motionPivot;
        public UnityEvent OnActivated => onActivated;
        public UnityEvent<float> OnValueChanged => onValueChanged;
        public UnityEvent<Vector2> OnJoystickChanged => onJoystickChanged;

        public void Configure(string id, CabinControlKind controlKind, Transform pivot,
            Vector3 axis, Vector3 secondaryAxis, float minAngle = -45, float maxAngle = 45,
            float travel = .012f, float range = 1.8f, bool rotateButton = false)
        {
            stableId = id;
            kind = controlKind;
            motionPivot = pivot;
            localAxis = axis.sqrMagnitude > .00001f ? axis.normalized : Vector3.up;
            secondaryLocalAxis = secondaryAxis.sqrMagnitude > .00001f ? secondaryAxis.normalized : Vector3.right;
            minimumAngle = minAngle;
            maximumAngle = maxAngle;
            buttonTravel = Mathf.Max(0, travel);
            buttonUsesRotation = rotateButton;
            interactionRange = Mathf.Max(.1f, range);
            normalizedValue = kind == CabinControlKind.Knob || kind == CabinControlKind.Lever ? .5f : 0;
            joystickValue = Vector2.zero;
            CaptureRestPose();
            ApplyPose();
        }

        public bool CanInteractFrom(Vector3 origin)
        {
            return isActiveAndEnabled && (origin - transform.position).sqrMagnitude <= interactionRange * interactionRange;
        }

        public bool TryActivate(Vector3 origin)
        {
            if (!CanInteractFrom(origin)) return false;
            Activate();
            return true;
        }

        public void Activate()
        {
            if (kind == CabinControlKind.Button) SetNormalizedValue(1);
            else if (kind == CabinControlKind.Toggle) SetNormalizedValue(normalizedValue >= .5f ? 0 : 1);
            onActivated.Invoke();
        }

        /// <summary>Call when a press ends; buttons remain depressed between Activate and Release.</summary>
        public void Release()
        {
            if (kind == CabinControlKind.Button) SetNormalizedValue(0);
        }

        public bool TrySetNormalizedValue(float value, Vector3 origin)
        {
            if (!CanInteractFrom(origin)) return false;
            SetNormalizedValue(value);
            return true;
        }

        public void SetNormalizedValue(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            var clamped = Mathf.Clamp01(value);
            if (kind == CabinControlKind.Toggle || kind == CabinControlKind.Button) clamped = clamped >= .5f ? 1 : 0;
            var changed = !Mathf.Approximately(normalizedValue, clamped);
            normalizedValue = clamped;
            ApplyPose();
            if (changed) onValueChanged.Invoke(normalizedValue);
        }

        public bool TrySetJoystick(Vector2 value, Vector3 origin)
        {
            if (!CanInteractFrom(origin)) return false;
            SetJoystick(value);
            return true;
        }

        public void SetJoystick(Vector2 value)
        {
            if (float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.x) || float.IsInfinity(value.y)) return;
            var clamped = Vector2.ClampMagnitude(value, 1);
            var changed = (clamped - joystickValue).sqrMagnitude > .000001f;
            joystickValue = clamped;
            ApplyPose();
            if (changed) onJoystickChanged.Invoke(joystickValue);
        }

        public void CaptureRestPose()
        {
            if (motionPivot == null) { restPoseCaptured = false; return; }
            restLocalPosition = motionPivot.localPosition;
            restLocalRotation = motionPivot.localRotation;
            restPoseCaptured = true;
        }

        void Awake() { if (!restPoseCaptured) CaptureRestPose(); ApplyPose(); }

        void ApplyPose()
        {
            if (motionPivot == null || !restPoseCaptured || kind == CabinControlKind.Device) return;
            motionPivot.localPosition = restLocalPosition;
            motionPivot.localRotation = restLocalRotation;
            if (kind == CabinControlKind.Button)
            {
                if (buttonUsesRotation)
                {
                    motionPivot.localRotation = restLocalRotation * Quaternion.AngleAxis(Mathf.Lerp(minimumAngle, maximumAngle, normalizedValue), localAxis);
                    return;
                }
                // Travel is measured in metres even when an FBX parent carries a 100x unit conversion.
                var worldMotion = motionPivot.TransformDirection(localAxis) * (buttonTravel * normalizedValue);
                motionPivot.localPosition = restLocalPosition + (motionPivot.parent != null
                    ? motionPivot.parent.InverseTransformVector(worldMotion) : worldMotion);
            }
            else if (kind == CabinControlKind.Joystick)
                motionPivot.localRotation = restLocalRotation * Quaternion.AngleAxis(joystickValue.x * maximumAngle, localAxis) *
                    Quaternion.AngleAxis(joystickValue.y * maximumAngle, secondaryLocalAxis);
            else
                motionPivot.localRotation = restLocalRotation * Quaternion.AngleAxis(
                    Mathf.Lerp(minimumAngle, maximumAngle, normalizedValue), localAxis);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            interactionRange = Mathf.Max(.1f, interactionRange);
            if (string.IsNullOrWhiteSpace(stableId)) stableId = "cabin-" + Guid.NewGuid().ToString("N");
        }
#endif
    }
}
