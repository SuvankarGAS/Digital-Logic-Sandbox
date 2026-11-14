using System.Collections.Generic;
using System.Text;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Simulation.Propagation;

namespace DigitalLogicSandbox.Simulation.Diagnostics
{
    /// <summary>Turns runtime simulation outcomes into user-facing diagnostics for the bottom panel.</summary>
    public static class SimulationDiagnostics
    {
        /// <summary>
        /// Adds a warning describing an oscillation (budget exhausted) to <paramref name="report"/>, naming up to
        /// <paramref name="maxGates"/> of the gates that were still switching. No-op for a stable result.
        /// </summary>
        public static void ReportLastSettle(SimulationContext context, ValidationReport report, int maxGates = 5)
        {
            ReportContention(context, report);
            SettleResult result = context.LastResult;
            if (result.Status != SettleStatus.Oscillating)
            {
                return;
            }

            IReadOnlyList<int> gates = context.UnstableGates;
            var builder = new StringBuilder();
            builder.Append("The circuit did not stabilise after ").Append(result.Evaluations)
                .Append(" gate evaluations (limit ").Append(context.EvaluationBudget)
                .Append("). A feedback loop is oscillating; its signals are shown as unknown (X). Reset the simulation after fixing the loop.");

            if (gates.Count > 0)
            {
                builder.Append(" Still switching: ");
                int shown = gates.Count < maxGates ? gates.Count : maxGates;
                for (int i = 0; i < shown; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(context.Circuit.DescribeGate(gates[i]));
                }

                if (gates.Count > shown)
                {
                    builder.Append(" and ").Append(gates.Count - shown).Append(" more");
                }

                builder.Append('.');
            }

            context.Circuit.GetGateOrigin(gates.Count > 0 ? gates[0] : 0, out Engine.CompiledInstance instance,
                out Core.Nodes.NodeId node);
            Core.Nodes.NodeId rootNode = instance.Parent == null ? node : instance.RootNode();
            report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.Oscillation, builder.ToString(),
                rootNode));
        }

        /// <summary>
        /// Adds a warning when shared buses are driven HIGH and LOW at the same time (bus contention), naming the
        /// drivers of the first contended bus. No-op when no bus is contended.
        /// </summary>
        public static void ReportContention(SimulationContext context, ValidationReport report)
        {
            if (context.ContentionCount == 0)
            {
                return;
            }

            Engine.CompiledCircuit circuit = context.Circuit;
            for (int net = 0; net < circuit.NetCount; net++)
            {
                if (!context.IsContended(net))
                {
                    continue;
                }

                int gate = circuit.NetDriverGate[net];
                var builder = new StringBuilder();
                builder.Append(context.ContentionCount).Append(context.ContentionCount == 1 ? " bus bit is" : " bus bits are")
                    .Append(" driven HIGH and LOW at the same time (bus contention); the value is shown as unknown (X). ")
                    .Append("Enable only one tri-state driver per bus. First contended bus has ")
                    .Append(circuit.GetSharedDriverCount(net)).Append(" drivers");
                if (gate >= 0)
                {
                    builder.Append(", e.g. ").Append(circuit.DescribeGate(gate));
                }

                builder.Append('.');
                Core.Nodes.NodeId rootNode = default;
                if (gate >= 0)
                {
                    circuit.GetGateOrigin(gate, out Engine.CompiledInstance instance, out Core.Nodes.NodeId node);
                    rootNode = instance.Parent == null ? node : instance.RootNode();
                }

                report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.BusContention, builder.ToString(),
                    rootNode));
                return;
            }
        }
    }
}
