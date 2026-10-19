using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using LudumDare60.AssemblyDemo;
using LudumDare60.ReturnWindow;
using LudumDare60.UI;
using LudumDare60.TerminalDialogue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
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
                    var scale = puzzle.Modules[1].modelVisual.GetChild(0).localScale;
                    Require(scale.x < 0 && scale.y < 0 && scale.z < 0,"engine end plate negative scale retained",report);
                    Require(Mathf.Abs(scale.x+.2371762f) < .00001f && Mathf.Abs(scale.z+.2783778f) < .00001f,"engine end plate scale matches screenshot",report);
                }
                else Require(instance.GetComponentsInChildren<MeshFilter>(true).Count(m => !m.name.StartsWith("__XRay")) == 4,"life excludes old tank and door",report);
                Require(!puzzle.TrySnapSelected() && !puzzle.IsAssemblyComplete,name+" scattered pieces do not auto-complete",report);
                CapturePuzzle(puzzle,name+"-before");
                puzzle.SelectModule(1); var start = puzzle.Selected.Position;
                puzzle.ApplyMotion(new Vector2(.2f,.3f),17); puzzle.ResetDemo();
                Require(Vector2.Distance(puzzle.Modules[1].Position,start) < .0001f,name+" reset restores scatter",report);
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
            // Real input bindings intentionally ignore a background game window.
            if (!Application.isFocused)
            {
                EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
                if (EditorApplication.timeSinceStartup-started > 90) Finish(new TimeoutException("Focus the Game view to validate real keyboard bindings."));
                return;
            }
            runtimeReport = new StringBuilder();
            try
            {
                var service = Object.FindFirstObjectByType<ShipAssemblyController>();
                Require(service != null,"SHIP assembly service exists",runtimeReport);
                Require(service.Recipes.Length == 4,"four production repair recipes retained",runtimeReport);
                service.SelectRecipe(2);
                Require(!service.CanStartSelected(out _),"engine remains locked before energy and navigation repairs",runtimeReport);
                foreach (var id in service.Recipes.SelectMany(r => r.Materials).Select(c => c.ItemId).Distinct())
                    Require(service.Inventory.Items.TryAdd(id,100),"test materials added to shared inventory: "+id,runtimeReport);
                for (int index = 0; index < 4; index++)
                {
                    Require(service.SelectRecipe(index),"select production recipe "+index,runtimeReport);
                    var recipe = service.SelectedRecipe;
                    var counts = recipe.Materials.Select(c => service.Inventory.Items.GetQuantity(c.ItemId)).ToArray();
                    Require(service.TryStartSelected(),"paid production session starts "+recipe.RecipeId+": "+service.StatusMessage,runtimeReport);
                    var puzzle = service.ActivePuzzle;
                    Require(puzzle != null && puzzle.IsInitialized,"production puzzle initialized "+recipe.RecipeId,runtimeReport);
                    CheckWorkbenchControls(service,index);
                    for (int cost = 0; cost < counts.Length; cost++)
                        Require(service.Inventory.Items.GetQuantity(recipe.Materials[cost].ItemId) == counts[cost]-recipe.Materials[cost].Quantity,"material deducted exactly once "+index+"/"+cost,runtimeReport);
                    if (index == 2 || index == 3)
                    {
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

        private static void CheckWorkbenchControls(ShipAssemblyController service,int index)
        {
            var view = Object.FindObjectsByType<AssemblyScreenView>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Single(v => v.Controller == service && v.gameObject.layer != 31);
            view.gameObject.SetActive(true); view.Refresh();
            Require(view.isActiveAndEnabled,"main scene workbench view active "+index,runtimeReport);
            var guide = view.GetComponentInChildren<AssemblyKeyGuide>();
            Require(guide != null && !guide.IsExpanded,"main scene guide defaults to six keys "+index,runtimeReport);
            Require(guide.GetComponentsInChildren<TMPro.TMP_Text>().Length == 7,"collapsed guide contains only heading and QWE/ASD "+index,runtimeReport);
            Require(guide.ExpandButton != null && guide.ExpandButton.isActiveAndEnabled,"main scene expand button available "+index,runtimeReport);
            Require(guide.GetComponentsInChildren<Graphic>().Count(g => g.raycastTarget) == 1,"only guide toggle intercepts clicks "+index,runtimeReport);
            var puzzle = service.ActivePuzzle;
            var poses = puzzle.Modules.Select(m => m.Position).ToArray();
            service.AssemblyCamera.Render();
            var mount = view.GetComponent<TerminalScreenMount>();
            Require(mount != null && mount.CurvedScreen && mount.FitToScreen(),"main scene curved terminal renders "+index,runtimeReport);
            Canvas.ForceUpdateCanvases();
            var copy = Object.FindObjectsByType<AssemblyKeyGuide>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Single(g => g != guide && g.gameObject.activeInHierarchy && g.canvas != null
                    && g.canvas.worldCamera != null && g.canvas.worldCamera.targetTexture == mount.ScreenTexture);
            Require(!copy.IsExpanded && VertexCount(copy) > 100,"curved terminal retains six drawn keycaps "+index,runtimeReport);
            int collapsedVertices = VertexCount(copy);
            CaptureTexture(mount.ScreenTexture,"main-"+index+"-collapsed");
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(guide.ExpandButton.gameObject,pointer,ExecuteEvents.pointerClickHandler);
            Require(guide.IsExpanded,"real UI click expands controls "+index,runtimeReport);
            Require(guide.GetComponentsInChildren<TMPro.TMP_Text>().Length > 20,"expanded controls include remaining keys "+index,runtimeReport);
            mount.FitToScreen(); Canvas.ForceUpdateCanvases();
            Require(copy.IsExpanded && VertexCount(copy) > collapsedVertices,"curved terminal synchronizes expanded geometry "+index,runtimeReport);
            CaptureTexture(mount.ScreenTexture,"main-"+index+"-expanded");
            ExecuteEvents.Execute(guide.ExpandButton.gameObject,pointer,ExecuteEvents.pointerClickHandler);
            Require(!guide.IsExpanded,"second real UI click collapses controls "+index,runtimeReport);
            Require(puzzle.Modules.Select((m,i) => Vector2.Distance(m.Position,poses[i]) < .0001f).All(same => same),"help toggle leaves all part poses unchanged "+index,runtimeReport);
            mount.FitToScreen();
            service.SetFocused(true);
            Require(service.InputEnabled,"main scene accepts focused keyboard input "+index
                +" (app="+Application.isFocused+", timeScale="+Time.timeScale+", canRepair="+service.RepairScreen.CanBeginRepair(index)+")",runtimeReport);
            var input = view.PuzzleImage.GetComponent<AssemblyScreenInput>();
            Require(input != null && input.isActiveAndEnabled,"main scene input adapter enabled "+index,runtimeReport);
            var keyboard = Keyboard.current;
            Require(keyboard != null,"keyboard device present "+index,runtimeReport);
            var update = typeof(AssemblyScreenInput).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
            puzzle.SelectModule(1);
            var before = puzzle.Selected.Position;
            float angle = puzzle.Selected.RotationDegrees;
            try
            {
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D,Key.E)); InputSystem.Update();
                update.Invoke(input,null);
                Require(Vector2.Distance(before,puzzle.Selected.Position) > .00001f,"real D binding moves selected part "+index,runtimeReport);
                Require(Mathf.Abs(Mathf.DeltaAngle(angle,puzzle.Selected.RotationDegrees)) > .0001f,"real E binding rotates selected part "+index,runtimeReport);
            }
            finally { InputSystem.QueueStateEvent(keyboard,new KeyboardState()); InputSystem.Update(); puzzle.ResetDemo(); service.SetFocused(false); }
        }

        private static int VertexCount(Graphic graphic)
        {
            var mesh = graphic.canvasRenderer.GetMesh();
            return mesh != null ? mesh.vertexCount : 0;
        }

        private static void CaptureTexture(RenderTexture texture,string name)
        {
            var pixels = new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = texture; pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0); pixels.Apply();
                Directory.CreateDirectory("Logs/NativeRepairLevels"); File.WriteAllBytes("Logs/NativeRepairLevels/"+name+".png",pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
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
