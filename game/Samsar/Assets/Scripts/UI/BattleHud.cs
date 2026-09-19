using System.Collections.Generic;
using UnityEngine;

namespace Samsar
{
    /// <summary>
    /// Боевой интерфейс: прочность, модули, броня, уровень машины, урон (полный HUD),
    /// миникарта, таймеры, зона, таблица боя, выбор модуля при повышении, выбор точки старта,
    /// панель после гибели и пауза.
    /// </summary>
    public class BattleHud : MonoBehaviour
    {
        public static BattleHud Instance;

        BattleManager battle;
        Vehicle player;
        MapGenerator map;
        Texture2D white;
        Texture2D minimap;
        bool showBigMap;
        bool paused;
        float hitMarkerTime = -10f;
        readonly List<string> log = new List<string>();
        readonly List<float> logTime = new List<float>();
        Vector2 scoreScroll;
        bool scoreVisible;

        GUIStyle label, small, big, center, title, right, boxed;
        bool stylesReady;

        void Awake()
        {
            Instance = this;
            white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
        }

        void Update()
        {
            if (battle == null) battle = BattleManager.Instance;
            if (battle == null) return;
            player = battle.State.Player;
            if (map == null) map = battle.Map;
            if (map != null && minimap == null) minimap = map.GetMinimap();

            if (Input.GetKeyDown(KeyCode.M)) showBigMap = !showBigMap;
            scoreVisible = Input.GetKey(KeyCode.Tab);

            // вспышка попадания
            if (player != null && player.LastDamageFrame == Time.frameCount) hitMarkerTime = Time.time;

            // пауза
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (paused) paused = false;
                else paused = true;
                Time.timeScale = paused ? 0f : 1f;
                Cursor.visible = paused;
                Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
            }
            if (!paused && Cursor.lockState != CursorLockMode.Locked && !showBigMap && battle.State.State == BattleState.Running)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public void AddLog(string text)
        {
            log.Insert(0, text);
            logTime.Insert(0, Time.time);
            if (log.Count > 6) { log.RemoveAt(log.Count - 1); logTime.RemoveAt(logTime.Count - 1); }
        }

        public void OnLevelUp(Vehicle v)
        {
            AddLog("Уровень машины: " + v.LevelRoman);
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            big = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            center = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            right = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleRight };
            boxed = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8) };
            stylesReady = true;
        }

        void DrawRect(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        void DrawBar(Rect r, float fill, Color fg, Color bg)
        {
            DrawRect(r, bg);
            DrawRect(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fill), r.height), fg);
        }

        void OnGUI()
        {
            if (battle == null) battle = BattleManager.Instance;
            if (battle == null) return;
            if (!stylesReady) EnsureStyles();
            var state = battle.State;

            if (state.State == BattleState.Countdown) { DrawSpawnSelection(); return; }

            DrawMinimap();

            if (player != null && player.Alive) DrawPlayerPanels();
            DrawTopBar();
            DrawAbilityBar();
            DrawCrosshair();
            DrawDamageVignette();
            DrawLog();

            if (player != null && player.PendingModuleLevel > 1 && player.Alive) DrawModuleChoice();
            if (player != null && !player.Alive && player.PendingRespawnPanel) DrawDeathPanel();
            if (player != null && !player.Alive && !player.PendingRespawnPanel) DrawSpectateBar();
            if (showBigMap) DrawBigMap();
            if (scoreVisible) DrawScoreboard();
            if (paused) DrawPause();
        }

        // ==================== верхняя строка ====================

        void DrawTopBar()
        {
            var state = battle.State;
            var r = new Rect(Screen.width * 0.5f - 250f, 8f, 500f, 46f);
            DrawRect(r, new Color(0f, 0f, 0f, 0.5f));
            int minutes = Mathf.FloorToInt(Mathf.Max(0f, state.TimeLeft) / 60f);
            int seconds = Mathf.FloorToInt(Mathf.Max(0f, state.TimeLeft) % 60f);
            string zoneText;
            if (state.ZoneWarningPlaying) zoneText = "<color=#ff5533>ЗОНА СЖИМАЕТСЯ: " + Mathf.CeilToInt(state.StageTimer) + " с</color>";
            else zoneText = "До сужения: " + Mathf.CeilToInt(state.StageTimer) + " с";
            GUI.Label(new Rect(r.x + 12f, r.y + 4f, 240f, 22f), "Бой: " + minutes.ToString("00") + ":" + seconds.ToString("00"), label);
            GUI.Label(new Rect(r.x + 12f, r.y + 24f, 300f, 20f), zoneText, small);
            GUI.Label(new Rect(r.x + 260f, r.y + 4f, 230f, 22f), "Живых: <b>" + state.AliveCount + "</b> из " + state.Participants.Count, right);
            GUI.Label(new Rect(r.x + 260f, r.y + 24f, 230f, 20f), "Этап зоны " + (state.ZoneStage + 1) + " | Радиус " + Mathf.RoundToInt(state.ZoneRadius) + " м", right);

            if (!RangeMode)
            {
                float airdrop = Mathf.Max(0f, battle.AirdropTimer);
                GUI.Label(new Rect(Screen.width * 0.5f - 120f, 58f, 240f, 20f),
                    "<color=#66ffaa>Воздушный груз через " + Mathf.CeilToInt(airdrop) + " с</color>", center);
            }
        }

        bool RangeMode { get { return battle != null && battle.RangeMode; } }

        // ==================== панель игрока ====================

        void DrawPlayerPanels()
        {
            // прочность
            var hpRect = new Rect(20f, Screen.height - 130f, 320f, 26f);
            float hpFill = player.Health / Mathf.Max(1f, player.Stats.MaxHealth);
            DrawBar(hpRect, hpFill, hpFill > 0.5f ? new Color(0.35f, 0.85f, 0.35f) : hpFill > 0.25f ? new Color(0.95f, 0.75f, 0.25f) : new Color(0.95f, 0.3f, 0.25f), new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(new Rect(hpRect.x + 6f, hpRect.y, hpRect.width, hpRect.height),
                Mathf.CeilToInt(player.Health) + " / " + Mathf.RoundToInt(player.Stats.MaxHealth) + " ед. прочности", label);
            if (player.ShieldUntil > Time.time)
                GUI.Label(new Rect(hpRect.x + 200f, hpRect.y, 130f, hpRect.height),
                    "<color=#88ccff>ЭКРАН " + Mathf.CeilToInt(player.ShieldUntil - Time.time) + "с</color>", label);

            // модули и броня
            var modRect = new Rect(20f, Screen.height - 100f, 320f, 42f);
            DrawRect(modRect, new Color(0f, 0f, 0f, 0.5f));
            string modules = "Гусеница: " + (player.TrackBroken ? "<color=#ff5533>повреждена</color>" : "<color=#88ff88>ок</color>")
                + "  Двиг.: " + (player.EngineBroken ? "<color=#ff5533>повреждён</color>" : "<color=#88ff88>ок</color>")
                + "  Орудие: " + (player.GunBroken ? "<color=#ff5533>повреждено</color>" : "<color=#88ff88>ок</color>");
            GUI.Label(new Rect(modRect.x + 8f, modRect.y + 2f, 320f, 20f), modules, label);
            GUI.Label(new Rect(modRect.x + 8f, modRect.y + 21f, 320f, 20f),
                "Броня лоб/борт/корма: " + Mathf.RoundToInt(player.Stats.ArmorFront) + " / " +
                Mathf.RoundToInt(player.Stats.ArmorSide) + " / " + Mathf.RoundToInt(player.Stats.ArmorRear) + " мм", small);

            // боезапас и перезарядка
            var ammoRect = new Rect(20f, Screen.height - 54f, 320f, 40f);
            DrawRect(ammoRect, new Color(0f, 0f, 0f, 0.5f));
            float reload = player.Stats.Reload > 0f ? 1f - Mathf.Clamp01(player.ReloadTimer / player.Stats.Reload) : 1f;
            GUI.Label(new Rect(ammoRect.x + 8f, ammoRect.y + 2f, 300f, 20f),
                "Снаряды: <b>" + player.Ammo + "</b> / " + player.Stats.AmmoMax +
                "   Урон: <b>" + Mathf.RoundToInt(player.Stats.Damage) + "</b> (пробой " + Mathf.RoundToInt(player.Stats.Penetration) + " мм)", label);
            DrawBar(new Rect(ammoRect.x + 8f, ammoRect.y + 24f, 304f, 10f), reload,
                reload >= 1f ? new Color(0.4f, 0.75f, 1f) : new Color(0.9f, 0.6f, 0.2f), new Color(0f, 0f, 0f, 0.7f));

            // уровень и опыт
            var lvlRect = new Rect(Screen.width * 0.5f - 190f, Screen.height - 74f, 380f, 58f);
            DrawRect(lvlRect, new Color(0f, 0f, 0f, 0.5f));
            float xpFill = 1f;
            if (player.Level < GameConfig.MaxLevel)
            {
                float prev = GameConfig.LevelXp[player.Level - 1];
                float next = GameConfig.LevelXp[player.Level];
                xpFill = Mathf.Clamp01((player.Experience - prev) / Mathf.Max(1f, next - prev));
            }
            GUI.Label(new Rect(lvlRect.x + 8f, lvlRect.y + 2f, 364f, 22f),
                "<b>" + player.Spec.Name + "</b> — уровень <b>" + player.LevelRoman + "</b> (" + player.Level + "/7)   " +
                TankSpecs.ClassName(player.Spec.Class), label);
            DrawBar(new Rect(lvlRect.x + 8f, lvlRect.y + 28f, 364f, 12f), xpFill, new Color(1f, 0.85f, 0.3f), new Color(0f, 0f, 0f, 0.7f));
            GUI.Label(new Rect(lvlRect.x + 8f, lvlRect.y + 40f, 364f, 18f),
                player.Level < GameConfig.MaxLevel ? "Опыт: " + Mathf.RoundToInt(player.Experience) + " / " + Mathf.RoundToInt(GameConfig.LevelXp[player.Level]) : "Максимальный уровень", small);

            // сводка по бою (урон)
            var st = battle.State.Stats;
            var stRect = new Rect(20f, Screen.height - 200f, 320f, 62f);
            DrawRect(stRect, new Color(0f, 0f, 0f, 0.42f));
            GUI.Label(new Rect(stRect.x + 8f, stRect.y + 2f, 300f, 20f), "Урон: <b>" + Mathf.RoundToInt(st.DamageDealt) + "</b>   Уничтожено: <b>" + st.Kills + "</b>", label);
            GUI.Label(new Rect(stRect.x + 8f, stRect.y + 22f, 300f, 18f), "Выстрелов: " + st.ShotsFired + " | Попаданий: " + st.Hits + " | Пробитий: " + st.Penetrations, small);
            GUI.Label(new Rect(stRect.x + 8f, stRect.y + 40f, 300f, 18f), "Получено урона: " + Mathf.RoundToInt(st.DamageTaken) + " | Добыча: " + st.LootTaken, small);

            if (player.SquadMate != null && player.SquadMate.Alive)
            {
                var mate = player.SquadMate;
                var mr = new Rect(20f, Screen.height - 250f, 320f, 44f);
                DrawRect(mr, new Color(0f, 0f, 0f, 0.42f));
                GUI.Label(new Rect(mr.x + 8f, mr.y + 2f, 300f, 20f), "Взвод: " + mate.Spec.Name + " (" + mate.LevelRoman + ")", label);
                DrawBar(new Rect(mr.x + 8f, mr.y + 24f, 200f, 10f), mate.Health / mate.Stats.MaxHealth, new Color(0.4f, 0.8f, 0.95f), new Color(0f, 0f, 0f, 0.6f));
                float dist = Vector3.Distance(player.transform.position, mate.transform.position);
                GUI.Label(new Rect(mr.x + 214f, mr.y + 20f, 100f, 18f), Mathf.RoundToInt(dist) + " м", small);
            }
        }

        // ==================== умения ====================

        void DrawAbilityBar()
        {
            var r = new Rect(Screen.width - 320f, Screen.height - 150f, 300f, 130f);
            DrawRect(r, new Color(0f, 0f, 0f, 0.5f));
            GUI.Label(new Rect(r.x + 8f, r.y + 2f, 280f, 20f), "Боевые умения", label);
            DrawAbilitySlot(new Rect(r.x + 8f, r.y + 24f, 284f, 46f), 1, "Q");
            DrawAbilitySlot(new Rect(r.x + 8f, r.y + 74f, 284f, 46f), 2, "E");
        }

        void DrawAbilitySlot(Rect r, int slot, string key)
        {
            var id = player.AbilityOf(slot);
            float cd = player.CooldownOf(slot);
            float total = player.AbilityCooldownTotal(slot);
            DrawRect(r, new Color(0.1f, 0.12f, 0.14f, 0.9f));
            if (cd > 0f) DrawRect(new Rect(r.x, r.y, r.width * Mathf.Clamp01(cd / Mathf.Max(1f, total)), r.height), new Color(0.4f, 0.15f, 0.1f, 0.65f));
            else DrawRect(new Rect(r.x, r.y, r.width, r.height), new Color(0.15f, 0.35f, 0.2f, 0.55f));
            GUI.Label(new Rect(r.x + 8f, r.y + 2f, r.width - 16f, 20f), "<b>[" + key + "] " + TankSpecs.AbilityName(id) + "</b>", label);
            GUI.Label(new Rect(r.x + 8f, r.y + 22f, r.width - 16f, 20f),
                cd > 0f ? "перезарядка " + Mathf.CeilToInt(cd) + " с" : "<color=#88ff88>готово</color>", small);
        }

        // ==================== прицел и маркеры ====================

        void DrawCrosshair()
        {
            if (player == null || !player.Alive) return;
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            float spread = player.CurrentDispersion;
            var rig = Camera.main != null ? Camera.main.GetComponent<PlayerCameraRig>() : null;
            bool sniper = rig != null && rig.Sniper;

            if (sniper)
            {
                DrawRect(new Rect(0f, 0f, Screen.width, cy - 2f), new Color(0f, 0f, 0f, 0.85f));
                DrawRect(new Rect(0f, cy + 2f, Screen.width, Screen.height - cy), new Color(0f, 0f, 0f, 0.85f));
                DrawRect(new Rect(0f, 0f, cx - 2f, Screen.height), new Color(0f, 0f, 0f, 0.85f));
                DrawRect(new Rect(cx + 2f, 0f, Screen.width - cx, Screen.height), new Color(0f, 0f, 0f, 0.85f));
                DrawRect(new Rect(cx - 260f, cy - 1f, 520f, 2f), new Color(0.1f, 0.1f, 0.1f, 0.9f));
                DrawRect(new Rect(cx - 1f, cy - 260f, 2f, 520f), new Color(0.1f, 0.1f, 0.1f, 0.9f));
                for (int i = -5; i <= 5; i++)
                {
                    if (i == 0) continue;
                    DrawRect(new Rect(cx + i * 46f - 1f, cy - 7f, 2f, 14f), new Color(0.05f, 0.05f, 0.05f, 0.95f));
                    DrawRect(new Rect(cx - 7f, cy + i * 46f - 1f, 14f, 2f), new Color(0.05f, 0.05f, 0.05f, 0.95f));
                }
                GUI.Label(new Rect(cx - 60f, cy + 274f, 120f, 22f), "×8 СНАЙПЕРСКИЙ", center);
            }
            else
            {
                float gap = 12f + spread * 55f;
                float len = 16f;
                Color c = hitMarkerTime > Time.time - 0.12f ? new Color(1f, 0.4f, 0.2f, 1f) : new Color(1f, 1f, 1f, 0.85f);
                DrawRect(new Rect(cx - gap - len, cy - 1f, len, 2f), c);
                DrawRect(new Rect(cx + gap, cy - 1f, len, 2f), c);
                DrawRect(new Rect(cx - 1f, cy - gap - len, 2f, len), c);
                DrawRect(new Rect(cx - 1f, cy + gap, 2f, len), c);
                DrawRect(new Rect(cx - 1f, cy - 1f, 2f, 2f), c);
            }

            // индикатор сведения и готовности орудия
            string ready = player.CanShoot ? "<color=#88ff88>ОРУДИЕ ГОТОВО</color>" : "<color=#ffaa55>ПЕРЕЗАРЯДКА " + player.ReloadTimer.ToString("0.0") + " с</color>";
            if (player.Ammo <= 0) ready = "<color=#ff5533>НЕТ СНАРЯДОВ — ИЩИ ДОБЫЧУ</color>";
            GUI.Label(new Rect(cx - 150f, cy + 82f, 300f, 22f), ready, center);
        }

        void DrawDamageVignette()
        {
            if (player == null || !player.Alive) return;
            float hp = player.Health / Mathf.Max(1f, player.Stats.MaxHealth);
            float recent = Mathf.Clamp01(1f - (Time.time - player.LastDamageTime) / 0.6f);
            float intensity = Mathf.Max(1f - hp, 0f) * 0.25f + recent * 0.35f;
            if (intensity <= 0.01f) return;
            Color c = new Color(0.6f, 0.05f, 0.05f, intensity * 0.5f);
            DrawRect(new Rect(0f, 0f, Screen.width, 12f), c);
            DrawRect(new Rect(0f, Screen.height - 12f, Screen.width, 12f), c);
            DrawRect(new Rect(0f, 0f, 12f, Screen.height), c);
            DrawRect(new Rect(Screen.width - 12f, 0f, 12f, Screen.height), c);
        }

        void DrawLog()
        {
            float y = 120f;
            for (int i = 0; i < log.Count; i++)
            {
                if (Time.time - logTime[i] > 7f) continue;
                float alpha = Mathf.Clamp01(1f - (Time.time - logTime[i]) / 7f);
                GUI.Label(new Rect(20f, y, 420f, 22f), "<color=#ffdd99>" + log[i] + "</color>", label);
                y += 22f;
            }
        }

        // ==================== миникарта ====================

        Rect MinimapRect
        {
            get { return new Rect(Screen.width - 270f, 20f, 250f, 250f); }
        }

        Vector2 MapPoint(Rect r, Vector3 world)
        {
            float u = world.x / MapGenerator.Size + 0.5f;
            float v = world.z / MapGenerator.Size + 0.5f;
            return new Vector2(r.x + u * r.width, r.y + v * r.height);
        }

        void DrawCircle(Rect mapRect, Vector2 worldCenter, float worldRadius, Color color, int segments = 64)
        {
            float prevX = 0f, prevY = 0f;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 p = new Vector3(worldCenter.x + Mathf.Cos(a) * worldRadius, 0f, worldCenter.y + Mathf.Sin(a) * worldRadius);
                var sp = MapPoint(mapRect, p);
                if (i > 0)
                {
                    float dx = sp.x - prevX, dy = sp.y - prevY;
                    int steps = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dy * dy)));
                    for (int s = 0; s < steps; s++)
                    {
                        float t = s / (float)steps;
                        DrawRect(new Rect(prevX + dx * t, prevY + dy * t, 1.6f, 1.6f), color);
                    }
                }
                prevX = sp.x; prevY = sp.y;
            }
        }

        void DrawMinimap()
        {
            var r = MinimapRect;
            DrawRect(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), new Color(0f, 0f, 0f, 0.7f));
            if (minimap != null) GUI.DrawTexture(r, minimap, ScaleMode.StretchToFill, false);

            var state = battle.State;
            DrawCircle(r, state.ZoneCenter, state.ZoneRadius, new Color(1f, 0.35f, 0.2f, 0.9f));
            DrawCircle(r, state.NextZoneCenter, Mathf.Max(30f, state.NextZoneRadius), new Color(1f, 0.9f, 0.3f, 0.6f));

            // техника
            foreach (var v in Vehicle.All)
            {
                if (v == null) continue;
                var sp = MapPoint(r, v.transform.position);
                if (v.IsPlayer)
                {
                    DrawRect(new Rect(sp.x - 4f, sp.y - 4f, 8f, 8f), new Color(0.4f, 1f, 0.4f, 1f));
                }
                else if (v.IsSquadMate)
                {
                    DrawRect(new Rect(sp.x - 3.5f, sp.y - 3.5f, 7f, 7f), new Color(0.4f, 0.8f, 1f, 1f));
                }
                else if (v.Alive && v.VisibleToMe(player))
                {
                    DrawRect(new Rect(sp.x - 3.5f, sp.y - 3.5f, 7f, 7f), new Color(1f, 0.3f, 0.25f, 1f));
                }
            }

            // добыча
            var lootRoot = GameObject.Find("~loot");
            if (lootRoot != null)
            {
                foreach (Transform t in lootRoot.transform)
                {
                    var p = t.GetComponent<LootPickup>();
                    if (p == null || p.Taken) continue;
                    var sp = MapPoint(r, t.position);
                    Color c = p.IsAirDrop ? new Color(0.3f, 1f, 0.5f, 1f) : new Color(0.9f, 0.8f, 0.3f, 0.9f);
                    float s = p.IsAirDrop ? 7f : 4f;
                    DrawRect(new Rect(sp.x - s * 0.5f, sp.y - s * 0.5f, s, s), c);
                }
            }

            GUI.Label(new Rect(r.x, r.y + r.height + 4f, r.width, 20f), "M — большая карта, Tab — таблица", small);
        }

        void DrawBigMap()
        {
            float size = Mathf.Min(Screen.width, Screen.height) * 0.8f;
            var r = new Rect((Screen.width - size) * 0.5f, (Screen.height - size) * 0.5f, size, size);
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));
            if (minimap != null) GUI.DrawTexture(r, minimap, ScaleMode.StretchToFill, false);
            var state = battle.State;
            DrawCircle(r, state.ZoneCenter, state.ZoneRadius, new Color(1f, 0.3f, 0.2f, 1f), 96);
            DrawCircle(r, state.NextZoneCenter, Mathf.Max(30f, state.NextZoneRadius), new Color(1f, 0.9f, 0.3f, 0.8f), 96);
            foreach (var v in Vehicle.All)
            {
                if (v == null || !v.Alive) continue;
                var sp = MapPoint(r, v.transform.position);
                if (v.IsPlayer) DrawRect(new Rect(sp.x - 5f, sp.y - 5f, 10f, 10f), new Color(0.4f, 1f, 0.4f));
                else if (v.IsSquadMate) DrawRect(new Rect(sp.x - 5f, sp.y - 5f, 10f, 10f), new Color(0.4f, 0.8f, 1f));
                else if (v.VisibleToMe(player)) DrawRect(new Rect(sp.x - 4f, sp.y - 4f, 8f, 8f), new Color(1f, 0.3f, 0.25f));
            }
            GUI.Label(new Rect(r.x, r.y - 30f, r.width, 26f), "КАРТА 3×3 КМ", title);
        }

        // ==================== выбор точки старта ====================

        void DrawSpawnSelection()
        {
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(new Rect(0f, 30f, Screen.width, 40f), "ВЫБОР ТОЧКИ СТАРТА", title);
            GUI.Label(new Rect(0f, 70f, Screen.width, 24f),
                "Начало боя через " + Mathf.CeilToInt(battle.State.Countdown) + " с — нажми на точку на карте (или ждём автоматически)", center);

            float size = Mathf.Min(Screen.width, Screen.height) * 0.7f;
            var r = new Rect((Screen.width - size) * 0.5f, 110f, size, size);
            DrawRect(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), new Color(0f, 0f, 0f, 0.8f));
            if (minimap != null) GUI.DrawTexture(r, minimap, ScaleMode.StretchToFill, false);

            for (int i = 0; i < battle.SpawnOptions.Count; i++)
            {
                var sp = MapPoint(r, battle.SpawnOptions[i]);
                var btn = new Rect(sp.x - 46f, sp.y - 16f, 92f, 32f);
                if (GUI.Button(btn, "Точка " + (i + 1)))
                {
                    battle.ChooseSpawn(battle.SpawnOptions[i]);
                    AudioSynth.PlayUi("click", 0.6f);
                }
            }
        }

        // ==================== выбор модуля ====================

        void DrawModuleChoice()
        {
            var v = player;
            int level = v.PendingModuleLevel;
            var opts = v.Spec.ModulesForLevel(level);
            float w = 380f, h = 260f;
            var r = new Rect(Screen.width * 0.5f - w * 0.5f, Screen.height * 0.5f - h * 0.5f, w, h);
            DrawRect(new Rect(r.x - 6f, r.y - 6f, r.width + 12f, r.height + 12f), new Color(0f, 0f, 0f, 0.75f));
            DrawRect(r, new Color(0.09f, 0.11f, 0.13f, 0.98f));
            GUI.Label(new Rect(r.x, r.y + 8f, r.width, 30f), "УРОВЕНЬ " + Vehicle.Roman(level) + " — ВЫБЕРИ МОДУЛЬ", center);
            GUI.Label(new Rect(r.x, r.y + 40f, r.width, 22f), "Машина улучшилась. Установи один из двух модулей:", center);

            for (int i = 0; i < opts.Length && i < 2; i++)
            {
                var btn = new Rect(r.x + 18f, r.y + 76f + i * 74f, r.width - 36f, 64f);
                DrawRect(btn, new Color(0.14f, 0.17f, 0.2f, 1f));
                GUI.Label(new Rect(btn.x + 12f, btn.y + 6f, btn.width - 24f, 24f), "<b>" + opts[i].Name + "</b>", label);
                GUI.Label(new Rect(btn.x + 12f, btn.y + 30f, btn.width - 24f, 24f), opts[i].Desc, small);
                if (GUI.Button(btn, GUIContent.none))
                {
                    v.ChooseModule(level, i);
                    v.PendingModuleLevel = 0;
                    AddLog("Установлен модуль: " + opts[i].Name);
                    AudioSynth.PlayUi("levelup", 0.7f);
                }
            }
            GUI.Label(new Rect(r.x, r.y + h - 28f, r.width, 22f), "Клик по модулю. Меню не блокирует бой — не останавливайся!", small);
        }

        // ==================== панель после гибели ====================

        void DrawDeathPanel()
        {
            float w = 460f, h = 260f;
            var r = new Rect(Screen.width * 0.5f - w * 0.5f, Screen.height * 0.5f - h * 0.5f, w, h);
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));
            DrawRect(r, new Color(0.1f, 0.08f, 0.08f, 0.98f));
            GUI.Label(new Rect(r.x, r.y + 14f, r.width, 34f), "МАШИНА УНИЧТОЖЕНА", title);
            GUI.Label(new Rect(r.x, r.y + 58f, r.width, 24f),
                "Место в бою: <b>" + player.Place + "</b> из " + battle.State.Participants.Count, center);
            GUI.Label(new Rect(r.x, r.y + 86f, r.width, 24f),
                "Урон: " + Mathf.RoundToInt(battle.State.Stats.DamageDealt) + " | Уничтожено: " + battle.State.Stats.Kills, center);
            GUI.Label(new Rect(r.x, r.y + 116f, r.width, 24f),
                "Заряды «Возрождение»: <b>" + player.RespawnCharges + "</b>", center);

            if (GUI.Button(new Rect(r.x + 24f, r.y + 160f, r.width - 48f, 40f), "Возродиться (" + player.RespawnCharges + ")"))
            {
                battle.PlayerRespawn();
                AddLog("Возрождение!");
            }
            if (GUI.Button(new Rect(r.x + 24f, r.y + 206f, (r.width - 60f) * 0.5f, 36f), "Наблюдать"))
            {
                battle.Spectate();
            }
            if (GUI.Button(new Rect(r.x + 36f + (r.width - 60f) * 0.5f, r.y + 206f, (r.width - 60f) * 0.5f, 36f), "Выйти из боя"))
            {
                battle.LeaveBattle();
            }
        }

        void DrawSpectateBar()
        {
            var r = new Rect(Screen.width * 0.5f - 260f, Screen.height - 70f, 520f, 50f);
            DrawRect(r, new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(new Rect(r.x, r.y + 4f, r.width, 22f), "Наблюдение: WASD — полёт, ЛКМ — обзор, Shift — ускорение", center);
            if (GUI.Button(new Rect(r.x + r.width * 0.5f - 90f, r.y + 24f, 180f, 22f), "Выйти из боя")) battle.LeaveBattle();
        }

        // ==================== таблица ====================

        void DrawScoreboard()
        {
            var rows = battle.BuildScoreTable();
            float w = 640f, h = Mathf.Min(560f, 90f + rows.Count * 22f);
            var r = new Rect(Screen.width * 0.5f - w * 0.5f, Screen.height * 0.5f - h * 0.5f, w, h);
            DrawRect(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), new Color(0f, 0f, 0f, 0.8f));
            DrawRect(r, new Color(0.08f, 0.1f, 0.12f, 0.98f));
            GUI.Label(new Rect(r.x, r.y + 8f, r.width, 26f), "ТАБЛИЦА БОЯ (живых: " + battle.State.AliveCount + ")", center);

            float y = r.y + 40f;
            GUI.Label(new Rect(r.x + 12f, y, 60f, 20f), "<b>Место</b>", label);
            GUI.Label(new Rect(r.x + 80f, y, 220f, 20f), "<b>Участник</b>", label);
            GUI.Label(new Rect(r.x + 310f, y, 90f, 20f), "<b>Уровень</b>", label);
            GUI.Label(new Rect(r.x + 400f, y, 90f, 20f), "<b>Урон</b>", label);
            GUI.Label(new Rect(r.x + 500f, y, 80f, 20f), "<b>Фраги</b>", label);
            y += 24f;

            for (int i = 0; i < rows.Count && y < r.y + h - 24f; i++)
            {
                var row = rows[i];
                if (row.IsPlayer) DrawRect(new Rect(r.x + 8f, y - 2f, r.width - 16f, 20f), new Color(0.3f, 0.35f, 0.2f, 0.6f));
                if (row.IsSquadMate) DrawRect(new Rect(r.x + 8f, y - 2f, r.width - 16f, 20f), new Color(0.2f, 0.3f, 0.4f, 0.5f));
                string place = row.Alive ? "—" : row.Place.ToString();
                GUI.Label(new Rect(r.x + 12f, y, 60f, 20f), place, label);
                GUI.Label(new Rect(r.x + 80f, y, 220f, 20f), row.Name + (row.Alive ? "" : " <color=#888888>(уничтожен)</color>"), label);
                GUI.Label(new Rect(r.x + 310f, y, 90f, 20f), Vehicle.Roman(Mathf.Clamp(row.Level, 1, 7)), label);
                GUI.Label(new Rect(r.x + 400f, y, 90f, 20f), row.Damage.ToString(), label);
                GUI.Label(new Rect(r.x + 500f, y, 80f, 20f), row.Kills.ToString(), label);
                y += 22f;
            }
        }

        // ==================== пауза ====================

        void DrawPause()
        {
            float w = 460f, h = 400f;
            var r = new Rect(Screen.width * 0.5f - w * 0.5f, Screen.height * 0.5f - h * 0.5f, w, h);
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.65f));
            DrawRect(r, new Color(0.09f, 0.11f, 0.13f, 0.99f));
            GUI.Label(new Rect(r.x, r.y + 12f, r.width, 30f), "ПАУЗА", title);

            GUI.Label(new Rect(r.x + 20f, r.y + 60f, 420f, 22f), "Чувствительность мыши: " + GameConfig.MouseSensitivity.ToString("0.0"), label);
            GameConfig.MouseSensitivity = GUI.HorizontalSlider(new Rect(r.x + 20f, r.y + 84f, 420f, 20f), GameConfig.MouseSensitivity, 0.2f, 3f);

            GUI.Label(new Rect(r.x + 20f, r.y + 116f, 420f, 22f), "Качество графики: " + (GameConfig.Quality == 0 ? "низкое" : GameConfig.Quality == 1 ? "среднее" : "высокое"), label);
            if (GUI.Button(new Rect(r.x + 20f, r.y + 140f, 130f, 30f), "Ниже")) { GameConfig.Quality = Mathf.Max(0, GameConfig.Quality - 1); GameConfig.ApplyQuality(); }
            if (GUI.Button(new Rect(r.x + 160f, r.y + 140f, 130f, 30f), "Выше")) { GameConfig.Quality = Mathf.Min(2, GameConfig.Quality + 1); GameConfig.ApplyQuality(); }

            bool inv = GUI.Toggle(new Rect(r.x + 20f, r.y + 182f, 420f, 24f), GameConfig.InvertY, " Инвертировать мышь по вертикали");
            GameConfig.InvertY = inv;

            bool models = GUI.Toggle(new Rect(r.x + 20f, r.y + 206f, 420f, 24f), GameConfig.UseImportedModels, " Модели из Blender (если выключить — упрощённые)");
            if (models != GameConfig.UseImportedModels)
            {
                GameConfig.UseImportedModels = models;
                AddLog(models ? "Модели из Blender: вкл (применится в следующем бою)" : "Упрощённые модели: вкл (применится в следующем бою)");
            }

            GUI.Label(new Rect(r.x + 20f, r.y + 214f, 420f, 22f), "Управление: WASD — движение, A/D — поворот, ПКМ — снайперский прицел,\nЛКМ — выстрел, Q/E — умения, M — карта, Tab — таблица, Space — тормоз", small);

            if (GUI.Button(new Rect(r.x + 20f, r.y + 280f, 200f, 40f), "Продолжить"))
            {
                paused = false;
                Time.timeScale = 1f;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (GUI.Button(new Rect(r.x + 240f, r.y + 280f, 200f, 40f), "Выйти в меню"))
            {
                Time.timeScale = 1f;
                GameManager.ToMenu();
            }
            GUI.Label(new Rect(r.x + 20f, r.y + 330f, 420f, 60f),
                "Подсказка: попадание в борт и корму пробивает легче, лобовая броня держит. Красная зона наносит урон — уходи вовремя.", small);
        }
    }
}
