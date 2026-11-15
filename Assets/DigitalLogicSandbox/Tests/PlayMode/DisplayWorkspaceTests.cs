using System;
using System.Collections;
using System.IO;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Rendering.Displays;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.UI.Panels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>
    /// Phase D in the real Main scene: display components in the library, LEDs, seven-segment, binary, hex and
    /// decimal displays following live signals (with and without animation), and persistence of display projects.
    /// </summary>
    public sealed class DisplayWorkspaceTests
    {
        private AppBootstrap _app;
        private WorkspaceController _workspace;
        private string _root;

        private static PinRef Pin(NodeId node)
        {
            return new PinRef(node, BoundarySignatures.Pin);
        }

        private IEnumerator LoadMain(bool animate = false)
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            _app = AppBootstrap.Instance;
            _workspace = _app.Workspace;
            _workspace.WorkspaceCamera.Animate = false;
            _workspace.AnimationSettings.Enabled = animate;
            _root = Path.Combine(Application.temporaryCachePath, "DisplayWorkspaceTest-" + Guid.NewGuid().ToString("N"));
            _app.Projects.ProjectsDirectory = Path.Combine(_root, "Projects");
            _app.Projects.AutosaveDirectory = Path.Combine(_root, "Autosave");
            _app.Projects.RememberLastProject = false;
            _app.Projects.AutosaveIntervalSeconds = 0f;
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

        private GateView View(NodeId node)
        {
            Assert.That(_workspace.Presenter.TryGetGateView(node, out GateView view), Is.True);
            return view;
        }

        [UnityTest]
        public IEnumerator Library_OffersDisplays_FollowingTheBusWidth()
        {
            yield return LoadMain();
            LibraryPanel library = _app.Shell.Library;
            library.SetBusParameters(8, 2);
            int found = 0;
            int primitives = 0;
            foreach (LibraryItemView item in library.Items)
            {
                ComponentRef component = item.Request.Component;
                found += component == ComponentRef.Display(DisplayStyle.Led) ? 1 : 0;
                found += component == ComponentRef.Display(DisplayStyle.SevenSegment) ? 1 : 0;
                found += component == ComponentRef.Display(DisplayStyle.Binary, 8) ? 1 : 0;
                found += component == ComponentRef.Display(DisplayStyle.Hex, 8) ? 1 : 0;
                found += component == ComponentRef.Display(DisplayStyle.Decimal, 8) ? 1 : 0;
                found += component == ComponentRef.Display(DisplayStyle.SignedDecimal, 8) ? 1 : 0;
                primitives += component.Kind == ComponentKind.Primitive ? 1 : 0;
            }

            Assert.That(found, Is.EqualTo(6));
            Assert.That(primitives, Is.EqualTo(2), "Built-in gates remain exactly AND and NOT.");
        }

        [UnityTest]
        public IEnumerator BusDisplays_ShowTheLiveValue()
        {
            yield return LoadMain();
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBus(8), new Vector2(-10, 0));
            NodeId hex = _workspace.PlaceNode(ComponentRef.Display(DisplayStyle.Hex, 8), new Vector2(0, 6));
            NodeId dec = _workspace.PlaceNode(ComponentRef.Display(DisplayStyle.Decimal, 8), new Vector2(0, 2));
            NodeId signed = _workspace.PlaceNode(ComponentRef.Display(DisplayStyle.SignedDecimal, 8), new Vector2(0, -2));
            NodeId bin = _workspace.PlaceNode(ComponentRef.Display(DisplayStyle.Binary, 8), new Vector2(0, -6));
            foreach (NodeId display in new[] { hex, dec, signed, bin })
            {
                Assert.That(_workspace.Connect(Pin(a), Pin(display)), Is.EqualTo(ConnectionError.None));
            }

            _workspace.Sync();
            Assert.That(_workspace.Session.IsSimulating, Is.True, _workspace.Session.Report.ToString());
            Assert.That(_workspace.Session.Report.HasErrors, Is.False);
            yield return null;
            Assert.That(View(dec).DisplayText, Is.EqualTo("0"));

            Assert.That(_workspace.SetInputValue(a, 0xA5), Is.True);
            _workspace.Sync();
            yield return null;

            Assert.That(View(hex).IsDisplay, Is.True);
            Assert.That(View(hex).DisplayText, Is.EqualTo("A5"));
            Assert.That(View(hex).GetDisplayDigit(0), Is.EqualTo(DisplayEncoding.Glyph(0xA)));
            Assert.That(View(dec).DisplayText, Is.EqualTo("165"));
            Assert.That(View(signed).DisplayText, Is.EqualTo("-91"));
            Assert.That(View(bin).DisplayText, Is.EqualTo("10100101"));
            Assert.That(View(bin).GetDisplayInput(0), Is.EqualTo(Signal.High));
            Assert.That(View(bin).GetDisplayInput(1), Is.EqualTo(Signal.Low));

            // Undo restores the previous value on every display.
            Assert.That(_workspace.Undo(), Is.True);
            _workspace.Sync();
            yield return null;
            Assert.That(View(dec).DisplayText, Is.EqualTo("0"));
        }

        [UnityTest]
        public IEnumerator ChangingDisplayStyleAndBusWidth_AfterPlacement()
        {
            yield return LoadMain();
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBus(8), new Vector2(-10, 0));
            NodeId display = _workspace.PlaceNode(ComponentRef.Display(DisplayStyle.Hex, 8), new Vector2(0, 0));
            Assert.That(_workspace.Connect(Pin(a), Pin(display)), Is.EqualTo(ConnectionError.None));
            Assert.That(_workspace.SetInputValue(a, 0xA5), Is.True);
            _workspace.Sync();
            yield return null;
            Assert.That(View(display).DisplayText, Is.EqualTo("A5"));

            // Style change through the workspace: same node, connection kept, new readout.
            Assert.That(_workspace.ChangeComponent(display, -1, -1, (int)DisplayStyle.Decimal), Is.True);
            _workspace.Sync();
            yield return null;
            Assert.That(_workspace.Circuit.IsDriven(Pin(display)), Is.True);
            Assert.That(View(display).DisplayText, Is.EqualTo("165"));

            // Style change through the inspector's style buttons.
            _workspace.Selection.Select(display);
            _app.Shell.Inspector.Rebuild();
            UnityEngine.UI.Button binary = null;
            foreach (UnityEngine.UI.Button button in _app.Shell.Inspector.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (button.name == "Style " + DisplayStyle.Binary)
                {
                    binary = button;
                }
            }

            Assert.That(binary, Is.Not.Null, "The inspector offers a style picker for displays.");
            binary.onClick.Invoke();
            _workspace.Sync();
            yield return null;
            Assert.That(View(display).DisplayText, Is.EqualTo("10100101"));

            // Narrowing the input bus drops the 8-bit connection; undo brings it back.
            Assert.That(_workspace.ChangeComponent(a, 4), Is.True);
            _workspace.Sync();
            yield return null;
            Assert.That(_workspace.Circuit.IsDriven(Pin(display)), Is.False);
            Assert.That(_workspace.Undo(), Is.True);
            Assert.That(_workspace.Undo(), Is.True);
            Assert.That(_workspace.Undo(), Is.True);
            _workspace.Sync();
            yield return null;
            Assert.That(_workspace.Circuit.GetNode(display).Component, Is.EqualTo(ComponentRef.Display(DisplayStyle.Hex, 8)));
            Assert.That(_workspace.Circuit.GetNode(a).Component, Is.EqualTo(ComponentRef.InputBus(8)));
            Assert.That(_workspace.Circuit.IsDriven(Pin(display)), Is.True);
            Assert.That(View(display).DisplayText, Is.EqualTo("A5"));
        }

        [UnityTest]
        public IEnumerator LedAndSevenSegment_FollowSingleBitInputs()
        {
            yield return LoadMain();
            NodeId x = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-10, 2));
            NodeId y = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-10, -2));
            NodeId led = _workspace.PlaceNode(ComponentRef.Display(DisplayStyle.Led), new Vector2(-2, 6));
            NodeId seg = _workspace.PlaceNode(ComponentRef.Display(DisplayStyle.SevenSegment), new Vector2(2, 0));
            Assert.That(_workspace.Connect(Pin(x), Pin(led)), Is.EqualTo(ConnectionError.None));
            Assert.That(_workspace.Connect(Pin(x), new PinRef(seg, BoundarySignatures.BitPin(1))), Is.EqualTo(ConnectionError.None)); // b
            Assert.That(_workspace.Connect(Pin(y), new PinRef(seg, BoundarySignatures.BitPin(2))), Is.EqualTo(ConnectionError.None)); // c
            _workspace.Sync();
            yield return null;
            Assert.That(View(led).DisplayText, Is.EqualTo("OFF"));
            Assert.That(View(seg).DisplayText, Is.EqualTo("blank"));

            _workspace.ToggleInput(x);
            _workspace.ToggleInput(y);
            _workspace.Sync();
            yield return null;
            Assert.That(View(led).DisplayText, Is.EqualTo("ON"));
            Assert.That(View(seg).DisplayText, Is.EqualTo("b c"));
            Assert.That(View(seg).GetDisplayDigit(0), Is.EqualTo(DisplayEncoding.Glyph(1)), "b and c draw a '1'.");

            // Unused segment inputs are not reported as problems.
            Assert.That(_workspace.Session.Report.Count(Core.Validation.DiagnosticCode.UnconnectedInput), Is.Zero);
        }

        [UnityTest]
        public IEnumerator DisplaysUpdate_WhenTheAnimatedSignalArrives()
        {
            yield return LoadMain(animate: true);
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBus(4), new Vector2(-10, 0));
            NodeId hex = _workspace.PlaceNode(ComponentRef.Display(DisplayStyle.Hex, 4), new Vector2(4, 0));
            _workspace.Connect(Pin(a), Pin(hex));
            _workspace.Sync();
            yield return null;
            _workspace.SetInputValue(a, 12);
            _workspace.Sync();
            _workspace.SignalAnimation.CompleteAll();
            yield return null;
            Assert.That(View(hex).DisplayText, Is.EqualTo("C"));
        }

        [UnityTest]
        public IEnumerator DisplayProject_SurvivesSaveAndOpen()
        {
            yield return LoadMain();
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBus(6), new Vector2(-10, 0));
            NodeId dec = _workspace.PlaceNode(ComponentRef.Display(DisplayStyle.Decimal, 6), new Vector2(0, 0));
            _workspace.Connect(Pin(a), Pin(dec));
            _workspace.SetInputValue(a, 42);
            _workspace.Sync();
            string path = Path.Combine(_root, "displays" + DigitalLogicSandbox.Persistence.ProjectFormat.FileExtension);
            Assert.That(_app.Projects.SaveAs(path, out string message), Is.True, message);
            _app.Projects.NewProject();
            yield return null;
            Assert.That(_app.Projects.Open(path, out message), Is.True, message);
            yield return null;

            Assert.That(_workspace.Circuit.TryGetNode(dec, out NodeDefinition node), Is.True);
            Assert.That(node.Component, Is.EqualTo(ComponentRef.Display(DisplayStyle.Decimal, 6)));
            Assert.That(View(dec).DisplayText, Is.EqualTo("42"));
        }

        [UnityTest]
        public IEnumerator LedColour_FromTheInspector_SurvivesSaveAndOpen()
        {
            yield return LoadMain();
            NodeId a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, 0));
            NodeId led = _workspace.PlaceNode(ComponentRef.Display(DisplayStyle.Led), new Vector2(0, 0));
            Assert.That(_workspace.Connect(Pin(a), Pin(led)), Is.EqualTo(ConnectionError.None));
            _workspace.SetInput(a, Signal.High);
            _workspace.Sync();
            yield return null;
            Assert.That(View(led).DisplayText, Is.EqualTo("ON"));

            _workspace.Selection.Select(led);
            _app.Shell.Inspector.Rebuild();
            UnityEngine.UI.Button blue = null;
            foreach (UnityEngine.UI.Button button in _app.Shell.Inspector.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (button.name == "Colour " + LedColors.ToId(4))
                {
                    blue = button;
                }
            }

            Assert.That(blue, Is.Not.Null, "The inspector offers LED colour swatches.");
            blue.onClick.Invoke();
            _workspace.Sync();
            yield return null;
            Assert.That(_workspace.Circuit.GetNode(led).Component.LedColor, Is.EqualTo(4));
            Assert.That(View(led).Geometry.LedColor, Is.EqualTo(4));
            Assert.That(_workspace.Circuit.IsDriven(Pin(led)), Is.True, "Changing colour keeps the wire.");
            Assert.That(View(led).DisplayText, Is.EqualTo("ON"), "Colour never changes the logic.");

            string path = Path.Combine(_root, "leds" + DigitalLogicSandbox.Persistence.ProjectFormat.FileExtension);
            Assert.That(_app.Projects.SaveAs(path, out string message), Is.True, message);
            _app.Projects.NewProject();
            yield return null;
            Assert.That(_app.Projects.Open(path, out message), Is.True, message);
            yield return null;
            Assert.That(_workspace.Circuit.GetNode(led).Component.LedColor, Is.EqualTo(4));

            Assert.That(_workspace.ChangeLedColor(led, 0), Is.True);
            Assert.That(_workspace.Undo(), Is.True);
            Assert.That(_workspace.Circuit.GetNode(led).Component.LedColor, Is.EqualTo(4));
        }
    }
}
