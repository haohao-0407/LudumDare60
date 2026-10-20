using System;
using System.Linq;
using LudumDare60.ReturnWindow;
using LudumDare60.TerminalDialogue;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LudumDare60.Landing.Editor
{
    /// <summary>Connects the existing physical mission instruments to the authored SHIP cockpit.</summary>
    public static class CockpitMissionInstaller
    {
        const string InstrumentRoot = "Mission Instruments";
        const string ArtRoot = "Assets/ReturnWindow/Art/";

        [MenuItem("Tools/Landing/Install mission clock and calendar in SHIP")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Install the mission instruments in Edit Mode after compilation and asset import.");

            Scene scene = SceneManager.GetSceneByPath(LandingSceneInstaller.ShipPath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open Assets/Scenes/SHIP.unity before installing its mission instruments.");
            var opening = Single<CockpitOpeningController>(scene);
            var departure = Single<CockpitDepartureController>(scene);
            var camera = Single<CockpitCameraController>(scene);
            if (!opening.Landing || !opening.Landing.ShipRig || !opening.Terminal ||
                !opening.Terminal.Conversation || !camera.ViewCamera)
                throw new InvalidOperationException("The opening flow, landing rig, terminal and cockpit camera must be configured first.");
            if (departure.gameObject != opening.gameObject || departure.Opening != opening)
                throw new InvalidOperationException("The departure flow must belong to the existing opening controller.");
            var missions = Components<CockpitMissionController>(scene);
            if (missions.Length > 1 || (missions.Length == 1 && missions[0].gameObject != opening.gameObject))
                throw new InvalidOperationException("SHIP must have at most one mission controller, on the opening controller object.");
            var existingScreens = camera.Screens ?? Array.Empty<CockpitScreenTarget>();
            var tutorialScreen = opening.TutorialScreen
                ? opening.TutorialScreen
                : existingScreens.FirstOrDefault(screen => screen && screen.name.Contains("COMMUNICATION"))
                    ?? existingScreens.FirstOrDefault(screen => screen && screen.name.Contains("SYSTEM"))
                    ?? existingScreens.FirstOrDefault(screen => screen);
            if (!tutorialScreen)
                throw new InvalidOperationException("The cockpit camera needs an existing display for the landing tutorial.");
            var terminalScreen = existingScreens.FirstOrDefault(screen => screen && screen.name == "SCREEN_NAVIGATION");
            if (!terminalScreen)
                throw new InvalidOperationException("The cockpit camera needs its SCREEN_NAVIGATION dialogue display.");

            var calendarPrefab = RequiredAsset<GameObject>(ArtRoot + "Prefabs/ReturnCalendar.prefab");
            var countdownPrefab = RequiredAsset<GameObject>(ArtRoot + "Prefabs/ReturnCountdown.prefab");
            var paper = RequiredAsset<Material>(ArtRoot + "Materials/RW_Aged_Paper.mat");
            var font = RequiredAsset<TMP_FontAsset>("Assets/Fonts/Terminal/Glass_TTY_VT220 SDF.asset");
            if (!calendarPrefab.GetComponent<ReturnCalendarView>() || !countdownPrefab.GetComponent<ReturnCountdownView>())
                throw new InvalidOperationException("The existing instrument prefabs must contain their calendar and countdown views.");
            var warnings = new[] { "50Percent", "25Percent", "10Percent" }
                .Select(level => RequiredAsset<TerminalDialogueDefinition>(
                    "Assets/Dialogue/pioneer60/MissionUpdates/" + level + "/AllSystemsReady.asset")).ToArray();
            var story = opening.Terminal.Conversation;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install SHIP mission clock and calendar");
            try
            {
                Transform rig = opening.Landing.ShipRig;
                Transform root = Child(rig, InstrumentRoot);
                Undo.RecordObject(root, "Align mission instruments with the ship rig");
                root.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.localScale = Vector3.one;
                var calendarObject = Instrument(root, calendarPrefab, "Return Calendar");
                var countdownObject = Instrument(root, countdownPrefab, "Departure Countdown");
                var calendar = calendarObject.GetComponent<ReturnCalendarView>();
                var countdown = countdownObject.GetComponent<ReturnCountdownView>();
                if (!calendar || !countdown)
                    throw new InvalidOperationException("Existing mission instrument objects have incompatible components.");

                // These are the reviewed ReturnWindow cockpit positions, relative to the moving ship.
                // The screen stack remains unobstructed; both props sit beside the starboard window.
                Vector3 eye = camera.FreeView ? rig.InverseTransformPoint(camera.FreeView.position)
                    : rig.InverseTransformPoint(camera.ViewCamera.transform.position);
                Mount(calendar.transform, new Vector3(-.78f, 1.10f, -.03f), eye, .18f);
                Mount(countdown.transform, new Vector3(-.73f, 1.66f, -.06f), eye, .08f);
                ConfigureChecklist(calendar, paper, font);
                ReadingLight(calendar.transform, new Vector3(0, .22f, .30f),
                    new Vector3(0, .18f, .012f), .65f);
                ReadingLight(countdown.transform, new Vector3(0, .11f, .32f),
                    new Vector3(0, .08f, .066f), .50f);

                var calendarTarget = FocusTarget(calendar.transform, new Vector3(0, .18f, .014f),
                    new Vector3(.30f, .36f, .018f), .54f, 45);
                var countdownTarget = FocusTarget(countdown.transform, new Vector3(0, .08f, .066f),
                    new Vector3(.38f, .16f, .025f), .40f, 40);
                Undo.RecordObject(camera, "Register mission instrument camera views");
                camera.Screens = (camera.Screens ?? Array.Empty<CockpitScreenTarget>())
                    .Where(target => target && target != calendarTarget && target != countdownTarget)
                    .Concat(new[] { calendarTarget, countdownTarget }).Distinct().ToArray();
                Dirty(camera);
                Undo.RecordObject(opening, "Bind opening flow to its cockpit camera and tutorial display");
                opening.CockpitCamera = camera;
                opening.TutorialScreen = tutorialScreen;
                Dirty(opening);

                var mission = missions.FirstOrDefault();
                if (!mission) mission = Undo.AddComponent<CockpitMissionController>(opening.gameObject);
                Undo.RecordObject(mission, "Bind SHIP mission clock");
                mission.Opening = opening;
                mission.Departure = departure;
                mission.Calendar = calendar;
                mission.Countdown = countdown;
                mission.TerminalScreen = terminalScreen;
                mission.WarningStories = warnings;
                mission.enabled = true;
                Dirty(mission);
                Undo.RecordObject(departure, "Bind departure authorization deadline");
                departure.Mission = mission;
                Dirty(departure);

                Undo.RecordObject(story, "Connect mission deadline story");
                CockpitMissionStory.Configure(story);
                Dirty(story);
                calendar.DayCount = MissionSchedule.DefaultDays;
                calendar.StartDate = new DateTime(1981, 8, 25);
                calendar.RepairChecklistMask = 0;
                calendar.DepartureConfirmed = false;
                RestoreCalendarStrokeShapes(calendar, calendarPrefab);
                calendar.Render(0, 1, false, 0);
                // Render's pencil animation collapses the unused stroke endpoints.
                // Keep full source shapes in the saved scene so runtime Initialize can cache them.
                RestoreCalendarStrokeShapes(calendar, calendarPrefab);
                countdown.Render(MissionSchedule.DefaultDays * 86400d, false, false, false);
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                    if (component) Dirty(component);
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                    text.ForceMeshUpdate(true);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Could not save SHIP mission instruments.");
                AssetDatabase.SaveAssets();
                Undo.CollapseUndoOperations(undoGroup);
                Debug.Log("SHIP mission deadline installed: physical countdown, dated calendar, four repair checklist marks and two clickable close-up views.", mission);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        static void ReadingLight(Transform instrument, Vector3 position, Vector3 aim, float intensity)
        {
            var support = Child(instrument, "Instrument reading light");
            Undo.RecordObject(support, "Aim local instrument reading light");
            support.localPosition = position;
            support.localRotation = Quaternion.LookRotation(aim - position, Vector3.up);
            support.localScale = Vector3.one;
            var light = support.GetComponent<Light>();
            if (!light) light = Undo.AddComponent<Light>(support.gameObject);
            Undo.RecordObject(light, "Illuminate instrument lettering and paper");
            // Existing props use URP/Lit paper and enamel. A short-range warm reading
            // light keeps their authored materials readable through landing/night states.
            light.type = LightType.Spot;
            light.color = new Color(1f, .88f, .70f);
            light.intensity = intensity;
            light.range = .70f;
            light.spotAngle = 110f;
            light.innerSpotAngle = 90f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.cullingMask = 1 << instrument.gameObject.layer;
            light.enabled = true;
            Dirty(light);
        }

        static void RestoreCalendarStrokeShapes(ReturnCalendarView calendar, GameObject prefab)
        {
            var sourceStrokes = prefab.GetComponentsInChildren<LineRenderer>(true)
                .Where(line => line.name.StartsWith("Cross day ", StringComparison.Ordinal) &&
                    line.name.Contains(" stroke ")).ToArray();
            if (sourceStrokes.Length != ReturnCalendarView.CellsPerPage * 2)
                throw new InvalidOperationException("The calendar prefab must contain two pencil strokes for every date.");
            foreach (var source in sourceStrokes)
            {
                string path = AnimationUtility.CalculateTransformPath(source.transform, prefab.transform);
                var targetTransform = calendar.transform.Find(path);
                var target = targetTransform ? targetTransform.GetComponent<LineRenderer>() : null;
                if (!target || source.positionCount != 2 ||
                    (source.GetPosition(1) - source.GetPosition(0)).sqrMagnitude <= .000001f)
                    throw new InvalidOperationException("Missing or collapsed calendar pencil stroke: " + path);
                Undo.RecordObject(target, "Preserve complete calendar pencil stroke");
                target.positionCount = source.positionCount;
                target.SetPosition(0, source.GetPosition(0));
                target.SetPosition(1, source.GetPosition(1));
                target.enabled = false;
                Dirty(target);
            }
        }

        static void ConfigureChecklist(ReturnCalendarView calendar, Material paper, TMP_FontAsset font)
        {
            var plan = calendar.transform.Find("Active mission schedule");
            if (!plan) throw new InvalidOperationException("The existing calendar is missing its active mission schedule.");
            // The original model combines dates, grid and checklist into one print mesh.
            // A matching paper patch replaces only its four old activity labels, preserving the grid and boxes.
            var patch = plan.Find("SHIP checklist label patch");
            if (!patch)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "SHIP checklist label patch";
                Undo.RegisterCreatedObjectUndo(quad, "Replace calendar checklist labels");
                Undo.DestroyObjectImmediate(quad.GetComponent<Collider>());
                patch = quad.transform;
                patch.SetParent(plan, false);
            }
            Undo.RecordObject(patch, "Place calendar checklist label patch");
            patch.localPosition = new Vector3(-.003f, .0525f, .012f);
            patch.localRotation = Quaternion.Euler(0, 180, 0);
            patch.localScale = new Vector3(.215f, .039f, 1);
            var renderer = patch.GetComponent<MeshRenderer>();
            Undo.RecordObject(renderer, "Match calendar paper");
            renderer.sharedMaterial = paper;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            string[] labels = { "POWER", "NAVIGATION", "ENGINE", "LIFE SUPPORT" };
            for (int i = 0; i < labels.Length; i++)
            {
                Transform label = Child(plan, "SHIP repaired system " + i);
                var text = label.GetComponent<TextMeshPro>();
                if (!text) text = Undo.AddComponent<TextMeshPro>(label.gameObject);
                label = text.transform;
                Undo.RecordObject(label, "Place calendar system label");
                label.localPosition = new Vector3(-.003f, .066f - i * .009f, .0127f);
                label.localRotation = Quaternion.Euler(0, 180, 0);
                label.localScale = Vector3.one;
                Undo.RecordObject(text, "Label calendar repair checklist");
                text.font = font;
                text.text = labels[i];
                text.fontSize = .0065f * 14f;
                text.fontStyle = FontStyles.Bold;
                text.color = Color.black;
                text.alignment = TextAlignmentOptions.Left;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
                text.rectTransform.sizeDelta = new Vector2(.213f, .009f);
                text.enableAutoSizing = false;
                text.richText = false;
                text.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        static CockpitScreenTarget FocusTarget(Transform instrument, Vector3 center, Vector3 size, float distance, float fieldOfView)
        {
            var collider = instrument.GetComponent<BoxCollider>();
            if (!collider) collider = Undo.AddComponent<BoxCollider>(instrument.gameObject);
            Undo.RecordObject(collider, "Configure instrument click surface");
            collider.center = center;
            collider.size = size;
            collider.isTrigger = true;
            collider.enabled = true;
            var viewpoint = Child(instrument, "Close-up viewpoint");
            Undo.RecordObject(viewpoint, "Place instrument close-up view");
            viewpoint.localPosition = center + Vector3.forward * distance;
            viewpoint.localRotation = Quaternion.Euler(0, 180, 0);
            viewpoint.localScale = Vector3.one;
            var target = instrument.GetComponent<CockpitScreenTarget>();
            if (!target) target = Undo.AddComponent<CockpitScreenTarget>(instrument.gameObject);
            Undo.RecordObject(target, "Bind instrument close-up view");
            target.HitCollider = collider;
            target.ViewPoint = viewpoint;
            target.FieldOfView = fieldOfView;
            Dirty(target);
            Dirty(collider);
            return target;
        }

        static void Mount(Transform prop, Vector3 bottom, Vector3 eye, float halfHeight)
        {
            Undo.RecordObject(prop, "Mount mission instrument in cockpit");
            prop.localPosition = bottom;
            prop.localRotation = Quaternion.LookRotation(eye - bottom - Vector3.up * halfHeight, Vector3.up);
            prop.localScale = Vector3.one;
            Dirty(prop);
        }

        static GameObject Instrument(Transform root, GameObject prefab, string name)
        {
            var found = root.Find(name);
            if (found) return found.gameObject;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            instance.name = name;
            Undo.RegisterCreatedObjectUndo(instance, "Add existing mission instrument");
            return instance;
        }

        static Transform Child(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child) return child;
            var created = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, "Create mission instrument support");
            created.transform.SetParent(parent, false);
            return created.transform;
        }

        static T Single<T>(Scene scene) where T : Component
        {
            var components = Components<T>(scene);
            if (components.Length != 1)
                throw new InvalidOperationException("Expected one " + typeof(T).Name + " in SHIP, found " + components.Length + ".");
            return components[0];
        }

        static T[] Components<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        static T RequiredAsset<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new InvalidOperationException("Missing mission asset: " + path);
            return asset;
        }

        static void Dirty(UnityEngine.Object target)
        {
            EditorUtility.SetDirty(target);
            if (PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }
}
