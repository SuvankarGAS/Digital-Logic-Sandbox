using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Simulation.Diagnostics;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Simulation.Propagation;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.SimulationTestUtil;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.Simulation
{
    public sealed class SimulationEngineTests
    {
        private TestEnvironment _env;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
        }

        // ------------------------------------------------------------------------------------------------
        // Builders
        // ------------------------------------------------------------------------------------------------

        private static CircuitDefinition SingleGate(ComponentRef gate, int inputs)
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Single");
            NodeDefinition node = circuit.AddNode(gate, CircuitPoint.Zero);
            if (inputs == 2)
            {
                NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero);
                NodeDefinition b = circuit.AddInputBoundary("B", CircuitPoint.Zero);
                circuit.Connect(BoundaryOut(a), new PinRef(node.Id, AndGate.InputA));
                circuit.Connect(BoundaryOut(b), new PinRef(node.Id, AndGate.InputB));
                NodeDefinition output = circuit.AddOutputBoundary("OUT", CircuitPoint.Zero);
                circuit.Connect(new PinRef(node.Id, AndGate.Output), BoundaryIn(output));
            }
            else
            {
                NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero);
                circuit.Connect(BoundaryOut(a), new PinRef(node.Id, NotGate.Input));
                NodeDefinition output = circuit.AddOutputBoundary("OUT", CircuitPoint.Zero);
                circuit.Connect(new PinRef(node.Id, NotGate.Output), BoundaryIn(output));
            }

            return circuit;
        }

        /// <summary>Input -> chain of NOTs -> output.</summary>
        private static CircuitDefinition NotChain(CircuitDefinition circuit, string inputName, int length)
        {
            NodeDefinition input = circuit.AddInputBoundary(inputName, CircuitPoint.Zero);
            PinRef previous = BoundaryOut(input);
            for (int i = 0; i < length; i++)
            {
                NodeDefinition not = circuit.AddNode(Not, CircuitPoint.Zero);
                circuit.Connect(previous, new PinRef(not.Id, NotGate.Input));
                previous = new PinRef(not.Id, NotGate.Output);
            }

            NodeDefinition output = circuit.AddOutputBoundary(inputName + "_OUT", CircuitPoint.Zero);
            circuit.Connect(previous, BoundaryIn(output));
            return circuit;
        }

        // ------------------------------------------------------------------------------------------------
        // Gate and circuit behaviour
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void And_TruthTable()
        {
            AssertTruthTable(CreateContext(_env, SingleGate(And, 2)), 0, 0, 0, 1);
        }

        [Test]
        public void Not_TruthTable()
        {
            AssertTruthTable(CreateContext(_env, SingleGate(Not, 1)), 1, 0);
        }

        [Test]
        public void Nand_FromAndAndNot()
        {
            AssertTruthTable(CreateContext(_env, BuildNand()), 1, 1, 1, 0);
        }

        [Test]
        public void Or_FromThreeNotsAndAnd()
        {
            AssertTruthTable(CreateContext(_env, BuildOrFromPrimitives()), 0, 1, 1, 1);
        }

        [Test]
        public void Or_FromPackagedNand_BehavesLikeOr()
        {
            CircuitDefinition nand = BuildNand();
            DefinitionRef nandRef = _env.Publish(nand);

            SimulationContext context = CreateContext(_env, BuildOrFromNand(nandRef, nand));

            AssertTruthTable(context, 0, 1, 1, 1);
            Assert.That(context.Circuit.GateCount, Is.EqualTo(6), "3 NAND instances x (AND + NOT).");
            Assert.That(context.Circuit.HierarchyDepth, Is.EqualTo(1));
        }

        [Test]
        public void PackagedOr_UsedAsGate_InAnotherCircuit()
        {
            CircuitDefinition nand = BuildNand();
            DefinitionRef nandRef = _env.Publish(nand);
            CircuitDefinition or = BuildOrFromNand(nandRef, nand);
            _env.Publish(or);

            SimulationContext context = CreateContext(_env, Wrap(or, "Main"));

            AssertTruthTable(context, 0, 1, 1, 1);
            Assert.That(context.Circuit.HierarchyDepth, Is.EqualTo(2));
        }

        [Test]
        public void DeeplyNestedWrappers_PreserveBehaviour()
        {
            CircuitDefinition current = BuildNand("L0");
            _env.Publish(current);
            for (int level = 1; level <= 10; level++)
            {
                current = Wrap(current, "L" + level);
                _env.Publish(current);
            }

            SimulationContext context = CreateContext(_env, Wrap(current, "Root"));

            AssertTruthTable(context, 1, 1, 1, 0);
            Assert.That(context.Circuit.GateCount, Is.EqualTo(2), "Wrappers add no logic.");
            Assert.That(context.Circuit.HierarchyDepth, Is.EqualTo(11));
        }

        [Test]
        public void MultipleInstances_EvaluateIndependently()
        {
            CircuitDefinition nand = BuildNand();
            DefinitionRef nandRef = _env.Publish(nand);
            var circuit = new CircuitDefinition(DefinitionId.New(), "TwoNands");
            NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition b = circuit.AddInputBoundary("B", CircuitPoint.Zero);
            NodeDefinition first = circuit.AddNode(ComponentRef.Custom(nandRef), CircuitPoint.Zero);
            NodeDefinition second = circuit.AddNode(ComponentRef.Custom(nandRef), CircuitPoint.Zero);
            NodeDefinition out1 = circuit.AddOutputBoundary("Y1", CircuitPoint.Zero);
            NodeDefinition out2 = circuit.AddOutputBoundary("Y2", CircuitPoint.Zero);
            var nandA = nand.Inputs[0].Id;
            var nandB = nand.Inputs[1].Id;
            var nandOut = nand.Outputs[0].Id;
            // first = NAND(A, A), second = NAND(B, B)
            circuit.Connect(BoundaryOut(a), new PinRef(first.Id, nandA));
            circuit.Connect(BoundaryOut(a), new PinRef(first.Id, nandB));
            circuit.Connect(BoundaryOut(b), new PinRef(second.Id, nandA));
            circuit.Connect(BoundaryOut(b), new PinRef(second.Id, nandB));
            circuit.Connect(new PinRef(first.Id, nandOut), BoundaryIn(out1));
            circuit.Connect(new PinRef(second.Id, nandOut), BoundaryIn(out2));

            SimulationContext context = CreateContext(_env, circuit);

            Assert.That(Evaluate(context, 1, 0), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(Evaluate(context, 0, 1), Is.EqualTo(new[] { 1, 0 }));

            Assert.That(context.Circuit.Root.TryGetChild(first.Id, out CompiledInstance firstInstance), Is.True);
            Assert.That(context.Circuit.Root.TryGetChild(second.Id, out CompiledInstance secondInstance), Is.True);
            NodeDefinition innerAnd = nand.Nodes[2];
            var innerAndOut = new PinRef(innerAnd.Id, AndGate.Output);
            firstInstance.TryGetOutputPinNet(innerAndOut, out int firstNet);
            secondInstance.TryGetOutputPinNet(innerAndOut, out int secondNet);
            Assert.That(firstNet, Is.Not.EqualTo(secondNet), "Instances must never share nets.");
            Assert.That(context.GetNet(firstNet), Is.EqualTo(Signal.Low));
            Assert.That(context.GetNet(secondNet), Is.EqualTo(Signal.High));
        }

        [Test]
        public void HierarchyMap_ExposesInnerSignalsPerInstance()
        {
            CircuitDefinition nand = BuildNand();
            DefinitionRef nandRef = _env.Publish(nand);
            CircuitDefinition root = Wrap(nand, "Main");
            SimulationContext context = CreateContext(_env, root);
            Evaluate(context, 1, 1);

            CompiledInstance instance = null;
            foreach (CompiledInstance child in context.Circuit.Root.Children.Values)
            {
                instance = child;
            }

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.Definition.Reference, Is.EqualTo(nandRef));
            NodeDefinition innerAnd = nand.Nodes[2];
            Assert.That(instance.TryGetOutputPinNet(new PinRef(innerAnd.Id, AndGate.Output), out int andNet), Is.True);
            Assert.That(context.GetNet(andNet), Is.EqualTo(Signal.High), "Inner AND output for A=B=1.");
            Assert.That(context.GetNet(instance.GetInputPinNet(new PinRef(innerAnd.Id, AndGate.InputA))),
                Is.EqualTo(Signal.High), "Inner boundary resolves to the outer input net.");
            Assert.That(instance.Path, Does.Contain("NAND"));
        }

        [Test]
        public void FanOut_UpdatesAllReaders()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "FanOut");
            NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero);
            for (int i = 0; i < 3; i++)
            {
                NodeDefinition not = circuit.AddNode(Not, CircuitPoint.Zero);
                NodeDefinition output = circuit.AddOutputBoundary("Y" + i, CircuitPoint.Zero);
                circuit.Connect(BoundaryOut(a), new PinRef(not.Id, NotGate.Input));
                circuit.Connect(new PinRef(not.Id, NotGate.Output), BoundaryIn(output));
            }

            SimulationContext context = CreateContext(_env, circuit);

            Assert.That(Evaluate(context, 0), Is.EqualTo(new[] { 1, 1, 1 }));
            Assert.That(Evaluate(context, 1), Is.EqualTo(new[] { 0, 0, 0 }));
            Assert.That(context.Circuit.GetFanOut(context.Circuit.GetInputNet(0)), Is.EqualTo(3));
        }

        [Test]
        public void DisconnectedInputs_ReadLowSafely()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Floating");
            NodeDefinition and = circuit.AddNode(And, CircuitPoint.Zero);
            NodeDefinition not = circuit.AddNode(Not, CircuitPoint.Zero);
            NodeDefinition andOut = circuit.AddOutputBoundary("AND", CircuitPoint.Zero);
            NodeDefinition notOut = circuit.AddOutputBoundary("NOT", CircuitPoint.Zero);
            NodeDefinition undriven = circuit.AddOutputBoundary("NONE", CircuitPoint.Zero);
            circuit.Connect(new PinRef(and.Id, AndGate.Output), BoundaryIn(andOut));
            circuit.Connect(new PinRef(not.Id, NotGate.Output), BoundaryIn(notOut));

            SimulationContext context = CreateContext(_env, circuit);

            Assert.That(context.GetOutput(0), Is.EqualTo(Signal.Low));
            Assert.That(context.GetOutput(1), Is.EqualTo(Signal.High));
            Assert.That(context.GetOutput(2), Is.EqualTo(Signal.Low));
            Assert.That(undriven, Is.Not.Null);
        }

        [Test]
        public void UnconnectedCustomGateInput_ReadsLow()
        {
            CircuitDefinition nand = BuildNand();
            DefinitionRef nandRef = _env.Publish(nand);
            var circuit = new CircuitDefinition(DefinitionId.New(), "HalfConnected");
            NodeDefinition gate = circuit.AddNode(ComponentRef.Custom(nandRef), CircuitPoint.Zero);
            NodeDefinition output = circuit.AddOutputBoundary("OUT", CircuitPoint.Zero);
            circuit.Connect(new PinRef(gate.Id, nand.Outputs[0].Id), BoundaryIn(output));

            SimulationContext context = CreateContext(_env, circuit);

            Assert.That(context.GetOutput(0), Is.EqualTo(Signal.High), "NAND(0, 0) = 1");
        }

        [Test]
        public void InputWiredStraightThroughCustomGate_Works()
        {
            var buffer = new CircuitDefinition(DefinitionId.New(), "Wire");
            NodeDefinition input = buffer.AddInputBoundary("IN", CircuitPoint.Zero);
            NodeDefinition output = buffer.AddOutputBoundary("OUT", CircuitPoint.Zero);
            buffer.Connect(BoundaryOut(input), BoundaryIn(output));
            _env.Publish(buffer);

            SimulationContext context = CreateContext(_env, Wrap(buffer, "Main"));

            Assert.That(Evaluate(context, 1), Is.EqualTo(new[] { 1 }));
            Assert.That(Evaluate(context, 0), Is.EqualTo(new[] { 0 }));
            Assert.That(context.Circuit.GateCount, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------------------------------------
        // Event-driven propagation
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void UnrelatedInput_DoesNotEvaluateUnrelatedGates()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "TwoChains");
            NotChain(circuit, "A", 100);
            NotChain(circuit, "B", 100);
            SimulationContext context = CreateContext(_env, circuit);

            context.SetInput(0, Signal.High);
            SettleResult result = context.Settle();

            Assert.That(result.Evaluations, Is.EqualTo(100), "Only chain A is evaluated.");
            Assert.That(result.Waves, Is.EqualTo(100));
        }

        [Test]
        public void SettingSameValue_DoesNoWork()
        {
            SimulationContext context = CreateContext(_env, BuildNand());

            Assert.That(context.SetInput(0, Signal.Low), Is.False);
            Assert.That(context.Settle().Evaluations, Is.EqualTo(0));
        }

        [Test]
        public void PropagationStopsWhenOutputsDoNotChange()
        {
            // AND(A, B) with B = 0: toggling A re-evaluates the AND but its output stays LOW, so nothing downstream runs.
            var circuit = new CircuitDefinition(DefinitionId.New(), "Masked");
            NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero);
            circuit.AddInputBoundary("B", CircuitPoint.Zero);
            NodeDefinition and = circuit.AddNode(And, CircuitPoint.Zero);
            circuit.Connect(BoundaryOut(a), new PinRef(and.Id, AndGate.InputA));
            PinRef previous = new PinRef(and.Id, AndGate.Output);
            for (int i = 0; i < 50; i++)
            {
                NodeDefinition not = circuit.AddNode(Not, CircuitPoint.Zero);
                circuit.Connect(previous, new PinRef(not.Id, NotGate.Input));
                previous = new PinRef(not.Id, NotGate.Output);
            }

            SimulationContext context = CreateContext(_env, circuit);
            context.SetInput(0, Signal.High);

            Assert.That(context.Settle().Evaluations, Is.EqualTo(1));
        }

        [Test]
        public void ChangeLog_RecordsOnlyChangedNetsInWaveOrder()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Chain");
            NotChain(circuit, "A", 3);
            SimulationContext context = CreateContext(_env, circuit);
            Assert.That(context.Changes.Count, Is.EqualTo(0), "Reset clears the log.");

            context.SetInput(0, Signal.High);
            context.Settle();

            SignalChangeLog log = context.Changes;
            Assert.That(log.Count, Is.EqualTo(4), "Input + 3 NOT outputs.");
            for (int i = 0; i < log.Count; i++)
            {
                Assert.That(log[i].Wave, Is.EqualTo(i));
            }

            Assert.That(log[0].Previous, Is.EqualTo(Signal.Low));
            Assert.That(log[0].Current, Is.EqualTo(Signal.High));
            Assert.That(log[1].Current, Is.EqualTo(Signal.Low));
            Assert.That(log[3].Current, Is.EqualTo(Signal.Low));
        }

        [Test]
        public void FanOutChanges_ShareTheSameWave()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "FanOut");
            NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero);
            for (int i = 0; i < 4; i++)
            {
                NodeDefinition not = circuit.AddNode(Not, CircuitPoint.Zero);
                circuit.Connect(BoundaryOut(a), new PinRef(not.Id, NotGate.Input));
            }

            SimulationContext context = CreateContext(_env, circuit);
            context.SetInput(0, Signal.High);
            context.Settle();

            Assert.That(context.Changes.Count, Is.EqualTo(5));
            for (int i = 1; i < 5; i++)
            {
                Assert.That(context.Changes[i].Wave, Is.EqualTo(1));
            }
        }

        [Test]
        public void RecordChangesDisabled_RecordsNothing()
        {
            SimulationContext context = CreateContext(_env, BuildNand(), new SimulationOptions { RecordChanges = false });
            Evaluate(context, 1, 1);
            Assert.That(context.Changes.Count, Is.EqualTo(0));
        }

        [Test]
        public void StepWave_AdvancesOneWaveAtATime()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Chain");
            NotChain(circuit, "A", 3);
            SimulationContext context = CreateContext(_env, circuit);
            context.SetInput(0, Signal.High);

            Assert.That(context.StepWave().Status, Is.EqualTo(SettleStatus.Pending));
            Assert.That(context.StepWave().Status, Is.EqualTo(SettleStatus.Pending));
            Assert.That(context.GetOutput(0), Is.EqualTo(Signal.High), "Output not reached yet (initially HIGH).");
            Assert.That(context.StepWave().Status, Is.EqualTo(SettleStatus.Stable));
            Assert.That(context.GetOutput(0), Is.EqualTo(Signal.Low));
            Assert.That(context.HasPendingWork, Is.False);
        }

        [Test]
        public void OddNotRing_IsDetectedAsOscillationWithoutHanging()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Ring");
            NodeDefinition n1 = circuit.AddNode(Not, CircuitPoint.Zero);
            NodeDefinition n2 = circuit.AddNode(Not, CircuitPoint.Zero);
            NodeDefinition n3 = circuit.AddNode(Not, CircuitPoint.Zero);
            circuit.Connect(new PinRef(n1.Id, NotGate.Output), new PinRef(n2.Id, NotGate.Input));
            circuit.Connect(new PinRef(n2.Id, NotGate.Output), new PinRef(n3.Id, NotGate.Input));
            circuit.Connect(new PinRef(n3.Id, NotGate.Output), new PinRef(n1.Id, NotGate.Input));

            CompileResult compiled = new CircuitCompiler(_env.Catalog).Compile(circuit);
            Assert.That(compiled.Succeeded, Is.True, "Loops compile (with a warning).");
            Assert.That(compiled.Report.Contains(DiagnosticCode.CombinationalCycle), Is.True);
            Assert.That(compiled.Circuit.HasCombinationalCycles, Is.True);

            var options = new SimulationOptions { MinimumEvaluationBudget = 500 };
            var context = new SimulationContext(compiled.Circuit, options);

            Assert.That(context.LastResult.Status, Is.EqualTo(SettleStatus.Oscillating));
            Assert.That(context.LastResult.Evaluations, Is.EqualTo(500));
            Assert.That(context.UnstableGates.Count, Is.GreaterThan(0));
            Assert.That(context.HasPendingWork, Is.False, "Aborted work is cleared.");

            var report = new ValidationReport();
            SimulationDiagnostics.ReportLastSettle(context, report);
            Assert.That(report.HasErrors, Is.True);
            Assert.That(report.Diagnostics[0].Message, Does.Contain("Ring"));
        }

        [Test]
        public void Reset_ReturnsToInitialState()
        {
            SimulationContext context = CreateContext(_env, BuildNand());
            Evaluate(context, 1, 1);
            Assert.That(context.GetOutput(0), Is.EqualTo(Signal.Low));

            context.Reset();

            Assert.That(context.GetInput(0), Is.EqualTo(Signal.Low));
            Assert.That(context.GetInput(1), Is.EqualTo(Signal.Low));
            Assert.That(context.GetOutput(0), Is.EqualTo(Signal.High));
        }

        [Test]
        public void IndependentContexts_ShareTopologyButNotState()
        {
            CompiledCircuit compiled = Compile(_env, BuildNand());
            var live = new SimulationContext(compiled);
            var analysis = new SimulationContext(compiled);

            Evaluate(live, 1, 1);
            Evaluate(analysis, 0, 0);

            Assert.That(live.GetOutput(0), Is.EqualTo(Signal.Low));
            Assert.That(analysis.GetOutput(0), Is.EqualTo(Signal.High));
        }

        // ------------------------------------------------------------------------------------------------
        // Compilation errors
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void MissingDefinition_FailsCompilation()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Missing");
            circuit.AddNode(ComponentRef.Custom(new DefinitionRef(DefinitionId.New(), 1)), CircuitPoint.Zero);

            CompileResult result = new CircuitCompiler(_env.Catalog).Compile(circuit);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Report.Contains(DiagnosticCode.MissingComponent), Is.True);
        }

        [Test]
        public void CircularDefinitions_FailCompilationWithoutRecursingForever()
        {
            var idA = DefinitionId.New();
            var idB = DefinitionId.New();
            var defB = new CircuitDefinition(idB, "B");
            defB.AddNode(ComponentRef.Custom(new DefinitionRef(idA, 1)), CircuitPoint.Zero);
            var defA = new CircuitDefinition(idA, "A");
            defA.AddNode(ComponentRef.Custom(new DefinitionRef(idB, 1)), CircuitPoint.Zero);
            _env.Publish(defB);
            _env.Publish(defA);

            CompileResult result = new CircuitCompiler(_env.Catalog).Compile(defA);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Report.Contains(DiagnosticCode.CircularReference), Is.True);
        }

        [Test]
        public void ExcessiveDepth_FailsCompilation()
        {
            CircuitDefinition current = BuildNand();
            _env.Publish(current);
            for (int i = 0; i < 4; i++)
            {
                current = Wrap(current, "W" + i);
                _env.Publish(current);
            }

            CompileResult result = new CircuitCompiler(_env.Catalog, new SimulationOptions { MaxHierarchyDepth = 3 })
                .Compile(Wrap(current, "Root"));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Report.Count(DiagnosticCode.HierarchyTooDeep), Is.EqualTo(1));
        }
    }
}
