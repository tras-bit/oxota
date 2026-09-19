using UnityEngine;

namespace Samsar
{
    /// <summary>
    /// Обнаружение противников: круговая зона видимости 360°, автообнаружение в упор (50 м),
    /// линия видимости через препятствия, дымовая завеса скрывает машину.
    /// </summary>
    public static class VisionSystem
    {
        static float timer;

        public static void Tick(float dt)
        {
            timer -= dt;
            if (timer > 0f) return;
            timer = GameConfig.VisionUpdateInterval;

            var all = Vehicle.All;
            for (int i = 0; i < all.Count; i++)
            {
                var observer = all[i];
                if (observer == null || !observer.Alive) continue;
                Vector3 eye = observer.transform.position + Vector3.up * 2.4f;
                float radius = observer.Stats.DetectRadius;

                for (int j = 0; j < all.Count; j++)
                {
                    var target = all[j];
                    if (target == null || target == observer || !target.Alive) continue;
                    if (target.SquadMate == observer) continue;

                    Vector3 tp = target.transform.position;
                    float dist = Vector3.Distance(eye, tp);

                    if (dist < GameConfig.AutoDetectRange)
                    {
                        target.MarkSeenBy(observer);
                        continue;
                    }
                    if (dist > radius) continue;
                    if (target.SmokeUntil > Time.time && dist > GameConfig.AutoDetectRange * 1.2f) continue;
                    if (observer.SmokeUntil > Time.time && dist > 120f) continue;

                    Vector3 targetPoint = tp + Vector3.up * 1.6f;
                    if (HasLineOfSight(eye, targetPoint))
                        target.MarkSeenBy(observer);
                }
            }
        }

        public static bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            RaycastHit hit;
            if (Physics.Linecast(from, to, out hit, ~0, QueryTriggerInteraction.Ignore))
            {
                var v = hit.collider.GetComponentInParent<Vehicle>();
                return v != null;   // препятствие — это сама цель
            }
            return true;
        }

        /// <summary>Подсветить всех противников вокруг точки (дрон, артразведка).</summary>
        public static int RevealAround(Vector3 point, float radius, float seconds, Vehicle owner)
        {
            int count = 0;
            foreach (var v in Vehicle.All)
            {
                if (v == null || !v.Alive) continue;
                if (owner != null && v.SquadMate == owner) continue;
                if (Vector3.Distance(v.transform.position, point) <= radius)
                {
                    v.RevealedUntil = Mathf.Max(v.RevealedUntil, Time.time + seconds);
                    count++;
                }
            }
            return count;
        }
    }
}
