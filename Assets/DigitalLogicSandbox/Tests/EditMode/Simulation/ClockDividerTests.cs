using System;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Tests.EditMode.Support;
using DigitalLogicSandbox.TruthTable.Generation;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using static DigitalLogicSandbox.Tests.EditMode.Support.SimulationTestUtil;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;
using Is = NUnit.Framework.Is;

namespace DigitalLogicSandbox.Tests.EditMode.Simulation
{
    /// <summary>Clock dividers: one clock domain per divider, all derived from the base clock.</summary>
    public sealed class ClockDividerTests
    {
        private TestEnvironment _env;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
        }

        /// <summary>Clocks ÷1, ÷2 and ÷4 exposed as outputs C1, C2, C4, plus a packaged gate with an inner ÷2 clock (T).</summary>
        private CircuitDefinition BuildDomains()
        {
            var tap = new CircuitDefinition(DefinitionId.New(), "Slow Tap");
            NodeDefinition inner = tap.AddNode(ComponentRef.ClockDivided(2), CircuitPoint.Zero);
            tap.Connect(new PinRef(inner.Id, BoundarySignatures.Pin),
                BoundaryIn(tap.AddOutputBoundary("CLK", CircuitPoint.Zero)));
            _env.Publish(tap);

            var root = new CircuitDefinition(DefinitionId.New(), "Main");
            int[] dividers = { 1, 2, 4 };
            for (int i = 0; i < dividers.Length; i++)
            {
                NodeDefinition clock = root.AddNode(ComponentRef.ClockDivided(dividers[i]), CircuitPoint.Zero);
                root.Connect(new PinRef(clock.Id, BoundarySignatures.Pin),
                    BoundaryIn(root.AddOutputBoundary("C" + dividers[i], CircuitPoint.Zero)));
            }

            NodeDefinition gate = root.AddNode(ComponentRef.Custom(tap.Reference), CircuitPoint.Zero);
            root.Connect(new PinRef(gate.Id, tap.Outputs[0].Id), BoundaryIn(root.AddOutputBoundary("T", CircuitPoint.Zero)));
            return root;
        }

        [Test]
        public void Schedule_SlowerDomainsRiseWithTheBaseClock()
        {
            string[] expected =
            {
                "1010101010101010", // ÷1
                "1100110011001100", // ÷2
                "1111000011110000", // ÷4
            };

            for (int k = 0; k < expected.Length; k++)
            {
                Assert.That(ClockSchedule.Level(k, 0), Is.EqualTo(Signal.Low), "Every domain powers on LOW.");
                for (int edge = 1; edge <= 16; edge++)
                {
                    Signal want = expected[k][edge - 1] == '1' ? Signal.High : Signal.Low;
                    Assert.That(ClockSchedule.Level(k, edge), Is.EqualTo(want), "Divider " + (1 << k) + ", edge " + edge);
                }
            }

            Assert.That(ClockSchedule.Level(6, 64), Is.EqualTo(Signal.High));
            Assert.That(ClockSchedule.Level(6, 65), Is.EqualTo(Signal.Low), "÷64 has a period of 128 base edges.");
        }

        [Test]
        public void ComponentRef_CarriesTheDivider()
        {
            Assert.That(ComponentRef.Clock.ClockDivider, Is.EqualTo(1));
            Assert.That(ComponentRef.ClockDivided(1), Is.EqualTo(ComponentRef.Clock));
            ComponentRef slow = ComponentRef.ClockDivided(8);
            Assert.That(slow.ClockDivider, Is.EqualTo(8));
            Assert.That(slow.ClockExponent, Is.EqualTo(3));
            Assert.That(slow, Is.Not.EqualTo(ComponentRef.Clock));
            Assert.That(slow.WithClockDivider(1), Is.EqualTo(ComponentRef.Clock));
            Assert.That(slow.ToString(), Is.EqualTo("Clock/8"));
            Assert.That(ComponentRef.Utility(ComponentKind.Clock, 1, 2), Is.EqualTo(ComponentRef.ClockDivided(4)));
            Assert.That(ComponentRef.Utility(ComponentKind.Clock, 1, 99).ClockDivider, Is.EqualTo(64), "Clamped.");

            Assert.Throws<ArgumentOutOfRangeException>(() => ComponentRef.ClockDivided(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => ComponentRef.ClockDivided(128));
            Assert.Throws<ArgumentOutOfRangeException>(() => ComponentRef.ClockDivided(0));
            Assert.Throws<InvalidOperationException>(() => ComponentRef.InputBoundary.WithClockDivider(2));
            Assert.That(ComponentRef.InputBoundary.ClockDivider, Is.EqualTo(1));
        }

        [Test]
        public void SameDivider_SharesANet_AcrossTheHierarchy()
        {
            CompiledCircuit circuit = Compile(_env, BuildDomains());
            Assert.That(circuit.ClockDomainCount, Is.EqualTo(3));
            Assert.That(circuit.HasClockDomain(0) && circuit.HasClockDomain(1) && circuit.HasClockDomain(2), Is.True);
            Assert.That(circuit.HasClockDomain(3), Is.False);
            Assert.That(circuit.GetOutputNet(0), Is.EqualTo(circuit.GetClockNet(0)));
            Assert.That(circuit.GetOutputNet(1), Is.EqualTo(circuit.GetClockNet(1)));
            Assert.That(circuit.GetOutputNet(2), Is.EqualTo(circuit.GetClockNet(2)));
            Assert.That(circuit.GetOutputNet(3), Is.EqualTo(circuit.GetClockNet(1)), "The inner ÷2 clock joins the root ÷2 domain.");
            Assert.That(circuit.GetClockNet(0), Is.Not.EqualTo(circuit.GetClockNet(1)));
            Assert.That(circuit.ClockNet, Is.EqualTo(circuit.GetClockNet(0)), "ClockNet is the fastest domain.");
        }

        [Test]
        public void Runner_TicksEveryDomainOnItsSchedule()
        {
            var runner = new SimulationRunner();
            runner.Load(Compile(_env, BuildDomains()));
            string c1 = string.Empty, c2 = string.Empty, c4 = string.Empty, t = string.Empty;
            for (int edge = 0; edge < 8; edge++)
            {
                Assert.That(runner.TickClock(), Is.True);
                SimulationContext context = runner.Context;
                c1 += context.GetOutput(0).ToDisplayChar();
                c2 += context.GetOutput(1).ToDisplayChar();
                c4 += context.GetOutput(2).ToDisplayChar();
                t += context.GetOutput(3).ToDisplayChar();
                Assert.That(context.GetClock(1), Is.EqualTo(context.GetOutput(1)));
            }

            Assert.That(c1, Is.EqualTo("10101010"));
            Assert.That(c2, Is.EqualTo("11001100"));
            Assert.That(c4, Is.EqualTo("11110000"));
            Assert.That(t, Is.EqualTo(c2));

            runner.Reset();
            Assert.That(runner.Context.GetClock(2), Is.EqualTo(Signal.Low));
            runner.TickClock();
            Assert.That(runner.Context.GetClock(2), Is.EqualTo(Signal.High), "After a reset all domains rise together.");
        }

        [Test]
        public void SingleBaseClock_BehavesAsBefore()
        {
            var root = new CircuitDefinition(DefinitionId.New(), "Main");
            NodeDefinition clock = root.AddNode(ComponentRef.Clock, CircuitPoint.Zero);
            root.Connect(new PinRef(clock.Id, BoundarySignatures.Pin), BoundaryIn(root.AddOutputBoundary("C", CircuitPoint.Zero)));
            var runner = new SimulationRunner();
            runner.Load(Compile(_env, root));
            Assert.That(runner.Circuit.ClockDomainCount, Is.EqualTo(1));
            for (int edge = 1; edge <= 6; edge++)
            {
                runner.TickClock();
                Assert.That(runner.Context.Clock, Is.EqualTo(edge % 2 == 1 ? Signal.High : Signal.Low));
            }
        }

        [Test]
        public void TickingSeveralDomains_AllocatesNoGarbage()
        {
            var runner = new SimulationRunner();
            runner.Load(Compile(_env, BuildDomains()));
            runner.ClockFrequency = 10f;
            runner.Context.RecordChanges = false;
            runner.Advance(1.0); // warm up

            Assert.That(() =>
            {
                for (int i = 0; i < 50; i++)
                {
                    runner.Advance(0.05);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void SequenceAnalysis_StepsOneBaseCycle_SlowDomainsFollowTheirSchedule()
        {
            var analyzer = new SequenceAnalyzer(_env.Catalog);
            SequenceTable table = analyzer.Run(BuildDomains(), SequenceSpec.FromPatterns(new string[0], 8), null);
            Assert.That(table.Clocked, Is.True);
            Assert.That(table.RowCount, Is.EqualTo(9));
            string c1 = string.Empty, c2 = string.Empty, c4 = string.Empty;
            for (int row = 1; row < table.RowCount; row++)
            {
                c1 += table.GetOutput(row, 0).ToDisplayChar();
                c2 += table.GetOutput(row, 1).ToDisplayChar();
                c4 += table.GetOutput(row, 2).ToDisplayChar();
            }

            // Each step ends after base edge 2·step: ÷1 is back LOW, ÷2 alternates, ÷4 changes every second step.
            Assert.That(c1, Is.EqualTo("00000000"));
            Assert.That(c2, Is.EqualTo("10101010"));
            Assert.That(c4, Is.EqualTo("11001100"));
        }
    }
}
