using DigitalLogicSandbox.Core.Signals;

namespace DigitalLogicSandbox.Simulation.Propagation
{
    /// <summary>
    /// One net value change produced by the engine. <see cref="Wave"/> is the propagation depth at which it
    /// happened: 0 = external input change, 1 = output of gates evaluated directly after it, and so on.
    /// Presentation layers use the wave to animate in logical order; the engine never waits for them.
    /// </summary>
    public readonly struct SignalChange
    {
        public SignalChange(int net, Signal previous, Signal current, int wave)
        {
            Net = net;
            Previous = previous;
            Current = current;
            Wave = wave;
        }

        public int Net { get; }

        public Signal Previous { get; }

        public Signal Current { get; }

        public int Wave { get; }

        public override string ToString()
        {
            return "net " + Net + ": " + Previous + " -> " + Current + " (wave " + Wave + ")";
        }
    }
}
