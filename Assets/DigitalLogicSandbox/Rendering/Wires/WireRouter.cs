using System.Collections.Generic;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering.Wires
{
    /// <summary>
    /// Orthogonal wire routing between an output pin and an input pin. Purely visual: the simulation never sees
    /// routes.
    /// <para>
    /// Each pin has an outward normal (the side of the gate it sits on, which follows the gate rotation). The wire
    /// leaves the source along its normal, enters the destination against its normal, and in between takes the
    /// cheapest of a small, fixed set of orthogonal candidate shapes (L, Z and detour shapes). Candidates are scored
    /// by length and bend count; shapes that pass through either endpoint gate's body, or double back over a pin
    /// stub, are heavily penalised. This is deterministic and allocation-free (scratch buffers are reused), and is
    /// cheap enough to run for every wire touching a moving gate each frame.
    /// </para>
    /// <para>
    /// Only the two endpoint gates are treated as obstacles. Full obstacle-aware routing over the whole workspace
    /// (A* on a visibility grid) is a possible future extension; routes are cached per wire so it could be added
    /// behind the same API.
    /// </para>
    /// </summary>
    public static class WireRouter
    {
        /// <summary>Straight run out of a pin before the first bend.</summary>
        public const float Stub = 0.5f;

        /// <summary>Margin around gate bodies that a wire must not enter (smaller than <see cref="Stub"/>).</summary>
        public const float BodyMargin = 0.25f;

        private const float BendCost = 0.6f;
        private const float BodyCrossingCost = 1000f;
        private const float ReversalCost = 200f;
        private const int MaxShapePoints = 6;
        private const int MaxCoordinates = 8;

        private static readonly Vector2[] Candidate = new Vector2[MaxShapePoints];
        private static readonly Vector2[] Best = new Vector2[MaxShapePoints];
        private static readonly float[] MidXs = new float[MaxCoordinates];
        private static readonly float[] MidYs = new float[MaxCoordinates];

        private static int _bestCount;
        private static float _bestCost;

        /// <summary>
        /// Routes from an unrotated output (exits to the right) to an unrotated input (enters from the left) with no
        /// obstacles. Used for preview wires that are not attached to a gate at one end.
        /// </summary>
        public static void Route(Vector2 from, Vector2 to, List<Vector3> points)
        {
            Route(from, Vector2.right, default, to, Vector2.left, default, points);
        }

        /// <summary>
        /// Routes a wire between two pins.
        /// </summary>
        /// <param name="from">Source (output) pin, world space.</param>
        /// <param name="fromNormal">Outward unit normal of the source pin (axis-aligned).</param>
        /// <param name="fromBody">World bounds of the source gate, or a zero-size rect for none.</param>
        /// <param name="to">Destination (input) pin, world space.</param>
        /// <param name="toNormal">Outward unit normal of the destination pin (axis-aligned).</param>
        /// <param name="toBody">World bounds of the destination gate, or a zero-size rect for none.</param>
        /// <param name="points">Receives the polyline, starting at <paramref name="from"/>.</param>
        public static void Route(Vector2 from, Vector2 fromNormal, Rect fromBody, Vector2 to, Vector2 toNormal,
            Rect toBody, List<Vector3> points)
        {
            points.Clear();
            fromNormal = Axis(fromNormal);
            toNormal = Axis(toNormal);

            // Straight wire: pins face each other on the same line with nothing in between.
            if (IsDirectLine(from, fromNormal, to, toNormal))
            {
                points.Add(from);
                points.Add(to);
                return;
            }

            Vector2 a = from + fromNormal * Stub;
            Vector2 b = to + toNormal * Stub;
            int midXCount = CollectMidXs(a, b, fromBody, toBody);
            int midYCount = CollectMidYs(a, b, fromBody, toBody);
            Rect bodyA = Inflate(fromBody, BodyMargin);
            Rect bodyB = Inflate(toBody, BodyMargin);

            _bestCost = float.MaxValue;
            _bestCount = 0;

            // L shapes.
            Consider(a, new Vector2(b.x, a.y), b, fromNormal, toNormal, bodyA, bodyB);
            Consider(a, new Vector2(a.x, b.y), b, fromNormal, toNormal, bodyA, bodyB);

            for (int i = 0; i < midXCount; i++)
            {
                float x = MidXs[i];
                Consider(a, new Vector2(x, a.y), new Vector2(x, b.y), b, fromNormal, toNormal, bodyA, bodyB);
            }

            for (int i = 0; i < midYCount; i++)
            {
                float y = MidYs[i];
                Consider(a, new Vector2(a.x, y), new Vector2(b.x, y), b, fromNormal, toNormal, bodyA, bodyB);
            }

            // Detours that combine a free row and a free column (needed when pins face away from each other).
            for (int i = 0; i < midXCount; i++)
            {
                for (int j = 0; j < midYCount; j++)
                {
                    float x = MidXs[i];
                    float y = MidYs[j];
                    ConsiderFive(a, new Vector2(a.x, y), new Vector2(x, y), new Vector2(x, b.y), b,
                        fromNormal, toNormal, bodyA, bodyB);
                    ConsiderFive(a, new Vector2(x, a.y), new Vector2(x, y), new Vector2(b.x, y), b,
                        fromNormal, toNormal, bodyA, bodyB);
                }
            }

            points.Add(from);
            for (int i = 0; i < _bestCount; i++)
            {
                points.Add(Best[i]);
            }

            points.Add(to);
            Simplify(points);
        }

        /// <summary>Removes duplicate and collinear interior points.</summary>
        public static void Simplify(List<Vector3> points)
        {
            for (int i = points.Count - 2; i >= 1; i--)
            {
                Vector3 previous = points[i - 1];
                Vector3 current = points[i];
                Vector3 next = points[i + 1];
                bool duplicate = (current - previous).sqrMagnitude < 1e-6f;
                bool collinear = (Mathf.Abs(previous.x - current.x) < 1e-4f && Mathf.Abs(current.x - next.x) < 1e-4f) ||
                    (Mathf.Abs(previous.y - current.y) < 1e-4f && Mathf.Abs(current.y - next.y) < 1e-4f);
                if (duplicate || collinear)
                {
                    points.RemoveAt(i);
                }
            }

            if (points.Count > 2 && (points[points.Count - 1] - points[points.Count - 2]).sqrMagnitude < 1e-6f)
            {
                points.RemoveAt(points.Count - 2);
            }
        }

        /// <summary>Shortest distance from a point to a polyline.</summary>
        public static float DistanceToPath(IReadOnlyList<Vector3> path, Vector2 point)
        {
            float best = float.MaxValue;
            for (int i = 0; i < path.Count - 1; i++)
            {
                float distance = DistanceToSegment(point, path[i], path[i + 1]);
                if (distance < best)
                {
                    best = distance;
                }
            }

            return best;
        }

        public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            float t = lengthSquared < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
            return Vector2.Distance(point, a + ab * t);
        }

        /// <summary>True when an axis-aligned segment passes through the interior of a rect.</summary>
        public static bool SegmentCrossesRect(Vector2 p, Vector2 q, Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return false;
            }

            const float epsilon = 1e-4f;
            float minX = Mathf.Min(p.x, q.x);
            float maxX = Mathf.Max(p.x, q.x);
            float minY = Mathf.Min(p.y, q.y);
            float maxY = Mathf.Max(p.y, q.y);
            return maxX > rect.xMin + epsilon && minX < rect.xMax - epsilon &&
                maxY > rect.yMin + epsilon && minY < rect.yMax - epsilon;
        }

        private static bool IsDirectLine(Vector2 from, Vector2 fromNormal, Vector2 to, Vector2 toNormal)
        {
            if (fromNormal != -toNormal)
            {
                return false;
            }

            Vector2 delta = to - from;
            bool aligned = Mathf.Abs(fromNormal.x) > 0.5f ? Mathf.Abs(delta.y) < 1e-4f : Mathf.Abs(delta.x) < 1e-4f;
            return aligned && Vector2.Dot(delta, fromNormal) >= 2f * Stub;
        }

        private static int CollectMidXs(Vector2 a, Vector2 b, Rect fromBody, Rect toBody)
        {
            Rect bodyA = Inflate(fromBody, Stub);
            Rect bodyB = Inflate(toBody, Stub);
            int count = 0;
            float middle = Mathf.Round(a.x + b.x) * 0.5f; // midpoint snapped to the half-grid
            if (Mathf.Abs(b.x - a.x) >= 1f)
            {
                middle = Mathf.Clamp(middle, Mathf.Min(a.x, b.x) + 0.5f, Mathf.Max(a.x, b.x) - 0.5f);
            }

            MidXs[count++] = middle;
            count = AddBodyEdges(MidXs, count, bodyA.xMin, bodyA.xMax, bodyB.xMin, bodyB.xMax, bodyA, bodyB);
            return count;
        }

        private static int CollectMidYs(Vector2 a, Vector2 b, Rect fromBody, Rect toBody)
        {
            Rect bodyA = Inflate(fromBody, Stub);
            Rect bodyB = Inflate(toBody, Stub);
            int count = 0;
            MidYs[count++] = (a.y + b.y) * 0.5f;
            count = AddBodyEdges(MidYs, count, bodyA.yMin, bodyA.yMax, bodyB.yMin, bodyB.yMax, bodyA, bodyB);
            return count;
        }

        /// <summary>
        /// Adds free coordinates on one axis: the edges of each (stub-inflated) body, the outer edges of both
        /// together, and the gap between them.
        /// </summary>
        private static int AddBodyEdges(float[] values, int count, float aMin, float aMax, float bMin, float bMax,
            Rect bodyA, Rect bodyB)
        {
            bool hasA = bodyA.width > 0f && bodyA.height > 0f;
            bool hasB = bodyB.width > 0f && bodyB.height > 0f;
            if (hasA)
            {
                values[count++] = aMin;
                values[count++] = aMax;
            }

            if (hasB)
            {
                values[count++] = bMin;
                values[count++] = bMax;
            }

            if (hasA && hasB)
            {
                values[count++] = Mathf.Min(aMin, bMin);
                values[count++] = Mathf.Max(aMax, bMax);
                // The gap between two bodies, when there is one.
                if (aMax < bMin || bMax < aMin)
                {
                    values[count++] = aMax < bMin ? (aMax + bMin) * 0.5f : (bMax + aMin) * 0.5f;
                }
            }

            return count;
        }

        private static void Consider(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 fromNormal, Vector2 toNormal,
            Rect bodyA, Rect bodyB)
        {
            Candidate[0] = p0;
            Candidate[1] = p1;
            Candidate[2] = p2;
            Evaluate(3, fromNormal, toNormal, bodyA, bodyB);
        }

        private static void Consider(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 fromNormal,
            Vector2 toNormal, Rect bodyA, Rect bodyB)
        {
            Candidate[0] = p0;
            Candidate[1] = p1;
            Candidate[2] = p2;
            Candidate[3] = p3;
            Evaluate(4, fromNormal, toNormal, bodyA, bodyB);
        }

        private static void ConsiderFive(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4,
            Vector2 fromNormal, Vector2 toNormal, Rect bodyA, Rect bodyB)
        {
            Candidate[0] = p0;
            Candidate[1] = p1;
            Candidate[2] = p2;
            Candidate[3] = p3;
            Candidate[4] = p4;
            Evaluate(5, fromNormal, toNormal, bodyA, bodyB);
        }

        private static void Evaluate(int count, Vector2 fromNormal, Vector2 toNormal, Rect bodyA, Rect bodyB)
        {
            float cost = 0f;
            Vector2 previousDirection = fromNormal; // direction of the stub leaving the source
            for (int i = 0; i < count - 1; i++)
            {
                Vector2 p = Candidate[i];
                Vector2 q = Candidate[i + 1];
                Vector2 delta = q - p;
                float length = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
                if (length < 1e-4f)
                {
                    continue;
                }

                Vector2 direction = Axis(delta);
                cost += length;
                if (direction != previousDirection)
                {
                    cost += direction == -previousDirection ? ReversalCost : BendCost;
                }

                if (SegmentCrossesRect(p, q, bodyA) || SegmentCrossesRect(p, q, bodyB))
                {
                    cost += BodyCrossingCost;
                }

                previousDirection = direction;
            }

            // The stub into the destination travels against its outward normal.
            Vector2 entry = -toNormal;
            if (entry != previousDirection)
            {
                cost += entry == -previousDirection ? ReversalCost : BendCost;
            }

            if (cost < _bestCost - 1e-4f)
            {
                _bestCost = cost;
                _bestCount = count;
                for (int i = 0; i < count; i++)
                {
                    Best[i] = Candidate[i];
                }
            }
        }

        private static Rect Inflate(Rect body, float margin)
        {
            if (body.width <= 0f || body.height <= 0f)
            {
                return default;
            }

            return Rect.MinMaxRect(body.xMin - margin, body.yMin - margin, body.xMax + margin, body.yMax + margin);
        }

        private static Vector2 Axis(Vector2 v)
        {
            if (Mathf.Abs(v.x) >= Mathf.Abs(v.y))
            {
                return v.x >= 0f ? Vector2.right : Vector2.left;
            }

            return v.y >= 0f ? Vector2.up : Vector2.down;
        }
    }
}
