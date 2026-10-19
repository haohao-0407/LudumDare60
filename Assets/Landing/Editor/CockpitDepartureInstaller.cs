using System;
using System.Linq;
using LudumDare60.TerminalDialogue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LudumDare60.Landing.Editor
{
    /// <summary>Connects departure to the existing cockpit without rebuilding its authored rig.</summary>
    public static class CockpitDepartureInstaller
    {
        const string MenuPath = "Tools/Landing/Install departure flow in SHIP";
        const string StoryGuid = "5f37b0bcdadb4af0a181775f0bc4b3a0";
        const string AudioRoot = "Assets/Pioneer60Audio/Audio/";
        const string StoryAudioRoot = AudioRoot + "02_Single_Shots/08_Repair_Story/";

        [MenuItem(MenuPath)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Install the departure flow in Edit Mode after compilation and asset import.");

            Scene scene = SceneManager.GetSceneByPath(LandingSceneInstaller.ShipPath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open Assets/Scenes/SHIP.unity before installing its departure flow.");

            // Complete all scene, asset and serialized-field checks before adding or changing anything.
            var openings = Components<CockpitOpeningController>(scene);
            if (openings.Length != 1)
                throw new InvalidOperationException("Expected one CockpitOpeningController in SHIP; found " + openings.Length + ".");
            CockpitOpeningController opening = openings[0];
            var landing = RequiredSceneReference(scene, opening.Landing, "Opening.Landing");
            var assembly = RequiredSceneReference(scene, opening.Assembly, "Opening.Assembly");
            RequiredSceneReference(scene, opening.Salvage, "Opening.Salvage");
            var terminal = RequiredSceneReference(scene, opening.Terminal, "Opening.Terminal");
            var damage = RequiredSceneReference(scene, opening.DamageEffects, "Opening.DamageEffects");
            var repairBridge = RequiredSceneReference(scene, opening.RepairBridge, "Opening.RepairBridge");
            var repairScreen = RequiredSceneReference(scene, assembly.RepairScreen, "Opening.Assembly.RepairScreen");
            RequiredSceneReference(scene, landing.ShipRig, "Opening.Landing.ShipRig");
            RequiredSceneReference(scene, landing.ViewCamera, "Opening.Landing.ViewCamera");
            RequiredSceneReference(scene, landing.ShakePivot, "Opening.Landing.ShakePivot");
            if (damage.Sequence != landing || repairBridge.Effects != damage || repairBridge.RepairScreen != repairScreen)
                throw new InvalidOperationException("Opening, landing damage and assembly must share the same landing sequence and repair screen.");

            var launch = ExistingOnOpening<LaunchSequenceController>(scene, opening);
            var departure = ExistingOnOpening<CockpitDepartureController>(scene, opening);
            var story = RequiredAsset<TerminalDialogueDefinition>(CockpitOpeningInstaller.StoryPath);
            if (AssetDatabase.AssetPathToGUID(CockpitOpeningInstaller.StoryPath) != StoryGuid)
                throw new InvalidOperationException("PioneerOpeningStory must retain its existing GUID: " + StoryGuid);
            if (story.Graph == null) throw new InvalidOperationException("PioneerOpeningStory has no dialogue graph.");
            var graphErrors = story.Graph.Validate();
            if (graphErrors.Count > 0)
                throw new InvalidOperationException("Opening conversation is invalid:\n" + string.Join("\n", graphErrors));
            foreach (string nodeId in new[] { "departure_ready", "departure_testing", "departure_launch_ready", "launch_orbit" })
                if (story.Graph.FindNode(nodeId) == null)
                    throw new InvalidOperationException("PioneerOpeningStory is missing departure node: " + nodeId);

            var countdownClip = RequiredAsset<AudioClip>(AudioRoot + "02_Single_Shots/03_Terminal/UI_confirm.wav");
            var ignitionClip = RequiredAsset<AudioClip>(StoryAudioRoot + "STORY_takeoff.wav");
            var engineLoopClip = RequiredAsset<AudioClip>(
                "Assets/Pioneer60Audio/494827__nicktimesaudio__seamless-rocket-booster-roar-crackle.mp3");
            var readyClip = RequiredAsset<AudioClip>(StoryAudioRoot + "STORY_ready_departure.wav");
            var checkClips = new[]
            {
                RequiredAsset<AudioClip>(StoryAudioRoot + "STORY_power_restore.wav"),
                RequiredAsset<AudioClip>(StoryAudioRoot + "STORY_navigation_online.wav"),
                RequiredAsset<AudioClip>(StoryAudioRoot + "STORY_engine_test.wav"),
                RequiredAsset<AudioClip>(StoryAudioRoot + "STORY_life_support_valve.wav")
            };
            var terminalFields = new SerializedObject(terminal);
            var conversation = terminalFields.FindProperty("conversation");
            if (conversation == null || conversation.propertyType != SerializedPropertyType.ObjectReference)
                throw new InvalidOperationException("The opening terminal is missing its serialized conversation field.");

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install cockpit departure flow");
            try
            {
                if (!launch) launch = Undo.AddComponent<LaunchSequenceController>(opening.gameObject);
                if (!departure) departure = Undo.AddComponent<CockpitDepartureController>(opening.gameObject);

                Undo.RecordObject(launch, "Bind cockpit launch sequence");
                launch.Landing = landing;
                launch.RepairScreen = repairScreen;
                launch.CountdownClip = countdownClip;
                launch.IgnitionClip = ignitionClip;
                launch.EngineLoopClip = engineLoopClip;
                launch.enabled = true;
                Dirty(launch);

                Undo.RecordObject(departure, "Bind cockpit departure checks");
                departure.Opening = opening;
                departure.Launch = launch;
                departure.ReadyClip = readyClip;
                departure.CheckClips = checkClips;
                departure.enabled = true;
                Dirty(departure);

                if (terminal.Conversation != story)
                {
                    Undo.RecordObject(terminal, "Bind the opening and departure conversation");
                    conversation.objectReferenceValue = story;
                    terminalFields.ApplyModifiedProperties();
                    Dirty(terminal);
                }

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save SHIP.");
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }

            Debug.Log("SHIP departure flow saved: four system checks, launch sequence and departure audio connected.", departure);
        }

        static T ExistingOnOpening<T>(Scene scene, CockpitOpeningController opening) where T : Component
        {
            T[] components = Components<T>(scene);
            if (components.Length > 1)
                throw new InvalidOperationException("SHIP has multiple " + typeof(T).Name + " components.");
            if (components.Length == 0) return null;
            if (components[0].gameObject != opening.gameObject)
                throw new InvalidOperationException("The existing " + typeof(T).Name + " must be on the CockpitOpeningController object before installing departure.");
            return components[0];
        }

        static T RequiredSceneReference<T>(Scene scene, T reference, string label) where T : Component
        {
            if (!reference || reference.gameObject.scene != scene)
                throw new InvalidOperationException(label + " must explicitly reference an object in SHIP.");
            return reference;
        }

        static T[] Components<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        }

        static T RequiredAsset<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new InvalidOperationException("Missing departure asset: " + path);
            return asset;
        }

        static void Dirty(UnityEngine.Object target)
        {
            EditorUtility.SetDirty(target);
            if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }
}
