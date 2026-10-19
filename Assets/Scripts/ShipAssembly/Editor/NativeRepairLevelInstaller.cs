using System;
using System.IO;
using System.Linq;
using LudumDare60.AssemblyDemo;
using LudumDare60.ReturnWindow;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LudumDare60.ShipAssembly.Editor
{
    /// <summary>Builds native repair levels from recorded engine and life-support poses.</summary>
    public static class NativeRepairLevelInstaller
    {
        public const string EnginePrefab = "Assets/AssemblyDemo/Prefabs/PropulsionEngineAssembly.prefab";
        public const string LifePrefab = "Assets/AssemblyDemo/Prefabs/LifeSupportAssembly.prefab";
        public const string PreviewScene = "Assets/AssemblyDemo/Scenes/EngineAndLifeAssembly.unity";
        public const string EngineScene = "Assets/AssemblyDemo/Scenes/PropulsionEngineAssembly.unity";
        public const string LifeScene = "Assets/AssemblyDemo/Scenes/LifeSupportAssembly.unity";
        public const string EngineMeshAsset = "Assets/ArtAssets/3DModels/raptor_low.glb";
        public static readonly string[] EngineNames = { "BezierCurve.011", "Cube.007", "Cylinder.001" };
        // Screenshots 2026-10-20 01:54:15--01:55:42, in source-prefab local coordinates.
        public static readonly Vector3[] EngineStart = {
            new Vector3(2.64f,1.47f,.23f), new Vector3(1.13f,5.12f,.57f), new Vector3(1.7f,-7.391827f,-1.037691f)
        };
        public static readonly Vector3[] EngineTarget = {
            EngineStart[0], new Vector3(1.13f,2.09f,-.12f), new Vector3(1.7f,-2.26f,.63f)
        };
        public static readonly Vector3[] EngineStartEuler = {
            new Vector3(-73.322f,84.553f,124.291f), new Vector3(0,136.361f,180), new Vector3(-.056f,79.308f,179.704f)
        };
        public static readonly Vector3[] EngineTargetEuler = {
            new Vector3(-73.322f,84.553f,124.291f), new Vector3(0,136.361f,180), new Vector3(0,100.692f,0)
        };
        public static readonly Vector3[] EngineScales = {
            Vector3.one*.6111977f, new Vector3(-.2371762f,-.2371762f,-.2783778f), Vector3.one*.2447915f
        };
        public static readonly string[] LifeNames = { "CUT_test", "CUT_test.001", "CUT_test.003", "CUT_test.004" };
        public static readonly Vector3[] LifeStart = {
            new Vector3(.071f,0,0), new Vector3(-1.207f,.155f,0), new Vector3(-1.335f,-.004f,0), new Vector3(.22f,.143f,0)
        };
        public static readonly Vector3[] LifeTarget = {
            LifeStart[0], new Vector3(.056f,.023f,0), new Vector3(-.196f,.011f,0), new Vector3(.063f,.153f,0)
        };
        public static readonly Vector3[] LifeStartEuler = { Vector3.zero, new Vector3(0,0,90), Vector3.zero, new Vector3(0,0,90) };

        [MenuItem("Tools/Ship Assembly/Build Engine And Revised Life Support Levels")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before rebuilding levels.");
            StageUtility.GoToMainStage();
            Directory.CreateDirectory("Logs/NativeRepairLevels");
            EditorSceneManager.SaveOpenScenes();
            Backup("Assets/Prefabs/引擎.prefab", "engine-source.prefab");
            Backup("Assets/Prefabs/生命装置.prefab", "life-source.prefab");
            Backup(LifePrefab,"life-previous.prefab");
            Backup("Assets/ShipAssembly/Recipes/PropulsionEngine.asset","engine-previous-recipe.asset");
            Backup("Assets/ShipAssembly/Recipes/LifeSupport.asset","life-previous-recipe.asset");
            BuildEngine();
            Build("Assets/Prefabs/生命装置.prefab",LifePrefab,"LIFE SUPPORT",LifeNames,new[] { "BODY", "RESERVOIR", "FILTER", "BASE RAIL" },
                LifeStart,LifeTarget,LifeStartEuler,new Vector3[4],Quaternion.Euler(0,180,0));
            BindRecipe("PropulsionEngine",EnginePrefab);
            BindRecipe("LifeSupport",LifePrefab);
            BuildPreview(PreviewScene,0);
            BuildPreview(EngineScene,0);
            BuildPreview(LifeScene,1);
            AssetDatabase.SaveAssets();
            Debug.Log("[NativeRepairLevels] Engine (3 parts) and life support (4 parts) built; SHIP recipes updated.");
        }

        [MenuItem("Tools/Ship Assembly/Rebuild Updated Low Poly Engine Only")]
        public static void InstallEngine()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before rebuilding the engine.");
            StageUtility.GoToMainStage();
            Directory.CreateDirectory("Logs/EngineRefresh");
            foreach (var path in new[] { "Assets/Prefabs/引擎.prefab", EnginePrefab, "Assets/ShipAssembly/Recipes/PropulsionEngine.asset" })
            {
                var backup = "Logs/EngineRefresh/"+Path.GetFileName(path)+".before";
                if (File.Exists(path) && !File.Exists(backup)) File.Copy(path,backup);
            }
            BuildEngine();
            BindRecipe("PropulsionEngine",EnginePrefab);
            foreach (var path in new[] { PreviewScene,EngineScene,LifeScene }) RefreshEnginePreview(path);
            AssetDatabase.SaveAssets();
            Debug.Log("[NativeRepairLevels] Low-poly engine rebuilt from 2026-10-20 screenshots; existing preview UI and other repair assets preserved.");
        }

        private static void BuildEngine()
        {
            Build("Assets/Prefabs/引擎.prefab",EnginePrefab,"PROPULSION ENGINE",EngineNames,new[] { "ENGINE BODY", "END PLATE", "NOZZLE" },
                EngineStart,EngineTarget,EngineStartEuler,EngineTargetEuler,Quaternion.Euler(0,90,0)*Quaternion.Euler(90,0,0));
        }

        private static void RefreshEnginePreview(string path)
        {
            if (!File.Exists(path)) return;
            var scene = EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var layout in root.GetComponentsInChildren<DeviceAssemblyLayout>(true))
                    {
                        var source = PrefabUtility.GetCorrespondingObjectFromSource(layout.gameObject);
                        if (AssetDatabase.GetAssetPath(source) != EnginePrefab) continue;
                        layout.transform.localPosition = new Vector3(-layout.viewCenter.x,-layout.viewCenter.y,0);
                    }
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.CloseScene(scene,true); }
        }

        private static void Backup(string path,string name)
        {
            string destination = "Logs/NativeRepairLevels/"+name;
            if (File.Exists(path) && !File.Exists(destination)) File.Copy(path,destination);
        }

        private static Transform Part(GameObject source,string name) => source.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);

        private static void Build(string sourcePath,string prefabPath,string label,string[] names,string[] labels,
            Vector3[] start,Vector3[] target,Vector3[] startEuler,Vector3[] targetEuler,Quaternion basis)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new FileNotFoundException(sourcePath);
            var root = new GameObject(label + " Assembly"); root.SetActive(false);
            try
            {
                var assembly = root.AddComponent<AssemblyDemoController>();
                assembly.Modules = new AssemblyModule[names.Length]; assembly.LockWhenComplete = true;
                assembly.MoveSpeed = 1.8f; assembly.RotationSpeed = 70;
                var layout = root.AddComponent<DeviceAssemblyLayout>(); layout.assembly = assembly; layout.deviceLabel = label; layout.modelToBoard = basis;
                layout.nativeStartPositions = start; layout.nativeTargetPositions = target;
                layout.nativeStartRotations = startEuler.Select(Quaternion.Euler).ToArray();
                layout.nativeTargetRotations = targetEuler.Select(Quaternion.Euler).ToArray();
                layout.assembledLocalPositions = new Vector3[names.Length];
                layout.assembledModelPositions = new Vector3[names.Length];
                layout.assembledModelRotations = new Quaternion[names.Length];
                var visuals = new Transform[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    var partObject = new GameObject(names[i]); partObject.transform.SetParent(root.transform,false);
                    var module = partObject.AddComponent<AssemblyModule>(); assembly.Modules[i] = module; module.moduleLabel = labels[i];
                    var pivot = new GameObject("Native model").transform; pivot.SetParent(partObject.transform,false); visuals[i] = pivot;
                    var original = Part(source,names[i]);
                    var clone = Object.Instantiate(original.gameObject,pivot); clone.name = names[i];
                    clone.transform.localPosition = Vector3.zero; clone.transform.localRotation = Quaternion.identity;
                    clone.transform.localScale = original.localScale; // Includes the engine end plate's three negative scale axes.
                    foreach (var renderer in clone.GetComponentsInChildren<MeshRenderer>(true)) renderer.enabled = true;
                    module.modelVisual = pivot;
                    module.portAngles = new float[i == 0 ? names.Length-1 : 1];
                    module.portDistances = new float[module.portAngles.Length]; module.portKeys = new string[module.portAngles.Length];
                }
                // Fit both the scattered and assembled native models; the source root's presentation scale is irrelevant to the puzzle.
                Bounds union = PoseBounds(assembly,layout,visuals,1,false);
                union.Encapsulate(PoseBounds(assembly,layout,visuals,1,true));
                layout.modelScale = Mathf.Min(5,14f/Mathf.Max(.001f,union.size.x),6.4f/Mathf.Max(.001f,union.size.y));
                Bounds targetBounds = PoseBounds(assembly,layout,visuals,layout.modelScale,true);
                var initialBounds = PoseBounds(assembly,layout,visuals,layout.modelScale,false);
                initialBounds.Encapsulate(targetBounds); layout.viewCenter = initialBounds.center;
                for (int i = 0; i < names.Length; i++)
                {
                    var module = assembly.Modules[i]; var visual = visuals[i];
                    var bounds = LocalBounds(module.transform,visual);
                    module.modelBoundsCenter = new Vector2(bounds.center.x,bounds.center.y);
                    module.modelBoundsSize = new Vector2(Mathf.Max(.14f,bounds.size.x),Mathf.Max(.14f,bounds.size.y));
                    module.radius = Mathf.Max(.1f,bounds.extents.magnitude);
                    Vector3 goal = basis * target[i] * layout.modelScale;
                    layout.assembledLocalPositions[i] = new Vector3(goal.x,goal.y,0);
                    layout.assembledModelPositions[i] = new Vector3(0,0,goal.z);
                    layout.assembledModelRotations[i] = basis * layout.nativeTargetRotations[i];
                }
                for (int i = 1; i < names.Length; i++)
                {
                    Vector2 delta = layout.assembledLocalPositions[i]-layout.assembledLocalPositions[0];
                    if (delta.magnitude < .01f) throw new InvalidOperationException("Cannot derive a connector between coincident pivots.");
                    float angle = AssemblySnapSolver.ClockwiseAngle(delta);
                    SetPort(assembly.Modules[0],i-1,angle,delta.magnitude*.5f,labels[i]);
                    SetPort(assembly.Modules[i],0,angle+180,delta.magnitude*.5f,labels[i]);
                }
                var effect = root.AddComponent<Xray>(); var originalEffect = source.GetComponent<Xray>();
                if (originalEffect != null) EditorUtility.CopySerialized(originalEffect,effect);
                effect.wireframeSourceRoot = root.transform; effect.hideOriginalModel = true; effect.showOutline = true;
                effect.outlineColor = effect.visibleColor = Color.white;
                root.SetActive(true); PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Bounds PoseBounds(AssemblyDemoController assembly,DeviceAssemblyLayout layout,Transform[] visuals,float scale,bool solved)
        {
            bool first = true; var bounds = new Bounds();
            for (int i = 0; i < visuals.Length; i++)
            {
                Vector3 position = layout.modelToBoard * (solved ? layout.nativeTargetPositions[i] : layout.nativeStartPositions[i]) * scale;
                Quaternion rotation = solved ? layout.nativeTargetRotations[i] : layout.nativeStartRotations[i];
                var relative = layout.modelToBoard * rotation * Quaternion.Inverse(layout.modelToBoard * layout.nativeTargetRotations[i]);
                var up = relative * Vector3.up; float turn = Mathf.Atan2(-up.x,up.y)*Mathf.Rad2Deg;
                assembly.Modules[i].transform.localPosition = new Vector3(position.x,position.y,0);
                assembly.Modules[i].transform.localRotation = Quaternion.Euler(0,0,turn);
                visuals[i].localPosition = new Vector3(0,0,position.z);
                visuals[i].localRotation = Quaternion.Inverse(Quaternion.Euler(0,0,turn))*layout.modelToBoard*rotation;
                visuals[i].localScale = Vector3.one*scale;
                foreach (var renderer in visuals[i].GetComponentsInChildren<MeshRenderer>(true))
                    if (first) { bounds = renderer.bounds; first = false; } else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        private static Bounds LocalBounds(Transform module,Transform visual)
        {
            bool first = true; var bounds = new Bounds();
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                var b = filter.sharedMesh.bounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    var p = module.InverseTransformPoint(filter.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z))));
                    if (first) { bounds = new Bounds(p,Vector3.zero); first = false; } else bounds.Encapsulate(p);
                }
            }
            return bounds;
        }
        private static void SetPort(AssemblyModule module,int index,float angle,float distance,string key)
        {
            module.portAngles[index] = AssemblySnapSolver.NormalizeAngle(angle); module.portDistances[index] = distance; module.portKeys[index] = key;
        }
        private static void BindRecipe(string name,string path)
        {
            var recipe = AssetDatabase.LoadAssetAtPath<AssemblyRecipe>("Assets/ShipAssembly/Recipes/"+name+".asset");
            if (recipe == null) throw new FileNotFoundException(name);
            recipe.PuzzlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var layout = recipe.PuzzlePrefab.GetComponent<DeviceAssemblyLayout>();
            recipe.PuzzleOffset = new Vector3(-layout.viewCenter.x,-layout.viewCenter.y,0);
            recipe.OrthographicSize = 5.4f; EditorUtility.SetDirty(recipe);
        }

        private static void BuildPreview(string path,int selected)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                var root = new GameObject("Engine And Life Support Playtest"); root.SetActive(false); SceneManager.MoveGameObjectToScene(root,scene);
                var preview = root.AddComponent<NativeRepairLevelPreview>(); preview.levels = new GameObject[2];
                string[] paths = { EnginePrefab,LifePrefab };
                for (int i = 0; i < 2; i++)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]),scene);
                    instance.transform.SetParent(root.transform,false);
                    var layout = instance.GetComponent<DeviceAssemblyLayout>(); instance.transform.localPosition = new Vector3(-layout.viewCenter.x,-layout.viewCenter.y,0);
                    preview.levels[i] = instance; instance.SetActive(i == selected);
                }
                var cameraObject = new GameObject("Assembly Camera",typeof(Camera),typeof(AudioListener)); SceneManager.MoveGameObjectToScene(cameraObject,scene);
                preview.viewCamera = cameraObject.GetComponent<Camera>(); cameraObject.tag = "MainCamera"; cameraObject.transform.position = new Vector3(0,0,-10);
                preview.viewCamera.orthographic = true; preview.viewCamera.orthographicSize = 5.4f;
                preview.viewCamera.clearFlags = CameraClearFlags.SolidColor; preview.viewCamera.backgroundColor = new Color(.018f,.028f,.032f);
                var canvas = new GameObject("Playtest Controls",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster)); SceneManager.MoveGameObjectToScene(canvas,scene);
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600,900);
                preview.title = Text(canvas.transform,"Title",new Vector2(.04f,.92f),new Vector2(.96f,.99f),30);
                preview.instructions = Text(canvas.transform,"Instructions",new Vector2(.04f,.02f),new Vector2(.96f,.18f),23);
                var actions = new UnityEngine.Events.UnityAction[] { preview.SelectEngine,preview.SelectLifeSupport };
                for (int i = 0; i < 2; i++)
                {
                    var go = new GameObject(i == 0 ? "F1 / ENGINE" : "F2 / LIFE SUPPORT",typeof(RectTransform),typeof(Image),typeof(Button)); go.transform.SetParent(canvas.transform,false);
                    var rect = go.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(.04f+i*.47f,.85f); rect.anchorMax = new Vector2(.49f+i*.47f,.91f); rect.offsetMin = rect.offsetMax = Vector2.zero;
                    go.GetComponent<Image>().color = new Color(.07f,.14f,.13f,.96f);
                    var text = Text(go.transform,"Label",Vector2.zero,Vector2.one,24); text.text = go.name; text.alignment = TextAlignmentOptions.Center;
                    UnityEventTools.AddPersistentListener(go.GetComponent<Button>().onClick,actions[i]);
                }
                var events = new GameObject("EventSystem",typeof(UnityEngine.EventSystems.EventSystem),typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule)); SceneManager.MoveGameObjectToScene(events,scene);
                preview.selectedLevel = selected; root.SetActive(true); preview.SelectLevel(selected);
                EditorSceneManager.SaveScene(scene,path);
            }
            finally { EditorSceneManager.CloseScene(scene,true); }
        }
        private static TMP_Text Text(Transform parent,string name,Vector2 min,Vector2 max,float size)
        {
            var go = new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)); var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent,false);
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<TextMeshProUGUI>(); text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Terminal/Glass_TTY_VT220 SDF.asset");
            text.fontSize = size; text.color = new Color(.7f,.94f,.82f); text.raycastTarget = false; return text;
        }
    }
}
