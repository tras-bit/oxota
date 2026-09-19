using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    /// <summary>
    /// Карта 3x3 км: холмы, река с мостом, заброшенный город, деревня, лес, поля,
    /// железнодорожная станция с поездом. Всё окружение разрушаемое (кроме земли и рельсов).
    /// </summary>
    public class MapGenerator : MonoBehaviour
    {
        public const float Size = GameConfig.MapSize;
        public const int Res = GameConfig.TerrainRes;

        public float[,] Heights;
        public Texture2D Minimap;
        public Material GroundMaterial;

        GameObject root;
        Transform props;
        Vector3 cityCenter, stationCenter, villageCenter;
        float cityRadius, stationRadius;
        readonly List<Vector3> placedPoints = new List<Vector3>();

        static readonly Color Grass = new Color(0.30f, 0.36f, 0.22f);
        static readonly Color GrassDry = new Color(0.40f, 0.40f, 0.24f);
        static readonly Color Dirt = new Color(0.42f, 0.36f, 0.27f);
        static readonly Color Sand = new Color(0.55f, 0.50f, 0.36f);
        static readonly Color Water = new Color(0.16f, 0.24f, 0.28f);

        public void Build(int seed)
        {
            Random.InitState(seed);
            Noise.Seed(seed);
            if (root != null) Destroy(root);
            root = new GameObject("Map");
            props = new GameObject("Props").transform;
            props.SetParent(root.transform, false);
            placedPoints.Clear();
            Minimap = new Texture2D(256, 256, TextureFormat.RGBA32, false);

            Debug.Log("[Samsar] Генерация карты, seed = " + seed);
            float t0 = Time.realtimeSinceStartup;

            GenerateHeights(seed);
            BuildTerrainMesh();
            StampBaseMinimap();

            cityCenter = new Vector3(-Size * 0.26f, 0f, Size * 0.22f);
            stationCenter = new Vector3(Size * 0.30f, 0f, -Size * 0.24f);
            villageCenter = new Vector3(Size * 0.28f, 0f, Size * 0.26f);
            cityRadius = 520f;
            stationRadius = 300f;

            Flatten(cityCenter, cityRadius * 0.75f, cityRadius * 1.15f);
            Flatten(stationCenter, stationRadius * 0.9f, stationRadius * 1.4f);
            Flatten(villageCenter, 200f, 320f);

            BuildRailway();
            BuildCity();
            BuildStation();
            BuildVillage();
            BuildRoads();
            BuildForest();
            BuildFields();
            BuildRocks();

            Minimap.Apply();
            Debug.Log(string.Format("[Samsar] Карта готова за {0:F1} c. Объектов окружения: {1}", Time.realtimeSinceStartup - t0, props.childCount));
        }

        // ==================== рельеф ====================

        void GenerateHeights(int seed)
        {
            Heights = new float[Res + 1, Res + 1];
            float cs = Size / Res;
            for (int z = 0; z <= Res; z++)
            {
                for (int x = 0; x <= Res; x++)
                {
                    float wx = x * cs - Size * 0.5f;
                    float wz = z * cs - Size * 0.5f;
                    float mountains = (wx - 1350f) / 420f;
                    float am = Mathf.Clamp01(1f - mountains);
                    float bm = Mathf.Clamp01((wx - 1650f) / 300f);
                    float h = 0f;
                    if (am > 0f)
                        h += Noise.Fbm(wx * 0.00045f, wz * 0.00045f, 5) * 46f * (am * am * (3f - 2f * am));
                    if (bm > 0f)
                        h += Noise.Fbm(wx * 0.0011f, wz * 0.0011f, 4) * 85f * bm * bm;
                    h += Noise.Fbm(wx * 0.0035f, wz * 0.0035f, 3) * 7f;
                    h += Noise.Perlin(wx * 0.012f, wz * 0.012f) * 1.5f;
                    Heights[x, z] = h;
                }
            }
        }

        public float SampleHeight(float worldX, float worldZ)
        {
            float u = (worldX + Size * 0.5f) / Size * Res;
            float v = (worldZ + Size * 0.5f) / Size * Res;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, Res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, Res - 1);
            float fx = Mathf.Clamp01(u - x0);
            float fz = Mathf.Clamp01(v - z0);
            float h00 = Heights[x0, z0], h10 = Heights[x0 + 1, z0], h01 = Heights[x0, z0 + 1], h11 = Heights[x0 + 1, z0 + 1];
            return Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fz);
        }

        void Flatten(Vector3 center, float flatRadius, float falloff)
        {
            float cs = Size / Res;
            for (int z = 0; z <= Res; z++)
            {
                for (int x = 0; x <= Res; x++)
                {
                    float wx = x * cs - Size * 0.5f;
                    float wz = z * cs - Size * 0.5f;
                    float d = Vector2.Distance(new Vector2(wx, wz), new Vector2(center.x, center.z));
                    if (d > falloff) continue;
                    float k = d <= flatRadius ? 0f : Mathf.Clamp01((d - flatRadius) / (falloff - flatRadius));
                    k = k * k * (3f - 2f * k);
                    Heights[x, z] = Mathf.Lerp(Heights[x, z], 0f, 1f - k);
                }
            }
        }

        void BuildTerrainMesh()
        {
            var mb = new MeshBuilder();
            int[] idx = new int[(Res + 1) * (Res + 1)];
            float cs = Size / Res;
            var groundTex = BuildGroundTexture();

            for (int z = 0; z <= Res; z++)
            {
                for (int x = 0; x <= Res; x++)
                {
                    float wx = x * cs - Size * 0.5f;
                    float wz = z * cs - Size * 0.5f;
                    float h = Heights[x, z];
                    var uv = new Vector2(x / (float)Res, z / (float)Res);
                    var color = ColorForHeight(h);
                    int i = mb.AddVertex(new Vector3(wx, h, wz), uv, color);
                    idx[z * (Res + 1) + x] = i;
                }
            }
            for (int z = 0; z < Res; z++)
            {
                for (int x = 0; x < Res; x++)
                {
                    int a = idx[z * (Res + 1) + x];
                    int b = idx[z * (Res + 1) + x + 1];
                    int c = idx[(z + 1) * (Res + 1) + x + 1];
                    int d = idx[(z + 1) * (Res + 1) + x];
                    mb.AddTriangle(a, d, c);
                    mb.AddTriangle(a, c, b);
                }
            }

            var go = new GameObject("Terrain");
            go.transform.SetParent(root.transform, false);
            var mesh = mb.ToMesh();
            go.AddComponent<MeshFilter>().mesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            GroundMaterial = MatLib.Ground(Color.white);
            if (GroundMaterial.HasProperty("_MainTex")) GroundMaterial.SetTexture("_MainTex", groundTex);
            GroundMaterial.mainTextureScale = Vector2.one;
            rend.sharedMaterial = GroundMaterial;
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            go.isStatic = true;
        }

        Color ColorForHeight(float h)
        {
            if (h < 0.4f) return Water;
            if (h < 1.8f) return Sand;
            float t = Mathf.InverseLerp(2f, 26f, h);
            return Color.Lerp(Grass, Dirt, Mathf.Clamp01(t * 0.8f));
        }

        /// <summary>Текстура земли 512x512 по рельефу (трава, грязь, песок, вода).</summary>
        Texture2D BuildGroundTexture()
        {
            const int S = 512;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float wx = x / (float)S * Size - Size * 0.5f;
                    float wz = y / (float)S * Size - Size * 0.5f;
                    float h = SampleHeight(wx, wz);
                    Color c = ColorForHeight(h);
                    float detail = Noise.Fbm(wx * 0.02f, wz * 0.02f, 4) * 0.5f + 0.5f;
                    c *= 0.85f + detail * 0.3f;
                    float patch = Noise.Fbm(wx * 0.004f, wz * 0.004f, 3);
                    if (patch > 0.15f) c = Color.Lerp(c, GrassDry, Mathf.Clamp01((patch - 0.15f) * 2f));
                    if (h < 0.6f)
                    {
                        float shore = Mathf.Clamp01((0.6f - h) * 1.5f);
                        c = Color.Lerp(c, Water * 1.1f, shore);
                    }
                    px[y * S + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        void StampBaseMinimap()
        {
            for (int y = 0; y < Minimap.height; y++)
            {
                for (int x = 0; x < Minimap.width; x++)
                {
                    float wx = x / (float)Minimap.width * Size - Size * 0.5f;
                    float wz = y / (float)Minimap.height * Size - Size * 0.5f;
                    float h = SampleHeight(wx, wz);
                    Color c = ColorForHeight(h) * 0.85f;
                    Minimap.SetPixel(x, y, c);
                }
            }
        }

        void StampMinimap(Vector3 worldPos, float radius, Color color)
        {
            float u = (worldPos.x / Size + 0.5f) * Minimap.width;
            float v = (worldPos.z / Size + 0.5f) * Minimap.height;
            int r = Mathf.CeilToInt(radius / Size * Minimap.width);
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    int px = Mathf.RoundToInt(u) + x;
                    int py = Mathf.RoundToInt(v) + y;
                    if (px < 0 || py < 0 || px >= Minimap.width || py >= Minimap.height) continue;
                    if (x * x + y * y > r * r) continue;
                    Minimap.SetPixel(px, py, color);
                }
            }
        }

        // ==================== объекты ====================

        GameObject Proto(PrimitiveType type, Transform parent, string name, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!collider)
            {
                var c = go.GetComponent<Collider>();
                if (c != null) DestroyImmediate(c);
            }
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return go;
        }

        /// <summary>Коробка-здание с толщиной стен и проёмами (можно заехать внутрь).</summary>
        GameObject Building(Vector3 center, Vector2 size, float height, float rotY, Color wallColor, bool hollow, bool destructible)
        {
            var holder = Proto(PrimitiveType.Cube, props, "building", MatLib.Metal(wallColor, 0.15f), false);
            holder.transform.position = center + Vector3.up * height * 0.5f;
            holder.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
            holder.transform.localScale = new Vector3(size.x, height, size.y);
            var wallMat = MatLib.Metal(wallColor, 0.12f);
            holder.GetComponent<Renderer>().sharedMaterial = wallMat;

            // крыша
            var roof = Proto(PrimitiveType.Cube, holder.transform, "roof", MatLib.Metal(wallColor * 0.7f, 0.2f), false);
            roof.transform.localPosition = new Vector3(0f, 0.52f, 0f);
            roof.transform.localScale = new Vector3(1.06f, 0.12f, 1.06f);

            if (hollow)
            {
                // делаем комнату: убираем сплошной объём, ставим стены
                var col = holder.GetComponent<Collider>();
                if (col != null) DestroyImmediate(col);
                var holderRend = holder.GetComponent<Renderer>();
                if (holderRend != null) holderRend.enabled = false;
                float t = 0.45f;                     // толщина стены
                float gap = size.x * 0.34f;          // проём
                // боковые стены
                CreateWall(holder.transform, new Vector3(-0.5f + t * 0.5f / size.x, 0f, 0f), new Vector3(t, height, size.y));
                CreateWall(holder.transform, new Vector3(0.5f - t * 0.5f / size.x, 0f, 0f), new Vector3(t, height, size.y));
                // задняя стена
                CreateWall(holder.transform, new Vector3(0f, 0f, 0.5f - t * 0.5f / size.y), new Vector3(size.x, height, t));
                // передняя стена с проёмом
                float side = (size.x - gap) * 0.5f;
                CreateWall(holder.transform, new Vector3(-(gap * 0.5f + side * 0.5f) / size.x, 0f, -0.5f + t * 0.5f / size.y), new Vector3(side, height, t));
                CreateWall(holder.transform, new Vector3((gap * 0.5f + side * 0.5f) / size.x, 0f, -0.5f + t * 0.5f / size.y), new Vector3(side, height, t));
            }
            else
            {
                var box = holder.GetComponent<BoxCollider>();
                if (box == null) box = holder.AddComponent<BoxCollider>();
                box.size = Vector3.one;
            }

            if (destructible)
            {
                var d = holder.AddComponent<Destructible>();
                d.Setup(900f + height * 120f, new Vector3(size.x, height, size.y), wallColor, true);
            }

            StampMinimap(center, Mathf.Max(size.x, size.y) * 0.6f, new Color(0.35f, 0.33f, 0.3f));
            placedPoints.Add(center);
            return holder;
        }

        void CreateWall(Transform parent, Vector3 localPos, Vector3 worldSize)
        {
            var w = Proto(PrimitiveType.Cube, parent, "wall", MatLib.Metal(new Color(0.4f, 0.38f, 0.34f), 0.1f), true);
            w.transform.localPosition = localPos + new Vector3(0f, 0f, 0f);
            var ls = parent.localScale;
            w.transform.localScale = new Vector3(worldSize.x / ls.x, worldSize.y / ls.y, worldSize.z / ls.z);
        }

        GameObject Fence(Vector3 from, Vector3 to, Color color)
        {
            Vector3 mid = (from + to) * 0.5f;
            float len = Vector3.Distance(from, to);
            var f = Proto(PrimitiveType.Cube, props, "fence", MatLib.Metal(color, 0.08f), true);
            f.transform.position = mid + Vector3.up * 0.9f;
            f.transform.rotation = Quaternion.LookRotation((to - from).normalized, Vector3.up);
            f.transform.localScale = new Vector3(0.15f, 1.8f, len);
            var d = f.AddComponent<Destructible>();
            d.Setup(160f, new Vector3(0.15f, 1.8f, len), color);
            return f;
        }

        GameObject Tree(Vector3 pos, Color leaf)
        {
            var holder = Proto(PrimitiveType.Cylinder, props, "tree", MatLib.Flat(new Color(0.22f, 0.17f, 0.12f), 0.1f), true);
            float h = Random.Range(7f, 14f);
            holder.transform.position = pos + Vector3.up * h * 0.35f;
            holder.transform.localScale = new Vector3(0.55f, h * 0.35f, 0.55f);
            var crown = Proto(PrimitiveType.Sphere, holder.transform, "crown", MatLib.Flat(leaf, 0.05f), false);
            crown.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            crown.transform.localScale = new Vector3(3.2f, 2.0f, 3.2f);
            var d = holder.AddComponent<Destructible>();
            d.Setup(220f + h * 12f, new Vector3(3f, h, 3f), new Color(0.25f, 0.3f, 0.18f), true);
            return holder;
        }

        void BuildCity()
        {
            int blocks = 5;
            float blockSize = cityRadius * 2f / blocks;
            for (int bx = 0; bx < blocks; bx++)
            {
                for (int bz = 0; bz < blocks; bz++)
                {
                    Vector3 blockCenter = cityCenter + new Vector3((bx - (blocks - 1) * 0.5f) * blockSize, 0f, (bz - (blocks - 1) * 0.5f) * blockSize);
                    if (Random.value < 0.12f) continue;     // пустыри/парки
                    int perSide = 2;
                    float cell = blockSize * 0.42f;
                    for (int i = 0; i < perSide; i++)
                    {
                        for (int j = 0; j < perSide; j++)
                        {
                            Vector3 p = blockCenter + new Vector3((i - 0.5f) * cell, 0f, (j - 0.5f) * cell);
                            p.x += Random.Range(-6f, 6f);
                            p.z += Random.Range(-6f, 6f);
                            float w = Random.Range(16f, 26f);
                            float l = Random.Range(16f, 30f);
                            float h = Random.Range(7f, 17f) * (Random.value < 0.2f ? 1.6f : 1f);
                            bool hollow = Random.value < 0.45f;
                            Color wall = Random.value < 0.5f ? new Color(0.45f, 0.42f, 0.36f) : new Color(0.38f, 0.36f, 0.33f);
                            Building(new Vector3(FromGround(p).x, SampleHeight(p.x, p.z), p.z), new Vector2(w, l), h, Random.Range(0f, 90f), wall, hollow, Random.value < 0.8f);
                        }
                    }
                    // заборы по краям квартала
                    if (Random.value < 0.5f)
                    {
                        float s = blockSize * 0.5f;
                        Vector3 a = blockCenter + new Vector3(-s, 0f, -s);
                        Vector3 b = blockCenter + new Vector3(s, 0f, -s);
                        Fence(FromGround(a), FromGround(b), new Color(0.3f, 0.3f, 0.28f));
                    }
                }
            }
            // городская площадь с памятником
            var monument = Proto(PrimitiveType.Cube, props, "monument_base", MatLib.Metal(new Color(0.4f, 0.4f, 0.4f), 0.3f), true);
            monument.transform.position = FromGround(cityCenter) + Vector3.up * 1.5f;
            monument.transform.localScale = new Vector3(10f, 3f, 10f);
            var obelisk = Proto(PrimitiveType.Cube, monument.transform, "obelisk", MatLib.Metal(new Color(0.45f, 0.42f, 0.4f), 0.4f), false);
            obelisk.transform.localPosition = new Vector3(0f, 3.4f, 0f);
            obelisk.transform.localScale = new Vector3(0.22f, 2.4f, 0.22f);
            StampMinimap(cityCenter, cityRadius, new Color(0.3f, 0.29f, 0.27f));
        }

        void BuildStation()
        {
            // рельсы идут поперёк юго-восточного угла
            var dir = new Vector3(1f, 0f, 0.35f).normalized;
            var side = new Vector3(-dir.z, 0f, dir.x);
            for (int track = -1; track <= 1; track++)
            {
                var points = new List<Vector3>();
                for (int i = -14; i <= 14; i++)
                {
                    Vector3 p = stationCenter + dir * (i * 34f) + side * (track * 8f);
                    points.Add(FromGround(p) + Vector3.up * 0.25f);
                }
                var rail = Proto(PrimitiveType.Cube, props, "rail", MatLib.Metal(new Color(0.35f, 0.34f, 0.33f), 0.5f), false);
                rail.transform.position = points[points.Count / 2];
                rail.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                rail.transform.localScale = new Vector3(3.2f, 0.5f, 900f);
                var ballast = Proto(PrimitiveType.Cube, props, "ballast", MatLib.Flat(new Color(0.28f, 0.26f, 0.24f), 0.05f), false);
                ballast.transform.position = rail.transform.position - Vector3.up * 0.3f;
                ballast.transform.rotation = rail.transform.rotation;
                ballast.transform.localScale = new Vector3(6f, 0.6f, 920f);
            }

            // перрон и склады
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = stationCenter + dir * (i * 70f - 70f) + side * -34f;
                var platform = Proto(PrimitiveType.Cube, props, "platform", MatLib.Metal(new Color(0.42f, 0.4f, 0.37f), 0.1f), true);
                platform.transform.position = FromGround(p) + Vector3.up * 1.1f;
                platform.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                platform.transform.localScale = new Vector3(14f, 2.2f, 66f);
            }
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = stationCenter + dir * (i * 90f - 135f) + side * -58f;
                Building(FromGround(p), new Vector2(Random.Range(26f, 40f), Random.Range(14f, 20f)), Random.Range(9f, 14f), Random.Range(0f, 180f), new Color(0.36f, 0.34f, 0.32f), Random.value < 0.5f, true);
            }

            // поезд: локомотив и вагоны
            var trainColor = new Color(0.22f, 0.26f, 0.25f);
            for (int i = 0; i < 9; i++)
            {
                Vector3 p = stationCenter + dir * (i * 22f - 96f) + side * 8f;
                var wagon = Proto(PrimitiveType.Cube, props, i == 0 ? "locomotive" : "wagon", MatLib.Metal(i == 0 ? trainColor * 1.1f : trainColor, 0.25f), true);
                wagon.transform.position = FromGround(p) + Vector3.up * 2.6f;
                wagon.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                wagon.transform.localScale = new Vector3(4.6f, 3.6f, i == 0 ? 22f : 20f);
                var d = wagon.AddComponent<Destructible>();
                d.Setup(1500f, wagon.transform.localScale, trainColor, true);
                // колёсные тележки
                for (int w = -1; w <= 1; w += 2)
                {
                    var wheels = Proto(PrimitiveType.Cylinder, wagon.transform, "wheels", MatLib.Flat(new Color(0.1f, 0.1f, 0.1f), 0.3f), false);
                    wheels.transform.localPosition = new Vector3(0f, -0.5f, w * 0.33f);
                    wheels.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    wheels.transform.localScale = new Vector3(0.9f, 0.42f, 0.9f);
                }
            }

            // водонапорная башня
            Vector3 towerPos = stationCenter + side * 45f + dir * 40f;
            var towerBase = Proto(PrimitiveType.Cylinder, props, "watertower", MatLib.Metal(new Color(0.4f, 0.36f, 0.32f), 0.2f), true);
            towerBase.transform.position = FromGround(towerPos) + Vector3.up * 9f;
            towerBase.transform.localScale = new Vector3(9f, 9f, 9f);
            var tank = Proto(PrimitiveType.Cylinder, towerBase.transform, "tank", MatLib.Metal(new Color(0.3f, 0.34f, 0.32f), 0.3f), false);
            tank.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            tank.transform.localScale = new Vector3(0.85f, 0.5f, 0.85f);
            var td = towerBase.AddComponent<Destructible>();
            td.Setup(900f, new Vector3(9f, 18f, 9f), new Color(0.4f, 0.36f, 0.32f), true);

            StampMinimap(stationCenter, stationRadius, new Color(0.33f, 0.32f, 0.3f));
            StampMinimap(stationCenter, stationRadius * 0.4f, new Color(0.28f, 0.27f, 0.26f));
        }

        void BuildVillage()
        {
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                Vector3 p = villageCenter + new Vector3(Mathf.Cos(a) * Random.Range(60f, 220f), 0f, Mathf.Sin(a) * Random.Range(60f, 200f));
                var house = Proto(PrimitiveType.Cube, props, "house", MatLib.Metal(new Color(0.44f, 0.4f, 0.34f), 0.1f), true);
                float w = Random.Range(10f, 16f), l = Random.Range(12f, 20f), h = Random.Range(5f, 8f);
                house.transform.position = FromGround(p) + Vector3.up * h * 0.5f;
                house.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 180f), 0f);
                house.transform.localScale = new Vector3(w, h, l);
                var roof = Proto(PrimitiveType.Cube, house.transform, "roof", MatLib.Metal(new Color(0.32f, 0.25f, 0.2f), 0.15f), false);
                roof.transform.localPosition = new Vector3(0f, 0.62f, 0f);
                roof.transform.localScale = new Vector3(1.1f, 0.3f, 1.1f);
                var d = house.AddComponent<Destructible>();
                d.Setup(700f, new Vector3(w, h, l), new Color(0.44f, 0.4f, 0.34f), true);
                // забор вокруг дома
                if (Random.value < 0.6f)
                {
                    float s = Mathf.Max(w, l) * 0.9f;
                    Vector3 c = FromGround(p);
                    var c1 = c + new Vector3(-s, 0f, -s);
                    var c2 = c + new Vector3(s, 0f, -s);
                    var c3 = c + new Vector3(s, 0f, s);
                    Fence(c1, c2, new Color(0.32f, 0.3f, 0.26f));
                    if (Random.value < 0.5f) Fence(c2, c3, new Color(0.32f, 0.3f, 0.26f));
                }
            }
            StampMinimap(villageCenter, 260f, new Color(0.34f, 0.32f, 0.28f));
        }

        void BuildForest()
        {
            int count = GameConfig.Quality == 0 ? 420 : GameConfig.Quality == 1 ? 700 : 950;
            int placed = 0;
            int guard = 0;
            while (placed < count && guard++ < count * 12)
            {
                Vector3 p = RandomPointInMap(Size * 0.47f);
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(cityCenter.x, cityCenter.z)) < cityRadius * 1.1f) continue;
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(stationCenter.x, stationCenter.z)) < stationRadius * 1.15f) continue;
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(villageCenter.x, villageCenter.z)) < 240f) continue;
                float cluster = Noise.Fbm(p.x * 0.0016f, p.z * 0.0016f, 3);
                if (cluster < 0.05f) continue;
                if (SampleHeight(p.x, p.z) < 1f) continue;
                Color leaf = Random.value < 0.45f ? new Color(0.16f, 0.24f, 0.13f) : Random.value < 0.5f
                    ? new Color(0.24f, 0.3f, 0.15f) : new Color(0.3f, 0.33f, 0.18f);
                Tree(FromGround(p), leaf);
                StampMinimap(p, 6f, new Color(0.17f, 0.22f, 0.14f));
                placed++;
            }
        }

        void BuildFields()
        {
            // поля: квадраты с заборами и стогами
            for (int f = 0; f < 7; f++)
            {
                Vector3 c = RandomPointInMap(Size * 0.4f);
                if (Vector2.Distance(new Vector2(c.x, c.z), new Vector2(cityCenter.x, cityCenter.z)) < cityRadius) continue;
                float s = Random.Range(70f, 130f);
                Vector3 a = FromGround(c + new Vector3(-s, 0f, -s));
                Vector3 b = FromGround(c + new Vector3(s, 0f, -s));
                Vector3 d2 = FromGround(c + new Vector3(s, 0f, s));
                Vector3 e = FromGround(c + new Vector3(-s, 0f, s));
                Fence(a, b, new Color(0.3f, 0.28f, 0.24f));
                Fence(b, d2, new Color(0.3f, 0.28f, 0.24f));
                Fence(d2, e, new Color(0.3f, 0.28f, 0.24f));
                Fence(e, a, new Color(0.3f, 0.28f, 0.24f));
                int hay = Random.Range(3, 9);
                for (int h = 0; h < hay; h++)
                {
                    Vector3 p = c + new Vector3(Random.Range(-s * 0.8f, s * 0.8f), 0f, Random.Range(-s * 0.8f, s * 0.8f));
                    var haystack = Proto(PrimitiveType.Cylinder, props, "haystack", MatLib.Flat(new Color(0.55f, 0.48f, 0.25f), 0.05f), true);
                    haystack.transform.position = FromGround(p) + Vector3.up * 1.7f;
                    haystack.transform.localScale = new Vector3(3.4f, 3.4f, 3.4f);
                    var d = haystack.AddComponent<Destructible>();
                    d.Setup(400f, new Vector3(3.4f, 3.4f, 3.4f), new Color(0.55f, 0.48f, 0.25f));
                }
            }
        }

        void BuildRoads()
        {
            var mb = new MeshBuilder();
            var roadColor = new Color(0.22f, 0.21f, 0.2f);
            // город — станция
            AddRoad(mb, cityCenter, stationCenter, 12f);
            // город — деревня
            AddRoad(mb, cityCenter, villageCenter, 10f);
            // станция — деревня (дуга)
            AddRoad(mb, stationCenter, villageCenter, 9f);
            // подъезды к центру карты
            AddRoad(mb, Vector3.zero, cityCenter, 11f);
            AddRoad(mb, Vector3.zero, stationCenter, 11f);

            var go = new GameObject("Roads");
            go.transform.SetParent(root.transform, false);
            var mesh = mb.ToMesh(false);
            go.AddComponent<MeshFilter>().mesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = MatLib.Flat(roadColor, 0.05f);
            StampMinimap(cityCenter, 40f, new Color(0.22f, 0.21f, 0.2f));
        }

        void AddRoad(MeshBuilder mb, Vector3 a, Vector3 b, float width)
        {
            var pts = new List<Vector3>();
            int steps = 46;
            float bend = Random.Range(-0.12f, 0.12f);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 p = Vector3.Lerp(a, b, t);
                Vector3 perp = Vector3.Cross((b - a).normalized, Vector3.up);
                p += perp * Mathf.Sin(t * Mathf.PI) * (Vector3.Distance(a, b) * bend);
                pts.Add(new Vector3(p.x, SampleHeight(p.x, p.z) + 0.25f, p.z));
            }
            mb.AddRibbon(pts, width, 0.05f, new Color(0.24f, 0.23f, 0.22f), 0.05f);
            // мост через реку не строим отдельно: карта сухая, вода только в низинах
            for (int i = 0; i < pts.Count; i += 4) StampMinimap(pts[i], 8f, new Color(0.22f, 0.21f, 0.2f));
        }

        void BuildRailway()
        {
            var dir = new Vector3(1f, 0f, 0.35f).normalized;
            var side = new Vector3(-dir.z, 0f, dir.x);
            for (int track = 0; track < 3; track++)
            {
                var go = new GameObject("Rail");
                go.transform.SetParent(root.transform, false);
                var rail = Proto(PrimitiveType.Cube, go.transform, "rail_line", MatLib.Metal(new Color(0.32f, 0.31f, 0.3f), 0.4f), false);
                Vector3 c = stationCenter + side * ((track - 1) * 8f);
                c.y = SampleHeight(c.x, c.z) + 0.3f;
                rail.transform.position = c;
                rail.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                rail.transform.localScale = new Vector3(3.2f, 0.5f, 1700f);
                var sleeper = Proto(PrimitiveType.Cube, go.transform, "sleepers", MatLib.Flat(new Color(0.26f, 0.23f, 0.2f), 0.05f), false);
                sleeper.transform.position = c - Vector3.up * 0.28f;
                sleeper.transform.rotation = rail.transform.rotation;
                sleeper.transform.localScale = new Vector3(5.4f, 0.5f, 1700f);
            }
            // стрелки и тупик у станции
            StampMinimap(stationCenter + side * 8f, 30f, new Color(0.25f, 0.24f, 0.23f));
        }

        void BuildRocks()
        {
            for (int i = 0; i < 70; i++)
            {
                Vector3 p = RandomPointInMap(Size * 0.45f);
                if (SampleHeight(p.x, p.z) < 3f) continue;
                var rock = Proto(PrimitiveType.Sphere, props, "rock", MatLib.Metal(new Color(0.36f, 0.35f, 0.33f), 0.05f), true);
                float s = Random.Range(3f, 11f);
                rock.transform.position = FromGround(p) + Vector3.up * s * 0.18f;
                rock.transform.localScale = new Vector3(s, s * Random.Range(0.5f, 0.9f), s * Random.Range(0.8f, 1.2f));
                rock.transform.rotation = Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(0f, 360f), Random.Range(-10f, 10f));
            }
        }

        // ==================== утилиты ====================

        public Vector3 FromGround(Vector3 p, float offset = 0f)
        {
            return new Vector3(p.x, SampleHeight(p.x, p.z) + offset, p.z);
        }

        public static Vector3 RandomPointInMap(float radius)
        {
            float a = Random.value * Mathf.PI * 2f;
            float r = Mathf.Sqrt(Random.value) * radius;
            return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        /// <summary>Найти точку для старта внутри безопасной зоны: подальше от города (чтобы не застрять в доме).</summary>
        public Vector3 FindSpawnPoint(Vector2 center, float radius)
        {
            for (int attempt = 0; attempt < 240; attempt++)
            {
                float a = Random.value * Mathf.PI * 2f;
                float r = Mathf.Sqrt(Random.value) * radius * 0.85f;
                Vector3 p = new Vector3(center.x + Mathf.Cos(a) * r, 0f, center.y + Mathf.Sin(a) * r);
                if (Mathf.Abs(p.x) > Size * 0.46f || Mathf.Abs(p.z) > Size * 0.46f) continue;
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(cityCenter.x, cityCenter.z)) < 120f) continue;
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(stationCenter.x, stationCenter.z)) < 90f) continue;
                float h = SampleHeight(p.x, p.z);
                if (h < 1f) continue;
                // проверяем, что рядом нет препятствий
                bool blocked = Physics.CheckBox(new Vector3(p.x, h + 3.2f, p.z), new Vector3(5f, 1.6f, 5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                if (blocked) continue;
                return new Vector3(p.x, h, p.z);
            }
            var fallback = RandomPointInMap(radius * 0.6f);
            return FromGround(fallback, 1f);
        }

        public Texture2D GetMinimap() { return Minimap; }
    }
}
