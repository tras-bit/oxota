using UnityEngine;

namespace Samsar
{
    /// <summary>
    /// Разрушаемый объект окружения: дома, заборы, деревья. Есть два состояния — целое и обломки.
    /// </summary>
    public class Destructible : MonoBehaviour
    {
        public float Health = 1200f;
        public bool Tall;
        public Vector3 Size = Vector3.one;
        public Color DebrisColor = new Color(0.35f, 0.33f, 0.3f);
        bool broken;

        public void Setup(float health, Vector3 size, Color debris, bool tall = false)
        {
            Health = health;
            Size = size;
            DebrisColor = debris;
            Tall = tall;
        }

        public void Hit(float damage, Vector3 point, Vector3 dir)
        {
            if (broken) return;
            Health -= damage;
            if (Health <= 0f) Break(dir);
        }

        void Break(Vector3 dir)
        {
            broken = true;
            var battle = BattleManager.Instance;
            // заменить на обломки
            var go = new GameObject("debris");
            go.transform.SetParent(transform.parent, false);
            go.transform.position = transform.position;
            go.transform.rotation = transform.rotation;

            float h = Size.y;
            Vector3 flat = new Vector3(Size.x, h * 0.35f, Size.z);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(200f, Size.x * Size.y * Size.z * 40f);
            rb.drag = 0.6f;
            rb.angularDrag = 1.2f;
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.SetParent(go.transform, false);
            box.transform.localPosition = new Vector3(0f, flat.y * 0.5f, 0f);
            box.transform.localScale = flat;
            box.GetComponent<Renderer>().sharedMaterial = MatLib.Metal(DebrisColor * 0.9f, 0.1f);
            var col = go.AddComponent<BoxCollider>();
            col.size = flat;
            col.center = new Vector3(0f, flat.y * 0.5f, 0f);

            Vfx.Explosion(transform.position + Vector3.up * h * 0.4f, 1.2f);
            Vfx.Smoke(transform.position + Vector3.up * h * 0.5f, 4f, 2f, 2f);
            AudioSynth.PlayAt("explosion", transform.position, 0.7f);

            // обломки исчезают со временем (чтобы не копить физику)
            var k = go.AddComponent<AutoDestroy>();
            k.life = 45f;
            if (battle != null) battle.ReportDestruction();

            Destroy(gameObject);
        }
    }
}
