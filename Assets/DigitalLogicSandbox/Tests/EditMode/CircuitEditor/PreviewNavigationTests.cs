using DigitalLogicSandbox.CircuitEditor.Navigation;
using DigitalLogicSandbox.Commands.Circuit;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.CircuitEditor
{
    /// <summary>Opening a published gate straight from the library (preview level, no instance).</summary>
    public sealed class PreviewNavigationTests
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

        private static NodeId Find(CircuitDefinition circuit, ComponentRef component, int occurrence = 0)
        {
            return NavigationTests.FindNode(circuit, component, occurrence);
        }

        [Test]
        public void OpenDefinition_ShowsReadOnlyPreviewOnItsOwnSession()
        {
            CircuitDefinition root = Wrap(_nand, "Main");
            _navigator.LoadRoot(root);
            _navigator.RebuildDirtySessions();
            NodeId rootOutput = Find(root, ComponentRef.OutputBoundary);
            Signal rootBefore = _navigator.RootSession.GetOutputValue(rootOutput);

            Assert.That(_navigator.TryOpenDefinition(_nand.Reference, out string error), Is.True, error);

            NavigationLevel level = _navigator.Current;
            Assert.That(level.Kind, Is.EqualTo(NavigationLevelKind.Preview));
            Assert.That(level.IsPreview, Is.True);
            Assert.That(level.Circuit, Is.SameAs(_nand), "The preview shows the shared published definition.");
            Assert.That(level.IsEditable, Is.False);
            Assert.That(level.CanToggleInputs, Is.True, "Nothing drives a preview's inputs, so they can be toggled.");
            Assert.That(level.Session, Is.Not.SameAs(_navigator.RootSession));
            Assert.That(_navigator.Levels.Count, Is.EqualTo(2));

            _navigator.RebuildDirtySessions();
            Assert.That(level.Session.IsSimulating, Is.True, level.Session.Report.ToString());
            NodeId a = Find(_nand, ComponentRef.InputBoundary, 0);
            NodeId b = Find(_nand, ComponentRef.InputBoundary, 1);
            NodeId output = Find(_nand, ComponentRef.OutputBoundary);
            level.Session.SetInput(a, Signal.High);
            level.Session.SetInput(b, Signal.High);
            Assert.That(level.Session.GetOutputValue(output), Is.EqualTo(Signal.Low), "NAND(1, 1) = 0.");
            Assert.That(_navigator.RootSession.GetOutputValue(rootOutput), Is.EqualTo(rootBefore),
                "The main circuit's simulation is untouched.");
            Assert.That(_navigator.ResolveScope(level), Is.Not.Null);
        }

        [Test]
        public void OpenDefinition_ReplacesOpenedLevels()
        {
            CircuitDefinition root = Wrap(_nand, "Main");
            _navigator.LoadRoot(root);
            Assert.That(_navigator.TryOpen(Find(root, ComponentRef.Custom(_nand.Reference)), out _), Is.True);

            Assert.That(_navigator.TryOpenDefinition(_nand.Reference, out string error), Is.True, error);
            Assert.That(_navigator.Levels.Count, Is.EqualTo(2));
            Assert.That(_navigator.Current.IsPreview, Is.True);
        }

        [Test]
        public void NestedGatesOpenFromAPreview()
        {
            CircuitDefinition or = BuildOrFromNand(_nand.Reference, _nand, "OR");
            _env.Publish(or);
            _navigator.LoadRoot(new CircuitDefinition(DefinitionId.New(), "Main"));
            Assert.That(_navigator.TryOpenDefinition(or.Reference, out string error), Is.True, error);
            _navigator.RebuildDirtySessions();
            NavigationLevel preview = _navigator.Current;

            NodeId nand = Find(or, ComponentRef.Custom(_nand.Reference));
            Assert.That(_navigator.TryOpen(nand, out error), Is.True, error);
            NavigationLevel inner = _navigator.Current;
            Assert.That(inner.Kind, Is.EqualTo(NavigationLevelKind.Inspect));
            Assert.That(inner.Session, Is.SameAs(preview.Session), "Nested levels read the preview's simulation.");
            Assert.That(inner.Path, Is.EqualTo(new[] { nand }));
            Assert.That(_navigator.ResolveScope(inner), Is.Not.Null);

            Assert.That(_navigator.TryNavigateTo(1, out error), Is.True, error);
            Assert.That(_navigator.Current, Is.SameAs(preview));
        }

        [Test]
        public void EditFromPreview_CreatesNextVersion_DiscardReturnsToPreview()
        {
            _navigator.LoadRoot(new CircuitDefinition(DefinitionId.New(), "Main"));
            _navigator.TryOpenDefinition(_nand.Reference, out _);
            NavigationLevel preview = _navigator.Current;

            Assert.That(_navigator.CanBeginEdit(out string error), Is.True, error);
            Assert.That(_navigator.TryBeginEdit(out error), Is.True, error);
            NavigationLevel draft = _navigator.Current;
            Assert.That(draft.IsDraft, Is.True);
            Assert.That(draft.Circuit.Version, Is.EqualTo(2));
            Assert.That(draft.DraftSource, Is.EqualTo(_nand.Reference));

            Assert.That(_navigator.DiscardDraft(), Is.True);
            Assert.That(_navigator.Current, Is.SameAs(preview));
            Assert.That(_navigator.PreviewSession, Is.SameAs(preview.Session), "The preview session survives a discard.");

            Assert.That(_navigator.TryNavigateTo(0, out error), Is.True, error);
            Assert.That(_navigator.PreviewSession, Is.Null, "Leaving the preview releases its session.");
        }

        [Test]
        public void OpenDefinition_RefusesMissingDefinitionAndUnsavedDraft()
        {
            CircuitDefinition root = Wrap(_nand, "Main");
            _navigator.LoadRoot(root);
            CircuitDefinition unpublished = BuildNand("Unpublished");

            Assert.That(_navigator.TryOpenDefinition(unpublished.Reference, out string error), Is.False);
            Assert.That(error, Does.Contain("missing"));
            Assert.That(_navigator.Levels.Count, Is.EqualTo(1));

            _navigator.TryOpen(Find(root, ComponentRef.Custom(_nand.Reference)), out _);
            _navigator.TryBeginEdit(out _);
            NavigationLevel draft = _navigator.Current;
            draft.History.Execute(new RemoveNodesCommand(draft.Circuit, new[] { Find(draft.Circuit, Not) }));

            Assert.That(_navigator.TryOpenDefinition(_nand.Reference, out error), Is.False);
            Assert.That(error, Does.Contain("Save or discard"));
            Assert.That(_navigator.Current, Is.SameAs(draft));
        }

        [Test]
        public void ValidateStack_DropsPreviewWhenItsDefinitionIsRemoved()
        {
            CircuitDefinition extra = BuildNand("Extra");
            _env.Publish(extra);
            _navigator.LoadRoot(new CircuitDefinition(DefinitionId.New(), "Main"));
            _navigator.TryOpenDefinition(extra.Reference, out _);

            _env.Library.Remove(extra.Reference);
            Assert.That(_navigator.ValidateStack(), Is.True);
            Assert.That(_navigator.Levels.Count, Is.EqualTo(1));
            Assert.That(_navigator.PreviewSession, Is.Null);
        }
    }
}
