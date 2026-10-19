using System;

namespace LudumDare60.Salvage
{
    /// <summary>All distances are normalized to a track from 0 (bottom) to 1 (top).</summary>
    [Serializable]
    public sealed class SalvageSettings
    {
        public float SearchDurationMin = 1.5f;
        public float SearchDurationMax = 4f;
        public float SignalWindow = 1.5f;

        public float ControlSize = 0.25f;
        public float InitialControlPosition = 0.25f;
        public float LiftAcceleration = 2.8f;
        public float Gravity = 1.8f;
        public float MaxControlSpeed = 1f;
        public float BoundaryBounce = 0.15f;

        public float TargetSize = 0.1f;
        public float InitialTargetPosition = 0.3f;
        public float TargetSpeed = 0.5f;
        public float TargetAcceleration = 1.6f;
        public float RetargetIntervalMin = 0.35f;
        public float RetargetIntervalMax = 1.1f;

        public float InitialProgress = 0.3f;
        public float ProgressGainPerSecond = 0.22f;
        public float ProgressLossPerSecond = 0.18f;
        public float MaxSalvageDuration = 45f;

        /// <summary>Attempts use a validated copy, so tuning cannot change a running attempt.</summary>
        public SalvageSettings CreateValidatedCopy()
        {
            Validate();
            return (SalvageSettings)MemberwiseClone();
        }

        public void Validate()
        {
            Range(SearchDurationMin, 0f, 600f, nameof(SearchDurationMin));
            Range(SearchDurationMax, SearchDurationMin, 600f, nameof(SearchDurationMax));
            Range(SignalWindow, 0.01f, 600f, nameof(SignalWindow));
            Range(ControlSize, 0.01f, 1f, nameof(ControlSize));
            Range(InitialControlPosition, ControlSize / 2f, 1f - ControlSize / 2f, nameof(InitialControlPosition));
            Positive(LiftAcceleration, nameof(LiftAcceleration));
            Positive(Gravity, nameof(Gravity));
            Positive(MaxControlSpeed, nameof(MaxControlSpeed));
            Range(BoundaryBounce, 0f, 1f, nameof(BoundaryBounce));
            Range(TargetSize, 0f, 1f, nameof(TargetSize));
            Range(InitialTargetPosition, TargetSize / 2f, 1f - TargetSize / 2f, nameof(InitialTargetPosition));
            Range(TargetSpeed, 0f, float.MaxValue, nameof(TargetSpeed));
            Positive(TargetAcceleration, nameof(TargetAcceleration));
            Range(RetargetIntervalMin, 0.01f, 600f, nameof(RetargetIntervalMin));
            Range(RetargetIntervalMax, RetargetIntervalMin, 600f, nameof(RetargetIntervalMax));
            if (InitialProgress <= 0f || InitialProgress >= 1f || float.IsNaN(InitialProgress))
                throw new ArgumentOutOfRangeException(nameof(InitialProgress), "Initial progress must be between 0 and 1, exclusively.");
            Positive(ProgressGainPerSecond, nameof(ProgressGainPerSecond));
            Positive(ProgressLossPerSecond, nameof(ProgressLossPerSecond));
            Range(MaxSalvageDuration, 0.01f, 600f, nameof(MaxSalvageDuration));
        }

        private static void Positive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(name, "Value must be finite and positive.");
        }

        private static void Range(float value, float min, float max, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new ArgumentOutOfRangeException(name, $"Value must be finite and between {min} and {max}.");
        }
    }
}
