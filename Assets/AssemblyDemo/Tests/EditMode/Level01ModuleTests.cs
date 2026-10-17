using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace LudumDare60.AssemblyDemo.Tests
{
    public class Level01ModuleTests
    {
        private GameObject root;
        private Texture2D texture;
        private Sprite sprite;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Level 01 module tests");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            if (sprite != null) Object.DestroyImmediate(sprite);
            if (texture != null) Object.DestroyImmediate(texture);
        }

        [Test]
        public void UnequalPortDistancesSnapAtTheCorrectArtworkEdges()
        {
            var moving = Module("Cabinet", new Vector2(-2.56f, 0.025f), 0f, 90f);
            moving.portDistances = new[] { 0.4f, 0.7f };
            moving.RotationDegrees = 1f;
            var target = Module("Core", Vector2.zero, 270f, 180f);
            target.portDistances = new[] { 1.8f, 1.1f };

            Assert.That(moving.TrySnapTo(target), Is.True);

            AssertVector(new Vector2(-2.5f, 0f), moving.Position);
            AssertVector(moving.PortPosition(1), target.PortPosition(0));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(moving.RotationDegrees, 0f)), Is.LessThan(0.0001f));
            AssertVector(Vector2.zero, target.Position);
            Assert.That(moving.radius, Is.EqualTo(0.9f), "Artwork port distances must not replace the whitebox radius.");
        }

        [Test]
        public void BestPortPairWinsWhenMultipleDistancesAreWithinCaptureRange()
        {
            var moving = Module("Moving", new Vector2(-2.54f, 0f), 90f, 90f);
            moving.portDistances = new[] { 0.7f, 0.75f };
            var target = Module("Target", Vector2.zero, 270f);
            target.portDistances = new[] { 1.8f };

            Assert.That(moving.TrySnapTo(target), Is.True);

            AssertVector(new Vector2(-2.55f, 0f), moving.Position);
            AssertVector(moving.PortPosition(1), target.PortPosition(0));
        }

        [TestCase("tank", "tank", true)]
        [TestCase("tank", "cabinet", false)]
        [TestCase("", "tank", true)]
        [TestCase("tank", "", true)]
        [TestCase(null, "tank", true)]
        public void PortKeysRestrictSnappingAndEmptyKeysKeepLegacyCompatibility(string movingKey, string targetKey, bool expected)
        {
            Vector2 initial = new Vector2(-1.86f, 0f);
            var moving = Module("Moving", initial, 90f);
            var target = Module("Target", Vector2.zero, 270f);
            moving.portKeys = new[] { movingKey };
            target.portKeys = new[] { targetKey };

            Assert.That(moving.PortsCompatible(0, target, 0), Is.EqualTo(expected));
            Assert.That(moving.TrySnapTo(target), Is.EqualTo(expected));
            AssertVector(expected ? new Vector2(-1.8f, 0f) : initial, moving.Position);
            AssertVector(Vector2.zero, target.Position);
        }

        [Test]
        public void MissingAndInvalidPortDistancesFallBackToRadius()
        {
            var module = Module("Fallback", Vector2.zero, 0f, 90f, 180f, 270f, 0f, 90f);
            module.radius = 1.25f;
            module.portDistances = new[] { float.NaN, -1f, 0f, float.PositiveInfinity, 0.4f };

            foreach (int index in new[] { 0, 1, 2, 3, 5 })
                Assert.That(module.PortDistance(index), Is.EqualTo(1.25f));
            Assert.That(module.PortDistance(4), Is.EqualTo(0.4f));
            AssertVector(Vector2.up * 0.4f, module.PortPosition(4));
            Assert.That(module.PortKey(5), Is.Empty);
            module.portDistances = null;
            Assert.That(module.PortDistance(0), Is.EqualTo(1.25f));
        }

        [Test]
        public void ArtworkUsesItsAspectRatioAndOffsetForRenderingAndMousePicking()
        {
            var module = Module("Tall cabinet", Vector2.zero, 90f);
            module.artwork = CreateSprite();
            module.artworkHeight = 4f;
            module.artworkOffset = new Vector2(0.8f, -0.2f);

            module.Rebuild();

            var renderer = module.GetComponentInChildren<SpriteRenderer>(true);
            Assert.That(renderer, Is.Not.Null);
            AssertVector(new Vector2(1f, 4f), renderer.bounds.size);
            AssertVector(module.artworkOffset, renderer.bounds.center);
            Assert.That(module.ContainsLocalPoint(new Vector2(0.8f, 1.7f)), Is.True,
                "The whole tall artwork must be clickable, including outside the old radius square.");
            Assert.That(module.ContainsLocalPoint(new Vector2(1.31f, -0.2f)), Is.False);
            Assert.That(module.ContainsLocalPoint(new Vector2(0.8f, 1.81f)), Is.False);
            Assert.That(module.transform.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void SelectionLeavesArtworkUntintedAndRemovingArtworkRestoresWhitebox()
        {
            var module = Module("Artwork fallback", Vector2.zero, 90f);
            module.artwork = CreateSprite();
            module.Rebuild();
            module.SetHighlight(true, null);
            var renderer = module.GetComponentInChildren<SpriteRenderer>(true);

            Assert.That(renderer.color, Is.EqualTo(Color.white));
            Assert.That(module.GetComponentsInChildren<MeshFilter>(true), Is.Empty);
            Assert.That(module.GetComponentsInChildren<LineRenderer>(true)
                .Any(line => line.name == "Inscribed circle" || line.name == "Center"), Is.False);

            module.artwork = null;
            module.Rebuild();

            Assert.That(module.GetComponentsInChildren<SpriteRenderer>(true), Is.Empty);
            Assert.That(module.GetComponentsInChildren<MeshFilter>(true).Length, Is.EqualTo(1));
            Assert.That(module.GetComponentsInChildren<LineRenderer>(true)
                .Any(line => line.name == "Inscribed circle"), Is.True);
            Assert.That(module.ContainsLocalPoint(new Vector2(0.8f, 0.8f)), Is.True);
            Assert.That(module.ContainsLocalPoint(new Vector2(0f, 0.91f)), Is.False);
        }

        [Test]
        public void FourPartAssemblyConnectsMovesAsOneAndResetsWithIndependentPortDistances()
        {
            var cabinet = Module("Cabinet", new Vector2(-2.25f, 0f), 90f);
            cabinet.portDistances = new[] { 0.45f };
            cabinet.portKeys = new[] { "CONTROL" };
            var core = Module("Core", Vector2.zero, 270f, 90f, 180f);
            core.portDistances = new[] { 1.8f, 1.8f, 1.6f };
            core.portKeys = new[] { "CONTROL", "PRESSURE", "MOUNT" };
            var tank = Module("Tank", new Vector2(2.2f, 0f), 270f);
            tank.portDistances = new[] { 0.4f };
            tank.portKeys = new[] { "PRESSURE" };
            var frame = Module("Base frame", new Vector2(0f, -1.9f), 0f);
            frame.portDistances = new[] { 0.3f };
            frame.portKeys = new[] { "MOUNT" };
            var controller = root.AddComponent<AssemblyDemoController>();
            controller.Modules = new[] { cabinet, core, tank, frame };
            Vector2[] initialPositions = controller.Modules.Select(module => module.Position).ToArray();
            typeof(AssemblyDemoController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);

            Assert.That(controller.TrySnapSelected(), Is.True);
            controller.SelectModule(2);
            Assert.That(controller.TrySnapSelected(), Is.True);
            controller.SelectModule(3);
            Assert.That(controller.TrySnapSelected(), Is.True);
            Assert.That(controller.ConnectionCount, Is.EqualTo(3));

            controller.SelectModule(1);
            controller.ApplyMotion(new Vector2(1.3f, -0.8f), 37f);

            AssertVector(new Vector2(1.3f, -0.8f), core.Position);
            AssertVector(cabinet.PortPosition(0), core.PortPosition(0));
            AssertVector(tank.PortPosition(0), core.PortPosition(1));
            AssertVector(frame.PortPosition(0), core.PortPosition(2));
            foreach (var module in controller.Modules)
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(module.RotationDegrees, 37f)), Is.LessThan(0.0001f));

            controller.ResetDemo();

            Assert.That(controller.ConnectionCount, Is.Zero);
            Assert.That(controller.Selected, Is.SameAs(cabinet));
            for (int i = 0; i < controller.Modules.Length; i++)
            {
                AssertVector(initialPositions[i], controller.Modules[i].Position);
                Assert.That(controller.IsConnected(controller.Modules[i]), Is.False);
            }
        }

        private AssemblyModule Module(string label, Vector2 position, params float[] angles)
        {
            var child = new GameObject(label);
            child.transform.SetParent(root.transform, false);
            var module = child.AddComponent<AssemblyModule>();
            module.Position = position;
            module.portAngles = angles;
            return module;
        }

        private Sprite CreateSprite()
        {
            texture = new Texture2D(16, 64);
            // A noncentral pivot checks that artworkOffset denotes the visible bounds center.
            sprite = Sprite.Create(texture, new Rect(0f, 0f, 16f, 64f), Vector2.zero, 16f);
            return sprite;
        }

        private static void AssertVector(Vector2 expected, Vector2 actual)
        {
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(0.0001f),
                "Expected " + expected + "; received " + actual);
        }
    }
}
