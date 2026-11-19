using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.GateLibrary.Examples;
using DigitalLogicSandbox.Rendering.Displays;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.UI.ContextMenus;
using DigitalLogicSandbox.UI.Dialogs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>The CPU demo's program editor: finding program words, applying a program, labels and undo.</summary>
    public sealed class ProgramEditorTests
    {
        private const string Countdown = "LDI 9\nOUT\nSUBI 1\nJZ 5\nJMP 1\nHLT";

        private AppBootstrap _app;
        private WorkspaceController _workspace;
        private string _root;

        private IEnumerator LoadDemo()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            _app = AppBootstrap.Instance;
            _workspace = _app.Workspace;
            _workspace.WorkspaceCamera.Animate = false;
            _root = Path.Combine(Application.temporaryCachePath, "ProgramEditorTest-" + Guid.NewGuid().ToString("N"));
            _app.Projects.ProjectsDirectory = Path.Combine(_root, "Projects");
            _app.Projects.AutosaveDirectory = Path.Combine(_root, "Autosave");
            _app.Projects.RememberLastProject = false;
            _app.Projects.AutosaveIntervalSeconds = 0f;
            _app.Shell.RequestCpuDemo();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null && Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private NodeId Rom()
        {
            foreach (NodeDefinition node in _workspace.RootCircuit.Nodes)
            {
                if (node.Component.Kind == ComponentKind.Custom &&
                    node.Component.Definition.Id.Value == ExampleGatePack.IdPrefix + "rom16x8")
                {
                    return node.Id;
                }
            }

            Assert.Fail("No ROM in the demo.");
            return NodeId.None;
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

        private void RunToHalt()
        {
            NodeId halt = Labelled("HALT");
            int edges = 0;
            while (Shown(halt) != "ON")
            {
                Assert.That(edges, Is.LessThan(400), "The program halts.");
                Assert.That(_workspace.TickClock(), Is.True);
                edges++;
            }
        }

        private List<NodeId> Words()
        {
            var words = new List<NodeId>();
            Assert.That(_workspace.TryGetProgramWords(Rom(), words), Is.True);
            return words;
        }

        [UnityTest]
        public IEnumerator ProgramWords_AreFoundByStructure_InInputOrder()
        {
            yield return LoadDemo();
            List<NodeId> words = Words();
            Assert.That(words.Count, Is.EqualTo(16));
            for (int i = 0; i < 16; i++)
            {
                NodeDefinition word = _workspace.RootCircuit.GetNode(words[i]);
                Assert.That(word.Component.ConstantValue, Is.EqualTo(CpuDemo.Fibonacci[i]), "Word " + i);
                Assert.That(word.Label, Is.EqualTo(CpuAssembler.WordLabel(i, CpuDemo.Fibonacci[i])));
                Assert.That(_workspace.TryGetProgramOwner(words[i], out NodeId owner), Is.True);
                Assert.That(owner, Is.EqualTo(Rom()));
            }

            Assert.That(_workspace.HasProgram(Labelled("HALT")), Is.False, "A display has no program.");
            NodeId gate = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.And), new Vector2(40f, 40f));
            Assert.That(_workspace.HasProgram(gate), Is.False, "A primitive gate has no program.");
        }

        [UnityTest]
        public IEnumerator Dialog_OpensFromTheContextMenu_ShowsErrors_AndAppliesAProgramThatRuns()
        {
            yield return LoadDemo();
            _workspace.Play();
            _workspace.ClockEnabled = false;
            NodeId rom = Rom();

            ContextMenuView menu = _app.Shell.ContextMenu;
            menu.Show(new ContextMenuRequest(Vector2.zero, rom, null));
            Assert.That(menu.Labels, Does.Contain("Edit program…"));
            Assert.That(menu.Invoke("Edit program…"), Is.True);
            yield return null;

            ProgramEditorDialog dialog = _app.Shell.ProgramEditor;
            Assert.That(dialog.IsOpen, Is.True);
            Assert.That(dialog.SourceField.text, Is.EqualTo(CpuAssembler.Listing(ToInts(CpuDemo.Fibonacci))));
            Assert.That(dialog.ApplyButton.interactable, Is.True);

            dialog.SetSource("LDI 9\nFOO");
            Assert.That(dialog.ApplyButton.interactable, Is.False);
            Assert.That(dialog.MessagesText, Does.Contain("Line 2"));
            Assert.That(dialog.Apply(), Is.False);

            dialog.SetSource(Countdown);
            Assert.That(dialog.ApplyButton.interactable, Is.True);
            int undoDepth = _workspace.History.UndoCount;
            Assert.That(dialog.Apply(), Is.True);
            Assert.That(dialog.IsOpen, Is.False);
            Assert.That(_workspace.History.UndoCount, Is.EqualTo(undoDepth + 1), "One undo step for the program.");

            List<NodeId> words = Words();
            Assert.That(_workspace.RootCircuit.GetNode(words[0]).Label, Is.EqualTo("0: LDI 9"));
            Assert.That(_workspace.RootCircuit.GetNode(words[5]).Label, Is.EqualTo("5: HLT"));
            Assert.That(_workspace.RootCircuit.GetNode(words[6]).Label, Is.EqualTo("6: NOP"));
            yield return null;

            _workspace.ResetSimulation();
            RunToHalt();
            // 9, 8, … 1 on OUT; SUBI reaches 0 and JZ jumps to HLT.
            Assert.That(Shown(Labelled("OUT")), Is.EqualTo(DisplayEncoding.FormatValue(DisplayStyle.Decimal, 4, 1, 0)));

            Assert.That(_workspace.History.Undo(), Is.True);
            for (int i = 0; i < 16; i++)
            {
                NodeDefinition word = _workspace.RootCircuit.GetNode(words[i]);
                Assert.That(word.Component.ConstantValue, Is.EqualTo(CpuDemo.Fibonacci[i]));
                Assert.That(word.Label, Is.EqualTo(CpuAssembler.WordLabel(i, CpuDemo.Fibonacci[i])));
            }

            yield return null;
            _workspace.ResetSimulation();
            RunToHalt();
            Assert.That(Shown(Labelled("OUT")), Is.EqualTo(DisplayEncoding.FormatValue(DisplayStyle.Decimal, 4, 8, 0)));
        }

        [UnityTest]
        public IEnumerator ChangingOneWord_UpdatesItsMnemonic_ButNotAFreeTextLabel()
        {
            yield return LoadDemo();
            List<NodeId> words = Words();
            int undoDepth = _workspace.History.UndoCount;
            Assert.That(_workspace.ChangeConstantValue(words[0], CpuAssembler.Assemble("LDI", 2)), Is.True);
            Assert.That(_workspace.RootCircuit.GetNode(words[0]).Label, Is.EqualTo("0: LDI 2"));
            Assert.That(_workspace.History.UndoCount, Is.EqualTo(undoDepth + 1));
            Assert.That(_workspace.History.Undo(), Is.True);
            Assert.That(_workspace.RootCircuit.GetNode(words[0]).Label, Is.EqualTo("0: LDI 1"));

            Assert.That(_workspace.SetNodeLabel(words[1], "store a"), Is.True);
            Assert.That(_workspace.ChangeConstantValue(words[1], CpuAssembler.Assemble("STA", 5)), Is.True);
            Assert.That(_workspace.RootCircuit.GetNode(words[1]).Label, Is.EqualTo("store a"));
        }

        [UnityTest]
        public IEnumerator ApplyingAnUnchangedProgram_AddsNoUndoStep()
        {
            yield return LoadDemo();
            int undoDepth = _workspace.History.UndoCount;
            Assert.That(_workspace.ApplyProgram(Rom(), CpuDemo.Fibonacci), Is.EqualTo(0));
            Assert.That(_workspace.History.UndoCount, Is.EqualTo(undoDepth));
            Assert.That(_workspace.ApplyProgram(Rom(), new byte[17]), Is.EqualTo(-1), "More words than the ROM holds.");
        }

        private static int[] ToInts(byte[] bytes)
        {
            var result = new int[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                result[i] = bytes[i];
            }

            return result;
        }
    }
}
