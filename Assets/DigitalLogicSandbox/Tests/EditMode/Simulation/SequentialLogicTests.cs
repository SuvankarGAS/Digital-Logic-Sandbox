using System.Collections.Generic;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Simulation.Propagation;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using static DigitalLogicSandbox.Tests.EditMode.Support.SequentialCircuits;
using static DigitalLogicSandbox.Tests.EditMode.Support.SimulationTestUtil;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;
using Is = NUnit.Framework.Is;

namespace DigitalLogicSandbox.Tests.EditMode.Simulation
{
    /// <summary>
    /// Phase B: memory built from AND/NOT feedback loops, unit-delay timing, power-on state and the global clock.
    /// </summary>
    public sealed class SequentialLogicTests
    {
        private TestEnvironment _env;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
        }

        private static void Set(SimulationContext context, int input, int value)
        {
            context.SetInput(input, value != 0 ? Signal.High : Signal.Low);
            Assert.That(context.Settle().IsStable, Is.True);
        }

        private static void Clock(SimulationContext context, Signal value)
        {
            context.SetClock(value);
            Assert.That(context.Settle().IsStable, Is.True);
        }

        private static void Pulse(SimulationContext context)
        {
            Clock(context, Signal.High);
            Clock(context, Signal.Low);
        }

        private static int ReadBits(SimulationContext context)
        {
            int value = 0;
            for (int i = 0; i < context.Circuit.OutputCount; i++)
            {
                Signal bit = context.GetOutput(i);
                Assert.That(bit.IsBinary(), Is.True, "Output " + i + " is " + bit);
                if (bit == Signal.High)
                {
                    value |= 1 << i;
                }
            }

            return value;
        }

        private CircuitDefinition PublishDLatch(bool reverseOrder = false)
        {
            CircuitDefinition latch = BuildDLatch(reverseOrder ? "D Latch (reversed)" : "D Latch", reverseOrder);
            _env.Publish(latch);
            return latch;
        }

        private CircuitDefinition PublishDff(CircuitDefinition latch)
        {
            CircuitDefinition dff = BuildDff(latch);
            _env.Publish(dff);
            return dff;
        }

        // ------------------------------------------------------------------------------------------------
        // Latches
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void SrLatch_SetsResetsAndHolds()
        {
            SimulationContext latch = CreateContext(_env, BuildSrLatch());
            Assert.That(latch.Circuit.IsSequential, Is.True);
            Assert.That(latch.GetOutput(0).IsBinary(), Is.True, "Resolved power-on gives a binary state.");
            Assert.That(latch.GetOutput(1), Is.Not.EqualTo(latch.GetOutput(0)));

            Set(latch, 0, 1); // S
            Assert.That(latch.GetOutput(0), Is.EqualTo(Signal.High));
            Assert.That(latch.GetOutput(1), Is.EqualTo(Signal.Low));
            Set(latch, 0, 0);
            Assert.That(latch.GetOutput(0), Is.EqualTo(Signal.High), "Holds after S is released.");

            Set(latch, 1, 1); // R
            Assert.That(latch.GetOutput(0), Is.EqualTo(Signal.Low));
            Assert.That(latch.GetOutput(1), Is.EqualTo(Signal.High));
            Set(latch, 1, 0);
            Assert.That(latch.GetOutput(0), Is.EqualTo(Signal.Low), "Holds after R is released.");
        }

        [Test]
        public void SrLatch_UnknownPowerOn_StaysUnknownUntilDriven()
        {
            var options = new SimulationOptions { PowerOnState = PowerOnState.Unknown };
            SimulationContext latch = CreateContext(_env, BuildSrLatch(), options);
            Assert.That(latch.GetOutput(0), Is.EqualTo(Signal.Unknown));
            Assert.That(latch.GetOutput(1), Is.EqualTo(Signal.Unknown));

            Set(latch, 0, 1);
            Assert.That(latch.GetOutput(0), Is.EqualTo(Signal.High));
            Set(latch, 0, 0);
            Assert.That(latch.GetOutput(0), Is.EqualTo(Signal.High));
            Assert.That(latch.GetOutput(1), Is.EqualTo(Signal.Low));
        }

        [Test]
        public void PowerOnResolution_IsRepeatable()
        {
            CircuitDefinition circuit = BuildSrLatch();
            CompiledCircuit compiled = Compile(_env, circuit);
            var first = new SimulationContext(compiled);
            var second = new SimulationContext(Compile(_env, circuit));
            Assert.That(first.ResolvedLoopNets, Is.GreaterThan(0));
            Assert.That(second.GetOutput(0), Is.EqualTo(first.GetOutput(0)));

            Set(first, 0, 1);
            first.Reset();
            Assert.That(first.GetOutput(0), Is.EqualTo(second.GetOutput(0)), "Reset returns to the same power-on state.");
        }

        [Test]
        public void DLatch_IsTransparentWhileEnabled_AndHoldsWhenDisabled()
        {
            SimulationContext latch = CreateContext(_env, BuildDLatch());
            Set(latch, 1, 1); // E
            Set(latch, 0, 1); // D
            Assert.That(latch.GetOutput(0), Is.EqualTo(Signal.High));
            Set(latch, 0, 0);
            Assert.That(latch.GetOutput(0), Is.EqualTo(Signal.Low), "Transparent: Q follows D.");
            Set(latch, 0, 1);
            Set(latch, 1, 0); // disable with D = 1
            Set(latch, 0, 0);
            Assert.That(latch.GetOutput(0), Is.EqualTo(Signal.High), "Holds the value present when E fell.");
            Assert.That(latch.GetOutput(1), Is.EqualTo(Signal.Low));
        }

        // ------------------------------------------------------------------------------------------------
        // Flip-flops (nested custom gates)
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void DFlipFlop_CapturesOnRisingEdgeOnly()
        {
            CircuitDefinition dff = PublishDff(PublishDLatch());
            SimulationContext ff = CreateContext(_env, dff);
            Assert.That(ff.Circuit.HierarchyDepth, Is.EqualTo(1));

            Set(ff, 0, 1); // D = 1 while CLK low
            int before = ReadBits(ff) & 1;
            Set(ff, 1, 1); // rising edge
            Assert.That(ff.GetOutput(0), Is.EqualTo(Signal.High));
            Set(ff, 0, 0); // D changes while CLK high
            Assert.That(ff.GetOutput(0), Is.EqualTo(Signal.High), "No change while the clock is high.");
            Set(ff, 1, 0); // falling edge
            Assert.That(ff.GetOutput(0), Is.EqualTo(Signal.High), "No change on the falling edge.");
            Set(ff, 1, 1); // rising edge with D = 0
            Assert.That(ff.GetOutput(0), Is.EqualTo(Signal.Low));
            Assert.That(ff.GetOutput(1), Is.EqualTo(Signal.High));
            Assert.That(before, Is.InRange(0, 1));
        }

        [Test]
        public void FlipFlopBehaviour_DoesNotDependOnGateCreationOrder()
        {
            CircuitDefinition normal = PublishDff(PublishDLatch());
            CircuitDefinition reversed = PublishDff(PublishDLatch(reverseOrder: true));
            SimulationContext a = CreateContext(_env, normal);
            SimulationContext b = CreateContext(_env, reversed);

            var random = new System.Random(1234);
            for (int step = 0; step < 200; step++)
            {
                int input = random.Next(2);
                int value = random.Next(2);
                Set(a, input, value);
                Set(b, input, value);
                if (step == 0)
                {
                    // Align power-on states, which legitimately depend on gate order.
                    Set(a, 1, 1);
                    Set(b, 1, 1);
                    Set(a, 1, 0);
                    Set(b, 1, 0);
                }

                Assert.That(b.GetOutput(0), Is.EqualTo(a.GetOutput(0)), "Step " + step);
                Assert.That(b.GetOutput(1), Is.EqualTo(a.GetOutput(1)), "Step " + step);
            }
        }

        [Test]
        public void UnitDelay_WavesFollowLogicalDepth()
        {
            // A chain of three NOTs from one input: each gate is one wave, regardless of creation order.
            var circuit = new CircuitDefinition(DefinitionId.New(), "Chain");
            NodeDefinition input = circuit.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition third = circuit.AddNode(Not, CircuitPoint.Zero);
            NodeDefinition second = circuit.AddNode(Not, CircuitPoint.Zero);
            NodeDefinition first = circuit.AddNode(Not, CircuitPoint.Zero);
            circuit.Connect(BoundaryOut(input), new PinRef(first.Id, NotGate.Input));
            circuit.Connect(new PinRef(first.Id, NotGate.Output),
                new PinRef(second.Id, NotGate.Input));
            circuit.Connect(new PinRef(second.Id, NotGate.Output),
                new PinRef(third.Id, NotGate.Input));
            circuit.Connect(new PinRef(third.Id, NotGate.Output),
                BoundaryIn(circuit.AddOutputBoundary("Y", CircuitPoint.Zero)));

            SimulationContext context = CreateContext(_env, circuit);
            context.SetInput(0, Signal.High);
            SettleResult result = context.Settle();
            Assert.That(result.Waves, Is.EqualTo(3));
            Assert.That(result.Evaluations, Is.EqualTo(3));
            Assert.That(context.GetOutput(0), Is.EqualTo(Signal.Low));
        }

        // ------------------------------------------------------------------------------------------------
        // Clocked circuits
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void RippleCounter_CountsClockCycles()
        {
            CircuitDefinition counter = BuildRippleCounter(PublishDff(PublishDLatch()), 4);
            SimulationContext context = CreateContext(_env, counter);
            Assert.That(context.Circuit.HasClock, Is.True);
            Assert.That(context.Circuit.HierarchyDepth, Is.EqualTo(2));

            int start = ReadBits(context);
            for (int cycle = 1; cycle <= 20; cycle++)
            {
                Pulse(context);
                Assert.That(ReadBits(context), Is.EqualTo((start + cycle) % 16), "After cycle " + cycle);
            }
        }

        [Test]
        public void Register_LoadsOnlyOnRisingClockEdge()
        {
            CircuitDefinition register = BuildRegister(PublishDff(PublishDLatch()), 4);
            SimulationContext context = CreateContext(_env, register);
            Pulse(context); // load 0000
            Assert.That(ReadBits(context), Is.EqualTo(0));

            Set(context, 1, 1);
            Set(context, 3, 1); // D = 1010 (bits 1 and 3)
            Assert.That(ReadBits(context), Is.EqualTo(0), "Inputs alone do not change the register.");
            Clock(context, Signal.High);
            Assert.That(ReadBits(context), Is.EqualTo(0b1010));
            Set(context, 0, 1);
            Clock(context, Signal.Low);
            Assert.That(ReadBits(context), Is.EqualTo(0b1010), "Falling edge and input changes are ignored.");
            Clock(context, Signal.High);
            Assert.That(ReadBits(context), Is.EqualTo(0b1011));
        }

        [Test]
        public void ClockNodes_InNestedGates_ShareTheGlobalClock()
        {
            // A packaged gate with an internal clock node exposed as an output.
            var tap = new CircuitDefinition(DefinitionId.New(), "Clock Tap");
            NodeDefinition innerClock = tap.AddNode(ComponentRef.Clock, CircuitPoint.Zero);
            tap.Connect(new PinRef(innerClock.Id, BoundarySignatures.Pin),
                BoundaryIn(tap.AddOutputBoundary("CLK", CircuitPoint.Zero)));
            _env.Publish(tap);

            var root = new CircuitDefinition(DefinitionId.New(), "Main");
            NodeDefinition clock = root.AddNode(ComponentRef.Clock, CircuitPoint.Zero);
            NodeDefinition first = root.AddNode(ComponentRef.Custom(tap.Reference), CircuitPoint.Zero);
            NodeDefinition second = root.AddNode(ComponentRef.Custom(tap.Reference), CircuitPoint.Zero);
            root.Connect(new PinRef(clock.Id, BoundarySignatures.Pin),
                BoundaryIn(root.AddOutputBoundary("ROOT", CircuitPoint.Zero)));
            root.Connect(new PinRef(first.Id, tap.Outputs[0].Id), BoundaryIn(root.AddOutputBoundary("A", CircuitPoint.Zero)));
            root.Connect(new PinRef(second.Id, tap.Outputs[0].Id), BoundaryIn(root.AddOutputBoundary("B", CircuitPoint.Zero)));

            SimulationContext context = CreateContext(_env, root);
            Assert.That(context.Circuit.HasClock, Is.True);
            Assert.That(context.Circuit.GetOutputNet(0), Is.EqualTo(context.Circuit.ClockNet));
            Assert.That(context.Circuit.GetOutputNet(1), Is.EqualTo(context.Circuit.ClockNet));
            Assert.That(context.Circuit.GetOutputNet(2), Is.EqualTo(context.Circuit.ClockNet));
            Clock(context, Signal.High);
            Assert.That(ReadBits(context), Is.EqualTo(0b111));
        }

        [Test]
        public void CombinationalCircuit_IsNotSequential_AndHasNoUnknowns()
        {
            CircuitDefinition nand = BuildNand();
            SimulationContext context = CreateContext(_env, nand,
                new SimulationOptions { PowerOnState = PowerOnState.Unknown });
            Assert.That(context.Circuit.IsSequential, Is.False);
            Assert.That(context.GetOutput(0), Is.EqualTo(Signal.High), "Power-on X never survives combinational logic.");
            AssertTruthTable(context, 1, 1, 1, 0);
        }

        [Test]
        public void FeedbackLoop_CompilesWithFeedbackWarning()
        {
            CompileResult result = new CircuitCompiler(_env.Catalog).Compile(BuildSrLatch());
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Report.Contains(DiagnosticCode.FeedbackLoop), Is.True);
            Assert.That(result.Report.HasErrors, Is.False);
        }

        // ------------------------------------------------------------------------------------------------
        // Runner clock
        // ------------------------------------------------------------------------------------------------

        private SimulationRunner CounterRunner(out CompiledCircuit compiled)
        {
            compiled = Compile(_env, BuildRippleCounter(PublishDff(PublishDLatch()), 3));
            var runner = new SimulationRunner();
            runner.Load(compiled);
            return runner;
        }

        [Test]
        public void Runner_TicksClockInRealTime_OnlyWhileRunning()
        {
            SimulationRunner runner = CounterRunner(out _);
            runner.ClockFrequency = 2f; // edge every 0.25 s
            int start = ReadBits(runner.Context);

            Assert.That(runner.Advance(0.1), Is.EqualTo(0));
            Assert.That(runner.Advance(0.15), Is.EqualTo(1), "First edge after a quarter second.");
            Assert.That(runner.Context.Clock, Is.EqualTo(Signal.High));
            Assert.That(runner.Advance(0.75), Is.EqualTo(3));
            Assert.That(runner.ClockEdges, Is.EqualTo(4));
            Assert.That(runner.ClockCycles, Is.EqualTo(2));
            Assert.That(ReadBits(runner.Context), Is.EqualTo((start + 2) % 8));

            runner.Pause();
            Assert.That(runner.Advance(5.0), Is.EqualTo(0), "The clock stops while paused.");
            runner.Play();
            runner.ClockEnabled = false;
            Assert.That(runner.Advance(5.0), Is.EqualTo(0), "The clock can be stopped while running.");
            runner.ClockEnabled = true;
            Assert.That(runner.Advance(100.0), Is.EqualTo(SimulationRunner.MaxEdgesPerAdvance),
                "A long hitch is capped.");

            runner.Reset();
            Assert.That(runner.ClockEdges, Is.Zero);
            Assert.That(runner.Context.Clock, Is.EqualTo(Signal.Low));

            runner.ClockFrequency = 1000f;
            Assert.That(runner.ClockFrequency, Is.EqualTo(SimulationRunner.MaxClockFrequency));
        }

        [Test]
        public void Runner_StepWalksThroughClockEdgesWaveByWave()
        {
            SimulationRunner runner = CounterRunner(out _);
            int start = ReadBits(runner.Context);
            runner.Pause();

            int steps = 0;
            bool more = runner.Step(); // starts the rising edge
            steps++;
            Assert.That(runner.Context.Clock, Is.EqualTo(Signal.High));
            while (more)
            {
                more = runner.Step();
                steps++;
                Assert.That(steps, Is.LessThan(500));
            }

            Assert.That(steps, Is.GreaterThan(1), "An edge propagates over several waves.");
            Assert.That(ReadBits(runner.Context), Is.EqualTo((start + 1) % 8));

            runner.Step(); // falling edge
            Assert.That(runner.Context.Clock, Is.EqualTo(Signal.Low));
        }

        [Test]
        public void Runner_TickClock_ManualPulseAndEvent()
        {
            SimulationRunner runner = CounterRunner(out _);
            var seen = new List<Signal>();
            runner.ClockTicked += seen.Add;
            int start = ReadBits(runner.Context);
            Assert.That(runner.TickClock(), Is.True);
            Assert.That(runner.TickClock(), Is.True);
            Assert.That(seen, Is.EqualTo(new[] { Signal.High, Signal.Low }));
            Assert.That(ReadBits(runner.Context), Is.EqualTo((start + 1) % 8));

            var combinational = new SimulationRunner();
            combinational.Load(Compile(_env, BuildNand()));
            Assert.That(combinational.TickClock(), Is.False, "No clock node, no tick.");
            Assert.That(combinational.Advance(10.0), Is.Zero);
        }

        [Test]
        public void ClockTicking_AllocatesNoGarbage()
        {
            SimulationRunner runner = CounterRunner(out _);
            runner.ClockFrequency = 10f;
            runner.Context.RecordChanges = false;
            runner.Advance(1.0); // warm up

            Assert.That(() =>
            {
                for (int i = 0; i < 50; i++)
                {
                    runner.Advance(0.05);
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }
}
