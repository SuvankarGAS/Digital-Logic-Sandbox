using System;
using System.Collections;
using System.IO;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.GateLibrary.Examples;
using DigitalLogicSandbox.UI.Panels;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>Phase E in the real Main scene: the File-menu example pack, used and inspected like any custom gate.</summary>
    public sealed class ExamplePackWorkspaceTests
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
            _root = Path.Combine(Application.temporaryCachePath, "ExamplePackTest-" + Guid.NewGuid().ToString("N"));
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

        private int ExampleItems()
        {
            int count = 0;
            foreach (LibraryItemView item in _app.Shell.Library.Items)
            {
                ComponentRef component = item.Request.Component;
                if (component.Kind == ComponentKind.Custom && ExampleGatePack.IsExampleId(component.Definition.Id))
                {
                    count++;
                }
            }

            return count;
        }

        private CircuitDefinition Example(string key)
        {
            Assert.That(_workspace.Library.TryGetLatest(new DefinitionId(ExampleGatePack.IdPrefix + key),
                out CircuitDefinition definition), Is.True, key);
            return definition;
        }

        [UnityTest]
        public IEnumerator FileMenu_AddsTheExampleGates_AsOneUndoableStep()
        {
            yield return LoadMain();
            Assert.That(ExampleItems(), Is.Zero, "A new project starts with only the built-in gates.");

            _app.Shell.Toolbar.ExampleGatesButton.onClick.Invoke();
            yield return null;
            Assert.That(ExampleItems(), Is.EqualTo(17));
            bool memoryHeader = false;
            foreach (TextMeshProUGUI text in _app.Shell.Library.GetComponentsInChildren<TextMeshProUGUI>())
            {
                memoryHeader |= text.text == ExampleGatePack.MemoryCategory;
            }

            Assert.That(memoryHeader, Is.True, "The library groups the pack by category.");

            Assert.That(_workspace.LoadExampleGates().Succeeded, Is.False, "A second load is refused.");
            Assert.That(_workspace.Library.DefinitionCount, Is.EqualTo(17));

            Assert.That(_workspace.History.Undo(), Is.True);
            yield return null;
            Assert.That(ExampleItems(), Is.Zero);
            Assert.That(_workspace.History.Redo(), Is.True);
            yield return null;
            Assert.That(ExampleItems(), Is.EqualTo(17));
        }

        [UnityTest]
        public IEnumerator PlacedAlu_Computes_AndOpensDownToItsAdder()
        {
            yield return LoadMain();
            Assert.That(_workspace.LoadExampleGates().Succeeded, Is.True);
            CircuitDefinition alu = Example("alu4");

            NodeId a = _workspace.PlaceNode(ComponentRef.InputBus(4), new Vector2(-10, 3));
            NodeId b = _workspace.PlaceNode(ComponentRef.InputBus(4), new Vector2(-10, 0));
            NodeId op = _workspace.PlaceNode(ComponentRef.InputBus(2), new Vector2(-10, -3));
            NodeId gate = _workspace.PlaceNode(ComponentRef.Custom(alu.Reference), Vector2.zero);
            NodeId y = _workspace.PlaceNode(ComponentRef.OutputBus(4), new Vector2(10, 0));
            var pin = BoundarySignatures.Pin;
            Assert.That(_workspace.Connect(new PinRef(a, pin), new PinRef(gate, alu.Inputs[0].Id)), Is.EqualTo(ConnectionError.None));
            Assert.That(_workspace.Connect(new PinRef(b, pin), new PinRef(gate, alu.Inputs[1].Id)), Is.EqualTo(ConnectionError.None));
            Assert.That(_workspace.Connect(new PinRef(op, pin), new PinRef(gate, alu.Inputs[2].Id)), Is.EqualTo(ConnectionError.None));
            Assert.That(_workspace.Connect(new PinRef(gate, alu.Outputs[0].Id), new PinRef(y, pin)), Is.EqualTo(ConnectionError.None));
            _workspace.Sync();

            CircuitSession session = _workspace.Session;
            session.SetInputBits(a, 12);
            session.SetInputBits(b, 5);
            session.SetInputBits(op, 1); // subtract
            _workspace.Sync();
            yield return null;
            Assert.That(session.TryGetOutputBits(y, out ulong difference), Is.True);
            Assert.That(difference, Is.EqualTo(7UL), "12 - 5");

            session.SetInputBits(op, 0); // add, wrapping at 4 bits
            _workspace.Sync();
            Assert.That(session.TryGetOutputBits(y, out ulong sum), Is.True);
            Assert.That(sum, Is.EqualTo(1UL), "12 + 5 = 17 = 1 with carry");

            // Inspect: ALU, then the Adder 4-bit inside it, then back by breadcrumb.
            Assert.That(_workspace.OpenNode(gate), Is.True);
            yield return null;
            Assert.That(_workspace.Circuit, Is.SameAs(alu));
            NodeId inner = NodeId.None;
            foreach (NodeDefinition node in alu.Nodes)
            {
                if (node.Component.Kind == ComponentKind.Custom && node.Component.Definition.Id == Example("adder4").Id)
                {
                    inner = node.Id;
                }
            }

            Assert.That(_workspace.OpenNode(inner), Is.True);
            yield return null;
            Assert.That(_workspace.Circuit, Is.SameAs(Example("adder4")));
            Assert.That(_app.Shell.NavigationBar.Crumbs.Count, Is.EqualTo(3), "Main Circuit › ALU 4-bit › Adder 4-bit");

            _app.Shell.NavigationBar.Crumbs[0].onClick.Invoke();
            yield return null;
            Assert.That(_workspace.Navigator.Levels.Count, Is.EqualTo(1));
            Assert.That(session.TryGetOutputBits(y, out ulong after), Is.True);
            Assert.That(after, Is.EqualTo(1UL), "Inspection changed nothing.");
        }
    }
}
