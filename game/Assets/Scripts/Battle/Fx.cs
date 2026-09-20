// Боевые эффекты: попадания, взрывы, дым, трассеры. Всё создаётся кодом, без префабов.
using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    public static class Fx
    {
        static Material unlit, smokeMat;
        static Shader particleShader;
        static Transform root;

        static Transform Root()
        {
            if (root == null)
            {
                var go = GameObject.Find("~FX");
                if (go == null) go = new GameObject("~FX");
                root = go.transform;
            }
            return root;
        }

        // Материалы эффектов кэшируются по цвету: на бою в 30 машин каждая вспышка
        // и попадание создавали бы новый Material (нативный объект, не освобождается
        // вместе с частицей) — за 20 минут боя их набегали бы тысячи.
        static readonly Dictionary<Color, Material> matCache = new Dictionary<Color, Material>();

        public static Material UnlitMaterial(Color c)
        {
            Material cached;
            if (matCache.TryGetValue(c, out cached) && cached != null) return cached;
            if (unlit == null)
            {
                var sh = Shader.Find("Particles/Standard Unlit");
                if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Additive");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                unlit = new Material(sh);
            }
            var m = new Material(unlit);
            m.color = c;
            m.SetColor("_TintColor", c);
            matCache[c] = m;
            return m;
        }

        static Material ParticleMat(Color c)
        {
            Material cached;
            if (matCache.TryGetValue(c, out cached) && cached != null) return cached;
            if (particleShader == null)
            {
                particleShader = Shader.Find("Particles/Standard Unlit");
                if (particleShader == null) particleShader = Shader.Find("Legacy Shaders/Particles/Additive");
                if (particleShader == null) particleShader = Shader.Find("Sprites/Default");
            }
            var m = new Material(particleShader);
            m.color = c;
            m.SetColor("_Color", c);
            m.SetColor("_TintColor", c);
            m.mainTexture = SmokeTexture();
            matCache[c] = m;
            return m;
        }

        static Texture2D smokeTex;
        static Texture2D SmokeTexture()
        {
            if (smokeTex != null) return smokeTex;
            const int S = 64;
            smokeTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - S / 2f) / (S / 2f), dy = (y - S / 2f) / (S / 2f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    px[y * S + x] = new Color(1f, 1f, 1f, a);
                }
            smokeTex.SetPixels(px);
            smokeTex.Apply();
            return smokeTex;
        }

        static ParticleSystem NewSystem(string name, Color color, float sizeMin, float sizeMax, float life)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root(), false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startColor = color;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startLifetime = life;
            main.startSpeed = 2f;
            main.gravityModifier = 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var em = ps.emission;
            em.enabled = false;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.2f;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.material = ParticleMat(color);
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        static void Burst(ParticleSystem ps, int count, Vector3 pos, float speed, float life,
                          Color color, float sizeMin, float sizeMax)
        {
            ps.transform.position = pos;
            var main = ps.main;
            main.startColor = color;
            main.startSpeed = speed;
            main.startLifetime = life;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            ps.Emit(count);
            Object.Destroy(ps.gameObject, life + 1.5f);
        }

        // ---- конкретные эффекты ----
        public static void Impact(Vector3 pos, Vector3 normal, bool penetrated)
        {
            var ps = NewSystem("impact", Color.white, 0.1f, 0.25f, 0.5f);
            ps.transform.rotation = Quaternion.LookRotation(normal);
            Burst(ps, penetrated ? 22 : 12, pos,
                  penetrated ? 9f : 5f, 0.55f,
                  penetrated ? new Color(1f, 0.85f, 0.5f) : new Color(1f, 0.6f, 0.3f),
                  penetrated ? 0.08f : 0.12f, penetrated ? 0.22f : 0.35f);
            // пыль
            var dust = NewSystem("dust", Color.white, 0.4f, 0.9f, 1.2f);
            Burst(dust, 10, pos, 2.5f, 1.2f, new Color(0.55f, 0.5f, 0.42f, 0.55f), 0.5f, 1.2f);
        }

        public static void MuzzleFlash(Vector3 pos, Vector3 dir, float caliber)
        {
            var ps = NewSystem("muzzle", Color.white, 0.3f, 0.7f, 0.25f);
            ps.transform.rotation = Quaternion.LookRotation(dir);
            Burst(ps, Mathf.RoundToInt(14 + caliber * 40f), pos, 12f, 0.25f,
                  new Color(1f, 0.9f, 0.6f), 0.3f, 0.9f);

            var lightGo = new GameObject("muzzleLight");
            lightGo.transform.SetParent(Root(), false);
            lightGo.transform.position = pos;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 22f;
            l.intensity = 4f;
            l.color = new Color(1f, 0.85f, 0.6f);
            Object.Destroy(lightGo, 0.08f);

            // дым и пыль из-под ствола
            var dust = NewSystem("gunSmoke", Color.white, 0.4f, 1.0f, 1.6f);
            Burst(dust, 8, pos, 3f, 1.6f, new Color(0.62f, 0.6f, 0.55f, 0.4f), 0.5f, 1.4f);
        }

        public static void Explosion(Vector3 pos, float scale)
        {
            var fire = NewSystem("explosion", Color.white, 1f, 2.5f, 1.1f);
            Burst(fire, 60, pos, 12f * scale, 1.0f, new Color(1f, 0.6f, 0.2f), 0.8f * scale, 2.6f * scale);
            var smoke = NewSystem("smoke", Color.white, 1.5f, 3.5f, 3.5f);
            Burst(smoke, 30, pos, 4f * scale, 3.5f, new Color(0.12f, 0.12f, 0.12f, 0.75f), 1.5f * scale, 4f * scale);

            var lightGo = new GameObject("boomLight");
            lightGo.transform.SetParent(Root(), false);
            lightGo.transform.position = pos;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 40f * scale;
            l.intensity = 8f;
            l.color = new Color(1f, 0.7f, 0.35f);
            Object.Destroy(lightGo, 0.25f);

            var ps = NewSystem("debris", Color.white, 0.1f, 0.4f, 1.4f);
            var debMain = ps.main;
            debMain.gravityModifier = 1.4f;
            Burst(ps, 25, pos, 14f * scale, 1.4f, new Color(0.35f, 0.32f, 0.28f), 0.1f, 0.4f);
        }

        public static void Spawn(Vector3 pos, float scale)
        {
            var ps = NewSystem("spawn", Color.white, 1f, 2f, 1.0f);
            Burst(ps, 30, pos, 8f * scale, 1.0f, new Color(0.6f, 0.85f, 1f, 0.7f), 0.6f * scale, 1.8f * scale);
        }

        public static ParticleSystem MakeSmoke(Vector3 pos, Color color, float life)
        {
            var ps = NewSystem("smokeLoop", color, 1.5f, 3f, life);
            var main = ps.main;
            main.loop = true;
            main.startSpeed = 1.5f;
            main.startLifetime = life;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 12f;
            ps.transform.position = pos;
            ps.Play();
            return ps;
        }

        public static ParticleSystem MakeFire(Vector3 pos, float scale, Transform parent)
        {
            var ps = NewSystem("fire", new Color(1f, 0.55f, 0.15f), 1.0f * scale, 2.2f * scale, 0.9f);
            var main = ps.main;
            main.loop = true;
            main.startSpeed = 2.5f;
            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 25f;
            ps.transform.SetParent(parent, true);
            ps.transform.position = pos;
            ps.Play();
            return ps;
        }

        public static ParticleSystem MakeDust(Vector3 pos, float radius, Transform parent)
        {
            var ps = NewSystem("dustTrail", new Color(0.6f, 0.55f, 0.45f, 0.35f), 1f, 2.5f, 2.2f);
            var main = ps.main;
            main.loop = true;
            main.startSpeed = 1.2f;
            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 8f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(radius, 0.2f, radius);
            if (parent != null) ps.transform.SetParent(parent, true);
            ps.transform.position = pos;
            ps.Play();
            return ps;
        }
    }
}
