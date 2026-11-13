namespace DigitalLogicSandbox.Core.Signals
{
    public static class SignalExtensions
    {
        public static bool IsHigh(this Signal signal)
        {
            return signal == Signal.High;
        }

        public static bool IsLow(this Signal signal)
        {
            return signal == Signal.Low;
        }

        /// <summary>True for the two binary states (Low, High).</summary>
        public static bool IsBinary(this Signal signal)
        {
            return signal == Signal.Low || signal == Signal.High;
        }

        public static Signal FromBool(bool value)
        {
            return value ? Signal.High : Signal.Low;
        }

        /// <summary>Single-character representation used by truth tables, logs and exports.</summary>
        public static char ToDisplayChar(this Signal signal)
        {
            switch (signal)
            {
                case Signal.Low:
                    return '0';
                case Signal.High:
                    return '1';
                case Signal.HighImpedance:
                    return 'Z';
                default:
                    return 'X';
            }
        }
    }
}
