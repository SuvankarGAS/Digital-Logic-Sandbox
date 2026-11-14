using System.Collections;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.GateLibrary.Packaging;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.UI.Panels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>
    /// End-to-end packaging: build NAND visually, package it, place instances, compose OR from NAND and verify
    /// behaviour through the live simulation and views. Also undo of packaging.
    /// </summary>
    public sealed class PackagingIntegrationTests
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
                Library = _library,
                Theme = VisualTheme.CreateRuntimeDefault(),
                Camera = camera,
                IsPointerOverUi = () => false,
                IsTextInputFocused = () => false,
            });
            _workspace.WorkspaceCamera.Animate = false;
            _workspace.LoadCircuit(new CircuitDefinition(DefinitionId.New(), WorkspaceController.DefaultCircuitName));
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_workspace.gameObject);
            Object.Destroy(_cameraObject);
        }

        private static PinRef Pin(NodeId boundary)
        {
            return new PinRef(boundary, BoundarySignatures.Pin);
        }

        private void BuildNandInWorkspace()
        {
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, 1));
            NodeId b = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, -1));
            NodeId and = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.And), new Vector2(-1, 0));
            NodeId not = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(3, 0));
            NodeId output = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(7, 0));
            _workspace.Connect(Pin(a), new PinRef(and, AndGate.InputA));
            _workspace.Connect(Pin(b), new PinRef(and, AndGate.InputB));
            _workspace.Connect(new PinRef(and, AndGate.Output), new PinRef(not, NotGate.Input));
            _workspace.Connect(new PinRef(not, NotGate.Output), Pin(output));
        }

        private CircuitDefinition Package(string name, bool clear)
        {
            PackagingSpec spec = _workspace.CreatePackagingSpec(selectionOnly: false);
            spec.Name = name;
            PackagingResult result = _workspace.PackageCustomGate(spec, clear);
            Assert.That(result.Succeeded, Is.True, result.Report.ToString());
            return result.Definition;
        }

        [UnityTest]
        public IEnumerator PackageNand_PlaceInstance_SimulatesAsNand()
        {
            BuildNandInWorkspace();
            int nodesBefore = _workspace.Circuit.Nodes.Count;
            CircuitDefinition nand = Package("NAND", clear: false);
            yield return null;

            Assert.That(_library.Contains(nand.Reference), Is.True);
            Assert.That(_workspace.Circuit.Nodes.Count, Is.EqualTo(nodesBefore), "Original circuit kept intact.");

            // A fresh circuit using the packaged gate.
            _workspace.LoadCircuit(new CircuitDefinition(DefinitionId.New(), "Host"));
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, 1));
            NodeId b = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, -1));
            NodeId gate = _workspace.PlaceNode(ComponentRef.Custom(nand.Reference), new Vector2(0, 0));
            NodeId output = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(6, 0));
            _workspace.Connect(Pin(a), new PinRef(gate, nand.Inputs[0].Id));
            _workspace.Connect(Pin(b), new PinRef(gate, nand.Inputs[1].Id));
            _workspace.Connect(new PinRef(gate, nand.Outputs[0].Id), Pin(output));
            yield return null;

            Assert.That(_workspace.Presenter.TryGetGateView(gate, out GateView view), Is.True);
            Assert.That(view.Geometry.Kind, Is.EqualTo(ComponentKind.Custom));
            Assert.That(view.Geometry.Title, Is.EqualTo("NAND"));
            Assert.That(view.Geometry.InputPins.Length, Is.EqualTo(2), "Only external pins are shown.");
            Assert.That(view.Geometry.InputNames, Is.EqualTo(new[] { "A", "B" }));

            int[,] table = { { 0, 0, 1 }, { 0, 1, 1 }, { 1, 0, 1 }, { 1, 1, 0 } };
            for (int row = 0; row < 4; row++)
            {
                _workspace.SetInput(a, table[row, 0] == 1 ? Signal.High : Signal.Low);
                _workspace.SetInput(b, table[row, 1] == 1 ? Signal.High : Signal.Low);
                yield return null;
                Signal expected = table[row, 2] == 1 ? Signal.High : Signal.Low;
                Assert.That(_workspace.Session.GetOutputValue(output), Is.EqualTo(expected), "Row " + row);
                Assert.That(_workspace.Presenter.TryGetGateView(output, out GateView lamp) && lamp.StateSignal == expected,
                    Is.True, "Indicator row " + row);
            }
        }

        [UnityTest]
        public IEnumerator ComposeOr_FromPackagedNand_AndPackageIt()
        {
            BuildNandInWorkspace();
            CircuitDefinition nand = Package("NAND", clear: true);
            yield return null;
            Assert.That(_workspace.Circuit.Nodes.Count, Is.EqualTo(0), "Clear option removed the packaged parts.");

            // OR(A, B) = NAND(NAND(A, A), NAND(B, B))
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-8, 2));
            NodeId b = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-8, -2));
            NodeId n1 = _workspace.PlaceNode(ComponentRef.Custom(nand.Reference), new Vector2(-2, 2));
            NodeId n2 = _workspace.PlaceNode(ComponentRef.Custom(nand.Reference), new Vector2(-2, -2));
            NodeId n3 = _workspace.PlaceNode(ComponentRef.Custom(nand.Reference), new Vector2(4, 0));
            NodeId output = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(9, 0));
            PortId inA = nand.Inputs[0].Id;
            PortId inB = nand.Inputs[1].Id;
            PortId q = nand.Outputs[0].Id;
            _workspace.Connect(Pin(a), new PinRef(n1, inA));
            _workspace.Connect(Pin(a), new PinRef(n1, inB));
            _workspace.Connect(Pin(b), new PinRef(n2, inA));
            _workspace.Connect(Pin(b), new PinRef(n2, inB));
            _workspace.Connect(new PinRef(n1, q), new PinRef(n3, inA));
            _workspace.Connect(new PinRef(n2, q), new PinRef(n3, inB));
            _workspace.Connect(new PinRef(n3, q), Pin(output));
            yield return null;

            _workspace.SetInput(a, Signal.High);
            yield return null;
            Assert.That(_workspace.Session.GetOutputValue(output), Is.EqualTo(Signal.High));
            _workspace.SetInput(a, Signal.Low);
            yield return null;
            Assert.That(_workspace.Session.GetOutputValue(output), Is.EqualTo(Signal.Low));

            CircuitDefinition or = Package("OR", clear: false);
            Assert.That(_library.DefinitionCount, Is.EqualTo(2));
            Assert.That(_library.FindReferencingDefinition(nand.Reference), Is.SameAs(or));

            // Undo the OR publication, then the whole NAND packaging (+ clear) step by step.
            _workspace.Undo();
            Assert.That(_library.Contains(or.Reference), Is.False);
        }

        [UnityTest]
        public IEnumerator UndoPackagingWithClear_RestoresCircuitAndRemovesGate()
        {
            BuildNandInWorkspace();
            string countBefore = _workspace.Circuit.Nodes.Count + "/" + _workspace.Circuit.Connections.Count;
            CircuitDefinition nand = Package("NAND", clear: true);
            yield return null;

            _workspace.Undo();
            yield return null;
            Assert.That(_library.Contains(nand.Reference), Is.False);
            Assert.That(_workspace.Circuit.Nodes.Count + "/" + _workspace.Circuit.Connections.Count, Is.EqualTo(countBefore));
            Assert.That(_workspace.Presenter.GateViewCount, Is.EqualTo(5));
            Assert.That(_workspace.Session.IsSimulating, Is.True);

            _workspace.Redo();
            Assert.That(_library.Contains(nand.Reference), Is.True);
            Assert.That(_workspace.Circuit.Nodes.Count, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator InvalidPackaging_PublishesNothing()
        {
            _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.And), Vector2.zero);
            PackagingSpec spec = _workspace.CreatePackagingSpec(selectionOnly: false);
            spec.Name = "Nothing";
            PackagingResult result = _workspace.PackageCustomGate(spec);
            yield return null;

            Assert.That(result.Succeeded, Is.False);
            Assert.That(_library.DefinitionCount, Is.EqualTo(0));
            Assert.That(_workspace.History.UndoDescription, Does.Not.Contain("Package"));
        }
    }

    /// <summary>Packaging through the real UI in the Main scene.</summary>
    public sealed class PackagingDialogTests
    {
        [UnityTest]
        public IEnumerator Dialog_PackagesCircuit_AndLibraryShowsGate()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            AppBootstrap app = AppBootstrap.Instance;
            WorkspaceController workspace = app.Workspace;

            NodeId a = workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-4, 0));
            NodeId not = workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), Vector2.zero);
            NodeId output = workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(4, 0));
            workspace.Connect(new PinRef(a, BoundarySignatures.Pin), new PinRef(not, NotGate.Input));
            workspace.Connect(new PinRef(not, NotGate.Output), new PinRef(output, BoundarySignatures.Pin));
            workspace.Selection.Clear();
            yield return null;

            app.Shell.PackagingDialog.Open(selectionOnly: false);
            Assert.That(app.Shell.PackagingDialog.IsOpen, Is.True);
            Assert.That(workspace.InputSuspended, Is.True, "Workspace input is blocked while the dialog is open.");
            Assert.That(app.Shell.PackagingDialog.PackageButton.interactable, Is.False, "A name is required.");

            app.Shell.PackagingDialog.NameField.text = "BUFFER-NOT";
            yield return null;
            Assert.That(app.Shell.PackagingDialog.PackageButton.interactable, Is.True,
                app.Shell.PackagingDialog.MessagesText);

            app.Shell.PackagingDialog.PackageButton.onClick.Invoke();
            yield return null;

            Assert.That(app.Shell.PackagingDialog.IsOpen, Is.False);
            Assert.That(workspace.InputSuspended, Is.False);
            Assert.That(app.Library.DefinitionCount, Is.EqualTo(1));

            bool listed = false;
            foreach (LibraryItemView item in app.Shell.Library.Items)
            {
                listed |= item.Request.Component.Kind == ComponentKind.Custom && item.Title == "BUFFER-NOT";
            }

            Assert.That(listed, Is.True, "The library panel updates automatically.");

            workspace.Selection.Select(not);
            yield return null;
            Assert.That(app.Shell.Inspector.HeaderText, Is.EqualTo("NOT"));
            workspace.Selection.Clear();
            yield return null;
            Assert.That(app.Shell.Inspector.HeaderText, Is.EqualTo("Circuit"));
        }
    }
}
