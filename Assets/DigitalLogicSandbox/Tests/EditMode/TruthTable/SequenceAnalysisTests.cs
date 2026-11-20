using System;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Tests.EditMode.Support;
using DigitalLogicSandbox.TruthTable.Generation;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.SequentialCircuits;
using static DigitalLogicSandbox.Tests.EditMode.Support.SimulationTestUtil;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.TruthTables
{
    /// <summary>
    /// Phase B: sequence analysis (explicit power-on state plus input and clock steps) for circuits that a
    /// combinational truth table refuses.
    /// </summary>
    public sealed class SequenceAnalysisTests
    {
        private TestEnvironment _env;
        private SequenceAnalyzer _analyzer;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
            _analyzer = new SequenceAnalyzer(_env.Catalog);
        }

        private CircuitDefinition PublishDff()
        {
            CircuitDefinition latch = BuildDLatch();
            _env.Publish(latch);
            CircuitDefinition dff = BuildDff(latch);
            _env.Publish(dff);
            return dff;
        }

        private static int Bits(SequenceTable table, int row)
        {
            int value = 0;
            for (int i = 0; i < table.Outputs.Count; i++)
            {
                Signal bit = table.GetOutput(row, i);
                Assert.That(bit.IsBinary(), Is.True, "Row " + row + " output " + i + " is " + bit);
                value |= bit == Signal.High ? 1 << i : 0;
            }

            return value;
        }

        [Test]
        public void ParsePattern_AcceptsSeparators_AndRejectsOtherCharacters()
        {
            Signal[] values = SequenceSpec.ParsePattern("01 1,0_1|0\t1");
            Assert.That(values, Is.EqualTo(new[]
            {
                Signal.Low, Signal.High, Signal.High, Signal.Low, Signal.High, Signal.Low, Signal.High,
            }));
            Assert.That(SequenceSpec.ParsePattern(string.Empty), Is.Empty);
            Assert.That(() => SequenceSpec.ParsePattern("012"),
                Throws.TypeOf<FormatException>().With.Message.Contains("'2'"));
        }

        [Test]
        public void FromPatterns_HoldsTheLastValue_AndUsesTheLongestPattern()
        {
            SequenceSpec spec = SequenceSpec.FromPatterns(new[] { "1", "0101" });
            Assert.That(spec.StepCount, Is.EqualTo(4));
            for (int step = 0; step < 4; step++)
            {
                Assert.That(spec.Steps[step][0], Is.EqualTo(Signal.High), "Input 0 holds its last value.");
                Assert.That(spec.Steps[step][1], Is.EqualTo(step % 2 == 1 ? Signal.High : Signal.Low));
            }

            Assert.That(SequenceSpec.FromPatterns(new[] { "1", "0101" }, 2).StepCount, Is.EqualTo(2));
            Assert.That(SequenceSpec.FromPatterns(new string[0], 3).StepCount, Is.EqualTo(3));
        }

        [Test]
        public void DFlipFlop_CapturesDOnRisingEdgesOfItsClockInput()
        {
            CircuitDefinition dff = PublishDff();
            // Steps:        1  2  3  4  5
            // D             1  1  0  0  0
            // CLK           0  1  1  0  1
            // Q expected    ?  1  1  1  0
            SequenceSpec spec = SequenceSpec.FromPatterns(new[] { "11000", "01101" });
            var report = new ValidationReport();
            SequenceTable table = _analyzer.Run(dff, spec, report);

            Assert.That(table, Is.Not.Null, report.ToString());
            Assert.That(table.Clocked, Is.False, "No clock node: CLK is an ordinary input.");
            Assert.That(table.StepHeader, Is.EqualTo("Step"));
            Assert.That(table.RowCount, Is.EqualTo(6), "Power-on row plus five steps.");
            Assert.That(table.GetOutput(2, 0), Is.EqualTo(Signal.High));
            Assert.That(table.GetOutput(3, 0), Is.EqualTo(Signal.High), "Holds while CLK stays high.");
            Assert.That(table.GetOutput(4, 0), Is.EqualTo(Signal.High), "Falling edge does not capture.");
            Assert.That(table.GetOutput(5, 0), Is.EqualTo(Signal.Low), "Next rising edge captures D = 0.");
            for (int row = 1; row < table.RowCount; row++)
            {
                Assert.That(table.GetOutput(row, 1), Is.EqualTo(table.GetOutput(row, 0).Inverted()), "QN = NOT Q");
                Assert.That(table.IsStable(row), Is.True);
            }
        }

        [Test]
        public void ClockedCounter_AdvancesOncePerStep_WhenPulsingTheClock()
        {
            CircuitDefinition counter = BuildRippleCounter(PublishDff(), 3);
            SequenceTable table = _analyzer.Run(counter, SequenceSpec.FromPatterns(new string[0], 10), null);

            Assert.That(table.Clocked, Is.True);
            Assert.That(table.StepHeader, Is.EqualTo("Cycle"));
            int start = Bits(table, 0);
            for (int row = 1; row < table.RowCount; row++)
            {
                Assert.That(Bits(table, row), Is.EqualTo((start + row) % 8), "Cycle " + row);
            }

            var spec = SequenceSpec.FromPatterns(new string[0], 4);
            spec.PulseClock = false;
            SequenceTable unclocked = _analyzer.Run(counter, spec, null);
            Assert.That(unclocked.Clocked, Is.False);
            for (int row = 1; row < unclocked.RowCount; row++)
            {
                Assert.That(Bits(unclocked, row), Is.EqualTo(Bits(unclocked, 0)), "Without clock pulses nothing changes.");
            }
        }

        [Test]
        public void PowerOnState_IsExplicit_UnknownStartsAsX_UntilDriven()
        {
            CircuitDefinition latch = BuildSrLatch();
            var spec = SequenceSpec.FromPatterns(new[] { "010", "000" }); // S pulse, R low
            spec.PowerOnState = PowerOnState.Unknown;
            SequenceTable unknown = _analyzer.Run(latch, spec, null);

            Assert.That(unknown.PowerOnState, Is.EqualTo(PowerOnState.Unknown));
            Assert.That(unknown.GetOutput(0, 0), Is.EqualTo(Signal.Unknown));
            Assert.That(unknown.GetOutput(1, 0), Is.EqualTo(Signal.Unknown), "Hold keeps the unknown state.");
            Assert.That(unknown.GetOutput(2, 0), Is.EqualTo(Signal.High), "S drives Q to 1.");
            Assert.That(unknown.GetOutput(3, 0), Is.EqualTo(Signal.High), "And it is remembered.");

            spec.PowerOnState = PowerOnState.Resolved;
            SequenceTable resolved = _analyzer.Run(latch, spec, null);
            Assert.That(resolved.GetOutput(0, 0).IsBinary(), Is.True);
            Assert.That(resolved.GetOutput(0, 1), Is.EqualTo(resolved.GetOutput(0, 0).Inverted()));
            Assert.That(_analyzer.Run(latch, spec, null).GetOutput(0, 0), Is.EqualTo(resolved.GetOutput(0, 0)),
                "Resolved power-on is repeatable.");
        }

        [Test]
        public void CombinationalCircuits_CanAlsoBeSequenced()
        {
            CircuitDefinition nand = BuildNand();
            SequenceTable table = _analyzer.Run(nand, SequenceSpec.FromPatterns(new[] { "0011", "0101" }), null);
            Assert.That(table.GetOutput(1, 0), Is.EqualTo(Signal.High));
            Assert.That(table.GetOutput(2, 0), Is.EqualTo(Signal.High));
            Assert.That(table.GetOutput(3, 0), Is.EqualTo(Signal.High));
            Assert.That(table.GetOutput(4, 0), Is.EqualTo(Signal.Low));
        }

        [Test]
        public void Analysis_RunsInIsolation_AndNeverTouchesTheLiveContext()
        {
            CompiledCircuit compiled = Compile(_env, BuildRippleCounter(PublishDff(), 3));
            var live = new SimulationContext(compiled, SimulationOptions.Default);
            live.SetClock(Signal.High);
            live.Settle();
            Signal[] before = new Signal[compiled.OutputCount];
            for (int i = 0; i < before.Length; i++)
            {
                before[i] = live.GetOutput(i);
            }

            long evaluations = live.TotalEvaluations;
            int recorded = live.Changes.Count;

            _analyzer.Run(compiled, SequenceSpec.FromPatterns(new string[0], 12), null);

            Assert.That(live.Clock, Is.EqualTo(Signal.High));
            Assert.That(live.TotalEvaluations, Is.EqualTo(evaluations));
            Assert.That(live.Changes.Count, Is.EqualTo(recorded));
            for (int i = 0; i < before.Length; i++)
            {
                Assert.That(live.GetOutput(i), Is.EqualTo(before[i]));
            }
        }

        [Test]
        public void Limits_AndMismatches_ReportUsefulErrors()
        {
            CircuitDefinition nand = BuildNand();
            _analyzer.MaxSteps = 4;
            var report = new ValidationReport();
            Assert.That(_analyzer.Run(nand, SequenceSpec.FromPatterns(new[] { "00000", "1" }), report), Is.Null);
            Assert.That(report.Contains(DiagnosticCode.TruthTableTooLarge), Is.True);
            Assert.That(report.ToString(), Does.Contain("limit is 4"));

            report = new ValidationReport();
            Assert.That(_analyzer.Run(nand, SequenceSpec.FromPatterns(new[] { "0" }), report), Is.Null);
            Assert.That(report.Contains(DiagnosticCode.InterfaceMismatch), Is.True);

            var noOutputs = new CircuitDefinition(DefinitionId.New(), "Empty");
            noOutputs.AddInputBoundary("A", CircuitPoint.Zero);
            report = new ValidationReport();
            Assert.That(_analyzer.Run(noOutputs, SequenceSpec.FromPatterns(new[] { "1" }), report), Is.Null);
            Assert.That(report.Contains(DiagnosticCode.NoOutputs), Is.True);
        }

        [Test]
        public void Oscillation_IsReportedAndShownAsUnknown()
        {
            // Odd ring of three NOTs closed on itself, observed through an output.
            var ring = new CircuitDefinition(DefinitionId.New(), "Ring");
            var nots = new NodeDefinition[3];
            for (int i = 0; i < nots.Length; i++)
            {
                nots[i] = ring.AddNode(Not, CircuitPoint.Zero);
            }

            for (int i = 0; i < nots.Length; i++)
            {
                ring.Connect(new PinRef(nots[i].Id, NotGate.Output), new PinRef(nots[(i + 1) % 3].Id, NotGate.Input));
            }

            ring.Connect(new PinRef(nots[2].Id, NotGate.Output), BoundaryIn(ring.AddOutputBoundary("OUT", CircuitPoint.Zero)));
            var report = new ValidationReport();
            SequenceTable table = _analyzer.Run(ring, SequenceSpec.FromPatterns(new string[0], 2), report);

            Assert.That(table, Is.Not.Null, report.ToString());
            Assert.That(table.GetOutput(1, 0), Is.EqualTo(Signal.Unknown));
            Assert.That(report.Contains(DiagnosticCode.Oscillation), Is.True);
        }

        [Test]
        public void Export_UsesStepLabels_AndPowerOnRow()
        {
            SequenceTable table = _analyzer.Run(BuildRippleCounter(PublishDff(), 2),
                SequenceSpec.FromPatterns(new string[0], 2), null, "My, Counter");
            string[] csv = table.ToCsv().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.That(csv[0], Is.EqualTo("Cycle,Q0,Q1"));
            Assert.That(csv[1], Does.StartWith("Power-on,"));
            Assert.That(csv.Length, Is.EqualTo(4));
            Assert.That(table.ToTabSeparated(), Does.StartWith("Cycle\tQ0\tQ1"));
            Assert.That(table.Title, Is.EqualTo("My, Counter"));
        }

        /// <summary>Clock ÷1 and ÷2 wired straight to outputs C1 and C2.</summary>
        private static CircuitDefinition BuildClockTaps()
        {
            var root = new CircuitDefinition(DefinitionId.New(), "Taps");
            int[] dividers = { 1, 2 };
            for (int i = 0; i < dividers.Length; i++)
            {
                NodeDefinition clock = root.AddNode(ComponentRef.ClockDivided(dividers[i]), CircuitPoint.Zero);
                root.Connect(new PinRef(clock.Id, BoundarySignatures.Pin),
                    BoundaryIn(root.AddOutputBoundary("C" + dividers[i], CircuitPoint.Zero)));
            }

            return root;
        }

        [Test]
        public void ClockedSteps_RecordAfterInputs_ClockHigh_AndAfterCycle()
        {
            var report = new ValidationReport();
            SequenceTable table = _analyzer.Run(BuildClockTaps(), SequenceSpec.FromPatterns(new string[0], 2), report);
            Assert.That(table, Is.Not.Null, report.ToString());
            Assert.That(table.Clocked, Is.True);
            Assert.That(table.ClockExponents, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(table.ClockName(0), Is.EqualTo("CLK"));
            Assert.That(table.ClockName(1), Is.EqualTo("CLK÷2"));

            // ÷1: low before the pulse, high during it, low after it, in every cycle.
            for (int row = 1; row <= 2; row++)
            {
                Assert.That(table.GetOutput(row, 0, SequencePhase.AfterInputs), Is.EqualTo(Signal.Low));
                Assert.That(table.GetOutput(row, 0, SequencePhase.ClockHigh), Is.EqualTo(Signal.High));
                Assert.That(table.GetOutput(row, 0, SequencePhase.AfterCycle), Is.EqualTo(Signal.Low));
                Assert.That(table.GetOutput(row, 0), Is.EqualTo(Signal.Low), "The plain accessor is the end of the step.");
                Assert.That(table.GetClock(row, 0, SequencePhase.ClockHigh), Is.EqualTo(Signal.High));
            }

            // ÷2 rises with the first base edge and falls with the third.
            Assert.That(table.GetOutput(1, 1, SequencePhase.AfterInputs), Is.EqualTo(Signal.Low));
            Assert.That(table.GetOutput(1, 1, SequencePhase.ClockHigh), Is.EqualTo(Signal.High));
            Assert.That(table.GetOutput(1, 1, SequencePhase.AfterCycle), Is.EqualTo(Signal.High));
            Assert.That(table.GetOutput(2, 1, SequencePhase.AfterInputs), Is.EqualTo(Signal.High));
            Assert.That(table.GetOutput(2, 1, SequencePhase.ClockHigh), Is.EqualTo(Signal.Low));
            Assert.That(table.GetClock(2, 1, SequencePhase.AfterCycle), Is.EqualTo(Signal.Low));
            Assert.That(table.GetClock(0, 0, SequencePhase.ClockHigh), Is.EqualTo(Signal.Low), "Power-on: clocks LOW.");
        }

        [Test]
        public void RisingEdgeFlipFlop_ChangesWithTheClock_NotWithTheInputs()
        {
            CircuitDefinition dff = PublishDff();
            var root = new CircuitDefinition(DefinitionId.New(), "Clocked DFF");
            NodeDefinition d = root.AddInputBoundary("D", CircuitPoint.Zero);
            NodeDefinition clock = root.AddNode(ComponentRef.Clock, CircuitPoint.Zero);
            NodeDefinition ff = root.AddNode(ComponentRef.Custom(dff.Reference), CircuitPoint.Zero);
            root.Connect(BoundaryOut(d), new PinRef(ff.Id, dff.Inputs[0].Id));
            root.Connect(new PinRef(clock.Id, BoundarySignatures.Pin), new PinRef(ff.Id, dff.Inputs[1].Id));
            root.Connect(new PinRef(ff.Id, dff.Outputs[0].Id), BoundaryIn(root.AddOutputBoundary("Q", CircuitPoint.Zero)));

            var report = new ValidationReport();
            SequenceTable table = _analyzer.Run(root, SequenceSpec.FromPatterns(new[] { "0110" }), report);
            Assert.That(table, Is.Not.Null, report.ToString());

            // Cycle 2 sets D = 1: Q still shows the old 0 before the edge, then captures 1 on the rising edge.
            Assert.That(table.GetOutput(1, 0), Is.EqualTo(Signal.Low));
            Assert.That(table.GetOutput(2, 0, SequencePhase.AfterInputs), Is.EqualTo(Signal.Low));
            Assert.That(table.GetOutput(2, 0, SequencePhase.ClockHigh), Is.EqualTo(Signal.High));
            Assert.That(table.GetOutput(2, 0, SequencePhase.AfterCycle), Is.EqualTo(Signal.High));
            // Cycle 4 sets D = 0: the change appears only with the clock.
            Assert.That(table.GetOutput(4, 0, SequencePhase.AfterInputs), Is.EqualTo(Signal.High));
            Assert.That(table.GetOutput(4, 0, SequencePhase.ClockHigh), Is.EqualTo(Signal.Low));
            Assert.That(table.OutputChanged(2, 0) && table.OutputChanged(4, 0), Is.True);
            Assert.That(table.OutputChanged(3, 0), Is.False);
            // Cycle 1 also counts when the resolved power-on state happened to be Q = 1.
            int powerOnChange = table.GetOutput(0, 0) != table.GetOutput(1, 0) ? 1 : 0;
            Assert.That(table.OutputChanged(1, 0), Is.EqualTo(powerOnChange == 1));
            Assert.That(table.ChangedRowCount, Is.EqualTo(2 + powerOnChange));
        }

        [Test]
        public void PowerOnRow_HoldsTheRealInputValues()
        {
            SequenceTable table = _analyzer.Run(BuildNand(), SequenceSpec.FromPatterns(new[] { "0011", "0101" }), null);
            Assert.That(table.GetInput(0, 0), Is.EqualTo(Signal.Low));
            Assert.That(table.GetInput(0, 1), Is.EqualTo(Signal.Low));
            string[] csv = table.ToCsv().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.That(csv[1], Is.EqualTo("Power-on,0,0,1"), "Inputs are 0 at power-on, not '-'.");
        }

        [Test]
        public void OutputChanges_AreFlaggedPerRow()
        {
            SequenceTable table = _analyzer.Run(BuildNand(), SequenceSpec.FromPatterns(new[] { "0011", "0101" }), null);
            for (int row = 0; row <= 3; row++)
            {
                Assert.That(table.OutputChanged(row, 0), Is.False, "NAND stays 1 for rows 0-3.");
                Assert.That(table.AnyOutputChanged(row), Is.False);
            }

            Assert.That(table.OutputChanged(4, 0), Is.True);
            Assert.That(table.AnyOutputChanged(4), Is.True);
            Assert.That(table.ChangedRowCount, Is.EqualTo(1));
        }

        [Test]
        public void Counter_ReturnsToItsPowerOnState_AfterEightCycles()
        {
            SequenceTable table = _analyzer.Run(BuildRippleCounter(PublishDff(), 3),
                SequenceSpec.FromPatterns(new string[0], 10), null);
            Assert.That(table.RepeatFromRow, Is.EqualTo(0));
            Assert.That(table.RepeatRow, Is.EqualTo(8));
            Assert.That(table.RepeatPeriod, Is.EqualTo(8));
            Assert.That(table.SettledFromRow, Is.EqualTo(-1), "A counter keeps changing.");
        }

        [Test]
        public void HeldInputs_SettleWithoutBeingMistakenForACycle()
        {
            // A = 0 then held at 1; B = 1 from the start: NAND settles from step 2 on.
            SequenceTable table = _analyzer.Run(BuildNand(), SequenceSpec.FromPatterns(new[] { "01", "1" }, 5), null);
            Assert.That(table.SettledFromRow, Is.EqualTo(2));
            Assert.That(table.RepeatRow, Is.EqualTo(-1), "An unchanging stretch is not a repeating cycle.");
            Assert.That(table.RepeatPeriod, Is.Zero);

            SequenceTable changing = _analyzer.Run(BuildNand(), SequenceSpec.FromPatterns(new[] { "0101", "1" }), null);
            Assert.That(changing.SettledFromRow, Is.EqualTo(-1));
            Assert.That(changing.RepeatFromRow, Is.EqualTo(1), "Step 3 repeats the inputs and state of step 1.");
            Assert.That(changing.RepeatPeriod, Is.EqualTo(2));
        }

        [Test]
        public void RepeatPeriod_IsRight_WhenStatesHoldForSeveralCycles()
        {
            // A ÷4 clock seen at the end of each cycle reads (power-on L) H H L L H H …: the period is 4 cycles.
            // Cycle 3 (L) has the same signals as power-on but not the same clock-schedule position, so it is not
            // a repeat; cycle 4 is.
            var root = new CircuitDefinition(DefinitionId.New(), "Slow");
            NodeDefinition slow = root.AddNode(ComponentRef.ClockDivided(4), CircuitPoint.Zero);
            root.Connect(new PinRef(slow.Id, BoundarySignatures.Pin),
                BoundaryIn(root.AddOutputBoundary("C4", CircuitPoint.Zero)));
            SequenceTable table = _analyzer.Run(root, SequenceSpec.FromPatterns(new string[0], 10), null);

            Assert.That(table.GetOutput(1, 0), Is.EqualTo(Signal.High));
            Assert.That(table.GetOutput(2, 0), Is.EqualTo(Signal.High));
            Assert.That(table.GetOutput(3, 0), Is.EqualTo(Signal.Low));
            Assert.That(table.RepeatFromRow, Is.EqualTo(0));
            Assert.That(table.RepeatRow, Is.EqualTo(4));
            Assert.That(table.RepeatPeriod, Is.EqualTo(4));
        }

        [Test]
        public void Presets_ProduceReadablePatterns()
        {
            Assert.That(SequencePresets.BinaryCount(0, 2, 4), Is.EqualTo("0011"));
            Assert.That(SequencePresets.BinaryCount(1, 2, 4), Is.EqualTo("0101"));
            Assert.That(SequencePresets.BinaryCount(0, 1, 6), Is.EqualTo("0101 01"));
            Assert.That(SequencePresets.Alternate(5), Is.EqualTo("0101 0"));
            Assert.That(SequencePresets.Constant(Signal.High, 3), Is.EqualTo("111"));
            Assert.That(SequencePresets.Pulse(2, 4), Is.EqualTo("0010"));
            Assert.That(SequencePresets.Format(new[] { Signal.High, Signal.Low, Signal.Low, Signal.High, Signal.High }),
                Is.EqualTo("1001 1"));
            Assert.That(SequenceSpec.ParsePattern(SequencePresets.BinaryCount(2, 3, 8)).Length, Is.EqualTo(8));
        }
    }

    internal static class SignalTestExtensions
    {
        public static Signal Inverted(this Signal signal)
        {
            return signal == Signal.High ? Signal.Low : signal == Signal.Low ? Signal.High : signal;
        }
    }
}
