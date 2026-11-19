using DigitalLogicSandbox.GateLibrary.Examples;
using NUnit.Framework;

namespace DigitalLogicSandbox.Tests.EditMode.GateLibrary
{
    /// <summary>The CPU 4-bit's text assembler: parsing, errors, listing and word labels.</summary>
    public sealed class CpuAssemblerTests
    {
        [Test]
        public void Listing_ParsesBackToTheSameWords_ForAll256Words()
        {
            var words = new int[256];
            for (int i = 0; i < 256; i++)
            {
                words[i] = i;
            }

            AssemblyResult result = CpuAssembler.Parse(CpuAssembler.Listing(words), 256);
            Assert.That(result.HasErrors, Is.False, string.Join("\n", result.Errors));
            for (int i = 0; i < 256; i++)
            {
                Assert.That(result.Words[i], Is.EqualTo(i), "Word " + i);
            }
        }

        [Test]
        public void Parse_IgnoresCommentsAndBlankLines_AndPadsWithNop()
        {
            AssemblyResult result = CpuAssembler.Parse("; countdown\n\n  ldi 0x9 ; start\nOUT\nsubi 0b1\n\nJZ 5\nJMP 1\nHLT");
            Assert.That(result.HasErrors, Is.False, string.Join("\n", result.Errors));
            Assert.That(result.Words[0], Is.EqualTo(0x19));
            Assert.That(result.Words[1], Is.EqualTo(0xB0));
            Assert.That(result.Words[2], Is.EqualTo(0x71));
            Assert.That(result.Words[3], Is.EqualTo(0x95));
            Assert.That(result.Words[4], Is.EqualTo(0x81));
            Assert.That(result.Words[5], Is.EqualTo(0xF0));
            Assert.That(result.PaddedWords, Is.EqualTo(10));
            for (int i = 6; i < 16; i++)
            {
                Assert.That(result.Words[i], Is.EqualTo(0), "NOP padding");
            }
        }

        [Test]
        public void Parse_AcceptsAddressPrefixesAndRawWords()
        {
            AssemblyResult result = CpuAssembler.Parse("0: LDI 1\n1: 0x2A\n2: ? 0xC3\n3: 200", 4);
            Assert.That(result.HasErrors, Is.False, string.Join("\n", result.Errors));
            Assert.That(result.Words, Is.EqualTo(new byte[] { 0x11, 0x2A, 0xC3, 200 }));
            Assert.That(result.PaddedWords, Is.EqualTo(0));
        }

        [TestCase("FOO 1", 1, "Unknown instruction")]
        [TestCase("LDI 16", 1, "out of range")]
        [TestCase("LDI", 1, "needs an operand")]
        [TestCase("OUT 3", 1, "takes no operand")]
        [TestCase("NOP\n5: NOP", 2, "out of order")]
        [TestCase("NOP\n\n0x100", 3, "8-bit word")]
        public void Parse_ReportsErrorsOnTheRightLine(string source, int line, string message)
        {
            AssemblyResult result = CpuAssembler.Parse(source);
            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Errors[0].Line, Is.EqualTo(line));
            Assert.That(result.Errors[0].Message, Does.Contain(message));
        }

        [Test]
        public void Parse_RefusesMoreInstructionsThanWords()
        {
            AssemblyResult result = CpuAssembler.Parse("NOP\nNOP\nNOP", 2);
            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Errors[0].Line, Is.EqualTo(3));
            Assert.That(result.Errors[0].Message, Does.Contain("Too many"));
        }

        [Test]
        public void WordLabels_AreRecognised_AndFreeTextIsNot()
        {
            Assert.That(CpuAssembler.WordLabel(3, 0x20), Is.EqualTo("3: LDA 0"));
            Assert.That(CpuAssembler.IsWordLabel("3: LDA 0", out int address), Is.True);
            Assert.That(address, Is.EqualTo(3));
            Assert.That(CpuAssembler.IsWordLabel("12: ? 0xC3", out address), Is.True);
            Assert.That(address, Is.EqualTo(12));
            Assert.That(CpuAssembler.IsWordLabel("PC", out _), Is.False);
            Assert.That(CpuAssembler.IsWordLabel("Note: start here", out _), Is.False);
            Assert.That(CpuAssembler.IsWordLabel(null, out _), Is.False);
            Assert.That(CpuAssembler.Disassemble(0xB3), Is.EqualTo("? 0xB3"), "Stray operand bits are kept.");
        }

        [Test]
        public void CpuDemo_StillForwardsToTheAssembler()
        {
            Assert.That(CpuDemo.Assemble("JC", 13), Is.EqualTo(CpuAssembler.Assemble("JC", 13)));
            Assert.That(CpuDemo.Disassemble(0xF0), Is.EqualTo("HLT"));
        }
    }
}
