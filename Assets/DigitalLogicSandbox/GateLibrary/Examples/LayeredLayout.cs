using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;

namespace DigitalLogicSandbox.GateLibrary.Examples
{
    /// <summary>
    /// Arranges a generated circuit into left-to-right columns so it reads like a hand-drawn schematic when opened.
    /// Input boundaries form the first column, output boundaries the last, and every other node sits one column
    /// right of its furthest predecessor (feedback edges are ignored). Within a column nodes are placed near the
    /// average height of their predecessors and pushed apart so bodies never overlap. Purely cosmetic: simulation
    /// never depends on positions.
    /// </summary>
    public static class LayeredLayout
    {
        /// <summary>Clear space between columns (room for wire lanes).</summary>
        public const float ColumnGap = 3.5f;

        /// <summary>Clear space between nodes in one column.</summary>
        public const float RowGap = 1f;

        /// <summary>Positions are snapped to this grid.</summary>
        public const float Snap = 0.5f;

        /// <summary>
        /// Moves every node of <paramref name="circuit"/>. <paramref name="sizeOf"/> returns a node's body size
        /// (width, height) in world units.
        /// </summary>
        public static void Arrange(CircuitDefinition circuit, Func<NodeDefinition, CircuitPoint> sizeOf)
        {
            if (circuit == null)
            {
                throw new ArgumentNullException(nameof(circuit));
            }

            if (sizeOf == null)
            {
                throw new ArgumentNullException(nameof(sizeOf));
            }

            IReadOnlyList<NodeDefinition> nodes = circuit.Nodes;
            int count = nodes.Count;
            if (count == 0)
            {
                return;
            }

            var index = new Dictionary<NodeId, int>(count);
            for (int i = 0; i < count; i++)
            {
                index[nodes[i].Id] = i;
            }

            // Node-level edges (one per connection; duplicates between the same pair are harmless).
            var successors = new List<int>[count];
            var predecessors = new List<int>[count];
            for (int i = 0; i < count; i++)
            {
                successors[i] = new List<int>(2);
                predecessors[i] = new List<int>(2);
            }

            IReadOnlyList<ConnectionDefinition> connections = circuit.Connections;
            for (int i = 0; i < connections.Count; i++)
            {
                if (index.TryGetValue(connections[i].Source.Node, out int from) &&
                    index.TryGetValue(connections[i].Destination.Node, out int to) && from != to)
                {
                    successors[from].Add(to);
                    predecessors[to].Add(from);
                }
            }

            bool[] back = FindBackEdges(nodes, successors, out List<(int from, int to)> backEdges);
            int[] layer = AssignLayers(nodes, successors, predecessors, backEdges, back);

            int lastLayer = 0;
            for (int i = 0; i < count; i++)
            {
                lastLayer = Math.Max(lastLayer, layer[i]);
            }

            var columns = new List<int>[lastLayer + 1];
            for (int c = 0; c < columns.Length; c++)
            {
                columns[c] = new List<int>();
            }

            for (int i = 0; i < count; i++)
            {
                columns[layer[i]].Add(i);
            }

            var sizes = new CircuitPoint[count];
            for (int i = 0; i < count; i++)
            {
                sizes[i] = sizeOf(nodes[i]);
            }

            var y = new float[count];
            var placed = new bool[count];
            float x = 0f;
            float previousHalfWidth = 0f;
            for (int c = 0; c < columns.Length; c++)
            {
                List<int> column = columns[c];
                if (column.Count == 0)
                {
                    continue;
                }

                float halfWidth = 0f;
                for (int k = 0; k < column.Count; k++)
                {
                    halfWidth = Math.Max(halfWidth, sizes[column[k]].X * 0.5f);
                }

                x += c == 0 ? 0f : previousHalfWidth + ColumnGap + halfWidth;
                previousHalfWidth = halfWidth;

                PlaceColumn(nodes, column, predecessors, sizes, y, placed);
                for (int k = 0; k < column.Count; k++)
                {
                    int n = column[k];
                    circuit.MoveNode(nodes[n].Id, new CircuitPoint(SnapValue(x), SnapValue(y[n])));
                    placed[n] = true;
                }
            }
        }

        private static float SnapValue(float value)
        {
            return (float)Math.Round(value / Snap) * Snap;
        }

        /// <summary>
        /// Depth-first search from the sources (nodes without predecessors, in model order). An edge into a node that
        /// is still on the stack closes a loop and is marked as a back edge.
        /// </summary>
        private static bool[] FindBackEdges(IReadOnlyList<NodeDefinition> nodes, List<int>[] successors,
            out List<(int from, int to)> backEdges)
        {
            int count = nodes.Count;
            var state = new byte[count]; // 0 new, 1 on stack, 2 done
            var isBackTarget = new bool[count];
            backEdges = new List<(int, int)>();
            var stack = new Stack<(int node, int next)>();

            // Sources first so loops are entered from their natural input side; then any unvisited remainder.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int start = 0; start < count; start++)
                {
                    if (state[start] != 0 || (pass == 0 && !IsSource(nodes[start], start, successors)))
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
                                backEdges.Add((node, to));
                                isBackTarget[to] = true;
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

            return isBackTarget;
        }

        private static bool IsSource(NodeDefinition node, int index, List<int>[] successors)
        {
            return node.Component.Kind == ComponentKind.InputBoundary || node.Component.Kind == ComponentKind.Clock;
        }

        /// <summary>Longest-path layering over the forward edges; outputs share one final column.</summary>
        private static int[] AssignLayers(IReadOnlyList<NodeDefinition> nodes, List<int>[] successors,
            List<int>[] predecessors, List<(int from, int to)> backEdges, bool[] backTargets)
        {
            int count = nodes.Count;
            var skip = new HashSet<long>();
            for (int i = 0; i < backEdges.Count; i++)
            {
                skip.Add(((long)backEdges[i].from << 32) | (uint)backEdges[i].to);
            }

            var remaining = new int[count];
            for (int to = 0; to < count; to++)
            {
                for (int p = 0; p < predecessors[to].Count; p++)
                {
                    if (!skip.Contains(((long)predecessors[to][p] << 32) | (uint)to))
                    {
                        remaining[to]++;
                    }
                }
            }

            var layer = new int[count];
            var queue = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                if (remaining[i] == 0)
                {
                    queue.Enqueue(i);
                }
            }

            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                if (nodes[node].Component.Kind == ComponentKind.InputBoundary)
                {
                    layer[node] = 0;
                }

                for (int s = 0; s < successors[node].Count; s++)
                {
                    int to = successors[node][s];
                    if (skip.Contains(((long)node << 32) | (uint)to))
                    {
                        continue;
                    }

                    layer[to] = Math.Max(layer[to], layer[node] + 1);
                    if (--remaining[to] == 0)
                    {
                        queue.Enqueue(to);
                    }
                }
            }

            int inner = 0;
            for (int i = 0; i < count; i++)
            {
                if (nodes[i].Component.Kind != ComponentKind.OutputBoundary)
                {
                    inner = Math.Max(inner, layer[i]);
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (nodes[i].Component.Kind == ComponentKind.InputBoundary)
                {
                    layer[i] = 0;
                }
                else if (nodes[i].Component.Kind == ComponentKind.OutputBoundary)
                {
                    layer[i] = inner + 1;
                }
                else if (layer[i] == 0 && HasInputBoundaries(nodes))
                {
                    layer[i] = 1; // gates never share the input column
                }
            }

            return layer;
        }

        private static bool HasInputBoundaries(IReadOnlyList<NodeDefinition> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Component.Kind == ComponentKind.InputBoundary)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Boundaries keep their interface order (top to bottom). Other nodes aim for the average height of their
        /// already placed predecessors; then the column is swept top-down so neighbours keep a clear gap, and the
        /// whole column is shifted back towards its targets.
        /// </summary>
        private static void PlaceColumn(IReadOnlyList<NodeDefinition> nodes, List<int> column, List<int>[] predecessors,
            CircuitPoint[] sizes, float[] y, bool[] placed)
        {
            var target = new float[column.Count];
            var order = new int[column.Count];
            bool boundaries = true;
            for (int k = 0; k < column.Count; k++)
            {
                int n = column[k];
                order[k] = k;
                if (!nodes[n].IsBoundary)
                {
                    boundaries = false;
                }

                float sum = 0f;
                int known = 0;
                for (int p = 0; p < predecessors[n].Count; p++)
                {
                    int from = predecessors[n][p];
                    if (placed[from])
                    {
                        sum += y[from];
                        known++;
                    }
                }

                target[k] = known > 0 ? sum / known : -k * 2f;
            }

            if (boundaries)
            {
                // Interface order: the model order of boundary nodes is their port order.
                for (int k = 0; k < column.Count; k++)
                {
                    target[k] = -k;
                }
            }

            Array.Sort(order, (a, b) =>
            {
                int byTarget = target[b].CompareTo(target[a]); // higher y first (top of the screen)
                return byTarget != 0 ? byTarget : column[a].CompareTo(column[b]);
            });

            // Sweep from the top: each node sits at its target unless that would overlap the node above.
            float bottomOfPrevious = float.PositiveInfinity;
            float drift = 0f;
            var position = new float[column.Count];
            for (int i = 0; i < order.Length; i++)
            {
                int k = order[i];
                float half = sizes[column[k]].Y * 0.5f;
                float want = boundaries ? (i == 0 ? 0f : float.NegativeInfinity) : target[k];
                float limit = bottomOfPrevious - RowGap - half;
                float at = Math.Min(want, limit);
                if (float.IsNegativeInfinity(at))
                {
                    at = limit;
                }

                position[k] = at;
                bottomOfPrevious = at - half;
                if (!boundaries)
                {
                    drift += target[k] - at;
                }
            }

            // Re-centre: pushed-down nodes pull the column back up by their average displacement.
            float shift = boundaries ? 0f : drift / column.Count;
            for (int k = 0; k < column.Count; k++)
            {
                y[column[k]] = position[k] + shift;
            }

            if (boundaries)
            {
                // Centre boundary columns on the middle of the predecessors' span (or around zero).
                float top = position[order[0]] + sizes[column[order[0]]].Y * 0.5f;
                float bottom = position[order[order.Length - 1]] - sizes[column[order[order.Length - 1]]].Y * 0.5f;
                float centre = (top + bottom) * 0.5f;
                float aim = 0f;
                int known = 0;
                for (int k = 0; k < column.Count; k++)
                {
                    List<int> preds = predecessors[column[k]];
                    for (int p = 0; p < preds.Count; p++)
                    {
                        if (placed[preds[p]])
                        {
                            aim += y[preds[p]];
                            known++;
                        }
                    }
                }

                aim = known > 0 ? aim / known : 0f;
                for (int k = 0; k < column.Count; k++)
                {
                    y[column[k]] += aim - centre;
                }
            }
        }
    }
}
