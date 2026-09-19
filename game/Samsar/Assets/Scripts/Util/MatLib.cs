using UnityEngine;

namespace Samsar
{
    /// <summary>Материалы, создаваемые в рантайме (без ассетов) — чтобы проект работал «из коробки».</summary>
    public static class MatLib
    {
        static Texture2D softParticle;
        static Texture2D noiseTex;
        static Texture2D groundTex;
        static Texture2D metalTex;

        public static Shader Standard
        {
            get
            {
                var s = Shader.Find("Standard");
                if (s == null) s = Shader.Find("Diffuse");
                return s;
            }
        }

        /// <summary>Мягкий круглый спрайт для частиц (генерируется кодом).</summary>
        public static Texture2D SoftParticle
        {
            get
            {
                if (softParticle != null) return softParticle;
                const int S = 64;
                softParticle = new Texture2D(S, S, TextureFormat.RGBA32, false);
                var px = new Color[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float dx = (x + 0.5f) / S * 2f - 1f;
                        float dy = (y + 0.5f) / S * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                        px[y * S + x] = new Color(1f, 1f, 1f, a);
                    }
                softParticle.SetPixels(px);
                softParticle.Apply();
                softParticle.wrapMode = TextureWrapMode.Clamp;
                return softParticle;
            }
        }

        /// <summary>Серый шум — используется как деталь-текстура грунта и металла.</summary>
        public static Texture2D NoiseTex
        {
            get
            {
                if (noiseTex != null) return noiseTex;
                const int S = 256;
                Noise.EnsureSeed();
                noiseTex = new Texture2D(S, S, TextureFormat.RGBA32, true);
                var px = new Color[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float n = Noise.Fbm(x * 0.05f, y * 0.05f, 5) * 0.5f + 0.5f;
                        float g = Mathf.Clamp01(0.55f + n * 0.55f);
                        px[y * S + x] = new Color(g, g, g, 1f);
                    }
                noiseTex.SetPixels(px);
                noiseTex.Apply();
                noiseTex.wrapMode = TextureWrapMode.Repeat;
                return noiseTex;
            }
        }

        static Material Make(string name, Color color, float smoothness, float metallic, Texture2D tex = null, Vector2 tiling = default(Vector2), Texture2D normal = null)
        {
            var m = new Material(Standard) { name = name };
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (tex != null && m.HasProperty("_MainTex"))
            {
                m.SetTexture("_MainTex", tex);
                m.SetTextureScale("_MainTex", tiling == default(Vector2) ? Vector2.one * 4f : tiling);
            }
            if (normal != null && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", normal);
                m.EnableKeyword("_NORMALMAP");
            }
            return m;
        }

        public static Material Ground(Color tint)
        {
            if (groundTex == null) groundTex = NoiseTex;
            return Make("Ground", tint, 0.08f, 0f, groundTex, new Vector2(60f, 60f));
        }

        public static Material Metal(Color tint, float smoothness = 0.35f)
        {
            if (metalTex == null) metalTex = NoiseTex;
            return Make("Metal", tint, smoothness, 0.55f, metalTex, new Vector2(2f, 2f));
        }

        public static Material Flat(Color color, float smoothness = 0.15f, float metallic = 0f)
        {
            return Make("Flat", color, smoothness, metallic);
        }

        public static Material Transparent(Color color, float alpha)
        {
            var m = Make("Transparent", new Color(color.r, color.g, color.b, alpha), 0.2f, 0f);
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
            return m;
        }

        public static Material Unlit(Color color)
        {
            var sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            return new Material(sh) { color = color };
        }

        public static Material Particle()
        {
            var sh = Shader.Find("Particles/Standard Unlit");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", SoftParticle);
            return m;
        }

        /// <summary>Материал частиц с настройкой аддитивного/обычного смешивания.</summary>
        public static Material Particle(Color color, bool additive)
        {
            var m = Particle();
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", color);
            if (additive && m.HasProperty("_Mode"))
            {
                m.SetFloat("_Mode", 2f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_ALPHABLEND_ON");
                m.renderQueue = 3000;
            }
            return m;
        }
    }
}
