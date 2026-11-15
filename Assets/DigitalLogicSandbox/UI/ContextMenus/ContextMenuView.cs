using System;
using System.Collections.Generic;
using DigitalLogicSandbox.CircuitEditor.Placement;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.ContextMenus
{
    /// <summary>
    /// Right-click menu of the workspace. Items depend on what was under the pointer (custom gate, any node, wire
    /// or empty space) and on whether the current level is editable. A full-screen transparent blocker closes the
    /// menu on any outside click; Escape closes it as well.
    /// </summary>
    public sealed class ContextMenuView : MonoBehaviour, IPointerDownHandler
    {
        private const float ItemWidth = 230f;

        private readonly List<string> _labels = new List<string>();
        private readonly List<Button> _buttons = new List<Button>();

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private RectTransform _root;
        private RectTransform _menu;

        /// <summary>Raised by "Create Custom Gate..." items (true = from the selection).</summary>
        public event Action<bool> PackageRequested;

        /// <summary>"Generate Truth Table" on empty space (the shown circuit).</summary>
        public event Action CircuitTruthTableRequested;

        /// <summary>"Truth Table" on a gate.</summary>
        public event Action<NodeId> NodeTruthTableRequested;

        /// <summary>"Truth Table" on a library entry (a component with no instance in the circuit).</summary>
        public event Action<ComponentRef, string> ComponentTruthTableRequested;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>Labels of the items currently shown (tests and accessibility).</summary>
        public IReadOnlyList<string> Labels => _labels;

        public static ContextMenuView Create(Transform parent, UiFactory ui, WorkspaceController workspace)
        {
            Image blocker = ui.Panel("Context Menu", parent, new Color(0f, 0f, 0f, 0f));
            UiFactory.Stretch(blocker.rectTransform);
            var view = blocker.gameObject.AddComponent<ContextMenuView>();
            view._ui = ui;
            view._workspace = workspace;
            view._root = blocker.rectTransform;

            Image menu = ui.Card("Menu", blocker.rectTransform, ui.Theme.PanelHeader, radius: 12f);
            view._menu = menu.rectTransform;
            view._menu.anchorMin = view._menu.anchorMax = Vector2.zero;
            view._menu.pivot = new Vector2(0f, 1f);
            var layout = menu.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 1f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            menu.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            workspace.ContextMenuRequested += view.Show;
            blocker.gameObject.SetActive(false);
            return view;
        }

        /// <summary>Builds and shows the menu for <paramref name="request"/>.</summary>
        public void Show(ContextMenuRequest request)
        {
            UiFactory.DestroyChildren(_menu);
            _labels.Clear();
            _buttons.Clear();
            CircuitDefinition circuit = _workspace.Circuit;
            if (circuit == null)
            {
                return;
            }

            bool editable = _workspace.IsEditable;
            if (request.Node.IsValid && circuit.TryGetNode(request.Node, out NodeDefinition node))
            {
                if (node.Component.Kind == ComponentKind.Custom)
                {
                    NodeId id = request.Node;
                    Add("Open Internal Circuit", () => _workspace.OpenNode(id), _workspace.CanOpenNode(id));
                }

                if (!node.Component.IsUtility)
                {
                    NodeId gate = request.Node;
                    Add("Truth Table", () => NodeTruthTableRequested?.Invoke(gate), true);
                }

                Add("Rotate", () => _workspace.RotateSelection(), editable);
                Add("Duplicate", () => _workspace.DuplicateSelection(), editable);
                Add("Delete", () => _workspace.DeleteSelection(), editable);
                if (_workspace.CanPackage)
                {
                    Add("Create Custom Gate from Selection...", () => PackageRequested?.Invoke(true), true);
                }
            }
            else if (request.Wire.HasValue)
            {
                PinRef wire = request.Wire.Value;
                Add("Delete Wire", () => _workspace.DeleteWire(wire), editable);
            }
            else
            {
                if (_workspace.CanPackage)
                {
                    Add("Create Custom Gate...", () => PackageRequested?.Invoke(false), circuit.Outputs.Count > 0);
                }

                Add("Generate Truth Table...", () => CircuitTruthTableRequested?.Invoke(), circuit.Outputs.Count > 0);
                Add("Select All", _workspace.SelectAll, circuit.Nodes.Count > 0);
                Add("Frame Content", _workspace.FrameContent, true);
                if (_workspace.Navigator.CurrentIndex > 0)
                {
                    Add("Back to Parent Circuit", () => _workspace.NavigateUp(), true);
                }

                if (_workspace.Navigator.CanBeginEdit(out _))
                {
                    Add("Edit Implementation", () => _workspace.BeginEdit(), true);
                }
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Position(request.Screen);
        }

        /// <summary>Menu for a gate library entry: place it, or show its truth table.</summary>
        public void ShowForLibraryEntry(Vector2 screen, PlacementRequest request)
        {
            UiFactory.DestroyChildren(_menu);
            _labels.Clear();
            _buttons.Clear();
            bool canPlace = _workspace.Circuit != null && _workspace.IsEditable;
            Add("Place", () => _workspace.BeginPlacement(request, fromDrag: false), canPlace);
            if (!request.Component.IsUtility)
            {
                ComponentRef component = request.Component;
                string name = request.DisplayName;
                Add("Truth Table", () => ComponentTruthTableRequested?.Invoke(component, name), true);
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Position(screen);
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        /// <summary>Invokes the item with <paramref name="label"/> (as a click would). Returns false if absent or disabled.</summary>
        public bool Invoke(string label)
        {
            int index = _labels.IndexOf(label);
            if (index < 0 || !_buttons[index].interactable)
            {
                return false;
            }

            _buttons[index].onClick.Invoke();
            return true;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // Only the transparent blocker receives this: the click was outside the menu.
            if (eventData.pointerCurrentRaycast.gameObject == gameObject)
            {
                Close();
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
            }
        }

        private void Add(string label, Action action, bool enabled)
        {
            Button button = _ui.Button(label, _menu, label, () =>
            {
                Close();
                action();
            }, ItemWidth, 13f);
            TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.MidlineLeft;
            // Menu rows: no resting background, a soft tile on hover.
            ColorBlock colors = button.colors;
            Color hover = _ui.Theme.ButtonHover;
            colors.normalColor = new Color(hover.r, hover.g, hover.b, 0f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = colors.normalColor;
            button.colors = colors;
            button.interactable = enabled;
            _labels.Add(label);
            _buttons.Add(button);
        }

        private void Position(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 local);
            Vector2 size = _root.rect.size;
            Vector2 fromCorner = local + size * _root.pivot;
            float height = _labels.Count * 33f + 12f;
            float x = Mathf.Clamp(fromCorner.x, 0f, Mathf.Max(0f, size.x - ItemWidth - 8f));
            float y = Mathf.Clamp(fromCorner.y, height, size.y);
            _menu.anchoredPosition = new Vector2(x, y);
            _menu.sizeDelta = new Vector2(ItemWidth + 12f, _menu.sizeDelta.y);
        }

        private void OnDestroy()
        {
            if (_workspace != null)
            {
                _workspace.ContextMenuRequested -= Show;
            }
        }
    }
}
