using System;
using System.Text;
using DG.Tweening;
using DigitalLogicSandbox.CircuitEditor.Placement;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Rendering;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Panels
{
    /// <summary>Visual style of a library entry's leading tile.</summary>
    public enum LibraryTileStyle
    {
        /// <summary>Neutral tile (built-in gates).</summary>
        Neutral = 0,

        /// <summary>Accent-tinted tile (the user's own gates).</summary>
        Accent = 1,

        /// <summary>Icon on a neutral tile (inputs, outputs, buses, displays).</summary>
        Icon = 2,
    }

    /// <summary>Everything needed to build a library row; shortcut rows (Pinned, Recent) reuse the source row's spec.</summary>
    public sealed class LibraryEntrySpec
    {
        public PlacementRequest Request;

        /// <summary>Stable identity for preferences (see <see cref="LibraryPanel.KeyOf"/>).</summary>
        public string Key;

        public string Title;

        /// <summary>Short secondary text: pin summary or a one-line description.</summary>
        public string Detail;

        /// <summary>Longer description shown in the preview card (may be empty).</summary>
        public string Description;

        /// <summary>Custom gate category (empty for built-in entries).</summary>
        public string Category;

        /// <summary>Tile text (a letter or the gate's icon text) or an icon glyph for <see cref="LibraryTileStyle.Icon"/>.</summary>
        public string Tile;

        public LibraryTileStyle Style;

        /// <summary>Show <see cref="Detail"/> as a pill on the row (custom gates).</summary>
        public bool ShowDetail;

        /// <summary>Extra words the search matches (category, description, pin names, synonyms).</summary>
        public string SearchExtra;

        public int Inputs;

        public int Outputs;
    }

    /// <summary>
    /// One entry in the gate library: a miniature gate tile (body with input and output pin dots), the name, an
    /// optional pin-count pill and a pin star. Clicking arms placement (the next click in the workspace places it);
    /// dragging places it where the button is released; double-clicking a custom gate opens it; right-click opens the
    /// entry's context menu. Hover, press, keyboard highlight and the armed state are animated by DOTween on unscaled
    /// time, so there is no per-row update loop.
    /// </summary>
    public sealed class LibraryItemView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public const float RowHeight = 34f;

        /// <summary>Left padding of an unindented row's tile.</summary>
        public const float RowPadding = 8f;

        /// <summary>Indent of one tree level (rows sit under their section title).</summary>
        public const float IndentStep = 14f;

        private const float TileWidth = 30f;
        private const float TileHeight = 24f;
        private const int MaxDots = 4;
        private const float Fade = 0.12f;

        private static readonly StringBuilder Builder = new StringBuilder(64);

        private WorkspaceController _workspace;
        private UiFactory _ui;
        private Image _background;
        private Image _bar;
        private Image _ring;
        private TextMeshProUGUI _name;
        private Button _star;
        private TextMeshProUGUI _starIcon;
        private bool _hovered;
        private bool _pressed;
        private bool _armed;
        private bool _highlighted;
        private bool _pinned;
        private string _highlightFilter = string.Empty;

        public PlacementRequest Request => Spec.Request;

        public LibraryEntrySpec Spec { get; private set; }

        /// <summary>Stable preferences key.</summary>
        public string Key => Spec.Key;

        /// <summary>Text used by the library search filter.</summary>
        public string SearchText { get; private set; }

        /// <summary>Display name of the entry.</summary>
        public string Title => Spec.Title;

        /// <summary>True for rows in Pinned / Recent (copies of a main entry, not part of <see cref="LibraryPanel.Items"/>).</summary>
        public bool IsShortcut { get; private set; }

        /// <summary>Sibling index at creation; the search restores it when cleared.</summary>
        public int HomeIndex { get; set; }

        /// <summary>Search rank of the current filter (0 best); -1 when it does not match.</summary>
        public int Rank { get; private set; }

        public bool IsArmed => _armed;

        public bool IsHighlighted => _highlighted;

        public bool IsPinned => _pinned;

        /// <summary>Text currently shown as the name (with search highlighting markup).</summary>
        public string DisplayedName => _name.text;

        /// <summary>Horizontal indent of the tile and name (tree level).</summary>
        public float Indent { get; private set; }

        /// <summary>Left edge of the tile in row coordinates (layout tests).</summary>
        public float TileLeft => RowPadding + Indent;

        /// <summary>Raised on right-click with the screen position, for the item context menu.</summary>
        public event Action<LibraryItemView, Vector2> ContextRequested;

        /// <summary>Raised when the pointer starts (true) or stops (false) resting on the row.</summary>
        public event Action<LibraryItemView, bool> HoverChanged;

        /// <summary>Raised by the star button.</summary>
        public event Action<LibraryItemView> PinToggleRequested;

        /// <summary>Raised when the row is clicked, dragged or opened (hides the preview card).</summary>
        public event Action<LibraryItemView> Activated;

        public static LibraryItemView Create(Transform parent, UiFactory ui, WorkspaceController workspace,
            LibraryEntrySpec spec, bool shortcut = false, float indent = 0f)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            VisualTheme theme = ui.Theme;
            Color hover = theme.ButtonHover;
            Image background = ui.Panel("Item " + spec.Title, parent, new Color(hover.r, hover.g, hover.b, 0f), rounded: true);
            var layout = background.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = RowHeight;
            layout.minHeight = RowHeight;

            var view = background.gameObject.AddComponent<LibraryItemView>();
            view._workspace = workspace;
            view._ui = ui;
            view._background = background;
            view.Spec = spec;
            view.IsShortcut = shortcut;
            view.Indent = Mathf.Max(0f, indent);
            view.SearchText = string.IsNullOrEmpty(spec.SearchExtra) ? spec.Title : spec.Title + " " + spec.SearchExtra;

            // Keyboard highlight ring and the armed accent bar.
            view._ring = ui.Panel("Ring", background.transform, WithAlpha(theme.Accent, 0f), rounded: true);
            view._ring.sprite = RenderAssets.RoundedOutline;
            view._ring.raycastTarget = false;
            UiFactory.Stretch(view._ring.rectTransform);
            view._bar = ui.Panel("Bar", background.transform, WithAlpha(theme.Accent, 0f), rounded: true, radius: 2f);
            view._bar.raycastTarget = false;
            RectTransform bar = view._bar.rectTransform;
            bar.anchorMin = new Vector2(0f, 0.22f);
            bar.anchorMax = new Vector2(0f, 0.78f);
            bar.pivot = new Vector2(0f, 0.5f);
            bar.sizeDelta = new Vector2(3f, 0f);
            bar.anchoredPosition = new Vector2(1f, 0f);

            BuildTile(view, background.rectTransform, ui, spec, view.TileLeft);

            // Right side: star (hover or pinned), then the pin-count pill for custom gates.
            float right = 6f;
            view._star = ui.IconButton("Star", background.transform, Icons.Star, null, () => view.PinToggleRequested?.Invoke(view),
                22f, 12f);
            view._starIcon = view._star.GetComponentInChildren<TextMeshProUGUI>();
            RectTransform star = (RectTransform)view._star.transform;
            star.anchorMin = star.anchorMax = new Vector2(1f, 0.5f);
            star.pivot = new Vector2(1f, 0.5f);
            star.sizeDelta = new Vector2(22f, 22f);
            star.anchoredPosition = new Vector2(-right, 0f);
            Destroy(view._star.GetComponent<LayoutElement>());
            right += 24f;

            if (spec.ShowDetail && !string.IsNullOrEmpty(spec.Detail))
            {
                TextMeshProUGUI measure = ui.Text("Detail", background.transform, spec.Detail, 10.5f, theme.TextMuted,
                    TextAlignmentOptions.Center);
                // Measured from font metrics: rows built inside a hidden tab are inactive, and TMP reports no size then.
                float width = Mathf.Ceil(TextWidth(theme.UiFont, spec.Detail, 10.5f)) + 14f;
                Image pill = ui.Panel("Pill", background.transform, theme.ButtonNormal, rounded: true, radius: 8f);
                pill.raycastTarget = false;
                RectTransform pillRect = pill.rectTransform;
                pillRect.anchorMin = pillRect.anchorMax = new Vector2(1f, 0.5f);
                pillRect.pivot = new Vector2(1f, 0.5f);
                pillRect.sizeDelta = new Vector2(width, 17f);
                pillRect.anchoredPosition = new Vector2(-right, 0f);
                measure.transform.SetParent(pillRect, false);
                UiFactory.Stretch(measure.rectTransform);
                right += width + 6f;
            }

            view._name = ui.Text("Name", background.transform, spec.Title, 13.5f, theme.TextPrimary);
            view._name.richText = true;
            UiFactory.Stretch(view._name.rectTransform, view.TileLeft + TileWidth + 9f, right);

            view.Rank = 0;
            view.RefreshStar();
            view.ApplyState(instant: true);
            return view;
        }

        /// <summary>
        /// Miniature gate: a rounded body holding the letter, icon text or glyph, with up to four pin dots on each
        /// side (inputs left, outputs right). Gives the list the same shape language as the workspace.
        /// </summary>
        private static void BuildTile(LibraryItemView view, RectTransform row, UiFactory ui, LibraryEntrySpec spec,
            float left)
        {
            VisualTheme theme = ui.Theme;
            RectTransform holder = UiFactory.CreateRect("Tile", row);
            holder.anchorMin = holder.anchorMax = new Vector2(0f, 0.5f);
            holder.pivot = new Vector2(0f, 0.5f);
            holder.sizeDelta = new Vector2(TileWidth, TileHeight);
            holder.anchoredPosition = new Vector2(left, 0f);

            bool custom = spec.Style == LibraryTileStyle.Accent;
            Color body = custom ? theme.AccentSoft : theme.ButtonNormal;
            Color pin = custom ? WithAlpha(theme.Accent, 0.9f) : theme.TextMuted;
            Image bodyImage = ui.Panel("Body", holder, body, rounded: true, radius: 6f);
            bodyImage.raycastTarget = false;
            UiFactory.Stretch(bodyImage.rectTransform, 3f, 3f);

            if (spec.Style == LibraryTileStyle.Icon)
            {
                TextMeshProUGUI glyph = ui.Icon("Glyph", bodyImage.transform, spec.Tile, 12.5f, theme.TextSecondary);
                UiFactory.Stretch(glyph.rectTransform);
            }
            else
            {
                Color textColor = custom ? theme.Accent : theme.TextSecondary;
                TextMeshProUGUI text = ui.Text("TileText", bodyImage.transform, (spec.Tile ?? string.Empty).Trim(), 11f,
                    textColor, TextAlignmentOptions.Center, FontStyles.Bold);
                text.enableAutoSizing = true;
                text.fontSizeMin = 6.5f;
                text.fontSizeMax = 11f;
                text.overflowMode = TextOverflowModes.Overflow;
                UiFactory.Stretch(text.rectTransform, 1.5f, 1.5f);
            }

            AddDots(ui, holder, spec.Inputs, 0f, pin);
            AddDots(ui, holder, spec.Outputs, 1f, pin);
        }

        private static void AddDots(UiFactory ui, RectTransform holder, int count, float side, Color color)
        {
            int shown = Mathf.Min(count, MaxDots);
            if (shown <= 0)
            {
                return;
            }

            float span = TileHeight - 8f;
            for (int i = 0; i < shown; i++)
            {
                float t = shown == 1 ? 0.5f : (float)i / (shown - 1);
                Image dot = ui.Panel("Pin", holder, i == shown - 1 && count > MaxDots ? WithAlpha(color, color.a * 0.4f) : color);
                dot.sprite = RenderAssets.Circle;
                dot.raycastTarget = false;
                RectTransform rect = dot.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(side, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(5f, 5f);
                rect.anchoredPosition = new Vector2(side < 0.5f ? 2.5f : -2.5f, -4f - t * span);
            }
        }

        /// <summary>Width of <paramref name="text"/> at <paramref name="size"/>, from glyph advances (works while inactive).</summary>
        public static float TextWidth(TMP_FontAsset font, string text, float size)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0f;
            }

            if (font == null)
            {
                return text.Length * size * 0.6f;
            }

            float scale = size / font.faceInfo.pointSize * font.faceInfo.scale;
            float width = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                width += font.characterLookupTable.TryGetValue(text[i], out TMP_Character character)
                    ? character.glyph.metrics.horizontalAdvance * scale
                    : size * 0.6f;
            }

            return width;
        }

        /// <summary>Updates the search rank for <paramref name="filter"/> (0 = name starts with it, 1 = a word in the
        /// name does, 2 = the name contains it, 3 = other text matches, -1 = no match).</summary>
        public bool Matches(string filter)
        {
            Rank = RankOf(filter);
            return Rank >= 0;
        }

        private int RankOf(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return 0;
            }

            string title = Spec.Title;
            int index = title.IndexOf(filter, StringComparison.OrdinalIgnoreCase);
            if (index == 0)
            {
                return 0;
            }

            if (index > 0)
            {
                return char.IsLetterOrDigit(title[index - 1]) ? 2 : 1;
            }

            return SearchText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ? 3 : -1;
        }

        /// <summary>Shows the name with the part matching <paramref name="filter"/> highlighted.</summary>
        public void SetHighlightedText(string filter)
        {
            filter ??= string.Empty;
            if (filter == _highlightFilter)
            {
                return;
            }

            _highlightFilter = filter;
            string title = Spec.Title;
            int index = filter.Length == 0 ? -1 : title.IndexOf(filter, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                _name.text = Escape(title);
                return;
            }

            Builder.Clear();
            Builder.Append(Escape(title.Substring(0, index)));
            Builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(_ui.Theme.Accent)).Append("><u>");
            Builder.Append(Escape(title.Substring(index, filter.Length)));
            Builder.Append("</u></color>");
            Builder.Append(Escape(title.Substring(index + filter.Length)));
            _name.text = Builder.ToString();
        }

        private static string Escape(string text)
        {
            return string.IsNullOrEmpty(text) || text.IndexOf('<') < 0 ? text : "<noparse>" + text + "</noparse>";
        }

        public void SetArmed(bool armed)
        {
            if (_armed != armed)
            {
                _armed = armed;
                ApplyState(instant: false);
            }
        }

        public void SetHighlighted(bool highlighted)
        {
            if (_highlighted != highlighted)
            {
                _highlighted = highlighted;
                ApplyState(instant: false);
            }
        }

        public void SetPinned(bool pinned)
        {
            if (_pinned != pinned)
            {
                _pinned = pinned;
                RefreshStar();
            }
        }

        private void RefreshStar()
        {
            bool visible = _pinned || _hovered;
            _star.gameObject.SetActive(visible);
            Color color = _pinned ? _ui.Theme.Warning : _ui.Theme.TextMuted;
            ButtonVisual visual = _star.GetComponent<ButtonVisual>();
            if (visual != null)
            {
                visual.SetLabelColor(color);
            }
            else
            {
                _starIcon.color = color;
            }

            TooltipTrigger trigger = _star.GetComponent<TooltipTrigger>();
            if (trigger == null)
            {
                trigger = _ui.AddTooltip(_star.gameObject, string.Empty);
            }

            trigger.Text = _pinned ? "Remove from favorites" : "Add to favorites";
        }

        private void ApplyState(bool instant)
        {
            VisualTheme theme = _ui.Theme;
            Color hover = theme.ButtonHover;
            Color background = _pressed ? theme.ButtonPressed
                : _armed ? theme.AccentSoft
                : _hovered || _highlighted ? hover
                : WithAlpha(hover, 0f);
            Color bar = WithAlpha(theme.Accent, _armed ? 1f : 0f);
            Color ring = WithAlpha(theme.Accent, _highlighted ? 0.75f : 0f);
            _background.DOKill();
            _bar.DOKill();
            _ring.DOKill();
            if (instant || !isActiveAndEnabled)
            {
                _background.color = background;
                _bar.color = bar;
                _ring.color = ring;
                return;
            }

            float duration = _pressed ? 0.04f : Fade;
            _background.DOColor(background, duration).SetUpdate(true);
            _bar.DOColor(bar, duration).SetUpdate(true);
            _ring.DOColor(ring, duration).SetUpdate(true);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && !eventData.dragging)
            {
                Activated?.Invoke(this);
                if (eventData.clickCount == 2 && Request.Component.Kind == ComponentKind.Custom)
                {
                    // Double-click opens the gate's internal circuit (the first click armed placement; drop it).
                    _workspace.CancelPlacement();
                    _workspace.OpenDefinition(Request.Component.Definition);
                    return;
                }

                _workspace.BeginPlacement(Request, fromDrag: false);
            }
            else if (eventData.button == PointerEventData.InputButton.Right)
            {
                Activated?.Invoke(this);
                ContextRequested?.Invoke(this, eventData.position);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                Activated?.Invoke(this);
                _workspace.BeginPlacement(Request, fromDrag: true);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            // Required for OnBeginDrag/OnEndDrag; the workspace ghost follows the pointer by itself.
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            // Placement is committed or cancelled by the workspace when the button is released.
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                _pressed = true;
                ApplyState(instant: false);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_pressed)
            {
                _pressed = false;
                ApplyState(instant: false);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            RefreshStar();
            ApplyState(instant: false);
            if (!eventData.dragging)
            {
                HoverChanged?.Invoke(this, true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            _pressed = false;
            RefreshStar();
            ApplyState(instant: false);
            HoverChanged?.Invoke(this, false);
        }

        private void OnDisable()
        {
            if (_background == null)
            {
                return;
            }

            if (_hovered)
            {
                HoverChanged?.Invoke(this, false);
            }

            _hovered = false;
            _pressed = false;
            RefreshStar();
            ApplyState(instant: true);
        }

        private void OnDestroy()
        {
            if (_background != null)
            {
                _background.DOKill();
                _bar.DOKill();
                _ring.DOKill();
            }
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }
    }
}
