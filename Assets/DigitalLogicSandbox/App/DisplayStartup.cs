using UnityEngine;

namespace DigitalLogicSandbox.App
{
    /// <summary>
    /// Starts the player in borderless fullscreen at the desktop resolution, before Unity's splash screen.
    /// </summary>
    /// <remarks>
    /// The player settings already default to <see cref="FullScreenMode.FullScreenWindow"/>, but Unity remembers the
    /// last window mode and size between launches (in the registry on Windows). A session that was switched to a
    /// window with Alt+Enter, or an older windowed build, would otherwise keep reopening as a window. Player builds
    /// only; the editor's Game view is unaffected.
    /// </remarks>
    public static class DisplayStartup
    {
#if !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void EnterBorderlessFullscreen()
        {
            int width = Display.main.systemWidth;
            int height = Display.main.systemHeight;
            if (width <= 0 || height <= 0)
            {
                Resolution current = Screen.currentResolution;
                width = current.width;
                height = current.height;
            }

            if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow || Screen.width != width ||
                Screen.height != height)
            {
                Screen.SetResolution(width, height, FullScreenMode.FullScreenWindow);
            }
        }
#endif
    }
}
