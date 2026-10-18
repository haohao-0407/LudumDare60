
using UnityEngine;
using UnityEditor;
using System.IO;

public static class ExtractGltfMaterial
{
    [MenuItem("Assets/复制为独立材质 MAT", true)]
    private static bool Validate()
    {
        return Selection.activeObject is Material;
    }

    [MenuItem("Assets/复制为独立材质 MAT")]
    private static void Extract()
    {
        var source = Selection.activeObject as Material;
        if (source == null) return;

        // 保留原始 Shader、贴图引用及材质参数
        var copy = new Material(source);

        string path = EditorUtility.SaveFilePanelInProject(
            "保存独立材质",
            source.name + "_Editable",
            "mat",
            "选择材质保存位置"
        );

        if (string.IsNullOrEmpty(path))
        {
            Object.DestroyImmediate(copy);
            return;
        }

        path = AssetDatabase.GenerateUniqueAssetPath(path);
        copy.name = Path.GetFileNameWithoutExtension(path);

        AssetDatabase.CreateAsset(copy, path);
        AssetDatabase.SaveAssets();

        Selection.activeObject = copy;
        EditorGUIUtility.PingObject(copy);

        Debug.Log("材质已复制到：" + path);
    }
}
