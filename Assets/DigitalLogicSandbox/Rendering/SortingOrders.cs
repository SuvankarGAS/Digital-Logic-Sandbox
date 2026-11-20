namespace DigitalLogicSandbox.Rendering
{
    /// <summary>
    /// Draw order of workspace elements (all on the default sorting layer). Each gate is one sorting group, so the
    /// gate part orders below (outline … text) only sort inside a gate; gates are ordered among themselves by their
    /// stack order (<see cref="GateStackBase"/> … <see cref="GateStackMax"/>), above wires and below the ghost and the
    /// selection rectangle. Shadows sit on their own root under the wires.
    /// </summary>
    public static class SortingOrders
    {
        public const int GateShadow = -10;
        public const int Wire = 0;
        public const int SelectedWire = 1;
        public const int PreviewWire = 2;
        public const int SignalSpark = 3;

        // Parts inside a gate's sorting group.
        public const int GateOutline = 10;
        public const int GateFill = 11;
        public const int GateState = 12;
        public const int PinHighlight = 13;
        public const int Pin = 14;
        public const int Text = 15;

        /// <summary>Extra order of ghost parts inside the ghost (kept for parts that read it; the group decides).</summary>
        public const int GhostOffset = 100;

        /// <summary>Lowest gate stack order (above wires and signal sparks).</summary>
        public const int GateStackBase = 10;

        /// <summary>Highest gate stack order before the presenter renumbers the stack.</summary>
        public const int GateStackMax = 30000;

        /// <summary>The placement ghost: above every gate.</summary>
        public const int Ghost = 32000;

        public const int SelectionRectangle = 32500;
    }
}
