using DigitalLogicSandbox.Core.Validation;

namespace DigitalLogicSandbox.Simulation.Engine
{
    public sealed class SimulationOptions
    {
        /// <summary>Minimum evaluation budget per settle, regardless of circuit size.</summary>
        public const int DefaultMinimumEvaluationBudget = 10000;

        /// <summary>Budget multiplier: a stable acyclic circuit never needs more than a few evaluations per gate.</summary>
        public const int DefaultEvaluationsPerGate = 64;

        public static SimulationOptions Default => new SimulationOptions();

        /// <summary>Maximum custom-gate nesting depth accepted by the compiler.</summary>
        public int MaxHierarchyDepth { get; set; } = ValidationOptions.DefaultMaxHierarchyDepth;

        public int MinimumEvaluationBudget { get; set; } = DefaultMinimumEvaluationBudget;

        public int EvaluationsPerGate { get; set; } = DefaultEvaluationsPerGate;

        /// <summary>Record per-net changes for presentation layers. Disable for headless analysis (truth tables).</summary>
        public bool RecordChanges { get; set; } = true;

        /// <summary>Upper bound on buffered changes between consumer reads; beyond it the log marks itself overflowed.</summary>
        public int MaxRecordedChanges { get; set; } = 1 << 20;

        /// <summary>The safety limit on gate evaluations in a single settle for a circuit of the given size.</summary>
        public int EvaluationBudgetFor(int gateCount)
        {
            long budget = (long)gateCount * EvaluationsPerGate;
            if (budget < MinimumEvaluationBudget)
            {
                budget = MinimumEvaluationBudget;
            }

            return budget > int.MaxValue ? int.MaxValue : (int)budget;
        }
    }
}
