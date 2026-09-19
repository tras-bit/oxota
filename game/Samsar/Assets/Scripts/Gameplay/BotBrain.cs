using UnityEngine;

namespace Samsar
{
    /// <summary>
    /// ИИ-мародёр: едет по маршруту, собирает добычу, воюет, уходит из зоны, работает в группах.
    /// Сложность влияет на точность, реакцию и частоту использования умений.
    /// </summary>
    public class BotBrain : MonoBehaviour
    {
        public Vehicle Vehicle;
        public TankController Controller;
        public TurretAim Turret;
        public BotDifficulty Difficulty = BotDifficulty.Normal;

        enum Mode { Roam, Loot, Combat, FleeZone, Rescue }
        Mode mode = Mode.Roam;

        Vector3 moveTarget;
        float retargetTimer;
        Vehicle target;
        float targetLostTime;
        float reactionTimer;
        float aimErrorRadius;
        float wanderAngle;
        float stuckTimer;
        Vector3 lastPos;
        float avoidTimer;
        float avoidDir;
        float abilityCheckTimer;
        float lootScanTimer;
        Vector3 aimJitter;

        float AccuracyMul { get { return Difficulty == BotDifficulty.Hard ? 1.35f : Difficulty == BotDifficulty.Normal ? 1f : 0.62f; } }
        float ReactionTime { get { return Difficulty == BotDifficulty.Hard ? 0.35f : Difficulty == BotDifficulty.Normal ? 0.7f : 1.3f; } }
        float AbilityChance { get { return Difficulty == BotDifficulty.Hard ? 0.9f : Difficulty == BotDifficulty.Normal ? 0.6f : 0.25f; } }

        void Awake()
        {
            if (Vehicle == null) Vehicle = GetComponent<Vehicle>();
            if (Controller == null) Controller = GetComponent<TankController>();
            if (Turret == null) Turret = GetComponent<TurretAim>();
            lastPos = transform.position;
            wanderAngle = Random.value * 360f;
        }

        void Update()
        {
            if (Vehicle == null || !Vehicle.Alive) return;
            float dt = Time.deltaTime;
            var battle = BattleManager.Instance;
            if (battle == null) return;
            if (battle.State.State != BattleState.Running)
            {
                if (Controller != null) { Controller.MoveInput = Vector2.zero; Controller.Brake = true; }
                return;
            }

            retargetTimer -= dt;
            abilityCheckTimer -= dt;
            avoidTimer -= dt;
            reactionTimer -= dt;

            // ==== застряли? ====
            if ((transform.position - lastPos).sqrMagnitude < 0.05f) stuckTimer += dt; else stuckTimer = 0f;
            lastPos = transform.position;
            if (stuckTimer > 1.5f)
            {
                avoidDir = Random.value < 0.5f ? -1f : 1f;
                avoidTimer = 1.6f;
                stuckTimer = 0f;
            }

            Vector2 zoneCenter = battle.State.ZoneCenter;
            float zoneRadius = battle.State.ZoneRadius;
            float distToCenter = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), zoneCenter);

            // ==== выбор режима ====
            bool outsideZone = distToCenter > zoneRadius - 70f;
            if (outsideZone) mode = Mode.FleeZone;
            else
            {
                var enemy = FindTarget();
                if (enemy != null) mode = Mode.Combat;
                else
                {
                    var crate = LootSystem.NearestCrate(transform.position, 320f, zoneCenter, zoneRadius);
                    if (crate != null) { mode = Mode.Loot; moveTarget = crate.transform.position; }
                    else if (mode == Mode.FleeZone || mode == Mode.Combat) mode = Mode.Roam;
                }
            }

            // ==== действие по режиму ====
            switch (mode)
            {
                case Mode.FleeZone:
                    moveTarget = new Vector3(zoneCenter.x, transform.position.y, zoneCenter.y);
                    SetDrive(moveTarget, 1f);
                    if (Vector2.Distance(new Vector2(transform.position.x, transform.position.z), zoneCenter) > zoneRadius - 220f && UseAbilitySafe(2))
                    {
                    }
                    break;

                case Mode.Loot:
                    SetDrive(moveTarget, 1f);
                    if (retargetTimer <= 0f) retargetTimer = 1.2f;
                    break;

                case Mode.Combat:
                    CombatUpdate(dt);
                    break;

                default:
                    if (retargetTimer <= 0f)
                    {
                        retargetTimer = Random.Range(3f, 7f);
                        Vector2 c = zoneCenter;
                        float r = Random.Range(120f, Mathf.Max(150f, zoneRadius - 150f));
                        float a = Random.value * Mathf.PI * 2f;
                        moveTarget = new Vector3(c.x + Mathf.Cos(a) * r, transform.position.y, c.y + Mathf.Sin(a) * r);
                    }
                    SetDrive(moveTarget, 0.75f);
                    break;
            }

            // ==== лут под носом — подбираем всегда (проверка не каждый кадр) ====
            lootScanTimer -= dt;
            if (lootScanTimer <= 0f)
            {
                lootScanTimer = 0.4f;
                TryGrabNearbyLoot();
            }

            // ==== зона становится красной — предупреждение ====
            if (battle.State.ZoneWarningPlaying && mode != Mode.Combat && Random.value < dt * 0.4f)
                AudioSynth.PlayAt("zone", transform.position, 0.15f);
        }

        void CombatUpdate(float dt)
        {
            if (target == null || !target.Alive) { mode = Mode.Roam; return; }
            var battle = BattleManager.Instance;
            float dist = Vector3.Distance(transform.position, target.transform.position);

            // держим дистанцию по классу
            float ideal;
            switch (Vehicle.Spec.Class)
            {
                case TankClass.Td: ideal = 420f; break;
                case TankClass.Light: ideal = 190f; break;
                case TankClass.Heavy: ideal = 180f; break;
                default: ideal = 260f; break;
            }
            if (Vehicle.Health < Vehicle.Stats.MaxHealth * 0.35f) ideal *= 1.25f;

            Vector3 toTarget = (target.transform.position - transform.position).normalized;
            Vector3 desiredPoint = target.transform.position - toTarget * ideal;
            SetDrive(desiredPoint, 0.9f);

            // прицеливание с упреждением
            float shellSpeed = Vehicle.Spec.Class == TankClass.Td ? 320f : 280f;
            float flight = dist / shellSpeed;
            Vector3 predicted = target.transform.position + target.transform.forward * 0f;
            var targetRb = target.GetComponent<Rigidbody>();
            if (targetRb != null) predicted += targetRb.velocity * flight;

            // ошибка наведения по сложности
            float spread = (1f / AccuracyMul) * (8f + dist * 0.02f);
            aimJitter = Vector3.Lerp(aimJitter, Random.insideUnitSphere * spread, dt * 3f);
            Vector3 aimPoint = predicted + aimJitter + Vector3.up * 1.2f;

            bool aligned = Turret != null && Turret.AimAt(aimPoint);
            if (reactionTimer <= 0f && aligned && Vehicle.CanShoot && dist < 640f)
            {
                Vehicle.Fire(aimPoint);
                reactionTimer = ReactionTime * Random.Range(0.7f, 1.3f);
            }

            // ==== умения ====
            if (abilityCheckTimer <= 0f)
            {
                abilityCheckTimer = 0.8f;
                if (Random.value < AbilityChance)
                {
                    Vector3 abilityPoint = predicted;
                    float hpRatio = Vehicle.Health / Vehicle.Stats.MaxHealth;
                    // оборонительные
                    if (hpRatio < 0.45f && Vehicle.AbilityOf(2) == AbilityId.Repair) Vehicle.UseAbility(2, abilityPoint);
                    else if (hpRatio < 0.55f && Vehicle.AbilityOf(2) == AbilityId.Shield && dist < 350f) Vehicle.UseAbility(2, abilityPoint);
                    else if (Vehicle.AbilityOf(1) == AbilityId.IncendiaryRing && dist < 14f) Vehicle.UseAbility(1, abilityPoint);
                    else if (Vehicle.AbilityOf(1) == AbilityId.Airstrike && dist > 120f && dist < 500f) Vehicle.UseAbility(1, abilityPoint);
                    else if (Vehicle.AbilityOf(1) == AbilityId.Artillery && dist > 150f && dist < 550f) Vehicle.UseAbility(1, abilityPoint);
                    else if (Vehicle.AbilityOf(1) == AbilityId.ReconDrone && !Vehicle.VisibleToMe(target)) Vehicle.UseAbility(1, abilityPoint);
                    else if (Vehicle.AbilityOf(1) == AbilityId.SmokeScreen && hpRatio < 0.6f) Vehicle.UseAbility(1, abilityPoint);
                    else if (Vehicle.AbilityOf(2) == AbilityId.Boost && dist > 260f) Vehicle.UseAbility(2, abilityPoint);
                }
            }
        }

        bool UseAbilitySafe(int slot) { return Vehicle.UseAbility(slot, transform.position); }

        Vehicle FindTarget()
        {
            Vehicle best = null;
            float bestScore = float.MinValue;
            var battle = BattleManager.Instance;
            for (int i = 0; i < Vehicle.All.Count; i++)
            {
                var v = Vehicle.All[i];
                if (v == null || !v.Alive || v == Vehicle || v.SquadMate == Vehicle) continue;
                float dist = Vector3.Distance(transform.position, v.transform.position);
                if (dist > Vehicle.Stats.DetectRadius * 1.15f) continue;
                if (!Vehicle.VisibleToMe(v)) continue;
                // не гнаться далеко за зону
                Vector2 flat = new Vector2(v.transform.position.x, v.transform.position.z);
                if (Vector2.Distance(flat, battle.State.ZoneCenter) > battle.State.ZoneRadius + 120f) continue;

                float score = 1000f - dist;
                if (v.Health < v.Stats.MaxHealth * 0.35f) score += 250f;    // добить раненого
                if (v.RevealedUntil > Time.time) score += 60f;
                if (v == target) score += 120f;                             // не метаться
                if (dist < GameConfig.AutoDetectRange * 1.5f) score += 200f;
                if (score > bestScore) { bestScore = score; best = v; }
            }
            if (best != target && best != null) reactionTimer = ReactionTime;
            target = best;
            return best;
        }

        void TryGrabNearbyLoot()
        {
            // заезд на лут: если рядом (< 18 м) — едем к нему
            var crate = LootSystem.NearestCrate(transform.position, 22f, BattleManager.Instance.State.ZoneCenter, BattleManager.Instance.State.ZoneRadius);
            if (crate != null && mode != Mode.FleeZone && mode != Mode.Combat)
            {
                SetDrive(crate.transform.position, 1f);
            }
        }

        void SetDrive(Vector3 worldPoint, float throttle)
        {
            if (Controller == null) return;
            Vector3 to = worldPoint - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 1f) { Controller.MoveInput = new Vector2(0f, 0f); return; }
            to.Normalize();

            // объезд препятствий
            RaycastHit hit;
            Vector3 origin = transform.position + Vector3.up * 1.2f;
            if (avoidTimer <= 0f && Physics.Raycast(origin, to, out hit, 16f, ~0, QueryTriggerInteraction.Ignore))
            {
                var other = hit.collider.GetComponentInParent<Vehicle>();
                if (other == null)
                {
                    avoidDir = Vector3.Dot(Vector3.Cross(transform.forward, to), Vector3.up) > 0f ? -1f : 1f;
                    avoidTimer = 0.8f;
                    if (Random.value < 0.4f) avoidDir *= -1f;
                }
            }

            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            float signedAngle = Vector3.SignedAngle(fwd, to, Vector3.up);
            float steer = Mathf.Clamp(signedAngle / 35f, -1f, 1f);
            if (avoidTimer > 0f) steer = avoidDir;

            // если цель сзади — разворачиваемся на месте
            float forwardness = Vector3.Dot(fwd, to);
            float move = throttle;
            if (forwardness < -0.4f) move = -0.35f;
            else if (Mathf.Abs(signedAngle) > 75f) move = throttle * 0.25f;

            Controller.MoveInput = new Vector2(steer, move);
            Controller.Brake = false;
        }
    }
}
