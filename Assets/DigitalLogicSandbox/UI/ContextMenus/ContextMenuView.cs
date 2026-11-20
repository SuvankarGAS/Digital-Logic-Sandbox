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
        private readonly List<GameObject> _separators = new List<GameObject>();

        private UiFactory _ui;
        private WorkspaceController _workspace;
        private RectTransform _root;
        private RectTransform _menu;

        /// <summary>Raised by "Create custom gate…" items (true = from the selection).</summary>
        public event Action<bool> PackageRequested;

        /// <summary>"Generate Truth Table" on empty space (the shown circuit).</summary>
        public event Action CircuitTruthTableRequested;

        /// <summary>"Truth table" on a gate.</summary>
        public event Action<NodeId> NodeTruthTableRequested;

        /// <summary>"Truth table" on a library entry (a component with no instance in the circuit).</summary>
        public event Action<ComponentRef, string> ComponentTruthTableRequested;

        /// <summary>Raised by "Edit program…" on a gate fed by program words.</summary>
        public event Action<NodeId> ProgramEditRequested;

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
            ClearItems();
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
                    Add("Open internal circuit", () => _workspace.OpenNode(id), _workspace.CanOpenNode(id));
                    if (_workspace.HasProgram(id))
                    {
                        Add(editable ? "Edit program…" : "View program…", () => ProgramEditRequested?.Invoke(id), true);
                    }
                }

                if (!node.Component.IsUtility)
                {
                    NodeId gate = request.Node;
                    Add("Truth table", () => NodeTruthTableRequested?.Invoke(gate), true);
                }

                Add("Rotate", () => _workspace.RotateSelection(), editable);
                Add("Duplicate", () => _workspace.DuplicateSelection(), editable);
                Add("Delete", () => _workspace.DeleteSelection(), editable);
                if (_workspace.CanPackage)
                {
                    Add("Create custom gate from selection…", () => PackageRequested?.Invoke(true), true);
                }
            }
            else if (request.Wire.HasValue)
            {
                PinRef wire = request.Wire.Value;
                Add("Delete wire", () => _workspace.DeleteWire(wire), editable);
            }
            else
            {
                if (_workspace.CanPackage)
                {
                    Add("Create custom gate…", () => PackageRequested?.Invoke(false), circuit.Outputs.Count > 0);
                }

                Add("Generate truth table…", () => CircuitTruthTableRequested?.Invoke(), circuit.Outputs.Count > 0);
                Add("Select all", _workspace.SelectAll, circuit.Nodes.Count > 0);
                Add("Frame content", _workspace.FrameContent, true);
                Add("Tidy layout", () => _workspace.TidyLayout(), _workspace.CanTidyLayout);
                if (_workspace.Navigator.CurrentIndex > 0)
                {
                    Add("Back to parent circuit", () => _workspace.NavigateUp(), true);
                }

                if (_workspace.Navigator.CanBeginEdit(out _))
                {
                    Add("Edit implementation", () => _workspace.BeginEdit(), true);
                }
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Position(request.Screen);
        }

        /// <summary>
        /// Menu for a gate library entry: place it, show its truth table or open it, followed by
        /// <paramref name="extra"/> items (pinning, managing custom gates) after a separator.
        /// </summary>
        public void ShowForLibraryEntry(Vector2 screen, PlacementRequest request, IReadOnlyList<MenuItem> extra = null)
        {
            ClearItems();
            bool canPlace = _workspace.Circuit != null && _workspace.IsEditable;
            Add("Place", () => _workspace.BeginPlacement(request, fromDrag: false), canPlace);
            if (!request.Component.IsUtility)
            {
                ComponentRef component = request.Component;
                string name = request.DisplayName;
                Add("Truth table", () => ComponentTruthTableRequested?.Invoke(component, name), true);
                if (component.Kind == ComponentKind.Custom)
                {
                    Add("Open internal circuit", () => _workspace.OpenDefinition(component.Definition),
                        _workspace.CanOpenDefinition(component.Definition));
                }
            }

            if (extra != null && extra.Count > 0)
            {
                AddSeparator();
                AddItems(extra);
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Position(screen);
        }

        /// <summary>
        /// Shows a menu of arbitrary <paramref name="items"/> at <paramref name="screen"/> (for example the program
        /// editor's instruction set picker). Each item closes the menu before its action runs.
        /// </summary>
        public void ShowItems(Vector2 screen, IReadOnlyList<MenuItem> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            ClearItems();
            AddItems(items);

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

        /// <summary>
        /// Removes the rows of the previous menu. Only the rows: the card's shadow, fill and border are children of
        /// the menu too, and destroying them left the menu without a background.
        /// </summary>
        private void ClearItems()
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                GameObject row = _buttons[i].gameObject;
                row.SetActive(false);
                Destroy(row);
            }

            _buttons.Clear();
            _labels.Clear();
            for (int i = 0; i < _separators.Count; i++)
            {
                _separators[i].SetActive(false);
                Destroy(_separators[i]);
            }

            _separators.Clear();
        }

        private void AddItems(IReadOnlyList<MenuItem> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].IsSeparator)
                {
                    if (_buttons.Count > 0)
                    {
                        AddSeparator();
                    }

                    continue;
                }

                Add(items[i].Label, items[i].Action, items[i].Enabled && items[i].Action != null);
            }
        }

        private void AddSeparator()
        {
            RectTransform holder = UiFactory.CreateRect("Separator", _menu);
            holder.gameObject.AddComponent<LayoutElement>().preferredHeight = 9f;
            Image line = _ui.Panel("Line", holder, _ui.Theme.PanelBorder);
            line.raycastTarget = false;
            RectTransform rect = line.rectTransform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(-12f, 1f);
            _separators.Add(holder.gameObject);
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
            UiFactory.SetColorsInstant(button, colors);
            button.interactable = enabled;
            _labels.Add(label);
            _buttons.Add(button);
        }

        private void Position(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 local);
            Vector2 size = _root.rect.size;
            Vector2 fromCorner = local + size * _root.pivot;
            float height = _labels.Count * 33f + _separators.Count * 10f + 12f;
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

    /// <summary>One row of <see cref="ContextMenuView.ShowItems"/>.</summary>
    public readonly struct MenuItem
    {
        public MenuItem(string label, Action action, bool enabled = true)
        {
            Label = label ?? string.Empty;
            Action = action;
            Enabled = enabled;
        }

        public string Label { get; }

        public Action Action { get; }

        public bool Enabled { get; }

        /// <summary>A divider line between groups of items.</summary>
        public bool IsSeparator => Label.Length == 0 && Action == null;

        public static MenuItem Separator => new MenuItem(string.Empty, null, false);
    }
}
