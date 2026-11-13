using DigitalLogicSandbox.Commands.Circuit;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.Commands
{
    public sealed class CircuitCommandTests
    {
        private CommandHistory _history;

        [SetUp]
        public void SetUp()
        {
            _history = new CommandHistory();
        }

        [Test]
        public void AddNode_UndoRedo_RestoresSameId()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Test");
            var add = new AddNodeCommand(circuit, And, new CircuitPoint(2, 3), "AND");
            _history.Execute(add);
            NodeId id = add.CreatedNode;
            Assert.That(circuit.ContainsNode(id), Is.True);

            _history.Undo();
            Assert.That(circuit.Nodes.Count, Is.EqualTo(0));

            _history.Redo();
            Assert.That(circuit.ContainsNode(id), Is.True, "Redo must recreate the node with its original id.");
            Assert.That(circuit.GetNode(id).Position, Is.EqualTo(new CircuitPoint(2, 3)));
        }

        [Test]
        public void AddBoundary_CreatesInterfacePort_AndUndoRemovesIt()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Test");
            _history.Execute(new AddNodeCommand(circuit, ComponentRef.InputBoundary, CircuitPoint.Zero, "A", "A"));
            Assert.That(circuit.Inputs.Count, Is.EqualTo(1));
            Assert.That(circuit.Inputs[0].Name, Is.EqualTo("A"));

            _history.Undo();
            Assert.That(circuit.Inputs.Count, Is.EqualTo(0));
            _history.Redo();
            Assert.That(circuit.Inputs[0].Name, Is.EqualTo("A"));
        }

        [Test]
        public void RemoveNodes_Undo_RestoresNodesConnectionsAndInterfaceOrder()
        {
            CircuitDefinition circuit = BuildNand();
            string before = CircuitFingerprint.Of(circuit);
            var ids = new[] { circuit.Nodes[0].Id, circuit.Nodes[2].Id }; // input A and the AND gate

            _history.Execute(new RemoveNodesCommand(circuit, ids));
            Assert.That(circuit.Nodes.Count, Is.EqualTo(3));
            Assert.That(circuit.Inputs.Count, Is.EqualTo(1));

            _history.Undo();
            Assert.That(CircuitFingerprint.Of(circuit), Is.EqualTo(before));

            _history.Redo();
            Assert.That(circuit.Nodes.Count, Is.EqualTo(3));
            _history.Undo();
            Assert.That(CircuitFingerprint.Of(circuit), Is.EqualTo(before));
        }

        [Test]
        public void RemoveWireOnly_Undo_RestoresConnection()
        {
            CircuitDefinition circuit = BuildNand();
            string before = CircuitFingerprint.Of(circuit);
            PinRef destination = circuit.Connections[0].Destination;

            _history.Execute(new RemoveNodesCommand(circuit, null, new[] { destination }));
            Assert.That(circuit.IsDriven(destination), Is.False);
            Assert.That(circuit.Nodes.Count, Is.EqualTo(5));

            _history.Undo();
            Assert.That(CircuitFingerprint.Of(circuit), Is.EqualTo(before));
        }

        [Test]
        public void Connect_UndoRedo()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Test");
            NodeDefinition and = circuit.AddNode(And, CircuitPoint.Zero);
            NodeDefinition not = circuit.AddNode(Not, new CircuitPoint(4, 0));
            var source = new PinRef(and.Id, AndGate.Output);
            var destination = new PinRef(not.Id, NotGate.Input);

            _history.Execute(new ConnectCommand(circuit, source, destination));
            Assert.That(circuit.IsDriven(destination), Is.True);
            _history.Undo();
            Assert.That(circuit.IsDriven(destination), Is.False);
            _history.Redo();
            Assert.That(circuit.TryGetConnectionTo(destination, out ConnectionDefinition c) && c.Source == source, Is.True);
        }

        [Test]
        public void Transform_UndoRestoresPositionsAndRotations()
        {
            CircuitDefinition circuit = BuildNand();
            string before = CircuitFingerprint.Of(circuit);
            var nodes = new[] { circuit.Nodes[2].Id, circuit.Nodes[3].Id };
            TransformNodesCommand.CaptureTransforms(circuit, nodes, out CircuitPoint[] from, out Rotation[] fromRot);
            var to = new[] { from[0].Offset(1, 1), from[1].Offset(-2, 0.5f) };
            var toRot = new[] { Rotation.R90, Rotation.R180 };

            _history.Execute(new TransformNodesCommand("Move", circuit, nodes, from, to, fromRot, toRot));
            Assert.That(circuit.GetNode(nodes[0]).Position, Is.EqualTo(to[0]));
            Assert.That(circuit.GetNode(nodes[1]).Rotation, Is.EqualTo(Rotation.R180));

            _history.Undo();
            Assert.That(CircuitFingerprint.Of(circuit), Is.EqualTo(before));
        }

        [Test]
        public void Duplicate_CopiesInternalConnectionsOnly_AndUndoRemovesCopies()
        {
            CircuitDefinition circuit = BuildNand();
            string before = CircuitFingerprint.Of(circuit);
            NodeId and = circuit.Nodes[2].Id;
            NodeId not = circuit.Nodes[3].Id;
            int connectionsBefore = circuit.Connections.Count;

            var duplicate = new DuplicateNodesCommand(circuit, new[] { and, not }, 1f, -1f);
            _history.Execute(duplicate);

            Assert.That(duplicate.Created.Count, Is.EqualTo(2));
            Assert.That(circuit.Nodes.Count, Is.EqualTo(7));
            Assert.That(circuit.Connections.Count, Is.EqualTo(connectionsBefore + 1),
                "Only the AND→NOT connection lies inside the duplicated set.");
            Assert.That(circuit.GetNode(duplicate.Created[0].Id).Position,
                Is.EqualTo(circuit.GetNode(and).Position.Offset(1f, -1f)));

            _history.Undo();
            Assert.That(CircuitFingerprint.Of(circuit), Is.EqualTo(before));
        }

        [Test]
        public void BoundaryNames_AreSequentialAndSkipUsedNames()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Test");
            Assert.That(BoundaryNames.NextInputName(circuit), Is.EqualTo("A"));
            circuit.AddInputBoundary("A", CircuitPoint.Zero);
            circuit.AddInputBoundary("C", CircuitPoint.Zero);
            Assert.That(BoundaryNames.NextInputName(circuit), Is.EqualTo("B"));
            Assert.That(BoundaryNames.NextOutputName(circuit), Is.EqualTo("OUT"));
            circuit.AddOutputBoundary("OUT", CircuitPoint.Zero);
            Assert.That(BoundaryNames.NextOutputName(circuit), Is.EqualTo("OUT2"));
            Assert.That(BoundaryNames.NextName(circuit, PortDirection.Input), Is.EqualTo("B"));
        }

        [Test]
        public void History_NewCommandClearsRedo_AndRespectsMaxDepth()
        {
            var history = new CommandHistory(maxDepth: 3);
            var circuit = new CircuitDefinition(DefinitionId.New(), "Test");
            for (int i = 0; i < 5; i++)
            {
                history.Execute(new AddNodeCommand(circuit, Not, new CircuitPoint(i, 0), "NOT"));
            }

            Assert.That(history.UndoCount, Is.EqualTo(3));
            history.Undo();
            Assert.That(history.CanRedo, Is.True);
            history.Execute(new AddNodeCommand(circuit, Not, CircuitPoint.Zero, "NOT"));
            Assert.That(history.CanRedo, Is.False);
        }

        [Test]
        public void CompositeCommand_UndoesInReverseOrder()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Test");
            NodeDefinition and = circuit.AddNode(And, CircuitPoint.Zero);
            NodeDefinition not = circuit.AddNode(Not, new CircuitPoint(4, 0));
            string before = CircuitFingerprint.Of(circuit);
            var composite = new CompositeCommand("Wire and move", new ICommand[]
            {
                new ConnectCommand(circuit, new PinRef(and.Id, AndGate.Output), new PinRef(not.Id, NotGate.Input)),
                new RemoveNodesCommand(circuit, new[] { not.Id }),
            });

            _history.Execute(composite);
            Assert.That(circuit.Nodes.Count, Is.EqualTo(1));
            _history.Undo();
            Assert.That(CircuitFingerprint.Of(circuit), Is.EqualTo(before));
        }
    }
}
