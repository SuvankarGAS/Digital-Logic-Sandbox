using System.Collections.Generic;
using DigitalLogicSandbox.Utilities;
using NUnit.Framework;

namespace DigitalLogicSandbox.Tests.EditMode.Utilities
{
    public sealed class CycleFinderTests
    {
        /// <summary>Builds CSR arrays from an edge list.</summary>
        private static List<int[]> Find(int vertices, params (int from, int to)[] edges)
        {
            var offsets = new int[vertices + 1];
            foreach ((int from, int _) in edges)
            {
                offsets[from + 1]++;
            }

            for (int i = 0; i < vertices; i++)
            {
                offsets[i + 1] += offsets[i];
            }

            var targets = new int[edges.Length];
            var fill = new int[vertices];
            foreach ((int from, int to) in edges)
            {
                targets[offsets[from] + fill[from]++] = to;
            }

            var results = new List<int[]>();
            CycleFinder.FindCyclicComponents(vertices, offsets, targets, results);
            return results;
        }

        [Test]
        public void AcyclicGraph_HasNoCycles()
        {
            Assert.That(Find(4, (0, 1), (0, 2), (1, 3), (2, 3)), Is.Empty);
        }

        [Test]
        public void SimpleLoop_IsFound()
        {
            List<int[]> cycles = Find(3, (0, 1), (1, 2), (2, 0));
            Assert.That(cycles.Count, Is.EqualTo(1));
            Assert.That(cycles[0], Is.EquivalentTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void SelfEdge_IsACycle()
        {
            List<int[]> cycles = Find(2, (0, 1), (1, 1));
            Assert.That(cycles.Count, Is.EqualTo(1));
            Assert.That(cycles[0], Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void SeparateLoops_AreReportedSeparately()
        {
            List<int[]> cycles = Find(5, (0, 1), (1, 0), (2, 3), (3, 4), (4, 2), (1, 2));
            Assert.That(cycles.Count, Is.EqualTo(2));
        }

        [Test]
        public void VeryLongChain_DoesNotOverflowStack()
        {
            const int count = 100000;
            var edges = new (int, int)[count];
            for (int i = 0; i < count - 1; i++)
            {
                edges[i] = (i, i + 1);
            }

            edges[count - 1] = (count - 1, 0);

            List<int[]> cycles = Find(count, edges);

            Assert.That(cycles.Count, Is.EqualTo(1));
            Assert.That(cycles[0].Length, Is.EqualTo(count));
        }
    }
}
