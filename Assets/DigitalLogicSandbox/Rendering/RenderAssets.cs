using UnityEngine;

namespace DigitalLogicSandbox.Rendering
{
    /// <summary>
    /// Procedurally generated, cached sprites used by all workspace views (no texture assets to manage).
    /// The rounded rectangle is 9-sliced so any gate size keeps crisp corners.
    /// </summary>
    public static class RenderAssets
    {
        private const int TextureSize = 64;
        private const float PixelsPerUnit = 64f;

        private static Sprite _roundedRect;
        private static Sprite _circle;
        private static Sprite _square;

        /// <summary>White rounded rectangle, 1×1 unit native size, 9-slice borders of 16 px.</summary>
        public static Sprite RoundedRect => _roundedRect != null ? _roundedRect : _roundedRect = CreateRoundedRect();

        /// <summary>White anti-aliased disc, 1 unit diameter.</summary>
        public static Sprite Circle => _circle != null ? _circle : _circle = CreateCircle();

        /// <summary>Plain white square, 1×1 unit.</summary>
        public static Sprite Square => _square != null ? _square : _square = CreateSquare();

        private static Sprite CreateSquare()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                name = "DLS Square",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f, 0,
                SpriteMeshType.FullRect);
            sprite.name = "DLS Square";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private static Sprite CreateRoundedRect()
        {
            const float radius = 14f;
            Texture2D texture = CreateTexture("DLS RoundedRect");
            var pixels = new Color32[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float cx = Mathf.Clamp(px, radius, TextureSize - radius);
                    float cy = Mathf.Clamp(py, radius, TextureSize - radius);
                    float distance = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, TextureSize, TextureSize), new Vector2(0.5f, 0.5f),
                PixelsPerUnit, 0, SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16));
            sprite.name = "DLS RoundedRect";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private static Sprite CreateCircle()
        {
            Texture2D texture = CreateTexture("DLS Circle");
            var pixels = new Color32[TextureSize * TextureSize];
            float radius = TextureSize * 0.5f - 1f;
            float center = TextureSize * 0.5f;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, TextureSize, TextureSize), new Vector2(0.5f, 0.5f),
                PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = "DLS Circle";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private static Texture2D CreateTexture(string name)
        {
            return new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
        }
    }
}
