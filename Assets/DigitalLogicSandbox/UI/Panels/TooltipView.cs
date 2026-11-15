using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Panels
{
    /// <summary>
    /// The single shared tooltip: a small card that fades in near the pointer after a short rest on a control with a
    /// <see cref="TooltipTrigger"/>. One instance for the whole UI; inactive while nothing is hovered.
    /// </summary>
    public sealed class TooltipView : MonoBehaviour
    {
        /// <summary>Seconds the pointer must rest before the tooltip appears.</summary>
        public const float Delay = 0.45f;

        private const float FadeSpeed = 12f;
        private const float MaxWidth = 260f;

        private RectTransform _rect;
        private RectTransform _canvas;
        private CanvasGroup _group;
        private TextMeshProUGUI _text;
        private RectTransform _source;
        private float _showAt;

        public bool IsVisible => gameObject.activeSelf && _group.alpha > 0.5f;

        public string Text => _text.text;

        public static TooltipView Create(Transform parent, UiFactory ui)
        {
            Image card = ui.Card("Tooltip", parent, ui.Theme.PanelHeader, radius: 8f);
            card.raycastTarget = false;
            var view = card.gameObject.AddComponent<TooltipView>();
            view._rect = card.rectTransform;
            view._canvas = (RectTransform)parent;
            view._rect.anchorMin = view._rect.anchorMax = Vector2.zero;
            view._rect.pivot = new Vector2(0.5f, 1f);
            view._group = card.gameObject.AddComponent<CanvasGroup>();
            view._group.blocksRaycasts = false;
            view._group.interactable = false;

            view._text = ui.Text("Text", card.transform, string.Empty, 12f, ui.Theme.TextPrimary, TextAlignmentOptions.Center);
            view._text.enableWordWrapping = true;
            view._text.richText = true;
            UiFactory.Stretch(view._text.rectTransform, 10f, 10f, 6f, 6f);
            card.gameObject.SetActive(false);
            return view;
        }

        /// <summary>Schedules the tooltip for <paramref name="source"/> (shown after <see cref="Delay"/>).</summary>
        public void Request(RectTransform source, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            _source = source;
            _text.text = text;
            float width = Mathf.Min(MaxWidth, _text.GetPreferredValues(text).x + 1f);
            Vector2 size = _text.GetPreferredValues(text, width, 0f);
            _rect.sizeDelta = new Vector2(width + 20f, size.y + 12f);
            _showAt = Time.unscaledTime + Delay;
            _group.alpha = 0f;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Place();
        }

        /// <summary>Hides the tooltip if it belongs to <paramref name="source"/> (or always when null).</summary>
        public void Release(RectTransform source)
        {
            if (source == null || source == _source)
            {
                _source = null;
                gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            if (_source == null || !_source.gameObject.activeInHierarchy)
            {
                Release(null);
                return;
            }

            if (Time.unscaledTime >= _showAt && _group.alpha < 1f)
            {
                _group.alpha = Mathf.Min(1f, _group.alpha + Time.unscaledDeltaTime * FadeSpeed);
            }
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>Below the control, centred; flipped above it near the bottom; clamped to the screen.</summary>
        private void Place()
        {
            _source.GetWorldCorners(Corners);
            Vector2 bottom = ToCanvas(new Vector2((Corners[0].x + Corners[2].x) * 0.5f, Corners[0].y));
            Vector2 top = ToCanvas(new Vector2((Corners[0].x + Corners[2].x) * 0.5f, Corners[1].y));
            Vector2 size = _rect.sizeDelta;
            Vector2 canvas = _canvas.rect.size;
            const float gap = 8f;
            Vector2 position;
            if (bottom.y - gap - size.y < 4f)
            {
                _rect.pivot = new Vector2(0.5f, 0f);
                position = new Vector2(top.x, top.y + gap);
            }
            else
            {
                _rect.pivot = new Vector2(0.5f, 1f);
                position = new Vector2(bottom.x, bottom.y - gap);
            }

            position.x = Mathf.Clamp(position.x, size.x * 0.5f + 6f, canvas.x - size.x * 0.5f - 6f);
            _rect.anchoredPosition = position;
        }

        private Vector2 ToCanvas(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas, screen, null, out Vector2 local);
            return local + _canvas.rect.size * _canvas.pivot;
        }
    }
}
