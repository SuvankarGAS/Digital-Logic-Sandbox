using DG.Tweening;
using DigitalLogicSandbox.CircuitEditor.Interaction;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.Rendering;
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
        public const string DefaultCircuitName = "Main Circuit";

        [SerializeField] private VisualTheme _theme;
        [SerializeField] private Camera _camera;

        [Tooltip("Optional replacement bindings. Needs an action map named 'Workspace' with the standard actions.")]
        [SerializeField] private InputActionAsset _inputActions;

        private WorkspaceControls _controls;

        public static AppBootstrap Instance { get; private set; }

        public DefinitionLibrary Library { get; private set; }

        public ComponentCatalog Catalog { get; private set; }

        public WorkspaceController Workspace { get; private set; }

        public AppShell Shell { get; private set; }

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
                Theme = _theme,
                Camera = _camera,
                Controls = _controls,
                IsPointerOverUi = AppShell.IsPointerOverUi,
                IsTextInputFocused = AppShell.IsTextInputFocused,
            });

            Shell = AppShell.Create(_theme, Workspace, Catalog, Library);
            NewCircuit();
        }

        /// <summary>Starts an empty main circuit.</summary>
        public void NewCircuit()
        {
            Workspace.LoadCircuit(new CircuitDefinition(DefinitionId.New(), DefaultCircuitName));
            Workspace.ResetCamera();
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
