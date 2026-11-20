using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.GateLibrary.Examples;
using DigitalLogicSandbox.GateLibrary.Programs;
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

        private const string TinySpec = "opcode 2\nNOP 0 ; idle\nLDI 1 n ; A = n\nJMP 2 n ; jump\nHLT 3 ; stop";

        /// <summary>Publishes a 4-word ROM with 6-bit inputs and places it fed by four 6-bit constants.</summary>
        private NodeId PlaceSixBitRom(out CircuitDefinition definition)
        {
            definition = new CircuitDefinition(DefinitionId.New(), "ROM 4x6");
            for (int i = 0; i < 4; i++)
            {
                definition.AddInputBoundary("W" + i, new CircuitPoint(0f, -i), 6);
            }

            definition.AddOutputBoundary("DATA", new CircuitPoint(6f, 0f), 6);
            _app.Library.Add(definition);

            NodeId rom = _workspace.PlaceNode(ComponentRef.Custom(definition.Reference), new Vector2(60f, 60f));
            for (int i = 0; i < 4; i++)
            {
                NodeId word = _workspace.PlaceNode(ComponentRef.Constant(6, 0), new Vector2(50f, 64f - 2f * i));
                Assert.That(_workspace.Connect(new PinRef(word, BoundarySignatures.Pin),
                    new PinRef(rom, definition.Inputs[i].Id)), Is.EqualTo(ConnectionError.None));
            }

            return rom;
        }

        private static MenuItem Item(IReadOnlyList<MenuItem> items, string label)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Label == label)
                {
                    return items[i];
                }
            }

            Assert.Fail("No menu item '" + label + "'.");
            return default;
        }

        [UnityTest]
        public IEnumerator UserInstructionSet_DefinedInTheDialog_BindsAppliesAndUndoes()
        {
            yield return LoadDemo();
            NodeId rom = PlaceSixBitRom(out _);
            var words = new List<NodeId>();
            Assert.That(_workspace.TryGetProgramWords(rom, words), Is.True);
            Assert.That(words.Count, Is.EqualTo(4));
            Assert.That(_workspace.ProgramWordWidth(rom), Is.EqualTo(6));

            ProgramEditorDialog dialog = _app.Shell.ProgramEditor;
            Assert.That(dialog.Open(rom), Is.True);
            Assert.That(dialog.ResolvedSet.Id, Is.EqualTo(InstructionSetLibrary.RawId), "6-bit words default to raw.");
            Assert.That(dialog.SourceField.text, Is.EqualTo("0: 0x00\n1: 0x00\n2: 0x00\n3: 0x00"));
            Assert.That(dialog.InstructionSetText, Does.Contain("Raw words (default)"));

            IReadOnlyList<MenuItem> items = dialog.InstructionSetMenuItems();
            Assert.That(Item(items, "Raw words (current)").Enabled, Is.False);
            Item(items, "New instruction set…").Action();
            yield return null;

            InstructionSetDialog setDialog = dialog.SetDialog;
            Assert.That(setDialog.IsOpen, Is.True);
            setDialog.NameField.text = "Tiny";
            setDialog.SetSource("opcode 2\nNOP 0\nNOP 1");
            Assert.That(setDialog.SaveButton.interactable, Is.False);
            Assert.That(setDialog.MessagesText, Does.Contain("Line 3"));
            Assert.That(setDialog.Decorator.LineNumbersText, Does.Contain("!</color> 3"));
            setDialog.SetSource(TinySpec);
            Assert.That(setDialog.SaveButton.interactable, Is.True);
            Assert.That(setDialog.MessagesText, Does.Contain("LDI n (A = n)"));

            int undoDepth = _workspace.History.UndoCount;
            Assert.That(setDialog.Save(), Is.True);
            Assert.That(setDialog.IsOpen, Is.False);
            Assert.That(_workspace.History.UndoCount, Is.EqualTo(undoDepth + 1), "Add and bind are one step.");
            Assert.That(_workspace.InputSuspended, Is.True, "The program editor is still open.");
            Assert.That(dialog.ResolvedSet.Name, Is.EqualTo("Tiny"));
            Assert.That(dialog.SourceField.text, Is.EqualTo("0: NOP\n1: NOP\n2: NOP\n3: NOP"),
                "An untouched listing is shown in the new set.");
            Assert.That(dialog.ReferenceText, Does.Contain("JMP n (jump)"));

            dialog.SetSource("LDI 15\nJMP 0\nHLT");
            Assert.That(dialog.ApplyButton.interactable, Is.True, dialog.MessagesText);
            Assert.That(dialog.MessagesText, Does.Contain("3 of 4 6-bit words used; the rest are NOP"));
            Assert.That(dialog.Apply(), Is.True);
            int[] expected = { 31, 32, 48, 0 };
            string[] labels = { "0: LDI 15", "1: JMP 0", "2: HLT", "3: NOP" };
            for (int i = 0; i < 4; i++)
            {
                NodeDefinition word = _workspace.RootCircuit.GetNode(words[i]);
                Assert.That(word.Component.ConstantValue, Is.EqualTo(expected[i]), "Word " + i);
                Assert.That(word.Label, Is.EqualTo(labels[i]));
            }

            Assert.That(_workspace.ChangeConstantValue(words[1], 33), Is.True);
            Assert.That(_workspace.RootCircuit.GetNode(words[1]).Label, Is.EqualTo("1: JMP 1"),
                "Labels follow values in the gate's own instruction set.");

            Assert.That(_workspace.History.Undo(), Is.True);
            Assert.That(_workspace.History.Undo(), Is.True);
            Assert.That(_workspace.RootCircuit.GetNode(words[0]).Component.ConstantValue, Is.EqualTo(0));
            Assert.That(_workspace.History.Undo(), Is.True);
            Assert.That(_workspace.Programs.Sets, Is.Empty, "Undo removes the set and its binding.");
            Assert.That(_workspace.TryGetProgramInstructionSet(rom, out ResolvedInstructionSet set), Is.True);
            Assert.That(set.Id, Is.EqualTo(InstructionSetLibrary.RawId));
        }

        [UnityTest]
        public IEnumerator Editor_ShowsLineNumbers_ErrorMarkers_AndColouring()
        {
            yield return LoadDemo();
            ProgramEditorDialog dialog = _app.Shell.ProgramEditor;
            Assert.That(dialog.Open(Rom()), Is.True);
            Assert.That(dialog.ResolvedSet.Id, Is.EqualTo(InstructionSetLibrary.ExampleCpuId));
            Assert.That(dialog.InstructionSetText, Does.Contain("Example CPU (default)"));
            Assert.That(dialog.EditSetButton.gameObject.activeSelf, Is.False, "The built-in set is not editable.");

            const string source = "LDI 1 ; <b>start</b>\nFOO\nOUT";
            dialog.SetSource(source);
            yield return null;
            Assert.That(dialog.Decorator.LineNumbersText, Does.StartWith("1\n<color=#"));
            Assert.That(dialog.Decorator.LineNumbersText, Does.EndWith("!</color> 2\n3"));
            string rich = dialog.Decorator.HighlightText;
            Assert.That(rich, Does.Contain("<color=#").And.Contain(">LDI</color>"));
            string visible = Regex.Replace(rich, "<color=#[0-9A-F]{6}>|</color>", string.Empty)
                .Replace("<noparse><</noparse>", "<");
            Assert.That(visible, Is.EqualTo(source), "The colouring draws exactly the source text.");
            Assert.That(dialog.SourceField.textComponent.color.a, Is.EqualTo(0f), "The field's own glyphs are hidden.");
            dialog.Close();
        }

        [UnityTest]
        public IEnumerator InstructionSetBinding_SurvivesSaveAndOpen()
        {
            yield return LoadDemo();
            NodeId rom = PlaceSixBitRom(out CircuitDefinition definition);
            string id = _workspace.AddInstructionSet("Tiny", TinySpec, rom);
            Assert.That(id, Is.Not.Null);
            Assert.That(_workspace.ApplyProgram(rom, new[] { 31, 48 }), Is.EqualTo(2));

            string path = Path.Combine(_root, "isa.dlsproj");
            Assert.That(_app.Projects.SaveAs(path, out string message), Is.True, message);
            _app.Projects.NewProject();
            yield return null;
            Assert.That(_workspace.Programs.Sets, Is.Empty, "A new project has no instruction sets.");

            Assert.That(_app.Projects.Open(path, out message), Is.True, message);
            yield return null;
            Assert.That(_workspace.Programs.Sets.Count, Is.EqualTo(1));
            Assert.That(_workspace.Programs.Sets[0].Name, Is.EqualTo("Tiny"));
            Assert.That(_workspace.Programs.BindingOf(definition.Id), Is.EqualTo(id));

            NodeId reopened = NodeId.None;
            foreach (NodeDefinition node in _workspace.RootCircuit.Nodes)
            {
                if (node.Component.Kind == ComponentKind.Custom && node.Component.Definition.Id == definition.Id)
                {
                    reopened = node.Id;
                }
            }

            Assert.That(_app.Shell.ProgramEditor.Open(reopened), Is.True);
            Assert.That(_app.Shell.ProgramEditor.SourceField.text, Does.StartWith("0: LDI 15\n1: HLT"));
            _app.Shell.ProgramEditor.Close();
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
