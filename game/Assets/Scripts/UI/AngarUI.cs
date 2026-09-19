// Главное меню и 3D-ангар: вращаем машину, смотрим ТТХ, выбираем сложность и взвод.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Samsar
{
    public class AngarUI : MonoBehaviour
    {
        public TankLibrary library;
        public Transform previewPoint;
        public Camera previewCamera;

        Font font;
        Canvas canvas;
        GameObject currentModel;
        Text ttxText, headerText, statsText;
        Transform listRoot;
        readonly List<Image> listButtons = new List<Image>();
        readonly List<Text> listLabels = new List<Text>();
        Text diffText, squadText, statsText2;
        GameObject statsPanel;
        float rotateSpeed = 12f;

        void Awake()
        {
            Profile.Load();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildUI();
            ShowTank(GameSession.TankId);
            UpdateButtons();
        }

        void Update()
        {
            if (currentModel != null)
                currentModel.transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
            if (previewCamera != null)
            {
                float t = Time.time * 0.08f;
                Vector3 look = previewPoint != null ? previewPoint.position + Vector3.up * 1.2f : Vector3.zero;
                previewCamera.transform.position = look + new Vector3(Mathf.Sin(t) * 9f, 4.2f, Mathf.Cos(t) * 9f);
                previewCamera.transform.LookAt(look);
            }
        }

        // ---------- интерфейс ----------
        void BuildUI()
        {
            var go = new GameObject("AngarCanvas");
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            var title = MakeText(canvas.transform, "SAMSAR", 52, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -70), new Color(1f, 0.85f, 0.5f));
            title.fontStyle = FontStyle.Bold;
            MakeText(canvas.transform, "Стальной охотник — режим «последний выживший»", 20, TextAnchor.MiddleCenter,
                 new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -116), new Color(0.85f, 0.88f, 0.9f));

            // список машин слева
            var listPanel = MakePanel(canvas.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                  new Vector2(360, 620), new Vector2(200, 0), new Color(0.06f, 0.07f, 0.09f, 0.85f));
            listRoot = listPanel.transform;
            headerText = MakeText(listRoot, "Выбор машины", 22, TextAnchor.MiddleCenter,
                              new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -34), Color.white);

            float y = 40f;
            foreach (var spec in TankSpec.Roster)
            {
                var btn = MakePanel(listRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                new Vector2(320, 84), new Vector2(0, -80 - y), new Color(0.13f, 0.16f, 0.19f, 0.95f));
                var img = btn.GetComponent<Image>();
                var button = btn.AddComponent<Button>();
                button.targetGraphic = img;
                string id = spec.id;
                button.onClick.AddListener(() => { GameSession.TankId = id; ShowTank(id); UpdateButtons(); });
                var label = MakeText(btn.transform, spec.title, 20, TextAnchor.MiddleCenter,
                                 new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 14), Color.white);
                MakeText(btn.transform, ClassName(spec.cls) + " · " + Mathf.RoundToInt(spec.hp) + " HP · " +
                     Mathf.RoundToInt(spec.SpeedKmh) + " км/ч", 15, TextAnchor.MiddleCenter,
                     new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -18), new Color(0.75f, 0.8f, 0.85f));
                listButtons.Add(img);
                listLabels.Add(label);
                y += 92f;
            }

            // ТТХ справа
            var ttxPanel = MakePanel(canvas.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                                 new Vector2(420, 560), new Vector2(-230, 30), new Color(0.06f, 0.07f, 0.09f, 0.85f));
            ttxText = MakeText(ttxPanel.transform, "", 19, TextAnchor.UpperLeft,
                           new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), Color.white);
            ttxText.alignment = TextAnchor.UpperLeft;
            ttxText.rectTransform.sizeDelta = new Vector2(380, 480);

            // настройки боя внизу
            var bottom = MakePanel(canvas.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                               new Vector2(900, 190), new Vector2(0, 110), new Color(0.06f, 0.07f, 0.09f, 0.85f));
            var diffBtn = MakeButton(bottom.transform, new Vector2(-300, 40), new Vector2(260, 56), "Сложность: обычная",
                                 CycleDifficulty);
            diffText = diffBtn.GetComponentInChildren<Text>();
            var squadBtn = MakeButton(bottom.transform, new Vector2(0, 40), new Vector2(260, 56), "Соло", CycleSquad);
            squadText = squadBtn.GetComponentInChildren<Text>();
            MakeButton(bottom.transform, new Vector2(300, 40), new Vector2(260, 56), "Статистика", ToggleStats);

            var play = MakeButton(bottom.transform, new Vector2(0, -45), new Vector2(420, 70), "В БОЙ", StartBattle);
            play.GetComponent<Image>().color = new Color(0.85f, 0.5f, 0.12f, 1f);
            play.GetComponentInChildren<Text>().fontSize = 28;

            statsText = MakeText(canvas.transform, "", 17, TextAnchor.MiddleCenter,
                             new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 26),
                             new Color(0.7f, 0.75f, 0.8f));
            RefreshStats();
        }

        void RefreshStats()
        {
            statsText.text = string.Format(
                "Боёв: {0}   ·   побед: {1}   ·   уничтожено: {2}   ·   урона всего: {3}   ·   лучшее место: {4}   ·   достижений: {5}",
                Profile.Data.battles, Profile.Data.wins, Profile.Data.kills,
                Mathf.RoundToInt(Profile.Data.damage),
                Profile.Data.bestPlace >= 99 ? "—" : Profile.Data.bestPlace.ToString(),
                Profile.Data.achievements.Count);
        }

        static string ClassName(TankClass c)
        {
            switch (c)
            {
                case TankClass.LT: return "лёгкий танк";
                case TankClass.MT: return "средний танк";
                case TankClass.HT: return "тяжёлый танк";
                default: return "ПТ-САУ";
            }
        }

        GameObject MakePanel(Transform parent, Vector2 aMin, Vector2 aMax, Vector2 size, Vector2 pos, Color color)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        Text MakeText(Transform parent, string s, int size, TextAnchor anchor, Vector2 aMin, Vector2 aMax,
                  Vector2 pos, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = s;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(360, size + 14);
            return t;
        }

        Button MakeButton(Transform parent, Vector2 pos, Vector2 size, string label, UnityEngine.Events.UnityAction action)
        {
            var panel = Panel(parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, pos,
                              new Color(0.14f, 0.17f, 0.2f, 1f));
            var btn = panel.AddComponent<Button>();
            btn.targetGraphic = panel.GetComponent<Image>();
            btn.onClick.AddListener(action);
            MakeText(panel.transform, label, 19, TextAnchor.MiddleCenter,
                 new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Color.white);
            return btn;
        }

        // ---------- логика ----------
        void ShowTank(string id)
        {
            var spec = TankSpec.Get(id);
            if (currentModel != null) Destroy(currentModel);
            var prefab = library != null ? library.Get(spec.modelName) : null;
            if (prefab != null && previewPoint != null)
            {
                currentModel = Instantiate(prefab, previewPoint.position, Quaternion.Euler(0f, 140f, 0f));
                currentModel.name = "Preview_" + id;
                foreach (var c in currentModel.GetComponentsInChildren<Collider>()) Destroy(c);
            }
            ttxText.text = string.Format(
                "  {0}\n\n" +
                "  Прочность: {1}\n" +
                "  Максимальная скорость: {2} км/ч\n" +
                "  Разворот корпуса: {3} °/с\n" +
                "  Поворот башни: {4} °/с\n\n" +
                "  Разовый урон: {5}\n" +
                "  Бронепробиваемость: {6} мм\n" +
                "  Перезарядка: {7} с\n" +
                "  Разброс: {8} м на 100 м\n" +
                "  Сведение: {9} с\n" +
                "  Боекомплект: {10}\n\n" +
                "  Обзор: {11} м\n" +
                "  Броня корпуса/башни: {12}/{13} мм\n\n" +
                "  Умение 1: Авиаудар\n" +
                "  Умение 2: {14}",
                spec.title, Mathf.RoundToInt(spec.hp), Mathf.RoundToInt(spec.SpeedKmh),
                Mathf.RoundToInt(spec.turnRate), Mathf.RoundToInt(spec.turretTraverse),
                Mathf.RoundToInt(spec.damage), Mathf.RoundToInt(spec.penetration),
                spec.reload.ToString("0.0"), spec.dispersion.ToString("0.00"), spec.aimTime.ToString("0.0"),
                spec.shells, Mathf.RoundToInt(spec.viewRange),
                Mathf.RoundToInt(spec.armorHull), Mathf.RoundToInt(spec.armorTurret),
                TankAbilities.Describe(spec.ability2).title);
        }

        void UpdateButtons()
        {
            for (int i = 0; i < listButtons.Count && i < TankSpec.Roster.Length; i++)
            {
                bool sel = TankSpec.Roster[i].id == GameSession.TankId;
                listButtons[i].color = sel ? new Color(0.35f, 0.28f, 0.12f, 0.98f)
                                           : new Color(0.13f, 0.16f, 0.19f, 0.95f);
                listLabels[i].color = sel ? new Color(1f, 0.9f, 0.6f) : Color.white;
            }
            headerText.text = "Выбор машины (все доступны сразу)";
        }

        void CycleDifficulty()
        {
            GameSession.Difficulty = (BotDifficulty)(((int)GameSession.Difficulty + 1) % 3);
            diffText.text = "Сложность: " + (GameSession.Difficulty == BotDifficulty.Easy ? "лёгкая"
                            : GameSession.Difficulty == BotDifficulty.Normal ? "обычная" : "сложная");
        }

        void CycleSquad()
        {
            GameSession.SquadSize = GameSession.SquadSize == 1 ? 2 : 1;
            squadText.text = GameSession.SquadSize == 2 ? "Взвод (ИИ-напарник)" : "Соло";
        }

        void ToggleStats()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ДОСТИЖЕНИЯ");
            foreach (var a in AchievementSystem.All())
                sb.AppendLine("   • " + a);
            sb.AppendLine();
            sb.AppendLine("Управление в бою:");
            sb.AppendLine("   W/S — ход вперёд/назад, A/D — поворот;  мышь — прицел");
            sb.AppendLine("   ЛКМ — выстрел,  ПКМ — снайперский прицел ×8,  колесо — приближение камеры");
            sb.AppendLine("   1/Q — авиаудар,  2/E — второе умение,  H — ремонт напарнику");
            sb.AppendLine("   Tab — состояние боя,  Esc — пауза");
            MakeText(canvas.transform, sb.ToString(), 18, TextAnchor.UpperLeft,
                 new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), Color.white);
        }

        void StartBattle()
        {
            GameSession.Reset();
            SceneManager.LoadScene("Battle");
        }
    }
}
