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
using DigitalLogicSandbox.GateLibrary.Examples;
using DigitalLogicSandbox.Rendering.Gates;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>"Tidy layout" on the user's own circuits, and the part labels inside the generated gates.</summary>
    public sealed class TidyLayoutTests
    {
        private AppBootstrap _app;
        private WorkspaceController _workspace;
        private string _root;

        private IEnumerator LoadMain()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            _app = AppBootstrap.Instance;
            _workspace = _app.Workspace;
            _workspace.WorkspaceCamera.Animate = false;
            _root = Path.Combine(Application.temporaryCachePath, "TidyTest-" + Guid.NewGuid().ToString("N"));
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

        private static PinRef Pin(NodeId boundary)
        {
            return new PinRef(boundary, BoundarySignatures.Pin);
        }

        private void OpenBackgroundMenu()
        {
            Vector2 world = new Vector2(400f, 400f); // far from every part
            _workspace.RequestContextMenu(_workspace.WorkspaceCamera.WorldToScreen(world), world);
            Assert.That(_app.Shell.ContextMenu.IsOpen, Is.True);
        }

        [UnityTest]
        public IEnumerator TidyLayout_ArrangesAScrambledCircuit_AndOneUndoRestoresIt()
        {
            yield return LoadMain();
            // A NAND built back to front: output on the left, inputs on the right, the NOT turned sideways.
            NodeId lamp = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(-12, 3));
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(9, -4));
            NodeId b = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(2, 6));
            NodeId and = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.And), new Vector2(-3, -7));
            NodeId not = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(6, 2));
            _workspace.Connect(Pin(a), new PinRef(and, AndGate.InputA));
            _workspace.Connect(Pin(b), new PinRef(and, AndGate.InputB));
            _workspace.Connect(new PinRef(and, AndGate.Output), new PinRef(not, NotGate.Input));
            _workspace.Connect(new PinRef(not, NotGate.Output), Pin(lamp));
            _workspace.Selection.Clear();
            _workspace.Selection.Add(not);
            Assert.That(_workspace.RotateSelection(), Is.True);
            yield return null;

            CircuitDefinition circuit = _workspace.Circuit;
            var before = new CircuitPoint[circuit.Nodes.Count];
            var rotations = new Rotation[circuit.Nodes.Count];
            for (int i = 0; i < before.Length; i++)
            {
                before[i] = circuit.Nodes[i].Position;
                rotations[i] = circuit.Nodes[i].Rotation;
            }

            OpenBackgroundMenu();
            Assert.That(_app.Shell.ContextMenu.Invoke("Tidy layout"), Is.True);
            yield return null;

            Assert.That(_workspace.History.UndoDescription, Is.EqualTo("Tidy layout"));
            float x(NodeId id) => circuit.GetNode(id).Position.X;
            Assert.That(x(a), Is.LessThan(x(and)));
            Assert.That(x(b), Is.LessThan(x(and)));
            Assert.That(x(and), Is.LessThan(x(not)));
            Assert.That(x(not), Is.LessThan(x(lamp)), "Reads left to right.");
            Assert.That(circuit.GetNode(not).Rotation, Is.EqualTo(Rotation.R0));
            for (int i = 0; i < circuit.Nodes.Count; i++)
            {
                for (int j = i + 1; j < circuit.Nodes.Count; j++)
                {
                    Assert.That(_workspace.Geometry.GetBounds(circuit.Nodes[i].Id)
                        .Overlaps(_workspace.Geometry.GetBounds(circuit.Nodes[j].Id)), Is.False, "No overlaps.");
                }
            }

            Assert.That(_workspace.TidyLayout(), Is.False, "Tidying a tidy circuit changes nothing.");

            _workspace.History.Undo();
            yield return null;
            for (int i = 0; i < before.Length; i++)
            {
                Assert.That(circuit.Nodes[i].Position, Is.EqualTo(before[i]));
                Assert.That(circuit.Nodes[i].Rotation, Is.EqualTo(rotations[i]));
            }
        }

        [UnityTest]
        public IEnumerator CpuInternals_ShowPartLabels_AndTidyIsOffWhileInspecting()
        {
            yield return LoadMain();
            _app.Shell.RequestCpuDemo();
            yield return null;
            NodeId cpu = NodeId.None;
            foreach (NodeDefinition node in _workspace.RootCircuit.Nodes)
            {
                if (node.Component.Kind == ComponentKind.Custom &&
                    node.Component.Definition.Id.Value == ExampleGatePack.IdPrefix + "cpu4")
                {
                    cpu = node.Id;
                }
            }

            Assert.That(_workspace.OpenNode(cpu), Is.True);
            yield return null;

            NodeId pc = NodeId.None;
            foreach (NodeDefinition node in _workspace.Circuit.Nodes)
            {
                if (node.Label == "PC")
                {
                    pc = node.Id;
                }
            }

            Assert.That(_workspace.Presenter.TryGetGateView(pc, out GateView view), Is.True);
            Assert.That(view.CaptionText, Is.EqualTo("PC"), "The register is named above its body.");

            Assert.That(_workspace.IsEditable, Is.False);
            Assert.That(_workspace.CanTidyLayout, Is.False);
            OpenBackgroundMenu();
            Assert.That(_app.Shell.ContextMenu.Labels, Does.Contain("Tidy layout"));
            Assert.That(_app.Shell.ContextMenu.Invoke("Tidy layout"), Is.False, "Disabled while inspecting.");
        }
    }
}
