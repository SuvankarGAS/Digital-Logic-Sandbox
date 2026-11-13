using System;
using UnityEngine.InputSystem;

namespace DigitalLogicSandbox.CircuitEditor.Interaction
{
    /// <summary>
    /// Input System actions used by the workspace. Bindings are data (an <see cref="InputActionMap"/>), so they can
    /// be replaced by an asset or rebound at runtime. <see cref="CreateDefault"/> builds the standard
    /// mouse-and-keyboard map. The workspace polls these actions from one central update.
    /// </summary>
    public sealed class WorkspaceControls : IDisposable
    {
        public const string MapName = "Workspace";

        private readonly InputActionMap _map;
        private readonly bool _ownsMap;

        public WorkspaceControls(InputActionMap map, bool ownsMap = false)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _ownsMap = ownsMap;
            Point = Require("Point");
            Primary = Require("Primary");
            Secondary = Require("Secondary");
            Middle = Require("Middle");
            Scroll = Require("Scroll");
            Delete = Require("Delete");
            Undo = Require("Undo");
            Redo = Require("Redo");
            Duplicate = Require("Duplicate");
            SelectAll = Require("SelectAll");
            Rotate = Require("Rotate");
            Cancel = Require("Cancel");
            Frame = Require("Frame");
            MultiSelect = Require("MultiSelect");
            PanModifier = Require("PanModifier");
        }

        public InputActionMap Map => _map;

        public InputAction Point { get; }

        public InputAction Primary { get; }

        public InputAction Secondary { get; }

        public InputAction Middle { get; }

        public InputAction Scroll { get; }

        public InputAction Delete { get; }

        public InputAction Undo { get; }

        public InputAction Redo { get; }

        public InputAction Duplicate { get; }

        public InputAction SelectAll { get; }

        public InputAction Rotate { get; }

        public InputAction Cancel { get; }

        public InputAction Frame { get; }

        public InputAction MultiSelect { get; }

        public InputAction PanModifier { get; }

        /// <summary>Builds the default map (not yet enabled).</summary>
        public static WorkspaceControls CreateDefault()
        {
            var map = new InputActionMap(MapName);

            map.AddAction("Point", InputActionType.PassThrough, "<Pointer>/position", expectedControlLayout: "Vector2");
            map.AddAction("Primary", InputActionType.Button, "<Mouse>/leftButton");
            map.AddAction("Secondary", InputActionType.Button, "<Mouse>/rightButton");
            map.AddAction("Middle", InputActionType.Button, "<Mouse>/middleButton");
            map.AddAction("Scroll", InputActionType.PassThrough, "<Mouse>/scroll", expectedControlLayout: "Vector2");

            InputAction delete = map.AddAction("Delete", InputActionType.Button, "<Keyboard>/delete");
            delete.AddBinding("<Keyboard>/backspace");

            AddShortcut(map, "Undo", "<Keyboard>/z");
            AddShortcut(map, "Redo", "<Keyboard>/y");
            AddShortcut(map, "Duplicate", "<Keyboard>/d");
            AddShortcut(map, "SelectAll", "<Keyboard>/a");

            map.AddAction("Rotate", InputActionType.Button, "<Keyboard>/r");
            map.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape");
            map.AddAction("Frame", InputActionType.Button, "<Keyboard>/f");

            InputAction multi = map.AddAction("MultiSelect", InputActionType.Button, "<Keyboard>/shift");
            multi.AddBinding("<Keyboard>/ctrl");
            map.AddAction("PanModifier", InputActionType.Button, "<Keyboard>/space");

            return new WorkspaceControls(map, ownsMap: true);
        }

        public void Enable()
        {
            _map.Enable();
        }

        public void Disable()
        {
            _map.Disable();
        }

        public void Dispose()
        {
            _map.Disable();
            if (_ownsMap)
            {
                _map.Dispose();
            }
        }

        private static void AddShortcut(InputActionMap map, string name, string key)
        {
            map.AddAction(name, InputActionType.Button)
                .AddCompositeBinding("OneModifier")
                .With("Modifier", "<Keyboard>/ctrl")
                .With("Binding", key);
        }

        private InputAction Require(string name)
        {
            InputAction action = _map.FindAction(name, throwIfNotFound: false);
            if (action == null)
            {
                throw new ArgumentException("Input action map '" + _map.name + "' has no action named '" + name + "'.");
            }

            return action;
        }
    }
}
