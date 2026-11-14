using System;
using System.Diagnostics;
using System.Threading;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Simulation.Propagation;
using DigitalLogicSandbox.TruthTable.Generation;

namespace DigitalLogicSandbox.TruthTable.Evaluation
{
    public enum TruthTableStatus : byte
    {
        /// <summary>Created; no row evaluated yet.</summary>
        Pending = 0,

        Running = 1,

        Completed = 2,

        /// <summary>Stopped by the user; rows produced so far remain valid.</summary>
        Cancelled = 3,

        /// <summary>A row could not be evaluated (see <see cref="TruthTableJob.Error"/>), or the plan was refused.</summary>
        Failed = 4,
    }

    /// <summary>
    /// Incremental truth table evaluation. Rows are produced in order on the caller's thread, a slice at a time
    /// (<see cref="Advance"/> / <see cref="AdvanceFor"/>), so a UI can spread a large table over many frames,
    /// show progress and cancel without threads. Each job owns its own <see cref="SimulationContext"/>.
    /// </summary>
    public sealed class TruthTableJob
    {
        private const int RowsBetweenClockChecks = 32;

        private readonly CompiledCircuit _circuit;
        private readonly SimulationOptions _options;
        private readonly Stopwatch _clock = new Stopwatch();
        private SimulationContext _context;
        private long _releasedEvaluations;

        internal TruthTableJob(TruthTablePlan plan, SimulationOptions options)
        {
            Plan = plan;
            _circuit = plan.Circuit;
            _options = options;
            Table = new TruthTableData(plan.Title, plan.InputColumns, plan.OutputColumns, (int)plan.RowCount);
        }

        private TruthTableJob(TruthTablePlan plan)
        {
            Plan = plan;
            Status = TruthTableStatus.Failed;
            Error = plan.FirstError ?? "The truth table cannot be generated.";
        }

        public TruthTablePlan Plan { get; }

        /// <summary>The table being filled. Null for a refused plan.</summary>
        public TruthTableData Table { get; }

        public TruthTableStatus Status { get; private set; }

        /// <summary>Failure reason, or null.</summary>
        public string Error { get; private set; }

        public int CompletedRows => Table?.CompletedRows ?? 0;

        public int TotalRows => Table?.RowCount ?? 0;

        public float Progress => TotalRows == 0 ? 0f : (float)CompletedRows / TotalRows;

        public bool IsFinished => Status == TruthTableStatus.Completed || Status == TruthTableStatus.Cancelled ||
                                  Status == TruthTableStatus.Failed;

        /// <summary>Gate evaluations performed by this job (diagnostics).</summary>
        public long EvaluationCount => _context?.TotalEvaluations ?? _releasedEvaluations;

        /// <summary>Total time spent evaluating.</summary>
        public TimeSpan Elapsed => _clock.Elapsed;

        internal static TruthTableJob Refused(TruthTablePlan plan)
        {
            return new TruthTableJob(plan);
        }

        /// <summary>Evaluates up to <paramref name="maxRows"/> rows. Returns the number evaluated.</summary>
        public int Advance(int maxRows)
        {
            if (!Begin())
            {
                return 0;
            }

            _clock.Start();
            int done = 0;
            while (done < maxRows && Status == TruthTableStatus.Running)
            {
                EvaluateNextRow();
                done++;
            }

            _clock.Stop();
            return done;
        }

        /// <summary>
        /// Evaluates rows for roughly <paramref name="milliseconds"/> (at least one row). Returns true while the job
        /// still has work left.
        /// </summary>
        public bool AdvanceFor(double milliseconds)
        {
            if (!Begin())
            {
                return false;
            }

            long budgetTicks = (long)(Math.Max(0.0, milliseconds) * Stopwatch.Frequency / 1000.0);
            long start = Stopwatch.GetTimestamp();
            _clock.Start();
            do
            {
                for (int i = 0; i < RowsBetweenClockChecks && Status == TruthTableStatus.Running; i++)
                {
                    EvaluateNextRow();
                }
            }
            while (Status == TruthTableStatus.Running && Stopwatch.GetTimestamp() - start < budgetTicks);

            _clock.Stop();
            return !IsFinished;
        }

        /// <summary>Runs until finished or cancelled through <paramref name="cancellation"/>.</summary>
        public void RunToCompletion(CancellationToken cancellation = default)
        {
            while (!IsFinished)
            {
                if (cancellation.IsCancellationRequested)
                {
                    Cancel();
                    return;
                }

                Advance(256);
            }
        }

        /// <summary>Stops the job. Rows already produced stay valid; nothing else is affected.</summary>
        public void Cancel()
        {
            if (!IsFinished)
            {
                Status = TruthTableStatus.Cancelled;
                ReleaseContext();
            }
        }

        private bool Begin()
        {
            if (IsFinished)
            {
                return false;
            }

            if (Status == TruthTableStatus.Pending)
            {
                // The context starts from the all-LOW settled state, i.e. row 0's inputs.
                _context = new SimulationContext(_circuit, _options) { RecordChanges = false };
                Status = TruthTableStatus.Running;
            }

            return true;
        }

        private void EvaluateNextRow()
        {
            TruthTableData table = Table;
            int row = table.CompletedRows;
            int inputCount = table.InputCount;
            for (int i = 0; i < inputCount; i++)
            {
                _context.SetInput(table.Inputs[i].Index, TruthTableData.InputBit(row, i, inputCount) ? Signal.High : Signal.Low);
            }

            SettleResult result = _context.Settle();
            if (!result.IsStable)
            {
                Fail("Row " + row + " did not settle within the evaluation budget (the circuit oscillates).");
                return;
            }

            for (int o = 0; o < table.OutputCount; o++)
            {
                table.SetOutput(row, o, _context.GetOutput(table.Outputs[o].Index));
            }

            table.CompletedRows = row + 1;
            if (table.IsComplete)
            {
                Status = TruthTableStatus.Completed;
                ReleaseContext();
            }
        }

        /// <summary>Drops the evaluation context (its memory is no longer needed) but keeps its statistics.</summary>
        private void ReleaseContext()
        {
            if (_context != null)
            {
                _releasedEvaluations = _context.TotalEvaluations;
                _context = null;
            }
        }

        private void Fail(string error)
        {
            Error = error;
            Status = TruthTableStatus.Failed;
            ReleaseContext();
        }
    }
}
