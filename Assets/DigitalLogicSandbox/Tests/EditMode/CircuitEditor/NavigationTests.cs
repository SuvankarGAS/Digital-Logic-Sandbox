using DigitalLogicSandbox.CircuitEditor.Navigation;
using DigitalLogicSandbox.Commands.Circuit;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.CircuitEditor
{
    /// <summary>Hierarchical inspection: opening gates, breadcrumbs, scopes, edit mode and navigation safety.</summary>
    public sealed class NavigationTests
    {
        private TestEnvironment _env;
        private CircuitNavigator _navigator;
        private CircuitDefinition _nand;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
            _navigator = new CircuitNavigator(_env.Catalog, _env.Library);
            _nand = BuildNand("NAND");
            _env.Publish(_nand);
        }

        [TearDown]
        public void TearDown()
        {
            _navigator.Dispose();
        }

        internal static NodeId FindNode(CircuitDefinition circuit, ComponentRef component, int occurrence = 0)
        {
            foreach (NodeDefinition node in circuit.Nodes)
            {
                if (node.Component == component && occurrence-- == 0)
                {
                    return node.Id;
                }
            }

            Assert.Fail("No node of " + component + " in " + circuit.Name);
            return NodeId.None;
        }

        private CircuitDefinition PublishOr()
        {
            CircuitDefinition or = BuildOrFromNand(_nand.Reference, _nand, "OR");
            _env.Publish(or);
            return or;
        }

        private void LoadAndCompile(CircuitDefinition root)
        {
            _navigator.LoadRoot(root);
            _navigator.RebuildDirtySessions();
            Assert.That(_navigator.RootSession.IsSimulating, Is.True, _navigator.RootSession.Report.ToString());
        }

        [Test]
        public void Open_CustomGate_ShowsSharedDefinitionReadOnly()
        {
            CircuitDefinition root = Wrap(_nand, "Main");
            LoadAndCompile(root);
            NodeId instance = FindNode(root, ComponentRef.Custom(_nand.Reference));

            Assert.That(_navigator.TryOpen(instance, out string error), Is.True, error);

            NavigationLevel level = _navigator.Current;
            Assert.That(level.Kind, Is.EqualTo(NavigationLevelKind.Inspect));
            Assert.That(level.Circuit, Is.SameAs(_nand), "Inspection shows the shared definition, not a copy.");
            Assert.That(level.IsEditable, Is.False);
            Assert.That(level.CanToggleInputs, Is.False, "Inputs inside an instance are driven by the parent.");
            Assert.That(level.Path, Is.EqualTo(new[] { instance }));
            CompiledInstance scope = _navigator.ResolveScope(level);
            Assert.That(scope, Is.Not.Null);
            Assert.That(scope.Definition, Is.SameAs(_nand));
        }

        [Test]
        public void Open_IsRefused_ForPrimitivesBoundariesAndMissingDefinitions()
        {
            CircuitDefinition root = BuildNand("Main");
            NodeDefinition ghost = root.AddNode(ComponentRef.Custom(new DefinitionRef(DefinitionId.New(), 1)),
                new CircuitPoint(20, 0));
            _navigator.LoadRoot(root);

            Assert.That(_navigator.TryOpen(FindNode(root, And), out string primitiveError), Is.False);
            Assert.That(primitiveError, Does.Contain("Only custom gates"));
            Assert.That(_navigator.TryOpen(root.Inputs.Count > 0 ? root.Nodes[0].Id : NodeId.None, out _), Is.False);
            Assert.That(_navigator.TryOpen(ghost.Id, out string missingError), Is.False);
            Assert.That(missingError, Does.Contain("missing"));
            Assert.That(_navigator.Levels.Count, Is.EqualTo(1));
        }

        [Test]
        public void NestedNavigation_ThroughSeveralLevels_AndBackWithBreadcrumbs()
        {
            CircuitDefinition or = PublishOr();
            CircuitDefinition root = Wrap(or, "Main Circuit");
            LoadAndCompile(root);

            Assert.That(_navigator.TryOpen(FindNode(root, ComponentRef.Custom(or.Reference)), out _), Is.True);
            NodeId innerNand = FindNode(or, ComponentRef.Custom(_nand.Reference), 2);
            Assert.That(_navigator.TryOpen(innerNand, out string error), Is.True, error);

            Assert.That(_navigator.Levels.Count, Is.EqualTo(3));
            Assert.That(_navigator.PathText, Is.EqualTo("Main Circuit > OR > NAND"));
            Assert.That(_navigator.Current.Path.Length, Is.EqualTo(2));
            CompiledInstance scope = _navigator.ResolveScope(_navigator.Current);
            Assert.That(scope, Is.Not.Null);
            Assert.That(scope.Depth, Is.EqualTo(2));
            Assert.That(scope.NodeInParent, Is.EqualTo(innerNand));

            Assert.That(_navigator.TryNavigateTo(1, out _), Is.True);
            Assert.That(_navigator.Current.Circuit, Is.SameAs(or));
            Assert.That(_navigator.TryNavigateTo(0, out _), Is.True);
            Assert.That(_navigator.Current.Kind, Is.EqualTo(NavigationLevelKind.Root));
            Assert.That(_navigator.TryNavigateUp(out _), Is.False, "Already at the top.");
        }

        [Test]
        public void InspectedScopes_ShowTheSignalsOfTheirOwnInstance()
        {
            // Two NAND instances: the first sees A=1,A=1 → internal AND high; the second sees B=0,B=0 → AND low.
            var root = new CircuitDefinition(DefinitionId.New(), "Main");
            NodeDefinition a = root.AddInputBoundary("A", new CircuitPoint(0, 1));
            NodeDefinition b = root.AddInputBoundary("B", new CircuitPoint(0, -1));
            NodeDefinition first = root.AddNode(ComponentRef.Custom(_nand.Reference), new CircuitPoint(4, 1));
            NodeDefinition second = root.AddNode(ComponentRef.Custom(_nand.Reference), new CircuitPoint(4, -1));
            root.Connect(BoundaryOut(a), new PinRef(first.Id, _nand.Inputs[0].Id));
            root.Connect(BoundaryOut(a), new PinRef(first.Id, _nand.Inputs[1].Id));
            root.Connect(BoundaryOut(b), new PinRef(second.Id, _nand.Inputs[0].Id));
            root.Connect(BoundaryOut(b), new PinRef(second.Id, _nand.Inputs[1].Id));
            LoadAndCompile(root);
            _navigator.RootSession.SetInput(a.Id, Signal.High);
            _navigator.RootSession.SetInput(b.Id, Signal.Low);
            _navigator.RootSession.Runner.Context.Settle();

            NodeId and = FindNode(_nand, And);
            Assert.That(InternalAndOutput(first.Id, and), Is.EqualTo(Signal.High));
            Assert.That(_navigator.TryNavigateTo(0, out _), Is.True);
            Assert.That(InternalAndOutput(second.Id, and), Is.EqualTo(Signal.Low));
        }

        private Signal InternalAndOutput(NodeId instance, NodeId and)
        {
            Assert.That(_navigator.TryOpen(instance, out string error), Is.True, error);
            CompiledInstance scope = _navigator.ResolveScope(_navigator.Current);
            Assert.That(scope.TryGetOutputPinNet(new PinRef(and, AndGate.Output), out int net), Is.True);
            return _navigator.RootSession.Runner.Context.GetNet(net);
        }

        [Test]
        public void DepthLimit_RefusesOpeningBeyondIt()
        {
            CircuitDefinition or = PublishOr();
            CircuitDefinition root = Wrap(or, "Main");
            var shallow = new CircuitNavigator(_env.Catalog, _env.Library, maxDepth: 1);
            try
            {
                shallow.LoadRoot(root);
                Assert.That(shallow.TryOpen(FindNode(root, ComponentRef.Custom(or.Reference)), out _), Is.True);
                Assert.That(shallow.TryOpen(FindNode(or, ComponentRef.Custom(_nand.Reference)), out string error), Is.False);
                Assert.That(error, Does.Contain("limit"));
            }
            finally
            {
                shallow.Dispose();
            }
        }

        [Test]
        public void CircularNavigation_IsRefused()
        {
            CircuitDefinition root = Wrap(_nand, "Main");
            _navigator.LoadRoot(root);
            Assert.That(_navigator.TryOpen(FindNode(root, ComponentRef.Custom(_nand.Reference)), out _), Is.True);
            Assert.That(_navigator.TryBeginEdit(out string editError), Is.True, editError);

            // A draft of NAND that (wrongly) contains NAND v1: opening it would loop back to the same gate.
            NodeDefinition self = _navigator.Current.Circuit.AddNode(ComponentRef.Custom(_nand.Reference),
                new CircuitPoint(20, 0));
            Assert.That(_navigator.TryOpen(self.Id, out string error), Is.False);
            Assert.That(error, Does.Contain("circular"));
        }

        [Test]
        public void InternalLayout_IsPreservedInInspectionAndDrafts()
        {
            CircuitDefinition root = Wrap(_nand, "Main");
            _navigator.LoadRoot(root);
            string before = CircuitFingerprint.Of(_nand);
            Assert.That(_navigator.TryOpen(FindNode(root, ComponentRef.Custom(_nand.Reference)), out _), Is.True);
            Assert.That(_navigator.TryBeginEdit(out _), Is.True);

            CircuitDefinition draft = _navigator.Current.Circuit;
            foreach (NodeDefinition node in _nand.Nodes)
            {
                NodeDefinition copy = draft.GetNode(node.Id);
                Assert.That(copy.Position, Is.EqualTo(node.Position));
                Assert.That(copy.Rotation, Is.EqualTo(node.Rotation));
            }

            Assert.That(draft.Connections.Count, Is.EqualTo(_nand.Connections.Count));
            Assert.That(CircuitFingerprint.Of(_nand), Is.EqualTo(before));
        }

        [Test]
        public void BeginEdit_CreatesMutableDraftOfNextVersion_PublishedVersionUntouched()
        {
            CircuitDefinition root = Wrap(_nand, "Main");
            _navigator.LoadRoot(root);
            Assert.That(_navigator.TryBeginEdit(out string rootError), Is.False, "Root is already editable.");
            Assert.That(rootError, Is.Not.Null);
            Assert.That(_navigator.TryOpen(FindNode(root, ComponentRef.Custom(_nand.Reference)), out _), Is.True);
            string before = CircuitFingerprint.Of(_nand);

            Assert.That(_navigator.TryBeginEdit(out string error), Is.True, error);
            NavigationLevel draft = _navigator.Current;
            Assert.That(draft.Kind, Is.EqualTo(NavigationLevelKind.Draft));
            Assert.That(draft.Circuit.Id, Is.EqualTo(_nand.Id), "A new version keeps the stable definition id.");
            Assert.That(draft.Circuit.Version, Is.EqualTo(2));
            Assert.That(draft.Circuit.IsReadOnly, Is.False);
            Assert.That(draft.DraftSource, Is.EqualTo(_nand.Reference));
            Assert.That(draft.IsEditable && draft.CanToggleInputs, Is.True);
            Assert.That(draft.Session, Is.Not.SameAs(_navigator.RootSession), "Drafts simulate on their own.");

            draft.History.Execute(new RemoveNodesCommand(draft.Circuit, new[] { FindNode(draft.Circuit, Not) }));
            Assert.That(draft.HasUnsavedChanges, Is.True);
            Assert.That(CircuitFingerprint.Of(_nand), Is.EqualTo(before), "Editing never mutates a published version.");
            Assert.That(_navigator.TryBeginEdit(out _), Is.False, "Only one draft at a time.");
        }

        [Test]
        public void LeavingAnEditedDraft_RequiresSaveOrDiscard()
        {
            CircuitDefinition root = Wrap(_nand, "Main");
            _navigator.LoadRoot(root);
            _navigator.TryOpen(FindNode(root, ComponentRef.Custom(_nand.Reference)), out _);
            _navigator.TryBeginEdit(out _);
            NavigationLevel draft = _navigator.Current;
            draft.History.Execute(new RemoveNodesCommand(draft.Circuit, new[] { FindNode(draft.Circuit, Not) }));

            Assert.That(_navigator.TryNavigateTo(0, out string error), Is.False);
            Assert.That(error, Does.Contain("Save or discard"));
            Assert.That(_navigator.TryNavigateTo(0, out _, discardChanges: true), Is.True);
            Assert.That(_navigator.Draft, Is.Null);
            Assert.That(_env.Library.NextVersion(_nand.Id), Is.EqualTo(2), "Nothing was published.");
        }

        [Test]
        public void Discard_ReturnsToInspectingThePublishedVersion()
        {
            CircuitDefinition or = PublishOr();
            CircuitDefinition root = Wrap(or, "Main");
            LoadAndCompile(root);
            _navigator.TryOpen(FindNode(root, ComponentRef.Custom(or.Reference)), out _);
            _navigator.TryOpen(FindNode(or, ComponentRef.Custom(_nand.Reference)), out _);
            _navigator.TryBeginEdit(out _);

            Assert.That(_navigator.DiscardDraft(), Is.True);
            Assert.That(_navigator.Current.Kind, Is.EqualTo(NavigationLevelKind.Inspect));
            Assert.That(_navigator.Current.Circuit, Is.SameAs(_nand));
            Assert.That(_navigator.Levels.Count, Is.EqualTo(3));
            Assert.That(_navigator.ResolveScope(_navigator.Current), Is.Not.Null, "Back to the live instance view.");

            Assert.That(_navigator.TryBeginEdit(out _), Is.True);
            Assert.That(_navigator.TryNavigateTo(1, out _), Is.True, "An unchanged draft never blocks navigation.");
            Assert.That(_navigator.Draft, Is.Null);
        }

        [Test]
        public void ValidateStack_DropsLevelsWhoseInstanceDisappeared()
        {
            CircuitDefinition root = Wrap(_nand, "Main");
            _navigator.LoadRoot(root);
            NodeId instance = FindNode(root, ComponentRef.Custom(_nand.Reference));
            _navigator.TryOpen(instance, out _);
            root.RemoveNode(instance);

            Assert.That(_navigator.ValidateStack(), Is.True);
            Assert.That(_navigator.Levels.Count, Is.EqualTo(1));
        }
    }
}
