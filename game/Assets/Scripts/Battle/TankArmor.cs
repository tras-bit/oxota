// Зоны попадания и живучесть машины: корпус, башня, гусеницы, двигатель, орудие.
using UnityEngine;

namespace Samsar
{
    /// <summary>Маркер зоны: вешается на коллайдер-деталь машины.</summary>
    public class HitZone : MonoBehaviour
    {
        public ModuleType type = ModuleType.Hull;
        public float armorMultiplier = 1f;
        public TankArmor armor;
    }

    /// <summary>Живучесть машины + эффекты критических повреждений.</summary>
    public class TankArmor : MonoBehaviour
    {
        public TankController tank;
        public TankSpec spec;

        // прочность
        public float HullHp { get; private set; }
        public float HullMax { get; private set; }
        public float TracksHp { get; private set; } = 260f;
        public float EngineHp { get; private set; } = 240f;
        public float GunHp { get; private set; } = 200f;

        public bool TracksBroken => TracksHp <= 0f;
        public bool EngineDamaged => EngineHp <= 0f;
        public bool GunDamaged => GunHp <= 0f;

        float tracksMult_ = 1f, armorMult_ = 1f;   // бонусы модулей прокачки
        float repairTracksTimer_, repairEngineTimer_, repairGunTimer_;

        public float SpeedFactor => (EngineDamaged ? 0.55f : 1f) * (TracksBroken ? 0.25f : 1f) * tracksMult_;
        public float RotationFactor => (TracksBroken ? 0.35f : 1f) * (EngineDamaged ? 0.85f : 1f);

        public void Init(TankController owner, TankSpec s)
        {
            tank = owner;
            spec = s;
            HullMax = s.hp;
            HullHp = s.hp;
        }

        public void ApplyModuleBonus(string id)
        {
            switch (id)
            {
                case "armor_hull": armorMult_ *= 1.18f; break;
                case "armor_turret": armorMult_ *= 1.18f; break;
                case "hp":
                    HullMax *= 1.12f; HullHp = Mathf.Min(HullMax, HullHp + s_hpBonus());
                    break;
                case "tracks":
                    TracksHp = Mathf.Min(400f, TracksHp * 1.25f + 40f); tracksMult_ *= 1.04f; break;
                case "engine":
                    if (tank != null) tank.bonusSpeed = 1.10f; break;
            }
        }

        float s_hpBonus() { return HullMax * 0.12f; }

        public float ArmorFor(ModuleType m)
        {
            switch (m)
            {
                case ModuleType.Turret: return spec.armorTurret * armorMult_;
                case ModuleType.Tracks: return 30f;
                case ModuleType.Engine: return spec.armorRear * 0.8f;
                case ModuleType.Gun: return 40f;
                case ModuleType.Ammo: return spec.armorHull * 0.9f;
                default: return spec.armorHull * armorMult_;
            }
        }

        /// <summary>Получить попадание снаряда. Возвращает результат для HUD и статистики.</summary>
        public PenResult TakeHit(ModuleType zone, Vector3 shellDir, Vector3 normal, float penetration,
                                 float damage, float distance)
        {
            float armor = ArmorFor(zone);
            float mult = 1f;
            if (zone == ModuleType.Hull && tank != null)
            {
                // Откуда пришёл снаряд: корма тоньше лобовой плиты, борт — между ними.
                // Угол наклона уже учитывается нормалью попадания в DamageSystem.
                float along = Vector3.Dot(shellDir.normalized, tank.transform.forward);
                if (along > 0.35f) armor = spec.armorRear * armorMult_;          // удар в корму
                else if (along > -0.35f) armor = spec.armorHull * 0.7f * armorMult_; // удар в борт
                // иначе — в лобовую плиту: полная thickness
            }
            var res = DamageSystem.Resolve(armor, shellDir, normal, penetration, damage, zone,
                                           mult, distance, Random.value);

            if (res.penetrated || res.damage > 0f)
            {
                DamageHull(res.damage);
                DamageModules(zone, res);
            }
            return res;
        }

        void DamageModules(ModuleType zone, PenResult res)
        {
            float dmg = res.damage;
            switch (zone)
            {
                case ModuleType.Tracks:
                    TracksHp -= dmg * 1.6f;
                    if (TracksHp <= 0f) { repairTracksTimer_ = 7f; if (tank.IsPlayer) HUD.Toast("Гусеница сбита! Машина почти не двигается", HUD.ToastKind.Bad); }
                    break;
                case ModuleType.Engine:
                    EngineHp -= dmg * 1.2f;
                    if (EngineHp <= 0f) { repairEngineTimer_ = 9f; if (tank.IsPlayer) HUD.Toast("Двигатель повреждён! Скорость снижена", HUD.ToastKind.Bad); }
                    break;
                case ModuleType.Gun:
                    GunHp -= dmg * 1.4f;
                    if (GunHp <= 0f)
                    {
                        repairGunTimer_ = 8f;
                        if (tank != null && tank.gun != null) tank.gun.reloadPenalty = 1.8f;
                        if (tank.IsPlayer) HUD.Toast("Орудие повреждено! Перезарядка дольше", HUD.ToastKind.Bad);
                    }
                    break;
                case ModuleType.Ammo:
                    if (res.critical)
                    {
                        if (tank != null && tank.gun != null) tank.gun.reloadPenalty = 1.5f;
                        if (tank != null) tank.ApplyStun(2.5f);         // детонация БК: экипаж контужен
                        if (tank.IsPlayer) HUD.Toast("Детонация БК: перезарядка длиннее", HUD.ToastKind.Bad);
                    }
                    break;
            }
        }

        public void DamageHull(float dmg)
        {
            if (dmg <= 0f) return;
            if (tank != null) dmg *= tank.DamageTakenMult;   // «Стальная стена» и прочие защитные эффекты
            if (dmg <= 0f) return;
            HullHp -= dmg;
            if (!tank.IsPlayer) return;
        }

        public void Heal(float amount)
        {
            HullHp = Mathf.Min(HullMax, HullHp + amount);
        }

        public void RepairModules()
        {
            TracksHp = Mathf.Max(TracksHp, 260f);
            EngineHp = Mathf.Max(EngineHp, 240f);
            GunHp = Mathf.Max(GunHp, 200f);
            if (tank != null && tank.gun != null) tank.gun.reloadPenalty = 1f;
        }

        void Update()
        {
            // модули сами восстанавливаются со временем (как «ремонт в бою» в оригинале)
            if (TracksBroken)
            {
                repairTracksTimer_ -= Time.deltaTime;
                if (repairTracksTimer_ <= 0f) { TracksHp = 260f * 0.6f; Fx.Spawn(tank.transform.position, 0.4f); }
            }
            if (EngineDamaged)
            {
                repairEngineTimer_ -= Time.deltaTime;
                if (repairEngineTimer_ <= 0f) EngineHp = 240f * 0.6f;
            }
            if (GunDamaged)
            {
                repairGunTimer_ -= Time.deltaTime;
                if (repairGunTimer_ <= 0f) { GunHp = 200f * 0.6f; if (tank.gun != null) tank.gun.reloadPenalty = 1f; }
            }
        }
    }
}
