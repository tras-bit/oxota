using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    /// <summary>Подбираемый объект: ящик на карте, трофей с техники, воздушный груз.</summary>
    public class LootPickup : MonoBehaviour
    {
        public LootItem Item;
        public bool Taken;
        public bool IsAirDrop;
        public bool Falling;
        public float BaseY = float.NaN;
        public float BornTime;
        public float Life = 300f;
        float bobPhase;

        void Start()
        {
            bobPhase = Random.value * 6.28f;
            if (!Falling && float.IsNaN(BaseY)) BaseY = transform.position.y;
        }

        public void Land(float y)
        {
            BaseY = y;
            Falling = false;
        }

        void Update()
        {
            transform.Rotate(Vector3.up, 25f * Time.deltaTime, Space.World);
            if (!Falling)
            {
                if (float.IsNaN(BaseY)) BaseY = transform.position.y;
                var p = transform.position;
                p.y = BaseY + Mathf.Sin(Time.time * 2f + bobPhase) * 0.12f;
                transform.position = p;
            }
            if (Life > 0f && Time.time - BornTime > Life && !IsAirDrop) Destroy(gameObject);
        }

        public void Collect(Vehicle v)
        {
            if (Taken) return;
            Taken = true;
            v.ReceiveLoot(Item);
            Vfx.Dust(transform.position, 0.8f);
            Destroy(gameObject);
        }
    }

    /// <summary>Создание добычи: ящики на карте, трофеи, воздушный груз.</summary>
    public static class LootSystem
    {
        static Transform root;

        static Transform Root
        {
            get
            {
                if (root == null)
                {
                    var go = GameObject.Find("~loot");
                    if (go == null) go = new GameObject("~loot");
                    root = go.transform;
                }
                return root;
            }
        }

        static GameObject MakeVisual(string name, Color color, Vector3 size, bool glow)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(box.GetComponent<Collider>());
            box.transform.SetParent(go.transform, false);
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = MatLib.Metal(color, 0.4f);

            var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(edge.GetComponent<Collider>());
            edge.transform.SetParent(go.transform, false);
            edge.transform.localScale = new Vector3(size.x * 1.05f, size.y * 0.12f, size.z * 1.05f);
            edge.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            edge.GetComponent<Renderer>().sharedMaterial = MatLib.Flat(color * 1.4f, 0.6f, 0.3f);

            var trigger = go.AddComponent<SphereCollider>();
            trigger.radius = 4.2f;
            trigger.isTrigger = true;
            var pickup = go.AddComponent<LootPickup>();
            pickup.BornTime = Time.time;
            if (glow)
            {
                Vfx.Flare(go.transform);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 25f;
                light.intensity = 1.4f;
                light.color = color;
            }
            return go;
        }

        public static LootPickup SpawnCrate(Vector3 pos, int playerLevel)
        {
            var go = MakeVisual("crate", new Color(0.55f, 0.45f, 0.2f), new Vector3(1.5f, 1.3f, 1.5f), false);
            go.transform.position = pos;
            var p = go.GetComponent<LootPickup>();
            p.Item = LootTable.Crate(playerLevel);
            p.Life = 240f;
            return p;
        }

        public static LootPickup SpawnTrophy(Vector3 pos, int victimLevel, float victimMaxHp)
        {
            var go = MakeVisual("trophy", new Color(0.6f, 0.3f, 0.25f), new Vector3(1.7f, 1.1f, 1.3f), false);
            go.transform.position = pos;
            var p = go.GetComponent<LootPickup>();
            p.Item = LootTable.Trophy(victimLevel, victimMaxHp);
            p.Life = 300f;
            return p;
        }

        public static LootPickup SpawnAirDrop(Vector3 pos)
        {
            var go = MakeVisual("airdrop", new Color(0.2f, 0.75f, 0.4f), new Vector3(2.6f, 2.0f, 2.6f), true);
            go.transform.position = pos;
            var p = go.GetComponent<LootPickup>();
            p.Item = LootTable.AirDrop();
            p.IsAirDrop = true;
            p.Life = 0f;
            p.Falling = true;
            // парашютный спуск
            var fall = go.AddComponent<AirDropFall>();
            fall.Pickup = p;
            fall.StartY = pos.y + 120f;
            go.transform.position = new Vector3(pos.x, fall.StartY, pos.z);
            return p;
        }

        public static LootPickup NearestCrate(Vector3 from, float maxDist, Vector2 zoneCenter, float zoneRadius)
        {
            LootPickup best = null;
            float bestDist = maxDist;
            foreach (Transform child in Root)
            {
                var p = child.GetComponent<LootPickup>();
                if (p == null || p.Taken || p.IsAirDrop) continue;
                Vector2 flat = new Vector2(child.position.x, child.position.z);
                if (Vector2.Distance(flat, zoneCenter) > zoneRadius - 60f) continue;   // не бежим в красную зону
                float d = Vector3.Distance(from, child.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }

        public static LootPickup NearestAirDrop(Vector3 from)
        {
            LootPickup best = null;
            float bestDist = float.MaxValue;
            foreach (Transform child in Root)
            {
                var p = child.GetComponent<LootPickup>();
                if (p == null || p.Taken || !p.IsAirDrop) continue;
                float d = Vector3.Distance(from, child.position);
                if (d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        public static void ClearAll()
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
                Object.Destroy(root.GetChild(i).gameObject);
        }
    }

    /// <summary>Спуск воздушного груза на парашюте.</summary>
    public class AirDropFall : MonoBehaviour
    {
        public float StartY;
        public LootPickup Pickup;
        float targetY;
        bool landed;

        void Start()
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(transform.position.x, StartY, transform.position.z), Vector3.down, out hit, 400f))
                targetY = hit.point.y + 1f;
            else targetY = 0f;
        }

        void Update()
        {
            if (landed) return;
            var p = transform.position;
            p.y -= 16f * Time.deltaTime;
            if (p.y <= targetY)
            {
                p.y = targetY;
                landed = true;
                if (Pickup != null) Pickup.Land(targetY);
                Vfx.Dust(transform.position, 2.5f);
                AudioSynth.PlayAt("explosion", transform.position, 0.4f);
            }
            transform.position = p;
        }
    }
}
