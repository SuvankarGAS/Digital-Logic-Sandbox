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

        /// <summary>Wiring utility (Phase C): splits a bus into its single-bit wires. Adds no logic or delay.</summary>
        Splitter = 5,

        /// <summary>Wiring utility (Phase C): merges single-bit wires into a bus. Adds no logic or delay.</summary>
        Merger = 6,

        /// <summary>
        /// Wiring utility (Phase C): joins several drivers onto one shared bus (for tri-state outputs). The joined
        /// net is resolved from all its drivers; conflicting active drivers are reported as bus contention.
        /// </summary>
        BusJoin = 7,

        /// <summary>
        /// Bus driver utility (Phase C): passes its data input through while EN is HIGH and releases the bus
        /// (high impedance, Z) while EN is LOW. Z cannot be built from AND/NOT, so this is the one utility with
        /// behaviour; it is evaluated bit by bit and appears under "Bus", not among the built-in logic gates.
        /// </summary>
        TriState = 8,
    }
}
