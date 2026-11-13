using System.Collections.Generic;
using DigitalLogicSandbox.Commands.Library;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.GateLibrary.Packaging;
using DigitalLogicSandbox.Tests.EditMode.CircuitEditor;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.SimulationTestUtil;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.GateLibrary
{
    /// <summary>Custom gate versioning: drafts, retargeting references, cascades and safety of old references.</summary>
    public sealed class VersioningTests
    {
        private TestEnvironment _env;
        private CircuitDefinition _nand;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
            _nand = BuildNand("NAND");
            _env.Publish(_nand);
        }

        /// <summary>Draft of NAND v2 turned into AND: the NOT is removed and AND drives OUT directly.</summary>
        private CircuitDefinition AndDraft()
        {
            CircuitDefinition draft = DefinitionVersioning.CreateDraft(_env.Library, _nand);
            NodeId not = NavigationTests.FindNode(draft, Not);
            NodeId and = NavigationTests.FindNode(draft, And);
            draft.RemoveNode(not);
            draft.Connect(new PinRef(and, AndGate.Output), BoundaryIn(draft.GetNode(BoundaryNode(draft, draft.Outputs[0].Id))));
            return draft;
        }

        private static NodeId BoundaryNode(CircuitDefinition circuit, PortId port)
        {
            Assert.That(circuit.TryGetBoundaryNode(port, out NodeId node), Is.True);
            return node;
        }

        [Test]
        public void CreateDraft_IsNextVersionWithSameId()
        {
            CircuitDefinition draft = DefinitionVersioning.CreateDraft(_env.Library, _nand);
            Assert.That(draft.Id, Is.EqualTo(_nand.Id));
            Assert.That(draft.Version, Is.EqualTo(2));
            Assert.That(draft.IsReadOnly, Is.False);
            Assert.That(draft.Nodes.Count, Is.EqualTo(_nand.Nodes.Count));
            Assert.That(draft.Connections.Count, Is.EqualTo(_nand.Connections.Count));
            Assert.That(_nand.IsReadOnly, Is.True);
        }

        [Test]
        public void SavingNewVersion_KeepsOldReferencesValidAndUnchanged()
        {
            CircuitDefinition root = Wrap(_nand, "Main");
            CircuitDefinition v2 = AndDraft();
            _env.Library.Add(v2);

            Assert.That(_env.Library.Contains(_nand.Reference), Is.True, "Old versions stay in the library.");
            AssertTruthTable(CreateContext(_env, root), 1, 1, 1, 0); // still NAND v1
            AssertTruthTable(CreateContext(_env, Wrap(v2, "Main 2")), 0, 0, 0, 1); // v2 is now an AND
        }

        [Test]
        public void Retarget_SwitchesInstancesKeepingIdsLayoutAndConnections()
        {
            CircuitDefinition root = Wrap(_nand, "Main");
            NodeId instance = NavigationTests.FindNode(root, ComponentRef.Custom(_nand.Reference));
            CircuitPoint position = root.GetNode(instance).Position;
            CircuitDefinition v2 = AndDraft();
            _env.Library.Add(v2);
            string before = CircuitFingerprint.Of(root);

            var records = new List<RetargetRecord>();
            var map = new Dictionary<DefinitionRef, DefinitionRef> { { _nand.Reference, v2.Reference } };
            int dropped = ComponentRetargeting.Retarget(root, map, r => v2.GetSignature(), records);

            Assert.That(dropped, Is.Zero, "Port ids are unchanged, so every connection is kept.");
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(root.GetNode(instance).Component.Definition, Is.EqualTo(v2.Reference));
            Assert.That(root.GetNode(instance).Position, Is.EqualTo(position));
            Assert.That(root.Connections.Count, Is.EqualTo(3));
            AssertTruthTable(CreateContext(_env, root), 0, 0, 0, 1);

            ComponentRetargeting.Revert(root, records);
            Assert.That(CircuitFingerprint.Of(root), Is.EqualTo(before), "Revert restores the circuit exactly.");
        }

        [Test]
        public void Retarget_DropsConnectionsToRemovedPins_AndRevertRestoresThem()
        {
            CircuitDefinition root = Wrap(_nand, "Main");
            CircuitDefinition v2 = DefinitionVersioning.CreateDraft(_env.Library, _nand);
            v2.RemoveNode(BoundaryNode(v2, v2.Inputs[1].Id)); // B removed from the interface
            _env.Library.Add(v2);
            string before = CircuitFingerprint.Of(root);

            var records = new List<RetargetRecord>();
            var map = new Dictionary<DefinitionRef, DefinitionRef> { { _nand.Reference, v2.Reference } };
            int dropped = ComponentRetargeting.Retarget(root, map, r => v2.GetSignature(), records);

            Assert.That(dropped, Is.EqualTo(1));
            Assert.That(records[0].Dropped.Count, Is.EqualTo(1));
            Assert.That(root.Connections.Count, Is.EqualTo(2));
            ComponentRetargeting.Revert(root, records);
            Assert.That(CircuitFingerprint.Of(root), Is.EqualTo(before));
        }

        [Test]
        public void RetargetCommand_UndoRedo()
        {
            CircuitDefinition root = Wrap(_nand, "Main");
            CircuitDefinition v2 = AndDraft();
            _env.Library.Add(v2);
            string before = CircuitFingerprint.Of(root);
            var map = new Dictionary<DefinitionRef, DefinitionRef> { { _nand.Reference, v2.Reference } };
            var command = new RetargetReferencesCommand(root, map, r => v2.GetSignature());

            command.Execute();
            string after = CircuitFingerprint.Of(root);
            Assert.That(command.RetargetedNodes, Is.EqualTo(1));
            command.Undo();
            Assert.That(CircuitFingerprint.Of(root), Is.EqualTo(before));
            command.Execute();
            Assert.That(CircuitFingerprint.Of(root), Is.EqualTo(after));
        }

        [Test]
        public void Plan_WithoutCascade_MapsOnlyTheEditedGate()
        {
            CircuitDefinition or = BuildOrFromNand(_nand.Reference, _nand, "OR");
            _env.Publish(or);
            CircuitDefinition v2 = AndDraft();

            VersionPlan plan = DefinitionVersioning.Plan(_env.Library, v2, _nand.Reference, cascade: false);
            Assert.That(plan.Cascaded, Is.Empty);
            Assert.That(plan.Map.Count, Is.EqualTo(1));
            Assert.That(plan.Map[_nand.Reference], Is.EqualTo(v2.Reference));
        }

        [Test]
        public void Plan_WithCascade_CreatesNewVersionsOfDependents_OldVersionsUntouched()
        {
            CircuitDefinition or = BuildOrFromNand(_nand.Reference, _nand, "OR");
            _env.Publish(or);
            CircuitDefinition wrapper = Wrap(or, "OR wrapper");
            _env.Publish(wrapper);
            string orBefore = CircuitFingerprint.Of(or);
            CircuitDefinition v2 = AndDraft();

            VersionPlan plan = DefinitionVersioning.Plan(_env.Library, v2, _nand.Reference, cascade: true);

            Assert.That(plan.Cascaded.Count, Is.EqualTo(2), "OR and the wrapper that uses OR.");
            Assert.That(plan.Cascaded[0].Id, Is.EqualTo(or.Id), "Dependencies come first.");
            Assert.That(plan.Cascaded[0].Version, Is.EqualTo(2));
            Assert.That(plan.Cascaded[1].Id, Is.EqualTo(wrapper.Id));
            foreach (NodeDefinition node in plan.Cascaded[0].Nodes)
            {
                if (node.Component.Kind == ComponentKind.Custom)
                {
                    Assert.That(node.Component.Definition, Is.EqualTo(v2.Reference));
                }
            }

            foreach (CircuitDefinition definition in plan.AllNewDefinitions())
            {
                _env.Library.Add(definition);
            }

            Assert.That(CircuitFingerprint.Of(or), Is.EqualTo(orBefore), "OR v1 still references NAND v1.");
            AssertTruthTable(CreateContext(_env, Wrap(or, "Old")), 0, 1, 1, 1);
            // OR v2 = AND(AND(A,A), AND(B,B)) = A AND B.
            AssertTruthTable(CreateContext(_env, Wrap(plan.Cascaded[0], "New")), 0, 0, 0, 1);
        }

        [Test]
        public void AnalyzeImpact_CountsInstancesDependentsAndLostConnections()
        {
            CircuitDefinition or = BuildOrFromNand(_nand.Reference, _nand, "OR");
            _env.Publish(or);
            CircuitDefinition root = Wrap(_nand, "Main");
            CircuitDefinition v2 = DefinitionVersioning.CreateDraft(_env.Library, _nand);
            v2.RemoveNode(BoundaryNode(v2, v2.Inputs[1].Id));
            string rootBefore = CircuitFingerprint.Of(root);

            VersionImpact impact = DefinitionVersioning.AnalyzeImpact(_env.Library, v2, _nand.Reference, root);

            Assert.That(impact.MainCircuitInstances, Is.EqualTo(1));
            Assert.That(impact.MainCircuitDroppedConnections, Is.EqualTo(1));
            Assert.That(impact.DirectDependents.Count, Is.EqualTo(1));
            Assert.That(impact.CascadeCount, Is.EqualTo(1));
            Assert.That(impact.CascadeDroppedConnections, Is.EqualTo(3), "Each NAND in OR loses its B input.");
            Assert.That(CircuitFingerprint.Of(root), Is.EqualTo(rootBefore), "Analysis changes nothing.");
            Assert.That(_env.Library.NextVersion(_nand.Id), Is.EqualTo(2), "Analysis publishes nothing.");
        }

        [Test]
        public void DependsOn_DetectsTransitiveSelfContainment()
        {
            CircuitDefinition or = BuildOrFromNand(_nand.Reference, _nand, "OR");
            _env.Publish(or);

            Assert.That(DefinitionVersioning.DependsOn(_env.Library, or.Reference, _nand.Id), Is.True);
            Assert.That(DefinitionVersioning.DependsOn(_env.Library, _nand.Reference, _nand.Id), Is.True);
            Assert.That(DefinitionVersioning.DependsOn(_env.Library, _nand.Reference, or.Id), Is.False);
        }

        [Test]
        public void ValidateDefinition_RejectsDraftWithoutConnectedOutput_AndCircularSelfReference()
        {
            var packager = new CircuitPackager(_env.Catalog, _env.Library);
            CircuitDefinition draft = DefinitionVersioning.CreateDraft(_env.Library, _nand);
            Assert.That(packager.ValidateDefinition(draft).HasErrors, Is.False);

            draft.RemoveNode(NavigationTests.FindNode(draft, Not));
            ValidationReport report = packager.ValidateDefinition(draft);
            Assert.That(report.Contains(DiagnosticCode.NoOutputs), Is.True, report.ToString());

            CircuitDefinition selfContaining = DefinitionVersioning.CreateDraft(_env.Library, _nand);
            selfContaining.AddNode(ComponentRef.Custom(_nand.Reference), new CircuitPoint(20, 0));
            Assert.That(packager.ValidateDefinition(selfContaining).Contains(DiagnosticCode.CircularReference), Is.True);
        }

        [Test]
        public void PublishingCascadeAsComposite_UndoesInReverseOrder()
        {
            CircuitDefinition or = BuildOrFromNand(_nand.Reference, _nand, "OR");
            _env.Publish(or);
            CircuitDefinition v2 = AndDraft();
            VersionPlan plan = DefinitionVersioning.Plan(_env.Library, v2, _nand.Reference, cascade: true);
            var commands = new List<ICommand>();
            foreach (CircuitDefinition definition in plan.AllNewDefinitions())
            {
                commands.Add(new PublishDefinitionCommand(_env.Library, definition));
            }

            var history = new CommandHistory();
            history.Execute(new CompositeCommand("Save", commands));
            Assert.That(_env.Library.Contains(new DefinitionRef(or.Id, 2)), Is.True);

            Assert.That(history.Undo(), Is.True);
            Assert.That(_env.Library.Contains(v2.Reference), Is.False);
            Assert.That(_env.Library.Contains(new DefinitionRef(or.Id, 2)), Is.False);
            Assert.That(history.Redo(), Is.True);
            Assert.That(_env.Library.Contains(v2.Reference), Is.True);
        }
    }
}
