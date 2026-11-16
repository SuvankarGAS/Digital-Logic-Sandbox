using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering.Wires
{
    /// <summary>
    /// Maze router for settled wires: an A* search over a rectilinear grid that avoids every gate body in the search
    /// area and keeps clear of other nets' wires. Purely visual; the simulation never sees routes.
    /// <para>
    /// The grid has a line every <see cref="Step"/> world units plus one line through each stub end, so both
    /// endpoints are exact grid nodes even when a pin sits off the half-unit grid. Search states are (node, heading):
    /// a move costs its length, a bend costs <see cref="BendCost"/>, running along a lane another net already uses
    /// costs <see cref="LaneCost"/> extra per unit, and crossing another net costs a little. U-turns are not allowed.
    /// <see cref="PathCost"/> evaluates the same terms on an existing route, so routes can be compared.
    /// The heuristic is the Manhattan distance, so the result is the cheapest route under that cost.
    /// </para>
    /// <para>
    /// All buffers are static and grow only, so routing allocates nothing after warm-up. Searches are bounded: the
    /// grid is clipped to the endpoints' bounding box plus a margin, and expansions are capped. When a route cannot be
    /// found within those limits <see cref="TryRoute"/> returns false and the caller falls back to
    /// <see cref="WireRouter"/>. Single-threaded by design, like the rest of the presentation layer.
    /// </para>
    /// </summary>
    public static class GridRouter
    {
        /// <summary>Distance between grid lines (world units).</summary>
        public const float Step = 0.5f;

        /// <summary>Cost of one bend, in world units of length.</summary>
        public const float BendCost = 1.0f;

        /// <summary>Extra cost per unit of length for running in a lane another net already uses.</summary>
        public const float LaneCost = 4f;

        /// <summary>Extra cost for crossing another net's wire.</summary>
        public const float CrossingCost = 0.3f;

        /// <summary>
        /// Extra cost per unit of length along a <em>soft</em> lane: where another wire would like to run (its ideal
        /// route). Lower than <see cref="LaneCost"/>, so it only breaks ties between otherwise similar routes.
        /// </summary>
        public const float SoftLaneCost = 0.75f;

        /// <summary>Parallel wires closer than this (world units) count as sharing a lane.</summary>
        public const float LaneProximity = 0.3f;

        private const byte EdgeLane = 1;
        private const byte EdgeSoft = 2;

        private const float ReversalCost = 200f;

        /// <summary>Most grid lines per axis and grid nodes per search; larger areas fall back.</summary>
        public const int MaxLines = 400;
        public const int MaxNodes = 60000;

        /// <summary>Most states expanded per search before giving up.</summary>
        public const int MaxExpansions = 40000;

        private const byte FlagBlocked = 1;
        private const byte FlagLaneH = 2; // another net runs horizontally through this node
        private const byte FlagLaneV = 4; // another net runs vertically through this node

        private static readonly int[] Dx = { 1, 0, -1, 0 };
        private static readonly int[] Dy = { 0, 1, 0, -1 };

        private static float[] _xs = new float[64];
        private static float[] _ys = new float[64];
        private static int _nx;
        private static int _ny;

        private static byte[] _flags = new byte[1024];
        private static byte[] _laneRight = new byte[1024]; // edge node -> node + 1: EdgeLane / EdgeSoft bits
        private static byte[] _laneUp = new byte[1024]; // edge node -> node + nx: EdgeLane / EdgeSoft bits
        private static float[] _g = new float[4096];
        private static int[] _parent = new int[4096];
        private static int[] _seen = new int[4096];
        private static int[] _closed = new int[4096];
        private static int _generation;

        private static int[] _heapItems = new int[1024];
        private static float[] _heapKeys = new float[1024];
        private static int _heapCount;

        private static readonly List<Vector2> Corners = new List<Vector2>(32);

        /// <summary>Number of states expanded by the last search (for tests and profiling).</summary>
        public static int LastExpansions { get; private set; }

        /// <summary>
        /// True when other nets' lanes or crossings added cost anywhere during the last search. When false, the route
        /// would be the same with no other wires present, so it never needs improving when other wires move.
        /// </summary>
        public static bool LastLaneInfluenced { get; private set; }

        /// <summary>
        /// Routes a wire between two pins.
        /// </summary>
        /// <param name="from">Source (output) pin, world space.</param>
        /// <param name="fromNormal">Outward unit normal of the source pin (axis-aligned).</param>
        /// <param name="fromBody">World bounds of the source gate, or a zero-size rect.</param>
        /// <param name="to">Destination (input) pin.</param>
        /// <param name="toNormal">Outward unit normal of the destination pin.</param>
        /// <param name="toBody">World bounds of the destination gate, or a zero-size rect.</param>
        /// <param name="area">Search area (world); the grid is clipped to it.</param>
        /// <param name="obstacles">Bodies of other gates in the area (uninflated).</param>
        /// <param name="lanes">Axis-aligned segments of other nets' wires (x0, y0, x1, y1).</param>
        /// <param name="points">Receives the polyline, starting at <paramref name="from"/>.</param>
        /// <returns>False when no route was found within the limits; <paramref name="points"/> is then empty.</returns>
        public static bool TryRoute(Vector2 from, Vector2 fromNormal, Rect fromBody, Vector2 to, Vector2 toNormal,
            Rect toBody, Rect area, IReadOnlyList<Rect> obstacles, IReadOnlyList<Vector4> lanes, List<Vector3> points)
        {
            return TryRoute(from, fromNormal, fromBody, to, toNormal, toBody, area, obstacles, lanes, null, points);
        }

        /// <summary>
        /// As <see cref="TryRoute(Vector2, Vector2, Rect, Vector2, Vector2, Rect, Rect, IReadOnlyList{Rect}, IReadOnlyList{Vector4}, List{Vector3})"/>,
        /// with optional <paramref name="softLanes"/>: segments other wires would like to use, which cost
        /// <see cref="SoftLaneCost"/> per unit (they never set <see cref="LastLaneInfluenced"/>).
        /// </summary>
        public static bool TryRoute(Vector2 from, Vector2 fromNormal, Rect fromBody, Vector2 to, Vector2 toNormal,
            Rect toBody, Rect area, IReadOnlyList<Rect> obstacles, IReadOnlyList<Vector4> lanes,
            IReadOnlyList<Vector4> softLanes, List<Vector3> points)
        {
            points.Clear();
            LastExpansions = 0;
            LastLaneInfluenced = false;
            int fromDir = DirectionOf(fromNormal);
            int entryDir = DirectionOf(-toNormal);

            // Straight wire: pins face each other on one line with nothing in between (no gate, and no other net
            // already running along that line; the search below decides whether overlapping it is still cheapest).
            if (fromDir == entryDir && IsAligned(from, to, fromNormal) &&
                Vector2.Dot(to - from, fromNormal) >= 2f * WireRouter.Stub &&
                !StraightBlocked(from, to, fromBody, toBody, obstacles) && !StraightOnLane(from, to, lanes))
            {
                points.Add(from);
                points.Add(to);
                return true;
            }

            Vector2 a = from + Axis(fromNormal) * WireRouter.Stub;
            Vector2 b = to + Axis(toNormal) * WireRouter.Stub;
            area = Rect.MinMaxRect(Mathf.Min(area.xMin, Mathf.Min(a.x, b.x)), Mathf.Min(area.yMin, Mathf.Min(a.y, b.y)),
                Mathf.Max(area.xMax, Mathf.Max(a.x, b.x)), Mathf.Max(area.yMax, Mathf.Max(a.y, b.y)));

            if (!BuildLines(ref _xs, area.xMin, area.xMax, a.x, b.x, out _nx) ||
                !BuildLines(ref _ys, area.yMin, area.yMax, a.y, b.y, out _ny) ||
                _nx * _ny > MaxNodes)
            {
                return false;
            }

            int nodeCount = _nx * _ny;
            EnsureNodeCapacity(nodeCount);
            Array.Clear(_flags, 0, nodeCount);
            Array.Clear(_laneRight, 0, nodeCount);
            Array.Clear(_laneUp, 0, nodeCount);

            MarkBlocked(fromBody);
            MarkBlocked(toBody);
            for (int i = 0; i < obstacles.Count; i++)
            {
                MarkBlocked(obstacles[i]);
            }

            for (int i = 0; i < lanes.Count; i++)
            {
                MarkLane(lanes[i], EdgeLane);
            }

            if (softLanes != null)
            {
                for (int i = 0; i < softLanes.Count; i++)
                {
                    MarkLane(softLanes[i], EdgeSoft);
                }
            }

            int start = IndexOf(a);
            int goal = IndexOf(b);
            if (start < 0 || goal < 0)
            {
                return false;
            }

            // Stub ends are always usable, even when another gate overlaps them.
            _flags[start] &= unchecked((byte)~FlagBlocked);
            _flags[goal] &= unchecked((byte)~FlagBlocked);

            int goalState = Search(start, goal, fromDir, entryDir, b);
            if (goalState < 0)
            {
                return false;
            }

            Corners.Clear();
            for (int state = goalState; state >= 0; state = _parent[state])
            {
                int node = state >> 2;
                Corners.Add(new Vector2(_xs[node % _nx], _ys[node / _nx]));
            }

            points.Add(from);
            for (int i = Corners.Count - 1; i >= 0; i--)
            {
                points.Add(Corners[i]);
            }

            points.Add(to);
            WireRouter.Simplify(points);
            return true;
        }

        /// <summary>
        /// Cost of an existing orthogonal route under the same terms the search minimises: length, bends, length
        /// running along another net's lane (<paramref name="lanes"/>) and crossings of other nets. Used to compare a
        /// wire's current route with a freshly searched one. Allocation-free.
        /// </summary>
        public static float PathCost(IReadOnlyList<Vector3> path, IReadOnlyList<Vector4> lanes)
        {
            float cost = 0f;
            int previousDirection = -1;
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 p = path[i - 1];
                Vector2 q = path[i];
                float length = Mathf.Abs(q.x - p.x) + Mathf.Abs(q.y - p.y);
                if (length < 1e-5f)
                {
                    continue;
                }

                cost += length;
                int direction = DirectionOf(q - p);
                if (previousDirection >= 0 && direction != previousDirection)
                {
                    cost += BendCost;
                }

                previousDirection = direction;
                for (int k = 0; k < lanes.Count; k++)
                {
                    Vector4 lane = lanes[k];
                    float overlap = SegmentOverlap(p, q, new Vector2(lane.x, lane.y), new Vector2(lane.z, lane.w));
                    if (overlap > 0f)
                    {
                        cost += overlap * LaneCost;
                    }
                    else if (SegmentsCross(p, q, new Vector2(lane.x, lane.y), new Vector2(lane.z, lane.w)))
                    {
                        cost += CrossingCost;
                    }
                }
            }

            return cost;
        }

        /// <summary>Cost of an existing route along <paramref name="softLanes"/> (see <see cref="SoftLaneCost"/>).</summary>
        public static float SoftCost(IReadOnlyList<Vector3> path, IReadOnlyList<Vector4> softLanes)
        {
            float overlap = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                for (int k = 0; k < softLanes.Count; k++)
                {
                    Vector4 lane = softLanes[k];
                    overlap += SegmentOverlap(path[i - 1], path[i], new Vector2(lane.x, lane.y),
                        new Vector2(lane.z, lane.w));
                }
            }

            return overlap * SoftLaneCost;
        }

        /// <summary>Total length over which two orthogonal polylines run along the same line (allocation-free).</summary>
        public static float OverlapLength(IReadOnlyList<Vector3> a, IReadOnlyList<Vector3> b)
        {
            float total = 0f;
            for (int i = 1; i < a.Count; i++)
            {
                for (int j = 1; j < b.Count; j++)
                {
                    total += SegmentOverlap(a[i - 1], a[i], b[j - 1], b[j]);
                }
            }

            return total;
        }

        /// <summary>
        /// Length shared by two parallel axis-aligned segments on the same line, or on lines closer than
        /// <see cref="LaneProximity"/> (0 otherwise).
        /// </summary>
        private static float SegmentOverlap(Vector2 p, Vector2 q, Vector2 r, Vector2 s)
        {
            const float epsilon = 0.02f;
            bool pqHorizontal = Mathf.Abs(q.y - p.y) < epsilon;
            bool rsHorizontal = Mathf.Abs(s.y - r.y) < epsilon;
            bool pqVertical = Mathf.Abs(q.x - p.x) < epsilon;
            bool rsVertical = Mathf.Abs(s.x - r.x) < epsilon;
            if (pqHorizontal && rsHorizontal && !pqVertical && !rsVertical && Mathf.Abs(p.y - r.y) <= LaneProximity)
            {
                float lo = Mathf.Max(Mathf.Min(p.x, q.x), Mathf.Min(r.x, s.x));
                float hi = Mathf.Min(Mathf.Max(p.x, q.x), Mathf.Max(r.x, s.x));
                return Mathf.Max(hi - lo, 0f);
            }

            if (pqVertical && rsVertical && !pqHorizontal && !rsHorizontal && Mathf.Abs(p.x - r.x) <= LaneProximity)
            {
                float lo = Mathf.Max(Mathf.Min(p.y, q.y), Mathf.Min(r.y, s.y));
                float hi = Mathf.Min(Mathf.Max(p.y, q.y), Mathf.Max(r.y, s.y));
                return Mathf.Max(hi - lo, 0f);
            }

            return 0f;
        }

        /// <summary>True when a horizontal and a vertical segment intersect (endpoints included).</summary>
        private static bool SegmentsCross(Vector2 p, Vector2 q, Vector2 r, Vector2 s)
        {
            const float epsilon = 0.02f;
            bool pqHorizontal = Mathf.Abs(q.y - p.y) < epsilon;
            bool rsHorizontal = Mathf.Abs(s.y - r.y) < epsilon;
            if (pqHorizontal == rsHorizontal)
            {
                return false;
            }

            Vector2 h0 = pqHorizontal ? p : r;
            Vector2 h1 = pqHorizontal ? q : s;
            Vector2 v0 = pqHorizontal ? r : p;
            Vector2 v1 = pqHorizontal ? s : q;
            return v0.x >= Mathf.Min(h0.x, h1.x) - epsilon && v0.x <= Mathf.Max(h0.x, h1.x) + epsilon &&
                h0.y >= Mathf.Min(v0.y, v1.y) - epsilon && h0.y <= Mathf.Max(v0.y, v1.y) + epsilon;
        }

        private static int Search(int start, int goal, int fromDir, int entryDir, Vector2 goalPoint)
        {
            NextGeneration();
            _heapCount = 0;
            int startState = start * 4 + fromDir;
            _g[startState] = 0f;
            _parent[startState] = -1;
            _seen[startState] = _generation;
            Push(startState, Heuristic(start, goalPoint));

            int expansions = 0;
            while (_heapCount > 0)
            {
                int state = Pop();
                if (_closed[state] == _generation)
                {
                    continue;
                }

                _closed[state] = _generation;
                int node = state >> 2;
                if (node == goal)
                {
                    LastExpansions = expansions;
                    return state;
                }

                if (++expansions > MaxExpansions)
                {
                    LastExpansions = expansions;
                    return -1;
                }

                int heading = state & 3;
                int ix = node % _nx;
                int iy = node / _nx;
                float g = _g[state];
                for (int dir = 0; dir < 4; dir++)
                {
                    if (dir == ((heading + 2) & 3))
                    {
                        continue; // no U-turns
                    }

                    int jx = ix + Dx[dir];
                    int jy = iy + Dy[dir];
                    if (jx < 0 || jy < 0 || jx >= _nx || jy >= _ny)
                    {
                        continue;
                    }

                    int next = jy * _nx + jx;
                    if ((_flags[next] & FlagBlocked) != 0)
                    {
                        continue;
                    }

                    bool horizontal = (dir & 1) == 0;
                    float length = horizontal ? Mathf.Abs(_xs[jx] - _xs[ix]) : Mathf.Abs(_ys[jy] - _ys[iy]);
                    float cost = g + length;
                    if (dir != heading)
                    {
                        cost += BendCost;
                    }

                    byte edge = horizontal ? _laneRight[Mathf.Min(node, next)] : _laneUp[Mathf.Min(node, next)];
                    if ((edge & EdgeSoft) != 0)
                    {
                        cost += length * SoftLaneCost;
                    }

                    if ((edge & EdgeLane) != 0)
                    {
                        cost += length * LaneCost;
                        LastLaneInfluenced = true;
                    }
                    else if ((_flags[next] & (horizontal ? FlagLaneV : FlagLaneH)) != 0)
                    {
                        cost += CrossingCost;
                        LastLaneInfluenced = true;
                    }

                    if (next == goal && dir != entryDir)
                    {
                        // Entering the destination stub from the side (or backwards).
                        cost += dir == ((entryDir + 2) & 3) ? ReversalCost : BendCost;
                    }

                    int nextState = next * 4 + dir;
                    if (_closed[nextState] == _generation ||
                        (_seen[nextState] == _generation && _g[nextState] <= cost))
                    {
                        continue;
                    }

                    _seen[nextState] = _generation;
                    _g[nextState] = cost;
                    _parent[nextState] = state;
                    Push(nextState, cost + Heuristic(next, goalPoint));
                }
            }

            LastExpansions = expansions;
            return -1;
        }

        private static float Heuristic(int node, Vector2 goal)
        {
            return Mathf.Abs(_xs[node % _nx] - goal.x) + Mathf.Abs(_ys[node / _nx] - goal.y);
        }

        /// <summary>
        /// Grid lines along one axis: every multiple of <see cref="Step"/> inside [min, max], plus the two stub
        /// coordinates (multiples that nearly coincide with a stub coordinate are dropped in its favour).
        /// </summary>
        private static bool BuildLines(ref float[] lines, float min, float max, float s0, float s1, out int count)
        {
            count = 0;
            int first = Mathf.CeilToInt(min / Step);
            int last = Mathf.FloorToInt(max / Step);
            int capacity = last - first + 3;
            if (capacity > MaxLines)
            {
                return false;
            }

            if (lines.Length < capacity)
            {
                lines = new float[Mathf.NextPowerOfTwo(capacity)];
            }

            const float merge = 0.05f;
            for (int k = first; k <= last; k++)
            {
                float value = k * Step;
                if (Mathf.Abs(value - s0) >= merge && Mathf.Abs(value - s1) >= merge)
                {
                    lines[count++] = value;
                }
            }

            InsertSorted(lines, ref count, s0);
            if (Mathf.Abs(s1 - s0) > 1e-4f)
            {
                InsertSorted(lines, ref count, s1);
            }

            return true;
        }

        private static void InsertSorted(float[] lines, ref int count, float value)
        {
            int i = count++;
            while (i > 0 && lines[i - 1] > value)
            {
                lines[i] = lines[i - 1];
                i--;
            }

            lines[i] = value;
        }

        private static void MarkBlocked(Rect body)
        {
            if (body.width <= 0f || body.height <= 0f)
            {
                return;
            }

            float margin = WireRouter.BodyMargin;
            FindRange(_xs, _nx, body.xMin - margin, body.xMax + margin, out int x0, out int x1);
            FindRange(_ys, _ny, body.yMin - margin, body.yMax + margin, out int y0, out int y1);
            for (int iy = y0; iy <= y1; iy++)
            {
                int row = iy * _nx;
                for (int ix = x0; ix <= x1; ix++)
                {
                    _flags[row + ix] |= FlagBlocked;
                }
            }
        }

        /// <summary>
        /// Marks the grid lines a segment of another wire occupies. A lane also claims every parallel grid line closer
        /// than <see cref="LaneProximity"/>: grid lines are <see cref="Step"/> apart, so only an off-grid line (through a
        /// stub end) can be that close, and two wires that near look like one.
        /// </summary>
        private static void MarkLane(Vector4 segment, byte bit)
        {
            const float epsilon = 0.02f;
            float x0 = Mathf.Min(segment.x, segment.z);
            float x1 = Mathf.Max(segment.x, segment.z);
            float y0 = Mathf.Min(segment.y, segment.w);
            float y1 = Mathf.Max(segment.y, segment.w);
            if (y1 - y0 < epsilon)
            {
                FindRange(_ys, _ny, y0 - LaneProximity, y0 + LaneProximity, out int r0, out int r1, inclusive: true);
                FindRange(_xs, _nx, x0 - epsilon, x1 + epsilon, out int a, out int b, inclusive: true);
                for (int iy = r0; iy <= r1; iy++)
                {
                    for (int ix = a; ix <= b; ix++)
                    {
                        int node = iy * _nx + ix;
                        if (bit == EdgeLane)
                        {
                            _flags[node] |= FlagLaneH;
                        }

                        if (ix < b)
                        {
                            _laneRight[node] |= bit;
                        }
                    }
                }
            }
            else if (x1 - x0 < epsilon)
            {
                FindRange(_xs, _nx, x0 - LaneProximity, x0 + LaneProximity, out int c0, out int c1, inclusive: true);
                FindRange(_ys, _ny, y0 - epsilon, y1 + epsilon, out int a, out int b, inclusive: true);
                for (int ix = c0; ix <= c1; ix++)
                {
                    for (int iy = a; iy <= b; iy++)
                    {
                        int node = iy * _nx + ix;
                        if (bit == EdgeLane)
                        {
                            _flags[node] |= FlagLaneV;
                        }

                        if (iy < b)
                        {
                            _laneUp[node] |= bit;
                        }
                    }
                }
            }
        }

        /// <summary>Indices of the lines strictly inside (min, max), or inside [min, max] when inclusive.</summary>
        private static void FindRange(float[] lines, int count, float min, float max, out int first, out int last,
            bool inclusive = false)
        {
            first = LowerBound(lines, count, min);
            if (!inclusive)
            {
                while (first < count && lines[first] <= min + 1e-4f)
                {
                    first++;
                }
            }

            last = LowerBound(lines, count, max) - 1;
            if (inclusive && last + 1 < count && lines[last + 1] <= max)
            {
                last++;
            }

            if (!inclusive)
            {
                while (last >= 0 && lines[last] >= max - 1e-4f)
                {
                    last--;
                }
            }
        }

        private static int FindLine(float[] lines, int count, float value)
        {
            int i = LowerBound(lines, count, value - 0.02f);
            return i < count && Mathf.Abs(lines[i] - value) <= 0.02f ? i : -1;
        }

        /// <summary>First index whose line is at or above <paramref name="value"/>.</summary>
        private static int LowerBound(float[] lines, int count, float value)
        {
            int low = 0;
            int high = count;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (lines[mid] < value)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }

        private static int IndexOf(Vector2 point)
        {
            int ix = FindLine(_xs, _nx, point.x);
            int iy = FindLine(_ys, _ny, point.y);
            return ix < 0 || iy < 0 ? -1 : iy * _nx + ix;
        }

        private static bool StraightBlocked(Vector2 from, Vector2 to, Rect fromBody, Rect toBody,
            IReadOnlyList<Rect> obstacles)
        {
            for (int i = 0; i < obstacles.Count; i++)
            {
                Rect body = obstacles[i];
                if (WireRouter.SegmentCrossesRect(from, to, Rect.MinMaxRect(body.xMin - WireRouter.BodyMargin,
                        body.yMin - WireRouter.BodyMargin, body.xMax + WireRouter.BodyMargin,
                        body.yMax + WireRouter.BodyMargin)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool StraightOnLane(Vector2 from, Vector2 to, IReadOnlyList<Vector4> lanes)
        {
            for (int i = 0; i < lanes.Count; i++)
            {
                Vector4 lane = lanes[i];
                if (SegmentOverlap(from, to, new Vector2(lane.x, lane.y), new Vector2(lane.z, lane.w)) > 0.01f)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAligned(Vector2 from, Vector2 to, Vector2 normal)
        {
            Vector2 delta = to - from;
            return Mathf.Abs(normal.x) > 0.5f ? Mathf.Abs(delta.y) < 1e-4f : Mathf.Abs(delta.x) < 1e-4f;
        }

        private static void EnsureNodeCapacity(int nodes)
        {
            if (_flags.Length < nodes)
            {
                int size = Mathf.NextPowerOfTwo(nodes);
                _flags = new byte[size];
                _laneRight = new byte[size];
                _laneUp = new byte[size];
            }

            int states = nodes * 4;
            if (_g.Length < states)
            {
                int size = Mathf.NextPowerOfTwo(states);
                _g = new float[size];
                _parent = new int[size];
                _seen = new int[size];
                _closed = new int[size];
                _generation = 0;
            }
        }

        private static void NextGeneration()
        {
            if (++_generation == int.MaxValue)
            {
                Array.Clear(_seen, 0, _seen.Length);
                Array.Clear(_closed, 0, _closed.Length);
                _generation = 1;
            }
        }

        private static void Push(int item, float key)
        {
            if (_heapCount == _heapItems.Length)
            {
                Array.Resize(ref _heapItems, _heapItems.Length * 2);
                Array.Resize(ref _heapKeys, _heapKeys.Length * 2);
            }

            int i = _heapCount++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (_heapKeys[parent] <= key)
                {
                    break;
                }

                _heapItems[i] = _heapItems[parent];
                _heapKeys[i] = _heapKeys[parent];
                i = parent;
            }

            _heapItems[i] = item;
            _heapKeys[i] = key;
        }

        private static int Pop()
        {
            int top = _heapItems[0];
            int lastItem = _heapItems[--_heapCount];
            float lastKey = _heapKeys[_heapCount];
            int i = 0;
            while (true)
            {
                int child = i * 2 + 1;
                if (child >= _heapCount)
                {
                    break;
                }

                if (child + 1 < _heapCount && _heapKeys[child + 1] < _heapKeys[child])
                {
                    child++;
                }

                if (_heapKeys[child] >= lastKey)
                {
                    break;
                }

                _heapItems[i] = _heapItems[child];
                _heapKeys[i] = _heapKeys[child];
                i = child;
            }

            if (_heapCount > 0)
            {
                _heapItems[i] = lastItem;
                _heapKeys[i] = lastKey;
            }

            return top;
        }

        private static int DirectionOf(Vector2 v)
        {
            Vector2 axis = Axis(v);
            if (axis.x > 0.5f)
            {
                return 0;
            }

            if (axis.y > 0.5f)
            {
                return 1;
            }

            return axis.x < -0.5f ? 2 : 3;
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
