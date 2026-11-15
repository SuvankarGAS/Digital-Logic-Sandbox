using System;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Persistence;
using DigitalLogicSandbox.Persistence.Deserialization;
using DigitalLogicSandbox.Persistence.Serialization;
using DigitalLogicSandbox.Rendering.Displays;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Tests.EditMode.Support;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.SimulationTestUtil;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.Components
{
    /// <summary>Phase D: display components (model, signatures, decoding, layout, validation, persistence).</summary>
    public sealed class DisplayTests
    {
        private TestEnvironment _env;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
        }

        [Test]
        public void DisplayRef_CarriesStyleAndWidth_AndIsAUtility()
        {
            ComponentRef hex = ComponentRef.Display(DisplayStyle.Hex, 8);
            Assert.That(hex.Kind, Is.EqualTo(ComponentKind.Display));
            Assert.That(hex.DisplayStyle, Is.EqualTo(DisplayStyle.Hex));
            Assert.That(hex.Width, Is.EqualTo(8));
            Assert.That(hex.Arity, Is.Zero);
            Assert.That(hex.IsUtility, Is.True);
            Assert.That(hex, Is.Not.EqualTo(ComponentRef.Display(DisplayStyle.Decimal, 8)));
            Assert.That(hex, Is.EqualTo(ComponentRef.Utility(ComponentKind.Display, 8, (int)DisplayStyle.Hex)));

            Assert.Throws<ArgumentOutOfRangeException>(() => ComponentRef.Display(DisplayStyle.Led, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => ComponentRef.Display(DisplayStyle.SignedDecimal, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ComponentRef.Display(DisplayStyle.Binary, 33));

            // Displays are never built-in gates: the primitive registry still holds exactly AND and NOT.
            Assert.That(PrimitiveRegistry.CreateDefault().All.Count, Is.EqualTo(2));
        }

        [Test]
        public void Signatures_SevenSegmentHasSegmentInputs_BusStylesOneInput_NoOutputs()
        {
            Assert.That(_env.Catalog.TryGetSignature(ComponentRef.Display(DisplayStyle.SevenSegment), out ComponentSignature seg), Is.True);
            Assert.That(seg.Inputs.Count, Is.EqualTo(8));
            Assert.That(seg.Inputs[0].Name, Is.EqualTo("a"));
            Assert.That(seg.Inputs[7].Name, Is.EqualTo("dp"));
            Assert.That(seg.Outputs.Count, Is.Zero);

            Assert.That(_env.Catalog.TryGetSignature(ComponentRef.Display(DisplayStyle.Decimal, 6), out ComponentSignature dec), Is.True);
            Assert.That(dec.Inputs.Count, Is.EqualTo(1));
            Assert.That(dec.Inputs[0].Width, Is.EqualTo(6));
            Assert.That(dec.Outputs.Count, Is.Zero);
        }

        [Test]
        public void HexGlyphs_MatchTheConventionalSegments()
        {
            Assert.That(DisplayEncoding.Glyph(0), Is.EqualTo(0x3F));
            Assert.That(DisplayEncoding.Glyph(1), Is.EqualTo(DisplayEncoding.SegmentB | DisplayEncoding.SegmentC));
            Assert.That(DisplayEncoding.Glyph(8), Is.EqualTo(0x7F));
            Assert.That(DisplayEncoding.Glyph(0xA), Is.EqualTo(0x77));
            Assert.That(DisplayEncoding.Glyph(0xF), Is.EqualTo(0x71));
        }

        [Test]
        public void DigitCounts_FollowWidthAndStyle()
        {
            Assert.That(DisplayEncoding.DigitCount(DisplayStyle.Hex, 8), Is.EqualTo(2));
            Assert.That(DisplayEncoding.DigitCount(DisplayStyle.Hex, 9), Is.EqualTo(3));
            Assert.That(DisplayEncoding.DigitCount(DisplayStyle.Decimal, 8), Is.EqualTo(3)); // 255
            Assert.That(DisplayEncoding.DigitCount(DisplayStyle.Decimal, 32), Is.EqualTo(10)); // 4294967295
            Assert.That(DisplayEncoding.DigitCount(DisplayStyle.SignedDecimal, 8), Is.EqualTo(4)); // -128
            Assert.That(DisplayEncoding.DigitCount(DisplayStyle.SevenSegment, 1), Is.EqualTo(1));
            Assert.That(DisplayEncoding.LampCount(DisplayStyle.Binary, 5), Is.EqualTo(5));
            Assert.That(DisplayEncoding.InputCount(DisplayStyle.SevenSegment, 1), Is.EqualTo(8));
        }

        [Test]
        public void EncodeDigits_HexDecimalAndSigned()
        {
            var digits = new byte[10];
            Assert.That(DisplayEncoding.EncodeDigits(DisplayStyle.Hex, 8, 0xA5, 0, digits), Is.True);
            Assert.That(digits[0], Is.EqualTo(DisplayEncoding.Glyph(0xA)));
            Assert.That(digits[1], Is.EqualTo(DisplayEncoding.Glyph(5)));

            // Decimal is right-aligned with blank leading digits: 7 on a 3-digit display.
            Assert.That(DisplayEncoding.EncodeDigits(DisplayStyle.Decimal, 8, 7, 0, digits), Is.True);
            Assert.That(digits[0], Is.Zero);
            Assert.That(digits[1], Is.Zero);
            Assert.That(digits[2], Is.EqualTo(DisplayEncoding.Glyph(7)));

            // 0xA5 as signed 8-bit is -91: "-91" right-aligned on 4 digits.
            Assert.That(DisplayEncoding.EncodeDigits(DisplayStyle.SignedDecimal, 8, 0xA5, 0, digits), Is.True);
            Assert.That(digits[0], Is.Zero);
            Assert.That(digits[1], Is.EqualTo(DisplayEncoding.Dash));
            Assert.That(digits[2], Is.EqualTo(DisplayEncoding.Glyph(9)));
            Assert.That(digits[3], Is.EqualTo(DisplayEncoding.Glyph(1)));

            // The most negative value: -128.
            Assert.That(DisplayEncoding.EncodeDigits(DisplayStyle.SignedDecimal, 8, 0x80, 0, digits), Is.True);
            Assert.That(digits[0], Is.EqualTo(DisplayEncoding.Dash));
            Assert.That(digits[1], Is.EqualTo(DisplayEncoding.Glyph(1)));
        }

        [Test]
        public void EncodeDigits_UnreadableBitShowsDashes()
        {
            var digits = new byte[3];
            Assert.That(DisplayEncoding.EncodeDigits(DisplayStyle.Decimal, 8, 0x01, 0x10, digits), Is.False);
            Assert.That(digits, Is.EqualTo(new[] { DisplayEncoding.Dash, DisplayEncoding.Dash, DisplayEncoding.Dash }));

            // A raw seven-segment digit shows exactly the HIGH segments.
            Assert.That(DisplayEncoding.EncodeDigits(DisplayStyle.SevenSegment, 1, 0x86, 0, digits), Is.True);
            Assert.That(digits[0], Is.EqualTo(0x86));
        }

        [Test]
        public void FormatValue_GivesAPlainReading()
        {
            Assert.That(DisplayEncoding.FormatValue(DisplayStyle.Led, 1, 1, 0), Is.EqualTo("ON"));
            Assert.That(DisplayEncoding.FormatValue(DisplayStyle.Led, 1, 0, 0), Is.EqualTo("OFF"));
            Assert.That(DisplayEncoding.FormatValue(DisplayStyle.Led, 1, 0, 1), Is.EqualTo("?"));
            Assert.That(DisplayEncoding.FormatValue(DisplayStyle.SevenSegment, 1, 0b0000_0110, 0), Is.EqualTo("b c"));
            Assert.That(DisplayEncoding.FormatValue(DisplayStyle.SevenSegment, 1, 0, 0), Is.EqualTo("blank"));
            Assert.That(DisplayEncoding.FormatValue(DisplayStyle.Binary, 4, 0b1010, 0b0001), Is.EqualTo("101X"));
            Assert.That(DisplayEncoding.FormatValue(DisplayStyle.Hex, 12, 0xA5, 0), Is.EqualTo("0A5"));
            Assert.That(DisplayEncoding.FormatValue(DisplayStyle.Decimal, 8, 165, 0), Is.EqualTo("165"));
            Assert.That(DisplayEncoding.FormatValue(DisplayStyle.SignedDecimal, 8, 0xA5, 0), Is.EqualTo("-91"));
            Assert.That(DisplayEncoding.FormatValue(DisplayStyle.SignedDecimal, 4, 0x7, 0), Is.EqualTo("7"));
            Assert.That(DisplayEncoding.FormatValue(DisplayStyle.Decimal, 8, 0, 0x80), Is.EqualTo("?"));
        }

        [Test]
        public void Layout_SevenSegmentHasLabelledPins_AndDisplaysFitTheirDigits()
        {
            _env.Catalog.TryGetSignature(ComponentRef.Display(DisplayStyle.SevenSegment), out ComponentSignature seg);
            GateGeometry geometry = GateLayout.Compute(ComponentKind.Display, seg, Rotation.R0, string.Empty, null,
                DisplayStyle.SevenSegment);
            Assert.That(geometry.IsDisplay, Is.True);
            Assert.That(geometry.InputPins.Length, Is.EqualTo(8));
            Assert.That(geometry.InputNames[6], Is.EqualTo("g"));
            Assert.That(geometry.OutputPins.Length, Is.Zero);

            _env.Catalog.TryGetSignature(ComponentRef.Display(DisplayStyle.Hex, 16), out ComponentSignature hex);
            GateGeometry wide = GateLayout.Compute(ComponentKind.Display, hex, Rotation.R0, "HEX", null, DisplayStyle.Hex);
            Assert.That(wide.DisplayArea.width, Is.GreaterThanOrEqualTo(4 * GateLayout.DigitWidth));
            Assert.That(wide.Size.x, Is.GreaterThan(wide.DisplayArea.width));

            GateGeometry rotated = GateLayout.Compute(ComponentKind.Display, hex, Rotation.R90, "HEX", null, DisplayStyle.Hex);
            Assert.That(rotated.DisplayArea.height, Is.EqualTo(wide.DisplayArea.width).Within(1e-4f));
        }

        [Test]
        public void Validation_DoesNotWarnAboutUnusedDisplayInputs_AndRejectsWidthMismatch()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Displays");
            NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero, 4);
            NodeDefinition hex = circuit.AddNode(ComponentRef.Display(DisplayStyle.Hex, 4), new CircuitPoint(4, 0));
            NodeDefinition led = circuit.AddNode(ComponentRef.Display(DisplayStyle.Led), new CircuitPoint(4, 4));
            circuit.AddNode(ComponentRef.Display(DisplayStyle.SevenSegment), new CircuitPoint(8, 0));
            Assert.That(ConnectionRules.TryConnect(circuit, _env.Catalog, BoundaryOut(a), new PinRef(hex.Id, BoundarySignatures.Pin)),
                Is.EqualTo(ConnectionError.None));
            Assert.That(ConnectionRules.Check(circuit, _env.Catalog, BoundaryOut(a), new PinRef(led.Id, BoundarySignatures.Pin)),
                Is.EqualTo(ConnectionError.WidthMismatch));

            ValidationReport report = new CircuitValidator(_env.Catalog).Validate(circuit);
            Assert.That(report.HasErrors, Is.False);
            Assert.That(report.Count(DiagnosticCode.UnconnectedInput), Is.Zero);

            // Displays are observers: the circuit still compiles and simulates with them present.
            SimulationContext context = CreateContext(_env, circuit);
            Assert.That(context, Is.Not.Null);
        }

        [Test]
        public void Displays_RoundTripThroughTheCurrentSchema()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Displays");
            NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero, 8);
            NodeDefinition signed = circuit.AddNode(ComponentRef.Display(DisplayStyle.SignedDecimal, 8), new CircuitPoint(4, 0));
            circuit.AddNode(ComponentRef.Display(DisplayStyle.SevenSegment), new CircuitPoint(8, 0));
            circuit.AddNode(ComponentRef.Display(DisplayStyle.Led), new CircuitPoint(8, 6));
            circuit.Connect(BoundaryOut(a), new PinRef(signed.Id, BoundarySignatures.Pin));
            var document = new ProjectDocument { Name = "Displays", RootCircuit = circuit };

            string json = ProjectSerializer.ToJson(document);
            JObject parsed = JObject.Parse(json);
            Assert.That((int)parsed["schemaVersion"], Is.EqualTo(ProjectFormat.CurrentSchemaVersion));
            var node = (JObject)((JArray)parsed["rootCircuit"]["nodes"])[1];
            Assert.That((string)node["kind"], Is.EqualTo("display"));
            Assert.That((string)node["style"], Is.EqualTo("signeddecimal"));
            Assert.That((int)node["width"], Is.EqualTo(8));

            ProjectDocument loaded = new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault()).Read(json).Document;
            Assert.That(CircuitFingerprint.Of(loaded.RootCircuit), Is.EqualTo(CircuitFingerprint.Of(circuit)));
            Assert.That(loaded.RootCircuit.Nodes[1].Component, Is.EqualTo(ComponentRef.Display(DisplayStyle.SignedDecimal, 8)));
        }

        [Test]
        public void Version3Project_StillLoads()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Old");
            circuit.AddInputBoundary("A", CircuitPoint.Zero);
            JObject json = JObject.Parse(ProjectSerializer.ToJson(new ProjectDocument { Name = "Old", RootCircuit = circuit }));
            json["schemaVersion"] = 3;
            var result = new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault()).Read(json.ToString());
            Assert.That(result.StoredSchemaVersion, Is.EqualTo(3));
            Assert.That(result.Document.RootCircuit.Inputs.Count, Is.EqualTo(1));
        }

        [Test]
        public void LedColor_IsPresentational_AndPartOfTheReference()
        {
            ComponentRef plain = ComponentRef.Display(DisplayStyle.Led);
            ComponentRef red = plain.WithLedColor(1);
            Assert.That(plain.LedColor, Is.EqualTo(LedColors.Signal));
            Assert.That(red.LedColor, Is.EqualTo(1));
            Assert.That(red, Is.Not.EqualTo(plain), "The colour is part of the component reference.");
            Assert.That(red.WithLedColor(0), Is.EqualTo(plain));
            Assert.Throws<InvalidOperationException>(() => ComponentRef.Display(DisplayStyle.Hex, 4).WithLedColor(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => plain.WithLedColor(LedColors.Count));

            // Same pins, so colour can never change simulation.
            Assert.That(_env.Catalog.TryGetSignature(red, out ComponentSignature redPins), Is.True);
            Assert.That(_env.Catalog.TryGetSignature(plain, out ComponentSignature plainPins), Is.True);
            Assert.That(redPins.Inputs.Count, Is.EqualTo(plainPins.Inputs.Count));
            Assert.That(redPins.Inputs[0].Id, Is.EqualTo(plainPins.Inputs[0].Id));

            GateGeometry geometry = GateLayout.Compute(ComponentKind.Display, redPins, Rotation.R0, string.Empty, null,
                DisplayStyle.Led, red.LedColor);
            Assert.That(geometry.LedColor, Is.EqualTo(1));
        }

        [Test]
        public void LedColor_RoundTrips_AndOlderFilesUseTheDefault()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Leds");
            NodeDefinition green = circuit.AddNode(ComponentRef.Display(DisplayStyle.Led).WithLedColor(3), CircuitPoint.Zero);
            NodeDefinition plain = circuit.AddNode(ComponentRef.Display(DisplayStyle.Led), new CircuitPoint(0, 3));
            string json = ProjectSerializer.ToJson(new ProjectDocument { Name = "Leds", RootCircuit = circuit });

            JObject parsed = JObject.Parse(json);
            JArray nodes = (JArray)parsed["rootCircuit"]["nodes"];
            Assert.That((string)nodes[0]["color"], Is.EqualTo("green"));
            Assert.That(nodes[1]["color"], Is.Null, "The default colour is not written.");

            var deserializer = new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault());
            ProjectDocument loaded = deserializer.Read(json).Document;
            Assert.That(loaded.RootCircuit.GetNode(green.Id).Component.LedColor, Is.EqualTo(3));
            Assert.That(loaded.RootCircuit.GetNode(plain.Id).Component.LedColor, Is.EqualTo(LedColors.Signal));

            // A version 4 file (before LED colours) migrates and loads with the default colour.
            nodes[0].Value<JObject>().Remove("color");
            parsed["schemaVersion"] = 4;
            ProjectLoadResult old = deserializer.Read(parsed.ToString());
            Assert.That(old.StoredSchemaVersion, Is.EqualTo(4));
            Assert.That(old.Document.RootCircuit.GetNode(green.Id).Component.LedColor, Is.EqualTo(LedColors.Signal));

            // An unknown colour id (from a newer palette) falls back to the default instead of failing.
            nodes[0]["color"] = "ultraviolet";
            parsed["schemaVersion"] = ProjectFormat.CurrentSchemaVersion;
            Assert.That(deserializer.Read(parsed.ToString()).Document.RootCircuit.GetNode(green.Id).Component.LedColor,
                Is.EqualTo(LedColors.Signal));
        }
    }
}
