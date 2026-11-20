using DG.Tweening;
using DigitalLogicSandbox.Rendering;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI.Panels
{
    /// <summary>
    /// Visual state of one segment of the library's tab control (All · Favorites · Recent): a soft tile on hover, a
    /// darker one while pressed, and the raised tile for the active tab. Colours fade with DOTween on unscaled time.
    /// </summary>
    public sealed class LibraryTabView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
        IPointerUpHandler
    {
        private const float Fade = 0.1f;

        private VisualTheme _theme;
        private TextMeshProUGUI _label;
        private bool _active;
        private bool _hovered;
        private bool _pressed;

        /// <summary>Background tile of the segment.</summary>
        public Image Tile { get; private set; }

        public bool IsHovered => _hovered;

        public bool IsActive => _active;

        public void Initialize(VisualTheme theme, Image tile, TextMeshProUGUI label)
        {
            _theme = theme;
            Tile = tile;
            _label = label;
            Apply(instant: true);
        }

        /// <summary>Marks the segment as the shown tab.</summary>
        public void SetActive(bool active, bool instant)
        {
            if (_active != active)
            {
                _active = active;
                Apply(instant);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            Apply(instant: false);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            _pressed = false;
            Apply(instant: false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                _pressed = true;
                Apply(instant: false);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_pressed)
            {
                _pressed = false;
                Apply(instant: false);
            }
        }

        private void Apply(bool instant)
        {
            if (Tile == null || _theme == null)
            {
                return;
            }

            Color tile;
            Color label;
            if (_active)
            {
                tile = _hovered ? Color.Lerp(_theme.PanelHeader, Color.white, 0.06f) : _theme.PanelHeader;
                label = _theme.TextPrimary;
            }
            else
            {
                Color hover = _theme.ButtonHover;
                // A softer tile than the active tab's, so the selected tab always reads as selected.
                tile = _pressed ? _theme.ButtonPressed
                    : new Color(hover.r, hover.g, hover.b, _hovered ? 0.55f : 0f);
                label = _hovered || _pressed ? _theme.TextSecondary : _theme.TextMuted;
            }

            Tile.DOKill();
            _label.DOKill();
            if (instant || !isActiveAndEnabled)
            {
                Tile.color = tile;
                _label.color = label;
                return;
            }

            float duration = _pressed ? 0.04f : Fade;
            Tile.DOColor(tile, duration).SetUpdate(true);
            DOTween.To(() => _label.color, value => _label.color = value, label, duration).SetTarget(_label).SetUpdate(true);
        }

        private void OnDisable()
        {
            _hovered = false;
            _pressed = false;
            Apply(instant: true);
        }

        private void OnDestroy()
        {
            if (Tile != null)
            {
                Tile.DOKill();
            }

            if (_label != null)
            {
                _label.DOKill();
            }
        }
    }
}
