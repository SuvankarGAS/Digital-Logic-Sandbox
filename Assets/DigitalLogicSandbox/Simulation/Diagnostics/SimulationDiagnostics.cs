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
    }
}
