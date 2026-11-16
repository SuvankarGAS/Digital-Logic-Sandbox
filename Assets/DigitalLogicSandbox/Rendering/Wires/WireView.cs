using System.Collections.Generic;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Signals;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering.Wires
{
    /// <summary>
    /// Pooled visual for one connection (or the wiring preview). The path is a small mesh with one quad per straight
    /// segment, drawn by the <c>DigitalLogicSandbox/Wire</c> shader. The shader turns each quad into an anti-aliased
    /// capsule with a soft glow, so wires have crisp edges, round joints and no glow seams at bends. Overlapping wires
    /// (a shared fan-out trunk) merge cleanly instead of brightening.
    /// <para>
    /// Signal state is shown by colour, width and glow. A signal transition (<see cref="SetTransition"/>) draws the
    /// new state from the source up to a travelling front with a bright head, and the old state beyond it. All of
    /// this is driven through a <see cref="MaterialPropertyBlock"/>; the mesh is rebuilt only when the path
    /// changes. Has no Update: it changes only when the presenter or the animation controller pushes a new state.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class WireView : MonoBehaviour
    {
        /// <summary>Length (world units) of the bright head of a travelling signal.</summary>
        private const float FrontLength = 0.6f;

        /// <summary>Path distance used as "front beyond the end": the whole wire shows the settled state.</summary>
        private const float Settled = 1e6f;

        /// <summary>Glow intensity of energized wires relative to <see cref="VisualTheme.GlowStrength"/>.</summary>
        private const float HighGlowFactor = 1.6f;

        /// <summary>Radius of a junction dot relative to the wire's half width.</summary>
        public const float JunctionRadiusFactor = 2.1f;

        /// <summary>Width factor of bus wires relative to single-bit wires.</summary>
        public const float BusWidthFactor = 1.9f;

        /// <summary>Radius of a crossing hop (world units; single-bit wires).</summary>
        public const float HopRadius = 0.18f;

        /// <summary>
        /// Hop and gap size factor for bus wires. Smaller than <see cref="BusWidthFactor"/>, so hops on adjacent
        /// half-unit lanes never touch.
        /// </summary>
        public const float BusHopFactor = 1.3f;

        /// <summary>Half length of a crossing gap (world units; single-bit wires).</summary>
        public const float GapHalf = 0.2f;

        /// <summary>Straight segments approximating one hop's semicircle.</summary>
        private const int HopSegments = 8;

        private static readonly int ColorAId = Shader.PropertyToID("_ColorA");
        private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
        private static readonly int FrontColorId = Shader.PropertyToID("_FrontColor");
        private static readonly int WidthId = Shader.PropertyToID("_Width");
        private static readonly int PathId = Shader.PropertyToID("_Path");
        private static readonly int JunctionId = Shader.PropertyToID("_Junction");

        private readonly List<Vector3> _path = new List<Vector3>(8);
        private readonly List<Vector2> _crossings = new List<Vector2>(4);
        private readonly List<Vector2> _gaps = new List<Vector2>(4); // per segment: (from, to) distances along it
        private WireCrossingStyle _crossingStyle = WireCrossingStyle.Plain;
        private readonly List<Vector3> _points = new List<Vector3>(8);
        private readonly List<float> _cumulative = new List<float>(8);
        private readonly List<Vector3> _vertices = new List<Vector3>(32);
        private readonly List<Vector2> _local = new List<Vector2>(32);
        private readonly List<Vector2> _segment = new List<Vector2>(32);
        private readonly List<int> _triangles = new List<int>(48);

        private MeshRenderer _renderer;
        private Mesh _mesh;
        private MaterialPropertyBlock _block;
        private VisualTheme _theme;
        private SignalStyle _style;
        private Signal _signal;
        private bool _selected;
        private bool _hovered;
        private bool _isPreview;
        private bool _previewValid;
        private bool _inTransition;
        private Signal _transitionFrom;
        private Signal _transitionTo;
        private float _transitionProgress;
        private bool _isBus;
        private bool _hasJunction;
        private Vector2 _junction;
        private float _width;

        public PinRef Destination { get; set; }

        /// <summary>Multi-bit connection: drawn thicker, showing the aggregate state of its bits.</summary>
        public bool IsBus
        {
            get => _isBus;
            set
            {
                if (_isBus != value)
                {
                    _isBus = value;
                    if (_crossings.Count > 0 && _crossingStyle != WireCrossingStyle.Plain)
                    {
                        BuildDrawnPath(); // hops and gaps are larger on buses
                        RebuildMesh();
                    }

                    Apply();
                }
            }
        }

        /// <summary>The settled signal shown (during a transition: the state being left).</summary>
        public Signal Signal => _signal;

        public bool InTransition => _inTransition;

        public float TransitionProgress => _transitionProgress;

        /// <summary>Displayed core width in world units (during a transition: the wider of the two states).</summary>
        public float Width => _width;

        /// <summary>Path length in world units.</summary>
        public float Length => _cumulative.Count > 0 ? _cumulative[_cumulative.Count - 1] : 0f;

        /// <summary>Number of points of the drawn path (including the points of crossing hops).</summary>
        public int PointCount => _points.Count;

        /// <summary>Number of crossings drawn with the current style (0 for <see cref="WireCrossingStyle.Plain"/>).</summary>
        public int CrossingCount => _crossingStyle == WireCrossingStyle.Plain ? 0 : _crossings.Count;

        /// <summary>How crossings are drawn.</summary>
        public WireCrossingStyle CrossingStyle => _crossingStyle;

        /// <summary>Number of mesh quads (one per drawn straight piece; a gap splits a segment in two).</summary>
        public int QuadCount => _mesh != null ? _mesh.vertexCount / 4 : 0;

        /// <summary>True when a junction dot is drawn.</summary>
        public bool HasJunction => _hasJunction;

        /// <summary>World position of the junction dot (valid while <see cref="HasJunction"/>).</summary>
        public Vector2 Junction => _junction;

        /// <summary>Path point <paramref name="index"/> in world space.</summary>
        public Vector3 GetPathPoint(int index) => _points[index];

        public static WireView Create(Transform parent, VisualTheme theme, SignalStyle style = null)
        {
            var go = new GameObject("Wire");
            go.transform.SetParent(parent, false);
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = theme.WireMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.sortingOrder = SortingOrders.Wire;

            var mesh = new Mesh { name = "Wire" };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;

            var view = go.AddComponent<WireView>();
            view._renderer = renderer;
            view._mesh = mesh;
            view._block = new MaterialPropertyBlock();
            view._theme = theme;
            view._style = style ?? new SignalStyle(theme);
            view.ResetState();
            return view;
        }

        /// <summary>Shared style (set by the presenter so a mode switch affects every wire).</summary>
        public SignalStyle Style
        {
            get => _style;
            set
            {
                _style = value ?? new SignalStyle(_theme);
                Apply();
            }
        }

        public void ResetState()
        {
            _signal = Signal.Low;
            _selected = false;
            _hovered = false;
            _isPreview = false;
            _inTransition = false;
            _isBus = false;
            _hasJunction = false;
            _crossings.Clear();
            Destination = default;
            _renderer.sortingOrder = SortingOrders.Wire;
            Apply();
        }

        public void SetPath(IReadOnlyList<Vector3> points)
        {
            _path.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                _path.Add(points[i]);
            }

            BuildDrawnPath();
            RebuildMesh();
        }

        /// <summary>
        /// Sets the points where this wire crosses other nets (on its vertical segments) and how to draw them. Rebuilds
        /// the mesh only when something changed. Purely visual: hit-testing and routing use the plain path.
        /// </summary>
        public void SetCrossings(IReadOnlyList<Vector2> points, WireCrossingStyle style)
        {
            bool same = style == _crossingStyle && points.Count == _crossings.Count;
            for (int i = 0; same && i < points.Count; i++)
            {
                same = (points[i] - _crossings[i]).sqrMagnitude < 1e-8f;
            }

            if (same)
            {
                return;
            }

            _crossingStyle = style;
            _crossings.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                _crossings.Add(points[i]);
            }

            BuildDrawnPath();
            RebuildMesh();
        }

        /// <summary>Forgets the crossings without rebuilding (the next <see cref="SetPath"/> draws a plain wire).</summary>
        public void ClearCrossings()
        {
            _crossings.Clear();
        }

        private float HopSize => _isBus ? BusHopFactor : 1f;

        /// <summary>
        /// Builds the drawn polyline from the routed path: hops become semicircles bulging towards +x. Cumulative
        /// distances follow the drawn line, so the travelling front and particles follow the hops.
        /// </summary>
        private void BuildDrawnPath()
        {
            _points.Clear();
            _cumulative.Clear();
            bool hops = _crossingStyle == WireCrossingStyle.Hop && _crossings.Count > 0 && !_isPreview;
            float radius = HopRadius * HopSize;
            for (int i = 0; i < _path.Count; i++)
            {
                if (hops && i > 0)
                {
                    AddHops(_path[i - 1], _path[i], radius);
                }

                AddDrawnPoint(_path[i]);
            }
        }

        private void AddDrawnPoint(Vector3 point)
        {
            int count = _points.Count;
            float total = count > 0 ? _cumulative[count - 1] + Vector3.Distance(_points[count - 1], point) : 0f;
            _points.Add(point);
            _cumulative.Add(total);
        }

        /// <summary>Adds the hop arcs of one vertical segment, in travel order.</summary>
        private void AddHops(Vector3 a, Vector3 b, float radius)
        {
            if (Mathf.Abs(b.x - a.x) > 0.02f)
            {
                return;
            }

            float direction = Mathf.Sign(b.y - a.y);
            float length = Mathf.Abs(b.y - a.y);
            float last = -radius; // end of the previous hop along the segment
            while (true)
            {
                // Next crossing along the travel direction (crossings are few: a selection pass is fine).
                float best = float.MaxValue;
                for (int k = 0; k < _crossings.Count; k++)
                {
                    float t = CrossingDistance(a, direction, length, _crossings[k], radius);
                    if (t >= 0f && t >= last + radius && t < best)
                    {
                        best = t;
                    }
                }

                if (best == float.MaxValue)
                {
                    return;
                }

                for (int s = 0; s <= HopSegments; s++)
                {
                    float angle = Mathf.PI * s / HopSegments;
                    float along = best - radius * Mathf.Cos(angle);
                    AddDrawnPoint(new Vector3(a.x + radius * Mathf.Sin(angle), a.y + direction * along, a.z));
                }

                last = best + radius;
            }
        }

        /// <summary>
        /// Distance along a vertical segment of a crossing on it, or -1 when the crossing is not on the segment or
        /// too close to its ends for <paramref name="halfSize"/>.
        /// </summary>
        private static float CrossingDistance(Vector3 a, float direction, float length, Vector2 crossing,
            float halfSize)
        {
            if (Mathf.Abs(crossing.x - a.x) > 0.02f)
            {
                return -1f;
            }

            float t = (crossing.y - a.y) * direction;
            return t >= halfSize + 0.02f && t <= length - halfSize - 0.02f ? t : -1f;
        }

        /// <summary>
        /// Shows a dot at <paramref name="position"/> (where this wire leaves a trunk shared with other wires of the
        /// same source), or hides it.
        /// </summary>
        public void SetJunction(bool visible, Vector2 position = default)
        {
            if (_hasJunction == visible && (!visible || _junction == position))
            {
                return;
            }

            _hasJunction = visible;
            _junction = position;
            Apply();
        }

        /// <summary>World position at the normalised distance <paramref name="t"/> (0 = source, 1 = destination).</summary>
        public Vector3 GetPointAt(float t)
        {
            int count = _points.Count;
            if (count == 0)
            {
                return transform.position;
            }

            float length = Length;
            if (count == 1 || length <= 0f)
            {
                return _points[0];
            }

            float target = Mathf.Clamp01(t) * length;
            for (int i = 1; i < count; i++)
            {
                if (_cumulative[i] >= target)
                {
                    float segment = _cumulative[i] - _cumulative[i - 1];
                    float local = segment > 0f ? (target - _cumulative[i - 1]) / segment : 0f;
                    return Vector3.Lerp(_points[i - 1], _points[i], local);
                }
            }

            return _points[count - 1];
        }

        /// <summary>Shows a settled signal (ends any transition).</summary>
        public void SetSignal(Signal signal)
        {
            if (_signal == signal && !_inTransition)
            {
                return;
            }

            _signal = signal;
            _inTransition = false;
            Apply();
        }

        /// <summary>
        /// Shows <paramref name="to"/> travelling over <paramref name="from"/>: the first <paramref name="progress"/>
        /// of the path already carries the new state. Purely visual; never changes logical values.
        /// </summary>
        public void SetTransition(Signal from, Signal to, float progress)
        {
            _signal = from;
            _transitionFrom = from;
            _transitionTo = to;
            _transitionProgress = Mathf.Clamp01(progress);
            _inTransition = true;
            Apply();
        }

        public void SetSelected(bool selected)
        {
            if (_selected == selected)
            {
                return;
            }

            _selected = selected;
            _renderer.sortingOrder = selected ? SortingOrders.SelectedWire : SortingOrders.Wire;
            Apply();
        }

        public void SetHovered(bool hovered)
        {
            if (_hovered == hovered)
            {
                return;
            }

            _hovered = hovered;
            Apply();
        }

        /// <summary>Turns this view into the wiring preview; <paramref name="valid"/> colours it accordingly.</summary>
        public void SetPreview(bool valid)
        {
            _isPreview = true;
            _previewValid = valid;
            _renderer.sortingOrder = SortingOrders.PreviewWire;
            Apply();
        }

        /// <summary>Re-applies colours and widths (after a style change).</summary>
        public void RefreshStyle()
        {
            Apply();
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }

        /// <summary>Quad half extent around each segment: room for the widest core plus the glow.</summary>
        private float Extent => _theme.WireGlowRadius + _theme.WireWidth(Signal.High) * BusWidthFactor * 1.5f;

        private void RebuildMesh()
        {
            _vertices.Clear();
            _local.Clear();
            _segment.Clear();
            _triangles.Clear();

            float e = Extent;
            for (int i = 1; i < _points.Count; i++)
            {
                Vector3 a = _points[i - 1];
                Vector3 b = _points[i];
                Vector2 delta = new Vector2(b.x - a.x, b.y - a.y);
                float length = delta.magnitude;
                if (length < 1e-5f)
                {
                    continue;
                }

                Vector2 d = delta / length;
                Vector2 n = new Vector2(-d.y, d.x);
                float start = _cumulative[i - 1];
                CollectGaps(a, b, length);
                float pieceStart = 0f;
                for (int g = 0; g <= _gaps.Count; g++)
                {
                    float pieceEnd = g < _gaps.Count ? _gaps[g].x : length;
                    if (pieceEnd - pieceStart > 1e-4f)
                    {
                        // A piece keeps the path distances of the full segment, so a transition jumps the gap.
                        Vector3 p = a + (Vector3)(d * pieceStart);
                        Vector3 q = a + (Vector3)(d * pieceEnd);
                        AddQuad(p, q, d, n, e, start + pieceStart, pieceEnd - pieceStart);
                    }

                    if (g < _gaps.Count)
                    {
                        pieceStart = _gaps[g].y;
                    }
                }
            }

            // A degenerate path (one point, or all points equal) still shows a round dot.
            if (_vertices.Count == 0 && _points.Count > 0)
            {
                Vector3 p = _points[0];
                AddVertex(p, Vector2.right, Vector2.up, -e, -e, 0f, 0f);
                AddVertex(p, Vector2.right, Vector2.up, -e, e, 0f, 0f);
                AddVertex(p, Vector2.right, Vector2.up, e, e, 0f, 0f);
                AddVertex(p, Vector2.right, Vector2.up, e, -e, 0f, 0f);
                _triangles.Add(0);
                _triangles.Add(1);
                _triangles.Add(2);
                _triangles.Add(0);
                _triangles.Add(2);
                _triangles.Add(3);
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _local);
            _mesh.SetUVs(1, _segment);
            _mesh.SetTriangles(_triangles, 0, true);
        }

        private void AddQuad(Vector3 a, Vector3 b, Vector2 d, Vector2 n, float e, float start, float length)
        {
            int baseIndex = _vertices.Count;
            AddVertex(a, d, n, -e, -e, start, length);
            AddVertex(a, d, n, -e, e, start, length);
            AddVertex(b, d, n, e, e, start, length, length);
            AddVertex(b, d, n, e, -e, start, length, length);

            _triangles.Add(baseIndex);
            _triangles.Add(baseIndex + 1);
            _triangles.Add(baseIndex + 2);
            _triangles.Add(baseIndex);
            _triangles.Add(baseIndex + 2);
            _triangles.Add(baseIndex + 3);
        }

        /// <summary>Sorted (from, to) gap intervals along the segment a-b (gap style, vertical segments only).</summary>
        private void CollectGaps(Vector3 a, Vector3 b, float length)
        {
            _gaps.Clear();
            if (_crossingStyle != WireCrossingStyle.Gap || _crossings.Count == 0 || _isPreview ||
                Mathf.Abs(b.x - a.x) > 0.02f)
            {
                return;
            }

            float half = GapHalf * HopSize;
            float direction = Mathf.Sign(b.y - a.y);
            for (int k = 0; k < _crossings.Count; k++)
            {
                float t = CrossingDistance(a, direction, length, _crossings[k], half);
                if (t < 0f)
                {
                    continue;
                }

                int index = _gaps.Count;
                _gaps.Add(new Vector2(t - half, t + half));
                while (index > 0 && _gaps[index - 1].x > _gaps[index].x)
                {
                    Vector2 swap = _gaps[index - 1];
                    _gaps[index - 1] = _gaps[index];
                    _gaps[index] = swap;
                    index--;
                }
            }
        }

        /// <param name="along">Offset along the segment direction from <paramref name="point"/>.</param>
        /// <param name="across">Offset along the segment normal.</param>
        /// <param name="localBase">Distance of <paramref name="point"/> from the segment start.</param>
        private void AddVertex(Vector3 point, Vector2 d, Vector2 n, float along, float across, float start,
            float length, float localBase = 0f)
        {
            Vector3 world = new Vector3(point.x + d.x * along + n.x * across, point.y + d.y * along + n.y * across, point.z);
            _vertices.Add(transform.InverseTransformPoint(world));
            _local.Add(new Vector2(localBase + along, across));
            _segment.Add(new Vector2(start, length));
        }

        private void Apply()
        {
            if (_renderer == null)
            {
                return;
            }

            Color colorA;
            Color colorB;
            Color front;
            float widthA;
            float widthB;
            float glowA;
            float glowB;
            float frontDistance = Settled;

            if (_isPreview)
            {
                colorA = _previewValid ? _theme.CompatiblePin : _theme.TextSecondary;
                widthA = _theme.WireWidth(Signal.High);
                glowA = _previewValid ? _theme.GlowStrength : 0f;
                colorB = colorA;
                widthB = widthA;
                glowB = glowA;
                front = colorA;
            }
            else if (_selected)
            {
                colorA = _theme.Selection;
                widthA = _style.WidthOf(_signal) * 1.5f;
                glowA = _theme.GlowStrength * HighGlowFactor;
                colorB = colorA;
                widthB = widthA;
                glowB = glowA;
                front = colorA;
            }
            else if (_inTransition)
            {
                colorA = Hover(_style.ColorOf(_transitionTo));
                colorB = Hover(_style.ColorOf(_transitionFrom));
                widthA = _style.WidthOf(_transitionTo);
                widthB = _style.WidthOf(_transitionFrom);
                glowA = GlowOf(_transitionTo);
                glowB = GlowOf(_transitionFrom);
                front = _style.FrontColorOf(_transitionTo);
                frontDistance = _transitionProgress * Length;
            }
            else
            {
                colorA = Hover(_style.ColorOf(_signal));
                widthA = _style.WidthOf(_signal);
                glowA = GlowOf(_signal);
                colorB = colorA;
                widthB = widthA;
                glowB = glowA;
                front = colorA;
            }

            if (_isBus && !_isPreview)
            {
                widthA *= BusWidthFactor;
                widthB *= BusWidthFactor;
            }

            _width = Mathf.Max(widthA, widthB);

            _block.SetColor(ColorAId, colorA);
            _block.SetColor(ColorBId, colorB);
            _block.SetColor(FrontColorId, front);
            _block.SetVector(WidthId, new Vector4(widthA * 0.5f, widthB * 0.5f, glowA, glowB));
            _block.SetVector(PathId, new Vector4(frontDistance, FrontLength, _theme.WireGlowRadius, 0f));
            _block.SetVector(JunctionId, _hasJunction && !_isPreview
                ? new Vector4(_junction.x, _junction.y, _width * 0.5f * JunctionRadiusFactor, 0f)
                : Vector4.zero);
            _renderer.SetPropertyBlock(_block);
        }

        private Color Hover(Color color)
        {
            return _hovered ? Color.Lerp(color, Color.white, 0.35f) : color;
        }

        /// <summary>Glow intensity of a state: energized (HIGH) wires glow, UNKNOWN faintly, others not.</summary>
        private float GlowOf(Signal signal)
        {
            switch (signal)
            {
                case Signal.High:
                    return _theme.GlowStrength * HighGlowFactor;
                case Signal.Unknown:
                    return _theme.GlowStrength * HighGlowFactor * 0.5f;
                default:
                    return 0f;
            }
        }
    }
}
