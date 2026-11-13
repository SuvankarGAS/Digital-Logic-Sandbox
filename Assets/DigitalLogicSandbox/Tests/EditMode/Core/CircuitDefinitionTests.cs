using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.Core
{
    public sealed class CircuitDefinitionTests
    {
        private CircuitDefinition _circuit;

        [SetUp]
        public void SetUp()
        {
            _circuit = new CircuitDefinition(DefinitionId.New(), "Test");
        }

        [Test]
        public void AddNode_AssignsUniqueIds()
        {
            NodeDefinition first = _circuit.AddNode(And, CircuitPoint.Zero);
            NodeDefinition second = _circuit.AddNode(Not, CircuitPoint.Zero);

            Assert.That(first.Id, Is.Not.EqualTo(second.Id));
            Assert.That(_circuit.Nodes.Count, Is.EqualTo(2));
            Assert.That(_circuit.GetNode(second.Id), Is.SameAs(second));
        }

        [Test]
        public void AddNode_RejectsBoundaryComponents()
        {
            Assert.Throws<ArgumentException>(() => _circuit.AddNode(ComponentRef.InputBoundary, CircuitPoint.Zero));
        }

        [Test]
        public void AddBoundaries_CreateInterfacePortsInOrder()
        {
            NodeDefinition a = _circuit.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition b = _circuit.AddInputBoundary("B", CircuitPoint.Zero);
            NodeDefinition output = _circuit.AddOutputBoundary("OUT", CircuitPoint.Zero);

            ComponentSignature signature = _circuit.GetSignature();
            Assert.That(signature.InputCount, Is.EqualTo(2));
            Assert.That(signature.Inputs[0].Name, Is.EqualTo("A"));
            Assert.That(signature.Inputs[1].Name, Is.EqualTo("B"));
            Assert.That(signature.Outputs[0].Name, Is.EqualTo("OUT"));
            Assert.That(_circuit.TryGetBoundaryNode(a.InterfacePort, out NodeId aNode) && aNode == a.Id, Is.True);
            Assert.That(_circuit.TryGetBoundaryNode(b.InterfacePort, out NodeId bNode) && bNode == b.Id, Is.True);
            Assert.That(output.Component.Kind, Is.EqualTo(ComponentKind.OutputBoundary));
        }

        [Test]
        public void InterfacePortIds_AreUniqueAcrossInputsAndOutputs()
        {
            NodeDefinition a = _circuit.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition output = _circuit.AddOutputBoundary("OUT", CircuitPoint.Zero);
            Assert.That(a.InterfacePort, Is.Not.EqualTo(output.InterfacePort));
        }

        [Test]
        public void Connect_SupportsFanOutFromOneOutput()
        {
            NodeDefinition input = _circuit.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition not1 = _circuit.AddNode(Not, CircuitPoint.Zero);
            NodeDefinition not2 = _circuit.AddNode(Not, CircuitPoint.Zero);

            _circuit.Connect(BoundaryOut(input), new PinRef(not1.Id, NotGate.Input));
            _circuit.Connect(BoundaryOut(input), new PinRef(not2.Id, NotGate.Input));

            List<Net> nets = NetBuilder.Build(_circuit);
            Assert.That(nets.Count, Is.EqualTo(1));
            Assert.That(nets[0].Driver, Is.EqualTo(BoundaryOut(input)));
            Assert.That(nets[0].Sinks.Count, Is.EqualTo(2));
        }

        [Test]
        public void Connect_RejectsSecondDriverOnSameInput()
        {
            NodeDefinition a = _circuit.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition b = _circuit.AddInputBoundary("B", CircuitPoint.Zero);
            NodeDefinition not = _circuit.AddNode(Not, CircuitPoint.Zero);
            var destination = new PinRef(not.Id, NotGate.Input);

            Assert.That(_circuit.TryConnect(BoundaryOut(a), destination), Is.EqualTo(ConnectionError.None));
            Assert.That(_circuit.TryConnect(BoundaryOut(b), destination),
                Is.EqualTo(ConnectionError.DestinationAlreadyDriven));
            Assert.That(_circuit.Connections.Count, Is.EqualTo(1));
        }

        [Test]
        public void Connect_RejectsSelfConnection()
        {
            NodeDefinition not = _circuit.AddNode(Not, CircuitPoint.Zero);
            ConnectionError error = _circuit.TryConnect(new PinRef(not.Id, NotGate.Output), new PinRef(not.Id, NotGate.Input));
            Assert.That(error, Is.EqualTo(ConnectionError.SelfConnection));
        }

        [Test]
        public void Connect_RejectsMissingNodes()
        {
            NodeDefinition not = _circuit.AddNode(Not, CircuitPoint.Zero);
            var ghost = new PinRef(new NodeId(999), new PortId(0));

            Assert.That(_circuit.TryConnect(ghost, new PinRef(not.Id, NotGate.Input)),
                Is.EqualTo(ConnectionError.MissingSourceNode));
            Assert.That(_circuit.TryConnect(new PinRef(not.Id, NotGate.Output), ghost),
                Is.EqualTo(ConnectionError.MissingDestinationNode));
        }

        [Test]
        public void Disconnect_RemovesConnectionByDestination()
        {
            CircuitDefinition nand = BuildNand();
            ConnectionDefinition first = nand.Connections[0];

            Assert.That(nand.Disconnect(first.Destination), Is.True);
            Assert.That(nand.IsDriven(first.Destination), Is.False);
            Assert.That(nand.Connections.Count, Is.EqualTo(3));
            Assert.That(nand.Disconnect(first.Destination), Is.False);
        }

        [Test]
        public void RemoveNode_RemovesTouchingConnections()
        {
            CircuitDefinition nand = BuildNand();
            NodeDefinition and = FindFirst(nand, ComponentKind.Primitive);

            Assert.That(nand.RemoveNode(and.Id), Is.True);
            Assert.That(nand.ContainsNode(and.Id), Is.False);
            foreach (ConnectionDefinition connection in nand.Connections)
            {
                Assert.That(connection.Source.Node, Is.Not.EqualTo(and.Id));
                Assert.That(connection.Destination.Node, Is.Not.EqualTo(and.Id));
            }

            Assert.That(nand.Connections.Count, Is.EqualTo(1), "Only NOT -> OUT should remain.");
        }

        [Test]
        public void RemoveBoundary_RemovesInterfacePort()
        {
            NodeDefinition a = _circuit.AddInputBoundary("A", CircuitPoint.Zero);
            _circuit.AddInputBoundary("B", CircuitPoint.Zero);

            _circuit.RemoveNode(a.Id);

            Assert.That(_circuit.Inputs.Count, Is.EqualTo(1));
            Assert.That(_circuit.Inputs[0].Name, Is.EqualTo("B"));
            Assert.That(_circuit.GetSignature().InputCount, Is.EqualTo(1));
            Assert.That(_circuit.TryGetBoundaryNode(a.InterfacePort, out _), Is.False);
        }

        [Test]
        public void RenameAndReorderInterfacePorts_UpdateSignature()
        {
            NodeDefinition a = _circuit.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition b = _circuit.AddInputBoundary("B", CircuitPoint.Zero);
            ComponentSignature before = _circuit.GetSignature();

            _circuit.RenameInterfacePort(a.InterfacePort, "X");
            _circuit.MoveInterfacePort(b.InterfacePort, 0);

            ComponentSignature after = _circuit.GetSignature();
            Assert.That(after, Is.Not.SameAs(before), "Signature cache must be invalidated.");
            Assert.That(after.Inputs[0].Name, Is.EqualTo("B"));
            Assert.That(after.Inputs[1].Name, Is.EqualTo("X"));
            Assert.That(after.Inputs[1].Id, Is.EqualTo(a.InterfacePort), "Port ids are stable across reordering.");
        }

        [Test]
        public void Clone_IsDeepAndIndependent()
        {
            CircuitDefinition original = BuildNand();
            int originalNodes = original.Nodes.Count;
            int originalConnections = original.Connections.Count;
            NodeDefinition originalNot = original.Nodes[3];

            CircuitDefinition copy = original.Clone(DefinitionId.New(), 1);
            copy.MoveNode(originalNot.Id, new CircuitPoint(100, 100));
            copy.RemoveNode(copy.Nodes[0].Id);
            NodeDefinition added = copy.AddNode(And, CircuitPoint.Zero);

            Assert.That(original.Nodes.Count, Is.EqualTo(originalNodes));
            Assert.That(original.Connections.Count, Is.EqualTo(originalConnections));
            Assert.That(original.Inputs.Count, Is.EqualTo(2));
            Assert.That(originalNot.Position, Is.EqualTo(new CircuitPoint(6, 0)));
            Assert.That(original.ContainsNode(added.Id), Is.False, "Clone continues id allocation without collisions.");
            Assert.That(copy.GetNode(originalNot.Id).Position, Is.EqualTo(new CircuitPoint(100, 100)));
        }

        [Test]
        public void CloneAsVersion_KeepsIdentityAndIsMutable()
        {
            CircuitDefinition original = BuildNand();
            original.Freeze();

            CircuitDefinition next = original.CloneAsVersion(2);

            Assert.That(next.Id, Is.EqualTo(original.Id));
            Assert.That(next.Version, Is.EqualTo(2));
            Assert.That(next.IsReadOnly, Is.False);
            Assert.DoesNotThrow(() => next.Name = "NAND v2");
        }

        [Test]
        public void Frozen_RejectsAllMutations()
        {
            CircuitDefinition nand = BuildNand();
            NodeDefinition node = nand.Nodes[2];
            nand.Freeze();

            Assert.Throws<InvalidOperationException>(() => nand.AddNode(And, CircuitPoint.Zero));
            Assert.Throws<InvalidOperationException>(() => nand.RemoveNode(node.Id));
            Assert.Throws<InvalidOperationException>(() => nand.MoveNode(node.Id, new CircuitPoint(1, 1)));
            Assert.Throws<InvalidOperationException>(() => nand.Name = "Other");
            Assert.Throws<InvalidOperationException>(() => nand.Disconnect(nand.Connections[0].Destination));
            Assert.That(nand.TryConnect(nand.Connections[0].Source, new PinRef(node.Id, new PortId(5))),
                Is.EqualTo(ConnectionError.CircuitReadOnly));
        }

        [Test]
        public void Changed_IsRaisedForEachMutation()
        {
            var changes = new List<CircuitChange>();
            CircuitDefinition nand = BuildNand();
            nand.Changed += changes.Add;
            NodeDefinition not = nand.Nodes[3];

            nand.RemoveNode(not.Id);

            Assert.That(changes.Count, Is.EqualTo(3));
            Assert.That(changes[0].Kind, Is.EqualTo(CircuitChangeKind.Disconnected));
            Assert.That(changes[1].Kind, Is.EqualTo(CircuitChangeKind.Disconnected));
            Assert.That(changes[2].Kind, Is.EqualTo(CircuitChangeKind.NodeRemoved));
            Assert.That(changes[2].Node, Is.EqualTo(not.Id));
        }

        [Test]
        public void RestoreNode_PreservesIdAndAdvancesAllocator()
        {
            _circuit.RestoreNode(new NodeId(10), And, CircuitPoint.Zero, Rotation.R90, "restored");
            NodeDefinition next = _circuit.AddNode(Not, CircuitPoint.Zero);

            Assert.That(_circuit.GetNode(new NodeId(10)).Rotation, Is.EqualTo(Rotation.R90));
            Assert.That(next.Id.Value, Is.GreaterThan(10));
            Assert.Throws<InvalidOperationException>(() =>
                _circuit.RestoreNode(new NodeId(10), Not, CircuitPoint.Zero, Rotation.R0, null));
        }

        [Test]
        public void RestoreBoundary_InsertsPortAtIndex()
        {
            _circuit.AddInputBoundary("A", CircuitPoint.Zero);
            _circuit.AddInputBoundary("C", CircuitPoint.Zero);

            _circuit.RestoreBoundary(new NodeId(50), new PortId(40), PortDirection.Input, "B", CircuitPoint.Zero,
                Rotation.R0, null, 1);

            Assert.That(_circuit.Inputs[1].Name, Is.EqualTo("B"));
            Assert.That(_circuit.Inputs[1].Id, Is.EqualTo(new PortId(40)));
            NodeDefinition fresh = _circuit.AddOutputBoundary("OUT", CircuitPoint.Zero);
            Assert.That(fresh.InterfacePort.Value, Is.GreaterThan(40));
        }

        private static NodeDefinition FindFirst(CircuitDefinition circuit, ComponentKind kind)
        {
            foreach (NodeDefinition node in circuit.Nodes)
            {
                if (node.Component.Kind == kind)
                {
                    return node;
                }
            }

            throw new AssertionException("No node of kind " + kind);
        }
    }
}
