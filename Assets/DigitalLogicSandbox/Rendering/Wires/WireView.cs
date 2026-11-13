using System.Collections.Generic;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Signals;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering.Wires
{
    /// <summary>
    /// Pooled visual for one connection (or the wiring preview). Signal state is shown by colour and width. Has no
    /// Update; it changes only when the presenter pushes a new path or state.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class WireView : MonoBehaviour
    {
        private LineRenderer _line;
        private VisualTheme _theme;
        private Signal _signal;
        private bool _selected;
        private bool _hovered;
        private bool _isPreview;
        private bool _previewValid;

        public PinRef Destination { get; set; }

        public LineRenderer Line => _line;

        public Signal Signal => _signal;

        public static WireView Create(Transform parent, VisualTheme theme)
        {
            var go = new GameObject("Wire");
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = theme.UnlitMaterial;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = SortingOrders.Wire;
            line.positionCount = 0;
            var view = go.AddComponent<WireView>();
            view._line = line;
            view._theme = theme;
            view.ResetState();
            return view;
        }

        public void ResetState()
        {
            _signal = Signal.Low;
            _selected = false;
            _hovered = false;
            _isPreview = false;
            Destination = default;
            _line.sortingOrder = SortingOrders.Wire;
            Apply();
        }

        public void SetPath(IReadOnlyList<Vector3> points)
        {
            _line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
            {
                _line.SetPosition(i, points[i]);
            }
        }

        public void SetSignal(Signal signal)
        {
            if (_signal == signal)
            {
                return;
            }

            _signal = signal;
            Apply();
        }

        public void SetSelected(bool selected)
        {
            if (_selected == selected)
            {
                return;
            }

            _selected = selected;
            _line.sortingOrder = selected ? SortingOrders.SelectedWire : SortingOrders.Wire;
            Apply();
        }

        public void SetHovered(bool hovered)
        {
            if (_hovered == hovered)
            {
                return;
            }

            _hovered = hovered;
            Apply();
        }

        /// <summary>Turns this view into the wiring preview; <paramref name="valid"/> colours it accordingly.</summary>
        public void SetPreview(bool valid)
        {
            _isPreview = true;
            _previewValid = valid;
            _line.sortingOrder = SortingOrders.PreviewWire;
            Apply();
        }

        private void Apply()
        {
            Color color;
            float width = _theme.WireWidth(_signal);
            if (_isPreview)
            {
                color = _previewValid ? _theme.CompatiblePin : _theme.TextSecondary;
                width = _theme.WireWidth(Signal.High);
            }
            else if (_selected)
            {
                color = _theme.Selection;
                width *= 1.5f;
            }
            else
            {
                color = _theme.SignalColor(_signal);
                if (_hovered)
                {
                    color = Color.Lerp(color, Color.white, 0.35f);
                }
            }

            _line.startColor = color;
            _line.endColor = color;
            _line.widthMultiplier = width;
        }
    }
}
