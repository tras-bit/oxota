// Боевые умения (2 на машину, первое — авиаудар): авиаудар, дым, ремонт, огненное кольцо, маскировка.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    public class TankAbilities : MonoBehaviour
    {
        public class Ability
        {
            public string id, title, desc;
            public float cooldown, cdLeft;
            public int charges = 1, maxCharges = 2;
            public bool Ready => cdLeft <= 0f && charges > 0;
            public float Progress => cooldown <= 0f ? 1f : Mathf.Clamp01(1f - cdLeft / cooldown);
        }

        public TankController tank;
        public float CooldownMult = 1f;
        public readonly List<Ability> abilities = new List<Ability>();

        public void Init(TankController t)
        {
            tank = t;
            abilities.Clear();
            abilities.Add(Describe("air_strike"));
            abilities.Add(Describe(t.spec.ability2));
        }

        public static Ability Describe(string id)
        {
            var a = new Ability { id = id };
            switch (id)
            {
                case "air_strike":
                    a.title = "Авиаудар"; a.cooldown = 75f; a.desc = "Удар авиации по площади с оглушением";
                    break;
                case "smoke":
                    a.title = "Дымовая завеса"; a.cooldown = 45f; a.desc = "Скрывает машину от обнаружения";
                    break;
                case "repair":
                    a.title = "Полевой ремонт"; a.cooldown = 60f; a.desc = "Ремонт модулей и +25% прочности";
                    break;
                case "fire_ring":
                    a.title = "Огненное кольцо"; a.cooldown = 70f; a.desc = "Кольцо огня вокруг машины";
                    break;
                case "camouflage":
                    a.title = "Маскировка"; a.cooldown = 55f; a.desc = "Полная невидимость на 8 секунд";
                    break;
                case "shield":
                    a.title = "Стальная стена"; a.cooldown = 80f; a.desc = "8 секунд урон снижен на 65%";
                    break;
                case "turbo":
                    a.title = "Форсаж"; a.cooldown = 60f; a.desc = "+60% скорости на 8 секунд";
                    break;
                case "radar":
                    a.title = "Разведка"; a.cooldown = 90f; a.desc = "12 секунд видны все противники на карте";
                    break;
                default:
                    a.title = id; a.cooldown = 60f; break;
            }
            return a;
        }

        public void AddCharge(int n)
        {
            foreach (var a in abilities)
                a.charges = Mathf.Min(a.maxCharges, a.charges + n);
        }

        public void RefillCharges()
        {
            foreach (var a in abilities) a.charges = a.maxCharges;
        }

        void Update()
        {
            foreach (var a in abilities)
                if (a.cdLeft > 0f) a.cdLeft -= Time.deltaTime;
        }

        public bool Use(int index, Vector3 aimPoint)
        {
            if (index < 0 || index >= abilities.Count) return false;
            var a = abilities[index];
            if (!a.Ready || tank.Dead) return false;
            a.cdLeft = a.cooldown * CooldownMult;
            a.charges--;
            switch (a.id)
            {
                case "air_strike": StartCoroutine(AirStrike(aimPoint)); break;
                case "smoke": StartCoroutine(Smoke()); break;
                case "repair": DoRepair(); break;
                case "fire_ring": StartCoroutine(FireRing()); break;
                case "camouflage": StartCoroutine(Camouflage()); break;
                case "shield": StartCoroutine(Shield()); break;
                case "turbo": StartCoroutine(Turbo()); break;
                case "radar": StartCoroutine(Radar()); break;
            }
            Sfx.Ability();
            if (tank.IsPlayer) HUD.Toast("Умение: " + a.title, HUD.ToastKind.Info);
            return true;
        }

        // ---- реализация ----
        IEnumerator AirStrike(Vector3 target)
        {
            Vector3 dir = (target - tank.transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude < 1f) dir = tank.transform.forward;
            dir.Normalize();
            HUD.Toast("Авиаудар вызван — удар через 3 секунды", HUD.ToastKind.Bad);
            MinimapWidget.MarkAbility(target);

            yield return new WaitForSeconds(3f);

            for (int i = 0; i < 5; i++)
            {
                Vector3 pos = target + dir * (i * 9f - 18f);
                RaycastHit hit;
                if (Physics.Raycast(pos + Vector3.up * 120f, Vector3.down, out hit, 300f, ~0, QueryTriggerInteraction.Ignore))
                    pos = hit.point;
                Fx.Explosion(pos + Vector3.up * 0.6f, 1.5f);
                ApplyAreaDamage(pos, 7f, 420f, 3f);
                yield return new WaitForSeconds(0.28f);
            }
        }

        void ApplyAreaDamage(Vector3 center, float radius, float damage, float stunTime)
        {
            foreach (var t in TankRegistry.Alive())
            {
                if (t == tank) continue;
                if (t.IsPlayer == tank.IsPlayer && !tank.IsPlayer) continue;   // боты не бьют своих этим
                float d = Vector3.Distance(t.transform.position, center);
                if (d > radius) continue;
                float dmg = damage * Mathf.Clamp01(1f - d / radius);
                t.ApplyRawDamage(dmg, tank);
                t.ApplySlow(0.45f, stunTime);
                t.ApplyStun(stunTime);        // ударная волна: экипаж в шоке
                if (tank.IsPlayer) HUD.DamagePopup(t.transform.position + Vector3.up * 2.2f,
                                                   Mathf.RoundToInt(dmg), HUD.HitKind.Penetration);
                if (!tank.IsPlayer) t.AddXp(dmg * Rules.XpDamage * 0.5f);
                if (!t.IsPlayer && tank.IsPlayer) tank.AddXp(dmg * Rules.XpDamage * 0.5f);
                if (t.Dead && tank.IsPlayer) tank.Kills++;
            }
        }

        IEnumerator Smoke()
        {
            var ps = Fx.MakeSmoke(tank.transform.position + Vector3.up * 1.2f, new Color(0.72f, 0.72f, 0.74f), 12f);
            var follow = ps.gameObject.AddComponent<FollowTarget>();
            follow.target = tank.transform;
            follow.offset = new Vector3(0f, 1.2f, 0f);
            tank.HiddenUntil = Time.time + 9f;
            yield return new WaitForSeconds(9f);
            if (ps != null) ps.Stop();
        }

        void DoRepair()
        {
            tank.armor.RepairModules();
            tank.armor.Heal(tank.armor.HullMax * 0.25f);
            Fx.Spawn(tank.transform.position, 1f);
        }

        IEnumerator FireRing()
        {
            var ring = FireRingVisual.Create(tank.transform, 12f, 8f);
            float t = 0f;
            while (t < 8f)
            {
                t += 0.25f;
                foreach (var other in TankRegistry.Alive())
                {
                    if (other == tank) continue;
                    if (other.IsPlayer == tank.IsPlayer && !tank.IsPlayer) continue;
                    if (Vector3.Distance(other.transform.position, tank.transform.position) > 12f) continue;
                    other.ApplyRawDamage(60f * 0.25f, tank);
                    other.ApplySlow(0.55f, 1.2f);
                    if (tank.IsPlayer && other.Dead) tank.Kills++;
                }
                yield return new WaitForSeconds(0.25f);
            }
            if (ring != null) Destroy(ring.gameObject);
        }

        IEnumerator Shield()
        {
            tank.DamageTakenMult = 0.35f;
            HUD.Toast("Стальная стена: урон снижен на 65% (8 с)", HUD.ToastKind.Good);
            // вспышки-искры по корпусу, пока держится броня
            for (int i = 0; i < 4; i++)
            {
                Fx.Spawn(tank.transform.position + Vector3.up * 1f, 1.4f);
                yield return new WaitForSeconds(2f);
            }
            tank.DamageTakenMult = 1f;
        }

        IEnumerator Turbo()
        {
            tank.TurboUntil = Time.time + 8f;
            HUD.Toast("Форсаж: +60% скорости (8 с)", HUD.ToastKind.Good);
            yield return new WaitForSeconds(8f);
        }

        IEnumerator Radar()
        {
            TankController.RadarUntil = Time.time + 12f;
            HUD.Toast("Разведка: противники видны 12 секунд", HUD.ToastKind.Info);
            yield return new WaitForSeconds(12f);
        }

        IEnumerator Camouflage()
        {
            tank.HiddenUntil = Time.time + 8f;
            HUD.Toast("Маскировка активна 8 секунд", HUD.ToastKind.Good);
            yield return new WaitForSeconds(8f);
        }
    }

    /// <summary>Объект, следующий за целью (дым, эффекты).</summary>
    public class FollowTarget : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset;
        void LateUpdate()
        {
            if (target == null) { Destroy(gameObject); return; }
            transform.position = target.position + offset;
        }
    }

    /// <summary>Визуал огненного кольца.</summary>
    public class FireRingVisual : MonoBehaviour
    {
        public static FireRingVisual Create(Transform owner, float radius, float life)
        {
            var go = new GameObject("FireRing");
            var v = go.AddComponent<FireRingVisual>();
            var lr = go.AddComponent<LineRenderer>();
            lr.loop = true;
            lr.widthMultiplier = 0.8f;
            lr.material = Fx.UnlitMaterial(new Color(1f, 0.45f, 0.1f, 0.9f));
            lr.positionCount = 48;
            for (int i = 0; i < lr.positionCount; i++)
            {
                float a = i / (float)lr.positionCount * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0.4f, Mathf.Sin(a) * radius));
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Fx.MakeFire(new Vector3(Mathf.Cos(a) * radius, 0.5f, Mathf.Sin(a) * radius), 0.8f, go.transform);
            }
            return v;
        }
    }
}
