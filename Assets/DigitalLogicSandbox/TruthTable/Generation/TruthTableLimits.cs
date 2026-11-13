using System;

namespace DigitalLogicSandbox.TruthTable.Generation
{
    /// <summary>
    /// Configurable bounds for truth table generation. A table has 2^N rows for N inputs, so both the input count
    /// and the row count are capped. Tables above <see cref="ConfirmRows"/> are only generated after the user
    /// confirms. Public fields so the settings can be serialized by Unity and by the project file.
    /// </summary>
    [Serializable]
    public sealed class TruthTableLimits
    {
        /// <summary>Hard ceiling regardless of configuration (2^24 rows is already ~16.7 million).</summary>
        public const int AbsoluteMaxInputs = 24;

        public const int DefaultMaxInputs = 16;
        public const int DefaultMaxRows = 1 << 16;
        public const int DefaultConfirmRows = 1 << 10;
        public const double DefaultFrameBudgetMilliseconds = 6.0;

        /// <summary>Circuits with more inputs are refused.</summary>
        public int MaxInputs = DefaultMaxInputs;

        /// <summary>Tables with more rows are refused.</summary>
        public int MaxRows = DefaultMaxRows;

        /// <summary>Tables with more rows need an explicit confirmation before generation starts.</summary>
        public int ConfirmRows = DefaultConfirmRows;

        /// <summary>Time the UI spends generating rows per frame, so the editor never freezes.</summary>
        public double FrameBudgetMilliseconds = DefaultFrameBudgetMilliseconds;

        public int EffectiveMaxInputs => Math.Max(0, Math.Min(MaxInputs, AbsoluteMaxInputs));

        public int EffectiveMaxRows => Math.Max(1, Math.Min(MaxRows, 1 << AbsoluteMaxInputs));

        public int EffectiveConfirmRows => Math.Max(1, ConfirmRows);

        public double EffectiveFrameBudget => Math.Max(0.5, Math.Min(FrameBudgetMilliseconds, 100.0));

        /// <summary>Rows needed for <paramref name="inputCount"/> binary inputs (saturates instead of overflowing).</summary>
        public static long RowsFor(int inputCount)
        {
            if (inputCount <= 0)
            {
                return 1;
            }

            return inputCount >= 62 ? long.MaxValue : 1L << inputCount;
        }

        public void CopyFrom(TruthTableLimits other)
        {
            if (other == null)
            {
                return;
            }

            MaxInputs = other.MaxInputs;
            MaxRows = other.MaxRows;
            ConfirmRows = other.ConfirmRows;
            FrameBudgetMilliseconds = other.FrameBudgetMilliseconds;
        }
    }
}
