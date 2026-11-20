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
        private static Sprite _roundedOutline;
        private static Sprite _softShadow;
        private static Sprite _glow;

        /// <summary>9-slice border (texels) of <see cref="SoftShadow"/>.</summary>
        public const int ShadowBorder = 30;

        /// <summary>
        /// Rounded rectangle outline (≈1.5 texel stroke) matching <see cref="RoundedRect"/>; 9-sliced with the same
        /// borders so a fill and its outline line up at any size.
        /// </summary>
        public static Sprite RoundedOutline => _roundedOutline != null ? _roundedOutline : _roundedOutline = CreateRoundedOutline();

        /// <summary>Blurred rounded rectangle for drop shadows; 9-slice borders of <see cref="ShadowBorder"/> texels.</summary>
        public static Sprite SoftShadow => _softShadow != null ? _softShadow : _softShadow = CreateSoftShadow();

        /// <summary>Radial glow (bright centre fading smoothly to transparent), 1 unit diameter.</summary>
        public static Sprite Glow => _glow != null ? _glow : _glow = CreateGlow();

        /// <summary>White rounded rectangle, 1×1 unit native size, 9-slice borders of 16 px.</summary>
        public static Sprite RoundedRect => _roundedRect != null ? _roundedRect : _roundedRect = CreateRoundedRect();

        /// <summary>White anti-aliased disc, 1 unit diameter.</summary>
        public static Sprite Circle => _circle != null ? _circle : _circle = CreateCircle();

        /// <summary>Plain white square, 1×1 unit.</summary>
        public static Sprite Square => _square != null ? _square : _square = CreateSquare();

        /// <summary>
        /// Seven-segment bar: an elongated hexagon with 45° pointed ends, 1 unit thick. 9-sliced with end borders of
        /// half the thickness, so a sliced renderer of size (length / thickness, 1) keeps true 45° tips at any length.
        /// </summary>
        public static Sprite Segment => _segment != null ? _segment : _segment = CreateSegment();

        /// <summary>
        /// Soft halo for a lit segment: a blurred capsule 1 unit thick whose ends are 9-sliced like <see cref="Segment"/>.
        /// </summary>
        public static Sprite SegmentGlow => _segmentGlow != null ? _segmentGlow : _segmentGlow = CreateSegmentGlow();

        private static Sprite _segment;
        private static Sprite _segmentGlow;

        private static Sprite CreateSegment()
        {
            const int width = 128;
            const int height = 32;
            const float half = height * 0.5f;
            const float inset = 1f; // keeps the anti-aliased edge inside the texture
            float flat = width * 0.5f - half; // where the pointed tip starts
            Texture2D texture = CreateTexture("DLS Segment", width, height);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float px = Mathf.Abs(x + 0.5f - width * 0.5f);
                    float py = Mathf.Abs(y + 0.5f - half);
                    // Distance to the hexagon: the flat sides, and the 45° tips (normalised by √2).
                    float sides = py - (half - inset);
                    float tips = (px - flat + py - (half - inset)) * 0.70710678f;
                    float alpha = Mathf.Clamp01(0.5f - Mathf.Max(sides, tips));
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            // The tip region (half the height) on each end is the 9-slice border; the body stretches.
            return Finish(texture, pixels, "DLS Segment", width, height, height, new Vector4(half, 0f, half, 0f));
        }

        private static Sprite CreateSegmentGlow()
        {
            const int width = 128;
            const int height = 64;
            const float half = height * 0.5f;
            float core = width * 0.5f - half; // capsule centre line half-length
            Texture2D texture = CreateTexture("DLS SegmentGlow", width, height);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float px = Mathf.Max(0f, Mathf.Abs(x + 0.5f - width * 0.5f) - core);
                    float py = y + 0.5f - half;
                    float t = Mathf.Clamp01(1f - Mathf.Sqrt(px * px + py * py) / half);
                    float alpha = t * t * (3f - 2f * t) * t; // smooth, concentrated near the bar
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            return Finish(texture, pixels, "DLS SegmentGlow", width, height, height, new Vector4(half, 0f, half, 0f));
        }

        private static Sprite Finish(Texture2D texture, Color32[] pixels, string name, int width, int height,
            float pixelsPerUnit, Vector4 border)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f),
                pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private static Texture2D CreateTexture(string name, int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
        }

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

        private static float RoundedRectDistance(float px, float py, float inset, float radius)
        {
            // Signed distance to a rounded rectangle inset by 'inset' texels (negative inside).
            float half = TextureSize * 0.5f;
            float qx = Mathf.Abs(px - half) - (half - inset - radius);
            float qy = Mathf.Abs(py - half) - (half - inset - radius);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        private static Sprite CreateRoundedOutline()
        {
            const float radius = 14f;
            const float stroke = 1.6f;
            Texture2D texture = CreateTexture("DLS RoundedOutline");
            var pixels = new Color32[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float d = RoundedRectDistance(x + 0.5f, y + 0.5f, 0f, radius);
                    // Band between -stroke and 0, anti-aliased on both edges.
                    float alpha = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(d + stroke + 0.5f);
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            return Finish(texture, pixels, "DLS RoundedOutline", new Vector4(16, 16, 16, 16));
        }

        private static Sprite CreateSoftShadow()
        {
            const float radius = 10f;
            Texture2D texture = CreateTexture("DLS SoftShadow");
            var pixels = new Color32[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float d = RoundedRectDistance(x + 0.5f, y + 0.5f, ShadowBorder - 2f, radius);
                    float t = Mathf.Clamp01(1f - (d + 6f) / (ShadowBorder - 2f));
                    float alpha = t * t * (3f - 2f * t); // smoothstep falloff
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            return Finish(texture, pixels, "DLS SoftShadow", new Vector4(ShadowBorder, ShadowBorder, ShadowBorder, ShadowBorder));
        }

        private static Sprite CreateGlow()
        {
            Texture2D texture = CreateTexture("DLS Glow");
            var pixels = new Color32[TextureSize * TextureSize];
            float center = TextureSize * 0.5f;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = (x + 0.5f - center) / center;
                    float dy = (y + 0.5f - center) / center;
                    float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(t * t * 255f));
                }
            }

            return Finish(texture, pixels, "DLS Glow", Vector4.zero);
        }

        private static Sprite Finish(Texture2D texture, Color32[] pixels, string name, Vector4 border)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, TextureSize, TextureSize), new Vector2(0.5f, 0.5f),
                PixelsPerUnit, 0, SpriteMeshType.FullRect, border);
            sprite.name = name;
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
