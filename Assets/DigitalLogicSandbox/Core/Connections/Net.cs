using System.Collections.Generic;

namespace DigitalLogicSandbox.Core.Connections
{
    /// <summary>
    /// A logical signal net: exactly one driving output pin and every input pin it drives. Derived from
    /// connections on demand; nets are never stored, so they cannot drift out of sync with the graph.
    /// </summary>
    public sealed class Net
    {
        private readonly List<PinRef> _sinks = new List<PinRef>();

        public Net(PinRef driver)
        {
            Driver = driver;
        }

        public PinRef Driver { get; }

        public IReadOnlyList<PinRef> Sinks => _sinks;

        internal void AddSink(PinRef sink)
        {
            _sinks.Add(sink);
        }

        public override string ToString()
        {
            return "Net(" + Driver + " -> " + _sinks.Count + " sinks)";
        }
    }
}
