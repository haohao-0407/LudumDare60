using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LudumDare60.Landing
{
    /// <summary>One continuous cabin view, with an additive exterior and an opaque exterior-only handoff.</summary>
    [DefaultExecutionOrder(-11000)]
    public sealed class LandingSequenceController : MonoBehaviour
    {
        public enum LandingPhase { Idle, SpaceApproach, AtmosphericEntry, WaitingForTerrain, Descent, Touchdown, Landed, Cancelled, Failed }

        [Header("Existing SHIP objects")]
        public Transform ShipRig;
        public Camera ViewCamera;
        public Transform ShakePivot;
        public Light ShipSun;
        public Light CabinFill;
        public Volume SourceVolume;

        [Header("Destination and owned runtime materials")]
        public string DestinationScenePath = "Assets/MarsBase/Scenes/MarsBase_RetroFuture.unity";
        public Material TransitionSkyTemplate;
        public Material HazeMaterialTemplate;
        public Material DustMaterialTemplate;

        [Header("Sequence")]
        public bool AutoStart = true;
        [Min(5f)] public float Duration = 45f;
        [Min(10f)] public float ApproachHeight = 180f;
        public Vector3 ApproachOffset = new Vector3(0f, 0f, -240f);
        [Min(30f)] public float LoadTimeout = 120f;
        [Min(5f)] public float ExteriorHazeRadius = 25f;
        [Range(0f, 1f)] public float SpaceAmbientScale = .18f;
        [Range(0f, 1f)] public float SpaceSunIntensityMultiplier = .35f;
        public UnityEvent OnTouchdown = new UnityEvent();
        public UnityEvent OnLanded = new UnityEvent();

        public static LandingSequenceController Active { get; private set; }
        public LandingPhase Phase { get; private set; } = LandingPhase.Idle;
        public float Progress { get; private set; }
        public bool IsSequenceActive => Phase >= LandingPhase.SpaceApproach && Phase <= LandingPhase.Touchdown;
        public bool HasLanded => Phase == LandingPhase.Landed;
        public string FailureReason { get; private set; }

        const float HandoffProgress = .50f;
        const float TouchdownProgress = .93f;
        static readonly int AtmosphereBlendId = Shader.PropertyToID("_AtmosphereBlend");
        static readonly int StarVisibilityId = Shader.PropertyToID("_StarVisibility");
        static readonly int PlanetVisibilityId = Shader.PropertyToID("_PlanetVisibility");
        static readonly int PlanetAngularRadiusId = Shader.PropertyToID("_PlanetAngularRadius");
        static readonly int PlanetDirectionId = Shader.PropertyToID("_PlanetDirection");
        static readonly int EntryGlowId = Shader.PropertyToID("_EntryGlow");
        static readonly int SunDirectionId = Shader.PropertyToID("_SunDirection");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        Coroutine sequence;
        AsyncOperation pendingLoad;
        AsyncOperation pendingUnload;
        Scene sourceActiveScene;
        Scene destinationScene;
        MarsLandingEnvironment destination;
        bool ownsDestination;
        bool cancelled;
        bool captured;
        bool exteriorRevealed;
        bool touchdownInvoked;
        bool applicationQuitting;
        float loadStartedAt;
        float shakeClock;
        float sourceVolumeWeight;
        Vector3 originalRigPosition;
        Quaternion originalRigRotation;
        Vector3 originalShakePosition;
        Quaternion originalShakeRotation;
        CameraClearFlags originalClearFlags;
        float originalFarClip;
        RenderState originalRender;
        LightState originalSun;
        LightState originalCabin;
        Material skyMaterial;
        Material hazeMaterial;
        Material dustMaterial;
        GameObject effectsRoot;
        Transform hazeDome;
        ParticleSystem dust;
        Vector3 descentStart;
        Vector3 planetDirection;

        void Start()
        {
            if (AutoStart) BeginLanding();
        }

        /// <summary>Safe for terminal commands or future UI; returns false if already running or landed.</summary>
        public bool BeginLanding()
        {
            if (!isActiveAndEnabled || IsSequenceActive || HasLanded || (pendingLoad != null && !pendingLoad.isDone) || (pendingUnload != null && !pendingUnload.isDone)) return false;
            if (Active && Active != this) return false;
            if (!ShipRig || !ViewCamera || !ShakePivot || !ShipSun || !TransitionSkyTemplate || !HazeMaterialTemplate || !DustMaterialTemplate)
            {
                Fail("Landing bindings are incomplete. Rebuild the landing setup in the Unity Editor.");
                return false;
            }
            destinationScene = SceneManager.GetSceneByPath(DestinationScenePath);
            if ((!destinationScene.IsValid() || !destinationScene.isLoaded) && !Application.CanStreamedLevelBeLoaded(DestinationScenePath))
            {
                Fail("The Mars destination is absent from the active Build Profile scene list: " + DestinationScenePath);
                return false;
            }

            Active = this;
            cancelled = false;
            exteriorRevealed = false;
            touchdownInvoked = false;
            Progress = 0f;
            shakeClock = 0f;
            FailureReason = null;
            Phase = LandingPhase.SpaceApproach;
            CaptureSource();
            CreateEffects();
            ApplyFrame(0f);
            if (destinationScene.IsValid() && destinationScene.isLoaded)
            {
                ownsDestination = false;
                if (!BindDestination()) return false;
            }
            else
            {
                ownsDestination = true;
                loadStartedAt = Time.realtimeSinceStartup;
                try
                {
                    pendingLoad = SceneManager.LoadSceneAsync(DestinationScenePath, LoadSceneMode.Additive);
                    if (pendingLoad == null) throw new InvalidOperationException("Unity returned no scene loading operation.");
                    // Activation is never blocked: other scene loads/unloads must remain able to complete.
                    pendingLoad.completed += DestinationLoaded;
                }
                catch (Exception exception)
                {
                    Fail("Unable to load the Mars destination: " + exception.Message);
                    return false;
                }
            }
            sequence = StartCoroutine(PlaySequence());
            return true;
        }

        public bool WantsDestination(Scene scene)
        {
            return IsSequenceActive && !cancelled && scene.path == DestinationScenePath;
        }

        void DestinationLoaded(AsyncOperation operation)
        {
            operation.completed -= DestinationLoaded;
            pendingLoad = null;
            destinationScene = SceneManager.GetSceneByPath(DestinationScenePath);
            if (cancelled || !this || !isActiveAndEnabled)
            {
                // LoadSceneAsync cannot be cancelled. Release exactly the scene this sequence requested.
                if (ownsDestination && destinationScene.IsValid() && destinationScene.isLoaded)
                    pendingUnload = SceneManager.UnloadSceneAsync(destinationScene);
                return;
            }
            BindDestination();
        }

        bool BindDestination()
        {
            if (destinationScene.IsValid() && destinationScene.isLoaded)
            {
                foreach (GameObject root in destinationScene.GetRootGameObjects())
                {
                    destination = root.GetComponentInChildren<MarsLandingEnvironment>(true);
                    if (destination) break;
                }
            }
            if (!destination || !destination.LandingAnchor || !destination.ExteriorRoot || !destination.Sun || !destination.SkyboxMaterial)
            {
                Fail("MarsBase_RetroFuture is missing its configured MarsLandingEnvironment or LandingAnchor.");
                return false;
            }
            destination.PrepareForLanding();
            CopyPanorama(destination.SkyboxMaterial, skyMaterial);
            return true;
        }

        IEnumerator PlaySequence()
        {
            float elapsed = 0f;
            while (!cancelled && Progress < 1f)
            {
                if (pendingLoad != null && Time.realtimeSinceStartup - loadStartedAt > LoadTimeout)
                {
                    Fail("The Mars scene did not finish loading within " + LoadTimeout.ToString("0") + " seconds.");
                    yield break;
                }
                if (Time.deltaTime <= 0f)
                {
                    yield return null;
                    continue;
                }
                float next = Mathf.Min(1f, (elapsed + Time.deltaTime) / Mathf.Max(5f, Duration));
                if (!exteriorRevealed && next >= HandoffProgress)
                {
                    next = HandoffProgress;
                    // One fully covered frame precedes the relocation, including at low frame rates.
                    if (Progress < HandoffProgress)
                    {
                        elapsed = next * Mathf.Max(5f, Duration);
                        Progress = next;
                        ApplyFrame(next);
                        yield return null;
                        continue;
                    }
                    if (!destination)
                    {
                        Phase = LandingPhase.WaitingForTerrain;
                        ApplyFrame(next);
                        yield return null;
                        continue;
                    }
                    RevealDestination();
                }
                elapsed = next * Mathf.Max(5f, Duration);
                Progress = next;
                Phase = next < .18f ? LandingPhase.SpaceApproach : next < HandoffProgress ? LandingPhase.AtmosphericEntry : next < TouchdownProgress ? LandingPhase.Descent : LandingPhase.Touchdown;
                ApplyFrame(next);
                if (next >= TouchdownProgress && !touchdownInvoked)
                {
                    touchdownInvoked = true;
                    OnTouchdown.Invoke();
                }
                yield return null;
            }
            if (cancelled) yield break;
            FinishLanding();
        }

        void RevealDestination()
        {
            Transform anchor = destination.LandingAnchor;
            descentStart = anchor.position + Vector3.up * ApproachHeight + anchor.TransformDirection(ApproachOffset);
            ShipRig.SetPositionAndRotation(descentStart, anchor.rotation);
            destination.RevealExterior();
            exteriorRevealed = true;
            dust.transform.SetPositionAndRotation(anchor.position + Vector3.up * .3f, Quaternion.identity);
            dust.Play();
        }

        void ApplyFrame(float progress)
        {
            if (!skyMaterial) return;
            float atmosphere = SmoothRange(.15f, .69f, progress);
            float entry = Mathf.Sin(SmoothRange(.16f, .64f, progress) * Mathf.PI);
            float haze = SmoothRange(.31f, .47f, progress) * (1f - SmoothRange(.55f, .71f, progress));
            skyMaterial.SetFloat(AtmosphereBlendId, atmosphere);
            skyMaterial.SetFloat(StarVisibilityId, 1f - SmoothRange(.12f, .40f, progress));
            skyMaterial.SetFloat(PlanetVisibilityId, 1f - SmoothRange(.33f, .48f, progress));
            skyMaterial.SetFloat(PlanetAngularRadiusId, Mathf.Lerp(.28f, 1.05f, SmoothRange(0f, .48f, progress)));
            skyMaterial.SetVector(PlanetDirectionId, planetDirection);
            skyMaterial.SetFloat(EntryGlowId, entry * .65f);
            skyMaterial.SetVector(SunDirectionId, -ShipSun.transform.forward);
            RenderSettings.skybox = skyMaterial;
            Color hazeColor = destination ? destination.FogColor : new Color(.72f, .43f, .29f);
            hazeColor.a = haze;
            hazeMaterial.SetColor(ColorId, hazeColor);
            hazeDome.gameObject.SetActive(haze > .0001f);
            hazeDome.position = ViewCamera.transform.position;

            ApplyLighting(atmosphere);
            if (CabinFill)
            {
                CabinFill.enabled = true;
                CabinFill.color = Color.Lerp(new Color(1f, .26f, .07f), new Color(1f, .61f, .34f), atmosphere);
                CabinFill.intensity = entry * 1.25f + Mathf.Sin(atmosphere * Mathf.PI) * .18f;
            }
            if (exteriorRevealed)
            {
                float descent = SmoothRange(HandoffProgress, TouchdownProgress, progress);
                ShipRig.position = Vector3.Lerp(descentStart, destination.LandingAnchor.position, descent);
                dust.transform.position = destination.LandingAnchor.position + Vector3.up * .3f;
                float landingDust = SmoothRange(.75f, .91f, progress) * (1f - SmoothRange(.94f, 1f, progress));
                ParticleSystem.EmissionModule emission = dust.emission;
                emission.rateOverTime = landingDust * 42f;
            }
            ApplyShake(progress, entry);
            // The rig may have moved since the first dome update; follow the same rendered viewpoint.
            hazeDome.position = ViewCamera.transform.position;
        }

        void ApplyLighting(float blend)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(originalRender.AmbientSky * SpaceAmbientScale, destination ? destination.AmbientSkyColor : new Color(.55f, .48f, .42f), blend);
            RenderSettings.ambientEquatorColor = Color.Lerp(originalRender.AmbientEquator * SpaceAmbientScale, destination ? destination.AmbientEquatorColor : new Color(.43f, .34f, .27f), blend);
            RenderSettings.ambientGroundColor = Color.Lerp(originalRender.AmbientGround * SpaceAmbientScale, destination ? destination.AmbientGroundColor : new Color(.23f, .16f, .11f), blend);
            RenderSettings.ambientIntensity = Mathf.Lerp(originalRender.AmbientIntensity, destination ? destination.AmbientIntensity : 1f, blend);
            RenderSettings.sun = ShipSun;
            ShipSun.enabled = true;
            ShipSun.useColorTemperature = false;
            Color targetSun = destination ? EffectiveLightColor(destination.Sun) : new Color(1f, .72f, .51f);
            ShipSun.color = Color.Lerp(originalSun.EffectiveColor, targetSun, blend);
            ShipSun.intensity = Mathf.Lerp(originalSun.Intensity * SpaceSunIntensityMultiplier, destination ? destination.Sun.intensity : originalSun.Intensity, blend);
            if (destination)
            {
                ShipSun.transform.rotation = Quaternion.Slerp(originalSun.Rotation, destination.Sun.transform.rotation, blend);
                RenderSettings.fog = destination.FogEnabled && blend > .01f;
                RenderSettings.fogMode = destination.FogMode;
                RenderSettings.fogColor = destination.FogColor;
                RenderSettings.fogDensity = destination.FogDensity * blend;
                RenderSettings.fogStartDistance = Mathf.Lerp(10000f, destination.FogStartDistance, blend);
                RenderSettings.fogEndDistance = Mathf.Lerp(12000f, destination.FogEndDistance, blend);
                if (destination.GlobalVolume) destination.GlobalVolume.weight = destination.AuthoredVolumeWeight * blend;
                destination.SetSupplementalLightBlend(blend);
            }
            else RenderSettings.fog = false;
            if (SourceVolume) SourceVolume.weight = sourceVolumeWeight * (1f - blend);
        }

        void ApplyShake(float progress, float entry)
        {
            shakeClock += Time.deltaTime;
            float braking = SmoothRange(.76f, .87f, progress) * (1f - SmoothRange(.90f, TouchdownProgress, progress));
            float strength = .0015f * (1f - SmoothRange(.1f, .2f, progress)) + entry * .012f + braking * .006f;
            float x = (Mathf.PerlinNoise(13f, shakeClock * 13f) - .5f) * 2f;
            float y = (Mathf.PerlinNoise(39f, shakeClock * 17f) - .5f) * 2f;
            float settleSeconds = Mathf.Max(0f, (progress - TouchdownProgress) * Mathf.Max(5f, Duration));
            float bump = progress >= TouchdownProgress ? Mathf.Sin(settleSeconds * 17f) * Mathf.Exp(-settleSeconds * 4f) : 0f;
            ShakePivot.localPosition = originalShakePosition + new Vector3(x * strength, y * strength - bump * .055f, 0f);
            ShakePivot.localRotation = originalShakeRotation * Quaternion.Euler(y * strength * 15f + bump * .7f, 0f, x * strength * 10f);
        }

        void FinishLanding()
        {
            ShipRig.SetPositionAndRotation(destination.LandingAnchor.position, destination.LandingAnchor.rotation);
            ResetShake();
            destination.ApplyFinalEnvironment();
            ShipSun.enabled = false;
            if (CabinFill) CabinFill.intensity = 0f;
            if (SourceVolume) SourceVolume.weight = 0f;
            // SetActiveScene changes environment settings; explicitly apply the matching endpoint again.
            SceneManager.SetActiveScene(destinationScene);
            destination.ApplyFinalEnvironment();
            if (dust) dust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (hazeDome) hazeDome.gameObject.SetActive(false);
            Phase = LandingPhase.Landed;
            Progress = 1f;
            sequence = null;
            if (Active == this) Active = null;
            OnLanded.Invoke();
        }

        void CaptureSource()
        {
            sourceActiveScene = SceneManager.GetActiveScene();
            originalRender = RenderState.Capture();
            originalRigPosition = ShipRig.position;
            originalRigRotation = ShipRig.rotation;
            originalShakePosition = ShakePivot.localPosition;
            originalShakeRotation = ShakePivot.localRotation;
            originalSun = LightState.Capture(ShipSun);
            originalCabin = LightState.Capture(CabinFill);
            sourceVolumeWeight = SourceVolume ? SourceVolume.weight : 0f;
            originalClearFlags = ViewCamera.clearFlags;
            originalFarClip = ViewCamera.farClipPlane;
            ViewCamera.clearFlags = CameraClearFlags.Skybox;
            ViewCamera.farClipPlane = Mathf.Max(originalFarClip, 1500f);
            Vector4 authoredPlanet = TransitionSkyTemplate.HasProperty(PlanetDirectionId)
                ? TransitionSkyTemplate.GetVector(PlanetDirectionId) : Vector4.zero;
            planetDirection = new Vector3(authoredPlanet.x, authoredPlanet.y, authoredPlanet.z);
            planetDirection = planetDirection.sqrMagnitude > .0001f ? planetDirection.normalized : ViewCamera.transform.forward;
            captured = true;
        }

        void CreateEffects()
        {
            skyMaterial = new Material(TransitionSkyTemplate) { name = "Landing Sky (runtime)" };
            hazeMaterial = new Material(HazeMaterialTemplate) { name = "Landing Exterior Haze (runtime)" };
            dustMaterial = new Material(DustMaterialTemplate) { name = "Landing Dust (runtime)" };
            effectsRoot = new GameObject("Landing Effects (runtime)");
            SceneManager.MoveGameObjectToScene(effectsRoot, gameObject.scene);
            effectsRoot.transform.SetParent(transform, false);
            GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "Exterior Haze Dome";
            dome.transform.SetParent(effectsRoot.transform, false);
            dome.transform.localScale = Vector3.one * ExteriorHazeRadius * 2f;
            Collider collider = dome.GetComponent<Collider>();
            if (collider) { collider.enabled = false; Destroy(collider); }
            MeshRenderer renderer = dome.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = hazeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            hazeDome = dome.transform;

            GameObject dustObject = new GameObject("Landing Ground Dust");
            dustObject.transform.SetParent(effectsRoot.transform, false);
            dust = dustObject.AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = dust.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(.7f, .35f, .18f, .28f), new Color(.85f, .53f, .31f, .38f));
            main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = false;
            ParticleSystem.EmissionModule emission = dust.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 3.5f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            ParticleSystem.VelocityOverLifetimeModule velocity = dust.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.radial = new ParticleSystem.MinMaxCurve(3f, 7f);
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(.35f, .8f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            ParticleSystem.SizeOverLifetimeModule size = dust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .5f, 1f, 2f));
            Gradient fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .14f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule color = dust.colorOverLifetime;
            color.enabled = true;
            color.color = fade;
            ParticleSystemRenderer dustRenderer = dust.GetComponent<ParticleSystemRenderer>();
            dustRenderer.sharedMaterial = dustMaterial;
            dustRenderer.shadowCastingMode = ShadowCastingMode.Off;
            dustRenderer.receiveShadows = false;
            dustRenderer.sortMode = ParticleSystemSortMode.Distance;
        }

        public void CancelLanding()
        {
            if (HasLanded || (!IsSequenceActive && !captured)) return;
            Phase = LandingPhase.Cancelled;
            CancelAndRestore();
        }

        void Fail(string message)
        {
            if (Phase == LandingPhase.Failed) return;
            FailureReason = message;
            Phase = LandingPhase.Failed;
            Debug.LogError("[Landing] " + message, this);
            CancelAndRestore();
        }

        void CancelAndRestore()
        {
            cancelled = true;
            if (sequence != null) { StopCoroutine(sequence); sequence = null; }
            if (Active == this) Active = null;
            if (captured)
            {
                if (sourceActiveScene.IsValid() && sourceActiveScene.isLoaded) SceneManager.SetActiveScene(sourceActiveScene);
                originalRender.Apply();
                if (ShipRig) ShipRig.SetPositionAndRotation(originalRigPosition, originalRigRotation);
                ResetShake();
                originalSun.Apply(ShipSun);
                originalCabin.Apply(CabinFill);
                if (SourceVolume) SourceVolume.weight = sourceVolumeWeight;
                if (ViewCamera) { ViewCamera.clearFlags = originalClearFlags; ViewCamera.farClipPlane = originalFarClip; }
                captured = false;
            }
            if (destinationScene.IsValid() && destinationScene.isLoaded)
            {
                if (ownsDestination) pendingUnload = SceneManager.UnloadSceneAsync(destinationScene);
                else if (destination) destination.RestorePresentation();
            }
            destination = null;
            DestroyEffects();
        }

        void ResetShake()
        {
            if (!ShakePivot) return;
            ShakePivot.localPosition = originalShakePosition;
            ShakePivot.localRotation = originalShakeRotation;
        }

        void DestroyEffects()
        {
            if (effectsRoot) Destroy(effectsRoot);
            if (skyMaterial) Destroy(skyMaterial);
            if (hazeMaterial) Destroy(hazeMaterial);
            if (dustMaterial) Destroy(dustMaterial);
            effectsRoot = null;
            skyMaterial = null;
            hazeMaterial = null;
            dustMaterial = null;
        }

        void OnApplicationQuit() { applicationQuitting = true; }

        void OnDisable()
        {
            if (applicationQuitting) return;
            if (HasLanded) { ResetShake(); DestroyEffects(); }
            else CancelLanding();
            if (Active == this) Active = null;
        }

        static float SmoothRange(float start, float end, float value)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));
        }

        static Color EffectiveLightColor(Light light)
        {
            return light.useColorTemperature ? light.color * Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature) : light.color;
        }

        static void CopyPanorama(Material source, Material target)
        {
            if (source.HasProperty("_MainTex")) target.SetTexture("_MainTex", source.GetTexture("_MainTex"));
            foreach (string property in new[] { "_Tint", "_HazeColor" })
                if (source.HasProperty(property)) target.SetColor(property, source.GetColor(property));
            foreach (string property in new[] { "_Exposure", "_Rotation", "_HazeHeight", "_SeamWidth" })
                if (source.HasProperty(property)) target.SetFloat(property, source.GetFloat(property));
        }

        struct LightState
        {
            public bool Enabled;
            public bool UseTemperature;
            public float Temperature;
            public float Intensity;
            public Color Color;
            public Color EffectiveColor;
            public Quaternion Rotation;
            public static LightState Capture(Light light)
            {
                if (!light) return default;
                return new LightState { Enabled = light.enabled, UseTemperature = light.useColorTemperature, Temperature = light.colorTemperature,
                    Intensity = light.intensity, Color = light.color, EffectiveColor = EffectiveLightColor(light), Rotation = light.transform.rotation };
            }
            public void Apply(Light light)
            {
                if (!light) return;
                light.enabled = Enabled;
                light.useColorTemperature = UseTemperature;
                light.colorTemperature = Temperature;
                light.intensity = Intensity;
                light.color = Color;
                light.transform.rotation = Rotation;
            }
        }

        struct RenderState
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
            public static RenderState Capture()
            {
                return new RenderState { Sky = RenderSettings.skybox, Sun = RenderSettings.sun, Fog = RenderSettings.fog,
                    FogColor = RenderSettings.fogColor, FogMode = RenderSettings.fogMode, FogDensity = RenderSettings.fogDensity,
                    FogStart = RenderSettings.fogStartDistance, FogEnd = RenderSettings.fogEndDistance, AmbientMode = RenderSettings.ambientMode,
                    AmbientSky = RenderSettings.ambientSkyColor, AmbientEquator = RenderSettings.ambientEquatorColor,
                    AmbientGround = RenderSettings.ambientGroundColor, AmbientIntensity = RenderSettings.ambientIntensity };
            }
            public void Apply()
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
            }
        }
    }
}
