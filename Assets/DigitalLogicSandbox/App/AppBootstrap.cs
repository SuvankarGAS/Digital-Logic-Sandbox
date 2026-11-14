using DG.Tweening;
using DigitalLogicSandbox.Animation.Signals;
using DigitalLogicSandbox.CircuitEditor.Interaction;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.TruthTable.Generation;
using DigitalLogicSandbox.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace DigitalLogicSandbox.App
{
    /// <summary>
    /// Composition root. It creates the domain services (primitive registry, definition library, catalog), the
    /// workspace and the UI shell, then wires them together. This is the only place that knows about every layer.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class AppBootstrap : MonoBehaviour
    {
        [SerializeField] private VisualTheme _theme;
        [SerializeField] private Camera _camera;

        [Tooltip("Optional replacement bindings. Needs an action map named 'Workspace' with the standard actions.")]
        [SerializeField] private InputActionAsset _inputActions;

        [Tooltip("Initial signal animation settings (the user can change them at runtime).")]
        [SerializeField] private SignalAnimationSettings _signalAnimation = new SignalAnimationSettings();

        [Tooltip("Truth table limits: maximum inputs and rows, confirmation threshold and per-frame time budget.")]
        [SerializeField] private TruthTableLimits _truthTableLimits = new TruthTableLimits();

        [Tooltip("Editor only: reopen the last project and offer autosave recovery when entering play mode. Builds always do.")]
        [SerializeField] private bool _restoreLastSessionInEditor;

        [Tooltip("Seconds between autosaves while there are unsaved changes.")]
        [SerializeField] private float _autosaveIntervalSeconds = 60f;

        private WorkspaceControls _controls;

        public static AppBootstrap Instance { get; private set; }

        public DefinitionLibrary Library { get; private set; }

        public ComponentCatalog Catalog { get; private set; }

        public WorkspaceController Workspace { get; private set; }

        public AppShell Shell { get; private set; }

        public ProjectController Projects { get; private set; }

        public TruthTableLimits TruthTableLimits => _truthTableLimits;

        public VisualTheme Theme => _theme;

        private void Awake()
        {
            Instance = this;
            DOTween.Init(recycleAllByDefault: false, useSafeMode: true, LogBehaviour.ErrorsOnly);

            if (_theme == null)
            {
                Debug.LogWarning("AppBootstrap: no VisualTheme assigned; using runtime defaults.");
                _theme = VisualTheme.CreateRuntimeDefault();
            }

            _theme.EnsureMaterials();
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            EnsureEventSystem();

            Library = new DefinitionLibrary();
            Catalog = new ComponentCatalog(PrimitiveRegistry.CreateDefault(), Library);

            _controls = _inputActions != null
                ? new WorkspaceControls(_inputActions.FindActionMap(WorkspaceControls.MapName, throwIfNotFound: true))
                : WorkspaceControls.CreateDefault();

            var workspaceObject = new GameObject("Workspace");
            Workspace = workspaceObject.AddComponent<WorkspaceController>();
            Workspace.Initialize(new WorkspaceSetup
            {
                Catalog = Catalog,
                Library = Library,
                Theme = _theme,
                Camera = _camera,
                Controls = _controls,
                IsPointerOverUi = AppShell.IsPointerOverUi,
                IsTextInputFocused = AppShell.IsTextInputFocused,
                SignalAnimation = _signalAnimation ?? new SignalAnimationSettings(),
            });

            _truthTableLimits ??= new TruthTableLimits();
            Projects = gameObject.AddComponent<ProjectController>();
            Projects.AutosaveIntervalSeconds = _autosaveIntervalSeconds;
            Projects.Initialize(Workspace, Library, Catalog.Primitives, Workspace.AnimationSettings, _truthTableLimits);

            Shell = AppShell.Create(_theme, Workspace, Catalog, Library, _truthTableLimits, Projects);

            // Builds reopen the last project and offer to recover autosaved work. In the editor this is opt-in so
            // entering play mode (and automated tests) always start from a clean, empty project.
            bool restore = !Application.isEditor || _restoreLastSessionInEditor;
            Projects.RememberLastProject = restore;
            Projects.Startup(restore, restore ? AskRecover : null);
        }

        private void AskRecover(string description, System.Action recover, System.Action discard)
        {
            Shell.Confirm.Show("Recover unsaved work?", description, "Recover", recover, "Discard", discard, null);
        }

        private void OnDestroy()
        {
            _controls?.Dispose();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }
    }
}
