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
using DigitalLogicSandbox.Persistence.Recovery;
using DigitalLogicSandbox.UI.Dialogs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>Phase 8 in the real Main scene: save/open round trip, unsaved-change prompts, recovery, undo.</summary>
    public sealed class ProjectPersistenceTests
    {
        private AppBootstrap _app;
        private WorkspaceController _workspace;
        private ProjectController _projects;
        private string _root;

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
            _projects = _app.Projects;
            _root = Path.Combine(Application.temporaryCachePath, "ProjectPersistenceTest-" + Guid.NewGuid().ToString("N"));
            _projects.ProjectsDirectory = Path.Combine(_root, "Projects");
            _projects.AutosaveDirectory = Path.Combine(_root, "Autosave");
            _projects.RememberLastProject = false;
            _projects.AutosaveIntervalSeconds = 0f; // no timed autosave during tests
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null && Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        /// <summary>Main circuit: two switches → published NAND instance → lamp.</summary>
        private (CircuitDefinition nand, NodeId a, NodeId b, NodeId lamp) BuildMain()
        {
            CircuitDefinition nand = HierarchyFixtures.PublishNand(_app.Library);
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, 1));
            NodeId b = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, -1));
            NodeId gate = _workspace.PlaceNode(ComponentRef.Custom(nand.Reference), new Vector2(0, 0));
            NodeId lamp = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(6, 0));
            _workspace.Connect(Pin(a), new PinRef(gate, nand.Inputs[0].Id));
            _workspace.Connect(Pin(b), new PinRef(gate, nand.Inputs[1].Id));
            _workspace.Connect(new PinRef(gate, nand.Outputs[0].Id), Pin(lamp));
            return (nand, a, b, lamp);
        }

        private Signal RootInput(NodeId boundary)
        {
            return _workspace.Navigator.RootSession.GetInputValue(boundary);
        }

        private Signal RootOutput(NodeId boundary)
        {
            return _workspace.Navigator.RootSession.GetOutputValue(boundary);
        }

        [UnityTest]
        public IEnumerator SaveAs_New_Open_RestoresCircuitLibraryInputsAndSettings()
        {
            yield return LoadMain();
            Assert.That(_projects.IsDirty, Is.False, "A fresh project starts clean.");
            Assert.That(_app.Shell.Toolbar.TitleText, Does.Contain("Untitled").And.Not.Contain("*"));

            (CircuitDefinition nand, NodeId a, NodeId b, NodeId lamp) = BuildMain();
            _workspace.ToggleInput(a);
            _workspace.ToggleInput(b);
            _workspace.GridVisible = false;
            _workspace.AnimationSettings.Speed = 2f;
            yield return null;
            Assert.That(RootOutput(lamp), Is.EqualTo(Signal.Low), "NAND(1,1) = 0");
            Assert.That(_projects.IsDirty, Is.True);
            Assert.That(_app.Shell.Toolbar.TitleText, Does.EndWith("*"));

            int nodeCount = _workspace.RootCircuit.Nodes.Count;
            int connectionCount = _workspace.RootCircuit.Connections.Count;
            DefinitionId rootId = _workspace.RootCircuit.Id;
            string path = _projects.ResolvePath("Round Trip");
            Assert.That(_projects.SaveAs(path, out string saveMessage), Is.True, saveMessage);
            Assert.That(File.Exists(path), Is.True);
            Assert.That(_projects.IsDirty, Is.False);
            Assert.That(_app.Shell.Toolbar.TitleText, Does.Contain("Round Trip").And.Not.Contain("*"));

            _projects.NewProject();
            yield return null;
            Assert.That(_app.Library.AllVersions.Count, Is.Zero, "The custom gate library belongs to the project.");
            Assert.That(_workspace.RootCircuit.Nodes.Count, Is.Zero);
            Assert.That(_workspace.GridVisible, Is.True, "Defaults are restored.");
            Assert.That(_projects.CurrentPath, Is.Null);

            Assert.That(_projects.Open(path, out string openMessage), Is.True, openMessage);
            yield return null;
            CircuitDefinition root = _workspace.RootCircuit;
            Assert.That(root.Id, Is.EqualTo(rootId));
            Assert.That(root.Nodes.Count, Is.EqualTo(nodeCount));
            Assert.That(root.Connections.Count, Is.EqualTo(connectionCount));
            Assert.That(_app.Library.AllVersions.Count, Is.EqualTo(1));
            Assert.That(_app.Library.AllVersions[0].Id, Is.EqualTo(nand.Id));
            Assert.That(_app.Library.AllVersions[0].Name, Is.EqualTo("NAND"));
            Assert.That(RootInput(a), Is.EqualTo(Signal.High));
            Assert.That(RootInput(b), Is.EqualTo(Signal.High));
            Assert.That(RootOutput(lamp), Is.EqualTo(Signal.Low), "The loaded gate simulates from its definition.");
            Assert.That(_workspace.GridVisible, Is.False);
            Assert.That(_workspace.AnimationSettings.Speed, Is.EqualTo(2f));
            Assert.That(_workspace.History.CanUndo, Is.False, "History starts fresh after opening.");
            Assert.That(_projects.IsDirty, Is.False);
            Assert.That(_app.Shell.Toolbar.TitleText, Does.Contain("Round Trip").And.Not.Contain("*"));

            // The loaded instance can still be opened for inspection.
            NodeId gate = NodeId.None;
            foreach (NodeDefinition node in root.Nodes)
            {
                if (node.Component.Kind == ComponentKind.Custom)
                {
                    gate = node.Id;
                }
            }

            Assert.That(_workspace.OpenNode(gate), Is.True);
            Assert.That(_workspace.Circuit.Id, Is.EqualTo(nand.Id));
            Assert.That(_workspace.Navigator.TryNavigateTo(0, out string error), Is.True, error);
            Assert.That(_workspace.Circuit, Is.SameAs(root));
        }

        [UnityTest]
        public IEnumerator NewProject_WithUnsavedChanges_AsksFirst()
        {
            yield return LoadMain();
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, Vector2.zero);
            yield return null;
            Assert.That(_projects.IsDirty, Is.True);

            ConfirmDialog confirm = _app.Shell.Confirm;
            _app.Shell.Toolbar.NewButton.onClick.Invoke();
            Assert.That(confirm.IsOpen, Is.True);
            Assert.That(confirm.TitleText, Is.EqualTo("Unsaved changes"));
            Assert.That(_workspace.InputSuspended, Is.True);

            confirm.CancelButton.onClick.Invoke();
            Assert.That(confirm.IsOpen, Is.False);
            Assert.That(_workspace.InputSuspended, Is.False);
            Assert.That(_workspace.RootCircuit.TryGetNode(a, out _), Is.True, "Cancel keeps the work.");
            Assert.That(_projects.IsDirty, Is.True);

            _app.Shell.RequestNew();
            confirm.SecondaryButton.onClick.Invoke(); // Don't save
            Assert.That(_workspace.RootCircuit.Nodes.Count, Is.Zero);
            Assert.That(_projects.IsDirty, Is.False);

            // A clean project is replaced without asking.
            _app.Shell.RequestNew();
            Assert.That(confirm.IsOpen, Is.False);
        }

        [UnityTest]
        public IEnumerator Exit_WithUnsavedChanges_AsksFirst()
        {
            yield return LoadMain();
            int quits = 0;
            _app.Shell.QuitAction = () => quits++;
            _workspace.PlaceNode(ComponentRef.InputBoundary, Vector2.zero);
            yield return null;
            Assert.That(_projects.IsDirty, Is.True);

            ConfirmDialog confirm = _app.Shell.Confirm;
            _app.Shell.Toolbar.ExitButton.onClick.Invoke();
            Assert.That(confirm.IsOpen, Is.True);
            Assert.That(confirm.TitleText, Is.EqualTo("Unsaved changes"));

            confirm.CancelButton.onClick.Invoke();
            Assert.That(quits, Is.Zero, "Cancel keeps the application open.");

            _app.Shell.Toolbar.ExitMenuButton.onClick.Invoke();
            Assert.That(confirm.IsOpen, Is.True, "File > Exit asks too.");
            confirm.SecondaryButton.onClick.Invoke(); // Don't save
            Assert.That(quits, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Exit_CleanProject_QuitsImmediately()
        {
            yield return LoadMain();
            int quits = 0;
            _app.Shell.QuitAction = () => quits++;
            Assert.That(_projects.IsDirty, Is.False);

            _app.Shell.Toolbar.ExitButton.onClick.Invoke();
            Assert.That(_app.Shell.Confirm.IsOpen, Is.False);
            Assert.That(quits, Is.EqualTo(1));

            Assert.That(_app.Shell.Toolbar.MinimizeButton, Is.Not.Null);
            Assert.That(_app.Shell.Toolbar.MinimizeButton.gameObject.activeInHierarchy, Is.True);
            _app.Shell.Toolbar.MinimizeButton.onClick.Invoke(); // no-op in the editor
        }

        [UnityTest]
        public IEnumerator SaveFromThePrompt_OnAnUntitledProject_AsksForANameThenContinues()
        {
            yield return LoadMain();
            _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), Vector2.zero);
            yield return null;

            _app.Shell.RequestNew();
            _app.Shell.Confirm.PrimaryButton.onClick.Invoke(); // Save
            ProjectFileDialog files = _app.Shell.ProjectFiles;
            Assert.That(files.IsOpen, Is.True);
            Assert.That(files.Mode, Is.EqualTo(ProjectFileDialogMode.SaveAs));
            files.NameField.text = "Prompted";
            Assert.That(files.Confirm(), Is.True);
            Assert.That(File.Exists(_projects.ResolvePath("Prompted")), Is.True);
            Assert.That(_workspace.RootCircuit.Nodes.Count, Is.Zero, "The new project followed the save.");
            Assert.That(_projects.CurrentPath, Is.Null);
        }

        [UnityTest]
        public IEnumerator FileDialog_SaveAs_AsksBeforeOverwriting_AndOpenListsProjects()
        {
            yield return LoadMain();
            BuildMain();
            yield return null;
            ProjectFileDialog files = _app.Shell.ProjectFiles;

            _app.Shell.Toolbar.SaveButton.onClick.Invoke(); // never saved → Save As
            Assert.That(files.IsOpen, Is.True);
            Assert.That(files.Mode, Is.EqualTo(ProjectFileDialogMode.SaveAs));
            files.NameField.text = "First";
            files.ConfirmButton.onClick.Invoke();
            Assert.That(files.IsOpen, Is.False);
            Assert.That(_projects.DisplayName, Is.EqualTo("First"));

            _app.Shell.RequestSaveAs();
            files.NameField.text = "Second";
            Assert.That(files.Confirm(), Is.True);

            // Saving "Second" over "First" needs a second click.
            _app.Shell.RequestSaveAs();
            files.NameField.text = "First";
            Assert.That(files.Confirm(), Is.False);
            Assert.That(files.MessageText, Does.Contain("already exists"));
            Assert.That(files.Confirm(), Is.True);
            Assert.That(_projects.DisplayName, Is.EqualTo("First"));

            _app.Shell.Toolbar.OpenButton.onClick.Invoke();
            Assert.That(files.IsOpen, Is.True);
            Assert.That(files.Mode, Is.EqualTo(ProjectFileDialogMode.Open));
            Assert.That(files.Items.Count, Is.EqualTo(2));
            files.NameField.text = "Missing";
            Assert.That(files.Confirm(), Is.False);
            Assert.That(files.MessageText, Does.Contain("not found"));
            files.NameField.text = "Second";
            Assert.That(files.Confirm(), Is.True);
            Assert.That(_projects.DisplayName, Is.EqualTo("Second"));
            Assert.That(_workspace.RootCircuit.Nodes.Count, Is.EqualTo(4));
        }

        [UnityTest]
        public IEnumerator InputToggle_IsUndoable_AndMarksTheProjectDirty()
        {
            yield return LoadMain();
            (_, NodeId a, _, NodeId lamp) = BuildMain();
            _projects.SaveAs(_projects.ResolvePath("Undo"), out _);
            int before = _workspace.History.UndoCount;

            Assert.That(_workspace.ToggleInput(a), Is.True);
            Assert.That(_workspace.History.UndoCount, Is.EqualTo(before + 1));
            Assert.That(RootInput(a), Is.EqualTo(Signal.High));
            Assert.That(_projects.IsDirty, Is.True);

            _workspace.History.Undo();
            Assert.That(RootInput(a), Is.EqualTo(Signal.Low));
            Assert.That(RootOutput(lamp), Is.EqualTo(Signal.High));
            _workspace.History.Redo();
            Assert.That(RootInput(a), Is.EqualTo(Signal.High));
            Assert.That(_workspace.ToggleInput(lamp), Is.False, "Lamps are not toggles.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator RenameCustomGate_UpdatesLibraryAndUndoes()
        {
            yield return LoadMain();
            (CircuitDefinition nand, _, _, _) = BuildMain();

            Assert.That(_workspace.RenameCustomGate(nand.Id, "Not And"), Is.True);
            Assert.That(nand.Name, Is.EqualTo("Not And"));
            Assert.That(nand.IsReadOnly, Is.True, "Renaming does not unpublish the definition.");
            yield return null;
            Assert.That(_app.Shell.Library.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true),
                Has.Some.Matches<TMPro.TextMeshProUGUI>(t => t.text.Contains("Not And")));

            _workspace.History.Undo();
            Assert.That(nand.Name, Is.EqualTo("NAND"));
            Assert.That(_workspace.RenameCustomGate(nand.Id, "   "), Is.False, "Blank names are rejected.");
        }

        [UnityTest]
        public IEnumerator Autosave_IsOfferedAtStartup_AndRecoversTheWork()
        {
            yield return LoadMain();
            BuildMain();
            yield return null;
            Assert.That(_projects.AutosaveNow(), Is.True);
            Assert.That(File.Exists(_projects.UntitledAutosavePath), Is.True);

            // Simulated restart: the question is asked over an empty project.
            string question = null;
            Action recover = null;
            _projects.Startup(false, (text, onRecover, onDiscard) =>
            {
                question = text;
                recover = onRecover;
            });
            Assert.That(question, Does.Contain("untitled project"));
            Assert.That(_workspace.RootCircuit.Nodes.Count, Is.Zero);

            recover();
            yield return null;
            Assert.That(_workspace.RootCircuit.Nodes.Count, Is.EqualTo(4));
            Assert.That(_app.Library.AllVersions.Count, Is.EqualTo(1));
            Assert.That(_projects.IsDirty, Is.True, "Recovered work still has to be saved.");

            // Saving clears the autosave, so it is not offered again.
            Assert.That(_projects.SaveAs(_projects.ResolvePath("Recovered"), out _), Is.True);
            Assert.That(File.Exists(_projects.UntitledAutosavePath), Is.False);
            Assert.That(ProjectFileStore.FindRecovery(null, _projects.UntitledAutosavePath), Is.Null);
        }

        [UnityTest]
        public IEnumerator Autosave_Discard_DeletesItAndKeepsTheEmptyProject()
        {
            yield return LoadMain();
            BuildMain();
            _projects.AutosaveNow();
            Action discard = null;
            _projects.Startup(false, (text, onRecover, onDiscard) => discard = onDiscard);
            Assert.That(File.Exists(_projects.UntitledAutosavePath), Is.True, "Nothing is deleted before the answer.");
            discard();
            Assert.That(File.Exists(_projects.UntitledAutosavePath), Is.False);
            Assert.That(_workspace.RootCircuit.Nodes.Count, Is.Zero);
            yield return null;
        }
    }
}
