using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.GateLibrary.Examples
{
    /// <summary>Vertical offset of a pin from its node's centre (world units, up is positive).</summary>
    public delegate float PinOffset(NodeDefinition node, PortId port);

    /// <summary>
    /// Arranges a circuit into left-to-right columns so it reads like a hand-drawn schematic (a layered,
    /// Sugiyama-style layout). Used for the generated example gates and by the editor's "Tidy layout" command.
    /// Purely cosmetic: simulation never depends on positions.
    /// <list type="number">
    /// <item>Columns: input boundaries first, output boundaries last, every other node right of its predecessors
    /// (feedback edges are ignored). Nodes with more outgoing than incoming wires (constants, splitters) then move
    /// right, next to their users, and the opposite kind moves left.</item>
    /// <item>Wires spanning several columns get a virtual node in each column they cross (shared per source pin),
    /// so they reserve a lane and take part in ordering.</item>
    /// <item>Order within a column: alternating barycenter sweeps on pin heights plus adjacent swaps, keeping the
    /// order with the fewest crossings. Boundary columns keep their interface order.</item>
    /// <item>Heights: nodes move towards the median height of the pins they connect to, with the column order and
    /// spacing kept by an isotonic (pool-adjacent-violators) fit, so chains end up straight.</item>
    /// <item>Column gaps grow with the number of wires running between the columns.</item>
    /// </list>
    /// Deterministic: the same circuit always gets the same positions.
    /// </summary>
    public static class LayeredLayout
    {
        /// <summary>Smallest clear space between columns (room for wire lanes).</summary>
        public const float ColumnGap = 3.5f;

        /// <summary>Largest clear space between columns, however many wires pass.</summary>
        public const float MaxColumnGap = 7f;

        /// <summary>Clear space between nodes in one column.</summary>
        public const float RowGap = 1f;

        /// <summary>Clear space between a wire lane and a node or another lane.</summary>
        public const float LaneGap = 0.5f;

        /// <summary>Positions are snapped to this grid.</summary>
        public const float Snap = 0.5f;

        /// <summary>Distance between neighbouring pins on a gate (matches the renderer's pin spacing).</summary>
        public const float PinPitch = 1f;

        private const int OrderingSweeps = 24;
        private const int StaleSweepLimit = 6;
        private const int TransposeLayerLimit = 160;
        private const int CoordinateRounds = 10;
        private const float VirtualWeight = 4f;

        /// <summary>Moves every node of <paramref name="circuit"/> to its computed position.</summary>
        public static void Arrange(CircuitDefinition circuit, Func<NodeDefinition, CircuitPoint> sizeOf,
            PinOffset pinOffset = null)
        {
            CircuitPoint[] positions = ComputePositions(circuit, sizeOf, pinOffset);
            IReadOnlyList<NodeDefinition> nodes = circuit.Nodes;
            NodeId[] ids = new NodeId[nodes.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = nodes[i].Id;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                circuit.MoveNode(ids[i], positions[i]);
            }
        }

        /// <summary>
        /// Computes a position for every node of <paramref name="circuit"/> (indexed like <c>circuit.Nodes</c>)
        /// without changing the circuit. <paramref name="sizeOf"/> returns a node's body size (width, height);
        /// <paramref name="pinOffset"/> a pin's height relative to the node centre (null: every pin at the centre).
        /// </summary>
        public static CircuitPoint[] ComputePositions(CircuitDefinition circuit, Func<NodeDefinition, CircuitPoint> sizeOf,
            PinOffset pinOffset = null)
        {
            if (circuit == null)
            {
                throw new ArgumentNullException(nameof(circuit));
            }

            if (sizeOf == null)
            {
                throw new ArgumentNullException(nameof(sizeOf));
            }

            var layout = new Graph(circuit, sizeOf, pinOffset ?? ((node, port) => 0f));
            return layout.Run();
        }

        /// <summary>
        /// Pin offsets for unrotated nodes, from their signatures: pins are <see cref="PinPitch"/> apart and centred,
        /// the first on top; single-pin utilities (boundaries, clocks, constants) have it at the centre. This is the
        /// renderer's rule, restated here so layout code needs no rendering dependency.
        /// </summary>
        public static PinOffset SignaturePins(ComponentCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            return (node, port) =>
            {
                if (!catalog.TryGetSignature(node.Component, out ComponentSignature signature))
                {
                    return 0f;
                }

                ComponentKind kind = node.Component.Kind;
                if (node.IsBoundary || kind == ComponentKind.Clock || kind == ComponentKind.Constant)
                {
                    return 0f;
                }

                float offset;
                if (TryPinOffset(signature.Inputs, port, out offset) || TryPinOffset(signature.Outputs, port, out offset))
                {
                    return offset;
                }

                return 0f;
            };
        }

        private static bool TryPinOffset(IReadOnlyList<PortDescriptor> ports, PortId port, out float offset)
        {
            for (int i = 0; i < ports.Count; i++)
            {
                if (ports[i].Id == port)
                {
                    offset = ((ports.Count - 1) * 0.5f - i) * PinPitch;
                    return true;
                }
            }

            offset = 0f;
            return false;
        }

        /// <summary>A wire piece between two neighbouring columns.</summary>
        private struct Segment
        {
            public int Upper;       // node in column c
            public int Lower;       // node in column c + 1
            public float UpperPin;  // pin height relative to the upper node's centre
            public float LowerPin;
        }

        /// <summary>The layout state of one run: real nodes first, then virtual (lane) nodes.</summary>
        private sealed class Graph
        {
            private readonly CircuitDefinition _circuit;
            private readonly IReadOnlyList<NodeDefinition> _nodes;
            private readonly int _real;
            private readonly List<int> _layer = new List<int>();
            private readonly List<float> _width = new List<float>();
            private readonly List<float> _height = new List<float>();
            private readonly List<Segment> _segments = new List<Segment>();
            private readonly List<List<int>> _down = new List<List<int>>(); // segment indices where the node is upper
            private readonly List<List<int>> _up = new List<List<int>>();   // segment indices where the node is lower
            private readonly Func<NodeDefinition, CircuitPoint> _sizeOf;
            private readonly PinOffset _pinOffset;

            private List<int>[] _columns;
            private List<int>[] _segmentsAfter; // segments between column c and c + 1
            private bool[] _fixed;              // boundary-only columns keep their interface order
            private float[] _y;
            private bool _hasInputs;

            public Graph(CircuitDefinition circuit, Func<NodeDefinition, CircuitPoint> sizeOf, PinOffset pinOffset)
            {
                _circuit = circuit;
                _nodes = circuit.Nodes;
                _real = _nodes.Count;
                _sizeOf = sizeOf;
                _pinOffset = pinOffset;
            }

            public CircuitPoint[] Run()
            {
                var result = new CircuitPoint[_real];
                if (_real == 0)
                {
                    return result;
                }

                for (int i = 0; i < _real; i++)
                {
                    CircuitPoint size = _sizeOf(_nodes[i]);
                    _width.Add(size.X);
                    _height.Add(size.Y);
                    _layer.Add(0);
                    _down.Add(new List<int>(2));
                    _up.Add(new List<int>(2));
                    if (_nodes[i].Component.Kind == ComponentKind.InputBoundary)
                    {
                        _hasInputs = true;
                    }
                }

                List<(int from, int to, PortId fromPort, PortId toPort)> forward = Layer();
                BuildSegments(forward);
                BuildColumns();
                OrderColumns();
                AssignHeights();
                float[] x = ColumnPositions();

                for (int i = 0; i < _real; i++)
                {
                    result[i] = new CircuitPoint(SnapValue(x[_layer[i]]), _y[i]);
                }

                return result;
            }

            // ------------------------------------------------------------------------------------------------
            // 1. Columns
            // ------------------------------------------------------------------------------------------------

            private List<(int from, int to, PortId fromPort, PortId toPort)> Layer()
            {
                var index = new Dictionary<NodeId, int>(_real);
                for (int i = 0; i < _real; i++)
                {
                    index[_nodes[i].Id] = i;
                }

                var successors = new List<int>[_real];
                var predecessors = new List<int>[_real];
                for (int i = 0; i < _real; i++)
                {
                    successors[i] = new List<int>(2);
                    predecessors[i] = new List<int>(2);
                }

                var edges = new List<(int from, int to, PortId fromPort, PortId toPort)>();
                IReadOnlyList<ConnectionDefinition> connections = _circuit.Connections;
                for (int i = 0; i < connections.Count; i++)
                {
                    if (index.TryGetValue(connections[i].Source.Node, out int from) &&
                        index.TryGetValue(connections[i].Destination.Node, out int to) && from != to)
                    {
                        successors[from].Add(to);
                        predecessors[to].Add(from);
                        edges.Add((from, to, connections[i].Source.Port, connections[i].Destination.Port));
                    }
                }

                HashSet<long> back = FindBackEdges(successors);
                var forwardSucc = new List<int>[_real];
                var forwardPred = new List<int>[_real];
                for (int i = 0; i < _real; i++)
                {
                    forwardSucc[i] = new List<int>(successors[i].Count);
                    forwardPred[i] = new List<int>(predecessors[i].Count);
                }

                var forward = new List<(int from, int to, PortId fromPort, PortId toPort)>(edges.Count);
                for (int i = 0; i < edges.Count; i++)
                {
                    if (!back.Contains(Key(edges[i].from, edges[i].to)))
                    {
                        forwardSucc[edges[i].from].Add(edges[i].to);
                        forwardPred[edges[i].to].Add(edges[i].from);
                        forward.Add(edges[i]);
                    }
                }

                List<int> topological = LongestPath(forwardSucc, forwardPred);
                Balance(topological, forwardSucc, forwardPred);
                return forward;
            }

            private static long Key(int from, int to)
            {
                return ((long)from << 32) | (uint)to;
            }

            private bool IsSource(int node)
            {
                ComponentKind kind = _nodes[node].Component.Kind;
                return kind == ComponentKind.InputBoundary || kind == ComponentKind.Clock || kind == ComponentKind.Constant;
            }

            /// <summary>
            /// Depth-first search from the sources (in model order), then from any unvisited remainder. An edge into a
            /// node still on the stack closes a loop and is a back edge.
            /// </summary>
            private HashSet<long> FindBackEdges(List<int>[] successors)
            {
                var state = new byte[_real]; // 0 new, 1 on stack, 2 done
                var back = new HashSet<long>();
                var stack = new Stack<(int node, int next)>();
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int start = 0; start < _real; start++)
                    {
                        if (state[start] != 0 || (pass == 0 && !IsSource(start)))
                        {
                            continue;
                        }

                        stack.Push((start, 0));
                        state[start] = 1;
                        while (stack.Count > 0)
                        {
                            (int node, int next) = stack.Pop();
                            if (next < successors[node].Count)
                            {
                                stack.Push((node, next + 1));
                                int to = successors[node][next];
                                if (state[to] == 1)
                                {
                                    back.Add(Key(node, to));
                                }
                                else if (state[to] == 0)
                                {
                                    state[to] = 1;
                                    stack.Push((to, 0));
                                }
                            }
                            else
                            {
                                state[node] = 2;
                            }
                        }
                    }
                }

                return back;
            }

            /// <summary>Longest-path columns over the forward edges; returns a topological order.</summary>
            private List<int> LongestPath(List<int>[] successors, List<int>[] predecessors)
            {
                var remaining = new int[_real];
                for (int i = 0; i < _real; i++)
                {
                    remaining[i] = predecessors[i].Count;
                }

                var order = new List<int>(_real);
                var queue = new Queue<int>();
                for (int i = 0; i < _real; i++)
                {
                    // Gates never share the input column: other sources (constants, clocks) start one column right,
                    // so whatever they feed lands right of them.
                    bool input = _nodes[i].Component.Kind == ComponentKind.InputBoundary;
                    _layer[i] = _hasInputs && !input ? 1 : 0;
                }

                for (int i = 0; i < _real; i++)
                {
                    if (remaining[i] == 0)
                    {
                        queue.Enqueue(i);
                    }
                }

                while (queue.Count > 0)
                {
                    int node = queue.Dequeue();
                    order.Add(node);
                    for (int s = 0; s < successors[node].Count; s++)
                    {
                        int to = successors[node][s];
                        _layer[to] = Math.Max(_layer[to], _layer[node] + 1);
                        if (--remaining[to] == 0)
                        {
                            queue.Enqueue(to);
                        }
                    }
                }

                int inner = 0;
                for (int i = 0; i < _real; i++)
                {
                    if (_nodes[i].Component.Kind == ComponentKind.InputBoundary)
                    {
                        _layer[i] = 0;
                    }
                    else if (_nodes[i].Component.Kind != ComponentKind.OutputBoundary)
                    {
                        inner = Math.Max(inner, _layer[i]);
                    }
                }

                for (int i = 0; i < _real; i++)
                {
                    if (_nodes[i].Component.Kind == ComponentKind.OutputBoundary)
                    {
                        _layer[i] = inner + 1;
                    }
                }

                return order;
            }

            /// <summary>
            /// Moves each non-boundary node within the columns its neighbours allow, towards the side it has more
            /// wires to: constants and splitters end up next to their users, which shortens the long wires.
            /// </summary>
            private void Balance(List<int> topological, List<int>[] successors, List<int>[] predecessors)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int k = 0; k < topological.Count; k++)
                    {
                        int node = pass == 0 ? topological[topological.Count - 1 - k] : topological[k];
                        if (_nodes[node].IsBoundary || successors[node].Count == 0)
                        {
                            continue;
                        }

                        int low = _hasInputs ? 1 : 0;
                        for (int p = 0; p < predecessors[node].Count; p++)
                        {
                            low = Math.Max(low, _layer[predecessors[node][p]] + 1);
                        }

                        int high = int.MaxValue;
                        for (int s = 0; s < successors[node].Count; s++)
                        {
                            high = Math.Min(high, _layer[successors[node][s]] - 1);
                        }

                        if (high < low)
                        {
                            continue;
                        }

                        int outgoing = successors[node].Count;
                        int incoming = predecessors[node].Count;
                        if (outgoing > incoming)
                        {
                            _layer[node] = high;
                        }
                        else if (incoming > outgoing)
                        {
                            _layer[node] = low;
                        }
                    }
                }
            }

            // ------------------------------------------------------------------------------------------------
            // 2. Segments and virtual nodes
            // ------------------------------------------------------------------------------------------------

            private void BuildSegments(List<(int from, int to, PortId fromPort, PortId toPort)> forward)
            {
                // Group the wires by source pin: one lane chain per pin, branching off where each wire ends.
                var groups = new Dictionary<long, List<int>>();
                var groupOrder = new List<long>();
                var pinIds = new Dictionary<long, PortId>();
                var portNumbers = new Dictionary<PortId, int>();
                for (int i = 0; i < forward.Count; i++)
                {
                    if (!portNumbers.TryGetValue(forward[i].fromPort, out int portNumber))
                    {
                        portNumber = portNumbers.Count;
                        portNumbers[forward[i].fromPort] = portNumber;
                    }

                    long key = Key(forward[i].from, portNumber);
                    if (!groups.TryGetValue(key, out List<int> list))
                    {
                        list = new List<int>(2);
                        groups[key] = list;
                        groupOrder.Add(key);
                        pinIds[key] = forward[i].fromPort;
                    }

                    list.Add(i);
                }

                for (int g = 0; g < groupOrder.Count; g++)
                {
                    List<int> wires = groups[groupOrder[g]];
                    wires.Sort((a, b) =>
                    {
                        int byLayer = _layer[forward[a].to].CompareTo(_layer[forward[b].to]);
                        return byLayer != 0 ? byLayer : a.CompareTo(b);
                    });

                    int source = forward[wires[0]].from;
                    int previous = source;
                    float previousPin = _pinOffset(_nodes[source], pinIds[groupOrder[g]]);
                    int column = _layer[source];
                    for (int w = 0; w < wires.Count; w++)
                    {
                        int target = forward[wires[w]].to;
                        int targetColumn = _layer[target];
                        if (targetColumn <= _layer[source])
                        {
                            continue; // cannot happen for forward edges; ignore defensively
                        }

                        while (column < targetColumn - 1)
                        {
                            int lane = AddVirtual(column + 1);
                            AddSegment(previous, lane, previousPin, 0f);
                            previous = lane;
                            previousPin = 0f;
                            column++;
                        }

                        // Wires are visited by target column, so the chain's tail (or the source) is now exactly one
                        // column before the target and feeds it; later, further wires extend the same chain.
                        AddSegment(previous, target, previousPin, _pinOffset(_nodes[target], forward[wires[w]].toPort));
                    }
                }
            }

            private int AddVirtual(int column)
            {
                _layer.Add(column);
                _width.Add(0f);
                _height.Add(0f);
                _down.Add(new List<int>(2));
                _up.Add(new List<int>(1));
                return _layer.Count - 1;
            }

            private void AddSegment(int upper, int lower, float upperPin, float lowerPin)
            {
                _segments.Add(new Segment { Upper = upper, Lower = lower, UpperPin = upperPin, LowerPin = lowerPin });
                int index = _segments.Count - 1;
                _down[upper].Add(index);
                _up[lower].Add(index);
            }

            private bool IsVirtual(int node)
            {
                return node >= _real;
            }

            // ------------------------------------------------------------------------------------------------
            // 3. Order within columns
            // ------------------------------------------------------------------------------------------------

            private void BuildColumns()
            {
                int last = 0;
                for (int i = 0; i < _layer.Count; i++)
                {
                    last = Math.Max(last, _layer[i]);
                }

                _columns = new List<int>[last + 1];
                _segmentsAfter = new List<int>[last + 1];
                _fixed = new bool[last + 1];
                for (int c = 0; c <= last; c++)
                {
                    _columns[c] = new List<int>();
                    _segmentsAfter[c] = new List<int>();
                    _fixed[c] = true;
                }

                // Model order first (boundaries in interface order), lanes after the real nodes.
                for (int i = 0; i < _layer.Count; i++)
                {
                    int c = _layer[i];
                    _columns[c].Add(i);
                    if (IsVirtual(i) || !_nodes[i].IsBoundary)
                    {
                        _fixed[c] = false;
                    }
                }

                for (int s = 0; s < _segments.Count; s++)
                {
                    _segmentsAfter[_layer[_segments[s].Upper]].Add(s);
                }

                _y = new float[_layer.Count];
            }

            private void OrderColumns()
            {
                int count = _columns.Length;
                for (int c = 0; c < count; c++)
                {
                    StackColumn(c);
                }

                // Lanes start next to the wire they carry rather than below every real node.
                for (int c = 1; c < count; c++)
                {
                    if (!_fixed[c])
                    {
                        SortByBarycenter(c, true);
                    }
                }

                var best = new int[count][];
                SaveOrder(best);
                long bestCrossings = TotalCrossings();
                int stale = 0;
                for (int sweep = 0; sweep < OrderingSweeps && bestCrossings > 0; sweep++)
                {
                    bool down = sweep % 2 == 0;
                    for (int k = 0; k < count; k++)
                    {
                        int c = down ? k : count - 1 - k;
                        if (_fixed[c] || (down && c == 0) || (!down && c == count - 1))
                        {
                            continue;
                        }

                        SortByBarycenter(c, down);
                    }

                    Transpose();
                    long crossings = TotalCrossings();
                    if (crossings < bestCrossings)
                    {
                        bestCrossings = crossings;
                        SaveOrder(best);
                        stale = 0;
                    }
                    else if (++stale >= StaleSweepLimit)
                    {
                        break;
                    }
                }

                for (int c = 0; c < count; c++)
                {
                    _columns[c].Clear();
                    _columns[c].AddRange(best[c]);
                    StackColumn(c);
                }
            }

            private void SaveOrder(int[][] into)
            {
                for (int c = 0; c < _columns.Length; c++)
                {
                    into[c] = _columns[c].ToArray();
                }
            }

            private float HalfHeight(int node)
            {
                return _height[node] * 0.5f;
            }

            /// <summary>Required distance between the centres of two vertically neighbouring nodes.</summary>
            private float Separation(int upper, int lower)
            {
                float gap = IsVirtual(upper) || IsVirtual(lower) ? LaneGap : RowGap;
                return HalfHeight(upper) + gap + HalfHeight(lower);
            }

            /// <summary>Packs a column top-down at minimum spacing (relative heights for ordering).</summary>
            private void StackColumn(int c)
            {
                List<int> column = _columns[c];
                float y = 0f;
                for (int k = 0; k < column.Count; k++)
                {
                    if (k > 0)
                    {
                        y -= Separation(column[k - 1], column[k]);
                    }

                    _y[column[k]] = y;
                }
            }

            private void SortByBarycenter(int c, bool usePredecessors)
            {
                List<int> column = _columns[c];
                int n = column.Count;
                var keys = new float[n];
                var order = new int[n];
                for (int k = 0; k < n; k++)
                {
                    int node = column[k];
                    order[k] = k;
                    List<int> segs = usePredecessors ? _up[node] : _down[node];
                    if (segs.Count == 0)
                    {
                        keys[k] = _y[node];
                        continue;
                    }

                    float sum = 0f;
                    for (int s = 0; s < segs.Count; s++)
                    {
                        Segment segment = _segments[segs[s]];
                        sum += usePredecessors
                            ? _y[segment.Upper] + segment.UpperPin - segment.LowerPin
                            : _y[segment.Lower] + segment.LowerPin - segment.UpperPin;
                    }

                    keys[k] = sum / segs.Count;
                }

                Array.Sort(order, (a, b) =>
                {
                    int byKey = keys[b].CompareTo(keys[a]); // higher first (top of the screen)
                    return byKey != 0 ? byKey : a.CompareTo(b);
                });

                var sorted = new int[n];
                for (int k = 0; k < n; k++)
                {
                    sorted[k] = column[order[k]];
                }

                column.Clear();
                column.AddRange(sorted);
                StackColumn(c);
            }

            /// <summary>Swaps neighbours in each column while that lowers the crossings on both sides.</summary>
            private void Transpose()
            {
                for (int c = 0; c < _columns.Length; c++)
                {
                    List<int> column = _columns[c];
                    if (_fixed[c] || column.Count < 2 || column.Count > TransposeLayerLimit)
                    {
                        continue;
                    }

                    for (int round = 0; round < 4; round++)
                    {
                        bool improved = false;
                        for (int k = 0; k + 1 < column.Count; k++)
                        {
                            long before = CrossingsAround(c);
                            Swap(column, k);
                            StackColumn(c);
                            long after = CrossingsAround(c);
                            if (after < before)
                            {
                                improved = true;
                            }
                            else
                            {
                                Swap(column, k);
                                StackColumn(c);
                            }
                        }

                        if (!improved)
                        {
                            break;
                        }
                    }
                }
            }

            private static void Swap(List<int> column, int k)
            {
                int t = column[k];
                column[k] = column[k + 1];
                column[k + 1] = t;
            }

            private long CrossingsAround(int c)
            {
                long total = Crossings(c);
                if (c > 0)
                {
                    total += Crossings(c - 1);
                }

                return total;
            }

            private long TotalCrossings()
            {
                long total = 0;
                for (int c = 0; c + 1 < _columns.Length; c++)
                {
                    total += Crossings(c);
                }

                return total;
            }

            private float[] _upperKeys = Array.Empty<float>();
            private float[] _lowerKeys = Array.Empty<float>();
            private int[] _sortIndex = Array.Empty<int>();
            private float[] _mergeBuffer = Array.Empty<float>();

            /// <summary>Crossings between column <paramref name="c"/> and the next, from pin heights.</summary>
            private long Crossings(int c)
            {
                List<int> segs = _segmentsAfter[c];
                int n = segs.Count;
                if (n < 2)
                {
                    return 0;
                }

                if (_upperKeys.Length < n)
                {
                    _upperKeys = new float[n * 2];
                    _lowerKeys = new float[n * 2];
                    _sortIndex = new int[n * 2];
                    _mergeBuffer = new float[n * 2];
                }

                for (int i = 0; i < n; i++)
                {
                    Segment segment = _segments[segs[i]];
                    _upperKeys[i] = _y[segment.Upper] + segment.UpperPin;
                    _lowerKeys[i] = _y[segment.Lower] + segment.LowerPin;
                    _sortIndex[i] = i;
                }

                float[] upper = _upperKeys;
                float[] lower = _lowerKeys;
                Array.Sort(_sortIndex, 0, n, Comparer<int>.Create((a, b) =>
                {
                    int byUpper = upper[b].CompareTo(upper[a]);
                    return byUpper != 0 ? byUpper : lower[b].CompareTo(lower[a]);
                }));

                // Two wires cross when the one leaving higher arrives strictly lower: count pairs i < j with
                // lower[j] > lower[i] in this order (merge-sort inversion count).
                var sequence = new float[n];
                for (int i = 0; i < n; i++)
                {
                    sequence[i] = lower[_sortIndex[i]];
                }

                return CountRisingPairs(sequence, 0, n);
            }

            /// <summary>Counts pairs i &lt; j with a[j] &gt; a[i]; sorts the range descending.</summary>
            private long CountRisingPairs(float[] a, int start, int end)
            {
                int length = end - start;
                if (length < 2)
                {
                    return 0;
                }

                int middle = start + length / 2;
                long count = CountRisingPairs(a, start, middle) + CountRisingPairs(a, middle, end);
                int i = start;
                int j = middle;
                int k = 0;
                while (i < middle && j < end)
                {
                    if (a[j] > a[i])
                    {
                        // a[j] beats every remaining left element (left half is descending from i on).
                        count += middle - i;
                        _mergeBuffer[k++] = a[j++];
                    }
                    else
                    {
                        _mergeBuffer[k++] = a[i++];
                    }
                }

                while (i < middle)
                {
                    _mergeBuffer[k++] = a[i++];
                }

                while (j < end)
                {
                    _mergeBuffer[k++] = a[j++];
                }

                Array.Copy(_mergeBuffer, 0, a, start, length);
                return count;
            }

            // ------------------------------------------------------------------------------------------------
            // 4. Heights
            // ------------------------------------------------------------------------------------------------

            private void AssignHeights()
            {
                for (int c = 0; c < _columns.Length; c++)
                {
                    StackColumn(c);
                    Centre(c);
                }

                for (int round = 0; round < CoordinateRounds; round++)
                {
                    // Alternate predecessor and successor passes, finishing with passes that use both sides.
                    int mode = round >= CoordinateRounds - 2 ? 2 : round % 2;
                    bool down = mode != 1;
                    for (int k = 0; k < _columns.Length; k++)
                    {
                        int c = down ? k : _columns.Length - 1 - k;
                        FitColumn(c, mode);
                    }
                }

                for (int c = 0; c < _columns.Length; c++)
                {
                    SnapColumn(c);
                }
            }

            private void Centre(int c)
            {
                List<int> column = _columns[c];
                if (column.Count == 0)
                {
                    return;
                }

                float top = _y[column[0]] + HalfHeight(column[0]);
                float bottom = _y[column[column.Count - 1]] - HalfHeight(column[column.Count - 1]);
                float shift = -(top + bottom) * 0.5f;
                for (int k = 0; k < column.Count; k++)
                {
                    _y[column[k]] += shift;
                }
            }

            private readonly List<float> _wants = new List<float>();

            /// <summary>
            /// Moves the column's nodes towards the median height their wires ask for (mode 0: predecessors,
            /// 1: successors, 2: both) while keeping their order and spacing: an isotonic fit by pooling adjacent
            /// violators.
            /// </summary>
            private void FitColumn(int c, int mode)
            {
                List<int> column = _columns[c];
                int n = column.Count;
                if (n == 0)
                {
                    return;
                }

                // z = y + offset turns "y[k] <= y[k-1] - separation" into "z[k] <= z[k-1]".
                var offset = new float[n];
                var target = new float[n];
                var weight = new float[n];
                for (int k = 0; k < n; k++)
                {
                    int node = column[k];
                    offset[k] = k == 0 ? 0f : offset[k - 1] + Separation(column[k - 1], node);
                    float want = Desired(node, mode, out int neighbours);
                    target[k] = want + offset[k];
                    weight[k] = neighbours == 0 ? 0.01f : neighbours * (IsVirtual(node) ? VirtualWeight : 1f);
                }

                // Pool adjacent violators for a non-increasing sequence.
                var blockValue = new float[n];
                var blockWeight = new float[n];
                var blockEnd = new int[n];
                int blocks = 0;
                for (int k = 0; k < n; k++)
                {
                    blockValue[blocks] = target[k];
                    blockWeight[blocks] = weight[k];
                    blockEnd[blocks] = k;
                    blocks++;
                    while (blocks > 1 && blockValue[blocks - 1] > blockValue[blocks - 2])
                    {
                        float w = blockWeight[blocks - 2] + blockWeight[blocks - 1];
                        blockValue[blocks - 2] = (blockValue[blocks - 2] * blockWeight[blocks - 2] +
                                                  blockValue[blocks - 1] * blockWeight[blocks - 1]) / w;
                        blockWeight[blocks - 2] = w;
                        blockEnd[blocks - 2] = blockEnd[blocks - 1];
                        blocks--;
                    }
                }

                int start = 0;
                for (int b = 0; b < blocks; b++)
                {
                    for (int k = start; k <= blockEnd[b]; k++)
                    {
                        _y[column[k]] = blockValue[b] - offset[k];
                    }

                    start = blockEnd[b] + 1;
                }
            }

            /// <summary>Median height the node's wires ask for, or its current height when it has none.</summary>
            private float Desired(int node, int mode, out int neighbours)
            {
                _wants.Clear();
                if (mode != 1)
                {
                    List<int> up = _up[node];
                    for (int s = 0; s < up.Count; s++)
                    {
                        Segment segment = _segments[up[s]];
                        _wants.Add(_y[segment.Upper] + segment.UpperPin - segment.LowerPin);
                    }
                }

                if (mode != 0)
                {
                    List<int> down = _down[node];
                    for (int s = 0; s < down.Count; s++)
                    {
                        Segment segment = _segments[down[s]];
                        _wants.Add(_y[segment.Lower] + segment.LowerPin - segment.UpperPin);
                    }
                }

                neighbours = _wants.Count;
                if (neighbours == 0)
                {
                    return _y[node];
                }

                _wants.Sort();
                int middle = neighbours / 2;
                return neighbours % 2 == 1 ? _wants[middle] : (_wants[middle - 1] + _wants[middle]) * 0.5f;
            }

            /// <summary>Snaps heights to the grid, then pushes nodes down where snapping made them too close.</summary>
            private void SnapColumn(int c)
            {
                List<int> column = _columns[c];
                for (int k = 0; k < column.Count; k++)
                {
                    float y = SnapValue(_y[column[k]]);
                    if (k > 0)
                    {
                        float limit = _y[column[k - 1]] - CeilToSnap(Separation(column[k - 1], column[k]));
                        y = Math.Min(y, limit);
                    }

                    _y[column[k]] = y;
                }
            }

            // ------------------------------------------------------------------------------------------------
            // 5. Column positions
            // ------------------------------------------------------------------------------------------------

            private float[] ColumnPositions()
            {
                int count = _columns.Length;
                var x = new float[count];
                var halfWidth = new float[count];
                for (int c = 0; c < count; c++)
                {
                    for (int k = 0; k < _columns[c].Count; k++)
                    {
                        halfWidth[c] = Math.Max(halfWidth[c], _width[_columns[c][k]] * 0.5f);
                    }
                }

                var lanes = new HashSet<long>();
                for (int c = 1; c < count; c++)
                {
                    // Wires that change height between the columns need a vertical lane each (one per source pin).
                    lanes.Clear();
                    List<int> segs = _segmentsAfter[c - 1];
                    for (int s = 0; s < segs.Count; s++)
                    {
                        Segment segment = _segments[segs[s]];
                        float from = _y[segment.Upper] + segment.UpperPin;
                        float to = _y[segment.Lower] + segment.LowerPin;
                        if (Math.Abs(from - to) > 0.01f)
                        {
                            lanes.Add(Key(segment.Upper, (int)Math.Round(segment.UpperPin * 4f) + 1024));
                        }
                    }

                    float gap = Math.Min(MaxColumnGap, Math.Max(ColumnGap, 1.5f + lanes.Count * 0.25f));
                    x[c] = x[c - 1] + halfWidth[c - 1] + gap + halfWidth[c];
                }

                return x;
            }
        }

        private static float SnapValue(float value)
        {
            return (float)Math.Round(value / Snap) * Snap;
        }

        private static float CeilToSnap(float value)
        {
            return (float)Math.Ceiling(value / Snap - 1e-4f) * Snap;
        }
    }
}
