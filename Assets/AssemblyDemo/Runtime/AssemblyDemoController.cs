using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LudumDare60.AssemblyDemo
{
    public sealed class AssemblyDemoController : MonoBehaviour
    {
        public AssemblyModule[] Modules;
        [Tooltip("Optional camera for mouse picking. Falls back to Camera.main; keyboard assembly works without one.")]
        public Camera ViewCamera;
        public const float AngleTolerance = AssemblyModule.AngleTolerance;
        public const float LateralTolerance = AssemblyModule.LateralTolerance;
        public const float ContactTolerance = AssemblyModule.ContactTolerance;
        [Min(0)] public float MoveSpeed = 2.4f;
        [Min(0)] public float RotationSpeed = 80f;
        public AssemblyModule Selected => Modules != null && Modules.Length > 0 ? Modules[selectedIndex] : null;
        public int ConnectionCount => connections.Count;
        /// <summary>External reading/menu/end-state lock. Also guards programmatic input.</summary>
        public bool InputBlocked { get; set; }
        public int RequiredConnectionCount => Modules == null ? 0 : Mathf.Max(0, Modules.Length - 1);
        /// <summary>All required modules belong to one real, compatible-port assembly.</summary>
        public bool IsAssemblyComplete
        {
            get
            {
                if (Modules == null || Modules.Length < 2 || connections.Count < RequiredConnectionCount) return false;
                foreach (var module in Modules) if (module == null) return false;
                CollectGroup(Modules[0]);
                foreach (var module in Modules) if (!group.Contains(module)) return false;
                return true;
            }
        }

        private sealed class Connection
        {
            public AssemblyModule A, B;
            public int PortA, PortB;
        }
        private readonly List<Connection> connections = new List<Connection>();
        private readonly HashSet<AssemblyModule> group = new HashSet<AssemblyModule>();
        private readonly HashSet<int> occupied = new HashSet<int>();
        private readonly float[] movingPort = new float[1], fixedPort = new float[1];
        private Vector2[] startPositions;
        private float[] startRotations;
        private int selectedIndex;
        private float snapBlockedUntil;

        private void Awake()
        {
            if (Modules == null || Modules.Length == 0) Modules = GetComponentsInChildren<AssemblyModule>();
            startPositions = new Vector2[Modules.Length];
            startRotations = new float[Modules.Length];
            for (int i = 0; i < Modules.Length; i++)
            {
                startPositions[i] = Modules[i].Position;
                startRotations[i] = Modules[i].RotationDegrees;
            }
            connections.Clear();
            selectedIndex = 0;
            UpdateHighlights();
        }

        private void Update()
        {
            if (InputBlocked || Selected == null || !Application.isFocused) return;
            var keys = Keyboard.current;
            if (keys != null)
            {
                if (keys.rKey.wasPressedThisFrame) { ResetDemo(); return; }
                if (keys.tabKey.wasPressedThisFrame) SelectModule((selectedIndex + 1) % Modules.Length);
                if (keys.digit1Key.wasPressedThisFrame) SelectModule(0);
                if (keys.digit2Key.wasPressedThisFrame) SelectModule(1);
                if (keys.digit3Key.wasPressedThisFrame) SelectModule(2);
                if (keys.digit4Key.wasPressedThisFrame) SelectModule(3);
                if (keys.xKey.wasPressedThisFrame) DetachSelected();
                Vector2 move = new Vector2((keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0),
                    (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0));
                float turn = (keys.eKey.isPressed ? 1 : 0) - (keys.qKey.isPressed ? 1 : 0);
                float fine = keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed ? 0.25f : 1f;
                ApplyMotion(move.normalized * MoveSpeed * fine * Time.deltaTime, turn * RotationSpeed * fine * Time.deltaTime);
            }
            var mouse = Mouse.current;
            var pickingCamera = ViewCamera != null ? ViewCamera : Camera.main;
            if (mouse != null && pickingCamera != null && mouse.leftButton.wasPressedThisFrame && pickingCamera.pixelRect.Contains(mouse.position.ReadValue()))
            {
                Ray ray = pickingCamera.ScreenPointToRay(mouse.position.ReadValue());
                for (int i = Modules.Length - 1; i >= 0; i--)
                {
                    var plane = new Plane(Vector3.forward, Modules[i].transform.position);
                    if (!plane.Raycast(ray, out float distance)) continue;
                    Vector3 world = ray.GetPoint(distance);
                    Vector3 local = Modules[i].transform.InverseTransformPoint(world);
                    if (Modules[i].ContainsLocalPoint(local))
                    { SelectModule(i); break; }
                }
            }
            if (Time.unscaledTime >= snapBlockedUntil) TrySnapSelected();
        }

        public void SelectModule(int index)
        {
            if (InputBlocked || Modules == null || index < 0 || index >= Modules.Length) return;
            selectedIndex = index;
            UpdateHighlights();
        }

        public bool IsConnected(AssemblyModule module)
        {
            foreach (var connection in connections)
                if (connection.A == module || connection.B == module) return true;
            return false;
        }

        public void ResetDemo()
        {
            if (InputBlocked || startPositions == null) return;
            connections.Clear();
            for (int i = 0; i < Modules.Length; i++)
            {
                Modules[i].Position = startPositions[i];
                Modules[i].RotationDegrees = startRotations[i];
            }
            selectedIndex = 0;
            snapBlockedUntil = 0;
            UpdateHighlights();
        }

        public void DetachSelected()
        {
            if (InputBlocked || Selected == null) return;
            connections.RemoveAll(c => c.A == Selected || c.B == Selected);
            // Give the player time to move clear before magnetic capture can happen again.
            snapBlockedUntil = Time.unscaledTime + 1.5f;
            UpdateHighlights();
        }

        private void CollectGroup(AssemblyModule start)
        {
            group.Clear();
            group.Add(start);
            bool changed;
            do
            {
                changed = false;
                foreach (var c in connections)
                {
                    if (group.Contains(c.A)) changed |= group.Add(c.B);
                    if (group.Contains(c.B)) changed |= group.Add(c.A);
                }
            } while (changed);
        }

        /// <summary>World-space translation and clockwise rotation around the selected module.</summary>
        public void ApplyMotion(Vector2 translation, float clockwiseDelta)
        {
            if (InputBlocked || Selected == null || (translation.sqrMagnitude == 0 && Mathf.Abs(clockwiseDelta) < 0.00001f)) return;
            CollectGroup(Selected);
            Vector2 pivot = Selected.Position;
            var rotation = Quaternion.Euler(0, 0, -clockwiseDelta);
            foreach (var module in group)
            {
                module.Position = pivot + (Vector2)(rotation * (module.Position - pivot)) + translation;
                module.RotationDegrees += clockwiseDelta;
            }
        }

        private bool PortOccupied(AssemblyModule module, int port)
        {
            foreach (var c in connections)
                if ((c.A == module && c.PortA == port) || (c.B == module && c.PortB == port)) return true;
            return false;
        }

        public bool TrySnapSelected()
        {
            if (InputBlocked || Selected == null) return false;
            CollectGroup(Selected);
            bool found = false;
            SnapSolution best = default;
            AssemblyModule moving = null, target = null;
            int fromPort = -1, toPort = -1;
            foreach (var candidate in Modules)
            {
                if (!group.Contains(candidate)) continue;
                foreach (var other in Modules)
                {
                    if (group.Contains(other)) continue;
                    for (int a = 0; a < candidate.PortCount; a++)
                    {
                        if (PortOccupied(candidate, a)) continue;
                        movingPort[0] = candidate.PortAngle(a);
                        for (int b = 0; b < other.PortCount; b++)
                        {
                            if (PortOccupied(other, b)) continue;
                            if (!candidate.PortsCompatible(a, other, b)) continue;
                            if (Mathf.Abs(candidate.transform.position.z - other.transform.position.z) > 0.001f) continue;
                            fixedPort[0] = other.PortAngle(b);
                            if (!AssemblySnapSolver.TrySolve(candidate.Position, candidate.RotationDegrees, candidate.PortDistance(a), movingPort,
                                other.Position, other.RotationDegrees, other.PortDistance(b), fixedPort,
                                AngleTolerance, LateralTolerance, ContactTolerance, out var solution)) continue;
                            if (found && solution.Score >= best.Score) continue;
                            found = true;
                            best = solution;
                            moving = candidate;
                            target = other;
                            fromPort = a;
                            toPort = b;
                        }
                    }
                }
            }
            if (!found) return false;
            Vector2 pivot = moving.Position;
            float delta = Mathf.DeltaAngle(moving.RotationDegrees, best.RotationDegrees);
            var rotation = Quaternion.Euler(0, 0, -delta);
            foreach (var module in group)
            {
                module.Position = best.Position + (Vector2)(rotation * (module.Position - pivot));
                module.RotationDegrees += delta;
            }
            connections.Add(new Connection { A = moving, PortA = fromPort, B = target, PortB = toPort });
            UpdateHighlights();
            return true;
        }

        private void UpdateHighlights()
        {
            if (Modules == null) return;
            foreach (var module in Modules)
            {
                occupied.Clear();
                for (int i = 0; i < module.PortCount; i++) if (PortOccupied(module, i)) occupied.Add(i);
                module.SetHighlight(module == Selected, occupied);
            }
        }
    }
}
