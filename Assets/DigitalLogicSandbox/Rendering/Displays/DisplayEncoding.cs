using System.Globalization;
using System.Text;
using DigitalLogicSandbox.Core.Nodes;

namespace DigitalLogicSandbox.Rendering.Displays
{
    /// <summary>
    /// Pure decoding of display inputs into what a display shows (Phase D): seven-segment masks for hexadecimal and
    /// decimal digits, lamp counts, and a plain-text reading. Input bits are passed as two masks: <c>high</c> (bit i
    /// set when input bit i is HIGH) and <c>unknown</c> (bit i set when input bit i is neither HIGH nor LOW, i.e.
    /// UNKNOWN or high impedance). No allocation except in <see cref="FormatValue"/>.
    /// <para>
    /// Segment masks use the conventional layout: bit 0 = a (top), 1 = b (top right), 2 = c (bottom right),
    /// 3 = d (bottom), 4 = e (bottom left), 5 = f (top left), 6 = g (middle), 7 = dp (decimal point).
    /// </para>
    /// </summary>
    public static class DisplayEncoding
    {
        public const byte SegmentA = 1 << 0;
        public const byte SegmentB = 1 << 1;
        public const byte SegmentC = 1 << 2;
        public const byte SegmentD = 1 << 3;
        public const byte SegmentE = 1 << 4;
        public const byte SegmentF = 1 << 5;
        public const byte SegmentG = 1 << 6;
        public const byte SegmentDp = 1 << 7;

        /// <summary>A minus sign / "no value" dash: the middle segment only.</summary>
        public const byte Dash = SegmentG;

        /// <summary>Seven-segment glyphs of 0–9 and A, b, C, d, E, F.</summary>
        private static readonly byte[] HexGlyphs =
        {
            0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F, 0x77, 0x7C, 0x39, 0x5E, 0x79, 0x71,
        };

        /// <summary>Glyph of one hexadecimal digit (0–15).</summary>
        public static byte Glyph(int digit)
        {
            return HexGlyphs[digit & 0xF];
        }

        /// <summary>
        /// The hexadecimal digit whose glyph is exactly the segments a–g of <paramref name="mask"/> (the decimal point
        /// is ignored), or false when those segments form no digit.
        /// </summary>
        public static bool TryDecodeGlyph(byte mask, out int digit)
        {
            int segments = mask & 0x7F;
            for (int i = 0; i < HexGlyphs.Length; i++)
            {
                if (HexGlyphs[i] == segments)
                {
                    digit = i;
                    return true;
                }
            }

            digit = -1;
            return false;
        }

        /// <summary>Number of input bits a display reads: 8 for seven-segment (a–g, dp), otherwise its width.</summary>
        public static int InputCount(DisplayStyle style, int width)
        {
            return style == DisplayStyle.SevenSegment ? 8 : style == DisplayStyle.Led ? 1 : width;
        }

        /// <summary>Lamps drawn by a display: 1 for an LED, one per bit for a binary display, otherwise 0.</summary>
        public static int LampCount(DisplayStyle style, int width)
        {
            return style == DisplayStyle.Led ? 1 : style == DisplayStyle.Binary ? width : 0;
        }

        /// <summary>
        /// Seven-segment digits drawn by a display: 1 for a raw seven-segment display, one per 4 bits for hex, and
        /// enough for the largest magnitude (plus a sign) for the decimal styles.
        /// </summary>
        public static int DigitCount(DisplayStyle style, int width)
        {
            switch (style)
            {
                case DisplayStyle.SevenSegment:
                    return 1;
                case DisplayStyle.Hex:
                    return (width + 3) / 4;
                case DisplayStyle.Decimal:
                    return DecimalDigits(Mask(width));
                case DisplayStyle.SignedDecimal:
                    return 1 + DecimalDigits(1UL << (width - 1));
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Fills <paramref name="digits"/> (index 0 = leftmost) with the segment masks to show. Returns false when the
        /// value is not readable (a bus bit is UNKNOWN or Z): bus styles then show dashes. For a raw seven-segment
        /// display the masks are the HIGH inputs, and the return value says whether every segment is binary.
        /// </summary>
        public static bool EncodeDigits(DisplayStyle style, int width, ulong high, ulong unknown, byte[] digits)
        {
            int count = DigitCount(style, width);
            if (style == DisplayStyle.SevenSegment)
            {
                digits[0] = (byte)(high & 0xFF);
                return (unknown & 0xFF) == 0;
            }

            ulong mask = Mask(width);
            high &= mask;
            if ((unknown & mask) != 0)
            {
                for (int i = 0; i < count; i++)
                {
                    digits[i] = Dash;
                }

                return false;
            }

            switch (style)
            {
                case DisplayStyle.Hex:
                    for (int i = 0; i < count; i++)
                    {
                        digits[i] = Glyph((int)(high >> (4 * (count - 1 - i))));
                    }

                    return true;
                case DisplayStyle.Decimal:
                    WriteDecimal(high, false, digits, count);
                    return true;
                case DisplayStyle.SignedDecimal:
                    bool negative = ((high >> (width - 1)) & 1UL) != 0;
                    // Two's complement; the most negative value (e.g. 1000) maps to its own magnitude (8).
                    ulong magnitude = negative ? ((~high + 1UL) & mask) : high;

                    WriteDecimal(magnitude, negative, digits, count);
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>
        /// The reading as text: "ON"/"OFF" for an LED; for seven-segment the lit segment names, prefixed by the digit
        /// they form when they form one ("7 (a b c)", "5. (a c d f g dp)", "a b", "blank"); bits most significant first
        /// for binary, and the number for hex/decimal; "?" when a bus bit is unreadable.
        /// </summary>
        public static string FormatValue(DisplayStyle style, int width, ulong high, ulong unknown)
        {
            switch (style)
            {
                case DisplayStyle.Led:
                    return (unknown & 1UL) != 0 ? "?" : (high & 1UL) != 0 ? "ON" : "OFF";
                case DisplayStyle.SevenSegment:
                {
                    var text = new StringBuilder(24);
                    for (int i = 0; i < Core.Ports.BoundarySignatures.SegmentNames.Length; i++)
                    {
                        if (((unknown >> i) & 1UL) != 0)
                        {
                            text.Append(text.Length > 0 ? " " : string.Empty).Append(Core.Ports.BoundarySignatures.SegmentNames[i]).Append('?');
                        }
                        else if (((high >> i) & 1UL) != 0)
                        {
                            text.Append(text.Length > 0 ? " " : string.Empty).Append(Core.Ports.BoundarySignatures.SegmentNames[i]);
                        }
                    }

                    if (text.Length == 0)
                    {
                        return "blank";
                    }

                    if ((unknown & 0xFF) == 0 && TryDecodeGlyph((byte)high, out int digit))
                    {
                        string glyph = digit.ToString("X", CultureInfo.InvariantCulture) + ((high & SegmentDp) != 0 ? "." : string.Empty);
                        return glyph + " (" + text + ")";
                    }

                    return text.ToString();
                }
            }

            ulong mask = Mask(width);
            if (style == DisplayStyle.Binary)
            {
                var bits = new StringBuilder(width);
                for (int bit = width - 1; bit >= 0; bit--)
                {
                    bits.Append(((unknown >> bit) & 1UL) != 0 ? 'X' : ((high >> bit) & 1UL) != 0 ? '1' : '0');
                }

                return bits.ToString();
            }

            if ((unknown & mask) != 0)
            {
                return "?";
            }

            high &= mask;
            switch (style)
            {
                case DisplayStyle.Hex:
                    return high.ToString("X" + DigitCount(style, width).ToString(CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture);
                case DisplayStyle.SignedDecimal:
                    long value = ((high >> (width - 1)) & 1UL) != 0 ? (long)high - (1L << width) : (long)high;
                    return value.ToString(CultureInfo.InvariantCulture);
                default:
                    return high.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static void WriteDecimal(ulong value, bool negative, byte[] digits, int count)
        {
            int index = count - 1;
            do
            {
                digits[index--] = Glyph((int)(value % 10UL));
                value /= 10UL;
            }
            while (value != 0 && index >= 0);

            if (negative && index >= 0)
            {
                digits[index--] = Dash;
            }

            while (index >= 0)
            {
                digits[index--] = 0;
            }
        }

        private static int DecimalDigits(ulong value)
        {
            int digits = 1;
            while (value >= 10UL)
            {
                value /= 10UL;
                digits++;
            }

            return digits;
        }

        private static ulong Mask(int width)
        {
            return width >= 64 ? ulong.MaxValue : (1UL << width) - 1UL;
        }
    }
}
