using System.Collections.Generic;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Commands.Circuit;
using DigitalLogicSandbox.Commands.Library;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.Commands
{
    /// <summary>Phase 8 undo/redo commands: rename custom gate, reorder interface, change input value.</summary>
    public sealed class ProjectCommandTests
    {
        private TestEnvironment _env;
        private CommandHistory _history;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
            _history = new CommandHistory();
        }

        [Test]
        public void RenameCustomGate_RenamesEveryVersion_WithoutANewVersion_AndUndoes()
        {
            CircuitDefinition nand = BuildNand();
            _env.Publish(nand);
            CircuitDefinition v2 = nand.CloneAsVersion(2);
            _env.Publish(v2);
            var updated = new List<CircuitDefinition>();
            _env.Library.DefinitionUpdated += updated.Add;
            string fingerprint = CircuitFingerprint.Of(nand);

            _history.Execute(new RenameCustomGateCommand(_env.Library, nand.Id, "  Not-And  "));

            Assert.That(nand.Name, Is.EqualTo("Not-And"));
            Assert.That(v2.Name, Is.EqualTo("Not-And"));
            Assert.That(nand.IsReadOnly, Is.True, "Still published: metadata only.");
            Assert.That(CircuitFingerprint.Of(nand), Is.EqualTo(fingerprint), "Structure untouched.");
            Assert.That(_env.Library.AllVersions.Count, Is.EqualTo(2), "No new version.");
            Assert.That(updated.Count, Is.EqualTo(2));

            _history.Undo();
            Assert.That(nand.Name, Is.EqualTo("NAND"));
            Assert.That(v2.Name, Is.EqualTo("NAND"));
            _history.Redo();
            Assert.That(v2.Name, Is.EqualTo("Not-And"));
        }

        [Test]
        public void PublishedDefinitions_RejectStructuralEdits_ButAcceptMetadata()
        {
            CircuitDefinition nand = BuildNand();
            _env.Publish(nand);
            Assert.That(() => nand.Name = "x", Throws.InvalidOperationException);
            Assert.That(() => nand.AddNode(Not, new CircuitPoint(0, 0)), Throws.InvalidOperationException);
            nand.UpdateMetadata("Renamed", "d", "c", "i");
            Assert.That(nand.Name, Is.EqualTo("Renamed"));
            Assert.That(nand.Category, Is.EqualTo("c"));
        }

        [Test]
        public void MoveInterfacePort_ReordersAndUndoes()
        {
            CircuitDefinition nand = BuildNand();
            PortId a = nand.Inputs[0].Id;
            _history.Execute(new MoveInterfacePortCommand(nand, a, 1));
            Assert.That(nand.Inputs[1].Id, Is.EqualTo(a));
            _history.Undo();
            Assert.That(nand.Inputs[0].Id, Is.EqualTo(a));
        }

        [Test]
        public void SetInputValue_IsUndoable_AndRestoresThePreviousValue()
        {
            CircuitDefinition nand = BuildNand();
            using (var session = new CircuitSession(_env.Catalog))
            {
                session.Bind(nand);
                session.Rebuild();
                NodeId a = nand.Nodes[0].Id;
                NodeId output = nand.Nodes[4].Id;
                NodeId b = nand.Nodes[1].Id;
                session.SetInput(b, Signal.High);

                var command = new SetInputValueCommand(session, a, Signal.High, "A");
                Assert.That(command.Description, Is.EqualTo("Set input 'A' to 1"));
                _history.Execute(command);
                Assert.That(session.GetInputValue(a), Is.EqualTo(Signal.High));
                Assert.That(session.GetOutputValue(output), Is.EqualTo(Signal.Low));

                _history.Undo();
                Assert.That(session.GetInputValue(a), Is.EqualTo(Signal.Low));
                Assert.That(session.GetOutputValue(output), Is.EqualTo(Signal.High));

                _history.Redo();
                Assert.That(session.GetInputValue(a), Is.EqualTo(Signal.High));
            }
        }

        [Test]
        public void LibraryClear_RemovesEverything_AndAnnouncesIt()
        {
            CircuitDefinition nand = BuildNand();
            _env.Publish(nand);
            _env.Publish(BuildOrFromNand(nand.Reference, nand));
            var removed = new List<CircuitDefinition>();
            _env.Library.DefinitionRemoved += removed.Add;

            _env.Library.Clear();
            Assert.That(_env.Library.AllVersions.Count, Is.Zero);
            Assert.That(_env.Library.DefinitionCount, Is.Zero);
            Assert.That(removed.Count, Is.EqualTo(2));
            Assert.That(removed[0].Name, Is.EqualTo("OR"), "Dependents first.");
        }

        [Test]
        public void RestoreIdAllocators_OnlyMovesForward()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "c");
            circuit.RestoreIdAllocators(10, 5);
            Assert.That(circuit.AddNode(Not, new CircuitPoint(0, 0)).Id.Value, Is.EqualTo(10));
            Assert.That(circuit.AddInputBoundary("A", new CircuitPoint(0, 0)).InterfacePort.Value, Is.EqualTo(5));
            circuit.RestoreIdAllocators(1, 1);
            Assert.That(circuit.NextNodeIdValue, Is.EqualTo(12));
        }
    }
}
