using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DigitalLogicSandbox.UI.Dialogs
{
    /// <summary>
    /// The Windows common Open and Save As dialogs (comdlg32), modal over the application window. Unity's runtime
    /// has no file dialogs of its own; on other platforms <see cref="IsAvailable"/> is false and callers use the
    /// in-app <see cref="ProjectFileDialog"/>. The calls block the main thread until the user closes the dialog.
    /// </summary>
    public static class NativeFileDialog
    {
        /// <summary>Filter for project files, in the comdlg32 "label\0pattern\0" form.</summary>
        public const string ProjectFilter = "Digital Logic Sandbox project (*.dlsproj)\0*.dlsproj\0All files (*.*)\0*.*\0\0";

        public const string CsvFilter = "CSV file (*.csv)\0*.csv\0All files (*.*)\0*.*\0\0";

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int MaxPath = 4096;
        private const int OfnOverwritePrompt = 0x00000002;
        private const int OfnHideReadOnly = 0x00000004;
        private const int OfnNoChangeDir = 0x00000008;
        private const int OfnPathMustExist = 0x00000800;
        private const int OfnFileMustExist = 0x00001000;
        private const int OfnExplorer = 0x00080000;

        public static bool IsAvailable => true;

        /// <summary>Asks for an existing file. Returns false when the user cancels.</summary>
        public static bool TryOpen(string title, string filter, string initialDirectory, out string path)
        {
            return Run(false, title, filter, initialDirectory, null, null, out path);
        }

        /// <summary>
        /// Asks where to save. <paramref name="defaultExtension"/> (without the dot) is appended when the user types
        /// none. Replacing an existing file is confirmed by the dialog itself. Returns false when the user cancels.
        /// </summary>
        public static bool TrySave(string title, string filter, string initialDirectory, string defaultName,
            string defaultExtension, out string path)
        {
            return Run(true, title, filter, initialDirectory, defaultName, defaultExtension, out path);
        }

        private static bool Run(bool save, string title, string filter, string initialDirectory, string defaultName,
            string defaultExtension, out string path)
        {
            path = null;
            IntPtr buffer = Marshal.AllocHGlobal(MaxPath * 2);
            try
            {
                char[] initial = new char[MaxPath];
                if (!string.IsNullOrEmpty(defaultName))
                {
                    string name = defaultName.Length < MaxPath ? defaultName : defaultName.Substring(0, MaxPath - 1);
                    name.CopyTo(0, initial, 0, name.Length);
                }

                Marshal.Copy(initial, 0, buffer, MaxPath);
                if (!string.IsNullOrEmpty(initialDirectory))
                {
                    Directory.CreateDirectory(initialDirectory);
                }

                var info = new OpenFileName
                {
                    structSize = Marshal.SizeOf(typeof(OpenFileName)),
                    owner = GetActiveWindow(),
                    filter = filter,
                    filterIndex = 1,
                    file = buffer,
                    maxFile = MaxPath,
                    initialDir = initialDirectory,
                    title = title,
                    defExt = defaultExtension,
                    flags = OfnExplorer | OfnNoChangeDir | OfnHideReadOnly | OfnPathMustExist |
                            (save ? OfnOverwritePrompt : OfnFileMustExist),
                };

                bool picked = save ? GetSaveFileNameW(ref info) : GetOpenFileNameW(ref info);
                if (!picked)
                {
                    return false; // cancelled (or an error, which comdlg32 reports the same way)
                }

                path = Marshal.PtrToStringUni(buffer);
                return !string.IsNullOrEmpty(path);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileNameW(ref OpenFileName info);

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetSaveFileNameW(ref OpenFileName info);

        /// <summary>OPENFILENAMEW (Win32 layout; field order matters).</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int structSize;
            public IntPtr owner;
            public IntPtr instance;
            public string filter;
            public IntPtr customFilter;
            public int maxCustFilter;
            public int filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            public string initialDir;
            public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public string defExt;
            public IntPtr custData;
            public IntPtr hook;
            public IntPtr templateName;
            public IntPtr reserved;
            public int reservedInt;
            public int flagsEx;
        }
#else
        public static bool IsAvailable => false;

        public static bool TryOpen(string title, string filter, string initialDirectory, out string path)
        {
            path = null;
            return false;
        }

        public static bool TrySave(string title, string filter, string initialDirectory, string defaultName,
            string defaultExtension, out string path)
        {
            path = null;
            return false;
        }
#endif

        /// <summary>True in Windows player builds: the editor keeps the in-app dialogs so tests never block.</summary>
        public static bool DefaultEnabled => IsAvailable && !Application.isEditor;
    }
}
