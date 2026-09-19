// Менеджер боя: спавн участников, зона, лут, воздушные грузы, статистика и конец боя.
using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    /// <summary>Реестр всех машин в бою (к нему обращаются ИИ, зона, HUD).</summary>
    public static class TankRegistry
    {
        public static readonly List<TankController> All = new List<TankController>();
        public static TankController Player;

        public static int AliveCount(bool enemiesOnly = false)
        {
            int n = 0;
            foreach (var t in All)
                if (!t.Dead && (!enemiesOnly || (!t.IsPlayer && !t.IsAlly))) n++;
            return n;
        }

        public static IEnumerable<TankController> Alive()
        {
            foreach (var t in All) if (!t.Dead) yield return t;
        }
    }

    public class BattleManager : MonoBehaviour
    {
        public static BattleManager Instance;

        public ZoneController zone;
        public LootSpawner loot;
        public TankLibrary library;
        public Transform[] spawnPoints;

        public float TimeLeft { get; private set; }
        public bool Finished { get; private set; }
        public int PlayerPlace { get; private set; }
        public float BattleElapsed => Rules.BattleTime - TimeLeft;

        float detectTimer, airDropTimer;
        int lastAlive;

        void Awake()
        {
            Instance = this;
            TimeLeft = Rules.BattleTime;
        }

        void Start()
        {
            SpawnEveryone();
            if (zone != null) zone.Begin();
            lastAlive = TankRegistry.All.Count;
        }

        void SpawnEveryone()
        {
            var pts = spawnPoints != null && spawnPoints.Length > 0 ? spawnPoints : FallbackSpawns(16);
            int idx = 0;

            // игрок
            var playerTank = SpawnTank(GameSession.TankId, true, false, GameSession.PlayerName, pts[idx++ % pts.Length].position);
            TankRegistry.Player = playerTank;
            PlayerInput.Attach(playerTank);
            if (CameraRig.Instance != null) CameraRig.Instance.Follow(playerTank);

            // ИИ-напарник по взводу
            if (GameSession.SquadSize >= 2)
            {
                var mate = SpawnTank(RandomTankId(), false, true, "Напарник", pts[idx++ % pts.Length].position + Vector3.right * 6f);
                mate.vision.rangeMult = 1.1f;
            }

            // противники: боты разной сложности (мародёры + охотники)
            int total = Mathf.Clamp(GameSession.BotsInBattle, 4, Rules.MaxCombatants - 2);
            for (int i = 0; i < total; i++)
            {
                var pos = pts[idx++ % pts.Length].position + Random.insideUnitSphere * 8f;
                pos.y = 2f;
                var bot = SpawnTank(RandomTankId(), false, false, "Мародёр-" + (i + 1), pos);
                var ai = bot.gameObject.AddComponent<BotController>();
                ai.Init(bot, DifficultyFor(i));
                if (i % 4 == 0) bot.vision.rangeMult = 1.15f;   // «главари» видят дальше
            }
            HUD.Toast(string.Format("Бой начался: {0} машин, зона сужается", TankRegistry.All.Count), HUD.ToastKind.Info);
        }

        BotDifficulty DifficultyFor(int i)
        {
            var baseD = GameSession.Difficulty;
            if (i % 7 == 3 && baseD != BotDifficulty.Hard) baseD = (BotDifficulty)((int)baseD + 1);
            return baseD;
        }

        static string RandomTankId()
        {
            var r = TankSpec.Roster[Random.Range(0, TankSpec.Roster.Length)];
            return r.id;
        }

        Transform[] FallbackSpawns(int n)
        {
            var list = new List<Transform>();
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Spawn_fallback" + i);
                float a = i / (float)n * Mathf.PI * 2f;
                go.transform.position = new Vector3(Mathf.Cos(a) * 700f, 2f, Mathf.Sin(a) * 700f);
                list.Add(go.transform);
            }
            return list.ToArray();
        }

        public TankController SpawnTank(string specId, bool isPlayer, bool isAlly, string callsign, Vector3 pos)
        {
            var spec = TankSpec.Get(specId);
            var model = library != null ? library.Get(spec.modelName) : null;
            GameObject go;
            if (model != null)
            {
                go = Instantiate(model, pos, Quaternion.Euler(0f, Random.value * 360f, 0f));
                go.name = "Tank_" + callsign;
                go.transform.localScale = Vector3.one;
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.transform.localScale = new Vector3(3f, 1.2f, 6f);
                go.transform.position = pos;
                go.name = "Tank_fallback_" + callsign;
            }
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();

            var tank = go.GetComponent<TankController>();
            if (tank == null) tank = go.AddComponent<TankController>();
            tank.Configure(spec, isPlayer, isAlly, callsign);
            TankRegistry.All.Add(tank);
            tank.Died += OnTankDied;
            return tank;
        }

        void Update()
        {
            if (Finished) return;
            TimeLeft -= Time.deltaTime;

            detectTimer -= Time.deltaTime;
            if (detectTimer <= 0f)
            {
                detectTimer = 0.2f;
                UpdateDetection();
            }

            airDropTimer -= Time.deltaTime;
            if (airDropTimer <= 0f)
            {
                airDropTimer = Rules.AirDropInterval;
                if (loot != null) loot.SpawnAirDrop();
            }

            if (awaitingRespawn)
            {
                respawnDecisionTimer -= Time.deltaTime;
                if (respawnDecisionTimer <= 0f) { awaitingRespawn = false; HUD.HideRespawnPrompt(); }
            }

            // напарник-бот возрождается через время
            TryRespawnAlly();

            int alive = TankRegistry.AliveCount();
            if (alive != lastAlive)
            {
                lastAlive = alive;
                HUD.SetAliveCount(alive);
            }

            if (TankRegistry.Player != null && TankRegistry.Player.Dead && !Finished && !awaitingRespawn)
                FinishBattle();
            else if (alive <= 1 && !Finished)
                FinishBattle();
            else if (TimeLeft <= 0f && !Finished)
                FinishBattle();
        }

        public bool awaitingRespawn;
        float respawnDecisionTimer;

        /// <summary>Игрок нажал «Возродиться» после уничтожения (жетон получен из лута).</summary>
        public void DoPlayerRespawn()
        {
            var player = TankRegistry.Player;
            if (player == null) return;
            Vector3 pos = zone != null ? zone.RandomPointInside() : Vector3.zero;
            if (player.Respawn(pos)) awaitingRespawn = false;
        }

        float allyRespawnTimer;
        void TryRespawnAlly()
        {
            if (GameSession.SquadSize < 2) return;
            TankController mate = null;
            foreach (var t in TankRegistry.All)
                if (t.IsAlly) { mate = t; break; }
            if (mate == null || !mate.Dead) { allyRespawnTimer = Rules.SquadRespawnTime; return; }

            allyRespawnTimer -= Time.deltaTime;
            if (allyRespawnTimer <= 0f)
            {
                var player = TankRegistry.Player;
                Vector3 pos = player != null ? player.transform.position + player.transform.right * -8f : Vector3.zero;
                mate.armor.RepairModules();
                mate.armor.Heal(mate.armor.HullMax);
                mate.Respawn(pos);
                allyRespawnTimer = Rules.SquadRespawnTime;
                HUD.Toast("Напарник вернулся в бой", HUD.ToastKind.Info);
            }
        }

        void UpdateDetection()
        {
            var player = TankRegistry.Player;
            if (player == null) return;

            foreach (var t in TankRegistry.All)
            {
                if (t.IsPlayer || t.IsAlly)
                {
                    t.vision.VisibleToPlayer = true;
                    t.vision.SetRenderersVisible(true);
                    continue;
                }
                bool visible = !t.Dead && player.vision.CanSee(t);
                t.vision.VisibleToPlayer = visible;
                t.vision.SetRenderersVisible(visible);
                t.vision.CanSeePlayer = t.vision.CanSee(player);
            }
        }

        void OnTankDied(TankController t)
        {
            if (t == TankRegistry.Player)
            {
                // «Возрождение» из лута: игрок остаётся в бою, если есть жетон
                if (t.RespawnAvailable)
                {
                    awaitingRespawn = true;
                    respawnDecisionTimer = 15f;
                    HUD.ShowRespawnPrompt(t);
                    return;
                }
            }
            HUD.KillFeed(t.Callsign, t.IsPlayer || t.IsAlly);
        }

        public void FinishBattle()
        {
            if (Finished) return;
            Finished = true;

            var player = TankRegistry.Player;
            int alive = TankRegistry.AliveCount();
            int rank = 1;
            foreach (var t in TankRegistry.All)
            {
                if (player == null) break;
                if (!t.Dead && t != player) rank++;
            }
            PlayerPlace = Mathf.Max(1, rank);

            GameSession.LastPlace = PlayerPlace;
            GameSession.LastKills = player != null ? player.Kills : 0;
            GameSession.LastDamage = player != null ? player.DamageDealt : 0f;
            GameSession.LastSurvived = player != null ? player.SurvivalTime : 0f;
            GameSession.LastLoot = player != null ? player.LootPicked : 0;
            GameSession.LastVictory = PlayerPlace == 1 && player != null && !player.Dead;

            Profile.RecordBattle(GameSession.LastPlace, GameSession.LastKills, GameSession.LastDamage,
                                 GameSession.LastVictory, alive);
            HUD.ShowPostBattle();
        }

        public void ReturnToAngar()
        {
            TankRegistry.All.Clear();
            TankRegistry.Player = null;
            UnityEngine.SceneManagement.SceneManager.LoadScene("Main");
        }
    }
}
