using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace LudumDare60.Landing
{
    /// <summary>
    /// Owns the damage presentation added to the cabin. It never changes the authored cabin lights
    /// or the global AudioListener, and can be stopped or repaired independently of the landing rig.
    /// </summary>
    public sealed class HardLandingDamageEffects : MonoBehaviour
    {
        [Serializable]
        public sealed class DamageSite
        {
            [Tooltip("Authoring anchor. The installer places this site's effects beneath it.")]
            public Transform Transform;
            public VisualEffect Sparks;
            public Light Flash;
            public AudioSource ElectricalAudio;
        }

        [Header("Landing and owned damage objects")]
        public LandingSequenceController Sequence;
        public DamageSite[] Sites = Array.Empty<DamageSite>();
        public AudioSource ImpactAudio;
        public AudioSource HullAudio;
        public AudioSource DamagedAmbience;
        public AudioSource AlarmAudio;
        public AudioClip[] ElectricalClips = Array.Empty<AudioClip>();

        [Header("Scaled-time choreography")]
        public Vector2 HullDelayRange = new Vector2(.1f, .3f);
        [Min(0f)] public float FirstSiteDelay = .2f;
        [Min(0f)] public float SiteStagger = .3f;
        public Vector2 SparkBurstDuration = new Vector2(.18f, .36f);
        public Vector2 RepeatInterval = new Vector2(2.5f, 7.5f);
        public Vector2 ElectricalPulseInterval = new Vector2(.035f, .09f);
        [Min(0f)] public float AmbienceDelay = .6f;
        [Min(0f)] public float AlarmDelay = .55f;
        [Min(0f)] public float AlarmDuration = 6f;
        [Min(.1f)] public float AlarmPulseInterval = .8f;

        public bool IsDamaged { get; private set; }

        sealed class SiteState
        {
            public DamageSite Site;
            public bool FlashWasEnabled;
            public float FlashIntensity;
            public float NextBurst;
            public float BurstEnds;
            public float NextPulse;
            public float FlashEnds;
            public bool Bursting;
        }

        struct AudioState
        {
            public AudioSource Source;
            public float Pitch;
        }

        readonly List<AudioState> audioStates = new List<AudioState>();
        readonly HashSet<AudioSource> pausedAudio = new HashSet<AudioSource>();
        SiteState[] siteStates = Array.Empty<SiteState>();
        LandingSequenceController subscribedSequence;
        LandingSequenceController.LandingPhase lastPhase;
        bool bindingsCaptured;
        bool hadSequence;
        bool touchdownConsumed;
        bool effectsRunning;
        bool paused;
        bool impactStarted;
        bool hullStarted;
        bool ambienceStarted;
        bool alarmStopped;
        float elapsed;
        float hullDelay;
        float nextAlarmPulse;

        void Awake()
        {
            CaptureBindings();
            // The installer also sets initialEventName to OnStop so an enabled VFX component cannot
            // emit before this Awake. Explicit Play below is the only start event this script sends.
            StopDamageEffects();
        }

        void OnEnable()
        {
            CaptureBindings();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
            StopDamageEffects();
        }

        void Subscribe()
        {
            if (subscribedSequence == Sequence) return;
            Unsubscribe();
            subscribedSequence = Sequence;
            if (!subscribedSequence) return;
            hadSequence = true;
            lastPhase = subscribedSequence.Phase;
            subscribedSequence.OnTouchdown.AddListener(HandleTouchdown);
        }

        void Unsubscribe()
        {
            if (subscribedSequence)
                subscribedSequence.OnTouchdown.RemoveListener(HandleTouchdown);
            subscribedSequence = null;
        }

        void Update()
        {
            if (subscribedSequence != Sequence)
            {
                RepairAll();
                touchdownConsumed = false;
                Subscribe();
            }

            if (Sequence)
            {
                LandingSequenceController.LandingPhase phase = Sequence.Phase;
                bool reset = phase == LandingSequenceController.LandingPhase.Cancelled ||
                             phase == LandingSequenceController.LandingPhase.Failed ||
                             phase == LandingSequenceController.LandingPhase.Idle ||
                             (phase == LandingSequenceController.LandingPhase.SpaceApproach && phase != lastPhase);
                if (reset)
                {
                    if (IsDamaged || effectsRunning) RepairAll();
                    touchdownConsumed = false;
                }
                lastPhase = phase;
            }
            else if (hadSequence && (IsDamaged || effectsRunning))
            {
                RepairAll();
                touchdownConsumed = false;
            }

            ApplyPause(Time.timeScale <= 0f);
            if (!effectsRunning || paused) return;

            elapsed += Time.deltaTime;
            if (!impactStarted)
            {
                impactStarted = true;
                Play(ImpactAudio);
            }
            if (!hullStarted && elapsed >= hullDelay)
            {
                hullStarted = true;
                Play(HullAudio);
            }
            if (!ambienceStarted && elapsed >= Mathf.Max(0f, AmbienceDelay))
            {
                ambienceStarted = true;
                Play(DamagedAmbience);
            }
            UpdateAlarm();

            for (int i = 0; i < siteStates.Length; i++)
            {
                SiteState state = siteStates[i];
                if (state.Site == null) continue;
                if (!state.Bursting && elapsed >= state.NextBurst)
                    BeginBurst(state);
                if (!state.Bursting) continue;

                if (elapsed >= state.BurstEnds)
                {
                    EndBurst(state);
                    continue;
                }

                if (elapsed >= state.NextPulse)
                    Pulse(state);
                else if (elapsed >= state.FlashEnds)
                    RestoreFlash(state);
            }
        }

        void HandleTouchdown()
        {
            if (touchdownConsumed) return;
            touchdownConsumed = true;
            TriggerDamage();
        }

        /// <summary>Starts the presentation once. RepairAll permits a deliberate manual replay.</summary>
        public void TriggerDamage()
        {
            if (!isActiveAndEnabled || IsDamaged || effectsRunning) return;
            CaptureBindings();
            StopDamageEffects();
            IsDamaged = true;
            effectsRunning = true;
            elapsed = 0f;
            hullDelay = Sample(HullDelayRange, 0f);
            impactStarted = false;
            hullStarted = false;
            ambienceStarted = false;
            alarmStopped = false;
            nextAlarmPulse = Mathf.Max(0f, AlarmDelay);

            for (int i = 0; i < siteStates.Length; i++)
            {
                SiteState state = siteStates[i];
                state.NextBurst = Mathf.Max(0f, FirstSiteDelay) + i * Mathf.Max(0f, SiteStagger);
                state.Bursting = false;
                if (state.Site != null && state.Site.Sparks)
                    state.Site.Sparks.pause = Time.timeScale <= 0f;
            }

            paused = false;
            ApplyPause(Time.timeScale <= 0f);
            if (!paused)
            {
                impactStarted = true;
                Play(ImpactAudio);
            }
        }

        void BeginBurst(SiteState state)
        {
            state.Bursting = true;
            state.BurstEnds = elapsed + Sample(SparkBurstDuration, .04f);
            if (state.Site.Sparks)
            {
                state.Site.Sparks.pause = false;
                state.Site.Sparks.Play();
            }
            Pulse(state);
        }

        void Pulse(SiteState state)
        {
            // Schedule from now rather than catching up missed frames: a frame hitch must not stack
            // many electrical one-shots. The flash and electrical crackle share this pulse clock.
            float interval = Sample(ElectricalPulseInterval, .02f);
            state.NextPulse = elapsed + interval;
            state.FlashEnds = elapsed + Mathf.Min(interval * .65f, .045f);
            if (state.Site.Flash)
            {
                state.Site.Flash.enabled = true;
                state.Site.Flash.intensity = Mathf.Max(.01f, state.FlashIntensity) * UnityEngine.Random.Range(.65f, 1.25f);
            }

            AudioSource source = state.Site.ElectricalAudio;
            if (!source || !source.isActiveAndEnabled) return;
            AudioClip clip = PickElectricalClip(source.clip);
            if (!clip) return;
            source.pitch = OriginalPitch(source) * UnityEngine.Random.Range(.92f, 1.08f);
            source.PlayOneShot(clip);
        }

        void EndBurst(SiteState state)
        {
            state.Bursting = false;
            if (state.Site.Sparks) state.Site.Sparks.Stop();
            RestoreFlash(state);
            state.NextBurst = elapsed + Sample(RepeatInterval, .1f);
        }

        void UpdateAlarm()
        {
            if (!AlarmAudio || alarmStopped) return;
            float starts = Mathf.Max(0f, AlarmDelay);
            float ends = starts + Mathf.Max(0f, AlarmDuration);
            if (elapsed >= ends)
            {
                AlarmAudio.Stop();
                alarmStopped = true;
            }
            else if (elapsed >= nextAlarmPulse && AlarmAudio.clip && AlarmAudio.isActiveAndEnabled)
            {
                AlarmAudio.PlayOneShot(AlarmAudio.clip);
                nextAlarmPulse = elapsed + Mathf.Max(.1f, AlarmPulseInterval);
            }
        }

        void ApplyPause(bool shouldPause)
        {
            if (paused == shouldPause) return;
            paused = shouldPause;
            for (int i = 0; i < siteStates.Length; i++)
            {
                DamageSite site = siteStates[i].Site;
                if (site != null && site.Sparks)
                    site.Sparks.pause = shouldPause || !effectsRunning;
            }

            if (shouldPause)
            {
                for (int i = 0; i < audioStates.Count; i++)
                {
                    AudioSource source = audioStates[i].Source;
                    if (!source || !source.isPlaying) continue;
                    source.Pause();
                    pausedAudio.Add(source);
                }
            }
            else
            {
                foreach (AudioSource source in pausedAudio)
                    if (source) source.UnPause();
                pausedAudio.Clear();
            }
        }

        /// <summary>Stops all owned presentation and restores flash lights; damage state is retained.</summary>
        public void StopDamageEffects()
        {
            CaptureBindings();
            effectsRunning = false;
            for (int i = 0; i < siteStates.Length; i++)
            {
                SiteState state = siteStates[i];
                state.Bursting = false;
                if (state.Site != null && state.Site.Sparks)
                {
                    // Reinit clears residual live particles on cancellation/repair. The authored
                    // initial event is OnStop; Stop is repeated for safely configured other graphs.
                    state.Site.Sparks.Reinit();
                    state.Site.Sparks.Stop();
                    state.Site.Sparks.pause = true;
                }
                RestoreFlash(state);
            }
            for (int i = 0; i < audioStates.Count; i++)
            {
                AudioState state = audioStates[i];
                if (!state.Source) continue;
                state.Source.Stop();
                state.Source.pitch = state.Pitch;
            }
            pausedAudio.Clear();
            paused = false;
        }

        /// <summary>Integration point for a future repair system. A consumed touchdown stays consumed.</summary>
        public void RepairAll()
        {
            StopDamageEffects();
            IsDamaged = false;
        }

        void CaptureBindings()
        {
            if (bindingsCaptured) return;
            bindingsCaptured = true;
            int count = Sites == null ? 0 : Sites.Length;
            siteStates = new SiteState[count];
            for (int i = 0; i < count; i++)
            {
                DamageSite site = Sites[i];
                Light flash = site == null ? null : site.Flash;
                siteStates[i] = new SiteState
                {
                    Site = site,
                    FlashWasEnabled = flash && flash.enabled,
                    FlashIntensity = flash ? flash.intensity : 0f
                };
                if (site != null) AddAudio(site.ElectricalAudio);
            }
            AddAudio(ImpactAudio);
            AddAudio(HullAudio);
            AddAudio(DamagedAmbience);
            AddAudio(AlarmAudio);
        }

        void AddAudio(AudioSource source)
        {
            if (!source) return;
            for (int i = 0; i < audioStates.Count; i++)
                if (audioStates[i].Source == source) return;
            audioStates.Add(new AudioState { Source = source, Pitch = source.pitch });
        }

        float OriginalPitch(AudioSource source)
        {
            for (int i = 0; i < audioStates.Count; i++)
                if (audioStates[i].Source == source) return audioStates[i].Pitch;
            return 1f;
        }

        static void RestoreFlash(SiteState state)
        {
            if (state.Site == null || !state.Site.Flash) return;
            state.Site.Flash.intensity = state.FlashIntensity;
            state.Site.Flash.enabled = state.FlashWasEnabled;
        }

        static void Play(AudioSource source)
        {
            if (source && source.clip && source.isActiveAndEnabled) source.Play();
        }

        AudioClip PickElectricalClip(AudioClip fallback)
        {
            if (ElectricalClips == null || ElectricalClips.Length == 0) return fallback;
            int start = UnityEngine.Random.Range(0, ElectricalClips.Length);
            for (int offset = 0; offset < ElectricalClips.Length; offset++)
            {
                AudioClip clip = ElectricalClips[(start + offset) % ElectricalClips.Length];
                if (clip) return clip;
            }
            return fallback;
        }

        static float Sample(Vector2 range, float minimum)
        {
            float low = Mathf.Max(minimum, Mathf.Min(range.x, range.y));
            float high = Mathf.Max(low, Mathf.Max(range.x, range.y));
            return UnityEngine.Random.Range(low, high);
        }
    }
}
