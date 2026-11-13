using DigitalLogicSandbox.Core.Nodes;
using UnityEngine;

namespace DigitalLogicSandbox.CircuitEditor.Placement
{
    /// <summary>
    /// Grid snapping for node positions. Pins are spaced 1 unit apart and centred on the gate, so snapping gate
    /// centres to a half-unit grid keeps every pin on the half-unit grid as well. That lets wires between pins at the
    /// same height run perfectly straight.
    /// </summary>
    public static class GridSnap
    {
        public const float Step = 0.5f;

        public static float Snap(float value)
        {
            return Mathf.Round(value / Step) * Step;
        }

        public static Vector2 Snap(Vector2 value)
        {
            return new Vector2(Snap(value.x), Snap(value.y));
        }

        public static CircuitPoint ToPoint(Vector2 value, bool snap)
        {
            Vector2 v = snap ? Snap(value) : value;
            return new CircuitPoint(v.x, v.y);
        }

        public static Vector2 ToVector(CircuitPoint point)
        {
            return new Vector2(point.X, point.Y);
        }
    }
}
