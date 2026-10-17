using System;

namespace LudumDare60.Salvage
{
    public enum SalvageState
    {
        Idle,
        Searching,
        SignalDetected,
        Salvaging,
        Succeeded,
        Failed,
        Cancelled
    }

    public enum SalvageEndReason
    {
        None,
        Recovered,
        SignalMissed,
        TargetLost,
        TimedOut,
        Cancelled
    }

    /// <summary>A target/reward contract for inventory or world systems; contains no engine objects.</summary>
    public sealed class SalvageTarget
    {
        public string TargetId { get; }
        public string RewardId { get; }
        public int RewardQuantityMin { get; }
        public int RewardQuantityMax { get; }

        public SalvageTarget(string targetId, string rewardId, int rewardQuantityMin = 1, int rewardQuantityMax = 1)
        {
            if (string.IsNullOrWhiteSpace(targetId)) throw new ArgumentException("A target ID is required.", nameof(targetId));
            if (string.IsNullOrWhiteSpace(rewardId)) throw new ArgumentException("A reward ID is required.", nameof(rewardId));
            if (rewardQuantityMin < 1) throw new ArgumentOutOfRangeException(nameof(rewardQuantityMin));
            if (rewardQuantityMax < rewardQuantityMin) throw new ArgumentOutOfRangeException(nameof(rewardQuantityMax));
            TargetId = targetId;
            RewardId = rewardId;
            RewardQuantityMin = rewardQuantityMin;
            RewardQuantityMax = rewardQuantityMax;
        }
    }

    /// <summary>Read-only presentation data. Positions, sizes and progress use the range [0, 1].</summary>
    public readonly struct SalvageSnapshot
    {
        public Guid AttemptId { get; }
        public SalvageState State { get; }
        public string TargetId { get; }
        public float ControlPosition { get; }
        public float ControlSize { get; }
        public float ControlMin => ControlPosition - ControlSize / 2f;
        public float ControlMax => ControlPosition + ControlSize / 2f;
        public float TargetPosition { get; }
        public float Progress { get; }
        public float ElapsedSeconds { get; }
        public float TimeRemaining { get; }
        public bool IsControlHeld { get; }
        public bool IsPaused { get; }
        public bool IsActive => State == SalvageState.Searching || State == SalvageState.SignalDetected || State == SalvageState.Salvaging;
        public bool IsTargetInControl => State == SalvageState.Salvaging && TargetPosition >= ControlMin && TargetPosition <= ControlMax;

        internal SalvageSnapshot(Guid attemptId, SalvageState state, string targetId, float controlPosition,
            float controlSize, float targetPosition, float progress, float elapsedSeconds, float timeRemaining,
            bool isControlHeld, bool isPaused)
        {
            AttemptId = attemptId;
            State = state;
            TargetId = targetId;
            ControlPosition = controlPosition;
            ControlSize = controlSize;
            TargetPosition = targetPosition;
            Progress = progress;
            ElapsedSeconds = elapsedSeconds;
            TimeRemaining = timeRemaining;
            IsControlHeld = isControlHeld;
            IsPaused = isPaused;
        }
    }

    /// <summary>A completion notification, not an inventory transaction. Reward quantity is zero unless successful.</summary>
    public sealed class SalvageResult
    {
        public Guid AttemptId { get; }
        public SalvageState State { get; }
        public SalvageEndReason Reason { get; }
        public string TargetId { get; }
        public string RewardId { get; }
        public int RewardQuantity { get; }
        public float ElapsedSeconds { get; }
        public int RandomSeed { get; }
        public bool IsSuccess => State == SalvageState.Succeeded;

        internal SalvageResult(SalvageSnapshot snapshot, SalvageEndReason reason, string rewardId, int quantity, int seed)
        {
            AttemptId = snapshot.AttemptId;
            State = snapshot.State;
            Reason = reason;
            TargetId = snapshot.TargetId;
            RewardId = rewardId;
            RewardQuantity = quantity;
            ElapsedSeconds = snapshot.ElapsedSeconds;
            RandomSeed = seed;
        }
    }
}
