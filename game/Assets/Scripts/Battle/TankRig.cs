// Сборка машины из FBX-модели: иерархия корпус/башня/орудие, коллайдеры зон, эффекты,
// анимация ходовой (катки, подвеска), откат ствола, следы гусениц и пыль.
using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    /// <summary>Готовит модель к бою: делит на корпус/башню/орудие, ставит коллайдеры, оживляет ходовую.</summary>
    public class TankRig : MonoBehaviour
    {
        public Transform HullRoot { get; private set; }
        public Transform BodyRoot { get; private set; }     // качающаяся часть корпуса (подвеска)
        public Transform TurretPivot { get; private set; }

        /// <summary>Есть ли вращающаяся башня. У ПТ-САУ рубка неподвижна — орудие в секторе.</summary>
        public bool HasTurret { get; private set; }
        public Transform GunPivot { get; private set; }
        public Transform GunTip { get; private set; }

        readonly List<ParticleSystem> fires = new List<ParticleSystem>();
        readonly List<HitZone> zones = new List<HitZone>();
        bool built;

        TankController owner;
        TankSpec spec;

        // ходовая
        struct Wheel { public Transform t; public float r; }
        readonly List<Wheel> wheels = new List<Wheel>();
        readonly List<ParticleSystem> dusts = new List<ParticleSystem>();
        readonly List<Transform> marks = new List<Transform>();
        int markIndex;
        float markDist;
        Transform markParent;

        // подвеска/откат
        Vector3 gunRestLocal;
        float recoil, pitch, roll, bobPhase;
        float driveSpeed, driveSteer, driveThrottle;

        static readonly string[] TurretParts =
        { "_turret", "_mantlet", "_cupola", "_hatch", "_vision", "_sight", "_aa_mg",
          "_basket", "_smoke", "_antenna", "_casemate", "_cas_" };
        static readonly string[] GunParts = { "_gun", "_muzzle" };
        static readonly string[] GearParts =
        { "_track", "_wheel", "_sprocket", "_idler", "_roller", "_link" };
        static readonly string[] SpinParts =
        { "_wheel", "_sprocket", "_idler", "_roller" };

        const int MarksPerTank = 36;

        public void Build(TankSpec spec, TankController tank)
        {
            if (built) return;
            built = true;
            owner = tank;
            this.spec = spec;
            zones.Clear();
            wheels.Clear();

            var meshes = new List<Transform>();
            foreach (var r in GetComponentsInChildren<Renderer>())
                if (r.transform != transform) meshes.Add(r.transform);

            HullRoot = new GameObject("Hull").transform;
            HullRoot.SetParent(transform, false);
            BodyRoot = new GameObject("Body").transform;
            BodyRoot.SetParent(HullRoot, false);
            TurretPivot = new GameObject("TurretPivot").transform;
            TurretPivot.SetParent(transform, false);
            GunPivot = new GameObject("GunPivot").transform;
            GunPivot.SetParent(TurretPivot, false);

            var turretList = new List<Transform>();
            var gunList = new List<Transform>();
            var hullList = new List<Transform>();
            var gearList = new List<Transform>();
            var bodyList = new List<Transform>();
            foreach (var m in meshes)
            {
                string n = m.name.ToLowerInvariant();
                if (ContainsAny(n, GunParts)) gunList.Add(m);
                else if (ContainsAny(n, TurretParts)) turretList.Add(m);
                else if (ContainsAny(n, GearParts)) { gearList.Add(m); hullList.Add(m); }
                else { bodyList.Add(m); hullList.Add(m); }
            }
            HasTurret = turretList.Count > 0;
            if (turretList.Count == 0 && gunList.Count > 0)
            {   // ПТ-САУ без башни: орудие всё равно наводится по вертикали
                TurretPivot.position = WorldBounds(gunList).center;
            }
            else
            {
                TurretPivot.position = WorldBounds(turretList.Count > 0 ? turretList : meshes).center;
            }

            foreach (var m in gearList) m.SetParent(HullRoot, true);   // гусеницы остаются «на земле»
            foreach (var m in bodyList) m.SetParent(BodyRoot, true);   // корпус качается на подвеске
            foreach (var m in turretList) m.SetParent(TurretPivot, true);
            foreach (var m in gunList) m.SetParent(GunPivot, true);

            foreach (var m in gearList)
            {
                string n = m.name.ToLowerInvariant();
                if (ContainsAny(n, SpinParts)) wheels.Add(new Wheel { t = m, r = WheelRadius(m, n) });
            }

            // качающаяся часть орудия: точка вращения — казённик
            if (gunList.Count > 0)
            {
                Bounds gb = WorldBounds(gunList);
                GunPivot.position = new Vector3(gb.center.x, gb.center.y, gb.min.z + 0.15f);
            }
            else
            {
                Bounds tb = WorldBoundsOf(TurretPivot);
                GunPivot.position = new Vector3(tb.center.x, tb.center.y, tb.max.z - 0.4f);
            }
            gunRestLocal = GunPivot.localPosition;

            // ---- коллайдеры и зоны попадания ----
            // Корпус считаем по деталям корпуса (без гусениц и башни), а хитбоксы гусениц — по самим
            // лентам из модели: у корпуса низ уже гусениц, и по габариту корпуса хитбоксы уезжали мимо.
            Bounds hull = bodyList.Count > 0 ? WorldBounds(bodyList) : WorldBoundsOf(HullRoot);
            AddBox(HullRoot, "hit_hull", hull.center, hull.size * 0.98f, ModuleType.Hull);
            var trackMesh = new List<Transform>();
            foreach (var m in gearList)
                if (m.name.ToLowerInvariant().Contains("_track")) trackMesh.Add(m);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 c;
                Vector3 size;
                if (trackMesh.Count > 0)
                {
                    var side = new List<Transform>();
                    foreach (var m in trackMesh)
                        if (s < 0 ? m.position.x < transform.position.x : m.position.x > transform.position.x)
                            side.Add(m);
                    var tb = side.Count > 0 ? WorldBounds(side) : hull;
                    c = tb.center;
                    size = new Vector3(Mathf.Max(0.5f, tb.size.x), hull.size.y * 0.7f, tb.size.z * 0.98f);
                }
                else
                {
                    float halfW = Mathf.Max(0.6f, hull.extents.x);
                    c = hull.center + new Vector3(s * (halfW + 0.05f), -hull.extents.y * 0.25f, 0f);
                    size = new Vector3(0.75f, hull.size.y * 0.5f, hull.size.z * 0.96f);
                }
                AddBox(HullRoot, "hit_tracks" + s, c, size, ModuleType.Tracks);
            }
            AddBox(HullRoot, "hit_engine",
                   hull.center - transform.forward * (hull.extents.z * 0.68f),
                   new Vector3(hull.size.x * 0.7f, hull.size.y * 0.8f, hull.extents.z * 0.55f),
                   ModuleType.Engine);
            AddBox(HullRoot, "hit_ammo", hull.center + Vector3.up * 0.05f,
                   new Vector3(hull.size.x * 0.45f, hull.size.y * 0.45f, hull.extents.z * 0.4f),
                   ModuleType.Ammo);
            Bounds tb2 = WorldBoundsOf(TurretPivot);
            AddBox(TurretPivot, "hit_turret", tb2.center, tb2.size * 1.02f, ModuleType.Turret);
            Bounds gb2 = WorldBoundsOf(GunPivot);
            AddBox(GunPivot, "hit_gun", gb2.center + transform.forward * (gb2.extents.z * 0.4f),
                   new Vector3(0.5f, 0.5f, gb2.size.z * 0.9f), ModuleType.Gun);

            foreach (var z in zones) z.armor = tank.armor;

            // ---- точка вылета снаряда ----
            GunTip = new GameObject("GunTip").transform;
            GunTip.SetParent(GunPivot, false);
            GunTip.localPosition = new Vector3(0f, 0f, gb2.extents.z * 1.0f + 0.3f);

            // ---- пыль из-под гусениц ----
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 p = hull.center + transform.right * (s * (halfW + 0.25f))
                                           - transform.forward * (hull.extents.z * 0.75f);
                p.y = hull.min.y + 0.2f;
                var ps = Fx.MakeDust(p, 0.9f, transform);
                var em = ps.emission;
                em.rateOverTime = 0f;
                dusts.Add(ps);
            }

            // ---- физика ----
            if (GetComponent<Collider>() == null)
            {
                var root = gameObject.AddComponent<BoxCollider>();
                root.size = Vector3.one * 0.1f;
                root.center = new Vector3(0f, 0.5f, 0f);
            }
            var pmat = new PhysicMaterial("tank") { dynamicFriction = 0.9f, staticFriction = 0.9f, bounciness = 0f };
            foreach (var c in GetComponentsInChildren<Collider>())
            {
                c.material = pmat;
                c.isTrigger = false;
            }
            SafeTag(gameObject, "Tank");
        }

        // ---------- анимация ----------
        /// <summary>Данные движения за кадр: скорость вперёд, руль (−1..1), газ (−1..1).</summary>
        public void Drive(float forwardSpeed, float steer, float throttle)
        {
            driveSpeed = forwardSpeed;
            driveSteer = steer;
            driveThrottle = throttle;
        }

        /// <summary>Откат ствола после выстрела.</summary>
        public void KickRecoil(float power = 1f)
        {
            recoil = Mathf.Clamp01(recoil + power);
        }

        /// <summary>Сброс визуала (после возрождения).</summary>
        public void ResetVisuals()
        {
            recoil = 0f; pitch = 0f; roll = 0f;
            if (GunPivot != null) GunPivot.localPosition = gunRestLocal;
            if (BodyRoot != null) BodyRoot.localRotation = Quaternion.identity;
        }

        void Update()
        {
            if (!built) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            bool dead = owner != null && owner.Dead;
            float speed = dead ? 0f : driveSpeed;
            float maxSpeed = spec != null ? Mathf.Max(1f, spec.maxSpeed) : 15f;
            float rel = Mathf.Clamp(speed / maxSpeed, -1.5f, 1.5f);

            // катки: угловая скорость = v / r, вокруг поперечной оси корпуса
            if (wheels.Count > 0 && Mathf.Abs(speed) > 0.02f)
            {
                for (int i = 0; i < wheels.Count; i++)
                {
                    var w = wheels[i];
                    if (w.t == null) continue;
                    float deg = Mathf.Rad2Deg * (speed / Mathf.Max(0.05f, w.r)) * dt;
                    w.t.RotateAround(w.t.position, transform.right, deg);
                }
            }

            // подвеска: клюёт носом при торможении, приседает при разгоне, кренится в повороте
            float targetPitch = dead ? 0f : Mathf.Clamp(-(driveThrottle - rel) * 3.4f, -3.6f, 3.6f);
            float targetRoll = dead ? 0f : Mathf.Clamp(driveSteer * Mathf.Clamp01(Mathf.Abs(rel)) * 3.2f, -3.6f, 3.6f);
            pitch = Mathf.Lerp(pitch, targetPitch, dt * 2.2f);
            roll = Mathf.Lerp(roll, targetRoll, dt * 2.6f);
            bobPhase += dt * (2f + Mathf.Abs(speed) * 0.9f);
            float bob = dead ? 0f : Mathf.Sin(bobPhase) * Mathf.Clamp01(Mathf.Abs(rel)) * 0.5f;
            if (BodyRoot != null) BodyRoot.localRotation = Quaternion.Euler(pitch + bob, 0f, roll);

            // откат ствола: резко назад — плавно вперёд
            if (recoil > 0f && GunPivot != null)
            {
                recoil = Mathf.MoveTowards(recoil, 0f, dt * 1.9f);
                GunPivot.localPosition = gunRestLocal + Vector3.back * (recoil * recoil * 0.45f);
            }

            // пыль: чем быстрее — тем плотнее
            float dustRate = dead ? 0f : Mathf.Clamp01(Mathf.Abs(rel) * 1.6f) * 28f;
            for (int i = 0; i < dusts.Count; i++)
            {
                var ps = dusts[i];
                if (ps == null) continue;
                var em = ps.emission;
                em.rateOverTime = dustRate;
            }

            // следы гусениц
            TrackMarks(dt, speed, maxSpeed);
        }

        // ---------- следы от гусениц ----------
        void TrackMarks(float dt, float speed, float maxSpeed)
        {
            if (marks.Count == 0)
            {
                var cam = Camera.main;
                if (cam == null) return;
                if ((cam.transform.position - transform.position).sqrMagnitude > 200f * 200f) return;
                CreateMarkPool();
            }
            if (Mathf.Abs(speed) < 2f) return;

            markDist += Mathf.Abs(speed) * dt;
            if (markDist < 1.1f) return;
            markDist = 0f;

            float halfW = Mathf.Max(0.6f, spec != null ? spec.width * 0.5f - 0.4f : 1f);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 p = transform.position + transform.right * (s * halfW) - transform.up * 0.2f;
                RaycastHit hit;
                if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var m = marks[markIndex];
                    markIndex = (markIndex + 1) % marks.Count;
                    if (m == null) continue;
                    m.gameObject.SetActive(true);
                    m.position = hit.point + hit.normal * 0.03f;
                    // квад лежит в XZ: кладём его по нормали склона и доводим по курсу машины
                    m.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) *
                                 Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                }
            }
        }

        void CreateMarkPool()
        {
            markParent = new GameObject("~TrackMarks_" + name).transform;
            markParent.SetParent(null, true);
            var mat = MarkMaterial();
            var mesh = QuadMesh();
            for (int i = 0; i < MarksPerTank; i++)
            {
                var q = new GameObject("mark");
                q.transform.SetParent(markParent, false);
                var mf = q.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var r = q.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                q.SetActive(false);
                marks.Add(q.transform);
            }
        }

        static Mesh markMesh;
        /// <summary>Квад лежит в плоскости XZ, нормаль вверх — ориентация по склону однозначна.</summary>
        static Mesh QuadMesh()
        {
            if (markMesh != null) return markMesh;
            markMesh = new Mesh();
            markMesh.name = "trackMarkQuad";
            markMesh.vertices = new[]
            {
                new Vector3(-0.28f, 0f, -0.65f), new Vector3(0.28f, 0f, -0.65f),
                new Vector3(0.28f, 0f, 0.65f), new Vector3(-0.28f, 0f, 0.65f)
            };
            markMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            markMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            markMesh.RecalculateNormals();
            markMesh.RecalculateBounds();
            return markMesh;
        }

        static Material markMat;
        static Material MarkMaterial()
        {
            if (markMat != null) return markMat;
            var sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            markMat = new Material(sh);
            markMat.color = new Color(0.07f, 0.06f, 0.05f, 0.32f);
            markMat.renderQueue = 2900;
            return markMat;
        }

        public static void SafeTag(GameObject go, string tag)
        {
            try { go.tag = tag; } catch { /* тег не объявлен в проекте — не критично */ }
        }

        /// <summary>Точка прицеливания в конкретную зону (для ботов: ходовая, МТО, орудие).</summary>
        public Vector3 ZoneAimPoint(ModuleType type)
        {
            for (int i = 0; i < zones.Count; i++)
                if (zones[i] != null && zones[i].type == type) return zones[i].transform.position;
            return transform.position + Vector3.up * 1.5f;
        }

        /// <summary>Радиус катка для прокрутки: берём из габаритов самой детали модели,
        /// иначе — типовая оценка. Иначе гусеница «проскальзывает» на классах с другими катками
        /// (у ЛТ 0.34 м, у ТТ 0.42 м — одна константа не подходит всем).</summary>
        static float WheelRadius(Transform t, string n)
        {
            var r = t.GetComponent<Renderer>();
            if (r != null)
            {
                Vector3 size = r.bounds.size;
                float d = Mathf.Max(Mathf.Min(size.y, size.z), 0.05f);   // каток — диск в плоскости YZ
                if (d > 0.16f && d < 2.5f) return d * 0.5f;
            }
            if (n.Contains("_roller")) return 0.16f;
            if (n.Contains("_sprocket")) return 0.36f;
            if (n.Contains("_idler")) return 0.30f;
            return 0.40f;   // опорные катки
        }

        static bool ContainsAny(string name, string[] keys)
        {
            foreach (var k in keys) if (name.Contains(k)) return true;
            return false;
        }

        static Bounds WorldBounds(List<Transform> list)
        {
            bool first = true;
            Bounds b = new Bounds();
            foreach (var t in list)
            {
                var r = t.GetComponent<Renderer>();
                if (r == null) continue;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            if (first) b = new Bounds(Vector3.zero, Vector3.one * 2f);
            return b;
        }

        static Bounds WorldBoundsOf(Transform root)
        {
            var list = new List<Transform>();
            foreach (var r in root.GetComponentsInChildren<Renderer>()) list.Add(r.transform);
            return WorldBounds(list);
        }

        HitZone AddBox(Transform parent, string name, Vector3 worldCenter, Vector3 size, ModuleType type)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldCenter;
            go.transform.rotation = Quaternion.identity;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(Mathf.Max(0.6f, size.x), Mathf.Max(0.6f, size.y), Mathf.Max(0.6f, size.z));
            var zone = go.AddComponent<HitZone>();
            zone.type = type;
            zones.Add(zone);
            return zone;
        }

        public void SetBurning(bool on)
        {
            if (on && fires.Count == 0)
            {
                var ps = Fx.MakeSmoke(transform.position + Vector3.up * 1.8f, new Color(0.08f, 0.08f, 0.08f), 7f);
                ps.transform.SetParent(transform, true);
                fires.Add(ps);
                Fx.MakeFire(transform.position + Vector3.up * 1.2f, 1.4f, transform);
            }
            else if (!on)
            {
                foreach (var f in fires) if (f) Destroy(f.gameObject);
                fires.Clear();
            }
        }

        public float HullHeight()
        {
            return WorldBoundsOf(transform).size.y;
        }

        /// <summary>Меши модели — для переключения видимости при обнаружении.</summary>
        public List<Renderer> Renderers()
        {
            var list = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>()) list.Add(r);
            return list;
        }
    }
}
