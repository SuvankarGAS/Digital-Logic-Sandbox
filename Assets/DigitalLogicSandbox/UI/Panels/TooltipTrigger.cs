using UnityEngine;
using UnityEngine.EventSystems;

namespace DigitalLogicSandbox.UI.Panels
{
    /// <summary>Shows a tooltip text in the shared <see cref="TooltipView"/> while the pointer rests on this control.</summary>
    public sealed class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        private UiFactory _ui;

        public string Text { get; set; }

        public void Initialize(UiFactory ui, string text)
        {
            _ui = ui;
            Text = text;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (HasTooltip && !eventData.dragging)
            {
                _ui.Tooltip.Request((RectTransform)transform, Text);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (HasTooltip)
            {
                _ui.Tooltip.Release((RectTransform)transform);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // Clicking means the user knows what the control does: get out of the way.
            if (HasTooltip)
            {
                _ui.Tooltip.Release((RectTransform)transform);
            }
        }

        private void OnDisable()
        {
            if (HasTooltip)
            {
                _ui.Tooltip.Release((RectTransform)transform);
            }
        }

        // Unity-aware null check: the tooltip may already be destroyed during teardown.
        private bool HasTooltip => _ui != null && _ui.Tooltip;
    }
}
