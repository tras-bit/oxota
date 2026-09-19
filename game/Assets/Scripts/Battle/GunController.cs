// Орудие: перезарядка, боекомплект, разброс, прокачка в бою.
using UnityEngine;

namespace Samsar
{
    public class GunController : MonoBehaviour
    {
        public TankController tank;
        public TankSpec spec;

        public float damageMult = 1f;
        public float reloadMult = 1f;
        public float dispersionMult = 1f;
        public float aimTimeMult = 1f;
        public float reloadPenalty = 1f;      // от повреждения орудия/БК

        public int Ammo { get; private set; }
        public float ReloadLeft { get; private set; }
        public float ReloadTime => spec.reload * reloadMult * reloadPenalty;
        public bool Ready => ReloadLeft <= 0f && Ammo > 0;
        public float ReloadProgress => ReloadTime <= 0f ? 1f : Mathf.Clamp01(1f - ReloadLeft / ReloadTime);

        float recoilKick;

        public void Init(TankController t, TankSpec s)
        {
            tank = t;
            spec = s;
            Ammo = s.shells;
        }

        public void AddAmmo(int n)
        {
            Ammo += n;
        }

        public void RefillAmmo()
        {
            Ammo = Mathf.Max(Ammo, Mathf.CeilToInt(spec.shells * 0.6f));
        }

        /// <summary>После прокачки уровень орудия даёт −50% времени перезарядки (как в оригинале).</summary>
        public void OnLevelUp()
        {
            ReloadLeft = Mathf.Min(ReloadLeft, ReloadTime * 0.5f);
            recoilKick = 1f;
        }

        void Update()
        {
            if (ReloadLeft > 0f) ReloadLeft -= Time.deltaTime;
        }

        public bool TryFire(Vector3 aimPoint)
        {
            if (!Ready || tank == null || tank.Dead) return false;

            Vector3 origin = tank.rig.GunTip.position;
            Vector3 dir = (aimPoint - origin).normalized;

            // разброс: метры на 100 м, зависит от прокачки и режима
            float distance = Vector3.Distance(origin, aimPoint);
            float disp = spec.dispersion * dispersionMult * (distance / 100f);
            if (tank.turret != null && tank.turret.SniperMode) disp *= 0.45f;
            Vector3 spread = new Vector3(Random.Range(-disp, disp), Random.Range(-disp, disp),
                                         Random.Range(-disp, disp)) * 0.35f;

            Shell.Fire(tank, origin, (dir + spread).normalized,
                       spec.damage * damageMult, spec.penetration, spec.shellSpeed, distance);

            Fx.MuzzleFlash(origin, dir, spec.gun_caliber_placeholder());
            Sfx.Shot(origin, spec.cls);
            Ammo--;
            ReloadLeft = ReloadTime;
            recoilKick = 1f;
            if (tank.IsPlayer) HUD.PunchCrosshair(recoilKick);
            return true;
        }
    }

    internal static class GunSpecExt
    {
        /// <summary>Калибр для эффекта: берём из габаритов модели (упрощённо).</summary>
        public static float gun_caliber_placeholder(this TankSpec s)
        {
            switch (s.cls)
            {
                case TankClass.LT: return 0.076f;
                case TankClass.MT: return 0.100f;
                case TankClass.HT: return 0.122f;
                default: return 0.128f;
            }
        }
    }
}
