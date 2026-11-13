using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.UI.ContextMenus;
using DigitalLogicSandbox.UI.Dialogs;
using DigitalLogicSandbox.UI.Inspector;
using DigitalLogicSandbox.UI.Panels;
using DigitalLogicSandbox.UI.Toolbar;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI
{
    /// <summary>
    /// Screen-space UI around the workspace: toolbar (top), breadcrumb bar, gate library (left), inspector (right), status bar (bottom), context menu and
    /// modal dialogs. The
    /// workspace camera renders full screen behind the panels. Pointer input over the panels is filtered out by
    /// the workspace through <see cref="IsPointerOverUi"/>.
    /// </summary>
    public sealed class AppShell : MonoBehaviour
    {
        private Canvas _canvas;

        public UiFactory Factory { get; private set; }

        public ToolbarView Toolbar { get; private set; }

        public LibraryPanel Library { get; private set; }

        public StatusBarView StatusBar { get; private set; }

        public InspectorPanel Inspector { get; private set; }

        public PackagingDialog PackagingDialog { get; private set; }

        public NavigationBar NavigationBar { get; private set; }

        public ContextMenuView ContextMenu { get; private set; }

        public VersionSaveDialog VersionSaveDialog { get; private set; }

        public Canvas Canvas => _canvas;

        public static AppShell Create(VisualTheme theme, WorkspaceController workspace, ComponentCatalog catalog,
            DefinitionLibrary library)
        {
            var go = new GameObject("UI", typeof(RectTransform));
            go.layer = 5;
            var shell = go.AddComponent<AppShell>();
            shell._canvas = go.AddComponent<Canvas>();
            shell._canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            shell._canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            shell.Factory = new UiFactory(theme);
            shell.Library = LibraryPanel.Create(go.transform, shell.Factory, workspace, catalog, library);
            shell.Toolbar = ToolbarView.Create(go.transform, shell.Factory, workspace);
            shell.StatusBar = StatusBarView.Create(go.transform, shell.Factory, workspace);
            shell.Inspector = InspectorPanel.Create(go.transform, shell.Factory, workspace);
            shell.NavigationBar = NavigationBar.Create(go.transform, shell.Factory, workspace);
            shell.ContextMenu = ContextMenuView.Create(go.transform, shell.Factory, workspace);
            shell.PackagingDialog = PackagingDialog.Create(go.transform, shell.Factory, workspace);
            shell.VersionSaveDialog = VersionSaveDialog.Create(go.transform, shell.Factory, workspace);

            shell.Toolbar.CreateGateRequested += () => shell.PackagingDialog.Open(selectionOnly: false);
            shell.Inspector.PackageRequested += selectionOnly => shell.PackagingDialog.Open(selectionOnly);
            shell.ContextMenu.PackageRequested += selectionOnly => shell.PackagingDialog.Open(selectionOnly);
            shell.Inspector.SaveDraftRequested += shell.VersionSaveDialog.Open;
            shell.NavigationBar.SaveRequested += shell.VersionSaveDialog.Open;
            return shell;
        }

        /// <summary>True while the pointer is over any UI element that blocks raycasts.</summary>
        public static bool IsPointerOverUi()
        {
            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }

        /// <summary>True while a text field has keyboard focus (workspace shortcuts must not fire).</summary>
        public static bool IsTextInputFocused()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
            {
                return false;
            }

            TMP_InputField field = eventSystem.currentSelectedGameObject.GetComponent<TMP_InputField>();
            return field != null && field.isFocused;
        }
    }
}
