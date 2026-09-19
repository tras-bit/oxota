using UnityEngine;

namespace Samsar
{
    /// <summary>Наведение башни и орудия на точку прицеливания; угол расхождения для стрельбы.</summary>
    public class TurretAim : MonoBehaviour
    {
        public Vehicle Vehicle;
        [HideInInspector] public float AimErrorDeg = 180f;
        public float TurretSpeedDeg = 32f;      // градусов в секунду
        public float PitchSpeedDeg = 22f;
        public float MaxDepression = -12f;
        public float MaxElevation = 22f;

        float turretYaw;      // текущий абсолютный угол башни
        float barrelPitch;
        Vector3 lastAimDir = Vector3.forward;

        void Awake()
        {
            if (Vehicle == null) Vehicle = GetComponent<Vehicle>();
        }

        public bool AimAt(Vector3 worldPoint, bool instant = false)
        {
            if (Vehicle == null || Vehicle.Turret == null) return false;
            Vector3 origin = Vehicle.Turret.position + Vector3.up * 0.3f;
            Vector3 dir = worldPoint - origin;
            if (dir.sqrMagnitude < 0.01f) return AimErrorDeg < 1f;
            lastAimDir = dir.normalized;

            float wantedYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float flatDist = new Vector2(dir.x, dir.z).magnitude;
            float wantedPitch = Mathf.Clamp(Mathf.Atan2(dir.y, Mathf.Max(0.01f, flatDist)) * Mathf.Rad2Deg,
                MaxDepression, MaxElevation);

            // ограничение поворота башни у ПТ-САУ (рубка)
            if (Vehicle.Model != null && Vehicle.Model.TurretYawLimit < 180f)
            {
                float hullYaw = transform.eulerAngles.y;
                float delta = Mathf.DeltaAngle(hullYaw, wantedYaw);
                delta = Mathf.Clamp(delta, -Vehicle.Model.TurretYawLimit, Vehicle.Model.TurretYawLimit);
                wantedYaw = hullYaw + delta;
                // доворачиваем корпус, если цель вне сектора
                if (Mathf.Abs(Mathf.DeltaAngle(hullYaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg)) > Vehicle.Model.TurretYawLimit)
                {
                    var tc = GetComponent<TankController>();
                    if (tc != null) tc.MoveInput = new Vector2(Mathf.Clamp(delta * 0.05f, -1f, 1f), tc.MoveInput.y);
                }
            }

            float step = (instant ? 999f : TurretSpeedDeg * Time.deltaTime);
            // при поломке орудия башня крутится медленнее
            if (Vehicle.GunBroken) step *= 0.45f;
            turretYaw = Mathf.MoveTowardsAngle(Vehicle.Turret.eulerAngles.y, wantedYaw, step);
            barrelPitch = Mathf.MoveTowards(barrelPitch, wantedPitch, (instant ? 999f : PitchSpeedDeg * Time.deltaTime));

            Vehicle.Turret.rotation = Quaternion.Euler(0f, turretYaw, 0f);
            if (Vehicle.Model != null && Vehicle.Model.Barrel != null)
                Vehicle.Model.Barrel.localRotation = Quaternion.Euler(-barrelPitch, 0f, 0f);

            AimErrorDeg = Mathf.Abs(Mathf.DeltaAngle(turretYaw, wantedYaw)) + Mathf.Abs(barrelPitch - wantedPitch);
            // расхождение при вращении башни добавляет разброс
            if (Mathf.Abs(Mathf.DeltaAngle(turretYaw, wantedYaw)) > 4f)
                Vehicle.AimBloom = Mathf.Min(Vehicle.AimBloom + Time.deltaTime * 0.45f, 1.0f);

            return AimErrorDeg < 1.5f;
        }

        /// <summary>Точка вылета снаряда с учётом положения ствола.</summary>
        public Vector3 MuzzlePosition
        {
            get { return Vehicle.BarrelTip != null ? Vehicle.BarrelTip.position : transform.position + Vector3.up * 1.6f; }
        }

        public Vector3 AimDirection { get { return lastAimDir; } }
    }
}
