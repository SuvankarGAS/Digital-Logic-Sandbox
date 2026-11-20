using System;
using System.Collections.Generic;
using System.Text;
using DigitalLogicSandbox.GateLibrary.Programs;
using DigitalLogicSandbox.Rendering;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Dialogs
{
    /// <summary>
    /// Turns a multi-line <see cref="TMP_InputField"/> into a small code editor. It adds a line-number gutter, with a
    /// '!' marker on lines that have errors, and syntax colouring. The colouring is a rich-text copy of the source
    /// drawn behind the field's own text, which is made invisible (the caret and selection stay visible). Both
    /// follow the field's scrolling in <see cref="LateUpdate"/>. The field still owns editing; this only draws.
    /// </summary>
    public sealed class CodeFieldDecorator : MonoBehaviour
    {
        public const float GutterWidth = 36f;

        private readonly StringBuilder _builder = new StringBuilder(512);
        private readonly List<SyntaxToken> _tokens = new List<SyntaxToken>(64);
        private readonly HashSet<int> _errorLines = new HashSet<int>();
        private readonly string[] _colors = new string[ProgramSyntax.KindCount];

        private TMP_InputField _field;
        private TextMeshProUGUI _highlight;
        private TextMeshProUGUI _lineNumbers;
        private RectTransform _source;
        private Action<string, List<SyntaxToken>> _tokenize;
        private string _errorColor;
        private int _shownLines = -1;
        private bool _numbersDirty = true;

        /// <summary>Rich text drawn behind the field (tags plus the escaped source).</summary>
        public string HighlightText => _highlight.text;

        /// <summary>The gutter's text: one line number per source line, '!' on error lines.</summary>
        public string LineNumbersText => _lineNumbers.text;

        /// <summary>Adds the gutter and colouring to <paramref name="field"/> (built by <see cref="UiFactory.InputField(string, Transform, string)"/>).</summary>
        public static CodeFieldDecorator Attach(TMP_InputField field, UiFactory ui,
            Action<string, List<SyntaxToken>> tokenize)
        {
            if (field == null || ui == null || tokenize == null)
            {
                throw new ArgumentNullException(field == null ? nameof(field) : ui == null ? nameof(ui) : nameof(tokenize));
            }

            var decorator = field.gameObject.AddComponent<CodeFieldDecorator>();
            decorator.Build(field, ui, tokenize);
            return decorator;
        }

        /// <summary>Replaces the tokenizer (e.g. when the instruction set changes) and recolours.</summary>
        public void SetTokenizer(Action<string, List<SyntaxToken>> tokenize)
        {
            _tokenize = tokenize ?? throw new ArgumentNullException(nameof(tokenize));
            Refresh();
        }

        /// <summary>Marks the 1-based <paramref name="lines"/> that have errors (shown as '!' in the gutter).</summary>
        public void SetErrors(IReadOnlyList<AssemblyError> errors)
        {
            _errorLines.Clear();
            if (errors != null)
            {
                for (int i = 0; i < errors.Count; i++)
                {
                    if (errors[i].Line > 0)
                    {
                        _errorLines.Add(errors[i].Line);
                    }
                }
            }

            _numbersDirty = true;
            RebuildLineNumbers();
        }

        /// <summary>Recolours the current text and updates the gutter.</summary>
        public void Refresh()
        {
            string text = _field.text ?? string.Empty;
            _tokenize(text, _tokens);
            _builder.Clear();
            ProgramSyntax.AppendRichText(_builder, text, _tokens, _colors);
            _highlight.text = _builder.ToString();
            RebuildLineNumbers();
            SyncPosition();
        }

        private void Build(TMP_InputField field, UiFactory ui, Action<string, List<SyntaxToken>> tokenize)
        {
            _field = field;
            _tokenize = tokenize;
            VisualTheme theme = ui.Theme;
            _colors[(int)SyntaxKind.Address] = Hex(theme.TextMuted);
            _colors[(int)SyntaxKind.Mnemonic] = Hex(theme.Accent);
            _colors[(int)SyntaxKind.UnknownWord] = Hex(theme.Warning);
            _colors[(int)SyntaxKind.Number] = Hex(theme.TextPrimary);
            _colors[(int)SyntaxKind.Comment] = Hex(theme.TextMuted);
            _colors[(int)SyntaxKind.Keyword] = Hex(theme.Accent);
            _colors[(int)SyntaxKind.Operand] = Hex(theme.TextSecondary);
            _colors[(int)SyntaxKind.Marker] = Hex(theme.TextMuted);
            _errorColor = Hex(theme.Error);

            TextMeshProUGUI text = (TextMeshProUGUI)field.textComponent;
            _source = text.rectTransform;
            RectTransform viewport = field.textViewport;

            // Make room for the gutter on the left of the text area.
            viewport.offsetMin = new Vector2(viewport.offsetMin.x + GutterWidth, viewport.offsetMin.y);

            RectTransform gutter = UiFactory.CreateRect("Gutter", field.transform);
            gutter.anchorMin = new Vector2(0f, 0f);
            gutter.anchorMax = new Vector2(0f, 1f);
            gutter.pivot = new Vector2(0f, 0.5f);
            gutter.offsetMin = new Vector2(4f, viewport.offsetMin.y);
            gutter.offsetMax = new Vector2(GutterWidth + 2f, viewport.offsetMax.y);
            gutter.gameObject.AddComponent<RectMask2D>();
            _lineNumbers = ui.Text("Line Numbers", gutter, "1", text.fontSize, theme.TextMuted,
                TextAlignmentOptions.TopRight);
            UiFactory.Stretch(_lineNumbers.rectTransform, 0f, 6f, 0f, 0f);
            ConfigureLike(_lineNumbers, text);
            _lineNumbers.alignment = TextAlignmentOptions.TopRight;
            _lineNumbers.richText = true;
            _lineNumbers.raycastTarget = false;

            _highlight = ui.Text("Highlight", viewport, string.Empty, text.fontSize, theme.TextPrimary,
                text.alignment);
            ConfigureLike(_highlight, text);
            _highlight.richText = true;
            _highlight.raycastTarget = false;
            _highlight.rectTransform.SetSiblingIndex(_source.GetSiblingIndex());

            // The field's own glyphs become invisible; the caret has its own colour, the selection is drawn separately.
            Color hidden = text.color;
            hidden.a = 0f;
            text.color = hidden;
            field.customCaretColor = true;

            field.onValueChanged.AddListener(_ => Refresh());
            Refresh();
        }

        private static void ConfigureLike(TextMeshProUGUI target, TextMeshProUGUI source)
        {
            target.font = source.font;
            target.fontSize = source.fontSize;
            target.fontStyle = source.fontStyle;
            target.lineSpacing = source.lineSpacing;
            target.characterSpacing = source.characterSpacing;
            target.margin = source.margin;
            target.enableWordWrapping = source.enableWordWrapping;
            target.overflowMode = TextOverflowModes.Overflow;
            target.extraPadding = source.extraPadding;
        }

        private void RebuildLineNumbers()
        {
            int lines = ProgramSyntax.LineCount(_field.text);
            if (!_numbersDirty && lines == _shownLines)
            {
                return;
            }

            _builder.Clear();
            for (int i = 1; i <= lines; i++)
            {
                if (i > 1)
                {
                    _builder.Append('\n');
                }

                if (_errorLines.Contains(i))
                {
                    _builder.Append("<color=#").Append(_errorColor).Append(">!</color> ");
                }

                _builder.Append(i);
            }

            _lineNumbers.text = _builder.ToString();
            _shownLines = lines;
            _numbersDirty = false;
        }

        private void LateUpdate()
        {
            SyncPosition();
        }

        /// <summary>Copies the field's scroll offset (it moves its text rect) to the colouring and the gutter.</summary>
        private void SyncPosition()
        {
            RectTransform highlight = _highlight.rectTransform;
            highlight.anchorMin = _source.anchorMin;
            highlight.anchorMax = _source.anchorMax;
            highlight.pivot = _source.pivot;
            highlight.offsetMin = _source.offsetMin;
            highlight.offsetMax = _source.offsetMax;

            RectTransform numbers = _lineNumbers.rectTransform;
            Vector2 position = numbers.anchoredPosition;
            position.y = _source.anchoredPosition.y;
            numbers.anchoredPosition = position;
        }

        private static string Hex(Color color)
        {
            return ColorUtility.ToHtmlStringRGB(color);
        }
    }
}
