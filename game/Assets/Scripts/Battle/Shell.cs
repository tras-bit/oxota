// Снаряд: летит время, падает по баллистике, пробивает броню по углу попадания.
using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    public class Shell : MonoBehaviour
    {
        static readonly Queue<Shell> pool = new Queue<Shell>();

        public TankController owner;
        float damage, penetration, speed;
        Vector3 velocity;
        float life = 8f;
        TrailRenderer trail;
        bool active;

        public static Shell Fire(TankController owner, Vector3 origin, Vector3 dir,
                                 float damage, float penetration, float speed, float flightDistance)
        {
            Shell s = pool.Count > 0 ? pool.Dequeue() : Create();
            s.gameObject.SetActive(true);
            s.transform.position = origin;
            s.transform.rotation = Quaternion.LookRotation(dir);
            s.owner = owner;
            s.damage = damage;
            s.penetration = penetration;
            s.speed = speed;
            // предварительный подъём ствола на дистанцию (упрощённая баллистика)
            float drop = 0.5f * (Physics.gravity.y * 0.5f) * Mathf.Pow(flightDistance / speed, 2f);
            Vector3 aim = dir;
            if (flightDistance > 40f)
                aim = (dir + Vector3.up * (-drop / Mathf.Max(30f, flightDistance)) * 1.4f).normalized;
            s.velocity = aim * speed;
            s.life = 8f;
            s.active = true;
            if (s.trail != null) { s.trail.Clear(); s.trail.emitting = true; }
            return s;
        }

        static Shell Create()
        {
            var go = new GameObject("Shell");
            var s = go.AddComponent<Shell>();
            s.trail = go.AddComponent<TrailRenderer>();
            s.trail.time = 0.35f;
            s.trail.startWidth = 0.22f;
            s.trail.endWidth = 0.03f;
            s.trail.material = Fx.UnlitMaterial(new Color(1f, 0.85f, 0.45f, 0.85f));
            s.trail.numCapVertices = 2;
            return s;
        }

        void Update()
        {
            if (!active) return;

            life -= Time.deltaTime;
            if (life <= 0f) { Deactivate(); return; }

            velocity += Physics.gravity * 0.5f * Time.deltaTime;   // часть гравитации — играбельно
            Vector3 from = transform.position;
            Vector3 step = velocity * Time.deltaTime;
            Vector3 to = from + step;

            // не засчитываем попадание в собственную машину (ствол, корпус) — пропускаем её коллайдеры
            Vector3 dir = step.normalized;
            float remaining = step.magnitude + 0.3f;
            Vector3 origin = from;
            bool resolved = false;
            for (int i = 0; i < 4 && remaining > 0.01f; i++)
            {
                RaycastHit hit;
                if (!Physics.Raycast(origin, dir, out hit, remaining, ~0, QueryTriggerInteraction.Ignore))
                    break;
                var ownTank = hit.collider.GetComponentInParent<TankController>();
                if (ownTank != null && ownTank == owner)
                {
                    float advance = hit.distance + 0.05f;
                    remaining -= advance;
                    origin += dir * advance;
                    continue;
                }
                Resolve(hit);
                resolved = true;
                break;
            }
            if (resolved) return;
            transform.position = to;
            transform.rotation = Quaternion.LookRotation(velocity);
        }

        void Resolve(RaycastHit hit)
        {
            var zone = hit.collider.GetComponent<HitZone>();
            var armor = hit.collider.GetComponentInParent<TankArmor>();
            Vector3 dir = velocity.normalized;

            if (zone != null && armor != null && armor.tank != owner)
            {
                float dist = Vector3.Distance(owner != null ? owner.transform.position : transform.position,
                                              hit.point);
                var res = armor.TakeHit(zone.type, dir, hit.normal, penetration, damage, dist);

                if (owner != null && owner.IsPlayer)
                {
                    HUD.ShowHitMarker(res.penetrated, res.critical);
                    if (res.damage >= 1f)
                        HUD.DamagePopup(hit.point, Mathf.RoundToInt(res.damage),
                                        res.penetrated ? HUD.HitKind.Penetration : HUD.HitKind.Bounce);
                }
                Sfx.Impact(hit.point, res.penetrated);
                if (res.penetrated)
                {
                    if (owner != null) owner.DamageDealt += res.damage;
                    if (owner != null && owner.IsPlayer)
                        owner.AddXp(res.damage * Rules.XpDamage);
                    Fx.Impact(hit.point, hit.normal, true);
                }
                else
                {
                    Fx.Impact(hit.point, hit.normal, false);
                }
                armor.tank.ReceiveHit(res, owner);
            }
            else
            {
                // попадание в объект окружения — разрушаем мелкие
                var prop = hit.collider.GetComponentInParent<DestructibleProp>();
                if (prop != null) prop.TakeHit(damage * 0.4f, hit.point);
                Fx.Impact(hit.point, hit.normal, false);
                Sfx.Impact(hit.point, false);
            }
            Deactivate();
        }

        void Deactivate()
        {
            active = false;
            if (trail != null) trail.emitting = false;
            transform.position = new Vector3(0f, -500f, 0f);
            pool.Enqueue(this);
            gameObject.SetActive(false);
        }
    }
}
