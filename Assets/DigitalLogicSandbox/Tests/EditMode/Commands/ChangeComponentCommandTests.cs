using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Commands.Circuit;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;

namespace DigitalLogicSandbox.Tests.EditMode.Commands
{
    /// <summary>Changing the width or style of a placed component, with undo and redo.</summary>
    public sealed class ChangeComponentCommandTests
    {
        private TestEnvironment _env;
        private CommandHistory _history;
        private CircuitDefinition _circuit;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
            _history = new CommandHistory();
            _circuit = new CircuitDefinition(DefinitionId.New(), "Resize");
        }

        private static PinRef Bit(NodeDefinition node, int bit)
        {
            return new PinRef(node.Id, BoundarySignatures.BitPin(bit));
        }

        private static PinRef BusPin(NodeDefinition node)
        {
            return new PinRef(node.Id, BoundarySignatures.Pin);
        }

        [Test]
        public void ChangingAClockDivider_KeepsConnections_AndUndoes()
        {
            NodeDefinition clock = _circuit.AddNode(ComponentRef.Clock, new CircuitPoint(0f, 0f));
            NodeDefinition lamp = _circuit.AddOutputBoundary("Y", new CircuitPoint(4f, 0f));
            _circuit.Connect(BusPin(clock), BusPin(lamp));

            var command = new ChangeComponentCommand(_circuit, _env.Catalog, clock.Id, ComponentRef.ClockDivided(4));
            _history.Execute(command);
            Assert.That(_circuit.GetNode(clock.Id).Component.ClockDivider, Is.EqualTo(4));
            Assert.That(command.DroppedConnections, Is.Zero);
            Assert.That(_circuit.IsDriven(BusPin(lamp)), Is.True);

            _history.Undo();
            Assert.That(_circuit.GetNode(clock.Id).Component, Is.EqualTo(ComponentRef.Clock));
            _history.Redo();
            Assert.That(_circuit.GetNode(clock.Id).Component, Is.EqualTo(ComponentRef.ClockDivided(4)));
        }

        [Test]
        public void NarrowingASplitter_DropsDanglingConnections_UndoRestoresThem()
        {
            NodeDefinition bus = _circuit.AddInputBoundary("A", new CircuitPoint(0f, 0f), 4);
            NodeDefinition splitter = _circuit.AddNode(ComponentRef.Splitter(4), new CircuitPoint(4f, 0f));
            _circuit.Connect(BusPin(bus), BusPin(splitter));
            var outputs = new NodeDefinition[4];
            for (int i = 0; i < 4; i++)
            {
                outputs[i] = _circuit.AddOutputBoundary("Y" + i, new CircuitPoint(8f, i));
                _circuit.Connect(Bit(splitter, i), BusPin(outputs[i]));
            }

            CircuitPoint position = splitter.Position;
            var command = new ChangeComponentCommand(_circuit, _env.Catalog, splitter.Id, ComponentRef.Splitter(2));
            _history.Execute(command);

            NodeDefinition changed = _circuit.GetNode(splitter.Id);
            Assert.That(changed.Component, Is.EqualTo(ComponentRef.Splitter(2)));
            Assert.That(changed.Position, Is.EqualTo(position), "Same node, same place.");
            Assert.That(_circuit.IsDriven(BusPin(outputs[0])) && _circuit.IsDriven(BusPin(outputs[1])), Is.True,
                "Bits 0 and 1 still exist and stay connected.");
            Assert.That(_circuit.IsDriven(BusPin(outputs[2])) || _circuit.IsDriven(BusPin(outputs[3])), Is.False,
                "Bits 2 and 3 are gone.");
            Assert.That(_circuit.IsDriven(BusPin(changed)), Is.False, "A 4-bit bus no longer fits a 2-bit splitter.");
            Assert.That(command.DroppedConnections, Is.EqualTo(3));

            _history.Undo();
            Assert.That(_circuit.GetNode(splitter.Id).Component, Is.EqualTo(ComponentRef.Splitter(4)));
            Assert.That(_circuit.Connections.Count, Is.EqualTo(5), "Every original connection is back.");
            for (int i = 0; i < 4; i++)
            {
                Assert.That(_circuit.TryGetConnectionTo(BusPin(outputs[i]), out ConnectionDefinition c), Is.True);
                Assert.That(c.Source, Is.EqualTo(Bit(splitter, i)));
            }

            _history.Redo();
            Assert.That(_circuit.GetNode(splitter.Id).Component, Is.EqualTo(ComponentRef.Splitter(2)));
            Assert.That(_circuit.Connections.Count, Is.EqualTo(2));
        }

        [Test]
        public void WideningAMerger_KeepsBitInputs()
        {
            var inputs = new NodeDefinition[4];
            NodeDefinition merger = _circuit.AddNode(ComponentRef.Merger(4), new CircuitPoint(4f, 0f));
            for (int i = 0; i < 4; i++)
            {
                inputs[i] = _circuit.AddInputBoundary("I" + i, new CircuitPoint(0f, i));
                _circuit.Connect(BusPin(inputs[i]), Bit(merger, i));
            }

            var command = new ChangeComponentCommand(_circuit, _env.Catalog, merger.Id, ComponentRef.Merger(8));
            _history.Execute(command);

            Assert.That(command.DroppedConnections, Is.EqualTo(0));
            for (int i = 0; i < 4; i++)
            {
                Assert.That(_circuit.IsDriven(Bit(merger, i)), Is.True);
            }
        }

        [Test]
        public void ChangingABusBoundaryWidth_KeepsItsInterfacePort()
        {
            NodeDefinition first = _circuit.AddInputBoundary("A", new CircuitPoint(0f, 0f), 4);
            NodeDefinition second = _circuit.AddInputBoundary("B", new CircuitPoint(0f, 2f), 1);
            PortId port = first.InterfacePort;

            _history.Execute(new ChangeComponentCommand(_circuit, _env.Catalog, first.Id, ComponentRef.InputBus(8)));

            NodeDefinition changed = _circuit.GetNode(first.Id);
            Assert.That(changed.InterfacePort, Is.EqualTo(port));
            Assert.That(_circuit.IndexOfInput(port), Is.EqualTo(0), "Interface order is unchanged.");
            Assert.That(_circuit.Inputs[0].Name, Is.EqualTo("A"));
            Assert.That(_circuit.GetSignature().Inputs[0].Width, Is.EqualTo(8));
            Assert.That(_circuit.IndexOfInput(second.InterfacePort), Is.EqualTo(1));

            _history.Undo();
            Assert.That(_circuit.GetSignature().Inputs[0].Width, Is.EqualTo(4));
        }

        [Test]
        public void TryBuildVariant_ClampsDisplayWidthToTheNewStyle_AndRejectsBadValues()
        {
            Assert.That(WorkspaceController.TryBuildVariant(ComponentRef.Display(DisplayStyle.Hex, 8), -1, -1,
                (int)DisplayStyle.Led, out ComponentRef led, out string error), Is.True, error);
            Assert.That(led, Is.EqualTo(ComponentRef.Display(DisplayStyle.Led)));

            Assert.That(WorkspaceController.TryBuildVariant(ComponentRef.Splitter(4), 1, -1, -1, out _, out error),
                Is.False);
            Assert.That(error, Does.Contain("Width"));
            Assert.That(WorkspaceController.TryBuildVariant(ComponentRef.BusJoin(4, 2), -1, 12, -1, out _, out error),
                Is.False);
            Assert.That(WorkspaceController.TryBuildVariant(ComponentRef.BusJoin(4, 2), -1, 3, -1,
                out ComponentRef join, out _), Is.True);
            Assert.That(join.Arity, Is.EqualTo(3));
            Assert.That(join.Width, Is.EqualTo(4));
        }

        [Test]
        public void RejectsADifferentKind()
        {
            NodeDefinition splitter = _circuit.AddNode(ComponentRef.Splitter(4), new CircuitPoint(0f, 0f));
            Assert.Throws<System.ArgumentException>(() =>
                new ChangeComponentCommand(_circuit, _env.Catalog, splitter.Id, ComponentRef.Merger(4)));
        }
    }
}
