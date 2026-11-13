using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.GateLibrary.Custom;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.GateLibrary
{
    public sealed class DefinitionLibraryTests
    {
        [Test]
        public void Add_FreezesAndResolvesExactVersion()
        {
            var library = new DefinitionLibrary();
            CircuitDefinition nand = BuildNand();

            library.Add(nand);

            Assert.That(nand.IsReadOnly, Is.True);
            Assert.That(library.TryGetDefinition(nand.Reference, out CircuitDefinition found), Is.True);
            Assert.That(found, Is.SameAs(nand));
            Assert.That(library.TryGetDefinition(new DefinitionRef(nand.Id, 2), out _), Is.False);
        }

        [Test]
        public void Add_RejectsDuplicateVersion()
        {
            var library = new DefinitionLibrary();
            CircuitDefinition nand = BuildNand();
            library.Add(nand);

            Assert.Throws<InvalidOperationException>(() => library.Add(nand.CloneAsVersion(1)));
        }

        [Test]
        public void NewVersion_KeepsOlderVersionResolvable()
        {
            var library = new DefinitionLibrary();
            CircuitDefinition v1 = BuildNand();
            library.Add(v1);

            CircuitDefinition v2 = v1.CloneAsVersion(library.NextVersion(v1.Id));
            v2.AddNode(Not, CircuitPoint.Zero);
            library.Add(v2);

            Assert.That(library.TryGetDefinition(v1.Reference, out CircuitDefinition old), Is.True);
            Assert.That(old.Nodes.Count, Is.EqualTo(5), "Published v1 is untouched by v2 edits.");
            Assert.That(library.TryGetLatest(v1.Id, out CircuitDefinition latest) && latest.Version == 2, Is.True);
            Assert.That(library.NextVersion(v1.Id), Is.EqualTo(3));
            Assert.That(library.DefinitionCount, Is.EqualTo(1));
        }

        [Test]
        public void GetLatestDefinitions_ReturnsOneEntryPerId()
        {
            var library = new DefinitionLibrary();
            CircuitDefinition nand = BuildNand();
            CircuitDefinition or = BuildOrFromPrimitives();
            library.Add(nand);
            library.Add(or);
            library.Add(nand.CloneAsVersion(2));

            var latest = new List<CircuitDefinition>();
            library.GetLatestDefinitions(latest);

            Assert.That(latest.Count, Is.EqualTo(2));
            Assert.That(latest.Exists(d => d.Id == nand.Id && d.Version == 2), Is.True);
        }

        [Test]
        public void DefinitionAdded_IsRaised()
        {
            var library = new DefinitionLibrary();
            CircuitDefinition received = null;
            library.DefinitionAdded += d => received = d;
            CircuitDefinition nand = BuildNand();

            library.Add(nand);

            Assert.That(received, Is.SameAs(nand));
        }
    }
}
