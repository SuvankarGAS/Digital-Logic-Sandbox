using System;
using System.Collections.Generic;

namespace DigitalLogicSandbox.Utilities
{
    /// <summary>
    /// Finds cyclic strongly connected components in a directed graph given in CSR form
    /// (edges of vertex v are <c>targets[offsets[v] .. offsets[v + 1])</c>).
    /// Iterative Tarjan: no recursion, so graphs with many thousands of vertices cannot overflow the stack.
    /// </summary>
    public static class CycleFinder
    {
        /// <summary>
        /// Appends every component that contains a cycle (more than one vertex, or a vertex with a self edge) to
        /// <paramref name="results"/>. Vertex order inside a component follows discovery order.
        /// </summary>
        public static void FindCyclicComponents(int vertexCount, int[] offsets, int[] targets, List<int[]> results)
        {
            if (offsets == null)
            {
                throw new ArgumentNullException(nameof(offsets));
            }

            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (offsets.Length < vertexCount + 1)
            {
                throw new ArgumentException("Offsets must have vertexCount + 1 entries.", nameof(offsets));
            }

            var index = new int[vertexCount];
            var lowLink = new int[vertexCount];
            var onStack = new bool[vertexCount];
            var componentStack = new Stack<int>();
            var callVertex = new Stack<int>();
            var callEdge = new Stack<int>();
            var component = new List<int>();
            int counter = 0;

            for (int i = 0; i < vertexCount; i++)
            {
                index[i] = -1;
            }

            for (int start = 0; start < vertexCount; start++)
            {
                if (index[start] != -1)
                {
                    continue;
                }

                index[start] = lowLink[start] = counter++;
                componentStack.Push(start);
                onStack[start] = true;
                callVertex.Push(start);
                callEdge.Push(offsets[start]);

                while (callVertex.Count > 0)
                {
                    int v = callVertex.Pop();
                    int edge = callEdge.Pop();

                    if (edge < offsets[v + 1])
                    {
                        callVertex.Push(v);
                        callEdge.Push(edge + 1);

                        int w = targets[edge];
                        if (index[w] == -1)
                        {
                            index[w] = lowLink[w] = counter++;
                            componentStack.Push(w);
                            onStack[w] = true;
                            callVertex.Push(w);
                            callEdge.Push(offsets[w]);
                        }
                        else if (onStack[w] && index[w] < lowLink[v])
                        {
                            lowLink[v] = index[w];
                        }

                        continue;
                    }

                    if (lowLink[v] == index[v])
                    {
                        component.Clear();
                        int w;
                        do
                        {
                            w = componentStack.Pop();
                            onStack[w] = false;
                            component.Add(w);
                        }
                        while (w != v);

                        if (component.Count > 1 || HasSelfEdge(v, offsets, targets))
                        {
                            component.Reverse();
                            results.Add(component.ToArray());
                        }
                    }

                    if (callVertex.Count > 0)
                    {
                        int parent = callVertex.Peek();
                        if (lowLink[v] < lowLink[parent])
                        {
                            lowLink[parent] = lowLink[v];
                        }
                    }
                }
            }
        }

        private static bool HasSelfEdge(int v, int[] offsets, int[] targets)
        {
            for (int e = offsets[v]; e < offsets[v + 1]; e++)
            {
                if (targets[e] == v)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
