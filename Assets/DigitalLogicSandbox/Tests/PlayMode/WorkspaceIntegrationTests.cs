using System.Collections;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Placement;
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
using DigitalLogicSandbox.Rendering.Wires;
using DigitalLogicSandbox.UI.Panels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>
    /// Drives a headless workspace (no input map) through its public API: build a circuit, wire it, toggle inputs,
    /// and check model, simulation and views agree. Undo and redo are covered too.
    /// </summary>
    public sealed class WorkspaceIntegrationTests
    {
        private GameObject _cameraObject;
        private WorkspaceController _workspace;
        private DefinitionLibrary _library;

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
                Theme = VisualTheme.CreateRuntimeDefault(),
                Camera = camera,
                IsPointerOverUi = () => false,
                IsTextInputFocused = () => false,
            });
            _workspace.WorkspaceCamera.Animate = false;
            _workspace.LoadCircuit(new CircuitDefinition(DefinitionId.New(), "Main"));
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_workspace.gameObject);
            Object.Destroy(_cameraObject);
        }

        [UnityTest]
        public IEnumerator BuildAndCircuit_ToggleInputs_ViewsFollowSimulation()
        {
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, 1));
            NodeId b = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, -1));
            NodeId and = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.And), new Vector2(0, 0));
            NodeId output = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(6, 0));

            Assert.That(_workspace.Circuit.Inputs[0].Name, Is.EqualTo("A"));
            Assert.That(_workspace.Circuit.Inputs[1].Name, Is.EqualTo("B"));
            Assert.That(_workspace.Circuit.Outputs[0].Name, Is.EqualTo("OUT"));

            // Either order works: here the destination is given first.
            Assert.That(_workspace.Connect(new PinRef(and, AndGate.InputA), Pin(a)), Is.EqualTo(ConnectionError.None));
            Assert.That(_workspace.Connect(Pin(b), new PinRef(and, AndGate.InputB)), Is.EqualTo(ConnectionError.None));
            Assert.That(_workspace.Connect(new PinRef(and, AndGate.Output), Pin(output)), Is.EqualTo(ConnectionError.None));
            Assert.That(_workspace.Connect(Pin(b), new PinRef(and, AndGate.InputB)),
                Is.EqualTo(ConnectionError.DestinationAlreadyDriven));
            Assert.That(_workspace.Connect(Pin(a), Pin(b)), Is.Not.EqualTo(ConnectionError.None), "Output to output is rejected.");

            yield return null; // LateUpdate: recompile + presenter refresh

            Assert.That(_workspace.Session.IsSimulating, Is.True);
            Assert.That(_workspace.Presenter.GateViewCount, Is.EqualTo(4));
            Assert.That(_workspace.Presenter.WireViewCount, Is.EqualTo(3));
            Assert.That(OutputIndicator(output), Is.EqualTo(Signal.Low));

            _workspace.ToggleInput(a);
            _workspace.ToggleInput(b);
            yield return null;

            Assert.That(_workspace.Session.GetOutputValue(output), Is.EqualTo(Signal.High));
            Assert.That(OutputIndicator(output), Is.EqualTo(Signal.High));
            Assert.That(_workspace.Presenter.TryGetWireView(Pin(output), out WireView wire), Is.True);
            Assert.That(wire.Signal, Is.EqualTo(Signal.High));

            _workspace.ToggleInput(b);
            yield return null;
            Assert.That(OutputIndicator(output), Is.EqualTo(Signal.Low));
        }

        [UnityTest]
        public IEnumerator UndoRedo_RestoresModelViewsAndSimulation()
        {
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-4, 0));
            NodeId not = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(0, 0));
            NodeId output = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(4, 0));
            _workspace.Connect(Pin(a), new PinRef(not, NotGate.Input));
            _workspace.Connect(new PinRef(not, NotGate.Output), Pin(output));
            yield return null;
            Assert.That(OutputIndicator(output), Is.EqualTo(Signal.High));

            _workspace.Undo(); // disconnect NOT → OUT
            yield return null;
            Assert.That(_workspace.Presenter.WireViewCount, Is.EqualTo(1));
            Assert.That(OutputIndicator(output), Is.EqualTo(Signal.Low), "An undriven output floats LOW.");

            _workspace.Redo();
            yield return null;
            Assert.That(_workspace.Presenter.WireViewCount, Is.EqualTo(2));
            Assert.That(OutputIndicator(output), Is.EqualTo(Signal.High));

            _workspace.Selection.Select(not);
            Assert.That(_workspace.DeleteSelection(), Is.True);
            yield return null;
            Assert.That(_workspace.Circuit.Nodes.Count, Is.EqualTo(2));
            Assert.That(_workspace.Presenter.WireViewCount, Is.EqualTo(0));
            Assert.That(_workspace.Selection.IsEmpty, Is.True);

            _workspace.Undo();
            yield return null;
            Assert.That(_workspace.Circuit.ContainsNode(not), Is.True);
            Assert.That(_workspace.Presenter.WireViewCount, Is.EqualTo(2));
            Assert.That(OutputIndicator(output), Is.EqualTo(Signal.High));
        }

        [UnityTest]
        public IEnumerator MoveRotateDuplicate_AreUndoable_AndRerouteWires()
        {
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-4, 0));
            NodeId not = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(0, 0));
            _workspace.Connect(Pin(a), new PinRef(not, NotGate.Input));
            yield return null;

            _workspace.Presenter.TryGetWireView(new PinRef(not, NotGate.Input), out WireView wire);
            Vector3 endBefore = wire.Line.GetPosition(wire.Line.positionCount - 1);

            _workspace.MoveNodes(new[] { not }, new[] { new CircuitPoint(2, 3) });
            Vector3 endAfter = wire.Line.GetPosition(wire.Line.positionCount - 1);
            Assert.That(endAfter.y - endBefore.y, Is.EqualTo(3f).Within(1e-4f), "Wire follows the moved gate.");
            Assert.That(_workspace.Presenter.TryGetGateView(not, out GateView view) &&
                (Vector2)view.transform.position == new Vector2(2, 3), Is.True);

            _workspace.Selection.Select(not);
            _workspace.RotateSelection();
            Assert.That(_workspace.Circuit.GetNode(not).Rotation, Is.EqualTo(Rotation.R90));

            _workspace.DuplicateSelection();
            Assert.That(_workspace.Circuit.Nodes.Count, Is.EqualTo(3));
            Assert.That(_workspace.Selection.NodeCount, Is.EqualTo(1));
            Assert.That(_workspace.Selection.Contains(not), Is.False, "The copy becomes the selection.");

            _workspace.Undo();
            _workspace.Undo();
            _workspace.Undo();
            Assert.That(_workspace.Circuit.Nodes.Count, Is.EqualTo(2));
            Assert.That(_workspace.Circuit.GetNode(not).Position, Is.EqualTo(new CircuitPoint(0, 0)));
            Assert.That(_workspace.Circuit.GetNode(not).Rotation, Is.EqualTo(Rotation.R0));
        }

        [UnityTest]
        public IEnumerator Placement_SnapsToGrid_AndPausedSimulationSteps()
        {
            _workspace.BeginPlacement(new PlacementRequest(ComponentRef.InputBoundary, "Input"), fromDrag: false);
            Assert.That(_workspace.IsPlacing, Is.True);
            NodeId a = _workspace.CommitPlacement(new Vector2(-3.2f, 0.1f), keepPlacing: false);
            Assert.That(_workspace.IsPlacing, Is.False);
            Assert.That(_workspace.Circuit.GetNode(a).Position, Is.EqualTo(new CircuitPoint(-3f, 0f)));

            NodeId not1 = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(0, 0));
            NodeId not2 = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(4, 0));
            NodeId output = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(8, 0));
            _workspace.Connect(Pin(a), new PinRef(not1, NotGate.Input));
            _workspace.Connect(new PinRef(not1, NotGate.Output), new PinRef(not2, NotGate.Input));
            _workspace.Connect(new PinRef(not2, NotGate.Output), Pin(output));
            yield return null;

            _workspace.Pause();
            _workspace.ToggleInput(a);
            yield return null;
            Assert.That(_workspace.Session.GetOutputValue(output), Is.EqualTo(Signal.Low), "Paused: nothing propagates.");

            int steps = 0;
            while (_workspace.Step() && steps < 10)
            {
                steps++;
            }

            yield return null;
            Assert.That(_workspace.Session.GetOutputValue(output), Is.EqualTo(Signal.High));
            Assert.That(OutputIndicator(output), Is.EqualTo(Signal.High));
            Assert.That(steps, Is.GreaterThanOrEqualTo(1), "Two NOT gates take more than one wave.");
        }

        [UnityTest]
        public IEnumerator ToggleInput_InSameFrameAsEdits_IsNotLost()
        {
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-4, 0));
            NodeId output = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(4, 0));
            _workspace.Connect(Pin(a), Pin(output));
            Assert.That(_workspace.ToggleInput(a), Is.True, "Pending edits are compiled before toggling.");
            yield return null;
            Assert.That(_workspace.Session.GetInputValue(a), Is.EqualTo(Signal.High));
            Assert.That(OutputIndicator(output), Is.EqualTo(Signal.High));
        }

        private static PinRef Pin(NodeId boundary)
        {
            return new PinRef(boundary, BoundarySignatures.Pin);
        }

        private Signal OutputIndicator(NodeId outputBoundary)
        {
            Assert.That(_workspace.Presenter.TryGetGateView(outputBoundary, out GateView view), Is.True);
            return view.StateSignal;
        }
    }

    /// <summary>Boots the real Main scene and checks the composed application.</summary>
    public sealed class AppBootstrapTests
    {
        [UnityTest]
        public IEnumerator MainScene_ComposesWorkspaceAndUi_WithOnlyAndNotBuiltIn()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;

            AppBootstrap app = AppBootstrap.Instance;
            Assert.That(app, Is.Not.Null);
            Assert.That(app.Workspace.IsInitialized, Is.True);
            Assert.That(app.Workspace.Circuit, Is.Not.Null);

            int builtIns = 0;
            foreach (LibraryItemView item in app.Shell.Library.Items)
            {
                if (item.Request.Component.Kind == ComponentKind.Primitive)
                {
                    builtIns++;
                    Assert.That(item.Request.Component.PrimitiveId,
                        Is.EqualTo(BuiltInComponentIds.And).Or.EqualTo(BuiltInComponentIds.Not));
                }
            }

            Assert.That(builtIns, Is.EqualTo(2));

            NodeId node = app.Workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.And), Vector2.zero);
            yield return null;
            Assert.That(app.Shell.Toolbar.UndoButton.interactable, Is.True);
            Assert.That(app.Workspace.Circuit.ContainsNode(node), Is.True);

            app.Shell.Library.SetFilter("not");
            int visible = 0;
            foreach (LibraryItemView item in app.Shell.Library.Items)
            {
                visible += item.gameObject.activeSelf ? 1 : 0;
            }

            Assert.That(visible, Is.EqualTo(1));
        }
    }
}
