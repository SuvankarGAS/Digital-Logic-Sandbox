namespace DigitalLogicSandbox.Core.Nodes
{
    public enum ComponentKind : byte
    {
        /// <summary>A built-in logic primitive (initially only AND and NOT).</summary>
        Primitive = 0,

        /// <summary>An instance of a packaged circuit definition.</summary>
        Custom = 1,

        /// <summary>Circuit utility: external input boundary (user-togglable source). Not a logic gate.</summary>
        InputBoundary = 2,

        /// <summary>Circuit utility: external output boundary (indicator). Not a logic gate.</summary>
        OutputBoundary = 3,

        /// <summary>
        /// Circuit utility: clock source driven by the simulation's global clock (Phase B). Not a logic gate and not
        /// part of the circuit interface; every clock node in a hierarchy carries the same clock signal.
        /// </summary>
        Clock = 4,
    }
}
