using System;
using System.Collections.Generic;
using System.Text;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.TruthTable.Generation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs.Sequence
{
    /// <summary>
    /// Table view of a sequence analysis: one row per step (row 0 is the power-on state) showing the applied
    /// inputs and the outputs at the end of the step. Outputs that changed since the previous row are highlighted;
    /// rows where the circuit oscillated are marked. Rows are virtualised: a small pool of striped row objects
    /// is repositioned as the list scrolls.
    /// </summary>
    public sealed class SequenceTableView : MonoBehaviour
    {
        public const float RowHeight = 24f;

        private const float StepColumnWidth = 92f;
        private const float MinColumnWidth = 34f;
        private const float GroupGap = 20f;
        private const float HeaderHeight = 30f;
        private const float Inset = 12f;
        private const float CellFontSize = 14f;
        private const int PoolSize = 24;

        private readonly StringBuilder _builder = new StringBuilder(1024);
        private readonly List<TextMeshProUGUI> _rowTexts = new List<TextMeshProUGUI>(PoolSize);
        private readonly List<Image> _rowStripes = new List<Image>(PoolSize);
        private readonly List<int> _rowIndices = new List<int>(PoolSize);

        private UiFactory _ui;
        private TextMeshProUGUI _header;
        private Image _divider;
        private RectTransform _content;
        private ScrollRect _scroll;
        private float[] _centers = Array.Empty<float>();
        private float _digitHalf;
        private SequenceTable _table;
        private string _hexHigh;
        private string _hexLow;
        private string _hexUnknown;
        private string _hexMark;
        private string _hexAccent;

        public string HeaderText => _header.text;

        public static SequenceTableView Create(Transform parent, UiFactory ui)
        {
            Image frame = ui.Panel("Table", parent, ui.Theme.WorkspaceBackground, rounded: true, radius: 10f);
            var layout = frame.gameObject.AddComponent<LayoutElement>();
            layout.flexibleHeight = 1f;
            layout.minHeight = 200f;
            var view = frame.gameObject.AddComponent<SequenceTableView>();
            view._ui = ui;
            view._hexHigh = "#" + ColorUtility.ToHtmlStringRGB(ui.Theme.SignalColor(Signal.High));
            view._hexLow = "#" + ColorUtility.ToHtmlStringRGB(ui.Theme.TextSecondary);
            view._hexUnknown = "#" + ColorUtility.ToHtmlStringRGB(ui.Theme.Warning);
            view._hexAccent = "#" + ColorUtility.ToHtmlStringRGB(ui.Theme.Accent);
            Color accent = ui.Theme.Accent;
            view._hexMark = "#" + ColorUtility.ToHtmlStringRGBA(new Color(accent.r, accent.g, accent.b, 0.22f));
            view.Build(frame.rectTransform);
            return view;
        }

        private void Build(RectTransform frame)
        {
            Image band = _ui.Panel("Header Band", frame, new Color(1f, 1f, 1f, 0.03f));
            band.raycastTarget = false;
            RectTransform bandRect = band.rectTransform;
            bandRect.anchorMin = new Vector2(0f, 1f);
            bandRect.anchorMax = new Vector2(1f, 1f);
            bandRect.pivot = new Vector2(0.5f, 1f);
            bandRect.sizeDelta = new Vector2(0f, HeaderHeight);
            Image line = _ui.Panel("Line", band.transform, _ui.Theme.PanelBorder);
            line.raycastTarget = false;
            line.rectTransform.anchorMin = new Vector2(0f, 0f);
            line.rectTransform.anchorMax = new Vector2(1f, 0f);
            line.rectTransform.pivot = new Vector2(0.5f, 0f);
            line.rectTransform.sizeDelta = new Vector2(0f, 1f);

            _header = _ui.Text("Header", frame, string.Empty, CellFontSize, _ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft);
            _header.richText = true;
            _header.enableWordWrapping = false;
            _header.overflowMode = TextOverflowModes.Overflow;
            RectTransform header = _header.rectTransform;
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0f, 1f);
            header.offsetMin = new Vector2(Inset, -HeaderHeight);
            header.offsetMax = new Vector2(-Inset, 0f);

            RectTransform body = UiFactory.CreateRect("Body", frame);
            UiFactory.Stretch(body, 0f, 0f, HeaderHeight, 0f);
            _scroll = body.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = RowHeight * 1.5f;
            _scroll.inertia = false;
            RectTransform viewport = UiFactory.CreateRect("Viewport", body);
            UiFactory.Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            _content = UiFactory.CreateRect("Rows", viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0f, 1f);
            _content.sizeDelta = Vector2.zero;
            _scroll.viewport = viewport;
            _scroll.content = _content;
            _scroll.onValueChanged.AddListener(_ => RefreshRows());

            // Vertical divider between the input and output groups (positioned in Bind).
            _divider = _ui.Panel("Divider", frame, _ui.Theme.PanelBorder);
            _divider.raycastTarget = false;
            RectTransform divider = _divider.rectTransform;
            divider.anchorMin = new Vector2(0f, 0f);
            divider.anchorMax = new Vector2(0f, 1f);
            divider.pivot = new Vector2(0.5f, 0.5f);
            divider.sizeDelta = new Vector2(1f, -12f);
            _divider.gameObject.SetActive(false);

            for (int i = 0; i < PoolSize; i++)
            {
                Image stripe = _ui.Panel("Row " + i, _content, Color.clear);
                stripe.raycastTarget = false;
                RectTransform rect = stripe.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(0f, RowHeight);
                TextMeshProUGUI text = _ui.Text("Text", rect, string.Empty, CellFontSize, _ui.Theme.TextPrimary,
                    TextAlignmentOptions.MidlineLeft);
                text.richText = true;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
                UiFactory.Stretch(text.rectTransform, Inset, Inset);
                stripe.gameObject.SetActive(false);
                _rowStripes.Add(stripe);
                _rowTexts.Add(text);
                _rowIndices.Add(-1);
            }
        }

        /// <summary>Shows <paramref name="table"/> (null clears the view).</summary>
        public void Bind(SequenceTable table)
        {
            _table = table;
            if (table == null)
            {
                _header.text = string.Empty;
                _divider.gameObject.SetActive(false);
                SetRowCount(0);
                return;
            }

            int inputs = table.Inputs.Count;
            int outputs = table.Outputs.Count;
            if (_centers.Length != inputs + outputs)
            {
                _centers = new float[inputs + outputs];
            }

            float x = StepColumnWidth + 8f;
            float dividerX = -1f;
            _builder.Clear();
            _builder.Append("<b>").Append(table.StepHeader);
            for (int c = 0; c < _centers.Length; c++)
            {
                if (c == inputs && inputs > 0)
                {
                    dividerX = x + GroupGap * 0.5f;
                    x += GroupGap;
                }

                string name = c < inputs ? table.Inputs[c].Name : table.Outputs[c - inputs].Name;
                float nameWidth = _header.GetPreferredValues(name).x;
                float width = Mathf.Max(MinColumnWidth, nameWidth + 16f);
                _centers[c] = x + width * 0.5f;
                _builder.Append("<pos=").Append(Mathf.RoundToInt(_centers[c] - nameWidth * 0.5f)).Append('>');
                if (c >= inputs)
                {
                    _builder.Append("<color=").Append(_hexAccent).Append('>').Append(name).Append("</color>");
                }
                else
                {
                    _builder.Append(name);
                }

                x += width;
            }

            _builder.Append("</b>");
            _header.text = _builder.ToString();
            _digitHalf = _header.GetPreferredValues("0").x * 0.5f;
            _divider.gameObject.SetActive(dividerX > 0f);
            if (dividerX > 0f)
            {
                _divider.rectTransform.anchoredPosition = new Vector2(Inset + dividerX, 0f);
            }

            SetRowCount(table.RowCount);
        }

        /// <summary>Plain text of a displayed row, or null when the row is not in view.</summary>
        public string GetVisibleRowText(int row)
        {
            for (int i = 0; i < _rowTexts.Count; i++)
            {
                if (_rowIndices[i] == row)
                {
                    _rowTexts[i].ForceMeshUpdate(true);
                    return _rowTexts[i].GetParsedText();
                }
            }

            return null;
        }

        /// <summary>Header plus the rows in view, as plain text.</summary>
        public string VisibleText
        {
            get
            {
                var text = new StringBuilder(_header.text);
                for (int i = 0; i < _rowTexts.Count; i++)
                {
                    if (_rowIndices[i] >= 0)
                    {
                        _rowTexts[i].ForceMeshUpdate(true);
                        text.Append('\n').Append(_rowTexts[i].GetParsedText());
                    }
                }

                return text.ToString();
            }
        }

        /// <summary>Scrolls so that <paramref name="row"/> is the first visible row (as far as the table allows).</summary>
        public void ScrollToRow(int row)
        {
            Vector2 position = _content.anchoredPosition;
            position.y = Mathf.Max(0f, row * RowHeight);
            _content.anchoredPosition = position;
            RefreshRows();
        }

        private void SetRowCount(int rows)
        {
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, rows * RowHeight + 8f);
            _content.anchoredPosition = Vector2.zero;
            for (int i = 0; i < _rowIndices.Count; i++)
            {
                _rowIndices[i] = -1; // contents changed: reformat every row in view
            }

            RefreshRows();
        }

        private void RefreshRows()
        {
            int total = _table?.RowCount ?? 0;
            int first = Mathf.Max(0, Mathf.FloorToInt(_content.anchoredPosition.y / RowHeight));
            for (int i = 0; i < _rowTexts.Count; i++)
            {
                Image stripe = _rowStripes[i];
                int row = first + i;
                bool visible = row < total;
                if (stripe.gameObject.activeSelf != visible)
                {
                    stripe.gameObject.SetActive(visible);
                }

                if (!visible)
                {
                    _rowIndices[i] = -1;
                    continue;
                }

                if (_rowIndices[i] == row)
                {
                    continue; // already showing this row
                }

                _rowIndices[i] = row;
                stripe.rectTransform.anchoredPosition = new Vector2(0f, -row * RowHeight);
                stripe.color = row == 0 ? new Color(1f, 1f, 1f, 0.04f)
                    : (row & 1) == 0 ? new Color(1f, 1f, 1f, 0.02f) : Color.clear;
                FormatRow(row);
                _rowTexts[i].SetText(_builder);
            }
        }

        private void FormatRow(int row)
        {
            SequenceTable table = _table;
            int inputs = table.Inputs.Count;
            int outputs = table.Outputs.Count;
            _builder.Clear();
            if (row == 0)
            {
                _builder.Append("<color=").Append(_hexLow).Append('>').Append(table.RowLabel(0)).Append("</color>");
            }
            else
            {
                _builder.Append(table.RowLabel(row));
            }

            if (!table.IsStable(row))
            {
                _builder.Append(" <color=").Append(_hexUnknown).Append(">").Append(Icons.Warning).Append("</color>");
            }

            for (int c = 0; c < inputs; c++)
            {
                AppendValue(_centers[c], table.GetInput(row, c), marked: false);
            }

            for (int o = 0; o < outputs; o++)
            {
                AppendValue(_centers[inputs + o], table.GetOutput(row, o), table.OutputChanged(row, o));
            }
        }

        private void AppendValue(float center, Signal value, bool marked)
        {
            string color = value == Signal.High ? _hexHigh : value == Signal.Low ? _hexLow : _hexUnknown;
            _builder.Append("<pos=").Append(Mathf.RoundToInt(center - _digitHalf)).Append('>');
            if (marked)
            {
                _builder.Append("<mark=").Append(_hexMark).Append(" padding=\"6,6,2,2\">");
            }

            if (value == Signal.High)
            {
                _builder.Append("<b>");
            }

            _builder.Append("<color=").Append(color).Append('>').Append(TruthTableData.Symbol(value)).Append("</color>");
            if (value == Signal.High)
            {
                _builder.Append("</b>");
            }

            if (marked)
            {
                // An unmarked space ends the highlight; otherwise TMP joins it with the next marked cell.
                _builder.Append("</mark> ");
            }
        }
    }
}
