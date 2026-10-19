using System;
using System.Collections;
using System.Collections.Generic;
using LudumDare60.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LudumDare60.Landing
{
    /// <summary>A repaired cabin launches from its landed pose; only owned effects and explicit environment bindings are changed.</summary>
    [DisallowMultipleComponent]
    public sealed class LaunchSequenceController : MonoBehaviour
    {
        public enum LaunchPhase { Idle, Countdown, Ignition, Liftoff, Ascent, AtmosphericExit, Cruise, Cancelled, Failed }

        public LandingSequenceController Landing;
        public RepairScreenController RepairScreen;
        public AudioClip CountdownClip;
        public AudioClip IgnitionClip;
        public AudioClip EngineLoopClip;

        [Header("Scaled-time launch")]
        [Min(10f)] public float Duration = 40f;
        [Min(0f)] public float CountdownDuration = 3f;
        [Min(.1f)] public float IgnitionDuration = 2f;
        [Min(100f)] public float AscentHeight = 1400f;
        [Min(0f)] public float ForwardDistance = 300f;
        [Range(-30f, 30f)] public float PitchDegrees = -12f;
        [Tooltip("Direction of the receding planet in the camera's local view at launch; X is right, Y is up, Z is forward.")]
        public Vector3 PlanetLocalDirection = new Vector3(.45f, -.25f, 1f);
        public Vector3 AccelerationShakePosition = new Vector3(.008f, .025f, .012f);
        public Vector3 AccelerationShakeRotation = new Vector3(.35f, .2f, .45f);

        [Header("Owned launch effects")]
        public Vector3 EngineLocalPosition = new Vector3(0f, -.3f, 0f);
        [Min(0)] public int LiftoffDustBurstCount = 220;
        [Min(0f)] public float GroundDustEmission = 110f;
        [Range(0f, 1f)] public float CountdownVolume = .45f;
        [Range(0f, 1f)] public float IgnitionVolume = .7f;
        [Range(0f, 1f)] public float EngineVolume = .4f;
        [Range(0f, 1f)] public float SpaceReflectionIntensity = .25f;
        public UnityEvent OnIgnition = new UnityEvent();
        public UnityEvent OnLiftoff = new UnityEvent();
        public UnityEvent OnOrbitReached = new UnityEvent();
        public UnityEvent OnLaunchCancelled = new UnityEvent();

        public LaunchPhase Phase { get; private set; } = LaunchPhase.Idle;
        public bool IsLaunching => Phase >= LaunchPhase.Countdown && Phase <= LaunchPhase.AtmosphericExit;
        public bool HasReachedOrbit => Phase == LaunchPhase.Cruise;
        public string FailureReason { get; private set; }
        public float Progress { get; private set; }
        public float CountdownRemaining => Phase == LaunchPhase.Countdown ? Mathf.Max(0f, countdownSeconds - elapsed) : 0f;
        public float PhaseElapsed => Mathf.Max(0f, elapsed - phaseStartedAt);

        const float ExteriorHandoff = .60f;
        static LaunchSequenceController activeOwner;
        Coroutine routine;
        MarsLandingEnvironment environment;
        Transform rig;
        Camera viewCamera;
        Transform shakePivot;
        Light shipSun;
        Light marsSun;
        Light cabinFill;
        Volume shipVolume;
        Volume marsVolume;
        GameObject exteriorRoot;
        Scene groundActiveScene;
        Scene shipScene;
        Vector3 groundPosition;
        Quaternion groundRotation;
        Vector3 shakePosition;
        Quaternion shakeRotation;
        Vector3 planetDirection;
        bool exteriorWasActive;
        bool captured;
        bool exteriorHidden;
        bool coveredFrameRendered;
        bool ignitionInvoked;
        bool liftoffInvoked;
        bool audioPaused;
        bool applicationQuitting;
        float elapsed;
        float phaseStartedAt;
        float totalSeconds;
        float countdownSeconds;
        float ignitionSeconds;
        float flightSeconds;
        float shipVolumeWeight;
        float marsVolumeWeight;
        int countdownTick = int.MinValue;
        RenderSnapshot groundRender;
        LightSnapshot shipSunState;
        LightSnapshot marsSunState;
        LightSnapshot cabinFillState;
        LightSnapshot[] supplementalStates = Array.Empty<LightSnapshot>();
        Material skyMaterial;
        Material hazeMaterial;
        Material dustMaterial;
        Material flameMaterial;
        GameObject effectsRoot;
        Transform hazeDome;
        ParticleSystem groundDust;
        ParticleSystem engineFlame;
        AudioSource countdownAudio;
        AudioSource ignitionAudio;
        AudioSource engineAudio;
        readonly List<AudioSource> pausedAudio = new List<AudioSource>();

        public bool BeginLaunch()
        {
            if (!isActiveAndEnabled || IsLaunching || HasReachedOrbit) return false;
            if (activeOwner && activeOwner != this) return Reject("Another launch already owns the flight presentation.");
            if (!Landing || !Landing.HasLanded) return Reject("The ship must finish landing before launch.");
            if (!RepairScreen || !RepairScreen.AllRepaired) return Reject("All four ship systems must be repaired before launch.");
            environment = Landing.DestinationEnvironment;
            if (!environment || !environment.ExteriorRoot || !environment.ExteriorRoot.activeInHierarchy || !environment.Sun || !environment.SkyboxMaterial ||
                !Landing.ShipRig || !Landing.ViewCamera || !Landing.ShakePivot || !Landing.ShipSun ||
                !Landing.TransitionSkyTemplate || !Landing.HazeMaterialTemplate || !Landing.DustMaterialTemplate)
                return Reject("The landed ship or its launch environment bindings are incomplete.");
            if (Landing.ShipSun == environment.Sun)
                return Reject("The ship and Mars environment require separate directional light bindings.");

            FailureReason = null;
            elapsed = 0f;
            phaseStartedAt = 0f;
            Progress = 0f;
            countdownSeconds = Mathf.Max(0f, CountdownDuration);
            ignitionSeconds = Mathf.Max(.1f, IgnitionDuration);
            totalSeconds = Mathf.Max(Duration, countdownSeconds + ignitionSeconds + 8f);
            flightSeconds = totalSeconds - countdownSeconds - ignitionSeconds;
            countdownTick = int.MinValue;
            exteriorHidden = coveredFrameRendered = ignitionInvoked = liftoffInvoked = false;
            activeOwner = this;
            Phase = LaunchPhase.Countdown;
            try
            {
                CaptureGroundState();
                CreateEffects();
                ApplyFrame();
            }
            catch (Exception exception)
            {
                Abort(true, "Unable to prepare launch: " + exception.Message, false);
                return false;
            }
            routine = StartCoroutine(PlayLaunch());
            return true;
        }

        bool Reject(string reason)
        {
            FailureReason = reason;
            return false;
        }

        IEnumerator PlayLaunch()
        {
            while (IsLaunching && elapsed < totalSeconds)
            {
                ApplyAudioPause(Time.timeScale <= 0f);
                if (Time.deltaTime <= 0f) { yield return null; continue; }
                if (!Landing || !rig || !viewCamera || !shakePivot || !environment || !exteriorRoot || !shipSun || !marsSun)
                {
                    Abort(true, "A required launch object became unavailable during ascent.", true);
                    yield break;
                }

                float next = Mathf.Min(totalSeconds, elapsed + Time.deltaTime);
                float flight = Mathf.Clamp01((next - countdownSeconds - ignitionSeconds) / flightSeconds);
                if (!exteriorHidden && flight >= ExteriorHandoff)
                {
                    if (!coveredFrameRendered)
                    {
                        // Hold one fully opaque exterior frame before changing the loaded terrain's visibility.
                        elapsed = countdownSeconds + ignitionSeconds + ExteriorHandoff * flightSeconds;
                        UpdatePhaseAndCues();
                        if (!IsLaunching) yield break;
                        ApplyFrame();
                        coveredFrameRendered = true;
                        yield return null;
                        continue;
                    }
                    exteriorRoot.SetActive(false);
                    exteriorHidden = true;
                }

                elapsed = next;
                UpdatePhaseAndCues();
                if (!IsLaunching) yield break;
                ApplyFrame();
                if (elapsed >= totalSeconds)
                {
                    CompleteLaunch();
                    yield break;
                }
                yield return null;
            }
            if (IsLaunching) CompleteLaunch();
        }

        void UpdatePhaseAndCues()
        {
            float flight = FlightProgress;
            if (elapsed < countdownSeconds)
            {
                SetPhase(LaunchPhase.Countdown);
                int remaining = Mathf.CeilToInt(countdownSeconds - elapsed);
                if (remaining != countdownTick)
                {
                    countdownTick = remaining;
                    if (CountdownClip) countdownAudio.PlayOneShot(CountdownClip, CountdownVolume);
                }
                return;
            }
            if (!ignitionInvoked)
            {
                ignitionInvoked = true;
                SetPhase(LaunchPhase.Ignition);
                if (IgnitionClip) ignitionAudio.PlayOneShot(IgnitionClip, IgnitionVolume);
                if (EngineLoopClip) { engineAudio.clip = EngineLoopClip; engineAudio.Play(); }
                engineFlame.Play();
                groundDust.Play();
                OnIgnition.Invoke();
                if (!IsLaunching) return;
            }
            if (elapsed < countdownSeconds + ignitionSeconds) return;
            if (!liftoffInvoked)
            {
                liftoffInvoked = true;
                SetPhase(LaunchPhase.Liftoff);
                if (LiftoffDustBurstCount > 0) groundDust.Emit(LiftoffDustBurstCount);
                OnLiftoff.Invoke();
                if (!IsLaunching) return;
            }
            if (flight >= .43f) SetPhase(LaunchPhase.AtmosphericExit);
            else if (elapsed >= countdownSeconds + ignitionSeconds + 1.5f) SetPhase(LaunchPhase.Ascent);
        }

        void SetPhase(LaunchPhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            phaseStartedAt = elapsed;
        }

        float FlightProgress => Mathf.Clamp01((elapsed - countdownSeconds - ignitionSeconds) / flightSeconds);

        void ApplyFrame()
        {
            Progress = Mathf.Clamp01(elapsed / totalSeconds);
            float flight = FlightProgress;
            float ignition = Smooth(countdownSeconds, countdownSeconds + ignitionSeconds, elapsed);
            float thrust = ignition * (1f - Smooth(.86f, 1f, flight));
            float space = Smooth(.28f, .93f, flight);
            float height = flight * flight * (2f - flight);
            rig.position = groundPosition + Vector3.up * (AscentHeight * height) + groundRotation * Vector3.forward * (ForwardDistance * flight * flight);
            float pitch = PitchDegrees * Smooth(.02f, .42f, flight);
            rig.rotation = groundRotation * Quaternion.Euler(pitch, 0f, 0f);

            skyMaterial.SetFloat("_AtmosphereBlend", 1f - space);
            skyMaterial.SetFloat("_StarVisibility", Smooth(.45f, .90f, flight));
            skyMaterial.SetFloat("_PlanetVisibility", Smooth(.60f, .80f, flight));
            skyMaterial.SetFloat("_PlanetAngularRadius", Mathf.Lerp(1.1f, .22f, Smooth(.60f, 1f, flight)));
            skyMaterial.SetVector("_PlanetDirection", planetDirection);
            skyMaterial.SetFloat("_EntryGlow", Mathf.Sin(space * Mathf.PI) * .08f);
            RenderSettings.skybox = skyMaterial;

            float haze = Smooth(.42f, .56f, flight) * (1f - Smooth(.67f, .79f, flight));
            Color hazeColor = Color.Lerp(groundRender.FogColor, new Color(.18f, .12f, .10f), space);
            hazeColor.a = haze;
            hazeMaterial.SetColor("_Color", hazeColor);
            hazeDome.gameObject.SetActive(haze > .0001f);
            ApplyEnvironment(space);

            if (cabinFill)
            {
                cabinFill.enabled = true;
                cabinFill.color = Color.Lerp(new Color(.55f, .75f, 1f), new Color(1f, .65f, .32f), flight);
                float flash = ignitionInvoked ? 1f - Smooth(countdownSeconds + .03f, countdownSeconds + .5f, elapsed) : 0f;
                cabinFill.intensity = flash * 1.8f + thrust * (.22f + .12f * Mathf.Abs(Mathf.Sin(elapsed * 23f)));
            }

            float vibration = thrust * Mathf.Lerp(1f, .35f, Smooth(.4f, .9f, flight));
            Vector3 noise = new Vector3(Noise(13f, elapsed * 17f), Noise(37f, elapsed * 21f), Noise(73f, elapsed * 14f));
            shakePivot.localPosition = shakePosition + Vector3.Scale(AccelerationShakePosition, noise) * vibration;
            shakePivot.localRotation = shakeRotation * Quaternion.Euler(Vector3.Scale(AccelerationShakeRotation, noise) * vibration);
            hazeDome.position = viewCamera.transform.position;

            groundDust.transform.position = groundPosition + Vector3.up * .3f;
            ParticleSystem.EmissionModule dustEmission = groundDust.emission;
            dustEmission.rateOverTime = GroundDustEmission * ignition * (1f - Smooth(.02f, .20f, flight));
            engineFlame.transform.SetPositionAndRotation(rig.TransformPoint(EngineLocalPosition), rig.rotation * Quaternion.Euler(90f, 0f, 0f));
            ParticleSystem.EmissionModule flameEmission = engineFlame.emission;
            flameEmission.rateOverTime = thrust * 145f;
            engineAudio.volume = EngineVolume * thrust;
        }

        void ApplyEnvironment(float space)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(groundRender.AmbientSky, Landing.SpaceAmbientSkyColor, space);
            RenderSettings.ambientEquatorColor = Color.Lerp(groundRender.AmbientEquator, Landing.SpaceAmbientEquatorColor, space);
            RenderSettings.ambientGroundColor = Color.Lerp(groundRender.AmbientGround, Landing.SpaceAmbientGroundColor, space);
            RenderSettings.ambientIntensity = Mathf.Lerp(groundRender.AmbientIntensity, Landing.SpaceAmbientIntensity, space);
            RenderSettings.reflectionIntensity = Mathf.Lerp(groundRender.ReflectionIntensity, SpaceReflectionIntensity, space);
            RenderSettings.fog = groundRender.Fog && space < .9999f;
            RenderSettings.fogMode = groundRender.FogMode;
            RenderSettings.fogColor = groundRender.FogColor;
            RenderSettings.fogDensity = groundRender.FogDensity * (1f - space);
            RenderSettings.fogStartDistance = Mathf.Lerp(groundRender.FogStart, 10000f, space);
            RenderSettings.fogEndDistance = Mathf.Lerp(groundRender.FogEnd, 12000f, space);
            if (shipVolume) shipVolume.weight = Mathf.Lerp(shipVolumeWeight, Landing.SpaceVolumeWeight, space);
            if (marsVolume) marsVolume.weight = marsVolumeWeight * (1f - space);
            foreach (LightSnapshot supplemental in supplementalStates)
                if (supplemental.Light) supplemental.Light.intensity = supplemental.Intensity * (1f - space);

            marsSun.enabled = !exteriorHidden && marsSunState.Enabled;
            shipSun.enabled = exteriorHidden;
            Light sun = exteriorHidden ? shipSun : marsSun;
            sun.useColorTemperature = false;
            sun.color = Color.Lerp(marsSunState.EffectiveColor, Landing.SpaceSunColor, space);
            sun.intensity = Mathf.Lerp(marsSunState.Intensity, Landing.SpaceSunIntensity, space);
            sun.transform.rotation = Quaternion.Slerp(marsSunState.Rotation, Landing.SpaceSunRotation, space);
            RenderSettings.sun = sun;
            skyMaterial.SetVector("_SunDirection", -sun.transform.forward);
        }

        void CompleteLaunch()
        {
            elapsed = totalSeconds;
            // Active-scene lighting changes first; the final space snapshot is applied afterwards.
            if (shipScene.IsValid() && shipScene.isLoaded) SceneManager.SetActiveScene(shipScene);
            ApplyFrame();
            ResetShake();
            cabinFillState.Restore();
            groundDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            engineFlame.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            hazeDome.gameObject.SetActive(false);
            StopAudio();
            routine = null;
            SetPhase(LaunchPhase.Cruise);
            OnOrbitReached.Invoke();
        }

        public void CancelLaunch()
        {
            if (!IsLaunching) return;
            Abort(false, null, true);
        }

        void Abort(bool failed, string reason, bool notify)
        {
            bool wasLaunching = IsLaunching;
            if (routine != null) { StopCoroutine(routine); routine = null; }
            FailureReason = reason;
            RestoreGroundState();
            DestroyEffects();
            Phase = failed ? LaunchPhase.Failed : LaunchPhase.Cancelled;
            Progress = 0f;
            if (activeOwner == this) activeOwner = null;
            if (failed) Debug.LogError("[Launch] " + reason, this);
            if (notify && wasLaunching) OnLaunchCancelled.Invoke();
        }

        void CaptureGroundState()
        {
            rig = Landing.ShipRig;
            viewCamera = Landing.ViewCamera;
            shakePivot = Landing.ShakePivot;
            shipSun = Landing.ShipSun;
            marsSun = environment.Sun;
            cabinFill = Landing.CabinFill;
            shipVolume = Landing.SourceVolume;
            marsVolume = environment.GlobalVolume;
            exteriorRoot = environment.ExteriorRoot;
            groundActiveScene = SceneManager.GetActiveScene();
            shipScene = Landing.gameObject.scene;
            groundPosition = rig.position;
            groundRotation = rig.rotation;
            shakePosition = shakePivot.localPosition;
            shakeRotation = shakePivot.localRotation;
            exteriorWasActive = exteriorRoot.activeSelf;
            shipVolumeWeight = shipVolume ? shipVolume.weight : 0f;
            marsVolumeWeight = marsVolume ? marsVolume.weight : 0f;
            groundRender = RenderSnapshot.Capture();
            shipSunState = LightSnapshot.Capture(shipSun);
            marsSunState = LightSnapshot.Capture(marsSun);
            cabinFillState = LightSnapshot.Capture(cabinFill);
            Light[] supplemental = environment.SupplementalLights ?? Array.Empty<Light>();
            supplementalStates = new LightSnapshot[supplemental.Length];
            for (int i = 0; i < supplemental.Length; i++) supplementalStates[i] = LightSnapshot.Capture(supplemental[i]);
            planetDirection = viewCamera.transform.TransformDirection(PlanetLocalDirection.sqrMagnitude > .0001f ? PlanetLocalDirection.normalized : Vector3.forward);
            captured = true;
        }

        void RestoreGroundState()
        {
            if (!captured) return;
            if (groundActiveScene.IsValid() && groundActiveScene.isLoaded) SceneManager.SetActiveScene(groundActiveScene);
            if (rig) rig.SetPositionAndRotation(groundPosition, groundRotation);
            ResetShake();
            if (exteriorRoot) exteriorRoot.SetActive(exteriorWasActive);
            shipSunState.Restore();
            marsSunState.Restore();
            cabinFillState.Restore();
            foreach (LightSnapshot supplemental in supplementalStates) supplemental.Restore();
            if (shipVolume) shipVolume.weight = shipVolumeWeight;
            if (marsVolume) marsVolume.weight = marsVolumeWeight;
            groundRender.Restore();
            captured = false;
        }

        void ResetShake()
        {
            if (!shakePivot) return;
            shakePivot.localPosition = shakePosition;
            shakePivot.localRotation = shakeRotation;
        }

        void CreateEffects()
        {
            skyMaterial = new Material(Landing.TransitionSkyTemplate) { name = "Launch Sky (runtime)" };
            CopyPanorama(environment.SkyboxMaterial, skyMaterial);
            hazeMaterial = new Material(Landing.HazeMaterialTemplate) { name = "Launch Exterior Haze (runtime)" };
            dustMaterial = new Material(Landing.DustMaterialTemplate) { name = "Launch Dust (runtime)" };
            flameMaterial = new Material(Landing.DustMaterialTemplate) { name = "Launch Flame (runtime)" };
            flameMaterial.SetColor("_Color", new Color(3f, 1.8f, .75f, 1f));
            effectsRoot = new GameObject("Launch Effects (runtime)");
            SceneManager.MoveGameObjectToScene(effectsRoot, shipScene);
            effectsRoot.transform.SetParent(transform, false);

            GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "Launch Exterior Haze";
            dome.transform.SetParent(effectsRoot.transform, false);
            dome.transform.localScale = Vector3.one * Mathf.Max(5f, Landing.ExteriorHazeRadius) * 2f;
            Collider collider = dome.GetComponent<Collider>();
            if (collider) { collider.enabled = false; Destroy(collider); }
            MeshRenderer renderer = dome.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = hazeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            hazeDome = dome.transform;
            groundDust = CreateParticles("Launch Ground Dust", dustMaterial, false);
            engineFlame = CreateParticles("Engine Exhaust", flameMaterial, true);
            countdownAudio = CreateAudio("Countdown", false);
            ignitionAudio = CreateAudio("Engine Ignition", false);
            engineAudio = CreateAudio("Ascent Engine Loop", true);
            engineAudio.volume = 0f;
        }

        ParticleSystem CreateParticles(string name, Material material, bool flame)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(effectsRoot.transform, false);
            ParticleSystem particles = child.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = false;
            main.startLifetime = flame ? new ParticleSystem.MinMaxCurve(.22f, .45f) : new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = flame ? new ParticleSystem.MinMaxCurve(15f, 22f) : new ParticleSystem.MinMaxCurve(0f);
            main.startSize = flame ? new ParticleSystem.MinMaxCurve(.8f, 1.3f) : new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = flame ? new Color(1f, .9f, .65f, .8f) : new Color(.78f, .43f, .24f, .4f);
            main.maxParticles = flame ? 350 : Mathf.Max(850, LiftoffDustBurstCount + 600);
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = flame ? ParticleSystemShapeType.Cone : ParticleSystemShapeType.Circle;
            shape.radius = flame ? .45f : 5.5f;
            if (flame) shape.angle = 10f;
            else
            {
                shape.rotation = new Vector3(90f, 0f, 0f);
                ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                velocity.radial = new ParticleSystem.MinMaxCurve(6f, 13f);
                velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                velocity.y = new ParticleSystem.MinMaxCurve(.8f, 1.8f);
                velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            }
            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .4f, 1f, flame ? 1.3f : 2.2f));
            Gradient fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(flame ? new Color(1f, .25f, .05f) : Color.white, 1f) },
                new[] { new GradientAlphaKey(flame ? .8f : 0f, 0f), new GradientAlphaKey(1f, .05f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = fade;
            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            if (flame)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 1.6f;
                renderer.velocityScale = .07f;
            }
            return particles;
        }

        AudioSource CreateAudio(string name, bool loop)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(effectsRoot.transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.volume = 1f;
            source.dopplerLevel = 0f;
            return source;
        }

        void ApplyAudioPause(bool shouldPause)
        {
            if (audioPaused == shouldPause) return;
            audioPaused = shouldPause;
            if (shouldPause)
            {
                PauseAudio(countdownAudio);
                PauseAudio(ignitionAudio);
                PauseAudio(engineAudio);
            }
            else
            {
                foreach (AudioSource source in pausedAudio) if (source) source.UnPause();
                pausedAudio.Clear();
            }
        }

        void PauseAudio(AudioSource source)
        {
            if (!source || !source.isPlaying) return;
            source.Pause();
            pausedAudio.Add(source);
        }

        void StopAudio()
        {
            if (countdownAudio) countdownAudio.Stop();
            if (ignitionAudio) ignitionAudio.Stop();
            if (engineAudio) engineAudio.Stop();
            pausedAudio.Clear();
            audioPaused = false;
        }

        void DestroyEffects()
        {
            StopAudio();
            if (effectsRoot) { effectsRoot.SetActive(false); Destroy(effectsRoot); }
            if (skyMaterial) Destroy(skyMaterial);
            if (hazeMaterial) Destroy(hazeMaterial);
            if (dustMaterial) Destroy(dustMaterial);
            if (flameMaterial) Destroy(flameMaterial);
            effectsRoot = null;
            skyMaterial = null;
            hazeMaterial = null;
            dustMaterial = null;
            flameMaterial = null;
        }

        void OnApplicationQuit() { applicationQuitting = true; }

        void OnDisable()
        {
            if (!applicationQuitting && IsLaunching) CancelLaunch();
            // A completed cruise owns its sky until destruction. Disabling it does not destroy the active sky.
        }

        void OnDestroy()
        {
            if (!applicationQuitting && captured && (IsLaunching || RenderSettings.skybox == skyMaterial)) RestoreGroundState();
            DestroyEffects();
            if (activeOwner == this) activeOwner = null;
        }

        static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
        static float Noise(float seed, float time) => (Mathf.PerlinNoise(seed, time) - .5f) * 2f;

        static void CopyPanorama(Material source, Material target)
        {
            if (source.HasProperty("_MainTex")) target.SetTexture("_MainTex", source.GetTexture("_MainTex"));
            foreach (string property in new[] { "_Tint", "_HazeColor" })
                if (source.HasProperty(property)) target.SetColor(property, source.GetColor(property));
            foreach (string property in new[] { "_Exposure", "_Rotation", "_HazeHeight", "_SeamWidth" })
                if (source.HasProperty(property)) target.SetFloat(property, source.GetFloat(property));
        }

        struct LightSnapshot
        {
            public Light Light;
            public bool Enabled;
            public bool UseTemperature;
            public float Temperature;
            public float Intensity;
            public Color Color;
            public Color EffectiveColor;
            public Quaternion Rotation;
            public static LightSnapshot Capture(Light light)
            {
                if (!light) return default;
                return new LightSnapshot { Light = light, Enabled = light.enabled, UseTemperature = light.useColorTemperature,
                    Temperature = light.colorTemperature, Intensity = light.intensity, Color = light.color, Rotation = light.transform.rotation,
                    EffectiveColor = light.useColorTemperature ? light.color * Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature) : light.color };
            }
            public void Restore()
            {
                if (!Light) return;
                Light.enabled = Enabled;
                Light.useColorTemperature = UseTemperature;
                Light.colorTemperature = Temperature;
                Light.intensity = Intensity;
                Light.color = Color;
                Light.transform.rotation = Rotation;
            }
        }

        struct RenderSnapshot
        {
            public Material Sky;
            public Light Sun;
            public bool Fog;
            public Color FogColor;
            public FogMode FogMode;
            public float FogDensity;
            public float FogStart;
            public float FogEnd;
            public AmbientMode AmbientMode;
            public Color AmbientSky;
            public Color AmbientEquator;
            public Color AmbientGround;
            public float AmbientIntensity;
            public float ReflectionIntensity;
            public static RenderSnapshot Capture()
            {
                return new RenderSnapshot { Sky = RenderSettings.skybox, Sun = RenderSettings.sun, Fog = RenderSettings.fog,
                    FogColor = RenderSettings.fogColor, FogMode = RenderSettings.fogMode, FogDensity = RenderSettings.fogDensity,
                    FogStart = RenderSettings.fogStartDistance, FogEnd = RenderSettings.fogEndDistance, AmbientMode = RenderSettings.ambientMode,
                    AmbientSky = RenderSettings.ambientSkyColor, AmbientEquator = RenderSettings.ambientEquatorColor,
                    AmbientGround = RenderSettings.ambientGroundColor, AmbientIntensity = RenderSettings.ambientIntensity,
                    ReflectionIntensity = RenderSettings.reflectionIntensity };
            }
            public void Restore()
            {
                RenderSettings.skybox = Sky;
                RenderSettings.sun = Sun;
                RenderSettings.fog = Fog;
                RenderSettings.fogColor = FogColor;
                RenderSettings.fogMode = FogMode;
                RenderSettings.fogDensity = FogDensity;
                RenderSettings.fogStartDistance = FogStart;
                RenderSettings.fogEndDistance = FogEnd;
                RenderSettings.ambientMode = AmbientMode;
                RenderSettings.ambientSkyColor = AmbientSky;
                RenderSettings.ambientEquatorColor = AmbientEquator;
                RenderSettings.ambientGroundColor = AmbientGround;
                RenderSettings.ambientIntensity = AmbientIntensity;
                RenderSettings.reflectionIntensity = ReflectionIntensity;
            }
        }
    }
}
