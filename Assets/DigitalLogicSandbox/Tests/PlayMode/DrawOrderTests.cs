using System;
using System.Collections;
using System.IO;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Placement;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Rendering.Gates;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>
    /// Workspace draw order: every gate renders as one unit (no interleaving when gates overlap), newer and dragged
    /// gates are in front, shadows stay under wires, the placement ghost is above all gates, and the screen-space
    /// empty-state hint steps aside while placing.
    /// </summary>
    public sealed class DrawOrderTests
    {
        private AppBootstrap _app;
        private WorkspaceController _workspace;
        private string _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            _app = AppBootstrap.Instance;
            _workspace = _app.Workspace;
            _workspace.WorkspaceCamera.Animate = false;
            _root = Path.Combine(Application.temporaryCachePath, "DrawOrderTest-" + Guid.NewGuid().ToString("N"));
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

        private GateView View(NodeId node)
        {
            Assert.That(_workspace.Presenter.TryGetGateView(node, out GateView view), Is.True);
            return view;
        }

        private (NodeId and, NodeId not) PlaceOverlapping()
        {
            NodeId and = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.And), Vector2.zero);
            NodeId not = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(1f, 0f));
            return (and, not);
        }

        [UnityTest]
        public IEnumerator Gates_RenderAsUnits_AndNewestOnTop()
        {
            (NodeId and, NodeId not) = PlaceOverlapping();
            yield return null;

            GateView andView = View(and);
            GateView notView = View(not);
            Assert.That(andView.GetComponent<SortingGroup>(), Is.Not.Null, "Each gate is one sorting group.");
            Assert.That(notView.GetComponent<SortingGroup>(), Is.Not.Null);
            Assert.That(notView.DrawOrder, Is.GreaterThan(andView.DrawOrder), "The newer gate is in front.");
            Assert.That(andView.DrawOrder, Is.GreaterThan(SortingOrders.SignalSpark), "Gates draw above wires.");

            Assert.That(andView.Shadow.transform.IsChildOf(andView.transform), Is.False,
                "The shadow is outside the gate's group, so it stays under the wires.");
            Assert.That(andView.Shadow.sortingOrder, Is.LessThan(SortingOrders.Wire));
            Assert.That(andView.Shadow.gameObject.activeInHierarchy, Is.True);
            Assert.That((Vector2)andView.Shadow.transform.localPosition,
                Is.EqualTo(new Vector2(0f, -0.14f)).Using(Vector2EqualityComparer.Instance), "The shadow follows its gate.");
        }

        [UnityTest]
        public IEnumerator Dragging_BringsGateToFront()
        {
            (NodeId and, NodeId not) = PlaceOverlapping();
            yield return null;

            _workspace.Presenter.BringToFront(new[] { and });

            Assert.That(View(and).DrawOrder, Is.GreaterThan(View(not).DrawOrder));
        }

        [UnityTest]
        public IEnumerator DeletedGate_HidesItsShadow()
        {
            (NodeId and, _) = PlaceOverlapping();
            yield return null;
            SpriteRenderer shadow = View(and).Shadow;

            _workspace.Selection.Select(and);
            _workspace.DeleteSelection();
            yield return null;

            Assert.That(shadow.gameObject.activeInHierarchy, Is.False, "A pooled gate's shadow is hidden with it.");
        }

        [UnityTest]
        public IEnumerator Ghost_IsAboveAllGates()
        {
            (NodeId and, NodeId not) = PlaceOverlapping();
            _workspace.BeginPlacement(new PlacementRequest(ComponentRef.Primitive(BuiltInComponentIds.And), "AND"), false);
            _workspace.UpdatePlacementGhost(new Vector2(0.5f, 0f), visible: true);
            yield return null;

            GateView ghost = null;
            foreach (GateView view in UnityEngine.Object.FindObjectsOfType<GateView>())
            {
                if (view.name == "Placement Ghost")
                {
                    ghost = view;
                }
            }

            Assert.That(ghost, Is.Not.Null);
            Assert.That(ghost.DrawOrder, Is.EqualTo(SortingOrders.Ghost));
            Assert.That(ghost.DrawOrder, Is.GreaterThan(View(and).DrawOrder));
            Assert.That(ghost.DrawOrder, Is.GreaterThan(View(not).DrawOrder));
            Assert.That(ghost.Shadow.gameObject.activeSelf, Is.False, "The ghost has no shadow.");
            Assert.That(SortingOrders.Ghost, Is.GreaterThan(SortingOrders.GateStackMax));
            _workspace.CancelPlacement();
        }

        [UnityTest]
        public IEnumerator EmptyState_HidesWhilePlacing()
        {
            yield return null;
            var hint = _app.Shell.EmptyState;
            Assert.That(hint.IsShown, Is.True, "An empty canvas shows the hint.");

            _workspace.BeginPlacement(new PlacementRequest(ComponentRef.Primitive(BuiltInComponentIds.Not), "NOT"), false);
            Assert.That(hint.IsShown, Is.False, "The screen-space hint steps aside for the placement ghost.");

            _workspace.CancelPlacement();
            Assert.That(hint.IsShown, Is.True, "It returns when placement is cancelled on an empty canvas.");
        }

        [UnityTest]
        public IEnumerator Stack_Renumbers_KeepingOrder()
        {
            (NodeId and, NodeId not) = PlaceOverlapping();
            yield return null;
            CircuitPresenter presenter = _workspace.Presenter;
            presenter.SetStackCounterForTests(SortingOrders.GateStackMax - SortingOrders.GateStackBase);

            presenter.BringToFront(new[] { and });

            Assert.That(View(and).DrawOrder, Is.LessThanOrEqualTo(SortingOrders.GateStackMax));
            Assert.That(View(not).DrawOrder, Is.LessThanOrEqualTo(SortingOrders.GateStackMax));
            Assert.That(View(and).DrawOrder, Is.GreaterThan(View(not).DrawOrder));
            Assert.That(presenter.TopStackOrder, Is.LessThan(SortingOrders.GateStackBase + 10), "The stack was renumbered.");
        }
    }
}
