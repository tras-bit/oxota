// Наведение башни и орудия: скорость поворота, вертикальная наводка, снайперский режим.
using UnityEngine;

namespace Samsar
{
    public class TurretController : MonoBehaviour
    {
        public TankController tank;
        public float traversedSpeedMult = 1f;      // из модулей прокачки
        public bool SniperMode { get; private set; }
        public Vector3 AimPoint { get; private set; }

        float yaw, pitch;

        public void Init(TankController t)
        {
            tank = t;
        }

        public void AimAt(Vector3 worldPoint, bool sniper)
        {
            AimPoint = worldPoint;
            SniperMode = sniper;
            if (tank == null || tank.rig == null || tank.rig.TurretPivot == null) return;

            Vector3 pivot = tank.rig.TurretPivot.position;
            Vector3 dir = worldPoint - pivot;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;

            Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up);
            if (!tank.rig.HasTurret)
            {
                // ПТ-САУ: рубки нет, орудие ходит только в узком секторе от корпуса (как у настоящей САУ)
                Vector3 hullFwd = tank.transform.forward;
                hullFwd.y = 0f;
                float limit = 13f;
                float ang = Vector3.SignedAngle(hullFwd, dir.normalized, Vector3.up);
                float clamped = Mathf.Clamp(ang, -limit, limit);
                want = Quaternion.LookRotation(Quaternion.AngleAxis(clamped, Vector3.up) * hullFwd.normalized,
                                               Vector3.up);
            }
            float speed = tank.spec.turretTraverse * traversedSpeedMult * (SniperMode ? 0.7f : 1f);
            tank.rig.TurretPivot.rotation = Quaternion.RotateTowards(
                tank.rig.TurretPivot.rotation, want, speed * Time.deltaTime);

            // вертикаль по разнице высот
            if (tank.rig.GunPivot != null)
            {
                float dist = Mathf.Max(4f, Vector3.Distance(pivot, worldPoint));
                float dy = worldPoint.y - pivot.y;
                float wantPitch = -Mathf.Atan2(dy, dist) * Mathf.Rad2Deg;
                wantPitch = Mathf.Clamp(wantPitch, -8f, 20f);
                pitch = Mathf.MoveTowards(pitch, wantPitch, 25f * Time.deltaTime);
                var local = tank.rig.GunPivot.localRotation.eulerAngles;
                tank.rig.GunPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        /// <summary>Целится ли башня в цель (для ботов и подсказок).</summary>
        public bool OnTarget(Vector3 worldPoint, float toleranceDeg)
        {
            Vector3 dir = worldPoint - tank.rig.TurretPivot.position;
            return Vector3.Angle(tank.rig.TurretPivot.forward, dir) < toleranceDeg;
        }

        public Quaternion TurretRotation => tank.rig.TurretPivot != null ? tank.rig.TurretPivot.rotation : transform.rotation;
    }
}
