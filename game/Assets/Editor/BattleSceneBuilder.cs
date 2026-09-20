// Сборка боевой сцены «Battle» целиком из кода: террейн, город, лес, железная дорога,
// техника, лут, менеджеры боя, HUD и камера.
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Samsar.EditorTools
{
    public static class BattleSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Battle.unity";

        [MenuItem("Samsar/2. Собрать боевую сцену", false, 20)]
        public static void Build()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            TankAssetBuilder.BuildMaterials();
            TankAssetBuilder.BuildTankPrefabs();
            TankAssetBuilder.BuildPropPrefabs();

            SetupLighting();
            var terrain = BuildTerrain();
            BuildWorld(terrain);
            BuildControllers();
            BakeNavMesh();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("Боевая сцена собрана: " + ScenePath);
        }

        // ---------- окружение ----------
        static void SetupLighting()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.00055f;
            RenderSettings.fogColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.58f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.38f, 0.4f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.19f, 0.17f);

            var skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            if (skybox != null) RenderSettings.skybox = skybox;

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.35f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sunGo.transform.rotation = Quaternion.Euler(48f, 145f, 0f);
            sunGo.AddComponent<Samsar.MatchSunDirection>();
        }

        static Terrain BuildTerrain()
        {
            var grass = Load<Texture2D>("ground_grass_albedo.jpg");
            var grassN = Load<Texture2D>("ground_grass_normal.png");
            var dirt = Load<Texture2D>("ground_dirt_albedo.jpg");
            var dirtN = Load<Texture2D>("ground_dirt_normal.png");
            var rock = Load<Texture2D>("ground_rock_albedo.jpg");
            var rockN = Load<Texture2D>("ground_rock_normal.png");
            var asphalt = Load<Texture2D>("ground_asphalt_albedo.jpg");
            var asphaltN = Load<Texture2D>("ground_asphalt_normal.png");
            var terrain = TerrainBuilder.Create(grass, grassN, dirt, dirtN, rock, rockN, asphalt, asphaltN, 20260919f);
            terrain.transform.position = new Vector3(-TerrainBuilder.SizeMeters * 0.5f, -14f, -TerrainBuilder.SizeMeters * 0.5f);
            return terrain;
        }

        // ---------- наполнение карты ----------
        static void BuildWorld(Terrain terrain)
        {
            var world = new GameObject("World").transform;
            var props = TankAssetBuilder.LoadPropPrefabs();
            var rnd = new System.Random(4242);

            // 1) ГОРОД (смешанная карта: город + лес + поля)
            var cityRoot = new GameObject("City").transform;
            cityRoot.SetParent(world, false);
            for (int bi = 0; bi < 5; bi++)
                for (int bj = 0; bj < 5; bj++)
                {
                    float x = 120f + bi * 78f + (float)(rnd.NextDouble() * 14 - 7);
                    float z = 120f + bj * 78f + (float)(rnd.NextDouble() * 14 - 7);
                    if (rnd.NextDouble() < 0.18) continue;      // пустыри
                    var go = SpawnProp(props, "house", new Vector3(x, 0f, z), (float)rnd.NextDouble() * 360f, terrain, cityRoot);
                    MarkDestructible(go, 900f, DestructibleProp.PropKind.Building, props, "rubble");
                }
            // пара снесённых домов — руины
            for (int i = 0; i < 6; i++)
            {
                var pos = new Vector3(150f + i * 60f, 0f, 420f);
                SpawnProp(props, "rubble", pos, (float)rnd.NextDouble() * 360f, terrain, cityRoot);
            }

            // 2) ПРОМЗОНА: ангары (можно заехать внутрь) + контейнеры
            var indRoot = new GameObject("Industrial").transform;
            indRoot.SetParent(world, false);
            Vector3[] warehouses =
            {
                new Vector3(430f, 0f, 200f), new Vector3(300f, 0f, 60f), new Vector3(120f, 0f, -120f),
                new Vector3(760f, 0f, 640f), new Vector3(-150f, 0f, 520f)
            };
            foreach (var w in warehouses)
            {
                var go = SpawnProp(props, "warehouse", w, 0f, terrain, indRoot);
                MarkDestructible(go, 4000f, DestructibleProp.PropKind.Building, props, "rubble");
            }
            for (int i = 0; i < 40; i++)
            {
                var pos = new Vector3(Random.Range(-500f, 900f), 0f, Random.Range(-300f, 800f));
                var go = SpawnProp(props, "container", pos, Random.Range(0f, 360f), terrain, indRoot);
                MarkDestructible(go, 1200f, DestructibleProp.PropKind.Container, null, null);
            }
            for (int i = 0; i < 60; i++)
            {
                var pos = new Vector3(Random.Range(-700f, 1000f), 0f, Random.Range(-500f, 900f));
                var go = SpawnProp(props, "block", pos, Random.Range(0f, 360f), terrain, indRoot);
                MarkDestructible(go, 700f, DestructibleProp.PropKind.Small, null, null);
            }

            // 3) ЖЕЛЕЗНАЯ ДОРОГА И СТАНЦИЯ
            var railRoot = new GameObject("Railway").transform;
            railRoot.SetParent(world, false);
            var railSource = new List<GameObject>();
            for (float x = -1450f; x <= 1450f; x += 25f)
            {
                var go = SpawnProp(props, "rail_segment", new Vector3(x, 0f, -90f), 90f, terrain, railRoot);
                railSource.Add(go);
            }
            for (float x = -1450f; x <= -450f; x += 25f)     // ветка к станции
                railSource.Add(SpawnProp(props, "rail_segment", new Vector3(x, 0f, 330f), 0f, terrain, railRoot));
            StaticBatcher.Combine(railSource, "RAILS_COMBINED", true);

            var stationRoot = new GameObject("Station").transform;
            stationRoot.SetParent(world, false);
            SpawnProp(props, "station", new Vector3(-700f, 0f, 430f), 0f, terrain, stationRoot);
            SpawnProp(props, "tower", new Vector3(-1000f, 0f, -400f), 0f, terrain, stationRoot);
            SpawnProp(props, "tower", new Vector3(1100f, 0f, 900f), 0f, terrain, stationRoot);

            // 4) ЛЕС (запад) + отдельные рощи — объединяются в крупные меши
            var treeSource = new List<GameObject>();
            int nearTrees = 0;
            var forestRoot = new GameObject("Forest").transform;
            forestRoot.SetParent(world, false);
            for (int i = 0; i < 900; i++)
            {
                float x = Random.Range(-1450f, -200f);
                float z = Random.Range(-1300f, 1300f);
                float density = Mathf.PerlinNoise(x / 380f, z / 380f);
                if (density < 0.42f) continue;
                string kind = Random.value < 0.6f ? "tree_spruce" : "tree_birch";
                var tree = SpawnProp(props, kind, new Vector3(x, 0f, z), Random.Range(0f, 360f), terrain, forestRoot);
                // дальний лес склеиваем в один меш (быстро рисуется), а деревья ближе 500 м к центру
                // оставляем отдельными: их можно снести выстрелом, как заборы и дома
                if (new Vector2(x, z).magnitude < 500f)
                {
                    // статическая батч-разметка не даст упавшему дереву поехать — снимаем её
                    GameObjectUtility.SetStaticEditorFlags(tree, 0);
                    MarkDestructible(tree, 170f, DestructibleProp.PropKind.Tree, null, null);
                    nearTrees++;
                }
                else treeSource.Add(tree);
            }
            for (int i = 0; i < 4; i++)       // рощи в других частях карты
            {
                Vector3 c = new Vector3(Random.Range(-900f, 1200f), 0f, Random.Range(-1200f, 1200f));
                for (int j = 0; j < 40; j++)
                {
                    Vector3 p = c + new Vector3(Random.Range(-90f, 90f), 0f, Random.Range(-90f, 90f));
                    treeSource.Add(SpawnProp(props, Random.value < 0.5f ? "tree_spruce" : "tree_birch",
                                             p, Random.Range(0f, 360f), terrain, forestRoot));
                }
            }
            StaticBatcher.Combine(treeSource, "TREES_COMBINED", true);
            Debug.Log(string.Format("Лес: склеено {0}, отдельно разрушаемых деревьев {1}", treeSource.Count, nearTrees));

            // 5) ЗАБОРЫ, СТОГА И МЕЛОЧЬ ПО ВСЕЙ КАРТЕ
            var smallRoot = new GameObject("Details").transform;
            smallRoot.SetParent(world, false);
            for (int i = 0; i < 160; i++)
            {
                Vector3 p = RandomPoint(rnd, 1350f);
                var go = SpawnProp(props, "fence", p, (float)rnd.NextDouble() * 360f, terrain, smallRoot);
                MarkDestructible(go, 260f, DestructibleProp.PropKind.Small, null, null);
            }
            for (int i = 0; i < 120; i++)
            {
                Vector3 p = RandomPoint(rnd, 1300f);
                SpawnProp(props, "bale", p, (float)rnd.NextDouble() * 360f, terrain, smallRoot);
            }
            for (int i = 0; i < 80; i++)
            {
                Vector3 p = RandomPoint(rnd, 1200f);
                var go = SpawnProp(props, "block", p, (float)rnd.NextDouble() * 360f, terrain, smallRoot);
                MarkDestructible(go, 700f, DestructibleProp.PropKind.Small, null, null);
            }
        }

        static Vector3 RandomPoint(System.Random rnd, float radius)
        {
            double a = rnd.NextDouble() * Mathf.PI * 2.0;
            double r = System.Math.Sqrt(rnd.NextDouble()) * radius;
            return new Vector3((float)(System.Math.Cos(a) * r), 0f, (float)(System.Math.Sin(a) * r));
        }

        // ---------- объекты и материалы ----------
        static GameObject SpawnProp(Dictionary<string, GameObject> props, string key, Vector3 pos,
                                    float yaw, Terrain terrain, Transform parent)
        {
            GameObject prefab;
            if (!props.TryGetValue(key, out prefab) || prefab == null)
                return new GameObject("MISSING_" + key);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = key + "_" + Mathf.RoundToInt(pos.x) + "_" + Mathf.RoundToInt(pos.z);
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            TerrainBuilder.PlaceOnGround(go, terrain, 0f);
            AddColliders(go);
            MarkStatic(go);
            return go;
        }

        static void AddColliders(GameObject go)
        {
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                if (mf.GetComponent<Collider>() != null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
            }
        }

        static void MarkStatic(GameObject go)
        {
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.NavigationStatic | StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.ContributeGI);
        }

        static void MarkDestructible(GameObject go, float hp, DestructibleProp.PropKind kind,
                                     Dictionary<string, GameObject> props, string rubbleKey)
        {
            var dp = go.AddComponent<DestructibleProp>();
            dp.hp = hp;
            dp.kind = kind;
            if (rubbleKey != null && props != null && props.ContainsKey(rubbleKey))
                dp.rubbleModel = props[rubbleKey];
        }

        // ---------- менеджеры, HUD, камера ----------
        static void BuildControllers()
        {
            var managers = new GameObject("BattleSystems");
            var zone = managers.AddComponent<ZoneController>();
            var loot = managers.AddComponent<LootSpawner>();
            loot.boxesOnMap = 90;
            loot.mapRadius = 1300f;
            var bm = managers.AddComponent<BattleManager>();
            bm.zone = zone;
            bm.loot = loot;
            bm.spawnPoints = BuildSpawnPoints(bm).ToArray();

            var libGo = new GameObject("TankLibrary");
            var lib = libGo.AddComponent<TankLibrary>();
            var entries = new List<TankLibrary.Entry>();
            foreach (var spec in TankSpec.Roster)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tanks/" + spec.id + ".prefab");
                entries.Add(new TankLibrary.Entry { id = spec.id, model = prefab });
            }
            lib.tanks = entries.ToArray();
            bm.library = lib;

            var hudGo = new GameObject("HUD");
            hudGo.AddComponent<HUD>();

            var camGo = new GameObject("MainCamera");
            Samsar.TankRig.SafeTag(camGo, "MainCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane = 5000f;
            camGo.AddComponent<AudioListener>();
            var rig = camGo.AddComponent<CameraRig>();
            rig.cam = cam;
            camGo.transform.position = new Vector3(0f, 12f, -18f);
        }

        static List<Transform> BuildSpawnPoints(BattleManager bm)
        {
            var list = new List<Transform>();
            var root = new GameObject("SpawnPoints").transform;
            int n = 32;
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                float r = 980f + (i % 3) * 130f;
                var go = new GameObject("Spawn_" + i);
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(Mathf.Cos(a) * r, 4f, Mathf.Sin(a) * r);
                list.Add(go.transform);
            }
            return list;
        }

        static void BakeNavMesh()
        {
            var t = System.Type.GetType("UnityEditor.AI.NavMeshBuilder, UnityEditor");
            if (t != null)
            {
                var m = t.GetMethod("BuildNavMesh", BindingFlags.Public | BindingFlags.Static);
                if (m != null)
                {
                    m.Invoke(null, null);
                    Debug.Log("NavMesh испечён (легаси-способ).");
                    return;
                }
            }
            Debug.LogWarning("Автоматическая выпечка NavMesh недоступна в этой версии Unity. " +
                             "Открой Window → AI → Navigation и нажми Bake (боты умеют работать и без NavMesh, " +
                             "но с ним ходят умнее).");
        }

        static T Load<T>(string name) where T : Object
        {
            var path = "Assets/Textures/" + name;
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) Debug.LogWarning("Не найден ассет: " + path);
            return asset;
        }
    }

    /// <summary>Направление солнца соответствует превью-рендерам из Blender.</summary>
    public class MatchSunDirection : MonoBehaviour { }
}
