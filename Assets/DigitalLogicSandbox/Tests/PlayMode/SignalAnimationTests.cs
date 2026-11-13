using System.Collections;
using System.Collections.Generic;
using DigitalLogicSandbox.Animation.Controllers;
using DigitalLogicSandbox.Animation.Signals;
using DigitalLogicSandbox.App;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.Rendering.Wires;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using Object = UnityEngine.Object;

namespace DigitalLogicSandbox.Tests.PlayMode
{
    /// <summary>
    /// Animated signal propagation. The animation is advanced with explicit ticks (no frames are yielded), so
    /// timings are deterministic. Every test also checks that logical values never depend on the animation.
    /// </summary>
    public sealed class SignalAnimationTests
    {
        private const float Long = 10f;

        private GameObject _cameraObject;
        private WorkspaceController _workspace;
        private SignalAnimationSettings _settings;
        private NodeId _a;
        private NodeId _not;
        private NodeId _out;

        [SetUp]
        public void SetUp()
        {
            _cameraObject = new GameObject("Test Camera");
            var camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            var library = new DefinitionLibrary();
            _settings = new SignalAnimationSettings();
            _workspace = new GameObject("Workspace").AddComponent<WorkspaceController>();
            _workspace.Initialize(new WorkspaceSetup
            {
                Catalog = new ComponentCatalog(PrimitiveRegistry.CreateDefault(), library),
                Library = library,
                Theme = VisualTheme.CreateRuntimeDefault(),
                Camera = camera,
                IsPointerOverUi = () => false,
                IsTextInputFocused = () => false,
                SignalAnimation = _settings,
            });
            _workspace.WorkspaceCamera.Animate = false;
            _workspace.LoadCircuit(new CircuitDefinition(DefinitionId.New(), "Main"));

            // A ──► NOT ──► OUT
            _a = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, 0));
            _not = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), Vector2.zero);
            _out = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(6, 0));
            _workspace.Connect(Pin(_a), new PinRef(_not, NotGate.Input));
            _workspace.Connect(new PinRef(_not, NotGate.Output), Pin(_out));
            _workspace.Sync();
            Animator.CompleteAll();
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_workspace.gameObject);
            Object.Destroy(_cameraObject);
        }

        private SignalAnimationController Animator => _workspace.SignalAnimation;

        private static PinRef Pin(NodeId boundary)
        {
            return new PinRef(boundary, BoundarySignatures.Pin);
        }

        private WireView Wire(PinRef destination)
        {
            Assert.That(_workspace.Presenter.TryGetWireView(destination, out WireView view), Is.True);
            return view;
        }

        private WireView InputWire => Wire(new PinRef(_not, NotGate.Input));

        private WireView OutputWire => Wire(Pin(_out));

        private Signal Lamp
        {
            get
            {
                Assert.That(_workspace.Presenter.TryGetGateView(_out, out GateView view), Is.True);
                return view.StateSignal;
            }
        }

        /// <summary>Flips an input and lets the workspace hand the change log to the animator (no frame passes).</summary>
        private void Toggle(NodeId input)
        {
            Assert.That(_workspace.ToggleInput(input), Is.True);
            _workspace.Sync();
        }

        private void TickFor(float seconds, float step = 1f / 60f)
        {
            for (float t = 0f; t < seconds; t += step)
            {
                Animator.Tick(step);
            }
        }

        /// <summary>Every wire view shows the simulation's value of its net.</summary>
        private void AssertViewsMatchSimulation()
        {
            foreach (ConnectionDefinition connection in _workspace.Circuit.Connections)
            {
                int net = _workspace.Presenter.GetWireNet(connection.Destination);
                WireView view = Wire(connection.Destination);
                Assert.That(view.InTransition, Is.False, "Transition left over on " + connection);
                Assert.That(view.Signal, Is.EqualTo(_workspace.Runner.Context.GetNet(net)), connection.ToString());
            }
        }

        [Test]
        public void LowToHigh_SimulationIsImmediate_WiresAnimateFromTheSource()
        {
            Assert.That(Lamp, Is.EqualTo(Signal.High), "NOT 0 = 1 initially.");
            Toggle(_a);

            Assert.That(_workspace.Session.GetOutputValue(_out), Is.EqualTo(Signal.Low), "Logic is already settled.");
            Assert.That(Animator.IsAnimating, Is.True);
            Assert.That(InputWire.Signal, Is.EqualTo(Signal.Low), "Nothing is drawn before the first tick.");

            Animator.Tick(0.05f);
            Assert.That(InputWire.InTransition, Is.True);
            Assert.That(InputWire.TransitionProgress, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(Animator.ActiveParticleCount, Is.EqualTo(1), "One particle per travelling front.");
            Assert.That(Lamp, Is.EqualTo(Signal.High), "Downstream views still show the old value.");

            TickFor(Long);
            Assert.That(Animator.IsAnimating, Is.False);
            Assert.That(Animator.ActiveParticleCount, Is.Zero);
            Assert.That(InputWire.Signal, Is.EqualTo(Signal.High));
            Assert.That(OutputWire.Signal, Is.EqualTo(Signal.Low));
            Assert.That(Lamp, Is.EqualTo(Signal.Low));
            AssertViewsMatchSimulation();
        }

        [Test]
        public void HighToLow_AnimatesBackAndSettles()
        {
            Toggle(_a);
            TickFor(Long);
            Toggle(_a);
            Animator.Tick(0.05f);

            Assert.That(InputWire.InTransition, Is.True);
            Assert.That(InputWire.Signal, Is.EqualTo(Signal.High), "Leaving HIGH.");
            TickFor(Long);
            Assert.That(Lamp, Is.EqualTo(Signal.High));
            AssertViewsMatchSimulation();
        }

        [Test]
        public void Waves_AnimateInLogicalPropagationOrder()
        {
            Toggle(_a);
            Animator.Tick(0.02f);
            Assert.That(InputWire.InTransition, Is.True, "Wave 0: the input wire moves first.");
            Assert.That(OutputWire.InTransition, Is.False, "Wave 1 waits for wave 0 to arrive.");
            Assert.That(OutputWire.Signal, Is.EqualTo(Signal.High));

            float firstLeg = InputWire.Length / _settings.UnitsPerSecond;
            TickFor(firstLeg + _settings.WaveGap + 0.02f, 0.01f);
            Assert.That(InputWire.InTransition, Is.False, "Wave 0 has arrived.");
            Assert.That(InputWire.Signal, Is.EqualTo(Signal.High));
            Assert.That(OutputWire.InTransition, Is.True, "Then the NOT's output travels.");
        }

        [Test]
        public void FanOut_AllWiresOfANetTravelTogether()
        {
            NodeId second = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(0, -4));
            _workspace.Connect(Pin(_a), new PinRef(second, NotGate.Input));
            _workspace.Sync();
            Animator.CompleteAll();

            Toggle(_a);
            Animator.Tick(0.05f);
            WireView branch = Wire(new PinRef(second, NotGate.Input));
            Assert.That(InputWire.InTransition && branch.InTransition, Is.True);
            Assert.That(Animator.ActiveParticleCount, Is.EqualTo(2));
            TickFor(Long);
            AssertViewsMatchSimulation();
        }

        [Test]
        public void SimultaneousChanges_AnimateTogether()
        {
            NodeId b = _workspace.PlaceNode(ComponentRef.InputBoundary, new Vector2(-6, -4));
            NodeId lamp = _workspace.PlaceNode(ComponentRef.OutputBoundary, new Vector2(6, -4));
            _workspace.Connect(Pin(b), Pin(lamp));
            _workspace.Sync();
            Animator.CompleteAll();

            Assert.That(_workspace.ToggleInput(_a), Is.True);
            Assert.That(_workspace.ToggleInput(b), Is.True);
            _workspace.Sync();
            Animator.Tick(0.05f);

            Assert.That(InputWire.InTransition, Is.True);
            Assert.That(Wire(Pin(lamp)).InTransition, Is.True);
            TickFor(Long);
            AssertViewsMatchSimulation();
        }

        [Test]
        public void RapidToggling_NeverLeavesStaleVisuals()
        {
            for (int i = 0; i < 7; i++)
            {
                Toggle(_a);
                Animator.Tick(0.03f);
            }

            Assert.That(Animator.PulseCount, Is.LessThanOrEqualTo(2), "Superseded pulses are completed, not queued.");
            TickFor(Long);
            Assert.That(Animator.IsAnimating, Is.False);
            Assert.That(Animator.CreatedParticleCount, Is.LessThanOrEqualTo(4), "Particles are pooled.");
            Assert.That(_workspace.Session.GetOutputValue(_out), Is.EqualTo(Signal.Low), "Odd number of toggles.");
            AssertViewsMatchSimulation();
        }

        [Test]
        public void AnimationDisabled_ViewsUpdateInstantly()
        {
            _settings.Enabled = false;
            Toggle(_a);

            Assert.That(Animator.IsAnimating, Is.False);
            Assert.That(Lamp, Is.EqualTo(Signal.Low));
            AssertViewsMatchSimulation();
        }

        [Test]
        public void DisablingMidAnimation_CompletesToTheTrueState()
        {
            Toggle(_a);
            Animator.Tick(0.05f);
            _settings.Enabled = false;

            Assert.That(Animator.IsAnimating, Is.False);
            AssertViewsMatchSimulation();
        }

        [Test]
        public void AnimationNeverChangesLogicalValues()
        {
            var animated = new List<Signal>();
            for (int i = 0; i < 6; i++)
            {
                Toggle(_a);
                Animator.Tick(0.01f * i);
                animated.Add(_workspace.Session.GetOutputValue(_out));
            }

            _settings.Enabled = false;
            _workspace.ResetSimulation();
            _workspace.Sync();
            var instant = new List<Signal>();
            for (int i = 0; i < 6; i++)
            {
                Toggle(_a);
                instant.Add(_workspace.Session.GetOutputValue(_out));
            }

            Assert.That(animated, Is.EqualTo(instant));
        }

        [Test]
        public void ResetVisualState_DropsPulsesAndShowsTrueValues()
        {
            Toggle(_a);
            Animator.Tick(0.05f);
            _workspace.ResetVisualState();

            Assert.That(Animator.IsAnimating, Is.False);
            Assert.That(Animator.ActiveParticleCount, Is.Zero);
            AssertViewsMatchSimulation();
        }

        [Test]
        public void DeepCircuits_AreCompressedIntoTheBatchLimit()
        {
            NodeId previous = _not;
            _workspace.DeleteWire(Pin(_out));
            for (int i = 0; i < 24; i++)
            {
                NodeId next = _workspace.PlaceNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new Vector2(4 + i * 4, 0));
                _workspace.Connect(new PinRef(previous, NotGate.Output), new PinRef(next, NotGate.Input));
                previous = next;
            }

            _workspace.Connect(new PinRef(previous, NotGate.Output), Pin(_out));
            _workspace.Sync();
            Animator.CompleteAll();
            _settings.MaxBatchSeconds = 0.5f;

            Toggle(_a);
            TickFor(0.55f, 0.01f);
            Assert.That(Animator.IsAnimating, Is.False, "25 waves fit into the configured batch duration.");
            AssertViewsMatchSimulation();
        }

        [Test]
        public void StructuralEdit_DuringAnimation_IsSafe()
        {
            Toggle(_a);
            Animator.Tick(0.05f);
            _workspace.DeleteWire(new PinRef(_not, NotGate.Input));
            _workspace.Sync(); // recompiles: full refresh drops the pulses

            Assert.That(Animator.IsAnimating, Is.False);
            TickFor(1f);
            AssertViewsMatchSimulation();
        }

        [Test]
        public void TickingActiveAnimations_DoesNotAllocate()
        {
            _settings.Speed = SignalAnimationSettings.MinSpeed;
            Toggle(_a);
            TickFor(0.2f);
            Toggle(_a);
            TickFor(0.1f); // warm-up: pools, gradients and curves exist now
            Assert.That(Animator.IsAnimating, Is.True);

            Assert.That(() =>
            {
                for (int i = 0; i < 30; i++)
                {
                    Animator.Tick(0.001f);
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.That(Animator.IsAnimating, Is.True, "Still animating, so the ticks did real work.");
        }

        [Test]
        public void AllocationMeasurement_DetectsAllocations()
        {
            // Control for the test above: the constraint really measures managed allocations here.
            Assert.That(() => { _sink = new int[64]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory());
        }

        private static object _sink;

        [Test]
        public void VisualizationModes_DistinguishStatesByWidthAsWellAsColour()
        {
            Toggle(_a);
            TickFor(Long);
            float standardHigh = InputWire.Line.widthMultiplier;
            float standardLow = OutputWire.Line.widthMultiplier;
            Assert.That(standardHigh, Is.GreaterThan(standardLow), "Not colour alone: HIGH is wider.");

            _workspace.VisualizationMode = SignalVisualizationMode.HighContrast;
            Assert.That(InputWire.Line.widthMultiplier, Is.GreaterThan(standardHigh));
            Assert.That(OutputWire.Line.widthMultiplier, Is.LessThan(standardLow));
            AssertViewsMatchSimulation();
        }

        [Test]
        public void Settings_ClampSpeedAndScaleTimings()
        {
            _settings.Speed = 100f;
            Assert.That(_settings.Speed, Is.EqualTo(SignalAnimationSettings.MaxSpeed));
            _settings.Speed = 0f;
            Assert.That(_settings.Speed, Is.EqualTo(SignalAnimationSettings.MinSpeed));
            _settings.Speed = 2f;
            Assert.That(_settings.UnitsPerSecond, Is.EqualTo(SignalAnimationSettings.BaseUnitsPerSecond * 2f));
            Assert.That(_settings.WaveGap, Is.EqualTo(SignalAnimationSettings.BaseWaveGap / 2f));
        }
    }

    /// <summary>The Signals popup in the real Main scene.</summary>
    public sealed class SignalSettingsUiTests
    {
        [UnityTest]
        public IEnumerator SignalsPanel_ControlsTheSharedSettings()
        {
            yield return SceneManager.LoadSceneAsync("Main", LoadSceneMode.Single);
            yield return null;
            AppBootstrap app = AppBootstrap.Instance;
            SignalAnimationSettings settings = app.Workspace.AnimationSettings;
            Assert.That(settings.Enabled, Is.True, "The application animates signals by default.");

            app.Shell.SignalSettings.Toggle();
            Assert.That(app.Shell.SignalSettings.IsOpen, Is.True);
            app.Shell.SignalSettings.FasterButton.onClick.Invoke();
            Assert.That(settings.Speed, Is.EqualTo(2f));
            Assert.That(app.Shell.SignalSettings.SpeedText, Is.EqualTo("Speed 2x"));

            app.Shell.SignalSettings.HighContrastToggle.isOn = true;
            Assert.That(app.Workspace.VisualizationMode, Is.EqualTo(SignalVisualizationMode.HighContrast));
            app.Shell.SignalSettings.EnabledToggle.isOn = false;
            Assert.That(settings.Enabled, Is.False);
            app.Shell.SignalSettings.Toggle();
            Assert.That(app.Shell.SignalSettings.IsOpen, Is.False);
        }
    }
}
