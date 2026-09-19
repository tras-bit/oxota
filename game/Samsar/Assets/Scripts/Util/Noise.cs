using UnityEngine;

namespace Samsar
{
    /// <summary>Детерминированный шум (value noise) — чтобы карта всегда была одинаковой при одном seed.</summary>
    public static class Noise
    {
        static int[] perm = new int[512];
        static bool seeded;

        /// <summary>Инициализировать шум только если он ещё не задан (для утилит).</summary>
        public static void EnsureSeed()
        {
            if (!seeded) Seed(1337);
        }

        public static void Seed(int seed)
        {
            seeded = true;
            var rnd = new System.Random(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                int t = p[i]; p[i] = p[j]; p[j] = t;
            }
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
        }

        static float Fade(float t) { return t * t * t * (t * (t * 6f - 15f) + 10f); }
        static float Grad(int hash, float x, float y)
        {
            switch (hash & 7)
            {
                case 0: return x + y;
                case 1: return x - y;
                case 2: return -x + y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }

        /// <summary>Гладкий шум в диапазоне примерно [-1, 1].</summary>
        public static float Perlin(float x, float y)
        {
            if (!seeded) EnsureSeed();
            int xi = Mathf.FloorToInt(x) & 255;
            int yi = Mathf.FloorToInt(y) & 255;
            float xf = x - Mathf.Floor(x);
            float yf = y - Mathf.Floor(y);
            float u = Fade(xf);
            float v = Fade(yf);
            int aa = perm[perm[xi] + yi];
            int ab = perm[perm[xi] + yi + 1];
            int ba = perm[perm[xi + 1] + yi];
            int bb = perm[perm[xi + 1] + yi + 1];
            float x1 = Mathf.Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1f, yf), u);
            float x2 = Mathf.Lerp(Grad(ab, xf, yf - 1f), Grad(bb, xf - 1f, yf - 1f), u);
            return Mathf.Lerp(x1, x2, v);
        }

        /// <summary>Фрактальный шум: несколько октав.</summary>
        public static float Fbm(float x, float y, int octaves = 4, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Perlin(x * freq, y * freq) * amp;
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>Плоская площадка (для города/станции): сглаживает высоту к нулю в заданном радиусе.</summary>
        public static float Mask(float value, float centerX, float centerZ, float radius, float falloff)
        {
            float d = Mathf.Sqrt(centerX * centerX + centerZ * centerZ);
            float k = Mathf.Clamp01((d - radius) / Mathf.Max(0.001f, falloff));
            return value * k;
        }
    }
}
