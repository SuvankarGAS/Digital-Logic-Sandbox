namespace DigitalLogicSandbox.Core.Nodes
{
    /// <summary>
    /// What a <see cref="ComponentKind.Display"/> node shows (Phase D). Displays are pure observers: they read
    /// the nets wired to their inputs and never drive anything, so they add no logic and no delay.
    /// </summary>
    public enum DisplayStyle : byte
    {
        /// <summary>One lamp lit while its single input is HIGH.</summary>
        Led = 0,

        /// <summary>One seven-segment digit with a raw input per segment (a–g) and the decimal point.</summary>
        SevenSegment = 1,

        /// <summary>A row of lamps, one per bit of a bus (most significant bit on the left).</summary>
        Binary = 2,

        /// <summary>A bus value as hexadecimal seven-segment digits (one digit per 4 bits).</summary>
        Hex = 3,

        /// <summary>A bus value as an unsigned decimal number on seven-segment digits.</summary>
        Decimal = 4,

        /// <summary>A bus value as a two's-complement signed decimal number on seven-segment digits.</summary>
        SignedDecimal = 5,
    }

    public static class DisplayStyles
    {
        /// <summary>Highest defined style value.</summary>
        public const DisplayStyle Last = DisplayStyle.SignedDecimal;

        /// <summary>True for styles that read one bus input (the others have fixed single-bit inputs).</summary>
        public static bool IsBus(DisplayStyle style)
        {
            return style >= DisplayStyle.Binary && style <= Last;
        }

        /// <summary>Smallest valid width: 1 (fixed) for LED and seven-segment, 2 for signed decimal, else 1.</summary>
        public static int MinWidth(DisplayStyle style)
        {
            return style == DisplayStyle.SignedDecimal ? 2 : 1;
        }

        /// <summary>Largest valid width: 1 for the fixed single-bit styles, otherwise the bus maximum.</summary>
        public static int MaxWidth(DisplayStyle style)
        {
            return IsBus(style) ? Ports.PortDescriptor.MaxWidth : 1;
        }

        /// <summary>Stable lower-case identifier used by persistence.</summary>
        public static string ToId(DisplayStyle style)
        {
            switch (style)
            {
                case DisplayStyle.Led:
                    return "led";
                case DisplayStyle.SevenSegment:
                    return "sevensegment";
                case DisplayStyle.Binary:
                    return "binary";
                case DisplayStyle.Hex:
                    return "hex";
                case DisplayStyle.Decimal:
                    return "decimal";
                default:
                    return "signeddecimal";
            }
        }

        public static bool TryParseId(string id, out DisplayStyle style)
        {
            for (DisplayStyle s = DisplayStyle.Led; s <= Last; s++)
            {
                if (string.Equals(ToId(s), id, System.StringComparison.Ordinal))
                {
                    style = s;
                    return true;
                }
            }

            style = DisplayStyle.Led;
            return false;
        }

        /// <summary>Human-readable name, e.g. "Hex display".</summary>
        public static string DisplayName(DisplayStyle style)
        {
            switch (style)
            {
                case DisplayStyle.Led:
                    return "LED";
                case DisplayStyle.SevenSegment:
                    return "Seven-segment display";
                case DisplayStyle.Binary:
                    return "Binary display";
                case DisplayStyle.Hex:
                    return "Hex display";
                case DisplayStyle.Decimal:
                    return "Decimal display";
                default:
                    return "Signed decimal display";
            }
        }
    }
}
