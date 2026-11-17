using System;
using System.Collections.Generic;

namespace DigitalLogicSandbox.UI
{
    /// <summary>
    /// Project file operations as seen by the UI. Implemented by the application layer (which owns persistence),
    /// so the UI never references the persistence assembly or the composition root directly.
    /// </summary>
    public interface IProjectService
    {
        /// <summary>Raised when the project name, path or dirty state changes.</summary>
        event Action Changed;

        /// <summary>File name without extension, or "Untitled".</summary>
        string DisplayName { get; }

        /// <summary>Full path of the project file, or null for a project that was never saved.</summary>
        string CurrentPath { get; }

        /// <summary>True when there are changes since the last save or load.</summary>
        bool IsDirty { get; }

        /// <summary>Default folder for project files.</summary>
        string ProjectsDirectory { get; }

        /// <summary>Project files in <see cref="ProjectsDirectory"/>, newest first.</summary>
        IReadOnlyList<string> ListProjects();

        /// <summary>Turns a bare name ("adder") or a path into a full project path with the right extension.</summary>
        string ResolvePath(string nameOrPath);

        /// <summary>Discards the current project (no confirmation; the UI asks first) and starts an empty one.</summary>
        void NewProject();

        /// <summary>Discards the current project (no confirmation; the UI asks first) and opens the CPU demo.</summary>
        void OpenCpuDemo();

        /// <summary>Opens a project. <paramref name="message"/> explains failures or recovery notes.</summary>
        bool Open(string path, out string message);

        /// <summary>Saves to <see cref="CurrentPath"/>. Returns false (with a message) when there is no path or writing failed.</summary>
        bool Save(out string message);

        /// <summary>Saves to a new path, which becomes the current path.</summary>
        bool SaveAs(string path, out string message);
    }
}
