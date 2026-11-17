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
    /// decorated (hop or gap) and the horizontal one stays straight. Each crossing also says which side a hop bulges
    /// to: +x by default, -x when something (another net's vertical wire or a gate body) sits within
    /// <see cref="HopReach"/> on the +x side and the -x side is clear. Purely visual and allocation-free.
    /// </summary>
    public static class WireCrossings
    {
        /// <summary>
        /// Least distance between a crossing and the end of either segment. Closer crossings are a bend, a pin or a
        /// T-touch rather than a clean crossing, and a hop there would collide with the bend.
        /// </summary>
        public const float Clearance = 0.3f;

        /// <summary>
        /// How far beside the wire a hop needs free space: the largest hop (on a bus) plus a little air, so a hop
        /// never touches a neighbouring wire or a gate.
        /// </summary>
        public const float HopReach = WireView.HopRadius * WireView.BusHopFactor + 0.12f;

        private const float Epsilon = 0.02f;

        private static readonly List<Rect> NoBodies = new List<Rect>();

        /// <summary>
        /// Appends to <paramref name="results"/> every point where a vertical segment of <paramref name="path"/>
        /// strictly crosses one of <paramref name="others"/> (segments of other nets as x0, y0, x1, y1; their
        /// horizontal ones are crossed, their vertical ones and <paramref name="bodies"/> decide the hop side). Each
        /// result is (x, y, side) with side +1 or -1. Points are kept at least <see cref="Clearance"/> away from both
        /// segments' ends and are not duplicated.
        /// </summary>
        public static void Find(IReadOnlyList<Vector3> path, IReadOnlyList<Vector4> others, List<Vector3> results,
            IReadOnlyList<Rect> bodies = null)
        {
            bodies ??= NoBodies;
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

                    if (!Contains(results, x, y))
                    {
                        results.Add(new Vector3(x, y, SideOf(x, y, others, bodies)));
                    }
                }
            }
        }

        /// <summary>+1 unless the +x side of the crossing is blocked and the -x side is free.</summary>
        public static float SideOf(float x, float y, IReadOnlyList<Vector4> others, IReadOnlyList<Rect> bodies)
        {
            if (!IsBlocked(x, y, 1f, others, bodies))
            {
                return 1f;
            }

            return IsBlocked(x, y, -1f, others, bodies) ? 1f : -1f;
        }

        private static bool IsBlocked(float x, float y, float side, IReadOnlyList<Vector4> others,
            IReadOnlyList<Rect> bodies)
        {
            // The area a hop on this side covers, beside the wire (the wire's own line excluded).
            float near = side > 0f ? x + Epsilon : x - HopReach;
            float far = side > 0f ? x + HopReach : x - Epsilon;
            float bottom = y - HopReach;
            float top = y + HopReach;
            for (int k = 0; k < others.Count; k++)
            {
                Vector4 other = others[k];
                if (Mathf.Abs(other.z - other.x) > Epsilon)
                {
                    continue; // only vertical neighbours run alongside the hop
                }

                float ox = other.x;
                if (ox >= near && ox <= far && Mathf.Max(other.y, other.w) >= bottom && Mathf.Min(other.y, other.w) <= top)
                {
                    return true;
                }
            }

            for (int k = 0; k < bodies.Count; k++)
            {
                Rect body = bodies[k];
                if (body.xMax >= near && body.xMin <= far && body.yMax >= bottom && body.yMin <= top)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(List<Vector3> points, float x, float y)
        {
            for (int i = 0; i < points.Count; i++)
            {
                float dx = points[i].x - x;
                float dy = points[i].y - y;
                if (dx * dx + dy * dy < Epsilon * Epsilon)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
