// Управление игрока: WASD + мышь, стрельба, снайперский прицел, умения, помощь напарнику.
using UnityEngine;

namespace Samsar
{
    public class PlayerInput : MonoBehaviour, ITankInput
    {
        public TankController tank;
        public static PlayerInput Instance;

        public float Throttle { get; private set; }
        public float Steer { get; private set; }
        public Vector3 AimPoint { get; private set; }
        public bool Fire { get; private set; }
        public bool SniperMode { get; private set; }

        float aimDistance = 300f;
        float shareRepairTimer, shareLootTimer;

        public static PlayerInput Attach(TankController t)
        {
            var pi = t.gameObject.AddComponent<PlayerInput>();
            pi.tank = t;
            t.input = pi;
            Instance = pi;
            return pi;
        }

        void Update()
        {
            if (tank == null || tank.Dead) { Fire = false; Throttle = 0f; return; }

            // движение
            Throttle = Input.GetAxis("Vertical");
            Steer = Input.GetAxis("Horizontal");

            // прицел: луч из камеры в точку под курсором
            var cam = Camera.main;
            if (cam != null)
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 4000f, ~0, QueryTriggerInteraction.Ignore))
                {
                    AimPoint = hit.point;
                    aimDistance = hit.distance;
                }
                else
                {
                    AimPoint = ray.origin + ray.direction * 600f;
                    aimDistance = 600f;
                }
            }

            // режимы и стрельба
            SniperMode = Input.GetMouseButton(1);
            Fire = Input.GetMouseButton(0);

            // умения: 1 и 2 (или Q/E)
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Q))
                tank.abilities.Use(0, AimPoint);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.E))
                tank.abilities.Use(1, AimPoint);

            // ремонт напарника: удерживать H рядом с союзником (обмен ремонтом)
            if (Input.GetKey(KeyCode.H)) ShareRepair();
            else shareRepairTimer = 0f;

            // передача добычи напарнику: удерживать G рядом с союзником (снаряды и заряд умения)
            if (Input.GetKey(KeyCode.G)) ShareLoot();
            else shareLootTimer = 0f;

            if (Input.GetKeyDown(KeyCode.Tab)) HUD.ToggleStats();
            if (Input.GetKeyDown(KeyCode.Escape)) HUD.TogglePauseMenu();
        }

        void ShareRepair()
        {
            shareRepairTimer -= Time.deltaTime;
            if (shareRepairTimer > 0f) return;
            shareRepairTimer = 1f;
            foreach (var t in TankRegistry.All)
            {
                if (!t.IsAlly || t.Dead) continue;
                if (Vector3.Distance(t.transform.position, tank.transform.position) > 30f) continue;
                t.armor.Heal(t.armor.HullMax * 0.04f);
                t.armor.RepairModules();
                tank.armor.Heal(tank.armor.HullMax * 0.02f);
                HUD.Toast("Ремонт передан напарнику", HUD.ToastKind.Good);
            }
        }

        /// <summary>Отдать напарнику снаряды и заряд умения — взвод воюет как одно целое.</summary>
        void ShareLoot()
        {
            shareLootTimer -= Time.deltaTime;
            if (shareLootTimer > 0f) return;
            shareLootTimer = 2f;
            if (tank.abilities == null) return;
            foreach (var t in TankRegistry.All)
            {
                if (!t.IsAlly || t.Dead) continue;
                if (Vector3.Distance(t.transform.position, tank.transform.position) > 30f) continue;

                int shells = Mathf.Min(6, Mathf.Max(0, tank.gun.Ammo - 4));
                if (shells > 0) t.gun.AddAmmo(shells);

                int charges = 0;
                for (int i = 0; i < tank.abilities.abilities.Count && i < t.abilities.abilities.Count; i++)
                {
                    var mine = tank.abilities.abilities[i];
                    var mate = t.abilities.abilities[i];
                    if (mine.charges > 1 && mate.charges < mate.maxCharges) { mine.charges--; mate.charges++; charges++; }
                }
                if (shells > 0 || charges > 0)
                    HUD.Toast(string.Format("Напарнику передано: {0} снаряд(ов), заряды умений: {1}",
                                            shells, charges), HUD.ToastKind.Good);
                else
                    HUD.Toast("Передавать нечего — подбери добычу", HUD.ToastKind.Info);
            }
        }
    }
}
