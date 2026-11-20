using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI
{
    /// <summary>
    /// Vertical scroll view with eased, correctly scaled mouse-wheel scrolling. The Input System UI module reports
    /// <see cref="InputSystemUIInputModule.scrollDeltaPerTick"/> (6 by default) per wheel notch, which a plain
    /// <see cref="ScrollRect"/> multiplies by its sensitivity and applies instantly, so one notch jumped several rows.
    /// Here each notch moves <see cref="StepPixels"/> and the content glides there; touchpad deltas scroll in
    /// proportion. Dragging or setting the position directly cancels the glide.
    /// </summary>
    public sealed class SmoothScrollRect : ScrollRect
    {
        /// <summary>Distance of one wheel notch, in canvas units.</summary>
        public float StepPixels { get; set; } = 40f;

        /// <summary>Largest number of notches one scroll event can move.</summary>
        public const float MaxNotchesPerEvent = 3f;

        /// <summary>Easing speed (higher is snappier).</summary>
        public float Sharpness { get; set; } = 22f;

        /// <summary>Glide toward the target; off makes wheel steps instant (tests).</summary>
        public bool Animate { get; set; } = true;

        private float _target;
        private float _lastApplied;
        private bool _gliding;

        /// <summary>Vertical scroll offset the content is moving toward (0 = top).</summary>
        public float TargetOffset => _gliding ? _target : content != null ? content.anchoredPosition.y : 0f;

        /// <summary>Largest vertical offset (content height minus viewport height, at least 0).</summary>
        public float MaxOffset
        {
            get
            {
                if (content == null)
                {
                    return 0f;
                }

                RectTransform view = viewport != null ? viewport : (RectTransform)transform;
                return Mathf.Max(0f, content.rect.height - view.rect.height);
            }
        }

        public override void OnScroll(PointerEventData data)
        {
            if (!IsActive() || content == null || !vertical)
            {
                return;
            }

            float perTick = 1f;
            if (EventSystem.current != null && EventSystem.current.currentInputModule is InputSystemUIInputModule module &&
                Mathf.Abs(module.scrollDeltaPerTick) > 0.0001f)
            {
                perTick = Mathf.Abs(module.scrollDeltaPerTick);
            }

            // At most a few notches per event: accelerated or high-resolution wheels must not fling the list.
            float notches = Mathf.Clamp(data.scrollDelta.y / perTick, -MaxNotchesPerEvent, MaxNotchesPerEvent);
            if (Mathf.Approximately(notches, 0f))
            {
                return;
            }

            // Wheel up (positive delta) moves toward the top (smaller offset).
            ScrollTo(TargetOffset - notches * StepPixels, Animate);
        }

        /// <summary>Scrolls to vertical <paramref name="offset"/> (0 = top), gliding when <paramref name="animate"/>.</summary>
        public void ScrollTo(float offset, bool animate)
        {
            if (content == null)
            {
                return;
            }

            StopMovement();
            _target = Mathf.Clamp(offset, 0f, MaxOffset);
            if (!animate)
            {
                _gliding = false;
                SetOffset(_target);
                return;
            }

            _gliding = true;
            _lastApplied = content.anchoredPosition.y;
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            _gliding = false;
            base.OnBeginDrag(eventData);
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!_gliding || content == null)
            {
                return;
            }

            float current = content.anchoredPosition.y;
            if (!Mathf.Approximately(current, _lastApplied))
            {
                // Someone else moved the content (scrollbar, ScrollIntoView): stop gliding.
                _gliding = false;
                return;
            }

            _target = Mathf.Clamp(_target, 0f, MaxOffset);
            float next = Mathf.Lerp(current, _target, 1f - Mathf.Exp(-Sharpness * Time.unscaledDeltaTime));
            if (Mathf.Abs(next - _target) < 0.5f)
            {
                next = _target;
                _gliding = false;
            }

            SetOffset(next);
        }

        private void SetOffset(float offset)
        {
            Vector2 position = content.anchoredPosition;
            position.y = offset;
            content.anchoredPosition = position;
            _lastApplied = offset;
            UpdateBounds();
            onValueChanged.Invoke(normalizedPosition);
        }
    }
}
