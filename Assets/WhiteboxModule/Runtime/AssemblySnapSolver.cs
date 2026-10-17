using System.Collections.Generic;
using UnityEngine;

namespace LudumDare60.AssemblyDemo
{
    /// <summary>A proposed pose for the moving module; the fixed module is never changed.</summary>
    public readonly struct SnapSolution
    {
        public readonly Vector2 Position;
        public readonly float RotationDegrees;
        public readonly int MovingPortIndex;
        public readonly int FixedPortIndex;
        public readonly float AngleError;
        public readonly float LateralError;
        public readonly float ContactError;
        public readonly float Score;

        public SnapSolution(Vector2 position, float rotationDegrees, int movingPortIndex,
            int fixedPortIndex, float angleError, float lateralError, float contactError, float score)
        {
            Position = position;
            RotationDegrees = rotationDegrees;
            MovingPortIndex = movingPortIndex;
            FixedPortIndex = fixedPortIndex;
            AngleError = angleError;
            LateralError = lateralError;
            ContactError = contactError;
            Score = score;
        }
    }

    /// <summary>
    /// Pure 2D connector geometry. Angles are clockwise from world up: 0 up, 90 right.
    /// Arrow tails lie at module centers and tips lie on their inscribed circles.
    /// </summary>
    public static class AssemblySnapSolver
    {
        private const float DistanceEpsilon = 0.00001f;
        private const float AngleEpsilon = 0.0001f;

        public static float NormalizeAngle(float clockwiseDegrees)
        {
            float angle = clockwiseDegrees % 360f;
            return angle < 0f ? angle + 360f : angle;
        }

        public static Vector2 Direction(float clockwiseDegrees)
        {
            float radians = NormalizeAngle(clockwiseDegrees) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        public static float ClockwiseAngle(Vector2 direction)
        {
            return NormalizeAngle(Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// Finds the closest eligible pair of arrow tips. Both arrows must face the other
        /// center, agree with their common center line and point in opposite directions.
        /// Angle tolerance is in degrees; lateral and contact tolerances are world units.
        /// Null/empty angle lists supply one port at zero degrees. Ties prefer list order.
        /// Returned errors describe the original pose, before the exact correction.
        /// </summary>
        public static bool TrySolve(
            Vector2 movingCenter, float movingRotation, float movingRadius,
            IReadOnlyList<float> movingAngles,
            Vector2 fixedCenter, float fixedRotation, float fixedRadius,
            IReadOnlyList<float> fixedAngles,
            float angleTolerance, float lateralTolerance, float contactTolerance,
            out SnapSolution solution)
        {
            solution = default;
            if (!IsFinite(movingCenter) || !IsFinite(fixedCenter) ||
                !IsFinite(movingRotation) || !IsFinite(fixedRotation) ||
                !IsFinite(movingRadius) || !IsFinite(fixedRadius) ||
                movingRadius <= 0f || fixedRadius <= 0f ||
                !IsFinite(angleTolerance) || !IsFinite(lateralTolerance) ||
                !IsFinite(contactTolerance) || angleTolerance < 0f ||
                lateralTolerance < 0f || contactTolerance < 0f)
                return false;

            Vector2 towardsFixed = fixedCenter - movingCenter;
            if (towardsFixed.sqrMagnitude <= DistanceEpsilon * DistanceEpsilon)
                return false;

            float lineAngle = ClockwiseAngle(towardsFixed);
            int movingCount = movingAngles == null || movingAngles.Count == 0 ? 1 : movingAngles.Count;
            int fixedCount = fixedAngles == null || fixedAngles.Count == 0 ? 1 : fixedAngles.Count;
            bool found = false;
            float bestScore = float.PositiveInfinity;

            for (int movingIndex = 0; movingIndex < movingCount; movingIndex++)
            {
                float movingLocalAngle = PortAngle(movingAngles, movingIndex);
                if (!IsFinite(movingLocalAngle))
                    continue;

                float movingWorldAngle = NormalizeAngle(NormalizeAngle(movingRotation) +
                    NormalizeAngle(movingLocalAngle));
                Vector2 movingDirection = Direction(movingWorldAngle);
                // A generous user tolerance must never allow arrows facing away.
                if (Vector2.Dot(towardsFixed, movingDirection) <= 0f)
                    continue;

                for (int fixedIndex = 0; fixedIndex < fixedCount; fixedIndex++)
                {
                    float fixedLocalAngle = PortAngle(fixedAngles, fixedIndex);
                    if (!IsFinite(fixedLocalAngle))
                        continue;

                    float fixedWorldAngle = NormalizeAngle(NormalizeAngle(fixedRotation) +
                        NormalizeAngle(fixedLocalAngle));
                    Vector2 fixedDirection = Direction(fixedWorldAngle);
                    if (Vector2.Dot(-towardsFixed, fixedDirection) <= 0f)
                        continue;

                    float oppositeError = Mathf.Abs(Mathf.DeltaAngle(movingWorldAngle,
                        fixedWorldAngle + 180f));
                    float movingLineError = Mathf.Abs(Mathf.DeltaAngle(movingWorldAngle, lineAngle));
                    float fixedLineError = Mathf.Abs(Mathf.DeltaAngle(fixedWorldAngle, lineAngle + 180f));
                    float angleError = Mathf.Max(oppositeError, Mathf.Max(movingLineError, fixedLineError));
                    if (angleError > angleTolerance + AngleEpsilon)
                        continue;

                    float lateralError = Mathf.Max(Mathf.Abs(Cross(towardsFixed, movingDirection)),
                        Mathf.Abs(Cross(towardsFixed, fixedDirection)));
                    if (lateralError > lateralTolerance + DistanceEpsilon)
                        continue;

                    Vector2 movingTip = movingCenter + movingDirection * movingRadius;
                    Vector2 fixedTip = fixedCenter + fixedDirection * fixedRadius;
                    float contactError = Vector2.Distance(movingTip, fixedTip);
                    if (contactError > contactTolerance + DistanceEpsilon)
                        continue;

                    float score = NormalizedSquared(angleError, angleTolerance, AngleEpsilon) +
                        NormalizedSquared(lateralError, lateralTolerance, DistanceEpsilon) +
                        NormalizedSquared(contactError, contactTolerance, DistanceEpsilon);
                    if (score >= bestScore)
                        continue;

                    // Anchor to the fixed arrow. This makes both tips coincide exactly,
                    // even when the modules have different radii or multiple local ports.
                    Vector2 position = fixedCenter + fixedDirection * (movingRadius + fixedRadius);
                    float rotation = NormalizeAngle(fixedWorldAngle + 180f - NormalizeAngle(movingLocalAngle));
                    solution = new SnapSolution(position, rotation, movingIndex, fixedIndex,
                        angleError, lateralError, contactError, score);
                    bestScore = score;
                    found = true;
                }
            }

            return found;
        }

        private static float PortAngle(IReadOnlyList<float> angles, int index)
        {
            return angles == null || angles.Count == 0 ? 0f : angles[index];
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private static float NormalizedSquared(float error, float tolerance, float epsilon)
        {
            float normalized = error / Mathf.Max(tolerance, epsilon);
            return normalized * normalized;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }
    }
}
