using System;
using UnityEngine;

namespace LudumDare60.Salvage
{
    /// <summary>Optional Unity lifecycle adapter. Supply input from UI or the project's Input Actions.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Salvage/Salvage Controller")]
    public sealed class SalvageController : MonoBehaviour
    {
        [SerializeField] private SalvageDefinition defaultDefinition;
        private SalvageSession session;

        private SalvageSession Session
        {
            get
            {
                if (session != null) return session;
                session = new SalvageSession();
                session.SnapshotChanged += ForwardSnapshot;
                session.Completed += ForwardCompletion;
                return session;
            }
        }

        public SalvageSnapshot Snapshot => Session.Snapshot;
        public SalvageResult Result => Session.Result;
        public bool IsActive => Session.IsActive;
        public event Action<SalvageSnapshot> SnapshotChanged;
        public event Action<SalvageResult> Completed;

        public bool TryStartSalvage() => TryStartSalvage(defaultDefinition);

        public bool TryStartSalvage(SalvageDefinition definition, int? randomSeed = null)
        {
            if (!isActiveAndEnabled || definition == null || Session.IsActive) return false;
            return Session.TryStart(definition.CreateSettings(), definition.CreateTarget(), randomSeed);
        }

        // Parameterless methods are also convenient targets for future UnityEvents.
        public void StartSalvage() => TryStartSalvage();
        public void PressControl() => SetControlHeld(true);
        public void ReleaseControl() => SetControlHeld(false);
        public void SetControlHeld(bool held) => Session.SetControlHeld(held);
        public bool TryLockTarget() => Session.TryLockTarget();
        public void SetPaused(bool paused) => Session.SetPaused(paused);
        public void CancelSalvage() => Session.Cancel();

        private void Update() => Session.Tick(Time.deltaTime);

        private void OnDisable()
        {
            // Stop the attempt and release input when its terminal/object is disabled or destroyed.
            session?.Cancel();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) session?.SetControlHeld(false);
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused) session?.SetControlHeld(false);
        }

        private void OnDestroy()
        {
            if (session == null) return;
            session.SnapshotChanged -= ForwardSnapshot;
            session.Completed -= ForwardCompletion;
        }

        private void ForwardSnapshot(SalvageSnapshot snapshot) => SnapshotChanged?.Invoke(snapshot);
        private void ForwardCompletion(SalvageResult result) => Completed?.Invoke(result);
    }
}
