namespace DigitalLogicSandbox.Rendering
{
    /// <summary>Draw order of workspace elements (all on the default sorting layer).</summary>
    public static class SortingOrders
    {
        public const int GateShadow = -10;
        public const int Wire = 0;
        public const int SelectedWire = 1;
        public const int PreviewWire = 2;
        public const int SignalSpark = 3;
        public const int GateOutline = 10;
        public const int GateFill = 11;
        public const int GateState = 12;
        public const int PinHighlight = 13;
        public const int Pin = 14;
        public const int Text = 15;
        public const int GhostOffset = 100;
        public const int SelectionRectangle = 200;
    }
}
