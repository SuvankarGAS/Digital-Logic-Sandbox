using System;
using System.Diagnostics;
using System.IO;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Persistence;
using DigitalLogicSandbox.Persistence.Deserialization;
using DigitalLogicSandbox.Persistence.Recovery;
using DigitalLogicSandbox.Persistence.Serialization;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.PersistenceTests
{
    /// <summary>
    /// Profiles saving and loading a large project. Autosave writes on the main thread, so this measures the cost a
    /// user would see as a hitch. The thresholds are generous guards against regressions (orders of magnitude), not
    /// benchmarks; actual timings are written to the test output.
    /// </summary>
    public sealed class PersistencePerformanceTests
    {
        private const int Chains = 20;
        private const int ChainLength = 100; // 2 000 primitives + 100 custom gate instances

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "dls-perf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void LargeProject_SaveAndLoad_StayWellUnderAutosaveBudget()
        {
            var env = new TestEnvironment();
            CircuitDefinition nand = BuildNand();
            env.Publish(nand);
            CircuitDefinition root = BuildLargeRoot(nand);
            var document = new ProjectDocument { Name = "Large", RootCircuit = root };
            document.Definitions.AddRange(env.Library.AllVersions);

            // Warm up JSON code paths so the measurement reflects steady-state autosaves.
            ProjectSerializer.ToJson(document);

            var stopwatch = Stopwatch.StartNew();
            string json = ProjectSerializer.ToJson(document);
            long serializeMs = stopwatch.ElapsedMilliseconds;

            string path = Path.Combine(_directory, "Large" + ProjectFormat.FileExtension);
            var store = new ProjectFileStore();
            stopwatch.Restart();
            store.Save(path, document);
            long saveMs = stopwatch.ElapsedMilliseconds;

            stopwatch.Restart();
            ProjectLoadResult loaded = new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault()).Read(json);
            long loadMs = stopwatch.ElapsedMilliseconds;

            TestContext.WriteLine(root.Nodes.Count + " nodes, " + root.Connections.Count + " connections, " +
                (json.Length / 1024) + " KiB: serialize " + serializeMs + " ms, atomic save " + saveMs +
                " ms, load " + loadMs + " ms.");

            Assert.That(loaded.Report.HasErrors, Is.False, loaded.Report.ToString());
            Assert.That(loaded.Document.RootCircuit.Nodes.Count, Is.EqualTo(root.Nodes.Count));
            Assert.That(loaded.Document.RootCircuit.Connections.Count, Is.EqualTo(root.Connections.Count));
            Assert.That(CircuitFingerprint.Of(loaded.Document.RootCircuit), Is.EqualTo(CircuitFingerprint.Of(root)));
            Assert.That(saveMs, Is.LessThan(2000), "Autosave of a large project must not cause a long hitch.");
            Assert.That(loadMs, Is.LessThan(5000));
        }

        /// <summary>Parallel NOT chains with a NAND instance every 20 gates, all on the grid.</summary>
        private static CircuitDefinition BuildLargeRoot(CircuitDefinition nand)
        {
            var root = new CircuitDefinition(DefinitionId.New(), "Large");
            for (int c = 0; c < Chains; c++)
            {
                float y = c * 4f;
                NodeDefinition input = root.AddInputBoundary("I" + c, new CircuitPoint(-4f, y));
                PinRef previous = BoundaryOut(input);
                for (int i = 0; i < ChainLength; i++)
                {
                    NodeDefinition not = root.AddNode(Not, new CircuitPoint(i * 5f, y));
                    root.Connect(previous, new PinRef(not.Id, NotGate.Input));
                    previous = new PinRef(not.Id, NotGate.Output);
                    if (i % 20 == 19)
                    {
                        NodeDefinition gate = root.AddNode(ComponentRef.Custom(nand.Reference),
                            new CircuitPoint(i * 5f + 2.5f, y + 2f));
                        root.Connect(previous, new PinRef(gate.Id, nand.Inputs[0].Id));
                        root.Connect(previous, new PinRef(gate.Id, nand.Inputs[1].Id));
                    }
                }

                NodeDefinition output = root.AddOutputBoundary("O" + c, new CircuitPoint(ChainLength * 5f, y));
                root.Connect(previous, BoundaryIn(output));
            }

            return root;
        }
    }
}
