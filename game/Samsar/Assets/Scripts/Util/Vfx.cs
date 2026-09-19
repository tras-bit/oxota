using UnityEngine;

namespace Samsar
{
    /// <summary>Простые эффекты боя, создаваемые кодом: трассеры, вспышки, взрывы, дым.</summary>
    public static class Vfx
    {
        static Transform root;
        static Material additive;
        static Material alpha;
        static Material tracer;

        static Transform Root
        {
            get
            {
                if (root == null)
                {
                    var go = GameObject.Find("~vfx");
                    if (go == null) go = new GameObject("~vfx");
                    root = go.transform;
                }
                return root;
            }
        }

        static Material Additive { get { if (additive == null) additive = MatLib.Particle(Color.white, true); return additive; } }
        static Material Alpha { get { if (alpha == null) alpha = MatLib.Particle(new Color(0.75f, 0.72f, 0.68f, 0.55f), false); return alpha; } }
        static Material TracerMat { get { if (tracer == null) tracer = MatLib.Particle(new Color(1f, 0.82f, 0.35f, 0.9f), true); return tracer; } }

        public static ParticleSystem CreateSystem(string name, bool additiveBlend = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = additiveBlend ? Additive : Alpha;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ps.Stop();
            return ps;
        }

        static ParticleSystem Emit(ParticleSystem ps, Vector3 pos, int count)
        {
            ps.transform.position = pos;
            ps.Emit(count);
            return ps;
        }

        static void Defer(ParticleSystem ps, float life)
        {
            var keeper = ps.gameObject.GetComponent<AutoDestroy>();
            if (keeper == null) keeper = ps.gameObject.AddComponent<AutoDestroy>();
            keeper.life = life;
        }

        /// <summary>Трассер снаряда (линия со временем жизни).</summary>
        public static void Tracer(Vector3 from, Vector3 to, float width = 0.07f, float life = 0.055f)
        {
            var go = new GameObject("tracer");
            go.transform.SetParent(Root, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = TracerMat;
            lr.startWidth = width;
            lr.endWidth = width * 0.4f;
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            var k = go.AddComponent<AutoDestroy>();
            k.life = life;
        }

        public static void MuzzleFlash(Vector3 pos, Vector3 dir, float scale = 1f)
        {
            var ps = CreateSystem("muzzle");
            var main = ps.main;
            main.startLifetime = 0.12f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f * scale, 9f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f * scale, 1.4f * scale);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.45f, 1f), new Color(1f, 0.55f, 0.15f, 1f));
            main.maxParticles = 60;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.15f * scale;
            ps.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 0.001f ? dir : Vector3.forward);
            Emit(ps, pos, Mathf.RoundToInt(18 * scale));
            Defer(ps, 1.5f);

            var lightGo = new GameObject("flash");
            lightGo.transform.SetParent(Root, false);
            lightGo.transform.position = pos;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 14f * scale;
            l.intensity = 5f;
            l.color = new Color(1f, 0.85f, 0.6f);
            var k2 = lightGo.AddComponent<AutoDestroy>();
            k2.life = 0.07f;
        }

        public static void Impact(Vector3 pos, Vector3 normal, bool penetrated)
        {
            var ps = CreateSystem("impact");
            var main = ps.main;
            main.startLifetime = 0.6f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.22f);
            main.gravityModifier = 0.9f;
            main.startColor = penetrated
                ? new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.2f, 1f), new Color(1f, 0.3f, 0.1f, 1f))
                : new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.7f, 1f), new Color(1f, 0.8f, 0.3f, 1f));
            var em = ps.emission; em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            ps.transform.rotation = Quaternion.LookRotation(normal.sqrMagnitude > 0.001f ? -normal : Vector3.up);
            Emit(ps, pos, penetrated ? 26 : 14);
            Defer(ps, 2f);

            if (penetrated) Smoke(pos, 3.5f, 0.35f, 1.2f);
        }

        public static void Explosion(Vector3 pos, float scale = 3f)
        {
            var fire = CreateSystem("explosion");
            var main = fire.main;
            main.startLifetime = 0.9f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f * scale, 13f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f * scale, 3.2f * scale);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f, 1f), new Color(1f, 0.35f, 0.08f, 1f));
            main.gravityModifier = -0.15f;
            var em = fire.emission; em.rateOverTime = 0f;
            var shape = fire.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.6f * scale;
            Emit(fire, pos, Mathf.RoundToInt(60 * Mathf.Clamp(scale, 0.6f, 2f)));
            Defer(fire, 3f);

            var dirt = CreateSystem("dirt", false);
            var dm = dirt.main;
            dm.startLifetime = 1.8f;
            dm.startSpeed = new ParticleSystem.MinMaxCurve(3f * scale, 11f * scale);
            dm.startSize = new ParticleSystem.MinMaxCurve(0.4f * scale, 1.3f * scale);
            dm.gravityModifier = 1.1f;
            dm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 0.3f, 0.25f, 0.8f), new Color(0.2f, 0.18f, 0.15f, 0.6f));
            var dem = dirt.emission; dem.rateOverTime = 0f;
            Emit(dirt, pos, Mathf.RoundToInt(40 * Mathf.Clamp(scale, 0.6f, 2f)));
            Defer(dirt, 4f);

            Smoke(pos + Vector3.up * scale * 0.5f, 5f, 1.5f * scale, 2.5f * scale);

            var lg = new GameObject("boomlight");
            lg.transform.SetParent(Root, false);
            lg.transform.position = pos + Vector3.up;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 40f * scale;
            l.intensity = 8f;
            l.color = new Color(1f, 0.7f, 0.4f);
            var k = lg.AddComponent<AutoDestroy>();
            k.life = 0.25f;
        }

        public static void Smoke(Vector3 pos, float seconds, float size = 1f, float speed = 1.5f)
        {
            var ps = CreateSystem("smoke", false);
            var main = ps.main;
            main.startLifetime = seconds;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 0.33f, 0.3f, 0.5f), new Color(0.15f, 0.14f, 0.13f, 0.15f));
            main.gravityModifier = -0.06f;
            var em = ps.emission; em.rateOverTime = 0f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = size;
            Emit(ps, pos, 18);
            Defer(ps, seconds + 2f);
        }

        /// <summary>Постоянный дым (для подбитой техники).</summary>
        public static ParticleSystem AttachSmoke(Transform parent, Vector3 localPos, float size = 1.4f, Color? color = null)
        {
            var ps = CreateSystem("wrecksmoke", false);
            ps.transform.SetParent(parent, false);
            ps.transform.localPosition = localPos;
            var main = ps.main;
            main.startLifetime = 4f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(size, size * 2.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(color ?? new Color(0.12f, 0.11f, 0.1f, 0.75f));
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 14f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.6f;
            ps.Play();
            return ps;
        }

        public static void Dust(Vector3 pos, float scale = 1f)
        {
            var ps = CreateSystem("dust", false);
            var main = ps.main;
            main.startLifetime = 1.1f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f * scale, 2.4f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f * scale, 1.6f * scale);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.42f, 0.36f, 0.35f));
            main.gravityModifier = -0.02f;
            var em = ps.emission; em.rateOverTime = 0f;
            Emit(ps, pos, Mathf.RoundToInt(10 * scale));
            Defer(ps, 3f);
        }

        /// <summary>Дымовой сигнал (для воздушного груза).</summary>
        public static ParticleSystem Flare(Transform parent)
        {
            var ps = CreateSystem("flare", true);
            ps.transform.SetParent(parent, false);
            ps.transform.localPosition = new Vector3(0f, 2f, 0f);
            var main = ps.main;
            main.startLifetime = 3.5f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(1f, 3f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.4f, 1f, 0.5f, 0.9f), new Color(0.1f, 0.7f, 0.3f, 0.2f));
            main.gravityModifier = -0.08f;
            var em = ps.emission; em.rateOverTime = 22f;
            ps.Play();
            return ps;
        }

        public static void DamageNumber(Vector3 worldPos, string text, Color color, float size = 1f)
        {
            FloatingText.Spawn(worldPos, text, color, size);
        }
    }

    /// <summary>Служебный компонент: удаляет объект через N секунд.</summary>
    public class AutoDestroy : MonoBehaviour
    {
        public float life = 1f;
        float t;
        void Update()
        {
            t += Time.deltaTime;
            if (t >= life) Destroy(gameObject);
        }
    }
}
