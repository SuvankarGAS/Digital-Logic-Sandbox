using System.Collections.Generic;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering.Wires
{
    /// <summary>How a wire is drawn where it crosses another net's wire without connecting to it.</summary>
    public enum WireCrossingStyle
    {
        /// <summary>The vertical wire bulges into a small semicircle over the horizontal one.</summary>
        Hop = 0,

        /// <summary>The vertical wire is broken by a short gap on each side of the horizontal one.</summary>
        Gap = 1,

        /// <summary>Wires simply cross.</summary>
        Plain = 2,
    }

    /// <summary>
    /// Finds the points where a routed wire crosses other nets' wires. Routes are rectilinear, so every crossing has
    /// one vertical and one horizontal segment; by convention only the wire with the <em>vertical</em> segment is
    /// decorated (hop or gap) and the horizontal one stays straight. Purely visual and allocation-free.
    /// </summary>
    public static class WireCrossings
    {
        /// <summary>
        /// Least distance between a crossing and the end of either segment. Closer crossings are a bend, a pin or a
        /// T-touch rather than a clean crossing, and a hop there would collide with the bend.
        /// </summary>
        public const float Clearance = 0.3f;

        private const float Epsilon = 0.02f;

        /// <summary>
        /// Appends to <paramref name="results"/> every point where a vertical segment of <paramref name="path"/>
        /// strictly crosses one of <paramref name="others"/> (segments of other nets as x0, y0, x1, y1; only their
        /// horizontal ones matter). Points are kept at least <see cref="Clearance"/> away from both segments' ends
        /// and are not duplicated.
        /// </summary>
        public static void Find(IReadOnlyList<Vector3> path, IReadOnlyList<Vector4> others, List<Vector2> results)
        {
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 a = path[i - 1];
                Vector3 b = path[i];
                if (Mathf.Abs(b.x - a.x) > Epsilon || Mathf.Abs(b.y - a.y) < 2f * Clearance)
                {
                    continue; // not vertical, or too short to hold a crossing
                }

                float x = a.x;
                float yMin = Mathf.Min(a.y, b.y) + Clearance;
                float yMax = Mathf.Max(a.y, b.y) - Clearance;
                for (int k = 0; k < others.Count; k++)
                {
                    Vector4 other = others[k];
                    if (Mathf.Abs(other.w - other.y) > Epsilon)
                    {
                        continue; // not horizontal
                    }

                    float y = other.y;
                    float xMin = Mathf.Min(other.x, other.z) + Clearance;
                    float xMax = Mathf.Max(other.x, other.z) - Clearance;
                    if (y < yMin || y > yMax || x < xMin || x > xMax)
                    {
                        continue;
                    }

                    var point = new Vector2(x, y);
                    if (!Contains(results, point))
                    {
                        results.Add(point);
                    }
                }
            }
        }

        private static bool Contains(List<Vector2> points, Vector2 point)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if ((points[i] - point).sqrMagnitude < Epsilon * Epsilon)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
