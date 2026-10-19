using System;
using System.IO;
using System.Linq;
using LudumDare60.AssemblyDemo;
using LudumDare60.Storage;
using LudumDare60.Salvage;
using LudumDare60.TerminalDialogue;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LudumDare60.ReturnWindow.Editor
{
    /// <summary>Builds a separate, playable mission using the existing cockpit and repair assets.</summary>
    public static class ReturnWindowSceneBuilder
    {
        public const string ScenePath = "Assets/ReturnWindow/Scenes/ReturnWindow.unity";
        private static TMP_FontAsset font;

        public static void BuildAndPreview()
        {
            Build();
            ReturnPropBuilder.RenderPreviews();
        }

        public static void BuildAndValidate()
        {
            BuildAndPreview();
            ReturnWindowValidation.Run();
        }

        [MenuItem("Tools/Return Window/Build Playable Mission")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before building the mission.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory("Assets/ReturnWindow/Scenes");
            Directory.CreateDirectory("Assets/ReturnWindow/Art/Materials");
            AssetDatabase.Refresh();
            ReturnPropBuilder.BuildPrefabs();
            var story = ReturnStoryBuilder.CreateStory();
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Terminal/Glass_TTY_VT220 SDF.asset");
            if (font == null) throw new FileNotFoundException("Missing bundled Glass TTY font.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "ReturnWindow";
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.34f, .29f, .22f);
            RenderSettings.ambientEquatorColor = new Color(.21f, .19f, .16f);
            RenderSettings.ambientGroundColor = new Color(.09f, .065f, .04f);
            RenderSettings.fog = false;

            var cockpit = InstantiateAsset("Assets/CompactCockpit_WallIntegrated/Prefabs/CompactCockpit_WallIntegrated.prefab");
            cockpit.name = "Return Window Cockpit";
            var cameraObject = new GameObject("Pilot Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(new Vector3(0, 1.44f, .48f), Quaternion.Euler(12, 180, 0));
            var camera = cameraObject.GetComponent<Camera>();
            camera.nearClipPlane = .025f; camera.farClipPlane = 250; camera.fieldOfView = 66;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.29f, .15f, .085f);
            var pilot = cameraObject.AddComponent<ReturnPilotController>();
            PointLight("Warm cabin lamp", new Vector3(0, 1.93f, .05f), new Color(1, .79f, .55f), .55f, 3);
            PointLight("Console fill", new Vector3(0, 1.2f, .1f), new Color(.43f, .74f, .67f), .12f, 2);
            AddMarsHorizon();

            var service = new GameObject("Mission Session - 20 minutes / 7 days");
            var mission = service.AddComponent<MissionController>();
            var inventory = service.AddComponent<InventoryController>();

            var terminalObject = InstantiateAsset("Assets/Prefabs/TerminalDialogue/DOS Terminal System.prefab");
            terminalObject.name = "Return Mission AI";
            var terminal = terminalObject.GetComponent<TerminalDialogueController>();
            terminal.Configure(story);
            SetBool(terminal, "startAutomatically", true);
            SetBool(terminalObject.GetComponentInChildren<TerminalOptionsPanel>(true), "showConnectButton", false);
            foreach (var text in terminalObject.GetComponentsInChildren<TMP_Text>(true))
                if (text.name == "Transcript") text.fontSize = 39;
            var mount = terminalObject.GetComponent<TerminalScreenMount>();
            var nav = cockpit.GetComponentsInChildren<Transform>(true).First(t => t.name == "Screen_NAVIGATION");
            mount.AttachTo(nav);
            pilot.terminalLookTarget = nav;
            TerminalScreenMount.EnsureEventSystem();

            var calendarObject = InstantiateAsset(ReturnPropBuilder.CalendarPrefabPath);
            MountProp(calendarObject, new Vector3(-.78f, 1.10f, -.03f), camera.transform.position, .18f);
            var countdownObject = InstantiateAsset(ReturnPropBuilder.CountdownPrefabPath);
            MountProp(countdownObject, new Vector3(-.73f, 1.66f, -.06f), camera.transform.position, .08f);
            var calendar = calendarObject.GetComponent<ReturnCalendarView>();
            var countdown = countdownObject.GetComponent<ReturnCountdownView>();

            var repair = new GameObject("Repair Workbench Controller").AddComponent<ReturnRepairStation>();
            var assemblyObject = InstantiateAsset("Assets/AssemblyDemo/Prefabs/Level01/Level01Assembly.prefab");
            assemblyObject.name = "Propulsion Manifold - Existing Assembly Puzzle";
            assemblyObject.transform.position = new Vector3(50, 0, 0);
            var assembly = assemblyObject.GetComponent<AssemblyDemoController>();
            assembly.ViewCamera = camera;
            var repairBridge = repair.gameObject.AddComponent<MissionRepairBridge>();
            repairBridge.Configure(assembly);
            repair.pilotCamera = camera; repair.pilot = pilot; repair.terminal = terminal;
            repair.mission = mission; repair.workshop = assemblyObject; repair.assembly = assembly;
            repair.workshopUI = MakeRepairUI(repair);
            assemblyObject.SetActive(false);

            var salvage = new GameObject("Supply Recovery Controller").AddComponent<ReturnSalvageStation>();
            salvage.mission = mission; salvage.terminal = terminal; salvage.inventory = inventory;
            salvage.salvage = salvage.gameObject.AddComponent<SalvageController>();
            salvage.definition = MakeSalvageDefinition();
            MakeSalvageUI(salvage);
            pilot.salvageStation = salvage;

            var ending = new GameObject("Open Endings").AddComponent<ReturnEndingView>();
            var endingFocus = new GameObject("Ending Instrument Focus").transform;
            endingFocus.position = (calendarObject.transform.position + countdownObject.transform.position) * .5f + Vector3.up * .15f;
            ending.pilot = pilot; ending.stayedLookTarget = endingFocus;
            MakeEndingUI(ending);
            pilot.mission = mission; pilot.terminal = terminal; pilot.repairStation = repair;
            pilot.pausePanel = MakePauseUI(pilot);

            var bridge = new GameObject("Mission Presentation").AddComponent<MissionTerminalBridge>();
            bridge.mission = mission; bridge.terminal = terminal;
            bridge.calendar = calendar; bridge.countdown = countdown;
            bridge.repairStation = repair; bridge.endingView = ending;
            bridge.salvageStation = salvage;
            bridge.speaker = bridge.gameObject.AddComponent<AudioSource>();
            bridge.speaker.spatialBlend = 0; bridge.speaker.playOnAwake = false;
            bridge.warningClip = LoadSound("06_Communications/COM_telegraph_beep.wav");
            bridge.repairClip = LoadSound("08_Repair_Story/STORY_ready_departure.wav");
            bridge.departureClip = LoadSound("08_Repair_Story/STORY_takeoff.wav");
            AddAmbience(cockpit);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }
                .Concat(EditorBuildSettings.scenes.Where(s => s.path != ScenePath)).ToArray();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/ReturnWindowBuild.txt", "PASS\nScene: " + ScenePath
                + "\nClock: 1200 active seconds / 7 task days\nProps: editable Blender + GLB + independent Prefabs\nInventory: session only\n");
            Debug.Log("[ReturnWindow] Playable mission built: " + ScenePath);
        }

        [MenuItem("Tools/Return Window/Open Mission")]
        public static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        private static GameObject InstantiateAsset(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new FileNotFoundException(path);
            return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        }
        private static void MountProp(GameObject prop, Vector3 bottom, Vector3 eye, float halfHeight)
        {
            prop.transform.position = bottom;
            prop.transform.rotation = Quaternion.LookRotation(eye - (bottom + Vector3.up * halfHeight), Vector3.up);
        }
        private static void PointLight(string name, Vector3 position, Color color, float intensity, float range)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.type = LightType.Point; light.color = color; light.intensity = intensity; light.range = range;
            light.transform.position = position;
        }
        private static void AddMarsHorizon()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Mars visible through side ports";
            ground.transform.position = new Vector3(0, -.12f, 0);
            ground.transform.localScale = Vector3.one * 18;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader) { name = "ReturnWindow Mars Dust", color = new Color(.31f, .12f, .055f) };
            var path = "Assets/ReturnWindow/Art/Materials/MarsDust.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null) AssetDatabase.CreateAsset(material, path);
            else { UnityEngine.Object.DestroyImmediate(material); material = existing; }
            ground.GetComponent<Renderer>().sharedMaterial = material;
            var sun = new GameObject("Distant Martian sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1, .73f, .51f); sun.intensity = .65f;
            sun.transform.rotation = Quaternion.Euler(32, -35, 0);
        }
        private static AudioClip LoadSound(string relative) => AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Pioneer60Audio/Audio/02_Single_Shots/" + relative);
        private static void AddAmbience(GameObject cockpit)
        {
            var path = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Pioneer60Audio" })
                .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => p.IndexOf("cabin", StringComparison.OrdinalIgnoreCase) >= 0 && p.IndexOf("loop", StringComparison.OrdinalIgnoreCase) >= 0);
            if (path == null) return;
            var source = cockpit.AddComponent<AudioSource>(); source.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            source.loop = true; source.playOnAwake = true; source.volume = .12f;
        }
        private static void SetBool(UnityEngine.Object target, string name, bool value)
        {
            var data = new SerializedObject(target); data.FindProperty(name).boolValue = value; data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetString(UnityEngine.Object target, string name, string value)
        {
            var data = new SerializedObject(target); data.FindProperty(name).stringValue = value; data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static Canvas Overlay(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = order;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            return canvas;
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform)); var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
        private static TMP_Text Text(string name, Transform parent, string value, float size, Vector2 min, Vector2 max)
        {
            var rect = Rect(name, parent, min, max); var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = size; text.text = value; text.color = new Color(.78f, .86f, .74f);
            text.textWrappingMode = TextWrappingModes.Normal; text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.TopLeft; return text;
        }
        private static Button Button(string label, Transform parent, Vector2 min, Vector2 max, UnityAction action)
        {
            var rect = Rect(label, parent, min, max); var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(.08f, .12f, .11f, .96f); var button = rect.gameObject.AddComponent<Button>();
            var text = Text("Label", rect, label, 25, new Vector2(.04f, .08f), new Vector2(.96f, .92f));
            text.alignment = TextAlignmentOptions.Center;
            UnityEventTools.AddPersistentListener(button.onClick, action); return button;
        }
        private static GameObject MakeRepairUI(ReturnRepairStation repair)
        {
            var canvas = Overlay("Repair Instructions", 10);
            Text("Title", canvas.transform, "PROPULSION MANIFOLD / REPAIR WORKBENCH", 33, new Vector2(.06f, .91f), new Vector2(.94f, .98f));
            Text("Instructions", canvas.transform, "1-4 / CLICK: SELECT    WASD: MOVE    Q/E: ROTATE    SHIFT: FINE\nX: DISCONNECT    R: RESET PARTS    ESC: RETURN TO CABIN\nJoin matching CONTROL, PRESSURE and MOUNT ports. Mission time continues.",
                24, new Vector2(.06f, .02f), new Vector2(.73f, .14f));
            Button("RETURN TO CABIN", canvas.transform, new Vector2(.77f, .04f), new Vector2(.96f, .10f), repair.CloseRepair);
            canvas.gameObject.SetActive(false); return canvas.gameObject;
        }
        private static GameObject MakePauseUI(ReturnPilotController pilot)
        {
            var canvas = Overlay("Mission Paused", 30);
            var image = canvas.gameObject.AddComponent<Image>(); image.color = new Color(.008f, .018f, .02f, .94f);
            Text("Title", canvas.transform, "MISSION PAUSED", 48, new Vector2(.27f, .64f), new Vector2(.8f, .74f));
            Text("Help", canvas.transform, "MOUSE: LOOK AROUND\nE: CONNECT / LEAVE TERMINAL\nESC: PAUSE / RETURN\n\nStory reading freezes the mission and all repair work.\nRoutine terminal queries do not stop the clock.",
                28, new Vector2(.27f, .3f), new Vector2(.85f, .61f));
            // A dedicated component method is used so the button remains serialized after building.
            var resume = canvas.gameObject.AddComponent<ReturnResumeButton>(); resume.pilot = pilot;
            Button("RESUME", canvas.transform, new Vector2(.27f, .2f), new Vector2(.5f, .26f), resume.Resume);
            canvas.gameObject.SetActive(false); return canvas.gameObject;
        }
        private static void MakeEndingUI(ReturnEndingView ending)
        {
            var canvas = Overlay("Open Ending", 40);
            ending.overlay = canvas.gameObject.AddComponent<CanvasGroup>();
            var tint = canvas.gameObject.AddComponent<Image>(); tint.color = new Color(.009f, .017f, .018f, .81f);
            var readingPanel = Rect("Epilogue Reading Panel", canvas.transform, new Vector2(.09f, .23f), new Vector2(.72f, .89f));
            readingPanel.gameObject.AddComponent<Image>().color = new Color(.008f, .016f, .018f, .93f);
            ending.heading = Text("Heading", canvas.transform, "", 48, new Vector2(.12f, .75f), new Vector2(.92f, .86f));
            ending.heading.color = new Color(1, .65f, .29f);
            ending.body = Text("Epilogue", canvas.transform, "", 31, new Vector2(.12f, .27f), new Vector2(.69f, .72f));
            Button("BEGIN AGAIN", canvas.transform, new Vector2(.12f, .13f), new Vector2(.34f, .2f), ending.RestartMission);
            canvas.gameObject.SetActive(false);
        }

        private static SalvageDefinition MakeSalvageDefinition()
        {
            const string path = "Assets/ReturnWindow/Story/SupplyRecovery.asset";
            var asset = AssetDatabase.LoadAssetAtPath<SalvageDefinition>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<SalvageDefinition>();
            var data = new SerializedObject(asset);
            data.FindProperty("targetId").stringValue = "colony-supply-cache";
            data.FindProperty("rewardId").stringValue = "greenhouse-supplies";
            data.FindProperty("rewardQuantityMin").intValue = 1;
            data.FindProperty("rewardQuantityMax").intValue = 1;
            data.FindProperty("settings.SignalWindow").floatValue = 3;
            data.FindProperty("settings.ControlSize").floatValue = .35f;
            data.FindProperty("settings.TargetSpeed").floatValue = .27f;
            data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void MakeSalvageUI(ReturnSalvageStation station)
        {
            var canvas = Overlay("Supply Recovery", 15);
            var background = canvas.gameObject.AddComponent<Image>(); background.color = new Color(.007f, .02f, .021f, .96f);
            Text("Title", canvas.transform, "SURFACE RECOVERY / REMOTE WINCH", 40, new Vector2(.15f, .84f), new Vector2(.88f, .94f));
            station.statusText = Text("Status", canvas.transform, "SEARCHING", 30, new Vector2(.15f, .56f), new Vector2(.68f, .79f));
            Text("Instructions", canvas.transform, "SPACE: LOCK SIGNAL\nHOLD SPACE: RAISE CONTROL ZONE\nRELEASE: LOWER\n\nKeep the target inside the green zone.\nMission time continues during recovery.\nSupplies are optional; repairs authorize departure.",
                26, new Vector2(.15f, .24f), new Vector2(.67f, .56f));
            station.progressText = Text("Progress", canvas.transform, "", 26, new Vector2(.15f, .18f), new Vector2(.62f, .24f));
            var track = Rect("Signal Track", canvas.transform, new Vector2(.73f, .25f), new Vector2(.79f, .79f));
            track.gameObject.AddComponent<Image>().color = new Color(.08f, .13f, .12f);
            station.controlZone = Rect("Control Zone", track, new Vector2(0, .1f), new Vector2(1, .45f));
            station.controlZone.gameObject.AddComponent<Image>().color = new Color(.24f, .78f, .43f, .8f);
            station.targetMarker = Rect("Target", track, new Vector2(-.2f, .3f), new Vector2(1.2f, .3f));
            station.targetMarker.sizeDelta = new Vector2(0, 7);
            station.targetMarker.gameObject.AddComponent<Image>().color = new Color(1, .64f, .19f);
            var progress = Rect("Recovery Progress", canvas.transform, new Vector2(.15f, .15f), new Vector2(.64f, .16f));
            station.progressFill = progress.gameObject.AddComponent<Image>();
            station.progressFill.color = new Color(.29f, .8f, .45f);
            station.progressFill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (station.progressFill.sprite == null) throw new InvalidOperationException("Supply progress requires a UI sprite.");
            station.progressFill.type = Image.Type.Filled; station.progressFill.fillMethod = Image.FillMethod.Horizontal;
            Button("RETURN TO CABIN", canvas.transform, new Vector2(.15f, .06f), new Vector2(.4f, .12f), station.CloseSalvage);
            Button("RETRY RECOVERY", canvas.transform, new Vector2(.45f, .06f), new Vector2(.7f, .12f), station.RetrySalvage);
            station.panel = canvas.gameObject;
            canvas.gameObject.SetActive(false);
        }
    }
}
