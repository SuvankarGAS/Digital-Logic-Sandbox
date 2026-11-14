using System.Collections;
using System.Diagnostics;
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
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>
    /// Editor-level performance with thousands of gates: building the views once, then checking that an input
    /// toggle only updates the nets that changed, and that moving a gate reroutes only its own wires. Timings are
    /// written to the test output; thresholds are generous regression guards.
    /// </summary>
    public sealed class WorkspacePerformanceTests
    {
        private const int Chains = 20;
        private const int ChainLength = 100;

        private GameObject _cameraObject;
        private WorkspaceController _workspace;

        [SetUp]
        public void SetUp()
        {
            _cameraObject = new GameObject("Test Camera");
            var camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            var library = new DefinitionLibrary();
            var catalog = new ComponentCatalog(PrimitiveRegistry.CreateDefault(), library);
            _workspace = new GameObject("Workspace").AddComponent<WorkspaceController>();
            _workspace.Initialize(new WorkspaceSetup
            {
                Catalog = catalog,
                Library = library,
                Theme = VisualTheme.CreateRuntimeDefault(),
                Camera = camera,
                IsPointerOverUi = () => false,
                IsTextInputFocused = () => false,
            });
            _workspace.WorkspaceCamera.Animate = false;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_workspace.gameObject);
            Object.Destroy(_cameraObject);
        }

        [UnityTest]
        public IEnumerator ThousandsOfGates_ToggleAndMove_UpdateOnlyAffectedViews()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Large");
            var inputs = new NodeId[Chains];
            var outputs = new NodeId[Chains];
            NodeId middleGate = NodeId.None;
            for (int c = 0; c < Chains; c++)
            {
                float y = c * 4f;
                NodeDefinition input = circuit.AddInputBoundary("I" + c, new CircuitPoint(-4f, y));
                inputs[c] = input.Id;
                PinRef previous = new PinRef(input.Id, BoundarySignatures.Pin);
                for (int i = 0; i < ChainLength; i++)
                {
                    NodeDefinition not = circuit.AddNode(ComponentRef.Primitive(BuiltInComponentIds.Not),
                        new CircuitPoint(i * 5f, y));
                    circuit.Connect(previous, new PinRef(not.Id, NotGate.Input));
                    previous = new PinRef(not.Id, NotGate.Output);
                    if (c == Chains / 2 && i == ChainLength / 2)
                    {
                        middleGate = not.Id;
                    }
                }

                NodeDefinition output = circuit.AddOutputBoundary("O" + c, new CircuitPoint(ChainLength * 5f, y));
                outputs[c] = output.Id;
                circuit.Connect(previous, new PinRef(output.Id, BoundarySignatures.Pin));
            }

            int gateCount = Chains * (ChainLength + 2);
            int wireCount = Chains * (ChainLength + 1);

            var stopwatch = Stopwatch.StartNew();
            _workspace.LoadCircuit(circuit);
            yield return null; // compile + build views
            long loadMs = stopwatch.ElapsedMilliseconds;

            Assert.That(_workspace.Session.IsSimulating, Is.True, _workspace.Session.Report.ToString());
            Assert.That(_workspace.Presenter.GateViewCount, Is.EqualTo(gateCount));
            Assert.That(_workspace.Presenter.WireViewCount, Is.EqualTo(wireCount));

            // Toggle one input: only that chain's nets change (an even-length NOT chain follows its input).
            _workspace.Presenter.ResetDiagnostics();
            long evaluationsBefore = _workspace.Session.Runner.EvaluationCount;
            stopwatch.Restart();
            _workspace.ToggleInput(inputs[0]);
            yield return null;
            long toggleMs = stopwatch.ElapsedMilliseconds;
            long evaluations = _workspace.Session.Runner.EvaluationCount - evaluationsBefore;

            Assert.That(_workspace.Session.GetOutputValue(outputs[0]), Is.EqualTo(Signal.High));
            Assert.That(_workspace.Session.GetOutputValue(outputs[1]), Is.EqualTo(Signal.Low));
            Assert.That(evaluations, Is.LessThanOrEqualTo(ChainLength + 2), "Only the toggled chain is evaluated.");
            Assert.That(_workspace.Presenter.NetSignalUpdateCount, Is.LessThanOrEqualTo(ChainLength + 1),
                "Only the toggled chain's nets are pushed to views.");
            Assert.That(_workspace.Presenter.GateViewCount, Is.EqualTo(gateCount), "No views created or destroyed.");

            // Move one gate: only its two wires are rerouted.
            _workspace.Presenter.ResetDiagnostics();
            stopwatch.Restart();
            _workspace.MoveNodes(new[] { middleGate }, new[] { new CircuitPoint(ChainLength * 2.5f, 41f) });
            yield return null;
            long moveMs = stopwatch.ElapsedMilliseconds;
            Assert.That(_workspace.Presenter.WireRerouteCount, Is.EqualTo(2));

            TestContext.WriteLine(gateCount + " nodes / " + wireCount + " wires: load " + loadMs + " ms, toggle " +
                toggleMs + " ms (" + evaluations + " evaluations), move " + moveMs + " ms.");
            Assert.That(loadMs, Is.LessThan(10000));
            Assert.That(toggleMs, Is.LessThan(1000));
            Assert.That(moveMs, Is.LessThan(1000));
        }
    }
}
