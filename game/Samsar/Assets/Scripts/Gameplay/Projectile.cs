using UnityEngine;

namespace Samsar
{
    /// <summary>Снаряд: летит время, теряет пробитие на дистанции, пробивает/рикошетит.</summary>
    public class Projectile : MonoBehaviour
    {
        static Transform root;

        public Vehicle Shooter;
        public float Damage;
        public float Penetration;
        public float Speed;
        public float Life = 9f;

        Vector3 velocity;
        float born;
        bool resolved;

        static Transform Root
        {
            get
            {
                if (root == null)
                {
                    var go = GameObject.Find("~projectiles");
                    if (go == null) go = new GameObject("~projectiles");
                    root = go.transform;
                }
                return root;
            }
        }

        public static Projectile Spawn(Vector3 origin, Vector3 dir, float speed, float damage, float penetration, Vehicle shooter)
        {
            var go = new GameObject("shell");
            go.transform.SetParent(Root, false);
            go.transform.position = origin;
            var p = go.AddComponent<Projectile>();
            p.Shooter = shooter;
            p.Damage = damage;
            p.Penetration = penetration;
            p.Speed = speed;
            p.velocity = dir.normalized * speed;
            p.born = Time.time;
            go.transform.rotation = Quaternion.LookRotation(p.velocity);
            var mark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(mark.GetComponent<Collider>());
            mark.transform.SetParent(go.transform, false);
            mark.transform.localScale = Vector3.one * (penetration > 240f ? 0.5f : 0.38f);
            var rend = mark.GetComponent<Renderer>();
            rend.sharedMaterial = MatLib.Flat(new Color(1f, 0.85f, 0.4f), 0.6f, 0.2f);
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return p;
        }

        void Update()
        {
            if (resolved) return;
            float dt = Time.deltaTime;
            if (Time.time - born > Life) { Destroy(gameObject); return; }

            velocity += Vector3.down * GameConfig.ShellGravity * dt * Mathf.Clamp(Speed / 300f, 0.5f, 1.4f);
            Vector3 step = velocity * dt;
            float dist = step.magnitude;
            if (dist <= 0.0001f) return;

            var hits = Physics.RaycastAll(transform.position, step.normalized, dist, ~0, QueryTriggerInteraction.Collide);
            if (hits.Length > 0)
            {
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                for (int i = 0; i < hits.Length; i++)
                {
                    var v = hits[i].collider.GetComponentInParent<Vehicle>();
                    if (v != null && v == Shooter) continue;     // свой корпус/башня — пролетаем
                    Resolve(hits[i]);
                    return;
                }
            }
            transform.position += step;
            transform.rotation = Quaternion.LookRotation(velocity);
        }

        void Resolve(RaycastHit hit)
        {
            resolved = true;
            var victim = hit.collider.GetComponentInParent<Vehicle>();
            var shooterVehicle = Shooter;
            Vector3 dir = velocity.normalized;

            if (victim != null && victim == shooterVehicle)
            {
                // собственный корпус — игнорируем
                Destroy(gameObject);
                return;
            }

            if (victim != null && victim.Alive)
            {
                victim.TakeShell(Damage, Penetration, hit.point, hit.normal, dir, shooterVehicle);
            }
            else
            {
                // препятствие: эффект попадания
                bool destructible = hit.collider.GetComponentInParent<Destructible>() != null;
                if (destructible)
                {
                    var d = hit.collider.GetComponentInParent<Destructible>();
                    d.Hit(Damage, hit.point, dir);
                    Vfx.Explosion(hit.point, 0.6f);
                    AudioSynth.PlayAt("explosion", hit.point, 0.6f);
                }
                else
                {
                    Vfx.Impact(hit.point, hit.normal, false);
                    AudioSynth.PlayAt("hit", hit.point, 0.35f, Random.Range(0.95f, 1.05f));
                    Vfx.Dust(hit.point, 0.7f);
                }
            }
            Destroy(gameObject);
        }
    }
}
