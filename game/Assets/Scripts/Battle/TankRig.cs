// Сборка машины из FBX-модели: иерархия корпус/башня/орудие, коллайдеры зон, эффекты.
using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    /// <summary>Готовит модель к бою: делит на корпус/башню/орудие, ставит коллайдеры и HitZone.</summary>
    public class TankRig : MonoBehaviour
    {
        public Transform HullRoot { get; private set; }
        public Transform TurretPivot { get; private set; }
        public Transform GunPivot { get; private set; }
        public Transform GunTip { get; private set; }

        readonly List<ParticleSystem> fires = new List<ParticleSystem>();
        readonly List<HitZone> zones = new List<HitZone>();
        bool built;

        static readonly string[] TurretParts =
        { "_turret", "_mantlet", "_cupola", "_hatch", "_vision", "_sight", "_aa_mg",
          "_basket", "_smoke", "_antenna", "_casemate", "_cas_" };
        static readonly string[] GunParts = { "_gun", "_muzzle" };

        public void Build(TankSpec spec, TankController tank)
        {
            if (built) return;
            built = true;
            zones.Clear();

            var meshes = new List<Transform>();
            foreach (var r in GetComponentsInChildren<Renderer>())
                if (r.transform != transform) meshes.Add(r.transform);

            HullRoot = new GameObject("Hull").transform;
            HullRoot.SetParent(transform, false);
            TurretPivot = new GameObject("TurretPivot").transform;
            TurretPivot.SetParent(transform, false);
            GunPivot = new GameObject("GunPivot").transform;
            GunPivot.SetParent(TurretPivot, false);

            var turretList = new List<Transform>();
            var gunList = new List<Transform>();
            var hullList = new List<Transform>();
            foreach (var m in meshes)
            {
                string n = m.name.ToLowerInvariant();
                if (ContainsAny(n, GunParts)) gunList.Add(m);
                else if (ContainsAny(n, TurretParts)) turretList.Add(m);
                else hullList.Add(m);
            }
            if (turretList.Count == 0 && gunList.Count > 0)
            {   // ПТ-САУ без башни: орудие всё равно наводится по вертикали
                TurretPivot.position = WorldBounds(gunList).center;
            }
            else
            {
                TurretPivot.position = WorldBounds(turretList.Count > 0 ? turretList : meshes).center;
            }

            foreach (var m in hullList) m.SetParent(HullRoot, true);
            foreach (var m in turretList) m.SetParent(TurretPivot, true);
            foreach (var m in gunList) m.SetParent(GunPivot, true);

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

            // ---- коллайдеры и зоны попадания ----
            Bounds hull = WorldBoundsOf(HullRoot);
            AddBox(HullRoot, "hit_hull", hull.center, hull.size * 0.98f, ModuleType.Hull);
            float halfW = Mathf.Max(0.6f, hull.extents.x);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 c = hull.center + new Vector3(s * (halfW + 0.05f), -hull.extents.y * 0.25f, 0f);
                AddBox(HullRoot, "hit_tracks" + s, c,
                       new Vector3(0.75f, hull.size.y * 0.5f, hull.size.z * 0.96f), ModuleType.Tracks);
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

        public static void SafeTag(GameObject go, string tag)
        {
            try { go.tag = tag; } catch { /* тег не объявлен в проекте — не критично */ }
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
