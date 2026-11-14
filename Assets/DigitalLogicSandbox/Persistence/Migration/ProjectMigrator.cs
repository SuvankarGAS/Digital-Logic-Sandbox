using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DigitalLogicSandbox.Persistence.Migration
{
    /// <summary>
    /// One schema upgrade step, applied to the raw JSON before it is bound to DTOs. A migration from version N
    /// must leave the document valid for version N + 1 (the migrator updates <c>schemaVersion</c>).
    /// </summary>
    public interface IProjectMigration
    {
        /// <summary>The schema version this step upgrades from.</summary>
        int FromVersion { get; }

        /// <summary>Short description for diagnostics, e.g. "Rename 'wires' to 'connections'".</summary>
        string Description { get; }

        void Apply(JObject document);
    }

    /// <summary>
    /// Upgrades project JSON from its stored schema version to <see cref="ProjectFormat.CurrentSchemaVersion"/>
    /// by running consecutive <see cref="IProjectMigration"/> steps. Files from a newer application are refused
    /// rather than guessed at.
    /// </summary>
    public sealed class ProjectMigrator
    {
        private readonly Dictionary<int, IProjectMigration> _steps = new Dictionary<int, IProjectMigration>();

        public ProjectMigrator(int currentVersion, IEnumerable<IProjectMigration> steps = null)
        {
            if (currentVersion < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(currentVersion));
            }

            CurrentVersion = currentVersion;
            if (steps == null)
            {
                return;
            }

            foreach (IProjectMigration step in steps)
            {
                if (step == null)
                {
                    continue;
                }

                if (_steps.ContainsKey(step.FromVersion))
                {
                    throw new ArgumentException("Two migrations upgrade from schema version " + step.FromVersion + ".");
                }

                _steps.Add(step.FromVersion, step);
            }
        }

        /// <summary>The application's migrator: every step the shipped format has ever needed.</summary>
        public static ProjectMigrator CreateDefault()
        {
            // New steps are appended here whenever ProjectFormat.CurrentSchemaVersion is increased.
            return new ProjectMigrator(ProjectFormat.CurrentSchemaVersion, new IProjectMigration[]
            {
                new AddSimulationSettingsMigration(),
                new AddBusSupportMigration(),
            });
        }

        public int CurrentVersion { get; }

        /// <summary>
        /// Upgrades <paramref name="document"/> in place. Returns the version the file was stored with. Throws
        /// <see cref="ProjectFormatException"/> when the version is missing, newer than supported, or a step is
        /// missing.
        /// </summary>
        public int Migrate(JObject document, List<string> appliedSteps = null)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            JToken token = document["schemaVersion"];
            if (token == null || token.Type != JTokenType.Integer)
            {
                throw new ProjectFormatException("The file has no schema version; it is not a Digital Logic Sandbox project.");
            }

            int stored = token.Value<int>();
            if (stored > CurrentVersion)
            {
                throw new ProjectFormatException("The project was saved by a newer version of the application (schema " +
                    stored + "; this version supports up to " + CurrentVersion + "). Update the application to open it.");
            }

            if (stored < 1)
            {
                throw new ProjectFormatException("Invalid schema version " + stored + ".");
            }

            for (int version = stored; version < CurrentVersion; version++)
            {
                if (!_steps.TryGetValue(version, out IProjectMigration step))
                {
                    throw new ProjectFormatException("No migration exists from schema version " + version + " to " +
                        (version + 1) + ".");
                }

                step.Apply(document);
                document["schemaVersion"] = version + 1;
                appliedSteps?.Add("v" + version + " -> v" + (version + 1) + ": " + step.Description);
            }

            return stored;
        }
    }

    /// <summary>The file is not a readable project (corrupt JSON, unknown schema, missing data).</summary>
    public sealed class ProjectFormatException : Exception
    {
        public ProjectFormatException(string message, Exception inner = null)
            : base(message, inner)
        {
        }
    }
}
