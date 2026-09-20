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
            BuildControllers(terrain);
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
            // MatchSunDirection объявлен тут же, в Samsar.EditorTools — уточнять пространство
            // имён не нужно (Samsar.MatchSunDirection искал бы класс прямо в Samsar — его там нет).
            sunGo.AddComponent<MatchSunDirection>();
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
            // На самом первом открытии Unity мог ещё не доделать импорт — тогда Load
            // вернёт null (какие именно — см. предупреждения в Console). Ландшафт без
            // текстур строить нельзя — скажем об этом по-человечески, а не падением.
            if (grass == null || grassN == null || dirt == null || dirtN == null ||
                rock == null || rockN == null || asphalt == null || asphaltN == null)
                throw new System.Exception(
                    "Текстуры земли (ground_*.jpg / ground_*.png) ещё не импортировались. " +
                    "Дождись, пока Unity закончит импорт (индикатор внизу справа), и запусти " +
                    "сборку снова: меню Samsar → 0. СОБРАТЬ ВСЁ. Если снова не поможет — " +
                    "перезапусти Unity и дай ему минуту.");
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
            // город стоит в центре карты: там же оказывается финальная зона (180 м),
            // поэтому последний бой идёт в застройке, а не в чистом поле
            for (int bi = 0; bi < 5; bi++)
                for (int bj = 0; bj < 5; bj++)
                {
                    if (bi == 2 && bj == 2) continue;           // центральная площадь — место финала
                    float x = -180f + bi * 90f + (float)(rnd.NextDouble() * 16 - 8);
                    float z = -180f + bj * 90f + (float)(rnd.NextDouble() * 16 - 8);
                    if (rnd.NextDouble() < 0.15) continue;      // пустыри
                    var go = SpawnProp(props, "house", new Vector3(x, 0f, z), (float)rnd.NextDouble() * 360f, terrain, cityRoot);
                    MarkDestructible(go, 900f, DestructibleProp.PropKind.Building, props, "rubble");
                }
            // снесённые дома — руины по южной окраине города
            for (int i = 0; i < 6; i++)
            {
                var pos = new Vector3(-150f + i * 60f, 0f, -280f);
                SpawnProp(props, "rubble", pos, (float)rnd.NextDouble() * 360f, terrain, cityRoot);
            }
            // бетонные блоки и баррикады на центральной площади (укрытия для финала)
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                var pos = new Vector3(Mathf.Cos(a) * 46f, 0f, Mathf.Sin(a) * 46f);
                var go = SpawnProp(props, "block", pos, a * Mathf.Rad2Deg, terrain, cityRoot);
                MarkDestructible(go, 700f, DestructibleProp.PropKind.Small, null, null);
            }

            // 2) ПРОМЗОНА: ангары (можно заехать внутрь) + контейнеры
            var indRoot = new GameObject("Industrial").transform;
            indRoot.SetParent(world, false);
            Vector3[] warehouses =
            {
                new Vector3(430f, 0f, -160f), new Vector3(640f, 0f, 60f), new Vector3(340f, 0f, 330f),
                new Vector3(880f, 0f, 720f), new Vector3(-430f, 0f, 500f)
            };
            foreach (var w in warehouses)
            {
                var go = SpawnProp(props, "warehouse", w, 0f, terrain, indRoot);
                MarkDestructible(go, 4000f, DestructibleProp.PropKind.Building, props, "rubble");
            }
            for (int i = 0; i < 40; i++)
            {
                var pos = new Vector3(Random.Range(260f, 980f), 0f, Random.Range(-380f, 780f));
                if (TerrainBuilder.DistanceToRoad(pos.x, pos.z) < 18f) continue;
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
                var go = SpawnProp(props, "rail_segment", new Vector3(x, 0f, -420f), 90f, terrain, railRoot);
                railSource.Add(go);
            }
            for (float x = -1450f; x <= -450f; x += 25f)     // ветка к станции (она севернее города)
                railSource.Add(SpawnProp(props, "rail_segment", new Vector3(x, 0f, 330f), 0f, terrain, railRoot));
            StaticBatcher.Combine(railSource, "RAILS_COMBINED", true);

            var stationRoot = new GameObject("Station").transform;
            stationRoot.SetParent(world, false);
            SpawnProp(props, "station", new Vector3(-700f, 0f, 348f), 0f, terrain, stationRoot);
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
                if (TerrainBuilder.DistanceToRoad(x, z) < 24f) continue;      // не сажаем лес на асфальт
                if (NearSpawnPoint(x, z, 34f)) continue;                       // просека у точки старта
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
                    if (TerrainBuilder.DistanceToRoad(p.x, p.z) < 24f) continue;
                    if (NearSpawnPoint(p.x, p.z, 34f)) continue;
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
                if (TerrainBuilder.DistanceToRoad(p.x, p.z) < 20f) continue;
                var go = SpawnProp(props, "fence", p, (float)rnd.NextDouble() * 360f, terrain, smallRoot);
                MarkDestructible(go, 260f, DestructibleProp.PropKind.Small, null, null);
            }
            for (int i = 0; i < 120; i++)
            {
                Vector3 p = RandomPoint(rnd, 1300f);
                if (TerrainBuilder.DistanceToRoad(p.x, p.z) < 20f) continue;
                SpawnProp(props, "bale", p, (float)rnd.NextDouble() * 360f, terrain, smallRoot);
            }
            for (int i = 0; i < 80; i++)
            {
                Vector3 p = RandomPoint(rnd, 1200f);
                if (TerrainBuilder.DistanceToRoad(p.x, p.z) < 18f) continue;
                var go = SpawnProp(props, "block", p, (float)rnd.NextDouble() * 360f, terrain, smallRoot);
                MarkDestructible(go, 700f, DestructibleProp.PropKind.Small, null, null);
            }
            // сгоревшие танки — укрытия и ориентиры прошлых боёв (не разрушаются: уже мертвы)
            Vector3[] wrecks =
            {
                new Vector3(-64f, 0f, 128f),     // перекрёсток в городе
                new Vector3(158f, 0f, -96f),     // южная улица
                new Vector3(524f, 0f, 418f),     // промзона, между ангарми
                new Vector3(-640f, 0f, 400f),    // привокзальная площадь
                new Vector3(836f, 0f, -64f),     // восточная промзона
                new Vector3(64f, 0f, -424f)      // выезд из города на юг
            };
            foreach (var w in wrecks)
                SpawnProp(props, "wreck", w, Random.Range(0f, 360f), terrain, smallRoot);
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
            // NavigationStatic объявлен устаревшим (CS0618), но легаси-выпечка NavMesh
            // по-прежнему читает его, а боты ходят по NavMeshAgent — оставляем и глушим
            // предупреждение только здесь, чтобы Console была чистой.
#pragma warning disable CS0618
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.NavigationStatic | StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.ContributeGI);
#pragma warning restore CS0618
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
        static void BuildControllers(Terrain terrain)
        {
            var managers = new GameObject("BattleSystems");
            var zone = managers.AddComponent<ZoneController>();
            var loot = managers.AddComponent<LootSpawner>();
            loot.boxesOnMap = 90;
            loot.mapRadius = 1300f;
            var bm = managers.AddComponent<BattleManager>();
            bm.zone = zone;
            bm.loot = loot;
            bm.spawnPoints = BuildSpawnPoints(terrain).ToArray();

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
            var hud = hudGo.AddComponent<HUD>();
            hud.minimapBg = BuildMinimapTexture();

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

        /// <summary>Точки старта по кольцу карты. Список считается один раз и используется ещё
        /// при расстановке леса: вокруг каждой точки оставляем просеку, чтобы машина не застряла
        /// между деревьями на старте.</summary>
        public const int SpawnCount = 32;
        public const float SpawnRadiusMin = 980f;
        public const float SpawnRadiusStep = 130f;

        public static Vector3[] SpawnPositions()
        {
            var arr = new Vector3[SpawnCount];
            for (int i = 0; i < SpawnCount; i++)
            {
                float a = i / (float)SpawnCount * Mathf.PI * 2f;
                float r = SpawnRadiusMin + (i % 3) * SpawnRadiusStep;
                arr[i] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
            return arr;
        }

        static bool NearSpawnPoint(float x, float z, float clearance)
        {
            foreach (var p in SpawnPositions())
                if ((p - new Vector3(x, 0f, z)).sqrMagnitude < clearance * clearance)
                    return true;
            return false;
        }

        static List<Transform> BuildSpawnPoints(Terrain terrain)
        {
            var list = new List<Transform>();
            var root = new GameObject("SpawnPoints").transform;
            var positions = SpawnPositions();
            for (int i = 0; i < positions.Length; i++)
            {
                var go = new GameObject("Spawn_" + i);
                go.transform.SetParent(root, false);
                // высоту берём с рельефа: раньше точки висели на Y=4 при поверхности +5…+25,
                // и танки рождались под землёй — физика выплёвывала их кривыми
                Vector3 p = positions[i];
                if (terrain != null) p.y = TerrainBuilder.HeightAt(terrain, p);
                go.transform.position = p + Vector3.up * 1.2f;
                list.Add(go.transform);
            }
            return list;
        }

        /// <summary>Рисует подложку миникарты (дороги, город, промзона, железка, лес, точки старта)
        /// по тем же координатам, что и расстановка объектов в BuildWorld — чтобы карта
        /// совпадала с миром. Сохранив в PNG, отдаём через HUD.minimapBg.</summary>
        static Texture2D BuildMinimapTexture()
        {
            const int R = 512;
            var tex = new Texture2D(R, R, TextureFormat.RGBA32, false);
            var px = new Color[R * R];
            Color field = new Color(0.20f, 0.27f, 0.16f);      // поле
            Color road = new Color(0.62f, 0.57f, 0.47f);       // асфальт
            Color city = new Color(0.47f, 0.45f, 0.41f);       // город
            Color ind = new Color(0.38f, 0.34f, 0.31f);        // промзона
            Color forest = new Color(0.10f, 0.18f, 0.10f);     // лес
            Color rail = new Color(0.25f, 0.22f, 0.20f);       // железная дорога

            for (int y = 0; y < R; y++)
                for (int x = 0; x < R; x++)
                {
                    // пиксель → мир: карта от −1500 до +1500, юг внизу (текстура снизу вверх)
                    float wx = (x + 0.5f) / R * TerrainBuilder.SizeMeters - TerrainBuilder.SizeMeters * 0.5f;
                    float wz = (y + 0.5f) / R * TerrainBuilder.SizeMeters - TerrainBuilder.SizeMeters * 0.5f;
                    var c = field;

                    // западный лес — тот же шум Перлина, что фильтрует посадку деревьев
                    if (wx < -200f && wx > -1450f && wz > -1300f && wz < 1300f)
                    {
                        float density = Mathf.PerlinNoise(wx / 380f, wz / 380f);
                        if (density > 0.38f) c = Color.Lerp(field, forest,
                            Mathf.Clamp01((density - 0.38f) / 0.14f));
                    }
                    // город: 5×5 кварталов примерно −220…220
                    if (wx > -225f && wx < 225f && wz > -225f && wz < 225f) c = city;
                    // промзона: ангары + поле контейнеров
                    bool industrial = (wx > 250f && wx < 1000f && wz > -420f && wz < 800f) ||
                                      (wx > -550f && wx < -300f && wz > 350f && wz < 650f);
                    if (industrial) c = ind;
                    // железная дорога: магистраль Z=−420, ветка к станции Z=330
                    if (Mathf.Abs(wz + 420f) < 10f) c = rail;
                    if (Mathf.Abs(wz - 330f) < 10f && wx < -450f) c = rail;
                    // дороги — последними, поверх всего
                    if (TerrainBuilder.DistanceToRoad(wx, wz) < 13f) c = road;

                    px[y * R + x] = c;
                }

            // точки старта — жёлтые точки по кольцу (те же SpawnPositions)
            foreach (var s in SpawnPositions())
            {
                int mx = Mathf.RoundToInt((s.x + TerrainBuilder.SizeMeters * 0.5f) / TerrainBuilder.SizeMeters * R);
                int my = Mathf.RoundToInt((s.z + TerrainBuilder.SizeMeters * 0.5f) / TerrainBuilder.SizeMeters * R);
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int xx = Mathf.Clamp(mx + dx, 0, R - 1), yy = Mathf.Clamp(my + dy, 0, R - 1);
                        if (dx * dx + dy * dy <= 4) px[yy * R + xx] = new Color(0.95f, 0.8f, 0.25f);
                    }
            }

            tex.SetPixels(px);
            tex.Apply();
            var path = "Assets/Textures/minimap_bg.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
