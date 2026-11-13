using System;
using System.Collections.Generic;
using System.Diagnostics;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Simulation.Propagation;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using static DigitalLogicSandbox.Tests.EditMode.Support.SimulationTestUtil;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace DigitalLogicSandbox.Tests.EditMode.Simulation
{
    public sealed class SimulationPerformanceTests
    {
        private TestEnvironment _env;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
        }

        [Test]
        public void TogglingInputs_AllocatesNoGarbage()
        {
            CircuitDefinition nand = BuildNand();
            DefinitionRef nandRef = _env.Publish(nand);
            SimulationContext context = CreateContext(_env, BuildOrFromNand(nandRef, nand));

            // Warm up so lazily-sized buffers reach steady state.
            for (int i = 0; i < 8; i++)
            {
                context.SetInput(i & 1, (i & 2) != 0 ? Signal.High : Signal.Low);
                context.Settle();
                context.Changes.Clear();
            }

            Assert.That(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    context.SetInput(i & 1, (i & 2) != 0 ? Signal.High : Signal.Low);
                    context.Settle();
                    context.Changes.Clear();
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void StepWave_AllocatesNoGarbage()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Chain");
            NodeDefinition input = circuit.AddInputBoundary("A", CircuitPoint.Zero);
            PinRef previous = BoundaryOut(input);
            for (int i = 0; i < 20; i++)
            {
                NodeDefinition not = circuit.AddNode(Not, CircuitPoint.Zero);
                circuit.Connect(previous, new PinRef(not.Id, NotGate.Input));
                previous = new PinRef(not.Id, NotGate.Output);
            }

            SimulationContext context = CreateContext(_env, circuit);
            context.SetInput(0, Signal.High);
            context.Settle();
            context.Changes.Clear();

            Assert.That(() =>
            {
                for (int i = 0; i < 20; i++)
                {
                    context.SetInput(0, (i & 1) == 0 ? Signal.Low : Signal.High);
                    while (context.StepWave().Status == SettleStatus.Pending)
                    {
                    }

                    context.Changes.Clear();
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [TestCase(500, 12, 1)]
        [TestCase(5000, 24, 7)]
        public void RandomCircuit_MatchesReferenceEvaluator(int gateCount, int inputCount, int seed)
        {
            RandomCircuit random = RandomCircuit.Build(gateCount, inputCount, outputCount: 16, seed);

            var stopwatch = Stopwatch.StartNew();
            CompiledCircuit compiled = Compile(_env, random.Definition);
            long compileMs = stopwatch.ElapsedMilliseconds;
            var context = new SimulationContext(compiled, new SimulationOptions { RecordChanges = false });

            var rng = new Random(seed * 31);
            var inputs = new bool[inputCount];
            long totalEvaluations = 0;
            stopwatch.Restart();
            for (int vector = 0; vector < 64; vector++)
            {
                int flip = rng.Next(inputCount);
                inputs[flip] = !inputs[flip];
                context.SetInput(flip, SignalExtensions.FromBool(inputs[flip]));
                SettleResult result = context.Settle();
                totalEvaluations += result.Evaluations;
                Assert.That(result.IsStable, NUnit.Framework.Is.True);

                bool[] expected = random.EvaluateReference(inputs);
                for (int o = 0; o < expected.Length; o++)
                {
                    Assert.That(context.GetOutput(o), NUnit.Framework.Is.EqualTo(SignalExtensions.FromBool(expected[o])),
                        "Vector " + vector + ", output " + o);
                }
            }

            TestContext.WriteLine(gateCount + " gates: compile " + compileMs + " ms, 64 toggles in " +
                stopwatch.ElapsedMilliseconds + " ms, " + totalEvaluations + " evaluations (" +
                (totalEvaluations / 64) + " avg per toggle).");
        }

        [Test]
        public void ManyCustomGateInstances_CompileAndEvaluate()
        {
            CircuitDefinition nand = BuildNand();
            DefinitionRef nandRef = _env.Publish(nand);
            CircuitDefinition or = BuildOrFromNand(nandRef, nand);
            DefinitionRef orRef = _env.Publish(or);

            // A chain of 300 OR gates: Y = OR(...OR(OR(A, B), B)..., B).
            var circuit = new CircuitDefinition(DefinitionId.New(), "OrChain");
            NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition b = circuit.AddInputBoundary("B", CircuitPoint.Zero);
            PortId orA = or.Inputs[0].Id;
            PortId orB = or.Inputs[1].Id;
            PortId orOut = or.Outputs[0].Id;
            PinRef previous = BoundaryOut(a);
            for (int i = 0; i < 300; i++)
            {
                NodeDefinition gate = circuit.AddNode(ComponentRef.Custom(orRef), CircuitPoint.Zero);
                circuit.Connect(previous, new PinRef(gate.Id, orA));
                circuit.Connect(BoundaryOut(b), new PinRef(gate.Id, orB));
                previous = new PinRef(gate.Id, orOut);
            }

            NodeDefinition output = circuit.AddOutputBoundary("Y", CircuitPoint.Zero);
            circuit.Connect(previous, BoundaryIn(output));

            SimulationContext context = CreateContext(_env, circuit);

            Assert.That(context.Circuit.GateCount, NUnit.Framework.Is.EqualTo(300 * 6));
            Assert.That(Evaluate(context, 0, 0), NUnit.Framework.Is.EqualTo(new[] { 0 }));
            Assert.That(Evaluate(context, 1, 0), NUnit.Framework.Is.EqualTo(new[] { 1 }));
            Assert.That(Evaluate(context, 0, 1), NUnit.Framework.Is.EqualTo(new[] { 1 }));
            Assert.That(Evaluate(context, 0, 0), NUnit.Framework.Is.EqualTo(new[] { 0 }));
        }

        /// <summary>
        /// A random acyclic AND/NOT network plus an independent reference evaluator that computes outputs directly
        /// in creation (topological) order — a completely different algorithm from the event-driven engine.
        /// </summary>
        private sealed class RandomCircuit
        {
            private readonly int _inputCount;
            private readonly List<(bool isAnd, int a, int b)> _gates = new List<(bool, int, int)>();
            private readonly List<int> _outputSources = new List<int>();

            private RandomCircuit(int inputCount)
            {
                _inputCount = inputCount;
            }

            public CircuitDefinition Definition { get; private set; }

            /// <summary>Signal sources are indexed: 0..inputs-1 = inputs, then one per gate.</summary>
            public static RandomCircuit Build(int gateCount, int inputCount, int outputCount, int seed)
            {
                var rng = new Random(seed);
                var result = new RandomCircuit(inputCount);
                var circuit = new CircuitDefinition(DefinitionId.New(), "Random" + gateCount);
                var sources = new List<PinRef>();
                for (int i = 0; i < inputCount; i++)
                {
                    sources.Add(BoundaryOut(circuit.AddInputBoundary("I" + i, CircuitPoint.Zero)));
                }

                for (int g = 0; g < gateCount; g++)
                {
                    bool isAnd = rng.Next(2) == 0;
                    // Bias towards recent sources to build deep logic cones, with occasional long-range links.
                    int a = PickSource(rng, sources.Count);
                    int b = PickSource(rng, sources.Count);
                    if (isAnd)
                    {
                        NodeDefinition node = circuit.AddNode(And, CircuitPoint.Zero);
                        circuit.Connect(sources[a], new PinRef(node.Id, AndGate.InputA));
                        circuit.Connect(sources[b], new PinRef(node.Id, AndGate.InputB));
                        sources.Add(new PinRef(node.Id, AndGate.Output));
                    }
                    else
                    {
                        NodeDefinition node = circuit.AddNode(Not, CircuitPoint.Zero);
                        circuit.Connect(sources[a], new PinRef(node.Id, NotGate.Input));
                        sources.Add(new PinRef(node.Id, NotGate.Output));
                    }

                    result._gates.Add((isAnd, a, b));
                }

                for (int o = 0; o < outputCount; o++)
                {
                    int source = sources.Count - 1 - o * 7;
                    NodeDefinition output = circuit.AddOutputBoundary("O" + o, CircuitPoint.Zero);
                    circuit.Connect(sources[source], BoundaryIn(output));
                    result._outputSources.Add(source);
                }

                result.Definition = circuit;
                return result;
            }

            private static int PickSource(Random rng, int count)
            {
                if (rng.Next(8) == 0)
                {
                    return rng.Next(count);
                }

                int window = Math.Min(count, 32);
                return count - 1 - rng.Next(window);
            }

            public bool[] EvaluateReference(bool[] inputs)
            {
                var values = new bool[_inputCount + _gates.Count];
                Array.Copy(inputs, values, _inputCount);
                for (int g = 0; g < _gates.Count; g++)
                {
                    (bool isAnd, int a, int b) = _gates[g];
                    values[_inputCount + g] = isAnd ? values[a] && values[b] : !values[a];
                }

                var outputs = new bool[_outputSources.Count];
                for (int o = 0; o < outputs.Length; o++)
                {
                    outputs[o] = values[_outputSources[o]];
                }

                return outputs;
            }
        }
    }
}
