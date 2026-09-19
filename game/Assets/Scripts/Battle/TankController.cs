// Машина игрока/бота: движение, уровень прокачки в бою, жизнь и смерть.
using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    /// <summary>Единый вход управления — от игрока или от ИИ.</summary>
    public interface ITankInput
    {
        float Throttle { get; }     // -1..1
        float Steer { get; }        // -1..1
        Vector3 AimPoint { get; }   // точка прицеливания в мире
        bool Fire { get; }
        bool SniperMode { get; }
    }

    [RequireComponent(typeof(Rigidbody))]
    public class TankController : MonoBehaviour
    {
        public TankSpec spec;
        public bool IsPlayer;
        public bool IsAlly;                  // напарник по взводу (ИИ-союзник)
        public string Callsign = "Боец";
        public float bonusSpeed = 1f;

        public TankRig rig;
        public TankArmor armor;
        public TurretController turret;
        public GunController gun;
        public TankAbilities abilities;
        public VisionSystem vision;
        public ITankInput input;

        public float Xp;                     // опыт в бою
        public int Level = 1;
        public int Kills;
        public float DamageDealt;
        public int LootPicked;
        public bool Dead { get; private set; }
        public float HiddenUntil;              // до этого времени машина невидима (дым/маскировка)
        float slowFactor = 1f, slowUntil;
        public bool RespawnAvailable { get; private set; }
        public float SurvivalTime;

        public List<string> Modules = new List<string>();
        System.Random rnd;
        Rigidbody rb;
        Vector3 groundNormal = Vector3.up;
        float stuckTimer;

        public event System.Action<TankController> Died;
        public event System.Action<TankController, int> LeveledUp;

        // ---- инициализация ----
        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rnd = new System.Random(GetInstanceID());
        }

        public void Configure(TankSpec s, bool player, bool ally, string callSign)
        {
            spec = s;
            IsPlayer = player;
            IsAlly = ally;
            Callsign = callSign;
            Level = 1; Xp = 0f;

            // сначала компоненты, потом сборка модели (TankRig ставит коллайдеры зон)
            armor = gameObject.AddComponent<TankArmor>();
            armor.Init(this, spec);

            turret = gameObject.AddComponent<TurretController>();
            turret.Init(this);

            gun = gameObject.AddComponent<GunController>();
            gun.Init(this, spec);

            abilities = gameObject.AddComponent<TankAbilities>();
            abilities.Init(this);

            vision = GetComponent<VisionSystem>();
            if (vision == null) vision = gameObject.AddComponent<VisionSystem>();
            vision.Init(this, spec);

            // модель: деление на корпус/башню/орудие + коллайдеры зон попадания
            rig = GetComponent<TankRig>();
            if (rig == null) rig = gameObject.AddComponent<TankRig>();
            rig.Build(spec, this);

            Sfx.AttachEngine(this);

            if (GetComponent<TankOutsideZone>() == null) gameObject.AddComponent<TankOutsideZone>();

            if (rb == null) rb = GetComponent<Rigidbody>();
            rb.mass = 30000f;
            rb.drag = 0.4f;
            rb.angularDrag = 4f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.centerOfMass = new Vector3(0f, -0.4f, 0f);   // устойчивость на склонах
        }

        /// <summary>Замедление/оглушение от умений и критов.</summary>
        public void ApplySlow(float factor, float duration)
        {
            slowFactor = Mathf.Min(slowFactor, Mathf.Clamp(factor, 0.05f, 1f));
            slowUntil = Mathf.Max(slowUntil, Time.time + duration);
        }

        public bool IsHidden => Time.time < HiddenUntil;

        public void AddRespawnToken()
        {
            RespawnAvailable = true;
            if (IsPlayer) { HUD.Toast("Получено «Возрождение»! Сможешь вернуться в бой", HUD.ToastKind.Good); Sfx.Loot(); }
        }

        // ---- цикл ----
        void Update()
        {
            if (Dead) return;
            SurvivalTime += Time.deltaTime;

            if (input != null)
            {
                turret.AimAt(input.AimPoint, input.SniperMode);
                if (input.Fire) gun.TryFire(input.AimPoint);
            }
            AlignToGround();
            Sfx.UpdateEngine(this, 0f);
        }

        void FixedUpdate()
        {
            if (Dead || input == null) return;

            if (Time.time > slowUntil) slowFactor = Mathf.MoveTowards(slowFactor, 1f, Time.fixedDeltaTime * 0.5f);
            float speedMult = (armor != null ? armor.SpeedFactor : 1f) * slowFactor;
            float target = input.Throttle >= 0f ? input.Throttle * spec.maxSpeed
                                                : input.Throttle * spec.reverseSpeed;
            target *= speedMult * bonusSpeed;

            Vector3 fwd = transform.forward;
            Vector3 vel = rb.velocity;
            float fwdSpeed = Vector3.Dot(vel, fwd);
            float accel = spec.accel * (1f + Level * 0.03f);

            // тяга/торможение по продольной оси
            Vector3 force = fwd * Mathf.Clamp(target - fwdSpeed, -1f, 1f) * accel * rb.mass * 0.25f;
            rb.AddForce(force, ForceMode.Force);

            // поворот корпуса
            float steer = input.Steer * spec.turnRate * (armor != null ? armor.RotationFactor : 1f);
            float speedFactor = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(Mathf.Abs(fwdSpeed) / spec.maxSpeed));
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, steer * speedFactor * Time.fixedDeltaTime, 0f));

            // боковое скольжение гасим (гусеницы «держат»)
            Vector3 side = Vector3.Project(vel, transform.right);
            rb.AddForce(-side * rb.mass * 2.2f, ForceMode.Force);

            // антизастревание
            if (Mathf.Abs(input.Throttle) > 0.4f && Mathf.Abs(fwdSpeed) < 0.4f)
            {
                stuckTimer += Time.fixedDeltaTime;
                if (stuckTimer > 1.4f)
                {
                    rb.AddForce(Vector3.up * 4f + Vector3.ProjectOnPlane(transform.forward, Vector3.up) * 3f,
                                ForceMode.VelocityChange);
                    stuckTimer = 0f;
                }
            }
            else stuckTimer = 0f;

            if (rig != null) rig.Drive(fwdSpeed, input.Steer, input.Throttle);
        }

        void AlignToGround()
        {
            RaycastHit hit;
            Vector3 origin = transform.position + Vector3.up * 1.5f;
            if (Physics.Raycast(origin, Vector3.down, out hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                groundNormal = Vector3.Slerp(groundNormal, hit.normal, Time.deltaTime * 4f);
                Quaternion align = Quaternion.FromToRotation(Vector3.up, groundNormal);
                rb.rotation = Quaternion.Slerp(rb.rotation, align * Quaternion.Euler(0f, rb.rotation.eulerAngles.y, 0f),
                                               Time.deltaTime * 3f);
                rb.position = new Vector3(rb.position.x,
                    Mathf.Lerp(rb.position.y, hit.point.y + 0.05f, Time.deltaTime * 8f), rb.position.z);
            }
        }

        // ---- опыт и уровни ----
        public void AddXp(float amount)
        {
            if (Dead || amount <= 0f) return;
            Xp += amount;
            int lvl = Samsar.Progression.LevelForXp(Xp);
            if (lvl > Level)
            {
                Level = lvl;
                if (LeveledUp != null) LeveledUp(this, Level);
                if (!IsPlayer) AutoPickModule();
                else
                {
                    if (gun != null) gun.OnLevelUp();          // после прокачки орудия перезарядка вдвое быстрее
                    HUD.ShowUpgradeChoice(this, Level);        // выбор одного из двух модулей
                }
            }
        }

        void AutoPickModule()
        {
            var pair = UpgradeModule.RollPair(Level, rnd);
            ApplyModule(pair[rnd.Next(2)].id);
        }

        public void ApplyModule(string id)
        {
            var m = UpgradeModule.Get(id);
            Modules.Add(id);
            switch (id)
            {
                case "gun_damage": gun.damageMult *= 1.12f; break;
                case "gun_reload": gun.reloadMult *= 0.85f; break;
                case "gun_accuracy": gun.dispersionMult *= 0.75f; gun.aimTimeMult *= 0.75f; break;
                case "engine": bonusSpeed *= 1.10f; break;
                case "view": vision.rangeMult *= 1.20f; break;
                case "ammo": gun.AddAmmo(Mathf.CeilToInt(spec.shells * 0.25f) + 2); break;
                case "cooldown": abilities.CooldownMult *= 0.80f; break;
                case "repair": armor.RepairModules(); armor.Heal(armor.HullMax * 0.2f); break;
                default: armor.ApplyModuleBonus(id); break;
            }
            if (IsPlayer) HUD.Toast("Модуль: " + m.title, HUD.ToastKind.Good);
        }

        public string ModuleTitles()
        {
            var list = new List<string>();
            foreach (var id in Modules) list.Add(UpgradeModule.Get(id).title);
            return string.Join(", ", list);
        }

        // ---- урон ----
        public void ReceiveHit(PenResult res, TankController attacker)
        {
            if (Dead) return;
            if (IsPlayer)
            {
                HUD.DamagePopup(transform.position + Vector3.up * 2.4f, Mathf.RoundToInt(res.damage),
                                res.penetrated ? HUD.HitKind.Penetration : HUD.HitKind.Bounce);
                HUD.Toast(res.message, res.penetrated ? HUD.ToastKind.Bad : HUD.ToastKind.Info);
            }
            if (armor.HullHp <= 0f) Die(attacker);
        }

        public void ApplyRawDamage(float dmg, TankController attacker)
        {
            if (Dead) return;
            armor.DamageHull(dmg);
            if (armor.HullHp <= 0f) Die(attacker);
        }

        public void Die(TankController killer)
        {
            if (Dead) return;
            Dead = true;
            rb.velocity = Vector3.zero;
            rig.SetBurning(true);
            Fx.Explosion(transform.position + Vector3.up * 0.8f, 1.6f);
            Sfx.Explosion(transform.position, 1f);
            if (Died != null) Died(this);

            if (killer != null && killer != this)
            {
                killer.Kills++;
                killer.AddXp(Rules.XpKill);
                if (killer.IsPlayer) HUD.Toast("Уничтожен: " + Callsign, HUD.ToastKind.Good);
            }
            if (BattleManager.Instance != null && BattleManager.Instance.loot != null)
                BattleManager.Instance.loot.SpawnTrophy(transform.position);
            if (IsPlayer) HUD.Toast("Ваша машина уничтожена", HUD.ToastKind.Bad);
        }

        /// <summary>Возрождение из лута: возвращает машину в бой.</summary>
        public bool Respawn(Vector3 position)
        {
            if (!RespawnAvailable) return false;
            RespawnAvailable = false;
            Dead = false;
            armor.Heal(armor.HullMax * 0.7f);
            armor.RepairModules();
            gun.RefillAmmo();
            rig.SetBurning(false);
            rig.ResetVisuals();
            rb.position = position + Vector3.up * 1f;
            rb.velocity = Vector3.zero;
            transform.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
            Fx.Spawn(position, 1.2f);
            return true;
        }

        public string StatsLine()
        {
            return string.Format("{0}: урон {1}, фраги {2}, уровень {3}, добыча {4}",
                Callsign, Mathf.RoundToInt(DamageDealt), Kills, Level, LootPicked);
        }
    }
}
