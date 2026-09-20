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

            if (kind == PropKind.Tree)
            {
                // дерево валится в сторону от удара и остаётся лежать как укрытие
                StartCoroutine(FallOver(point));
                return;
            }

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

        /// <summary>Дерево падает: наклоняем ствол вокруг основания и глушим коллайдеры.</summary>
        System.Collections.IEnumerator FallOver(Vector3 point)
        {
            Vector3 dir = transform.position - point;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            dir.Normalize();
            // ось падения — перпендикуляр к направлению удара
            Vector3 axis = Vector3.Cross(Vector3.up, dir).normalized;
            Quaternion from = transform.rotation;
            float t = 0f, dur = 1.1f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                transform.rotation = Quaternion.AngleAxis(80f * k, axis) * from;
                yield return null;
            }
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                r.material.color = new Color(0.32f, 0.28f, 0.20f);   // сухая древесина
                r.enabled = true;
            }
            // щепа на месте сруба
            for (int i = 0; i < 5; i++)
            {
                var chip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chip.transform.position = transform.position + new Vector3(Random.Range(-1.2f, 1.2f), 0.25f,
                                                                          Random.Range(-1.2f, 1.2f));
                chip.transform.localScale = new Vector3(Random.Range(0.3f, 0.9f), Random.Range(0.15f, 0.35f),
                                                        Random.Range(0.3f, 0.9f));
                chip.transform.rotation = Quaternion.Euler(Random.Range(0f, 30f), Random.value * 360f,
                                                           Random.Range(0f, 30f));
                chip.GetComponent<Renderer>().material.color = new Color(0.34f, 0.29f, 0.20f);
                foreach (var c in chip.GetComponents<Collider>()) c.enabled = false;
                Destroy(chip, 25f);
            }
            Destroy(gameObject, 40f);
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
