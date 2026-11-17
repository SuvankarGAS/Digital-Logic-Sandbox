using System;
using System.Collections.Generic;
using System.IO;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.Persistence;
using DigitalLogicSandbox.Persistence.Deserialization;
using DigitalLogicSandbox.Persistence.Migration;
using DigitalLogicSandbox.Persistence.Recovery;
using DigitalLogicSandbox.Persistence.Serialization;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Tests.EditMode.Support;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.PersistenceTests
{
    public sealed class ProjectSerializationTests
    {
        private TestEnvironment _env;
        private CircuitDefinition _nand;
        private CircuitDefinition _nandV2;
        private CircuitDefinition _or;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
            _nand = BuildNand();
            _nand.Category = "Basic";
            _nand.Description = "Not AND";
            _env.Publish(_nand);
            _or = BuildOrFromNand(_nand.Reference, _nand);
            _env.Publish(_or);
            _nandV2 = _nand.CloneAsVersion(2);
            _nandV2.RotateNode(_nandV2.Nodes[2].Id, Rotation.R90);
            _env.Publish(_nandV2);
        }

        /// <summary>Main circuit: two inputs into an OR instance and a NAND v1 instance, two lamps.</summary>
        private CircuitDefinition BuildRoot()
        {
            var root = new CircuitDefinition(DefinitionId.New(), "Main Circuit");
            NodeDefinition a = root.AddInputBoundary("A", new CircuitPoint(-6, 1));
            NodeDefinition b = root.AddInputBoundary("B", new CircuitPoint(-6, -1));
            NodeDefinition or = root.AddNode(ComponentRef.Custom(_or.Reference), new CircuitPoint(0, 2), Rotation.R0, "my or");
            NodeDefinition nand = root.AddNode(ComponentRef.Custom(_nand.Reference), new CircuitPoint(0, -2), Rotation.R180);
            NodeDefinition orOut = root.AddOutputBoundary("OR", new CircuitPoint(6, 2));
            NodeDefinition nandOut = root.AddOutputBoundary("NAND", new CircuitPoint(6, -2));
            root.Connect(BoundaryOut(a), new PinRef(or.Id, _or.Inputs[0].Id));
            root.Connect(BoundaryOut(b), new PinRef(or.Id, _or.Inputs[1].Id));
            root.Connect(BoundaryOut(a), new PinRef(nand.Id, _nand.Inputs[0].Id));
            root.Connect(BoundaryOut(b), new PinRef(nand.Id, _nand.Inputs[1].Id));
            root.Connect(new PinRef(or.Id, _or.Outputs[0].Id), BoundaryIn(orOut));
            root.Connect(new PinRef(nand.Id, _nand.Outputs[0].Id), BoundaryIn(nandOut));

            // A deleted node: its id must never be reused after reloading.
            NodeDefinition scratch = root.AddNode(Not, new CircuitPoint(9, 9));
            root.RemoveNode(scratch.Id);
            root.MoveInterfacePort(b.InterfacePort, 0); // B before A
            return root;
        }

        private ProjectDocument BuildDocument(CircuitDefinition root)
        {
            var document = new ProjectDocument { Name = "Test", RootCircuit = root };
            document.Definitions.AddRange(_env.Library.AllVersions);
            document.InputValues[root.Inputs[0].Id] = 1UL;
            document.Settings.GridVisible = false;
            document.Settings.SnapToGrid = false;
            document.Settings.VisualizationMode = "HighContrast";
            document.Settings.WireCrossings = "Gap";
            document.Settings.Animation.Enabled = false;
            document.Settings.Animation.Speed = 2f;
            document.Settings.Animation.ShowParticles = false;
            document.Settings.TruthTable.MaxInputs = 10;
            document.Settings.TruthTable.ConfirmRows = 64;
            document.Settings.Camera.HasValue = true;
            document.Settings.Camera.X = 3.5f;
            document.Settings.Camera.Y = -2f;
            document.Settings.Camera.Size = 14f;
            return document;
        }

        private static ProjectLoadResult RoundTrip(ProjectDocument document)
        {
            string json = ProjectSerializer.ToJson(document);
            return new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault()).Read(json);
        }

        private static CircuitDefinition Find(ProjectDocument document, DefinitionRef reference)
        {
            foreach (CircuitDefinition definition in document.Definitions)
            {
                if (definition.Reference == reference)
                {
                    return definition;
                }
            }

            return null;
        }

        [Test]
        public void SaveAndLoad_RestoresTheMainCircuitExactly()
        {
            CircuitDefinition root = BuildRoot();
            ProjectLoadResult result = RoundTrip(BuildDocument(root));
            CircuitDefinition loaded = result.Document.RootCircuit;

            Assert.That(result.Report.Diagnostics.Count, Is.Zero, result.Report.ToString());
            Assert.That(loaded.Id, Is.EqualTo(root.Id));
            Assert.That(loaded.Name, Is.EqualTo("Main Circuit"));
            Assert.That(CircuitFingerprint.Of(loaded), Is.EqualTo(CircuitFingerprint.Of(root)),
                "Nodes, ids, positions, rotations, labels, interface order and connections.");
            Assert.That(loaded.Inputs[0].Name, Is.EqualTo("B"), "Interface order survives.");
            Assert.That(loaded.IsReadOnly, Is.False, "The main circuit stays editable.");
            Assert.That(loaded.NextNodeIdValue, Is.EqualTo(root.NextNodeIdValue), "Deleted ids are never reused.");
            Assert.That(loaded.NextPortIdValue, Is.EqualTo(root.NextPortIdValue));
            Assert.That(loaded.CreatedUtc, Is.EqualTo(root.CreatedUtc).Within(TimeSpan.FromMilliseconds(1)));
        }

        [Test]
        public void SaveAndLoad_RestoresCustomGateDefinitionsVersionsAndMetadata()
        {
            ProjectLoadResult result = RoundTrip(BuildDocument(BuildRoot()));
            ProjectDocument document = result.Document;

            Assert.That(document.Definitions.Count, Is.EqualTo(3));
            CircuitDefinition nand = Find(document, _nand.Reference);
            CircuitDefinition nandV2 = Find(document, _nandV2.Reference);
            CircuitDefinition or = Find(document, _or.Reference);
            Assert.That(nand, Is.Not.Null);
            Assert.That(nandV2, Is.Not.Null, "Both versions of NAND are kept.");
            Assert.That(nandV2.Id, Is.EqualTo(nand.Id), "Versions share the stable definition id.");
            Assert.That(CircuitFingerprint.Of(nand), Is.EqualTo(CircuitFingerprint.Of(_nand)));
            Assert.That(CircuitFingerprint.Of(nandV2), Is.EqualTo(CircuitFingerprint.Of(_nandV2)));
            Assert.That(CircuitFingerprint.Of(or), Is.EqualTo(CircuitFingerprint.Of(_or)));
            Assert.That(nand.Category, Is.EqualTo("Basic"));
            Assert.That(nand.Description, Is.EqualTo("Not AND"));
            Assert.That(nand.IsReadOnly && or.IsReadOnly && nandV2.IsReadOnly, Is.True, "Library definitions are published.");
        }

        [Test]
        public void EachDefinitionVersion_IsStoredOnce_RegardlessOfInstanceCount()
        {
            CircuitDefinition root = BuildRoot();
            for (int i = 0; i < 10; i++)
            {
                root.AddNode(ComponentRef.Custom(_nand.Reference), new CircuitPoint(20, i * 3));
            }

            ProjectDocument document = BuildDocument(root);
            document.Definitions.Add(_nand); // even a duplicated entry in the document is written once
            JObject json = JObject.Parse(ProjectSerializer.ToJson(document));

            Assert.That(((JArray)json["definitions"]).Count, Is.EqualTo(3));
            Assert.That((int)json["schemaVersion"], Is.EqualTo(ProjectFormat.CurrentSchemaVersion));
            Assert.That((string)json["format"], Is.EqualTo(ProjectFormat.FormatName));
        }

        [Test]
        public void LoadedNestedGates_SimulateCorrectly()
        {
            ProjectDocument document = RoundTrip(BuildDocument(BuildRoot())).Document;
            var env = new TestEnvironment();
            foreach (CircuitDefinition definition in document.Definitions)
            {
                env.Library.Add(definition);
            }

            SimulationContext context = SimulationTestUtil.CreateContext(env, document.RootCircuit);
            // Inputs are B, A (reordered). Outputs: OR, NAND.
            Assert.That(SimulationTestUtil.Evaluate(context, 0, 0), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(SimulationTestUtil.Evaluate(context, 0, 1), Is.EqualTo(new[] { 1, 1 }));
            Assert.That(SimulationTestUtil.Evaluate(context, 1, 0), Is.EqualTo(new[] { 1, 1 }));
            Assert.That(SimulationTestUtil.Evaluate(context, 1, 1), Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void ReferencesToOlderVersions_RemainOnThatVersion()
        {
            ProjectDocument document = RoundTrip(BuildDocument(BuildRoot())).Document;
            int v1 = 0;
            foreach (NodeDefinition node in document.RootCircuit.Nodes)
            {
                if (node.Component.Kind == ComponentKind.Custom && node.Component.Definition.Id == _nand.Id)
                {
                    Assert.That(node.Component.Definition.Version, Is.EqualTo(1));
                    v1++;
                }
            }

            Assert.That(v1, Is.EqualTo(1));
        }

        [Test]
        public void SettingsAndInputValues_RoundTrip()
        {
            CircuitDefinition root = BuildRoot();
            ProjectDocument document = RoundTrip(BuildDocument(root)).Document;
            ProjectSettingsData settings = document.Settings;

            Assert.That(document.Name, Is.EqualTo("Test"));
            Assert.That(settings.GridVisible, Is.False);
            Assert.That(settings.SnapToGrid, Is.False);
            Assert.That(settings.VisualizationMode, Is.EqualTo("HighContrast"));
            Assert.That(settings.WireCrossings, Is.EqualTo("Gap"));
            Assert.That(settings.Animation.Enabled, Is.False);
            Assert.That(settings.Animation.Speed, Is.EqualTo(2f));
            Assert.That(settings.Animation.ShowParticles, Is.False);
            Assert.That(settings.TruthTable.MaxInputs, Is.EqualTo(10));
            Assert.That(settings.TruthTable.ConfirmRows, Is.EqualTo(64));
            Assert.That(settings.Camera.HasValue, Is.True);
            Assert.That(settings.Camera.X, Is.EqualTo(3.5f));
            Assert.That(settings.Camera.Size, Is.EqualTo(14f));
            Assert.That(document.InputValues[root.Inputs[0].Id], Is.EqualTo(1UL));
            Assert.That(document.InputValues.Count, Is.EqualTo(1));
        }

        [Test]
        public void MissingCustomGate_IsReportedButTheProjectStillLoads()
        {
            ProjectDocument document = BuildDocument(BuildRoot());
            document.Definitions.Remove(_or); // the file no longer contains OR, which the root uses

            ProjectLoadResult result = RoundTrip(document);
            Assert.That(result.Report.Contains(DiagnosticCode.MissingDefinition), Is.True);
            Assert.That(result.Report.HasErrors, Is.False, "Missing references are warnings; the user keeps their work.");
            Assert.That(result.Document.RootCircuit.Nodes.Count, Is.EqualTo(BuildRoot().Nodes.Count), "The instance is kept.");
        }

        [Test]
        public void InvalidConnections_AreSkippedWithAWarning()
        {
            JObject json = JObject.Parse(ProjectSerializer.ToJson(BuildDocument(BuildRoot())));
            var connections = (JArray)json["rootCircuit"]["connections"];
            connections.Add(new JObject { ["fromNode"] = 1, ["fromPort"] = 0, ["toNode"] = 999, ["toPort"] = 0 });

            ProjectLoadResult result = new ProjectDeserializer().Read(json.ToString());
            Assert.That(result.Report.Contains(DiagnosticCode.DanglingConnection), Is.True);
            Assert.That(result.Document.RootCircuit.Connections.Count, Is.EqualTo(6));
        }

        [Test]
        public void UnknownPrimitive_IsReported()
        {
            JObject json = JObject.Parse(ProjectSerializer.ToJson(BuildDocument(BuildNand("Plain"))));
            foreach (JToken node in json["rootCircuit"]["nodes"])
            {
                if ((string)node["kind"] == "primitive")
                {
                    node["primitive"] = "xor";
                    break;
                }
            }

            ProjectLoadResult result = new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault()).Read(json.ToString());
            Assert.That(result.Report.Contains(DiagnosticCode.MissingComponent), Is.True);
        }

        [Test]
        public void DamagedFiles_RaiseClearErrors()
        {
            var reader = new ProjectDeserializer();
            Assert.That(() => reader.Read("{ not json"),
                Throws.TypeOf<ProjectFormatException>().With.Message.Contains("damaged"));
            Assert.That(() => reader.Read("{\"format\":\"Other\",\"schemaVersion\":1}"),
                Throws.TypeOf<ProjectFormatException>().With.Message.Contains("not a Digital Logic Sandbox project"));
            Assert.That(() => reader.Read("{\"rootCircuit\":{}}"),
                Throws.TypeOf<ProjectFormatException>().With.Message.Contains("schema version"));
            Assert.That(() => reader.Read("{\"schemaVersion\":1}"),
                Throws.TypeOf<ProjectFormatException>().With.Message.Contains("no main circuit"));

            JObject json = JObject.Parse(ProjectSerializer.ToJson(BuildDocument(BuildRoot())));
            json["rootCircuit"]["nodes"][0]["kind"] = "flux-capacitor";
            Assert.That(() => reader.Read(json.ToString()),
                Throws.TypeOf<ProjectFormatException>().With.Message.Contains("Unknown node kind"));
        }

        // ------------------------------------------------------------------------------------------------
        // Migration
        // ------------------------------------------------------------------------------------------------

        /// <summary>Pretend schema 1 called connections "wires"; schema 2 (the current one in this test) does not.</summary>
        private sealed class RenameWiresMigration : IProjectMigration
        {
            public int FromVersion => 1;

            public string Description => "Rename 'wires' to 'connections'";

            public void Apply(JObject document)
            {
                Rename((JObject)document["rootCircuit"]);
                foreach (JToken definition in (JArray)document["definitions"])
                {
                    Rename((JObject)definition);
                }
            }

            private static void Rename(JObject circuit)
            {
                JToken wires = circuit["wires"];
                circuit.Remove("wires");
                circuit["connections"] = wires;
            }
        }

        private static void ToLegacy(JToken circuit)
        {
            var obj = (JObject)circuit;
            obj["wires"] = obj["connections"];
            obj.Remove("connections");
        }

        [Test]
        public void OlderSchemas_AreMigratedBeforeBinding()
        {
            CircuitDefinition root = BuildRoot();
            JObject json = JObject.Parse(ProjectSerializer.ToJson(BuildDocument(root)));
            json["schemaVersion"] = 1;
            ToLegacy(json["rootCircuit"]);
            foreach (JToken definition in (JArray)json["definitions"])
            {
                ToLegacy(definition);
            }

            var migrator = new ProjectMigrator(2, new IProjectMigration[] { new RenameWiresMigration() });
            ProjectLoadResult result = new ProjectDeserializer(migrator).Read(json.ToString());

            Assert.That(result.StoredSchemaVersion, Is.EqualTo(1));
            Assert.That(result.WasMigrated, Is.True);
            Assert.That(result.AppliedMigrations[0], Does.Contain("v1 -> v2"));
            Assert.That(CircuitFingerprint.Of(result.Document.RootCircuit), Is.EqualTo(CircuitFingerprint.Of(root)));
        }

        [Test]
        public void NewerSchemas_AndMissingMigrations_AreRefused()
        {
            JObject json = JObject.Parse(ProjectSerializer.ToJson(BuildDocument(BuildRoot())));
            json["schemaVersion"] = ProjectFormat.CurrentSchemaVersion + 1;
            Assert.That(() => new ProjectDeserializer().Read(json.ToString()),
                Throws.TypeOf<ProjectFormatException>().With.Message.Contains("newer version"));

            json["schemaVersion"] = 1;
            var gap = new ProjectMigrator(3, new IProjectMigration[] { new RenameWiresMigration() });
            Assert.That(() => new ProjectDeserializer(gap).Read(json.ToString()),
                Throws.TypeOf<ProjectFormatException>().With.Message.Contains("No migration exists from schema version 2"));
        }

        [Test]
        public void TheShippedMigrator_AcceptsTheCurrentSchema()
        {
            ProjectMigrator migrator = ProjectMigrator.CreateDefault();
            Assert.That(migrator.CurrentVersion, Is.EqualTo(ProjectFormat.CurrentSchemaVersion));
            var document = new JObject { ["schemaVersion"] = ProjectFormat.CurrentSchemaVersion };
            Assert.That(migrator.Migrate(document), Is.EqualTo(ProjectFormat.CurrentSchemaVersion));
        }

        // ------------------------------------------------------------------------------------------------
        // Phase B: clock nodes and simulation settings (schema v2)
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void SaveAndLoad_RestoresClockNodesAndSimulationSettings()
        {
            var root = new CircuitDefinition(DefinitionId.New(), "Clocked");
            root.AddInputBoundary("EN", new CircuitPoint(-4, 3));
            NodeDefinition clock = root.AddNode(ComponentRef.Clock, new CircuitPoint(-4, 0), Rotation.R90, "main clock");
            NodeDefinition not = root.AddNode(Not, new CircuitPoint(0, 0));
            NodeDefinition lamp = root.AddOutputBoundary("Y", new CircuitPoint(4, 0));
            root.Connect(new PinRef(clock.Id, BoundarySignatures.Pin), new PinRef(not.Id, NotGate.Input));
            root.Connect(new PinRef(not.Id, NotGate.Output), BoundaryIn(lamp));
            ProjectDocument document = BuildDocument(root);
            document.Settings.Simulation.ClockFrequency = 7.5f;
            document.Settings.Simulation.ClockEnabled = false;
            document.Settings.Simulation.PowerOnState = "Unknown";

            string json = ProjectSerializer.ToJson(document);
            Assert.That(json, Does.Contain("\"kind\": \"clock\""));
            ProjectLoadResult result = new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault()).Read(json);

            Assert.That(result.Report.HasErrors, Is.False, result.Report.ToString());
            CircuitDefinition loaded = result.Document.RootCircuit;
            Assert.That(CircuitFingerprint.Of(loaded), Is.EqualTo(CircuitFingerprint.Of(root)));
            Assert.That(loaded.GetNode(clock.Id).Component, Is.EqualTo(ComponentRef.Clock));
            Assert.That(loaded.GetNode(clock.Id).Label, Is.EqualTo("main clock"));
            SimulationSettingsData simulation = result.Document.Settings.Simulation;
            Assert.That(simulation.ClockFrequency, Is.EqualTo(7.5f));
            Assert.That(simulation.ClockEnabled, Is.False);
            Assert.That(simulation.PowerOnState, Is.EqualTo("Unknown"));
        }

        [Test]
        public void SaveAndLoad_RestoresClockDividers_AndVersion5FilesLoadAsBaseClocks()
        {
            var root = new CircuitDefinition(DefinitionId.New(), "Clocks");
            root.AddInputBoundary("EN", new CircuitPoint(-4, 4));
            NodeDefinition fast = root.AddNode(ComponentRef.Clock, new CircuitPoint(-4, 0));
            NodeDefinition slow = root.AddNode(ComponentRef.ClockDivided(16), new CircuitPoint(-4, 2));
            NodeDefinition lamp = root.AddOutputBoundary("Y", new CircuitPoint(4, 0));
            root.Connect(new PinRef(slow.Id, BoundarySignatures.Pin), BoundaryIn(lamp));

            string json = ProjectSerializer.ToJson(BuildDocument(root));
            Assert.That(json, Does.Contain("\"divider\": 16"));
            ProjectLoadResult result = new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault()).Read(json);
            Assert.That(result.Report.HasErrors, Is.False, result.Report.ToString());
            CircuitDefinition loaded = result.Document.RootCircuit;
            Assert.That(loaded.GetNode(slow.Id).Component, Is.EqualTo(ComponentRef.ClockDivided(16)));
            Assert.That(loaded.GetNode(fast.Id).Component, Is.EqualTo(ComponentRef.Clock));

            // A version 5 file has no dividers: every clock runs at the base frequency.
            JObject old = JObject.Parse(ProjectSerializer.ToJson(BuildDocument(root)));
            old["schemaVersion"] = 5;
            foreach (JToken node in old.SelectTokens("$..nodes[*]"))
            {
                ((JObject)node).Remove("divider");
            }

            ProjectLoadResult migrated = new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault()).Read(old.ToString());
            Assert.That(migrated.Report.HasErrors, Is.False, migrated.Report.ToString());
            Assert.That(migrated.AppliedMigrations[0], Does.Contain("v5 -> v6"));
            Assert.That(migrated.Document.RootCircuit.GetNode(slow.Id).Component, Is.EqualTo(ComponentRef.Clock));

            // A damaged divider falls back to ÷1 instead of failing the load.
            JObject damaged = JObject.Parse(json);
            foreach (JToken node in damaged.SelectTokens("$..nodes[*]"))
            {
                if (node["divider"] != null)
                {
                    node["divider"] = 3;
                }
            }

            ProjectLoadResult lenient = new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault()).Read(damaged.ToString());
            Assert.That(lenient.Document.RootCircuit.GetNode(slow.Id).Component, Is.EqualTo(ComponentRef.Clock));
        }

        [Test]
        public void Version1Files_AreMigrated_WithSimulationDefaults()
        {
            JObject json = JObject.Parse(ProjectSerializer.ToJson(BuildDocument(BuildRoot())));
            json["schemaVersion"] = 1;
            ((JObject)json["settings"]).Remove("simulation");

            ProjectLoadResult result = new ProjectDeserializer().Read(json.ToString());

            Assert.That(result.StoredSchemaVersion, Is.EqualTo(1));
            Assert.That(result.WasMigrated, Is.True);
            Assert.That(result.AppliedMigrations[0], Does.Contain("v1 -> v2").And.Contain("simulation settings"));
            SimulationSettingsData simulation = result.Document.Settings.Simulation;
            Assert.That(simulation, Is.Not.Null);
            Assert.That(simulation.ClockFrequency, Is.EqualTo(2f));
            Assert.That(simulation.ClockEnabled, Is.True);
            Assert.That(simulation.PowerOnState, Is.EqualTo("Resolved"));
            Assert.That(result.Document.Settings.GridVisible, Is.False, "Other settings are untouched.");
        }

        [Test]
        public void MissingWireCrossingSetting_LoadsAsHop()
        {
            JObject json = JObject.Parse(ProjectSerializer.ToJson(BuildDocument(BuildRoot())));
            var settings = (JObject)json["settings"];
            Assume.That(settings.ContainsKey("wireCrossings"), Is.True);
            settings.Remove("wireCrossings");

            ProjectLoadResult result = new ProjectDeserializer().Read(json.ToString());

            Assert.That(result.Document.Settings.WireCrossings, Is.EqualTo("Hop"));
            Assert.That(result.Document.Settings.VisualizationMode, Is.EqualTo("HighContrast"), "Other settings are kept.");
        }

        [Test]
        public void MissingSimulationSettings_AreNormalizedToDefaults()
        {
            JObject json = JObject.Parse(ProjectSerializer.ToJson(BuildDocument(BuildRoot())));
            ((JObject)json["settings"]).Remove("simulation");
            ProjectLoadResult result = new ProjectDeserializer().Read(json.ToString());
            Assert.That(result.Document.Settings.Simulation.PowerOnState, Is.EqualTo("Resolved"));
        }
    }

    public sealed class ProjectFileStoreTests
    {
        private string _directory;
        private ProjectFileStore _store;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "dls-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _store = new ProjectFileStore();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        private static ProjectDocument Document(string circuitName)
        {
            return new ProjectDocument { RootCircuit = BuildNand(circuitName) };
        }

        [Test]
        public void Save_IsAtomic_AndKeepsThePreviousFileAsBackup()
        {
            string path = Path.Combine(_directory, "p" + ProjectFormat.FileExtension);
            _store.Save(path, Document("First"));
            Assert.That(File.Exists(path), Is.True);
            Assert.That(File.Exists(path + ProjectFileStore.TempSuffix), Is.False);
            Assert.That(File.Exists(path + ProjectFileStore.BackupSuffix), Is.False, "Nothing to back up yet.");

            _store.Save(path, Document("Second"));
            Assert.That(_store.Load(path).Document.RootCircuit.Name, Is.EqualTo("Second"));
            Assert.That(_store.Read(path + ProjectFileStore.BackupSuffix).Document.RootCircuit.Name, Is.EqualTo("First"));
        }

        [Test]
        public void DamagedProject_IsRecoveredFromTheBackup()
        {
            string path = Path.Combine(_directory, "p" + ProjectFormat.FileExtension);
            _store.Save(path, Document("First"));
            _store.Save(path, Document("Second"));
            File.WriteAllText(path, "{ truncated");

            ProjectOpenResult result = _store.Load(path);
            Assert.That(result.UsedFallback, Is.True);
            Assert.That(result.LoadedFrom, Is.EqualTo(path + ProjectFileStore.BackupSuffix));
            Assert.That(result.RecoveryNote, Does.Contain("backup"));
            Assert.That(result.Document.RootCircuit.Name, Is.EqualTo("First"));
        }

        [Test]
        public void InterruptedSave_IsRecoveredFromTheCompleteTemporaryFile()
        {
            string path = Path.Combine(_directory, "p" + ProjectFormat.FileExtension);
            File.WriteAllText(path + ProjectFileStore.TempSuffix, ProjectSerializer.ToJson(Document("Unfinished")));

            ProjectOpenResult result = _store.Load(path);
            Assert.That(result.UsedFallback, Is.True);
            Assert.That(result.RecoveryNote, Does.Contain("interrupted save"));
            Assert.That(result.Document.RootCircuit.Name, Is.EqualTo("Unfinished"));
        }

        [Test]
        public void UnreadableProjectWithoutFallback_Throws()
        {
            string path = Path.Combine(_directory, "p" + ProjectFormat.FileExtension);
            Assert.That(() => _store.Load(path), Throws.InstanceOf<FileNotFoundException>());
            File.WriteAllText(path, "garbage");
            Assert.That(() => _store.Load(path), Throws.TypeOf<ProjectFormatException>());
        }

        [Test]
        public void Autosave_IsOfferedOnlyWhenNewerThanTheProject()
        {
            string path = Path.Combine(_directory, "p" + ProjectFormat.FileExtension);
            _store.Save(path, Document("Saved"));
            string autosave = ProjectFileStore.AutosavePathFor(path);
            _store.WriteAutosave(autosave, Document("Autosaved"));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));

            RecoveryCandidate candidate = ProjectFileStore.FindRecovery(path, null);
            Assert.That(candidate, Is.Not.Null);
            Assert.That(candidate.ProjectPath, Is.EqualTo(path));
            Assert.That(_store.Read(candidate.AutosavePath).Document.RootCircuit.Name, Is.EqualTo("Autosaved"));

            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
            Assert.That(ProjectFileStore.FindRecovery(path, null), Is.Null, "Saved after the autosave: nothing to recover.");

            string untitled = Path.Combine(_directory, "Untitled.autosave");
            Assert.That(ProjectFileStore.FindRecovery(null, untitled), Is.Null);
            _store.WriteAutosave(untitled, Document("Untitled work"));
            Assert.That(ProjectFileStore.FindRecovery(null, untitled), Is.Not.Null);
        }

        [Test]
        public void ListProjects_ReturnsProjectFilesOnly_NewestFirst()
        {
            string older = Path.Combine(_directory, "older" + ProjectFormat.FileExtension);
            string newer = Path.Combine(_directory, "newer" + ProjectFormat.FileExtension);
            _store.Save(older, Document("Old"));
            _store.Save(newer, Document("New"));
            _store.Save(newer, Document("New 2")); // creates a .bak, which must not be listed
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-1));
            File.WriteAllText(Path.Combine(_directory, "notes.txt"), "x");

            List<string> files = ProjectFileStore.ListProjects(_directory);
            Assert.That(files.Count, Is.EqualTo(2));
            Assert.That(Path.GetFileName(files[0]), Is.EqualTo("newer" + ProjectFormat.FileExtension));
        }
    }
}
