using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.CircuitEditor
{
    public sealed class CircuitSessionTests
    {
        private TestEnvironment _env;
        private CircuitSession _session;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
            _session = new CircuitSession(_env.Catalog);
        }

        [TearDown]
        public void TearDown()
        {
            _session.Dispose();
        }

        [Test]
        public void Bind_MarksDirty_RebuildCompiles()
        {
            _session.Bind(BuildNand());
            Assert.That(_session.IsDirty, Is.True);
            Assert.That(_session.RebuildIfDirty(), Is.True);
            Assert.That(_session.IsSimulating, Is.True);
            Assert.That(_session.RebuildIfDirty(), Is.False);
        }

        [Test]
        public void ToggleInputs_PropagatesThroughNand()
        {
            CircuitDefinition nand = BuildNand();
            _session.Bind(nand);
            _session.Rebuild();
            NodeId a = nand.Nodes[0].Id;
            NodeId b = nand.Nodes[1].Id;
            NodeId output = nand.Nodes[4].Id;

            Assert.That(_session.GetOutputValue(output), Is.EqualTo(Signal.High));
            Assert.That(_session.ToggleInput(a), Is.True);
            Assert.That(_session.ToggleInput(b), Is.True);
            Assert.That(_session.GetInputValue(a), Is.EqualTo(Signal.High));
            Assert.That(_session.GetOutputValue(output), Is.EqualTo(Signal.Low));
        }

        [Test]
        public void ToggleInput_RejectsNonInputNodes()
        {
            CircuitDefinition nand = BuildNand();
            _session.Bind(nand);
            _session.Rebuild();
            Assert.That(_session.ToggleInput(nand.Nodes[2].Id), Is.False);
            Assert.That(_session.ToggleInput(new NodeId(999)), Is.False);
        }

        [Test]
        public void MovesAndRotations_DoNotTriggerRecompile()
        {
            CircuitDefinition nand = BuildNand();
            _session.Bind(nand);
            _session.Rebuild();
            int revision = _session.Revision;

            nand.MoveNode(nand.Nodes[2].Id, new CircuitPoint(10, 10));
            nand.RotateNode(nand.Nodes[3].Id, Rotation.R90);

            Assert.That(_session.IsDirty, Is.False);
            Assert.That(_session.RebuildIfDirty(), Is.False);
            Assert.That(_session.Revision, Is.EqualTo(revision));
        }

        [Test]
        public void StructuralEdit_Recompiles_AndKeepsInputValues()
        {
            CircuitDefinition nand = BuildNand();
            _session.Bind(nand);
            _session.Rebuild();
            NodeId a = nand.Nodes[0].Id;
            NodeId b = nand.Nodes[1].Id;
            _session.ToggleInput(a);
            _session.ToggleInput(b);

            NodeDefinition extra = nand.AddNode(Not, new CircuitPoint(12, 0));
            nand.Connect(new PinRef(nand.Nodes[3].Id, NotGate.Output), new PinRef(extra.Id, NotGate.Input));
            Assert.That(_session.IsDirty, Is.True);
            _session.Rebuild();

            Assert.That(_session.GetInputValue(a), Is.EqualTo(Signal.High), "Inputs survive recompilation by port id.");
            Assert.That(_session.GetOutputValue(nand.Nodes[4].Id), Is.EqualTo(Signal.Low));
        }

        [Test]
        public void FailedCompile_StopsSimulating_ThenRecovers()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Broken");
            var missing = ComponentRef.Custom(new DefinitionRef(DefinitionId.New(), 1));
            NodeDefinition node = circuit.AddNode(missing, CircuitPoint.Zero);
            _session.Bind(circuit);
            _session.Rebuild();

            Assert.That(_session.IsSimulating, Is.False);
            Assert.That(_session.Report.HasErrors, Is.True);

            circuit.RemoveNode(node.Id);
            _session.Rebuild();
            Assert.That(_session.IsSimulating, Is.True);
        }
    }
}
