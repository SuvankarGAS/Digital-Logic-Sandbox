using System.Collections.Generic;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Signals;
using TMPro;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering.Gates
{
    /// <summary>
    /// Pooled visual for one node: rounded body with outline, title, pins coloured by signal, and for boundaries a
    /// state indicator (input switch knob / output lamp). Built entirely from <see cref="GateGeometry"/>, so it
    /// knows nothing about which gate it represents. Has no Update.
    /// </summary>
    public sealed class GateView : MonoBehaviour
    {
        private readonly List<SpriteRenderer> _inputPins = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _outputPins = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _inputRings = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _outputRings = new List<SpriteRenderer>();
        private readonly List<Signal> _inputSignals = new List<Signal>();
        private readonly List<Signal> _outputSignals = new List<Signal>();

        private VisualTheme _theme;
        private SpriteRenderer _outline;
        private SpriteRenderer _fill;
        private SpriteRenderer _state;
        private SpriteRenderer _stateRing;
        private TextMeshPro _title;
        private TextMeshPro _subtitle;
        private GateGeometry _geometry;
        private Signal _stateSignal;
        private bool _selected;
        private bool _hovered;
        private bool _ghost;
        private bool _ghostValid = true;

        public NodeId Node { get; private set; }

        public GateGeometry Geometry => _geometry;

        public Signal StateSignal => _stateSignal;

        public bool IsSelected => _selected;

        public static GateView Create(Transform parent, VisualTheme theme)
        {
            var go = new GameObject("Gate");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<GateView>();
            view._theme = theme;
            view._outline = CreateSprite(go.transform, "Outline", RenderAssets.RoundedRect, theme, SortingOrders.GateOutline);
            view._fill = CreateSprite(go.transform, "Fill", RenderAssets.RoundedRect, theme, SortingOrders.GateFill);
            view._outline.drawMode = SpriteDrawMode.Sliced;
            view._fill.drawMode = SpriteDrawMode.Sliced;
            view._stateRing = CreateSprite(go.transform, "StateRing", RenderAssets.Circle, theme, SortingOrders.GateState);
            view._state = CreateSprite(go.transform, "State", RenderAssets.Circle, theme, SortingOrders.GateState + 1);
            view._title = CreateText(go.transform, "Title", 3.6f, FontStyles.Bold);
            view._subtitle = CreateText(go.transform, "Subtitle", 2.2f, FontStyles.Normal);
            return view;
        }

        public void Configure(NodeId node, GateGeometry geometry)
        {
            Node = node;
            _geometry = geometry;
            _selected = false;
            _hovered = false;
            _stateSignal = Signal.Low;

            Vector2 size = geometry.Size;
            _fill.size = size;
            bool isCustom = geometry.Kind == ComponentKind.Custom;
            bool isInput = geometry.Kind == ComponentKind.InputBoundary;
            bool isOutput = geometry.Kind == ComponentKind.OutputBoundary;
            bool isBoundary = isInput || isOutput;

            _fill.color = isBoundary ? _theme.BoundaryFill : isCustom ? _theme.CustomGateFill : _theme.GateFill;

            // Boundary state indicator sits at the end away from the pin.
            _state.gameObject.SetActive(isBoundary);
            _stateRing.gameObject.SetActive(isBoundary);
            float indicator = Mathf.Min(size.x, size.y) * 0.62f;
            Vector2 statePosition = Vector2.zero;
            if (isBoundary)
            {
                Vector2 pinOffset = isInput ? geometry.OutputPins[0] : geometry.InputPins[0];
                statePosition = -pinOffset.normalized * (Mathf.Max(size.x, size.y) * 0.5f - indicator * 0.5f - 0.15f);
                _state.transform.localPosition = statePosition;
                _state.transform.localScale = Vector3.one * indicator;
                _stateRing.transform.localPosition = statePosition;
                _stateRing.transform.localScale = Vector3.one * (indicator + 0.1f);
                _stateRing.color = _theme.GateOutline;
            }

            _title.text = geometry.Title;
            _title.rectTransform.sizeDelta = new Vector2(size.x, size.y);
            _title.alignment = TextAlignmentOptions.Center;
            if (isBoundary)
            {
                // Title shares the body with the indicator: shift it towards the pin side.
                float shift = (indicator * 0.5f + 0.1f);
                _title.transform.localPosition = new Vector3(-statePosition.normalized.x * shift,
                    -statePosition.normalized.y * shift, 0f);
                _title.fontSize = 3.0f;
            }
            else
            {
                _title.transform.localPosition = isCustom ? new Vector3(0f, 0.12f, 0f) : Vector3.zero;
                _title.fontSize = 3.6f;
            }

            _subtitle.gameObject.SetActive(isCustom);
            _subtitle.text = geometry.Subtitle;
            _subtitle.rectTransform.sizeDelta = new Vector2(size.x, 0.5f);
            _subtitle.transform.localPosition = new Vector3(0f, -0.32f, 0f);

            ConfigurePins(_inputPins, _inputRings, _inputSignals, geometry.InputPins, "In");
            ConfigurePins(_outputPins, _outputRings, _outputSignals, geometry.OutputPins, "Out");

            ApplyOutline();
            ApplyState();
            ApplyGhostTint();
        }

        public void SetPosition(Vector2 position)
        {
            transform.localPosition = new Vector3(position.x, position.y, 0f);
        }

        public void SetSelected(bool selected)
        {
            if (_selected == selected)
            {
                return;
            }

            _selected = selected;
            ApplyOutline();
        }

        public void SetHovered(bool hovered)
        {
            if (_hovered == hovered)
            {
                return;
            }

            _hovered = hovered;
            ApplyOutline();
        }

        /// <summary>Semi-transparent placement preview; <paramref name="valid"/> false tints it as invalid.</summary>
        public void SetGhost(bool ghost, bool valid = true)
        {
            _ghost = ghost;
            _ghostValid = valid;
            int offset = ghost ? SortingOrders.GhostOffset : 0;
            _outline.sortingOrder = SortingOrders.GateOutline + offset;
            _fill.sortingOrder = SortingOrders.GateFill + offset;
            _stateRing.sortingOrder = SortingOrders.GateState + offset;
            _state.sortingOrder = SortingOrders.GateState + 1 + offset;
            _title.sortingOrder = SortingOrders.Text + offset;
            _subtitle.sortingOrder = SortingOrders.Text + offset;
            SetPinOrder(_inputPins, _inputRings, offset);
            SetPinOrder(_outputPins, _outputRings, offset);
            ApplyOutline();
            ApplyGhostTint();
        }

        public void SetInputSignal(int index, Signal signal)
        {
            if ((uint)index >= (uint)_inputSignals.Count || _inputSignals[index] == signal)
            {
                return;
            }

            _inputSignals[index] = signal;
            _inputPins[index].color = Tint(_theme.SignalColor(signal));
        }

        public void SetOutputSignal(int index, Signal signal)
        {
            if ((uint)index >= (uint)_outputSignals.Count || _outputSignals[index] == signal)
            {
                return;
            }

            _outputSignals[index] = signal;
            _outputPins[index].color = Tint(_theme.SignalColor(signal));
        }

        public Signal GetInputSignal(int index)
        {
            return _inputSignals[index];
        }

        public Signal GetOutputSignal(int index)
        {
            return _outputSignals[index];
        }

        /// <summary>Boundary indicator state (switch value or lamp).</summary>
        public void SetStateSignal(Signal signal)
        {
            if (_stateSignal == signal)
            {
                return;
            }

            _stateSignal = signal;
            ApplyState();
        }

        public void SetPinHighlight(bool input, int index, bool highlighted)
        {
            List<SpriteRenderer> rings = input ? _inputRings : _outputRings;
            if ((uint)index < (uint)rings.Count)
            {
                rings[index].gameObject.SetActive(highlighted);
            }
        }

        public void ClearPinHighlights()
        {
            for (int i = 0; i < _inputRings.Count; i++)
            {
                _inputRings[i].gameObject.SetActive(false);
            }

            for (int i = 0; i < _outputRings.Count; i++)
            {
                _outputRings[i].gameObject.SetActive(false);
            }
        }

        private void ConfigurePins(List<SpriteRenderer> pins, List<SpriteRenderer> rings, List<Signal> signals,
            Vector2[] offsets, string prefix)
        {
            while (pins.Count < offsets.Length)
            {
                pins.Add(CreateSprite(transform, prefix + "Pin", RenderAssets.Circle, _theme, SortingOrders.Pin));
                SpriteRenderer ring = CreateSprite(transform, prefix + "Ring", RenderAssets.Circle, _theme,
                    SortingOrders.PinHighlight);
                rings.Add(ring);
                signals.Add(Signal.Low);
            }

            float pinSize = _theme.PinRadius * 2f;
            for (int i = 0; i < pins.Count; i++)
            {
                bool active = i < offsets.Length;
                pins[i].gameObject.SetActive(active);
                rings[i].gameObject.SetActive(false);
                if (!active)
                {
                    continue;
                }

                pins[i].transform.localPosition = offsets[i];
                pins[i].transform.localScale = Vector3.one * pinSize;
                pins[i].color = Tint(_theme.SignalColor(Signal.Low));
                rings[i].transform.localPosition = offsets[i];
                rings[i].transform.localScale = Vector3.one * (pinSize * 2.2f);
                rings[i].color = new Color(_theme.CompatiblePin.r, _theme.CompatiblePin.g, _theme.CompatiblePin.b, 0.55f);
                signals[i] = Signal.Low;
            }
        }

        private void ApplyOutline()
        {
            if (_geometry == null)
            {
                return;
            }

            bool isCustom = _geometry.Kind == ComponentKind.Custom;
            Color color = isCustom ? _theme.CustomGateOutline : _theme.GateOutline;
            float thickness = _theme.OutlineThickness;
            if (_ghost && !_ghostValid)
            {
                color = _theme.Invalid;
            }
            else if (_selected)
            {
                color = _theme.Selection;
                thickness = _theme.SelectedOutlineThickness;
            }
            else if (_hovered)
            {
                color = Color.Lerp(color, _theme.Hover, 0.6f);
            }

            _outline.size = _geometry.Size + Vector2.one * (thickness * 2f);
            _outline.color = Tint(color);
        }

        private void ApplyState()
        {
            if (_geometry == null || !_state.gameObject.activeSelf)
            {
                return;
            }

            Color color = _stateSignal == Signal.High
                ? _theme.SignalColor(Signal.High)
                : _stateSignal == Signal.Low ? _theme.ButtonNormal : _theme.SignalColor(_stateSignal);
            _state.color = Tint(color);
            _stateRing.color = Tint(_stateSignal == Signal.High ? _theme.SignalColor(Signal.High) : _theme.GateOutline);
        }

        private void ApplyGhostTint()
        {
            if (_geometry == null)
            {
                return;
            }

            Color fill = _geometry.Kind == ComponentKind.InputBoundary || _geometry.Kind == ComponentKind.OutputBoundary
                ? _theme.BoundaryFill
                : _geometry.Kind == ComponentKind.Custom ? _theme.CustomGateFill : _theme.GateFill;
            _fill.color = Tint(fill);
            _title.color = Tint(_theme.GateText);
            _subtitle.color = Tint(_theme.GateSubtext);
            ApplyState();
            for (int i = 0; i < _inputPins.Count; i++)
            {
                _inputPins[i].color = Tint(_theme.SignalColor(_inputSignals[i]));
            }

            for (int i = 0; i < _outputPins.Count; i++)
            {
                _outputPins[i].color = Tint(_theme.SignalColor(_outputSignals[i]));
            }
        }

        private Color Tint(Color color)
        {
            if (_ghost)
            {
                color.a *= 0.55f;
            }

            return color;
        }

        private static void SetPinOrder(List<SpriteRenderer> pins, List<SpriteRenderer> rings, int offset)
        {
            for (int i = 0; i < pins.Count; i++)
            {
                pins[i].sortingOrder = SortingOrders.Pin + offset;
                rings[i].sortingOrder = SortingOrders.PinHighlight + offset;
            }
        }

        private static SpriteRenderer CreateSprite(Transform parent, string name, Sprite sprite, VisualTheme theme, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = theme.UnlitMaterial;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static TextMeshPro CreateText(Transform parent, string name, float size, FontStyles style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshPro>();
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.sortingOrder = SortingOrders.Text;
            text.raycastTarget = false;
            return text;
        }
    }
}
