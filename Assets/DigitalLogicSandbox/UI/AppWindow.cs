#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
#endif

namespace DigitalLogicSandbox.UI
{
    /// <summary>
    /// Operations on the application window that Unity doesn't expose. The app runs borderless fullscreen, so there
    /// is no title bar to minimize from. Does nothing in the editor and on platforms other than Windows.
    /// </summary>
    public static class AppWindow
    {
        /// <summary>Minimizes the player window to the taskbar.</summary>
        public static void Minimize()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            const int swMinimize = 6;
            IntPtr window = GetActiveWindow();
            if (window != IntPtr.Zero)
            {
                ShowWindow(window, swMinimize);
            }
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
#endif
    }
}
