using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    /// <summary>Всплывающие числа урона и подписи в мире (рисуются в OnGUI).</summary>
    public class FloatingText : MonoBehaviour
    {
        class Item
        {
            public Vector3 pos;
            public string text;
            public Color color;
            public float size;
            public float born;
        }

        static FloatingText instance;
        static readonly List<Item> items = new List<Item>();
        GUIStyle style;

        public static void Spawn(Vector3 worldPos, string text, Color color, float size = 1f)
        {
            if (instance == null)
            {
                var go = new GameObject("~floatingtext");
                instance = go.AddComponent<FloatingText>();
                DontDestroyOnLoad(go);
            }
            if (items.Count > 120) items.RemoveAt(0);
            items.Add(new Item { pos = worldPos + Vector3.up * (1.6f + Random.value * 0.6f), text = text, color = color, size = size, born = Time.time });
        }

        void Update()
        {
            for (int i = items.Count - 1; i >= 0; i--)
            {
                items[i].pos += new Vector3(Random.Range(-0.6f, 0.6f), 0.9f, Random.Range(-0.6f, 0.6f)) * Time.deltaTime;
                if (Time.time - items[i].born > 1.8f) items.RemoveAt(i);
            }
        }

        void OnGUI()
        {
            var cam = Camera.main;
            if (cam == null || items.Count == 0) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label);
                style.alignment = TextAnchor.MiddleCenter;
                style.fontStyle = FontStyle.Bold;
            }
            foreach (var it in items)
            {
                Vector3 sp = cam.WorldToScreenPoint(it.pos);
                if (sp.z <= 0f) continue;
                float age = Time.time - it.born;
                float alpha = Mathf.Clamp01(1.5f - age);
                var st = new GUIStyle(style);
                st.fontSize = Mathf.RoundToInt(20f * it.size * (1f + Mathf.Clamp01(age * 2f) * 0.15f));
                st.normal.textColor = new Color(it.color.r, it.color.g, it.color.b, alpha);
                var shadow = new GUIStyle(st);
                shadow.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.7f);
                GUI.Label(new Rect(sp.x - 80f + 1f, Screen.height - sp.y - 20f + 1f, 160f, 30f), it.text, shadow);
                GUI.Label(new Rect(sp.x - 80f, Screen.height - sp.y - 20f, 160f, 30f), it.text, st);
            }
        }
    }
}
