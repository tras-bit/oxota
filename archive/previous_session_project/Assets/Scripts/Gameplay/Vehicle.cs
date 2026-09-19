using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    /// <summary>Рантайм-характеристики машины (спецификация + уровень + модули + эффекты).</summary>
    public class VStats
    {
        public float MaxHealth = 1800f;
        public float Damage = 320f;
        public float Reload = 6.4f;
        public float Penetration = 175f;
        public float Dispersion = 0.34f;
        public float AimTime = 2.1f;
        public float SpeedForward = 46f;
        public float SpeedReverse = 18f;
        public float TurnRate = 40f;
        public float ArmorFront = 120f;
        public float ArmorSide = 70f;
        public float ArmorRear = 45f;
        public float AbilityCd1 = 70f;
        public float AbilityCd2 = 110f;
        public int AmmoMax = 45;
        public float RepairSpeedMul = 1f;
        public float VisionRange = 420f;
        public float DetectRadius = 260f;
    }

    /// <summary>
    /// Машина: прочность, модули, орудие, уровни I..VII, умения, лут, обнаружение.
    /// Управление движением — в TankController/PlayerController/BotBrain.
    /// </summary>
    public class Vehicle : MonoBehaviour
    {
        public static readonly List<Vehicle> All = new List<Vehicle>();

        public TankSpec Spec;
        public string DisplayName = "Охотник";
        public bool IsPlayer;
        public bool IsBot;
        public bool IsSquadMate;
        public int SquadId;                 // 0..14, у взвода два участника с одинаковым SquadId
        public int Team;                    // 0 — игрок и его союзник, 1 — остальные (каждый сам за себя)
        public BotDifficulty Difficulty = BotDifficulty.Normal;

        public VStats Stats = new VStats();
        public int Level = 1;
        public float Experience;
        public int RespawnCharges;
        public int Ammo;
        public float Health;
        public bool Alive = true;
        public float ShieldUntil;
        public float BoostUntil;
        public float SmokeUntil;
        public float RingUntil;
        public float SpawnProtectedUntil;
        public float DeathTime = -999f;
        public float LastShotTime;
        public float ReloadTimer;
        public float AimBloom;              // накопленный разброс от движения и поворота башни
        public bool TrackBroken, EngineBroken, GunBroken;
        public float TrackRepairAt, EngineRepairAt, GunRepairAt;
        public float TrackSlowFactor = 0.35f;
        public float ModuleDamageTimer;

        public float AbilityCd1Timer, AbilityCd2Timer;
        public bool Ability1Ready { get { return AbilityCd1Timer <= 0f; } }
        public bool Ability2Ready { get { return AbilityCd2Timer <= 0f; } }

        public Vehicle LastAttacker;
        public float LastDamageTime;

        public int Kills, Deaths;
        public float DamageDealt;
        public int Place;
        public bool PendingRespawnPanel;
        public bool DeathHandled;
        public int PendingModuleLevel;      // уровень, на котором игрок ещё не выбрал модуль
        public Transform Turret;
        public Transform BarrelTip;
        public Transform BodyRoot;
        public TankModel Model;

        readonly Dictionary<Vehicle, float> seenBy = new Dictionary<Vehicle, float>();
        public float RevealedUntil;
        public int LastDamageFrame;

        public Vector3 AimPoint { get; set; }
        public float DistanceTravelled;

        /// <summary>Есть ли у машины союзник-взвод (для дележа лута и ремонта).</summary>
        public Vehicle SquadMate;

        public bool IsSpottedBy(float team)
        {
            if (RevealedUntil > Time.time) return true;
            foreach (var kv in seenBy)
            {
                if (kv.Key == null || !kv.Key.Alive) continue;
                if (kv.Key.Team == team && Time.time - kv.Value < GameConfig.SpotFadeTime) return true;
            }
            return false;
        }

        public void MarkSeenBy(Vehicle observer)
        {
            if (observer == null || observer == this) return;
            seenBy[observer] = Time.time;
        }

        public bool VisibleToMe(Vehicle target)
        {
            if (target == null || !target.Alive) return false;
            if (target.RevealedUntil > Time.time) return true;
            float t;
            if (target.seenBy.TryGetValue(this, out t) && Time.time - t < GameConfig.SpotFadeTime) return true;
            if (SquadMate != null && target.seenBy.TryGetValue(SquadMate, out t) && Time.time - t < GameConfig.SpotFadeTime) return true;
            return Vector3.Distance(transform.position, target.transform.position) < GameConfig.AutoDetectRange;
        }

        public static bool AreEnemies(Vehicle a, Vehicle b)
        {
            if (a == b) return false;
            if (a.SquadMate == b) return false;
            return true;    // режим «каждый сам за себя», кроме союзника по взводу
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public void Setup(TankSpec spec, int level = 1)
        {
            Spec = spec;
            Level = Mathf.Clamp(level, 1, GameConfig.MaxLevel);
            RebuildStats();
            Health = Stats.MaxHealth;
            Ammo = Mathf.Min(Spec.AmmoStart, Stats.AmmoMax);
            Alive = true;
            DeathTime = -999f;
        }

        public void RebuildStats()
        {
            var s = Stats;
            s.MaxHealth = Spec.HealthAt(Level);
            s.Damage = Spec.DamageAt(Level);
            s.Reload = Spec.ReloadAt(Level);
            s.Penetration = Spec.PenAt(Level);
            s.Dispersion = Spec.Dispersion;
            s.AimTime = Spec.AimTime;
            s.SpeedForward = Spec.SpeedAt(Level);
            s.SpeedReverse = Spec.SpeedAt(Level) * Spec.ReverseRatio;
            s.TurnRate = Spec.TurnRate;
            s.ArmorFront = Spec.ArmorAt(Level);
            s.ArmorSide = Spec.ArmorSide * Spec.ArmorMul[Mathf.Clamp(Level - 1, 0, 6)];
            s.ArmorRear = Spec.ArmorRear * Spec.ArmorMul[Mathf.Clamp(Level - 1, 0, 6)];
            s.AbilityCd1 = Spec.Ability1Cooldown;
            s.AbilityCd2 = Spec.Ability2Cooldown;
            s.AmmoMax = Spec.AmmoMax;
            s.VisionRange = Spec.ViewRange;
            s.DetectRadius = Spec.DetectRadius;
            // модули, выбранные в этом бою
            for (int lvl = 2; lvl <= Level; lvl++)
            {
                int choice = ChosenModules.ContainsKey(lvl) ? ChosenModules[lvl] : -1;
                if (choice < 0) continue;
                var opts = Spec.ModulesForLevel(lvl);
                if (choice >= opts.Length) continue;
                ApplyModule(opts[choice]);
            }
        }

        public readonly Dictionary<int, int> ChosenModules = new Dictionary<int, int>();

        /// <summary>Применить модуль к характеристикам (одноразово при выборе).</summary>
        public void ApplyModule(ModuleOption m)
        {
            var s = Stats;
            switch (m.Effect)
            {
                case ModuleEffect.Damage: s.Damage *= 1f + m.Value; break;
                case ModuleEffect.Reload: s.Reload *= 1f - m.Value; break;
                case ModuleEffect.Penetration: s.Penetration *= 1f + m.Value; break;
                case ModuleEffect.Accuracy: s.Dispersion *= 1f - m.Value; break;
                case ModuleEffect.AimTime: s.AimTime *= 1f - m.Value; break;
                case ModuleEffect.Health:
                    float ratio = s.MaxHealth > 0f ? Health / s.MaxHealth : 1f;
                    s.MaxHealth *= 1f + m.Value;
                    Health = Mathf.Min(s.MaxHealth, s.MaxHealth * Mathf.Max(ratio, 1f));
                    break;
                case ModuleEffect.Armor:
                    s.ArmorFront *= 1f + m.Value; s.ArmorSide *= 1f + m.Value; s.ArmorRear *= 1f + m.Value; break;
                case ModuleEffect.Speed: s.SpeedForward *= 1f + m.Value; s.SpeedReverse *= 1f + m.Value; break;
                case ModuleEffect.Turn: s.TurnRate *= 1f + m.Value; break;
                case ModuleEffect.Vision: s.VisionRange *= 1f + m.Value; s.DetectRadius *= 1f + m.Value; break;
                case ModuleEffect.AbilityCooldown: s.AbilityCd1 *= 1f - m.Value; s.AbilityCd2 *= 1f - m.Value; break;
                case ModuleEffect.AmmoCap: s.AmmoMax = Mathf.RoundToInt(s.AmmoMax * (1f + m.Value)); break;
                case ModuleEffect.Repair: s.RepairSpeedMul *= 1f + m.Value; break;
            }
        }

        /// <summary>Выбрать модуль на текущем уровне (вызывается из HUD при повышении).</summary>
        public void ChooseModule(int level, int optionIndex)
        {
            ChosenModules[level] = optionIndex;
            var opts = Spec.ModulesForLevel(level);
            if (optionIndex >= 0 && optionIndex < opts.Length) ApplyModule(opts[optionIndex]);
        }

        public void AddExperience(float xp)
        {
            if (!Alive) return;
            Experience += xp;
            while (Level < GameConfig.MaxLevel && Experience >= GameConfig.LevelXp[Level])
            {
                ComponentModuleSelection();
            }
        }

        void ComponentModuleSelection()
        {
            // если игрок не выбрал модуль на прошлом уровне — ставим первый вариант автоматически
            if (IsPlayer && PendingModuleLevel > 1 && PendingModuleLevel < Level)
                ChooseModule(PendingModuleLevel, 0);
            Level++;
            float oldMax = Stats.MaxHealth;
            RebuildStats();
            Health = Mathf.Min(Stats.MaxHealth, Health + (Stats.MaxHealth - oldMax));
            ReloadTimer = Mathf.Min(ReloadTimer, Stats.Reload * 0.5f);   // бонус: первый выстрел после улучшения
            Vfx.Dust(transform.position + Vector3.up * 1.5f, 2.5f);
            AudioSynth.PlayAt("levelup", transform.position, IsPlayer ? 1f : 0.2f);
            if (IsPlayer) Vfx.DamageNumber(transform.position + Vector3.up * 3f, "УРОВЕНЬ " + Roman(Level), new Color(1f, 0.85f, 0.3f), 1.4f);
            if (IsPlayer)
            {
                PendingModuleLevel = Level;
                if (BattleHud.Instance != null) BattleHud.Instance.OnLevelUp(this);
            }
            if (IsBot) PickModuleForBot(Level);
        }

        public static string Roman(int n)
        {
            string[] r = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            return n >= 1 && n <= r.Length ? r[n - 1] : n.ToString();
        }

        void PickModuleForBot(int level)
        {
            var opts = Spec.ModulesForLevel(level);
            int pick = Random.Range(0, opts.Length);
            ChooseModule(level, pick);
        }

        // ==================== бой ====================

        public bool CanShoot
        {
            get
            {
                return Alive && !GunBroken && ReloadTimer <= 0f && Ammo > 0 && Time.time - LastShotTime > 0.05f;
            }
        }

        public float CurrentDispersion
        {
            get
            {
                float baseDisp = Stats.Dispersion + AimBloom;
                return Mathf.Max(0.04f, baseDisp);
            }
        }

        /// <summary>Выстрел в направлении точки.</summary>
        public bool Fire(Vector3 aimPoint)
        {
            if (!CanShoot) return false;
            ReloadTimer = Stats.Reload;
            Ammo--;
            LastShotTime = Time.time;
            AimBloom = Mathf.Min(AimBloom + 0.25f, 1.2f);
            if (IsPlayer) BattleStats().ShotsFired++;

            Vector3 origin = BarrelTip != null ? BarrelTip.position : transform.position + Vector3.up * 1.6f + transform.forward * 3f;
            Vector3 dir = (aimPoint - origin).normalized;
            float dist = Vector3.Distance(origin, aimPoint);
            float spreadMeters = CurrentDispersion * dist / 100f;
            dir += Random.insideUnitSphere * (spreadMeters / Mathf.Max(1f, dist));
            dir.Normalize();

            Vfx.MuzzleFlash(origin, dir, Spec.Class == TankClass.Td || Spec.Class == TankClass.Heavy ? 1.3f : 1f);
            AudioSynth.PlayAt(Spec.Damage > 400f ? "shot_heavy" : "shot", origin, 1f, Random.Range(0.97f, 1.03f));

            Projectile.Spawn(
                origin,
                dir,
                Spec.Class == TankClass.Td ? 320f : Spec.Class == TankClass.Light ? 260f : 285f,
                Stats.Damage,
                Stats.Penetration,
                this);
            return true;
        }

        public PlayerStats BattleStats()
        {
            var g = BattleManager.Instance;
            return g != null ? g.State.Stats : new PlayerStats();
        }

        /// <summary>Попадание снаряда по машине.</summary>
        public void TakeShell(float damage, float penetration, Vector3 point, Vector3 normal, Vector3 shellDir, Vehicle shooter)
        {
            if (!Alive) return;
            if (shooter == this) return;
            if (Time.time < SpawnProtectedUntil) return;

            var battle = BattleManager.Instance;
            bool shield = ShieldUntil > Time.time;
            float angle = Vector3.Angle(normal, -shellDir);
            float distance = Vector3.Distance(shooter != null ? shooter.transform.position : point, point);
            float penLoss = Mathf.Clamp01(distance / GameConfig.MaxPenetrationDistance) * 0.25f;
            float effectivePen = penetration * (1f - penLoss);

            Vector3 local = transform.InverseTransformDirection(normal);
            float armor = Stats.ArmorSide;
            if (Mathf.Abs(local.z) > Mathf.Abs(local.x)) armor = local.z > 0f ? Stats.ArmorFront : Stats.ArmorRear;
            else armor = Stats.ArmorSide;

            // приведённая броня: угол от нормали
            float effectiveArmor = armor / Mathf.Max(0.35f, Mathf.Cos(angle * Mathf.Deg2Rad));

            bool ricochet = angle > GameConfig.RicochetAngle && effectivePen < effectiveArmor * 3f;
            bool penetrated = !ricochet && effectivePen >= effectiveArmor;

            if (shooter != null && shooter.IsPlayer)
            {
                var st = BattleStats();
                st.Hits++;
                if (penetrated) st.Penetrations++; else if (ricochet) st.Ricochets++;
            }

            if (ricochet)
            {
                Vfx.Impact(point, normal, false);
                AudioSynth.PlayAt("ricochet", point, 0.9f, Random.Range(0.95f, 1.1f));
                if (shooter != null && shooter.IsPlayer)
                    Vfx.DamageNumber(point, "РИКОШЕТ", new Color(0.8f, 0.85f, 0.9f), 0.85f);
                return;
            }

            if (!penetrated)
            {
                Vfx.Impact(point, normal, false);
                AudioSynth.PlayAt("hit", point, 0.8f, Random.Range(0.97f, 1.05f));
                if (shooter != null && shooter.IsPlayer)
                    Vfx.DamageNumber(point, "НЕ ПРОБИЛ", new Color(0.75f, 0.75f, 0.7f), 0.85f);
                // сотрясение от непробития
                AimBloom = Mathf.Min(AimBloom + 0.3f, 1.4f);
                return;
            }

            float dealt = damage * Random.Range(0.88f, 1.12f);
            dealt *= ImpactMultiplier(local, point);
            if (shield) dealt *= 0.35f;
            ApplyDamage(dealt, point, shooter, true);
            Vfx.Impact(point, normal, true);
            AudioSynth.PlayAt("penetration", point, 1f, Random.Range(0.95f, 1.05f));

            if (shooter != null && shooter.IsPlayer)
                Vfx.DamageNumber(point, Mathf.RoundToInt(dealt).ToString(), dealt > 400f ? new Color(1f, 0.5f, 0.2f) : new Color(1f, 0.85f, 0.3f), dealt > 400f ? 1.25f : 1f);
            else if (IsPlayer)
                Vfx.DamageNumber(point, "-" + Mathf.RoundToInt(dealt), new Color(1f, 0.3f, 0.25f), 1f);

            // критические повреждения модулей
            float roll = Random.value;
            if (roll < 0.22f) DamageModule(ModuleKind.Track);
            else if (roll < 0.32f) DamageModule(ModuleKind.Engine);
            else if (roll < 0.4f) DamageModule(ModuleKind.Gun);
        }

        float ImpactMultiplier(Vector3 localNormal, Vector3 point)
        {
            // попадание в крышу/корму даёт надбавку
            if (Mathf.Abs(localNormal.y) > 0.7f) return 1.15f;
            if (localNormal.z < -0.5f) return 1.1f;
            return 1f;
        }

        public enum ModuleKind { Track, Engine, Gun }

        public void DamageModule(ModuleKind kind)
        {
            float t = Time.time;
            switch (kind)
            {
                case ModuleKind.Track:
                    if (TrackBroken) return;
                    TrackBroken = true;
                    TrackRepairAt = t + GameConfig.TrackBreakTime / Mathf.Max(0.5f, Stats.RepairSpeedMul);
                    break;
                case ModuleKind.Engine:
                    if (EngineBroken) return;
                    EngineBroken = true;
                    EngineRepairAt = t + GameConfig.EngineRepairTime / Mathf.Max(0.5f, Stats.RepairSpeedMul);
                    break;
                case ModuleKind.Gun:
                    if (GunBroken) return;
                    GunBroken = true;
                    GunRepairAt = t + GameConfig.GunRepairTime / Mathf.Max(0.5f, Stats.RepairSpeedMul);
                    break;
            }
            if (kind != ModuleKind.Gun) TrackSlowFactor = 0.35f;
            Vfx.Smoke(transform.position + Vector3.up * 1.2f, 1.2f, 0.5f, 1f);
            AudioSynth.PlayAt("track", transform.position, IsPlayer ? 1f : 0.3f);
            if (IsPlayer)
                Vfx.DamageNumber(transform.position + Vector3.up * 2.6f, ModuleName(kind) + " повреждён", new Color(1f, 0.6f, 0.2f), 0.9f);
        }

        public static string ModuleName(ModuleKind k)
        {
            switch (k)
            {
                case ModuleKind.Track: return "Гусеница";
                case ModuleKind.Engine: return "Двигатель";
                default: return "Орудие";
            }
        }

        public void RepairAllModules()
        {
            TrackBroken = EngineBroken = GunBroken = false;
            TrackSlowFactor = 1f;
        }

        public void ApplyDamage(float amount, Vector3 point, Vehicle source, bool penetrated = true)
        {
            if (!Alive) return;
            if (ShieldUntil > Time.time) amount *= 0.35f;
            Health -= amount;
            LastDamageFrame = Time.frameCount;
            LastDamageTime = Time.time;
            if (source != null) LastAttacker = source;
            if (source != null)
            {
                source.DamageDealt += Mathf.Max(0f, Mathf.Min(amount, Health + amount));
                if (source.IsPlayer) BattleStats().DamageDealt += amount;
                if (source.IsPlayer) source.AddExperience(amount * GameConfig.XpPerDamage * 0.35f);
                if (IsPlayer) BattleStats().DamageTaken += amount;
            }
            if (Health <= 0f) Die(source);
        }

        public void Die(Vehicle killer)
        {
            if (!Alive) return;
            Alive = false;
            DeathTime = Time.time;
            Health = 0f;
            Deaths++;
            if (killer != null)
            {
                killer.Kills++;
                if (killer.IsPlayer)
                {
                    BattleStats().Kills++;
                    if (IsBot) BattleStats().BotKills++;
                    killer.AddExperience(GameConfig.XpPerKill);
                }
                if (killer.SquadMate != null && killer.SquadMate.IsPlayer) killer.SquadMate.AddExperience(GameConfig.XpPerKill * 0.4f);
            }

            Vfx.Explosion(transform.position + Vector3.up * 1f, Spec.Class == TankClass.Heavy ? 1.4f : 1f);
            AudioSynth.PlayAt("explosion", transform.position, 1f);
            Vfx.AttachSmoke(transform, new Vector3(0f, 1.4f, 0f), Spec.Class == TankClass.Heavy ? 1.8f : 1.2f);

            // обгоревший корпус
            if (Model != null) Model.SetBurnt(true);

            // трофей с техники
            LootSystem.SpawnTrophy(transform.position + Vector3.up * 0.6f, Level, Stats.MaxHealth);

            var battle = BattleManager.Instance;
            if (battle != null) battle.RegisterDeath(this, killer);
        }

        /// <summary>Возрождение из подобранного заряда.</summary>
        public void Respawn(Vector3 position)
        {
            Alive = true;
            Health = Stats.MaxHealth;
            Ammo = Mathf.Max(Ammo, Mathf.RoundToInt(Stats.AmmoMax * 0.5f));
            RepairAllModules();
            ReloadTimer = 0f;
            ShieldUntil = Time.time + 3f;
            SpawnProtectedUntil = Time.time + GameConfig.RespawnInvulnTime;
            seenBy.Clear();
            transform.position = position + Vector3.up * 1.2f;
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            RebuildStats();
            if (Model != null) Model.SetBurnt(false);
            var brain = GetComponent<BotBrain>();
            if (brain != null) brain.enabled = true;
        }

        // ==================== умения ====================

        public float CooldownOf(int slot)
        {
            return slot == 1 ? AbilityCd1Timer : AbilityCd2Timer;
        }

        public AbilityId AbilityOf(int slot)
        {
            return slot == 1 ? Spec.Ability1 : Spec.Ability2;
        }

        public float AbilityCooldownTotal(int slot)
        {
            return slot == 1 ? Stats.AbilityCd1 : Stats.AbilityCd2;
        }

        /// <summary>Применить заряд умения (из лута) — уменьшает кулдаун.</summary>
        public void ChargeAbility(int slot)
        {
            float reduce = AbilityCooldownTotal(slot) * 0.5f;
            if (slot == 1) AbilityCd1Timer = Mathf.Max(0f, AbilityCd1Timer - reduce);
            else AbilityCd2Timer = Mathf.Max(0f, AbilityCd2Timer - reduce);
        }

        public bool UseAbility(int slot, Vector3 targetPoint)
        {
            if (!Alive) return false;
            float cd = slot == 1 ? AbilityCd1Timer : AbilityCd2Timer;
            if (cd > 0f) return false;
            var id = AbilityOf(slot);
            if (id == AbilityId.None) return false;

            bool ok = AbilitySystem.Cast(this, id, targetPoint);
            if (!ok) return false;
            if (slot == 1) AbilityCd1Timer = Stats.AbilityCd1;
            else AbilityCd2Timer = Stats.AbilityCd2;
            return true;
        }

        void Update()
        {
            if (!Alive) return;
            float dt = Time.deltaTime;
            if (ReloadTimer > 0f) ReloadTimer -= dt;
            if (AbilityCd1Timer > 0f) AbilityCd1Timer -= dt;
            if (AbilityCd2Timer > 0f) AbilityCd2Timer -= dt;
            AimBloom = Mathf.Max(0f, AimBloom - dt * 0.55f);

            // авторемонт модулей
            if (TrackBroken && Time.time >= TrackRepairAt)
            {
                TrackBroken = false;
                TrackSlowFactor = 1f;
                if (IsPlayer) Vfx.DamageNumber(transform.position + Vector3.up * 2.4f, "Гусеница восстановлена", new Color(0.6f, 1f, 0.6f), 0.85f);
            }
            if (EngineBroken && Time.time >= EngineRepairAt)
            {
                EngineBroken = false;
                if (IsPlayer) Vfx.DamageNumber(transform.position + Vector3.up * 2.4f, "Двигатель восстановлен", new Color(0.6f, 1f, 0.6f), 0.85f);
            }
            if (GunBroken && Time.time >= GunRepairAt)
            {
                GunBroken = false;
                if (IsPlayer) Vfx.DamageNumber(transform.position + Vector3.up * 2.4f, "Орудие восстановлено", new Color(0.6f, 1f, 0.6f), 0.85f);
            }

            // пассивный опыт (выживание)
            AddExperience(GameConfig.XpPassivePerSecond * dt);
        }

        void OnTriggerEnter(Collider other)
        {
            if (!Alive) return;
            var pickup = other.GetComponent<LootPickup>();
            if (pickup != null && !pickup.Taken)
            {
                pickup.Collect(this);
            }
        }

        public void ReceiveLoot(LootItem item)
        {
            switch (item.Kind)
            {
                case LootKind.Ammo:
                    Ammo = Mathf.Min(Stats.AmmoMax, Ammo + item.Ammo);
                    break;
                case LootKind.Xp:
                    AddExperience(item.Xp);
                    break;
                case LootKind.Repair:
                    Health = Mathf.Min(Stats.MaxHealth, Health + item.RepairHp);
                    RepairAllModules();
                    break;
                case LootKind.Ability:
                    ChargeAbility(1);
                    ChargeAbility(2);
                    break;
                case LootKind.Shield:
                    ShieldUntil = Mathf.Max(ShieldUntil, Time.time + item.ShieldSeconds);
                    break;
                case LootKind.Respawn:
                    RespawnCharges++;
                    break;
            }
            if (IsPlayer && BattleManager.Instance != null)
            {
                BattleManager.Instance.State.Stats.LootTaken++;
                Vfx.DamageNumber(transform.position + Vector3.up * 3f, item.Label, LootTable.ColorOf(item.Kind), 0.95f);
            }
            AudioSynth.PlayAt("pickup", transform.position, IsPlayer ? 0.9f : 0.15f);
        }

        public string LevelRoman { get { return Roman(Level); } }
    }

    public enum BotDifficulty { Easy, Normal, Hard }
}
