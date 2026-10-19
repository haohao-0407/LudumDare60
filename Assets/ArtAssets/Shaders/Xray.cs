
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using LudumDare60.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class Xray : MonoBehaviour
{
    private const string GeneratedName =
        "__XRayWireframeGenerated";

    private const string ShaderName =
        "X-Ray/Effects/XRay";

    // =========================
    // Source
    // =========================

    [Header("Source")]
    [Tooltip("Optional low-poly wireframe source")]
    public Transform wireframeSourceRoot;

    [Tooltip("Material used to draw the wireframe")]
    public Material effectMaterial;

    [Header("Wireframe Structure")]
    [Tooltip("Sparse Structure 大幅精简线条并保持透视；Retro Quads 仅去斜线；Feature Edges 保留折角。")]
    public WireframeEdgeMode edgeMode = WireframeEdgeMode.RetroQuads;

    [Range(0f, 60f)]
    [Tooltip("Maximum angle between two triangle faces that may form a retro quad. Does not reduce the model's segment count.")]
    public float quadMaxAngle = 10f;

    [Range(0f, 180f)]
    [Tooltip("Only used by Feature Edges. Higher values retain fewer creases.")]
    public float featureAngle = 30f;

    [Range(4, 16)]
    [InspectorName("曲面分区数")]
    [Tooltip("仅 Sparse Structure：越小曲面线越少，建议 4。它是法线方向分区数，不是模型的真实面数。")]
    public int curveSegments = 4;

    [Range(0f, 0.1f)]
    [InspectorName("小零件过滤比例")]
    [Tooltip("仅 Sparse Structure：过滤最大尺寸小于整件模型最长边乘此比例的独立零件。越大细节越少；0 保留全部。")]
    public float smallPartFraction = 0.04f;

    [Range(0f, 0.03f)]
    [InspectorName("近邻线合并比例")]
    [Tooltip("仅 Sparse Structure：合并距离小于模型最长边乘此比例、方向近似平行且覆盖重叠的线。越大越简略；0 关闭。")]
    public float lineSpacingFraction = 0.005f;

    [Range(0f, 60f)]
    [InspectorName("平面合并阈值(度)")]
    [Tooltip("仅 Structural Edges：相邻三角面夹角小于此值时视为同一平面，合并成一个区域、线被吞掉。越大越干净，典型 10~20。")]
    public float planarAngle = 12f;

    [Range(0f, 0.1f)]
    [InspectorName("最短完整线比例")]
    [Tooltip("仅 Structural Edges：整条结构线的长度短于模型最长边乘此比例时，整条丢弃。按线判定，不会留半截毛刺。0 = 关闭。")]
    public float minEdgeLengthFraction = 0.015f;

    [Range(0f, 0.3f)]
    [InspectorName("线间距抑制比例")]
    [Tooltip("仅 Structural Edges：两条线质心距离小于模型最长边乘此比例、方向近似平行时，只保留更长的那条。用来压掉同心圆弧这类密集平行线。0 = 关闭。")]
    public float chainSeparationFraction = 0.04f;

    public enum ChainPruneMode
    {
        Off = 0,
        FragmentsOnly = 1,
        ClosedOrConnected = 2,
    }

    [InspectorName("悬空线处理")]
    [Tooltip("Off 不处理。\nFragments Only：只删两端都悬空的孤立碎片，安全。\nClosed Or Connected：反复剪到所有线闭环或首尾相连 —— 很激进，机械模型可能几乎全被剪掉。")]
    public ChainPruneMode pruneMode = ChainPruneMode.FragmentsOnly;

    [Range(0f, 180f)]
    [InspectorName("强折角角度(度)")]
    [Tooltip("仅 Structural Edges：折角达到此角度时线条取满宽度，低于它按比例变细。")]
    public float strongEdgeAngle = 90f;

    [Range(0.05f, 1f)]
    [InspectorName("弱线宽度比例")]
    [Tooltip("仅 Structural Edges：弱结构线的宽度相对强折角线的比例。1 = 所有线等粗。")]
    public float minorLineScale = 0.45f;

    [Tooltip("Disable for classic modeling-editor wireframes where all front and back edges share one style.")]
    public bool useSceneOcclusion = true;

    public int SourceEdgeCount { get; private set; }
    public int VisibleEdgeCount { get; private set; }
    public int ReconstructedQuadCount { get; private set; }
    public int RemovedPartCount { get; private set; }

    // =========================
    // Original Model
    // =========================

    [Header("Original Model")]
    [Tooltip("Hide original model surfaces and only show wireframe")]
    public bool hideOriginalModel = false;

    // 记录原始 Renderer 的启用状态
    private readonly Dictionary<MeshRenderer, bool> originalStates =
        new Dictionary<MeshRenderer, bool>();

    private bool originalsHidden = false;

    [Header("Silhouette Outline / 外轮廓描边")]
    [InspectorName("启用描边")]
    public bool showOutline = true;

    [ColorUsage(true, true), InspectorName("描边颜色")]
    public Color outlineColor = Color.white;

    [Range(0.5f, 8f), InspectorName("描边粗细（像素）")]
    public float outlineWidth = 2f;

    [Range(0f, 1f), InspectorName("描边不透明度")]
    public float outlineOpacity = 1f;

    // Original geometry, independent of wireframe simplification and optional low-poly proxy.
    internal readonly List<MeshFilter> OutlineSources = new List<MeshFilter>();

    // =========================
    // Visible Lines
    // =========================

    [Header("Visible Lines")]

    [ColorUsage(true, true)]
    public Color visibleColor =
        new Color(0.25f, 1f, 0.85f, 1f);

    [Range(0.25f, 12f)]
    public float visibleWidth = 1.4f;

    [Range(0f, 1f)]
    public float visibleOpacity = 1f;

    // =========================
    // Hidden Lines
    // =========================

    [Header("Hidden Lines")]

    public bool showHidden = true;

    [ColorUsage(true, true)]
    public Color hiddenColor =
        new Color(1f, 0.6f, 0.12f, 1f);

    [Range(0.25f, 12f)]
    public float hiddenWidth = 1f;

    [Range(0f, 1f)]
    public float hiddenOpacity = 0.35f;

    [Range(0f, 0.5f)]
    public float depthTolerance = 0.025f;

    // =========================
    // Style
    // =========================

    [Header("Style")]

    [Range(0.05f, 3f)]
    public float softness = 0.7f;

    public bool dashedLines = false;

    [Range(1f, 40f)]
    public float dashCount = 8f;

    [Range(0.05f, 1f)]
    public float dashDuty = 0.58f;

    [Range(-10f, 10f)]
    public float dashSpeed = 0f;

    [Range(0f, 1f)]
    public float pulseStrength = 0f;

    [Range(0f, 8f)]
    public float pulseSpeed = 1f;

    // =========================
    // Runtime Data
    // =========================

    private readonly List<MeshRenderer> generatedRenderers =
        new List<MeshRenderer>();

    private readonly struct DisplayMeshKey : System.IEquatable<DisplayMeshKey>
    {
        private readonly Mesh source;
        private readonly int signature;
        public DisplayMeshKey(Mesh source, int signature) { this.source = source; this.signature = signature; }
        public bool Equals(DisplayMeshKey other) => source == other.source && signature == other.signature;
        public override bool Equals(object obj) => obj is DisplayMeshKey other && Equals(other);
        public override int GetHashCode() => unchecked(source.GetHashCode() * 397 ^ signature);
    }
    private readonly Dictionary<DisplayMeshKey, Mesh> generatedMeshes = new Dictionary<DisplayMeshKey, Mesh>();
    private readonly Dictionary<DisplayMeshKey, RetroWireframeMeshBuilder.Statistics> meshStatistics =
        new Dictionary<DisplayMeshKey, RetroWireframeMeshBuilder.Statistics>();
    private readonly HashSet<MeshRenderer> maskedRenderers = new HashSet<MeshRenderer>();

    private Material temporaryMaterial;
    private MaterialPropertyBlock propertyBlock;

    private Transform builtSourceRoot;
    private Material builtEffectMaterial;
    private Shader builtMaterialShader;
    private WireframeEdgeMode builtEdgeMode;
    private float builtQuadAngle;
    private float builtFeatureAngle;
    private int builtCurveSegments;
    private float builtSmallPartFraction;
    private float builtLineSpacingFraction;
    private float builtPlanarAngle;
    private float builtMinEdgeFraction;
    private float builtStrongEdgeAngle;
    private float builtChainSeparation;
    private int builtPruneMode;

    private bool needsApply = true;
    private long lastValidationTicks;

    // =========================
    // Unity Lifecycle
    // =========================

    private void OnEnable()
    {
        RebuildOverlays();
        SyncOriginalVisibility();
        XrayOutlineRenderer.Register(this);
    }

    private void OnDisable()
    {
        XrayOutlineRenderer.Unregister(this);
        RestoreOriginalRenderers();
        ClearOverlays();
        OutlineSources.Clear();
        ReleaseTemporaryMaterial();
    }

    private void OnDestroy()
    {
        XrayOutlineRenderer.Unregister(this);
        RestoreOriginalRenderers();
        ClearOverlays();
        OutlineSources.Clear();
        ReleaseTemporaryMaterial();
    }

    private void OnValidate()
    {
        needsApply = true;
        lastValidationTicks = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    private void Update()
    {
        if (!isActiveAndEnabled)
            return;

        // 自动检测线框来源或材质是否改变
        Transform currentSource =
            wireframeSourceRoot != null
                ? wireframeSourceRoot
                : transform;

        if (currentSource != builtSourceRoot ||
            effectMaterial != builtEffectMaterial ||
            (effectMaterial != null ? effectMaterial.shader : null) != builtMaterialShader ||
            edgeMode != builtEdgeMode || quadMaxAngle != builtQuadAngle || featureAngle != builtFeatureAngle ||
            curveSegments != builtCurveSegments || smallPartFraction != builtSmallPartFraction || lineSpacingFraction != builtLineSpacingFraction ||
            planarAngle != builtPlanarAngle || minEdgeLengthFraction != builtMinEdgeFraction || strongEdgeAngle != builtStrongEdgeAngle ||
            chainSeparationFraction != builtChainSeparation || (int)pruneMode != builtPruneMode)
        {
#if UNITY_EDITOR
            // Wait for an Inspector drag to settle instead of rebuilding a large mesh on every slider step.
            if (!Application.isPlaying && (System.Diagnostics.Stopwatch.GetTimestamp() - lastValidationTicks)
                / (double)System.Diagnostics.Stopwatch.Frequency < 0.25)
            {
                if (needsApply) ApplyProperties();
                return;
            }
#endif
            RebuildOverlays();
        }

        // 根据 Inspector 开关控制原模型显示
        SyncOriginalVisibility();

        // 更新线框参数
        if (needsApply)
        {
            ApplyProperties();
        }
    }

    // =========================
    // Generate Wireframe
    // =========================

    [ContextMenu("Rebuild Wireframe Overlays")]
    public void RebuildOverlays()
    {
        RestoreOriginalRenderers();
        ClearOverlays();

        OutlineSources.Clear();
        GetComponentsInChildren(true, OutlineSources);
        OutlineSources.RemoveAll(filter => filter.gameObject.name.StartsWith(GeneratedName)
            || filter.GetComponent<MeshRenderer>() == null);

        if (!isActiveAndEnabled)
            return;

        Transform sourceRoot =
            wireframeSourceRoot != null
                ? wireframeSourceRoot
                : transform;

        builtSourceRoot = sourceRoot;
        builtEffectMaterial = effectMaterial;
        builtMaterialShader = effectMaterial != null ? effectMaterial.shader : null;
        builtEdgeMode = edgeMode;
        builtQuadAngle = quadMaxAngle;
        builtFeatureAngle = featureAngle;
        builtCurveSegments = curveSegments;
        builtSmallPartFraction = smallPartFraction;
        builtLineSpacingFraction = lineSpacingFraction;
        builtPlanarAngle = planarAngle;
        builtMinEdgeFraction = minEdgeLengthFraction;
        builtStrongEdgeAngle = strongEdgeAngle;
        builtChainSeparation = chainSeparationFraction;
        builtPruneMode = (int)pruneMode;
        SourceEdgeCount = VisibleEdgeCount = ReconstructedQuadCount = 0;
        RemovedPartCount = 0;

        Material material = ResolveMaterial();

        if (material == null)
            return;

        MeshRenderer[] sourceRenderers =
            sourceRoot.GetComponentsInChildren<MeshRenderer>(true);

        var worldBounds = new Bounds();
        var hasBounds = false;
        foreach (var source in sourceRenderers)
        {
            if (source == null || source.gameObject.name.StartsWith(GeneratedName)) continue;
            var filter = source.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            if (!hasBounds) { worldBounds = source.bounds; hasBounds = true; }
            else worldBounds.Encapsulate(source.bounds);
        }
        var modelSize = Mathf.Max(worldBounds.size.x, Mathf.Max(worldBounds.size.y, worldBounds.size.z));

        foreach (MeshRenderer source in sourceRenderers)
        {
            if (source == null)
                continue;

            // 跳过自动生成的线框
            if (source.gameObject.name.StartsWith(GeneratedName))
                continue;

            MeshFilter sourceFilter =
                source.GetComponent<MeshFilter>();

            if (sourceFilter == null ||
                sourceFilter.sharedMesh == null)
                continue;

            // 创建线框子物体
            GameObject go = new GameObject(GeneratedName);

            go.layer = source.gameObject.layer;

            go.transform.SetParent(source.transform, false);

            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            go.hideFlags =
                HideFlags.HideInHierarchy |
                HideFlags.DontSaveInEditor |
                HideFlags.DontSaveInBuild;

            // Source geometry is unchanged. Only the disposable display copy receives edge masks.
            MeshFilter filter = go.AddComponent<MeshFilter>();

            var sourceMesh = sourceFilter.sharedMesh;
            var scale = source.transform.lossyScale;
            var scaleFactor = Mathf.Max(1e-6f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
            var localModelSize = modelSize / scaleFactor;
            // 缓存键必须包含所有影响几何生成的参数，否则改参数会命中旧网格
            var keyHash = 17;
            keyHash = keyHash * 31 + (int)edgeMode;
            keyHash = keyHash * 31 + quadMaxAngle.GetHashCode();
            keyHash = keyHash * 31 + featureAngle.GetHashCode();
            keyHash = keyHash * 31 + curveSegments;
            keyHash = keyHash * 31 + smallPartFraction.GetHashCode();
            keyHash = keyHash * 31 + lineSpacingFraction.GetHashCode();
            keyHash = keyHash * 31 + planarAngle.GetHashCode();
            keyHash = keyHash * 31 + minEdgeLengthFraction.GetHashCode();
            keyHash = keyHash * 31 + strongEdgeAngle.GetHashCode();
            keyHash = keyHash * 31 + chainSeparationFraction.GetHashCode();
            keyHash = keyHash * 31 + (int)pruneMode;
            if (edgeMode == WireframeEdgeMode.SparseStructure) keyHash = keyHash * 31 + localModelSize.GetHashCode();

            var meshKey = new DisplayMeshKey(sourceMesh, keyHash);
            var hasMask = false;
            if (edgeMode != WireframeEdgeMode.AllTriangles)
            {
                if (!generatedMeshes.TryGetValue(meshKey, out var displayMesh))
                {
                    try
                    {
                        displayMesh = RetroWireframeMeshBuilder.Build(sourceMesh, edgeMode, quadMaxAngle,
                            featureAngle, out var statistics, curveSegments, localModelSize * smallPartFraction,
                            Mathf.Min(localModelSize * 0.00001f, sourceMesh.bounds.size.magnitude * 0.001f),
                            localModelSize * lineSpacingFraction,
                            planarAngle, localModelSize * minEdgeLengthFraction, strongEdgeAngle,
                            localModelSize * chainSeparationFraction, (int)pruneMode);
                        generatedMeshes.Add(meshKey, displayMesh);
                        meshStatistics.Add(meshKey, statistics);
                    }
                    catch (System.InvalidOperationException exception)
                    {
                        Debug.LogWarning("Xray: " + exception.Message + " Falling back to original triangles.", this);
                        generatedMeshes.Add(meshKey, null);
                    }
                }
                if (displayMesh != null)
                {
                    filter.sharedMesh = displayMesh;
                    hasMask = true;
                    var statistics = meshStatistics[meshKey];
                    SourceEdgeCount += statistics.InputEdges;
                    VisibleEdgeCount += statistics.VisibleEdges;
                    ReconstructedQuadCount += statistics.PairedQuads;
                    RemovedPartCount += statistics.RemovedParts;
                }
            }
            if (!hasMask) filter.sharedMesh = sourceMesh;

            // 创建独立线框 Renderer
            MeshRenderer renderer =
                go.AddComponent<MeshRenderer>();

            int submeshCount = Mathf.Max(
                1,
                filter.sharedMesh.subMeshCount
            );

            Material[] materials =
                new Material[submeshCount];

            for (int i = 0; i < submeshCount; i++)
            {
                materials[i] = material;
            }

            renderer.sharedMaterials = materials;

            renderer.shadowCastingMode =
                ShadowCastingMode.Off;

            renderer.receiveShadows = false;

            renderer.lightProbeUsage =
                LightProbeUsage.Off;

            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;

            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            renderer.allowOcclusionWhenDynamic = false;

            renderer.enabled = true;

            generatedRenderers.Add(renderer);
            if (hasMask) maskedRenderers.Add(renderer);
        }

        needsApply = true;
        ApplyProperties();
        SyncOriginalVisibility();
    }

    [ContextMenu("Apply Retro Wireframe Preset")]
    public void ApplyRetroPreset()
    {
        edgeMode = WireframeEdgeMode.RetroQuads;
        quadMaxAngle = 10f;
        ApplySeeThroughStyle();
        RebuildOverlays();
    }

    [ContextMenu("Apply Sparse See-Through Preset")]
    public void ApplySparsePreset()
    {
        edgeMode = WireframeEdgeMode.SparseStructure;
        curveSegments = 4;
        smallPartFraction = 0.04f;
        lineSpacingFraction = 0.005f;
        ApplySeeThroughStyle();
        RebuildOverlays();
    }

    private void ApplySeeThroughStyle()
    {
        useSceneOcclusion = false;
        hideOriginalModel = true;
        visibleColor = new Color(0.65f, 0.69f, 0.65f, 1f);
        visibleWidth = 0.75f;
        visibleOpacity = 0.9f;
        hiddenColor = visibleColor;
        hiddenWidth = visibleWidth;
        hiddenOpacity = visibleOpacity;
        showHidden = true;
        softness = 0.65f;
        dashedLines = false;
        pulseStrength = 0;
    }

    // =========================
    // Original Model Visibility
    // =========================

    private void SyncOriginalVisibility()
    {
        if (hideOriginalModel)
        {
            if (!originalsHidden)
            {
                HideOriginalRenderers();
            }
        }
        else
        {
            if (originalsHidden)
            {
                RestoreOriginalRenderers();
            }
        }
    }

    private void HideOriginalRenderers()
    {
        if (originalsHidden)
            return;

        originalStates.Clear();

        MeshRenderer[] renderers =
            GetComponentsInChildren<MeshRenderer>(true);

        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            // 不能关闭我们生成的线框 Renderer
            if (renderer.gameObject.name.StartsWith(GeneratedName))
                continue;

            // 保存原来是否启用
            originalStates[renderer] = renderer.enabled;

            // 仅关闭原始模型表面
            renderer.enabled = false;
        }

        // A separately supplied proxy must remain active so its generated wireframe can render,
        // but its original surface should not appear alongside the wireframe.
        if (wireframeSourceRoot != null && wireframeSourceRoot != transform && !wireframeSourceRoot.IsChildOf(transform))
            foreach (var renderer in wireframeSourceRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer == null || renderer.gameObject.name.StartsWith(GeneratedName) || originalStates.ContainsKey(renderer)) continue;
                originalStates[renderer] = renderer.enabled;
                renderer.enabled = false;
            }

        originalsHidden = true;
    }

    private void RestoreOriginalRenderers()
    {
        if (!originalsHidden)
            return;

        foreach (var pair in originalStates)
        {
            MeshRenderer renderer = pair.Key;

            if (renderer == null)
                continue;

            // 恢复之前的状态，而不是全部强制开启
            renderer.enabled = pair.Value;
        }

        originalStates.Clear();
        originalsHidden = false;
    }

    // Inspector 右上角菜单也能手动调用

    [ContextMenu("Hide Original Mesh Renderers")]
    public void HideOriginal()
    {
        hideOriginalModel = true;
        SyncOriginalVisibility();
    }

    [ContextMenu("Show Original Mesh Renderers")]
    public void ShowOriginal()
    {
        hideOriginalModel = false;
        SyncOriginalVisibility();
    }

    // =========================
    // Material
    // =========================

    private Material ResolveMaterial()
    {
        if (effectMaterial != null && effectMaterial.shader != null && effectMaterial.shader.name == ShaderName)
            return effectMaterial;

        if (temporaryMaterial != null)
            return temporaryMaterial;

        if (effectMaterial != null)
            Debug.LogWarning("Xray: Effect Material must use '" + ShaderName + "'. Using a temporary wireframe material; the supplied material is unchanged.", this);

        Shader shader = Shader.Find(ShaderName);

        if (shader == null)
        {
            Debug.LogError(
                "Wireframe shader not found: " + ShaderName,
                this
            );

            return null;
        }

        temporaryMaterial = new Material(shader)
        {
            name = "XRay Wireframe - Generated",
            hideFlags = HideFlags.HideAndDontSave
        };

        return temporaryMaterial;
    }

    // =========================
    // Shader Parameters
    // =========================

    /// <summary>
    /// 运行时切换线框颜色并立即生效（修复进度屏的状态配色用）。
    /// 直接改字段不会触发重刷，因为只有 OnValidate 会置 needsApply。
    /// </summary>
    public void SetWireframeColors(Color visible, Color hidden)
    {
        visibleColor = visible;
        hiddenColor = hidden;
        // 外轮廓跟着主体颜色走：状态变红/蓝时不会留下一条白边。
        // 只改 RGB，保留原有的 alpha（0 表示该装置不画轮廓）。
        outlineColor = new Color(visible.r, visible.g, visible.b, outlineColor.a);
        needsApply = true;
        ApplyProperties();
    }

    private void ApplyProperties()
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        propertyBlock.Clear();
        propertyBlock.SetFloat("_UseSceneDepth", useSceneOcclusion ? 1f : 0f);

        // Visible lines
        propertyBlock.SetColor(
            "_VisibleColor", visibleColor
        );

        propertyBlock.SetFloat(
            "_VisibleWidth", visibleWidth
        );

        propertyBlock.SetFloat(
            "_VisibleOpacity", visibleOpacity
        );

        // Hidden lines
        propertyBlock.SetColor(
            "_HiddenColor", hiddenColor
        );

        propertyBlock.SetFloat(
            "_HiddenWidth", hiddenWidth
        );

        propertyBlock.SetFloat(
            "_HiddenOpacity", hiddenOpacity
        );

        propertyBlock.SetFloat(
            "_ShowHidden", showHidden ? 1f : 0f
        );

        propertyBlock.SetFloat(
            "_DepthTolerance", depthTolerance
        );

        // Line style
        propertyBlock.SetFloat(
            "_Softness", softness
        );

        propertyBlock.SetFloat(
            "_MinorLineScale", minorLineScale
        );

        propertyBlock.SetFloat(
            "_Dashes", dashedLines ? 1f : 0f
        );

        propertyBlock.SetFloat(
            "_DashCount", dashCount
        );

        propertyBlock.SetFloat(
            "_DashDuty", dashDuty
        );

        propertyBlock.SetFloat(
            "_DashSpeed", dashSpeed
        );

        // Pulse
        propertyBlock.SetFloat(
            "_PulseStrength", pulseStrength
        );

        propertyBlock.SetFloat(
            "_PulseSpeed", pulseSpeed
        );

        // 提交到所有线框渲染器
        foreach (MeshRenderer renderer in generatedRenderers)
        {
            if (renderer != null)
            {
                propertyBlock.SetFloat("_UseEdgeMask", maskedRenderers.Contains(renderer) ? 1f : 0f);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        needsApply = false;
    }

    // =========================
    // Cleanup
    // =========================

    private void ClearOverlays()
    {
        for (int i = generatedRenderers.Count - 1; i >= 0; i--)
        {
            MeshRenderer renderer = generatedRenderers[i];

            if (renderer != null)
            {
                DestroySafe(renderer.gameObject);
            }
        }

        generatedRenderers.Clear();
        maskedRenderers.Clear();
        foreach (var mesh in generatedMeshes.Values) if (mesh != null) DestroySafe(mesh);
        generatedMeshes.Clear();
        meshStatistics.Clear();

        builtSourceRoot = null;
        builtEffectMaterial = null;
    }

    private void ReleaseTemporaryMaterial()
    {
        if (temporaryMaterial == null)
            return;

        DestroySafe(temporaryMaterial);
        temporaryMaterial = null;
    }

    private static void DestroySafe(Object obj)
    {
        if (obj == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Object.DestroyImmediate(obj);
        }
        else
        {
            Object.Destroy(obj);
        }
#else
        Object.Destroy(obj);
#endif
    }
}
