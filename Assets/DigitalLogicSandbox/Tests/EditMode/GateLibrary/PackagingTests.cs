using System.Collections.Generic;
using DigitalLogicSandbox.Commands.Circuit;
using DigitalLogicSandbox.Commands.Library;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.GateLibrary.Packaging;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.SimulationTestUtil;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.GateLibrary
{
    public sealed class PackagingTests
    {
        private TestEnvironment _env;
        private CircuitPackager _packager;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
            _packager = new CircuitPackager(_env.Catalog, _env.Library);
        }

        private CircuitDefinition PackageAndPublish(CircuitDefinition source, string name)
        {
            PackagingSpec spec = PackagingSpec.FromCircuit(source);
            spec.Name = name;
            PackagingResult result = _packager.Package(source, spec);
            Assert.That(result.Succeeded, Is.True, result.Report.ToString());
            _env.Library.Add(result.Definition);
            return result.Definition;
        }

        /// <summary>Truth table of a published gate, evaluated through an instance of it (as a user would place it).</summary>
        private SimulationContext InstanceContext(CircuitDefinition gate)
        {
            return CreateContext(_env, Wrap(gate, "Host"));
        }

        [Test]
        public void PackageNand_FromAndAndNot_BehavesAsNand()
        {
            CircuitDefinition nand = PackageAndPublish(BuildNand("Scratch"), "NAND");

            Assert.That(nand.Name, Is.EqualTo("NAND"));
            Assert.That(nand.IsReadOnly, Is.True, "Published definitions are frozen.");
            Assert.That(nand.Inputs.Count, Is.EqualTo(2));
            Assert.That(nand.Outputs.Count, Is.EqualTo(1));
            AssertTruthTable(InstanceContext(nand), 1, 1, 1, 0);
        }

        [Test]
        public void PackageOr_FromPackagedNand_IsNestedAndCorrect()
        {
            CircuitDefinition nand = PackageAndPublish(BuildNand("Scratch"), "NAND");
            CircuitDefinition orSource = BuildOrFromNand(nand.Reference, nand, "OR scratch");
            CircuitDefinition or = PackageAndPublish(orSource, "OR");

            int customNodes = 0;
            foreach (NodeDefinition node in or.Nodes)
            {
                if (node.Component.Kind == ComponentKind.Custom)
                {
                    customNodes++;
                    Assert.That(node.Component.Definition, Is.EqualTo(nand.Reference),
                        "Nested gates reference the shared definition instead of copying it.");
                }
            }

            Assert.That(customNodes, Is.EqualTo(3));
            AssertTruthTable(InstanceContext(or), 0, 1, 1, 1);
        }

        [Test]
        public void PackageOr_FromPrimitives_MatchesExpectedTable()
        {
            CircuitDefinition or = PackageAndPublish(BuildOrFromPrimitives("Scratch"), "OR");
            AssertTruthTable(InstanceContext(or), 0, 1, 1, 1);
        }

        [Test]
        public void Packaging_DoesNotModifyTheOriginalCircuit()
        {
            CircuitDefinition source = BuildNand("Scratch");
            string before = CircuitFingerprint.Of(source);
            PackagingSpec spec = PackagingSpec.FromCircuit(source);
            spec.Name = "NAND";
            spec.Inputs[0].Name = "X";
            PackagingSpec.Move(spec.Inputs, 0, 1);

            CircuitDefinition gate = _packager.Package(source, spec).Definition;
            _env.Library.Add(gate);

            Assert.That(CircuitFingerprint.Of(source), Is.EqualTo(before));
            Assert.That(source.IsReadOnly, Is.False);
            Assert.That(gate.Id, Is.Not.EqualTo(source.Id), "Packaging generates a new unique id.");
        }

        [Test]
        public void PortNamesAndOrder_FollowTheSpec_AndPortIdsAreStable()
        {
            CircuitDefinition source = BuildNand("Scratch");
            PortId sourceA = source.Inputs[0].Id;
            PortId sourceB = source.Inputs[1].Id;
            PackagingSpec spec = PackagingSpec.FromCircuit(source);
            spec.Name = "NAND";
            spec.Inputs[0].Name = "LEFT";
            spec.Inputs[1].Name = "RIGHT";
            spec.Outputs[0].Name = "Q";
            PackagingSpec.Move(spec.Inputs, 1, 0);

            CircuitDefinition gate = _packager.Package(source, spec).Definition;

            Assert.That(gate.Inputs[0].Name, Is.EqualTo("RIGHT"));
            Assert.That(gate.Inputs[0].Id, Is.EqualTo(sourceB));
            Assert.That(gate.Inputs[1].Name, Is.EqualTo("LEFT"));
            Assert.That(gate.Inputs[1].Id, Is.EqualTo(sourceA));
            Assert.That(gate.Outputs[0].Name, Is.EqualTo("Q"));
            ComponentSignature signature = gate.GetSignature();
            Assert.That(signature.Inputs[0].Name, Is.EqualTo("RIGHT"));
        }

        [Test]
        public void Packaging_PreservesLayoutAndMetadata()
        {
            CircuitDefinition source = BuildNand("Scratch");
            PackagingSpec spec = PackagingSpec.FromCircuit(source);
            spec.Name = "  NAND  ";
            spec.Description = "Not-and";
            spec.Category = "Logic";
            spec.Icon = "&!";

            CircuitDefinition gate = _packager.Package(source, spec).Definition;

            Assert.That(gate.Name, Is.EqualTo("NAND"));
            Assert.That(gate.Description, Is.EqualTo("Not-and"));
            Assert.That(gate.Category, Is.EqualTo("Logic"));
            Assert.That(gate.Icon, Is.EqualTo("&!"));
            foreach (NodeDefinition node in source.Nodes)
            {
                Assert.That(gate.TryGetNode(node.Id, out NodeDefinition copy), Is.True);
                Assert.That(copy.Position, Is.EqualTo(node.Position));
            }

            Assert.That(gate.Connections.Count, Is.EqualTo(source.Connections.Count));
        }

        [Test]
        public void MultipleInstances_EvaluateIndependently()
        {
            CircuitDefinition nand = PackageAndPublish(BuildNand("Scratch"), "NAND");
            var host = new CircuitDefinition(DefinitionId.New(), "Two NANDs");
            NodeDefinition a = host.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition b = host.AddInputBoundary("B", new CircuitPoint(0, 2));
            NodeDefinition first = host.AddNode(ComponentRef.Custom(nand.Reference), new CircuitPoint(4, 0));
            NodeDefinition second = host.AddNode(ComponentRef.Custom(nand.Reference), new CircuitPoint(4, 4));
            NodeDefinition out1 = host.AddOutputBoundary("Q1", new CircuitPoint(8, 0));
            NodeDefinition out2 = host.AddOutputBoundary("Q2", new CircuitPoint(8, 4));
            host.Connect(BoundaryOut(a), new PinRef(first.Id, nand.Inputs[0].Id));
            host.Connect(BoundaryOut(a), new PinRef(first.Id, nand.Inputs[1].Id));
            host.Connect(BoundaryOut(b), new PinRef(second.Id, nand.Inputs[0].Id));
            host.Connect(BoundaryOut(b), new PinRef(second.Id, nand.Inputs[1].Id));
            host.Connect(new PinRef(first.Id, nand.Outputs[0].Id), BoundaryIn(out1));
            host.Connect(new PinRef(second.Id, nand.Outputs[0].Id), BoundaryIn(out2));

            SimulationContext context = CreateContext(_env, host);
            int[] outputs = Evaluate(context, 1, 0);
            Assert.That(outputs, Is.EqualTo(new[] { 0, 1 }), "Instance state is not shared.");
            outputs = Evaluate(context, 0, 1);
            Assert.That(outputs, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void PackagingSelection_IncludesOnlySelectedNodes_AndWarnsAboutCutWires()
        {
            CircuitDefinition source = BuildNand("Scratch");
            // Select input A, the AND and output: input B's wire into the AND is cut.
            NodeId a = source.Nodes[0].Id;
            NodeId and = source.Nodes[2].Id;
            NodeId not = source.Nodes[3].Id;
            NodeId output = source.Nodes[4].Id;
            PackagingSpec spec = PackagingSpec.FromCircuit(source, new[] { a, and, not, output });
            spec.Name = "Partial";

            PackagingResult result = _packager.Package(source, spec);

            Assert.That(result.Succeeded, Is.True, result.Report.ToString());
            Assert.That(spec.Inputs.Count, Is.EqualTo(1));
            Assert.That(result.Definition.Nodes.Count, Is.EqualTo(4));
            Assert.That(result.Report.Contains(DiagnosticCode.ExternalConnection), Is.True);
            Assert.That(result.Definition.Connections.Count, Is.EqualTo(3));
        }

        [Test]
        public void Validation_RequiresName()
        {
            CircuitDefinition source = BuildNand("Scratch");
            PackagingSpec spec = PackagingSpec.FromCircuit(source);
            spec.Name = "   ";
            PackagingResult result = _packager.Package(source, spec);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Report.Contains(DiagnosticCode.MissingName), Is.True);
        }

        [Test]
        public void Validation_RequiresAtLeastOneConnectedOutput()
        {
            var source = new CircuitDefinition(DefinitionId.New(), "No outputs");
            NodeDefinition a = source.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition not = source.AddNode(Not, new CircuitPoint(3, 0));
            source.Connect(BoundaryOut(a), new PinRef(not.Id, NotGate.Input));
            PackagingSpec spec = PackagingSpec.FromCircuit(source);
            spec.Name = "Broken";
            Assert.That(_packager.Package(source, spec).Report.Contains(DiagnosticCode.NoOutputs), Is.True);

            source.AddOutputBoundary("OUT", new CircuitPoint(6, 0)); // present but unconnected
            spec = PackagingSpec.FromCircuit(source);
            spec.Name = "Broken";
            PackagingResult result = _packager.Package(source, spec);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Report.Contains(DiagnosticCode.NoOutputs), Is.True);
        }

        [Test]
        public void Validation_RejectsDuplicateAndEmptyPortNames()
        {
            CircuitDefinition source = BuildNand("Scratch");
            PackagingSpec spec = PackagingSpec.FromCircuit(source);
            spec.Name = "NAND";
            spec.Inputs[1].Name = "A";
            Assert.That(_packager.Package(source, spec).Report.Contains(DiagnosticCode.DuplicatePortName), Is.True);

            spec.Inputs[1].Name = " ";
            PackagingResult result = _packager.Package(source, spec);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Report.Contains(DiagnosticCode.EmptyPortName), Is.True);
        }

        [Test]
        public void Validation_RejectsCombinationalFeedback()
        {
            var source = new CircuitDefinition(DefinitionId.New(), "Loop");
            NodeDefinition not = source.AddNode(Not, CircuitPoint.Zero);
            NodeDefinition not2 = source.AddNode(Not, new CircuitPoint(3, 0));
            NodeDefinition output = source.AddOutputBoundary("OUT", new CircuitPoint(6, 0));
            source.Connect(new PinRef(not.Id, NotGate.Output), new PinRef(not2.Id, NotGate.Input));
            source.Connect(new PinRef(not2.Id, NotGate.Output), new PinRef(not.Id, NotGate.Input));
            source.Connect(new PinRef(not2.Id, NotGate.Output), BoundaryIn(output));
            PackagingSpec spec = PackagingSpec.FromCircuit(source);
            spec.Name = "Oscillator";

            PackagingResult result = _packager.Package(source, spec);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Report.Contains(DiagnosticCode.CombinationalCycle), Is.True);
        }

        [Test]
        public void Validation_RejectsMissingComponentReferences()
        {
            var source = new CircuitDefinition(DefinitionId.New(), "Missing");
            NodeDefinition ghost = source.AddNode(ComponentRef.Custom(new DefinitionRef(DefinitionId.New(), 1)), CircuitPoint.Zero);
            NodeDefinition output = source.AddOutputBoundary("OUT", new CircuitPoint(6, 0));
            source.Connect(BoundaryOut(source.AddInputBoundary("A", new CircuitPoint(-4, 0))),
                BoundaryIn(output));
            PackagingSpec spec = PackagingSpec.FromCircuit(source);
            spec.Name = "Bad";

            PackagingResult result = _packager.Package(source, spec);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Report.Contains(DiagnosticCode.MissingComponent) ||
                result.Report.Contains(DiagnosticCode.MissingDefinition), Is.True);
            Assert.That(ghost, Is.Not.Null);
        }

        [Test]
        public void Validation_RejectsSpecsThatDoNotMapEveryBoundary()
        {
            CircuitDefinition source = BuildNand("Scratch");
            PackagingSpec spec = PackagingSpec.FromCircuit(source);
            spec.Name = "NAND";
            spec.Inputs.RemoveAt(1);
            Assert.That(_packager.Package(source, spec).Report.Contains(DiagnosticCode.InterfaceMismatch), Is.True);

            spec = PackagingSpec.FromCircuit(source);
            spec.Name = "NAND";
            spec.Inputs.Add(new PortSpec(source.Outputs[0].Id, "WRONG"));
            Assert.That(_packager.Package(source, spec).Succeeded, Is.False);
        }

        [Test]
        public void Validation_WarnsAboutUnusedInputsAndDuplicateNames()
        {
            CircuitDefinition nand = PackageAndPublish(BuildNand("Scratch"), "NAND");
            CircuitDefinition source = BuildNand("Scratch 2");
            source.AddInputBoundary("C", new CircuitPoint(0, -3)); // intentionally unused
            PackagingSpec spec = PackagingSpec.FromCircuit(source);
            spec.Name = "nand";

            PackagingResult result = _packager.Package(source, spec);
            Assert.That(result.Succeeded, Is.True, "Unused inputs are allowed with a warning.");
            Assert.That(result.Report.Contains(DiagnosticCode.UnusedInterfaceInput), Is.True);
            Assert.That(result.Report.Contains(DiagnosticCode.DuplicateGateName), Is.True);
            Assert.That(nand, Is.Not.Null);
        }

        [Test]
        public void Validation_WarnsWhenGateHasNoInputs()
        {
            var source = new CircuitDefinition(DefinitionId.New(), "Constant");
            NodeDefinition and = source.AddNode(And, CircuitPoint.Zero);
            NodeDefinition output = source.AddOutputBoundary("OUT", new CircuitPoint(4, 0));
            source.Connect(new PinRef(and.Id, AndGate.Output), BoundaryIn(output));
            PackagingSpec spec = PackagingSpec.FromCircuit(source);
            spec.Name = "Zero";

            PackagingResult result = _packager.Package(source, spec);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Report.Contains(DiagnosticCode.NoInputs), Is.True);
        }

        [Test]
        public void HierarchyValidation_RejectsCircularReferencesInLibrary()
        {
            // A definition can never contain itself, even indirectly; the validator rejects such graphs.
            CircuitDefinition nand = PackageAndPublish(BuildNand("Scratch"), "NAND");
            CircuitDefinition self = nand.CloneAsVersion(2);
            self.AddNode(ComponentRef.Custom(self.Reference), new CircuitPoint(20, 0));

            var report = new ValidationReport();
            new HierarchyValidator(_env.Library).Validate(self, report);
            Assert.That(report.Contains(DiagnosticCode.CircularReference), Is.True);
        }

        [Test]
        public void PublishCommand_UndoRemovesGate_RedoRestoresIt()
        {
            CircuitDefinition source = BuildNand("Scratch");
            PackagingSpec spec = PackagingSpec.FromCircuit(source);
            spec.Name = "NAND";
            CircuitDefinition gate = _packager.Package(source, spec).Definition;
            var history = new CommandHistory();
            int added = 0;
            int removed = 0;
            _env.Library.DefinitionAdded += _ => added++;
            _env.Library.DefinitionRemoved += _ => removed++;

            history.Execute(new PublishDefinitionCommand(_env.Library, gate));
            Assert.That(_env.Library.Contains(gate.Reference), Is.True);
            history.Undo();
            Assert.That(_env.Library.Contains(gate.Reference), Is.False);
            history.Redo();
            Assert.That(_env.Library.Contains(gate.Reference), Is.True);
            Assert.That(added, Is.EqualTo(2));
            Assert.That(removed, Is.EqualTo(1));
        }

        [Test]
        public void Library_RefusesToRemoveDefinitionsUsedByOtherGates()
        {
            CircuitDefinition nand = PackageAndPublish(BuildNand("Scratch"), "NAND");
            PackageAndPublish(BuildOrFromNand(nand.Reference, nand, "OR scratch"), "OR");

            Assert.That(_env.Library.FindReferencingDefinition(nand.Reference), Is.Not.Null);
            Assert.Throws<System.InvalidOperationException>(() => _env.Library.Remove(nand.Reference));
            Assert.That(_env.Library.Contains(nand.Reference), Is.True);
        }

        [Test]
        public void InterfaceCommands_RenameMoveAndProperties_AreUndoable()
        {
            CircuitDefinition circuit = BuildNand("Scratch");
            string before = CircuitFingerprint.Of(circuit);
            var history = new CommandHistory();
            PortId b = circuit.Inputs[1].Id;

            history.Execute(new RenameInterfacePortCommand(circuit, b, "SEL"));
            history.Execute(new MoveInterfacePortCommand(circuit, b, 0));
            history.Execute(new SetCircuitPropertiesCommand(circuit, "Renamed", "Desc", "Cat"));
            Assert.That(circuit.Inputs[0].Name, Is.EqualTo("SEL"));
            Assert.That(circuit.Name, Is.EqualTo("Renamed"));

            history.Undo();
            history.Undo();
            history.Undo();
            Assert.That(CircuitFingerprint.Of(circuit), Is.EqualTo(before));
            Assert.That(circuit.Name, Is.EqualTo("Scratch"));
            Assert.That(circuit.Category, Is.EqualTo(string.Empty));
        }

        [Test]
        public void SpecFromCircuit_DefaultsToInterfaceOrderAndNames()
        {
            CircuitDefinition circuit = BuildNand("Scratch");
            PackagingSpec spec = PackagingSpec.FromCircuit(circuit);
            var names = new List<string>();
            foreach (PortSpec port in spec.Inputs)
            {
                names.Add(port.Name);
            }

            Assert.That(names, Is.EqualTo(new[] { "A", "B" }));
            Assert.That(spec.Outputs[0].Name, Is.EqualTo("OUT"));
            Assert.That(spec.IsSubset, Is.False);
        }
    }
}
