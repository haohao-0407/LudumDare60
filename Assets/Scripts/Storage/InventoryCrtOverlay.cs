using UnityEngine;
using UnityEngine.UI;

namespace LudumDare60.Storage
{
    /// <summary>Texture-free CRT scanlines, soft edge shading and a faint travelling phosphor band.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class InventoryCrtOverlay : MaskableGraphic
    {
        private float sweep;
        private float nextRefresh;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        private void Update()
        {
            if (!Application.IsPlaying(gameObject) || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.08f;
            sweep = Mathf.Repeat(Time.unscaledTime * 0.045f, 1f);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            for (var y = rect.yMin; y < rect.yMax; y += 5f)
                Quad(mesh, rect.xMin, y, rect.xMax, Mathf.Min(y + 1.2f, rect.yMax), new Color(0f, 0.01f, 0.01f, 0.13f));
            const int strips = 12;
            for (var i = 0; i < strips; i++)
            {
                var tint = new Color(0f, 0.005f, 0.003f, 0.21f * (1f - i / (float)strips));
                var x = rect.width * 0.05f / strips;
                var y = rect.height * 0.07f / strips;
                Quad(mesh, rect.xMin + i * x, rect.yMin, rect.xMin + (i + 1) * x, rect.yMax, tint);
                Quad(mesh, rect.xMax - (i + 1) * x, rect.yMin, rect.xMax - i * x, rect.yMax, tint);
                Quad(mesh, rect.xMin, rect.yMin + i * y, rect.xMax, rect.yMin + (i + 1) * y, tint);
                Quad(mesh, rect.xMin, rect.yMax - (i + 1) * y, rect.xMax, rect.yMax - i * y, tint);
            }
            for (var i = -6; i <= 6; i++)
            {
                var y = rect.yMin + (1f - sweep) * rect.height + i * 3f;
                if (y < rect.yMin || y + 3f > rect.yMax) continue;
                Quad(mesh, rect.xMin, y, rect.xMax, y + 3f, new Color(0.5f, 1f, 0.75f, 0.018f * (1f - Mathf.Abs(i) / 7f)));
            }
        }

        private static void Quad(VertexHelper mesh, float left, float bottom, float right, float top, Color tint)
        {
            var index = mesh.currentVertCount;
            mesh.AddVert(new Vector3(left, bottom), tint, Vector2.zero);
            mesh.AddVert(new Vector3(left, top), tint, Vector2.zero);
            mesh.AddVert(new Vector3(right, top), tint, Vector2.zero);
            mesh.AddVert(new Vector3(right, bottom), tint, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
