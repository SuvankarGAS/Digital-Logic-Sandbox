using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Signals;

namespace DigitalLogicSandbox.Simulation.Engine
{
    /// <summary>
    /// Timing of the clock domains. The project frequency is the base rate; a clock with divider 2^k runs 2^k times
    /// slower. Edges are counted on the base clock: after base edge <c>E</c> (from 1), domain k is HIGH when
    /// <c>((E + 2^k - 1) &gt;&gt; k)</c> is odd. Every domain therefore rises on edge 1, slower domains rise together
    /// with the base clock, and all domains are LOW at edge 0 (power-on).
    /// </summary>
    public static class ClockSchedule
    {
        /// <summary>Number of clock domains (dividers ÷1 … ÷2^<see cref="ComponentRef.MaxClockExponent"/>).</summary>
        public const int DomainCount = ComponentRef.MaxClockExponent + 1;

        /// <summary>Level of the domain with divider 2^<paramref name="exponent"/> after base edge <paramref name="edge"/>.</summary>
        public static Signal Level(int exponent, long edge)
        {
            if (edge <= 0)
            {
                return Signal.Low;
            }

            long divider = 1L << exponent;
            return ((edge + divider - 1) >> exponent & 1L) == 1L ? Signal.High : Signal.Low;
        }
    }
}
