using System;
using System.Collections.Generic;
using System.Linq;
using LudumDare60.Salvage;
using LudumDare60.ShipAssembly;
using LudumDare60.TerminalDialogue;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LudumDare60.Landing.Editor
{
    /// <summary>Connects opening story and standby pages without changing the authored screen pipeline.</summary>
    public static class CockpitOpeningInstaller
    {
        public const string StoryPath = "Assets/Landing/PioneerOpeningStory.asset";

        [MenuItem("Tools/Landing/Install opening flow in SHIP")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Install the opening flow in Edit Mode after compilation.");
            Scene scene = SceneManager.GetSceneByPath(LandingSceneInstaller.ShipPath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open Assets/Scenes/SHIP.unity before installing its opening flow.");

            // Resolve and validate everything before creating components or writing serialized data.
            var flows = Components<CockpitOpeningController>(scene);
            if (flows.Length > 1) throw new InvalidOperationException("SHIP has multiple CockpitOpeningController components.");
            CockpitOpeningController flow = flows.FirstOrDefault();
            var landing = Resolve(scene, flow ? flow.Landing : null, "landing sequence");
            var assembly = Resolve(scene, flow ? flow.Assembly : null, "assembly controller");
            var salvage = Resolve(scene, flow ? flow.Salvage : null, "salvage controller");
            var terminal = Resolve(scene, flow ? flow.Terminal : null, "dialogue controller");
            var damage = Resolve(scene, flow ? flow.DamageEffects : null, "hard landing damage effects");
            var repairs = Resolve(scene, flow ? flow.RepairBridge : null, "hard landing repair bridge");
            if (!landing.ShipRig || !landing.ViewCamera || !landing.ShakePivot)
                throw new InvalidOperationException("The existing landing rig must be configured first.");
            if (damage.Sequence != landing || repairs.Effects != damage || !repairs.RepairScreen || assembly.RepairScreen != repairs.RepairScreen)
                throw new InvalidOperationException("Landing, damage and assembly must already share the configured repair screen and repair bridge.");

            var story = AssetDatabase.LoadAssetAtPath<TerminalDialogueDefinition>(StoryPath);
            if (!story || story.Graph == null) throw new InvalidOperationException("Missing opening conversation: " + StoryPath);
            var storyErrors = story.Graph.Validate();
            if (storyErrors.Count > 0)
                throw new InvalidOperationException("Opening conversation is invalid:\n" + string.Join("\n", storyErrors));
            if (story.Graph.FindNode("landing_crash_report") == null || story.Graph.FindNode("landing_retry") == null)
                throw new InvalidOperationException("Opening conversation must contain landing_crash_report and landing_retry nodes.");

            var assemblyView = Single(Components<AssemblyScreenView>(scene).Where(view => view.Controller == assembly), "assembly screen view");
            var salvageView = Single(Components<SalvageScreenView>(scene).Where(view =>
            {
                var fields = new SerializedObject(view);
                return fields.FindProperty("controller")?.objectReferenceValue == salvage;
            }), "salvage screen view bound to the salvage controller");
            // Salvage Screen is a Transform wrapper; its child prefab owns the actual Canvas/View.
            RectTransform assemblyRoot = RequiredRect(assemblyView);
            RectTransform salvageRoot = RequiredRect(salvageView);
            var repairCanvas = Single(Components<Canvas>(scene).Where(canvas =>
                canvas.name == "REPAIR_SCREEN_UI" && canvas.transform.IsChildOf(repairs.RepairScreen.transform)), "REPAIR_SCREEN_UI canvas");
            RectTransform repairRoot = RequiredRect(repairCanvas);
            var assemblyInputs = assemblyRoot.GetComponentsInChildren<AssemblyScreenInput>(true)
                .Where(input => input.Controller == assembly).Cast<Behaviour>().ToArray();
            if (assemblyInputs.Length == 0)
                throw new InvalidOperationException("The assembly screen has no configured AssemblyScreenInput.");

            var assemblyStandby = ExistingStandby(scene, flow ? flow.AssemblyStandby : null, assemblyRoot);
            var repairStandby = ExistingStandby(scene, flow ? flow.RepairInfoStandby : null, repairRoot);
            var salvageStandby = ExistingStandby(scene, flow ? flow.SalvageStandby : null, salvageRoot);
            var existingPages = new[] { assemblyStandby, repairStandby, salvageStandby }.Where(page => page).ToArray();
            if (existingPages.Distinct().Count() != existingPages.Length)
                throw new InvalidOperationException("Each display must use its own CockpitStandbyScreen component.");

            var terminalFields = new SerializedObject(terminal);
            var conversation = RequiredProperty(terminalFields, "conversation", SerializedPropertyType.ObjectReference);
            var startAutomatically = RequiredProperty(terminalFields, "startAutomatically", SerializedPropertyType.Boolean);
            var salvageFields = new SerializedObject(salvage);
            var featureEnabled = RequiredProperty(salvageFields, "featureEnabled", SerializedPropertyType.Boolean);
            var repairFields = new SerializedObject(repairs.RepairScreen);
            var resetOnPlay = RequiredProperty(repairFields, "resetOnPlay", SerializedPropertyType.Boolean);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install cockpit opening flow");
            try
            {
                if (!flow) flow = Undo.AddComponent<CockpitOpeningController>(landing.gameObject);
                assemblyStandby = ConfigureStandby(assemblyStandby, assemblyRoot, "ASSEMBLY WORKBENCH",
                    "ALL SYSTEMS NOMINAL\nREPAIR WORKBENCH NOT REQUIRED", assemblyInputs);
                repairStandby = ConfigureStandby(repairStandby, repairRoot, "SHIP DIAGNOSTICS",
                    "ALL SYSTEMS NOMINAL\nNO REPAIRS REQUIRED", Array.Empty<Behaviour>());
                salvageStandby = ConfigureStandby(salvageStandby, salvageRoot, "SALVAGE CONTROL",
                    "AWAITING PILOT AUTHORIZATION\nSALVAGE OPERATIONS ON HOLD", new Behaviour[] { salvageView });

                Undo.RecordObject(flow, "Bind opening flow");
                flow.Landing = landing;
                flow.Assembly = assembly;
                flow.Salvage = salvage;
                flow.Terminal = terminal;
                flow.DamageEffects = damage;
                flow.RepairBridge = repairs;
                flow.AssemblyStandby = assemblyStandby;
                flow.RepairInfoStandby = repairStandby;
                flow.SalvageStandby = salvageStandby;
                flow.enabled = true;
                Dirty(flow);

                Undo.RecordObject(landing, "Wait for the story landing command");
                landing.AutoStart = false;
                Dirty(landing);

                Undo.RecordObject(terminal, "Assign opening conversation");
                conversation.objectReferenceValue = story;
                // Keep the established TerminalDialogueController.Start lifecycle. Flow.Start only
                // initializes healthy systems; it intentionally does not call Terminal.Begin.
                startAutomatically.boolValue = true;
                terminalFields.ApplyModifiedProperties();
                Dirty(terminal);

                Undo.RecordObject(salvage, "Start salvage in standby");
                featureEnabled.boolValue = false;
                salvageFields.ApplyModifiedProperties();
                Dirty(salvage);

                Undo.RecordObject(repairs.RepairScreen, "Keep repair runtime initialization");
                resetOnPlay.boolValue = true;
                repairFields.ApplyModifiedProperties();
                Dirty(repairs.RepairScreen);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save SHIP.");
                Undo.CollapseUndoOperations(undoGroup);
                Debug.Log("SHIP opening flow saved: story-triggered landing, three standby pages and gated salvage. Existing cameras, screen canvases and render textures preserved.", flow);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        static CockpitStandbyScreen ConfigureStandby(CockpitStandbyScreen existing, RectTransform root,
            string systemTitle, string statusText, Behaviour[] requiredInputs)
        {
            bool created = !existing;
            var page = existing ? existing : Undo.AddComponent<CockpitStandbyScreen>(root.gameObject);
            Undo.RecordObject(page, "Configure cockpit standby page");
            page.ScreenRoot = root;
            if (!page.Font)
                page.Font = root.GetComponentsInChildren<TMP_Text>(true).Select(label => label.font).FirstOrDefault(font => font);
            if (created || string.IsNullOrWhiteSpace(page.SystemTitle) || page.SystemTitle == "AUXILIARY SYSTEM")
                page.SystemTitle = systemTitle;
            if (created || string.IsNullOrWhiteSpace(page.StatusText) || page.StatusText == "SYSTEM AVAILABLE WHEN REQUIRED\nAWAITING ACTIVATION")
                page.StatusText = statusText;
            page.InputBehaviours = (page.InputBehaviours ?? Array.Empty<Behaviour>()).Concat(requiredInputs)
                .Where(input => input).Distinct().ToArray();
            page.SetStandby(true); // Edit Mode records the initial state; runtime creates the overlay.
            page.enabled = true;
            Dirty(page);
            return page;
        }

        static CockpitStandbyScreen ExistingStandby(Scene scene, CockpitStandbyScreen preferred, RectTransform root)
        {
            var onRoot = root.GetComponent<CockpitStandbyScreen>();
            if (preferred && (preferred.gameObject.scene != scene || (preferred.ScreenRoot && preferred.ScreenRoot != root)))
                throw new InvalidOperationException("An existing standby binding targets a different scene or screen: " + preferred.name);
            if (preferred && onRoot && preferred != onRoot)
                throw new InvalidOperationException("Two standby controllers would cover " + root.name + ". Resolve the duplicate first.");
            var result = preferred ? preferred : onRoot;
            if (result && result.ScreenRoot && result.ScreenRoot != root)
                throw new InvalidOperationException("The existing standby component on " + root.name + " is bound to another screen.");
            return result;
        }

        static RectTransform RequiredRect(Component component)
        {
            var root = component.transform as RectTransform;
            if (!root || !root.GetComponent<Canvas>())
                throw new InvalidOperationException(component.name + " must own the existing screen Canvas and RectTransform.");
            return root;
        }

        static T Resolve<T>(Scene scene, T preferred, string label) where T : Component
        {
            if (preferred)
            {
                if (preferred.gameObject.scene != scene)
                    throw new InvalidOperationException("The existing " + label + " reference belongs to another scene.");
                return preferred;
            }
            return Single(Components<T>(scene), label);
        }

        static T Single<T>(IEnumerable<T> source, string label) where T : Component
        {
            T[] matches = source.ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Expected one " + label + " in SHIP; found " + matches.Length + ".");
            return matches[0];
        }

        static T[] Components<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        }

        static SerializedProperty RequiredProperty(SerializedObject owner, string name, SerializedPropertyType type)
        {
            SerializedProperty property = owner.FindProperty(name);
            if (property == null || property.propertyType != type)
                throw new InvalidOperationException(owner.targetObject.name + " is missing the expected serialized field: " + name);
            return property;
        }

        static void Dirty(UnityEngine.Object target)
        {
            EditorUtility.SetDirty(target);
            if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }
}
