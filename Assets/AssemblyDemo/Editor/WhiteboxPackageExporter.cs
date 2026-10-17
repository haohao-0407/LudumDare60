using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LudumDare60.AssemblyDemo.Editor
{
    /// <summary>Packaging utilities stay in the host demo and are deliberately not exported.</summary>
    public static class WhiteboxPackageExporter
    {
        public const string PackageRoot = "Assets/WhiteboxModule";

        [MenuItem("Tools/Whitebox Module/Export Reusable Package")]
        public static void Export()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("Stop Play mode before exporting."); return; }
            AssetDatabase.Refresh();
            StripSavedGeometry(AssemblyDemoTools.ModulePrefabPath);
            StripSavedGeometry(AssemblyDemoTools.AssemblyPrefabPath);
            // Refresh the open scene's prefab instances after migrating their source assets.
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path == AssemblyDemoTools.ScenePath && !scene.isDirty)
                EditorSceneManager.OpenScene(scene.path);
            var assets = AssetDatabase.GetDependencies(AssemblyDemoTools.ModulePrefabPath, true);
            foreach (string asset in assets)
                if (!asset.StartsWith(PackageRoot + "/") && !asset.StartsWith("Resources/") && !asset.StartsWith("Library/"))
                    throw new System.InvalidOperationException("Unexpected prefab dependency outside portable folder: " + asset);
            Directory.CreateDirectory("Exports");
            string destination = Path.GetFullPath("Exports/WhiteboxModule-1.0.0.unitypackage");
            UnityEditor.AssetPackage.Package.Export(new UnityEditor.AssetPackage.ExportPackageParameters(
                PackageRoot, destination, string.Empty, ExportPackageOptions.Recurse));
            Debug.Log("[WhiteboxModule] Exported standalone package: " + destination);
        }

        private static void StripSavedGeometry(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var module in root.GetComponentsInChildren<AssemblyModule>(true))
                {
                    module.enabled = false; // Disposes the transient preview first.
                    for (int i = module.transform.childCount - 1; i >= 0; i--)
                    {
                        var child = module.transform.GetChild(i);
                        if (child.name == "Whitebox geometry") Object.DestroyImmediate(child.gameObject);
                    }
                    module.enabled = true;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
