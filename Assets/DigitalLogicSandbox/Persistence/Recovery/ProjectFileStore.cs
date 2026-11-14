using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalLogicSandbox.Persistence.Deserialization;
using DigitalLogicSandbox.Persistence.Migration;
using DigitalLogicSandbox.Persistence.Serialization;

namespace DigitalLogicSandbox.Persistence.Recovery
{
    /// <summary>Result of opening a project file, including how it was recovered.</summary>
    public sealed class ProjectOpenResult
    {
        internal ProjectOpenResult(ProjectLoadResult load, string path, string loadedFrom, string recoveryNote)
        {
            Load = load;
            Path = path;
            LoadedFrom = loadedFrom;
            RecoveryNote = recoveryNote;
        }

        public ProjectLoadResult Load { get; }

        public ProjectDocument Document => Load.Document;

        /// <summary>The project path that was requested.</summary>
        public string Path { get; }

        /// <summary>The file actually read (the project, its backup, or an unfinished save).</summary>
        public string LoadedFrom { get; }

        /// <summary>Non-null when the project could only be read from a backup or temporary file.</summary>
        public string RecoveryNote { get; }

        public bool UsedFallback => RecoveryNote != null;
    }

    /// <summary>An autosave that holds newer work than the project it belongs to.</summary>
    public sealed class RecoveryCandidate
    {
        internal RecoveryCandidate(string autosavePath, string projectPath, DateTime autosaveUtc)
        {
            AutosavePath = autosavePath;
            ProjectPath = projectPath;
            AutosaveUtc = autosaveUtc;
        }

        public string AutosavePath { get; }

        /// <summary>The project it belongs to, or null for an untitled project.</summary>
        public string ProjectPath { get; }

        public DateTime AutosaveUtc { get; }
    }

    /// <summary>
    /// File-system side of persistence:
    /// <list type="bullet">
    /// <item>Atomic saves: write <c>*.tmp</c>, flush to disk, then replace the project and keep the previous file
    /// as <c>*.bak</c>. An interrupted save never damages the existing project.</item>
    /// <item>Fallback loading: if the project is unreadable, the backup (or a complete leftover temporary file) is
    /// used and the user is told.</item>
    /// <item>Autosave files next to the project (or in an untitled slot), detected on startup when newer than the
    /// project.</item>
    /// </list>
    /// </summary>
    public sealed class ProjectFileStore
    {
        public const string TempSuffix = ".tmp";
        public const string BackupSuffix = ".bak";
        public const string AutosaveSuffix = ".autosave";

        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private readonly ProjectDeserializer _deserializer;

        public ProjectFileStore(ProjectDeserializer deserializer = null)
        {
            _deserializer = deserializer ?? new ProjectDeserializer();
        }

        // ------------------------------------------------------------------------------------------------
        // Saving
        // ------------------------------------------------------------------------------------------------

        /// <summary>Serializes and writes <paramref name="document"/> atomically, keeping a backup of the previous file.</summary>
        public void Save(string path, ProjectDocument document)
        {
            document.SavedUtc = DateTime.UtcNow;
            WriteAtomic(path, ProjectSerializer.ToJson(document), keepBackup: true);
        }

        /// <summary>Writes the autosave for a project (atomic, no backup).</summary>
        public void WriteAutosave(string autosavePath, ProjectDocument document)
        {
            document.SavedUtc = DateTime.UtcNow;
            WriteAtomic(autosavePath, ProjectSerializer.ToJson(document), keepBackup: false);
        }

        public static void WriteAtomic(string path, string contents, bool keepBackup)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("A file path is required.", nameof(path));
            }

            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = path + TempSuffix;
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Utf8.GetBytes(contents);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }

            string backup = keepBackup ? path + BackupSuffix : null;
            try
            {
                File.Replace(temp, path, backup, ignoreMetadataErrors: true);
            }
            catch (Exception exception) when (exception is PlatformNotSupportedException || exception is IOException)
            {
                // Some file systems do not support File.Replace: fall back to copy + move (still never leaves the
                // project missing without either the backup or the complete temporary file on disk).
                if (backup != null)
                {
                    File.Copy(path, backup, overwrite: true);
                }

                File.Delete(path);
                File.Move(temp, path);
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Loading
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Reads a project. When the file is missing or damaged, a complete <c>*.tmp</c> (interrupted save) or the
        /// <c>*.bak</c> backup is used instead and <see cref="ProjectOpenResult.RecoveryNote"/> explains it. Throws
        /// <see cref="ProjectFormatException"/> (or <see cref="FileNotFoundException"/>) when nothing is readable.
        /// </summary>
        public ProjectOpenResult Load(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("A file path is required.", nameof(path));
            }

            Exception primaryError;
            try
            {
                return new ProjectOpenResult(Read(path), path, path, null);
            }
            catch (Exception exception) when (IsReadFailure(exception))
            {
                primaryError = exception;
            }

            string[] fallbacks = { path + TempSuffix, path + BackupSuffix };
            for (int i = 0; i < fallbacks.Length; i++)
            {
                if (!File.Exists(fallbacks[i]))
                {
                    continue;
                }

                try
                {
                    ProjectLoadResult load = Read(fallbacks[i]);
                    string note = (primaryError is FileNotFoundException ? "The project file was missing" : "The project file could not be read") +
                                  " (" + primaryError.Message + "). Opened " +
                                  (i == 0 ? "the copy left by an interrupted save" : "the backup from the previous save") +
                                  " instead; save to repair the project file.";
                    return new ProjectOpenResult(load, path, fallbacks[i], note);
                }
                catch (Exception exception) when (IsReadFailure(exception))
                {
                    // try the next fallback
                }
            }

            if (primaryError is FileNotFoundException)
            {
                throw primaryError;
            }

            throw primaryError as ProjectFormatException ??
                  new ProjectFormatException("The project could not be read: " + primaryError.Message, primaryError);
        }

        /// <summary>Reads an autosave or any project file without fallbacks.</summary>
        public ProjectLoadResult Read(string path)
        {
            string json = File.ReadAllText(path, Utf8);
            return _deserializer.Read(json);
        }

        // ------------------------------------------------------------------------------------------------
        // Autosave and recovery
        // ------------------------------------------------------------------------------------------------

        public static string AutosavePathFor(string projectPath)
        {
            return projectPath + AutosaveSuffix;
        }

        /// <summary>
        /// Returns the autosave for <paramref name="projectPath"/> (or the untitled autosave when it is null) if it
        /// exists and is newer than the project file; otherwise null.
        /// </summary>
        public static RecoveryCandidate FindRecovery(string projectPath, string untitledAutosavePath)
        {
            string autosave = projectPath != null ? AutosavePathFor(projectPath) : untitledAutosavePath;
            if (string.IsNullOrEmpty(autosave) || !File.Exists(autosave))
            {
                return null;
            }

            DateTime autosaveTime = File.GetLastWriteTimeUtc(autosave);
            if (projectPath != null && File.Exists(projectPath) && File.GetLastWriteTimeUtc(projectPath) >= autosaveTime)
            {
                return null; // the project was saved after the autosave: nothing to recover
            }

            return new RecoveryCandidate(autosave, projectPath, autosaveTime);
        }

        public static void DeleteIfExists(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>Project files in <paramref name="directory"/>, most recently written first.</summary>
        public static List<string> ListProjects(string directory)
        {
            var files = new List<string>();
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return files;
            }

            files.AddRange(Directory.GetFiles(directory, "*" + ProjectFormat.FileExtension));
            files.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            return files;
        }

        private static bool IsReadFailure(Exception exception)
        {
            return exception is IOException || exception is UnauthorizedAccessException ||
                   exception is ProjectFormatException;
        }
    }
}
