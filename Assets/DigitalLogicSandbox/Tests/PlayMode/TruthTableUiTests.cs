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
using DigitalLogicSandbox.TruthTable.Evaluation;
using DigitalLogicSandbox.UI.Dialogs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>The truth table panel in the real Main scene: entry points, isolation, large tables, export.</summary>
    public sealed class TruthTableUiTests
    {
        private AppBootstrap _app;
        private WorkspaceController _workspace;
        private TruthTableDialog _dialog;
        private string _exportDirectory;

        private static PinRef Pin(NodeId boundary)
        {
            return new PinRef(boundary, BoundarySignatures.Pin);
        }

        private IEnumerator LoadMain()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            _app = AppBootstrap.Instance;
            _workspace = _app.Workspace;
            _workspace.WorkspaceCamera.Animate = false;
            _dialog = _app.Shell.TruthTable;
            _exportDirectory = Path.Combine(Application.temporaryCachePath, "TruthTableExportTest");
            _dialog.ExportDirectory = _exportDirectory;
        }

        [TearDown]
        public void TearDown()
        {
            if (_exportDirectory != null && Directory.Exists(_exportDirectory))
            {
                Directory.Delete(_exportDirectory, true);
            }
        }

        /// <summary>NAND from AND + NOT with two switches and a lamp, built through the editor API.</summary>
        private (NodeId a, NodeId b, NodeId lamp) BuildNand()
        {
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, 1));
            NodeId b = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, -1));
            NodeId and = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.And), new Vector2(-1, 0));
            NodeId not = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(3, 0));
            NodeId lamp = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(7, 0));
            _workspace.Connect(Pin(a), new PinRef(and, AndGate.InputA));
            _workspace.Connect(Pin(b), new PinRef(and, AndGate.InputB));
            _workspace.Connect(new PinRef(and, AndGate.Output), new PinRef(not, NotGate.Input));
            _workspace.Connect(new PinRef(not, NotGate.Output), Pin(lamp));
            return (a, b, lamp);
        }

        [UnityTest]
        public IEnumerator ToolbarTruthTable_ShowsTheNandTable_WithoutTouchingTheLiveCircuit()
        {
            yield return LoadMain();
            (NodeId a, _, _) = BuildNand();
            _workspace.ToggleInput(a); // A = 1, B = 0  -> OUT = 1
            yield return null;
            Signal liveA = _workspace.Runner.Context.GetInput(0);
            Signal liveOut = _workspace.Runner.Context.GetOutput(0);
            long liveEvaluations = _workspace.Runner.EvaluationCount;
            int historyCount = _workspace.History.UndoCount;

            _app.Shell.Toolbar.TruthTableButton.onClick.Invoke();
            yield return null;

            Assert.That(_dialog.IsOpen, Is.True);
            Assert.That(_workspace.InputSuspended, Is.True);
            Assert.That(_dialog.TitleText, Does.Contain(WorkspaceController.DefaultCircuitName));
            Assert.That(_dialog.SummaryText, Does.Contain("2 inputs, 1 output").And.Contain("4 combinations"));
            Assert.That(_dialog.Job.Status, Is.EqualTo(TruthTableStatus.Completed));
            Assert.That(_dialog.HeaderCells[0].text, Is.EqualTo(_workspace.Circuit.Inputs[0].Name));
            Assert.That(_dialog.HeaderCells[1].text, Is.EqualTo(_workspace.Circuit.Inputs[1].Name));
            Assert.That(_dialog.HeaderCells[2].text, Is.EqualTo(_workspace.Circuit.Outputs[0].Name));
            Assert.That(_dialog.GetVisibleRowText(0), Is.EqualTo("001"));
            Assert.That(_dialog.GetVisibleRowText(1), Is.EqualTo("011"));
            Assert.That(_dialog.GetVisibleRowText(2), Is.EqualTo("101"));
            Assert.That(_dialog.GetVisibleRowText(3), Is.EqualTo("110"));
            Assert.That(_dialog.GetVisibleRowText(4), Is.Null, "Only 4 rows exist.");
            Assert.That(_dialog.StatusText, Does.Contain("Complete"));

            // The live simulation and history are exactly as before.
            Assert.That(_workspace.Runner.Context.GetInput(0), Is.EqualTo(liveA));
            Assert.That(_workspace.Runner.Context.GetOutput(0), Is.EqualTo(liveOut));
            Assert.That(_workspace.Runner.EvaluationCount, Is.EqualTo(liveEvaluations));
            Assert.That(_workspace.History.UndoCount, Is.EqualTo(historyCount));

            // Copy and export.
            string copied = null;
            _dialog.Copied += text => copied = text;
            _dialog.CopyButton.onClick.Invoke();
            Assert.That(copied, Does.EndWith("0\t0\t1\r\n0\t1\t1\r\n1\t0\t1\r\n1\t1\t0\r\n"));
            Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo(copied));

            string path = _dialog.ExportCsv();
            Assert.That(path, Is.Not.Null);
            Assert.That(File.Exists(path), Is.True);
            Assert.That(File.ReadAllText(path), Does.EndWith("0,0,1\r\n0,1,1\r\n1,0,1\r\n1,1,0\r\n"));
            Assert.That(File.Exists(path + ".tmp"), Is.False);

            _dialog.Close();
            Assert.That(_dialog.IsOpen, Is.False);
            Assert.That(_workspace.InputSuspended, Is.False);
        }

        [UnityTest]
        public IEnumerator ContextMenu_TruthTableOfAGate_AndOfTheCircuit()
        {
            yield return LoadMain();
            CircuitDefinition nand = HierarchyFixtures.PublishNand(_app.Library);
            NodeId gate = _workspace.PlaceNode(ComponentRef.Custom(nand.Reference), Vector2.zero);
            _workspace.Selection.Clear();
            yield return null;

            Vector2 screen = _workspace.WorkspaceCamera.WorldToScreen(Vector2.zero);
            _workspace.RequestContextMenu(screen, Vector2.zero);
            Assert.That(_app.Shell.ContextMenu.Labels, Does.Contain("Truth Table"));
            Assert.That(_app.Shell.ContextMenu.Invoke("Truth Table"), Is.True);
            yield return null;

            Assert.That(_dialog.IsOpen, Is.True);
            Assert.That(_dialog.TitleText, Does.Contain("NAND"));
            Assert.That(_dialog.Job.Status, Is.EqualTo(TruthTableStatus.Completed));
            Assert.That(_dialog.GetVisibleRowText(3), Is.EqualTo("110"));
            _dialog.Close();

            // Empty space: the main circuit has no outputs yet, so the item is disabled and generation explains why.
            _workspace.RequestContextMenu(_workspace.WorkspaceCamera.WorldToScreen(new Vector2(20, 20)), new Vector2(20, 20));
            Assert.That(_app.Shell.ContextMenu.Labels, Does.Contain("Generate Truth Table..."));
            _app.Shell.ContextMenu.Close();
            _dialog.OpenForCurrentCircuit();
            Assert.That(_dialog.Plan.CanGenerate, Is.False);
            Assert.That(_dialog.StatusText, Does.Contain("no outputs"));
            Assert.That(_dialog.CopyButton.interactable, Is.False);
            Assert.That(_dialog.ExportButton.interactable, Is.False);
            _dialog.Close();

            // Primitive gates are tabulated by the engine too (through a one-gate harness).
            NodeId not = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(0, -6));
            Assert.That(_dialog.OpenForNode(not), Is.True);
            Assert.That(_dialog.GetVisibleRowText(0), Is.EqualTo("01"));
            Assert.That(_dialog.GetVisibleRowText(1), Is.EqualTo("10"));
            Assert.That(_dialog.OpenForNode(gate), Is.True);
            Assert.That(_dialog.Table.RowCount, Is.EqualTo(4));
            _dialog.Close();
        }

        [UnityTest]
        public IEnumerator LargeTable_NeedsConfirmation_RunsAcrossFrames_AndCanBeCancelled()
        {
            yield return LoadMain();
            const int inputs = 16; // 65,536 rows (the default maximum)
            NodeId first = default;
            NodeId second = default;
            for (int i = 0; i < inputs; i++)
            {
                NodeId input = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-10, i * 2));
                if (i == 0)
                {
                    first = input;
                }
                else if (i == 1)
                {
                    second = input;
                }
            }

            NodeId and = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.And), new Vector2(0, 0));
            NodeId lamp = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(6, 0));
            _workspace.Connect(Pin(first), new PinRef(and, AndGate.InputA));
            _workspace.Connect(Pin(second), new PinRef(and, AndGate.InputB));
            _workspace.Connect(new PinRef(and, AndGate.Output), Pin(lamp));
            yield return null;

            _dialog.OpenForCurrentCircuit();
            Assert.That(_dialog.Plan.RequiresConfirmation, Is.True);
            Assert.That(_dialog.Table, Is.Null, "Large tables are never generated automatically.");
            Assert.That(_dialog.GenerateButton.gameObject.activeSelf, Is.True);
            Assert.That(_dialog.StatusText, Does.Contain("65,536"));

            _dialog.Driver.FrameBudgetMilliseconds = 0.5;
            _dialog.GenerateButton.onClick.Invoke();
            Assert.That(_dialog.Driver.IsRunning, Is.True);
            Assert.That(_dialog.CancelButton.gameObject.activeSelf, Is.True);
            Assert.That(_dialog.GenerateButton.gameObject.activeSelf, Is.False);
            Assert.That(_dialog.CopyButton.interactable, Is.False, "No export while generating.");

            yield return null;
            yield return null;
            int afterTwoFrames = _dialog.Job.CompletedRows;
            Assert.That(afterTwoFrames, Is.GreaterThan(0));
            Assert.That(afterTwoFrames, Is.LessThan(_dialog.Job.TotalRows), "Work is spread over frames.");
            Assert.That(_dialog.StatusText, Does.Contain("Generating"));

            _dialog.CancelButton.onClick.Invoke();
            Assert.That(_dialog.Job.Status, Is.EqualTo(TruthTableStatus.Cancelled));
            Assert.That(_dialog.StatusText, Does.Contain("Cancelled"));
            Assert.That(_dialog.CopyButton.interactable, Is.True, "Generated rows can still be copied.");
            int kept = _dialog.Job.CompletedRows;
            yield return null;
            Assert.That(_dialog.Job.CompletedRows, Is.EqualTo(kept), "Nothing runs after cancelling.");

            // Run it fully and check the virtualized list far down the table.
            _dialog.Regenerate();
            Assert.That(_dialog.Table, Is.Null);
            _dialog.Generate();
            _dialog.Driver.RunToCompletion();
            Assert.That(_dialog.Job.Status, Is.EqualTo(TruthTableStatus.Completed));
            int lastRow = _dialog.Table.RowCount - 1;
            _dialog.ScrollToRow(lastRow);
            Assert.That(_dialog.GetVisibleRowText(lastRow), Is.EqualTo(new string('1', inputs) + "1"));
            Assert.That(_dialog.GetVisibleRowText(0), Is.Null, "Rows scrolled out of view are recycled.");
            Assert.That(_dialog.Scroll.content.childCount, Is.LessThan(100), "Only a fixed pool of row objects exists.");
            _dialog.Close();
        }
    }
}
