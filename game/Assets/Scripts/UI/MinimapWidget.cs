// Миникарта: игрок, напарник, обнаруженные противники, воздушные грузы, граница зоны.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Samsar
{
    public class MinimapWidget : MonoBehaviour
    {
        public const float MapExtent = 1700f;      // половина отображаемой области, м
        public const float Size = 230f;            // размер карты на экране, px

        Image bg, zoneImage, hardZoneImage;
        RectTransform root;
        readonly Dictionary<TankController, Image> tankMarkers = new Dictionary<TankController, Image>();
        readonly List<Marker> tempMarkers = new List<Marker>();
        Sprite ring, dot, square;
        Font font;

        class Marker
        {
            public Image img;
            public Vector3 world;
            public float until;
            public Color color;
        }

        public void Build(Transform canvas, Font f)
        {
            font = f;
            var go = new GameObject("Minimap");
            go.transform.SetParent(canvas, false);
            root = go.AddComponent<RectTransform>();
            root.anchorMin = root.anchorMax = new Vector2(1f, 0f);
            root.pivot = new Vector2(1f, 0f);
            root.anchoredPosition = new Vector2(-24, 24);
            root.sizeDelta = new Vector2(Size, Size);

            bg = NewImage(root, new Color(0.07f, 0.09f, 0.1f, 0.82f), new Vector2(Size, Size), Vector2.zero);
            dot = MakeCircleSprite(16, false);
            square = MakeSquareSprite();
            ring = MakeCircleSprite(128, true);

            zoneImage = NewImage(root, new Color(1f, 0.85f, 0.2f, 0.55f), new Vector2(100, 100), Vector2.zero);
            zoneImage.sprite = ring;
            zoneImage.type = Image.Type.Simple;
            hardZoneImage = NewImage(root, new Color(1f, 0.25f, 0.15f, 0.8f), new Vector2(100, 100), Vector2.zero);
            hardZoneImage.sprite = ring;
            hardZoneImage.type = Image.Type.Simple;

            var label = NewText(root, "Карта 3×3 км", 13, new Vector2(Size * 0.5f, -14f), Color.white);
            label.alignment = TextAnchor.MiddleCenter;
        }

        Image NewImage(Transform parent, Color c, Vector2 size, Vector2 pos)
        {
            var go = new GameObject("img");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return img;
        }

        Text NewText(Transform parent, string s, int size, Vector2 pos, Color c)
        {
            var go = new GameObject("txt");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = s;
            t.fontSize = size;
            t.color = c;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(Size, 20);
            rt.anchoredPosition = pos;
            return t;
        }

        static Sprite MakeCircleSprite(int res, bool outline)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            var px = new Color[res * res];
            float half = res * 0.5f;
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half)) / half;
                    float a = outline ? Mathf.Clamp01((1f - Mathf.Abs(d - 0.94f) * 26f)) : Mathf.Clamp01((1f - d) * 9f);
                    px[y * res + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeSquareSprite()
        {
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var px = new Color[64];
            for (int i = 0; i < 64; i++) px[i] = new Color(1f, 1f, 1f, 1f);
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f));
        }

        public static void MarkAirDrop(Vector3 pos) { if (HUD.Instance != null) HUD.Instance.GetComponent<MinimapWidget>().AddMark(pos, new Color(0.2f, 0.8f, 1f), 300f); }
        public static void MarkAbility(Vector3 pos) { if (HUD.Instance != null) HUD.Instance.GetComponent<MinimapWidget>().AddMark(pos, new Color(1f, 0.4f, 0.2f), 8f); }

        void AddMark(Vector3 world, Color color, float life)
        {
            var img = NewImage(root, color, new Vector2(14, 14), Vector2.zero);
            img.sprite = dot;
            tempMarkers.Add(new Marker { img = img, world = world, until = Time.time + life, color = color });
        }

        Vector2 ToMap(Vector3 world)
        {
            float scale = Size / (MapExtent * 2f);
            return new Vector2(world.x * scale, world.z * scale);
        }

        public void Refresh(TankController player)
        {
            if (ZoneController.Instance != null)
            {
                var z = ZoneController.Instance;
                float scale = Size / (MapExtent * 2f);
                zoneImage.rectTransform.sizeDelta = Vector2.one * (z.YellowRadius * 2f * scale);
                hardZoneImage.rectTransform.sizeDelta = Vector2.one * (z.RedRadius * 2f * scale);
            }

            foreach (var t in TankRegistry.All)
            {
                Image marker;
                if (!tankMarkers.TryGetValue(t, out marker) || marker == null)
                {
                    marker = NewImage(root, Color.white, new Vector2(12, 12), Vector2.zero);
                    marker.sprite = square;
                    tankMarkers[t] = marker;
                }
                if (t.Dead)
                {
                    marker.color = new Color(0.4f, 0.4f, 0.4f, 0.5f);
                }
                else if (t.IsPlayer) marker.color = new Color(1f, 0.9f, 0.3f);
                else if (t.IsAlly) marker.color = new Color(0.4f, 1f, 0.5f);
                else marker.color = t.vision != null && t.vision.VisibleToPlayer ? new Color(1f, 0.35f, 0.3f)
                                                                                 : new Color(0f, 0f, 0f, 0f);
                marker.rectTransform.anchoredPosition = ToMap(t.transform.position);
                marker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -t.transform.eulerAngles.y);
            }

            for (int i = tempMarkers.Count - 1; i >= 0; i--)
            {
                var m = tempMarkers[i];
                if (Time.time > m.until)
                {
                    Destroy(m.img.gameObject);
                    tempMarkers.RemoveAt(i);
                    continue;
                }
                m.img.rectTransform.anchoredPosition = ToMap(m.world);
                var c = m.color;
                c.a = Mathf.PingPong(Time.time * 2f, 1f) * 0.8f + 0.2f;
                m.img.color = c;
            }
        }
    }
}
