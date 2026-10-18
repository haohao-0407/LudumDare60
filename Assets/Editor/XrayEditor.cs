using LudumDare60.Rendering;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Xray)), CanEditMultipleObjects]
public sealed class XrayEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("稀疏透视：合并曲面方向分区，过滤小零件。保留前后透视，不改成只画正面轮廓。", MessageType.Info);
        if (GUILayout.Button("应用稀疏透视预设（推荐）", GUILayout.Height(32)))
        {
            foreach (var selected in targets)
            {
                var effect = (Xray)selected;
                Undo.RecordObject(effect, "Apply sparse see-through preset");
                EnsureWireframeMaterial(effect);
                effect.ApplySparsePreset();
                MarkChanged(effect);
            }
        }
        if (GUILayout.Button("应用复古线框预设", GUILayout.Height(30)))
        {
            foreach (var selected in targets)
            {
                var effect = (Xray)selected;
                Undo.RecordObject(effect, "Apply retro wireframe preset");
                EnsureWireframeMaterial(effect);
                effect.ApplyRetroPreset();
                MarkChanged(effect);
            }
        }
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("启用描边：在原模型整体剪影外画白边。粗细按屏幕像素计算；内部透视线框不变，不描绘每条三角形边。", MessageType.None);
        if (targets.Length != 1) return;
        var current = (Xray)target;
        if (current.edgeMode != WireframeEdgeMode.AllTriangles)
            EditorGUILayout.HelpBox("显示边数：" + current.VisibleEdgeCount + " / " + current.SourceEdgeCount
                + "；过滤小零件：" + current.RemovedPartCount + "；识别四边面：" + current.ReconstructedQuadCount + "。", MessageType.None);
        if (current.edgeMode == WireframeEdgeMode.SparseStructure)
            EditorGUILayout.HelpBox("曲面分区数：越小曲面线越少，推荐 4。小零件过滤比例：推荐 0.04；仍密可试 0.06–0.08，会省略更多零件。近邻线合并比例：推荐 0.005；仍密可试 0.01。两个比例设 0 可关闭相应过滤。", MessageType.Info);
        if (current.edgeMode == WireframeEdgeMode.RetroQuads)
            EditorGUILayout.HelpBox("仍然太密时：为 Wireframe Source Root 指定低模版本。圆柱先用 8–12 个周向分段，球体先用 8–12 条经线、4–6 层纬线。", MessageType.Info);
        if (GUILayout.Button("重新生成线框"))
            foreach (var selected in targets) ((Xray)selected).RebuildOverlays();
    }

    private static void MarkChanged(Xray effect)
    {
        EditorUtility.SetDirty(effect);
        PrefabUtility.RecordPrefabInstancePropertyModifications(effect);
        if (effect.gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(effect.gameObject.scene);
    }

    private static void EnsureWireframeMaterial(Xray effect)
    {
        const string shaderName = "X-Ray/Effects/XRay";
        if (effect.effectMaterial != null && effect.effectMaterial.shader != null && effect.effectMaterial.shader.name == shaderName) return;
        var shader = Shader.Find(shaderName);
        if (shader == null) return;
        const string path = "Assets/ArtAssets/Shaders/M_Xray_Retro.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null || material.shader != shader)
        {
            // Keep the user's original material intact. The component gets a correctly typed variant.
            material = new Material(shader) { name = "M_Xray_Retro" };
            AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(path));
            AssetDatabase.SaveAssets();
        }
        effect.effectMaterial = material;
    }
}
