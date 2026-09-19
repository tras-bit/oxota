using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Samsar
{
    /// <summary>
    /// Управляет боем: строит карту, расставляет участников, ведёт зону, воздушный груз,
    /// условия победы, итоговую таблицу и переход к результатам.
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        public static BattleManager Instance;

        public GameState State = new GameState();
        public MapGenerator Map;
        public Vehicle PlayerVehicle;

        public readonly List<Vector3> SpawnOptions = new List<Vector3>();
        public Vector3 ChosenSpawn;
        public bool SpawnChosen;
        public float BattleStartTime;
        public float AirdropTimer;
        public float CrateTimer;
        public int DestroyedObjects;
        public bool RangeMode;
        public GUIStyle _style;

        readonly List<Vehicle> bots = new List<Vehicle>();
        ZoneWall zoneWall;
        Camera mainCamera;
        CameraFly spectator;

        void Awake()
        {
            Instance = this;
            State = new GameState();
            Application.targetFrameRate = 120;
        }

        void Start()
        {
            GameConfig.ApplyQuality();
            mainCamera = Camera.main;
            if (GameManager.Mode == MatchMode.TestRange)
            {
                RangeMode = true;
                BuildTestRange();
            }
            else
            {
                BuildBattle();
            }
        }

        void OnDestroy()
        {
            Vehicle.All.Clear();
            LootSystem.ClearAll();
            Time.timeScale = 1f;
        }

        // ==================== сборка боя ====================

        void BuildBattle()
        {
            State.SpawnSeed = GameManager.NextSeed != 0 ? GameManager.NextSeed : Random.Range(1, 999999);
            Random.InitState(State.SpawnSeed);

            var mapGo = new GameObject("MapGenerator");
            Map = mapGo.AddComponent<MapGenerator>();
            Map.Build(State.SpawnSeed);
            Physics.SyncTransforms();

            State.ZoneCenter = Vector2.zero;
            State.ZoneRadius = GameConfig.ZoneStartRadius;
            State.NextZoneCenter = Vector2.zero;
            State.NextZoneRadius = GameConfig.ZoneStartRadius;
            State.StageTimer = GameConfig.ZoneTimes[0];

            int participants = Mathf.Clamp(GameConfig.BotCount, 4, 40);
            int playerTeam = 0;
            var playerSpec = TankSpecs.Get(GameConfig.SelectedTank);

            // точки старта на выбор (20 секунд на выбор)
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
                float r = Random.Range(750f, 1250f);
                SpawnOptions.Add(Map.FindSpawnPoint(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r), 420f));
            }
            ChosenSpawn = SpawnOptions[0];

            // союзник по взводу
            Vehicle squadMate = null;
            if (GameConfig.Squad)
            {
                var mateSpec = TankSpecs.Get((GameConfig.SelectedTank + 1 + Random.Range(0, TankSpecs.Count - 1)) % TankSpecs.Count);
                Vector3 squadPos = Map.FindSpawnPoint(new Vector2(ChosenSpawn.x, ChosenSpawn.z), 60f);
                squadMate = SpawnVehicle(mateSpec, squadPos, "Союзник", false, true, 0, playerTeam, BotDifficulty.Normal);
                squadMate.IsSquadMate = true;
                bots.Add(squadMate);
            }

            PlayerVehicle = SpawnVehicle(playerSpec, ChosenSpawn, "ВЫ", true, false, 0, playerTeam, BotDifficulty.Normal);
            PlayerVehicle.RespawnCharges = 1;
            State.Player = PlayerVehicle;
            if (squadMate != null) { PlayerVehicle.SquadMate = squadMate; squadMate.SquadMate = PlayerVehicle; }

            // противники: 5–10 ботов гарантированно рядом по сложности, остальные — остальные слоты (мародёры)
            int botCount = Mathf.Max(4, participants - 1 - (squadMate != null ? 1 : 0));
            var difficulty = (BotDifficulty)Mathf.Clamp(GameConfig.Difficulty, 0, 2);
            for (int i = 0; i < botCount; i++)
            {
                var spec = TankSpecs.Get(Random.Range(0, TankSpecs.Count));
                int squadId = 1 + i / 2;
                bool wantsSquad = Random.value < 0.5f && i + 1 < botCount;
                Vector3 pos = Map.FindSpawnPoint(Vector2.zero, GameConfig.ZoneStartRadius * 0.92f);
                var bot = SpawnVehicle(spec, pos, "Мародёр " + (i + 1), false, true, squadId, 1, difficulty);
                if (i == 1 || i == 3) bot.RespawnCharges = 1;      // часть ботов тоже может возродиться
                else if (Random.value < 0.35f) bot.RespawnCharges = 1;
                bots.Add(bot);
                if (wantsSquad && bots.Count > 1)
                {
                    var mate = bots[bots.Count - 2];
                    if (mate != null && mate != bot && mate.SquadId == squadId && mate.SquadMate == null)
                    {
                        bot.SquadMate = mate;
                        mate.SquadMate = bot;
                    }
                }
            }

            foreach (var v in Vehicle.All) State.Participants.Add(v);
            State.AliveCount = State.Participants.Count;
            State.PlaceCounter = State.Participants.Count;

            // зона: стена и кольцо
            var wallGo = new GameObject("ZoneWall");
            zoneWall = wallGo.AddComponent<ZoneWall>();
            zoneWall.Build();

            // камера: обзор карты на время отсчёта
            if (mainCamera != null)
            {
                var rig0 = mainCamera.GetComponent<PlayerCameraRig>();
                if (rig0 != null) rig0.enabled = false;
                mainCamera.transform.position = new Vector3(0f, 1500f, -1000f);
                mainCamera.transform.rotation = Quaternion.Euler(58f, 0f, 0f);
                mainCamera.farClipPlane = 6000f;
            }

            State.State = BattleState.Countdown;
            State.Countdown = GameConfig.CountdownTime;
            AudioBus.Ensure();
            Debug.Log(string.Format("[Samsar] Бой: участников {0}, seed {1}", State.Participants.Count, State.SpawnSeed));
        }

        void BuildTestRange()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "RangeGround";
            ground.transform.localScale = new Vector3(120f, 1f, 120f);
            ground.GetComponent<Renderer>().sharedMaterial = MatLib.Ground(new Color(0.34f, 0.36f, 0.28f));

            var map = new GameObject("RangeMap");
            Map = map.AddComponent<MapGenerator>();

            // мишени
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Cos(a) * (120f + i * 24f), 0f, Mathf.Sin(a) * (120f + i * 24f));
                var spec = TankSpecs.Get(i % TankSpecs.Count);
                var target = SpawnVehicle(spec, p, "Мишень " + (i + 1), false, true, 100 + i, 1, BotDifficulty.Easy);
                target.Ammo = 0;
                var brain = target.GetComponent<BotBrain>();
                if (brain != null) brain.enabled = false;
                target.RespawnCharges = 0;
            }

            for (int i = 0; i < 6; i++)
            {
                var crate = LootSystem.SpawnCrate(new Vector3(Random.Range(-90f, 90f), 1.2f, Random.Range(40f, 120f)), 1);
                crate.Life = 0f;
            }

            var spec2 = TankSpecs.Get(GameConfig.SelectedTank);
            PlayerVehicle = SpawnVehicle(spec2, new Vector3(0f, 1f, 0f), "ВЫ", true, false, 0, 0, BotDifficulty.Normal);
            PlayerVehicle.RespawnCharges = 99;
            State.Player = PlayerVehicle;
            State.State = BattleState.Running;
            State.TimeLeft = 100000f;
            State.Participants.AddRange(Vehicle.All);
            State.AliveCount = State.Participants.Count;
            BattleStartTime = Time.time;
            State.ZoneRadius = 1e9f;
        }

        public Vehicle SpawnVehicle(TankSpec spec, Vector3 pos, string name, bool isPlayer, bool isBot, int squadId, int team, BotDifficulty difficulty)
        {
            var bodyMat = MatLib.Metal(spec.BodyColor, 0.32f);
            var darkMat = MatLib.Metal(new Color(0.16f, 0.15f, 0.14f), 0.2f);
            var trackMat = MatLib.Flat(new Color(0.07f, 0.07f, 0.07f), 0.1f);
            var glassMat = MatLib.Metal(new Color(0.25f, 0.35f, 0.4f), 0.85f);

            var model = TankModel.Build(spec, bodyMat, darkMat, trackMat, glassMat);
            var go = model.Root;
            go.transform.position = pos + Vector3.up * (model.RideHeight + 0.4f);
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            var v = go.AddComponent<Vehicle>();
            v.Model = model;
            v.Turret = model.Turret;
            v.BarrelTip = model.BarrelTip;
            v.DisplayName = name;
            v.IsPlayer = isPlayer;
            v.IsBot = isBot;
            v.SquadId = squadId;
            v.Team = team;
            v.Difficulty = difficulty;
            v.Setup(spec);

            var ctrl = go.AddComponent<TankController>();
            ctrl.Vehicle = v;
            var turret = go.AddComponent<TurretAim>();
            turret.Vehicle = v;

            if (isPlayer)
            {
                var pc = go.AddComponent<PlayerController>();
                pc.Vehicle = v;
                pc.Controller = ctrl;
                pc.Turret = turret;
                model.Root.tag = "Player";
                SetupPlayerCamera(go);
            }
            else if (isBot)
            {
                var brain = go.AddComponent<BotBrain>();
                brain.Vehicle = v;
                brain.Controller = ctrl;
                brain.Turret = turret;
                brain.Difficulty = difficulty;
            }
            return v;
        }

        void SetupPlayerCamera(GameObject player)
        {
            if (mainCamera == null)
            {
                var camGo = new GameObject("MainCamera");
                camGo.tag = "MainCamera";
                mainCamera = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                camGo.AddComponent<PlayerCameraRig>();
            }
            mainCamera.farClipPlane = 5000f;
            var rig = mainCamera.GetComponent<PlayerCameraRig>();
            if (rig == null) rig = mainCamera.gameObject.AddComponent<PlayerCameraRig>();
            rig.enabled = true;
            rig.SetTarget(player.transform, player.GetComponent<Vehicle>());
        }

        // ==================== ход боя ====================

        void Update()
        {
            float dt = Time.deltaTime;
            VisionSystem.Tick(dt);
            AbilitySystem.Tick(dt);

            if (State.State == BattleState.Countdown)
            {
                State.Countdown -= dt;
                if (State.Countdown <= 0f) BeginBattle();
                return;
            }

            if (State.State != BattleState.Running) return;

            State.TimeLeft -= dt;
            UpdateBotRespawns();
            UpdateZone(dt);
            UpdateLoot(dt);
            UpdateAlive();
            CheckEnd();

            if (State.Player != null && State.Player.Alive)
            {
                var stats = State.Stats;
                stats.MaxLevelReached = Mathf.Max(stats.MaxLevelReached, State.Player.Level);
            }
        }

        /// <summary>Боты, у которых есть заряд «Возрождение», возвращаются в бой через 8 секунд.</summary>
        void UpdateBotRespawns()
        {
            if (RangeMode) return;
            if (Time.time - BattleStartTime > GameConfig.RespawnWindow) return;
            for (int i = 0; i < bots.Count; i++)
            {
                var bot = bots[i];
                if (bot == null || !bot.IsBot || bot.Alive || bot.RespawnCharges <= 0) continue;
                if (Time.time - bot.DeathTime < 8f) continue;
                bot.RespawnCharges--;
                Vector3 pos = Map.FindSpawnPoint(State.ZoneCenter, Mathf.Max(120f, State.ZoneRadius * 0.75f));
                bot.Respawn(pos);
            }
        }

        void BeginBattle()
        {
            State.State = BattleState.Running;
            // вернуть камеру игроку
            if (mainCamera != null)
            {
                var rig = mainCamera.GetComponent<PlayerCameraRig>();
                if (rig != null)
                {
                    rig.enabled = true;
                    if (PlayerVehicle != null) rig.SetTarget(PlayerVehicle.transform, PlayerVehicle);
                    mainCamera.transform.position = PlayerVehicle != null
                        ? PlayerVehicle.transform.position + PlayerVehicle.transform.forward * -14f + Vector3.up * 5f
                        : mainCamera.transform.position;
                }
            }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            BattleStartTime = Time.time;
            AirdropTimer = GameConfig.AirDropInterval;
            CrateTimer = 0f;
            State.Player.SpawnProtectedUntil = Time.time + 5f;
            if (State.Player.SquadMate != null) State.Player.SquadMate.SpawnProtectedUntil = Time.time + 5f;
            // стартовая порция добычи
            for (int i = 0; i < GameConfig.LootCratesOnMap / 2; i++)
            {
                Vector2 p = Random.insideUnitCircle * (GameConfig.ZoneStartRadius * 0.9f);
                Vector3 world = Map.FindSpawnPoint(p, 40f);
                LootSystem.SpawnCrate(world, State.Player.Level);
            }
            AudioSynth.PlayUi("zone", 0.5f);
            Debug.Log("[Samsar] Бой начался");
        }

        void UpdateZone(float dt)
        {
            if (RangeMode) return;

            State.StageTimer -= dt;
            float stageDuration = GameConfig.ZoneTimes[Mathf.Clamp(State.ZoneStage, 0, GameConfig.ZoneTimes.Length - 1)];
            float t = 1f - Mathf.Clamp01(State.StageTimer / Mathf.Max(1f, stageDuration));
            // плавное сужение к следующему кругу
            State.ZoneRadius = Mathf.Lerp(ZoneRadiusAt(State.ZoneStage), State.NextZoneRadius, Mathf.Clamp01(t));
            State.ZoneCenter = Vector2.Lerp(ZoneCenterAt(State.ZoneStage), State.NextZoneCenter, Mathf.Clamp01(t));

            bool warning = State.StageTimer < GameConfig.ZoneRedWarning;
            if (warning && !State.ZoneWarningPlaying)
            {
                State.ZoneWarningPlaying = true;
                AudioSynth.PlayUi("zone", 0.6f);
            }
            if (!warning) State.ZoneWarningPlaying = false;

            if (State.StageTimer <= 0f && State.ZoneStage < GameConfig.ZoneRadii.Length - 1)
            {
                State.ZoneStage++;
                State.NextZoneRadius = GameConfig.ZoneRadii[Mathf.Clamp(State.ZoneStage + 1, 0, GameConfig.ZoneRadii.Length - 1)];
                float angle = Random.value * Mathf.PI * 2f;
                float shift = Mathf.Max(0f, State.ZoneRadius - State.NextZoneRadius) * Random.Range(0.2f, 0.65f);
                State.NextZoneCenter = State.ZoneCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * shift;
                State.StageTimer = GameConfig.ZoneTimes[Mathf.Clamp(State.ZoneStage, 0, GameConfig.ZoneTimes.Length - 1)];
                Debug.Log(string.Format("[Samsar] Зона: этап {0}, радиус {1:F0}", State.ZoneStage, State.ZoneRadius));
            }

            // урон вне безопасной зоны
            float zoneDamage = GameConfig.ZoneDamagePerSecond * (State.ZoneWarningPlaying ? 1.6f : 1f);
            foreach (var v in Vehicle.All)
            {
                if (v == null || !v.Alive) continue;
                Vector2 flat = new Vector2(v.transform.position.x, v.transform.position.z);
                if (Vector2.Distance(flat, State.ZoneCenter) > State.ZoneRadius)
                {
                    v.ApplyDamage(zoneDamage * dt, v.transform.position, null);
                    if (v.IsPlayer && Random.value < dt * 0.6f)
                        Vfx.DamageNumber(v.transform.position + Vector3.up * 2.2f, "КРАСНАЯ ЗОНА", new Color(1f, 0.25f, 0.2f), 1f);
                }
            }

            if (zoneWall != null) zoneWall.UpdateZone(State.ZoneCenter, State.ZoneRadius, State.ZoneWarningPlaying);
        }

        float ZoneRadiusAt(int stage)
        {
            if (stage == 0) return GameConfig.ZoneStartRadius;
            return GameConfig.ZoneRadii[Mathf.Clamp(stage, 0, GameConfig.ZoneRadii.Length - 1)];
        }

        Vector2 ZoneCenterAt(int stage)
        {
            if (stage <= 0) return Vector2.zero;
            return State.ZoneCenter;
        }

        void UpdateLoot(float dt)
        {
            if (RangeMode) return;
            CrateTimer -= dt;
            if (CrateTimer <= 0f)
            {
                CrateTimer = GameConfig.LootRespawnTime;
                if (CountCrates() < GameConfig.LootCratesOnMap)
                {
                    Vector2 p = Random.insideUnitCircle * Mathf.Max(80f, State.ZoneRadius * 0.85f);
                    var world = Map.FindSpawnPoint(State.ZoneCenter + p, 30f);
                    LootSystem.SpawnCrate(world, State.Player != null ? State.Player.Level : 1);
                }
            }

            AirdropTimer -= dt;
            if (AirdropTimer <= 0f)
            {
                AirdropTimer = GameConfig.AirDropInterval;
                Vector2 p = Random.insideUnitCircle * Mathf.Max(100f, State.ZoneRadius * 0.6f);
                Vector3 pos = Map.FromGround(new Vector3(State.ZoneCenter.x + p.x, 0f, State.ZoneCenter.y + p.y), 1f);
                LootSystem.SpawnAirDrop(pos);
                if (State.Player != null && State.Player.Alive)
                    Vfx.DamageNumber(State.Player.transform.position + Vector3.up * 4f, "Воздушный груз сброшен!", LootTable.ColorOf(LootKind.AirDrop), 1.2f);
                AudioSynth.PlayUi("pickup", 0.7f);
            }
        }

        int CountCrates()
        {
            int n = 0;
            var root = GameObject.Find("~loot");
            if (root == null) return 0;
            foreach (Transform t in root.transform)
                if (t.GetComponent<LootPickup>() != null && !t.GetComponent<LootPickup>().IsAirDrop) n++;
            return n;
        }

        void UpdateAlive()
        {
            int alive = 0;
            foreach (var v in Vehicle.All)
                if (v != null && v.Alive) alive++;
            State.AliveCount = alive;
        }

        void CheckEnd()
        {
            var player = State.Player;
            if (player != null)
            {
                if (player.Alive)
                {
                    player.DeathHandled = false;
                    player.PendingRespawnPanel = false;
                }
                else if (!player.DeathHandled)
                {
                    player.DeathHandled = true;
                    bool canRespawn = player.RespawnCharges > 0 && (Time.time - BattleStartTime) < GameConfig.RespawnWindow;
                    player.PendingRespawnPanel = canRespawn;
                    if (!canRespawn) player.PendingRespawnPanel = false;
                }
            }

            if (State.AliveCount <= 1 || State.TimeLeft <= 0f) FinishBattle();
        }

        public void RegisterDeath(Vehicle victim, Vehicle killer)
        {
            State.AliveCount = Mathf.Max(0, State.AliveCount - 1);
            victim.Place = State.PlaceCounter;
            State.PlaceCounter = Mathf.Max(1, State.PlaceCounter - 1);
            if (victim.IsPlayer)
            {
                State.Stats.FinalPlace = victim.Place;
                State.Stats.Survived = false;
            }
            if (killer != null && killer.IsPlayer)
            {
                string label = victim.IsBot ? "+1 уничтожен (мародёр)" : "+1 уничтожен";
                Vfx.DamageNumber(victim.transform.position + Vector3.up * 3f, label, new Color(1f, 0.6f, 0.2f), 1.1f);
            }
            if (victim.IsBot && State.Player != null && State.Player.Alive)
                AudioSynth.PlayUi("hit", 0.25f);
        }

        public void ReportDestruction()
        {
            DestroyedObjects++;
        }

        public void FinishBattle()
        {
            if (State.State == BattleState.Finished) return;
            State.State = BattleState.Finished;
            var player = State.Player;
            if (player != null)
            {
                State.Stats.Survived = player.Alive;
                if (player.Alive)
                {
                    State.Stats.Won = true;
                    State.Stats.FinalPlace = 1;
                }
                else if (State.Stats.FinalPlace == 0) State.Stats.FinalPlace = player.Place;
            }
            State.Stats.Achievements = Achievements.Evaluate(State.Stats, State);
            ResultsData.Fill(this);
            Debug.Log("[Samsar] Бой завершён. Место: " + State.Stats.FinalPlace);
            Time.timeScale = 1f;
            SceneManager.LoadScene("Results");
        }

        public List<ScoreRow> BuildScoreTable()
        {
            var rows = new List<ScoreRow>();
            foreach (var v in Vehicle.All)
            {
                if (v == null) continue;
                rows.Add(new ScoreRow
                {
                    Name = v.IsPlayer ? "ВЫ" : v.DisplayName,
                    IsPlayer = v.IsPlayer,
                    IsBot = v.IsBot,
                    Damage = Mathf.RoundToInt(v.DamageDealt),
                    Kills = v.Kills,
                    Place = v.Place,
                    Alive = v.Alive,
                    Level = v.Level,
                    IsSquadMate = v.IsSquadMate,
                });
            }
            rows.Sort((a, b) =>
            {
                if (a.Alive != b.Alive) return a.Alive ? -1 : 1;
                int pa = a.Place == 0 ? 999 : a.Place;
                int pb = b.Place == 0 ? 999 : b.Place;
                return pa.CompareTo(pb);
            });
            return rows;
        }

        /// <summary>Выбор точки старта во время отсчёта (вызывается из HUD).</summary>
        public void ChooseSpawn(Vector3 pos)
        {
            if (State.State != BattleState.Countdown) return;
            ChosenSpawn = pos;
            SpawnChosen = true;
            if (PlayerVehicle != null)
            {
                PlayerVehicle.transform.position = pos + Vector3.up * 1.5f;
                if (PlayerVehicle.SquadMate != null)
                    PlayerVehicle.SquadMate.transform.position = pos + new Vector3(14f, 1.5f, 10f);
            }
            BeginBattle();
        }

        /// <summary>Игрок подобрал «Возрождение»: возродиться в безопасном месте.</summary>
        public void PlayerRespawn()
        {
            var v = State.Player;
            if (v == null || v.RespawnCharges <= 0) return;
            v.RespawnCharges--;
            Vector3 pos = Map.FindSpawnPoint(State.ZoneCenter, Mathf.Max(120f, State.ZoneRadius * 0.7f));
            v.Respawn(pos);
            v.PendingRespawnPanel = false;
            if (spectator != null) { spectator.enabled = false; spectator = null; }
            var rig = mainCamera != null ? mainCamera.GetComponent<PlayerCameraRig>() : null;
            if (rig != null) rig.enabled = true;
        }

        public void Spectate()
        {
            var v = State.Player;
            if (v != null) v.PendingRespawnPanel = false;
            var rig = mainCamera != null ? mainCamera.GetComponent<PlayerCameraRig>() : null;
            if (rig != null) rig.enabled = false;
            if (spectator == null && mainCamera != null) spectator = mainCamera.gameObject.AddComponent<CameraFly>();
        }

        public void LeaveBattle()
        {
            if (State.State == BattleState.Finished) return;
            State.State = BattleState.Finished;
            ResultsData.Fill(this);
            SceneManager.LoadScene("Results");
        }
    }

    /// <summary>Сбор данных для экрана результатов.</summary>
    public static class ResultsData
    {
        public static PlayerStats Stats;
        public static List<ScoreRow> Score = new List<ScoreRow>();
        public static int Participants;
        public static float BattleTime;
        public static int DestroyedObjects;

        public static void Fill(BattleManager bm)
        {
            Stats = bm.State.Stats;
            Score = bm.BuildScoreTable();
            Participants = bm.State.Participants.Count;
            BattleTime = Time.time - bm.BattleStartTime;
            DestroyedObjects = bm.DestroyedObjects;
        }
    }
}
