using System.Collections;
using System.Collections.Generic;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Navigation;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Rendering.Gates;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>Shared builders: a published NAND (AND + NOT) and a main circuit A, B → NAND → OUT.</summary>
    internal static class HierarchyFixtures
    {
        public static CircuitDefinition PublishNand(DefinitionLibrary library)
        {
            var nand = new CircuitDefinition(DefinitionId.New(), "NAND");
            NodeDefinition a = nand.AddInputBoundary("A", new CircuitPoint(-6, 1));
            NodeDefinition b = nand.AddInputBoundary("B", new CircuitPoint(-6, -1));
            NodeDefinition and = nand.AddNode(ComponentRef.Primitive(BuiltInComponentIds.And), new CircuitPoint(-1, 0));
            NodeDefinition not = nand.AddNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new CircuitPoint(3, 0));
            NodeDefinition output = nand.AddOutputBoundary("OUT", new CircuitPoint(7, 0));
            nand.Connect(new PinRef(a.Id, BoundarySignatures.Pin), new PinRef(and.Id, AndGate.InputA));
            nand.Connect(new PinRef(b.Id, BoundarySignatures.Pin), new PinRef(and.Id, AndGate.InputB));
            nand.Connect(new PinRef(and.Id, AndGate.Output), new PinRef(not.Id, NotGate.Input));
            nand.Connect(new PinRef(not.Id, NotGate.Output), new PinRef(output.Id, BoundarySignatures.Pin));
            library.Add(nand);
            return nand;
        }

        public static NodeId Find(CircuitDefinition circuit, string primitiveId)
        {
            foreach (NodeDefinition node in circuit.Nodes)
            {
                if (node.Component.Kind == ComponentKind.Primitive && node.Component.PrimitiveId == primitiveId)
                {
                    return node.Id;
                }
            }

            return NodeId.None;
        }

        public static NodeId Boundary(CircuitDefinition circuit, bool input, int index)
        {
            circuit.TryGetBoundaryNode(input ? circuit.Inputs[index].Id : circuit.Outputs[index].Id, out NodeId node);
            return node;
        }
    }

    /// <summary>
    /// Opening custom gates in the workspace: nested live signals, read-only inspection, breadcrumbs, edit mode and
    /// saving new versions (with and without updating references), all through the real editor and views.
    /// </summary>
    public sealed class HierarchyIntegrationTests
    {
        private readonly List<string> _notifications = new List<string>();
        private GameObject _cameraObject;
        private WorkspaceController _workspace;
        private DefinitionLibrary _library;
        private CircuitDefinition _nand;
        private NodeId _a;
        private NodeId _b;
        private NodeId _gate;
        private NodeId _out;

        [SetUp]
        public void SetUp()
        {
            _cameraObject = new GameObject("Test Camera");
            var camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            _library = new DefinitionLibrary();
            var catalog = new ComponentCatalog(PrimitiveRegistry.CreateDefault(), _library);
            _workspace = new GameObject("Workspace").AddComponent<WorkspaceController>();
            _workspace.Initialize(new WorkspaceSetup
            {
                Catalog = catalog,
                Library = _library,
                Theme = VisualTheme.CreateRuntimeDefault(),
                Camera = camera,
                IsPointerOverUi = () => false,
                IsTextInputFocused = () => false,
            });
            _workspace.WorkspaceCamera.Animate = false;
            _workspace.Notification += _notifications.Add;
            _nand = HierarchyFixtures.PublishNand(_library);

            _workspace.LoadCircuit(new CircuitDefinition(DefinitionId.New(), WorkspaceController.DefaultCircuitName));
            _a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, 1));
            _b = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, -1));
            _gate = _workspace.PlaceNode(ComponentRef.Custom(_nand.Reference), Vector2.zero);
            _out = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(6, 0));
            _workspace.Connect(new PinRef(_a, BoundarySignatures.Pin), new PinRef(_gate, _nand.Inputs[0].Id));
            _workspace.Connect(new PinRef(_b, BoundarySignatures.Pin), new PinRef(_gate, _nand.Inputs[1].Id));
            _workspace.Connect(new PinRef(_gate, _nand.Outputs[0].Id), new PinRef(_out, BoundarySignatures.Pin));
            _workspace.Selection.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_workspace.gameObject);
            Object.Destroy(_cameraObject);
        }

        private Signal OutputValue()
        {
            return _workspace.Navigator.RootSession.GetOutputValue(_out);
        }

        private Signal InternalOutput(string primitiveId)
        {
            NodeId node = HierarchyFixtures.Find(_workspace.Circuit, primitiveId);
            Assert.That(_workspace.Presenter.TryGetGateView(node, out GateView view), Is.True);
            return view.GetOutputSignal(0);
        }

        /// <summary>Edits the opened NAND into an AND: the NOT is removed and AND drives OUT.</summary>
        private void EditIntoAnd()
        {
            Assert.That(_workspace.BeginEdit(), Is.True, string.Join("\n", _notifications));
            CircuitDefinition draft = _workspace.Circuit;
            NodeId and = HierarchyFixtures.Find(draft, BuiltInComponentIds.And);
            _workspace.Selection.Select(HierarchyFixtures.Find(draft, BuiltInComponentIds.Not));
            Assert.That(_workspace.DeleteSelection(), Is.True);
            Assert.That(_workspace.Connect(new PinRef(and, AndGate.Output),
                new PinRef(HierarchyFixtures.Boundary(draft, false, 0), BoundarySignatures.Pin)), Is.EqualTo(ConnectionError.None));
        }

        [UnityTest]
        public IEnumerator OpenGate_ShowsInternalsWithLiveSignalsOfThatInstance()
        {
            _workspace.SetInput(_a, Signal.High);
            _workspace.SetInput(_b, Signal.High);
            yield return null;
            Assert.That(OutputValue(), Is.EqualTo(Signal.Low));

            Assert.That(_workspace.OpenNode(_gate), Is.True);
            yield return null;

            Assert.That(_workspace.Circuit, Is.SameAs(_nand));
            Assert.That(_workspace.Navigator.Levels.Count, Is.EqualTo(2));
            Assert.That(_workspace.Presenter.GateViewCount, Is.EqualTo(_nand.Nodes.Count), "Internal gates and boundaries.");
            Assert.That(_workspace.Presenter.WireViewCount, Is.EqualTo(_nand.Connections.Count));
            Assert.That(InternalOutput(BuiltInComponentIds.And), Is.EqualTo(Signal.High));
            Assert.That(InternalOutput(BuiltInComponentIds.Not), Is.EqualTo(Signal.Low));

            // A change in the parent propagates into the inspected instance's views.
            _workspace.Navigator.RootSession.SetInput(_b, Signal.Low);
            yield return null;
            Assert.That(InternalOutput(BuiltInComponentIds.And), Is.EqualTo(Signal.Low));
            Assert.That(InternalOutput(BuiltInComponentIds.Not), Is.EqualTo(Signal.High));
        }

        [UnityTest]
        public IEnumerator OpenFromLibrary_PreviewsGateWithoutAnInstance()
        {
            _workspace.SetInput(_a, Signal.High);
            _workspace.SetInput(_b, Signal.High);
            yield return null;
            Assert.That(OutputValue(), Is.EqualTo(Signal.Low));

            Assert.That(_workspace.CanOpenDefinition(_nand.Reference), Is.True);
            Assert.That(_workspace.OpenDefinition(_nand.Reference), Is.True, string.Join("\n", _notifications));
            yield return null;

            Assert.That(_workspace.CurrentLevel.IsPreview, Is.True);
            Assert.That(_workspace.Circuit, Is.SameAs(_nand));
            Assert.That(_workspace.IsEditable, Is.False);
            Assert.That(_workspace.Presenter.GateViewCount, Is.EqualTo(_nand.Nodes.Count));

            // The preview's inputs are its own: toggling them never reaches the main circuit.
            NodeId previewA = HierarchyFixtures.Boundary(_nand, true, 0);
            Assert.That(_workspace.CanToggleInputs, Is.True);
            Assert.That(_workspace.ToggleInput(previewA), Is.True);
            yield return null;
            Assert.That(OutputValue(), Is.EqualTo(Signal.Low), "Main circuit unchanged.");
            Assert.That(_workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), Vector2.zero),
                Is.EqualTo(NodeId.None), "Previews are read-only.");

            // Edit from the preview, then back to the main circuit.
            Assert.That(_workspace.BeginEdit(), Is.True, string.Join("\n", _notifications));
            Assert.That(_workspace.CurrentLevel.IsDraft, Is.True);
            Assert.That(_workspace.DiscardDraft(), Is.True);
            Assert.That(_workspace.CurrentLevel.IsPreview, Is.True);
            Assert.That(_workspace.NavigateTo(0), Is.True);
            yield return null;
            Assert.That(_workspace.Circuit, Is.Not.SameAs(_nand));
            Assert.That(_workspace.Navigator.PreviewSession, Is.Null);
        }

        [UnityTest]
        public IEnumerator InspectionMode_IsReadOnly()
        {
            _workspace.OpenNode(_gate);
            yield return null;
            string before = _nand.Nodes.Count + "/" + _nand.Connections.Count;
            NodeId and = HierarchyFixtures.Find(_nand, BuiltInComponentIds.And);

            Assert.That(_workspace.IsEditable, Is.False);
            Assert.That(_workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), Vector2.zero), Is.EqualTo(NodeId.None));
            _workspace.Selection.Select(and);
            Assert.That(_workspace.DeleteSelection(), Is.False);
            Assert.That(_workspace.DuplicateSelection(), Is.False);
            Assert.That(_workspace.RotateSelection(), Is.False);
            Assert.That(_workspace.ToggleInput(HierarchyFixtures.Boundary(_nand, true, 0)), Is.False);
            Assert.That(_workspace.CheckConnection(new PinRef(and, AndGate.Output), new PinRef(and, AndGate.InputA), out _, out _),
                Is.EqualTo(ConnectionError.CircuitReadOnly));
            Assert.That(_workspace.History.CanUndo, Is.False);
            Assert.That(_workspace.CanPackage, Is.False);
            Assert.That(_workspace.PackageCustomGate(_workspace.CreatePackagingSpec(false)).Succeeded, Is.False);
            Assert.That(_nand.Nodes.Count + "/" + _nand.Connections.Count, Is.EqualTo(before));
            Assert.That(_notifications.Count, Is.GreaterThan(0), "Refusals are explained to the user.");
        }

        [UnityTest]
        public IEnumerator Breadcrumbs_ReturnToMainCircuit_WithViewsAndCameraRestored()
        {
            _workspace.WorkspaceCamera.MoveTo(new Vector2(3f, -2f), 7f);
            _workspace.OpenNode(_gate);
            yield return null;
            Assert.That(_workspace.NavigateTo(0), Is.True);
            yield return null;

            Assert.That(_workspace.CurrentLevel.Kind, Is.EqualTo(NavigationLevelKind.Root));
            Assert.That(_workspace.Presenter.TryGetGateView(_gate, out _), Is.True);
            Assert.That(_workspace.WorkspaceCamera.Position.x, Is.EqualTo(3f).Within(0.01f));
            Assert.That(_workspace.WorkspaceCamera.Size, Is.EqualTo(7f).Within(0.01f));
            Assert.That(_workspace.History.CanUndo, Is.True, "The main circuit's history is back.");

            _workspace.SetInput(_a, Signal.High);
            yield return null;
            Assert.That(OutputValue(), Is.EqualTo(Signal.High), "Simulation keeps working after navigation.");
        }

        [UnityTest]
        public IEnumerator SaveDraft_UpdatingMainCircuit_SwitchesInstance_AndUndoRestores()
        {
            _workspace.OpenNode(_gate);
            EditIntoAnd();
            yield return null;
            Assert.That(_workspace.CurrentLevel.IsDraft, Is.True);
            Assert.That(_workspace.Circuit.Version, Is.EqualTo(2));
            Assert.That(_workspace.NavigateTo(0), Is.False, "Unsaved draft blocks navigation.");

            DraftSaveResult result = _workspace.SaveDraft(VersionUpdateMode.UpdateMainCircuit);
            yield return null;

            Assert.That(result.Succeeded, Is.True, result.Error + " " + result.Report);
            Assert.That(result.RetargetedNodes, Is.EqualTo(1));
            Assert.That(_workspace.CurrentLevel.Kind, Is.EqualTo(NavigationLevelKind.Root));
            Assert.That(_library.Contains(_nand.Reference) && _library.Contains(result.Saved.Reference), Is.True);
            DefinitionRef v2 = result.Saved.Reference;
            Assert.That(_workspace.Circuit.GetNode(_gate).Component.Definition, Is.EqualTo(v2));
            Assert.That(_workspace.Presenter.TryGetGateView(_gate, out GateView view) && view.Geometry.Title == "NAND v2", Is.True);

            int[,] and = { { 0, 0, 0 }, { 0, 1, 0 }, { 1, 0, 0 }, { 1, 1, 1 } };
            for (int row = 0; row < 4; row++)
            {
                _workspace.SetInput(_a, and[row, 0] == 1 ? Signal.High : Signal.Low);
                _workspace.SetInput(_b, and[row, 1] == 1 ? Signal.High : Signal.Low);
                yield return null;
                Assert.That(OutputValue(), Is.EqualTo(and[row, 2] == 1 ? Signal.High : Signal.Low), "Row " + row);
            }

            Assert.That(_workspace.Undo(), Is.True);
            yield return null;
            Assert.That(_workspace.Circuit.GetNode(_gate).Component.Definition, Is.EqualTo(_nand.Reference));
            Assert.That(_library.Contains(v2), Is.False);
            Assert.That(OutputValue(), Is.EqualTo(Signal.Low), "1 NAND 1 again.");

            Assert.That(_workspace.Redo(), Is.True);
            yield return null;
            Assert.That(_workspace.Circuit.GetNode(_gate).Component.Definition, Is.EqualTo(v2));
            Assert.That(OutputValue(), Is.EqualTo(Signal.High));
        }

        [UnityTest]
        public IEnumerator SaveDraft_KeepingReferences_LeavesExistingInstancesOnOldVersion()
        {
            _workspace.SetInput(_a, Signal.High);
            _workspace.SetInput(_b, Signal.High);
            _workspace.OpenNode(_gate);
            EditIntoAnd();
            DraftSaveResult result = _workspace.SaveDraft(VersionUpdateMode.KeepReferences);
            yield return null;

            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(_workspace.Circuit.GetNode(_gate).Component.Definition, Is.EqualTo(_nand.Reference));
            Assert.That(OutputValue(), Is.EqualTo(Signal.Low), "Still NAND v1.");
            Assert.That(_library.NextVersion(_nand.Id), Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator Draft_RejectsSelfContainment_AndInvalidSaves()
        {
            _workspace.OpenNode(_gate);
            Assert.That(_workspace.BeginEdit(), Is.True);
            yield return null;
            Assert.That(_workspace.SaveDraft(VersionUpdateMode.KeepReferences).Succeeded, Is.False, "Nothing changed.");

            Assert.That(_workspace.PlaceNode(ComponentRef.Custom(_nand.Reference), new Vector2(10, 0)), Is.EqualTo(NodeId.None),
                "A gate cannot contain itself.");

            _workspace.Selection.Select(HierarchyFixtures.Find(_workspace.Circuit, BuiltInComponentIds.Not));
            _workspace.DeleteSelection(); // output now undriven
            DraftSaveResult invalid = _workspace.SaveDraft(VersionUpdateMode.KeepReferences);
            Assert.That(invalid.Succeeded, Is.False);
            Assert.That(invalid.Report.HasErrors, Is.True);
            Assert.That(_library.NextVersion(_nand.Id), Is.EqualTo(2), "Nothing published.");

            Assert.That(_workspace.DiscardDraft(), Is.True);
            yield return null;
            Assert.That(_workspace.Circuit, Is.SameAs(_nand), "Back to inspecting the original.");
        }

        [UnityTest]
        public IEnumerator DraftSimulatesOnItsOwn_WithToggleableInputs()
        {
            _workspace.OpenNode(_gate);
            _workspace.BeginEdit();
            yield return null;
            NodeId a = HierarchyFixtures.Boundary(_workspace.Circuit, true, 0);
            NodeId b = HierarchyFixtures.Boundary(_workspace.Circuit, true, 1);
            NodeId output = HierarchyFixtures.Boundary(_workspace.Circuit, false, 0);

            Assert.That(_workspace.CanToggleInputs, Is.True);
            Assert.That(_workspace.ToggleInput(a), Is.True);
            Assert.That(_workspace.ToggleInput(b), Is.True);
            yield return null;
            Assert.That(_workspace.Session.GetOutputValue(output), Is.EqualTo(Signal.Low));
            Assert.That(OutputValue(), Is.EqualTo(Signal.High), "The main circuit's inputs are untouched (both 0).");
        }

        [UnityTest]
        public IEnumerator InsideAnInspectedGate_ChangedSignalsAnimate()
        {
            _workspace.AnimationSettings.Enabled = true;
            _workspace.OpenNode(_gate);
            yield return null;
            Assert.That(_workspace.Circuit, Is.SameAs(_nand));
            Assert.That(_workspace.SignalAnimation.IsAnimating, Is.False);

            // Raise both main inputs: inside NAND the AND output rises and the NOT output falls.
            CircuitSession root = _workspace.Navigator.RootSession;
            Assert.That(root.ToggleInput(_a), Is.True);
            Assert.That(root.ToggleInput(_b), Is.True);
            _workspace.Sync();

            Assert.That(_workspace.SignalAnimation.PulseCount, Is.GreaterThan(0),
                "The inspected level's wires carry pulses instead of being recoloured instantly.");
            Assert.That(_workspace.SignalAnimation.AnimatedWireCount, Is.GreaterThan(0));
            Assert.That(InternalOutput(BuiltInComponentIds.Not), Is.EqualTo(Signal.High),
                "The NOT output still shows its old value while the pulse travels.");

            _workspace.SignalAnimation.CompleteAll();
            Assert.That(InternalOutput(BuiltInComponentIds.And), Is.EqualTo(Signal.High));
            Assert.That(InternalOutput(BuiltInComponentIds.Not), Is.EqualTo(Signal.Low));
            Assert.That(OutputValue(), Is.EqualTo(Signal.Low), "Simulation result does not depend on animation.");
        }

        [UnityTest]
        public IEnumerator MainClock_KeepsTicking_WhileADraftIsEdited_AndViewsCatchUpOnReturn()
        {
            NodeId clock = _workspace.PlaceNode(ComponentRef.Clock, new Vector2(-6, -5));
            NodeId lamp = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(6, -5));
            Assert.That(_workspace.Connect(new PinRef(clock, BoundarySignatures.Pin), new PinRef(lamp, BoundarySignatures.Pin)),
                Is.EqualTo(ConnectionError.None));
            _workspace.ClockFrequency = 50f;
            _workspace.Navigator.ClockEnabled = false;
            _workspace.Sync();
            yield return null;
            CircuitSession root = _workspace.Navigator.RootSession;
            Signal before = root.GetOutputValue(lamp);
            Assert.That(_workspace.Presenter.TryGetGateView(lamp, out GateView shownBefore), Is.True);
            Assert.That(shownBefore.GetInputSignal(0), Is.EqualTo(before));

            _workspace.OpenNode(_gate);
            Assert.That(_workspace.BeginEdit(), Is.True, string.Join("\n", _notifications));
            yield return null;
            Assert.That(_workspace.Session, Is.Not.SameAs(root), "The draft has its own session.");

            // Run until the main circuit's lamp has changed, then hold the clock still.
            long edges = root.Runner.ClockEdges;
            _workspace.Navigator.ClockEnabled = true;
            float timeout = Time.realtimeSinceStartup + 5f;
            while (root.GetOutputValue(lamp) == before && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            _workspace.Navigator.ClockEnabled = false;
            Assert.That(root.Runner.ClockEdges, Is.GreaterThan(edges), "The main clock ticks while the draft is shown.");
            Signal after = root.GetOutputValue(lamp);
            Assert.That(after, Is.Not.EqualTo(before));
            Assert.That(root.Runner.Context.Changes.Count, Is.Zero, "Background changes are not queued for drawing.");

            Assert.That(_workspace.DiscardDraft(), Is.True);
            Assert.That(_workspace.NavigateTo(0), Is.True);
            yield return null;
            Assert.That(_workspace.Presenter.TryGetGateView(lamp, out GateView shownAfter), Is.True);
            Assert.That(shownAfter.GetInputSignal(0), Is.EqualTo(after), "Returning redraws from the live simulation.");
        }
    }

    /// <summary>The navigation UI in the real Main scene: context menu, breadcrumbs, edit/save flow.</summary>
    public sealed class NavigationUiTests
    {
        [UnityTest]
        public IEnumerator ContextMenu_Breadcrumbs_AndVersionDialog()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            AppBootstrap app = AppBootstrap.Instance;
            WorkspaceController workspace = app.Workspace;
            workspace.WorkspaceCamera.Animate = false;
            CircuitDefinition nand = HierarchyFixtures.PublishNand(app.Library);
            NodeId gate = workspace.PlaceNode(ComponentRef.Custom(nand.Reference), Vector2.zero);
            workspace.Selection.Clear();
            yield return null;

            Vector2 screen = workspace.WorkspaceCamera.WorldToScreen(Vector2.zero);
            workspace.RequestContextMenu(screen, Vector2.zero);
            Assert.That(app.Shell.ContextMenu.IsOpen, Is.True);
            Assert.That(app.Shell.ContextMenu.Labels, Does.Contain("Open internal circuit"));
            Assert.That(workspace.Selection.Contains(gate), Is.True, "Right-click selects the gate.");
            Assert.That(app.Shell.ContextMenu.Invoke("Open internal circuit"), Is.True);
            yield return null;

            Assert.That(app.Shell.ContextMenu.IsOpen, Is.False);
            Assert.That(workspace.Circuit, Is.SameAs(nand));
            Assert.That(app.Shell.NavigationBar.Crumbs.Count, Is.EqualTo(2));
            Assert.That(app.Shell.NavigationBar.ModeText, Does.Contain("INSPECTING"));
            Assert.That(app.Shell.NavigationBar.EditButton.gameObject.activeSelf, Is.True);
            Assert.That(app.Shell.Inspector.HeaderText, Does.Contain("inspecting"));

            app.Shell.NavigationBar.EditButton.onClick.Invoke();
            yield return null;
            Assert.That(workspace.CurrentLevel.IsDraft, Is.True);
            Assert.That(app.Shell.NavigationBar.SaveButton.gameObject.activeSelf, Is.True);
            Assert.That(app.Shell.NavigationBar.SaveButton.interactable, Is.False, "No changes yet.");

            NodeId not = HierarchyFixtures.Find(workspace.Circuit, BuiltInComponentIds.Not);
            workspace.Selection.Select(not);
            workspace.RotateSelection();
            yield return null;
            Assert.That(app.Shell.NavigationBar.SaveButton.interactable, Is.True);

            app.Shell.NavigationBar.SaveButton.onClick.Invoke();
            Assert.That(app.Shell.VersionSaveDialog.IsOpen, Is.True);
            Assert.That(workspace.InputSuspended, Is.True);
            Assert.That(app.Shell.VersionSaveDialog.SelectedMode, Is.EqualTo(VersionUpdateMode.UpdateMainCircuit),
                "The main circuit uses the gate, so updating it is the default.");
            Assert.That(app.Shell.VersionSaveDialog.ImpactText, Does.Contain("1 instance"));
            app.Shell.VersionSaveDialog.SaveButton.onClick.Invoke();
            yield return null;

            Assert.That(app.Shell.VersionSaveDialog.IsOpen, Is.False);
            Assert.That(workspace.CurrentLevel.Kind, Is.EqualTo(NavigationLevelKind.Root));
            Assert.That(app.Shell.NavigationBar.Crumbs.Count, Is.EqualTo(1));
            Assert.That(workspace.Circuit.GetNode(gate).Component.Definition.Version, Is.EqualTo(2));
            Assert.That(app.Shell.StatusBar.MessageText, Does.Contain("Saved 'NAND' v2"));

            // Crumb click path: open again and use the first crumb to go back.
            workspace.OpenNode(gate);
            yield return null;
            app.Shell.NavigationBar.Crumbs[0].onClick.Invoke();
            yield return null;
            Assert.That(workspace.CurrentLevel.Kind, Is.EqualTo(NavigationLevelKind.Root));
        }
    }
}
