using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LudumDare60.AssemblyDemo.Editor
{
    /// <summary>Imports the reference cutouts and creates the playable first assembly level.</summary>
    public static class Level01AssemblyTools
    {
        public const string ArtPath = "Assets/AssemblyDemo/Art/Level01";
        public const string PrefabPath = "Assets/AssemblyDemo/Prefabs/Level01";
        public const string ScenePath = AssemblyDemoTools.ScenePath;
        private static readonly string[] AssetNames = { "ControlCabinet", "MachineCore", "PressureTank", "SupportFrame" };

        [MenuItem("Tools/Assembly Demo/Install Level 01 Artwork", priority = 1)]
        public static void InstallLevel01()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before installing the level.");
            foreach (string name in AssetNames)
                if (!File.Exists($"{ArtPath}/{name}.png")) throw new FileNotFoundException($"Missing level artwork: {name}.png");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Directory.CreateDirectory(PrefabPath);
            Directory.CreateDirectory("Assets/AssemblyDemo/Scenes");
            AssetDatabase.Refresh();
            var sprites = new Sprite[AssetNames.Length];
            for (int i = 0; i < sprites.Length; i++) sprites[i] = ImportSprite(AssetNames[i]);

            // Preserve the previous scene as a reviewable backup, outside the Assets import tree.
            string backup = Path.GetFullPath("../output/assembly-level01/AssemblyDemo.before-artwork.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (File.Exists(ScenePath) && !File.Exists(backup)) File.Copy(ScenePath, backup);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "AssemblyDemo";
            RenderSettings.skybox = null;
            var cameraObject = new GameObject("Assembly Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 3.45f;
            camera.transform.position = new Vector3(0, -0.10f, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.105f, 0.12f, 0.135f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 30;
            camera.allowHDR = false;
            cameraObject.AddComponent<AudioListener>();

            var root = new GameObject("Level 01 - Industrial Assembly");
            var controller = root.AddComponent<AssemblyDemoController>();
            // Sizes preserve the proportions of the four objects in the supplied reference.
            var cabinet = CreatePart(root.transform, 0, "01 / CONTROL", sprites[0], 3.05f,
                new Vector2(-3.80f, 0.90f), new[] { 90f }, new[] { "CONTROL" });
            var core = CreatePart(root.transform, 1, "02 / CORE", sprites[1], 3.40f,
                new Vector2(0, 0.55f), new[] { 270f, 90f, 180f }, new[] { "CONTROL", "PRESSURE", "MOUNT" });
            var tank = CreatePart(root.transform, 2, "03 / TANK", sprites[2], 2.80f,
                new Vector2(4.00f, 0.65f), new[] { 270f }, new[] { "PRESSURE" });
            var frame = CreatePart(root.transform, 3, "04 / BASE", sprites[3], 0.65f,
                new Vector2(2.40f, -2.30f), new[] { 0f }, new[] { "MOUNT" });

            // A repeatable introductory puzzle: a quarter-turn cabinet, an inverted tank,
            // and an offset horizontal base. Awake captures these exact poses for R reset.
            cabinet.RotationDegrees = 90f;
            tank.RotationDegrees = 180f;

            // The antenna extends beyond the core housing. Its left connector belongs on
            // the housing at mid-height, rather than on the far tip of that antenna.
            core.portDistances[0] = LeftHousingDistance(sprites[1], core.artworkHeight);
            frame.portDistances[0] = TopRailDistance(sprites[3], frame.artworkHeight);
            cabinet.artworkOffset = new Vector2(0, (cabinet.artworkHeight - core.artworkHeight) * 0.5f);
            tank.artworkOffset = new Vector2(0, (tank.artworkHeight - core.artworkHeight) * 0.5f);
            cabinet.Rebuild();
            core.Rebuild();
            tank.Rebuild();
            frame.Rebuild();

            controller.Modules = new[] { cabinet, core, tank, frame };
            controller.MoveSpeed = 2.0f;
            controller.RotationSpeed = 65f;
            controller.ViewCamera = null;
            for (int i = 0; i < controller.Modules.Length; i++)
            {
                var module = controller.Modules[i];
                PrefabUtility.SaveAsPrefabAssetAndConnect(module.gameObject,
                    $"{PrefabPath}/{AssetNames[i]}.prefab", InteractionMode.AutomatedAction);
            }
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, $"{PrefabPath}/Level01Assembly.prefab", InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene, ScenePath);
            var buildScenes = new System.Collections.Generic.List<EditorBuildSettingsScene> {
                new EditorBuildSettingsScene(ScenePath, true)
            };
            foreach (var existing in EditorBuildSettings.scenes)
                if (existing.path != ScenePath) buildScenes.Add(existing);
            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            Debug.Log("[Level01] Four artwork modules installed. 1-4 / Tab / click: select; WASD: move; Q/E: rotate; Shift: fine; X: detach; R: reset.");
        }

        private static AssemblyModule CreatePart(Transform parent, int index, string label, Sprite sprite,
            float height, Vector2 position, float[] angles, string[] keys)
        {
            var go = new GameObject(AssetNames[index]);
            go.transform.SetParent(parent, false);
            var module = go.AddComponent<AssemblyModule>();
            module.moduleLabel = label;
            module.artwork = sprite;
            module.artworkHeight = height;
            module.artworkOffset = Vector2.zero;
            module.portAngles = angles;
            module.portKeys = keys;
            module.radius = height * 0.5f;
            module.portDistances = new float[angles.Length];
            float halfWidth = height * sprite.bounds.size.x / sprite.bounds.size.y * 0.5f;
            for (int i = 0; i < angles.Length; i++)
                module.portDistances[i] = (angles[i] == 90 || angles[i] == 270 ? halfWidth : height * 0.5f) + 0.015f;
            module.Position = position;
            module.RotationDegrees = 0;
            module.Rebuild();
            return module;
        }

        private static Sprite ImportSprite(string name)
        {
            string pngPath = $"{ArtPath}/{name}.png";
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 200;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            var pixels = texture.GetPixels32();
            int minX = texture.width, minY = texture.height, maxX = -1, maxY = -1;
            int transparent = 0;
            for (int y = 0; y < texture.height; y++)
                for (int x = 0; x < texture.width; x++)
                {
                    byte alpha = pixels[y * texture.width + x].a;
                    if (alpha < 8) transparent++;
                    if (alpha < 24) continue;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }
            if (maxX < minX || transparent < pixels.Length / 100)
                throw new InvalidOperationException($"{name} must contain visible artwork and an actual transparent background.");
            // A trimmed Sprite rect keeps source PNG pixels untouched and makes world sizes precise.
            minX = Math.Max(0, minX - 2); minY = Math.Max(0, minY - 2);
            maxX = Math.Min(texture.width - 1, maxX + 2); maxY = Math.Min(texture.height - 1, maxY + 2);
            var sprite = Sprite.Create(texture, new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1),
                new Vector2(0.5f, 0.5f), 200, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            string spritePath = $"{ArtPath}/{name}.sprite.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(sprite, existing);
                UnityEngine.Object.DestroyImmediate(sprite);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(sprite, spritePath);
            return sprite;
        }

        private static float LeftHousingDistance(Sprite sprite, float worldHeight)
        {
            var rect = sprite.rect;
            var texture = sprite.texture;
            var pixels = texture.GetPixels32();
            var edges = new System.Collections.Generic.List<int>();
            // Sample a band around the connector's height; use the median to ignore thin protrusions.
            for (int row = 0; row < 11; row++)
            {
                int y = Mathf.RoundToInt(rect.yMin + rect.height * (0.45f + row * 0.01f));
                for (int x = (int)rect.xMin; x < rect.center.x; x++)
                    if (pixels[y * texture.width + x].a >= 128) { edges.Add(x); break; }
            }
            edges.Sort();
            float left = edges.Count > 0 ? edges[edges.Count / 2] : rect.xMin;
            return (rect.center.x - left) * worldHeight / rect.height + 0.015f;
        }

        private static float TopRailDistance(Sprite sprite, float worldHeight)
        {
            var rect = sprite.rect;
            var texture = sprite.texture;
            var pixels = texture.GetPixels32();
            var edges = new System.Collections.Generic.List<int>();
            // The bolt on the far right sits above the load-bearing horizontal rail.
            for (int column = 0; column < 11; column++)
            {
                int x = Mathf.RoundToInt(rect.xMin + rect.width * (0.45f + column * 0.01f));
                for (int y = (int)rect.yMax - 1; y > rect.center.y; y--)
                    if (pixels[y * texture.width + x].a >= 128) { edges.Add(y); break; }
            }
            edges.Sort();
            float top = edges.Count > 0 ? edges[edges.Count / 2] : rect.yMax;
            return (top - rect.center.y) * worldHeight / rect.height + 0.015f;
        }

        [MenuItem("Tools/Assembly Demo/Play Level 01", priority = 2)]
        public static void PlayLevel01()
        {
            if (EditorApplication.isPlaying) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying = true;
        }
    }
}
