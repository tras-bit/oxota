// Разрушаемые объекты окружения: заборы, блоки, деревья, дома, контейнеры.
using UnityEngine;

namespace Samsar
{
    public class DestructibleProp : MonoBehaviour
    {
        public float hp = 500f;
        public bool spawnRubble = true;
        public PropKind kind = PropKind.Small;
        public GameObject rubbleModel;

        public enum PropKind { Small, Tree, Building, Container }

        float maxHp;
        bool broken;

        void Awake()
        {
            maxHp = hp;
        }

        public void TakeHit(float damage, Vector3 point)
        {
            if (broken) return;
            hp -= damage;
            Fx.Impact(point, Vector3.up, false);
            if (hp <= 0f) Break(point);
        }

        void Break(Vector3 point)
        {
            broken = true;
            Fx.Explosion(transform.position + Vector3.up * 1.5f, kind == PropKind.Building ? 1.4f : 0.7f);

            if (spawnRubble && kind == PropKind.Building)
            {
                // дом «оседает»: убираем верхние части, оставляем обломки
                foreach (var r in GetComponentsInChildren<Renderer>())
                {
                    if (r.transform.position.y > transform.position.y + 1.5f)
                        r.enabled = false;
                }
                SpawnRubble(transform.position);
            }
            else
            {
                SpawnRubble(transform.position);
                Destroy(gameObject, 0.2f);
            }
        }

        void SpawnRubble(Vector3 pos)
        {
            if (rubbleModel != null)
            {
                var go = Instantiate(rubbleModel, pos, Quaternion.Euler(0f, Random.value * 360f, 0f));
                go.name = "Rubble_" + name;
                foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.transform.position = pos + new Vector3(Random.Range(-1f, 1f), 0.3f, Random.Range(-1f, 1f));
                    cube.transform.localScale = new Vector3(Random.Range(0.6f, 1.6f), Random.Range(0.3f, 0.8f), Random.Range(0.6f, 1.6f));
                    cube.transform.rotation = Quaternion.Euler(Random.Range(0f, 20f), Random.value * 360f, Random.Range(0f, 20f));
                    cube.GetComponent<Renderer>().material.color = new Color(0.35f, 0.33f, 0.3f);
                    foreach (var c in cube.GetComponents<Collider>()) c.enabled = false;
                    Destroy(cube, 12f);
                }
            }
        }

        public float HealthFraction => Mathf.Clamp01(hp / Mathf.Max(1f, maxHp));
    }
}
