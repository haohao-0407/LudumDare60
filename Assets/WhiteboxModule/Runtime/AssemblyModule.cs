using System;
using System.Collections.Generic;
using UnityEngine;

namespace LudumDare60.AssemblyDemo
{
    /// <summary>A reusable XY whitebox. Local port angles increase clockwise from up.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Whitebox Module/Whitebox Module")]
    public sealed class AssemblyModule : MonoBehaviour
    {
        public const float AngleTolerance = 5f;
        public const float LateralTolerance = 0.16f;
        public const float ContactTolerance = 0.12f;
        public string moduleLabel = "MODULE";
        [Min(0.01f)] public float radius = 0.9f;
        [Tooltip("0 = up, 90 = right; empty means one 0-degree port.")]
        public float[] portAngles = Array.Empty<float>();
        [SerializeField, HideInInspector] private Material fillMaterial, inkMaterial;
        [SerializeField, HideInInspector] private Color selectionColor = new Color(0.36f,0.94f,0.73f);

        // Generated geometry is a disposable cache, never part of a prefab or saved scene.
        [NonSerialized] private Transform geometryRoot;
        [NonSerialized] private Mesh faceMesh;
        [NonSerialized] private LineRenderer outline;
        [NonSerialized] private LineRenderer[] arrows;
        [NonSerialized] private int visualSignature;
        [NonSerialized] private bool dirty = true;
        [NonSerialized] private bool selected;
        private readonly HashSet<int> highlightedPorts = new HashSet<int>();
        private const HideFlags GeometryFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

        public Vector2 Position { get => transform.position; set => transform.position = new Vector3(value.x, value.y, transform.position.z); }
        public float RotationDegrees { get => Mathf.Repeat(-transform.eulerAngles.z, 360f); set => transform.rotation = Quaternion.Euler(0,0,-value); }
        public int PortCount => portAngles == null || portAngles.Length == 0 ? 1 : portAngles.Length;
        public float PortAngle(int index) => portAngles == null || portAngles.Length == 0 ? 0f : portAngles[index];
        public Vector2 PortDirection(int index) => AssemblySnapSolver.Direction(RotationDegrees + PortAngle(index));
        public Vector2 PortPosition(int index) => Position + PortDirection(index) * radius;

        /// <summary>Copies the caller's array; omitted/null angles mean a single 0-degree arrow.</summary>
        public void SetAngles(params float[] angles)
        {
            var copy = angles == null ? Array.Empty<float>() : new float[angles.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                if (!Finite(angles[i])) throw new ArgumentException("Angles must be finite.", nameof(angles));
                copy[i] = AssemblySnapSolver.NormalizeAngle(angles[i]);
            }
            portAngles = copy;
            Rebuild();
        }

        public void SetRadius(float value)
        {
            if (!Finite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Radius must be finite and positive.");
            radius = value;
            Rebuild();
        }

        /// <summary>One-shot alignment of this module to target; does not create a persistent joint.</summary>
        public bool TrySnapTo(AssemblyModule target)
        {
            if (target == null || target == this || Mathf.Abs(transform.position.z - target.transform.position.z) > 0.001f) return false;
            if (!AssemblySnapSolver.TrySolve(Position, RotationDegrees, radius, portAngles,
                target.Position, target.RotationDegrees, target.radius, target.portAngles,
                AngleTolerance, LateralTolerance, ContactTolerance, out var solution)) return false;
            Position = solution.Position;
            RotationDegrees = solution.RotationDegrees;
            return true;
        }

        private void OnEnable() { dirty = true; }
        private void OnDisable() { ClearGeometry(); }
        private void OnDestroy() { ClearGeometry(); }
        private void OnValidate() { dirty = true; }

        private int VisualSignature()
        {
            unchecked
            {
                int hash = radius.GetHashCode();
                for (int i = 0; i < PortCount; i++) hash = hash * 397 ^ PortAngle(i).GetHashCode();
                return hash;
            }
        }

        private void Update()
        {
            if (dirty || geometryRoot == null || visualSignature != VisualSignature()) Rebuild();
        }

        public void ConfigureVisuals(Material fill, Material ink)
        {
            fillMaterial = fill;
            inkMaterial = ink;
            Rebuild();
        }

        /// <summary>Recreates only nonpersistent geometry. Prefab source data stays intact.</summary>
        public void Rebuild()
        {
            dirty = false;
            if (!isActiveAndEnabled) { ClearGeometry(); return; }
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(this)) return;
#endif
            if (!gameObject.scene.IsValid()) return;
            if (!Finite(radius) || radius <= 0) radius = 0.01f;
            if (portAngles != null)
                for (int i = 0; i < portAngles.Length; i++)
                    portAngles[i] = Finite(portAngles[i]) ? AssemblySnapSolver.NormalizeAngle(portAngles[i]) : 0;
            if (fillMaterial == null) fillMaterial = Resources.Load<Material>("WhiteboxModule/Whitebox");
            if (inkMaterial == null) inkMaterial = Resources.Load<Material>("WhiteboxModule/VertexInk");
            if (fillMaterial == null || inkMaterial == null) return;
            ClearGeometry();
            geometryRoot = MakeObject("Whitebox geometry", transform).transform;
            var face = MakeObject("White square", geometryRoot);
            face.transform.localPosition = new Vector3(0,0,radius * 0.08f);
            face.transform.localScale = new Vector3(radius * 2,radius * 2,1);
            faceMesh = new Mesh { name = "Whitebox quad", hideFlags = GeometryFlags };
            faceMesh.vertices = new[] {
                new Vector3(-0.5f,-0.5f,0),new Vector3(-0.5f,0.5f,0),
                new Vector3(0.5f,0.5f,0),new Vector3(0.5f,-0.5f,0)
            };
            faceMesh.triangles = new[] { 0,1,2,0,2,3 };
            faceMesh.colors = new[] { Color.white,Color.white,Color.white,Color.white };
            faceMesh.RecalculateBounds();
            face.AddComponent<MeshFilter>().sharedMesh = faceMesh;
            face.AddComponent<MeshRenderer>().sharedMaterial = fillMaterial;
            outline = MakeLine("Square border", new[] {
                new Vector3(-radius,-radius,0),new Vector3(-radius,radius,0),
                new Vector3(radius,radius,0),new Vector3(radius,-radius,0)
            },radius * 0.035f,true);
            var circle = new Vector3[96];
            for (int i = 0; i < circle.Length; i++) circle[i] = AssemblySnapSolver.Direction(i * 360f / circle.Length) * radius;
            MakeLine("Inscribed circle",circle,radius * 0.018f,true);
            arrows = new LineRenderer[PortCount * 2];
            for (int i = 0; i < PortCount; i++)
            {
                Vector2 direction = AssemblySnapSolver.Direction(PortAngle(i));
                Vector2 tip = direction * radius;
                Vector2 side = new Vector2(-direction.y,direction.x) * radius * 0.11f;
                Vector2 neck = tip - direction * radius * 0.22f;
                arrows[i * 2] = MakeLine($"Port {i} shaft",new Vector3[] { Vector2.zero,tip },radius * 0.035f,false);
                arrows[i * 2 + 1] = MakeLine($"Port {i} arrowhead",new Vector3[] { neck + side,tip,neck - side },radius * 0.05f,false);
            }
            MakeLine("Center",new[] { new Vector3(-radius * 0.045f,0,-radius * 0.02f),new Vector3(radius * 0.045f,0,-radius * 0.02f) },radius * 0.09f,false);
            visualSignature = VisualSignature();
            ApplyHighlight();
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private GameObject MakeObject(string label, Transform parent)
        {
            var go = new GameObject(label) { hideFlags = GeometryFlags, layer = gameObject.layer };
            go.transform.SetParent(parent,false);
            return go;
        }

        private void ClearGeometry()
        {
            if (geometryRoot != null)
            {
                geometryRoot.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(geometryRoot.gameObject); else DestroyImmediate(geometryRoot.gameObject);
            }
            geometryRoot = null;
            if (faceMesh != null)
            {
                if (Application.isPlaying) Destroy(faceMesh); else DestroyImmediate(faceMesh);
                faceMesh = null;
            }
            outline = null;
            arrows = null;
        }

        private LineRenderer MakeLine(string label,Vector3[] points,float width,bool loop)
        {
            var child = MakeObject(label,geometryRoot);
            child.transform.localPosition = new Vector3(0,0,-radius * 0.025f);
            var line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = inkMaterial;
            line.useWorldSpace = false;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.loop = loop;
            line.widthMultiplier = width;
            line.numCapVertices = line.numCornerVertices = 3;
            line.startColor = line.endColor = new Color(0.18f,0.25f,0.32f);
            return line;
        }

        public void SetHighlight(bool isSelected,ISet<int> connectedPorts)
        {
            selected = isSelected;
            highlightedPorts.Clear();
            if (connectedPorts != null) foreach (int port in connectedPorts) highlightedPorts.Add(port);
            ApplyHighlight();
        }

        private void ApplyHighlight()
        {
            if (outline != null)
            {
                outline.startColor = outline.endColor = selected ? selectionColor : new Color(0.55f,0.61f,0.66f);
                outline.widthMultiplier = radius * (selected ? 0.055f : 0.025f);
            }
            if (arrows == null) return;
            for (int i = 0; i < arrows.Length; i++)
                if (arrows[i] != null) arrows[i].startColor = arrows[i].endColor = highlightedPorts.Contains(i / 2)
                    ? new Color(0.02f,0.60f,0.40f) : new Color(0.18f,0.25f,0.32f);
        }
    }
}
