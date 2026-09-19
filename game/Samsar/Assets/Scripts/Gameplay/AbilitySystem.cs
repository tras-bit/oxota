using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    /// <summary>Отложенные действия (для авиаудара, артзалпа и т.п.).</summary>
    public class Scheduler : MonoBehaviour
    {
        static Scheduler instance;
        public static Scheduler Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = GameObject.Find("~scheduler");
                    if (go == null) go = new GameObject("~scheduler");
                    instance = go.AddComponent<Scheduler>();
                }
                return instance;
            }
        }

        public static Coroutine After(float seconds, System.Action action)
        {
            return Instance.StartCoroutine(Run(seconds, action));
        }

        static IEnumerator Run(float seconds, System.Action action)
        {
            yield return new WaitForSeconds(seconds);
            if (action != null) action();
        }
    }

    /// <summary>Реализация боевых умений (у каждой машины их два).</summary>
    public static class AbilitySystem
    {
        class RingFx
        {
            public Vehicle Owner;
            public float Until;
            public ParticleSystem Fx;
        }

        static readonly List<RingFx> rings = new List<RingFx>();

        public static bool Cast(Vehicle caster, AbilityId id, Vector3 point)
        {
            switch (id)
            {
                case AbilityId.Airstrike: return Airstrike(caster, point);
                case AbilityId.Artillery: return Artillery(caster, point);
                case AbilityId.ReconDrone: return ReconDrone(caster, point);
                case AbilityId.SmokeScreen: return SmokeScreen(caster);
                case AbilityId.Boost: return Boost(caster);
                case AbilityId.Repair: return Repair(caster);
                case AbilityId.Shield: return Shield(caster);
                case AbilityId.IncendiaryRing: return IncendiaryRing(caster);
                default: return false;
            }
        }

        static void Announce(Vehicle caster, string text)
        {
            if (caster.IsPlayer)
            {
                Vfx.DamageNumber(caster.transform.position + Vector3.up * 3.4f, text, new Color(1f, 0.8f, 0.35f), 1.1f);
                AudioSynth.PlayUi("click", 0.8f);
            }
        }

        // ==== Авиаудар: метка, через 3 секунды удар по площади ====
        static bool Airstrike(Vehicle caster, Vector3 point)
        {
            Announce(caster, "Авиаудар!");
            Marker(point, 16f, new Color(1f, 0.35f, 0.2f), 3f);
            AudioSynth.PlayAt("aircraft", point, 0.8f);
            var owner = caster;
            Scheduler.After(3f, () =>
            {
                AudioSynth.PlayAt("explosion", point, 1f);
                for (int i = 0; i < 5; i++)
                {
                    Vector3 p = point + new Vector3(Random.Range(-9f, 9f), 0f, Random.Range(-9f, 9f));
                    Vfx.Explosion(p, 2.6f);
                    AreaDamage(owner, p, 13f, 620f, 1f);
                }
            });
            return true;
        }

        // ==== Артзалп: 6 разрывов по площади ====
        static bool Artillery(Vehicle caster, Vector3 point)
        {
            Announce(caster, "Артзалп!");
            Marker(point, 20f, new Color(1f, 0.6f, 0.2f), 1.6f);
            var owner = caster;
            Scheduler.After(1.6f, () =>
            {
                for (int i = 0; i < 6; i++)
                {
                    float delay = i * 0.18f;
                    Vector3 p = point + new Vector3(Random.Range(-10f, 10f), 0f, Random.Range(-10f, 10f));
                    Scheduler.After(delay, () =>
                    {
                        Vfx.Explosion(p, 2.2f);
                        AudioSynth.PlayAt("explosion", p, 0.9f);
                        AreaDamage(owner, p, 11f, 380f, 0.8f);
                    });
                }
            });
            return true;
        }

        // ==== Разведдрон: подсветка вокруг точки ====
        static bool ReconDrone(Vehicle caster, Vector3 point)
        {
            Announce(caster, "Разведдрон");
            int found = VisionSystem.RevealAround(point, 160f, 14f, caster);
            Marker(point, 24f, new Color(0.4f, 0.8f, 1f), 14f);
            var drone = new GameObject("drone");
            drone.transform.position = point + Vector3.up * 30f;
            var ps = drone.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 2f;
            main.startSpeed = 6f;
            main.startSize = 1.6f;
            main.startColor = new Color(0.5f, 0.85f, 1f, 0.7f);
            var em = ps.emission;
            em.rateOverTime = 16f;
            drone.GetComponent<ParticleSystemRenderer>().material = MatLib.Particle(new Color(0.5f, 0.85f, 1f), true);
            ps.Play();
            Object.Destroy(drone, 14f);
            if (caster.IsPlayer) Vfx.DamageNumber(point + Vector3.up * 4f, "Обнаружено: " + found, new Color(0.6f, 0.9f, 1f), 1f);
            return true;
        }

        // ==== Дымовая завеса ====
        static bool SmokeScreen(Vehicle caster)
        {
            Announce(caster, "Дымовая завеса");
            caster.SmokeUntil = Time.time + 14f;
            var fx = new GameObject("smokecloud" + caster.GetInstanceID());
            fx.transform.position = caster.transform.position;
            var ps = fx.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 9f;
            main.startSpeed = 3.5f;
            main.startSize = new ParticleSystem.MinMaxCurve(9f, 18f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.85f, 0.85f, 0.5f), new Color(0.6f, 0.6f, 0.6f, 0.2f));
            main.gravityModifier = -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 26f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 9f;
            fx.GetComponent<ParticleSystemRenderer>().material = MatLib.Particle(new Color(0.85f, 0.85f, 0.85f, 0.6f), false);
            ps.Play();
            Object.Destroy(fx, 16f);
            AudioSynth.PlayAt("explosion", caster.transform.position, 0.35f);
            return true;
        }

        static bool Boost(Vehicle caster)
        {
            Announce(caster, "Форсаж");
            caster.BoostUntil = Time.time + 8f;
            Vfx.Dust(caster.transform.position, 1.6f);
            return true;
        }

        static bool Repair(Vehicle caster)
        {
            Announce(caster, "Полевой ремонт");
            caster.Health = Mathf.Min(caster.Stats.MaxHealth, caster.Health + caster.Stats.MaxHealth * 0.28f);
            caster.RepairAllModules();
            Vfx.Dust(caster.transform.position + Vector3.up, 1.4f);
            Vfx.Smoke(caster.transform.position + Vector3.up * 1.5f, 2f, 1f, 1.5f);
            return true;
        }

        static bool Shield(Vehicle caster)
        {
            Announce(caster, "Экран защиты");
            caster.ShieldUntil = Time.time + 9f;
            var go = new GameObject("shieldfx");
            go.transform.SetParent(caster.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 1.2f;
            main.startSpeed = 1.2f;
            main.startSize = 0.9f;
            main.startColor = new Color(0.4f, 0.7f, 1f, 0.85f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var em = ps.emission;
            em.rateOverTime = 60f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 3.2f;
            go.GetComponent<ParticleSystemRenderer>().material = MatLib.Particle(new Color(0.45f, 0.75f, 1f), true);
            ps.Play();
            Object.Destroy(go, 9f);
            return true;
        }

        // ==== Огненное кольцо: урон и замедление врагов рядом ====
        static bool IncendiaryRing(Vehicle caster)
        {
            Announce(caster, "Огненное кольцо");
            caster.RingUntil = Time.time + 10f;
            var go = new GameObject("ring" + caster.GetInstanceID());
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 1.6f;
            main.startSpeed = 2.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(2.2f, 4.5f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.15f, 1f), new Color(1f, 0.25f, 0.05f, 0.6f));
            main.gravityModifier = -0.12f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 90f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 9f;
            go.GetComponent<ParticleSystemRenderer>().material = MatLib.Particle(new Color(1f, 0.55f, 0.15f), true);
            ps.Play();
            rings.Add(new RingFx { Owner = caster, Until = caster.RingUntil, Fx = ps });
            Scheduler.After(11f, () => { if (ps != null) Object.Destroy(ps.gameObject); });
            return true;
        }

        /// <summary>Обновление постоянных эффектов (кольцо огня).</summary>
        public static void Tick(float dt)
        {
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                if (r.Owner == null || !r.Owner.Alive || Time.time > r.Until)
                {
                    if (r.Fx != null) Object.Destroy(r.Fx.gameObject, 2f);
                    rings.RemoveAt(i);
                    continue;
                }
                if (r.Fx != null) r.Fx.transform.position = r.Owner.transform.position;
                foreach (var v in Vehicle.All)
                {
                    if (v == null || !v.Alive || v == r.Owner || v.SquadMate == r.Owner) continue;
                    if (Vector3.Distance(v.transform.position, r.Owner.transform.position) < 9f)
                    {
                        v.ApplyDamage(70f * dt, v.transform.position, r.Owner);
                        v.TrackSlowFactor = 0.55f;
                    }
                }
            }
        }

        /// <summary>Метка на земле (предупреждение о ударе).</summary>
        public static void Marker(Vector3 point, float radius, Color color, float seconds)
        {
            var go = new GameObject("marker");
            var mb = new MeshBuilder();
            mb.AddRing(Vector3.zero, radius, 0.8f, 48, color);
            go.AddComponent<MeshFilter>().mesh = mb.ToMesh(false);
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = MatLib.Transparent(color, 0.85f);
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            RaycastHit hit;
            float y = 0f;
            if (Physics.Raycast(point + Vector3.up * 60f, Vector3.down, out hit, 200f)) y = hit.point.y;
            go.transform.position = new Vector3(point.x, y + 0.25f, point.z);
            var k = go.AddComponent<AutoDestroy>();
            k.life = seconds;
        }

        /// <summary>Урон по площади: задевает всех, кроме стрелявшего и его союзника.</summary>
        public static void AreaDamage(Vehicle owner, Vector3 point, float radius, float damage, float falloff)
        {
            foreach (var v in Vehicle.All)
            {
                if (v == null || !v.Alive) continue;
                if (owner != null && (v == owner || v.SquadMate == owner)) continue;
                float d = Vector3.Distance(v.transform.position, point);
                if (d > radius) continue;
                float t = 1f - (d / radius) * falloff;
                v.ApplyDamage(damage * Mathf.Clamp01(t), v.transform.position, owner);
                if (v.Model != null && v.Model.HullCollider != null) v.DamageModule(Random.value < 0.4f ? Vehicle.ModuleKind.Track : Vehicle.ModuleKind.Engine);
            }
        }
    }
}
