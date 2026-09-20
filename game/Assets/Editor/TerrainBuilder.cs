// Генерация ландшафта 3×3 км: холмы, равнины, площадка города, слои текстур.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Samsar.EditorTools
{
    public static class TerrainBuilder
    {
        public const int SizeMeters = 3000;

        /// <summary>Дорожная сеть по карте (мировые координаты XZ, terrain стоит в −1500…1500):
        /// выезды по краям карты, шоссе через город и станцию, ветка в промзону.
        /// Дороги красятся асфальтом и учитываются при расстановке объектов.</summary>
        static readonly Vector2[][] Roads =
        {
            // запад → город → станция → восточный выезд (главное шоссе)
            new[] { new Vector2(-1500f, 120f), new Vector2(-700f, 430f), new Vector2(450f, 540f),
                    new Vector2(1200f, 480f), new Vector2(1500f, 430f) },
            // город → промзона → южный выезд
            new[] { new Vector2(450f, 540f), new Vector2(200f, -100f), new Vector2(-150f, -560f),
                    new Vector2(-120f, -1500f) },
            // разъезд в промзоне
            new[] { new Vector2(200f, -100f), new Vector2(760f, 260f), new Vector2(1180f, 700f),
                    new Vector2(1500f, 900f) },
            // северный выезд от города
            new[] { new Vector2(450f, 540f), new Vector2(700f, 1100f), new Vector2(900f, 1500f) },
        };

        /// <summary>Расстояние от точки мира до ближайшей дороги (метры). Нужно, чтобы не ставить
        /// деревья, заборы и стога посреди асфальта.</summary>
        public static float DistanceToRoad(float x, float z)
        {
            float best = float.MaxValue;
            foreach (var road in Roads)
                for (int i = 0; i + 1 < road.Length; i++)
                    best = Mathf.Min(best, SegmentDistance(x, z, road[i], road[i + 1]));
            return best;
        }

        static float SegmentDistance(float px, float pz, Vector2 a, Vector2 b)
        {
            float vx = b.x - a.x, vz = b.y - a.y;
            float wx = px - a.x, wz = pz - a.y;
            float len2 = vx * vx + vz * vz;
            float t = len2 <= 0.0001f ? 0f : Mathf.Clamp01((wx * vx + wz * vz) / len2);
            float dx = wx - vx * t, dz = wz - vz * t;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
        public const int HeightRes = 513;
        public const int AlphaRes = 1024;

        /// <summary>Создаёт ландшафт и раскрашивает слои (трава/земля/камень/асфальт).</summary>
        public static Terrain Create(Texture2D grass, Texture2D grassN, Texture2D dirt, Texture2D dirtN,
                                     Texture2D rock, Texture2D rockN, Texture2D asphalt, Texture2D asphaltN,
                                     float seed)
        {
            // Слои ландшафта — отдельные ассеты-файлы: ссылка из TerrainData на
            // сохранённый файл переживает любые пересохранения. Раньше слои добавлялись
            // под-ассетами в BattleTerrain.asset через AssetDatabase.AddObjectToAsset,
            // но в Unity 2022.3 чтение data.terrainLayers после записи возвращает
            // не те объекты (вплоть до null) — сборка падала на ровном месте.
            Directory.CreateDirectory("Assets/Terrain");
            var layers = new[]
            {
                SaveLayer("Grass", grass, grassN, 12f),
                SaveLayer("Dirt", dirt, dirtN, 14f),
                SaveLayer("Rock", rock, rockN, 18f),
                SaveLayer("Asphalt", asphalt, asphaltN, 20f),
            };

            var data = new TerrainData();
            data.heightmapResolution = HeightRes;
            data.alphamapResolution = AlphaRes;
            data.size = new Vector3(SizeMeters, 90f, SizeMeters);
            data.terrainLayers = layers;

            var heights = new float[HeightRes, HeightRes];
            var rnd = new System.Random((int)seed);
            float ox = (float)rnd.NextDouble() * 100f, oz = (float)rnd.NextDouble() * 100f;
            for (int y = 0; y < HeightRes; y++)
                for (int x = 0; x < HeightRes; x++)
                {
                    float fx = x / (float)(HeightRes - 1), fz = y / (float)(HeightRes - 1);
                    float wx = fx * SizeMeters, wz = fz * SizeMeters;
                    float h = 0.5f;
                    h += Mathf.PerlinNoise((wx + ox) / 1400f, (wz + oz) / 1400f) * 0.30f;
                    h += Mathf.PerlinNoise((wx + ox) / 420f, (wz + oz) / 420f) * 0.14f;
                    h += Mathf.PerlinNoise((wx + ox) / 130f, (wz + oz) / 130f) * 0.05f;

                    float cx = Mathf.Abs(fx - 0.5f), cz = Mathf.Abs(fz - 0.5f);     // площадка города — центр карты
                    float city = Mathf.Clamp01(1f - (cx + cz) * 5.5f);
                    h = Mathf.Lerp(h, 0.46f, city * 0.85f);

                    heights[y, x] = Mathf.Clamp01(h * 0.55f);
                }
            data.SetHeights(0, 0, heights);
            data.SetAlphamaps(0, 0, PaintLayers(heights, ox, oz));

            // данные ландшафта обязаны быть ассетом, иначе сцена его потеряет
            // (слои уже сохранены отдельными файлами выше)
            DeleteAssetIfExists("Assets/Terrain/BattleTerrain.asset");
            AssetDatabase.CreateAsset(data, "Assets/Terrain/BattleTerrain.asset");
            AssetDatabase.SaveAssets();

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain_3000m";
            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 6f;
            terrain.basemapDistance = 900f;
            terrain.detailObjectDistance = 120f;
            terrain.treeDistance = 0f;
            terrain.drawInstanced = true;
            // CreateTerrainGameObject уже вешает TerrainCollider — берём существующий,
            // второй не нужен (было: задвоенный коллайдер и падение на ровном месте)
            var collider = go.GetComponent<TerrainCollider>();
            if (collider == null) collider = go.AddComponent<TerrainCollider>();
            collider.terrainData = data;
            return terrain;
        }

        /// <summary>Создаёт слой и сразу сохраняет его отдельным ассетом — так ссылка
        /// из TerrainData никогда не теряется и не зависит от геттера terrainLayers.</summary>
        static TerrainLayer SaveLayer(string name, Texture2D albedo, Texture2D normal, float tileSize)
        {
            var layer = MakeLayer(name, albedo, normal, tileSize);
            var path = "Assets/Terrain/Layer_" + name + ".terrainlayer";
            DeleteAssetIfExists(path);
            AssetDatabase.CreateAsset(layer, path);
            return layer;
        }

        static void DeleteAssetIfExists(string path)
        {
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        }

        static TerrainLayer MakeLayer(string name, Texture2D albedo, Texture2D normal, float tileSize)
        {
            var layer = new TerrainLayer();
            layer.name = name;
            layer.diffuseTexture = albedo;
            layer.normalMapTexture = normal;
            layer.tileSize = new Vector2(tileSize, tileSize);
            layer.metallic = 0f;
            layer.smoothness = name == "Asphalt" ? 0.25f : 0.1f;
            return layer;
        }

        static float[,,] PaintLayers(float[,] heights, float ox, float oz)
        {
            int res = AlphaRes;
            var map = new float[res, res, 4];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float fx = x / (float)(res - 1), fz = y / (float)(res - 1);
                    float wx = fx * SizeMeters, wz = fz * SizeMeters;

                    float slope = Mathf.Abs(Slope(heights, fx, fz));
                    float noise = Mathf.PerlinNoise((wx + ox) / 220f, (wz + oz) / 220f);
                    float patch = Mathf.PerlinNoise((wx + ox) / 900f, (wz + oz) / 900f);

                    float rock = Mathf.Clamp01((slope - 0.30f) * 3.2f);
                    float dirt = Mathf.Clamp01((noise - 0.45f) * 1.8f) * (1f - rock);
                    float grass = Mathf.Clamp01(1f - rock - dirt) * Mathf.Clamp01(0.5f + patch);
                    float asphalt = 0f;

                    // площадка города (в центре) и полосы вдоль железной дороги — асфальт
                    float city = Mathf.Clamp01(1f - (Mathf.Abs(fx - 0.5f) + Mathf.Abs(fz - 0.5f)) * 5.5f);
                    float rail1 = Mathf.Clamp01(1f - Mathf.Abs(wz - 780f) / 40f);      // линия у станции: z = 330
                    float rail2 = Mathf.Clamp01(1f - Mathf.Abs(wz - 1080f) / 40f);     // южная ветка: z = -420
                    asphalt = Mathf.Clamp01(city * 0.85f + Mathf.Max(rail1, rail2) * 0.7f);

                    // дороги: полотно 7 м в каждую сторону, обочина в 4 м уходит в грунт
                    float roadDist = DistanceToRoad(wx - SizeMeters * 0.5f, wz - SizeMeters * 0.5f);
                    float road = Mathf.Clamp01(1f - (roadDist - 7f) / 4f);
                    asphalt = Mathf.Max(asphalt, road);
                    dirt = Mathf.Max(dirt, Mathf.Clamp01(1f - (roadDist - 11f) / 5f) * 0.5f);
                    grass *= (1f - Mathf.Clamp01(asphalt + dirt));

                    float sum = grass + dirt + rock + asphalt + 0.0001f;
                    map[y, x, 0] = grass / sum;
                    map[y, x, 1] = dirt / sum;
                    map[y, x, 2] = rock / sum;
                    map[y, x, 3] = asphalt / sum;
                }
            return map;
        }

        static float Slope(float[,] h, float fx, float fz)
        {
            int res = h.GetLength(0);
            int x = Mathf.Clamp(Mathf.RoundToInt(fx * (res - 1)), 1, res - 2);
            int y = Mathf.Clamp(Mathf.RoundToInt(fz * (res - 1)), 1, res - 2);
            float dx = h[y, x + 1] - h[y, x - 1];
            float dz = h[y + 1, x] - h[y - 1, x];
            return Mathf.Sqrt(dx * dx + dz * dz) * 40f * SizeMeters / res;
        }

        /// <summary>Высота рельефа в мировой точке (для расстановки объектов).</summary>
        public static float HeightAt(Terrain terrain, Vector3 world)
        {
            return terrain.SampleHeight(world) + terrain.transform.position.y;
        }

        /// <summary>Расстановка по поверхности: объект ставится на землю.</summary>
        public static void PlaceOnGround(GameObject go, Terrain terrain, float extraY = 0f)
        {
            Vector3 p = go.transform.position;
            bool inside = p.x >= 0 && p.x <= SizeMeters && p.z >= 0 && p.z <= SizeMeters;
            p.y = inside ? HeightAt(terrain, p) + extraY : extraY;
            go.transform.position = p;
        }
    }
}
