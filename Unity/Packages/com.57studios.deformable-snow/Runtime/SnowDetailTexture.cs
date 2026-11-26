using UnityEngine;

namespace Studios57.DeformableSnow
{
    /// <summary>
    /// Small tiling detail texture for the snow surface: a seamless multi-octave height
    /// field baked into a tangent-style normal in RG, the raw height in B and a second
    /// noise in A (used for subtle albedo breakup). Generated once at runtime so the
    /// feature ships without a texture asset and without a texture import to maintain.
    ///
    /// The surface shader projects it in world XZ, so no UV layout or lightmap concern.
    /// </summary>
    public static class SnowDetailTexture
    {
        private const int Resolution = 256;
        // Frequencies are whole cycles across the texture, which is what makes it tile.
        private static readonly int[] Frequencies = { 3, 6, 12, 24, 48 };
        private static readonly float[] Amplitudes = { 1.0f, 0.55f, 0.3f, 0.16f, 0.09f };

        private static Texture2D cached;

        public static Texture2D Get()
        {
            if (cached != null)
                return cached;

            var heights = new float[Resolution * Resolution];
            float total = 0f;
            for (int i = 0; i < Frequencies.Length; i++)
                total += Amplitudes[i];

            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    float u = x / (float)Resolution;
                    float v = y / (float)Resolution;
                    float h = 0f;
                    for (int o = 0; o < Frequencies.Length; o++)
                    {
                        h += Amplitudes[o] * PeriodicNoise(u * Frequencies[o], v * Frequencies[o],
                            Frequencies[o], o);
                    }
                    heights[y * Resolution + x] = h / total;
                }
            }

            var pixels = new Color32[Resolution * Resolution];
            // Value noise has a kink where lattice cells meet, and the normal is a
            // derivative, so blur the height before differentiating or the grid shows up.
            for (int pass = 0; pass < 3; pass++)
                heights = Smooth(heights);

            // 256 texels over a few metres; a slope scale of ~6 gives a believable grain
            // once the shader multiplies by its detail strength.
            const float slopeScale = 6f;
            for (int y = 0; y < Resolution; y++)
            {
                int y0 = (y + Resolution - 1) % Resolution;
                int y1 = (y + 1) % Resolution;
                for (int x = 0; x < Resolution; x++)
                {
                    int x0 = (x + Resolution - 1) % Resolution;
                    int x1 = (x + 1) % Resolution;

                    float dx = (heights[y * Resolution + x1] - heights[y * Resolution + x0]) * slopeScale;
                    float dy = (heights[y1 * Resolution + x] - heights[y0 * Resolution + x]) * slopeScale;

                    var n = new Vector3(-dx, -dy, 1f);
                    n.Normalize();

                    float height = heights[y * Resolution + x];
                    float tint = PeriodicNoise(x / (float)Resolution * 8f, y / (float)Resolution * 8f, 8, 91);

                    pixels[y * Resolution + x] = new Color32(
                        ToByte(n.x * 0.5f + 0.5f),
                        ToByte(n.y * 0.5f + 0.5f),
                        ToByte(height),
                        ToByte(tint));
                }
            }

            var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, true, true)
            {
                name = "DeformableSnow Detail",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            cached = texture;
            return texture;
        }

        private static byte ToByte(float value)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
        }

        private static float[] Smooth(float[] source)
        {
            var result = new float[source.Length];
            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    float sum = 0f;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = (y + dy + Resolution) % Resolution;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = (x + dx + Resolution) % Resolution;
                            sum += source[yy * Resolution + xx] * (dx == 0 ? 2 : 1) * (dy == 0 ? 2 : 1);
                        }
                    }
                    result[y * Resolution + x] = sum / 16f;
                }
            }
            return result;
        }

        private static float PeriodicNoise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = x - xi;
            float yf = y - yi;
            float sx = xf * xf * (3f - 2f * xf);
            float sy = yf * yf * (3f - 2f * yf);

            float a = Hash01(xi, yi, period, seed);
            float b = Hash01(xi + 1, yi, period, seed);
            float c = Hash01(xi, yi + 1, period, seed);
            float d = Hash01(xi + 1, yi + 1, period, seed);

            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        // The lattice wraps at "period" so opposite edges of the texture match exactly.
        private static float Hash01(int x, int y, int period, int seed)
        {
            int px = ((x % period) + period) % period;
            int py = ((y % period) + period) % period;
            uint h = (uint)(px * 73856093) ^ (uint)(py * 19349663) ^ (uint)(seed * 83492791);
            h ^= h >> 13;
            h *= 0x85ebca6bu;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / 16777215f;
        }
    }
}
