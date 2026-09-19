using UnityEngine;

namespace Samsar
{
    /// <summary>Экран результатов боя: место, урон, фраги, точность, достижения, таблица.</summary>
    public class ResultsUI : MonoBehaviour
    {
        GUIStyle title, big, label, small, center;
        bool ready;
        Texture2D white;
        bool registered;

        void Start()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            if (!registered)
            {
                registered = true;
                Career.RegisterBattle(ResultsData.Stats);
            }
        }

        void EnsureStyles()
        {
            if (ready) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            big = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            label = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            small = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            center = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            ready = true;
        }

        void Panel(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        void OnGUI()
        {
            EnsureStyles();
            var s = ResultsData.Stats ?? new PlayerStats();
            Panel(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.05f, 0.06f, 0.07f, 1f));

            string headline = s.Won ? "ПОБЕДА — ПОСЛЕДНИЙ ОХОТНИК" : s.Survived ? "БОЙ ЗАВЕРШЁН" : "МАШИНА УНИЧТОЖЕНА";
            var color = s.Won ? new Color(1f, 0.85f, 0.3f) : new Color(0.9f, 0.9f, 0.95f);
            GUI.color = color;
            GUI.Label(new Rect(0f, 26f, Screen.width, 54f), headline, title);
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, 82f, Screen.width, 26f),
                "Место: <b>" + (s.FinalPlace > 0 ? s.FinalPlace.ToString() : "—") + "</b> из " + ResultsData.Participants +
                "   ·   Время боя: " + Mathf.FloorToInt(ResultsData.BattleTime / 60f) + " мин " + Mathf.FloorToInt(ResultsData.BattleTime % 60f) + " с", center);

            // сводка
            var left = new Rect(40f, 130f, Screen.width * 0.42f - 60f, 320f);
            Panel(left, new Color(0f, 0f, 0f, 0.55f));
            float y = left.y + 12f;
            GUI.Label(new Rect(left.x + 16f, y, left.width - 32f, 28f), "РЕЗУЛЬТАТ БОЯ", big);
            y += 38f;
            Row(left, ref y, "Нанесено урона", Mathf.RoundToInt(s.DamageDealt).ToString());
            Row(left, ref y, "Получено урона", Mathf.RoundToInt(s.DamageTaken).ToString());
            Row(left, ref y, "Уничтожено машин", s.Kills.ToString());
            Row(left, ref y, "из них мародёров (ИИ)", s.BotKills.ToString());
            Row(left, ref y, "Выстрелов", s.ShotsFired.ToString());
            Row(left, ref y, "Попаданий / пробитий", s.Hits + " / " + s.Penetrations);
            Row(left, ref y, "Рикошетов", s.Ricochets.ToString());
            Row(left, ref y, "Точность", Mathf.RoundToInt(s.Accuracy * 100f) + " %");
            Row(left, ref y, "Доля пробитий", Mathf.RoundToInt(s.PenRatio * 100f) + " %");
            Row(left, ref y, "Собрано добычи", s.LootTaken.ToString());
            Row(left, ref y, "Максимальный уровень машины", Vehicle.Roman(Mathf.Clamp(s.MaxLevelReached, 1, 7)));
            Row(left, ref y, "Разрушено объектов", ResultsData.DestroyedObjects.ToString());

            // достижения
            var right = new Rect(Screen.width * 0.5f + 20f, 130f, Screen.width * 0.5f - 60f, 320f);
            Panel(right, new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(new Rect(right.x + 16f, right.y + 12f, right.width - 32f, 28f), "ДОСТИЖЕНИЯ", big);
            float ry = right.y + 48f;
            if (s.Achievements == null || s.Achievements.Count == 0)
            {
                GUI.Label(new Rect(right.x + 16f, ry, right.width - 32f, 24f), "В этот раз без особых отметок — попробуй ещё.", label);
            }
            else
            {
                foreach (var a in s.Achievements)
                {
                    GUI.Label(new Rect(right.x + 16f, ry, right.width - 32f, 24f), "★ <b>" + a + "</b>", label);
                    ry += 26f;
                }
            }

            // таблица
            var table = new Rect(40f, 470f, Screen.width - 80f, Screen.height - 560f);
            Panel(table, new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(new Rect(table.x + 14f, table.y + 8f, 300f, 24f), "ИТОГОВАЯ ТАБЛИЦА", big);
            float ty = table.y + 40f;
            GUI.Label(new Rect(table.x + 16f, ty, 80f, 20f), "<b>Место</b>", label);
            GUI.Label(new Rect(table.x + 110f, ty, 260f, 20f), "<b>Участник</b>", label);
            GUI.Label(new Rect(table.x + 380f, ty, 100f, 20f), "<b>Уровень</b>", label);
            GUI.Label(new Rect(table.x + 490f, ty, 120f, 20f), "<b>Урон</b>", label);
            GUI.Label(new Rect(table.x + 620f, ty, 100f, 20f), "<b>Уничтожено</b>", label);
            ty += 24f;
            int shown = 0;
            foreach (var row in ResultsData.Score)
            {
                if (ty > table.y + table.height - 24f) break;
                if (shown > 24) break;
                if (row.IsPlayer) Panel(new Rect(table.x + 12f, ty - 2f, table.width - 24f, 20f), new Color(0.3f, 0.35f, 0.2f, 0.6f));
                else if (row.IsSquadMate) Panel(new Rect(table.x + 12f, ty - 2f, table.width - 24f, 20f), new Color(0.2f, 0.3f, 0.4f, 0.5f));
                GUI.Label(new Rect(table.x + 16f, ty, 90f, 20f), row.Alive ? "в бою" : row.Place.ToString(), label);
                GUI.Label(new Rect(table.x + 110f, ty, 260f, 20f), row.Name, label);
                GUI.Label(new Rect(table.x + 380f, ty, 100f, 20f), Vehicle.Roman(Mathf.Clamp(row.Level, 1, 7)), label);
                GUI.Label(new Rect(table.x + 490f, ty, 120f, 20f), row.Damage.ToString(), label);
                GUI.Label(new Rect(table.x + 620f, ty, 100f, 20f), row.Kills.ToString(), label);
                ty += 22f;
                shown++;
            }

            // кнопки
            float by = Screen.height - 64f;
            if (GUI.Button(new Rect(Screen.width * 0.5f - 330f, by, 210f, 44f), "В АНГАР"))
            {
                AudioSynth.PlayUi("click", 0.6f);
                GameManager.ToHangar();
            }
            if (GUI.Button(new Rect(Screen.width * 0.5f - 105f, by, 210f, 44f), "ЕЩЁ БОЙ"))
            {
                AudioSynth.PlayUi("click", 0.6f);
                GameManager.StartBattle(GameConfig.SelectedTank, GameConfig.BotCount, GameConfig.Difficulty, GameConfig.Squad);
            }
            if (GUI.Button(new Rect(Screen.width * 0.5f + 120f, by, 210f, 44f), "В ГЛАВНОЕ МЕНЮ"))
            {
                AudioSynth.PlayUi("click", 0.6f);
                GameManager.ToMenu();
            }
        }

        void Row(Rect panel, ref float y, string name, string value)
        {
            GUI.Label(new Rect(panel.x + 16f, y, panel.width - 220f, 22f), name, label);
            var rstyle = new GUIStyle(label) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(panel.x + panel.width - 210f, y, 190f, 22f), "<b>" + value + "</b>", rstyle);
            y += 22f;
        }
    }
}
