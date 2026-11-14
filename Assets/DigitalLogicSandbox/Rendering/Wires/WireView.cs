using System.Collections.Generic;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Signals;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering.Wires
{
    /// <summary>
    /// Pooled visual for one connection (or the wiring preview). Signal state is shown by colour and width. A
    /// signal transition (<see cref="SetTransition"/>) draws the new state from the source up to a travelling
    /// front and the old state beyond it, using the line's gradient and width curve (no extra objects). Has no
    /// Update; it changes only when the presenter or the animation controller pushes a new state.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class WireView : MonoBehaviour
    {
        private const float FrontLength = 0.06f;
        private const float EdgeSoftness = 0.004f;

        private static readonly AnimationCurve ConstantWidth = AnimationCurve.Constant(0f, 1f, 1f);

        private readonly List<Vector3> _points = new List<Vector3>(8);
        private readonly List<float> _cumulative = new List<float>(8);
        private readonly GradientColorKey[] _colorKeys = new GradientColorKey[5];
        private readonly GradientAlphaKey[] _alphaKeys = { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
        private readonly Keyframe[] _widthKeys = new Keyframe[4];

        private LineRenderer _line;
        private VisualTheme _theme;
        private SignalStyle _style;
        private Gradient _gradient;
        private AnimationCurve _widthCurve;
        private Signal _signal;
        private bool _selected;
        private bool _hovered;
        private bool _isPreview;
        private bool _previewValid;
        private bool _inTransition;
        private Signal _transitionFrom;
        private Signal _transitionTo;
        private float _transitionProgress;

        public PinRef Destination { get; set; }

        /// <summary>Multi-bit connection: drawn thicker, showing the aggregate state of its bits.</summary>
        public bool IsBus
        {
            get => _isBus;
            set
            {
                if (_isBus != value)
                {
                    _isBus = value;
                    Apply();
                }
            }
        }

        private bool _isBus;

        /// <summary>Width factor of bus wires relative to single-bit wires.</summary>
        public const float BusWidthFactor = 1.9f;

        public LineRenderer Line => _line;

        /// <summary>The settled signal shown (during a transition: the state being left).</summary>
        public Signal Signal => _signal;

        public bool InTransition => _inTransition;

        public float TransitionProgress => _transitionProgress;

        /// <summary>Path length in world units.</summary>
        public float Length => _cumulative.Count > 0 ? _cumulative[_cumulative.Count - 1] : 0f;

        public static WireView Create(Transform parent, VisualTheme theme, SignalStyle style = null)
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
            view._style = style ?? new SignalStyle(theme);
            view._gradient = new Gradient();
            view._widthCurve = new AnimationCurve();
            view.ResetState();
            return view;
        }

        /// <summary>Shared style (set by the presenter so a mode switch affects every wire).</summary>
        public SignalStyle Style
        {
            get => _style;
            set
            {
                _style = value ?? new SignalStyle(_theme);
                Apply();
            }
        }

        public void ResetState()
        {
            _signal = Signal.Low;
            _selected = false;
            _hovered = false;
            _isPreview = false;
            _inTransition = false;
            _isBus = false;
            Destination = default;
            _line.sortingOrder = SortingOrders.Wire;
            Apply();
        }

        public void SetPath(IReadOnlyList<Vector3> points)
        {
            _line.positionCount = points.Count;
            _points.Clear();
            _cumulative.Clear();
            float total = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                _line.SetPosition(i, points[i]);
                if (i > 0)
                {
                    total += Vector3.Distance(points[i - 1], points[i]);
                }

                _points.Add(points[i]);
                _cumulative.Add(total);
            }
        }

        /// <summary>World position at the normalised distance <paramref name="t"/> (0 = source, 1 = destination).</summary>
        public Vector3 GetPointAt(float t)
        {
            int count = _points.Count;
            if (count == 0)
            {
                return transform.position;
            }

            float length = Length;
            if (count == 1 || length <= 0f)
            {
                return _points[0];
            }

            float target = Mathf.Clamp01(t) * length;
            for (int i = 1; i < count; i++)
            {
                if (_cumulative[i] >= target)
                {
                    float segment = _cumulative[i] - _cumulative[i - 1];
                    float local = segment > 0f ? (target - _cumulative[i - 1]) / segment : 0f;
                    return Vector3.Lerp(_points[i - 1], _points[i], local);
                }
            }

            return _points[count - 1];
        }

        /// <summary>Shows a settled signal (ends any transition).</summary>
        public void SetSignal(Signal signal)
        {
            if (_signal == signal && !_inTransition)
            {
                return;
            }

            _signal = signal;
            _inTransition = false;
            Apply();
        }

        /// <summary>
        /// Shows <paramref name="to"/> travelling over <paramref name="from"/>: the first <paramref name="progress"/>
        /// of the path already carries the new state. Purely visual; never changes logical values.
        /// </summary>
        public void SetTransition(Signal from, Signal to, float progress)
        {
            _signal = from;
            _transitionFrom = from;
            _transitionTo = to;
            _transitionProgress = Mathf.Clamp01(progress);
            _inTransition = true;
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

        /// <summary>Re-applies colours and widths (after a style change).</summary>
        public void RefreshStyle()
        {
            Apply();
        }

        private void Apply()
        {
            if (_inTransition && !_isPreview && !_selected)
            {
                ApplyTransition();
                return;
            }

            Color color;
            float width = _style.WidthOf(_signal);
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
                color = _style.ColorOf(_signal);
                if (_hovered)
                {
                    color = Color.Lerp(color, Color.white, 0.35f);
                }
            }

            _line.widthCurve = ConstantWidth;
            _line.startColor = color;
            _line.endColor = color;
            _line.widthMultiplier = _isBus && !_isPreview ? width * BusWidthFactor : width;
        }

        private void ApplyTransition()
        {
            float p = Mathf.Clamp(_transitionProgress, EdgeSoftness * 2f, 1f - EdgeSoftness * 2f);
            Color to = _style.ColorOf(_transitionTo);
            Color from = _style.ColorOf(_transitionFrom);
            if (_hovered)
            {
                to = Color.Lerp(to, Color.white, 0.35f);
                from = Color.Lerp(from, Color.white, 0.35f);
            }

            _colorKeys[0] = new GradientColorKey(to, 0f);
            _colorKeys[1] = new GradientColorKey(to, Mathf.Max(0f, p - FrontLength));
            _colorKeys[2] = new GradientColorKey(_style.FrontColorOf(_transitionTo), p);
            _colorKeys[3] = new GradientColorKey(from, Mathf.Min(1f, p + EdgeSoftness));
            _colorKeys[4] = new GradientColorKey(from, 1f);
            _gradient.SetKeys(_colorKeys, _alphaKeys);
            _line.colorGradient = _gradient;

            float toWidth = _style.WidthOf(_transitionTo);
            float fromWidth = _style.WidthOf(_transitionFrom);
            float max = Mathf.Max(toWidth, fromWidth);
            _widthKeys[0] = new Keyframe(0f, toWidth / max, 0f, 0f);
            _widthKeys[1] = new Keyframe(p, toWidth / max, 0f, 0f);
            _widthKeys[2] = new Keyframe(Mathf.Min(1f, p + EdgeSoftness), fromWidth / max, 0f, 0f);
            _widthKeys[3] = new Keyframe(1f, fromWidth / max, 0f, 0f);
            _widthCurve.keys = _widthKeys;
            _line.widthCurve = _widthCurve;
            _line.widthMultiplier = _isBus ? max * BusWidthFactor : max;
        }
    }
}
