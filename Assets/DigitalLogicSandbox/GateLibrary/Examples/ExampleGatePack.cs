using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.GateLibrary.Packaging;

namespace DigitalLogicSandbox.GateLibrary.Examples
{
    /// <summary>Body size of a node in world units (width, height), measured against <paramref name="catalog"/>.</summary>
    public delegate CircuitPoint NodeSizer(ComponentCatalog catalog, CircuitDefinition circuit, NodeDefinition node);

    /// <summary>
    /// The optional example gate pack (Phase E): logic, routing, arithmetic and memory parts up to a 4-bit ALU and a
    /// 16×4 RAM. Every gate is an ordinary custom gate built only from the AND and NOT primitives, wiring components
    /// (splitters, mergers) and earlier gates of the pack, referenced by ID rather than copied. Nothing here is
    /// special-cased elsewhere: once published, the gates behave exactly like user-packaged ones.
    /// </summary>
    public static class ExampleGatePack
    {
        public const string IdPrefix = "example.";

        public const string LogicCategory = "Examples · Logic";
        public const string RoutingCategory = "Examples · Routing";
        public const string ArithmeticCategory = "Examples · Arithmetic";
        public const string MemoryCategory = "Examples · Memory";
        public const string ComputerCategory = "Examples · Computer";

        /// <summary>Suffix added to a pack gate's name when the library already has a gate of that name.</summary>
        public const string ClashSuffix = " (example)";

        /// <summary>True for definition IDs that belong to the pack.</summary>
        public static bool IsExampleId(DefinitionId id)
        {
            return id.IsValid && id.Value.StartsWith(IdPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Builds the whole pack in dependency order into a private staging library. Positions are arranged with
        /// <see cref="LayeredLayout"/> using <paramref name="sizer"/> (or a pin-count estimate when null).
        /// </summary>
        public static ExamplePack Build(PrimitiveRegistry primitives, NodeSizer sizer = null,
            Func<string, string> nameFor = null)
        {
            if (primitives == null)
            {
                throw new ArgumentNullException(nameof(primitives));
            }

            var pack = new ExamplePack(primitives, sizer ?? EstimateSize, nameFor);
            pack.BuildAll();
            return pack;
        }

        /// <summary>
        /// Prepares the pack for <paramref name="target"/>: refuses when it is already there, renames gates whose name
        /// is taken, and validates every gate with the packaging rules. Nothing is published; the caller publishes
        /// <see cref="ExamplePackPlan.Definitions"/> in order (as one undoable step).
        /// </summary>
        public static ExamplePackPlan Prepare(PrimitiveRegistry primitives, DefinitionLibrary target, NodeSizer sizer = null)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var latest = new List<CircuitDefinition>();
            target.GetLatestDefinitions(latest);
            for (int i = 0; i < latest.Count; i++)
            {
                if (IsExampleId(latest[i].Id))
                {
                    return ExamplePackPlan.Refused("The example gates are already in this project.");
                }
            }

            // Names are settled before each gate is built: published (staged) definitions are read-only.
            var renamed = new List<string>();
            ExamplePack pack = Build(primitives, sizer, name =>
            {
                if (!target.ContainsName(name))
                {
                    return name;
                }

                string unique = name + ClashSuffix;
                for (int n = 2; target.ContainsName(unique); n++)
                {
                    unique = name + " (example " + n + ")";
                }

                renamed.Add(name);
                return unique;
            });

            var packager = new CircuitPackager(pack.Catalog, pack.Library);
            for (int i = 0; i < pack.Definitions.Count; i++)
            {
                ValidationReport report = packager.ValidateDefinition(pack.Definitions[i]);
                if (report.HasErrors)
                {
                    return ExamplePackPlan.Refused("Example gate '" + pack.Definitions[i].Name + "' is invalid: " +
                                                   FirstError(report));
                }
            }

            return new ExamplePackPlan(pack.Definitions, renamed, null);
        }

        private static string FirstError(ValidationReport report)
        {
            for (int i = 0; i < report.Diagnostics.Count; i++)
            {
                if (report.Diagnostics[i].Severity == DiagnosticSeverity.Error)
                {
                    return report.Diagnostics[i].Message;
                }
            }

            return "unknown error";
        }

        /// <summary>Rough body size from pin counts, for callers without the renderer's measurements.</summary>
        public static CircuitPoint EstimateSize(ComponentCatalog catalog, CircuitDefinition circuit, NodeDefinition node)
        {
            if (node.IsBoundary)
            {
                return new CircuitPoint(2.4f, node.Component.Width > 1 ? 1.6f : 1.2f);
            }

            int pins = 1;
            if (catalog.TryGetSignature(node.Component, out ComponentSignature signature))
            {
                pins = Math.Max(1, Math.Max(signature.Inputs.Count, signature.Outputs.Count));
            }

            return new CircuitPoint(node.Component.Kind == ComponentKind.Custom ? 3.5f : 2f, Math.Max(1.5f, pins * 0.75f + 0.5f));
        }
    }

    /// <summary>Result of <see cref="ExampleGatePack.Prepare"/>.</summary>
    public sealed class ExamplePackPlan
    {
        internal ExamplePackPlan(IReadOnlyList<CircuitDefinition> definitions, IReadOnlyList<string> renamed, string error)
        {
            Definitions = definitions ?? Array.Empty<CircuitDefinition>();
            Renamed = renamed ?? Array.Empty<string>();
            Error = error;
        }

        /// <summary>A plan that adds nothing, with the reason.</summary>
        public static ExamplePackPlan Refused(string error)
        {
            return new ExamplePackPlan(null, null, error);
        }

        /// <summary>Gates to publish, in dependency order (empty when refused).</summary>
        public IReadOnlyList<CircuitDefinition> Definitions { get; }

        /// <summary>Original names of gates that were renamed because the name was taken.</summary>
        public IReadOnlyList<string> Renamed { get; }

        /// <summary>Why the pack cannot be added, or null.</summary>
        public string Error { get; }

        public bool Succeeded => Error == null;
    }

    /// <summary>The built pack: definitions in dependency order plus the staging library they resolve against.</summary>
    public sealed class ExamplePack
    {
        private readonly List<CircuitDefinition> _definitions = new List<CircuitDefinition>();
        private readonly Dictionary<string, CircuitDefinition> _byKey = new Dictionary<string, CircuitDefinition>();
        private readonly NodeSizer _sizer;
        private readonly PinOffset _pins;
        private readonly Func<string, string> _nameFor;

        internal ExamplePack(PrimitiveRegistry primitives, NodeSizer sizer, Func<string, string> nameFor)
        {
            Library = new DefinitionLibrary();
            Catalog = new ComponentCatalog(primitives, Library);
            _sizer = sizer;
            _pins = LayeredLayout.SignaturePins(Catalog);
            _nameFor = nameFor;
        }

        public IReadOnlyList<CircuitDefinition> Definitions => _definitions;

        /// <summary>Staging library holding the pack (so gates resolve each other before publication).</summary>
        public DefinitionLibrary Library { get; }

        public ComponentCatalog Catalog { get; }

        /// <summary>A pack gate by its ID suffix, e.g. "full-adder".</summary>
        public CircuitDefinition Get(string key)
        {
            return _byKey[key];
        }

        internal void BuildAll()
        {
            // Logic
            Add("nand", "NAND", ExampleGatePack.LogicCategory, "NOT (A AND B).", b =>
            {
                b.Out("OUT", b.Not(b.And(b.In("A"), b.In("B"))));
            });
            Add("or", "OR", ExampleGatePack.LogicCategory, "A OR B, from De Morgan: NOT (NOT A AND NOT B).", b =>
            {
                b.Out("OUT", b.Not(b.And(b.Not(b.In("A")), b.Not(b.In("B")))));
            });
            Add("nor", "NOR", ExampleGatePack.LogicCategory, "NOT (A OR B) = NOT A AND NOT B.", b =>
            {
                b.Out("OUT", b.And(b.Not(b.In("A")), b.Not(b.In("B"))));
            });
            Add("xor", "XOR", ExampleGatePack.LogicCategory, "HIGH when exactly one input is HIGH: (A OR B) AND (A NAND B).", b =>
            {
                PinRef a = b.In("A");
                PinRef bb = b.In("B");
                b.Out("OUT", b.And(b.Gate("or", a, bb)[0], b.Gate("nand", a, bb)[0]));
            });
            AddBitwise("and4", "AND 4-bit", "Y = A AND B, bit by bit, on 4-bit buses.", (b, x, y) => b.And(x, y));
            AddBitwise("or4", "OR 4-bit", "Y = A OR B, bit by bit, on 4-bit buses.", (b, x, y) => b.Gate("or", x, y)[0]);
            AddBitwise("xor4", "XOR 4-bit", "Y = A XOR B, bit by bit, on 4-bit buses. XOR with all ones inverts A.",
                (b, x, y) => b.Gate("xor", x, y)[0]);

            // Routing
            Add("mux2", "MUX 2:1", ExampleGatePack.RoutingCategory, "OUT = A when S is 0, B when S is 1.", b =>
            {
                PinRef s = b.In("S");
                PinRef a = b.In("A");
                PinRef bb = b.In("B");
                b.Out("OUT", b.Gate("or", b.And(a, b.Not(s)), b.And(bb, s))[0]);
            });
            Add("mux4", "MUX 4:1", ExampleGatePack.RoutingCategory, "OUT = input number S (I0 … I3); S is a 2-bit bus.", b =>
            {
                PinRef[] s = b.Split(b.In("S", 2), 2);
                PinRef i0 = b.In("I0");
                PinRef i1 = b.In("I1");
                PinRef i2 = b.In("I2");
                PinRef i3 = b.In("I3");
                PinRef low = b.Gate("mux2", s[0], i0, i1)[0];
                PinRef high = b.Gate("mux2", s[0], i2, i3)[0];
                b.Out("OUT", b.Gate("mux2", s[1], low, high)[0]);
            });
            Add("mux2x4", "MUX 2:1 4-bit", ExampleGatePack.RoutingCategory, "OUT = bus A when S is 0, bus B when S is 1 (4-bit buses).", b =>
            {
                PinRef s = b.In("S");
                PinRef[] a = b.Split(b.In("A", 4), 4);
                PinRef[] bb = b.Split(b.In("B", 4), 4);
                var bits = new PinRef[4];
                for (int i = 0; i < 4; i++)
                {
                    bits[i] = b.Gate("mux2", s, a[i], bb[i])[0];
                }

                b.Out("OUT", b.Merge(bits), 4);
            });
            AddBusMux4("mux4x4", "MUX 4:1 4-bit", 4);
            AddBusMux4("mux4x8", "MUX 4:1 8-bit", 8);
            Add("dec2", "Decoder 2→4", ExampleGatePack.RoutingCategory, "Output Y(A) is HIGH, the others LOW; A is a 2-bit bus.", b =>
            {
                PinRef[] a = b.Split(b.In("A", 2), 2);
                PinRef n0 = b.Not(a[0]);
                PinRef n1 = b.Not(a[1]);
                b.Out("Y0", b.And(n1, n0));
                b.Out("Y1", b.And(n1, a[0]));
                b.Out("Y2", b.And(a[1], n0));
                b.Out("Y3", b.And(a[1], a[0]));
            });
            Add("dec4", "Decoder 4→16", ExampleGatePack.RoutingCategory, "Output Y(A) is HIGH, the others LOW; A is a 4-bit bus. Two 2→4 decoders, ANDed.", b =>
            {
                PinRef[] a = b.Split(b.In("A", 4), 4);
                PinRef[] low = b.Gate("dec2", b.Merge(a[0], a[1]));
                PinRef[] high = b.Gate("dec2", b.Merge(a[2], a[3]));
                for (int i = 0; i < 16; i++)
                {
                    b.Out("Y" + i, b.And(high[i >> 2], low[i & 3]));
                }
            });

            // Arithmetic
            Add("half-adder", "Half Adder", ExampleGatePack.ArithmeticCategory, "Adds two bits: S = A XOR B, C = A AND B.", b =>
            {
                PinRef a = b.In("A");
                PinRef bb = b.In("B");
                b.Out("S", b.Gate("xor", a, bb)[0]);
                b.Out("C", b.And(a, bb));
            });
            Add("full-adder", "Full Adder", ExampleGatePack.ArithmeticCategory, "Adds two bits and a carry with two half adders.", b =>
            {
                PinRef a = b.In("A");
                PinRef bb = b.In("B");
                PinRef cin = b.In("CIN");
                PinRef[] first = b.Gate("half-adder", a, bb);
                PinRef[] second = b.Gate("half-adder", first[0], cin);
                b.Out("S", second[0]);
                b.Out("COUT", b.Gate("or", first[1], second[1])[0]);
            });
            Add("adder4", "Adder 4-bit", ExampleGatePack.ArithmeticCategory, "S = A + B + CIN over 4-bit buses, a ripple of four full adders.", b =>
            {
                PinRef[] a = b.Split(b.In("A", 4), 4);
                PinRef[] bb = b.Split(b.In("B", 4), 4);
                PinRef carry = b.In("CIN");
                var sum = new PinRef[4];
                for (int i = 0; i < 4; i++)
                {
                    PinRef[] fa = b.Gate("full-adder", a[i], bb[i], carry);
                    sum[i] = fa[0];
                    carry = fa[1];
                }

                b.Out("S", b.Merge(sum), 4);
                b.Out("COUT", carry);
            });
            Add("alu4", "ALU 4-bit", ExampleGatePack.ArithmeticCategory,
                "OP 0: A + B, 1: A - B, 2: A AND B, 3: A OR B. COUT is the adder carry (no borrow when subtracting), " +
                "0 for logic operations. ZERO is HIGH when Y is 0.", b =>
            {
                PinRef aBus = b.In("A", 4);
                PinRef bBus = b.In("B", 4);
                PinRef opBus = b.In("OP", 2);
                PinRef[] op = b.Split(opBus, 2);

                // Subtraction is A + NOT B + 1: XOR B with OP0 on every bit and carry OP0 in.
                PinRef bx = b.Label(b.Gate("xor4", bBus, b.Merge(op[0], op[0], op[0], op[0]))[0], "B or NOT B");
                PinRef[] adder = b.Gate("adder4", aBus, bx, op[0]);
                b.Label(adder[0], "Add / subtract");
                PinRef and = b.Gate("and4", aBus, bBus)[0];
                PinRef or = b.Gate("or4", aBus, bBus)[0];
                PinRef y = b.Label(b.Gate("mux4x4", opBus, adder[0], adder[0], and, or)[0], "Result (by OP)");

                b.Out("Y", y, 4);
                b.Out("COUT", b.And(adder[1], b.Not(op[1])));
                PinRef[] bits = b.Split(y, 4);
                PinRef any = b.Gate("or", b.Gate("or", bits[0], bits[1])[0], b.Gate("or", bits[2], bits[3])[0])[0];
                b.Out("ZERO", b.Label(b.Not(any), "Zero test"));
            });

            // Memory
            Add("d-latch", "D Latch", ExampleGatePack.MemoryCategory, "Q follows D while E is HIGH and holds while E is LOW. Cross-coupled NAND gates.", b =>
            {
                PinRef d = b.In("D");
                PinRef e = b.In("E");
                // The reset side is built first. Power-on (resolved mode) forces the first unknown net of each loop
                // LOW, in the order the cycle search reaches the loop's gates; entering through the reset side, that
                // net is inside QN's gate, so every latch, flip-flop, register and counter powers on storing 0.
                PinRef resetBar = b.Gate("nand", b.Not(d), e)[0];
                PinRef setBar = b.Gate("nand", d, e)[0];
                NodeDefinition qGate = b.Instance("nand");
                NodeDefinition qnGate = b.Instance("nand");
                PinRef q = b.OutputOf(qGate, 0);
                PinRef qn = b.OutputOf(qnGate, 0);
                b.Wire(setBar, b.InputOf(qGate, 0));
                b.Wire(qn, b.InputOf(qGate, 1));
                b.Wire(resetBar, b.InputOf(qnGate, 0));
                b.Wire(q, b.InputOf(qnGate, 1));
                b.Out("Q", q);
                b.Out("QN", qn);
            });
            Add("latch4", "Latch 4-bit", ExampleGatePack.MemoryCategory, "Four D latches sharing E: Q follows bus D while E is HIGH and holds while E is LOW.", b =>
            {
                PinRef[] d = b.Split(b.In("D", 4), 4);
                PinRef e = b.In("E");
                var q = new PinRef[4];
                for (int i = 0; i < 4; i++)
                {
                    q[i] = b.Gate("d-latch", d[i], e)[0];
                }

                b.Out("Q", b.Merge(q), 4);
            });
            Add("dff", "D Flip-Flop", ExampleGatePack.MemoryCategory, "Stores D on the rising edge of CLK. Master-slave pair of D latches.", b =>
            {
                PinRef d = b.In("D");
                PinRef clk = b.In("CLK");
                PinRef[] master = b.Gate("d-latch", d, b.Not(clk));
                PinRef[] slave = b.Gate("d-latch", master[0], clk);
                b.Label(master[0], "Master");
                b.Label(slave[0], "Slave");
                b.Out("Q", slave[0]);
                b.Out("QN", slave[1]);
            });
            Add("register4", "Register 4-bit", ExampleGatePack.MemoryCategory, "On a rising CLK edge Q takes D when LOAD is HIGH, otherwise keeps its value.", b =>
            {
                PinRef d = b.In("D", 4);
                PinRef load = b.In("LOAD");
                PinRef clk = b.In("CLK");
                var flipFlops = new NodeDefinition[4];
                var q = new PinRef[4];
                for (int i = 0; i < 4; i++)
                {
                    flipFlops[i] = b.Label(b.Instance("dff"), "Bit " + i);
                    q[i] = b.OutputOf(flipFlops[i], 0);
                }

                PinRef stored = b.Merge(q);
                PinRef[] next = b.Split(b.Label(b.Gate("mux2x4", load, stored, d)[0], "Load or keep"), 4);
                for (int i = 0; i < 4; i++)
                {
                    b.Wire(next[i], b.InputOf(flipFlops[i], 0));
                    b.Wire(clk, b.InputOf(flipFlops[i], 1));
                }

                b.Out("Q", stored, 4);
            });
            Add("counter4", "Counter 4-bit", ExampleGatePack.MemoryCategory,
                "Counts up by one on each rising CLK edge and wraps from 15 to 0. Synchronous: a register that loads " +
                "its own value plus one, so all bits change together.", b =>
            {
                PinRef clock = b.In("CLK");
                NodeDefinition register = b.Label(b.Instance("register4"), "Count");
                PinRef q = b.OutputOf(register, 0);
                PinRef next = b.Label(b.Gate("adder4", q, b.Const(4, 0), b.Const(1, 1))[0], "Count + 1");
                b.Wire(next, b.InputOf(register, 0));
                b.Wire(b.Const(1, 1), b.InputOf(register, 1));
                b.Wire(clock, b.InputOf(register, 2));
                b.Out("Q", q, 4);
            });
            Add("ram16x4", "RAM 16×4", ExampleGatePack.MemoryCategory,
                "16 words of 4 bits. DOUT always shows the word at ADDR. While WE and CLK are both HIGH, DIN is written " +
                "to that word. Change ADDR only while CLK is LOW.", b =>
            {
                PinRef addr = b.In("ADDR", 4);
                PinRef din = b.In("DIN", 4);
                PinRef we = b.In("WE");
                PinRef clk = b.In("CLK");
                PinRef[] a = b.Split(addr, 4);
                PinRef low = b.Merge(a[0], a[1]);
                PinRef high = b.Merge(a[2], a[3]);
                PinRef[] select = b.Gate("dec4", addr);
                b.Label(select[0], "Address decoder");
                PinRef write = b.And(we, clk);

                var words = new PinRef[16];
                for (int word = 0; word < 16; word++)
                {
                    words[word] = b.Label(b.Gate("latch4", din, b.And(select[word], write))[0], "Word " + word);
                }

                var groups = new PinRef[4];
                for (int g = 0; g < 4; g++)
                {
                    groups[g] = b.Label(b.Gate("mux4x4", low, words[g * 4], words[g * 4 + 1], words[g * 4 + 2],
                        words[g * 4 + 3])[0], "Words " + g * 4 + "-" + (g * 4 + 3));
                }

                b.Out("DOUT", b.Label(b.Gate("mux4x4", high, groups[0], groups[1], groups[2], groups[3])[0], "Read"), 4);
            });

            // Computer
            Add("rom16x8", "ROM 16×8", ExampleGatePack.ComputerCategory,
                "DATA is word W(ADDR). Wire a Constant to each W input to hold a program: the words survive Reset " +
                "and saving. A tree of five MUX 4:1 8-bit selects the word.", b =>
            {
                var words = new PinRef[16];
                for (int w = 0; w < 16; w++)
                {
                    words[w] = b.In("W" + w, 8);
                }

                PinRef[] a = b.Split(b.In("ADDR", 4), 4);
                PinRef low = b.Merge(a[0], a[1]);
                PinRef high = b.Merge(a[2], a[3]);
                var groups = new PinRef[4];
                for (int g = 0; g < 4; g++)
                {
                    groups[g] = b.Label(b.Gate("mux4x8", low, words[g * 4], words[g * 4 + 1], words[g * 4 + 2],
                        words[g * 4 + 3])[0], "W" + g * 4 + "-W" + (g * 4 + 3));
                }

                b.Out("DATA", b.Label(b.Gate("mux4x8", high, groups[0], groups[1], groups[2], groups[3])[0], "Read"), 8);
            });
            Add("cpu4", "CPU 4-bit", ExampleGatePack.ComputerCategory, CpuDescription, BuildCpu);
        }

        /// <summary>Instruction set of the CPU (opcode = INSTR bits 7–4, operand n = bits 3–0).</summary>
        public const string CpuDescription =
            "Single-cycle 4-bit accumulator CPU: one instruction per rising CLK edge, fetched from INSTR (wire PC to a " +
            "ROM's ADDR and its DATA to INSTR). Opcode = INSTR bits 7-4, n = bits 3-0. 0 NOP, 1 LDI n (A = n), " +
            "2 LDA n (A = RAM[n]), 3 STA n (RAM[n] = A), 4 ADD n, 5 SUB n (A = A ± RAM[n]), 6 ADDI n, 7 SUBI n " +
            "(A = A ± n), 8 JMP n, 9 JZ n, A JC n (jump if the last ADD/SUB gave zero / a carry), B OUT (OUT = A), " +
            "F HLT. Powers on with PC = 0 and A = 0.";

        /// <summary>
        /// Datapath of the CPU, all from earlier pack gates: a 4→16 decoder on the opcode, registers for PC, A and
        /// OUT, two flag flip-flops, the ALU, and a RAM 16×4 for data. Every register loads on the rising CLK edge;
        /// the RAM writes during the LOW half (its CLK is NOT CLK), while its address and data are stable.
        /// </summary>
        private void BuildCpu(Builder b)
        {
            PinRef[] instr = b.Split(b.In("INSTR", 8), 8);
            PinRef clk = b.In("CLK");
            PinRef n = b.Merge(instr[0], instr[1], instr[2], instr[3]);
            PinRef[] op = b.Gate("dec4", b.Merge(instr[4], instr[5], instr[6], instr[7]));
            b.Label(op[0], "Opcode decoder");

            PinRef Or(PinRef x, PinRef y) => b.Gate("or", x, y)[0];
            PinRef immediate = Or(op[1], Or(op[6], op[7])); // LDI, ADDI, SUBI use n itself
            PinRef arithmetic = Or(Or(op[4], op[5]), Or(op[6], op[7]));
            PinRef subtract = Or(op[5], op[7]);
            PinRef loadA = Or(arithmetic, Or(op[1], op[2]));

            // Registers are placed first so their outputs can feed the logic that computes their inputs.
            NodeDefinition pcRegister = b.Label(b.Instance("register4"), "PC");
            NodeDefinition aRegister = b.Label(b.Instance("register4"), "A");
            NodeDefinition outRegister = b.Label(b.Instance("register4"), "OUT");
            NodeDefinition zeroFlag = b.Label(b.Instance("dff"), "Z flag");
            NodeDefinition carryFlag = b.Label(b.Instance("dff"), "C flag");
            PinRef pc = b.OutputOf(pcRegister, 0);
            PinRef a = b.OutputOf(aRegister, 0);
            PinRef zero = b.OutputOf(zeroFlag, 0);
            PinRef carry = b.OutputOf(carryFlag, 0);

            // Operand: n for immediates, otherwise the RAM word at n. The RAM writes A during the LOW half of STA.
            PinRef ram = b.Label(b.Gate("ram16x4", n, a, op[3], b.Not(clk))[0], "Data RAM");
            PinRef operand = b.Label(b.Gate("mux2x4", immediate, ram, n)[0], "Operand");
            PinRef[] alu = b.Gate("alu4", a, operand, b.Merge(subtract, b.Const(1, 0)));
            b.Label(alu[0], "ALU");
            PinRef nextA = b.Label(b.Gate("mux2x4", arithmetic, operand, alu[0])[0], "Next A");

            b.Wire(nextA, b.InputOf(aRegister, 0));
            b.Wire(loadA, b.InputOf(aRegister, 1));
            b.Wire(clk, b.InputOf(aRegister, 2));

            // Flags change only on arithmetic instructions.
            b.Wire(b.Gate("mux2", arithmetic, zero, alu[2])[0], b.InputOf(zeroFlag, 0));
            b.Wire(clk, b.InputOf(zeroFlag, 1));
            b.Wire(b.Gate("mux2", arithmetic, carry, alu[1])[0], b.InputOf(carryFlag, 0));
            b.Wire(clk, b.InputOf(carryFlag, 1));

            // PC: n when a jump is taken, otherwise PC + 1 (PC + 0 while halted).
            PinRef halt = op[15];
            PinRef taken = Or(op[8], Or(b.And(op[9], zero), b.And(op[10], carry)));
            PinRef next = b.Label(b.Gate("adder4", pc, b.Const(4, 0), b.Not(halt))[0], "PC + 1");
            PinRef nextPc = b.Label(b.Gate("mux2x4", taken, next, n)[0], "Next PC");

            b.Wire(nextPc, b.InputOf(pcRegister, 0));
            b.Wire(b.Const(1, 1), b.InputOf(pcRegister, 1));
            b.Wire(clk, b.InputOf(pcRegister, 2));

            b.Wire(a, b.InputOf(outRegister, 0));
            b.Wire(op[11], b.InputOf(outRegister, 1));
            b.Wire(clk, b.InputOf(outRegister, 2));

            b.Out("PC", pc, 4);
            b.Out("A", a, 4);
            b.Out("OUT", b.OutputOf(outRegister, 0), 4);
            b.Out("HALT", halt);
        }

        /// <summary>A 4-bit bitwise gate: <paramref name="bit"/> applied to each pair of bits of A and B.</summary>
        private void AddBitwise(string key, string name, string description, Func<Builder, PinRef, PinRef, PinRef> bit)
        {
            Add(key, name, ExampleGatePack.LogicCategory, description, b =>
            {
                PinRef[] a = b.Split(b.In("A", 4), 4);
                PinRef[] bb = b.Split(b.In("B", 4), 4);
                var y = new PinRef[4];
                for (int i = 0; i < 4; i++)
                {
                    y[i] = bit(b, a[i], bb[i]);
                }

                b.Out("Y", b.Merge(y), 4);
            });
        }

        /// <summary>A 4:1 multiplexer on <paramref name="width"/>-bit buses: one MUX 4:1 per bit, sharing S.</summary>
        private void AddBusMux4(string key, string name, int width)
        {
            Add(key, name, ExampleGatePack.RoutingCategory,
                "OUT = bus number S (I0 … I3) on " + width + "-bit buses; S is a 2-bit bus. One MUX 4:1 per bit.", b =>
            {
                PinRef select = b.In("S", 2);
                var inputs = new PinRef[4][];
                for (int i = 0; i < 4; i++)
                {
                    inputs[i] = b.Split(b.In("I" + i, width), width);
                }

                var bits = new PinRef[width];
                for (int bit = 0; bit < width; bit++)
                {
                    bits[bit] = b.Gate("mux4", select, inputs[0][bit], inputs[1][bit], inputs[2][bit], inputs[3][bit])[0];
                }

                b.Out("OUT", b.Merge(bits), width);
            });
        }

        private void Add(string key, string name, string category, string description, Action<Builder> build)
        {
            var circuit = new CircuitDefinition(new DefinitionId(ExampleGatePack.IdPrefix + key), _nameFor?.Invoke(name) ?? name)
            {
                Category = category,
                Description = description,
            };
            build(new Builder(this, circuit));
            LayeredLayout.Arrange(circuit, node => _sizer(Catalog, circuit, node), _pins);
            Library.Add(circuit);
            _definitions.Add(circuit);
            _byKey[key] = circuit;
        }

        /// <summary>Wiring helpers for one gate under construction. Positions are set afterwards by the layout.</summary>
        private sealed class Builder
        {
            private static readonly ComponentRef AndRef = ComponentRef.Primitive(BuiltInComponentIds.And);
            private static readonly ComponentRef NotRef = ComponentRef.Primitive(BuiltInComponentIds.Not);

            private readonly ExamplePack _pack;
            private readonly CircuitDefinition _circuit;

            public Builder(ExamplePack pack, CircuitDefinition circuit)
            {
                _pack = pack;
                _circuit = circuit;
            }

            public PinRef In(string name, int width = 1)
            {
                return new PinRef(_circuit.AddInputBoundary(name, CircuitPoint.Zero, width).Id, BoundarySignatures.Pin);
            }

            public void Out(string name, PinRef source, int width = 1)
            {
                NodeDefinition output = _circuit.AddOutputBoundary(name, CircuitPoint.Zero, width);
                Wire(source, new PinRef(output.Id, BoundarySignatures.Pin));
            }

            public void Wire(PinRef source, PinRef destination)
            {
                _circuit.Connect(source, destination);
            }

            /// <summary>Names the node that drives <paramref name="pin"/> (drawn above it); returns the pin.</summary>
            public PinRef Label(PinRef pin, string text)
            {
                _circuit.SetNodeLabel(pin.Node, text);
                return pin;
            }

            /// <summary>Names <paramref name="node"/> (drawn above it); returns it.</summary>
            public NodeDefinition Label(NodeDefinition node, string text)
            {
                _circuit.SetNodeLabel(node.Id, text);
                return node;
            }

            public PinRef Not(PinRef input)
            {
                NodeDefinition not = _circuit.AddNode(NotRef, CircuitPoint.Zero);
                Wire(input, new PinRef(not.Id, NotGate.Input));
                return new PinRef(not.Id, NotGate.Output);
            }

            /// <summary>A fixed value (a Constant utility, itself compiled to AND/NOT gates).</summary>
            public PinRef Const(int width, int value)
            {
                NodeDefinition constant = _circuit.AddNode(ComponentRef.Constant(width, value), CircuitPoint.Zero);
                return new PinRef(constant.Id, BoundarySignatures.Pin);
            }

            public PinRef And(PinRef a, PinRef b)
            {
                NodeDefinition and = _circuit.AddNode(AndRef, CircuitPoint.Zero);
                Wire(a, new PinRef(and.Id, AndGate.InputA));
                Wire(b, new PinRef(and.Id, AndGate.InputB));
                return new PinRef(and.Id, AndGate.Output);
            }

            /// <summary>An unconnected instance of an earlier pack gate (for feedback wiring).</summary>
            public NodeDefinition Instance(string key)
            {
                return _circuit.AddNode(ComponentRef.Custom(_pack.Get(key).Reference), CircuitPoint.Zero);
            }

            public PinRef InputOf(NodeDefinition instance, int index)
            {
                return new PinRef(instance.Id, _pack.Library.TryGetDefinition(instance.Component.Definition,
                    out CircuitDefinition definition) ? definition.Inputs[index].Id : default);
            }

            public PinRef OutputOf(NodeDefinition instance, int index)
            {
                return new PinRef(instance.Id, _pack.Library.TryGetDefinition(instance.Component.Definition,
                    out CircuitDefinition definition) ? definition.Outputs[index].Id : default);
            }

            /// <summary>An instance of an earlier pack gate with its inputs wired in order; returns its outputs.</summary>
            public PinRef[] Gate(string key, params PinRef[] inputs)
            {
                CircuitDefinition definition = _pack.Get(key);
                if (inputs.Length != definition.Inputs.Count)
                {
                    throw new ArgumentException(definition.Name + " has " + definition.Inputs.Count + " inputs, not " +
                                                inputs.Length + ".");
                }

                NodeDefinition instance = _circuit.AddNode(ComponentRef.Custom(definition.Reference), CircuitPoint.Zero);
                for (int i = 0; i < inputs.Length; i++)
                {
                    Wire(inputs[i], new PinRef(instance.Id, definition.Inputs[i].Id));
                }

                var outputs = new PinRef[definition.Outputs.Count];
                for (int i = 0; i < outputs.Length; i++)
                {
                    outputs[i] = new PinRef(instance.Id, definition.Outputs[i].Id);
                }

                return outputs;
            }

            /// <summary>Splits a bus into its bits (index 0 = least significant).</summary>
            public PinRef[] Split(PinRef bus, int width)
            {
                NodeDefinition splitter = _circuit.AddNode(ComponentRef.Splitter(width), CircuitPoint.Zero);
                Wire(bus, new PinRef(splitter.Id, BoundarySignatures.Pin));
                var bits = new PinRef[width];
                for (int i = 0; i < width; i++)
                {
                    bits[i] = new PinRef(splitter.Id, BoundarySignatures.BitPin(i));
                }

                return bits;
            }

            /// <summary>Merges bits (index 0 = least significant) into a bus.</summary>
            public PinRef Merge(params PinRef[] bits)
            {
                NodeDefinition merger = _circuit.AddNode(ComponentRef.Merger(bits.Length), CircuitPoint.Zero);
                for (int i = 0; i < bits.Length; i++)
                {
                    Wire(bits[i], new PinRef(merger.Id, BoundarySignatures.BitPin(i)));
                }

                return new PinRef(merger.Id, BoundarySignatures.Pin);
            }
        }
    }
}
