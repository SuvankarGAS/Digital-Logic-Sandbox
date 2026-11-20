using System;
using System.Collections.Generic;
using DigitalLogicSandbox.CircuitEditor.Placement;
using DigitalLogicSandbox.Commands.Library;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.UI;
using DigitalLogicSandbox.UI.Panels;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.GateLibrary
{
    /// <summary>Custom gate management from the library (details, delete, usage) and per-user library preferences.</summary>
    public sealed class LibraryManagementTests
    {
        [Test]
        public void SetMetadata_AppliesToAllVersions_AndUndoRestores()
        {
            var library = new DefinitionLibrary();
            CircuitDefinition v1 = BuildNand();
            library.Add(v1);
            CircuitDefinition v2 = v1.CloneAsVersion(2);
            library.Add(v2);
            int updates = 0;
            library.DefinitionUpdated += _ => updates++;

            var command = new SetCustomGateMetadataCommand(library, v1.Id, "Nand Gate", "Not-and", "Logic", "NA");
            command.Execute();

            Assert.That(v1.Name, Is.EqualTo("Nand Gate"));
            Assert.That(v2.Category, Is.EqualTo("Logic"));
            Assert.That(v2.Icon, Is.EqualTo("NA"));
            Assert.That(v1.Description, Is.EqualTo("Not-and"));
            Assert.That(updates, Is.EqualTo(2));

            command.Undo();
            Assert.That(v1.Name, Is.EqualTo("NAND"));
            Assert.That(v2.Name, Is.EqualTo("NAND"));
            Assert.That(v2.Category, Is.Empty);

            command.Execute();
            Assert.That(v2.Name, Is.EqualTo("Nand Gate"), "Redo reproduces the same state.");
        }

        [Test]
        public void SetMetadata_NullKeepsFields()
        {
            var library = new DefinitionLibrary();
            CircuitDefinition nand = BuildNand();
            nand.Description = "keep me";
            library.Add(nand);

            new SetCustomGateMetadataCommand(library, nand.Id, null, null, "Logic", null).Execute();

            Assert.That(nand.Name, Is.EqualTo("NAND"));
            Assert.That(nand.Description, Is.EqualTo("keep me"));
            Assert.That(nand.Category, Is.EqualTo("Logic"));
        }

        [Test]
        public void Delete_RemovesEveryVersion_AndUndoRestoresThem()
        {
            var library = new DefinitionLibrary();
            CircuitDefinition v1 = BuildNand();
            library.Add(v1);
            CircuitDefinition v2 = v1.CloneAsVersion(2);
            library.Add(v2);

            var command = new DeleteCustomGateCommand(library, v1.Id, "NAND");
            command.Execute();

            Assert.That(library.Contains(v1.Reference), Is.False);
            Assert.That(library.Contains(v2.Reference), Is.False);
            Assert.That(library.DefinitionCount, Is.Zero);

            command.Undo();
            Assert.That(library.TryGetDefinition(v1.Reference, out CircuitDefinition restored), Is.True);
            Assert.That(restored, Is.SameAs(v1));
            Assert.That(library.TryGetLatest(v1.Id, out CircuitDefinition latest) && latest == v2, Is.True);
        }

        [Test]
        public void Delete_RefusedWhileAnotherGateUsesIt_AndRemovesNothing()
        {
            var library = new DefinitionLibrary();
            CircuitDefinition nand = BuildNand();
            library.Add(nand);
            CircuitDefinition or = BuildOrFromNand(nand.Reference, nand);
            library.Add(or);

            Assert.Throws<InvalidOperationException>(() => new DeleteCustomGateCommand(library, nand.Id).Execute());
            Assert.That(library.Contains(nand.Reference), Is.True);
        }

        [Test]
        public void CountUsages_CountsMainCircuitAndOtherGates()
        {
            var library = new DefinitionLibrary();
            CircuitDefinition nand = BuildNand();
            library.Add(nand);
            CircuitDefinition or = BuildOrFromNand(nand.Reference, nand);
            library.Add(or);
            var main = new CircuitDefinition(DefinitionId.New(), "Main");
            main.AddNode(ComponentRef.Custom(nand.Reference), CircuitPoint.Zero);
            var users = new List<string>();

            int total = DefinitionUsage.CountUsages(library, main, nand.Id, out int root, users);

            Assert.That(root, Is.EqualTo(1));
            Assert.That(total, Is.EqualTo(4), "One in the main circuit plus three inside OR.");
            Assert.That(users, Is.EqualTo(new[] { "OR" }));
            Assert.That(DefinitionUsage.CountUsages(library, main, or.Id), Is.Zero);
        }

        [Test]
        public void CountUsages_IncludesInstancesOfOlderVersions()
        {
            var library = new DefinitionLibrary();
            CircuitDefinition v1 = BuildNand();
            library.Add(v1);
            library.Add(v1.CloneAsVersion(2));
            var main = new CircuitDefinition(DefinitionId.New(), "Main");
            main.AddNode(ComponentRef.Custom(v1.Reference), CircuitPoint.Zero);

            Assert.That(DefinitionUsage.CountUsages(library, main, v1.Id), Is.EqualTo(1));
        }

        [Test]
        public void UniqueName_SkipsTakenNames()
        {
            var library = new DefinitionLibrary();
            library.Add(BuildNand("NAND copy"));
            library.Add(BuildNand("NAND copy 2"));

            Assert.That(DefinitionUsage.UniqueName(library, "NAND copy"), Is.EqualTo("NAND copy 3"));
            Assert.That(DefinitionUsage.UniqueName(library, "XOR"), Is.EqualTo("XOR"));
        }

        [Test]
        public void KeyOf_IsStableAcrossVersionsAndIgnoresDisplayNames()
        {
            DefinitionId id = DefinitionId.New();
            string v1 = LibraryPanel.KeyOf(new PlacementRequest(ComponentRef.Custom(new DefinitionRef(id, 1)), "NAND"));
            string v2 = LibraryPanel.KeyOf(new PlacementRequest(ComponentRef.Custom(new DefinitionRef(id, 2)), "Renamed"));

            Assert.That(v1, Is.EqualTo(v2));
            Assert.That(LibraryPanel.KeyOf(new PlacementRequest(And, "AND")), Is.Not.EqualTo(v1));
            Assert.That(LibraryPanel.KeyOf(new PlacementRequest(ComponentRef.InputBus(4), "Bus Input")),
                Is.EqualTo(LibraryPanel.KeyOf(new PlacementRequest(ComponentRef.InputBus(8), "Bus Input"))),
                "Bus entries keep their identity when the bus width changes.");
        }

        [Test]
        public void Preferences_RoundTripThroughStore()
        {
            var store = new MemoryPreferenceStore();
            var preferences = new UiPreferences(store);
            preferences.LibraryWidth = 333f;
            preferences.LibraryCollapsed = true;
            preferences.LibraryTab = 2;
            preferences.SetSectionExpanded("section:buses", true);
            preferences.SetSectionExpanded("section:gates", false);
            preferences.SetPinned("p:AND", true);
            preferences.AddRecent("p:NOT");
            preferences.AddRecent("u:Input");

            var reloaded = new UiPreferences(store);

            Assert.That(reloaded.LibraryWidth, Is.EqualTo(333f));
            Assert.That(reloaded.LibraryCollapsed, Is.True);
            Assert.That(reloaded.LibraryTab, Is.EqualTo(2));
            Assert.That(reloaded.IsSectionExpanded("section:buses", false), Is.True);
            Assert.That(reloaded.IsSectionExpanded("section:gates", true), Is.False);
            Assert.That(reloaded.IsSectionExpanded("section:unknown", true), Is.True);
            Assert.That(reloaded.Pinned, Is.EqualTo(new[] { "p:AND" }));
            Assert.That(reloaded.Recent, Is.EqualTo(new[] { "u:Input", "p:NOT" }));
        }

        [Test]
        public void Preferences_RecentIsBoundedAndDeduplicated_WidthClamped()
        {
            var preferences = new UiPreferences(new MemoryPreferenceStore());
            for (int i = 0; i < UiPreferences.MaxRecent + 3; i++)
            {
                preferences.AddRecent("k" + i);
            }

            preferences.AddRecent("k" + (UiPreferences.MaxRecent + 1));
            Assert.That(preferences.Recent.Count, Is.EqualTo(UiPreferences.MaxRecent));
            Assert.That(preferences.Recent[0], Is.EqualTo("k" + (UiPreferences.MaxRecent + 1)));

            preferences.LibraryWidth = 9999f;
            Assert.That(preferences.LibraryWidth, Is.EqualTo(UiPreferences.MaxLibraryWidth));
        }

        [Test]
        public void Preferences_IgnoreCorruptData()
        {
            var store = new MemoryPreferenceStore();
            store.Save(UiPreferences.StorageKey, "{not json");

            var preferences = new UiPreferences(store);

            Assert.That(preferences.LibraryWidth, Is.EqualTo(UiPreferences.DefaultLibraryWidth));
            Assert.That(preferences.Pinned, Is.Empty);
        }
    }
}
