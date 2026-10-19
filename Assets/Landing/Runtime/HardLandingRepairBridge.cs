using System;
using LudumDare60.ShipAssembly;
using LudumDare60.UI;
using UnityEngine;

namespace LudumDare60.Landing
{
    /// <summary>Projects the existing repair system's state onto the owned landing damage effects.</summary>
    [DisallowMultipleComponent]
    public sealed class HardLandingRepairBridge : MonoBehaviour
    {
        public HardLandingDamageEffects Effects;
        public RepairScreenController RepairScreen;
        [Tooltip("Effects.Sites index -> authoritative repair device. Unmapped sites retain their current state.")]
        public RepairDeviceId[] SiteDevices =
        {
            RepairDeviceId.PowerUnit,
            RepairDeviceId.Navigation,
            RepairDeviceId.LifeSupport
        };

        RepairScreenController subscribedScreen;
        HardLandingDamageEffects boundEffects;
        RepairDeviceId[] boundMappingSource;
        RepairDeviceId[] boundSiteDevices = Array.Empty<RepairDeviceId>();
        bool started;
        bool bindingsDirty = true;
        bool warnedMissingBinding;
        bool warnedMapping;

        void OnEnable()
        {
            RefreshBindings();
        }

        void Start()
        {
            // RepairScreen.Awake may reset serialized preview states. Read only after every Awake.
            started = true;
            RefreshBindings();
        }

        void OnDisable()
        {
            Unsubscribe();
            boundEffects = null;
            bindingsDirty = true;
        }

        void OnValidate()
        {
            bindingsDirty = true;
        }

        void Update()
        {
            // Only detect binding edits here. Repair states are read on subscription or state events.
            if (BindingsChanged()) RefreshBindings();
        }

        /// <summary>Rebinds explicit references and immediately replays their authoritative snapshot.</summary>
        public void RefreshBindings()
        {
            if (!isActiveAndEnabled) return;
            if (subscribedScreen != RepairScreen)
            {
                Unsubscribe();
                subscribedScreen = RepairScreen;
                if (subscribedScreen) subscribedScreen.StateChanged += HandleStateChanged;
            }
            boundEffects = Effects;
            boundMappingSource = SiteDevices;
            boundSiteDevices = SiteDevices == null ? Array.Empty<RepairDeviceId>() : (RepairDeviceId[])SiteDevices.Clone();
            bindingsDirty = false;
            if (started) SynchronizePresentation();
        }

        void Unsubscribe()
        {
            if (subscribedScreen) subscribedScreen.StateChanged -= HandleStateChanged;
            subscribedScreen = null;
        }

        bool BindingsChanged()
        {
            return bindingsDirty || subscribedScreen != RepairScreen || boundEffects != Effects || boundMappingSource != SiteDevices;
        }

        void HandleStateChanged(int deviceIndex, RepairDeviceState state)
        {
            if (!started || !isActiveAndEnabled) return;
            if (BindingsChanged()) RefreshBindings();
            else SynchronizePresentation();
        }

        void SynchronizePresentation()
        {
            if (!boundEffects || !subscribedScreen)
            {
                if (!warnedMissingBinding)
                {
                    warnedMissingBinding = true;
                    Debug.LogWarning("[Landing Repair] Assign Effects and RepairScreen explicitly. Existing damage presentation is unchanged.", this);
                }
                return;
            }

            int siteCount = boundEffects.Sites == null ? 0 : boundEffects.Sites.Length;
            if (boundSiteDevices.Length != siteCount) WarnMappingOnce();
            int mappedCount = Mathf.Min(boundSiteDevices.Length, siteCount);
            for (int site = 0; site < mappedCount; site++)
            {
                int device = (int)boundSiteDevices[site];
                // GetState clamps invalid indices, so reject invalid enum values instead of repairing the wrong site.
                if (device < 0 || device >= RepairScreenController.DeviceCount)
                {
                    WarnMappingOnce();
                    continue;
                }
                boundEffects.SetSiteRepaired(site, subscribedScreen.GetState(device) == RepairDeviceState.Repaired);
            }

            // Three visible spark sites do not represent all four systems: the engine also gates healthy ambience.
            boundEffects.SetAllRepairsComplete(subscribedScreen.AllRepaired);
        }

        void WarnMappingOnce()
        {
            if (warnedMapping) return;
            warnedMapping = true;
            Debug.LogWarning("[Landing Repair] SiteDevices must map each damage site to a valid repair device. Invalid or unmapped sites retain their current state.", this);
        }
    }
}
