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
using DigitalLogicSandbox.Persistence;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.UI.Dialogs;
using DigitalLogicSandbox.UI.Panels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>
    /// Phase B in the real Main scene: the clock node, real-time ticking, clock and power-on settings, the
    /// truth table's hand-off to sequence analysis, and persistence of clocked circuits.
    /// </summary>
    public sealed class SequentialWorkspaceTests
    {
        private AppBootstrap _app;
        private WorkspaceController _workspace;
        private string _root;

        private static PinRef Pin(NodeId node)
        {
            return new PinRef(node, BoundarySignatures.Pin);
        }

        private IEnumerator LoadMain()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            _app = AppBootstrap.Instance;
            _workspace = _app.Workspace;
            _workspace.WorkspaceCamera.Animate = false;
            _root = Path.Combine(Application.temporaryCachePath, "SequentialWorkspaceTest-" + Guid.NewGuid().ToString("N"));
            _app.Projects.ProjectsDirectory = Path.Combine(_root, "Projects");
            _app.Projects.AutosaveDirectory = Path.Combine(_root, "Autosave");
            _app.Projects.RememberLastProject = false;
            _app.Projects.AutosaveIntervalSeconds = 0f;
            _app.Shell.SequenceAnalysis.ExportDirectory = Path.Combine(_root, "Exports");
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null && Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        /// <summary>Clock → NOT → lamp.</summary>
        private (NodeId clock, NodeId lamp) BuildClocked()
        {
            NodeId clock = _workspace.PlaceNode(ComponentRef.Clock, new Vector2(-5, 0));
            NodeId not = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(0, 0));
            NodeId lamp = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(5, 0));
            _workspace.Connect(Pin(clock), new PinRef(not, NotGate.Input));
            _workspace.Connect(new PinRef(not, NotGate.Output), Pin(lamp));
            _workspace.Sync();
            return (clock, lamp);
        }

        [UnityTest]
        public IEnumerator Library_OffersTheClock_AndItRendersAsAUtilityBox()
        {
            yield return LoadMain();
            bool found = false;
            foreach (LibraryItemView item in _app.Shell.Library.Items)
            {
                found |= item.Request.Component == ComponentRef.Clock;
            }

            Assert.That(found, Is.True, "Circuit I/O lists the clock.");

            (NodeId clock, NodeId lamp) = BuildClocked();
            yield return null;
            Assert.That(_workspace.Runner.HasClock, Is.True);
            Assert.That(_workspace.Presenter.TryGetGateView(clock, out GateView view), Is.True);
            Assert.That(view.Geometry.Kind, Is.EqualTo(ComponentKind.Clock));
            Assert.That(view.Geometry.Title, Is.EqualTo("CLK"));
            Assert.That(view.Geometry.InputPins.Length, Is.Zero);
            Assert.That(view.Geometry.OutputPins.Length, Is.EqualTo(1));
            Signal clockLevel = _workspace.Runner.Context.Clock;
            Assert.That(clockLevel.IsBinary(), Is.True);
            Assert.That(_workspace.Session.GetOutputValue(lamp),
                Is.EqualTo(clockLevel == Signal.High ? Signal.Low : Signal.High), "Lamp = NOT clock.");
        }

        [UnityTest]
        public IEnumerator Clock_TicksInRealTimeWhilePlaying_AndCanBeSteppedManually()
        {
            yield return LoadMain();
            (NodeId clock, NodeId lamp) = BuildClocked();
            _workspace.ClockFrequency = 20f;
            _workspace.Play();
            yield return new WaitForSecondsRealtime(0.4f);

            SimulationRunner runner = _workspace.Runner;
            Assert.That(runner.ClockEdges, Is.GreaterThan(2), "About 16 edges expected at 20 Hz in 0.4 s.");
            Assert.That(_workspace.Session.GetOutputValue(lamp), Is.EqualTo(runner.Context.Clock == Signal.High ? Signal.Low : Signal.High));
            Assert.That(_app.Shell.StatusBar.ClockText, Does.Contain("CLK").And.Contain("20 Hz"));

            // Disabled clock: no real-time edges, manual edges still work.
            _workspace.ClockEnabled = false;
            long edges = runner.ClockEdges;
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(runner.ClockEdges, Is.EqualTo(edges));
            Assert.That(_workspace.TickClock(), Is.True);
            yield return null;
            Assert.That(runner.ClockEdges, Is.EqualTo(edges + 1));
            Assert.That(_workspace.Presenter.TryGetGateView(clock, out GateView view), Is.True);
            Assert.That(view.StateSignal, Is.EqualTo(runner.Context.Clock), "The clock box shows the clock level.");
            Assert.That(_app.Shell.StatusBar.ClockText, Does.Contain("manual"));

            // Paused: Step walks a clock edge wave by wave.
            _workspace.Pause();
            Signal level = runner.Context.Clock;
            runner.Step();
            Assert.That(runner.Context.Clock, Is.Not.EqualTo(level));
        }

        [UnityTest]
        public IEnumerator TruthTable_RefusesClockedCircuits_AndHandsOverToSequenceAnalysis()
        {
            yield return LoadMain();
            BuildClocked();
            yield return null;
            long liveEdges = _workspace.Runner.ClockEdges;

            _app.Shell.Toolbar.TruthTableButton.onClick.Invoke();
            yield return null;
            TruthTableDialog truthTable = _app.Shell.TruthTable;
            Assert.That(truthTable.IsOpen, Is.True);
            Assert.That(truthTable.StatusText, Does.Contain("Sequence Analysis"));
            Assert.That(truthTable.SequenceButton.gameObject.activeSelf, Is.True);

            truthTable.SequenceButton.onClick.Invoke();
            yield return null;
            SequenceAnalysisDialog sequence = _app.Shell.SequenceAnalysis;
            Assert.That(truthTable.IsOpen, Is.False);
            Assert.That(sequence.IsOpen, Is.True);
            Assert.That(_workspace.InputSuspended, Is.True);
            Assert.That(sequence.PulseClockToggle.isOn, Is.True);
            Assert.That(sequence.Table, Is.Not.Null, sequence.StatusText);
            Assert.That(sequence.Table.Clocked, Is.True);

            sequence.StepsField.text = "3";
            Assert.That(sequence.Run(), Is.Not.Null);
            Assert.That(sequence.Table.RowCount, Is.EqualTo(4));
            Assert.That(sequence.TableText, Does.Contain("Cycle").And.Contain("Power-on"));
            Assert.That(sequence.StatusText, Does.Contain("one clock pulse per step"));
            Assert.That(_workspace.Runner.ClockEdges, Is.EqualTo(liveEdges), "The live clock is untouched.");

            string copied = null;
            sequence.Copied += text => copied = text;
            sequence.CopyButton.onClick.Invoke();
            Assert.That(copied, Does.StartWith("Cycle\t"));
            string path = sequence.ExportCsv();
            Assert.That(File.Exists(path), Is.True);

            sequence.Close();
            Assert.That(_workspace.InputSuspended, Is.False);
        }

        [UnityTest]
        public IEnumerator SequenceAnalysis_RejectsInvalidPatterns()
        {
            yield return LoadMain();
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-4, 0));
            NodeId lamp = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(4, 0));
            _workspace.Connect(Pin(a), Pin(lamp));
            yield return null;

            SequenceAnalysisDialog sequence = _app.Shell.SequenceAnalysis;
            sequence.OpenForCurrentCircuit();
            Assert.That(sequence.PatternFields.Count, Is.EqualTo(1));
            Assert.That(sequence.PulseClockToggle.interactable, Is.False, "No clock in this circuit.");
            Assert.That(sequence.Table, Is.Not.Null);

            sequence.PatternFields[0].text = "01x";
            Assert.That(sequence.Run(), Is.Null);
            Assert.That(sequence.StatusText, Does.Contain("Invalid character 'x'"));
            Assert.That(sequence.CopyButton.interactable, Is.False);

            sequence.PatternFields[0].text = "0110";
            SequenceTableCheck(sequence);
            sequence.Close();
        }

        private static void SequenceTableCheck(SequenceAnalysisDialog sequence)
        {
            Assert.That(sequence.Run(), Is.Not.Null);
            Assert.That(sequence.Table.GetOutput(1, 0), Is.EqualTo(Signal.Low));
            Assert.That(sequence.Table.GetOutput(2, 0), Is.EqualTo(Signal.High));
            Assert.That(sequence.Table.GetOutput(3, 0), Is.EqualTo(Signal.High));
            Assert.That(sequence.Table.GetOutput(4, 0), Is.EqualTo(Signal.Low));
        }

        [UnityTest]
        public IEnumerator ClockedProjects_AndSimulationSettings_SurviveSaveAndOpen()
        {
            yield return LoadMain();
            (NodeId clock, _) = BuildClocked();
            _workspace.ClockFrequency = 6.5f;
            _workspace.ClockEnabled = false;
            _workspace.PowerOnState = PowerOnState.Unknown;
            Assert.That(_app.Projects.IsDirty, Is.True, "Settings changes mark the project dirty.");

            string path = Path.Combine(_root, "Projects", "Clocked" + ProjectFormat.FileExtension);
            Assert.That(_app.Projects.SaveAs(path, out string message), Is.True, message);
            _app.Projects.NewProject();
            yield return null;
            Assert.That(_workspace.ClockFrequency, Is.EqualTo(SimulationRunner.DefaultClockFrequency));
            Assert.That(_workspace.PowerOnState, Is.EqualTo(PowerOnState.Resolved));

            Assert.That(_app.Projects.Open(path, out message), Is.True, message);
            yield return null;
            Assert.That(_workspace.Circuit.GetNode(clock).Component, Is.EqualTo(ComponentRef.Clock));
            Assert.That(_workspace.Runner.HasClock, Is.True);
            Assert.That(_workspace.ClockFrequency, Is.EqualTo(6.5f));
            Assert.That(_workspace.ClockEnabled, Is.False);
            Assert.That(_workspace.PowerOnState, Is.EqualTo(PowerOnState.Unknown));
            Assert.That(_workspace.Runner.PowerOnState, Is.EqualTo(PowerOnState.Unknown));
        }
    }
}
