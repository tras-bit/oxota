using UnityEngine;

namespace Samsar
{
    /// <summary>Управление игроком: движение WASD, мышь — прицел и камера, ЛКМ — выстрел, Q/E — умения, ПКМ — снайперский прицел.</summary>
    public class PlayerController : MonoBehaviour
    {
        public Vehicle Vehicle;
        public TankController Controller;
        public TurretAim Turret;

        public bool SniperMode;
        public float AimDistance = 800f;

        PlayerCameraRig rig;

        void Start()
        {
            rig = Camera.main != null ? Camera.main.GetComponent<PlayerCameraRig>() : null;
        }

        void Update()
        {
            if (Vehicle == null || !Vehicle.Alive)
            {
                if (Controller != null) Controller.MoveInput = Vector2.zero;
                return;
            }
            float dt = Time.deltaTime;

            // ==== движение ====
            float throttle = 0f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) throttle += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) throttle -= 1f;
            float steer = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) steer -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) steer += 1f;
            bool brake = Input.GetKey(KeyCode.Space);
            Controller.MoveInput = new Vector2(steer, throttle);
            Controller.Brake = brake;

            // ==== камера и прицел ====
            if (rig != null)
            {
                float sens = GameConfig.MouseSensitivity * (SniperMode ? 0.35f : 1f);
                float mx = Input.GetAxis("Mouse X") * sens * 3f;
                float my = Input.GetAxis("Mouse Y") * sens * 3f * (GameConfig.InvertY ? -1f : 1f);
                rig.AddRotation(mx, my);
                Vector3 aim = rig.GetAimPoint();
                Turret.AimAt(aim);
                Vehicle.AimPoint = aim;

                // снайперский режим
                bool wantSniper = Input.GetMouseButton(1);
                if (wantSniper != SniperMode)
                {
                    SniperMode = wantSniper;
                    rig.SetSniper(SniperMode);
                    AudioSynth.PlayUi("click", 0.4f);
                }
            }
            else
            {
                Vector3 fwd = transform.forward * 400f;
                Turret.AimAt(transform.position + fwd);
                Vehicle.AimPoint = transform.position + fwd;
            }

            // ==== выстрел ====
            if (Input.GetMouseButton(0) && Vehicle.CanShoot)
            {
                if (Vehicle.Fire(Vehicle.AimPoint))
                {
                    // отдача: разброс растёт
                    Vehicle.AimBloom = Mathf.Min(Vehicle.AimBloom + 0.35f, 1.5f);
                }
            }

            // ==== умения ====
            if (Input.GetKeyDown(KeyCode.Q)) TryAbility(1);
            if (Input.GetKeyDown(KeyCode.E)) TryAbility(2);

            // ==== расход топлива/двигатель: звук по нагрузке ====
            float load = Mathf.Abs(Controller.CurrentSpeed) / Mathf.Max(1f, Vehicle.Stats.SpeedForward / 3.6f);
            if (AudioBus.Instance != null) AudioBus.Instance.Engine(Vehicle.Alive ? 0.25f + load * 0.75f : 0f);

            // разброс от движения
            float speedRatio = Mathf.Abs(Controller.CurrentSpeed) / Mathf.Max(1f, Vehicle.Stats.SpeedForward / 3.6f);
            Vehicle.AimBloom = Mathf.Min(Vehicle.AimBloom + dt * speedRatio * 0.5f, 1.1f);
        }

        void TryAbility(int slot)
        {
            var id = Vehicle.AbilityOf(slot);
            if (id == AbilityId.None) return;
            Vector3 target = Vehicle.AimPoint;
            if (!Vehicle.UseAbility(slot, target))
            {
                float cd = Vehicle.CooldownOf(slot);
                if (cd > 0f)
                    Vfx.DamageNumber(Vehicle.transform.position + Vector3.up * 3.6f,
                        TankSpecs.AbilityName(id) + ": перезарядка " + Mathf.CeilToInt(cd) + " с", new Color(1f, 0.7f, 0.4f), 0.85f);
            }
        }
    }
}
