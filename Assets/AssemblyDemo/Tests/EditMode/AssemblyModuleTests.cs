using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LudumDare60.AssemblyDemo.Tests
{
    public class AssemblyModuleTests
    {
        private const string PrefabPath = "Assets/WhiteboxModule/Prefabs/WhiteboxModule.prefab";
        private GameObject root;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Standalone whitebox module test");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public void SetAnglesNormalizesItsOwnCopyWithoutChangingTheCallersArray()
        {
            var module = Module("Module");
            float[] input = { -30f, 360f, 450f };

            module.SetAngles(input);

            CollectionAssert.AreEqual(new[] { -30f, 360f, 450f }, input);
            CollectionAssert.AreEqual(new[] { 330f, 0f, 90f }, module.portAngles);
            Assert.That(module.portAngles, Is.Not.SameAs(input));
            input[0] = 20f;
            Assert.That(module.PortAngle(0), Is.EqualTo(330f));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SetAnglesWithNullOrEmptyInputRestoresOneUpwardPort(bool useNull)
        {
            var module = Module("Module");
            module.SetAngles(90f, 180f);

            if (useNull) module.SetAngles((float[])null);
            else module.SetAngles();

            Assert.That(module.PortCount, Is.EqualTo(1));
            Assert.That(module.PortAngle(0), Is.EqualTo(0f));
            AssertVector(Vector2.up, module.PortDirection(0));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void SetAnglesRejectsNonFiniteValuesWithoutPartiallyReplacingPorts(float invalid)
        {
            var module = Module("Module");
            module.SetAngles(15f, 270f);

            Assert.Throws<ArgumentException>(() => module.SetAngles(45f, invalid));

            CollectionAssert.AreEqual(new[] { 15f, 270f }, module.portAngles);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void SetRadiusRejectsNonPositiveOrNonFiniteValuesWithoutChangingRadius(float invalid)
        {
            var module = Module("Module");
            module.SetRadius(0.8f);

            Assert.Throws<ArgumentOutOfRangeException>(() => module.SetRadius(invalid));

            Assert.That(module.radius, Is.EqualTo(0.8f));
        }

        [TestCase(0.001f)]
        [TestCase(1.25f)]
        public void SetRadiusAcceptsFinitePositiveValues(float radius)
        {
            var module = Module("Module");

            module.SetRadius(radius);

            Assert.That(module.radius, Is.EqualTo(radius));
        }

        [Test]
        public void TrySnapToCorrectsNearOpposingPortsWithoutMovingOrBindingTarget()
        {
            var moving = Module("Moving");
            moving.Position = new Vector2(-2.08f, 0.03f);
            moving.RotationDegrees = 3f;
            moving.SetRadius(1f);
            moving.SetAngles(90f);
            var target = Module("Target");
            target.SetRadius(1f);
            target.SetAngles(270f);

            Assert.That(moving.TrySnapTo(target), Is.True);

            AssertVector(new Vector2(-2f, 0f), moving.Position);
            AssertVector(target.PortPosition(0), moving.PortPosition(0));
            AssertVector(-target.PortDirection(0), moving.PortDirection(0));
            AssertVector(Vector2.zero, target.Position);
            AssertAngle(0f, target.RotationDegrees);

            moving.Position += new Vector2(3f, 2f);
            moving.RotationDegrees += 20f;
            AssertVector(Vector2.zero, target.Position);
            AssertAngle(0f, target.RotationDegrees);
        }

        [Test]
        public void TrySnapToRejectsDistantTargetAndSelfWithoutChangingPose()
        {
            var moving = Module("Moving");
            moving.Position = new Vector2(-10f, 0f);
            moving.SetAngles(90f);
            var target = Module("Target");
            target.SetAngles(270f);

            Assert.That(moving.TrySnapTo(target), Is.False);
            Assert.That(moving.TrySnapTo(moving), Is.False);

            AssertVector(new Vector2(-10f, 0f), moving.Position);
            AssertAngle(0f, moving.RotationDegrees);
            AssertVector(Vector2.zero, target.Position);
        }

        [Test]
        public void RebuildUpdatesPrefabInstanceArrowsWithoutBreakingItsPrefabConnection()
        {
            var prefab = LoadPrefab();
            var module = InstantiatePrefab(prefab);
            // Direct field edits exercise the same data path as Inspector edits.
            module.portAngles = new[] { 0f, 90f, 180f };
            module.radius = 1.25f;

            module.Rebuild();
            module.Rebuild();

            AssertPrefabConnection(module, prefab);
            AssertArrows(module, Vector2.up * 1.25f, Vector2.right * 1.25f, Vector2.down * 1.25f);

            // Removing ports must remove stale arrow geometry as well.
            module.portAngles = new[] { 270f };
            module.Rebuild();

            AssertPrefabConnection(module, prefab);
            AssertArrows(module, Vector2.left * 1.25f);
        }

        [Test]
        public void EditingOnePrefabInstanceDoesNotChangeTheOtherInstanceOrSourceAsset()
        {
            var prefab = LoadPrefab();
            var source = prefab.GetComponent<AssemblyModule>();
            var first = InstantiatePrefab(prefab);
            var second = InstantiatePrefab(prefab);
            float[] sourceAngles = Enumerable.Range(0, source.PortCount).Select(source.PortAngle).ToArray();
            float sourceRadius = source.radius;
            Vector2[] secondTips = Enumerable.Range(0, second.PortCount)
                .Select(i => second.PortDirection(i) * second.radius).ToArray();
            second.Rebuild();
            var secondShaft = PortLines(second, " shaft")[0];

            first.SetAngles(90f, 180f);
            first.SetRadius(1.7f);
            first.Rebuild();

            AssertArrows(first, Vector2.right * 1.7f, Vector2.down * 1.7f);
            Assert.That(second.radius, Is.EqualTo(sourceRadius));
            CollectionAssert.AreEqual(sourceAngles,
                Enumerable.Range(0, second.PortCount).Select(second.PortAngle).ToArray());
            AssertArrows(second, secondTips);
            Assert.That(PortLines(second, " shaft")[0], Is.SameAs(secondShaft));
            Assert.That(source.radius, Is.EqualTo(sourceRadius));
            CollectionAssert.AreEqual(sourceAngles,
                Enumerable.Range(0, source.PortCount).Select(source.PortAngle).ToArray());
            AssertPrefabConnection(first, prefab);
            AssertPrefabConnection(second, prefab);
        }

        private AssemblyModule Module(string label)
        {
            var child = new GameObject(label);
            child.transform.SetParent(root.transform, false);
            return child.AddComponent<AssemblyModule>();
        }

        private static GameObject LoadPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, "The standalone module prefab must exist before these tests run.");
            Assert.That(prefab.GetComponent<AssemblyModule>(), Is.Not.Null);
            return prefab;
        }

        private AssemblyModule InstantiatePrefab(GameObject prefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            return instance.GetComponent<AssemblyModule>();
        }

        private static void AssertPrefabConnection(AssemblyModule module, GameObject prefab)
        {
            Assert.That(PrefabUtility.GetPrefabInstanceStatus(module.gameObject),
                Is.EqualTo(PrefabInstanceStatus.Connected));
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(module.gameObject), Is.SameAs(prefab));
        }

        private static LineRenderer[] PortLines(AssemblyModule module, string suffix)
        {
            return module.GetComponentsInChildren<LineRenderer>(true)
                .Where(line => line.name.StartsWith("Port ", StringComparison.Ordinal) &&
                    line.name.EndsWith(suffix, StringComparison.Ordinal))
                .OrderBy(line => line.name, StringComparer.Ordinal).ToArray();
        }

        private static void AssertArrows(AssemblyModule module, params Vector2[] tips)
        {
            var shafts = PortLines(module, " shaft");
            var heads = PortLines(module, " arrowhead");
            Assert.That(shafts.Length, Is.EqualTo(tips.Length));
            Assert.That(heads.Length, Is.EqualTo(tips.Length));
            for (int i = 0; i < tips.Length; i++)
            {
                Assert.That(shafts[i].useWorldSpace, Is.False);
                Assert.That(shafts[i].positionCount, Is.EqualTo(2));
                AssertVector(Vector2.zero, shafts[i].GetPosition(0));
                AssertVector(tips[i], shafts[i].GetPosition(1));
                Assert.That(heads[i].positionCount, Is.EqualTo(3));
                AssertVector(tips[i], heads[i].GetPosition(1));
            }
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
