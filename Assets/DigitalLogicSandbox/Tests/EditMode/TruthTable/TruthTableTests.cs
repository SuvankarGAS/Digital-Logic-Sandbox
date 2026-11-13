using System.Text;
using System.Threading;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Tests.EditMode.Support;
using DigitalLogicSandbox.TruthTable.Evaluation;
using DigitalLogicSandbox.TruthTable.Export;
using DigitalLogicSandbox.TruthTable.Generation;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.TruthTables
{
    /// <summary>
    /// Truth tables are produced by running the real simulation engine. Expected values below are test oracles,
    /// never implementation data.
    /// </summary>
    public sealed class TruthTableTests
    {
        private TestEnvironment _env;
        private TruthTableGenerator _generator;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
            _generator = new TruthTableGenerator(_env.Catalog);
        }

        // ------------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------------

        private TruthTableData Generate(CircuitDefinition circuit)
        {
            TruthTableJob job = _generator.Generate(circuit);
            Assert.That(job.Status, Is.EqualTo(TruthTableStatus.Completed), job.Error ?? job.Plan.Report.ToString());
            return job.Table;
        }

        /// <summary>"in|out" strings for every row, e.g. "01|1".</summary>
        private static string[] Rows(TruthTableData table)
        {
            var rows = new string[table.CompletedRows];
            var builder = new StringBuilder();
            for (int r = 0; r < rows.Length; r++)
            {
                builder.Clear();
                for (int i = 0; i < table.InputCount; i++)
                {
                    builder.Append(TruthTableData.Symbol(table.GetInput(r, i)));
                }

                builder.Append('|');
                for (int o = 0; o < table.OutputCount; o++)
                {
                    builder.Append(TruthTableData.Symbol(table.GetOutput(r, o)));
                }

                rows[r] = builder.ToString();
            }

            return rows;
        }

        private static string[] Headers(TruthTableData table)
        {
            var names = new string[table.InputCount + table.OutputCount];
            for (int i = 0; i < table.InputCount; i++)
            {
                names[i] = table.Inputs[i].Name;
            }

            for (int o = 0; o < table.OutputCount; o++)
            {
                names[table.InputCount + o] = table.Outputs[o].Name;
            }

            return names;
        }

        /// <summary>N inputs, one output = AND of the first two (or the first input when N == 1).</summary>
        private static CircuitDefinition WideCircuit(int inputs)
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Wide" + inputs);
            NodeDefinition first = null;
            NodeDefinition second = null;
            for (int i = 0; i < inputs; i++)
            {
                NodeDefinition input = circuit.AddInputBoundary("I" + i, new CircuitPoint(0, i));
                first ??= input;
                if (i == 1)
                {
                    second = input;
                }
            }

            NodeDefinition output = circuit.AddOutputBoundary("OUT", new CircuitPoint(10, 0));
            if (second == null)
            {
                circuit.Connect(BoundaryOut(first), BoundaryIn(output));
                return circuit;
            }

            NodeDefinition and = circuit.AddNode(And, new CircuitPoint(5, 0));
            circuit.Connect(BoundaryOut(first), new PinRef(and.Id, AndGate.InputA));
            circuit.Connect(BoundaryOut(second), new PinRef(and.Id, AndGate.InputB));
            circuit.Connect(new PinRef(and.Id, AndGate.Output), BoundaryIn(output));
            return circuit;
        }

        // ------------------------------------------------------------------------------------------------
        // Gate tables
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void And_TruthTable_IsGeneratedByTheEngine()
        {
            CircuitDefinition harness = TruthTableTargets.ForComponent(_env.Catalog, And, "AND");
            TruthTableData table = Generate(harness);

            Assert.That(table.Title, Is.EqualTo("AND"));
            Assert.That(Headers(table), Is.EqualTo(new[] { "A", "B", "OUT" }));
            Assert.That(Rows(table), Is.EqualTo(new[] { "00|0", "01|0", "10|0", "11|1" }));
        }

        [Test]
        public void Not_TruthTable_IsGeneratedByTheEngine()
        {
            TruthTableData table = Generate(TruthTableTargets.ForComponent(_env.Catalog, Not, "NOT"));

            Assert.That(Headers(table), Is.EqualTo(new[] { "IN", "OUT" }));
            Assert.That(Rows(table), Is.EqualTo(new[] { "0|1", "1|0" }));
        }

        [Test]
        public void Nand_BuiltFromAndAndNot_HasTheNandTable()
        {
            TruthTableData table = Generate(BuildNand());

            Assert.That(Headers(table), Is.EqualTo(new[] { "A", "B", "OUT" }));
            Assert.That(Rows(table), Is.EqualTo(new[] { "00|1", "01|1", "10|1", "11|0" }));
        }

        [Test]
        public void Or_BuiltFromPrimitives_HasTheOrTable()
        {
            Assert.That(Rows(Generate(BuildOrFromPrimitives())), Is.EqualTo(new[] { "00|0", "01|1", "10|1", "11|1" }));
        }

        [Test]
        public void Or_BuiltFromPackagedNandGates_HasTheOrTable()
        {
            CircuitDefinition nand = BuildNand();
            DefinitionRef nandRef = _env.Publish(nand);
            CircuitDefinition or = BuildOrFromNand(nandRef, nand);
            _env.Publish(or);

            Assert.That(Rows(Generate(or)), Is.EqualTo(new[] { "00|0", "01|1", "10|1", "11|1" }));

            // The placed custom gate resolves to its packaged definition.
            CircuitDefinition target = TruthTableTargets.ForComponent(_env.Catalog, ComponentRef.Custom(or.Reference), "OR");
            Assert.That(target, Is.SameAs(or));
        }

        [Test]
        public void NestedCustomGates_AreTabulatedThroughEveryLevel()
        {
            CircuitDefinition nand = BuildNand();
            _env.Publish(nand);
            CircuitDefinition level1 = Wrap(nand, "Wrapped NAND");
            _env.Publish(level1);
            CircuitDefinition level2 = Wrap(level1, "Twice wrapped NAND");
            _env.Publish(level2);

            Assert.That(Rows(Generate(level2)), Is.EqualTo(new[] { "00|1", "01|1", "10|1", "11|0" }));
        }

        // ------------------------------------------------------------------------------------------------
        // Columns and ordering
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void MultipleOutputs_AreEvaluatedIndependently()
        {
            // AND, NAND and a pass-through of B from the same two inputs.
            var circuit = new CircuitDefinition(DefinitionId.New(), "Multi");
            NodeDefinition a = circuit.AddInputBoundary("A", new CircuitPoint(0, 1));
            NodeDefinition b = circuit.AddInputBoundary("B", new CircuitPoint(0, -1));
            NodeDefinition and = circuit.AddNode(And, new CircuitPoint(3, 0));
            NodeDefinition not = circuit.AddNode(Not, new CircuitPoint(6, 0));
            NodeDefinition andOut = circuit.AddOutputBoundary("AND", new CircuitPoint(9, 1));
            NodeDefinition nandOut = circuit.AddOutputBoundary("NAND", new CircuitPoint(9, 0));
            NodeDefinition bOut = circuit.AddOutputBoundary("B copy", new CircuitPoint(9, -1));
            circuit.Connect(BoundaryOut(a), new PinRef(and.Id, AndGate.InputA));
            circuit.Connect(BoundaryOut(b), new PinRef(and.Id, AndGate.InputB));
            circuit.Connect(new PinRef(and.Id, AndGate.Output), new PinRef(not.Id, NotGate.Input));
            circuit.Connect(new PinRef(and.Id, AndGate.Output), BoundaryIn(andOut));
            circuit.Connect(new PinRef(not.Id, NotGate.Output), BoundaryIn(nandOut));
            circuit.Connect(BoundaryOut(b), BoundaryIn(bOut));

            TruthTableData table = Generate(circuit);

            Assert.That(Headers(table), Is.EqualTo(new[] { "A", "B", "AND", "NAND", "B copy" }));
            Assert.That(Rows(table), Is.EqualTo(new[] { "00|010", "01|011", "10|010", "11|101" }));
        }

        [Test]
        public void Rows_FollowBinaryOrder_WithTheFirstInputAsMostSignificantBit()
        {
            // Three inputs; outputs echo each input so the column-to-bit mapping is visible.
            var circuit = new CircuitDefinition(DefinitionId.New(), "Echo");
            for (int i = 0; i < 3; i++)
            {
                string name = new[] { "C", "A", "B" }[i];
                NodeDefinition input = circuit.AddInputBoundary(name, new CircuitPoint(0, i));
                NodeDefinition output = circuit.AddOutputBoundary(name + "'", new CircuitPoint(5, i));
                circuit.Connect(BoundaryOut(input), BoundaryIn(output));
            }

            TruthTableData table = Generate(circuit);

            Assert.That(table.RowCount, Is.EqualTo(8));
            Assert.That(Headers(table), Is.EqualTo(new[] { "C", "A", "B", "C'", "A'", "B'" }), "Interface order, not alphabetical.");
            Assert.That(Rows(table), Is.EqualTo(new[]
            {
                "000|000", "001|001", "010|010", "011|011", "100|100", "101|101", "110|110", "111|111",
            }));
            Assert.That(table.RowOf(new[] { Signal.High, Signal.Low, Signal.High }), Is.EqualTo(5));
        }

        [Test]
        public void ReorderingTheInterface_ReordersTheColumns()
        {
            CircuitDefinition nand = BuildNand();
            nand.MoveInterfacePort(nand.Inputs[1].Id, 0); // B before A

            TruthTableData table = Generate(nand);
            Assert.That(Headers(table), Is.EqualTo(new[] { "B", "A", "OUT" }));
            Assert.That(Rows(table), Is.EqualTo(new[] { "00|1", "01|1", "10|1", "11|0" }));
        }

        [Test]
        public void CircuitWithoutInputs_HasASingleRow()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Constant");
            NodeDefinition not = circuit.AddNode(Not, new CircuitPoint(0, 0));
            NodeDefinition output = circuit.AddOutputBoundary("ONE", new CircuitPoint(5, 0));
            circuit.Connect(new PinRef(not.Id, NotGate.Output), BoundaryIn(output));

            TruthTableData table = Generate(circuit);
            Assert.That(table.RowCount, Is.EqualTo(1));
            Assert.That(Rows(table), Is.EqualTo(new[] { "|1" }));
        }

        // ------------------------------------------------------------------------------------------------
        // Isolation
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void Generation_LeavesTheLiveSimulationUntouched()
        {
            CircuitDefinition nand = BuildNand();
            CompiledCircuit compiled = SimulationTestUtil.Compile(_env, nand);
            var runner = new SimulationRunner();
            runner.Load(compiled);
            runner.SetInput(0, Signal.High);
            runner.Context.Changes.Clear();

            var netsBefore = new Signal[compiled.NetCount];
            runner.Context.CopyNets(netsBefore);
            long evaluationsBefore = runner.EvaluationCount;

            // Reuse the very same compiled circuit as the live runner (the most sharing possible).
            TruthTablePlan plan = _generator.Plan(compiled);
            TruthTableJob job = _generator.Start(plan);
            job.RunToCompletion();
            Assert.That(job.Status, Is.EqualTo(TruthTableStatus.Completed));
            Assert.That(job.EvaluationCount, Is.GreaterThan(0));

            var netsAfter = new Signal[compiled.NetCount];
            runner.Context.CopyNets(netsAfter);
            Assert.That(netsAfter, Is.EqualTo(netsBefore), "Live net values changed.");
            Assert.That(runner.Context.GetInput(0), Is.EqualTo(Signal.High));
            Assert.That(runner.Context.GetInput(1), Is.EqualTo(Signal.Low));
            Assert.That(runner.EvaluationCount, Is.EqualTo(evaluationsBefore));
            Assert.That(runner.Context.Changes.Count, Is.Zero, "Live change log received events.");
        }

        [Test]
        public void Generation_DoesNotModifyTheCircuitDefinition()
        {
            CircuitDefinition nand = BuildNand();
            string before = CircuitFingerprint.Of(nand);
            Generate(nand);
            Assert.That(CircuitFingerprint.Of(nand), Is.EqualTo(before));
        }

        // ------------------------------------------------------------------------------------------------
        // Limits, errors, cancellation
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void TooManyInputs_AreRefusedWithAClearError()
        {
            var generator = new TruthTableGenerator(_env.Catalog, new TruthTableLimits { MaxInputs = 4 });
            TruthTablePlan plan = generator.Plan(WideCircuit(5));

            Assert.That(plan.CanGenerate, Is.False);
            Assert.That(plan.Report.Contains(DiagnosticCode.TruthTableTooLarge), Is.True);
            Assert.That(plan.FirstError, Does.Contain("5 inputs").And.Contain("limit is 4"));
            Assert.That(() => generator.Start(plan), Throws.InvalidOperationException);

            TruthTableJob refused = generator.Generate(WideCircuit(5));
            Assert.That(refused.Status, Is.EqualTo(TruthTableStatus.Failed));
            Assert.That(refused.Table, Is.Null);
            Assert.That(refused.Error, Does.Contain("limit"));
        }

        [Test]
        public void TooManyRows_AreRefused_AndLargeTablesNeedConfirmation()
        {
            var limits = new TruthTableLimits { MaxInputs = 20, MaxRows = 256, ConfirmRows = 16 };
            var generator = new TruthTableGenerator(_env.Catalog, limits);

            TruthTablePlan small = generator.Plan(WideCircuit(4)); // 16 rows
            Assert.That(small.CanGenerate, Is.True);
            Assert.That(small.RequiresConfirmation, Is.False);

            TruthTablePlan large = generator.Plan(WideCircuit(6)); // 64 rows
            Assert.That(large.CanGenerate, Is.True);
            Assert.That(large.RequiresConfirmation, Is.True);
            Assert.That(large.Report.HasWarnings, Is.True);

            TruthTablePlan tooLarge = generator.Plan(WideCircuit(9)); // 512 rows
            Assert.That(tooLarge.CanGenerate, Is.False);
            Assert.That(tooLarge.RowCount, Is.EqualTo(512));
            Assert.That(tooLarge.FirstError, Does.Contain("512").And.Contain("256"));
        }

        [Test]
        public void DefaultLimits_AreBoundedByTheAbsoluteCeiling()
        {
            var limits = new TruthTableLimits { MaxInputs = 100, MaxRows = int.MaxValue };
            Assert.That(limits.EffectiveMaxInputs, Is.EqualTo(TruthTableLimits.AbsoluteMaxInputs));
            Assert.That(limits.EffectiveMaxRows, Is.EqualTo(1 << TruthTableLimits.AbsoluteMaxInputs));
            Assert.That(new TruthTableLimits().EffectiveMaxInputs, Is.EqualTo(TruthTableLimits.DefaultMaxInputs));
            Assert.That(TruthTableLimits.RowsFor(0), Is.EqualTo(1));
            Assert.That(TruthTableLimits.RowsFor(70), Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void CircuitWithoutOutputs_ReportsAUsefulError()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Sink");
            circuit.AddInputBoundary("A", new CircuitPoint(0, 0));

            TruthTablePlan plan = _generator.Plan(circuit);
            Assert.That(plan.CanGenerate, Is.False);
            Assert.That(plan.Report.Contains(DiagnosticCode.NoOutputs), Is.True);
            Assert.That(plan.FirstError, Does.Contain("no outputs"));
        }

        [Test]
        public void MissingCustomGateDefinition_ReportsAUsefulError()
        {
            CircuitDefinition nand = BuildNand(); // never published
            CircuitDefinition user = Wrap(nand, "Uses missing NAND");

            TruthTablePlan plan = _generator.Plan(user);
            Assert.That(plan.CanGenerate, Is.False);
            Assert.That(plan.Circuit, Is.Null);
            Assert.That(plan.Report.HasErrors, Is.True);
            Assert.That(plan.FirstError, Is.Not.Null.And.Not.Empty);
            Assert.That(plan.Inputs.Count, Is.EqualTo(2), "Columns are still described for the UI.");
        }

        [Test]
        public void FeedbackLoop_IsRefused_BecauseATruthTableCannotDescribeState()
        {
            // NOT feeding itself through an AND with input A: a combinational cycle.
            var circuit = new CircuitDefinition(DefinitionId.New(), "Loop");
            NodeDefinition a = circuit.AddInputBoundary("A", new CircuitPoint(0, 0));
            NodeDefinition and = circuit.AddNode(And, new CircuitPoint(3, 0));
            NodeDefinition not = circuit.AddNode(Not, new CircuitPoint(6, 0));
            NodeDefinition output = circuit.AddOutputBoundary("OUT", new CircuitPoint(9, 0));
            circuit.Connect(BoundaryOut(a), new PinRef(and.Id, AndGate.InputA));
            circuit.Connect(new PinRef(not.Id, NotGate.Output), new PinRef(and.Id, AndGate.InputB));
            circuit.Connect(new PinRef(and.Id, AndGate.Output), new PinRef(not.Id, NotGate.Input));
            circuit.Connect(new PinRef(not.Id, NotGate.Output), BoundaryIn(output));

            TruthTablePlan plan = _generator.Plan(circuit);
            Assert.That(plan.CanGenerate, Is.False);
            Assert.That(plan.FirstError, Does.Contain("feedback loop").And.Contain("combinational"));
        }

        [Test]
        public void Cancel_StopsTheJob_AndKeepsTheRowsAlreadyProduced()
        {
            TruthTableJob job = _generator.Start(_generator.Plan(WideCircuit(6)));
            Assert.That(job.Status, Is.EqualTo(TruthTableStatus.Pending));

            Assert.That(job.Advance(5), Is.EqualTo(5));
            Assert.That(job.Status, Is.EqualTo(TruthTableStatus.Running));
            Assert.That(job.Progress, Is.EqualTo(5f / 64f).Within(1e-6));

            job.Cancel();
            Assert.That(job.Status, Is.EqualTo(TruthTableStatus.Cancelled));
            Assert.That(job.IsFinished, Is.True);
            Assert.That(job.CompletedRows, Is.EqualTo(5));
            Assert.That(job.Advance(10), Is.Zero, "A cancelled job never resumes.");
            Assert.That(job.Table.GetOutput(4, 0), Is.EqualTo(Signal.Low));
            Assert.That(() => job.Table.GetOutput(5, 0), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void CancellationToken_StopsRunToCompletion()
        {
            using (var source = new CancellationTokenSource())
            {
                source.Cancel();
                TruthTableJob job = _generator.Start(_generator.Plan(WideCircuit(8)));
                job.RunToCompletion(source.Token);
                Assert.That(job.Status, Is.EqualTo(TruthTableStatus.Cancelled));
                Assert.That(job.CompletedRows, Is.Zero);
            }

            using (var source = new CancellationTokenSource())
            {
                source.Cancel();
                TruthTableJob viaGenerate = _generator.Generate(WideCircuit(3), source.Token);
                Assert.That(viaGenerate.Status, Is.EqualTo(TruthTableStatus.Cancelled));
            }
        }

        [Test]
        public void TimeSlicedGeneration_ProgressesAcrossSlices_AndCompletes()
        {
            TruthTableJob job = _generator.Start(_generator.Plan(WideCircuit(12))); // 4096 rows
            int slices = 0;
            while (job.AdvanceFor(0.05))
            {
                slices++;
                Assert.That(job.CompletedRows, Is.GreaterThan(0));
                Assert.That(slices, Is.LessThan(100000));
            }

            Assert.That(job.Status, Is.EqualTo(TruthTableStatus.Completed));
            Assert.That(slices, Is.GreaterThan(0), "A 0.05 ms budget should need several slices.");
            Assert.That(job.Table.GetOutput(job.TotalRows - 1, 0), Is.EqualTo(Signal.High));
            Assert.That(job.Table.GetOutput(job.TotalRows / 2, 0), Is.EqualTo(Signal.Low)); // 1000...0: I1 low
        }

        [Test]
        public void LargeTable_IsEvaluatedIncrementally_NotFromScratchPerRow()
        {
            CircuitDefinition circuit = WideCircuit(10);
            TruthTableJob job = _generator.Generate(circuit);
            Assert.That(job.Status, Is.EqualTo(TruthTableStatus.Completed));

            // One AND gate: the event-driven engine only evaluates it when I0 or I1 changes, which happens at most
            // once per row (plus the initial settle), far less than one evaluation per input per row.
            Assert.That(job.EvaluationCount, Is.LessThanOrEqualTo(job.TotalRows + 1));
        }

        // ------------------------------------------------------------------------------------------------
        // Export
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void Csv_ContainsHeadersAndEveryRow()
        {
            TruthTableData table = Generate(BuildNand());
            Assert.That(TruthTableExporter.ToCsv(table),
                Is.EqualTo("A,B,OUT\r\n0,0,1\r\n0,1,1\r\n1,0,1\r\n1,1,0\r\n"));
            Assert.That(TruthTableExporter.ToTabSeparated(table),
                Is.EqualTo("A\tB\tOUT\r\n0\t0\t1\r\n0\t1\t1\r\n1\t0\t1\r\n1\t1\t0\r\n"));
        }

        [Test]
        public void Csv_EscapesPortNames()
        {
            CircuitDefinition nand = BuildNand();
            nand.RenameInterfacePort(nand.Inputs[0].Id, "Carry, in");
            nand.RenameInterfacePort(nand.Outputs[0].Id, "Say \"hi\"");

            string csv = TruthTableExporter.ToCsv(Generate(nand));
            Assert.That(csv, Does.StartWith("\"Carry, in\",B,\"Say \"\"hi\"\"\"\r\n"));
            Assert.That(TruthTableExporter.ToTabSeparated(Generate(nand)), Does.StartWith("Carry, in\tB\tSay \"hi\"\r\n"));
        }

        [Test]
        public void Export_OfACancelledTable_ContainsOnlyCompletedRows()
        {
            TruthTableJob job = _generator.Start(_generator.Plan(BuildNand()));
            job.Advance(2);
            job.Cancel();
            Assert.That(TruthTableExporter.ToCsv(job.Table), Is.EqualTo("A,B,OUT\r\n0,0,1\r\n0,1,1\r\n"));
        }

        [Test]
        public void SuggestedFileName_IsFileSystemSafe()
        {
            TruthTableData table = Generate(BuildNand("Full Adder/v2"));
            Assert.That(TruthTableExporter.SuggestFileName(table), Is.EqualTo("Full_Adder_v2_truth_table"));
        }
    }
}
