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
        public AudioSource HealthyAmbience;
        public AudioSource AlarmAudio;
        public AudioClip[] ElectricalClips = Array.Empty<AudioClip>();
        [Min(0f)] public float AmbienceCrossfadeDuration = 1.2f;

        [Header("Scaled-time choreography")]
        public Vector2 HullDelayRange = new Vector2(.1f, .3f);
        [Min(0f)] public float FirstSiteDelay = .2f;
        [Min(0f)] public float SiteStagger = .3f;
        public Vector2 SparkBurstDuration = new Vector2(.18f, .36f);
        public Vector2 RepeatInterval = new Vector2(2.5f, 7.5f);
        public Vector2 ElectricalPulseInterval = new Vector2(.035f, .09f);

        [Header("电流声疲劳（听久了太刺耳，随时间变轻变疏）")]
        [Tooltip("受损开始后多久内保持原始电流声（秒）")]
        [Min(0f)] public float ElectricalFatigueDelay = 30f;
        [Tooltip("衰减过程时长（秒）")]
        [Min(0.01f)] public float ElectricalFatigueFade = 10f;
        [Tooltip("稳定后单次脉冲的音量倍率。0.35 ≈ 比原来轻 -9dB")]
        [Range(.05f, 1f)] public float ElectricalFatigueGain = .35f;
        [Tooltip("稳定后脉冲与爆闪之间的间隔倍率。1.8 = 稀疏一半多")]
        [Range(1f, 4f)] public float ElectricalFatigueIntervalScale = 1.8f;
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
            public bool Repaired;
        }

        struct AudioState
        {
            public AudioSource Source;
            public float Pitch;
            public float Volume;
        }

        readonly List<AudioState> audioStates = new List<AudioState>();
        readonly HashSet<AudioSource> pausedAudio = new HashSet<AudioSource>();
        SiteState[] siteStates = Array.Empty<SiteState>();
        LandingSequenceController subscribedSequence;
        LandingSequenceController.LandingPhase lastPhase;
        bool bindingsCaptured;
        bool hadSequence;
        bool touchdownConsumed;
        bool damageTriggered;
        bool allRepairsComplete;
        bool effectsRunning;
        bool operationalAmbience;
        bool resumeOperationalAmbience;
        bool paused;
        bool impactStarted;
        bool hullStarted;
        bool ambienceStarted;
        bool alarmStopped;
        bool ambienceFading;
        float elapsed;
        float hullDelay;
        float nextAlarmPulse;
        float ambienceFadeElapsed;
        float damagedFadeStart;
        float healthyFadeStart;
        float damagedFadeTarget;
        float healthyFadeTarget;

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
            if (resumeOperationalAmbience && !damageTriggered)
            {
                resumeOperationalAmbience = false;
                StartOperationalAmbience();
                return;
            }
            if (!damageTriggered) return;
            if (Sequence && (Sequence.Phase == LandingSequenceController.LandingPhase.Cancelled ||
                             Sequence.Phase == LandingSequenceController.LandingPhase.Failed ||
                             Sequence.Phase == LandingSequenceController.LandingPhase.Idle))
            {
                ResetPresentation();
                return;
            }

            // Re-enabling resumes only persistent damage/ambience, never the collision cues.
            effectsRunning = true;
            IsDamaged = !allRepairsComplete;
            impactStarted = true;
            hullStarted = true;
            CrossfadeAmbience(allRepairsComplete);
            ApplyPause(Time.timeScale <= 0f);
        }

        void OnDisable()
        {
            Unsubscribe();
            bool wasOperational = operationalAmbience;
            StopDamageEffects();
            resumeOperationalAmbience = wasOperational;
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
                ResetPresentation();
                Subscribe();
            }

            if (Sequence)
            {
                LandingSequenceController.LandingPhase phase = Sequence.Phase;
                bool reset = !operationalAmbience && (phase == LandingSequenceController.LandingPhase.Cancelled ||
                             phase == LandingSequenceController.LandingPhase.Failed ||
                             phase == LandingSequenceController.LandingPhase.Idle ||
                             (phase == LandingSequenceController.LandingPhase.SpaceApproach && phase != lastPhase));
                if (reset)
                {
                    if (damageTriggered || IsDamaged || effectsRunning) ResetPresentation();
                    touchdownConsumed = false;
                }
                lastPhase = phase;
            }
            else if (hadSequence && (damageTriggered || IsDamaged || effectsRunning))
            {
                ResetPresentation();
            }

            ApplyPause(Time.timeScale <= 0f);
            if (!effectsRunning || paused) return;
            if (operationalAmbience)
            {
                UpdateAmbience();
                return;
            }

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
                CrossfadeAmbience(allRepairsComplete);
            UpdateAmbience();
            if (!IsDamaged) return;
            UpdateAlarm();

            for (int i = 0; i < siteStates.Length; i++)
            {
                SiteState state = siteStates[i];
                if (state.Site == null || state.Repaired) continue;
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

        /// <summary>Runs the normal cabin bed during the opening without any collision cues.</summary>
        public void StartOperationalAmbience()
        {
            if (!isActiveAndEnabled || damageTriggered) return;
            StopDamageEffects();
            operationalAmbience = true;
            effectsRunning = true;
            CrossfadeAmbience(true);
            ApplyPause(Time.timeScale <= 0f);
        }

        /// <summary>Starts this landing once, preserving repair state already supplied by the caller.</summary>
        public void TriggerDamage()
        {
            if (!isActiveAndEnabled || damageTriggered) return;
            CaptureBindings();
            StopDamageEffects();
            damageTriggered = true;
            IsDamaged = !allRepairsComplete;
            effectsRunning = true;
            elapsed = 0f;
            hullDelay = Sample(HullDelayRange, 0f);
            impactStarted = false;
            hullStarted = false;
            ambienceStarted = false;
            alarmStopped = allRepairsComplete;
            nextAlarmPulse = Mathf.Max(0f, AlarmDelay);

            for (int i = 0; i < siteStates.Length; i++)
            {
                SiteState state = siteStates[i];
                state.NextBurst = Mathf.Max(0f, FirstSiteDelay) + i * Mathf.Max(0f, SiteStagger);
                state.Bursting = false;
                if (state.Site != null && state.Site.Sparks)
                    state.Site.Sparks.pause = Time.timeScale <= 0f || state.Repaired || allRepairsComplete;
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
            // 疲劳：受损后越久，电流声越轻、越稀疏，避免长时间滋滋扎耳朵
            float fatigue = ElectricalFatigue01;

            // Schedule from now rather than catching up missed frames: a frame hitch must not stack
            // many electrical one-shots. The flash and electrical crackle share this pulse clock.
            float interval = Sample(ElectricalPulseInterval, .02f) * Mathf.Lerp(1f, FatigueIntervalScale, fatigue);
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
            source.volume = OriginalVolume(source) * Mathf.Lerp(1f, FatigueGain, fatigue);
            source.PlayOneShot(clip);
        }

        void EndBurst(SiteState state)
        {
            state.Bursting = false;
            if (state.Site.Sparks) state.Site.Sparks.Stop();
            RestoreFlash(state);
            // 疲劳后两次爆发之间也拉长，整体从"持续滋滋"退成"偶尔跳一下"
            state.NextBurst = elapsed + Sample(RepeatInterval, .1f) * Mathf.Lerp(1f, FatigueIntervalScale, ElectricalFatigue01);
        }

        /// <summary>0 = 原始电流声，1 = 完全进入疲劳状态。smoothstep 过渡，中途不会有突变。</summary>
        float ElectricalFatigue01
        {
            get
            {
                if (ElectricalFatigueFade <= 0f) return elapsed >= ElectricalFatigueDelay ? 1f : 0f;
                float t = Mathf.Clamp01((elapsed - ElectricalFatigueDelay) / ElectricalFatigueFade);
                return t * t * (3f - 2f * t);
            }
        }

        // 兜底：Inspector 里被误填成 0 时，间隔倍率 0 会让每帧都触发脉冲
        float FatigueGain => Mathf.Clamp(ElectricalFatigueGain, .05f, 1f);
        float FatigueIntervalScale => Mathf.Max(1f, ElectricalFatigueIntervalScale);

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
                SiteState state = siteStates[i];
                DamageSite site = state.Site;
                if (site != null && site.Sparks)
                    site.Sparks.pause = shouldPause || !effectsRunning || !IsDamaged || state.Repaired;
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
            operationalAmbience = false;
            resumeOperationalAmbience = false;
            ambienceFading = false;
            ambienceStarted = false;
            for (int i = 0; i < siteStates.Length; i++)
                ClearSite(siteStates[i]);
            for (int i = 0; i < audioStates.Count; i++)
            {
                AudioState state = audioStates[i];
                if (!state.Source) continue;
                state.Source.Stop();
                state.Source.pitch = state.Pitch;
                state.Source.volume = state.Volume;
            }
            pausedAudio.Clear();
            paused = false;
        }

        /// <summary>Immediately clears a repaired site. Before touchdown this only caches repair state.</summary>
        public void SetSiteRepaired(int siteIndex, bool repaired)
        {
            CaptureBindings();
            if (siteIndex < 0 || siteIndex >= siteStates.Length) return;
            SiteState state = siteStates[siteIndex];
            if (state.Repaired == repaired) return;
            state.Repaired = repaired;
            if (repaired)
            {
                ClearSite(state);
            }
            else if (damageTriggered && !allRepairsComplete && isActiveAndEnabled)
            {
                bool wasRunning = effectsRunning;
                effectsRunning = true;
                IsDamaged = true;
                state.NextBurst = elapsed + Sample(RepeatInterval, .1f);
                if (!wasRunning) CrossfadeAmbience(false);
            }
        }

        public bool IsSiteRepaired(int siteIndex)
        {
            CaptureBindings();
            return siteIndex >= 0 && siteIndex < siteStates.Length && siteStates[siteIndex].Repaired;
        }

        /// <summary>
        /// Global repair result, including devices without a spark site. Site masks remain authoritative
        /// and independent, so an unrepaired site can resume if the global completion is revoked.
        /// </summary>
        public void SetAllRepairsComplete(bool complete)
        {
            CaptureBindings();
            if (allRepairsComplete == complete) return;
            allRepairsComplete = complete;
            IsDamaged = damageTriggered && !complete;
            if (complete)
            {
                for (int i = 0; i < siteStates.Length; i++) ClearSite(siteStates[i]);
                StopOwnedAudio(AlarmAudio);
                alarmStopped = true;
            }

            if (!damageTriggered || !isActiveAndEnabled) return;
            effectsRunning = true;
            if (!complete)
            {
                // Restore periodic faults without repeating the one-time collision choreography.
                for (int i = 0; i < siteStates.Length; i++)
                    if (!siteStates[i].Repaired)
                        siteStates[i].NextBurst = elapsed + Sample(RepeatInterval, .1f);
                alarmStopped = elapsed >= Mathf.Max(0f, AlarmDelay) + Mathf.Max(0f, AlarmDuration);
                nextAlarmPulse = Mathf.Max(elapsed, AlarmDelay);
            }
            CrossfadeAmbience(complete);
        }

        /// <summary>Manual full repair: clear every site and restore healthy ambience after touchdown.</summary>
        public void RepairAll()
        {
            CaptureBindings();
            for (int i = 0; i < siteStates.Length; i++) SetSiteRepaired(i, true);
            SetAllRepairsComplete(true);
            // A deliberate manual repair can restore healthy ambience after an explicit full stop;
            // repeated state synchronization through SetAllRepairsComplete remains side-effect free.
            if (damageTriggered && isActiveAndEnabled && !effectsRunning)
            {
                effectsRunning = true;
                CrossfadeAmbience(true);
            }
        }

        void ResetPresentation()
        {
            StopDamageEffects();
            IsDamaged = false;
            damageTriggered = false;
            touchdownConsumed = false;
            // Repair masks are owned by the repair system and may already have been synchronized
            // for the next landing. A cancellation is not a successful repair or an ambience cue.
        }

        void ClearSite(SiteState state)
        {
            state.Bursting = false;
            if (state.Site != null)
            {
                if (state.Site.Sparks)
                {
                    state.Site.Sparks.Reinit();
                    state.Site.Sparks.Stop();
                    state.Site.Sparks.pause = true;
                }
                StopOwnedAudio(state.Site.ElectricalAudio);
            }
            RestoreFlash(state);
        }

        void StopOwnedAudio(AudioSource source)
        {
            if (!source) return;
            source.Stop();
            source.pitch = OriginalPitch(source);
            source.volume = OriginalVolume(source);   // 疲劳时改过音量，停下来要还原
            pausedAudio.Remove(source);
        }

        void CrossfadeAmbience(bool healthy)
        {
            ambienceStarted = true;
            damagedFadeStart = PlayingVolume(DamagedAmbience);
            healthyFadeStart = PlayingVolume(HealthyAmbience);
            damagedFadeTarget = healthy ? 0f : OriginalVolume(DamagedAmbience);
            healthyFadeTarget = healthy ? OriginalVolume(HealthyAmbience) : 0f;
            if (DamagedAmbience) DamagedAmbience.volume = damagedFadeStart;
            if (HealthyAmbience) HealthyAmbience.volume = healthyFadeStart;
            StartAmbience(healthy ? HealthyAmbience : DamagedAmbience);
            ambienceFadeElapsed = 0f;
            ambienceFading = true;
            if (AmbienceCrossfadeDuration <= 0f) UpdateAmbience();
        }

        void StartAmbience(AudioSource source)
        {
            if (!source || !source.clip || !source.isActiveAndEnabled || source.isPlaying || pausedAudio.Contains(source)) return;
            source.Play();
            // Repair UI can send completion while the game is paused. Never let its newly started
            // ambience escape the same per-source pause used by an already-playing ambience.
            if (Time.timeScale <= 0f)
            {
                source.Pause();
                pausedAudio.Add(source);
            }
        }

        float PlayingVolume(AudioSource source)
        {
            return source && (source.isPlaying || pausedAudio.Contains(source)) ? source.volume : 0f;
        }

        void UpdateAmbience()
        {
            if (!ambienceFading) return;
            ambienceFadeElapsed += Time.deltaTime;
            float t = AmbienceCrossfadeDuration <= 0f ? 1f : Mathf.Clamp01(ambienceFadeElapsed / AmbienceCrossfadeDuration);
            if (DamagedAmbience) DamagedAmbience.volume = Mathf.Lerp(damagedFadeStart, damagedFadeTarget, t);
            if (HealthyAmbience) HealthyAmbience.volume = Mathf.Lerp(healthyFadeStart, healthyFadeTarget, t);
            if (t < 1f) return;
            ambienceFading = false;
            if (damagedFadeTarget <= 0f) StopOwnedAudio(DamagedAmbience);
            if (healthyFadeTarget <= 0f) StopOwnedAudio(HealthyAmbience);
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
            AddAudio(HealthyAmbience);
            AddAudio(AlarmAudio);
        }

        void AddAudio(AudioSource source)
        {
            if (!source) return;
            for (int i = 0; i < audioStates.Count; i++)
                if (audioStates[i].Source == source) return;
            audioStates.Add(new AudioState { Source = source, Pitch = source.pitch, Volume = source.volume });
        }

        float OriginalPitch(AudioSource source)
        {
            for (int i = 0; i < audioStates.Count; i++)
                if (audioStates[i].Source == source) return audioStates[i].Pitch;
            return 1f;
        }

        float OriginalVolume(AudioSource source)
        {
            if (!source) return 0f;
            for (int i = 0; i < audioStates.Count; i++)
                if (audioStates[i].Source == source) return audioStates[i].Volume;
            return source.volume;
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
