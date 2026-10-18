using NUnit.Framework;
using UnityEngine;

namespace LudumDare60.AssemblyDemo.Tests
{
    public class AssemblySnapSolverTests
    {
        private static readonly float[] ZeroPort = { 0f };

        [TestCase(0f, 0f, 1f)]
        [TestCase(90f, 1f, 0f)]
        [TestCase(180f, 0f, -1f)]
        [TestCase(270f, -1f, 0f)]
        [TestCase(360f, 0f, 1f)]
        [TestCase(-90f, -1f, 0f)]
        public void DirectionUsesClockwiseDegreesFromUp(float angle, float x, float y)
        {
            AssertVector(new Vector2(x, y), AssemblySnapSolver.Direction(angle));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(angle,
                AssemblySnapSolver.ClockwiseAngle(new Vector2(x, y)))), Is.LessThan(0.0001f));
        }

        [Test]
        public void NegativeRotationTurnsCounterclockwiseAndPositiveTurnsClockwise()
        {
            Assert.That(AssemblySnapSolver.Direction(-15f).x, Is.LessThan(0f));
            Assert.That(AssemblySnapSolver.Direction(15f).x, Is.GreaterThan(0f));
            Assert.That(AssemblySnapSolver.NormalizeAngle(-15f), Is.EqualTo(345f));
            Assert.That(AssemblySnapSolver.NormalizeAngle(720f), Is.EqualTo(0f));
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(180f)]
        [TestCase(270f)]
        public void ExactContactWorksOnEverySide(float fixedAngle)
        {
            Vector2 movingCenter = AssemblySnapSolver.Direction(fixedAngle) * 2f;
            bool snapped = AssemblySnapSolver.TrySolve(movingCenter, fixedAngle + 180f, 1f, ZeroPort,
                Vector2.zero, fixedAngle, 1f, ZeroPort, 0f, 0f, 0f, out SnapSolution solution);

            Assert.That(snapped, Is.True);
            AssertVector(movingCenter, solution.Position);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(fixedAngle + 180f, solution.RotationDegrees)),
                Is.LessThan(0.0001f));
        }

        [Test]
        public void EmptyAndNullPortListsSupplyZeroDegreePort()
        {
            bool snapped = AssemblySnapSolver.TrySolve(new Vector2(0f, -2f), 360f, 1f, null,
                Vector2.zero, 180f, 1f, new float[0], 0f, 0f, 0f, out SnapSolution solution);

            Assert.That(snapped, Is.True);
            Assert.That(solution.MovingPortIndex, Is.Zero);
            Assert.That(solution.FixedPortIndex, Is.Zero);
            Assert.That(solution.RotationDegrees, Is.Zero);
        }

        [Test]
        public void NearContactCorrectsBothPoseErrorsAndCoincidesTipsForUnequalRadii()
        {
            Vector2 fixedCenter = new Vector2(2f, 3f);
            const float fixedRotation = 25f;
            float[] fixedPorts = { 40f };
            float[] movingPorts = { 15f };
            const float fixedRadius = 1.2f;
            const float movingRadius = 0.7f;
            Vector2 expectedCenter = fixedCenter + AssemblySnapSolver.Direction(65f) * 1.9f;

            bool snapped = AssemblySnapSolver.TrySolve(expectedCenter + new Vector2(0.03f, -0.02f),
                232f, movingRadius, movingPorts, fixedCenter, fixedRotation, fixedRadius, fixedPorts,
                5f, 0.2f, 0.15f, out SnapSolution solution);

            Assert.That(snapped, Is.True);
            AssertVector(expectedCenter, solution.Position);
            Assert.That(solution.RotationDegrees, Is.EqualTo(230f).Within(0.0001f));
            Vector2 movingDirection = AssemblySnapSolver.Direction(solution.RotationDegrees + movingPorts[0]);
            Vector2 fixedDirection = AssemblySnapSolver.Direction(fixedRotation + fixedPorts[0]);
            AssertVector(-fixedDirection, movingDirection);
            AssertVector(fixedCenter + fixedDirection * fixedRadius,
                solution.Position + movingDirection * movingRadius);
            Assert.That(solution.AngleError, Is.GreaterThan(0f));
            Assert.That(solution.LateralError, Is.GreaterThan(0f));
            Assert.That(solution.ContactError, Is.GreaterThan(0f));
        }

        [Test]
        public void SearchesAllPortsAndSelectsBestEligiblePair()
        {
            bool snapped = AssemblySnapSolver.TrySolve(new Vector2(-2f, 0f), 0f, 1f,
                new[] { 93f, 0f, 90f }, Vector2.zero, 0f, 1f, new[] { 273f, 270f },
                10f, 0.3f, 0.3f, out SnapSolution solution);

            Assert.That(snapped, Is.True);
            Assert.That(solution.MovingPortIndex, Is.EqualTo(2));
            Assert.That(solution.FixedPortIndex, Is.EqualTo(1));
            Assert.That(solution.RotationDegrees, Is.Zero);
        }

        [TestCase(0f, 0f)]
        [TestCase(180f, 0f)]
        public void RejectsSameFacingOrOutwardPointingArrowsEvenWithBroadTolerance(
            float movingRotation, float fixedRotation)
        {
            bool snapped = AssemblySnapSolver.TrySolve(new Vector2(0f, -2f), movingRotation, 1f,
                ZeroPort, Vector2.zero, fixedRotation, 1f, ZeroPort, 180f, 5f, 5f, out _);

            Assert.That(snapped, Is.False);
        }

        [Test]
        public void ParallelOpposingArrowsCannotSnapFromTheSide()
        {
            bool snapped = AssemblySnapSolver.TrySolve(new Vector2(-2f, 0f), 0f, 1f, ZeroPort,
                Vector2.zero, 180f, 1f, ZeroPort, 10f, 5f, 5f, out _);

            Assert.That(snapped, Is.False);
        }

        [Test]
        public void CollinearOpposingArrowsCannotSnapFromFarAway()
        {
            bool snapped = AssemblySnapSolver.TrySolve(new Vector2(0f, -8f), 0f, 1f, ZeroPort,
                Vector2.zero, 180f, 1f, ZeroPort, 10f, 0.3f, 0.4f, out _);

            Assert.That(snapped, Is.False);
        }

        [TestCase(8f, true)]
        [TestCase(7.9f, false)]
        public void AngleToleranceIncludesItsBoundary(float tolerance, bool expected)
        {
            bool snapped = AssemblySnapSolver.TrySolve(new Vector2(0f, -2f), 8f, 1f, ZeroPort,
                Vector2.zero, 180f, 1f, ZeroPort, tolerance, 1f, 1f, out _);

            Assert.That(snapped, Is.EqualTo(expected));
        }

        [TestCase(0.2f, true)]
        [TestCase(0.19f, false)]
        public void LateralToleranceIncludesItsBoundary(float tolerance, bool expected)
        {
            bool snapped = AssemblySnapSolver.TrySolve(new Vector2(0.2f, -2f), 0f, 1f, ZeroPort,
                Vector2.zero, 180f, 1f, ZeroPort, 10f, tolerance, 1f, out _);

            Assert.That(snapped, Is.EqualTo(expected));
        }

        [TestCase(0.25f, true)]
        [TestCase(0.24f, false)]
        public void ContactToleranceIncludesItsBoundary(float tolerance, bool expected)
        {
            bool snapped = AssemblySnapSolver.TrySolve(new Vector2(0f, -2.25f), 0f, 1f, ZeroPort,
                Vector2.zero, 180f, 1f, ZeroPort, 10f, 0.3f, tolerance, out _);

            Assert.That(snapped, Is.EqualTo(expected));
        }

        [Test]
        public void WraparoundAnglesHaveSmallError()
        {
            bool snapped = AssemblySnapSolver.TrySolve(new Vector2(0f, -2f), 359f, 1f, ZeroPort,
                Vector2.zero, 181f, 1f, ZeroPort, 3f, 0.1f, 0.1f, out SnapSolution solution);

            Assert.That(snapped, Is.True);
            Assert.That(solution.AngleError, Is.EqualTo(2f).Within(0.001f));
            Assert.That(solution.RotationDegrees, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void CoincidentCentersDoNotDefineAValidConnection()
        {
            Assert.That(AssemblySnapSolver.TrySolve(Vector2.zero, 0f, 1f, ZeroPort,
                Vector2.zero, 180f, 1f, ZeroPort, 180f, 5f, 5f, out _), Is.False);
        }

        [Test]
        public void InvalidRadiusOrToleranceIsRejected()
        {
            Assert.That(AssemblySnapSolver.TrySolve(new Vector2(0f, -2f), 0f, -1f, ZeroPort,
                Vector2.zero, 180f, 1f, ZeroPort, 10f, 0.3f, 0.3f, out _), Is.False);
            Assert.That(AssemblySnapSolver.TrySolve(new Vector2(0f, -2f), 0f, 1f, ZeroPort,
                Vector2.zero, 180f, 1f, ZeroPort, float.NaN, 0.3f, 0.3f, out _), Is.False);
        }

        private static void AssertVector(Vector2 expected, Vector2 actual)
        {
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(0.0001f),
                "Expected " + expected + "; received " + actual);
        }
    }
}
