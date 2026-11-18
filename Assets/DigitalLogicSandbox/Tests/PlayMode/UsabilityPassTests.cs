using System;
using System.Collections;
using System.IO;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.GateLibrary.Examples;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>Usability pass: the rough edges found by walking the app as a new user stay fixed.</summary>
    public sealed class UsabilityPassTests
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
            _root = Path.Combine(Application.temporaryCachePath, "UsabilityTest-" + Guid.NewGuid().ToString("N"));
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

        private NodeId FindCustom(CircuitDefinition circuit, string key)
        {
            foreach (NodeDefinition node in circuit.Nodes)
            {
                if (node.Component.Kind == ComponentKind.Custom &&
                    node.Component.Definition.Id.Value == ExampleGatePack.IdPrefix + key)
                {
                    return node.Id;
                }
            }

            Assert.Fail("No " + key);
            return NodeId.None;
        }

        [UnityTest]
        public IEnumerator CpuDemo_IsTitled_Hints_AndHasReadableBreadcrumbsOnFirstOpen()
        {
            yield return LoadMain();
            _app.Shell.RequestCpuDemo();
            yield return null;
            Assert.That(_app.Projects.DisplayName, Is.EqualTo(CpuDemo.MainCircuitName), "Not 'Untitled'.");
            Assert.That(_app.Shell.StatusBar.MessageText, Is.EqualTo(CpuDemo.OpenHint));
            Assert.That(_app.Shell.StatusBar.MessageText, Does.Not.Contain("Feedback"),
                "Intended memory loops do not raise a permanent warning.");

            // Opening the first level used to measure the crumbs while the bar was hidden ("… › N…").
            Assert.That(_workspace.OpenNode(FindCustom(_workspace.RootCircuit, "cpu4")), Is.True);
            yield return null;
            foreach (Button crumb in _app.Shell.NavigationBar.Crumbs)
            {
                TextMeshProUGUI label = crumb.GetComponentInChildren<TextMeshProUGUI>();
                Assert.That(crumb.GetComponent<LayoutElement>().preferredWidth,
                    Is.GreaterThanOrEqualTo(label.GetPreferredValues(label.text).x), label.text + " fits its crumb.");
            }

            _app.Shell.RequestNew();
            yield return null;
            Assert.That(_app.Projects.DisplayName, Is.EqualTo("Untitled"));
        }

        [UnityTest]
        public IEnumerator PackagingDialog_PromptsForAName_WithoutCallingItAnError()
        {
            yield return LoadMain();
            _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-4, 0));
            _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(4, 0));
            yield return null;
            _app.Shell.PackagingDialog.Open(false);
            yield return null;
            Assert.That(_app.Shell.PackagingDialog.MessagesText, Does.Contain("Give the gate a name."));
            Assert.That(_app.Shell.PackagingDialog.MessagesText, Does.Not.Contain("Error"));
            Assert.That(_app.Shell.PackagingDialog.PackageButton.interactable, Is.False, "Still needs a name.");
        }

        [UnityTest]
        public IEnumerator WideTruthTables_GrowTheWindow_SoEveryOutputIsVisible()
        {
            yield return LoadMain();
            Assert.That(_workspace.LoadExampleGates().Succeeded, Is.True);
            Assert.That(_workspace.Library.TryGetLatest(new DefinitionId(ExampleGatePack.IdPrefix + "alu4"),
                out CircuitDefinition alu), Is.True);
            _app.Shell.TruthTable.OpenForComponent(ComponentRef.Custom(alu.Reference));
            yield return null;
            // 10 input bits and 6 outputs no longer fit the default 620-unit window.
            Assert.That(_app.Shell.TruthTable.WindowWidth, Is.GreaterThan(620f));
            Assert.That(_app.Shell.TruthTable.Scroll.content.rect.width,
                Is.LessThanOrEqualTo(_app.Shell.TruthTable.Scroll.viewport.rect.width + 1f), "No sideways scrolling needed.");
        }
    }
}
