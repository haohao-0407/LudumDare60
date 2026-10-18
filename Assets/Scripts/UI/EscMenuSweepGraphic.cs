using UnityEngine;

namespace LudumDare60.UI
{
    // A clipped gradient drawn directly by uGUI; no extra textures, materials or raycast targets.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EscMenuSweepGraphic : UnityEngine.UI.MaskableGraphic
    {
        [SerializeField] private Color sweepColor = new Color(0.8f, 1, 0.94f, 0.42f);
        [SerializeField, Range(0.05f, 0.5f)] private float bandWidth = 0.19f;
        private float progress, strength;

        protected override void Awake() { base.Awake(); raycastTarget = false; }

        public void SetSweep(float position, float opacity)
        {
            if (progress == position && strength == opacity) return;
            progress = position; strength = opacity; SetVerticesDirty();
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            if (strength <= 0 || progress > 1) return;
            var bounds = rectTransform.rect;
            float center = Mathf.Lerp(-bandWidth, 1 + bandWidth, progress);
            const int segments = 80;
            for (int i = 0; i <= segments; i++)
            {
                float x = (float)i / segments;
                AddVertex(vh, bounds, x, 0, center - 0.025f);
                AddVertex(vh, bounds, x, 1, center + 0.025f);
                if (i == 0) continue;
                int v = i * 2;
                vh.AddTriangle(v - 2, v - 1, v); vh.AddTriangle(v, v - 1, v + 1);
            }
        }

        private void AddVertex(UnityEngine.UI.VertexHelper vh, Rect bounds, float x, float y, float center)
        {
            float glow = Mathf.Clamp01(1 - Mathf.Abs(x - center) / bandWidth);
            var tint = sweepColor; tint.a *= glow * glow * strength;
            vh.AddVert(new Vector3(Mathf.Lerp(bounds.xMin, bounds.xMax, x), Mathf.Lerp(bounds.yMin, bounds.yMax, y)), tint, Vector2.zero);
        }
    }
}
