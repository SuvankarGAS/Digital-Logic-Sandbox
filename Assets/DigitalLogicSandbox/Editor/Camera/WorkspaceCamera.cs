using System;
using DG.Tweening;
using UnityEngine;

namespace DigitalLogicSandbox.CircuitEditor.CameraControl
{
    /// <summary>
    /// Orthographic workspace camera: pan, zoom at a screen point, framing and reset. Direct manipulation
    /// (dragging, scroll zoom) is applied immediately. Toolbar actions (zoom buttons, reset, frame) are smoothed
    /// with DOTween. Any direct manipulation kills a running tween.
    /// </summary>
    public sealed class WorkspaceCamera
    {
        public const float DefaultSize = 10f;
        public const float MinSize = 2f;
        public const float MaxSize = 80f;
        public const float ZoomFactor = 1.15f;
        public const float TweenDuration = 0.25f;

        private readonly Camera _camera;
        private readonly Transform _transform;

        public WorkspaceCamera(Camera camera)
        {
            _camera = camera ? camera : throw new ArgumentNullException(nameof(camera));
            _transform = camera.transform;
            _camera.orthographic = true;
        }

        public Camera Camera => _camera;

        public float Size => _camera.orthographicSize;

        public Vector2 Position => _transform.position;

        /// <summary>Animate toolbar camera actions (disable for tests or reduced motion).</summary>
        public bool Animate { get; set; } = true;

        public Vector2 ScreenToWorld(Vector2 screen)
        {
            Vector3 world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            return new Vector2(world.x, world.y);
        }

        public Vector2 WorldToScreen(Vector2 world)
        {
            Vector3 screen = _camera.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
            return new Vector2(screen.x, screen.y);
        }

        /// <summary>World units per screen pixel at the current zoom.</summary>
        public float WorldPerPixel => _camera.orthographicSize * 2f / Mathf.Max(1, _camera.pixelHeight);

        /// <summary>Moves the view so content follows a pointer drag of <paramref name="screenDelta"/> pixels.</summary>
        public void Pan(Vector2 screenDelta)
        {
            KillTweens();
            Vector2 delta = screenDelta * WorldPerPixel;
            _transform.position -= new Vector3(delta.x, delta.y, 0f);
        }

        /// <summary>Zooms by mouse-wheel notches, keeping the world point under <paramref name="screenPoint"/> fixed.</summary>
        public void ZoomAt(Vector2 screenPoint, float notches)
        {
            if (Mathf.Approximately(notches, 0f))
            {
                return;
            }

            KillTweens();
            Vector2 before = ScreenToWorld(screenPoint);
            _camera.orthographicSize = ClampSize(_camera.orthographicSize * Mathf.Pow(ZoomFactor, -notches));
            Vector2 after = ScreenToWorld(screenPoint);
            Vector2 correction = before - after;
            _transform.position += new Vector3(correction.x, correction.y, 0f);
        }

        public void ZoomIn()
        {
            SetSize(_camera.orthographicSize / (ZoomFactor * ZoomFactor));
        }

        public void ZoomOut()
        {
            SetSize(_camera.orthographicSize * ZoomFactor * ZoomFactor);
        }

        public void ResetView()
        {
            MoveTo(Vector2.zero, DefaultSize);
        }

        /// <summary>Fits <paramref name="bounds"/> (world space) into the view with a margin.</summary>
        public void Frame(Rect bounds)
        {
            float aspect = Mathf.Max(0.1f, _camera.aspect);
            float size = Mathf.Max(bounds.height * 0.5f, bounds.width * 0.5f / aspect) * 1.2f + 1f;
            MoveTo(bounds.center, size);
        }

        public void MoveTo(Vector2 center, float size)
        {
            KillTweens();
            size = ClampSize(size);
            var target = new Vector3(center.x, center.y, _transform.position.z);
            if (!Animate)
            {
                _transform.position = target;
                _camera.orthographicSize = size;
                return;
            }

            _transform.DOMove(target, TweenDuration).SetEase(Ease.OutCubic);
            _camera.DOOrthoSize(size, TweenDuration).SetEase(Ease.OutCubic);
        }

        private void SetSize(float size)
        {
            KillTweens();
            size = ClampSize(size);
            if (!Animate)
            {
                _camera.orthographicSize = size;
                return;
            }

            _camera.DOOrthoSize(size, TweenDuration).SetEase(Ease.OutCubic);
        }

        /// <summary>Stops running camera tweens (call before the camera is destroyed).</summary>
        public void StopAnimations()
        {
            KillTweens();
        }

        private void KillTweens()
        {
            _transform.DOKill();
            _camera.DOKill();
        }

        private static float ClampSize(float size)
        {
            return Mathf.Clamp(size, MinSize, MaxSize);
        }
    }
}
