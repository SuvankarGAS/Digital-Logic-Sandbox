using System;
using DigitalLogicSandbox.TruthTable.Evaluation;
using UnityEngine;

namespace DigitalLogicSandbox.TruthTable.UI
{
    /// <summary>
    /// Runs at most one <see cref="TruthTableJob"/> on the main thread, spending a fixed time budget per frame so
    /// large tables never freeze the editor. One driver exists per truth table panel (not per row or gate).
    /// </summary>
    public sealed class TruthTableJobDriver : MonoBehaviour
    {
        private double _frameBudgetMilliseconds = 6.0;

        /// <summary>Raised every frame in which rows were produced, and once more when the job finishes.</summary>
        public event Action<TruthTableJob> Progressed;

        /// <summary>Raised once when the current job completes, fails or is cancelled.</summary>
        public event Action<TruthTableJob> Finished;

        public TruthTableJob Current { get; private set; }

        public bool IsRunning => Current != null && !Current.IsFinished;

        public double FrameBudgetMilliseconds
        {
            get => _frameBudgetMilliseconds;
            set => _frameBudgetMilliseconds = Math.Max(0.5, value);
        }

        /// <summary>Starts <paramref name="job"/>, cancelling any job still running.</summary>
        public void Run(TruthTableJob job)
        {
            Cancel();
            Current = job ?? throw new ArgumentNullException(nameof(job));
            if (job.IsFinished)
            {
                Finish();
            }
        }

        /// <summary>Cancels the running job (rows produced so far stay in its table).</summary>
        public void Cancel()
        {
            if (IsRunning)
            {
                Current.Cancel();
                Finish();
            }
        }

        /// <summary>Completes the running job synchronously (tests, small tables).</summary>
        public void RunToCompletion()
        {
            if (!IsRunning)
            {
                return;
            }

            Current.RunToCompletion();
            Progressed?.Invoke(Current);
            Finish();
        }

        private void Update()
        {
            if (!IsRunning)
            {
                return;
            }

            Current.AdvanceFor(_frameBudgetMilliseconds);
            Progressed?.Invoke(Current);
            if (Current.IsFinished)
            {
                Finish();
            }
        }

        private void Finish()
        {
            Finished?.Invoke(Current);
        }

        private void OnDestroy()
        {
            if (IsRunning)
            {
                Current.Cancel();
            }
        }
    }
}
