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
        private readonly Func<string, string> _nameFor;

        internal ExamplePack(PrimitiveRegistry primitives, NodeSizer sizer, Func<string, string> nameFor)
        {
            Library = new DefinitionLibrary();
            Catalog = new ComponentCatalog(primitives, Library);
            _sizer = sizer;
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
                PinRef[] a = b.Split(aBus, 4);
                PinRef[] bb = b.Split(b.In("B", 4), 4);
                PinRef opBus = b.In("OP", 2);
                PinRef[] op = b.Split(opBus, 2);

                // Subtraction is A + NOT B + 1: XOR each B bit with OP0 and carry OP0 in.
                var bx = new PinRef[4];
                for (int i = 0; i < 4; i++)
                {
                    bx[i] = b.Gate("xor", bb[i], op[0])[0];
                }

                PinRef[] adder = b.Gate("adder4", aBus, b.Merge(bx), op[0]);
                PinRef[] sum = b.Split(adder[0], 4);
                var y = new PinRef[4];
                for (int i = 0; i < 4; i++)
                {
                    PinRef and = b.And(a[i], bb[i]);
                    PinRef or = b.Gate("or", a[i], bb[i])[0];
                    y[i] = b.Gate("mux4", opBus, sum[i], sum[i], and, or)[0];
                }

                b.Out("Y", b.Merge(y), 4);
                b.Out("COUT", b.And(adder[1], b.Not(op[1])));
                PinRef any = b.Gate("or", b.Gate("or", y[0], y[1])[0], b.Gate("or", y[2], y[3])[0])[0];
                b.Out("ZERO", b.Not(any));
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
            Add("dff", "D Flip-Flop", ExampleGatePack.MemoryCategory, "Stores D on the rising edge of CLK. Master-slave pair of D latches.", b =>
            {
                PinRef d = b.In("D");
                PinRef clk = b.In("CLK");
                PinRef[] master = b.Gate("d-latch", d, b.Not(clk));
                PinRef[] slave = b.Gate("d-latch", master[0], clk);
                b.Out("Q", slave[0]);
                b.Out("QN", slave[1]);
            });
            Add("register4", "Register 4-bit", ExampleGatePack.MemoryCategory, "On a rising CLK edge Q takes D when LOAD is HIGH, otherwise keeps its value.", b =>
            {
                PinRef[] d = b.Split(b.In("D", 4), 4);
                PinRef load = b.In("LOAD");
                PinRef clk = b.In("CLK");
                var q = new PinRef[4];
                for (int i = 0; i < 4; i++)
                {
                    NodeDefinition flipFlop = b.Instance("dff");
                    q[i] = b.OutputOf(flipFlop, 0);
                    PinRef next = b.Gate("mux2", load, q[i], d[i])[0];
                    b.Wire(next, b.InputOf(flipFlop, 0));
                    b.Wire(clk, b.InputOf(flipFlop, 1));
                }

                b.Out("Q", b.Merge(q), 4);
            });
            Add("counter4", "Counter 4-bit", ExampleGatePack.MemoryCategory,
                "Counts up by one on each rising CLK edge and wraps from 15 to 0. Synchronous: a register that loads " +
                "its own value plus one, so all bits change together.", b =>
            {
                PinRef clock = b.In("CLK");
                NodeDefinition register = b.Instance("register4");
                PinRef q = b.OutputOf(register, 0);
                PinRef next = b.Gate("adder4", q, b.Const(4, 0), b.Const(1, 1))[0];
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
                PinRef[] din = b.Split(b.In("DIN", 4), 4);
                PinRef we = b.In("WE");
                PinRef clk = b.In("CLK");
                PinRef[] a = b.Split(addr, 4);
                PinRef low = b.Merge(a[0], a[1]);
                PinRef high = b.Merge(a[2], a[3]);
                PinRef[] select = b.Gate("dec4", addr);
                PinRef write = b.And(we, clk);

                var cells = new PinRef[16, 4];
                for (int word = 0; word < 16; word++)
                {
                    PinRef enable = b.And(select[word], write);
                    for (int bit = 0; bit < 4; bit++)
                    {
                        cells[word, bit] = b.Gate("d-latch", din[bit], enable)[0];
                    }
                }

                var dout = new PinRef[4];
                for (int bit = 0; bit < 4; bit++)
                {
                    var groups = new PinRef[4];
                    for (int g = 0; g < 4; g++)
                    {
                        groups[g] = b.Gate("mux4", low, cells[g * 4, bit], cells[g * 4 + 1, bit], cells[g * 4 + 2, bit],
                            cells[g * 4 + 3, bit])[0];
                    }

                    dout[bit] = b.Gate("mux4", high, groups[0], groups[1], groups[2], groups[3])[0];
                }

                b.Out("DOUT", b.Merge(dout), 4);
            });

            // Computer
            Add("rom16x8", "ROM 16×8", ExampleGatePack.ComputerCategory,
                "DATA is word W(ADDR). Wire a Constant to each W input to hold a program: the words survive Reset " +
                "and saving. Each data bit is a tree of five MUX 4:1.", b =>
            {
                var words = new PinRef[16][];
                for (int w = 0; w < 16; w++)
                {
                    words[w] = b.Split(b.In("W" + w, 8), 8);
                }

                PinRef[] a = b.Split(b.In("ADDR", 4), 4);
                PinRef low = b.Merge(a[0], a[1]);
                PinRef high = b.Merge(a[2], a[3]);
                var data = new PinRef[8];
                for (int bit = 0; bit < 8; bit++)
                {
                    var groups = new PinRef[4];
                    for (int g = 0; g < 4; g++)
                    {
                        groups[g] = b.Gate("mux4", low, words[g * 4][bit], words[g * 4 + 1][bit], words[g * 4 + 2][bit],
                            words[g * 4 + 3][bit])[0];
                    }

                    data[bit] = b.Gate("mux4", high, groups[0], groups[1], groups[2], groups[3])[0];
                }

                b.Out("DATA", b.Merge(data), 8);
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
            PinRef[] nBits = { instr[0], instr[1], instr[2], instr[3] };
            PinRef[] op = b.Gate("dec4", b.Merge(instr[4], instr[5], instr[6], instr[7]));

            PinRef Or(PinRef x, PinRef y) => b.Gate("or", x, y)[0];
            PinRef immediate = Or(op[1], Or(op[6], op[7])); // LDI, ADDI, SUBI use n itself
            PinRef arithmetic = Or(Or(op[4], op[5]), Or(op[6], op[7]));
            PinRef subtract = Or(op[5], op[7]);
            PinRef loadA = Or(arithmetic, Or(op[1], op[2]));

            // Registers are placed first so their outputs can feed the logic that computes their inputs.
            NodeDefinition pcRegister = b.Instance("register4");
            NodeDefinition aRegister = b.Instance("register4");
            NodeDefinition outRegister = b.Instance("register4");
            NodeDefinition zeroFlag = b.Instance("dff");
            NodeDefinition carryFlag = b.Instance("dff");
            PinRef pc = b.OutputOf(pcRegister, 0);
            PinRef a = b.OutputOf(aRegister, 0);
            PinRef zero = b.OutputOf(zeroFlag, 0);
            PinRef carry = b.OutputOf(carryFlag, 0);

            // Operand: n for immediates, otherwise the RAM word at n. The RAM writes A during the LOW half of STA.
            PinRef[] ram = b.Split(b.Gate("ram16x4", n, a, op[3], b.Not(clk))[0], 4);
            var operandBits = new PinRef[4];
            for (int i = 0; i < 4; i++)
            {
                operandBits[i] = b.Gate("mux2", immediate, ram[i], nBits[i])[0];
            }

            PinRef operand = b.Merge(operandBits);
            PinRef[] alu = b.Gate("alu4", a, operand, b.Merge(subtract, b.Const(1, 0)));
            PinRef[] y = b.Split(alu[0], 4);
            var nextA = new PinRef[4];
            for (int i = 0; i < 4; i++)
            {
                nextA[i] = b.Gate("mux2", arithmetic, operandBits[i], y[i])[0];
            }

            b.Wire(b.Merge(nextA), b.InputOf(aRegister, 0));
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
            PinRef[] next = b.Split(b.Gate("adder4", pc, b.Const(4, 0), b.Not(halt))[0], 4);
            var nextPc = new PinRef[4];
            for (int i = 0; i < 4; i++)
            {
                nextPc[i] = b.Gate("mux2", taken, next[i], nBits[i])[0];
            }

            b.Wire(b.Merge(nextPc), b.InputOf(pcRegister, 0));
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

        private void Add(string key, string name, string category, string description, Action<Builder> build)
        {
            var circuit = new CircuitDefinition(new DefinitionId(ExampleGatePack.IdPrefix + key), _nameFor?.Invoke(name) ?? name)
            {
                Category = category,
                Description = description,
            };
            build(new Builder(this, circuit));
            LayeredLayout.Arrange(circuit, node => _sizer(Catalog, circuit, node));
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
