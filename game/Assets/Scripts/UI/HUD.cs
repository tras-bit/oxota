// Боевой интерфейс: прочность, модули, уровень машины, снаряды, умения, миникарта,
// попадания, лента уничтожений, выбор модулей прокачки, итоги боя.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Samsar
{
    public class HUD : MonoBehaviour
    {
        public enum ToastKind { Info, Good, Bad }
        public enum HitKind { Penetration, Bounce }

        public static HUD Instance;

        Canvas canvas;
        Font font;

        Text timerText, aliveText, zoneText, hpText, levelText, ammoText, repairText, speedText;
        Image hpFill, xpFill, crosshairReload;
        RectTransform crosshair, hitMarker;
        Text stateText;
        readonly List<Text> killFeed = new List<Text>();
        readonly List<Text> toasts = new List<Text>();
        readonly List<DamagePopupItem> popups = new List<DamagePopupItem>();
        Transform abilityPanel;
        readonly List<AbilityButton> abilityButtons = new List<AbilityButton>();
        GameObject upgradePanel, respawnPanel, postPanel, statsPanel, pausePanel;
        Text postBody;
        Text upgradeTitle;
        readonly List<Button> upgradeButtons = new List<Button>();
        readonly List<Text> upgradeLabels = new List<Text>();
        MinimapWidget minimap;
        float crosshairPunch, hitMarkerTimer;
        UpgradeModule[] pendingPair;
        float upgradeTimeout;

        class DamagePopupItem
        {
            public Text text;
            public Vector3 world;
            public float life;
            public float speed;
        }

        class AbilityButton
        {
            public Text label, cooldownText;
            public Image fill;
        }

        // ---------- создание интерфейса ----------
        void Awake()
        {
            Instance = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildCanvas();
            BuildTopBar();
            BuildBottomLeft();
            BuildAbilityPanel();
            BuildCrosshair();
            BuildKillFeed();
            BuildPanels();
            minimap = gameObject.AddComponent<MinimapWidget>();
            minimap.Build(canvas.transform, font);
            HUD.Toast("Бой начался. Цель: выжить любой ценой", ToastKind.Info);
        }

        void BuildCanvas()
        {
            var go = new GameObject("HUDCanvas");
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
        }

        Text MakeText(Transform parent, string content, int size, TextAnchor anchor,
                      Vector2 anchorMin, Vector2 anchorMax, Vector2 offset, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.text = content;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = offset;
            rt.sizeDelta = new Vector2(420, size + 12);
            return t;
        }

        Image MakeImage(Transform parent, Color color, Vector2 min, Vector2 max, Vector2 size, Vector2 pos)
        {
            var go = new GameObject("Image");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return img;
        }

        void BuildTopBar()
        {
            var top = new GameObject("TopBar").transform;
            top.SetParent(canvas.transform, false);
            var bar = MakeImage(top, new Color(0f, 0f, 0f, 0.35f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                new Vector2(620, 54), new Vector2(0, -34));
            timerText = MakeText(bar.transform, "20:00", 30, TextAnchor.MiddleCenter,
                                 new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -34), Color.white);
            aliveText = MakeText(bar.transform, "Живых: 30", 20, TextAnchor.MiddleLeft,
                                 new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-276, -34),
                                 new Color(0.85f, 0.9f, 1f));
            zoneText = MakeText(bar.transform, "Зона: сужение", 20, TextAnchor.MiddleRight,
                                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(264, -34),
                                new Color(1f, 0.85f, 0.4f));
        }

        void BuildBottomLeft()
        {
            var rootT = new GameObject("StatusPanel").transform;
            rootT.SetParent(canvas.transform, false);
            MakeImage(rootT, new Color(0f, 0f, 0f, 0.4f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                      new Vector2(460, 132), new Vector2(250, 86));

            MakeImage(rootT, new Color(0.15f, 0.15f, 0.15f, 0.9f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                      new Vector2(400, 22), new Vector2(236, 122));
            hpFill = MakeImage(rootT, new Color(0.35f, 0.8f, 0.4f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                               new Vector2(396, 18), new Vector2(234, 122));
            hpFill.type = Image.Type.Filled;
            hpFill.fillMethod = Image.FillMethod.Horizontal;
            hpText = MakeText(rootT, "900 / 900", 18, TextAnchor.MiddleLeft,
                              new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(120, 122), Color.white);

            xpFill = MakeImage(rootT, new Color(0.4f, 0.75f, 1f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                               new Vector2(400, 12), new Vector2(236, 100));
            xpFill.type = Image.Type.Filled;
            xpFill.fillMethod = Image.FillMethod.Horizontal;
            levelText = MakeText(rootT, "Уровень I  ·  опыт 0", 16, TextAnchor.MiddleLeft,
                                 new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(120, 100),
                                 new Color(0.8f, 0.9f, 1f));

            MakeText(rootT, "Модули:", 15, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(0f, 0f),
                     new Vector2(60, 74), new Color(0.75f, 0.78f, 0.8f));
            MakeText(rootT, "корпус", 15, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(0f, 0f),
                     new Vector2(180, 74), new Color(0.7f, 0.85f, 0.7f));
            MakeText(rootT, "двигатель", 15, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(0f, 0f),
                     new Vector2(300, 74), new Color(0.7f, 0.85f, 0.7f));
            MakeText(rootT, "орудие", 15, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(0f, 0f),
                     new Vector2(410, 74), new Color(0.7f, 0.85f, 0.7f));

            ammoText = MakeText(rootT, "Снаряды: 24", 18, TextAnchor.MiddleLeft,
                                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(110, 46), Color.white);
            speedText = MakeText(rootT, "0 км/ч", 16, TextAnchor.MiddleLeft,
                                 new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(300, 46),
                                 new Color(0.85f, 0.85f, 0.85f));
            repairText = MakeText(rootT, "H — ремонт напарнику · G — передать снаряды и заряды", 14, TextAnchor.MiddleLeft,
                                  new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(270, 20),
                                  new Color(0.7f, 0.75f, 0.8f));
        }

        void BuildAbilityPanel()
        {
            var panel = new GameObject("Abilities");
            panel.transform.SetParent(canvas.transform, false);
            var prt = panel.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0f);
            prt.anchorMax = new Vector2(0.5f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.anchoredPosition = new Vector2(0, 28);
            prt.sizeDelta = new Vector2(420, 110);
            abilityPanel = panel.transform;

            for (int i = 0; i < Rules.AbilitiesPerTank; i++)
            {
                var bg = MakeImage(abilityPanel, new Color(0.1f, 0.12f, 0.14f, 0.75f),
                                   new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                   new Vector2(150, 86), new Vector2((i - 0.5f) * 170f, 48));
                var fill = MakeImage(bg.transform, new Color(1f, 0.6f, 0.2f, 0.35f),
                                     new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                     new Vector2(150, 86), Vector2.zero);
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Vertical;
                fill.fillOrigin = (int)Image.OriginVertical.Bottom;
                var label = MakeText(bg.transform, "Умение", 17, TextAnchor.MiddleCenter,
                                     new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                     new Vector2(0, 12), Color.white);
                var cd = MakeText(bg.transform, "[1]", 15, TextAnchor.MiddleCenter,
                                  new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                  new Vector2(0, -22), new Color(0.9f, 0.9f, 0.6f));
                abilityButtons.Add(new AbilityButton { label = label, cooldownText = cd, fill = fill });
            }
        }

        void BuildCrosshair()
        {
            var go = new GameObject("Crosshair");
            go.transform.SetParent(canvas.transform, false);
            crosshair = go.AddComponent<RectTransform>();
            crosshair.anchorMin = crosshair.anchorMax = new Vector2(0.5f, 0.5f);
            crosshair.sizeDelta = new Vector2(60, 60);

            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                var tick = MakeImage(crosshair, new Color(1f, 1f, 1f, 0.85f),
                                     new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                     new Vector2(3, 14), Vector2.zero);
                tick.rectTransform.localRotation = Quaternion.Euler(0, 0, a);
                tick.rectTransform.anchoredPosition = Quaternion.Euler(0, 0, a) * new Vector3(0, 16f, 0);
            }
            var center = MakeImage(crosshair, new Color(1f, 0.4f, 0.2f, 0.9f),
                                   new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                   new Vector2(4, 4), Vector2.zero);
            crosshairReload = MakeImage(crosshair, new Color(1f, 0.8f, 0.3f, 0.9f),
                                        new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                        new Vector2(90, 6), new Vector2(0, -22));
            crosshairReload.type = Image.Type.Filled;
            crosshairReload.fillMethod = Image.FillMethod.Horizontal;

            var hm = new GameObject("HitMarker");
            hm.transform.SetParent(canvas.transform, false);
            hitMarker = hm.AddComponent<RectTransform>();
            hitMarker.anchorMin = hitMarker.anchorMax = new Vector2(0.5f, 0.5f);
            hitMarker.sizeDelta = new Vector2(40, 40);
            for (int i = 0; i < 2; i++)
            {
                var line = MakeImage(hitMarker, new Color(1f, 1f, 1f, 0.9f),
                                     new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                     new Vector2(3, 26), Vector2.zero);
                line.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f + i * 90f);
            }
            hitMarker.gameObject.SetActive(false);

            stateText = MakeText(canvas.transform, "", 22, TextAnchor.MiddleCenter,
                                 new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 new Vector2(0, -120), new Color(1f, 0.9f, 0.7f));
        }

        void BuildKillFeed()
        {
            for (int i = 0; i < 6; i++)
            {
                var t = MakeText(canvas.transform, "", 17, TextAnchor.MiddleRight,
                                 new Vector2(1f, 1f), new Vector2(1f, 1f),
                                 new Vector2(-210, -70 - i * 26), Color.white);
                killFeed.Add(t);
            }
        }

        // ---------- панели ----------
        void BuildPanels()
        {
            upgradePanel = MakeModal("UpgradePanel", "Новый уровень машины: выбери модуль", out upgradeTitle, 520, 260);
            for (int i = 0; i < 2; i++)
            {
                int idx = i;
                var btn = MakeButton(upgradePanel.transform, new Vector2((i - 0.5f) * 250f, -20), new Vector2(230, 120), "",
                                    () => ChooseUpgrade(idx));
                upgradeButtons.Add(btn);
                upgradeLabels.Add(btn.GetComponentInChildren<Text>());
            }
            upgradePanel.SetActive(false);

            respawnPanel = MakeModal("RespawnPanel", "«Возрождение» доступно! Вернуться в бой?", out _, 560, 190);
            MakeButton(respawnPanel.transform, new Vector2(-120, -40), new Vector2(200, 56), "Возродиться", () =>
            {
                if (BattleManager.Instance != null) BattleManager.Instance.DoPlayerRespawn();
                HideRespawnPrompt();
            });
            MakeButton(respawnPanel.transform, new Vector2(120, -40), new Vector2(200, 56), "Наблюдать", () =>
            {
                if (BattleManager.Instance != null) BattleManager.Instance.awaitingRespawn = false;
                HideRespawnPrompt();
                if (BattleManager.Instance != null) BattleManager.Instance.FinishBattle();
            });
            respawnPanel.SetActive(false);

            postPanel = null;
            statsPanel = MakeStatsPanel();
            pausePanel = MakePausePanel();
        }

        GameObject MakeModal(string name, string title, out Text titleText, float w, float h)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(canvas.transform, false);
            var rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            var img = panel.AddComponent<Image>();
            img.color = new Color(0.06f, 0.08f, 0.1f, 0.92f);
            titleText = MakeText(panel.transform, title, 22, TextAnchor.MiddleCenter,
                                 new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -36), Color.white);
            return panel;
        }

        Button MakeButton(Transform parent, Vector2 pos, Vector2 size, string label, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject("Button");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.16f, 0.19f, 0.22f, 0.95f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(action);
            var t = MakeText(go.transform, label, 18, TextAnchor.MiddleCenter,
                             new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Color.white);
            t.rectTransform.sizeDelta = size;
            return btn;
        }

        GameObject MakeStatsPanel()
        {
            var panel = MakeModal("StatsPanel", "Состояние боя (Tab)", out _, 720, 520);
            panel.transform.SetAsLastSibling();
            panel.SetActive(false);
            return panel;
        }

        GameObject MakePausePanel()
        {
            var panel = MakeModal("PausePanel", "Пауза", out _, 420, 240);
            MakeButton(panel.transform, new Vector2(0, -20), new Vector2(280, 54), "Вернуться в бой",
                       () => { Time.timeScale = 1f; pausePanel.SetActive(false); });
            MakeButton(panel.transform, new Vector2(0, -84), new Vector2(280, 54), "Выйти в ангар", () =>
            {
                Time.timeScale = 1f;
                if (BattleManager.Instance != null) BattleManager.Instance.ReturnToAngar();
            });
            panel.SetActive(false);
            return panel;
        }

        // ---------- обновление ----------
        void Update()
        {
            if (BattleManager.Instance != null)
            {
                float t = Mathf.Max(0f, BattleManager.Instance.TimeLeft);
                timerText.text = string.Format("{0:00}:{1:00}", Mathf.FloorToInt(t / 60f), Mathf.FloorToInt(t % 60f));
            }
            if (ZoneController.Instance != null)
            {
                var z = ZoneController.Instance;
                zoneText.text = string.Format("Зона: {0} м · до сужения {1:0} с",
                    Mathf.RoundToInt(z.YellowRadius), z.PhaseTimeLeft);
            }

            var player = TankRegistry.Player;
            if (player != null)
            {
                UpdatePlayerStatus(player);
                UpdateAbilities(player);
                if (minimap != null) minimap.Refresh(player);
            }

            UpdatePopups();
            UpdateKillFeedTimers();

            hitMarkerTimer -= Time.deltaTime;
            if (hitMarkerTimer <= 0f && hitMarker != null && hitMarker.gameObject.activeSelf)
                hitMarker.gameObject.SetActive(false);

            crosshairPunch = Mathf.MoveTowards(crosshairPunch, 0f, Time.deltaTime * 4f);
            if (crosshair != null)
                crosshair.localScale = Vector3.one * (1f + crosshairPunch * 0.35f);

            if (pendingPair != null)
            {
                upgradeTimeout -= Time.deltaTime;
                if (upgradeTimeout <= 0f) ChooseUpgrade(0);
            }
        }

        void UpdatePlayerStatus(TankController player)
        {
            var a = player.armor;
            float frac = Mathf.Clamp01(a.HullHp / Mathf.Max(1f, a.HullMax));
            hpFill.fillAmount = frac;
            hpFill.color = frac > 0.5f ? new Color(0.35f, 0.8f, 0.4f)
                          : frac > 0.25f ? new Color(0.95f, 0.8f, 0.3f) : new Color(0.9f, 0.3f, 0.25f);
            hpText.text = string.Format("{0} / {1}  ·  броня {2:0} мм",
                Mathf.RoundToInt(a.HullHp), Mathf.RoundToInt(a.HullMax), a.spec.armorHull);

            xpFill.fillAmount = Samsar.Progression.LevelProgress(player.Xp);
            levelText.text = string.Format("Уровень {0}  ·  опыт {1:0}  ·  до следующего {2:0}",
                Roman(player.Level), player.Xp, Samsar.Progression.XpToNext(player.Xp));

            ammoText.text = string.Format("Снаряды: {0}   ·   перезарядка {1:0.0} с",
                player.gun.Ammo, Mathf.Max(0f, player.gun.ReloadLeft));
            crosshairReload.fillAmount = player.gun.ReloadProgress;

            var rb = player.GetComponent<Rigidbody>();
            speedText.text = string.Format("{0:0} км/ч", rb != null ? rb.velocity.magnitude * 3.6f : 0f);

            stateText.text = player.Stunned ? string.Format("ОГЛУШЕН · {0:0.0} с", player.StunnedUntil - Time.time)
                           : player.armor.TracksBroken ? "ГУСЕНИЦА СБИТА"
                           : player.armor.EngineDamaged ? "ДВИГАТЕЛЬ ПОВРЕЖДЁН"
                           : player.armor.GunDamaged ? "ОРУДИЕ ПОВРЕЖДЕНО" : "";
            stateText.color = player.Stunned ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.5f, 0.35f);
        }

        void UpdateAbilities(TankController player)
        {
            var list = player.abilities.abilities;
            for (int i = 0; i < abilityButtons.Count && i < list.Count; i++)
            {
                var a = list[i];
                var ui = abilityButtons[i];
                ui.label.text = a.title;
                ui.fill.fillAmount = 1f - a.Progress;
                ui.cooldownText.text = string.Format("[{0}]  заряды: {1}{2}",
                    i + 1, a.charges, a.Ready ? "" : string.Format(" · {0:0}с", a.cdLeft));
                ui.cooldownText.color = a.Ready ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.6f, 0.5f);
            }
        }

        static string Roman(int n)
        {
            string[] r = { "I", "II", "III", "IV", "V", "VI", "VII" };
            return r[Mathf.Clamp(n - 1, 0, r.Length - 1)];
        }

        // ---------- всплывающие сообщения ----------
        public static void Toast(string msg, ToastKind kind)
        {
            if (Instance != null) Instance.ShowToast(msg, kind);
            Debug.Log("[HUD] " + msg);
        }

        void ShowToast(string msg, ToastKind kind)
        {
            Text t = null;
            foreach (var x in toasts) if (x.text == "") { t = x; break; }
            if (t == null && toasts.Count < 6)
            {
                t = MakeText(canvas.transform, "", 19, TextAnchor.MiddleCenter,
                             new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                             new Vector2(0, 180 + toasts.Count * 28), Color.white);
                toasts.Add(t);
            }
            if (t == null) { toastQueue.Add(msg); return; }
            t.text = msg;
            t.color = kind == ToastKind.Bad ? new Color(1f, 0.5f, 0.45f)
                    : kind == ToastKind.Good ? new Color(0.6f, 1f, 0.65f) : Color.white;
            toastTimes[t] = Time.time + 4f;
        }

        readonly List<string> toastQueue = new List<string>();
        readonly Dictionary<Text, float> toastTimes = new Dictionary<Text, float>();

        void UpdateKillFeedTimers()
        {
            var expire = new List<Text>();
            foreach (var kv in toastTimes)
                if (kv.Value < Time.time) expire.Add(kv.Key);
            foreach (var t in expire)
            {
                t.text = "";
                toastTimes.Remove(t);
                if (toastQueue.Count > 0) { ShowToast(toastQueue[0], ToastKind.Info); toastQueue.RemoveAt(0); }
            }
        }

        // ---------- попадания ----------
        public static void DamagePopup(Vector3 world, int amount, HitKind kind)
        {
            if (Instance == null || !Settings.ShowDamageNumbers) return;
            Instance.SpawnPopup(world, amount.ToString(), kind);
        }

        void SpawnPopup(Vector3 world, string text, HitKind kind)
        {
            var t = MakeText(canvas.transform, text, kind == HitKind.Penetration ? 26 : 20,
                             TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(0f, 0f),
                             Vector2.zero, kind == HitKind.Penetration ? new Color(1f, 0.85f, 0.3f) : new Color(0.8f, 0.8f, 0.8f));
            popups.Add(new DamagePopupItem { text = t, world = world, life = 1.1f, speed = 40f });
        }

        void UpdatePopups()
        {
            var cam = Camera.main;
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var p = popups[i];
                p.life -= Time.deltaTime;
                if (p.life <= 0f || cam == null)
                {
                    Destroy(p.text.gameObject);
                    popups.RemoveAt(i);
                    continue;
                }
                Vector3 sp = cam.WorldToScreenPoint(p.world);
                if (sp.z < 0f) { p.text.enabled = false; continue; }
                p.text.enabled = true;
                p.text.rectTransform.position = sp + Vector3.up * (1.1f - p.life) * p.speed;
                var c = p.text.color; c.a = Mathf.Clamp01(p.life); p.text.color = c;
            }
        }

        public static void ShowHitMarker(bool penetrated, bool critical)
        {
            if (Instance == null) return;
            Instance.hitMarkerTimer = 0.25f;
            Instance.hitMarker.gameObject.SetActive(true);
            foreach (var img in Instance.hitMarker.GetComponentsInChildren<Image>())
                img.color = critical ? new Color(1f, 0.4f, 0.25f, 0.95f)
                          : penetrated ? new Color(1f, 1f, 1f, 0.9f) : new Color(0.7f, 0.7f, 0.7f, 0.7f);
        }

        public static void PunchCrosshair(float amount)
        {
            if (Instance != null) Instance.crosshairPunch = Mathf.Clamp01(amount);
        }

        public static void SetSniperMode(bool on)
        {
            if (Instance == null || Instance.crosshair == null) return;
            Instance.crosshair.localScale = Vector3.one * (on ? 0.4f : 1f);
        }

        public static void SetAliveCount(int n)
        {
            if (Instance != null) Instance.aliveText.text = "Живых: " + n;
        }

        public static void KillFeed(string callsign, bool friendly)
        {
            if (Instance == null) return;
            Instance.AddKillLine(callsign, friendly);
        }

        void AddKillLine(string callsign, bool friendly)
        {
            for (int i = killFeed.Count - 1; i > 0; i--)
                killFeed[i].text = killFeed[i - 1].text;
            killFeed[0].text = friendly ? "✖ потерян: " + callsign : "✖ уничтожен: " + callsign;
            killFeed[0].color = friendly ? new Color(1f, 0.5f, 0.5f) : new Color(0.6f, 1f, 0.7f);
        }

        // ---------- прокачка ----------
        public static void ShowUpgradeChoice(TankController tank, int level)
        {
            if (Instance == null) return;
            Instance.pendingPair = UpgradeModule.RollPair(level, new System.Random(Random.Range(0, 99999)));
            Instance.upgradePanel.SetActive(true);
            Instance.upgradeTitle.text = "Уровень " + Roman(level) + ": выбери улучшение";
            for (int i = 0; i < Instance.upgradeLabels.Count; i++)
            {
                var m = Instance.pendingPair[i];
                Instance.upgradeLabels[i].text = m.title + "\n" + m.desc;
            }
            Instance.upgradeTimeout = 12f;
        }

        void ChooseUpgrade(int index)
        {
            if (pendingPair == null) return;
            var player = TankRegistry.Player;
            if (player != null && index < pendingPair.Length)
                player.ApplyModule(pendingPair[index].id);
            pendingPair = null;
            upgradePanel.SetActive(false);
        }

        // ---------- возрождение и итоги ----------
        public static void ShowRespawnPrompt(TankController tank)
        {
            if (Instance == null) return;
            Instance.respawnPanel.SetActive(true);
        }

        public static void HideRespawnPrompt()
        {
            if (Instance != null) Instance.respawnPanel.SetActive(false);
        }

        public static void ToggleStats()
        {
            if (Instance == null) return;
            bool on = !Instance.statsPanel.activeSelf;
            Instance.statsPanel.SetActive(on);
            if (!on) return;
            var txt = Instance.statsPanel.GetComponentInChildren<Text>();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            foreach (var t in TankRegistry.All)
                sb.AppendLine(string.Format("   {0,-16} {1,-4} уровень {2}  урон {3,5}  фраги {4}  {5}",
                    t.Callsign, t.Dead ? "✖" : "●", Roman(t.Level),
                    Mathf.RoundToInt(t.DamageDealt), t.Kills,
                    t.IsPlayer ? "(вы)" : t.IsAlly ? "(напарник)" : ""));
            // второй Text — список; создаём при необходимости
            var listText = Instance.statsListText;
            if (listText == null)
            {
                listText = Instance.MakeText(Instance.statsPanel.transform, "", 17, TextAnchor.UpperLeft,
                    new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(20, -60), Color.white);
                listText.rectTransform.anchorMin = new Vector2(0f, 0f);
                listText.rectTransform.anchorMax = new Vector2(1f, 1f);
                listText.rectTransform.sizeDelta = new Vector2(-40, -90);
                listText.alignment = TextAnchor.UpperLeft;
                Instance.statsListText = listText;
            }
            listText.text = sb.ToString();
            if (txt != null) txt.text = "";
        }

        Text statsListText;

        public static void TogglePauseMenu()
        {
            if (Instance == null) return;
            bool on = !Instance.pausePanel.activeSelf;
            Instance.pausePanel.SetActive(on);
            Time.timeScale = on ? 0f : 1f;
        }

        // ---------- итоги боя ----------
        public static void ShowPostBattle()
        {
            if (Instance != null) Instance.BuildPostBattle();
        }

        void BuildPostBattle()
        {
            if (postPanel == null)
            {
                postPanel = MakeModal("PostBattle", "Бой окончен", out _, 760, 560);
                postPanel.transform.SetAsLastSibling();
                postBody = MakeText(postPanel.transform, "", 21, TextAnchor.UpperLeft,
                                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                    new Vector2(0, -150), Color.white);
                postBody.rectTransform.sizeDelta = new Vector2(700, 340);
                MakeButton(postPanel.transform, new Vector2(-140, -220), new Vector2(240, 56), "В ангар", () =>
                {
                    if (BattleManager.Instance != null) BattleManager.Instance.ReturnToAngar();
                });
                MakeButton(postPanel.transform, new Vector2(140, -220), new Vector2(240, 56), "Ещё бой", () =>
                {
                    UnityEngine.SceneManagement.SceneManager.LoadScene("Battle");
                });
            }
            postPanel.SetActive(true);
            string body = string.Format(
                "\n   Место: {0} из {1}{2}\n\n   Уничтожено: {3}\n   Нанесено урона: {4}\n   Добычи собрано: {5}\n   Время в бою: {6:0} с\n\n   {7}",
                GameSession.LastPlace, TankRegistry.All.Count,
                GameSession.LastVictory ? "  — ПОБЕДА!" : "",
                GameSession.LastKills, Mathf.RoundToInt(GameSession.LastDamage),
                GameSession.LastLoot, GameSession.LastSurvived,
                GameSession.LastVictory ? "Вы последний стальной охотник!" : "Охота продолжается");
            postBody.text = body;
            AchievementSystem.ReportLastBattle();
        }
    }
}
