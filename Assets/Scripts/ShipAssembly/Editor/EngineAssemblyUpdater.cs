using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LudumDare60.AssemblyDemo;
using LudumDare60.ReturnWindow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LudumDare60.ShipAssembly.Editor
{
    /// <summary>Refreshes the engine only, preserving its authored visual corrections and assembly relationship.</summary>
    public static class EngineAssemblyUpdater
    {
        private const string SourcePath = "Assets/Prefabs/引擎.prefab";
        private const string RecipePath = "Assets/ShipAssembly/Recipes/PropulsionEngine.asset";
        private const float ViewAspect = 1.5f; // The existing SHIP render texture; never change the screen.
        private const float ViewPadding = 1.12f;

        private sealed class PartUpdate
        {
            public AssemblyModule Module;
            public Transform Source;
            public Vector3 SourcePosition, TargetPosition;
            public Quaternion SourceRotation, TargetRotation;
            public Vector3 StartVisualPosition, EndVisualPosition;
            public Quaternion StartVisualRotation, EndVisualRotation;
            public Vector3 VisualScale;
            public Vector3 StartPivot, EndPivot;
            public Quaternion StartModuleRotation;
        }

        [MenuItem("Tools/Ship Assembly/Update Engine Assembly Only")]
        public static void UpdateEngineOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before updating the engine.");
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && (stage.assetPath == SourcePath || stage.assetPath == NativeRepairLevelInstaller.EnginePrefab))
                throw new InvalidOperationException("Save and close the engine Prefab Stage before updating it.");

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NativeRepairLevelInstaller.EnginePrefab);
            var recipe = AssetDatabase.LoadAssetAtPath<AssemblyRecipe>(RecipePath);
            if (source == null || prefab == null || recipe == null || recipe.PuzzlePrefab != prefab)
                throw new InvalidOperationException("The engine source, assembly prefab and existing engine recipe must be connected.");
            if (EditorUtility.IsDirty(recipe))
                throw new InvalidOperationException("Save the engine recipe's Inspector edits before updating its view.");

            // Work on isolated prefab contents. Do not save/open scenes or run any gameplay callbacks.
            GameObject root = PrefabUtility.LoadPrefabContents(NativeRepairLevelInstaller.EnginePrefab);
            try
            {
                var assembly = root.GetComponent<AssemblyDemoController>();
                var layout = root.GetComponent<DeviceAssemblyLayout>();
                var parts = ReadUpdates(source, root, assembly, layout);
                bool wasActive = root.activeSelf;
                root.SetActive(false); // Dispose generated Xray/connector caches before editing source geometry.
                foreach (var part in parts)
                {
                    RefreshModel(part);
                    var module = part.Module;
                    var visual = module.modelVisual;
                    // Move the logical pivot to the actual visible mesh center, preserving the model pose.
                    module.transform.localPosition = Vector3.zero;
                    module.transform.localRotation = Quaternion.identity;
                    visual.localScale = part.VisualScale;
                    visual.localPosition = part.StartVisualPosition;
                    visual.localRotation = part.StartVisualRotation;
                    var startBounds = MeshBounds(root.transform, visual);
                    part.StartPivot = new Vector3(startBounds.center.x, startBounds.center.y, 0);
                    // Transport the same pivot so snapping and the completion pose agree after rotation.
                    var endPivot = part.EndVisualPosition + Quaternion.Inverse(part.StartModuleRotation) *
                        (part.StartPivot - part.StartVisualPosition);
                    part.EndPivot = new Vector3(endPivot.x, endPivot.y, 0);
                    module.transform.localPosition = part.StartPivot;
                    module.transform.localRotation = part.StartModuleRotation;
                    visual.localPosition = Quaternion.Inverse(part.StartModuleRotation) * (part.StartVisualPosition - part.StartPivot);
                    visual.localRotation = Quaternion.Inverse(part.StartModuleRotation) * part.StartVisualRotation;
                    var localBounds = MeshBounds(module.transform, visual);
                    module.modelBoundsCenter = new Vector2(localBounds.center.x, localBounds.center.y);
                    module.modelBoundsSize = new Vector2(Mathf.Max(.14f, localBounds.size.x), Mathf.Max(.14f, localBounds.size.y));
                    module.radius = Mathf.Max(.1f, new Vector2(localBounds.extents.x, localBounds.extents.y).magnitude);
                }

                var startAndEnd = new Bounds();
                bool first = true;
                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    layout.nativeStartPositions[i] = part.SourcePosition;
                    layout.nativeStartRotations[i] = part.SourceRotation;
                    layout.nativeTargetPositions[i] = part.TargetPosition;
                    layout.nativeTargetRotations[i] = part.TargetRotation;
                    layout.assembledLocalPositions[i] = part.EndPivot;
                    layout.assembledModelPositions[i] = part.EndVisualPosition - part.EndPivot;
                    layout.assembledModelRotations[i] = part.EndVisualRotation;
                    // Completion sets module rotation to identity and keeps the authored visual scale.
                    Encapsulate(ref startAndEnd, ref first, MeshBounds(root.transform, part.Module.modelVisual));
                    var visual = part.Module.modelVisual;
                    var endMatrix = Matrix4x4.TRS(part.EndVisualPosition, part.EndVisualRotation, part.VisualScale);
                    Encapsulate(ref startAndEnd, ref first, MeshBounds(visual, visual, endMatrix));
                }
                UpdatePorts(parts);
                FitDepth(parts, layout, ref startAndEnd);
                layout.viewCenter = startAndEnd.center;
                float size = Mathf.Max(.5f, startAndEnd.extents.y, startAndEnd.extents.x / ViewAspect) * ViewPadding;

                root.SetActive(wasActive);
                // File backups are for this edit only; no unrelated scene or asset is saved.
                string backup = Path.Combine("Logs/EngineAssemblyUpdates", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
                Directory.CreateDirectory(backup);
                File.Copy(NativeRepairLevelInstaller.EnginePrefab, Path.Combine(backup, "PropulsionEngineAssembly.prefab"));
                File.Copy(RecipePath, Path.Combine(backup, "PropulsionEngine.asset"));
                string previousRecipe = EditorJsonUtility.ToJson(recipe);
                try
                {
                    PrefabUtility.SaveAsPrefabAsset(root, NativeRepairLevelInstaller.EnginePrefab, out bool saved);
                    if (!saved) throw new IOException("Could not save the engine assembly prefab.");
                    recipe.PuzzleOffset = new Vector3(-layout.viewCenter.x, -layout.viewCenter.y, 0);
                    recipe.OrthographicSize = size;
                    EditorUtility.SetDirty(recipe);
                    AssetDatabase.SaveAssetIfDirty(recipe);
                }
                catch (Exception saveError)
                {
                    try
                    {
                        EditorJsonUtility.FromJsonOverwrite(previousRecipe, recipe);
                        File.Copy(Path.Combine(backup, "PropulsionEngineAssembly.prefab"), NativeRepairLevelInstaller.EnginePrefab, true);
                        File.Copy(Path.Combine(backup, "PropulsionEngine.asset"), RecipePath, true);
                        AssetDatabase.ImportAsset(NativeRepairLevelInstaller.EnginePrefab, ImportAssetOptions.ForceUpdate);
                        AssetDatabase.ImportAsset(RecipePath, ImportAssetOptions.ForceUpdate);
                    }
                    catch (Exception restoreError)
                    {
                        throw new AggregateException("Engine update could not be restored. Original files: " + backup, saveError, restoreError);
                    }
                    throw new IOException("Engine update was not saved; both original assets were restored. Backup: " + backup, saveError);
                }
                Debug.Log("[ShipAssembly] Updated engine geometry, hit bounds, ports, completion poses and view. " +
                          "Existing assembly relationships and visual corrections were retained. Other assets and material costs were not changed. Backup: " + backup);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static PartUpdate[] ReadUpdates(GameObject source, GameObject root, AssemblyDemoController assembly, DeviceAssemblyLayout layout)
        {
            if (assembly == null || layout == null || layout.assembly != assembly || assembly.Modules == null || assembly.Modules.Length != 3)
                throw new InvalidOperationException("Expected the existing three-part engine assembly.");
            if (root.transform.localScale != Vector3.one || Quaternion.Angle(root.transform.localRotation, Quaternion.identity) > .001f)
                throw new InvalidOperationException("The assembly root must keep unit scale and identity rotation.");
            RequireLength(layout.nativeStartPositions, 3); RequireLength(layout.nativeStartRotations, 3);
            RequireLength(layout.nativeTargetPositions, 3); RequireLength(layout.nativeTargetRotations, 3);
            RequireLength(layout.assembledLocalPositions, 3); RequireLength(layout.assembledModelPositions, 3);
            RequireLength(layout.assembledModelRotations, 3);
            if (!Finite(layout.modelScale) || layout.modelScale <= 0f || !Finite(layout.modelToBoard))
                throw new InvalidOperationException("Invalid native-to-board conversion.");

            var result = new PartUpdate[3];
            var basis = layout.modelToBoard.normalized;
            var sourceTransforms = source.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < result.Length; i++)
            {
                string name = NativeRepairLevelInstaller.EngineNames[i];
                var matches = sourceTransforms.Where(t => t.name == name).ToArray();
                var module = assembly.Modules[i];
                if (matches.Length != 1 || matches[0].parent != source.transform || module == null || module.name != name || module.transform.parent != root.transform)
                    throw new InvalidOperationException("Keep one top-level source part and one engine module named " + name + ".");
                var part = matches[0];
                var visual = module.modelVisual;
                if (visual == null || visual.parent != module.transform || visual.childCount != 1 || visual.GetChild(0).name != name)
                    throw new InvalidOperationException("Expected one named source model beneath Native model for " + name + ".");
                if ((module.transform.localScale - Vector3.one).sqrMagnitude > .000001f || Mathf.Abs(module.transform.localPosition.z) > .001f ||
                    Vector3.Dot(module.transform.localRotation * Vector3.forward, Vector3.forward) < .99999f)
                    throw new InvalidOperationException("Engine module transforms must use unit scale and the XY plane.");
                if (!Finite(part.localPosition) || !Finite(part.localRotation) || !Finite(part.localScale) ||
                    Mathf.Abs(part.localScale.x * part.localScale.y * part.localScale.z) < .000001f ||
                    !Finite(visual.localPosition) || !Finite(visual.localRotation) || !Finite(visual.localScale) ||
                    Mathf.Abs(visual.localScale.x * visual.localScale.y * visual.localScale.z) < .000001f)
                    throw new InvalidOperationException("Invalid engine part transform: " + name);
                if (part.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0 || part.GetComponentsInChildren<MeshFilter>(true).All(f => f.sharedMesh == null))
                    throw new InvalidOperationException("Engine parts must contain static MeshFilter geometry: " + name);
                RequireUniqueChildNames(part);
                RequireUniqueChildNames(visual.GetChild(0));
                if (!Finite(layout.nativeStartPositions[i]) || !Finite(layout.nativeTargetPositions[i]) ||
                    !Finite(layout.nativeStartRotations[i]) || !Finite(layout.nativeTargetRotations[i]))
                    throw new InvalidOperationException("Invalid recorded engine pose: " + name);

                var oldStart = layout.nativeStartPositions[i];
                var oldRotation = layout.nativeStartRotations[i].normalized;
                // Avoid repeated quaternion round trips when nothing in the source pose changed.
                bool moved = (part.localPosition - oldStart).sqrMagnitude > .00000001f;
                bool rotated = Quaternion.Angle(part.localRotation, oldRotation) > .001f;
                var sourcePosition = moved ? part.localPosition : oldStart;
                var sourceRotation = rotated ? part.localRotation : oldRotation;
                var delta = rotated ? sourceRotation * Quaternion.Inverse(oldRotation) : Quaternion.identity;
                var targetPosition = sourcePosition + delta * (layout.nativeTargetPositions[i] - oldStart);
                var targetRotation = rotated ? delta * layout.nativeTargetRotations[i] : layout.nativeTargetRotations[i];

                var startPosition = module.transform.localPosition + module.transform.localRotation * visual.localPosition;
                var startRotation = module.transform.localRotation * visual.localRotation;
                if (moved || rotated)
                {
                    var boardDelta = basis * delta * Quaternion.Inverse(basis);
                    startPosition = basis * sourcePosition * layout.modelScale + boardDelta * (startPosition - basis * oldStart * layout.modelScale);
                    startRotation = boardDelta * startRotation;
                }
                var endDelta = Quaternion.Angle(targetRotation, sourceRotation) <= .001f ? Quaternion.identity :
                    (basis * (targetRotation * Quaternion.Inverse(sourceRotation)) * Quaternion.Inverse(basis)).normalized;
                if (Vector3.Dot(endDelta * Vector3.forward, Vector3.forward) < .99999f)
                    throw new InvalidOperationException("The start-to-target rotation must stay in the assembly XY plane: " + name);
                var endRight = endDelta * Vector3.right;
                endDelta = Quaternion.AngleAxis(Mathf.Atan2(endRight.y, endRight.x) * Mathf.Rad2Deg, Vector3.forward);
                result[i] = new PartUpdate
                {
                    Module = module, Source = part, SourcePosition = sourcePosition, SourceRotation = sourceRotation,
                    TargetPosition = targetPosition, TargetRotation = targetRotation,
                    StartVisualPosition = startPosition, StartVisualRotation = startRotation,
                    EndVisualPosition = basis * targetPosition * layout.modelScale + endDelta * (startPosition - basis * sourcePosition * layout.modelScale),
                    EndVisualRotation = endDelta * startRotation, VisualScale = visual.localScale,
                    StartModuleRotation = Quaternion.Inverse(endDelta)
                };
            }
            return result;
        }

        private static void RefreshModel(PartUpdate part)
        {
            // Retain existing GameObject/component file IDs; add/remove only genuinely changed source nodes.
            SyncVisualTree(part.Source, part.Module.modelVisual.GetChild(0), true);
        }

        private static void SyncVisualTree(Transform source, Transform target, bool partRoot = false)
        {
            target.localPosition = partRoot ? Vector3.zero : source.localPosition;
            target.localRotation = partRoot ? Quaternion.identity : source.localRotation;
            target.localScale = source.localScale; // Preserve mirrored END PLATE axes.
            target.gameObject.SetActive(source.gameObject.activeSelf);
            var sourceFilter = source.GetComponent<MeshFilter>();
            var targetFilter = target.GetComponent<MeshFilter>();
            if (sourceFilter != null)
            {
                if (targetFilter == null) targetFilter = target.gameObject.AddComponent<MeshFilter>();
                targetFilter.sharedMesh = sourceFilter.sharedMesh;
            }
            else if (targetFilter != null) Object.DestroyImmediate(targetFilter);
            var sourceRenderer = source.GetComponent<MeshRenderer>();
            var targetRenderer = target.GetComponent<MeshRenderer>();
            if (sourceRenderer != null)
            {
                if (targetRenderer == null) targetRenderer = target.gameObject.AddComponent<MeshRenderer>();
                EditorUtility.CopySerialized(sourceRenderer, targetRenderer);
                targetRenderer.enabled = true;
            }
            else if (targetRenderer != null) Object.DestroyImmediate(targetRenderer);

            var remaining = target.Cast<Transform>().ToList();
            foreach (Transform sourceChild in source)
            {
                if ((sourceChild.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                var targetChild = remaining.FirstOrDefault(t => t.name == sourceChild.name && (t.gameObject.hideFlags & HideFlags.DontSave) == 0);
                if (targetChild == null)
                {
                    targetChild = new GameObject(sourceChild.name).transform;
                    targetChild.SetParent(target, false);
                    targetChild.gameObject.layer = target.gameObject.layer;
                }
                else remaining.Remove(targetChild);
                SyncVisualTree(sourceChild, targetChild);
                targetChild.SetSiblingIndex(sourceChild.GetSiblingIndex());
            }
            foreach (var removed in remaining) Object.DestroyImmediate(removed.gameObject);
        }

        private static void RequireUniqueChildNames(Transform root)
        {
            var names = new HashSet<string>();
            foreach (Transform child in root)
            {
                if ((child.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                if (!names.Add(child.name)) throw new InvalidOperationException("Engine model siblings need unique names: " + child.name);
                RequireUniqueChildNames(child);
            }
        }

        private static void FitDepth(PartUpdate[] parts, DeviceAssemblyLayout layout, ref Bounds bounds)
        {
            // Existing SHIP camera is at Z=-10, near=.05, far=30; leave the camera and screen untouched.
            const float minimum = -9.85f, maximum = 19.85f;
            if (bounds.size.z > maximum - minimum)
                throw new InvalidOperationException("Engine geometry is too deep for the existing camera. Reduce the source geometry's depth before updating.");
            float shift = Mathf.Clamp(0, minimum - bounds.min.z, maximum - bounds.max.z);
            if (shift == 0) return;
            var translation = Vector3.forward * shift;
            for (int i = 0; i < parts.Length; i++)
            {
                var module = parts[i].Module;
                module.modelVisual.localPosition += Quaternion.Inverse(module.transform.localRotation) * translation;
                layout.assembledModelPositions[i] += translation;
            }
            bounds.center += translation;
        }

        private static void UpdatePorts(PartUpdate[] parts)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                int count = i == 0 ? parts.Length - 1 : 1;
                var module = parts[i].Module;
                module.portAngles = new float[count]; module.portDistances = new float[count]; module.portKeys = new string[count];
            }
            for (int i = 1; i < parts.Length; i++)
            {
                Vector2 difference = parts[i].EndPivot - parts[0].EndPivot;
                if (!Finite(difference) || difference.magnitude < .02f)
                    throw new InvalidOperationException("Engine target centers overlap; author distinct assembly targets before updating.");
                float angle = AssemblySnapSolver.ClockwiseAngle(difference);
                string key = parts[i].Module.moduleLabel;
                if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Each engine part needs a connector label.");
                SetPort(parts[0].Module, i - 1, angle, difference.magnitude * .5f, key);
                SetPort(parts[i].Module, 0, angle + 180f, difference.magnitude * .5f, key);
            }
        }

        private static void SetPort(AssemblyModule module, int port, float angle, float distance, string key)
        {
            module.portAngles[port] = AssemblySnapSolver.NormalizeAngle(angle);
            module.portDistances[port] = distance;
            module.portKeys[port] = key;
        }

        private static Bounds MeshBounds(Transform relativeTo, Transform visual, Matrix4x4? placement = null)
        {
            bool first = true;
            var bounds = new Bounds();
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || IsGenerated(filter.transform, visual)) continue;
                var matrix = placement.HasValue
                    ? placement.Value * visual.worldToLocalMatrix * filter.transform.localToWorldMatrix
                    : relativeTo.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var mesh = filter.sharedMesh.bounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(mesh.center + Vector3.Scale(mesh.extents, new Vector3(x, y, z)));
                    if (!Finite(point)) throw new InvalidOperationException("Non-finite engine mesh bounds.");
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                }
            }
            if (first) throw new InvalidOperationException("The engine part has no readable mesh bounds.");
            return bounds;
        }

        private static bool IsGenerated(Transform node, Transform visual)
        {
            for (var current = node; current != null; current = current.parent)
            {
                if ((current.gameObject.hideFlags & HideFlags.DontSave) != 0) return true;
                if (current == visual) break;
            }
            return false;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool first, Bounds addition)
        { if (first) { bounds = addition; first = false; } else bounds.Encapsulate(addition); }
        private static void RequireLength(Array values, int length)
        { if (values == null || values.Length != length) throw new InvalidOperationException("Engine pose arrays must match the three existing modules."); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w) && Quaternion.Dot(value, value) > .000001f;
    }
}
