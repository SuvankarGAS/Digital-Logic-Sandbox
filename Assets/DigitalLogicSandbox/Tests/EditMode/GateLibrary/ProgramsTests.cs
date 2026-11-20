using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using DigitalLogicSandbox.Commands.Programs;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.GateLibrary.Programs;
using NUnit.Framework;

namespace DigitalLogicSandbox.Tests.EditMode.GateLibrary
{
    /// <summary>User instruction sets: spec parsing, the generic assembler, syntax colouring and the project library.</summary>
    public sealed class ProgramsTests
    {
        private const string TinySpec = "opcode 2\nNOP 0 ; idle\nLDI 1 n ; A = n\nJMP 2 n\nHLT 3";

        private static InstructionSet Tiny()
        {
            InstructionSetParseResult result = InstructionSet.Parse(TinySpec, "Tiny");
            Assert.That(result.HasErrors, Is.False, string.Join("\n", result.Errors));
            return result.Set;
        }

        // ------------------------------------------------------------------------------------------------
        // Instruction set specs
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void ExampleCpuSpec_EncodesExactlyLikeTheOriginalTable_ForAll256Words()
        {
            string[] original =
            {
                "NOP", "LDI", "LDA", "STA", "ADD", "SUB", "ADDI", "SUBI", "JMP", "JZ", "JC", "OUT", null, null, null, "HLT",
            };
            var assembler = new Assembler(InstructionSet.ExampleCpu, 8);
            for (int word = 0; word < 256; word++)
            {
                string mnemonic = original[word >> 4];
                bool takesOperand = mnemonic != null && mnemonic != "NOP" && mnemonic != "OUT" && mnemonic != "HLT";
                string expected = mnemonic == null || (!takesOperand && (word & 15) != 0)
                    ? "? 0x" + word.ToString("X2")
                    : takesOperand ? mnemonic + " " + (word & 15) : mnemonic;
                Assert.That(assembler.Disassemble(word), Is.EqualTo(expected), "Word " + word);
            }
        }

        [Test]
        public void Spec_ParsesOpcodeWidth_OperandsAndDescriptions()
        {
            InstructionSet set = Tiny();
            Assert.That(set.Name, Is.EqualTo("Tiny"));
            Assert.That(set.OpcodeBits, Is.EqualTo(2));
            Assert.That(set.Instructions.Count, Is.EqualTo(4));
            Assert.That(set.TryGetInstruction("ldi", out Instruction ldi), Is.True, "Case-insensitive.");
            Assert.That(ldi.Opcode, Is.EqualTo(1));
            Assert.That(ldi.TakesOperand, Is.True);
            Assert.That(ldi.Description, Is.EqualTo("A = n"));
            Assert.That(set.TryGetInstruction(3, out Instruction hlt), Is.True);
            Assert.That(hlt.TakesOperand, Is.False);
            Assert.That(set.Reference(6), Does.Contain("LDI n (A = n)").And.Contain("n is 0–15"));
        }

        [TestCase("NOP 0", 1, "Start with 'opcode N'")]
        [TestCase("opcode 9\nNOP 0", 1, "opcode N")]
        [TestCase("opcode 2\nNOP 0\nNOP 1", 3, "defined twice")]
        [TestCase("opcode 2\nNOP 0\nHLT 0", 3, "already used")]
        [TestCase("opcode 2\nBIG 4", 2, "does not fit")]
        [TestCase("opcode 2\n1AB 0", 2, "not a mnemonic")]
        [TestCase("opcode 2\nLDI 1 n extra", 2, "Write LDI")]
        [TestCase("opcode 2\nLDI x", 2, "not an opcode")]
        [TestCase("opcode 2\nNOP 0\nopcode 3", 3, "already set")]
        [TestCase("opcode 2\n; nothing yet", 2, "at least one instruction")]
        public void Spec_ReportsErrorsOnTheRightLine(string spec, int line, string message)
        {
            InstructionSetParseResult result = InstructionSet.Parse(spec, "Bad");
            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Set, Is.Null);
            Assert.That(result.Errors[0].Line, Is.EqualTo(line));
            Assert.That(result.Errors[0].Message, Does.Contain(message));
        }

        [Test]
        public void Spec_ThatDoesNotFitTheWords_IsReportedByTheAssembler()
        {
            Assert.That(InstructionSet.ExampleCpu.CheckWordWidth(8), Is.Null);
            var narrow = new Assembler(InstructionSet.ExampleCpu, 4);
            Assert.That(narrow.Problem, Does.Contain("no room for operands"));
            AssemblyResult result = narrow.Parse("NOP", 4);
            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Errors[0].Line, Is.EqualTo(0));
            Assert.That(result.Errors[0].ToString(), Does.Not.StartWith("Line"));
            Assert.That(narrow.Disassemble(5), Is.EqualTo("0x5"), "Listings fall back to raw words.");
        }

        // ------------------------------------------------------------------------------------------------
        // Assembler
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void UserSet_AssemblesSixBitWords()
        {
            var assembler = new Assembler(Tiny(), 6);
            Assert.That(assembler.MaxOperand, Is.EqualTo(15));
            AssemblyResult result = assembler.Parse("LDI 15\n  jmp 0 ; loop\nHLT", 4);
            Assert.That(result.HasErrors, Is.False, string.Join("\n", result.Errors));
            Assert.That(result.Words, Is.EqualTo(new[] { 31, 32, 48, 0 }));
            Assert.That(result.PaddedWords, Is.EqualTo(1));
            Assert.That(assembler.Disassemble(0), Is.EqualTo("NOP"));
            Assert.That(assembler.Disassemble(0x31), Is.EqualTo("? 0x31"), "Stray operand bits are kept.");
            Assert.That(assembler.WordLabel(2, 48), Is.EqualTo("2: HLT"));
            Assert.That(assembler.IsWordLabel("1: JMP 0", out int address), Is.True);
            Assert.That(address, Is.EqualTo(1));

            AssemblyResult bad = assembler.Parse("LDI 16\n64", 4);
            Assert.That(bad.Errors.Count, Is.EqualTo(2));
            Assert.That(bad.Errors[0].Message, Does.Contain("0 to 15"));
            Assert.That(bad.Errors[1].Message, Does.Contain("6-bit word"));
        }

        [Test]
        public void UserSet_ListingParsesBack_ForEveryWord()
        {
            var assembler = new Assembler(Tiny(), 6);
            var words = new int[64];
            for (int i = 0; i < words.Length; i++)
            {
                words[i] = i;
            }

            AssemblyResult result = assembler.Parse(assembler.Listing(words), 64);
            Assert.That(result.HasErrors, Is.False, string.Join("\n", result.Errors));
            Assert.That(result.Words, Is.EqualTo(words));
        }

        [Test]
        public void RawWords_AreNumbersOnly()
        {
            var assembler = new Assembler(null, 4);
            Assert.That(assembler.Listing(new[] { 10, 3 }), Is.EqualTo("0: 0xA\n1: 0x3"));
            AssemblyResult result = assembler.Parse("0xA\n0b11", 2);
            Assert.That(result.HasErrors, Is.False, string.Join("\n", result.Errors));
            Assert.That(result.Words, Is.EqualTo(new[] { 10, 3 }));
            Assert.That(assembler.Parse("LDI 1", 2).Errors[0].Message, Does.Contain("numbers only"));
            Assert.That(assembler.Parse("16", 2).Errors[0].Message, Does.Contain("4-bit word"));
            Assert.That(assembler.Reference, Does.StartWith("Raw words"));
            Assert.That(assembler.IsWordLabel("0: 0xA", out _), Is.True);
        }

        // ------------------------------------------------------------------------------------------------
        // Syntax colouring
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void ProgramTokens_CoverAddressMnemonicNumberAndComment()
        {
            const string text = "3: LDI 0x2 ; load\nFOO 1\n? 0xC3";
            var tokens = new List<SyntaxToken>();
            ProgramSyntax.TokenizeProgram(text, InstructionSet.ExampleCpu, tokens);
            Assert.That(tokens, Is.EqualTo(new[]
            {
                new SyntaxToken(0, 2, SyntaxKind.Address),
                new SyntaxToken(3, 3, SyntaxKind.Mnemonic),
                new SyntaxToken(7, 3, SyntaxKind.Number),
                new SyntaxToken(11, 6, SyntaxKind.Comment),
                new SyntaxToken(18, 3, SyntaxKind.UnknownWord),
                new SyntaxToken(22, 1, SyntaxKind.Number),
                new SyntaxToken(24, 1, SyntaxKind.Marker),
                new SyntaxToken(26, 4, SyntaxKind.Number),
            }));

            ProgramSyntax.TokenizeProgram("ldi 1", null, tokens);
            Assert.That(tokens[0].Kind, Is.EqualTo(SyntaxKind.UnknownWord), "Raw words know no mnemonics.");
        }

        [Test]
        public void SpecTokens_CoverKeywordMnemonicOperandAndComment()
        {
            var tokens = new List<SyntaxToken>();
            ProgramSyntax.TokenizeSpec("opcode 4 ; x\nLDI 1 n ; y", tokens);
            var kinds = new List<SyntaxKind>();
            for (int i = 0; i < tokens.Count; i++)
            {
                kinds.Add(tokens[i].Kind);
            }

            Assert.That(kinds, Is.EqualTo(new[]
            {
                SyntaxKind.Keyword, SyntaxKind.Number, SyntaxKind.Comment,
                SyntaxKind.Mnemonic, SyntaxKind.Number, SyntaxKind.Operand, SyntaxKind.Comment,
            }));
        }

        [Test]
        public void RichText_ShowsExactlyTheSource_AndCannotBeInjected()
        {
            const string text = "LDI 1 ; <b>bold</b> </color> <noparse>\n\nFOO";
            var tokens = new List<SyntaxToken>();
            ProgramSyntax.TokenizeProgram(text, InstructionSet.ExampleCpu, tokens);
            var colors = new string[ProgramSyntax.KindCount];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = "A1B2C3";
            }

            var builder = new StringBuilder();
            ProgramSyntax.AppendRichText(builder, text, tokens, colors);
            string rich = builder.ToString();
            Assert.That(rich, Does.StartWith("<color=#A1B2C3>LDI</color>"));
            string visible = Regex.Replace(rich, "<color=#[0-9A-F]{6}>|</color>", string.Empty)
                .Replace("<noparse><</noparse>", "<");
            Assert.That(visible, Is.EqualTo(text));
            Assert.That(ProgramSyntax.LineCount(text), Is.EqualTo(3));
            Assert.That(ProgramSyntax.LineCount(string.Empty), Is.EqualTo(1));
        }

        // ------------------------------------------------------------------------------------------------
        // Library and commands
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void Resolve_UsesBinding_ThenEightBitDefault_ThenRaw()
        {
            var library = new InstructionSetLibrary();
            var rom = new DefinitionId("rom");
            Assert.That(library.Resolve(rom, 8).Id, Is.EqualTo(InstructionSetLibrary.ExampleCpuId));
            Assert.That(library.Resolve(rom, 8).IsBound, Is.False);
            Assert.That(library.Resolve(rom, 6).Id, Is.EqualTo(InstructionSetLibrary.RawId));
            Assert.That(library.Resolve(rom, 6).Set, Is.Null);

            library.Insert(0, new InstructionSetEntry("tiny", "Tiny", TinySpec));
            library.SetBinding(rom, "tiny");
            ResolvedInstructionSet bound = library.Resolve(rom, 6);
            Assert.That(bound.Id, Is.EqualTo("tiny"));
            Assert.That(bound.IsBound, Is.True);
            Assert.That(bound.IsUserSet, Is.True);
            Assert.That(library.AssemblerFor(rom, 6).Disassemble(48), Is.EqualTo("HLT"));

            library.SetBinding(rom, InstructionSetLibrary.RawId);
            Assert.That(library.Resolve(rom, 8).Set, Is.Null, "Raw can be chosen for 8-bit words too.");

            library.SetBinding(rom, "tiny");
            library.Remove("tiny");
            Assert.That(library.Resolve(rom, 8).Id, Is.EqualTo(InstructionSetLibrary.ExampleCpuId),
                "A binding to a deleted set falls back to the default.");
        }

        [Test]
        public void InvalidStoredSpec_ResolvesToRawWords_UnderItsName()
        {
            var library = new InstructionSetLibrary();
            var rom = new DefinitionId("rom");
            library.Reset(new[] { new InstructionSetEntry("bad", "Broken", "LDI 1") },
                new[] { new KeyValuePair<DefinitionId, string>(rom, "bad") });
            ResolvedInstructionSet resolved = library.Resolve(rom, 8);
            Assert.That(resolved.Name, Is.EqualTo("Broken"));
            Assert.That(resolved.Set, Is.Null);
            Assert.That(library.Sets[0].Errors, Is.Not.Empty);
        }

        [Test]
        public void Commands_AddEditBindAndRemove_AreUndoable()
        {
            var library = new InstructionSetLibrary();
            var history = new CommandHistory();
            var rom = new DefinitionId("rom");
            int changes = 0;
            library.Changed += () => changes++;

            history.Execute(new AddInstructionSetCommand(library, new InstructionSetEntry("tiny", "Tiny", TinySpec)));
            history.Execute(new BindInstructionSetCommand(library, rom, "tiny", "Tiny"));
            history.Execute(new EditInstructionSetCommand(library,
                new InstructionSetEntry("tiny", "Tiny 2", TinySpec + "\n")));
            Assert.That(library.Sets[0].Name, Is.EqualTo("Tiny 2"));
            Assert.That(changes, Is.EqualTo(3));

            history.Execute(new RemoveInstructionSetCommand(library, "tiny"));
            Assert.That(library.Sets, Is.Empty);
            Assert.That(library.BindingOf(rom), Is.Null, "Bindings to a deleted set are removed with it.");

            Assert.That(history.Undo(), Is.True);
            Assert.That(library.Sets[0].Name, Is.EqualTo("Tiny 2"));
            Assert.That(library.BindingOf(rom), Is.EqualTo("tiny"), "Undo restores the bindings too.");
            Assert.That(history.Undo(), Is.True);
            Assert.That(library.Sets[0].Name, Is.EqualTo("Tiny"));
            Assert.That(history.Undo(), Is.True);
            Assert.That(library.BindingOf(rom), Is.Null);
            Assert.That(history.Undo(), Is.True);
            Assert.That(library.Sets, Is.Empty);
            Assert.That(history.Redo(), Is.True);
            Assert.That(library.Sets[0].Name, Is.EqualTo("Tiny"));
        }
    }
}
