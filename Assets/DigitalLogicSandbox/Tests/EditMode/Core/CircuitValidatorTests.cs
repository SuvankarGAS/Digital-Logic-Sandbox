using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.Core
{
    public sealed class CircuitValidatorTests
    {
        private TestEnvironment _env;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
        }

        private ValidationReport Validate(CircuitDefinition circuit, ValidationOptions options = null)
        {
            return new CircuitValidator(_env.Catalog, options).Validate(circuit);
        }

        [Test]
        public void Nand_IsValidWithoutWarnings()
        {
            ValidationReport report = Validate(BuildNand());
            Assert.That(report.HasErrors, Is.False, report.ToString());
            Assert.That(report.HasWarnings, Is.False, report.ToString());
        }

        [Test]
        public void OrFromPrimitives_IsValid()
        {
            ValidationReport report = Validate(BuildOrFromPrimitives());
            Assert.That(report.HasErrors, Is.False, report.ToString());
        }

        [Test]
        public void OrFromPackagedNand_IsValid()
        {
            CircuitDefinition nand = BuildNand();
            DefinitionRef nandRef = _env.Publish(nand);

            ValidationReport report = Validate(BuildOrFromNand(nandRef, nand));

            Assert.That(report.HasErrors, Is.False, report.ToString());
        }

        [Test]
        public void UnknownPrimitive_ReportsMissingComponent()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Bad");
            NodeDefinition node = circuit.AddNode(ComponentRef.Primitive("builtin.xor"), CircuitPoint.Zero);

            ValidationReport report = Validate(circuit);

            Assert.That(report.Contains(DiagnosticCode.MissingComponent), Is.True);
            Assert.That(report.Diagnostics[0].Node, Is.EqualTo(node.Id));
        }

        [Test]
        public void UnpublishedCustomGate_ReportsMissingComponent()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Bad");
            circuit.AddNode(ComponentRef.Custom(new DefinitionRef(DefinitionId.New(), 1)), CircuitPoint.Zero);

            Assert.That(Validate(circuit).Contains(DiagnosticCode.MissingComponent), Is.True);
        }

        [Test]
        public void UnconnectedGateInput_IsWarningNotError()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Partial");
            NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition and = circuit.AddNode(And, CircuitPoint.Zero);
            NodeDefinition output = circuit.AddOutputBoundary("OUT", CircuitPoint.Zero);
            circuit.Connect(BoundaryOut(a), new PinRef(and.Id, AndGate.InputA));
            circuit.Connect(new PinRef(and.Id, AndGate.Output), BoundaryIn(output));

            ValidationReport report = Validate(circuit);

            Assert.That(report.HasErrors, Is.False, report.ToString());
            Assert.That(report.Count(DiagnosticCode.UnconnectedInput), Is.EqualTo(1));
            Assert.That(report.Diagnostics[0].Pin, Is.EqualTo(new PinRef(and.Id, AndGate.InputB)));
        }

        [Test]
        public void UnusedInputAndUndrivenOutput_AreWarnings()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Empty");
            circuit.AddInputBoundary("A", CircuitPoint.Zero);
            circuit.AddOutputBoundary("OUT", CircuitPoint.Zero);

            ValidationReport report = Validate(circuit);

            Assert.That(report.HasErrors, Is.False);
            Assert.That(report.Contains(DiagnosticCode.UnusedInterfaceInput), Is.True);
            Assert.That(report.Contains(DiagnosticCode.UndrivenInterfaceOutput), Is.True);
        }

        [Test]
        public void DuplicateInputNames_AreErrors()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Dup");
            circuit.AddInputBoundary("A", CircuitPoint.Zero);
            circuit.AddInputBoundary("A", CircuitPoint.Zero);

            Assert.That(Validate(circuit).Contains(DiagnosticCode.DuplicatePortName), Is.True);
        }

        [Test]
        public void SameNameOnInputAndOutput_IsAllowed()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Buffer");
            NodeDefinition input = circuit.AddInputBoundary("X", CircuitPoint.Zero);
            NodeDefinition output = circuit.AddOutputBoundary("X", CircuitPoint.Zero);
            circuit.Connect(BoundaryOut(input), BoundaryIn(output));

            Assert.That(Validate(circuit).Contains(DiagnosticCode.DuplicatePortName), Is.False);
        }

        [Test]
        public void RequireOutputs_ReportsNoOutputs()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "NoOut");
            circuit.AddInputBoundary("A", CircuitPoint.Zero);

            ValidationReport report = Validate(circuit, new ValidationOptions { RequireOutputs = true });

            Assert.That(report.Contains(DiagnosticCode.NoOutputs), Is.True);
        }

        [Test]
        public void TwoNotLoop_ReportsCombinationalCycle()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Ring");
            NodeDefinition n1 = circuit.AddNode(Not, CircuitPoint.Zero);
            NodeDefinition n2 = circuit.AddNode(Not, CircuitPoint.Zero);
            circuit.Connect(new PinRef(n1.Id, NotGate.Output), new PinRef(n2.Id, NotGate.Input));
            circuit.Connect(new PinRef(n2.Id, NotGate.Output), new PinRef(n1.Id, NotGate.Input));

            ValidationReport report = Validate(circuit);

            Assert.That(report.Count(DiagnosticCode.CombinationalCycle), Is.EqualTo(1), report.ToString());
        }

        [Test]
        public void ThreeNodeLoopThroughAnd_ReportsCycle()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Loop");
            NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition and = circuit.AddNode(And, CircuitPoint.Zero);
            NodeDefinition n1 = circuit.AddNode(Not, CircuitPoint.Zero);
            NodeDefinition n2 = circuit.AddNode(Not, CircuitPoint.Zero);
            circuit.Connect(BoundaryOut(a), new PinRef(and.Id, AndGate.InputA));
            circuit.Connect(new PinRef(and.Id, AndGate.Output), new PinRef(n1.Id, NotGate.Input));
            circuit.Connect(new PinRef(n1.Id, NotGate.Output), new PinRef(n2.Id, NotGate.Input));
            circuit.Connect(new PinRef(n2.Id, NotGate.Output), new PinRef(and.Id, AndGate.InputB));

            Assert.That(Validate(circuit).Count(DiagnosticCode.CombinationalCycle), Is.EqualTo(1));
        }

        [Test]
        public void ReconvergentFanOut_IsNotACycle()
        {
            // A feeds both inputs of an AND through two different paths (diamond), which is acyclic.
            var circuit = new CircuitDefinition(DefinitionId.New(), "Diamond");
            NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition n1 = circuit.AddNode(Not, CircuitPoint.Zero);
            NodeDefinition n2 = circuit.AddNode(Not, CircuitPoint.Zero);
            NodeDefinition and = circuit.AddNode(And, CircuitPoint.Zero);
            NodeDefinition output = circuit.AddOutputBoundary("OUT", CircuitPoint.Zero);
            circuit.Connect(BoundaryOut(a), new PinRef(n1.Id, NotGate.Input));
            circuit.Connect(BoundaryOut(a), new PinRef(n2.Id, NotGate.Input));
            circuit.Connect(new PinRef(n1.Id, NotGate.Output), new PinRef(and.Id, AndGate.InputA));
            circuit.Connect(new PinRef(n2.Id, NotGate.Output), new PinRef(and.Id, AndGate.InputB));
            circuit.Connect(new PinRef(and.Id, AndGate.Output), BoundaryIn(output));

            ValidationReport report = Validate(circuit);

            Assert.That(report.HasErrors, Is.False, report.ToString());
        }

        [Test]
        public void MutuallyReferencingDefinitions_ReportCircularReference()
        {
            var idA = DefinitionId.New();
            var idB = DefinitionId.New();
            var defB = new CircuitDefinition(idB, "B");
            defB.AddNode(ComponentRef.Custom(new DefinitionRef(idA, 1)), CircuitPoint.Zero);
            var defA = new CircuitDefinition(idA, "A");
            defA.AddNode(ComponentRef.Custom(new DefinitionRef(idB, 1)), CircuitPoint.Zero);
            _env.Publish(defB);
            _env.Publish(defA);

            ValidationReport report = Validate(defA);

            Assert.That(report.Contains(DiagnosticCode.CircularReference), Is.True, report.ToString());
        }

        [Test]
        public void DraftContainingAnyVersionOfItself_ReportsCircularReference()
        {
            CircuitDefinition v1 = BuildNand();
            DefinitionRef v1Ref = _env.Publish(v1);
            CircuitDefinition draft = v1.CloneAsVersion(2);
            draft.AddNode(ComponentRef.Custom(v1Ref), CircuitPoint.Zero);

            Assert.That(Validate(draft).Contains(DiagnosticCode.CircularReference), Is.True);
        }

        [Test]
        public void NestingBeyondLimit_ReportsHierarchyTooDeep()
        {
            CircuitDefinition current = BuildNand("L0");
            _env.Publish(current);
            for (int level = 1; level <= 4; level++)
            {
                current = Wrap(current, "L" + level);
                _env.Publish(current);
            }

            CircuitDefinition root = Wrap(current, "Root"); // 5 custom levels below root

            Assert.That(Validate(root, new ValidationOptions { MaxHierarchyDepth = 5 }).HasErrors, Is.False);
            Assert.That(Validate(root, new ValidationOptions { MaxHierarchyDepth = 4 })
                .Contains(DiagnosticCode.HierarchyTooDeep), Is.True);
        }

        [Test]
        public void HierarchyValidator_ReturnsNestingDepth()
        {
            CircuitDefinition nand = BuildNand();
            DefinitionRef nandRef = _env.Publish(nand);
            CircuitDefinition or = BuildOrFromNand(nandRef, nand);
            _env.Publish(or);
            CircuitDefinition root = Wrap(or, "Main");

            int depth = new HierarchyValidator(_env.Catalog).Validate(root, new ValidationReport());

            Assert.That(depth, Is.EqualTo(2), "Main -> OR -> NAND");
        }

        [Test]
        public void MissingNestedDefinition_IsReported()
        {
            var inner = new CircuitDefinition(DefinitionId.New(), "Inner");
            inner.AddNode(ComponentRef.Custom(new DefinitionRef(DefinitionId.New(), 1)), CircuitPoint.Zero);
            _env.Publish(inner);
            var root = new CircuitDefinition(DefinitionId.New(), "Root");
            root.AddNode(ComponentRef.Custom(inner.Reference), CircuitPoint.Zero);

            Assert.That(Validate(root).Contains(DiagnosticCode.MissingDefinition), Is.True);
        }

        [Test]
        public void ConnectionRules_RejectWrongPinDirections()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Pins");
            NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition not = circuit.AddNode(Not, CircuitPoint.Zero);
            NodeDefinition output = circuit.AddOutputBoundary("OUT", CircuitPoint.Zero);

            Assert.That(ConnectionRules.Check(circuit, _env.Catalog, new PinRef(not.Id, NotGate.Input), BoundaryIn(output)),
                Is.EqualTo(ConnectionError.InvalidSourcePort));
            Assert.That(ConnectionRules.Check(circuit, _env.Catalog, BoundaryOut(a), new PinRef(not.Id, NotGate.Output)),
                Is.EqualTo(ConnectionError.InvalidDestinationPort));
            Assert.That(ConnectionRules.Check(circuit, _env.Catalog, BoundaryIn(output), new PinRef(not.Id, NotGate.Input)),
                Is.EqualTo(ConnectionError.InvalidSourcePort), "Output boundaries have no output pins.");
            Assert.That(ConnectionRules.TryConnect(circuit, _env.Catalog, BoundaryOut(a), new PinRef(not.Id, NotGate.Input)),
                Is.EqualTo(ConnectionError.None));
        }

        [Test]
        public void ConnectionRules_RejectUnresolvedComponents()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Unresolved");
            NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition ghost = circuit.AddNode(ComponentRef.Primitive("builtin.ghost"), CircuitPoint.Zero);

            Assert.That(ConnectionRules.Check(circuit, _env.Catalog, BoundaryOut(a), new PinRef(ghost.Id, new PortId(0))),
                Is.EqualTo(ConnectionError.UnresolvedDestinationComponent));
        }

        [Test]
        public void CustomGateSignature_ComesFromItsDefinition()
        {
            CircuitDefinition nand = BuildNand();
            DefinitionRef reference = _env.Publish(nand);

            Assert.That(_env.Catalog.TryGetSignature(ComponentRef.Custom(reference), out ComponentSignature signature), Is.True);
            Assert.That(signature.InputCount, Is.EqualTo(2));
            Assert.That(signature.OutputCount, Is.EqualTo(1));
            Assert.That(signature.Inputs[0].Name, Is.EqualTo("A"));
        }
    }
}
