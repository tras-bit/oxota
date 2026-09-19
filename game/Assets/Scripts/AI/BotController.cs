// ИИ-противники: маршруты, поиск добычи, бой, уход в зону. Умные боты работают в парах.
using UnityEngine;
using UnityEngine.AI;

namespace Samsar
{
    [RequireComponent(typeof(TankController))]
    public class BotController : MonoBehaviour, ITankInput
    {
        public TankController tank;
        NavMeshAgent agent;
        TankDifficulty d;
        float thinkTimer, reactionTimer, fireCooldown;
    float lootScanTimer;
        TankController target;
        Vector3 aimPoint, aimError;
        string state = "патруль";
        Vector3 patrolPoint;
        float lootTimer, abilityTimer;
        bool stuck;

        public float Throttle { get; private set; }
        public float Steer { get; private set; }
        public Vector3 AimPoint => aimPoint;
        public bool Fire { get; private set; }
        public bool SniperMode { get; private set; }

        struct TankDifficulty
        {
            public float reaction, aimErrorDeg, engageRange, accuracy, weakspotChance;
            public bool useAbilities, leadTarget, groupTactics;
        }

        public void Init(TankController t, BotDifficulty difficulty)
        {
            tank = t;
            t.input = this;
            d = Params(difficulty);

            agent = gameObject.AddComponent<NavMeshAgent>();
            agent.radius = Mathf.Max(2.2f, t.spec.width * 0.8f);
            agent.height = 2.5f;
            agent.speed = t.spec.maxSpeed;
            agent.acceleration = 8f;
            agent.angularSpeed = 0f;
            agent.updateRotation = false;
            agent.updatePosition = false;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            patrolPoint = t.transform.position + Random.insideUnitSphere * 120f;
            patrolPoint.y = 0f;
            aimPoint = t.transform.position + t.transform.forward * 200f;
        }

        static TankDifficulty Params(BotDifficulty diff)
        {
            switch (diff)
            {
                case BotDifficulty.Easy:
                    return new TankDifficulty { reaction = 1.5f, aimErrorDeg = 3.4f, engageRange = 250f, accuracy = 0.55f,
                                                weakspotChance = 0.05f, useAbilities = false, leadTarget = false, groupTactics = false };
                case BotDifficulty.Hard:
                    return new TankDifficulty { reaction = 0.45f, aimErrorDeg = 0.75f, engageRange = 360f, accuracy = 0.9f,
                                                weakspotChance = 0.55f, useAbilities = true, leadTarget = true, groupTactics = true };
                default:
                    return new TankDifficulty { reaction = 0.85f, aimErrorDeg = 1.7f, engageRange = 320f, accuracy = 0.75f,
                                                weakspotChance = 0.25f, useAbilities = true, leadTarget = true, groupTactics = false };
            }
        }

        void Update()
        {
            if (tank == null || tank.Dead)
            {
                Throttle = 0f; Steer = 0f; Fire = false;
                return;
            }

            thinkTimer -= Time.deltaTime;
            if (thinkTimer <= 0f)
            {
                thinkTimer = 0.35f;
                Think();
            }
            SteerAgent();
            AimAndShoot();
        }

        // ---- принятие решений ----
        void Think()
        {
            // 1) вне зоны — срочно внутрь
            if (ZoneController.Instance != null &&
                !ZoneController.Instance.IsInside(tank.transform.position))
            {
                state = "выход из зоны";
                Vector3 toCenter = ZoneController.Instance.Center - tank.transform.position;
                patrolPoint = tank.transform.position + toCenter.normalized * 120f;
                SetDestination(patrolPoint);
                return;
            }

            // 2) выбор цели
            target = PickTarget();

            if (target != null)
            {
                state = "бой";
                float dist = Vector3.Distance(transform.position, target.transform.position);
                Vector3 dir = (target.transform.position - transform.position).normalized;
                // держим дистанцию: ПТ и ТТ стреляют издалека, ЛТ идёт в сближение
                float want = tank.spec.cls == TankClass.TD ? 260f
                           : tank.spec.cls == TankClass.HT ? 200f
                           : tank.spec.cls == TankClass.MT ? 150f : 90f;
                Vector3 dest = want < dist ? tank.transform.position + dir * (dist - want)
                                           : tank.transform.position - dir * 25f;
                if (d.groupTactics)
                {
                    // обход с фланга
                    Vector3 flank = Vector3.Cross(Vector3.up, dir) * (Random.value > 0.5f ? 40f : -40f);
                    dest += flank;
                }
                SetDestination(dest);

                // умения
                abilityTimer -= 0.35f;
                if (d.useAbilities && abilityTimer <= 0f && tank.abilities != null)
                {
                    abilityTimer = Random.Range(12f, 30f);
                    for (int i = 0; i < tank.abilities.abilities.Count; i++)
                        if (tank.abilities.abilities[i].Ready)
                        {
                            Vector3 ap = target.transform.position + target.transform.forward * 10f;
                            if (tank.abilities.Use(i, ap)) break;
                        }
                }
                return;
            }

            // 3) ремонт
            if (tank.armor.HullHp < tank.armor.HullMax * 0.35f && tank.abilities != null)
            {
                for (int i = 0; i < tank.abilities.abilities.Count; i++)
                {
                    var a = tank.abilities.abilities[i];
                    if (a.id == "repair" && a.Ready) { tank.abilities.Use(i, tank.transform.position); break; }
                }
            }

            // 4) сбор добычи / патруль
            lootTimer -= 0.35f;
            if (lootTimer <= 0f)
            {
                lootTimer = 2.5f;
                var box = FindLoot();
                if (box != null)
                {
                    state = "сбор добычи";
                    SetDestination(box.transform.position);
                    return;
                }
                state = "патруль";
                if (Vector3.Distance(tank.transform.position, patrolPoint) < 15f ||
                    ZoneController.Instance != null && !ZoneController.Instance.IsInside(patrolPoint, ZoneController.Instance.YellowRadius * 0.8f))
                {
                    Vector2 p = Random.insideUnitCircle * (ZoneController.Instance != null ? ZoneController.Instance.RedRadius * 0.6f : 400f);
                    patrolPoint = new Vector3(p.x, 0f, p.y);
                }
                SetDestination(patrolPoint);
            }
        }

        LootBox FindLoot()
        {
            LootBox best = null;
            float bestScore = float.MaxValue;
            var boxes = Object.FindObjectsOfType<LootBox>();
            foreach (var b in boxes)
            {
                if (b.isAirDrop && Random.value > 0.5f) { /* за грузом идут не все */ }
                float dist = Vector3.Distance(transform.position, b.transform.position);
                if (dist > 500f) continue;
                float score = dist - (b.isAirDrop ? 350f : 0f) - (b.kind == LootKind.Repair && tank.armor.HullHp < tank.armor.HullMax * 0.5f ? 200f : 0f)
                              - (b.kind == LootKind.Shells && tank.gun.Ammo < 5 ? 250f : 0f);
                if (score < bestScore) { bestScore = score; best = b; }
            }
            return best;
        }

        TankController PickTarget()
        {
            TankController best = null;
            float bestScore = float.MaxValue;
            var visible = tank.vision.VisibleTargets();
            foreach (var t in visible)
            {
                if (t.Dead) continue;
                float dist = Vector3.Distance(transform.position, t.transform.position);
                if (dist > d.engageRange) continue;
                // приоритет: подбитые, напарник игрока, близкие
                float score = dist + (t.armor.HullHp / Mathf.Max(1f, t.armor.HullMax)) * 120f;
                if (t.IsPlayer) score -= 40f;
                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        void SetDestination(Vector3 point)
        {
            if (agent != null && agent.isOnNavMesh)
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(point, out hit, 25f, NavMesh.AllAreas))
                    agent.SetDestination(hit.position);
            }
            else patrolTarget = point;
        }

        Vector3 patrolTarget;

        // ---- управление ----
        void SteerAgent()
        {
            Vector3 dir;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.nextPosition = transform.position;
                dir = agent.desiredVelocity;
                if (dir.sqrMagnitude < 0.05f) dir = agent.steeringTarget - transform.position;
            }
            else
            {
                dir = patrolTarget - transform.position;
            }
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.3f) { Throttle = 0f; Steer = 0f; return; }

            Vector3 local = transform.InverseTransformDirection(dir.normalized);
            float desiredThrottle = state == "бой" ? 0.55f : 1f;
            Throttle = Mathf.Clamp(local.z * 1.6f, -1f, 1f) * desiredThrottle;
            Steer = Mathf.Clamp(local.x * 2.2f, -1f, 1f);

            stuck = Mathf.Abs(Throttle) > 0.5f && tank.gameObject.GetComponent<Rigidbody>().velocity.magnitude < 0.5f;
            if (stuck) Steer = Random.value > 0.5f ? 1f : -1f;
        }

        void AimAndShoot()
        {
            Fire = false;
            SniperMode = target != null && Vector3.Distance(transform.position, target.transform.position) > 220f;

            if (target != null && !target.Dead)
            {
                // точка прицеливания: корпус / гусеницы / МТО (сложные боты целятся в уязвимые зоны)
                Vector3 aimBase = target.transform.position + Vector3.up * 1.6f;
                if (Random.value < d.weakspotChance)
                {
                    float r = Random.value;
                    if (r < 0.4f) aimBase = target.transform.position + Vector3.up * 0.7f;                    // ходовая
                    else if (r < 0.7f) aimBase = target.transform.position - target.transform.forward * 3f + Vector3.up * 1.2f; // МТО
                }
                if (d.leadTarget && target.input != null)
                {
                    var rb = target.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        float flight = Vector3.Distance(transform.position, target.transform.position) / tank.spec.shellSpeed;
                        aimBase += rb.velocity * flight * (0.8f + Random.value * 0.4f);
                    }
                }
                // ошибка наведения по сложности
                aimError = Vector3.Lerp(aimError,
                    new Vector3(Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f), Random.Range(-1f, 1f)) * d.aimErrorDeg,
                    Time.deltaTime * 2f);
                aimPoint = aimBase + aimError * 1.6f;

                bool onTarget = tank.turret.OnTarget(aimPoint, 2.5f) && tank.turret.OnTarget(aimBase, 4.5f);
                if (onTarget)
                {
                    reactionTimer += Time.deltaTime * 1f;
                    if (reactionTimer > d.reaction)
                    {
                        fireCooldown -= Time.deltaTime;
                        if (fireCooldown <= 0f && tank.gun.Ready)
                        {
                            Fire = true;
                            fireCooldown = tank.gun.ReloadTime * (1.1f - d.accuracy * 0.3f) + Random.Range(0.05f, 0.5f);
                            reactionTimer = 0f;
                        }
                    }
                }
                else reactionTimer = Mathf.Max(0f, reactionTimer - Time.deltaTime * 0.5f);
            }
            else
            {
                // спокойно смотрит по направлению движения
                aimPoint = transform.position + transform.forward * 300f + Vector3.up * 20f;
                fireCooldown = 0f;
            }
        }
    }

    /// <summary>Парная тактика: боты-«напарники» держатся вместе и делят цели.</summary>
    public class BotSquad : MonoBehaviour
    {
        public static void Pair(TankController a, TankController b)
        {
            if (a == null || b == null) return;
            var marker = a.gameObject.AddComponent<BotSquad>();
            marker.partner = b;
        }

        public TankController partner;
        float timer;

        void Update()
        {
            if (partner == null || partner.Dead) return;
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = 3f;
            float dist = Vector3.Distance(transform.position, partner.transform.position);
            if (dist > 120f)
            {
                var agent = GetComponent<NavMeshAgent>();
                if (agent != null && agent.isOnNavMesh)
                {
                    Vector3 mid = (transform.position + partner.transform.position) * 0.5f;
                    agent.SetDestination(mid);
                }
            }
        }
    }
}
