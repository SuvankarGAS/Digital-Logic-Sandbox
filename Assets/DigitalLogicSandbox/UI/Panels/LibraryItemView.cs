using System;
using DigitalLogicSandbox.CircuitEditor.Placement;
using DigitalLogicSandbox.CircuitEditor.Workspace;
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

    /// <summary>
    /// One entry in the gate library: a small leading tile (a letter for gates, an icon for utilities), the name
    /// and, for custom gates, a muted pin summary. Descriptions are kept in a tooltip rather than on screen.
    /// Clicking arms placement (the next click in the workspace places it); dragging places it where the button is
    /// released.
    /// </summary>
    public sealed class LibraryItemView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private const float TileSize = 26f;
        private const float RowHeight = 36f;

        private WorkspaceController _workspace;
        private Image _background;
        private Color _normal;
        private Color _hover;

        public PlacementRequest Request { get; private set; }

        /// <summary>Text used by the library search filter.</summary>
        public string SearchText { get; private set; }

        /// <summary>Display name of the entry.</summary>
        public string Title { get; private set; }

        /// <summary>Raised on right-click with the screen position, for the item context menu.</summary>
        public event Action<LibraryItemView, Vector2> ContextRequested;

        /// <summary>
        /// Creates an entry. <paramref name="tile"/> is the tile content: a short text (custom gate icon text or a
        /// letter), or an icon glyph when <paramref name="style"/> is <see cref="LibraryTileStyle.Icon"/>.
        /// <paramref name="detail"/> is shown on the row when <paramref name="showDetail"/> is set, and always in
        /// the tooltip.
        /// </summary>
        public static LibraryItemView Create(Transform parent, UiFactory ui, WorkspaceController workspace,
            PlacementRequest request, string title, string detail, string tile, LibraryTileStyle style,
            bool showDetail, string searchExtra = null, string tooltip = null)
        {
            Color hover = ui.Theme.ButtonHover;
            Image background = ui.Panel("Item " + title, parent, new Color(hover.r, hover.g, hover.b, 0f), rounded: true);
            var layout = background.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = RowHeight;
            layout.minHeight = RowHeight;

            Color tileColor = style == LibraryTileStyle.Accent ? ui.Theme.AccentSoft : ui.Theme.ButtonNormal;
            Image badge = ui.Panel("Tile", background.transform, tileColor, rounded: true, radius: 7f);
            badge.raycastTarget = false;
            RectTransform badgeRect = badge.rectTransform;
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0f, 0.5f);
            badgeRect.pivot = new Vector2(0f, 0.5f);
            badgeRect.sizeDelta = new Vector2(TileSize, TileSize);
            badgeRect.anchoredPosition = new Vector2(6f, 0f);
            if (style == LibraryTileStyle.Icon)
            {
                TextMeshProUGUI glyph = ui.Icon("Glyph", badgeRect, tile, 14f, ui.Theme.TextSecondary);
                UiFactory.Stretch(glyph.rectTransform);
            }
            else
            {
                Color textColor = style == LibraryTileStyle.Accent ? ui.Theme.Accent : ui.Theme.TextSecondary;
                TextMeshProUGUI badgeText = ui.Text("TileText", badgeRect, (tile ?? string.Empty).Trim(), 12f, textColor,
                    TextAlignmentOptions.Center, FontStyles.Bold);
                badgeText.enableAutoSizing = true;
                badgeText.fontSizeMin = 7f;
                badgeText.fontSizeMax = 12f;
                badgeText.overflowMode = TextOverflowModes.Overflow;
                UiFactory.Stretch(badgeText.rectTransform, 2f, 2f);
            }

            float detailWidth = showDetail && !string.IsNullOrEmpty(detail) ? 72f : 8f;
            TextMeshProUGUI name = ui.Text("Name", background.transform, title, 13.5f, ui.Theme.TextPrimary);
            UiFactory.Stretch(name.rectTransform, 6f + TileSize + 10f, detailWidth);
            if (showDetail && !string.IsNullOrEmpty(detail))
            {
                TextMeshProUGUI info = ui.Text("Detail", background.transform, detail, 11f, ui.Theme.TextMuted,
                    TextAlignmentOptions.MidlineRight);
                info.rectTransform.anchorMin = new Vector2(1f, 0f);
                info.rectTransform.anchorMax = new Vector2(1f, 1f);
                info.rectTransform.pivot = new Vector2(1f, 0.5f);
                info.rectTransform.sizeDelta = new Vector2(70f, 0f);
                info.rectTransform.anchoredPosition = new Vector2(-10f, 0f);
            }

            var view = background.gameObject.AddComponent<LibraryItemView>();
            view._workspace = workspace;
            view._background = background;
            view._normal = background.color;
            view._hover = hover;
            view.Request = request;
            view.Title = title ?? string.Empty;
            view.SearchText = string.IsNullOrEmpty(searchExtra) ? view.Title : view.Title + " " + searchExtra;

            string tip = tooltip ?? detail;
            if (!string.IsNullOrEmpty(tip))
            {
                ui.AddTooltip(background.gameObject, tip + "\n<color=#" + ColorUtility.ToHtmlStringRGB(ui.Theme.TextMuted) +
                    ">" + (request.Component.Kind == Core.Nodes.ComponentKind.Custom
                        ? "Click or drag to place, double-click to open"
                        : "Click or drag to place") + "</color>");
            }

            return view;
        }

        public bool Matches(string filter)
        {
            return string.IsNullOrEmpty(filter) ||
                SearchText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && !eventData.dragging)
            {
                if (eventData.clickCount == 2 && Request.Component.Kind == Core.Nodes.ComponentKind.Custom)
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
                ContextRequested?.Invoke(this, eventData.position);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
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

        public void OnPointerEnter(PointerEventData eventData)
        {
            _background.color = _hover;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _background.color = _normal;
        }

        private void OnDisable()
        {
            if (_background != null)
            {
                _background.color = _normal;
            }
        }
    }
}
