using UnityEngine;
using UnityEngine.Rendering;

namespace LudumDare60.Landing
{
    /// <summary>Destination scene bindings. A normal standalone scene keeps its authored presentation.</summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class MarsLandingEnvironment : MonoBehaviour
    {
        public Transform LandingAnchor;
        public GameObject ExteriorRoot;
        public GameObject DuplicateCockpit;
        public Camera[] PresentationCameras = new Camera[0];
        public AudioListener[] PresentationListeners = new AudioListener[0];
        public Light Sun;
        public Light[] SupplementalLights = new Light[0];
        public Volume GlobalVolume;
        public Material SkyboxMaterial;

        [Header("Authored destination environment")]
        public bool FogEnabled = true;
        public Color FogColor = new Color(.72f, .43f, .29f);
        public FogMode FogMode = FogMode.ExponentialSquared;
        public float FogDensity = .0022f;
        public float FogStartDistance;
        public float FogEndDistance = 300f;
        public AmbientMode AmbientMode = AmbientMode.Trilight;
        public Color AmbientSkyColor = new Color(.55f, .48f, .42f);
        public Color AmbientEquatorColor = new Color(.43f, .34f, .27f);
        public Color AmbientGroundColor = new Color(.23f, .16f, .11f);
        public float AmbientIntensity = 1f;

        bool prepared;
        bool exteriorWasActive;
        bool duplicateWasActive;
        bool sunWasEnabled;
        bool[] cameraStates;
        bool[] listenerStates;
        bool[] supplementalStates;
        float[] supplementalIntensities;
        float volumeWeight;

        public float AuthoredVolumeWeight => prepared ? volumeWeight : (GlobalVolume ? GlobalVolume.weight : 0f);

        void Awake()
        {
            LandingSequenceController sequence = LandingSequenceController.Active;
            if (sequence && sequence.WantsDestination(gameObject.scene))
                PrepareForLanding();
        }

        public void PrepareForLanding()
        {
            if (prepared) return;
            prepared = true;
            exteriorWasActive = ExteriorRoot && ExteriorRoot.activeSelf;
            duplicateWasActive = DuplicateCockpit && DuplicateCockpit.activeSelf;
            sunWasEnabled = Sun && Sun.enabled;
            volumeWeight = GlobalVolume ? GlobalVolume.weight : 0f;
            cameraStates = new bool[PresentationCameras.Length];
            listenerStates = new bool[PresentationListeners.Length];
            supplementalStates = new bool[SupplementalLights.Length];
            supplementalIntensities = new float[SupplementalLights.Length];
            for (int i = 0; i < PresentationCameras.Length; i++)
            {
                if (!PresentationCameras[i]) continue;
                cameraStates[i] = PresentationCameras[i].enabled;
                PresentationCameras[i].enabled = false;
            }
            for (int i = 0; i < PresentationListeners.Length; i++)
            {
                if (!PresentationListeners[i]) continue;
                listenerStates[i] = PresentationListeners[i].enabled;
                PresentationListeners[i].enabled = false;
            }
            for (int i = 0; i < SupplementalLights.Length; i++)
            {
                Light light = SupplementalLights[i];
                if (!light || light == Sun) continue;
                supplementalStates[i] = light.enabled;
                supplementalIntensities[i] = light.intensity;
                light.enabled = false;
            }
            if (DuplicateCockpit) DuplicateCockpit.SetActive(false);
            if (Sun) Sun.enabled = false;
            if (GlobalVolume) GlobalVolume.weight = 0f;
            if (ExteriorRoot) ExteriorRoot.SetActive(false);
        }

        public void RevealExterior()
        {
            if (ExteriorRoot) ExteriorRoot.SetActive(true);
            // Keep duplicate cameras and cockpit disabled after their parent becomes active.
            if (DuplicateCockpit) DuplicateCockpit.SetActive(false);
            SetSupplementalLightBlend(0f);
        }

        public void SetSupplementalLightBlend(float blend)
        {
            if (!prepared) return;
            for (int i = 0; i < SupplementalLights.Length; i++)
            {
                Light light = SupplementalLights[i];
                if (!light || light == Sun) continue;
                light.enabled = supplementalStates[i];
                light.intensity = supplementalIntensities[i] * blend;
            }
        }

        public void ApplyFinalEnvironment()
        {
            RenderSettings.skybox = SkyboxMaterial;
            RenderSettings.fog = FogEnabled;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogMode = FogMode;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.fogStartDistance = FogStartDistance;
            RenderSettings.fogEndDistance = FogEndDistance;
            RenderSettings.ambientMode = AmbientMode;
            RenderSettings.ambientSkyColor = AmbientSkyColor;
            RenderSettings.ambientEquatorColor = AmbientEquatorColor;
            RenderSettings.ambientGroundColor = AmbientGroundColor;
            RenderSettings.ambientIntensity = AmbientIntensity;
            RenderSettings.sun = Sun;
            if (Sun) Sun.enabled = !prepared || sunWasEnabled;
            if (GlobalVolume) GlobalVolume.weight = AuthoredVolumeWeight;
            SetSupplementalLightBlend(1f);
        }

        /// <summary>Used only if a previously loaded scene was borrowed and the sequence is cancelled.</summary>
        public void RestorePresentation()
        {
            if (!prepared) return;
            for (int i = 0; i < PresentationCameras.Length; i++)
                if (PresentationCameras[i]) PresentationCameras[i].enabled = cameraStates[i];
            for (int i = 0; i < PresentationListeners.Length; i++)
                if (PresentationListeners[i]) PresentationListeners[i].enabled = listenerStates[i];
            SetSupplementalLightBlend(1f);
            if (Sun) Sun.enabled = sunWasEnabled;
            if (GlobalVolume) GlobalVolume.weight = volumeWeight;
            if (DuplicateCockpit) DuplicateCockpit.SetActive(duplicateWasActive);
            if (ExteriorRoot) ExteriorRoot.SetActive(exteriorWasActive);
            prepared = false;
        }
    }
}
