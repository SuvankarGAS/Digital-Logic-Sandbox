namespace DigitalLogicSandbox.Simulation.Propagation
{
    public enum SettleStatus : byte
    {
        /// <summary>No pending work remains; all nets hold consistent values.</summary>
        Stable = 0,

        /// <summary>The evaluation budget was exhausted: the circuit is oscillating (feedback loop).</summary>
        Oscillating = 1,

        /// <summary>Stopped early on request (single-wave stepping); more work is pending.</summary>
        Pending = 2,
    }

    public readonly struct SettleResult
    {
        public SettleResult(SettleStatus status, int evaluations, int waves)
        {
            Status = status;
            Evaluations = evaluations;
            Waves = waves;
        }

        public SettleStatus Status { get; }

        /// <summary>Gate evaluations performed by this call.</summary>
        public int Evaluations { get; }

        /// <summary>Propagation waves processed by this call.</summary>
        public int Waves { get; }

        public bool IsStable => Status == SettleStatus.Stable;

        public override string ToString()
        {
            return Status + " (" + Evaluations + " evaluations, " + Waves + " waves)";
        }
    }
}
