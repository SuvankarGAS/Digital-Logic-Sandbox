using System.Collections.Generic;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering.Wires
{
    /// <summary>
    /// Orthogonal wire routing between an output pin (left side of the route) and an input pin. Purely visual: the
    /// simulation never sees routes. Forward routes use a single vertical jog at the midpoint; backward routes
    /// leave the source to the right, travel around, and enter the destination from the left.
    /// </summary>
    public static class WireRouter
    {
        public const float Stub = 0.5f;

        public static void Route(Vector2 from, Vector2 to, List<Vector3> points)
        {
            points.Clear();
            points.Add(from);

            if (to.x - from.x >= 2f * Stub)
            {
                float middle = Mathf.Round(from.x + to.x) * 0.5f; // midpoint snapped to the half-grid
                middle = Mathf.Clamp(middle, from.x + Stub, to.x - Stub);
                points.Add(new Vector3(middle, from.y));
                points.Add(new Vector3(middle, to.y));
            }
            else
            {
                float exitX = from.x + Stub;
                float entryX = to.x - Stub;
                float detourY = Mathf.Abs(from.y - to.y) >= 1f
                    ? (from.y + to.y) * 0.5f
                    : Mathf.Max(from.y, to.y) + 1.5f;
                points.Add(new Vector3(exitX, from.y));
                points.Add(new Vector3(exitX, detourY));
                points.Add(new Vector3(entryX, detourY));
                points.Add(new Vector3(entryX, to.y));
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
    }
}
