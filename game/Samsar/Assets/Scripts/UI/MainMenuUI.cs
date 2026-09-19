using UnityEngine;

namespace Samsar
{
    /// <summary>Главное меню игры SAMSAR.</summary>
    public class MainMenuUI : MonoBehaviour
    {
        enum Page { Main, Settings, About }
        Page page = Page.Main;
        GUIStyle title, sub, label, center;
        bool ready;

        void Start()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            AudioBus.Ensure();
        }

        void EnsureStyles()
        {
            if (ready) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            sub = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            label = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            center = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            ready = true;
        }

        void OnGUI()
        {
            EnsureStyles();
            var tex = Texture2D.whiteTexture;
            GUI.color = new Color(0.04f, 0.05f, 0.06f, 1f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), tex);
            GUI.color = new Color(0.1f, 0.12f, 0.14f, 1f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height * 0.42f), tex);
            GUI.color = Color.white;

            GUI.Label(new Rect(0f, Screen.height * 0.10f, Screen.width, 80f), "SAMSAR", title);
            GUI.Label(new Rect(0f, Screen.height * 0.10f + 74f, Screen.width, 26f),
                "Стальной охотник: королевская битва с танками. Один режим, один победитель.", sub);

            float cx = Screen.width * 0.5f;
            float y = Screen.height * 0.42f;

            if (page == Page.Main)
            {
                if (GUI.Button(new Rect(cx - 180f, y, 360f, 54f), "В БОЙ"))
                {
                    AudioSynth.PlayUi("click", 0.7f);
                    GameManager.ToHangar();
                }
                if (GUI.Button(new Rect(cx - 180f, y + 64f, 360f, 44f), "Полигон (обучение стрельбе)"))
                {
                    AudioSynth.PlayUi("click", 0.7f);
                    GameManager.StartTestRange(GameConfig.SelectedTank);
                }
                if (GUI.Button(new Rect(cx - 180f, y + 118f, 360f, 44f), "Настройки"))
                {
                    AudioSynth.PlayUi("click", 0.6f);
                    page = Page.Settings;
                }
                if (GUI.Button(new Rect(cx - 180f, y + 172f, 360f, 44f), "Об игре"))
                {
                    AudioSynth.PlayUi("click", 0.6f);
                    page = Page.About;
                }
                if (GUI.Button(new Rect(cx - 180f, y + 226f, 360f, 44f), "Выход"))
                {
                    Application.Quit();
                }

                GUI.Label(new Rect(cx - 260f, y + 290f, 520f, 24f),
                    "Боёв: " + Career.Battles + "   Побед: " + Career.Wins + "   Уничтожено: " + Career.Kills +
                    "   Лучшее место: " + (Career.BestPlace > 90 ? "—" : Career.BestPlace.ToString()), center);
            }
            else if (page == Page.Settings)
            {
                GUI.Label(new Rect(cx - 220f, y, 440f, 30f), "НАСТРОЙКИ", new GUIStyle(title) { fontSize = 26 });
                GUI.Label(new Rect(cx - 220f, y + 46f, 300f, 24f), "Чувствительность мыши: " + GameConfig.MouseSensitivity.ToString("0.0"), label);
                GameConfig.MouseSensitivity = GUI.HorizontalSlider(new Rect(cx - 220f, y + 72f, 440f, 20f), GameConfig.MouseSensitivity, 0.2f, 3f);

                GUI.Label(new Rect(cx - 220f, y + 104f, 440f, 24f), "Качество: " + (GameConfig.Quality == 0 ? "низкое" : GameConfig.Quality == 1 ? "среднее" : "высокое"), label);
                if (GUI.Button(new Rect(cx - 220f, y + 130f, 210f, 36f), "Ниже")) { GameConfig.Quality = Mathf.Max(0, GameConfig.Quality - 1); GameConfig.ApplyQuality(); }
                if (GUI.Button(new Rect(cx + 10f, y + 130f, 210f, 36f), "Выше")) { GameConfig.Quality = Mathf.Min(2, GameConfig.Quality + 1); GameConfig.ApplyQuality(); }

                GameConfig.InvertY = GUI.Toggle(new Rect(cx - 220f, y + 176f, 440f, 24f), GameConfig.InvertY, " Инвертировать мышь по вертикали");

                GUI.Label(new Rect(cx - 220f, y + 210f, 440f, 24f), "Участников в бою (по умолчанию): " + GameConfig.BotCount, label);
                GameConfig.BotCount = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(cx - 220f, y + 236f, 440f, 20f), GameConfig.BotCount, 10f, 40f));

                if (GUI.Button(new Rect(cx - 220f, y + 280f, 200f, 40f), "Назад")) page = Page.Main;
            }
            else
            {
                GUI.Label(new Rect(cx - 300f, y - 20f, 600f, 30f), "ОБ ИГРЕ", new GUIStyle(title) { fontSize = 26 });
                string text =
                    "SAMSAR — прототип режима «Стальной охотник»: королевская битва на танках.\n\n" +
                    "• 30+ машин в бою, каждый сам за себя (соло или взвод из двух)\n" +
                    "• Зона сужается: жёлтая граница становится красной, вне неё машина гибнет\n" +
                    "• Прокачка прямо в бою: уровни I–VII, на каждом — выбор из двух модулей\n" +
                    "• Добыча: ящики на карте, трофеи с уничтоженных, воздушный груз по расписанию\n" +
                    "• Два боевых умения на машину, кулдаун и заряды из лута\n" +
                    "• Мародёры под управлением ИИ: три уровня сложности\n" +
                    "• Карта 3×3 км: город с интерьерами, лес, поля, железнодорожная станция\n" +
                    "• Разрушаемость: дома, заборы, деревья, поезд\n\n" +
                    "Версия: прототип (этап 1). Сделано в Arena.ai Agent Mode.";
                GUI.Label(new Rect(cx - 320f, y + 16f, 640f, 420f), text, center);
                if (GUI.Button(new Rect(cx - 100f, y + 380f, 200f, 40f), "Назад")) page = Page.Main;
            }
        }
    }
}
