// Ангар: выбор машины, сложности, взвода и старт боя. Интерфейс строится кодом.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Samsar
{
    /// <summary>Меню-ангар: 3D-превью машины, ТТХ, настройки боя, статистика и достижения.</summary>
    public class AngarUI : MonoBehaviour
    {
        public TankLibrary library;
        public Transform previewPoint;          // куда ставим модель для показа
        public Camera previewCamera;
        public Light spot;

        Canvas canvas;
        Font font;
        Text tankName, tankStats, abilityText, profileText, achievementsText;
        Text difficultyText, squadText, lastBattleText;
        RectTransform statsPanel;
        GameObject previewModel;
        int tankIndex;
        float spin;
        readonly List<Button> tankButtons = new List<Button>();

        static readonly Color Panel = new Color(0.07f, 0.08f, 0.07f, 0.86f);
        static readonly Color Accent = new Color(0.72f, 0.66f, 0.32f, 1f);

        void Start()
        {
            Application.targetFrameRate = 60;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildUI();
            SelectTank(0);
            RefreshProfile();
        }

        // ---------- каркас интерфейса ----------
        void BuildUI()
        {
            var go = new GameObject("AngarCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvas.sortingOrder = 5;

            // заголовок
            var title = MakeText(canvas.transform, "SAMSAR — «Стальной охотник»", 46, TextAnchor.MiddleCenter, Accent);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(900f, 60f));

            var subtitle = MakeText(canvas.transform,
                "один режим · последний выживший · 30+ машин · зона сжимается · прокачка I–VII прямо в бою",
                20, TextAnchor.MiddleCenter, new Color(0.85f, 0.85f, 0.8f));
            Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(1200f, 30f));

            // левая колонка — список машин
            // высота левой колонки считается по числу машин, иначе список вылезает за панель
            float listHeight = 120f + TankSpec.Roster.Length * 74f;
            var left = MakePanel(canvas.transform, new Vector2(0f, 0.5f), new Vector2(36f, 0f),
                                 new Vector2(360f, Mathf.Min(listHeight, 820f)));
            var leftTitle = MakeText(left, "МАШИНЫ", 24, TextAnchor.MiddleLeft, Accent);
            Place(leftTitle.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -18f), new Vector2(280f, 30f));
            float y = -60f;
            for (int i = 0; i < TankSpec.Roster.Length; i++)
            {
                int idx = i;
                string label = TankSpec.Roster[i].special ? "★ " + TankSpec.Roster[i].title
                                                          : "   " + TankSpec.Roster[i].title;
                var b = MakeButton(left, label, new Vector2(0f, 1f),
                                   new Vector2(16f, y), new Vector2(328f, 62f));
                b.onClick.AddListener(() => SelectTank(idx));
                tankButtons.Add(b);
                y -= 74f;
            }
            var hint = MakeText(left, "A/D, стрелки или клик — выбор машины\nEnter — в бой\n★ — спецмашина: открыта сразу", 16,
                                TextAnchor.UpperLeft, new Color(0.75f, 0.75f, 0.7f));
            Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(20f, 16f), new Vector2(320f, 60f));

            // правая колонка — настройки боя и статистика
            var right = MakePanel(canvas.transform, new Vector2(1f, 0.5f), new Vector2(-36f, 0f), new Vector2(400f, 520f));
            var rightTitle = MakeText(right, "БОЙ", 24, TextAnchor.MiddleLeft, Accent);
            Place(rightTitle.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -18f), new Vector2(240f, 30f));

            difficultyText = MakeText(right, "", 20, TextAnchor.MiddleCenter, Color.white);
            Place(difficultyText.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -64f), new Vector2(170f, 44f));
            MakeButton(right, "◀", new Vector2(1f, 1f), new Vector2(-190f, -64f), new Vector2(52f, 44f))
                .onClick.AddListener(() => ChangeDifficulty(-1));
            MakeButton(right, "▶", new Vector2(1f, 1f), new Vector2(-20f, -64f), new Vector2(52f, 44f))
                .onClick.AddListener(() => ChangeDifficulty(1));

            squadText = MakeText(right, "", 20, TextAnchor.MiddleCenter, Color.white);
            Place(squadText.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -124f), new Vector2(170f, 44f));
            MakeButton(right, "сменить", new Vector2(0f, 1f), new Vector2(216f, -124f), new Vector2(130f, 44f))
                .onClick.AddListener(ToggleSquad);

            lastBattleText = MakeText(right, "", 17, TextAnchor.UpperLeft, new Color(0.8f, 0.8f, 0.75f));
            Place(lastBattleText.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -186f), new Vector2(356f, 120f));

            var goBtn = MakeButton(right, "В БОЙ", new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(340f, 74f));
            goBtn.onClick.AddListener(StartBattle);
            var goText = goBtn.GetComponentInChildren<Text>();
            if (goText != null) { goText.fontSize = 30; goText.color = new Color(1f, 0.96f, 0.8f); }

            achievementsText = MakeText(right, "", 16, TextAnchor.LowerLeft, new Color(0.72f, 0.72f, 0.68f));
            Place(achievementsText.rectTransform, new Vector2(0f, 0f), new Vector2(20f, 110f), new Vector2(356f, 120f));

            // центр — ТТХ выбранной машины
            statsPanel = MakePanel(canvas.transform, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(620f, 190f));
            tankName = MakeText(statsPanel, "", 30, TextAnchor.UpperCenter, Accent);
            Place(tankName.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(580f, 40f));
            tankStats = MakeText(statsPanel, "", 19, TextAnchor.UpperLeft, Color.white);
            Place(tankStats.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -60f), new Vector2(300f, 120f));
            abilityText = MakeText(statsPanel, "", 19, TextAnchor.UpperLeft, new Color(0.85f, 0.85f, 0.8f));
            Place(abilityText.rectTransform, new Vector2(1f, 1f), new Vector2(-24f, -60f), new Vector2(280f, 120f));

            // итоги профиля слева снизу
            profileText = MakeText(canvas.transform, "", 18, TextAnchor.LowerLeft, new Color(0.8f, 0.8f, 0.75f));
            Place(profileText.rectTransform, new Vector2(0f, 0f), new Vector2(36f, 20f), new Vector2(420f, 120f));
        }

        // ---------- выбор машины ----------
        void SelectTank(int index)
        {
            if (TankSpec.Roster.Length == 0) return;
            tankIndex = Mathf.Clamp(index, 0, TankSpec.Roster.Length - 1);
            var spec = TankSpec.Roster[tankIndex];
            GameSession.TankId = spec.id;

            if (tankName != null) tankName.text = (spec.special ? "★ " : "") + spec.title;
            if (tankStats != null)
                tankStats.text = string.Format(
                    "Прочность: {0}\nСкорость: {1:0} км/ч\nОрудие: {2:0} мм / {3:0} урона\nПерезарядка: {4:0.0} с\n"
                    + "Броня: корпус {5:0} / башня {6:0} мм\nОбзор: {7:0} м · БК: {8} снарядов",
                    spec.hp, spec.SpeedKmh, spec.penetration, spec.damage, spec.reload,
                    spec.armorHull, spec.armorTurret, spec.viewRange, spec.shells);
            if (abilityText != null)
                abilityText.text = "Умения:\n• авиаудар по области (обязательное)\n• " + AbilityTitle(spec.ability2) +
                                   "\n\nГабариты: " + spec.width.ToString("0.0") + " × " +
                                   spec.length.ToString("0.0") + " м, корпус " +
                                   spec.hullHeight.ToString("0.0") + " м" +
                                   (spec.special ? "\n\n★ спецмашина — доступна сразу" : "");
            if (spot != null) spot.color = ParseColor(spec.color);

            ShowModel(spec);
            for (int i = 0; i < tankButtons.Count; i++)
            {
                var img = tankButtons[i].GetComponent<Image>();
                if (img != null) img.color = i == tankIndex ? new Color(0.32f, 0.34f, 0.22f, 0.95f)
                                                            : new Color(0.16f, 0.17f, 0.15f, 0.9f);
            }
            RefreshProfile();
        }

        void ShowModel(TankSpec spec)
        {
            if (previewModel != null) Destroy(previewModel);
            if (library == null || previewPoint == null) return;
            var prefab = library.Get(spec.id);
            if (prefab == null) return;
            previewModel = Instantiate(prefab, previewPoint.position, Quaternion.identity);
            previewModel.name = "PreviewModel";
            foreach (var c in previewModel.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var rb in previewModel.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            previewModel.transform.localScale = Vector3.one * 1.15f;
        }

        // ---------- настройки боя ----------
        void ChangeDifficulty(int dir)
        {
            int d = (int)GameSession.Difficulty + dir;
            if (d < 0) d = 2; if (d > 2) d = 0;
            GameSession.Difficulty = (BotDifficulty)d;
            RefreshProfile();
        }

        void ToggleSquad()
        {
            GameSession.SquadSize = GameSession.SquadSize == 1 ? 2 : 1;
            RefreshProfile();
        }

        void RefreshProfile()
        {
            if (difficultyText != null)
                difficultyText.text = "Сложность: " + DifficultyTitle(GameSession.Difficulty);
            if (squadText != null)
                squadText.text = GameSession.SquadSize >= 2 ? "Взвод: 2 (с ИИ-напарником)" : "Одиночный";
            Profile.Load();
            var p = Profile.Data;
            if (profileText != null)
                profileText.text = string.Format("Боёв: {0} · побед: {1}\nфраги: {2} · урон: {3}\nлучший результат: {4} место\nнаграды: {5}",
                    p.battles, p.wins, p.kills, Mathf.RoundToInt(p.damage), p.bestPlace, p.achievements.Count);
            if (lastBattleText != null)
                lastBattleText.text = p.battles > 0
                    ? string.Format("Последний бой:\nместо {0}, фраги {1}, урон {2}\nуровень {3}, добыча {4}",
                                    p.lastPlace, p.lastKills, Mathf.RoundToInt(p.lastDamage), p.lastLevel, p.lastLoot)
                    : "Боёв пока не было.\nПервый бой — самый важный.";
            if (achievementsText != null)
            {
                var sb = new System.Text.StringBuilder("НАГРАДЫ\n");
                foreach (var a in AchievementSystem.All)
                    sb.Append(Profile.Has(a.id) ? "★ " : "☆ ").Append(a.title).Append(" — ")
                      .Append(a.desc).Append('\n');
                achievementsText.text = sb.ToString();
            }
        }

        void StartBattle()
        {
            GameSession.TankId = TankSpec.Roster[tankIndex].id;
            SceneManager.LoadScene("Battle");
        }

        static string DifficultyTitle(BotDifficulty d)
        {
            switch (d)
            {
                case BotDifficulty.Easy: return "новичок";
                case BotDifficulty.Hard: return "ас";
                default: return "обычная";
            }
        }

        static string AbilityTitle(string id)
        {
            switch (id)
            {
                case "smoke": return "дымовая завеса";
                case "repair": return "полевой ремонт";
                case "fire_ring": return "огненное кольцо";
                case "camouflage": return "маскировка";
                case "boost": return "форсаж";
                default: return id;
            }
        }

        static Color ParseColor(string hex)
        {
            Color c;
            if (ColorUtility.TryParseHtmlString(hex, out c)) return c;
            return new Color(0.4f, 0.4f, 0.35f);
        }

        // ---------- вращение превью ----------
        void Update()
        {
            if (previewModel != null)
            {
                spin += Time.deltaTime * 14f;
                previewModel.transform.rotation = Quaternion.Euler(0f, spin, 0f);
            }
            if (previewCamera != null && previewPoint != null)
            {
                float t = Time.time * 0.16f;
                previewCamera.transform.position = previewPoint.position +
                    new Vector3(Mathf.Sin(t) * 9f, 3.4f, Mathf.Cos(t) * 9f);
                previewCamera.transform.LookAt(previewPoint.position + Vector3.up * 0.9f);
            }
            // горячие клавиши
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) StartBattle();
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) SelectTank(tankIndex + 1);
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) SelectTank(tankIndex - 1);
        }

        // ---------- строительные мелочи ----------
        RectTransform MakePanel(Transform parent, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            var go = new GameObject("Panel", typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = Panel;
            var rt = go.GetComponent<RectTransform>();
            Place(rt, anchor, offset, size);
            return rt;
        }

        Text MakeText(Transform parent, string text, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject("Text", typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.text = text;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        Button MakeButton(Transform parent, string label, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            var go = new GameObject("Button", typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.16f, 0.17f, 0.15f, 0.9f);
            Place(go.GetComponent<RectTransform>(), anchor, offset, size);
            var t = MakeText(go.transform, label, 20, TextAnchor.MiddleCenter, Color.white);
            Place(t.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            return go.GetComponent<Button>();
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
        }
    }
}
