using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;

namespace DigitalLogicSandbox.Core.Connections
{
    /// <summary>Groups a circuit's connections into nets (one driver, many sinks).</summary>
    public static class NetBuilder
    {
        /// <summary>
        /// Fills <paramref name="results"/> with one net per driving output pin, ordered by first appearance of
        /// the driver in the connection list (deterministic).
        /// </summary>
        public static void Build(CircuitDefinition circuit, List<Net> results)
        {
            if (circuit == null)
            {
                throw new ArgumentNullException(nameof(circuit));
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            var byDriver = new Dictionary<PinRef, Net>();
            IReadOnlyList<ConnectionDefinition> connections = circuit.Connections;
            for (int i = 0; i < connections.Count; i++)
            {
                ConnectionDefinition connection = connections[i];
                if (!byDriver.TryGetValue(connection.Source, out Net net))
                {
                    net = new Net(connection.Source);
                    byDriver.Add(connection.Source, net);
                    results.Add(net);
                }

                net.AddSink(connection.Destination);
            }
        }

        public static List<Net> Build(CircuitDefinition circuit)
        {
            var results = new List<Net>();
            Build(circuit, results);
            return results;
        }
    }
}
