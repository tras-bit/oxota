using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Samsar
{
    /// <summary>Экран загрузки: асинхронная подгрузка боя с прогрессом и подсказками.</summary>
    public class LoadingScreen : MonoBehaviour
    {
        public static string NextSceneName = "MainMenu";
        public static string LoadingText = "Загрузка";

        AsyncOperation op;
        float fakeProgress;
        int tipIndex;
        readonly string[] tips =
        {
            "Выходи из сужающейся зоны: в красной зоне машина быстро погибает.",
            "Подбирай добычу — она пополняет снаряды, даёт опыт и заряды умений.",
            "Воздушный груз видят все охотники: решай, стоит ли за него драться.",
            "Попадание в борт и корму пробивает легче, чем в лоб.",
            "Модули повреждены? Отпусти ремонт — гусеница и орудие восстанавливаются сами.",
            "Каждый новый уровень машины даёт выбор из двух модулей.",
            "Мародёры сбиваются в группы: обходи их с флангов.",
            "Умения восстанавливаются по кулдауну, заряды можно найти в луте.",
        };

        void Start()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            Time.timeScale = 1f;
            tipIndex = Random.Range(0, tips.Length);
            op = SceneManager.LoadSceneAsync(NextSceneName);
            op.allowSceneActivation = true;
        }

        void Update()
        {
            if (op == null) return;
            fakeProgress = Mathf.MoveTowards(fakeProgress, op.progress, Time.deltaTime * 0.7f);
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            var small = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0f, Screen.height * 0.36f, Screen.width, 40f), LoadingText.ToUpper(), style);

            var bar = new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.5f, 440f, 20f);
            var tex = Texture2D.whiteTexture;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(bar, tex);
            GUI.color = new Color(1f, 0.55f, 0.15f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(fakeProgress), bar.height), tex);
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, bar.y + 26f, Screen.width, 22f), Mathf.RoundToInt(fakeProgress * 100f) + "%", small);
            GUI.Label(new Rect(0f, bar.y + 70f, Screen.width, 22f), "Подсказка: " + tips[tipIndex], small);
        }
    }
}
