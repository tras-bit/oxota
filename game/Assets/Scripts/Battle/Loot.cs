// Добыча: ящики на карте, трофеи с уничтоженных, «Воздушный груз» по расписанию.
using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    public enum LootKind { Shells, Xp, AbilityCharge, Repair, Respawn, AirDrop }

    public class LootBox : MonoBehaviour
    {
        public LootKind kind;
        public float amount = 1f;
        public bool isAirDrop;
        public float life = 900f;
        float bobPhase;

        /// <summary>Все ящики на карте. Реестр вместо FindObjectsOfType: боты сканируют добычу
        /// каждые 2,5 с, и при 29 ботах полный обход сцены десятью машинами в секунду
        /// стал бы заметным рывком кадра.</summary>
        public static readonly List<LootBox> All = new List<LootBox>();

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static LootBox Spawn(Vector3 pos, LootKind kind, float amount, bool airDrop)
        {
            var go = GameObject.CreatePrimitive(airDrop ? PrimitiveType.Cube : PrimitiveType.Cube);
            go.name = airDrop ? "AirDrop" : "Loot_" + kind;
            go.transform.position = pos;
            float s = airDrop ? 1.6f : 1.0f;
            go.transform.localScale = new Vector3(s, s * 0.8f, s);
            var col = go.GetComponent<Collider>();
            col.isTrigger = true;

            var rend = go.GetComponent<MeshRenderer>();
            rend.material = BoxMaterial(ColorFor(kind, airDrop));

            var lb = go.AddComponent<LootBox>();
            lb.kind = kind;
            lb.amount = amount;
            lb.isAirDrop = airDrop;
            lb.bobPhase = Random.value * 6f;

            if (airDrop)
            {
                Fx.Explosion(pos + Vector3.up * 2f, 0.8f);
                HUD.Toast("Воздушный груз сброшен! Отмечен на карте", HUD.ToastKind.Info);
            }
            return lb;
        }

        // материал ящика кэшируется по цвету: ящиков за бой — сотни, и каждому
        // создавался свой Material (нативный объект, не освобождается с ящиком)
        static readonly Dictionary<Color, Material> boxMats = new Dictionary<Color, Material>();

        static Material BoxMaterial(Color c)
        {
            Material m;
            if (boxMats.TryGetValue(c, out m) && m != null) return m;
            m = new Material(Shader.Find("Standard"));
            m.color = c;
            m.SetFloat("_Glossiness", 0.6f);
            boxMats[c] = m;
            return m;
        }

        static Color ColorFor(LootKind k, bool air)
        {
            if (air) return new Color(0.15f, 0.75f, 1f);
            switch (k)
            {
                case LootKind.Shells: return new Color(0.85f, 0.7f, 0.25f);
                case LootKind.Xp: return new Color(0.4f, 0.85f, 0.45f);
                case LootKind.AbilityCharge: return new Color(0.65f, 0.4f, 0.9f);
                case LootKind.Repair: return new Color(0.9f, 0.35f, 0.3f);
                default: return new Color(1f, 0.95f, 0.6f);
            }
        }

        void Update()
        {
            transform.position += Vector3.up * Mathf.Sin((Time.time + bobPhase) * 2f) * 0.0035f;
            transform.Rotate(Vector3.up, 25f * Time.deltaTime, Space.World);
            life -= Time.deltaTime;
            if (life <= 0f) Destroy(gameObject);
        }

        void OnTriggerEnter(Collider other)
        {
            var tank = other.GetComponentInParent<TankController>();
            if (tank == null || tank.Dead) return;
            Collect(tank);
        }

        public void Collect(TankController tank)
        {
            switch (kind)
            {
                case LootKind.Shells:
                    tank.gun.AddAmmo(Mathf.RoundToInt(amount));
                    if (tank.IsPlayer) HUD.Toast("Добыча: +" + Mathf.RoundToInt(amount) + " снарядов", HUD.ToastKind.Info);
                    break;
                case LootKind.Xp:
                    tank.AddXp(amount);
                    break;
                case LootKind.AbilityCharge:
                    tank.abilities.AddCharge(Mathf.RoundToInt(amount));
                    if (tank.IsPlayer) HUD.Toast("Добыча: заряд умений +" + Mathf.RoundToInt(amount), HUD.ToastKind.Info);
                    break;
                case LootKind.Repair:
                    tank.armor.RepairModules();
                    tank.armor.Heal(amount);
                    if (tank.IsPlayer) HUD.Toast("Добыча: ремонт +" + Mathf.RoundToInt(amount), HUD.ToastKind.Info);
                    break;
                case LootKind.Respawn:
                    tank.AddRespawnToken();
                    break;
                case LootKind.AirDrop:
                    tank.AddXp(Rules.XpAirDrop);
                    tank.armor.Heal(tank.armor.HullMax * 0.5f);
                    tank.armor.RepairModules();
                    tank.gun.AddAmmo(Mathf.RoundToInt(tank.spec.shells * 0.6f));
                    tank.abilities.RefillCharges();
                    tank.AddRespawnToken();
                    if (tank.IsPlayer) HUD.Toast("Воздушный груз забран! Полный набор ресурсов", HUD.ToastKind.Good);
                    break;
            }
            tank.LootPicked++;
            Sfx.Loot();
            tank.AddXp(Rules.XpLoot);
            Fx.Spawn(transform.position, isAirDrop ? 1.5f : 0.6f);
            Destroy(gameObject);
        }
    }

    /// <summary>Расстановка лута на карте + воздушные грузы.</summary>
    public class LootSpawner : MonoBehaviour
    {
        public int boxesOnMap = 60;
        public float mapRadius = 1200f;
        public Transform propsRoot;

        readonly List<LootBox> spawned = new List<LootBox>();

        void Start()
        {
            for (int i = 0; i < boxesOnMap; i++)
                SpawnRandom();
        }

        public void SpawnRandom()
        {
            Vector2 p = Random.insideUnitCircle * mapRadius;
            Vector3 pos = new Vector3(p.x, 0f, p.y);
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 200f, Vector3.down, out hit, 400f, ~0, QueryTriggerInteraction.Ignore))
                pos = hit.point + Vector3.up * 0.8f;
            else pos.y = 1f;

            LootKind kind = (LootKind)Random.Range(0, 5);
            float amount = kind == LootKind.Shells ? Random.Range(4, 10)
                         : kind == LootKind.Xp ? Random.Range(80, 220)
                         : kind == LootKind.Repair ? Random.Range(120, 260) : 1;
            spawned.Add(LootBox.Spawn(pos, kind, amount, false));
        }

        public void SpawnAirDrop()
        {
            Vector3 pos = ZoneController.Instance != null
                ? ZoneController.Instance.RandomPointInside()
                : new Vector3(Random.Range(-600f, 600f), 2f, Random.Range(-600f, 600f));
            pos.y = Mathf.Max(1.5f, pos.y);
            spawned.Add(LootBox.Spawn(pos, LootKind.AirDrop, 1f, true));
            MinimapWidget.MarkAirDrop(pos);
        }

        /// <summary>Трофеи с уничтоженной машины (собираются автоматически рядом).</summary>
        /// <summary>Трофеи с убитой машины. С «Мародёров» падает больше: заряд умения почти всегда,
        /// жетон «Возрождения» — заметно чаще (они и есть источник трофеев в режиме).</summary>
        public void SpawnTrophy(Vector3 pos, bool rich = false)
        {
            LootKind kind = (LootKind)Random.Range(0, 4);
            float amount = kind == LootKind.Shells ? Random.Range(3, 8)
                         : kind == LootKind.Xp ? Random.Range(60, 140)
                         : kind == LootKind.Repair ? Random.Range(80, 180) : 1;
            LootBox.Spawn(pos + Vector3.up * 0.6f, kind, amount, false);
            if (Random.value > (rich ? 0.25f : 0.75f))
                LootBox.Spawn(pos + new Vector3(Random.Range(-3f, 3f), 0.6f, Random.Range(-3f, 3f)),
                              LootKind.AbilityCharge, 1f, false);
            if (Random.value > (rich ? 0.7f : 0.9f))
                LootBox.Spawn(pos + new Vector3(Random.Range(-4f, 4f), 0.6f, Random.Range(-4f, 4f)),
                              LootKind.Respawn, 1f, false);
        }
    }
}
