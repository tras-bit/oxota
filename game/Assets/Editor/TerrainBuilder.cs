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
        public const int HeightRes = 513;
        public const int AlphaRes = 1024;

        /// <summary>Создаёт ландшафт и раскрашивает слои (трава/земля/камень/асфальт).</summary>
        public static Terrain Create(Texture2D grass, Texture2D grassN, Texture2D dirt, Texture2D dirtN,
                                     Texture2D rock, Texture2D rockN, Texture2D asphalt, Texture2D asphaltN,
                                     float seed)
        {
            var data = new TerrainData();
            data.heightmapResolution = HeightRes;
            data.alphamapResolution = AlphaRes;
            data.size = new Vector3(SizeMeters, 90f, SizeMeters);

            data.terrainLayers = new[]
            {
                MakeLayer("Grass", grass, grassN, 12f),
                MakeLayer("Dirt", dirt, dirtN, 14f),
                MakeLayer("Rock", rock, rockN, 18f),
                MakeLayer("Asphalt", asphalt, asphaltN, 20f),
            };

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

                    float cx = Mathf.Abs(fx - 0.65f), cz = Mathf.Abs(fz - 0.68f);   // площадка города
                    float city = Mathf.Clamp01(1f - (cx + cz) * 6.5f);
                    h = Mathf.Lerp(h, 0.46f, city * 0.85f);

                    heights[y, x] = Mathf.Clamp01(h * 0.55f);
                }
            data.SetHeights(0, 0, heights);
            data.SetAlphamaps(0, 0, PaintLayers(heights, ox, oz));

            // данные ландшафта и слои обязаны быть ассетами, иначе сцена их потеряет
            Directory.CreateDirectory("Assets/Terrain");
            var assetPath = "Assets/Terrain/BattleTerrain.asset";
            if (File.Exists(assetPath)) AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(data, assetPath);
            foreach (var l in data.terrainLayers) AssetDatabase.AddObjectToAsset(l, data);
            AssetDatabase.SaveAssets();

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain_3000m";
            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 6f;
            terrain.basemapDistance = 900f;
            terrain.detailObjectDistance = 120f;
            terrain.treeDistance = 0f;
            terrain.drawInstanced = true;
            go.AddComponent<TerrainCollider>().terrainData = data;
            return terrain;
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

                    // площадка станции и города — асфальт
                    float city = Mathf.Clamp01(1f - (Mathf.Abs(fx - 0.65f) + Mathf.Abs(fz - 0.68f)) * 7f);
                    float station = Mathf.Clamp01(1f - Mathf.Abs(fz - 0.47f) * 40f);
                    asphalt = Mathf.Clamp01(city * 0.8f + station * 0.7f);
                    grass *= (1f - asphalt);

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
