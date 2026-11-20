using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.TruthTable.Generation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs.Sequence
{
    /// <summary>
    /// Timing diagram of a sequence analysis: lanes for the inputs (editable: click or drag across steps), the
    /// clock domains and the outputs, drawn as square waves against a step ruler. With a clock pulse every step is
    /// split into "after inputs" (¼), "clock high" (½) and "after cycle" (¼), so the clock shows as a real pulse
    /// and level-sensitive behaviour stays visible. A short lead-in shows the power-on state.
    /// <para>
    /// Everything is drawn by one <see cref="QuadGraphic"/> for the visible range only; lane names and ruler
    /// numbers come from small label pools. Scrolling: wheel (vertical when lanes overflow), Shift+wheel
    /// (horizontal), Ctrl+wheel (zoom), or the scrollbars.
    /// </para>
    /// </summary>
    public sealed class SequenceTimelineView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerMoveHandler, IPointerExitHandler, IScrollHandler
    {
        public const float NameWidth = 136f;
        public const float RulerHeight = 26f;
        public const float LaneHeight = 30f;
        public const float GroupHeight = 24f;
        public const float MinStepWidth = 10f;
        public const float MaxStepWidth = 120f;

        /// <summary>Width of the power-on lead-in, in steps.</summary>
        public const float LeadIn = 0.8f;

        private const float ScrollbarSize = 10f;
        private const float WavePad = 7f;
        private const int RulerPoolSize = 12;
        private const int NamePoolSize = 10;
        private const int GroupPoolSize = 3;

        private enum LaneKind : byte
        {
            Group,
            Input,
            Clock,
            Output,
        }

        private struct Lane
        {
            public LaneKind Kind;
            public int Index;
            public float Top;
            public float Height;
        }

        private readonly List<Lane> _lanes = new List<Lane>();
        private readonly List<TextMeshProUGUI> _rulerLabels = new List<TextMeshProUGUI>(RulerPoolSize);
        private readonly List<TextMeshProUGUI> _nameLabels = new List<TextMeshProUGUI>(NamePoolSize);
        private readonly List<TextMeshProUGUI> _groupLabels = new List<TextMeshProUGUI>(GroupPoolSize);

        private VisualTheme _theme;
        private QuadGraphic _graphic;
        private RectTransform _rect;
        private RectTransform _namesMask;
        private RectTransform _rulerMask;
        private Scrollbar _hScroll;
        private Scrollbar _vScroll;
        private TextMeshProUGUI _empty;

        private SequenceTable _table;
        private IReadOnlyList<string> _inputNames = Array.Empty<string>();
        private IReadOnlyList<Signal[]> _steps;
        private int[] _patternLengths = Array.Empty<int>();
        private int _stepCount;
        private bool _clocked;
        private float _contentHeight;

        private float _stepWidth = 40f;
        private float _scrollX;
        private float _scrollY;
        private bool _syncingScrollbars;
        private bool _fitPending = true;
        private bool _userZoomed;

        private int _hoverRow = -1;
        private int _hoverLane = -1;
        private int _cursorRow = -1;
        private bool _painting;
        private int _paintInput;
        private int _paintLastStep;
        private Signal _paintValue;

        private Color _inputColor;
        private Color _outputColor;
        private Color _clockColor;
        private Color _unknownColor;
        private Color _floatingColor;

        /// <summary>Raised when the user sets input <c>input</c> to <c>value</c> for steps <c>from..to</c> (1-based, inclusive).</summary>
        public event Action<int, int, int, Signal> CellsEdited;

        /// <summary>Raised when the hovered or pinned row changes (the dialog updates its readout).</summary>
        public event Action CursorChanged;

        /// <summary>Row under the pointer (0 = power-on), or -1.</summary>
        public int HoverRow => _hoverRow;

        /// <summary>Pinned row (click a step; ←/→ move it), or -1.</summary>
        public int CursorRow => _cursorRow;

        /// <summary>The row the readout should describe: hovered first, then pinned.</summary>
        public int FocusRow => _hoverRow >= 0 ? _hoverRow : _cursorRow;

        public float StepWidth => _stepWidth;

        public float ScrollX => _scrollX;

        /// <summary>Lanes currently laid out (group headers included).</summary>
        public int LaneCount => _lanes.Count;

        public static SequenceTimelineView Create(Transform parent, UiFactory ui)
        {
            VisualTheme theme = ui.Theme;
            Image frame = ui.Panel("Timeline", parent, theme.WorkspaceBackground, rounded: true, radius: 10f);
            frame.raycastTarget = false;
            var frameLayout = frame.gameObject.AddComponent<LayoutElement>();
            frameLayout.flexibleHeight = 1f;
            frameLayout.minHeight = 200f;
            frame.gameObject.AddComponent<RectMask2D>().padding = new Vector4(1f, 1f, 1f, 1f);

            RectTransform plot = UiFactory.CreateRect("Plot", frame.transform);
            UiFactory.Stretch(plot);
            plot.gameObject.AddComponent<CanvasRenderer>();
            var graphic = plot.gameObject.AddComponent<QuadGraphic>();
            graphic.raycastTarget = true;
            var view = plot.gameObject.AddComponent<SequenceTimelineView>();
            view._theme = theme;
            view._graphic = graphic;
            view._rect = plot;
            view._inputColor = theme.SignalColor(Signal.High);
            view._outputColor = theme.Accent;
            view._clockColor = Color.Lerp(theme.TextSecondary, theme.TextPrimary, 0.35f);
            view._unknownColor = theme.SignalColor(Signal.Unknown);
            view._floatingColor = theme.SignalColor(Signal.HighImpedance);

            view._namesMask = UiFactory.CreateRect("Names", frame.transform);
            view._namesMask.anchorMin = new Vector2(0f, 0f);
            view._namesMask.anchorMax = new Vector2(0f, 1f);
            view._namesMask.pivot = new Vector2(0f, 1f);
            view._namesMask.offsetMin = new Vector2(0f, ScrollbarSize);
            view._namesMask.offsetMax = new Vector2(NameWidth, -RulerHeight);
            view._namesMask.gameObject.AddComponent<RectMask2D>();
            for (int i = 0; i < GroupPoolSize; i++)
            {
                TextMeshProUGUI label = ui.Text("Group " + i, view._namesMask, string.Empty, 10f, theme.TextMuted,
                    TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                label.characterSpacing = 6f;
                label.richText = true;
                label.overflowMode = TextOverflowModes.Overflow;
                TopLeft(label.rectTransform);
                label.gameObject.SetActive(false);
                view._groupLabels.Add(label);
            }

            for (int i = 0; i < NamePoolSize; i++)
            {
                TextMeshProUGUI label = ui.Text("Name " + i, view._namesMask, string.Empty, 12.5f, theme.TextPrimary,
                    TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                TopLeft(label.rectTransform);
                label.gameObject.SetActive(false);
                view._nameLabels.Add(label);
            }

            view._rulerMask = UiFactory.CreateRect("Ruler", frame.transform);
            view._rulerMask.anchorMin = new Vector2(0f, 1f);
            view._rulerMask.anchorMax = new Vector2(1f, 1f);
            view._rulerMask.pivot = new Vector2(0f, 1f);
            view._rulerMask.offsetMin = new Vector2(NameWidth, -RulerHeight);
            view._rulerMask.offsetMax = new Vector2(-ScrollbarSize, 0f);
            view._rulerMask.gameObject.AddComponent<RectMask2D>();
            for (int i = 0; i < RulerPoolSize; i++)
            {
                TextMeshProUGUI label = ui.Text("Tick " + i, view._rulerMask, string.Empty, 10.5f, theme.TextMuted,
                    TextAlignmentOptions.Center);
                label.overflowMode = TextOverflowModes.Overflow;
                RectTransform rect = label.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(48f, RulerHeight);
                label.gameObject.SetActive(false);
                view._rulerLabels.Add(label);
            }

            view._empty = ui.Text("Empty", frame.transform, string.Empty, 13f, theme.TextMuted, TextAlignmentOptions.Center);
            UiFactory.Stretch(view._empty.rectTransform, NameWidth, 0f, RulerHeight, 0f);
            view._empty.enableWordWrapping = true;

            view._hScroll = MakeScrollbar(ui, frame.transform, horizontal: true);
            view._hScroll.onValueChanged.AddListener(view.OnHorizontalScrollbar);
            view._vScroll = MakeScrollbar(ui, frame.transform, horizontal: false);
            view._vScroll.onValueChanged.AddListener(view.OnVerticalScrollbar);
            return view;
        }

        private static void TopLeft(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
        }

        private static Scrollbar MakeScrollbar(UiFactory ui, Transform parent, bool horizontal)
        {
            Image track = ui.Panel(horizontal ? "HScroll" : "VScroll", parent, new Color(1f, 1f, 1f, 0.025f),
                rounded: true, radius: 4f);
            RectTransform rect = track.rectTransform;
            if (horizontal)
            {
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.offsetMin = new Vector2(NameWidth, 1f);
                rect.offsetMax = new Vector2(-ScrollbarSize - 1f, ScrollbarSize - 1f);
            }
            else
            {
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.offsetMin = new Vector2(-ScrollbarSize + 1f, ScrollbarSize);
                rect.offsetMax = new Vector2(-1f, -RulerHeight);
            }

            RectTransform area = UiFactory.CreateRect("Sliding Area", rect);
            UiFactory.Stretch(area, 1f, 1f, 1f, 1f);
            Image handle = ui.Panel("Handle", area, ui.Theme.TextMuted, rounded: true, radius: 4f);
            handle.rectTransform.sizeDelta = Vector2.zero;
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.targetGraphic = handle;
            scrollbar.direction = horizontal ? Scrollbar.Direction.LeftToRight : Scrollbar.Direction.TopToBottom;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = scrollbar.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.45f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.75f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.9f);
            colors.selectedColor = colors.normalColor;
            colors.fadeDuration = 0.08f;
            UiFactory.SetColorsInstant(scrollbar, colors);
            return scrollbar;
        }

        // ------------------------------------------------------------------------------------------------
        // Data
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Shows a result. <paramref name="steps"/> are the applied inputs (null when the patterns are invalid);
        /// <paramref name="patternLengths"/> marks where each input starts holding its last value. Scroll, zoom
        /// and the pinned step are kept.
        /// </summary>
        public void Bind(SequenceTable table, IReadOnlyList<string> inputNames, IReadOnlyList<Signal[]> steps,
            int[] patternLengths, string emptyMessage = null)
        {
            _table = table;
            _inputNames = inputNames ?? Array.Empty<string>();
            _steps = steps;
            _patternLengths = patternLengths ?? Array.Empty<int>();
            int stepCount = table != null ? table.StepCount : steps?.Count ?? 0;
            if (stepCount != _stepCount && !_userZoomed)
            {
                _fitPending = true; // keep the whole sequence in view until the user picks a zoom
            }

            _stepCount = stepCount;
            _clocked = table != null && table.Clocked;
            if (_cursorRow > _stepCount)
            {
                _cursorRow = -1;
            }

            BuildLanes();
            _empty.text = emptyMessage ?? string.Empty;
            _empty.gameObject.SetActive(!string.IsNullOrEmpty(emptyMessage));
            Redraw();
        }

        /// <summary>Fits the whole sequence into the view on the next draw (used when opening a circuit).</summary>
        public void ResetView()
        {
            _fitPending = true;
            _userZoomed = false;
            _scrollX = 0f;
            _scrollY = 0f;
            _cursorRow = -1;
            _hoverRow = -1;
        }

        private void BuildLanes()
        {
            _lanes.Clear();
            float y = 0f;
            int inputs = _inputNames.Count;
            if (inputs > 0)
            {
                AddLane(LaneKind.Group, 0, ref y, GroupHeight);
                for (int i = 0; i < inputs; i++)
                {
                    AddLane(LaneKind.Input, i, ref y, LaneHeight);
                }
            }

            int clocks = _table != null ? _table.ClockExponents.Count : 0;
            if (clocks > 0)
            {
                AddLane(LaneKind.Group, 1, ref y, GroupHeight);
                for (int i = 0; i < clocks; i++)
                {
                    AddLane(LaneKind.Clock, i, ref y, LaneHeight);
                }
            }

            int outputs = _table != null ? _table.Outputs.Count : 0;
            if (outputs > 0)
            {
                AddLane(LaneKind.Group, 2, ref y, GroupHeight);
                for (int i = 0; i < outputs; i++)
                {
                    AddLane(LaneKind.Output, i, ref y, LaneHeight);
                }
            }

            _contentHeight = y + 6f;
        }

        private void AddLane(LaneKind kind, int index, ref float y, float height)
        {
            _lanes.Add(new Lane { Kind = kind, Index = index, Top = y, Height = height });
            y += height;
        }

        // ------------------------------------------------------------------------------------------------
        // Geometry
        // ------------------------------------------------------------------------------------------------

        private float Width => _rect.rect.width;

        private float Height => _rect.rect.height;

        private float PlotLeft => NameWidth;

        private float PlotRight => Width - ScrollbarSize;

        private float PlotTop => RulerHeight;

        private float PlotBottom => Height - ScrollbarSize;

        private float ViewWidth => Mathf.Max(1f, PlotRight - PlotLeft);

        private float ViewHeight => Mathf.Max(1f, PlotBottom - PlotTop);

        private float ContentWidth => (LeadIn + _stepCount) * _stepWidth + 24f;

        /// <summary>X (frame-local) of time <paramref name="units"/> (steps since the start of the lead-in).</summary>
        private float X(float units)
        {
            return PlotLeft + units * _stepWidth - _scrollX;
        }

        /// <summary>Start of step <paramref name="row"/> in time units (row 0 = power-on starts at 0).</summary>
        private static float RowStart(int row)
        {
            return row == 0 ? 0f : LeadIn + row - 1;
        }

        private static float RowEnd(int row)
        {
            return row == 0 ? LeadIn : LeadIn + row;
        }

        private int RowAt(float x)
        {
            float units = (x - PlotLeft + _scrollX) / _stepWidth;
            if (units < 0f || x < PlotLeft || x > PlotRight)
            {
                return -1;
            }

            if (units < LeadIn)
            {
                return 0;
            }

            int row = Mathf.FloorToInt(units - LeadIn) + 1;
            return row <= _stepCount ? row : -1;
        }

        private int LaneAt(float y)
        {
            if (y < PlotTop || y > PlotBottom)
            {
                return -1;
            }

            float content = y - PlotTop + _scrollY;
            for (int i = 0; i < _lanes.Count; i++)
            {
                if (content >= _lanes[i].Top && content < _lanes[i].Top + _lanes[i].Height)
                {
                    return i;
                }
            }

            return -1;
        }

        private void ClampScroll()
        {
            _scrollX = Mathf.Clamp(_scrollX, 0f, Mathf.Max(0f, ContentWidth - ViewWidth));
            _scrollY = Mathf.Clamp(_scrollY, 0f, Mathf.Max(0f, _contentHeight - ViewHeight));
        }

        /// <summary>Sets the step width (zoom), keeping the time under <paramref name="anchorX"/> in place.</summary>
        public void SetStepWidth(float width, float anchorX = -1f)
        {
            width = Mathf.Clamp(width, MinStepWidth, MaxStepWidth);
            if (anchorX < 0f)
            {
                anchorX = PlotLeft + ViewWidth * 0.5f;
            }

            float units = (anchorX - PlotLeft + _scrollX) / _stepWidth;
            _stepWidth = width;
            _scrollX = units * _stepWidth - (anchorX - PlotLeft);
            _fitPending = false;
            _userZoomed = true;
            Redraw();
        }

        public void ZoomIn()
        {
            SetStepWidth(_stepWidth * 1.4f);
        }

        public void ZoomOut()
        {
            SetStepWidth(_stepWidth / 1.4f);
        }

        /// <summary>Fits every step into the view when they fit at a readable width, otherwise a default width.</summary>
        public void FitToView()
        {
            float fit = ViewWidth / Mathf.Max(1f, LeadIn + _stepCount + 0.3f);
            _stepWidth = Mathf.Clamp(fit, 22f, MaxStepWidth);
            _scrollX = 0f;
            _fitPending = false;
            _userZoomed = false;
            Redraw();
        }

        /// <summary>Pins the cursor on <paramref name="row"/> (-1 clears it) and scrolls it into view.</summary>
        public void SetCursor(int row)
        {
            row = row < 0 ? -1 : Mathf.Min(row, _stepCount);
            if (row == _cursorRow)
            {
                return;
            }

            _cursorRow = row;
            if (row >= 0)
            {
                float start = RowStart(row) * _stepWidth;
                float end = RowEnd(row) * _stepWidth;
                if (start < _scrollX)
                {
                    _scrollX = start - _stepWidth * 0.5f;
                }
                else if (end > _scrollX + ViewWidth)
                {
                    _scrollX = end - ViewWidth + _stepWidth * 0.5f;
                }
            }

            Redraw();
            CursorChanged?.Invoke();
        }

        /// <summary>Moves the pinned cursor by <paramref name="delta"/> rows (starts at power-on when none).</summary>
        public void MoveCursor(int delta)
        {
            int start = _cursorRow < 0 ? (delta > 0 ? -1 : _stepCount + 1) : _cursorRow;
            SetCursor(Mathf.Clamp(start + delta, 0, _stepCount));
        }

        // ------------------------------------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------------------------------------

        private void OnRectTransformDimensionsChange()
        {
            if (_graphic != null && isActiveAndEnabled)
            {
                Redraw();
            }
        }

        private void OnEnable()
        {
            if (_graphic != null)
            {
                Redraw();
            }
        }

        public void Redraw()
        {
            if (_graphic == null || Width <= NameWidth + 20f)
            {
                return;
            }

            if (_fitPending && _stepCount > 0)
            {
                _fitPending = false;
                float fit = ViewWidth / Mathf.Max(1f, LeadIn + _stepCount + 0.3f);
                _stepWidth = Mathf.Clamp(fit, 22f, MaxStepWidth);
            }

            ClampScroll();
            _graphic.Clear();
            DrawBackground();
            DrawLanes();
            DrawRuler();
            _graphic.Commit();
            PlaceLabels();
            SyncScrollbars();
        }

        private void Rect(float x0, float y0, float x1, float y1, Color color)
        {
            // Clip to the plot (right of the names column, below the ruler).
            x0 = Mathf.Max(x0, PlotLeft);
            x1 = Mathf.Min(x1, PlotRight);
            y0 = Mathf.Max(y0, PlotTop);
            y1 = Mathf.Min(y1, PlotBottom);
            _graphic.AddRect(x0, y0, x1, y1, color);
        }

        private void FirstAndLast(out int first, out int last)
        {
            float u0 = _scrollX / _stepWidth;
            float u1 = (_scrollX + ViewWidth) / _stepWidth;
            first = u0 < LeadIn ? 0 : Mathf.Max(1, Mathf.FloorToInt(u0 - LeadIn) + 1);
            last = Mathf.Min(_stepCount, Mathf.CeilToInt(u1 - LeadIn) + 1);
        }

        private void DrawBackground()
        {
            Color border = _theme.PanelBorder;
            // Names column and ruler band.
            _graphic.AddRect(0f, 0f, NameWidth, Height, new Color(1f, 1f, 1f, 0.018f));
            _graphic.AddRect(0f, 0f, Width, RulerHeight, new Color(1f, 1f, 1f, 0.03f));
            _graphic.AddRect(0f, RulerHeight - 1f, Width, RulerHeight, border);
            _graphic.AddRect(NameWidth - 1f, 0f, NameWidth, Height, border);

            if (_stepCount == 0 && _table == null)
            {
                return;
            }

            FirstAndLast(out int first, out int last);
            float bottom = Mathf.Min(PlotBottom, PlotTop + _contentHeight - _scrollY);
            for (int row = first; row <= last; row++)
            {
                float x0 = X(RowStart(row));
                float x1 = X(RowEnd(row));
                if (row == 0)
                {
                    Rect(x0, PlotTop, x1, bottom, new Color(1f, 1f, 1f, 0.035f));
                }
                else if ((row & 1) == 0)
                {
                    Rect(x0, PlotTop, x1, bottom, new Color(1f, 1f, 1f, 0.014f));
                }

                // Step boundary hairline.
                Rect(x1 - 0.5f, PlotTop, x1 + 0.5f, bottom, new Color(border.r, border.g, border.b, 0.55f));
            }

            if (_hoverRow >= 0 && _hoverRow != _cursorRow)
            {
                Rect(X(RowStart(_hoverRow)), PlotTop, X(RowEnd(_hoverRow)), bottom, new Color(1f, 1f, 1f, 0.045f));
            }

            if (_cursorRow >= 0)
            {
                Color accent = _theme.Accent;
                float x0 = X(RowStart(_cursorRow));
                float x1 = X(RowEnd(_cursorRow));
                Rect(x0, PlotTop, x1, bottom, new Color(accent.r, accent.g, accent.b, 0.09f));
                Rect(x0, PlotTop, x0 + 1.5f, bottom, new Color(accent.r, accent.g, accent.b, 0.8f));
                Rect(x1 - 1.5f, PlotTop, x1, bottom, new Color(accent.r, accent.g, accent.b, 0.8f));
            }
        }

        private void DrawLanes()
        {
            if (_lanes.Count == 0)
            {
                return;
            }

            FirstAndLast(out int first, out int last);
            Color border = _theme.PanelBorder;
            for (int l = 0; l < _lanes.Count; l++)
            {
                Lane lane = _lanes[l];
                float top = PlotTop + lane.Top - _scrollY;
                float bottom = top + lane.Height;
                if (bottom < PlotTop || top > PlotBottom)
                {
                    continue;
                }

                if (lane.Kind == LaneKind.Group)
                {
                    _graphic.AddRect(0f, Mathf.Max(top, PlotTop), Width, Mathf.Min(bottom, PlotBottom),
                        new Color(1f, 1f, 1f, 0.025f));
                    continue;
                }

                // Lane separator.
                Rect(PlotLeft, bottom - 1f, PlotRight, bottom, new Color(border.r, border.g, border.b, 0.35f));
                if (l == _hoverLane && lane.Kind == LaneKind.Input)
                {
                    _graphic.AddRect(0f, Mathf.Max(top, PlotTop), NameWidth - 1f, Mathf.Min(bottom, PlotBottom),
                        new Color(1f, 1f, 1f, 0.03f));
                }

                switch (lane.Kind)
                {
                    case LaneKind.Input:
                        DrawInputLane(lane.Index, top, first, last, l == _hoverLane);
                        break;
                    case LaneKind.Clock:
                        DrawSampledLane(lane, top, first, last, _clockColor);
                        break;
                    case LaneKind.Output:
                        DrawSampledLane(lane, top, first, last, _outputColor);
                        break;
                }
            }
        }

        private Signal InputValue(int input, int row)
        {
            if (row == 0)
            {
                return _table != null ? _table.GetInput(0, input) : Signal.Low;
            }

            if (_steps != null && row - 1 < _steps.Count)
            {
                return _steps[row - 1][input];
            }

            return _table != null && row < _table.RowCount ? _table.GetInput(row, input) : Signal.Low;
        }

        private void DrawInputLane(int input, float top, int first, int last, bool hovered)
        {
            int length = input < _patternLengths.Length ? _patternLengths[input] : int.MaxValue;
            Signal previous = first > 0 ? InputValue(input, first - 1) : Signal.Low;
            for (int row = first; row <= last; row++)
            {
                Signal value = InputValue(input, row);
                float x0 = X(RowStart(row));
                float x1 = X(RowEnd(row));
                bool held = row > 0 && row > length;
                if (hovered && row == _hoverRow && row > 0)
                {
                    // Edit affordance: the cell under the pointer.
                    Rect(x0 + 1f, top + 2f, x1 - 1f, top + LaneHeight - 3f, new Color(1f, 1f, 1f, 0.07f));
                }

                DrawWave(x0, x1, top, value, row == first ? value : previous, _inputColor, held ? 0.45f : 1f);
                previous = value;
            }
        }

        private void DrawSampledLane(Lane lane, float top, int first, int last, Color color)
        {
            if (_table == null)
            {
                return;
            }

            bool output = lane.Kind == LaneKind.Output;
            Signal previous = Sample(lane, Mathf.Max(0, first - 1), SequencePhase.AfterCycle);
            for (int row = first; row <= last; row++)
            {
                float start = RowStart(row);
                float end = RowEnd(row);
                if (output && _table.OutputChanged(row, lane.Index))
                {
                    Color accent = _theme.Accent;
                    Rect(X(start), top + 1f, X(end), top + LaneHeight - 2f, new Color(accent.r, accent.g, accent.b, 0.07f));
                }

                if (row == 0 || !_clocked)
                {
                    Signal value = Sample(lane, row, SequencePhase.AfterCycle);
                    DrawPhase(row, SequencePhase.AfterCycle, X(start), X(end), top, value,
                        row == first ? value : previous, color);
                    previous = value;
                    continue;
                }

                // ¼ after inputs · ½ clock high · ¼ after the falling edge.
                float q1 = start + 0.25f;
                float q3 = start + 0.75f;
                Signal a = Sample(lane, row, SequencePhase.AfterInputs);
                Signal b = Sample(lane, row, SequencePhase.ClockHigh);
                Signal c = Sample(lane, row, SequencePhase.AfterCycle);
                DrawPhase(row, SequencePhase.AfterInputs, X(start), X(q1), top, a, row == first ? a : previous, color);
                DrawPhase(row, SequencePhase.ClockHigh, X(q1), X(q3), top, b, a, color);
                DrawPhase(row, SequencePhase.AfterCycle, X(q3), X(end), top, c, b, color);
                previous = c;
            }
        }

        private void DrawPhase(int row, SequencePhase phase, float x0, float x1, float top, Signal value,
            Signal previous, Color color)
        {
            if (!_table.IsStable(row, phase))
            {
                Color warning = _theme.Warning;
                Rect(x0, top + 2f, x1, top + LaneHeight - 3f, new Color(warning.r, warning.g, warning.b, 0.12f));
            }

            DrawWave(x0, x1, top, value, previous, color, 1f);
        }

        private Signal Sample(Lane lane, int row, SequencePhase phase)
        {
            return lane.Kind == LaneKind.Clock
                ? _table.GetClock(row, lane.Index, phase)
                : _table.GetOutput(row, lane.Index, phase);
        }

        /// <summary>One segment of a square wave, with the vertical transition from <paramref name="previous"/>.</summary>
        private void DrawWave(float x0, float x1, float top, Signal value, Signal previous, Color color, float alpha)
        {
            float high = top + WavePad;
            float low = top + LaneHeight - WavePad;
            float mid = (high + low) * 0.5f;
            Color line = new Color(color.r, color.g, color.b, color.a * alpha);
            Color dim = new Color(color.r, color.g, color.b, 0.5f * alpha);
            switch (value)
            {
                case Signal.High:
                    Rect(x0, high, x1, low, new Color(color.r, color.g, color.b, 0.16f * alpha));
                    Rect(x0, high - 1f, x1, high + 1f, line);
                    break;
                case Signal.Low:
                    Rect(x0, low - 1f, x1, low + 1f, dim);
                    break;
                case Signal.HighImpedance:
                    Rect(x0, mid - 1f, x1, mid + 1f, new Color(_floatingColor.r, _floatingColor.g, _floatingColor.b, alpha));
                    break;
                default:
                    Color unknown = _unknownColor;
                    Rect(x0, high, x1, low, new Color(unknown.r, unknown.g, unknown.b, 0.22f * alpha));
                    Rect(x0, high - 1f, x1, high + 0.5f, new Color(unknown.r, unknown.g, unknown.b, 0.9f * alpha));
                    Rect(x0, low - 0.5f, x1, low + 1f, new Color(unknown.r, unknown.g, unknown.b, 0.9f * alpha));
                    break;
            }

            if (previous != value && x0 > PlotLeft)
            {
                Color edge = value == Signal.High || previous == Signal.High ? line : dim;
                Rect(x0 - 1f, high - 1f, x0 + 1f, low + 1f, edge);
            }
        }

        private void DrawRuler()
        {
            if (_stepCount == 0 && _table == null)
            {
                return;
            }

            FirstAndLast(out int first, out int last);
            Color tick = _theme.PanelBorder;
            for (int row = Mathf.Max(1, first); row <= last; row++)
            {
                float x = X(RowStart(row));
                if (x >= PlotLeft && x <= PlotRight)
                {
                    _graphic.AddRect(x - 0.5f, RulerHeight - 6f, x + 0.5f, RulerHeight - 1f, tick);
                }
            }
        }

        private int LabelInterval()
        {
            float minimum = 30f;
            int[] steps = { 1, 2, 5, 10, 20, 50, 100, 200, 500 };
            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i] * _stepWidth >= minimum)
                {
                    return steps[i];
                }
            }

            return 1000;
        }

        private void PlaceLabels()
        {
            // Ruler numbers (positions are relative to the ruler mask, which starts at the plot's left edge).
            FirstAndLast(out int first, out int last);
            int interval = LabelInterval();
            int used = 0;
            bool hasData = _stepCount > 0 || _table != null;
            if (hasData && first == 0)
            {
                Place(_rulerLabels[used++], "PO", (RowStart(0) + RowEnd(0)) * 0.5f);
            }

            int start = Mathf.Max(1, first);
            start = ((start - 1 + interval - 1) / interval) * interval;
            start = Mathf.Max(interval == 1 ? 1 : interval, start);
            for (int row = start; hasData && row <= last && used < _rulerLabels.Count; row += interval)
            {
                Place(_rulerLabels[used++], row.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (RowStart(row) + RowEnd(row)) * 0.5f);
            }

            for (int i = used; i < _rulerLabels.Count; i++)
            {
                SetActive(_rulerLabels[i], false);
            }

            // Lane names and group headers (relative to the names mask, which starts below the ruler).
            int names = 0;
            int groups = 0;
            for (int l = 0; l < _lanes.Count; l++)
            {
                Lane lane = _lanes[l];
                float y = lane.Top - _scrollY;
                if (y + lane.Height < 0f || y > ViewHeight)
                {
                    continue;
                }

                if (lane.Kind == LaneKind.Group)
                {
                    if (groups < _groupLabels.Count)
                    {
                        TextMeshProUGUI label = _groupLabels[groups++];
                        label.text = GroupTitle(lane.Index);
                        PlaceName(label, y, lane.Height, 12f);
                    }

                    continue;
                }

                if (names >= _nameLabels.Count)
                {
                    continue;
                }

                TextMeshProUGUI name = _nameLabels[names++];
                name.text = LaneName(lane);
                name.color = lane.Kind == LaneKind.Output ? _outputColor
                    : lane.Kind == LaneKind.Clock ? _clockColor : _theme.TextPrimary;
                PlaceName(name, y, lane.Height, 14f);
            }

            for (int i = names; i < _nameLabels.Count; i++)
            {
                SetActive(_nameLabels[i], false);
            }

            for (int i = groups; i < _groupLabels.Count; i++)
            {
                SetActive(_groupLabels[i], false);
            }
        }

        private string GroupTitle(int group)
        {
            switch (group)
            {
                case 0:
                    return "INPUTS  <color=#" + ColorUtility.ToHtmlStringRGB(_theme.Accent) + ">" + Icons.Pencil + "</color>";
                case 1:
                    return "CLOCK";
                default:
                    return "OUTPUTS";
            }
        }

        private string LaneName(Lane lane)
        {
            switch (lane.Kind)
            {
                case LaneKind.Input:
                    return lane.Index < _inputNames.Count ? _inputNames[lane.Index] : "?";
                case LaneKind.Clock:
                    return _table.ClockName(lane.Index);
                default:
                    return _table.Outputs[lane.Index].Name;
            }
        }

        private void Place(TextMeshProUGUI label, string text, float units)
        {
            label.text = text;
            label.rectTransform.anchoredPosition = new Vector2(units * _stepWidth - _scrollX, -1f);
            SetActive(label, true);
        }

        private static void PlaceName(TextMeshProUGUI label, float y, float height, float left)
        {
            RectTransform rect = label.rectTransform;
            rect.offsetMin = new Vector2(left, -y - height);
            rect.offsetMax = new Vector2(-8f, -y);
            SetActive(label, true);
        }

        private static void SetActive(Component component, bool active)
        {
            if (component.gameObject.activeSelf != active)
            {
                component.gameObject.SetActive(active);
            }
        }

        private void SyncScrollbars()
        {
            _syncingScrollbars = true;
            float contentWidth = ContentWidth;
            bool horizontal = contentWidth > ViewWidth + 0.5f;
            SetActive(_hScroll, horizontal);
            if (horizontal)
            {
                _hScroll.size = Mathf.Clamp01(ViewWidth / contentWidth);
                _hScroll.SetValueWithoutNotify(_scrollX / Mathf.Max(1f, contentWidth - ViewWidth));
            }

            bool vertical = _contentHeight > ViewHeight + 0.5f;
            SetActive(_vScroll, vertical);
            if (vertical)
            {
                _vScroll.size = Mathf.Clamp01(ViewHeight / _contentHeight);
                _vScroll.SetValueWithoutNotify(_scrollY / Mathf.Max(1f, _contentHeight - ViewHeight));
            }

            _syncingScrollbars = false;
        }

        private void OnHorizontalScrollbar(float value)
        {
            if (_syncingScrollbars)
            {
                return;
            }

            _scrollX = value * Mathf.Max(0f, ContentWidth - ViewWidth);
            Redraw();
        }

        private void OnVerticalScrollbar(float value)
        {
            if (_syncingScrollbars)
            {
                return;
            }

            _scrollY = value * Mathf.Max(0f, _contentHeight - ViewHeight);
            Redraw();
        }

        // ------------------------------------------------------------------------------------------------
        // Pointer input
        // ------------------------------------------------------------------------------------------------

        private Vector2 Local(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, eventData.position,
                eventData.pressEventCamera != null ? eventData.pressEventCamera : eventData.enterEventCamera,
                out Vector2 local);
            Rect bounds = _rect.rect;
            return new Vector2(local.x - bounds.xMin, bounds.yMax - local.y);
        }

        /// <summary>Hover from a frame-local point (tests drive this directly).</summary>
        public void HoverAt(Vector2 point)
        {
            int row = RowAt(point.x);
            int lane = LaneAt(point.y);
            if (row < 0)
            {
                lane = -1;
            }

            if (row == _hoverRow && lane == _hoverLane)
            {
                return;
            }

            _hoverRow = row;
            _hoverLane = lane;
            Redraw();
            CursorChanged?.Invoke();
        }

        public void ClearHover()
        {
            if (_hoverRow < 0 && _hoverLane < 0)
            {
                return;
            }

            _hoverRow = -1;
            _hoverLane = -1;
            Redraw();
            CursorChanged?.Invoke();
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (!_painting)
            {
                HoverAt(Local(eventData));
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_painting)
            {
                ClearHover();
            }
        }

        /// <summary>Press at a frame-local point: edits an input cell or pins the step (tests drive this directly).</summary>
        public void PressAt(Vector2 point)
        {
            int row = RowAt(point.x);
            int laneIndex = LaneAt(point.y);
            if (row < 0)
            {
                return;
            }

            if (laneIndex >= 0 && _lanes[laneIndex].Kind == LaneKind.Input && row > 0)
            {
                int input = _lanes[laneIndex].Index;
                _painting = true;
                _paintInput = input;
                _paintLastStep = row;
                _paintValue = InputValue(input, row) == Signal.High ? Signal.Low : Signal.High;
                CellsEdited?.Invoke(input, row, row, _paintValue);
                return;
            }

            SetCursor(row == _cursorRow ? -1 : row);
        }

        /// <summary>Continues a paint stroke at a frame-local point.</summary>
        public void DragTo(Vector2 point)
        {
            if (!_painting)
            {
                return;
            }

            float units = (point.x - PlotLeft + _scrollX) / _stepWidth;
            int row = Mathf.Clamp(Mathf.FloorToInt(units - LeadIn) + 1, 1, Mathf.Max(1, _stepCount));
            if (row == _paintLastStep)
            {
                return;
            }

            int from = row > _paintLastStep ? _paintLastStep + 1 : row;
            int to = row > _paintLastStep ? row : _paintLastStep - 1;
            _paintLastStep = row;
            CellsEdited?.Invoke(_paintInput, from, to, _paintValue);
        }

        public void EndPaint()
        {
            _painting = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                PressAt(Local(eventData));
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            EndPaint();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
        }

        public void OnDrag(PointerEventData eventData)
        {
            DragTo(Local(eventData));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            EndPaint();
        }

        public void OnScroll(PointerEventData eventData)
        {
            Vector2 delta = eventData.scrollDelta;
            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            bool control = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            bool shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            float amount = Mathf.Abs(delta.y) > Mathf.Abs(delta.x) ? delta.y : -delta.x;
            if (Mathf.Approximately(amount, 0f))
            {
                return;
            }

            float notches = Mathf.Sign(amount) * Mathf.Max(1f, Mathf.Abs(amount) / 120f);
            if (control)
            {
                SetStepWidth(_stepWidth * Mathf.Pow(1.15f, notches), Local(eventData).x);
                return;
            }

            bool vertical = !shift && _contentHeight > ViewHeight + 0.5f;
            if (vertical)
            {
                _scrollY -= notches * LaneHeight * 1.5f;
            }
            else
            {
                _scrollX -= notches * Mathf.Max(_stepWidth * 2f, 60f);
            }

            Redraw();
        }
    }
}
