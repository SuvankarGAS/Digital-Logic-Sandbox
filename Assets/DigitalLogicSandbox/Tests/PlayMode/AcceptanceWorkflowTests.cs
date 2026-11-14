using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.GateLibrary.Packaging;
using DigitalLogicSandbox.TruthTable.Generation;
using DigitalLogicSandbox.UI.Dialogs;
using DigitalLogicSandbox.UI.Panels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>
    /// The final acceptance workflow from the specification (section 20), end to end in the real Main scene: build
    /// NAND from AND and NOT, see animated propagation, generate its truth table, package it through the dialog,
    /// reuse and inspect it, build and package OR from NAND, verify OR's truth table, then save, start over and
    /// reopen. No step relies on a gate's name for behaviour: every result comes from the simulated circuits.
    /// </summary>
    public sealed class AcceptanceWorkflowTests
    {
        private AppBootstrap _app;
        private WorkspaceController _workspace;
        private string _root;

        private static PinRef Pin(NodeId boundary)
        {
            return new PinRef(boundary, BoundarySignatures.Pin);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null && Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [UnityTest]
        public IEnumerator CompleteWorkflow_NandToOr_SaveAndReopen()
        {
            // 1-2. Launch, new project.
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            _app = AppBootstrap.Instance;
            _workspace = _app.Workspace;
            _workspace.WorkspaceCamera.Animate = false;
            ProjectController projects = _app.Projects;
            _root = Path.Combine(Application.temporaryCachePath, "Acceptance-" + Guid.NewGuid().ToString("N"));
            projects.ProjectsDirectory = Path.Combine(_root, "Projects");
            projects.AutosaveDirectory = Path.Combine(_root, "Autosave");
            projects.RememberLastProject = false;
            projects.AutosaveIntervalSeconds = 0f;
            projects.NewProject();
            yield return null;

            // 3. Only AND and NOT are built in.
            var builtIn = new List<string>();
            foreach (LibraryItemView item in _app.Shell.Library.Items)
            {
                if (item.Request.Component.Kind == ComponentKind.Primitive)
                {
                    builtIn.Add(item.Title);
                }
            }

            Assert.That(builtIn, Is.EquivalentTo(new[] { "AND", "NOT" }));

            // 4-6. Two external inputs into AND, then NOT, then a lamp.
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-8, 1));
            NodeId b = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-8, -1));
            NodeId and = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.And), new Vector2(-2, 0));
            NodeId not = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(3, 0));
            NodeId lamp = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(8, 0));
            Assert.That(_workspace.Connect(Pin(a), new PinRef(and, AndGate.InputA)), Is.EqualTo(ConnectionError.None));
            Assert.That(_workspace.Connect(Pin(b), new PinRef(and, AndGate.InputB)), Is.EqualTo(ConnectionError.None));
            Assert.That(_workspace.Connect(new PinRef(and, AndGate.Output), new PinRef(not, NotGate.Input)),
                Is.EqualTo(ConnectionError.None));
            Assert.That(_workspace.Connect(new PinRef(not, NotGate.Output), Pin(lamp)), Is.EqualTo(ConnectionError.None));
            yield return null;

            // 7-9. Change inputs, observe outputs and animated propagation.
            Assert.That(_workspace.Session.GetOutputValue(lamp), Is.EqualTo(Signal.High), "NAND(0,0) = 1");
            Assert.That(_workspace.AnimationSettings.Enabled, Is.True);
            _workspace.ToggleInput(a);
            _workspace.ToggleInput(b);
            yield return null;
            Assert.That(_workspace.Session.GetOutputValue(lamp), Is.EqualTo(Signal.Low), "NAND(1,1) = 0");
            Assert.That(_workspace.SignalAnimation.PulseCount + _workspace.SignalAnimation.AnimatedWireCount,
                Is.GreaterThan(0), "Changed signals are animated.");

            // 10-11. Truth table generated from the circuit.
            TruthTableDialog table = _app.Shell.TruthTable;
            table.OpenForCurrentCircuit();
            table.Driver.RunToCompletion();
            AssertTable(table.Table, new[] { 1, 1, 1, 0 });
            table.Close();
            Assert.That(_workspace.Session.GetOutputValue(lamp), Is.EqualTo(Signal.Low),
                "Generating the table leaves the live simulation unchanged.");

            // 12-16. Package through the dialog.
            int nodesBefore = _workspace.Circuit.Nodes.Count;
            PackagingDialog dialog = _app.Shell.PackagingDialog;
            dialog.Open(selectionOnly: false);
            dialog.NameField.text = "NAND";
            yield return null;
            Assert.That(dialog.PackageButton.interactable, Is.True, dialog.MessagesText);
            dialog.PackageButton.onClick.Invoke();
            yield return null;
            Assert.That(_workspace.Circuit.Nodes.Count, Is.EqualTo(nodesBefore), "The original circuit is kept.");

            // 17. NAND is listed.
            CircuitDefinition nand = FindLatest("NAND");
            Assert.That(nand, Is.Not.Null);
            Assert.That(nand.Inputs.Count, Is.EqualTo(2));
            Assert.That(nand.Outputs.Count, Is.EqualTo(1));
            Assert.That(LibraryLists("NAND"), Is.True);

            // 18. Use NAND in another circuit: clear the canvas and build OR(A, B) = NAND(NAND(A,A), NAND(B,B)).
            _workspace.SelectAll();
            _workspace.DeleteSelection();
            yield return null;
            NodeId x = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-10, 2));
            NodeId y = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-10, -2));
            NodeId notX = _workspace.PlaceNode(ComponentRef.Custom(nand.Reference), new Vector2(-4, 2));
            NodeId notY = _workspace.PlaceNode(ComponentRef.Custom(nand.Reference), new Vector2(-4, -2));
            NodeId final = _workspace.PlaceNode(ComponentRef.Custom(nand.Reference), new Vector2(3, 0));
            NodeId orLamp = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(9, 0));
            PortId in0 = nand.Inputs[0].Id;
            PortId in1 = nand.Inputs[1].Id;
            PortId nandOut = nand.Outputs[0].Id;
            _workspace.Connect(Pin(x), new PinRef(notX, in0));
            _workspace.Connect(Pin(x), new PinRef(notX, in1));
            _workspace.Connect(Pin(y), new PinRef(notY, in0));
            _workspace.Connect(Pin(y), new PinRef(notY, in1));
            _workspace.Connect(new PinRef(notX, nandOut), new PinRef(final, in0));
            _workspace.Connect(new PinRef(notY, nandOut), new PinRef(final, in1));
            _workspace.Connect(new PinRef(final, nandOut), Pin(orLamp));
            yield return null;
            Assert.That(_workspace.Session.IsSimulating, Is.True, _workspace.Session.Report.ToString());

            // 19-20. Open NAND, inspect it, return with the breadcrumb.
            Assert.That(_workspace.OpenNode(final), Is.True);
            yield return null;
            Assert.That(_workspace.Navigator.CurrentIndex, Is.EqualTo(1));
            Assert.That(_workspace.Circuit.Nodes.Count, Is.EqualTo(nodesBefore), "Internal AND, NOT and boundaries.");
            Assert.That(_workspace.IsEditable, Is.False, "Inspection is read-only.");
            Assert.That(_app.Shell.NavigationBar.Crumbs.Count, Is.EqualTo(2));
            _app.Shell.NavigationBar.Crumbs[0].onClick.Invoke();
            yield return null;
            Assert.That(_workspace.Navigator.CurrentIndex, Is.EqualTo(0));

            // 21-22. Package OR from the NAND-based circuit.
            PackagingSpec spec = _workspace.CreatePackagingSpec(selectionOnly: false);
            spec.Name = "OR";
            PackagingResult packaged = _workspace.PackageCustomGate(spec);
            Assert.That(packaged.Succeeded, Is.True, packaged.Report.ToString());
            CircuitDefinition or = FindLatest("OR");
            Assert.That(LibraryLists("OR"), Is.True);

            // 23. OR truth table, evaluated through the nested NAND definitions.
            Assert.That(table.OpenForComponent(ComponentRef.Custom(or.Reference)), Is.True);
            table.Driver.RunToCompletion();
            AssertTable(table.Table, new[] { 0, 1, 1, 1 });
            table.Close();

            // Place OR so the saved main circuit holds a two-level hierarchy.
            _workspace.SelectAll();
            _workspace.DeleteSelection();
            NodeId orGate = _workspace.PlaceNode(ComponentRef.Custom(or.Reference), new Vector2(0, 0));
            _workspace.GridVisible = false;
            yield return null;

            // 24-25. Save, start over (as after closing the application), reopen.
            string path = projects.ResolvePath("Acceptance");
            Assert.That(projects.SaveAs(path, out string saveMessage), Is.True, saveMessage);
            projects.NewProject();
            yield return null;
            Assert.That(_app.Library.AllVersions.Count, Is.Zero);
            Assert.That(projects.Open(path, out string openMessage), Is.True, openMessage);
            yield return null;

            // 26. Circuits, custom gates, hierarchy and settings are restored.
            Assert.That(FindLatest("NAND"), Is.Not.Null);
            Assert.That(FindLatest("OR"), Is.Not.Null);
            Assert.That(LibraryLists("NAND") && LibraryLists("OR"), Is.True);
            Assert.That(_workspace.GridVisible, Is.False);
            Assert.That(_workspace.RootCircuit.ContainsNode(orGate), Is.True);
            Assert.That(_workspace.OpenNode(orGate), Is.True, "Open OR");
            yield return null;
            NodeId innerNand = NodeId.None;
            foreach (NodeDefinition node in _workspace.Circuit.Nodes)
            {
                if (node.Component.Kind == ComponentKind.Custom)
                {
                    innerNand = node.Id;
                    break;
                }
            }

            Assert.That(_workspace.OpenNode(innerNand), Is.True, "Open NAND inside OR");
            yield return null;
            Assert.That(_workspace.Navigator.CurrentIndex, Is.EqualTo(2));
            Assert.That(_app.Shell.NavigationBar.Crumbs.Count, Is.EqualTo(3));
            _app.Shell.NavigationBar.Crumbs[0].onClick.Invoke();
            yield return null;
            Assert.That(_workspace.Navigator.CurrentIndex, Is.EqualTo(0));

            Assert.That(table.OpenForComponent(ComponentRef.Custom(FindLatest("OR").Reference)), Is.True);
            table.Driver.RunToCompletion();
            AssertTable(table.Table, new[] { 0, 1, 1, 1 });
            table.Close();
        }

        private CircuitDefinition FindLatest(string name)
        {
            var latest = new List<CircuitDefinition>();
            _app.Library.GetLatestDefinitions(latest);
            foreach (CircuitDefinition definition in latest)
            {
                if (definition.Name == name)
                {
                    return definition;
                }
            }

            return null;
        }

        private bool LibraryLists(string name)
        {
            foreach (LibraryItemView item in _app.Shell.Library.Items)
            {
                if (item.Request.Component.Kind == ComponentKind.Custom && item.Title == name)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Checks a 2-input, 1-output table in binary row order (00, 01, 10, 11).</summary>
        private static void AssertTable(TruthTableData data, int[] expected)
        {
            Assert.That(data, Is.Not.Null, "The table was generated.");
            Assert.That(data.InputCount, Is.EqualTo(2));
            Assert.That(data.OutputCount, Is.EqualTo(1));
            Assert.That(data.RowCount, Is.EqualTo(4));
            for (int row = 0; row < 4; row++)
            {
                Assert.That(data.GetInput(row, 0), Is.EqualTo(row >= 2 ? Signal.High : Signal.Low));
                Assert.That(data.GetInput(row, 1), Is.EqualTo(row % 2 == 1 ? Signal.High : Signal.Low));
                Assert.That(data.GetOutput(row, 0), Is.EqualTo(expected[row] == 1 ? Signal.High : Signal.Low),
                    "Row " + row);
            }
        }
    }
}
