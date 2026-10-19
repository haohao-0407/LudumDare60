using System;

namespace LudumDare60.Salvage
{
    /// <summary>
    /// Engine-independent, single-threaded simulation. Call Tick with seconds and supply button state.
    /// No rendering, keyboard polling, coroutines, physics or inventory side effects.
    /// </summary>
    public sealed class SalvageSession
    {
        private const double StepSeconds = 1.0 / 120.0;
        private const int MaxStepsPerTick = 600;
        private SalvageSettings settings;
        private SalvageTarget target;
        private Random random;
        private int seed;
        private Guid attemptId;
        private double accumulator;
        private double elapsed;
        private double phaseElapsed;
        private float searchDuration;
        private float controlPosition;
        private float controlVelocity;
        private float targetPosition;
        private float targetVelocity;
        private float targetDestination;
        private float retargetRemaining;
        private float progress;
        private bool controlHeld;
        private bool paused;
        private bool completionPublished;

        public SalvageState State { get; private set; }
        public SalvageResult Result { get; private set; }
        public bool IsActive => State == SalvageState.Searching || State == SalvageState.SignalDetected || State == SalvageState.Salvaging;
        public SalvageSnapshot Snapshot => new SalvageSnapshot(attemptId, State, target?.TargetId,
            controlPosition, settings?.ControlSize ?? 0f, targetPosition, progress, (float)elapsed,
            RemainingTime(), controlHeld, paused, settings?.TargetSize ?? 0f);

        /// <summary>Emitted after a command or simulation tick; inspect State for phase changes.</summary>
        public event Action<SalvageSnapshot> SnapshotChanged;
        /// <summary>Emitted once per attempt, including failures and cancellation.</summary>
        public event Action<SalvageResult> Completed;

        /// <returns>False if another attempt is still active. Invalid configuration throws before changing state.</returns>
        public bool TryStart(SalvageSettings configuration, SalvageTarget salvageTarget, int? randomSeed = null)
        {
            if (IsActive) return false;
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (salvageTarget == null) throw new ArgumentNullException(nameof(salvageTarget));
            var validated = configuration.CreateValidatedCopy();
            settings = validated;
            target = salvageTarget;
            seed = randomSeed ?? Guid.NewGuid().GetHashCode();
            random = new Random(seed);
            attemptId = Guid.NewGuid();
            accumulator = elapsed = phaseElapsed = 0;
            controlPosition = settings.InitialControlPosition;
            controlVelocity = targetVelocity = 0f;
            targetPosition = settings.InitialTargetPosition;
            progress = settings.InitialProgress;
            controlHeld = paused = completionPublished = false;
            Result = null;
            searchDuration = RandomRange(settings.SearchDurationMin, settings.SearchDurationMax);
            ChooseDestination();
            State = searchDuration <= 0f ? SalvageState.SignalDetected : SalvageState.Searching;
            Publish();
            return true;
        }

        /// <summary>
        /// A fresh press during SignalDetected locks the target. During Salvaging, holding raises the control zone.
        /// Holding before the signal does not automatically lock it; release and press again.
        /// </summary>
        public void SetControlHeld(bool held)
        {
            if (!IsActive || controlHeld == held) return;
            // Releases are accepted while paused to prevent a stuck button after losing focus.
            if (paused && held) return;
            controlHeld = held;
            if (held && State == SalvageState.SignalDetected) BeginSalvaging();
            Publish();
        }

        /// <summary>Explicit alternative for a terminal command or a separate lock/confirm button.</summary>
        public bool TryLockTarget()
        {
            if (paused || State != SalvageState.SignalDetected) return false;
            BeginSalvaging();
            Publish();
            return true;
        }

        public void SetPaused(bool value)
        {
            if (!IsActive || paused == value) return;
            paused = value;
            controlHeld = false;
            Publish();
        }

        public bool Cancel()
        {
            if (!IsActive) return false;
            Finish(SalvageState.Cancelled, SalvageEndReason.Cancelled);
            Publish();
            return true;
        }

        /// <summary>Uses fixed simulation steps. Excess over five seconds is retained for later Tick calls.</summary>
        public void Tick(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime), "Delta time must be finite and non-negative.");
            if (!IsActive || paused) return;
            accumulator += deltaTime;
            var steps = 0;
            while (accumulator >= StepSeconds && IsActive && steps < MaxStepsPerTick)
            {
                accumulator -= StepSeconds;
                Simulate((float)StepSeconds);
                steps++;
            }
            if (steps > 0) Publish();
        }

        private void BeginSalvaging()
        {
            State = SalvageState.Salvaging;
            phaseElapsed = 0;
            // Do not apply time accumulated before the player's lock command to this phase.
            accumulator = 0;
        }

        private void Simulate(float dt)
        {
            elapsed += StepSeconds;
            phaseElapsed += StepSeconds;
            switch (State)
            {
                case SalvageState.Searching:
                    if (phaseElapsed >= searchDuration)
                    {
                        State = SalvageState.SignalDetected;
                        phaseElapsed = 0;
                    }
                    break;
                case SalvageState.SignalDetected:
                    if (phaseElapsed >= settings.SignalWindow)
                        Finish(SalvageState.Failed, SalvageEndReason.SignalMissed);
                    break;
                case SalvageState.Salvaging:
                    SimulateSalvaging(dt);
                    break;
            }
        }

        private void SimulateSalvaging(float dt)
        {
            controlVelocity = Clamp(controlVelocity + (controlHeld ? settings.LiftAcceleration : -settings.Gravity) * dt,
                -settings.MaxControlSpeed, settings.MaxControlSpeed);
            controlPosition += controlVelocity * dt;
            var halfSize = settings.ControlSize / 2f;
            if (controlPosition < halfSize || controlPosition > 1f - halfSize)
            {
                controlPosition = Clamp(controlPosition, halfSize, 1f - halfSize);
                controlVelocity = -controlVelocity * settings.BoundaryBounce;
            }

            retargetRemaining -= dt;
            if (retargetRemaining <= 0f) ChooseDestination();
            var desiredVelocity = Clamp((targetDestination - targetPosition) * 3f, -settings.TargetSpeed, settings.TargetSpeed);
            targetVelocity = MoveTowards(targetVelocity, desiredVelocity, settings.TargetAcceleration * dt);
            targetPosition += targetVelocity * dt;
            var targetHalfSize = settings.TargetSize / 2f;
            if (targetPosition < targetHalfSize || targetPosition > 1f - targetHalfSize)
            {
                targetPosition = Clamp(targetPosition, targetHalfSize, 1f - targetHalfSize);
                targetVelocity = 0f;
            }

            var inside = SalvageSnapshot.RangesOverlap(controlPosition - halfSize, controlPosition + halfSize,
                targetPosition - targetHalfSize, targetPosition + targetHalfSize);
            progress = Clamp(progress + (inside ? settings.ProgressGainPerSecond : -settings.ProgressLossPerSecond) * dt, 0f, 1f);
            if (progress >= 1f) Finish(SalvageState.Succeeded, SalvageEndReason.Recovered);
            else if (progress <= 0f) Finish(SalvageState.Failed, SalvageEndReason.TargetLost);
            else if (phaseElapsed >= settings.MaxSalvageDuration) Finish(SalvageState.Failed, SalvageEndReason.TimedOut);
        }

        private void ChooseDestination()
        {
            targetDestination = RandomRange(settings.TargetSize / 2f, 1f - settings.TargetSize / 2f);
            retargetRemaining = RandomRange(settings.RetargetIntervalMin, settings.RetargetIntervalMax);
        }

        private void Finish(SalvageState state, SalvageEndReason reason)
        {
            State = state;
            paused = controlHeld = false;
            accumulator = 0;
            // Inclusive upper bound without overflowing when RewardQuantityMax is int.MaxValue.
            var quantity = state == SalvageState.Succeeded
                ? (int)(target.RewardQuantityMin + (long)(random.NextDouble() * ((long)target.RewardQuantityMax - target.RewardQuantityMin + 1)))
                : 0;
            Result = new SalvageResult(Snapshot, reason, target.RewardId, quantity, seed);
        }

        private float RemainingTime()
        {
            switch (State)
            {
                case SalvageState.Searching: return Math.Max(0f, searchDuration - (float)phaseElapsed);
                case SalvageState.SignalDetected: return Math.Max(0f, settings.SignalWindow - (float)phaseElapsed);
                case SalvageState.Salvaging: return Math.Max(0f, settings.MaxSalvageDuration - (float)phaseElapsed);
                default: return 0f;
            }
        }

        private void Publish()
        {
            var snapshot = Snapshot;
            var completion = !completionPublished ? Result : null;
            if (completion != null) completionPublished = true;
            // All mutations finish before dispatch; a listener may start another attempt safely.
            SnapshotChanged?.Invoke(snapshot);
            if (completion != null) Completed?.Invoke(completion);
        }

        private float RandomRange(float min, float max) => min + (float)random.NextDouble() * (max - min);
        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        private static float MoveTowards(float value, float targetValue, float maxDelta)
            => value + Clamp(targetValue - value, -maxDelta, maxDelta);
    }
}
