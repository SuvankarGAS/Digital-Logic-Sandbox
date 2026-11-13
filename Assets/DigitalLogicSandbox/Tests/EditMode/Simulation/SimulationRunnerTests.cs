using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.SimulationTestUtil;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.Simulation
{
    public sealed class SimulationRunnerTests
    {
        private TestEnvironment _env;
        private SimulationRunner _runner;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
            _runner = new SimulationRunner();
        }

        [Test]
        public void Running_PropagatesImmediately()
        {
            _runner.Load(Compile(_env, BuildNand()));

            _runner.SetInput(0, Signal.High);
            _runner.SetInput(1, Signal.High);

            Assert.That(_runner.Context.GetOutput(0), Is.EqualTo(Signal.Low));
            Assert.That(_runner.EvaluationCount, Is.GreaterThan(0));
        }

        [Test]
        public void Paused_DefersPropagationUntilStepOrPlay()
        {
            _runner.Load(Compile(_env, BuildNand()));
            _runner.Pause();

            _runner.SetInput(0, Signal.High);
            _runner.SetInput(1, Signal.High);
            Assert.That(_runner.Context.GetOutput(0), Is.EqualTo(Signal.High), "Nothing propagated yet.");
            Assert.That(_runner.HasPendingWork, Is.True);

            Assert.That(_runner.Step(), Is.True, "AND evaluated; NOT still pending.");
            Assert.That(_runner.Step(), Is.False, "NOT evaluated; stable.");
            Assert.That(_runner.Context.GetOutput(0), Is.EqualTo(Signal.Low));
        }

        [Test]
        public void Play_FlushesPendingWork()
        {
            _runner.Load(Compile(_env, BuildNand()));
            _runner.Pause();
            _runner.SetInput(0, Signal.High);
            _runner.SetInput(1, Signal.High);

            _runner.Play();

            Assert.That(_runner.State, Is.EqualTo(SimulationState.Running));
            Assert.That(_runner.Context.GetOutput(0), Is.EqualTo(Signal.Low));
        }

        [Test]
        public void ToggleInput_FlipsValue()
        {
            _runner.Load(Compile(_env, BuildNand()));

            _runner.ToggleInput(0);
            Assert.That(_runner.Context.GetInput(0), Is.EqualTo(Signal.High));
            _runner.ToggleInput(0);
            Assert.That(_runner.Context.GetInput(0), Is.EqualTo(Signal.Low));
        }

        [Test]
        public void Reset_ClearsInputs()
        {
            _runner.Load(Compile(_env, BuildNand()));
            _runner.SetInput(0, Signal.High);
            _runner.SetInput(1, Signal.High);

            _runner.Reset();

            Assert.That(_runner.Context.GetInput(0), Is.EqualTo(Signal.Low));
            Assert.That(_runner.Context.GetOutput(0), Is.EqualTo(Signal.High));
        }

        [Test]
        public void Load_AfterEdit_PreservesInputValuesByPortId()
        {
            CircuitDefinition circuit = BuildNand();
            _runner.Load(Compile(_env, circuit));
            _runner.SetInput(0, Signal.High);
            _runner.SetInput(1, Signal.High);
            int loads = 0;
            _runner.Loaded += () => loads++;

            // Edit: add a new input at the front of the interface order; existing inputs keep their port ids.
            circuit.AddInputBoundary("C", CircuitPoint.Zero);
            circuit.MoveInterfacePort(circuit.Inputs[2].Id, 0);
            _runner.Load(Compile(_env, circuit));

            Assert.That(loads, Is.EqualTo(1));
            Assert.That(_runner.Context.GetInput(0), Is.EqualTo(Signal.Low), "New input C starts LOW.");
            Assert.That(_runner.Context.GetInput(1), Is.EqualTo(Signal.High), "A kept.");
            Assert.That(_runner.Context.GetInput(2), Is.EqualTo(Signal.High), "B kept.");
            Assert.That(_runner.Context.GetOutput(0), Is.EqualTo(Signal.Low), "Re-settled with kept inputs.");
            Assert.That(_runner.Context.Changes.Count, Is.EqualTo(0), "A reload is a full refresh, not an animation.");
        }

        [Test]
        public void StateChanged_IsRaised()
        {
            _runner.Load(Compile(_env, BuildNand()));
            SimulationState? observed = null;
            _runner.StateChanged += s => observed = s;

            _runner.Pause();

            Assert.That(observed, Is.EqualTo(SimulationState.Paused));
        }

        [Test]
        public void Controls_WithoutLoadedCircuit_Throw()
        {
            Assert.Throws<System.InvalidOperationException>(() => _runner.Step());
            Assert.Throws<System.InvalidOperationException>(() => _runner.SetInput(0, Signal.High));
        }
    }
}
