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
            PlacementRequest request, string title, string detail)
        {
            Image background = ui.Panel("Item " + title, parent, ui.Theme.ButtonNormal, rounded: true);
            var layout = background.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 34f;
            layout.minHeight = 34f;

            TextMeshProUGUI name = ui.Text("Name", background.transform, title, 14f, ui.Theme.TextPrimary,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UiFactory.Stretch(name.rectTransform, 12f, 80f);
            TextMeshProUGUI info = ui.Text("Detail", background.transform, detail, 12f, ui.Theme.TextSecondary,
                TextAlignmentOptions.MidlineRight);
            UiFactory.Stretch(info.rectTransform, 80f, 12f);

            var view = background.gameObject.AddComponent<LibraryItemView>();
            view._workspace = workspace;
            view._background = background;
            view._normal = ui.Theme.ButtonNormal;
            view._hover = ui.Theme.ButtonHover;
            view.Request = request;
            view.SearchText = title ?? string.Empty;
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
                _workspace.BeginPlacement(Request, fromDrag: false);
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
