using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LudumDare60.CompactCockpit.Editor
{
    /// <summary>Builds editable cockpit assets without changing the user's open scenes.</summary>
    public static class CompactCockpitImporter
    {
        public const string AssetRoot = "Assets/CompactCockpit_WallIntegrated";
        public const string CockpitPrefab = AssetRoot + "/Prefabs/CompactCockpit_WallIntegrated.prefab";
        private const string ValidationPath = "SourceArt/CompactCockpit_WallIntegrated/unity_validation.json";
        private static readonly string[] ScreenNames = { "RADAR", "ATTITUDE", "NAVIGATION", "COMMUNICATIONS", "SYSTEMS" };
        private static readonly Regex BlenderSuffix = new Regex(@"(?:\.\d+)+$", RegexOptions.Compiled);

        [Serializable]
        private sealed class AssemblyManifest
        {
            public string architecture;
            public DisplayEntry[] entries;
        }

        [Serializable]
        private sealed class DisplayEntry
        {
            public string name;
            public string file;
            public float[] position;
            public float[] normal;
            public float[] up;
            public float width;
            public float height;
            public string texture;
        }

        [Serializable]
        private sealed class PrefabCheck
        {
            public string path;
            public int meshCount;
            public int rendererCount;
            public int screenCount;
            public int nestedScreenCount;
            public float[] boundsCenter;
            public float[] boundsSize;
            public string[] materialAssets;
            public string[] screenTextureAssets;
        }

        [Serializable]
        private sealed class ValidationReport
        {
            public bool valid;
            public string generatedUtc;
            public string units = "metres";
            public string displayForward = "+Z";
            public string displayUp = "+Y";
            public string blenderToUnity = "(-X, Z, -Y)";
            public string activeSceneBefore;
            public string activeSceneAfter;
            public bool openScenesPreserved;
            public string[] openScenesBefore;
            public string[] openScenesAfter;
            public List<PrefabCheck> prefabs = new List<PrefabCheck>();
            public List<string> issues = new List<string>();
        }

        private sealed class SceneSnapshot
        {
            public string activeHandle;
            public string activePath;
            public string[] scenes;

            public static SceneSnapshot Capture()
            {
                var active = SceneManager.GetActiveScene();
                var sceneStates = new List<string>();
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (EditorSceneManager.IsPreviewScene(scene)) continue;
                    var roots = string.Join(",", scene.GetRootGameObjects()
                        .Select(root => root.GetEntityId().ToString()).OrderBy(id => id));
                    sceneStates.Add(scene.handle + "|" + scene.path + "|" + scene.name + "|dirty=" + scene.isDirty + "|roots=" + roots);
                }
                return new SceneSnapshot { activeHandle = active.handle.ToString(), activePath = active.path, scenes = sceneStates.ToArray() };
            }

            public bool Matches(SceneSnapshot other)
            {
                return activeHandle == other.activeHandle && scenes.SequenceEqual(other.scenes);
            }
        }

        [MenuItem("Tools/Compact Cockpit/Build Wall Integrated Prefabs")]
        private static void BuildMenu() { Debug.Log(Build()); }

        [MenuItem("Tools/Compact Cockpit/Validate Wall Integrated Prefabs")]
        private static void ValidateMenu() { Debug.Log(Validate()); }

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Build requires an idle editor outside Play Mode.");

            var before = SceneSnapshot.Capture();
            var manifest = ReadManifest();
            EnsureFolder(AssetRoot + "/Materials");
            EnsureFolder(AssetRoot + "/Prefabs/Screens");
            var editableMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var entry in manifest.entries)
                {
                    var root = NewRoot("Screen_" + entry.name, preview);
                    try
                    {
                        var geometry = InstantiateModel(entry.file, preview, root.transform);
                        geometry.name = "Geometry";
                        AssignEditableMaterials(geometry, manifest, editableMaterials);
                        SavePrefab(root, ScreenPrefabPath(entry.name));
                    }
                    finally { Object.DestroyImmediate(root); }
                }

                var cockpit = NewRoot("CompactCockpit_WallIntegrated", preview);
                try
                {
                    var architecture = InstantiateModel(manifest.architecture, preview, cockpit.transform);
                    architecture.name = "Architecture";
                    AssignEditableMaterials(architecture, manifest, editableMaterials);

                    foreach (var entry in manifest.entries)
                    {
                        var screenAsset = RequireAsset<GameObject>(ScreenPrefabPath(entry.name));
                        var screen = (GameObject)PrefabUtility.InstantiatePrefab(screenAsset, preview);
                        screen.name = "Screen_" + entry.name;
                        screen.transform.SetParent(cockpit.transform, false);
                        screen.transform.localPosition = ToUnity(entry.position);
                        screen.transform.localRotation = Quaternion.LookRotation(ToUnity(entry.normal), ToUnity(entry.up));
                        screen.transform.localScale = Vector3.one;
                    }
                    SavePrefab(cockpit, CockpitPrefab);
                }
                finally { Object.DestroyImmediate(cockpit); }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }

            var report = ValidateAssets(manifest);
            FinishReport(report, before);
            WriteReport(report);
            return JsonUtility.ToJson(report);
        }

        public static string Validate()
        {
            var before = SceneSnapshot.Capture();
            var report = ValidateAssets(ReadManifest());
            FinishReport(report, before);
            WriteReport(report);
            return JsonUtility.ToJson(report);
        }

        public static string UpdateArchitectureOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Architecture update requires an idle editor outside Play Mode.");

            var before = SceneSnapshot.Capture();
            if (!File.Exists(CockpitPrefab)) return Build();
            RequireAsset<GameObject>(CockpitPrefab);
            var manifest = ReadManifest();
            EnsureFolder(AssetRoot + "/Materials");
            var cockpit = PrefabUtility.LoadPrefabContents(CockpitPrefab);
            try
            {
                var architectureChildren = cockpit.transform.Cast<Transform>()
                    .Where(child => child.name == "Architecture").ToArray();
                if (architectureChildren.Length != 1)
                    throw new InvalidDataException("The cockpit prefab must have exactly one direct child named Architecture.");

                var previous = architectureChildren[0];
                var position = previous.localPosition;
                var rotation = previous.localRotation;
                var scale = previous.localScale;
                var siblingIndex = previous.GetSiblingIndex();
                var active = previous.gameObject.activeSelf;

                var replacement = InstantiateModel(manifest.architecture, cockpit.scene, cockpit.transform);
                replacement.name = "Architecture";
                AssignEditableMaterials(replacement, manifest, new Dictionary<string, Material>(StringComparer.Ordinal));
                replacement.transform.localPosition = position;
                replacement.transform.localRotation = rotation;
                replacement.transform.localScale = scale;
                replacement.SetActive(active);
                Object.DestroyImmediate(previous.gameObject);
                replacement.transform.SetSiblingIndex(siblingIndex);

                // The existing screen instances and their overrides are deliberately left untouched.
                SavePrefab(cockpit, CockpitPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(cockpit); }

            var report = ValidateAssets(manifest);
            FinishReport(report, before);
            WriteReport(report);
            return JsonUtility.ToJson(report);
        }

        private static AssemblyManifest ReadManifest()
        {
            var manifest = JsonUtility.FromJson<AssemblyManifest>(File.ReadAllText(AssetRoot + "/Models/display_assembly.json"));
            if (manifest == null || manifest.entries == null || manifest.entries.Length != ScreenNames.Length ||
                !manifest.entries.Select(entry => entry.name).OrderBy(name => name).SequenceEqual(ScreenNames.OrderBy(name => name)))
                throw new InvalidDataException("The manifest must contain the five expected cockpit displays exactly once.");
            foreach (var entry in manifest.entries)
            {
                if (entry.position == null || entry.position.Length != 3 || entry.normal == null || entry.normal.Length != 3 || entry.up == null || entry.up.Length != 3)
                    throw new InvalidDataException("Invalid transform for " + entry.name);
                RequireAsset<Texture2D>(AssetRoot + "/Textures/" + entry.texture);
            }
            return manifest;
        }

        private static T RequireAsset<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new FileNotFoundException("Asset has not imported successfully: " + path);
            return asset;
        }

        private static GameObject NewRoot(string name, Scene scene)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static GameObject InstantiateModel(string file, Scene scene, Transform parent)
        {
            var asset = RequireAsset<GameObject>(AssetRoot + "/Models/" + file);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.transform.SetParent(parent, false);
            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
                child.name = NormalizeName(child.name);
            return instance;
        }

        private static void AssignEditableMaterials(GameObject geometry, AssemblyManifest manifest, Dictionary<string, Material> cache)
        {
            foreach (var renderer in geometry.GetComponentsInChildren<Renderer>(true))
            {
                var sourceMaterials = renderer.sharedMaterials;
                for (int index = 0; index < sourceMaterials.Length; index++)
                {
                    var source = sourceMaterials[index];
                    if (!source) throw new InvalidDataException("Missing imported material on " + renderer.name);
                    var name = NormalizeName(source.name);
                    if (!cache.TryGetValue(name, out var editable))
                    {
                        var path = AssetRoot + "/Materials/" + name + ".mat";
                        editable = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (!editable)
                        {
                            editable = new Material(source) { name = name };
                            AssetDatabase.CreateAsset(editable, path);
                        }
                        var display = manifest.entries.FirstOrDefault(entry => name == "CRT_" + entry.name);
                        if (display != null)
                        {
                            var texture = RequireAsset<Texture2D>(AssetRoot + "/Textures/" + display.texture);
                            if (!editable.HasProperty("baseColorTexture") || !editable.HasProperty("emissiveTexture"))
                                throw new InvalidDataException("Imported CRT shader is missing its glTF texture properties: " + name);
                            editable.SetTexture("baseColorTexture", texture);
                            editable.SetTexture("emissiveTexture", texture);
                            EditorUtility.SetDirty(editable);
                        }
                        AssetDatabase.SaveAssetIfDirty(editable);
                        cache.Add(name, editable);
                    }
                    sourceMaterials[index] = editable;
                }
                renderer.sharedMaterials = sourceMaterials;
            }
        }

        private static void SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success) throw new IOException("Could not save prefab: " + path);
        }

        private static ValidationReport ValidateAssets(AssemblyManifest manifest)
        {
            var report = new ValidationReport { generatedUtc = DateTime.UtcNow.ToString("O") };
            foreach (var entry in manifest.entries)
                CheckPrefab(ScreenPrefabPath(entry.name), entry, manifest, report);
            CheckPrefab(CockpitPrefab, null, manifest, report);
            return report;
        }

        private static void CheckPrefab(string path, DisplayEntry display, AssemblyManifest manifest, ValidationReport report)
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(path))
            {
                report.issues.Add("Missing prefab: " + path);
                return;
            }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var check = new PrefabCheck { path = path };
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                var filters = root.GetComponentsInChildren<MeshFilter>(true);
                check.meshCount = filters.Length;
                check.rendererCount = renderers.Length;
                check.screenCount = renderers.Count(renderer => renderer.name.StartsWith("SCREEN_", StringComparison.Ordinal));
                var materialPaths = new HashSet<string>();
                var texturePaths = new HashSet<string>();
                if (root.transform.localPosition.sqrMagnitude > 0.000001f || Quaternion.Angle(root.transform.localRotation, Quaternion.identity) > 0.001f ||
                    (root.transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
                    report.issues.Add(path + ": root must have zero position, identity rotation and unit scale.");
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                        report.issues.Add(path + ": missing script on " + transform.name);
                }
                foreach (var filter in filters)
                {
                    if (!filter.sharedMesh || filter.sharedMesh.vertexCount == 0)
                        report.issues.Add(path + ": missing or empty mesh on " + filter.name);
                }
                var bounds = new Bounds(root.transform.position, Vector3.zero);
                bool hasBounds = false;
                foreach (var renderer in renderers)
                {
                    if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                    else bounds.Encapsulate(renderer.bounds);
                    if (renderer.sharedMaterials.Length == 0) report.issues.Add(path + ": no materials on " + renderer.name);
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (!material) { report.issues.Add(path + ": missing material on " + renderer.name); continue; }
                        if (!material.shader || !material.shader.isSupported)
                            report.issues.Add(path + ": unsupported shader on " + material.name);
                        var materialPath = AssetDatabase.GetAssetPath(material);
                        materialPaths.Add(materialPath);
                        if (!materialPath.StartsWith(AssetRoot + "/Materials/", StringComparison.Ordinal) || !materialPath.EndsWith(".mat", StringComparison.Ordinal))
                            report.issues.Add(path + ": material is not an editable standalone asset: " + material.name);
                        var entry = manifest.entries.FirstOrDefault(candidate => material.name == "CRT_" + candidate.name);
                        if (entry == null) continue;
                        foreach (string property in new[] { "baseColorTexture", "emissiveTexture" })
                        {
                            var texture = material.HasProperty(property) ? material.GetTexture(property) : null;
                            var texturePath = texture ? AssetDatabase.GetAssetPath(texture) : "";
                            texturePaths.Add(texturePath);
                            if (texturePath != AssetRoot + "/Textures/" + entry.texture)
                                report.issues.Add(path + ": incorrect external " + property + " on " + material.name);
                        }
                    }
                }
                check.boundsCenter = Components(bounds.center);
                check.boundsSize = Components(bounds.size);
                check.materialAssets = materialPaths.OrderBy(value => value).ToArray();
                check.screenTextureAssets = texturePaths.OrderBy(value => value).ToArray();
                if (display != null)
                {
                    if (check.screenCount != 1 || check.meshCount != 8)
                        report.issues.Add(path + ": expected one display and eight complete housing/label/screen meshes.");
                    if (bounds.size.x < display.width - 0.01f || bounds.size.x > display.width + 0.30f ||
                        bounds.size.y < display.height - 0.01f || bounds.size.y > display.height + 0.30f || bounds.size.z > 0.30f)
                        report.issues.Add(path + ": display bounds do not match the metre-scale export.");
                    if (texturePaths.Count != 1) report.issues.Add(path + ": expected exactly one CRT texture.");
                }
                else
                {
                    foreach (var entry in manifest.entries)
                    {
                        var child = root.transform.Find("Screen_" + entry.name);
                        if (!child) { report.issues.Add(path + ": missing nested display " + entry.name); continue; }
                        if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject) &&
                            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject) == ScreenPrefabPath(entry.name))
                            check.nestedScreenCount++;
                        else report.issues.Add(path + ": lost nested prefab connection for " + entry.name);
                        if ((child.localPosition - ToUnity(entry.position)).sqrMagnitude > 0.000001f ||
                            Vector3.Angle(child.forward, ToUnity(entry.normal)) > 0.05f || Vector3.Angle(child.up, ToUnity(entry.up)) > 0.05f ||
                            (child.localScale - Vector3.one).sqrMagnitude > 0.000001f)
                            report.issues.Add(path + ": incorrect assembled transform for " + entry.name);
                    }
                    if (check.screenCount != 5 || check.nestedScreenCount != 5 || texturePaths.Count != 5)
                        report.issues.Add(path + ": expected five displays, five nested prefab links and five textures.");
                    if (bounds.size.x < 2f || bounds.size.x > 3f || bounds.size.y < 1.8f || bounds.size.y > 2.7f || bounds.size.z < 2f || bounds.size.z > 3f)
                        report.issues.Add(path + ": cabin bounds are outside the expected compact metre-scale enclosure.");
                }
                report.prefabs.Add(check);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void FinishReport(ValidationReport report, SceneSnapshot before)
        {
            var after = SceneSnapshot.Capture();
            report.activeSceneBefore = before.activePath;
            report.activeSceneAfter = after.activePath;
            report.openScenesBefore = before.scenes;
            report.openScenesAfter = after.scenes;
            report.openScenesPreserved = before.Matches(after);
            if (!report.openScenesPreserved) report.issues.Add("The open scene state changed during prefab processing.");
            report.valid = report.issues.Count == 0;
        }

        private static void WriteReport(ValidationReport report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ValidationPath));
            File.WriteAllText(ValidationPath, JsonUtility.ToJson(report, true));
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static string NormalizeName(string name) { return BlenderSuffix.Replace(name, ""); }
        private static string ScreenPrefabPath(string name) { return AssetRoot + "/Prefabs/Screens/Screen_" + name + ".prefab"; }
        private static Vector3 ToUnity(float[] v) { return new Vector3(-v[0], v[2], -v[1]); }
        private static float[] Components(Vector3 v) { return new[] { v.x, v.y, v.z }; }
    }
}
