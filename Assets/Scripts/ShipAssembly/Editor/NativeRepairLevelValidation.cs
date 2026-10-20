using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using LudumDare60.AssemblyDemo;
using LudumDare60.ReturnWindow;
using LudumDare60.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LudumDare60.ShipAssembly.Editor
{
    [InitializeOnLoad]
    public static class NativeRepairLevelValidation
    {
        private const string RunKey = "NativeRepairLevels.ValidateSHIP";
        private static double started;
        private static StringBuilder runtimeReport;
        static NativeRepairLevelValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(RunKey,false)) return;
                started = EditorApplication.timeSinceStartup; EditorApplication.update += CheckShip;
            };
        }
        private static void Require(bool condition,string label,StringBuilder report)
        {
            if (!condition) throw new InvalidOperationException(label);
            report.AppendLine("PASS " + label);
        }

        [MenuItem("Tools/Ship Assembly/Validate Updated Life Support And All Arrows")]
        public static void GeometryLifeOnly()
        {
            var report = new StringBuilder(); Directory.CreateDirectory("Logs/NativeRepairLevels");
            try
            {
                CheckGeometry(NativeRepairLevelInstaller.LifePrefab,"life",NativeRepairLevelInstaller.LifeNames,report);
                foreach (string prefab in new[] { "EnergyAssembly", "NavigationAssembly", "PropulsionEngineAssembly", "LifeSupportAssembly" })
                {
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                    try
                    {
                        var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssemblyDemo/Prefabs/"+prefab+".prefab"),scene);
                        foreach (var module in root.GetComponent<AssemblyDemoController>().Modules)
                        {
                            module.Rebuild();
                            for (int port = 0; port < module.PortCount; port++)
                            {
                                var shaft = module.GetComponentsInChildren<LineRenderer>().Single(l => l.name == "Port "+port+" shaft");
                                var head = module.GetComponentsInChildren<LineRenderer>().Single(l => l.name == "Port "+port+" arrowhead");
                                var tip = (Vector2)shaft.transform.TransformPoint(shaft.GetPosition(1));
                                Require(Vector2.Distance(tip,module.PortPosition(port)) < .0001f,prefab+" "+module.name+" arrow tip matches solver "+port,report);
                                Require(Vector3.Distance(shaft.GetPosition(0),shaft.GetPosition(1)) >= AssemblyModule.GuideLength-.0001f &&
                                    shaft.widthMultiplier >= AssemblyModule.GuideShaftWidth && head.widthMultiplier >= AssemblyModule.GuideHeadWidth,
                                    prefab+" "+module.name+" readable arrow "+port,report);
                            }
                        }
                    }
                    finally { EditorSceneManager.CloseScene(scene,true); }
                }
                File.WriteAllText("Logs/NativeRepairLevels/life-v2-validation.txt",report.ToString());
                Debug.Log("[NativeRepairLevels] Updated life support and all subsystem arrows passed validation.");
            }
            catch (Exception e) { File.WriteAllText("Logs/NativeRepairLevels/life-v2-validation.txt",report+"FAIL "+e); throw; }
        }

        [MenuItem("Tools/Ship Assembly/Validate Engine And Revised Life Support Geometry")]
        public static void Geometry()
        {
            var report = new StringBuilder(); Directory.CreateDirectory("Logs/NativeRepairLevels");
            try
            {
                CheckGeometry(NativeRepairLevelInstaller.EnginePrefab,"engine",NativeRepairLevelInstaller.EngineNames,report);
                CheckGeometry(NativeRepairLevelInstaller.LifePrefab,"life",NativeRepairLevelInstaller.LifeNames,report);
                File.WriteAllText("Logs/NativeRepairLevels/geometry-validation.txt",report.ToString());
                Debug.Log("[NativeRepairLevels] Geometry validation passed\n" + report);
            }
            catch (Exception e) { File.WriteAllText("Logs/NativeRepairLevels/geometry-validation.txt",report+"FAIL "+e); throw; }
        }

        private static void CheckGeometry(string path,string name,string[] names,StringBuilder report)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),scene);
                var puzzle = instance.GetComponent<AssemblyDemoController>(); var layout = instance.GetComponent<DeviceAssemblyLayout>();
                typeof(AssemblyDemoController).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(puzzle,null);
                Require(puzzle.Modules.Length == names.Length,name+" correct part count",report);
                Require(puzzle.RequiredConnectionCount == names.Length-1,name+" correct connection count",report);
                for (int i = 0; i < names.Length; i++)
                {
                    var part = puzzle.Modules[i]; part.Rebuild();
                    Require(part.name == names[i],name+" actual mesh name "+i,report);
                    Require(Vector3.Distance(part.modelVisual.position,layout.modelToBoard*layout.nativeStartPositions[i]*layout.modelScale) < .0002f,name+" initial XYZ including depth "+i,report);
                    Require(Quaternion.Angle(part.transform.localRotation*part.modelVisual.localRotation,layout.modelToBoard*layout.nativeStartRotations[i]) < .02f,name+" initial 3D rotation "+i,report);
                    Require(Vector3.Dot(part.transform.forward,Vector3.forward) > .99999f && (part.transform.lossyScale-Vector3.one).sqrMagnitude < .000001f,name+" logical XY plane and unit scale "+i,report);
                    Require(part.ContainsLocalPoint(part.modelBoundsCenter),name+" mouse selection bounds "+i,report);
                }
                if (name == "engine")
                {
                    CheckUpdatedEngine(puzzle,report);
                    var scale = puzzle.Modules[1].modelVisual.GetChild(0).localScale;
                    Require(scale.x < 0 && scale.y < 0 && scale.z < 0,"engine end plate negative scale retained",report);
                    Require(Mathf.Abs(scale.x+.2371762f) < .00001f && Mathf.Abs(scale.z+.2783778f) < .00001f,"engine end plate scale matches screenshot",report);
                }
                else
                {
                    Require(instance.GetComponentsInChildren<MeshFilter>(true).Count(m => !m.name.StartsWith("__XRay")) == 4,"life contains only four v2 parts",report);
                    for (int i = 0; i < names.Length; i++)
                    {
                        var module = puzzle.Modules[i];
                        Require(AssetDatabase.GetAssetPath(module.modelVisual.GetChild(0).GetComponent<MeshFilter>().sharedMesh) == NativeRepairLevelInstaller.LifeMeshAsset,"life uses v2 mesh "+i,report);
                        Require(Vector3.Distance(layout.nativeTargetPositions[i],NativeRepairLevelInstaller.LifeTarget[i]) < .00001f,"life target matches 18:16 screenshot "+i,report);
                        Require(Vector3.Distance(layout.nativeStartPositions[i],NativeRepairLevelInstaller.LifeStart[i]) < .00001f &&
                            Quaternion.Angle(layout.nativeStartRotations[i],Quaternion.Euler(NativeRepairLevelInstaller.LifeStartEuler[i])) < .02f,"life scatter matches 18:17 screenshot "+i,report);
                        Require(module.modelBoundsCenter.sqrMagnitude < .000001f,"life pivot is at visible mesh center "+i,report);
                        var filter = module.modelVisual.GetChild(0).GetComponent<MeshFilter>();
                        var projected = new Bounds(); bool first = true;
                        foreach (var vertex in filter.sharedMesh.vertices)
                        {
                            Vector3 point = module.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                            if (first) { projected = new Bounds(point,Vector3.zero); first = false; } else projected.Encapsulate(point);
                        }
                        Require(Vector2.Distance(module.modelBoundsSize,new Vector2(projected.size.x,projected.size.y)) < .002f,"life selection frame fits actual projected vertices "+i,report);
                        Require(!module.ContainsLocalPoint(module.modelBoundsSize*.5f+Vector2.one*.02f),"life empty space outside mesh bounds is not selectable "+i,report);
                    }
                    Require(puzzle.Modules[0].modelBoundsSize.x/layout.modelScale < .9f,"life body no longer includes detached side geometry",report);
                }
                Require(!puzzle.TrySnapSelected() && !puzzle.IsAssemblyComplete,name+" scattered pieces do not auto-complete",report);
                CapturePuzzle(puzzle,name+"-before");
                puzzle.SelectModule(1); var start = puzzle.Selected.Position;
                puzzle.ApplyMotion(new Vector2(.2f,.3f),17); puzzle.ResetDemo();
                Require(Vector2.Distance(puzzle.Modules[1].Position,start) < .0001f,name+" reset restores scatter",report);
                if (name == "engine")
                {
                    puzzle.SelectModule(2); float turn = puzzle.Selected.RotationDegrees;
                    Require(Mathf.Abs(Mathf.DeltaAngle(0,turn)) > 175,"engine nozzle starts flipped",report);
                    puzzle.ApplyMotion(Vector2.one*.1f,35); puzzle.ResetDemo(); puzzle.SelectModule(2);
                    Require(Mathf.Abs(Mathf.DeltaAngle(turn,puzzle.Selected.RotationDegrees)) < .001f,"engine reset restores nozzle flip",report);
                    SetGoalPoses(puzzle); puzzle.SelectModule(2); puzzle.Selected.RotationDegrees = 180;
                    Require(!puzzle.TrySnapSelected(),"engine flipped nozzle is rejected at target position",report);
                }
                SetGoalPoses(puzzle);
                puzzle.SelectModule(1); puzzle.Selected.RotationDegrees += 25;
                Require(!puzzle.TrySnapSelected(),name+" wrong orientation is rejected",report);
                puzzle.Selected.RotationDegrees = 0;
                for (int i = 1; i < names.Length; i++)
                {
                    puzzle.SelectModule(i); puzzle.Selected.Position += new Vector2(.006f,.004f);
                    Require(puzzle.TrySnapSelected(),name+" keyed connector "+i,report);
                    if (i < names.Length-1) Require(!puzzle.IsAssemblyComplete,name+" partial assembly is incomplete "+i,report);
                }
                layout.PresentCompletion(); CheckTargets(puzzle,name,report);
                puzzle.InputBlocked = false; puzzle.ResetDemo(); puzzle.DetachSelected(); puzzle.ApplyMotion(Vector2.one,90);
                Require(puzzle.IsAssemblyComplete && puzzle.InputBlocked,name+" completed assembly stays locked",report);
                CapturePuzzle(puzzle,name+"-assembled");
            }
            finally { EditorSceneManager.CloseScene(scene,true); }
        }

        private static void SetGoalPoses(AssemblyDemoController puzzle)
        {
            var layout = puzzle.GetComponent<DeviceAssemblyLayout>();
            for (int i = 0; i < puzzle.Modules.Length; i++)
            {
                puzzle.Modules[i].transform.localPosition = layout.assembledLocalPositions[i];
                puzzle.Modules[i].RotationDegrees = 0;
            }
        }

        private static void CheckUpdatedEngine(AssemblyDemoController puzzle,StringBuilder report)
        {
            var layout = puzzle.GetComponent<DeviceAssemblyLayout>();
            for (int i = 0; i < NativeRepairLevelInstaller.EngineNames.Length; i++)
            {
                var module = puzzle.Modules[i];
                Require(Vector3.Distance(layout.nativeStartPositions[i],NativeRepairLevelInstaller.EngineStart[i]) < .00001f,"engine recorded scatter 2026-10-20 "+i,report);
                Require(Vector3.Distance(layout.nativeTargetPositions[i],NativeRepairLevelInstaller.EngineTarget[i]) < .00001f,"engine recorded target 2026-10-20 "+i,report);
                Require(Quaternion.Angle(layout.nativeStartRotations[i],Quaternion.Euler(NativeRepairLevelInstaller.EngineStartEuler[i])) < .02f,"engine recorded start rotation "+i,report);
                Require(Quaternion.Angle(layout.nativeTargetRotations[i],Quaternion.Euler(NativeRepairLevelInstaller.EngineTargetEuler[i])) < .02f,"engine recorded target rotation "+i,report);
                var model = module.modelVisual.GetChild(0);
                Require(Vector3.Distance(model.localScale,NativeRepairLevelInstaller.EngineScales[i]) < .00001f,"engine recorded signed scale "+i,report);
                Require(AssetDatabase.GetAssetPath(model.GetComponent<MeshFilter>().sharedMesh) == NativeRepairLevelInstaller.EngineMeshAsset,"engine uses raptor_low mesh "+i,report);
            }
        }
        private static void CheckTargets(AssemblyDemoController puzzle,string name,StringBuilder report)
        {
            var layout = puzzle.GetComponent<DeviceAssemblyLayout>();
            Require(puzzle.IsAssemblyComplete,name+" full physical graph",report);
            for (int i = 0; i < puzzle.Modules.Length; i++)
            {
                var part = puzzle.Modules[i];
                var goal = puzzle.transform.TransformPoint(layout.modelToBoard*layout.nativeTargetPositions[i]*layout.modelScale);
                Require(Vector3.Distance(part.modelVisual.position,goal) < .0003f,name+" final screenshot XYZ "+i,report);
                Require(Quaternion.Angle(Quaternion.Inverse(puzzle.transform.rotation)*part.modelVisual.rotation,layout.modelToBoard*layout.nativeTargetRotations[i]) < .02f,name+" final screenshot rotation "+i,report);
            }
        }

        [MenuItem("Tools/Ship Assembly/Validate Native Levels In SHIP Play Mode")]
        public static void RunInShip()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
            StageUtility.GoToMainStage(); EditorSceneManager.SaveOpenScenes(); EditorSceneManager.OpenScene("Assets/Scenes/SHIP.unity");
            SessionState.SetBool(RunKey,true); EditorApplication.isPlaying = true;
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView"); EditorWindow.GetWindow(type).Focus();
        }

        private static void CheckShip()
        {
            if (EditorApplication.isCompiling || !EditorApplication.isPlaying) return;
            Application.runInBackground = true; EditorApplication.QueuePlayerLoopUpdate();
            if (Time.frameCount < 20)
            {
                if (EditorApplication.timeSinceStartup-started > 30) Finish(new TimeoutException("SHIP did not start rendering frames."));
                return;
            }
            runtimeReport = new StringBuilder();
            try
            {
                var service = Object.FindFirstObjectByType<ShipAssemblyController>();
                Require(service != null,"SHIP assembly service exists",runtimeReport);
                Require(service.Recipes.Length == 4,"four production repair recipes retained",runtimeReport);
                // Prepare this validation explicitly; the production landing/repair availability gate stays intact.
                Require(service.ApplyCrashDamage(),"validation starts with damaged ship systems",runtimeReport);
                service.SetRepairsAvailable(true);
                foreach (var id in service.Recipes.SelectMany(r => r.Materials).Select(c => c.ItemId).Distinct())
                    Require(service.Inventory.Items.TryAdd(id,100),"test materials added to shared inventory: "+id,runtimeReport);
                for (int index = 0; index < 4; index++)
                {
                    Require(service.SelectRecipe(index),"select independent repair recipe "+index,runtimeReport);
                    Require(service.RepairScreen.GetState(index) == RepairDeviceState.Unrepaired,"device is unrepaired before independent availability check "+index,runtimeReport);
                    Require(service.CanStartSelected(out var reason),"device can start before any other device is repaired "+index+": "+reason,runtimeReport);
                }
                foreach (int index in new[] { 2, 3, 1, 0 })
                {
                    Require(service.SelectRecipe(index),"select production recipe "+index,runtimeReport);
                    var recipe = service.SelectedRecipe;
                    var counts = recipe.Materials.Select(c => service.Inventory.Items.GetQuantity(c.ItemId)).ToArray();
                    Require(service.TryStartSelected(),"paid production session starts "+recipe.RecipeId+": "+service.StatusMessage,runtimeReport);
                    var puzzle = service.ActivePuzzle;
                    Require(puzzle != null && puzzle.IsInitialized,"production puzzle initialized "+recipe.RecipeId,runtimeReport);
                    for (int cost = 0; cost < counts.Length; cost++)
                        Require(service.Inventory.Items.GetQuantity(recipe.Materials[cost].ItemId) == counts[cost]-recipe.Materials[cost].Quantity,"material deducted exactly once "+index+"/"+cost,runtimeReport);
                    if (index == 2 || index == 3)
                    {
                        if (index == 2) CheckUpdatedEngine(puzzle,runtimeReport);
                        Require(puzzle.Modules.Length == (index == 2 ? 3 : 4),"production uses new native part count "+index,runtimeReport);
                        CapturePuzzle(puzzle,index == 2 ? "engine-ship-before" : "life-ship-before");
                    }
                    puzzle.InputBlocked = false;
                    var original = puzzle.Modules.Select(m => m.Position).ToArray();
                    puzzle.SelectModule(1); puzzle.ApplyMotion(Vector2.one*.2f,15); puzzle.ResetDemo();
                    Require(Vector2.Distance(puzzle.Modules[1].Position,original[1]) < .0001f,"production reset preserves recorded scatter "+index,runtimeReport);
                    Require(service.TryStartSelected() && service.ActivePuzzle == puzzle,"resume reuses paid session "+index,runtimeReport);
                    for (int cost = 0; cost < counts.Length; cost++)
                        Require(service.Inventory.Items.GetQuantity(recipe.Materials[cost].ItemId) == counts[cost]-recipe.Materials[cost].Quantity,"resume does not charge materials again "+index+"/"+cost,runtimeReport);
                    Assemble(puzzle);
                    Require(service.IsSelectedCompleted && service.RepairScreen.GetState(index) == RepairDeviceState.Repaired,"native completion updates production repair screen "+index,runtimeReport);
                    if (index == 2 || index == 3)
                    {
                        CheckTargets(puzzle,recipe.RecipeId,runtimeReport);
                        CapturePuzzle(puzzle,index == 2 ? "engine-ship-assembled" : "life-ship-assembled");
                    }
                    puzzle.InputBlocked = false; puzzle.ResetDemo(); puzzle.DetachSelected();
                    Require(puzzle.IsAssemblyComplete && puzzle.InputBlocked,"completed production puzzle is immutable "+index,runtimeReport);
                }
                Require(service.RepairScreen.AllRepaired,"all devices complete after independent repair order",runtimeReport);
                service.SelectRecipe(2); var engine = service.ActivePuzzle; service.SelectRecipe(3); var life = service.ActivePuzzle;
                service.SelectRecipe(2); Require(service.ActivePuzzle == engine && engine.IsAssemblyComplete,"switching back retains engine model and joints",runtimeReport);
                service.SelectRecipe(3); Require(service.ActivePuzzle == life && life.IsAssemblyComplete,"switching back retains life model and joints",runtimeReport);
                Require(!service.TryStartSelected(),"completed device cannot be charged or started again",runtimeReport);
                Finish(null);
            }
            catch (Exception e) { Finish(e); }
        }

        private static void Assemble(AssemblyDemoController puzzle)
        {
            var core = puzzle.Modules.OrderByDescending(m => m.PortCount).First();
            foreach (var part in puzzle.Modules)
            {
                if (part == core) continue;
                int port = Enumerable.Range(0,core.PortCount).Single(i => core.PortKey(i) == part.PortKey(0));
                puzzle.InputBlocked = false;
                part.RotationDegrees = core.RotationDegrees+core.PortAngle(port)+180-part.PortAngle(0);
                part.Position = core.PortPosition(port)-part.PortDirection(0)*part.PortDistance(0);
                puzzle.SelectModule(Array.IndexOf(puzzle.Modules,part));
                if (!puzzle.TrySnapSelected()) throw new InvalidOperationException("Production solver rejected "+part.name);
            }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= CheckShip; SessionState.SetBool(RunKey,false);
            if (runtimeReport == null) runtimeReport = new StringBuilder();
            if (error != null) { runtimeReport.AppendLine("FAIL "+error); Debug.LogException(error); }
            Directory.CreateDirectory("Logs/NativeRepairLevels"); File.WriteAllText("Logs/NativeRepairLevels/ship-runtime-validation.txt",runtimeReport.ToString());
            if (error == null) Debug.Log("[NativeRepairLevels] SHIP runtime validation passed\n"+runtimeReport);
            EditorApplication.isPlaying = false;
        }

        private static void CapturePuzzle(AssemblyDemoController puzzle,string name)
        {
            var go = new GameObject("Native repair verification camera",typeof(Camera)); SceneManager.MoveGameObjectToScene(go,puzzle.gameObject.scene);
            var camera = go.GetComponent<Camera>(); var layout = puzzle.GetComponent<DeviceAssemblyLayout>();
            foreach (var child in puzzle.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 29;
            camera.transform.position = puzzle.transform.TransformPoint(new Vector3(layout.viewCenter.x,layout.viewCenter.y,-10));
            camera.orthographic = true; camera.orthographicSize = 5.4f; camera.cullingMask = 1 << 29;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f,.028f,.032f);
            var target = new RenderTexture(1600,900,24); var pixels = new Texture2D(1600,900,TextureFormat.RGB24,false);
            var old = RenderTexture.active; bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false; target.Create(); camera.targetTexture = target; camera.aspect = 1600f/900;
                camera.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,1600,900),0,0); pixels.Apply();
                File.WriteAllBytes("Logs/NativeRepairLevels/"+name+".png",pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = old; ShaderUtil.allowAsyncCompilation = async;
                Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(go);
            }
        }
    }
}
