using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Rendering.Wires;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>
    /// Wire crossings in the real Main scene: H runs straight along y = 0 and V comes down across it. Only V is
    /// decorated (hop or gap); the style comes from the Signal Settings popover and is saved with the project.
    /// </summary>
    public sealed class WireCrossingWorkspaceTests
    {
        private AppBootstrap _app;
        private WorkspaceController _workspace;
        private string _root;
        private NodeId _a;
        private NodeId _c;
        private PinRef _h;
        private PinRef _v;

        private static PinRef Pin(NodeId node)
        {
            return new PinRef(node, BoundarySignatures.Pin);
        }

        private IEnumerator LoadCrossing()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            _app = AppBootstrap.Instance;
            _workspace = _app.Workspace;
            _workspace.WorkspaceCamera.Animate = false;
            _workspace.AnimationSettings.Enabled = false;
            _root = Path.Combine(Application.temporaryCachePath, "WireCrossingTest-" + Guid.NewGuid().ToString("N"));
            _app.Projects.ProjectsDirectory = Path.Combine(_root, "Projects");
            _app.Projects.AutosaveDirectory = Path.Combine(_root, "Autosave");
            _app.Projects.RememberLastProject = false;
            _app.Projects.AutosaveIntervalSeconds = 0f;

            NodeId a = _a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(0f, 0f));
            NodeId y = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(20f, 0f));
            _c = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(6f, 4f));
            NodeId d = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(14f, -4f));
            Assert.That(_workspace.Connect(Pin(a), Pin(y)), Is.EqualTo(ConnectionError.None));
            Assert.That(_workspace.Connect(Pin(_c), Pin(d)), Is.EqualTo(ConnectionError.None));
            _h = Pin(y);
            _v = Pin(d);
            _workspace.Sync();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_workspace != null && _workspace.AnimationSettings != null)
            {
                _workspace.AnimationSettings.Enabled = true;
            }

            if (_root != null && Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private WireView Wire(PinRef destination)
        {
            Assert.That(_workspace.Presenter.TryGetWireView(destination, out WireView view), Is.True);
            return view;
        }

        private float RoutedLength(PinRef destination)
        {
            IReadOnlyList<Vector3> path = _workspace.Geometry.GetWirePath(destination);
            float length = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                length += Vector3.Distance(path[i - 1], path[i]);
            }

            return length;
        }

        [UnityTest]
        public IEnumerator HopIsTheDefault_AndOnlyTheVerticalWireHops()
        {
            yield return LoadCrossing();

            Assert.That(_workspace.WireCrossingStyle, Is.EqualTo(WireCrossingStyle.Hop));
            WireView v = Wire(_v);
            WireView h = Wire(_h);
            Assert.That(v.CrossingCount, Is.EqualTo(1));
            Assert.That(v.PointCount, Is.GreaterThan(_workspace.Geometry.GetWirePath(_v).Count), "The hop adds points.");
            Assert.That(v.Length, Is.GreaterThan(RoutedLength(_v) + 0.1f), "The arc is longer than the straight run.");
            Assert.That(h.CrossingCount, Is.Zero);
            Assert.That(h.PointCount, Is.EqualTo(2), "The horizontal wire stays straight.");
        }

        [UnityTest]
        public IEnumerator CloseCrossings_ShareOneBridge_OnTheirSide()
        {
            yield return LoadCrossing();
            var parent = new GameObject("Bridge Test");
            try
            {
                WireView wire = WireView.Create(parent.transform, _app.Theme);
                wire.SetPath(new List<Vector3> { new Vector3(0f, -4f, 0f), new Vector3(0f, 4f, 0f) });

                // Two crossings 0.25 apart (closer than a hop's diameter) on the +x side.
                var crossings = new List<Vector3> { new Vector3(0f, 0f, 1f), new Vector3(0f, 0.25f, 1f) };
                wire.SetCrossings(crossings, WireCrossingStyle.Hop);
                Assert.That(wire.PointCount, Is.EqualTo(2 + 10), "One bridge (two quarter arcs), not two hops.");
                AssertBridgeClears(wire, 1f, 0f, 0.25f);

                // The same crossings flagged for -x bulge the other way.
                crossings[0] = new Vector3(0f, 0f, -1f);
                crossings[1] = new Vector3(0f, 0.25f, -1f);
                wire.SetCrossings(crossings, WireCrossingStyle.Hop);
                AssertBridgeClears(wire, -1f, 0f, 0.25f);

                // Far-apart crossings still get a hop each.
                crossings[1] = new Vector3(0f, 2f, 1f);
                wire.SetCrossings(crossings, WireCrossingStyle.Hop);
                Assert.That(wire.PointCount, Is.EqualTo(2 + 20));

                // Gap style: close crossings make one longer gap (the segment is drawn in two pieces).
                crossings[1] = new Vector3(0f, 0.25f, 1f);
                wire.SetCrossings(crossings, WireCrossingStyle.Gap);
                Assert.That(wire.QuadCount, Is.EqualTo(2));
            }
            finally
            {
                Object.Destroy(parent);
            }
        }

        /// <summary>Every drawn point level with a crossed wire lies beside the wire, on <paramref name="side"/>.</summary>
        private static void AssertBridgeClears(WireView wire, float side, params float[] crossedY)
        {
            foreach (Vector3 point in wire.DrawnPoints)
            {
                Assert.That(point.x * side, Is.GreaterThanOrEqualTo(-1e-4f), "The bridge stays on its side.");
            }

            foreach (float y in crossedY)
            {
                bool covered = false;
                IReadOnlyList<Vector3> points = wire.DrawnPoints;
                for (int i = 1; i < points.Count; i++)
                {
                    Vector3 a = points[i - 1];
                    Vector3 b = points[i];
                    if ((a.y - y) * (b.y - y) <= 0f && a.y != b.y)
                    {
                        float x = Mathf.Lerp(a.x, b.x, (y - a.y) / (b.y - a.y));
                        Assert.That(Mathf.Abs(x), Is.GreaterThan(0.1f), "No drawn point touches the wire crossed at y = " + y);
                        covered = true;
                    }
                }

                Assert.That(covered, Is.True);
            }
        }

        [UnityTest]
        public IEnumerator GapAndPlainStyles_ChangeOnlyTheDrawing()
        {
            yield return LoadCrossing();
            WireView v = Wire(_v);
            int routedPoints = _workspace.Geometry.GetWirePath(_v).Count;

            _workspace.WireCrossingStyle = WireCrossingStyle.Gap;
            Assert.That(v.CrossingStyle, Is.EqualTo(WireCrossingStyle.Gap));
            Assert.That(v.PointCount, Is.EqualTo(routedPoints), "A gap does not change the path.");
            Assert.That(v.Length, Is.EqualTo(RoutedLength(_v)).Within(1e-3f));
            Assert.That(v.QuadCount, Is.EqualTo(routedPoints), "The crossed segment is drawn in two pieces.");

            _workspace.WireCrossingStyle = WireCrossingStyle.Plain;
            Assert.That(v.CrossingCount, Is.Zero);
            Assert.That(v.PointCount, Is.EqualTo(routedPoints));
            Assert.That(v.QuadCount, Is.EqualTo(routedPoints - 1));
            Assert.That(_workspace.Geometry.GetWirePath(_v).Count, Is.EqualTo(routedPoints),
                "Routing (and hit-testing) never sees the drawing style.");
        }

        [UnityTest]
        public IEnumerator SignalTravelsOverTheHop_AndSettlesOnTheRightValue()
        {
            yield return LoadCrossing();
            _workspace.AnimationSettings.Enabled = true;
            WireView v = Wire(_v);

            _workspace.SetInput(_c, Signal.High);
            _workspace.Sync();
            Assert.That(_workspace.SignalAnimation.IsAnimating, Is.True);
            _workspace.SignalAnimation.Tick(0.05f);
            Assert.That(v.InTransition, Is.True);

            for (int i = 0; i < 400 && _workspace.SignalAnimation.IsAnimating; i++)
            {
                _workspace.SignalAnimation.Tick(0.05f);
            }

            Assert.That(_workspace.SignalAnimation.IsAnimating, Is.False);
            Assert.That(v.InTransition, Is.False);
            Assert.That(v.Signal, Is.EqualTo(Signal.High));
            Assert.That(v.CrossingCount, Is.EqualTo(1), "Signal changes keep the hop.");
        }

        [UnityTest]
        public IEnumerator MovingAGate_UpdatesTheCrossings()
        {
            yield return LoadCrossing();
            Assert.That(Wire(_v).CrossingCount, Is.EqualTo(1));

            // Move H's input far above V's source: H no longer crosses V.
            _workspace.Circuit.MoveNode(_h.Node, new CircuitPoint(20f, 12f));
            _workspace.Circuit.MoveNode(_a, new CircuitPoint(0f, 12f));
            _workspace.Sync();
            yield return null;

            Assert.That(Wire(_v).CrossingCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PopoverButtons_SetTheStyle_WhichSurvivesSaveAndOpen()
        {
            yield return LoadCrossing();
            var settings = _app.Shell.SignalSettings;
            settings.Toggle();
            settings.CrossingButton(WireCrossingStyle.Gap).onClick.Invoke();
            Assert.That(_workspace.WireCrossingStyle, Is.EqualTo(WireCrossingStyle.Gap));
            Assert.That(Wire(_v).CrossingStyle, Is.EqualTo(WireCrossingStyle.Gap));
            settings.Toggle();

            string path = Path.Combine(_root, "crossings" + DigitalLogicSandbox.Persistence.ProjectFormat.FileExtension);
            Assert.That(_app.Projects.SaveAs(path, out string message), Is.True, message);
            _app.Projects.NewProject();
            yield return null;
            Assert.That(_workspace.WireCrossingStyle, Is.EqualTo(WireCrossingStyle.Hop), "A new project uses the default.");

            Assert.That(_app.Projects.Open(path, out message), Is.True, message);
            yield return null;
            Assert.That(_workspace.WireCrossingStyle, Is.EqualTo(WireCrossingStyle.Gap));
            Assert.That(Wire(_v).CrossingStyle, Is.EqualTo(WireCrossingStyle.Gap));
            Assert.That(Wire(_v).CrossingCount, Is.EqualTo(1));
        }
    }
}
