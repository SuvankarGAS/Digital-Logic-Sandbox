using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Panels
{
    /// <summary>
    /// Thin strip on the library's right edge: drag to resize the panel, double-click to reset its width. A hairline
    /// in the accent colour appears while hovered or dragged.
    /// </summary>
    public sealed class LibraryResizeHandle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private Image _line;
        private Color _color;
        private bool _hovered;
        private bool _dragging;
        private Canvas _canvas;

        /// <summary>Horizontal drag in canvas units since the last event.</summary>
        public event Action<float> Dragged;

        public event Action DragEnded;

        public event Action ResetRequested;

        public bool IsDragging => _dragging;

        public static LibraryResizeHandle Create(RectTransform panel, UiFactory ui)
        {
            RectTransform rect = UiFactory.CreateRect("Resize Handle", panel);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(10f, -2f * UiFactory.CardRadius);
            rect.anchoredPosition = new Vector2(1f, 0f);
            var hit = rect.gameObject.AddComponent<Image>();
            hit.color = Color.clear;

            var handle = rect.gameObject.AddComponent<LibraryResizeHandle>();
            handle._color = ui.Theme.Accent;
            handle._line = ui.Panel("Line", rect, new Color(handle._color.r, handle._color.g, handle._color.b, 0f),
                rounded: true, radius: 1f);
            handle._line.raycastTarget = false;
            RectTransform line = handle._line.rectTransform;
            line.anchorMin = new Vector2(0.5f, 0f);
            line.anchorMax = new Vector2(0.5f, 1f);
            line.sizeDelta = new Vector2(2f, 0f);
            ui.AddTooltip(rect.gameObject, "Drag to resize · double-click to reset");
            return handle;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            Refresh();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            _dragging = true;
            _canvas = GetComponentInParent<Canvas>();
            Refresh();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }

            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            Dragged?.Invoke(eventData.delta.x / Mathf.Max(0.01f, scale));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            Refresh();
            DragEnded?.Invoke();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount == 2 && !eventData.dragging)
            {
                ResetRequested?.Invoke();
            }
        }

        private void Refresh()
        {
            _line.DOKill();
            _line.DOFade(_hovered || _dragging ? 0.85f : 0f, 0.12f).SetUpdate(true);
        }

        private void OnDisable()
        {
            _hovered = false;
            _dragging = false;
            if (_line != null)
            {
                _line.DOKill();
                _line.color = new Color(_color.r, _color.g, _color.b, 0f);
            }
        }
    }
}
