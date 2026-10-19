using System;
using System.Linq;
using LudumDare60.ShipAssembly;
using LudumDare60.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;

namespace LudumDare60.Landing.Editor
{
    /// <summary>Adds the crash presentation without rebuilding the authored cockpit or camera rig.</summary>
    public static class HardLandingInstaller
    {
        const string SampleRoot = "Assets/Samples/Visual Effect Graph/17.6.0/Visual Effect Graph Additions/";
        const string AudioRoot = "Assets/Pioneer60Audio/Audio/";

        [MenuItem("Tools/Landing/Install hard landing effects in SHIP")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Install hard landing in Edit Mode after compilation.");
            var scene = SceneManager.GetSceneByPath(LandingSceneInstaller.ShipPath);
            if (!scene.isLoaded) throw new InvalidOperationException("Open SHIP before installing hard landing effects.");
            var sequence = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LandingSequenceController>(true)).Single();
            if (!sequence.ShipRig || !sequence.ViewCamera || !sequence.ShakePivot)
                throw new InvalidOperationException("The existing landing rig must already be wired.");

            // Validate every dependency before changing the scene.
            var sparksAsset = Required<VisualEffectAsset>(SampleRoot + "VFX/Sparks.vfx");
            var impact = Required<AudioClip>(AudioRoot + "03_Composites/CRASH_Impact_Short.wav");
            var hull = Required<AudioClip>(AudioRoot + "03_Composites/CRASH_Hull_Buckle.wav");
            var ambience = Required<AudioClip>(AudioRoot + "01_Loops/AMB_Cabin_Damaged_Loop.wav");
            var healthyAmbience = Required<AudioClip>(AudioRoot + "01_Loops/AMB_Cabin_Healthy_Loop.wav");
            var repairScreen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RepairScreenController>(true)).Single();
            var electrical = new[] {
                Required<AudioClip>(AudioRoot + "02_Single_Shots/02_Damage/SFX_electrical_glitch_01.wav"),
                Required<AudioClip>(AudioRoot + "02_Single_Shots/02_Damage/SFX_electrical_glitch_02.wav")
            };
            var warning = Required<AudioClip>("Assets/ReturnWindow/Art/Audio/WindowWarning.wav");
            var cameraBefore = sequence.ViewCamera.transform.localToWorldMatrix;
            var cameraLocalPosition = sequence.ViewCamera.transform.localPosition;
            var cameraLocalRotation = sequence.ViewCamera.transform.localRotation;

            Undo.SetCurrentGroupName("Install hard landing effects");
            var root = Child(sequence.ShipRig, "Hard Landing Damage");
            var damage = root.GetComponent<HardLandingDamageEffects>();
            if (!damage) damage = Undo.AddComponent<HardLandingDamageEffects>(root.gameObject);
            Undo.RecordObject(damage, "Configure hard landing damage");
            damage.Sequence = sequence;
            damage.ImpactAudio = Sound(root, "Impact Audio", impact, .85f, false, 0f);
            damage.HullAudio = Sound(root, "Hull Buckling Audio", hull, .42f, false, 0f);
            damage.DamagedAmbience = Sound(root, "Damaged Cabin Ambience", ambience, .19f, true, 0f);
            damage.HealthyAmbience = Sound(root, "Healthy Cabin Ambience", healthyAmbience, .19f, true, 0f);
            damage.AmbienceCrossfadeDuration = 1.2f;
            damage.AlarmAudio = Sound(root, "Damage Warning Audio", warning, .22f, false, 0f);
            damage.ElectricalClips = electrical;
            damage.Sites = new[] {
                Site(root, "Left Power Relay", new Vector3(.74f, 1.28f, -.38f), new Vector3(-.35f, .3f, .65f), sparksAsset),
                Site(root, "Right Console Wiring", new Vector3(-.74f, 1.50f, -.40f), new Vector3(.35f, .1f, .55f), sparksAsset),
                Site(root, "Overhead Harness", new Vector3(0f, 1.94f, -.50f), new Vector3(.15f, -.5f, .35f), sparksAsset)
            };
            var repairs = root.GetComponent<HardLandingRepairBridge>();
            if (!repairs) repairs = Undo.AddComponent<HardLandingRepairBridge>(root.gameObject);
            Undo.RecordObject(repairs, "Connect cabin faults to existing repairs");
            repairs.Effects = damage;
            repairs.RepairScreen = repairScreen;
            repairs.SiteDevices = new[] { RepairDeviceId.PowerUnit, RepairDeviceId.Navigation, RepairDeviceId.LifeSupport };
            EditorUtility.SetDirty(repairs);

            Undo.RecordObject(sequence, "Configure hard landing impact");
            sequence.TouchdownSpeed = 4f;
            sequence.HardLandingShakeDuration = 3f;
            sequence.HardLandingPosition = new Vector3(.09f, .16f, .10f);
            sequence.HardLandingRotation = new Vector3(3.5f, 2.2f, 4f);
            sequence.DustEmissionRate = 90f;
            sequence.DustBurstCount = 160;
            sequence.DustRadius = 5f;
            EditorUtility.SetDirty(sequence);
            EditorUtility.SetDirty(damage);

            if (sequence.ViewCamera.transform.localToWorldMatrix != cameraBefore ||
                sequence.ViewCamera.transform.localPosition != cameraLocalPosition ||
                sequence.ViewCamera.transform.localRotation != cameraLocalRotation)
                throw new InvalidOperationException("Camera pose changed unexpectedly; scene was not saved.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save SHIP.");
            Debug.Log("Hard landing wired: impact dust/shake, three electrical faults and existing audio. Camera pose preserved.");
        }

        static HardLandingDamageEffects.DamageSite Site(Transform root, string name, Vector3 position, Vector3 velocity, VisualEffectAsset asset)
        {
            var t = Child(root, name);
            Undo.RecordObject(t, "Position electrical fault");
            t.localPosition = position;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            var fx = t.GetComponent<VisualEffect>();
            if (!fx) fx = Undo.AddComponent<VisualEffect>(t.gameObject);
            Undo.RecordObject(fx, "Configure cabin sparks");
            fx.visualEffectAsset = asset;
            fx.initialEventName = "OnStop";
            fx.resetSeedOnPlay = true;
            fx.SetVector3("Initial Position", Vector3.zero);
            fx.SetVector3("Initial Velocity_vector", velocity);
            fx.SetFloat("Initial Velocity Divergence", .45f);
            fx.SetVector3("Gravity Vector", new Vector3(0f, -3.71f, 0f));
            fx.SetVector2("Burst Count Min/Max", new Vector2(18f, 36f));
            // One burst per Play; the runtime controller schedules the short fault windows.
            fx.SetVector2("Loop Duration Min/Max", new Vector2(2f, 3f));
            fx.SetVector2("Lifetime Min/Max", new Vector2(.16f, .55f));
            fx.SetVector2("Size Min/Max", new Vector2(.012f, .028f));
            fx.SetFloat("Spawn Rate", 35f);
            fx.SetFloat("Density", 1f);
            fx.SetFloat("Sparkle Width", .1f);
            fx.SetFloat("Drag", .4f);
            fx.SetFloat("Turbulence Intensity", .18f);
            fx.SetVector3("Bounds_center", new Vector3(0f, -.35f, .2f));
            fx.SetVector3("Bounds_size", new Vector3(2f, 2.5f, 2f));
            var color = new Gradient();
            color.SetKeys(new[] {
                new GradientColorKey(new Color(1.5f, 1.9f, 2.2f), 0f),
                new GradientColorKey(new Color(1.7f, .85f, .2f), .35f),
                new GradientColorKey(new Color(.8f, .16f, .025f), 1f)
            }, new[] {new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .6f), new GradientAlphaKey(0f, 1f)});
            fx.SetGradient("Color Over Life", color);
            fx.Stop();
            fx.pause = true;
            EditorUtility.SetDirty(fx);
            var flash = t.GetComponent<Light>();
            if (!flash) flash = Undo.AddComponent<Light>(t.gameObject);
            Undo.RecordObject(flash, "Configure electrical flash");
            flash.type = LightType.Point;
            flash.color = new Color(.55f, .76f, 1f);
            flash.range = 1f;
            flash.intensity = .28f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
            return new HardLandingDamageEffects.DamageSite {
                Transform = t, Sparks = fx, Flash = flash,
                ElectricalAudio = Sound(t, "Electrical Audio", null, .38f, false, .75f)
            };
        }

        static AudioSource Sound(Transform parent, string name, AudioClip clip, float volume, bool loop, float spatialBlend)
        {
            var t = Child(parent, name);
            var source = t.GetComponent<AudioSource>();
            if (!source) source = Undo.AddComponent<AudioSource>(t.gameObject);
            Undo.RecordObject(source, "Configure crash audio");
            source.clip = clip;
            source.playOnAwake = false;
            source.loop = loop;
            source.volume = volume;
            source.spatialBlend = spatialBlend;
            source.dopplerLevel = 0f;
            source.minDistance = 1.2f;
            source.maxDistance = 5f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.priority = loop ? 140 : 64;
            EditorUtility.SetDirty(source);
            return source;
        }

        static Transform Child(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing) return existing;
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(go, "Create hard landing effect");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static T Required<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new InvalidOperationException("Missing hard landing asset: " + path);
            return asset;
        }
    }
}
