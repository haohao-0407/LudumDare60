using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LudumDare60.Rendering
{
    /// <summary>Draws a union silhouette of the original model without filling or hiding its X-ray lines.</summary>
    internal static class XrayOutlineRenderer
    {
        private static readonly Dictionary<Xray, OutlinePass> Active = new Dictionary<Xray, OutlinePass>();
        private static Material material;
        private static bool subscribed;
        private static bool reportedShaderFailure;

        internal static void Register(Xray effect)
        {
            if (!Active.ContainsKey(effect)) Active.Add(effect, new OutlinePass(effect));
            if (subscribed) return;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            subscribed = true;
        }

        internal static void Unregister(Xray effect)
        {
            Active.Remove(effect);
            if (Active.Count != 0) return;
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            subscribed = false;
            CoreUtils.Destroy(material);
            material = null;
            reportedShaderFailure = false;
        }

        private static void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
                || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)) return;
            foreach (var entry in Active)
            {
                var effect = entry.Key;
                if (effect == null || !effect.isActiveAndEnabled || !effect.showOutline
                    || effect.outlineOpacity <= 0 || effect.outlineColor.a <= 0) continue;
                if (material == null)
                {
                    // Resources guarantees the shader is included in a player build too.
                    var shader = Resources.Load<Shader>("XraySilhouette");
                    if (shader == null || !shader.isSupported)
                    {
                        if (!reportedShaderFailure)
                            Debug.LogError("Xray outline: Resources/XraySilhouette.shader is missing or unsupported. Check shader import errors and its .meta GUID.", effect);
                        reportedShaderFailure = true;
                        return;
                    }
                    reportedShaderFailure = false;
                    material = CoreUtils.CreateEngineMaterial(shader);
                }
                camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(entry.Value);
            }
        }

        private readonly struct Shape
        {
            public readonly Mesh Mesh;
            public readonly Matrix4x4 Matrix;
            public Shape(Mesh mesh, Matrix4x4 matrix) { Mesh = mesh; Matrix = matrix; }
        }

        private sealed class OutlinePass : ScriptableRenderPass
        {
            private readonly Xray owner;
            private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            private readonly MaterialPropertyBlock maskProperties = new MaterialPropertyBlock();
            private static readonly int MaskId = Shader.PropertyToID("_XrayMask");
            private static readonly int ViewProjectionId = Shader.PropertyToID("_XrayViewProjection");
            private static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
            private static readonly int StepId = Shader.PropertyToID("_OutlineStep");

            private sealed class MaskData
            {
                public List<Shape> Shapes;
                public Material Material;
                public MaterialPropertyBlock Properties;
                public Matrix4x4 ViewProjection;
            }
            private sealed class CompositeData
            {
                public TextureHandle Mask;
                public Material Material;
                public MaterialPropertyBlock Properties;
                public Color Color;
                public Vector4 Step;
            }

            public OutlinePass(Xray owner)
            {
                this.owner = owner;
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                if (owner == null || !owner.isActiveAndEnabled || !owner.showOutline || material == null) return;
                var cameraData = frameData.Get<UniversalCameraData>();
                var resources = frameData.Get<UniversalResourceData>();
                var shapes = new List<Shape>(owner.OutlineSources.Count);
                foreach (var filter in owner.OutlineSources)
                {
                    if (filter == null || filter.sharedMesh == null || !filter.gameObject.activeInHierarchy
                        || (cameraData.camera.cullingMask & (1 << filter.gameObject.layer)) == 0) continue;
                    // Renderers are deliberately disabled by Xray; DrawMesh still draws their original silhouette.
                    shapes.Add(new Shape(filter.sharedMesh, filter.transform.localToWorldMatrix));
                }
                if (shapes.Count == 0) return;
                var descriptor = cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.graphicsFormat = GraphicsFormat.R8_UNorm;
                descriptor.msaaSamples = 1;
                descriptor.bindMS = false;
                var mask = UniversalRenderer.CreateRenderGraphTexture(graph, descriptor, "Xray original silhouette", true, FilterMode.Bilinear);

                using (var builder = graph.AddRasterRenderPass<MaskData>("Xray: original model silhouette", out var data))
                {
                    data.Shapes = shapes;
                    data.Material = material;
                    data.Properties = maskProperties;
                    // This target is always a texture, even when the camera draws directly to the backbuffer.
                    data.ViewProjection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true) * cameraData.GetViewMatrix();
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (MaskData data, RasterGraphContext context) =>
                    {
                        data.Properties.SetMatrix(ViewProjectionId, data.ViewProjection);
                        foreach (var shape in data.Shapes)
                            for (var submesh = 0; submesh < shape.Mesh.subMeshCount; submesh++)
                                context.cmd.DrawMesh(shape.Mesh, shape.Matrix, data.Material, submesh, 0, data.Properties);
                    });
                }
                using (var builder = graph.AddRasterRenderPass<CompositeData>("Xray: white silhouette outline", out var data))
                {
                    data.Mask = mask;
                    data.Material = material;
                    data.Properties = properties;
                    data.Color = owner.outlineColor;
                    data.Color.a *= owner.outlineOpacity;
                    var width = Mathf.Clamp(owner.outlineWidth, 0.5f, 8f);
                    data.Step = new Vector4(width / descriptor.width, width / descriptor.height, 0, 0);
                    builder.UseTexture(mask, AccessFlags.Read);
                    // Only blend the edge over the existing color: never sample/replace the camera color or depth.
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (CompositeData data, RasterGraphContext context) =>
                    {
                        data.Properties.Clear();
                        data.Properties.SetTexture(MaskId, data.Mask);
                        data.Properties.SetColor(ColorId, data.Color);
                        data.Properties.SetVector(StepId, data.Step);
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, 1, MeshTopology.Triangles, 3, 1, data.Properties);
                    });
                }
            }
        }
    }
}
