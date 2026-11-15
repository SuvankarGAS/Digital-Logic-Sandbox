using System;

namespace DigitalLogicSandbox.Core.Nodes
{
    /// <summary>
    /// The colours an LED display can light up in. Purely presentational: the colour is stored on the component
    /// reference (and in project files) but never affects simulation. Index 0 is the theme's HIGH signal colour;
    /// the actual colour values belong to the visual theme.
    /// </summary>
    public static class LedColors
    {
        /// <summary>Number of LED colours (indices 0 .. Count - 1).</summary>
        public const int Count = 6;

        /// <summary>The default colour: the theme's HIGH signal colour.</summary>
        public const int Signal = 0;

        private static readonly string[] Ids = { "signal", "red", "orange", "green", "blue", "pink" };
        private static readonly string[] Names = { "Signal", "Red", "Orange", "Green", "Blue", "Pink" };

        public static bool IsValid(int color)
        {
            return color >= 0 && color < Count;
        }

        /// <summary>Stable id used in project files.</summary>
        public static string ToId(int color)
        {
            return Ids[IsValid(color) ? color : Signal];
        }

        public static bool TryParseId(string id, out int color)
        {
            for (int i = 0; i < Ids.Length; i++)
            {
                if (string.Equals(Ids[i], id, StringComparison.OrdinalIgnoreCase))
                {
                    color = i;
                    return true;
                }
            }

            color = Signal;
            return false;
        }

        public static string DisplayName(int color)
        {
            return Names[IsValid(color) ? color : Signal];
        }
    }
}
