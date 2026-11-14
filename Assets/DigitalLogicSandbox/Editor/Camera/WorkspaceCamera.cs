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

        /// <summary>Fits <paramref name="bounds"/> (world space) into the whole camera view with a margin.</summary>
        public void Frame(Rect bounds)
        {
            Frame(bounds, new Rect(0f, 0f, _camera.pixelWidth, _camera.pixelHeight));
        }

        /// <summary>
        /// Fits <paramref name="bounds"/> (world space) into <paramref name="viewport"/>, the part of the screen (in
        /// pixels) that is not covered by panels, with a margin. Falls back to the whole view when the viewport is
        /// degenerate.
        /// </summary>
        public void Frame(Rect bounds, Rect viewport)
        {
            ComputeFrame(bounds, viewport, _camera.pixelWidth, _camera.pixelHeight, out Vector2 center, out float size);
            MoveTo(center, size);
        }

        /// <summary>Camera centre and orthographic size that fit world bounds into a screen viewport.</summary>
        public static void ComputeFrame(Rect bounds, Rect viewport, int screenWidth, int screenHeight,
            out Vector2 center, out float size)
        {
            screenWidth = Mathf.Max(1, screenWidth);
            screenHeight = Mathf.Max(1, screenHeight);
            var screen = new Rect(0f, 0f, screenWidth, screenHeight);
            viewport = Rect.MinMaxRect(Mathf.Max(screen.xMin, viewport.xMin), Mathf.Max(screen.yMin, viewport.yMin),
                Mathf.Min(screen.xMax, viewport.xMax), Mathf.Min(screen.yMax, viewport.yMax));
            if (viewport.width < MinViewportPixels || viewport.height < MinViewportPixels)
            {
                viewport = screen;
            }

            // Margin: 20 % of the content plus one grid unit on each side.
            float worldWidth = bounds.width * 1.2f + 2f;
            float worldHeight = bounds.height * 1.2f + 2f;
            size = 0.5f * Mathf.Max(worldHeight * screenHeight / viewport.height,
                worldWidth * screenHeight / viewport.width);
            size = ClampSize(size);

            // Shift the camera so the content centre lands on the viewport centre rather than the screen centre.
            float worldPerPixel = size * 2f / screenHeight;
            Vector2 offsetPixels = viewport.center - screen.center;
            center = bounds.center - offsetPixels * worldPerPixel;
        }

        /// <summary>Viewports smaller than this (pixels) are ignored when framing.</summary>
        public const float MinViewportPixels = 64f;

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
