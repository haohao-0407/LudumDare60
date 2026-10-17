using UnityEngine;

namespace LudumDare60.MarsCabin
{
    /// <summary>Editable screen surface; accepts static textures or a future UI/camera RenderTexture.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CabinScreenPanel : MonoBehaviour
    {
        [SerializeField] string stableId;
        [SerializeField] Renderer surface;
        [SerializeField] Texture displayTexture;
        [SerializeField, ColorUsage(false, true)] Color emissionTint = new Color(.15f, 1, .36f, 1);
        [SerializeField, Min(0)] float brightness = 1.8f;
        [SerializeField] bool powered = true;
        MaterialPropertyBlock properties;

        public string StableId => stableId;
        public Texture DisplayTexture => displayTexture;
        public bool Powered => powered;

        public void Configure(string id, Renderer renderer, Texture texture, float intensity = 1.8f)
        {
            stableId = id;
            surface = renderer;
            displayTexture = texture;
            brightness = Mathf.Max(0, intensity);
            Apply();
        }

        public void SetTexture(Texture texture) { displayTexture = texture; Apply(); }
        public void SetPowered(bool value) { powered = value; Apply(); }
        public void SetBrightness(float value) { brightness = Mathf.Max(0, value); Apply(); }
        public void SetEmissionTint(Color value) { emissionTint = value; Apply(); }
        void OnEnable() { Apply(); }
        void OnValidate() { brightness = Mathf.Max(0, brightness); Apply(); }

        public void Apply()
        {
            if (surface == null) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            surface.GetPropertyBlock(properties);
            properties.SetTexture("_BaseMap", displayTexture);
            properties.SetTexture("_EmissionMap", displayTexture);
            properties.SetColor("_BaseColor", powered ? Color.white : new Color(.005f, .012f, .008f));
            properties.SetColor("_EmissionColor", powered ? emissionTint * brightness : Color.black);
            surface.SetPropertyBlock(properties);
        }
    }
}
