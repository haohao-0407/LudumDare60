using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace LudumDare60.AssemblyDemo.Tests
{
    public class AssemblyControllerTests
    {
        private GameObject root;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Assembly controller test");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
        }

        [Test]
        public void CompleteAssemblyRequiresEveryModuleAndRevokesAfterDetach()
        {
            var left = Module("Left", new Vector2(-4f, 0f), 0f, 90f);
            var middle = Module("Middle", new Vector2(-2f, 0f), 0f, 270f, 90f);
            var right = Module("Right", Vector2.zero, 0f, 270f);
            var controller = Controller(left, middle, right);
            Assert.That(controller.IsAssemblyComplete, Is.False);
            Assert.That(controller.TrySnapSelected(), Is.True);
            Assert.That(controller.IsAssemblyComplete, Is.False, "One disconnected required module blocks launch.");
            Assert.That(controller.TrySnapSelected(), Is.True);
            Assert.That(controller.IsAssemblyComplete, Is.True);
            controller.DetachSelected();
            Assert.That(controller.IsAssemblyComplete, Is.False, "Detaching a repaired module must revoke clearance.");
        }

        [Test]
        public void ReadingLockBlocksMotionSnapDetachResetAndSelection()
        {
            var moving = Module("Moving", new Vector2(-2f, 0f), 0f, 90f);
            var target = Module("Target", Vector2.zero, 0f, 270f);
            var controller = Controller(moving, target);
            controller.InputBlocked = true;
            Assert.That(controller.TrySnapSelected(), Is.False);
            controller.ApplyMotion(Vector2.one, 45);
            controller.SelectModule(1);
            AssertVector(new Vector2(-2f, 0f), moving.Position);
            Assert.That(controller.Selected, Is.SameAs(moving));
            controller.InputBlocked = false;
            Assert.That(controller.TrySnapSelected(), Is.True);
            controller.InputBlocked = true;
            controller.DetachSelected(); controller.ResetDemo();
            Assert.That(controller.ConnectionCount, Is.EqualTo(1));
            Assert.That(controller.IsAssemblyComplete, Is.True);
            controller.InputBlocked = false;
            controller.DetachSelected();
            Assert.That(controller.IsAssemblyComplete, Is.False);
        }

        [Test]
        public void SnapCorrectsActualModuleTransformsAndKeepsTargetFixed()
        {
            var moving = Module("Moving", new Vector2(-2.08f, 0.03f), 3f, 90f);
            var target = Module("Target", Vector2.zero, 0f, 270f);
            var controller = Controller(moving, target);

            Assert.That(controller.TrySnapSelected(), Is.True);
            Assert.That(controller.ConnectionCount, Is.EqualTo(1));
            AssertContact(moving, 0, target, 0);
            AssertVector(new Vector2(-2f, 0f), moving.Position);
            AssertVector(Vector2.zero, target.Position);
            AssertAngle(0f, target.RotationDegrees);
        }

        [Test]
        public void MotionMovesWholeConnectedGroupAndRotatesClockwiseAroundSelected()
        {
            var left = Module("Left", new Vector2(-2f, 0f), 0f, 90f);
            var center = Module("Center", Vector2.zero, 0f, 270f);
            var unrelated = Module("Unrelated", new Vector2(4f, 3f), 20f, 0f);
            var controller = Controller(left, center, unrelated);
            Assert.That(controller.TrySnapSelected(), Is.True);

            controller.SelectModule(1);
            controller.ApplyMotion(new Vector2(1f, 2f), 90f);

            AssertVector(new Vector2(1f, 2f), center.Position);
            AssertVector(new Vector2(1f, 4f), left.Position);
            AssertAngle(90f, left.RotationDegrees);
            AssertAngle(90f, center.RotationDegrees);
            AssertVector(new Vector2(4f, 3f), unrelated.Position);
            AssertAngle(20f, unrelated.RotationDegrees);
            AssertContact(left, 0, center, 0);
        }

        [Test]
        public void ConnectingExistingAssemblyRotatesEveryMemberAndPreservesEarlierJoint()
        {
            var left = Module("Left", new Vector2(-4f, 0f), 0f, 90f);
            var middle = Module("Middle", new Vector2(-2f, 0f), 0f, 270f, 90f);
            var target = Module("Target", Vector2.zero, 4f, 270f);
            var controller = Controller(left, middle, target);
            Assert.That(controller.TrySnapSelected(), Is.True);

            // The selected module has no free ports; the group's other member supplies one.
            Assert.That(controller.TrySnapSelected(), Is.True);

            Assert.That(controller.ConnectionCount, Is.EqualTo(2));
            AssertContact(left, 0, middle, 0);
            AssertContact(middle, 1, target, 0);
            AssertAngle(4f, left.RotationDegrees);
            AssertAngle(4f, middle.RotationDegrees);
            AssertVector(Vector2.zero, target.Position);
            AssertAngle(4f, target.RotationDegrees);
        }

        [Test]
        public void OccupiedPortCannotAcceptASecondModule()
        {
            var first = Module("First", new Vector2(0f, -2f), 0f, 0f);
            var target = Module("Target", Vector2.zero, 0f, 180f);
            var second = Module("Second", new Vector2(0f, -2.1f), 0f, 0f);
            var controller = Controller(first, target, second);
            Assert.That(controller.TrySnapSelected(), Is.True);

            controller.SelectModule(2);
            Assert.That(controller.TrySnapSelected(), Is.False);

            Assert.That(controller.ConnectionCount, Is.EqualTo(1));
            Assert.That(controller.IsConnected(second), Is.False);
            AssertVector(new Vector2(0f, -2.1f), second.Position);
            AssertContact(first, 0, target, 0);
        }

        [Test]
        public void FreeDuplicatePortsCannotCreateAnotherConnectionWithinSameGroup()
        {
            var first = Module("First", new Vector2(0f, -2f), 0f, 0f, 0f);
            var second = Module("Second", Vector2.zero, 0f, 180f, 180f);
            var controller = Controller(first, second);
            Assert.That(controller.TrySnapSelected(), Is.True);

            Assert.That(controller.TrySnapSelected(), Is.False);
            controller.SelectModule(1);
            Assert.That(controller.TrySnapSelected(), Is.False);
            Assert.That(controller.ConnectionCount, Is.EqualTo(1));
        }

        [Test]
        public void DetachingMiddleModulePreservesConnectionsNotIncidentToIt()
        {
            var a = Module("A", new Vector2(-3f, 0f), 0f, 90f);
            var b = Module("B", new Vector2(-1f, 0f), 0f, 270f, 90f);
            var c = Module("C", new Vector2(1f, 0f), 0f, 270f, 90f);
            var d = Module("D", new Vector2(3f, 0f), 0f, 270f);
            var controller = Controller(a, b, c, d);
            Assert.That(controller.TrySnapSelected(), Is.True);
            Assert.That(controller.TrySnapSelected(), Is.True);
            Assert.That(controller.TrySnapSelected(), Is.True);
            Assert.That(controller.ConnectionCount, Is.EqualTo(3));

            controller.SelectModule(1);
            controller.DetachSelected();

            Assert.That(controller.ConnectionCount, Is.EqualTo(1));
            Assert.That(controller.IsConnected(a), Is.False);
            Assert.That(controller.IsConnected(b), Is.False);
            Assert.That(controller.IsConnected(c), Is.True);
            Assert.That(controller.IsConnected(d), Is.True);
            controller.ApplyMotion(Vector2.up, 20f);
            AssertVector(new Vector2(-3f, 0f), a.Position);
            AssertVector(new Vector2(-1f, 1f), b.Position);
            AssertVector(new Vector2(1f, 0f), c.Position);
            AssertVector(new Vector2(3f, 0f), d.Position);

            controller.SelectModule(2);
            controller.ApplyMotion(Vector2.up, -40f);
            AssertContact(c, 1, d, 0);
            Assert.That(controller.ConnectionCount, Is.EqualTo(1));
        }

        [Test]
        public void ConnectingTwoGroupsPreservesEveryJointAndKeepsTargetGroupFixed()
        {
            var a = Module("A", new Vector2(-4f, 0f), 0f, 90f);
            var b = Module("B", new Vector2(-2f, 0f), 0f, 270f, 90f);
            var c = Module("C", Vector2.zero, 4f, 270f, 90f);
            Vector2 initialD = AssemblySnapSolver.Direction(94f) * 2f;
            var d = Module("D", initialD, 4f, 270f);
            var controller = Controller(a, b, c, d);
            Assert.That(controller.TrySnapSelected(), Is.True); // A-B.
            controller.SelectModule(3);
            Assert.That(controller.TrySnapSelected(), Is.True); // D-C, already exact.
            Assert.That(controller.ConnectionCount, Is.EqualTo(2));

            controller.SelectModule(0);
            Assert.That(controller.TrySnapSelected(), Is.True); // AB-C.

            Assert.That(controller.ConnectionCount, Is.EqualTo(3));
            AssertContact(a, 0, b, 0);
            AssertContact(b, 1, c, 0);
            AssertContact(c, 1, d, 0);
            AssertVector(Vector2.zero, c.Position);
            AssertVector(initialD, d.Position);
            AssertAngle(4f, c.RotationDegrees);
            AssertAngle(4f, d.RotationDegrees);
        }

        [Test]
        public void ResetRestoresInitialPosesSelectionAndAvailabilityOfPorts()
        {
            Vector2 initialA = new Vector2(-2.08f, 0.03f);
            var a = Module("A", initialA, 3f, 90f);
            var b = Module("B", Vector2.zero, 0f, 270f);
            var controller = Controller(a, b);
            Assert.That(controller.TrySnapSelected(), Is.True);
            controller.SelectModule(1);
            controller.ApplyMotion(new Vector2(2f, 3f), 47f);

            controller.ResetDemo();

            Assert.That(controller.Selected, Is.SameAs(a));
            Assert.That(controller.ConnectionCount, Is.Zero);
            Assert.That(controller.IsConnected(a), Is.False);
            Assert.That(controller.IsConnected(b), Is.False);
            AssertVector(initialA, a.Position);
            AssertAngle(3f, a.RotationDegrees);
            AssertVector(Vector2.zero, b.Position);
            AssertAngle(0f, b.RotationDegrees);
            Assert.That(controller.TrySnapSelected(), Is.True);
            AssertContact(a, 0, b, 0);
        }

        [Test]
        public void MovementAndSnapDoNotRequireAnAssignedCamera()
        {
            var moving = Module("Moving", new Vector2(0f, -2.3f), 0f, 0f);
            var target = Module("Target", Vector2.zero, 0f, 180f);
            var controller = Controller(moving, target);
            controller.ViewCamera = null;

            controller.ApplyMotion(new Vector2(0f, 0.2f), 0f);

            AssertVector(new Vector2(0f, -2.1f), moving.Position);
            Assert.That(controller.TrySnapSelected(), Is.True);
            AssertContact(moving, 0, target, 0);
        }

        [Test]
        public void OffsetCameraDoesNotConstrainWorldSpaceMovement()
        {
            var moving = Module("Moving", new Vector2(40f, -12f), 0f, 0f);
            var camera = CreateCamera();
            camera.transform.position = new Vector3(100f, 50f, -10f);
            camera.orthographicSize = 1f;
            var controller = ControllerWithCamera(camera, moving);

            controller.ApplyMotion(new Vector2(3f, 7f), 0f);

            AssertVector(new Vector2(43f, -5f), moving.Position);
        }

        [Test]
        public void InitializationPreservesTheAssignedCameraViewport()
        {
            var moving = Module("Moving", Vector2.zero, 0f, 0f);
            var camera = CreateCamera();
            var initialRect = new Rect(0.2f, 0.15f, 0.65f, 0.7f);
            camera.rect = initialRect;

            ControllerWithCamera(camera, moving);

            Assert.That(camera.rect, Is.EqualTo(initialRect));
        }

        [TestCase(0.5f, 4.9f, 0f, true, TestName = "FixedAngleToleranceAcceptsSmallMisalignment")]
        [TestCase(0.5f, 5.1f, 0f, false, TestName = "FixedAngleToleranceRejectsExcessMisalignment")]
        [TestCase(1f, 4.5f, 0f, true, TestName = "FixedLateralToleranceAcceptsSmallOffsetFromArrowAxis")]
        [TestCase(1f, 4.7f, 0f, false, TestName = "FixedLateralToleranceRejectsExcessOffsetFromArrowAxis")]
        [TestCase(1f, 0f, 0.119f, true, TestName = "FixedContactToleranceAcceptsNearbyTips")]
        [TestCase(1f, 0f, 0.121f, false, TestName = "FixedContactToleranceRejectsSeparatedTips")]
        public void FixedTolerancesControlCapture(float radius, float rotation, float gap, bool expected)
        {
            var moving = Module("Moving", new Vector2(0f, -2f * radius - gap), rotation, 0f);
            var target = Module("Target", Vector2.zero, 0f, 180f);
            moving.radius = radius;
            target.radius = radius;
            var controller = Controller(moving, target);

            Assert.That(controller.TrySnapSelected(), Is.EqualTo(expected));
            Assert.That(controller.ConnectionCount, Is.EqualTo(expected ? 1 : 0));
            if (expected) AssertContact(moving, 0, target, 0);
        }

        private AssemblyModule Module(string label, Vector2 position, float rotation, params float[] ports)
        {
            var child = new GameObject(label);
            child.transform.SetParent(root.transform, false);
            var module = child.AddComponent<AssemblyModule>();
            module.moduleLabel = label;
            module.radius = 1f;
            module.portAngles = ports;
            module.Position = position;
            module.RotationDegrees = rotation;
            return module;
        }

        private AssemblyDemoController Controller(params AssemblyModule[] modules)
        {
            return ControllerWithCamera(null, modules);
        }

        private AssemblyDemoController ControllerWithCamera(Camera camera, params AssemblyModule[] modules)
        {
            var controller = root.AddComponent<AssemblyDemoController>();
            controller.Modules = modules;
            controller.ViewCamera = camera;
            // Awake is normally invoked by Unity. Calling it after assigning the fixture's
            // modules also gives deterministic initial poses when run outside Play mode.
            typeof(AssemblyDemoController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);
            return controller;
        }

        private Camera CreateCamera()
        {
            var child = new GameObject("Test camera");
            child.transform.SetParent(root.transform, false);
            var camera = child.AddComponent<Camera>();
            camera.orthographic = true;
            return camera;
        }

        private static void AssertContact(AssemblyModule first, int firstPort, AssemblyModule second, int secondPort)
        {
            AssertVector(first.PortPosition(firstPort), second.PortPosition(secondPort));
            AssertVector(first.PortDirection(firstPort), -second.PortDirection(secondPort));
            Assert.That(Vector2.Distance(first.Position, second.Position),
                Is.EqualTo(first.radius + second.radius).Within(0.0001f));
        }

        private static void AssertVector(Vector2 expected, Vector2 actual)
        {
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(0.0001f),
                "Expected " + expected + "; received " + actual);
        }

        private static void AssertAngle(float expected, float actual)
        {
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(expected, actual)), Is.LessThan(0.0001f));
        }
    }
}
