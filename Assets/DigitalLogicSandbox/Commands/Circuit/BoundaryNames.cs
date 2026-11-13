using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.Commands.Circuit
{
    /// <summary>Generates unique default names for new interface ports: inputs A, B, … Z, A1 …; outputs OUT, OUT2, ….</summary>
    public static class BoundaryNames
    {
        public static string NextInputName(CircuitDefinition circuit)
        {
            HashSet<string> used = Collect(circuit.Inputs);
            for (int suffix = 0; ; suffix++)
            {
                for (char letter = 'A'; letter <= 'Z'; letter++)
                {
                    string name = suffix == 0 ? letter.ToString() : letter.ToString() + suffix;
                    if (!used.Contains(name))
                    {
                        return name;
                    }
                }
            }
        }

        public static string NextOutputName(CircuitDefinition circuit)
        {
            HashSet<string> used = Collect(circuit.Outputs);
            if (!used.Contains("OUT"))
            {
                return "OUT";
            }

            for (int i = 2; ; i++)
            {
                string name = "OUT" + i;
                if (!used.Contains(name))
                {
                    return name;
                }
            }
        }

        public static string NextName(CircuitDefinition circuit, PortDirection direction)
        {
            return direction == PortDirection.Input ? NextInputName(circuit) : NextOutputName(circuit);
        }

        private static HashSet<string> Collect(IReadOnlyList<InterfacePort> ports)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ports.Count; i++)
            {
                used.Add(ports[i].Name);
            }

            return used;
        }
    }
}
