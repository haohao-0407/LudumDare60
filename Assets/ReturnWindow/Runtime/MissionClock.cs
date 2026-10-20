using System;
using System.Collections.Generic;

namespace LudumDare60.ReturnWindow
{
    public enum MissionState { Ready, Running, Departed, Stayed }

    /// <summary>
    /// Single-run mission time. All timestamps come from the same monotonic clock;
    /// paused wall time is discarded and launch is settled at the command timestamp.
    /// This class has no Unity dependency and never reads the operating-system clock.
    /// </summary>
    public sealed class MissionClock
    {
        private readonly double[] warningFractions;
        private readonly bool[] warningPublished;
        private double lastTimestamp;
        private double elapsedSeconds;
        private int completedDays;

        public MissionClock(double durationSeconds = 1200, int missionDays = 20, double[] warnings = null)
        {
            if (!IsFinite(durationSeconds) || durationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            if (missionDays < 1) throw new ArgumentOutOfRangeException(nameof(missionDays));
            DurationSeconds = durationSeconds;
            MissionDays = missionDays;
            var sorted = new List<double>(warnings ?? new[] { 0.5, 0.25, 0.1 });
            foreach (var warning in sorted)
                if (!IsFinite(warning) || warning <= 0 || warning >= 1) throw new ArgumentOutOfRangeException(nameof(warnings));
            sorted.Sort((a, b) => b.CompareTo(a));
            for (var i = sorted.Count - 1; i > 0; i--)
                if (sorted[i] == sorted[i - 1]) sorted.RemoveAt(i);
            warningFractions = sorted.ToArray();
            warningPublished = new bool[warningFractions.Length];
        }

        public double DurationSeconds { get; }
        public int MissionDays { get; }
        public double ElapsedSeconds => elapsedSeconds;
        public double RemainingSeconds => Math.Max(0, DurationSeconds - elapsedSeconds);
        public double RemainingFraction => RemainingSeconds / DurationSeconds;
        public double RemainingMissionSeconds => RemainingFraction * MissionDays * 86400.0;
        public int CompletedDays => completedDays;
        public int CurrentDay => Math.Min(MissionDays, completedDays + 1);
        public MissionState State { get; private set; }
        public bool IsPaused { get; private set; }
        public bool HasEnded => State == MissionState.Departed || State == MissionState.Stayed;

        /// <summary>One-based completed day, emitted once for every crossed boundary.</summary>
        public event Action<int> DayCompleted;
        public event Action<double> WarningReached;
        public event Action WindowClosed;
        public event Action<MissionState> StateChanged;
        public event Action Changed;

        public bool Start(double timestamp)
        {
            ValidateTimestamp(timestamp);
            if (State != MissionState.Ready) return false;
            lastTimestamp = timestamp;
            State = MissionState.Running;
            StateChanged?.Invoke(State);
            Changed?.Invoke();
            return true;
        }

        public void AdvanceTo(double timestamp)
        {
            ValidateTimestamp(timestamp);
            // An old input/frame timestamp cannot rewind time or make later ticks double count.
            if (timestamp <= lastTimestamp) return;
            var delta = timestamp - lastTimestamp;
            lastTimestamp = timestamp;
            if (State != MissionState.Running || IsPaused) return;

            elapsedSeconds = Math.Min(DurationSeconds, elapsedSeconds + delta);
            var oldCompletedDays = completedDays;
            completedDays = Math.Min(MissionDays, (int)Math.Floor(elapsedSeconds / DurationSeconds * MissionDays));
            var crossedWarnings = new List<double>();
            for (var i = 0; i < warningFractions.Length; i++)
            {
                if (warningPublished[i] || RemainingFraction > warningFractions[i]) continue;
                warningPublished[i] = true;
                crossedWarnings.Add(warningFractions[i]);
            }
            var closed = RemainingSeconds <= 0;
            // Commit the terminal state before publishing: listeners cannot launch at zero.
            if (closed) State = MissionState.Stayed;
            for (var day = oldCompletedDays + 1; day <= completedDays; day++) DayCompleted?.Invoke(day);
            foreach (var warning in crossedWarnings) WarningReached?.Invoke(warning);
            if (closed)
            {
                StateChanged?.Invoke(State);
                WindowClosed?.Invoke();
            }
            Changed?.Invoke();
        }

        public void SetPaused(bool paused, double timestamp)
        {
            ValidateTimestamp(timestamp);
            if (IsPaused == paused) return;
            AdvanceTo(timestamp);
            IsPaused = paused;
            Changed?.Invoke();
        }

        public bool TryLaunch(bool repairComplete, double timestamp)
        {
            AdvanceTo(timestamp);
            if (State != MissionState.Running || !repairComplete || RemainingSeconds <= 0) return false;
            State = MissionState.Departed;
            StateChanged?.Invoke(State);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Resume the existing deadline when a confirmed flight is cancelled.
        /// Time spent in the departure sequence is discarded without restoring any budget.
        /// </summary>
        public bool CancelDeparture(double timestamp)
        {
            ValidateTimestamp(timestamp);
            if (State != MissionState.Departed) return false;
            lastTimestamp = Math.Max(lastTimestamp, timestamp);
            State = MissionState.Running;
            StateChanged?.Invoke(State);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Re-anchor after a blocking scene load whose start callback Unity does not expose.
        /// Gameplay cannot run during that gap, so none of its wall time is mission time.
        /// </summary>
        public void DiscardSuspendedTime(double timestamp)
        {
            ValidateTimestamp(timestamp);
            lastTimestamp = Math.Max(lastTimestamp, timestamp);
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static void ValidateTimestamp(double timestamp)
        {
            if (!IsFinite(timestamp) || timestamp < 0) throw new ArgumentOutOfRangeException(nameof(timestamp));
        }
    }
}
