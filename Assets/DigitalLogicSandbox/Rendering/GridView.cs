using UnityEngine;

namespace DigitalLogicSandbox.Rendering
{
    /// <summary>
    /// Infinite procedural background (dot grid and vignette): a single quad parented to the camera, resized to the view. One central
    /// LateUpdate keeps the quad covering the view; dot size, contrast and the minor-dot fade adapt to zoom in the shader.
    /// </summary>
    public sealed class GridView : MonoBehaviour
    {
        private Camera _camera;
        private Material _material;
        private float _lastSize = -1f;
        private float _lastAspect = -1f;
        private static readonly int MinorAlphaId = Shader.PropertyToID("_MinorAlpha");

        public bool Visible
        {
            get => gameObject.activeSelf;
            set => gameObject.SetActive(value);
        }

        public static GridView Create(Camera camera, VisualTheme theme)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Grid";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(camera.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, camera.farClipPlane * 0.9f);
            var view = go.AddComponent<GridView>();
            view._camera = camera;
            view._material = new Material(theme.GridMaterial) { name = "Grid (instance)" };
            view._material.SetColor("_BackColor", theme.WorkspaceBackground);
            view._material.SetColor("_MinorColor", theme.GridMinor);
            view._material.SetColor("_MajorColor", theme.GridMajor);
            view._material.SetColor("_VignetteColor", theme.Vignette);
            view._material.SetFloat(MinorAlphaId, 1f);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = view._material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = -1000;
            camera.backgroundColor = theme.WorkspaceBackground;
            return view;
        }

        private void LateUpdate()
        {
            float size = _camera.orthographicSize;
            float aspect = _camera.aspect;
            if (Mathf.Approximately(size, _lastSize) && Mathf.Approximately(aspect, _lastAspect))
            {
                return;
            }

            _lastSize = size;
            _lastAspect = aspect;
            transform.localScale = new Vector3(size * 2f * aspect * 1.05f, size * 2f * 1.05f, 1f);
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
        }
    }
}
