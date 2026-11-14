using System;
using System.Collections;
using System.IO;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
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
    /// Phase C in the real Main scene: bus components in the library, bus boundaries with bit cells, splitters and
    /// mergers, thick bus wires, width-mismatch rejection, shared buses with tri-state drivers, undo of bus input
    /// values, and persistence of bus projects.
    /// </summary>
    public sealed class BusWorkspaceTests
    {
        private AppBootstrap _app;
        private WorkspaceController _workspace;
        private string _root;

        private static PinRef Pin(NodeId node)
        {
            return new PinRef(node, BoundarySignatures.Pin);
        }

        private static PinRef Bit(NodeId node, int bit)
        {
            return new PinRef(node, BoundarySignatures.BitPin(bit));
        }

        private IEnumerator LoadMain()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            _app = AppBootstrap.Instance;
            _workspace = _app.Workspace;
            _workspace.WorkspaceCamera.Animate = false;
            _root = Path.Combine(Application.temporaryCachePath, "BusWorkspaceTest-" + Guid.NewGuid().ToString("N"));
            _app.Projects.ProjectsDirectory = Path.Combine(_root, "Projects");
            _app.Projects.AutosaveDirectory = Path.Combine(_root, "Autosave");
            _app.Projects.RememberLastProject = false;
            _app.Projects.AutosaveIntervalSeconds = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null && Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        /// <summary>A[4] → splitter → NOT per bit → merger → Y[4].</summary>
        private (NodeId input, NodeId output, NodeId split) BuildInverter()
        {
            NodeId input = _workspace.PlaceNode(ComponentRef.InputBus(4), new Vector2(-10, 0));
            NodeId split = _workspace.PlaceNode(ComponentRef.Splitter(4), new Vector2(-5, 0));
            NodeId merge = _workspace.PlaceNode(ComponentRef.Merger(4), new Vector2(5, 0));
            NodeId output = _workspace.PlaceNode(ComponentRef.OutputBus(4), new Vector2(10, 0));
            Assert.That(_workspace.Connect(Pin(input), Pin(split)), Is.EqualTo(ConnectionError.None));
            for (int b = 0; b < 4; b++)
            {
                NodeId not = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(0, 3 - 2 * b));
                Assert.That(_workspace.Connect(Bit(split, b), new PinRef(not, NotGate.Input)), Is.EqualTo(ConnectionError.None));
                Assert.That(_workspace.Connect(new PinRef(not, NotGate.Output), Bit(merge, b)), Is.EqualTo(ConnectionError.None));
            }

            Assert.That(_workspace.Connect(Pin(merge), Pin(output)), Is.EqualTo(ConnectionError.None));
            _workspace.Sync();
            return (input, output, split);
        }

        [UnityTest]
        public IEnumerator Library_OffersBusComponents_WithAdjustableWidth()
        {
            yield return LoadMain();
            LibraryPanel library = _app.Shell.Library;
            library.SetBusParameters(8, 3);
            bool input = false, join = false, tri = false;
            foreach (LibraryItemView item in library.Items)
            {
                ComponentRef component = item.Request.Component;
                input |= component == ComponentRef.InputBus(8);
                join |= component == ComponentRef.BusJoin(8, 3);
                tri |= component == ComponentRef.TriState(8);
            }

            Assert.That(input && join && tri, Is.True, "Bus entries follow the chosen width.");

            // Built-in gates remain exactly AND and NOT.
            int primitives = 0;
            foreach (LibraryItemView item in library.Items)
            {
                primitives += item.Request.Component.Kind == ComponentKind.Primitive ? 1 : 0;
            }

            Assert.That(primitives, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator BusInverter_ShowsBitCells_AndThickWires_AndInvertsValues()
        {
            yield return LoadMain();
            (NodeId input, NodeId output, NodeId split) = BuildInverter();
            yield return null;

            Assert.That(_workspace.Presenter.TryGetGateView(input, out GateView inView), Is.True);
            Assert.That(inView.Geometry.IsBusBoundary, Is.True);
            Assert.That(inView.BitCount, Is.EqualTo(4));
            Assert.That(_workspace.Presenter.TryGetWireView(Pin(split), out WireView busWire), Is.True);
            Assert.That(busWire.IsBus, Is.True);
            Assert.That(_workspace.Presenter.TryGetWireView(Bit(_workspace.Circuit.Nodes[2].Id, 0), out WireView bitWire), Is.True);
            Assert.That(bitWire.IsBus, Is.False);

            Assert.That(_workspace.ToggleInput(input, 1), Is.True); // A = 0010
            Assert.That(_workspace.ToggleInput(input, 3), Is.True); // A = 1010
            _workspace.Sync();
            yield return null;

            Assert.That(_workspace.Session.GetInputBits(input), Is.EqualTo(10UL));
            Assert.That(_workspace.Session.TryGetOutputBits(output, out ulong value), Is.True);
            Assert.That(value, Is.EqualTo(5UL));
            Assert.That(inView.GetBitSignal(1), Is.EqualTo(Signal.High));
            Assert.That(inView.BusText, Does.Contain("10"));
            Assert.That(_workspace.Presenter.TryGetGateView(output, out GateView outView), Is.True);
            Assert.That(outView.BusText, Does.Contain("5"));
            Assert.That(_app.Shell.StatusBar.ValuesText, Does.Contain("5 (0101)"));

            // Each bit flip is one undo step.
            Assert.That(_workspace.Undo(), Is.True);
            _workspace.Sync();
            Assert.That(_workspace.Session.GetInputBits(input), Is.EqualTo(2UL));
            Assert.That(_workspace.SetInputValue(input, 15), Is.True);
            _workspace.Sync();
            Assert.That(_workspace.Session.TryGetOutputBits(output, out value) && value == 0, Is.True);
        }

        [UnityTest]
        public IEnumerator BitCellClick_TargetsTheBitUnderThePointer()
        {
            yield return LoadMain();
            (NodeId input, _, _) = BuildInverter();
            GateGeometry geometry = _workspace.Geometry.GetGate(input);
            Assert.That(geometry.BitAt(geometry.BitCells[2]), Is.EqualTo(2));
            Assert.That(geometry.BitCells[3].x, Is.LessThan(geometry.BitCells[0].x), "Most significant bit on the left.");
            Assert.That(geometry.BitAt(new Vector2(0f, 0.6f)), Is.EqualTo(-1), "The title row is not a bit.");
        }

        [UnityTest]
        public IEnumerator WidthMismatch_IsRejectedWithAnExplanation()
        {
            yield return LoadMain();
            NodeId bus = _workspace.PlaceNode(ComponentRef.InputBus(4), new Vector2(-5, 0));
            NodeId not = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(0, 0));
            string message = null;
            _workspace.Notification += m => message = m;
            Assert.That(_workspace.Connect(Pin(bus), new PinRef(not, NotGate.Input)), Is.EqualTo(ConnectionError.WidthMismatch));
            Assert.That(message, Does.Contain("4-bit").And.Contain("1-bit").And.Contain("splitter"));
            Assert.That(_workspace.Circuit.Connections.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SharedBus_WithTriStateDrivers_ReportsContention()
        {
            yield return LoadMain();
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBus(2), new Vector2(-10, 4));
            NodeId b = _workspace.PlaceNode(ComponentRef.InputBus(2), new Vector2(-10, -4));
            NodeId ea = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-10, 1));
            NodeId eb = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-10, -1));
            NodeId triA = _workspace.PlaceNode(ComponentRef.TriState(2), new Vector2(-4, 4));
            NodeId triB = _workspace.PlaceNode(ComponentRef.TriState(2), new Vector2(-4, -4));
            NodeId join = _workspace.PlaceNode(ComponentRef.BusJoin(2, 2), new Vector2(2, 0));
            NodeId y = _workspace.PlaceNode(ComponentRef.OutputBus(2), new Vector2(8, 0));
            _workspace.Connect(Pin(a), new PinRef(triA, BoundarySignatures.TriStateData));
            _workspace.Connect(Pin(b), new PinRef(triB, BoundarySignatures.TriStateData));
            _workspace.Connect(Pin(ea), new PinRef(triA, BoundarySignatures.TriStateEnable));
            _workspace.Connect(Pin(eb), new PinRef(triB, BoundarySignatures.TriStateEnable));
            _workspace.Connect(new PinRef(triA, BoundarySignatures.TriStateOutput), Bit(join, 0));
            _workspace.Connect(new PinRef(triB, BoundarySignatures.TriStateOutput), Bit(join, 1));
            Assert.That(_workspace.Connect(Pin(join), Pin(y)), Is.EqualTo(ConnectionError.None));
            _workspace.Sync();
            Assert.That(_workspace.Session.IsSimulating, Is.True, _workspace.Session.Report.ToString());

            _workspace.SetInputValue(a, 1);
            _workspace.SetInputValue(b, 2);
            _workspace.ToggleInput(ea);
            _workspace.Sync();
            yield return null;
            Assert.That(_workspace.Session.TryGetOutputBits(y, out ulong value) && value == 1, Is.True);

            _workspace.ToggleInput(eb);
            _workspace.Sync();
            yield return null;
            Assert.That(_workspace.Runner.Context.ContentionCount, Is.EqualTo(2));
            Assert.That(_workspace.Session.FormatBoundaryValue(y), Does.StartWith("?"));
            Assert.That(_app.Shell.StatusBar.StatusText, Does.Contain("Bus contention"));
        }

        [UnityTest]
        public IEnumerator BusProject_SurvivesSaveAndOpen()
        {
            yield return LoadMain();
            (NodeId input, NodeId output, _) = BuildInverter();
            _workspace.SetInputValue(input, 9);
            _workspace.Sync();
            string path = Path.Combine(_root, "bus" + DigitalLogicSandbox.Persistence.ProjectFormat.FileExtension);
            Assert.That(_app.Projects.SaveAs(path, out string message), Is.True, message);
            _app.Projects.NewProject();
            yield return null;
            Assert.That(_app.Projects.Open(path, out message), Is.True, message);
            yield return null;

            CircuitDefinition circuit = _workspace.Circuit;
            Assert.That(circuit.Inputs[0].Width, Is.EqualTo(4));
            Assert.That(_workspace.Session.GetInputBits(input), Is.EqualTo(9UL));
            Assert.That(_workspace.Session.TryGetOutputBits(output, out ulong value), Is.True);
            Assert.That(value, Is.EqualTo(6UL));
        }
    }
}
