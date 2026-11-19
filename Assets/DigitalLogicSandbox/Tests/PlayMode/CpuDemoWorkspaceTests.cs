using System;
using System.Collections;
using System.IO;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.GateLibrary.Examples;
using DigitalLogicSandbox.Rendering.Displays;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.UI.Dialogs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>Phase E+ in the real Main scene: File → "Open CPU demo", run to HALT, edit the program, inspect.</summary>
    public sealed class CpuDemoWorkspaceTests
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
            _root = Path.Combine(Application.temporaryCachePath, "CpuDemoTest-" + Guid.NewGuid().ToString("N"));
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

        private NodeId Labelled(string label)
        {
            foreach (NodeDefinition node in _workspace.RootCircuit.Nodes)
            {
                if (node.Label == label)
                {
                    return node.Id;
                }
            }

            Assert.Fail("No node labelled " + label);
            return NodeId.None;
        }

        private string Shown(NodeId display)
        {
            _workspace.Sync();
            Assert.That(_workspace.Presenter.TryGetGateView(display, out GateView view), Is.True);
            return view.DisplayText;
        }

        /// <summary>Ticks the clock until the HALT lamp lights (or a generous edge budget runs out).</summary>
        private int RunToHalt()
        {
            NodeId halt = Labelled("HALT");
            int edges = 0;
            while (Shown(halt) != "ON")
            {
                Assert.That(edges, Is.LessThan(400), "The program halts.");
                Assert.That(_workspace.TickClock(), Is.True);
                edges++;
            }

            return edges;
        }

        [UnityTest]
        public IEnumerator FileMenu_OpensTheDemo_WhichRunsToHalt_AfterAskingAboutUnsavedWork()
        {
            yield return LoadMain();
            _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), Vector2.zero);
            yield return null;
            Assert.That(_app.Projects.IsDirty, Is.True);

            ConfirmDialog confirm = _app.Shell.Confirm;
            _app.Shell.Toolbar.CpuDemoButton.onClick.Invoke();
            Assert.That(confirm.IsOpen, Is.True, "Unsaved work is not discarded silently.");
            confirm.SecondaryButton.onClick.Invoke(); // Don't save
            yield return null;

            Assert.That(_workspace.RootCircuit.Name, Is.EqualTo(CpuDemo.MainCircuitName));
            Assert.That(_workspace.Library.DefinitionCount, Is.EqualTo(27));
            Assert.That(_app.Projects.IsDirty, Is.False);
            Assert.That(_app.Projects.CurrentPath, Is.Null, "The demo is a new, unsaved project.");
            Assert.That(_workspace.ClockFrequency, Is.EqualTo(CpuDemo.ClockFrequency));

            _workspace.Play();
            _workspace.ClockEnabled = false; // tick by hand for a deterministic run
            int edges = RunToHalt();
            Assert.That(Shown(Labelled("OUT")), Is.EqualTo(DisplayEncoding.FormatValue(DisplayStyle.Decimal, 4, 8, 0)),
                "Fibonacci ends on 8.");
            Assert.That(Shown(Labelled("PC")), Is.EqualTo(DisplayEncoding.FormatValue(DisplayStyle.Hex, 4, 13, 0)));
            Assert.That(edges, Is.LessThan(200));
        }

        [UnityTest]
        public IEnumerator EditingAProgramWord_ChangesTheRun_AndUndoRestoresIt()
        {
            yield return LoadMain();
            _app.Shell.RequestCpuDemo();
            yield return null;
            _workspace.Play();
            _workspace.ClockEnabled = false;

            NodeId first = Labelled("0: " + CpuDemo.Disassemble(CpuDemo.Fibonacci[0]));
            Assert.That(_workspace.ChangeConstantValue(first, CpuDemo.Assemble("LDI", 2)), Is.True);
            Assert.That(_workspace.RootCircuit.GetNode(first).Component.ConstantValue, Is.EqualTo(0x12));
            yield return null;
            RunToHalt();
            // Starting from 2, 2: OUT shows 2, 2, 4, 6 and the next sum (16) overflows.
            Assert.That(Shown(Labelled("OUT")), Is.EqualTo(DisplayEncoding.FormatValue(DisplayStyle.Decimal, 4, 6, 0)));

            Assert.That(_workspace.ChangeConstantValue(first, 300), Is.False, "Out of range for 8 bits.");
            Assert.That(_workspace.History.Undo(), Is.True);
            Assert.That(_workspace.RootCircuit.GetNode(first).Component.ConstantValue, Is.EqualTo(CpuDemo.Fibonacci[0]));
            yield return null;
            _workspace.ResetSimulation();
            RunToHalt();
            Assert.That(Shown(Labelled("OUT")), Is.EqualTo(DisplayEncoding.FormatValue(DisplayStyle.Decimal, 4, 8, 0)));
        }

        [UnityTest]
        public IEnumerator Cpu_OpensDownToItsRam_AndBreadcrumbsReturn()
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
            NodeId ram = NodeId.None;
            foreach (NodeDefinition node in _workspace.Circuit.Nodes)
            {
                if (node.Component.Kind == ComponentKind.Custom &&
                    node.Component.Definition.Id.Value == ExampleGatePack.IdPrefix + "ram16x4")
                {
                    ram = node.Id;
                }
            }

            Assert.That(_workspace.OpenNode(ram), Is.True);
            yield return null;
            Assert.That(_app.Shell.NavigationBar.Crumbs.Count, Is.EqualTo(3), "CPU demo › CPU 4-bit › RAM 16×4");
            _app.Shell.NavigationBar.Crumbs[0].onClick.Invoke();
            yield return null;
            Assert.That(_workspace.Navigator.Levels.Count, Is.EqualTo(1));
        }
    }
}
