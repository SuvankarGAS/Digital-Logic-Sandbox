using System;
using DigitalLogicSandbox.CircuitEditor.Placement;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Panels
{
    /// <summary>
    /// One entry in the gate library. Clicking arms placement (the next click in the workspace places it).
    /// Dragging places it where the button is released.
    /// </summary>
    public sealed class LibraryItemView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private WorkspaceController _workspace;
        private Image _background;
        private Color _normal;
        private Color _hover;

        public PlacementRequest Request { get; private set; }

        /// <summary>Text used by the library search filter.</summary>
        public string SearchText { get; private set; }

        public static LibraryItemView Create(Transform parent, UiFactory ui, WorkspaceController workspace,
            PlacementRequest request, string title, string detail, string icon = null, string searchExtra = null)
        {
            Image background = ui.Panel("Item " + title, parent, ui.Theme.ButtonNormal, rounded: true);
            var layout = background.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 34f;
            layout.minHeight = 34f;

            float nameLeft = 12f;
            if (!string.IsNullOrWhiteSpace(icon))
            {
                // Small badge with the user's icon text in front of the name.
                Image badge = ui.Panel("Icon", background.transform, ui.Theme.Accent, rounded: true);
                badge.raycastTarget = false;
                RectTransform badgeRect = badge.rectTransform;
                badgeRect.anchorMin = new Vector2(0f, 0.5f);
                badgeRect.anchorMax = new Vector2(0f, 0.5f);
                badgeRect.pivot = new Vector2(0f, 0.5f);
                badgeRect.sizeDelta = new Vector2(BadgeSize, BadgeSize);
                badgeRect.anchoredPosition = new Vector2(8f, 0f);
                TextMeshProUGUI badgeText = ui.Text("IconText", badgeRect, icon.Trim(), 11f, ui.Theme.PanelBackground,
                    TextAlignmentOptions.Center, FontStyles.Bold);
                badgeText.enableAutoSizing = true;
                badgeText.fontSizeMin = 7f;
                badgeText.fontSizeMax = 11f;
                UiFactory.Stretch(badgeText.rectTransform, 1f, 1f);
                nameLeft = 8f + BadgeSize + 6f;
            }

            TextMeshProUGUI name = ui.Text("Name", background.transform, title, 14f, ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            name.overflowMode = TextOverflowModes.Ellipsis;
            UiFactory.Stretch(name.rectTransform, nameLeft, 80f);
            TextMeshProUGUI info = ui.Text("Detail", background.transform, detail, 12f, ui.Theme.TextSecondary,
                TextAlignmentOptions.MidlineRight);
            UiFactory.Stretch(info.rectTransform, 80f, 12f);

            var view = background.gameObject.AddComponent<LibraryItemView>();
            view._workspace = workspace;
            view._background = background;
            view._normal = ui.Theme.ButtonNormal;
            view._hover = ui.Theme.ButtonHover;
            view.Request = request;
            view.Title = title ?? string.Empty;
            view.SearchText = string.IsNullOrEmpty(searchExtra) ? view.Title : view.Title + " " + searchExtra;
            return view;
        }

        private const float BadgeSize = 22f;

        /// <summary>Display name of the entry.</summary>
        public string Title { get; private set; }

        /// <summary>Raised on right-click with the screen position, for the item context menu.</summary>
        public event Action<LibraryItemView, Vector2> ContextRequested;

        public bool Matches(string filter)
        {
            return string.IsNullOrEmpty(filter) ||
                SearchText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && !eventData.dragging)
            {
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
    }
}
